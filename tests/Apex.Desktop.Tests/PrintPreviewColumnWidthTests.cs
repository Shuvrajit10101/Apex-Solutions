using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>T0-11 review CRITIC-01 and C17/L3-03 — the approval pane's column budget.</b>
///
/// <para><b>The defect, rendered and read rather than reasoned about.</b> Every cell of the print-preview sheet
/// was a <c>&lt;TextBlock Width="120" TextTrimming="CharacterEllipsis"/&gt;</c> — a literal 120 DIP inside a
/// horizontal StackPanel, no star track, no tooltip. The window is monospace (<c>MainWindow.axaml</c> sets
/// <c>FontFamily="Consolas, Cascadia Mono, Menlo, monospace"</c>), so the cell held eighteen glyphs plus an
/// ellipsis. Captured with Skia at 1280x720 DIP (== 1920x1080 at 150%, an ordinary full-HD laptop), the purchase
/// record's approval pane painted:</para>
/// <code>
///   1. Widget  (HSN 84…      24,691.34
///   2. Widget  (HSN 84…      26,703.69
///   3. Widget  (HSN 84…      45,678.91
///   Tax Charged by the…
///   Supplier: Gujarat …
/// </code>
/// <para>Three supplier lines at three different quantities and three different rates — 2 @ 12,345.67, 3 @
/// 8,901.23 and 1 @ 45,678.91 — identical on screen apart from the serial, with the rate, the quantity and the
/// unit unreachable for EVERY possible item (the row format's own fixed overhead is 16 of the 18 glyphs), and
/// "HSN 84" positively asserted where 847130 was posted. So the slice's own headline defect — "a Purchase item
/// invoice printed with ZERO item detail" — was closed on paper and open on the pane the operator approves.
/// The same 120 DIP cut "Tax Charged by the Supplier" to "Tax Charged by the…", losing the one word that says
/// WHOSE tax the IGST below it is (C17/L3-03), and "Supplier: Gujarat Supplier" to "Supplier: Gujarat …".</para>
///
/// <para><b>The fix these lock.</b> The pane now sizes each column from the print model's OWN declared
/// <see cref="Apex.Ledger.Io.PrintColumn.Weight"/> — the same weights <c>ReportPdf</c> has always split the
/// paper's content width by, and which the pane threw away — floored at the 120 it has always used so no column
/// anywhere can narrow. Nothing about the record branch: the composed item row is shared by every outward tax
/// invoice and every bill of supply, so a record-only fix would have left the identical cut on those.</para>
///
/// <para><b>Why the glyph arithmetic is a recorded constant and not a measurement.</b> The committed headless
/// harness resolves no font — it reports ~11 DIP/glyph, a fiction — so measuring here would prove nothing on the
/// CI runners. <see cref="ConsolasAdvanceAt11"/> was measured out-of-band under a Skia harness that resolves the
/// real face, and the pane render above is its receipt: 27 glyphs of "Tax Charged by the Supplier" at 6.0479 =
/// 163.29 DIP against the 120 cell, and Avalonia's own <c>TextLine.HasCollapsed</c> reported <c>true</c> for
/// exactly the five strings listed. Everything asserted below is either that constant times a length, or a
/// number the view model computes, or a static XAML attribute — no live font is consulted.</para>
/// </summary>
public sealed class PrintPreviewColumnWidthTests
{
    /// <summary>
    /// The real Consolas advance at <c>FontSize="11"</c>, in DIP per glyph — measured out-of-band with SkiaSharp
    /// (<c>SKTypeface.FromFamilyName("Consolas")</c>) on the shipped face, and independently corroborated by the
    /// repository's own calibration in <c>SharedGridVariantBudgetLockTests</c> (0.5498 em). Consolas is
    /// monospace, so one advance describes every glyph.
    /// </summary>
    private const double ConsolasAdvanceAt11 = 6.0479;

    /// <summary>
    /// The width the Particulars column must hold, derived from the row format and NOT from any observed string.
    /// <c>BuildInvoicePreviewReport</c> composes an item cell as
    /// <c>$"{sr}. {Description}  (HSN {HsnSac})  {QuantityText} @ {RateText}"</c>, whose worst case with NO
    /// description at all is:
    /// <list type="bullet">
    /// <item>the format's own fixed overhead — <c>". "</c>, <c>"  (HSN "</c>, <c>")  "</c>, <c>" @ "</c> — plus a
    /// two-digit serial: 16 glyphs;</item>
    /// <item>a full eight-digit HSN: 8;</item>
    /// <item>a quantity at the projector's three-decimal format with its unit, <c>"1,000.000 Nos"</c>: 13;</item>
    /// <item>a lakh-scale rate, <c>"1,23,456.78"</c>: 11.</item>
    /// </list>
    /// 48 glyphs x 6.0479 = 290.30 DIP before a single character of the item's NAME. 300 is that worst case with
    /// the two-digit-serial case rounded up — a floor, never an equality, so a later rebalance may widen it.
    /// </summary>
    private const double ParticularsFloor = 300.0;

    /// <summary>The width every preview column has had since the pane was written. This is a FLOOR after the fix:
    /// the column budget may only ever widen a column, never narrow one, so no other report surface can regress.</summary>
    private const double LegacyCellWidth = 120.0;

    // ================================================================ the fixture
    //
    // The shape T0-11 S2 exists for, and the one the review measured: an INTER-state purchase item invoice from a
    // registered supplier, three lines at three different quantities and rates. Money is odd to the paisa
    // throughout — a round figure would let a formatting defect through — and every literal is derived here.

    private const string OurGstin = "27AAPFU0939F1ZV";        // Maharashtra (27) — the company
    private const string SupplierGstin = "24AAACC1206D1ZM";   // Gujarat (24)     — the supplier
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 4, 10);

    private const decimal Rate1 = 12_345.67m; private const decimal Qty1 = 2m;
    private const decimal Rate2 = 8_901.23m; private const decimal Qty2 = 3m;
    private const decimal Rate3 = 45_678.91m; private const decimal Qty3 = 1m;
    private const decimal Goods = Rate1 * Qty1 + Rate2 * Qty2 + Rate3 * Qty3;   // 97,073.94
    private const decimal Igst = 17_473.31m;                                    // 18% of the goods, to the paisa
    private const decimal SupplierLeg = Goods + Igst;                           // 1,14,547.25

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static (Company Company, Voucher Voucher) TaxedPurchaseRecord()
    {
        var c = CompanyFactory.CreateSeeded("Apex Record Fixture", FyStart);
        c.Address = "12 Marine Lines\nMumbai";
        c.Pin = "400020";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = OurGstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "24" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 GIDC Estate\nSurat", Pincode = "395003" };

        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var inputIgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Input, IsReverseCharge: false } g && g.TaxHead == GstTaxHead.Integrated).Id;

        var v = new Voucher(Guid.NewGuid(), typeId, DocDate, new List<EntryLine>
        {
            new(purchases.Id, new Money(Goods), DrCr.Debit),
            new(inputIgst, new Money(Igst), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(Goods))),
            new(supplier.Id, new Money(SupplierLeg), DrCr.Credit),
        }, number: 42, partyId: supplier.Id,
            referenceNo: "GJ/2025-26/0417", referenceDate: new DateOnly(2025, 4, 8),
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, Qty1, new Money(Rate1)),
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, Qty2, new Money(Rate2)),
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, Qty3, new Money(Rate3)),
            });
        return (c, v);
    }

    private static PreviewPage RecordPage()
    {
        var (c, v) = TaxedPurchaseRecord();
        var preview = new VoucherDetailViewModel(c, v).BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, preview.Kind);
        return Assert.Single(preview.Pages);
    }

    /// <summary>What the operator can actually read in a cell: the glyphs that fit, with the last one spent on the
    /// ellipsis when the string overflows — Avalonia's <c>CharacterEllipsis</c> behaviour, restated in arithmetic
    /// so it can be asserted without a font.</summary>
    private static string Painted(PreviewCell cell)
    {
        int capacity = (int)(cell.Width / ConsolasAdvanceAt11);
        return cell.Text.Length <= capacity ? cell.Text : cell.Text[..Math.Max(0, capacity - 1)] + "…";
    }

    // ================================================================ CRITIC-01

    /// <summary>
    /// 🔴 <b>CRITIC-01, the headline.</b> Three item rows at three different quantities and three different rates
    /// must not paint as three identical strings. Asserted on what the pane can PAINT, not on the row data — the
    /// shipped guard (<c>PurchaseRecordPrintTests</c>) asserts over the cell strings and is structurally blind to
    /// a cell that cuts them all at the same 18th glyph.
    /// </summary>
    [Fact]
    public void The_records_three_item_rows_do_not_paint_as_three_identical_rows()
    {
        var page = RecordPage();
        var items = page.Lines
            .Where(l => l.Cells[0].StartsWith("1. ", StringComparison.Ordinal)
                     || l.Cells[0].StartsWith("2. ", StringComparison.Ordinal)
                     || l.Cells[0].StartsWith("3. ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, items.Count);   // non-vacuity: the fixture really does carry three lines

        var painted = items.Select(l => Painted(l.Columns[0])).ToList();

        // Each rate is the particular the operator is checking the supplier's bill against, and each is the LAST
        // thing in the row — so it is the first thing a tail cut loses.
        foreach (var (row, rate) in painted.Zip(new[] { "12,345.67", "8,901.23", "45,678.91" }))
            Assert.Contains(rate, row, StringComparison.Ordinal);

        // …and the quantity and unit, which the pre-fix pane also could not reach for any item.
        foreach (var (row, qty) in painted.Zip(new[] { "2 Nos", "3 Nos", "1 Nos" }))
            Assert.Contains(qty, row, StringComparison.Ordinal);

        // The HSN must be the one that was POSTED. "HSN 84" is not a blank — it is a positive two-digit chapter
        // code where an eight-digit code exists.
        Assert.All(painted, row => Assert.Contains("HSN 847130", row, StringComparison.Ordinal));

        // Three different lines, three different paintings.
        Assert.Equal(3, painted.Distinct(StringComparer.Ordinal).Count());
    }

    // ================================================================ C17 / L3-03

    /// <summary>
    /// 🔴 <b>C17/L3-03.</b> The caption that says WHOSE tax the head rows state is the one thing RQ-11a makes
    /// binding about a record's tax, and the mirror has no per-rate breakup table to say it anywhere else — so it
    /// must survive the pane whole. The word that used to disappear was "Supplier".
    ///
    /// <para>The caption WORDING is untouched and is not this test's business: it is under an open R12 question
    /// (plan.md Phase 10.13). This asserts only that whatever the caption says, the operator can read it.</para>
    /// </summary>
    [Fact]
    public void The_supplier_tax_caption_and_the_supplier_name_paint_whole()
    {
        var page = RecordPage();

        var caption = Assert.Single(page.Lines,
            l => l.Cells[0] == GstReportSupport.SupplierTaxCaption).Columns[0];
        Assert.Equal(GstReportSupport.SupplierTaxCaption, Painted(caption));
        Assert.EndsWith(" Supplier", Painted(caption), StringComparison.Ordinal);

        var party = Assert.Single(page.Lines,
            l => l.Cells[0].StartsWith("Supplier: ", StringComparison.Ordinal)).Columns[0];
        Assert.Equal("Supplier: Gujarat Supplier", Painted(party));

        // The head row the caption governs, and the figure under it, are on the same pane and must also be whole.
        Assert.Equal("17,473.31", Painted(Assert.Single(page.Lines, l => l.Cells[0] == "IGST").Columns[2]));
        Assert.Equal("1,14,547.25", Painted(Assert.Single(page.Lines, l => l.Cells[0] == "Grand Total").Columns[2]));
    }

    // ================================================================ the budget itself

    /// <summary>
    /// The column budget, asserted as arithmetic rather than as an observed string: the Particulars column holds
    /// the item-row format's description-free worst case (see <see cref="ParticularsFloor"/>), and NO column is
    /// ever narrower than the 120 the pane has always used — so this fix can only widen, never regress another
    /// report surface.
    /// </summary>
    [Fact]
    public void Every_preview_column_holds_its_share_and_none_is_narrower_than_before()
    {
        var page = RecordPage();

        Assert.True(page.Columns[0].Width >= ParticularsFloor,
            $"Particulars is {page.Columns[0].Width:F2} DIP; the item row's description-free worst case is "
            + $"{48 * ConsolasAdvanceAt11:F2} DIP, so the rate and quantity are unreachable for every item.");

        Assert.All(page.Columns, w => Assert.True(w.Width >= LegacyCellWidth,
            $"a preview column narrowed to {w.Width:F2} DIP, below the {LegacyCellWidth} the pane has always used."));

        // The HEADER band and EVERY BODY LINE must share one set of widths, or the columns stop lining up under
        // their captions — which is how a money figure ends up read under the wrong heading. Asserted line by line
        // rather than header-against-itself: the two are laid out by separate loops over separate row objects, and
        // an equality between `Columns` and `HeaderColumns` alone would be satisfied by definition.
        Assert.NotEmpty(page.Lines);
        foreach (var line in page.Lines)
        {
            Assert.Equal(page.HeaderColumns.Count, line.Columns.Count);
            for (int i = 0; i < line.Columns.Count; i++)
                Assert.Equal(page.HeaderColumns[i].Width, line.Columns[i].Width);
        }

        // The widths come from the print model's own declared weights — the same weights the PDF has always split
        // the paper by — so the pane and the paper give the same column its share.
        Assert.True(page.Columns[0].Width > page.Columns[2].Width,
            "Particulars (weight 4) must be wider than Amount (weight 1.5); the pane is ignoring the weights.");
    }

    /// <summary>
    /// The composed item cell is written once and shared by every document kind, so widening it for the record
    /// widens it for the outward tax invoice and the bill of supply too. Locked here because a later "scope it to
    /// the record" edit would silently re-open the identical cut on the documents we ISSUE.
    /// </summary>
    [Fact]
    public void The_widened_particulars_column_is_the_shared_one_not_a_record_special_case()
    {
        var record = RecordPage();

        // A plain voucher preview and a POS receipt preview both go through the same pane and the same builder.
        var (c, v) = TaxedPurchaseRecord();
        var voucherPage = Assert.Single(
            new PrintPreviewViewModel(VoucherPrintProjector.ProjectVoucher(c, v)).Pages);

        Assert.All(record.Columns, col => Assert.True(col.Width >= LegacyCellWidth));
        Assert.All(voucherPage.Columns, col => Assert.True(col.Width >= LegacyCellWidth));
        Assert.True(voucherPage.Columns[0].Width > LegacyCellWidth,
            "the voucher mirror's Particulars column did not widen, so the budget is a record special case.");
    }

    // ================================================================ the markup

    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    /// <summary>
    /// The cell template itself, in the style of the repository's other static-XAML locks: the width must be BOUND
    /// (a literal is what made the cut invariant under every window size and every DPI) and the cell must carry a
    /// tooltip, which is the only recovery for a stock-item name longer than the widened column — the pane's own
    /// Subtitle five lines above has carried one all along.
    /// </summary>
    [Fact]
    public void The_preview_cell_template_binds_its_width_and_offers_the_full_text()
    {
        var path = AxamlPath();
        Assert.True(File.Exists(path), $"MainWindow.axaml not found at '{path}'.");
        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);

        // The preview sheet's two cell templates — the header band and the body cells — located by the compiled
        // binding's own data type, so the assertion cannot drift onto the payroll matrix's Text/Width cell (which
        // is the same idiom on a different model and is locked by its own test).
        var xNs = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var cells = doc.Descendants(Av + "DataTemplate")
            .Where(t => (string?)t.Attribute(xNs + "DataType") == "vm:PreviewCell")
            .SelectMany(t => t.Descendants(Av + "TextBlock"))
            .ToList();
        Assert.Equal(2, cells.Count);

        foreach (var cell in cells)
        {
            Assert.Equal("{Binding Width}", (string?)cell.Attribute("Width"));
            Assert.Equal("{Binding Text}", (string?)cell.Attribute("ToolTip.Tip"));
        }
    }
}
