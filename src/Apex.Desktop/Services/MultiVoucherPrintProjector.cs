using System;
using System.Collections.Generic;
using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects a <b>RANGE of vouchers</b> into a document SET for one collated print job — the multi-voucher half
/// of census row 12.6 ("Multi-account printing / multi-voucher (range) printing").
///
/// <para><b>Vendor grounding, quoted by content.</b> The reference product reaches this from the print menu and
/// configures it by voucher type: <i>"Press <b>Alt+P</b> (Print) &gt; <b>Others</b> &gt; select
/// <b>Multi-Voucher</b> &gt; press Enter"</i> and <i>"Type or select <b>Multi-Voucher Printing</b> &gt; and press
/// Enter"</i>, after which <i>"Press <b>C</b> (Configure), select <b>Voucher Type</b>"</i>
/// (<c>help.tallysolutions.com/faq-e-way-bill-tally/</c>;
/// <c>help.tallysolutions.com/gcc-sales-and-print-fta-tally/</c>). The party and period axes are attested on the
/// vendor's own release notes: <i>"You can also print, export, or e-mail multi-voucher reports for a specific
/// party and period."</i>
/// (<c>help.tallysolutions.com/tally-prime/release-notes-tally-prime/release-notes-tallyprime-2-1/</c>).</para>
///
/// <para><b>Those three axes are the whole selection surface, and no fourth was invented.</b> Voucher type,
/// party and period are each quoted above. Nothing else is offered, because nothing else is attested — a
/// sorting knob or a "skip cancelled" toggle would be ours, not the reference product's.</para>
///
/// <para><b>One <see cref="PrintReport"/> per voucher, handed to the EXISTING
/// <c>PrintKind.ReportSet</c> path</b> that multi-ACCOUNT printing already uses. That is deliberate reuse, not
/// a parallel engine: <c>ReportPdf.Render(documents, …)</c> already starts each document on a fresh sheet and
/// runs the page numbering across the whole job, and the F10 page range already applies to it. A second
/// renderer would be a second set of pagination bugs.</para>
///
/// <para>🔴 <b>The per-voucher document is the SAME projection the single-voucher preview draws</b>
/// (<c>PrintPreviewViewModel.BuildVoucherPreviewReport</c>): the same three columns, the same header rows, the
/// same cancelled marking. A voucher must not print one way alone and another way in a batch — that is the
/// preview/paper drift this project has already filed against the invoice mirror.</para>
///
/// <para><b>No clock.</b> The period is supplied by the caller (ER-12), so the projection is deterministic in
/// tests and cannot disagree with the books about what "this year" means.</para>
/// </summary>
public static class MultiVoucherPrintProjector
{
    /// <summary>The heading the preview column carries for a multi-voucher job.</summary>
    public const string JobTitle = "Multi-Voucher Printing";

    /// <summary>
    /// Invariant-culture day formatting. 🔴 <b>Deliberately invariant</b>, for the reason spelled out on
    /// <c>MultiAccountPrintProjector.Day</c>: a bare <c>ToString("dd-MM-yyyy")</c> reads the ambient calendar and
    /// printed <c>01-04-2563</c> on a Thai machine, so the paper disagreed with the books.
    /// </summary>
    private static string Day(DateOnly date) =>
        date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The vouchers a job would cover, in print order (date, then voucher number, then id) — the RANGE this row
    /// is named for. A null <paramref name="typeId"/> means every voucher type; a null
    /// <paramref name="partyId"/> means every party, including vouchers with no party at all.
    ///
    /// <para>🔴 <b>The bounds are INCLUSIVE at both ends</b>, because an operator who asks for 1-Apr to 30-Apr
    /// means April. An exclusive upper bound would silently drop the last day of every month-end run, and the
    /// loss would be invisible on the paper — there is no row saying "30-Apr omitted".</para>
    ///
    /// <para>🔴 <b>The final tie-break on <see cref="Voucher.Id"/> is load-bearing, not tidiness.</b> Two
    /// vouchers of different types can share a date and a number (each type numbers independently), so date and
    /// number alone do not totally order the set. Without a deterministic third key the same range would print
    /// in a different sheet order on different runs, and a reprint would not match the first copy.</para>
    /// </summary>
    public static IReadOnlyList<Voucher> Select(
        Company company,
        Guid? typeId,
        Guid? partyId,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var chosen = new List<Voucher>();
        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;
            if (typeId is Guid t && v.TypeId != t) continue;
            if (partyId is Guid p && v.PartyId != p) continue;
            chosen.Add(v);
        }

        chosen.Sort((a, b) =>
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.Number.CompareTo(b.Number);
            if (c != 0) return c;
            return a.Id.CompareTo(b.Id);
        });

        return chosen;
    }

    /// <summary>
    /// Projects the chosen vouchers into the document SET a <c>PrintKind.ReportSet</c> preview prints — one
    /// document per voucher, each starting a fresh sheet.
    /// </summary>
    public static IReadOnlyList<PrintReport> Project(Company company, IReadOnlyList<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(vouchers);

        var documents = new List<PrintReport>(vouchers.Count);
        foreach (var v in vouchers)
            documents.Add(Document(company, v));
        return documents;
    }

    /// <summary>
    /// One voucher as one printable document. Mirrors the single-voucher preview projection cell for cell.
    ///
    /// <para>🔴 <b>Three cells on every row, against three columns — the egress-band invariant.</b>
    /// <c>PrintReport</c> carries ONE column band and <c>ReportPdf</c> clamps with
    /// <c>Math.Min(Columns.Count, row.Cells.Count)</c>, so a row wider than the band is silently amputated on
    /// paper. That is the defect <c>MatrixSecondBandEgressTests</c> exists to prevent (a filed annual return
    /// printed with its money deleted). Every row built below emits exactly three cells.</para>
    /// </summary>
    private static PrintReport Document(Company company, Voucher voucher)
    {
        var type = company.FindVoucherType(voucher.TypeId);
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
        var number = company.FormatVoucherNumber(voucher);

        var rows = new List<PrintRow>
        {
            PrintRow.Header(
                string.IsNullOrEmpty(number) ? "No. -" : "No. " + number,
                "Date",
                Day(voucher.Date)),
        };

        // 🔴 A CANCELLED voucher is marked on the paper, exactly as the single-voucher path marks it
        // (VoucherPrintProjector sets IsCancelled and VoucherPdf over-prints it). A batch that printed a
        // cancelled voucher indistinguishable from a live one would put a withdrawn document in a counterparty's
        // hands. It is NOT dropped from the range: its number was spent and the vendor's own cheque-register
        // treatment keeps cancelled instruments visible rather than hiding them.
        if (voucher.Cancelled)
            rows.Add(PrintRow.Header("** CANCELLED **", string.Empty, string.Empty));

        if (!string.IsNullOrWhiteSpace(party))
            rows.Add(PrintRow.Header("Party: " + ReportPrintProjector.Ascii(party), string.Empty, string.Empty));

        decimal totalDebit = 0m, totalCredit = 0m;
        foreach (var l in voucher.Lines)
        {
            var isDebit = l.Side == DrCr.Debit;
            if (isDebit) totalDebit += l.Amount.Amount; else totalCredit += l.Amount.Amount;
            rows.Add(new PrintRow(
                ReportPrintProjector.Ascii(company.FindLedger(l.LedgerId)?.Name ?? "(unknown)"),
                isDebit ? IndianFormat.Amount(l.Amount) : string.Empty,
                isDebit ? string.Empty : IndianFormat.Amount(l.Amount)));
        }

        rows.Add(PrintRow.Total(
            "Total",
            IndianFormat.AmountAlways(totalDebit),
            IndianFormat.AmountAlways(totalCredit)));

        if (!string.IsNullOrWhiteSpace(voucher.Narration))
            rows.Add(PrintRow.Header(
                "Narration: " + ReportPrintProjector.Ascii(voucher.Narration!), string.Empty, string.Empty));

        return new PrintReport
        {
            // The voucher TYPE name is a master the user typed (a company may rename "Sales" or add its own
            // type), so the heading is flagged under ruling 18 — otherwise the renderer's ER-11 guard would
            // strip a vendor token out of a real, operator-chosen type name on every sheet of the batch.
            Title = ReportPrintProjector.Ascii(type?.Name ?? "Voucher"),
            TitleCarriesMasterName = true,
            Subtitle = ReportPrintProjector.Ascii(VoucherPrintProjector.CompanyDisplayName(company)),
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Debit", 1.5, CellAlign.Right),
                new PrintColumn("Credit", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }
}
