using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W0-1 follow-up — the three defects the W0-1 review left open.</b>
///
/// <list type="number">
/// <item><b>W0-1b — the POS TWIN.</b> W0-1 routed the voucher-screen document only. <c>PosReceiptPdf</c> was never
/// touched: it titled every receipt from <c>PosConfig.DefaultTitle</c> ("Retail Invoice"), drew a "Taxable" head line
/// unconditionally and CGST/SGST (or IGST) gated on nothing but <c>IsInterState</c>. So the SAME composition dealer
/// selling the SAME goods got a §31(3)(c) bill of supply from the voucher screen and, over the counter, a receipt
/// asserting the tax heads CGST Act §10(4) bars him from collecting — one dealer, two documents, two answers.</item>
/// <item><b>The §10 contradiction had to be refused at ACCEPT, not explained at print (R12 user decision).</b>
/// §10(4): a composition dealer "shall not collect any tax from the recipient on supplies made by him", so a §10
/// outward supply carrying POSTED forward CGST/SGST/IGST (or Compensation Cess) should never have been postable. The
/// posting is now refused with a named message. The print-path refusal REMAINS (it is not dead code): a book written
/// before this guard, or imported from one, can still contain the shape, and it must still open, read and print.</item>
/// <item><b>The projector must refuse it structurally.</b> <c>IsTaxInvoice</c> returned false for that voucher while
/// <c>ProjectInvoice</c> still returned a TAX INVOICE DTO (its own <c>IsBillOfSupply</c> bails before the §10 limb),
/// whose Grand Total understated the party leg by the whole tax. Exactly one <c>src/</c> caller checked first.</item>
/// </list>
///
/// <para>Sources (verbatim, official): CGST Act §31(3)(c), §10(4), §32(2), §2(98) —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; CGST Rules 46, 46A, 49 and composition
/// Rule 5(1)(f) — <c>https://cbic-gst.gov.in/pdf/01062021-CGST-Rules-2017-Part-A-Rules.pdf</c>.</para>
///
/// <para>Fixtures are deliberately odd-valued (₹10,225.37, ₹269.79, ₹55,810.14) — round numbers assert nothing.</para>
/// </summary>
public sealed class BillOfSupplyPosAndPostingGuardTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BillOfSupplyPosAndPostingGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBosFollowUp_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Reads back one of <c>IndianFormat</c>'s money cells ("64,323.55") as a decimal, so a preview row can
    /// be FOOTED rather than merely string-matched.</summary>
    private static decimal ParseIndian(string cell) => decimal.Parse(
        cell,
        System.Globalization.NumberStyles.AllowThousands | System.Globalization.NumberStyles.AllowDecimalPoint
            | System.Globalization.NumberStyles.AllowLeadingSign,
        System.Globalization.CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- POS scaffolding

    private sealed class PosKit
    {
        public required Company Company { get; init; }
        public required VoucherType PosType { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid ExemptItemId { get; init; }

        /// <summary>De-oiled cake (HSN 230630) — GST-exempt AND the one good CGST Rule 138(14)(e) expressly keeps
        /// inside the e-way net, "the goods, <b>other than de-oiled cake</b>, being transported, are specified in the
        /// Schedule appended to notification No. 2/2017- Central tax (Rate)". Any assertion here that an e-Way Bill is
        /// genuinely <b>Required</b> for a wholly-exempt movement must ride this item, never
        /// <see cref="ExemptItemId"/> (fresh milk 0401 is Schedule S. No. 25 — clause (e) RELIEVES it, so asserting
        /// Required for it states our engine's over-generation as though it were the statute). W0-9 tail finding #5.
        /// </summary>
        public required Guid DeOiledCakeItemId { get; init; }

        /// <summary>A Gujarat (24) buyer against a Maharashtra (27) home State — the INTER-State routing the whole
        /// bill-of-supply suite never rendered once (W0-1 follow-up review, finding #8).</summary>
        public required Guid OutOfStatePartyId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A GST company (Regular or Composition) with a POS-flagged Sales type, a taxable item and a wholly
    /// exempt one on the shelf, and the tender ledgers POS validation requires.</summary>
    private static PosKit NewPosKit(GstRegistrationType registration)
    {
        var c = CompanyFactory.CreateSeeded("POS " + registration + " Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var taxable = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        taxable.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var exempt = masters.CreateStockItem("Fresh Milk", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };
        // W0-9 tail finding #5 — the exempt good Rule 138(14)(e) carves OUT of its own relief, so a coverage assertion
        // driven on it is a statement about the statute rather than about our (deliberately over-generating) engine.
        var deOiledCake = masters.CreateStockItem("De-oiled Cake", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        deOiledCake.Gst = new StockItemGstDetails { HsnSac = "230630", Taxability = GstTaxability.Exempt };
        var main = c.MainLocation!.Id;

        AddLedger(c, "Sales (POS)", "Sales Accounts", openingIsDebit: false);
        // finding #8: a real out-of-state buyer, so the POS path can be driven on the INTER-State routing.
        var gujaratBuyer = AddLedger(c, "Gujarat Buyer", "Sundry Debtors", openingIsDebit: true);
        gujaratBuyer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24",
        };
        AddLedger(c, "Gift Voucher", "Sundry Debtors", openingIsDebit: true);
        AddLedger(c, "ICICI Card", "Bank Accounts", openingIsDebit: true);
        AddLedger(c, "SBI Cheque", "Bank Accounts", openingIsDebit: true);
        AddLedger(c, "Cash", "Cash-in-Hand", openingIsDebit: true);

        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig
            {
                PrintAfterSave = true,
                DefaultTitle = "Retail Invoice",
                Message1 = "Thank you, please call again",
            });
        c.AddVoucherType(posType);

        // Stock the shelf so the sales below have on-hand behind them.
        var ledgers = new LedgerService(c);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", openingIsDebit: false);
        ledgers.Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(30_000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(30_000m), DrCr.Credit),
            },
            inventoryLines: new[]
            {
                new VoucherInventoryLine(taxable.Id, main, 5m, Money.FromRupees(2_000m)),
                new VoucherInventoryLine(exempt.Id, main, 5m, Money.FromRupees(2_000m)),
                new VoucherInventoryLine(deOiledCake.Id, main, 5m, Money.FromRupees(2_000m)),
            }));

        return new PosKit
        {
            Company = c, PosType = posType, TaxableItemId = taxable.Id, ExemptItemId = exempt.Id,
            DeOiledCakeItemId = deOiledCake.Id, OutOfStatePartyId = gujaratBuyer.Id,
        };
    }

    /// <summary>Sells one unit of <paramref name="itemId"/> at an odd rate through the real POS screen and returns the
    /// receipt the print-after-save hand-off carries. Pass <paramref name="partyId"/> to bill a named buyer — an
    /// out-of-state one drives the INTER-State routing (finding #8); the default walk-in is intra-State.</summary>
    private PosReceiptData SellOverTheCounter(PosKit k, Guid itemId, string rate, Guid? partyId = null)
    {
        var vm = new PosBillingViewModel(k.Company, k.PosType, _storage, () => { }, () => { });
        PosReceiptData? receipt = null;
        vm.PrintReceiptRequested += r => receipt = r;

        if (partyId is { } pid)
            vm.SelectedParty = vm.Parties.Single(o => o.Ledger?.Id == pid);

        var line = vm.Items[0];
        line.SelectedItem = k.Company.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "1";
        line.RateText = rate;
        Assert.True(vm.Accept(), vm.Message);

        Assert.NotNull(receipt);
        return receipt!;
    }

    // ================================================================ 1 — W0-1b: the POS twin

    /// <summary>
    /// <b>W0-1b (HIGH).</b> A composition dealer's over-the-counter receipt asserted "Taxable / CGST / SGST" head
    /// lines. CGST Act §10(4) bars him from collecting "any tax from the recipient on supplies made by him" and
    /// §32(2) forbids a registered person collecting tax otherwise than as the Act allows, so the document may not
    /// state a tax head at all; §31(3)(c) makes it a bill of supply, and CGST Rule 49 prescribes eight particulars,
    /// none of which is a rate or an amount of tax. Rule 5(1)(f) then requires the composition wording "at the top".
    ///
    /// <para>The figures were already zero (<c>ComputeInvoiceTax</c> short-circuits for composition), so nothing was
    /// over-collected — but the paper still ASSERTED heads he may not show, and titled itself "Retail Invoice".</para>
    /// </summary>
    [Fact]
    public void A_composition_dealers_POS_receipt_is_a_bill_of_supply_carrying_no_tax_head()
    {
        var k = NewPosKit(GstRegistrationType.Composition);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37");   // odd paisa

        Assert.True(receipt.IsBillOfSupply);
        Assert.Equal(GstReportSupport.BillOfSupplyDeclaration, receipt.TopDeclaration);
        Assert.Equal(Money.FromRupees(10_225.37m), receipt.GrandTotal);     // Rule 49(g): the value of supply

        var text = AsLatin1(PosReceiptPdf.Render(receipt, new PageConfig()));
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("RETAIL INVOICE", text);
        Assert.DoesNotContain("Retail Invoice", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        // Rule 49 has no counterpart to Rule 46 (l) rate of tax or (m) amount of tax.
        Assert.DoesNotContain("CGST", text);
        Assert.DoesNotContain("SGST", text);
        Assert.DoesNotContain("IGST", text);
        Assert.DoesNotContain("GST Breakup", text);
        Assert.DoesNotContain("Taxable", text);
        Assert.Contains("Value of Supply", text);
        Assert.Contains("10,225.37", text);
        // Rule 5(1)(f): the composition wording, at the TOP of the bill of supply.
        Assert.Contains(GstReportSupport.BillOfSupplyDeclaration, text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #8 (MEDIUM) — the same receipt, billed to an OUT-OF-STATE buyer.</b>
    /// <c>SellOverTheCounter</c> never selected a party, so <c>interState</c> was false on every POS fixture in the
    /// suite and the <c>!IsBillOfSupply</c> gate around the IGST head line was never reached in this renderer or its
    /// mirror. <c>DoesNotContain("IGST")</c> on an intra-State receipt is vacuous — that line cannot appear intra-State
    /// whether the gate is there or not. Driven through the REAL POS screen with a Gujarat (24) buyer against a
    /// Maharashtra (27) home State, so <c>IsInterState</c> is genuinely true.
    /// </summary>
    [Fact]
    public void A_composition_dealers_INTER_state_POS_receipt_still_carries_no_tax_head()
    {
        var k = NewPosKit(GstRegistrationType.Composition);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37", k.OutOfStatePartyId);

        Assert.True(receipt.IsInterState);          // the branch that had no coverage at all
        Assert.True(receipt.IsBillOfSupply);
        Assert.Equal(Money.FromRupees(10_225.37m), receipt.GrandTotal);

        var text = AsLatin1(PosReceiptPdf.Render(receipt, new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("IGST", text);
        Assert.DoesNotContain("CGST", text);
        Assert.DoesNotContain("Taxable", text);
        Assert.Contains("Value of Supply", text);
        Assert.Contains("10,225.37", text);

        var preview = new PrintPreviewViewModel(receipt);
        Assert.Equal(GstReportSupport.BillOfSupplyTitle, preview.Pages[0].Title);
        Assert.StartsWith("Bill of Supply", preview.ReportTitle);
        var cells = preview.Pages.SelectMany(p => p.Lines).SelectMany(r => r.Cells).ToList();
        Assert.DoesNotContain("IGST", cells);
        Assert.Contains("Value of Supply", cells);
    }

    /// <summary>The other §31(3)(c) limb over the counter: a REGULAR dealer's wholly exempt POS sale is a bill of
    /// supply too — but he is not a composition taxable person, so Rule 5(1)(f)'s wording must NOT appear.</summary>
    [Fact]
    public void A_regular_dealers_wholly_exempt_POS_sale_is_a_bill_of_supply_without_the_section_10_wording()
    {
        var k = NewPosKit(GstRegistrationType.Regular);
        var receipt = SellOverTheCounter(k, k.ExemptItemId, "269.79");   // odd paisa

        Assert.True(receipt.IsBillOfSupply);
        Assert.Equal(string.Empty, receipt.TopDeclaration);
        Assert.Equal(Money.FromRupees(269.79m), receipt.GrandTotal);

        var text = AsLatin1(PosReceiptPdf.Render(receipt, new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("CGST", text);
        Assert.DoesNotContain("Composition taxable person", text);
        Assert.Contains("269.79", text);
    }

    /// <summary>ER-13 at the statutory boundary: an ordinary taxable POS sale by a Regular dealer is UNCHANGED — the
    /// operator's configured title, the "Taxable" head line and the CGST/SGST heads all still print.</summary>
    [Fact]
    public void An_ordinary_taxable_POS_receipt_is_unchanged()
    {
        var k = NewPosKit(GstRegistrationType.Regular);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37");

        Assert.False(receipt.IsBillOfSupply);
        Assert.Equal(string.Empty, receipt.TopDeclaration);

        var engine = GstService.ComputeLineTax(Money.FromRupees(10_225.37m), 1800, interState: false);
        Assert.Equal(engine.Cgst, receipt.TotalCgst);
        Assert.Equal(engine.Sgst, receipt.TotalSgst);

        var text = AsLatin1(PosReceiptPdf.Render(receipt, new PageConfig()));
        Assert.Contains("Retail Invoice", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.Contains("Taxable", text);
        Assert.Contains("CGST", text);
        Assert.Contains("SGST", text);
        Assert.Contains("GST Breakup", text);
        Assert.DoesNotContain("Composition taxable person", text);
        Assert.DoesNotContain("Value of Supply", text);
    }

    /// <summary>The operator approves the on-screen mirror and the customer receives the bytes; if the two disagree on
    /// what the document IS, the operator approved one document and issued another (the FIX-W1f lesson). The receipt
    /// mirror must suppress exactly what <c>PosReceiptPdf</c> suppresses.</summary>
    [Fact]
    public void The_POS_receipt_preview_mirror_suppresses_exactly_what_the_receipt_bytes_suppress()
    {
        var k = NewPosKit(GstRegistrationType.Composition);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37");

        var preview = new PrintPreviewViewModel(receipt);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Receipt, preview.Kind);
        Assert.Equal(GstReportSupport.BillOfSupplyTitle, preview.Pages[0].Title);

        var cells = preview.Pages.SelectMany(p => p.Lines).SelectMany(r => r.Cells).ToList();
        Assert.DoesNotContain("CGST", cells);
        Assert.DoesNotContain("SGST", cells);
        Assert.DoesNotContain("IGST", cells);
        Assert.DoesNotContain("Taxable", cells);
        Assert.DoesNotContain("Grand Total", cells);
        Assert.Contains("Value of Supply", cells);
        Assert.Contains(GstReportSupport.BillOfSupplyDeclaration, cells);

        // W0-1 follow-up review, findings #2/#5: the pane HEADING is not decoration. `ReportTitle` is bound as the
        // cascade column heading, is surfaced as PrintConfigViewModel.DocumentTitle, and is the DEFAULT SAVED FILE
        // NAME — so the operator saved "Retail Receipt No. 1.pdf" for a document whose title band, number caption and
        // closing declaration all read bill of supply. The invoice ctor was deliberately changed to name the document
        // kind on the principle that "a bill of supply must not be announced as a tax invoice anywhere in the app, on
        // screen or on paper"; the receipt twin was left on the old literal.
        Assert.StartsWith("Bill of Supply", preview.ReportTitle);
        Assert.DoesNotContain("Retail Receipt", preview.ReportTitle);
    }

    /// <summary>ER-13 for the same heading: a REGULAR dealer's ordinary taxable POS receipt keeps the "Retail
    /// Receipt No. N" heading it has always had.</summary>
    [Fact]
    public void An_ordinary_POS_receipt_preview_keeps_the_retail_receipt_heading()
    {
        var k = NewPosKit(GstRegistrationType.Regular);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37");

        var preview = new PrintPreviewViewModel(receipt);
        Assert.StartsWith("Retail Receipt", preview.ReportTitle);
        Assert.DoesNotContain("Bill of Supply", preview.ReportTitle);
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #3 (MEDIUM) — the approval screen did not add up to its own total.</b>
    /// <c>InvoicePdf.DrawClosingBlock</c> prints a "Compensation Cess" line whenever <c>TotalCess != 0</c> (FIX-1: cess
    /// is ring-fenced OUT of <c>TotalTax</c> but IN <c>GrandTotal</c>, because the accept path adds it to the party
    /// leg). <c>BuildInvoicePreviewReport</c> — the mirror the operator APPROVES before the bytes are issued — emitted
    /// only Taxable Value, CGST, SGST and Grand Total. On a cess-bearing invoice the visible money rows summed to
    /// ₹55,810.14 under a printed Grand Total of ₹64,323.55: the whole ₹8,513.41 of cess invisible. The same
    /// preview-vs-bytes divergence class as FIX-W1f / FIX-W1h, on the one line neither checked.
    /// <para><b>Bite:</b> delete the new Compensation Cess row from <c>BuildInvoicePreviewReport</c> and this goes
    /// red on the parity sum.</para>
    /// </summary>
    [Fact]
    public void The_invoice_preview_mirror_states_the_compensation_cess_the_bytes_state()
    {
        var inv = new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = new InvoicePartyBlock { Name = "Apex Traders" },
            Buyer = new InvoicePartyBlock { Name = "Acme Retail" },
            InvoiceNumber = "INV-0431",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "Maharashtra (27)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Aerated Beverage", HsnSac = "22021010",
                    QuantityText = "60.125", RateText = "786.64", TaxableValue = Money.FromRupees(47_296.73m),
                },
            },
            TotalTaxable = Money.FromRupees(47_296.73m),
            TotalCgst = Money.FromRupees(4_256.71m),
            TotalSgst = Money.FromRupees(4_256.70m),
            TotalCess = Money.FromRupees(8_513.41m),
        };
        Assert.Equal(Money.FromRupees(64_323.55m), inv.GrandTotal);

        var preview = new PrintPreviewViewModel(inv);
        var lines = preview.Pages.SelectMany(p => p.Lines).ToList();

        // The bytes state it …
        var bytes = AsLatin1(preview.PdfBytes);
        Assert.Contains("Compensation Cess", bytes);
        Assert.Contains("8,513.41", bytes);
        Assert.Contains("64,323.55", bytes);

        // … so the mirror must state it too, with the same figure.
        var cessRow = Assert.Single(lines, r => r.Cells[0] == "Compensation Cess");
        Assert.Equal("8,513.41", cessRow.Cells[2]);

        // And the mirror's own money rows must FOOT to the Grand Total it prints — the property that actually
        // failed: four rows summing to 55,810.14 under a printed total of 64,323.55.
        var money = lines
            .Where(r => r.Cells[0] is "Taxable Value" or "CGST" or "SGST" or "IGST" or "Compensation Cess" or "Round Off")
            .Sum(r => ParseIndian(r.Cells[2]));
        Assert.Equal(inv.GrandTotal.Amount, money);
        Assert.Equal("64,323.55", Assert.Single(lines, r => r.Cells[0] == "Grand Total").Cells[2]);
    }

    /// <summary>
    /// <b>FIX-W2b — the operator's approval screen had the same one-letter hole as the renderer.</b>
    /// <c>PrintPreviewViewModel</c> rejected the tax-invoice title by ORDINAL equality against the upper-case
    /// constant, so a DTO carrying "Tax Invoice" (the spelling <c>VoucherDetailViewModel.DocumentLabel</c> itself
    /// uses) headed a bill of supply "Tax Invoice" on screen and in the bytes. Locked here in the casings a real
    /// caller could plausibly produce.
    /// </summary>
    [Theory]
    [InlineData("Tax Invoice")]
    [InlineData("tax invoice")]
    [InlineData("  TAX INVOICE  ")]
    public void The_invoice_preview_mirror_rejects_the_tax_invoice_title_in_any_casing(string documentTitle)
    {
        var preview = new PrintPreviewViewModel(new InvoicePrintData
        {
            IsBillOfSupply = true,
            DocumentTitle = documentTitle,
            InvoiceNumber = "BOS-0421",
            InvoiceDateText = "31-03-2025",
            TotalTaxable = Money.FromRupees(47_296.73m),
        });

        Assert.Equal(GstReportSupport.BillOfSupplyTitle, preview.Pages[0].Title);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("Tax Invoice", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("tax invoice", text);
    }

    // ================================================================ 2 — ProjectInvoice refuses structurally

    /// <summary>
    /// Builds the §10 contradiction the way the shipped UI reaches it: post a taxed sale as a REGULAR dealer, then
    /// switch Registration Type to Composition in the F11 GST config (idempotent, and it checks no existing voucher).
    /// </summary>
    private (Company Company, Voucher Voucher, Guid CustomerId) CompositionContradiction(string companyName)
    {
        var c = CompanyFactory.CreateSeeded(companyName, FyStart);
        var gstSvc = new GstService(c);
        gstSvc.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var cgst = gstSvc.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgst = gstSvc.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;
        var supply = Money.FromRupees(47_296.73m);
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(12), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(55_810.14m), DrCr.Debit),   // 47,296.73 + 4,256.71 + 4,256.70
            new EntryLine(sales.Id, supply, DrCr.Credit),
            new EntryLine(cgst.Id, Money.FromRupees(4_256.71m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, supply)),
            new EntryLine(sgst.Id, Money.FromRupees(4_256.70m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 900, supply)),
        }, partyId: customer.Id);

        new LedgerService(c).Post(v);     // lawful while he is a REGULAR dealer

        c.Gst!.RegistrationType = GstRegistrationType.Composition;
        c.Gst!.CompositionSubType = CompositionSubType.Trader;
        return (c, v, customer.Id);
    }

    /// <summary>
    /// <b>The projector and its own predicate disagreed.</b> For a §10 voucher carrying posted forward tax,
    /// <c>IsTaxInvoice</c> returns false — but <c>ProjectInvoice</c> still returned a TAX INVOICE DTO, because its
    /// <c>IsBillOfSupply</c> bails on the posted-tax gate BEFORE the §10 limb. Its Grand Total (₹47,296.73, taken from
    /// a live <c>ComputeInvoiceTax</c> that short-circuits for composition) understated the posted party leg
    /// (₹55,810.14) by the whole ₹8,513.41 of tax. Exactly one <c>src/</c> call site checked <c>IsTaxInvoice</c>
    /// first; the projector is public and nothing pinned the mismatch.
    ///
    /// <para>The projector now refuses structurally, so a future caller cannot reintroduce the understated document by
    /// forgetting the predicate.</para>
    /// </summary>
    [Fact]
    public void ProjectInvoice_refuses_the_section_10_contradiction_instead_of_understating_it()
    {
        var (c, v, customerId) = CompositionContradiction("Refusal Co");

        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        Assert.False(VoucherPrintProjector.IsBillOfSupply(c, v));

        var ex = Assert.Throws<InvalidOperationException>(() => VoucherPrintProjector.ProjectInvoice(c, v));
        Assert.Contains("section 10", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("31(3)(c)", ex.Message, StringComparison.Ordinal);

        // …and the ONE real call site is unaffected: it still prints the plain Dr/Cr voucher, which states the
        // posted debt in full.
        var preview = new VoucherDetailViewModel(c, v).BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);
        var text = AsLatin1(preview.PdfBytes);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.Contains("55,810.14", text);
        Assert.Equal(Money.FromRupees(55_810.14m),
            v.Lines.Single(l => l.LedgerId == customerId && l.Side == DrCr.Debit).Amount);
    }

    // ================================================================ 3 — R12: refuse the POSTING, not the paper

    /// <summary>
    /// <b>R12 user decision (2026-08-10).</b> CGST Act §10(4): a composition dealer "shall not collect any tax from
    /// the recipient on supplies made by him". A §10 outward supply carrying posted forward CGST/SGST/IGST (or
    /// Compensation Cess) therefore records something the law forbids, and printing an unclassifiable document
    /// afterwards is the wrong remedy — the entry is refused at Accept, with a named message.
    /// </summary>
    [Fact]
    public void A_composition_company_cannot_post_an_outward_supply_that_collects_tax()
    {
        var (c, _, customerId) = CompositionContradiction("Accept Guard Co");
        var gstSvc = new GstService(c);
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var sales = c.FindLedgerByName("Sales")!;
        var cgst = gstSvc.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgst = gstSvc.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;
        var supply = Money.FromRupees(47_296.73m);

        var offending = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(20), new[]
        {
            new EntryLine(customerId, Money.FromRupees(55_810.14m), DrCr.Debit),
            new EntryLine(sales.Id, supply, DrCr.Credit),
            new EntryLine(cgst.Id, Money.FromRupees(4_256.71m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, supply)),
            new EntryLine(sgst.Id, Money.FromRupees(4_256.70m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 900, supply)),
        }, partyId: customerId);

        var before = c.Vouchers.Count;
        var ex = Assert.Throws<InvalidVoucherException>(() => new LedgerService(c).Post(offending));
        Assert.Contains("composition", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section 10(4)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, c.Vouchers.Count);       // nothing persisted
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #1 (HIGH) — the guard only saw tax it had TAGGED.</b>
    /// <c>IsCompositionSupplyCarryingForwardTax</c> asked <c>HasForwardTaxLines</c> / <c>HasPostedForwardCessLines</c>,
    /// which both require <c>line.Gst is not null</c> — the <see cref="GstLineTax"/> metadata only the GST-engine
    /// accept paths stamp. The shipped Sales <b>As-Voucher</b> screen builds every leg as a plain
    /// <c>new EntryLine(ledgerId, amount, side, …)</c> with <b>no</b> <c>gst:</c> argument
    /// (<c>VoucherEntryViewModel</c>), and its particulars picker is the UNFILTERED company ledger list — so a
    /// composition dealer could hand-key <c>Cr Output CGST 4,256.71 / Cr Output SGST 4,256.70</c> and the §10(4)
    /// guard never saw it. The posting the guard exists to refuse was ACCEPTED, and the same voucher then routed as a
    /// BILL OF SUPPLY carrying the Rule 5(1)(f) declaration that he may not collect tax — printed directly above entry
    /// rows reading Output CGST / Output SGST. The false statutory statement FIX-W1e removed, reached with no import,
    /// no crafted file and no tampering.
    ///
    /// <para>"Carries forward tax" is now a question about the GENERAL LEDGER, not about metadata: a posting to one of
    /// the company's own <b>Output</b> CGST/SGST/IGST/Cess ledgers is collected tax whether or not anything tagged it
    /// (the same ledger-classification read <c>GstReportSupport.RcmLines</c> already performs).</para>
    ///
    /// <para>Sources: CGST Act §10(4), §31(3)(c), §32(2) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; Rule 5(1)(f) —
    /// <c>https://cbic-gst.gov.in/pdf/01062021-CGST-Rules-2017-Part-A-Rules.pdf</c>.</para>
    /// </summary>
    [Fact]
    public void A_hand_keyed_untagged_tax_leg_is_still_a_section_10_4_collection_and_is_refused()
    {
        var (c, customerId, cgstId, sgstId) = CompositionWithOutputLedgers("Untagged Guard Co");
        var offending = UntaggedTaxedSale(c, customerId, cgstId, sgstId, FyStart.AddDays(14));

        // (a) THE POSTING IS REFUSED AT ACCEPT — which is what the slice advertises.
        var before = c.Vouchers.Count;
        var ex = Assert.Throws<InvalidVoucherException>(() => new LedgerService(c).Post(offending));
        Assert.Contains("section 10(4)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, c.Vouchers.Count);
    }

    /// <summary>
    /// The rehydration half of finding #1: a book that ALREADY contains the untagged shape still opens (the guard is
    /// entry-scoped), and the drilled voucher must not badge itself a bill of supply over entry rows that read Output
    /// CGST / Output SGST.
    /// </summary>
    [Fact]
    public void An_already_posted_untagged_tax_leg_is_never_badged_a_bill_of_supply()
    {
        var (c, customerId, cgstId, sgstId) = CompositionWithOutputLedgers("Untagged Legacy Co");
        var v = UntaggedTaxedSale(c, customerId, cgstId, sgstId, FyStart.AddDays(14));
        new LedgerService(c).Post(v, CostAllocationStrictness.Legacy);   // the rehydration path, as Load uses

        // The metadata-only predicates still say "no tax here" — that is the whole point of the finding.
        Assert.False(GstReportSupport.HasForwardTaxLines(v));
        Assert.False(GstReportSupport.HasPostedForwardCessLines(v));
        // …but the GL says otherwise, and the document kind must follow the GL.
        Assert.True(GstReportSupport.IsCompositionBillOfSupply(c, v));      // he IS a §10 dealer
        Assert.False(VoucherPrintProjector.IsBillOfSupply(c, v));           // …yet neither document may be issued
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));

        var detail = new VoucherDetailViewModel(c, v);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);

        // It prints the plain Dr/Cr voucher, which states the posted debt in full.
        var preview = detail.BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("55,810.14", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.DoesNotContain(GstReportSupport.BillOfSupplyDeclaration, text);
    }

    /// <summary>A composition company that still carries the six Output/Input tax ledgers (seeded while he was a
    /// REGULAR dealer, then retained across the F11 switch — <c>EnableGst</c> only SKIPS seeding for composition, it
    /// never deletes), plus a local customer.</summary>
    private static (Company Company, Guid CustomerId, Guid CgstId, Guid SgstId) CompositionWithOutputLedgers(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var gstSvc = new GstService(c);
        gstSvc.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });

        AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var cgst = gstSvc.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgst = gstSvc.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        c.Gst!.RegistrationType = GstRegistrationType.Composition;
        c.Gst!.CompositionSubType = CompositionSubType.Trader;
        return (c, customer.Id, cgst.Id, sgst.Id);
    }

    /// <summary>The As-Voucher shape exactly: four plain <see cref="EntryLine"/>s, <b>none</b> carrying
    /// <see cref="GstLineTax"/> — because the accept path never passes one.</summary>
    private static Voucher UntaggedTaxedSale(Company c, Guid customerId, Guid cgstId, Guid sgstId, DateOnly date)
    {
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var v = new Voucher(Guid.NewGuid(), salesType.Id, date, new[]
        {
            new EntryLine(customerId, Money.FromRupees(55_810.14m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales")!.Id, Money.FromRupees(47_296.73m), DrCr.Credit),
            new EntryLine(cgstId, Money.FromRupees(4_256.71m), DrCr.Credit),
            new EntryLine(sgstId, Money.FromRupees(4_256.70m), DrCr.Credit),
        }, partyId: customerId);
        Assert.All(v.Lines, l => Assert.Null(l.Gst));   // the As-Voucher path stamps no GST metadata
        return v;
    }

    /// <summary>A composition dealer's ORDINARY outward supply — the one the app actually posts, with no tax leg at
    /// all — is untouched by the guard (ER-13). So is a Regular dealer's taxed sale.</summary>
    [Fact]
    public void The_posting_guard_does_not_touch_an_ordinary_composition_sale_or_a_regular_taxed_sale()
    {
        var k = NewPosKit(GstRegistrationType.Composition);
        var receipt = SellOverTheCounter(k, k.TaxableItemId, "10225.37");
        Assert.Equal(Money.FromRupees(10_225.37m), receipt.GrandTotal);   // it posted, untaxed

        var reg = NewPosKit(GstRegistrationType.Regular);
        var taxed = SellOverTheCounter(reg, reg.TaxableItemId, "10225.37");
        Assert.True(taxed.TotalTax.Amount > 0m);                          // it posted, taxed
    }

    /// <summary>
    /// <b>🔴 THE EXISTING-DATA CLAUSE.</b> A guard that refuses to POST is not a guard that refuses to LOAD. A book
    /// written before this guard (or imported from one) can already contain the shape, and rejecting it on rehydration
    /// would make the whole company UNOPENABLE — strictly worse than the print-path interim it replaces. The guard is
    /// therefore scoped to the entry paths exactly as the cost-allocation invariant already is: the two rehydration
    /// paths (<c>SqliteCompanyStore.Load</c>, company import) pass <see cref="CostAllocationStrictness.Legacy"/>.
    ///
    /// <para>This drives a REAL save → load round trip through the SQLite store, then asserts the loaded voucher still
    /// reads and still prints — as the plain Dr/Cr voucher, which states every posted leg exactly.</para>
    /// </summary>
    [Fact]
    public void An_already_posted_anomalous_voucher_still_loads_reads_and_prints()
    {
        var (c, v, customerId) = CompositionContradiction("Legacy Anomaly Co");
        _storage.Save(c);

        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "Legacy Anomaly Co"));
        var loadedVoucher = reloaded.Vouchers.Single(x => x.Id == v.Id);

        // It READS: every posted leg survives the round trip, tax metadata included.
        Assert.Equal(Money.FromRupees(55_810.14m),
            loadedVoucher.Lines.Single(l => l.LedgerId == customerId && l.Side == DrCr.Debit).Amount);
        Assert.True(GstReportSupport.HasForwardTaxLines(loadedVoucher));
        Assert.True(GstReportSupport.IsCompositionBillOfSupply(reloaded, loadedVoucher)); // he IS a §10 dealer now

        // It DISPLAYS: no statutory badge is claimed for a document neither kind describes.
        var detail = new VoucherDetailViewModel(reloaded, loadedVoucher);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);

        // It PRINTS: the plain Dr/Cr voucher, stating the debt the GL recorded.
        var preview = detail.BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("55,810.14", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
    }

    // ================================================================ 3b — the e-Way Bill docType, NOW ROUTED

    /// <summary>
    /// <b>W0-8 — THE PIN IS RELEASED. This test asserted <c>"INV"</c>; it now asserts <c>"BIL"</c>, and that reversal
    /// is a CORRECTION, not a regression.</b>
    ///
    /// <para>What it used to pin: <c>EWayBillService</c> decided the NIC Part-A <c>docType</c> from
    /// <see cref="VoucherBaseType"/> alone, so every Sales voucher emitted <c>"INV"</c> — including the one this same
    /// app titles <b>BILL OF SUPPLY</b>, captions "Bill of Supply No: " and closes with "this bill of supply shows the
    /// actual price". <c>EWayBillJson</c> wrote <c>"docType":"INV"</c> into the portal request: one voucher, two
    /// mutually exclusive claims about what document it is. The corrective value was then UNVERIFIED — NIC's list
    /// refused automated retrieval (HTTP 403) and only non-official summaries were reachable — so R7 required pinning
    /// the contradiction rather than guessing at a value bound for a government filing.</para>
    ///
    /// <para><b>The source has since been read, live, in a real browser</b> (the 403 is a bot-block, not a missing
    /// document): <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>, published by the "Eway Bill
    /// Team, National Informatics Centre, Karnataka, Govt. of India", enumerates the complete Document Type domain as
    /// <b>INV</b> Tax Invoice, <b>BIL</b> Bill of Supply, <b>BOE</b> Bill of Entry, <b>CHL</b> Delivery Challan,
    /// <b>OTH</b> Others. <c>BIL</c> is unambiguously "Bill of Supply". The companion mapping page
    /// (<c>https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html</c>) explicitly permits
    /// <c>Outward | Supply | Bill of Supply</c>. And such a movement genuinely carries an e-Way Bill at all: CGST
    /// Rule 138(1) covers movement "in relation to a supply" — not "taxable supply" — and Explanation 2 reckons the
    /// consignment value from "an invoice, <b>a bill of supply</b> or a delivery challan".</para>
    ///
    /// <para><c>EWayBillService.PartACodesFor</c> now takes this limb from the SHARED
    /// <c>GstReportSupport.IsBillOfSupply</c>, so the printed title and the filed <c>docType</c> are one decision. The
    /// broader Part-A code correction (supplyType/subSupplyType as codes, CRN/DBN removed) lives in
    /// <c>Apex.Ledger.Tests.EWayPartACodeTests</c>.</para>
    /// </summary>
    [Fact]
    public void A_bill_of_supply_movement_emits_the_eWay_docType_BIL()
    {
        var k = NewPosKit(GstRegistrationType.Composition);
        var c = k.Company;
        c.Gst!.EWayBillEnabled = true;
        c.Gst!.EWayApplicableFrom = FyStart;

        // A plain (non-POS) Sales item invoice worth ₹2,00,000 — over the ₹50,000 Rule-138 threshold.
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive && !t.UseForPos);
        var customer = c.FindLedgerByName("Gujarat Buyer")!;
        var v = new Voucher(Guid.NewGuid(), salesType.Id, D1.AddDays(3), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(2_00_000m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales (POS)")!.Id, Money.FromRupees(2_00_000m), DrCr.Credit),
        }, partyId: customer.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.TaxableItemId, c.MainLocation!.Id, 1m, Money.FromRupees(2_00_000m)),
        });
        new LedgerService(c).Post(v);

        // The app's own print router says this is a bill of supply …
        Assert.True(VoucherPrintProjector.IsBillOfSupply(c, v));
        Assert.Equal(GstReportSupport.BillOfSupplyTitle, VoucherPrintProjector.ProjectInvoice(c, v).DocumentTitle);

        // … and the e-Way Part-A now agrees: BIL = Bill of Supply (NIC master-codes list, cited above).
        var eway = new EWayBillService(c);
        Assert.Equal(EWayCoverage.Required, eway.CoverageOf(v));
        var record = eway.PrepareRecord(v, D1.AddDays(3));
        Assert.Equal("BIL", record.DocType);
        Assert.NotEqual("INV", record.DocType); // the exact value this test used to assert
    }

    /// <summary>
    /// <b>🔴 PINNED GAP (W0-8, review findings #3 / #8 / #14) — the OTHER half of §31(3)(c) is still contradictory, and
    /// the slice that closed the composition half left this half with strictly LESS coverage than before.</b>
    ///
    /// <para><b>W0-9 — THE PIN IS RELEASED. This test asserted <c>"INV"</c>; it now asserts <c>"BIL"</c>, and the
    /// reversal is the acceptance criterion of the slice, not a regression.</b></para>
    ///
    /// <para>What it used to pin: the e-Way engine routed <c>docType</c> through <c>GstReportSupport.IsBillOfSupply</c>,
    /// which was the §10 <b>composition</b> limb only (it gated on
    /// <c>Gst is { Enabled: true, RegistrationType: Composition }</c>), while the printed title came from a predicate of
    /// the SAME NAME in <c>VoucherPrintProjector</c> that added the §31(3)(c) <b>wholly-exempt</b> limb. So a REGULAR
    /// dealer moving wholly-exempt goods — the commoner shape by far — had his paper titled BILL OF SUPPLY with the
    /// Rule-49 captions while the EWB-01 declared <c>docType "INV"</c>, a Tax Invoice. One consignment, two mutually
    /// exclusive statutory claims, with the wrong one on the government filing.</para>
    ///
    /// <para><b>What changed.</b> The exempt limb was lifted out of the Desktop projector into
    /// <c>GstReportSupport.IsBillOfSupply</c>, which is now the WHOLE of §31(3)(c) and is the one rule the printer, the
    /// e-Way Part-A and the POS receipt all read. Writing a second copy of the exempt rule inside the e-Way engine was
    /// never an option — that is the exact pathology the shared-predicate note exists to prevent. The document kind and
    /// the filed code are now ONE decision, asserted as such below.</para>
    ///
    /// <para><b>🔴 W0-9 TAIL review (finding #5) — THE CONSIGNMENT IS NOW A GOOD THE STATUTE ACTUALLY COVERS.</b> This
    /// drove its <c>EWayCoverage.Required</c> + <c>PrepareRecord</c> half on <c>k.ExemptItemId</c> — <b>Fresh Milk, HSN
    /// 040110</b> — which CGST <b>Rule 138(14)(e)</b> RELIEVES of the e-way bill outright (Schedule to Notification
    /// 2/2017-Central Tax (Rate), S. No. 25, tariff 0401). Asserting <c>Required</c> for it re-stated our engine's
    /// deliberate over-generation as though it were the law, and the over-generation is exactly what this slice's own
    /// <c>OneBillOfSupplyRuleTests.PINNED_GAP_the_rule_138_14_goods_relief_lists_are_not_modelled</c> records as a GAP
    /// ("when the goods lists land, this test must FAIL and be re-cut"). The two Ledger-layer tests were re-based off
    /// fresh milk for that reason; this one was missed, so when the goods lists land it would have failed at the same
    /// moment in TWO places — <c>CoverageOf</c> and <c>PrepareRecord</c>'s "Only a covered goods movement …" throw —
    /// while its doc framed the whole test as the bill-of-supply document-kind pin, making the failure read as a
    /// regression in <c>IsBillOfSupply</c> / the BIL routing. It now moves <b>de-oiled cake</b> (HSN 230630), which
    /// clause (e) expressly carves OUT of its own relief ("the goods, <b>other than de-oiled cake</b>, being
    /// transported, …"), so every assertion below — the exempt limb, the printed title AND the coverage — is a
    /// statement about the statute. The good is pinned by HSN off the voucher, so re-basing it back onto a relieved
    /// good reds here first, with a name that says why.</para>
    ///
    /// <para>Sources (R7): CGST Rule 138(14)(e) and the Rule 138(14) opening, read verbatim from CBIC's own
    /// consolidated rules PDF <c>https://cbic-gst.gov.in/pdf/01062021-CGST-Rules-2017-Part-A-Rules.pdf</c>
    /// (fetched, then extracted with <c>pdftotext -layout</c>); Schedule to Notification 2/2017 S. No. 25 = 0401 fresh
    /// milk and S. No. 102 = 2302/2304/2305/2306/2308/2309 "…concentrates &amp; additives, wheat bran &amp; de-oiled
    /// cake", read the same way from CBIC's copy of the notification
    /// <c>https://cbic-gst.gov.in/hindi/pdf/integrated-tax-rate/Notification%20for%20IGST%20exemption-2.pdf</c> (the
    /// Central Tax (Rate) twin on that host is a malformed PDF; the Schedule of goods is the same list).</para>
    /// </summary>
    [Fact]
    public void A_regular_dealers_wholly_exempt_movement_prints_BILL_OF_SUPPLY_and_files_BIL()
    {
        var k = NewPosKit(GstRegistrationType.Regular);
        var c = k.Company;
        c.Gst!.EWayBillEnabled = true;
        c.Gst!.EWayApplicableFrom = FyStart;

        // A Regular dealer's inter-state sale of a wholly EXEMPT stock item, ₹2,04,317.63 — odd to the paisa and over
        // the ₹50,000 Rule-138 threshold. No forward tax posts, so the consignment values off the stock value.
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive && !t.UseForPos);
        var customer = c.FindLedgerByName("Gujarat Buyer")!;
        var v = new Voucher(Guid.NewGuid(), salesType.Id, D1.AddDays(3), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(2_04_317.63m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales (POS)")!.Id, Money.FromRupees(2_04_317.63m), DrCr.Credit),
        }, partyId: customer.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.DeOiledCakeItemId, c.MainLocation!.Id, 1m, Money.FromRupees(2_04_317.63m)),
        });
        new LedgerService(c).Post(v);

        // The paper says BILL OF SUPPLY, via the §31(3)(c) exempt limb …
        Assert.True(VoucherPrintProjector.IsBillOfSupply(c, v));
        Assert.Equal(GstReportSupport.BillOfSupplyTitle, VoucherPrintProjector.ProjectInvoice(c, v).DocumentTitle);
        // … and the Ledger-layer predicate the e-Way engine consults now says the SAME thing, because the exempt limb
        // lives there too. It is still NOT the §10 limb — he is a Regular dealer, so no Rule 5(1)(f) declaration.
        Assert.True(GstReportSupport.IsBillOfSupply(c, v));
        Assert.False(GstReportSupport.IsCompositionBillOfSupply(c, v));
        Assert.Equal(string.Empty, VoucherPrintProjector.ProjectInvoice(c, v).TopDeclaration);

        // 🔴 W0-9 tail finding #5 — the coverage half below is a statement about the STATUTE only while the goods on
        // the truck are the ONE exempt good Rule 138(14)(e) refuses to relieve. Pinned off the VOUCHER, so re-basing it
        // onto a relieved good (fresh milk 0401, Sch. 2/2017 S. No. 25) reds HERE, before the coverage assertion, with
        // a message that names the good instead of looking like a BIL-routing regression.
        var movedItem = c.FindStockItem(v.InventoryLines.Single().StockItemId)!;
        Assert.Equal("230630", movedItem.Gst!.HsnSac);
        Assert.Equal(GstTaxability.Exempt, movedItem.Gst!.Taxability);

        var eway = new EWayBillService(c);
        Assert.Equal(EWayCoverage.Required, eway.CoverageOf(v));
        var record = eway.PrepareRecord(v, D1.AddDays(3));
        Assert.Equal("BIL", record.DocType);
        Assert.NotEqual("INV", record.DocType);   // the value this movement used to file — THE PIN, released.
    }

    // ================================================================ 4 — the mixed RCM / exempt shape

    /// <summary>
    /// <b>The "ANY line" semantics of <c>GstReportSupport.IsOutwardReverseChargeSupply</c>, pinned.</b> It returns true
    /// when ANY posted line's ledger carries <c>ReverseChargeApplicable</c>, so a sale mixing a reverse-charge leg with
    /// a wholly exempt leg takes TAX INVOICE for the WHOLE document. That is the correct direction:
    /// <list type="bullet">
    /// <item>CGST §2(98) defines reverse charge as "the liability to pay tax by the recipient of supply of goods or
    /// services or both <b>instead of the supplier</b>" — the tax is due, it merely moves, so the RCM leg is a
    /// <b>taxable</b> supply;</item>
    /// <item>§31(3)(c) reserves the bill of supply for a supply of <b>exempted</b> goods or services or a §10 dealer,
    /// and a supply containing a taxable leg is neither — so it is a Rule-46 tax invoice, which Rule 46(p)
    /// additionally requires to state "whether the tax is payable on reverse charge basis";</item>
    /// <item>Rule 46A's combined "invoice-cum-bill of supply" is <b>permissive</b> ("may be issued") and confined to an
    /// <b>unregistered</b> recipient, so it cannot make the bill of supply the required document here.</item>
    /// </list>
    /// Demoting the document to a bill of supply would also contradict the app's own return, which files this voucher
    /// in GSTR-1 Table 4B as a taxable reverse-charge outward supply (Gstr1.cs:249 keeps it OUT of the exempt bucket).
    /// </summary>
    [Fact]
    public void A_partly_reverse_charge_partly_exempt_sale_is_a_tax_invoice_for_the_whole_document()
    {
        var c = CompanyFactory.CreateSeeded("Mixed Rcm Exempt Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var rcmSales = AddLedger(c, "Sales (Reverse Charge)", "Sales Accounts", openingIsDebit: false);
        rcmSales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "996511", Taxability = GstTaxability.NilRated, ReverseChargeApplicable = true,
        };
        var exemptSales = AddLedger(c, "Sales (Exempt)", "Sales Accounts", openingIsDebit: false);
        exemptSales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "040110", Taxability = GstTaxability.Exempt,
        };
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var freight = masters.CreateStockItem("Freight Item", grp.Id, nos.Id);        // no GST block ⇒ resolves to the ledger
        var milk = masters.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        milk.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };
        var main = c.MainLocation!.Id;
        masters.AddOpeningBalance(freight.Id, main, 500m, Money.FromRupees(103.61m));
        masters.AddOpeningBalance(milk.Id, main, 500m, Money.FromRupees(29.43m));

        var rcmValue = Money.FromRupees(47_296.73m);   // 60.125 @ 786.64
        var exemptValue = Money.FromRupees(269.79m);   // 4.25 @ 63.48
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(9), new[]
        {
            new EntryLine(customer.Id, new Money(rcmValue.Amount + exemptValue.Amount), DrCr.Debit),
            new EntryLine(rcmSales.Id, rcmValue, DrCr.Credit),
            new EntryLine(exemptSales.Id, exemptValue, DrCr.Credit),
        }, partyId: customer.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(freight.Id, main, 60.125m, Money.FromRupees(786.64m)),
            new VoucherInventoryLine(milk.Id, main, 4.25m, Money.FromRupees(63.48m)),
        });
        new LedgerService(c).Post(v);

        Assert.All(v.Lines, l => Assert.Null(l.Gst));                     // no forward tax, as an RCM supply must be
        Assert.True(GstReportSupport.IsOutwardReverseChargeSupply(c, v)); // …on ANY line — this is the pinned semantics

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal(GstReportSupport.TaxInvoiceTitle, data.DocumentTitle);
        Assert.Equal(string.Empty, data.TopDeclaration);
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(c, v).DocumentLabel);

        // The document still foots to the debt the GL recorded — no tax is invented for either leg.
        Assert.Empty(data.TaxRows);
        Assert.Equal(new Money(rcmValue.Amount + exemptValue.Amount), data.TotalTaxable);
        Assert.Equal(v.Lines.Single(l => l.LedgerId == customer.Id && l.Side == DrCr.Debit).Amount, data.GrandTotal);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
