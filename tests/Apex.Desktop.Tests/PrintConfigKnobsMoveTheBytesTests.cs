using System;
using System.Linq;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>A knob the panel offers must do something.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> W2-31 wired the F8 format / F9 paper / F5 copies / F10 range knobs
/// through <see cref="PrintConfigViewModel"/> into <see cref="PageConfig"/>, and
/// <c>PrintConfigFormatCopiesRangeTests</c> proved every one of them moves the rendered bytes — <b>over a report
/// preview</b>. No test opened the same panel over a <i>document</i> preview, and that is the half that was
/// broken: measured against the renderers,
/// <list type="bullet">
///   <item><see cref="ReportPdf"/> reads all of them — <c>Formatted*</c>, <c>Draws*</c>, <c>IncludesPage</c>,
///     <c>StartPageNumber</c> and <c>EffectiveCopies</c>;</item>
///   <item><see cref="InvoicePdf"/>, <c>VoucherPdf</c>, <c>PayslipPdf</c> and <c>PosReceiptPdf</c> read
///     <b>only</b> <c>EffectiveCopies</c>.</item>
/// </list>
/// Yet <c>SupportsPageKnobs</c> returned a bare <c>true</c>, was bound to <b>nothing</b> in
/// <c>MainWindow.axaml</c>, and its own summary claimed the knobs "apply to EVERY preview kind". So on a
/// voucher or invoice the operator was shown a print format, a paper choice, a page range and a starting page
/// number, could set any of them, press Ctrl+A, and get <b>byte-identical output</b>. That is the same class of
/// defect as a caption naming an unbound key: the panel states a capability the product does not have.</para>
///
/// <para><b>The lock is behavioural and derives its truth from the PDF, not from the flag.</b> It renders, changes
/// one knob, renders again and compares bytes. It therefore cannot be satisfied by relabelling: either the
/// renderer honours the knob, or the panel must stop offering it. Both outcomes are correct; a knob that is
/// offered and ignored is not.</para>
/// </summary>
public sealed class PrintConfigKnobsMoveTheBytesTests
{
    // ---------------------------------------------------------------- fixtures

    /// <summary>Taxable ₹4,321.00 @ 18% intra-State — a document (Invoice-kind) preview.</summary>
    private static InvoicePrintData OutwardTaxInvoice()
    {
        var taxable = new Money(4_321m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        string Gstin(string first14) => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

        return new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders", AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = Gstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Gujarat Supplier", AddressLines = new[] { "9 Dockyard Road", "Surat" },
                Gstin = Gstin("24EEEEE0000E1Z"), StateText = "Gujarat (24)",
            },
            InvoiceNumber = "INV-0007",
            InvoiceDateText = "10-04-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Raw Cotton", HsnSac = "520100",
                    QuantityText = "8.000", RateText = "540.125", TaxableValue = taxable,
                },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero,
                },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    /// <summary>A report long enough to paginate, so a page RANGE is a meaningful thing to ask for.</summary>
    private static PrintReport LongReport()
    {
        var rows = new PrintRow[220];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new PrintRow($"Ledger {i + 1:000}", $"{(i + 1) * 100}.00");

        return new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "as at 31-Mar-2026",
            Columns = new[]
            {
                new PrintColumn("Particulars", 3.0),
                new PrintColumn("Debit", 1.0, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    private static PrintConfigViewModel PanelOver(PrintPreviewViewModel preview)
    {
        var panel = new PrintConfigViewModel(preview);
        Assert.True(panel.SupportsPageKnobs || true); // read once; the assertions below decide what it must be
        return panel;
    }

    /// <summary>Applies <paramref name="mutate"/> through the panel and returns the bytes before and after.</summary>
    private static (byte[] Before, byte[] After) Render(
        PrintPreviewViewModel preview, Action<PrintConfigViewModel> mutate)
    {
        byte[] before = preview.PdfBytes.ToArray();
        var panel = PanelOver(preview);
        mutate(panel);
        panel.Apply();
        return (before, preview.PdfBytes.ToArray());
    }

    // ---------------------------------------------------------------- the invariant

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> If the panel offers the page-layout knobs over this preview, each of
    /// them must change the rendered document. Applied to a DOCUMENT preview, which is where the knobs were inert.
    /// </summary>
    [Fact]
    public void An_offered_page_layout_knob_changes_a_document_preview()
    {
        var preview = new PrintPreviewViewModel(OutwardTaxInvoice());
        var panel = PanelOver(preview);

        if (!panel.SupportsPageKnobs)
            return;   // the panel does not offer them here — nothing is being claimed, so nothing is owed

        var format = Render(preview, p => p.PrintFormat = PrintFormat.DotMatrix);
        Assert.False(format.Before.SequenceEqual(format.After),
            "the panel offers an F8 print format over this document preview, but switching to Dot Matrix left the "
          + "PDF byte-identical — InvoicePdf reads none of PageConfig's Formatted*/Draws* members. Either honour "
          + "the knob in the renderer, or stop offering it here.");

        var paper = Render(preview, p => p.Paper = PaperKind.PrePrinted);
        Assert.False(paper.Before.SequenceEqual(paper.After),
            "the panel offers an F9 paper choice over this document preview, but selecting pre-printed stationery "
          + "left the PDF byte-identical.");

        var start = Render(preview, p => p.StartPageNumber = 7);
        Assert.False(start.Before.SequenceEqual(start.After),
            "the panel offers a starting page number over this document preview, but setting it to 7 left the PDF "
          + "byte-identical.");
    }

    /// <summary>
    /// The same invariant over a REPORT preview — where the knobs genuinely work. This is the non-vacuity half:
    /// it proves the byte comparison above can detect a change at all, so a green result upstream means the panel
    /// stopped over-offering rather than that the harness stopped looking.
    /// </summary>
    [Fact]
    public void The_same_knobs_do_change_a_report_preview()
    {
        var preview = new PrintPreviewViewModel(LongReport(), "Trial Balance");
        Assert.True(PanelOver(preview).SupportsPageKnobs,
            "the report preview must keep offering the page knobs — ReportPdf honours every one of them.");

        var format = Render(preview, p => p.PrintFormat = PrintFormat.DotMatrix);
        Assert.False(format.Before.SequenceEqual(format.After), "F8 format did not move the report bytes");

        var start = Render(preview, p => p.StartPageNumber = 7);
        Assert.False(start.Before.SequenceEqual(start.After), "starting page number did not move the report bytes");

        var range = Render(preview, p => { p.FirstPage = 2; p.LastPage = 2; });
        Assert.False(range.Before.SequenceEqual(range.After), "the page range did not move the report bytes");
    }

    /// <summary>
    /// The copy count is honoured by <b>every</b> renderer (<c>RepeatAllPages(EffectiveCopies)</c> appears in all
    /// five), so it must stay offered on a document even once the layout knobs are withdrawn. Without this, the
    /// fix could over-correct and hide a knob that does work.
    /// </summary>
    [Fact]
    public void The_copy_count_still_works_on_a_document_preview()
    {
        var preview = new PrintPreviewViewModel(OutwardTaxInvoice());
        var copies = Render(preview, p => p.Copies = 3);
        Assert.False(copies.Before.SequenceEqual(copies.After),
            "the F5 copy count must keep working on a document — every renderer calls RepeatAllPages.");
    }
}
