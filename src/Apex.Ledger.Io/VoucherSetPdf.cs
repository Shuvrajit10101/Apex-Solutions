using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// One voucher of a multi-voucher print job, carried in the SAME print model the voucher's single print uses:
/// an item invoice as its <see cref="InvoicePrintData"/>, anything else as its <see cref="VoucherPrintData"/>.
/// Exactly one of the two is set.
/// </summary>
public sealed class VoucherSetDocument
{
    /// <summary>The voucher's print model when it prints as a plain voucher, else <c>null</c>.</summary>
    public VoucherPrintData? Voucher { get; }

    /// <summary>The voucher's print model when it prints as an invoice, else <c>null</c>.</summary>
    public InvoicePrintData? Invoice { get; }

    private VoucherSetDocument(VoucherPrintData? voucher, InvoicePrintData? invoice)
    {
        Voucher = voucher;
        Invoice = invoice;
    }

    /// <summary>A voucher that prints as a plain voucher (<see cref="VoucherPdf"/>).</summary>
    public static VoucherSetDocument Of(VoucherPrintData voucher)
        => new(voucher ?? throw new ArgumentNullException(nameof(voucher)), null);

    /// <summary>A voucher that prints as an invoice (<see cref="InvoicePdf"/>).</summary>
    public static VoucherSetDocument Of(InvoicePrintData invoice)
        => new(null, invoice ?? throw new ArgumentNullException(nameof(invoice)));
}

/// <summary>
/// Renders a RANGE of vouchers as ONE collated PDF job — census row 12.6, the multi-voucher half — with every
/// voucher drawn by the renderer that prints it ALONE.
///
/// <para>🔴 <b>WHY NOT <see cref="ReportPdf"/>.</b> The first cut of this job projected each voucher into a
/// three-column Particulars/Debit/Credit <see cref="PrintReport"/> and claimed that "a voucher cannot print one way
/// alone and another way in a batch". That was false for the vouchers the vendor's own page uses the feature for:
/// an item Sales invoice printed alone carries its items, HSN and both GSTINs (<see cref="InvoicePdf"/>), and in
/// the batch it came out as a bare Dr/Cr list with none of them (A12 review w6, F2, asserted on PDF bytes). Here
/// each voucher goes through <see cref="VoucherPdf.RenderInto"/> or <see cref="InvoicePdf.RenderInto"/> — the
/// SAME body <c>Render</c> runs — onto one shared writer, so the sheets of a voucher in a job are the sheets it
/// prints alone.</para>
///
/// <para><b>What belongs to the job and what to the voucher.</b> Each voucher starts on a fresh sheet and numbers
/// its own sheets "Page 1 of N", exactly as it does alone. The F8/F9 format and paper and the F12 document knobs
/// (title override, narration, copy marking) apply to every voucher, as they apply to one. The F10 page range
/// selects the JOB's sheets (sheet 3 of the job is the third sheet out of the printer) and the F5 copy count
/// repeats the whole job collated. The F10 start number is not applied: each voucher is numbered as it is alone,
/// and renumbering it would be a sheet the voucher never prints.</para>
///
/// <para>De-branded and deterministic like every renderer here — no clock, no RNG, invariant formatting.</para>
/// </summary>
public static class VoucherSetPdf
{
    /// <summary>Renders the job to PDF bytes. Each document starts a fresh sheet.</summary>
    /// <param name="documents">The job, in print order.</param>
    /// <param name="config">The F12 document knobs, applied to every voucher.</param>
    /// <param name="page">The page knobs; F10 applies to the job's sheets, F5 to the whole job.</param>
    /// <param name="jobTitle">The PDF /Title of the job.</param>
    public static byte[] Render(
        IReadOnlyList<VoucherSetDocument> documents, PrintConfig config, PageConfig page, string jobTitle)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(page);

        var writer = new PdfWriter
        {
            DocumentTitle = string.IsNullOrWhiteSpace(jobTitle)
                ? "Apex Solutions Vouchers"
                : Debrand.Text(jobTitle) + " — Apex Solutions",
        };

        foreach (var document in documents)
        {
            if (document is null) continue;
            if (document.Invoice is { } invoice) InvoicePdf.RenderInto(writer, invoice, config, page);
            else if (document.Voucher is { } voucher) VoucherPdf.RenderInto(writer, voucher, config, page);
        }

        // F10 over the JOB's sheets. An out-of-bounds range selects nothing and yields ONE BLANK SHEET, never the
        // whole job — the same refusal to fall back to "print everything" every other renderer here makes.
        writer.RetainPages(page.IncludesPage);
        if (writer.PageCount == 0)
            writer.BeginPage(page.PageWidth, page.PageHeight);

        // F5: collated copies of the whole job (1,2,1,2 — never 1,1,2,2).
        writer.RepeatAllPages(page.EffectiveCopies);
        return writer.Build();
    }
}
