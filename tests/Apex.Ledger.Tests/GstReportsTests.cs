using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 4 slice 4b — GST report projections (phase4-gst-requirements RQ-20..RQ-24; ER-7). Proves the three
/// read-only projections over already-posted GST vouchers (TaxAnalysis, GSTR-1, GSTR-3B):
/// <list type="bullet">
///   <item>each report reads the posted tax from the tax <see cref="EntryLine"/>s' <see cref="GstLineTax"/>
///     (never recomputes) and reconciles to the tax-ledger postings, paisa-exact;</item>
///   <item>GSTR-1 sections outward supplies into B2B (party GSTIN) vs B2C, a rate-wise summary and an HSN
///     summary; cancelled/post-dated-after-`to` are excluded; exempt is shown;</item>
///   <item>GSTR-3B 3.1 output by head == Σ Output tax-ledger postings; 4 ITC by head == Σ Input postings;
///     net payable = output − ITC per head (display-only computation);</item>
///   <item>a non-GST company yields empty/GST-off reports with no crash.</item>
/// </list>
/// The synthetic company: home Maharashtra (27); a registered in-state customer (27), a registered
/// out-of-state customer (Gujarat 24), an unregistered B2C consumer; a registered in-state supplier. Posts:
/// an intra B2B sale (CGST+SGST), an inter B2B sale (IGST), a B2C intra sale, an exempt sale, and a purchase
/// (ITC). All figures worked by hand and reconciled to the paisa.
/// </summary>
public class GstReportsTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly D1 = new(2024, 4, 3);   // purchase (ITC)
    private static readonly DateOnly D2 = new(2024, 4, 5);   // intra B2B sale
    private static readonly DateOnly D3 = new(2024, 4, 7);   // inter B2B sale
    private static readonly DateOnly D4 = new(2024, 4, 9);   // B2C intra sale
    private static readonly DateOnly D5 = new(2024, 4, 11);  // exempt sale

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ---- A fully-posted GST company with all five scenarios. ----
    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstService Gst { get; init; }
        public required Domain.Ledger InCgst { get; init; }
        public required Domain.Ledger InSgst { get; init; }
        public required Domain.Ledger InIgst { get; init; }
        public required Domain.Ledger OutCgst { get; init; }
        public required Domain.Ledger OutSgst { get; init; }
        public required Domain.Ledger OutIgst { get; init; }
    }

    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("GST Reports Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        // Two taxable items at 18% and 5%, plus one exempt item.
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };

        // Opening stock for Gadget (40) and Book (5) so the B2C and exempt sales have stock on hand.
        inv.AddOpeningBalance(gadget.Id, main, 40m, Money.FromRupees(20m));
        inv.AddOpeningBalance(book.Id, main, 5m, Money.FromRupees(150m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var localDebtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        localDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var gujaratDebtor = Add(c, "Gujarat Debtor", "Sundry Debtors", true);
        gujaratDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // ---- D1: PURCHASE (ITC). 100 Widget @ ₹50 = ₹5000 taxable @ 18% intra ⇒ Input CGST 450 + SGST 450. ----
        var pTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) },
            interState: false, GstTaxDirection.Input);
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, D1, pLines, partyId: supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(50m)) }));

        // ---- D2: INTRA B2B SALE. 10 Widget @ ₹100 = ₹1000 @ 18% intra ⇒ CGST 90 + SGST 90. ----
        var s1Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var s1Lines = new List<EntryLine>
        {
            new(localDebtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        s1Lines.AddRange(s1Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, s1Lines, partyId: localDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 10m, Money.FromRupees(100m)) }));

        // ---- D3: INTER B2B SALE. 20 Widget @ ₹100 = ₹2000 @ 18% inter ⇒ IGST 360. ----
        var s2Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2000m), 1800) },
            interState: true, GstTaxDirection.Output);
        var s2Lines = new List<EntryLine>
        {
            new(gujaratDebtor.Id, Money.FromRupees(2360m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        };
        s2Lines.AddRange(s2Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D3, s2Lines, partyId: gujaratDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 20m, Money.FromRupees(100m)) }));

        // ---- D4: B2C INTRA SALE. 40 Gadget @ ₹25 = ₹1000 @ 5% intra ⇒ CGST 25 + SGST 25. ----
        var s3Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 500) },
            interState: false, GstTaxDirection.Output);
        var s3Lines = new List<EntryLine>
        {
            new(consumer.Id, Money.FromRupees(1050m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        s3Lines.AddRange(s3Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D4, s3Lines, partyId: consumer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(gadget.Id, main, 40m, Money.FromRupees(25m)) }));

        // ---- D5: EXEMPT SALE. 5 Book @ ₹200 = ₹1000 exempt ⇒ zero tax. ----
        var s4Lines = new List<EntryLine>
        {
            new(localDebtor.Id, Money.FromRupees(1000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D5, s4Lines, partyId: localDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 5m, Money.FromRupees(200m)) }));

        return new Fixture
        {
            Company = c,
            Gst = gst,
            InCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!,
            InSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!,
            InIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!,
            OutCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!,
            OutSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!,
            OutIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!,
        };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ================================================================ TaxAnalysis (RQ-20 period summary)

    [Fact]
    public void TaxAnalysis_outward_totals_by_head_reconcile_to_the_output_tax_ledgers()
    {
        var f = Build();
        var report = TaxAnalysis.Build(f.Company, From, To);

        // Output tax: CGST 90 (intra sale), SGST 90, IGST 360 (inter sale) — reconcile to the tax ledgers.
        Assert.Equal(Money.FromRupees(115m), report.Outward.TotalCgst);   // 90 (Widget 18%) + 25 (Gadget 5%)
        Assert.Equal(Money.FromRupees(115m), report.Outward.TotalSgst);
        Assert.Equal(Money.FromRupees(360m), report.Outward.TotalIgst);

        // Σ Output tax-ledger postings (credits are negative signed).
        Assert.Equal(-115m, LedgerBalances.SignedClosing(f.Company, f.OutCgst, To));
        Assert.Equal(-115m, LedgerBalances.SignedClosing(f.Company, f.OutSgst, To));
        Assert.Equal(-360m, LedgerBalances.SignedClosing(f.Company, f.OutIgst, To));
    }

    [Fact]
    public void TaxAnalysis_inward_totals_reconcile_to_the_input_tax_ledgers()
    {
        var f = Build();
        var report = TaxAnalysis.Build(f.Company, From, To);

        Assert.Equal(Money.FromRupees(450m), report.Inward.TotalCgst);
        Assert.Equal(Money.FromRupees(450m), report.Inward.TotalSgst);
        Assert.Equal(Money.Zero, report.Inward.TotalIgst);

        Assert.Equal(450m, LedgerBalances.SignedClosing(f.Company, f.InCgst, To));  // Input = debit (positive)
        Assert.Equal(450m, LedgerBalances.SignedClosing(f.Company, f.InSgst, To));
    }

    [Fact]
    public void TaxAnalysis_groups_rows_by_rate_and_head_with_taxable_value()
    {
        var f = Build();
        var report = TaxAnalysis.Build(f.Company, From, To);

        // Outward rate-wise: 18% has ₹1000 (intra) + ₹2000 (inter) taxable; 5% has ₹1000; exempt ₹1000.
        var r18 = report.Outward.RateRows.Single(r => r.RateBasisPoints == 1800 && r.Head == GstTaxHead.Integrated);
        Assert.Equal(Money.FromRupees(2000m), r18.TaxableValue);
        Assert.Equal(Money.FromRupees(360m), r18.Tax);

        var r18Central = report.Outward.RateRows.Single(r => r.RateBasisPoints == 900 && r.Head == GstTaxHead.Central);
        Assert.Equal(Money.FromRupees(1000m), r18Central.TaxableValue); // the intra 18% sale
        Assert.Equal(Money.FromRupees(90m), r18Central.Tax);
    }

    // ================================================================ GSTR-1 (RQ-21 outward supplies)

    [Fact]
    public void Gstr1_b2b_rows_carry_party_gstin_invoice_and_head_amounts()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);

        // Two B2B invoices: the intra Local Debtor (CGST+SGST) and the inter Gujarat Debtor (IGST).
        Assert.Equal(2, r.B2B.Count);

        var intra = r.B2B.Single(b => b.PartyGstin == GstinMaharashtra);
        Assert.Equal("27", intra.PlaceOfSupplyStateCode);
        Assert.Equal(Money.FromRupees(1000m), intra.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), intra.Cgst);
        Assert.Equal(Money.FromRupees(90m), intra.Sgst);
        Assert.Equal(Money.Zero, intra.Igst);

        var inter = r.B2B.Single(b => b.PartyGstin == GstinGujarat);
        Assert.Equal("24", inter.PlaceOfSupplyStateCode);
        Assert.Equal(Money.FromRupees(2000m), inter.TaxableValue);
        Assert.Equal(Money.FromRupees(360m), inter.Igst);
        Assert.Equal(Money.Zero, inter.Cgst);
    }

    [Fact]
    public void Gstr1_b2c_consolidates_unregistered_supplies_rate_wise()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);

        // Only the B2C consumer's ₹1000 @ 5% intra sale is B2C.
        var b2c = Assert.Single(r.B2C);
        Assert.Equal(500, b2c.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1000m), b2c.TaxableValue);
        Assert.Equal(Money.FromRupees(25m), b2c.Cgst);
        Assert.Equal(Money.FromRupees(25m), b2c.Sgst);
    }

    [Fact]
    public void Gstr1_rate_wise_summary_sums_all_outward_supplies()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);

        // 18%: ₹1000 (intra) + ₹2000 (inter) = ₹3000 taxable, tax 180 + 360 = 540.
        var rate18 = r.RateSummary.Single(x => x.RateBasisPoints == 1800);
        Assert.Equal(Money.FromRupees(3000m), rate18.TaxableValue);
        Assert.Equal(Money.FromRupees(540m), rate18.TotalTax);

        // 5%: ₹1000, tax 50.
        var rate5 = r.RateSummary.Single(x => x.RateBasisPoints == 500);
        Assert.Equal(Money.FromRupees(1000m), rate5.TaxableValue);
        Assert.Equal(Money.FromRupees(50m), rate5.TotalTax);
    }

    [Fact]
    public void Gstr1_hsn_summary_groups_by_hsn_with_qty_value_and_tax()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);

        // Widget HSN 847130: 10 (intra) + 20 (inter) = 30 Nos, taxable ₹3000, tax 540.
        var widget = r.HsnSummary.Single(h => h.HsnSac == "847130");
        Assert.Equal(30m, widget.Quantity);
        Assert.Equal("NOS", widget.Uqc);
        Assert.Equal(Money.FromRupees(3000m), widget.TaxableValue);
        Assert.Equal(Money.FromRupees(540m), widget.TotalTax);

        // Gadget HSN 852990: 40 Nos, taxable ₹1000, tax 50.
        var gadget = r.HsnSummary.Single(h => h.HsnSac == "852990");
        Assert.Equal(40m, gadget.Quantity);
        Assert.Equal(Money.FromRupees(50m), gadget.TotalTax);

        // Book HSN 490199 (exempt): 5 Nos, taxable ₹1000, tax 0.
        var book = r.HsnSummary.Single(h => h.HsnSac == "490199");
        Assert.Equal(Money.Zero, book.TotalTax);
        Assert.Equal(Money.FromRupees(1000m), book.TaxableValue);
    }

    [Fact]
    public void Gstr1_shows_exempt_outward_supplies()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);
        Assert.Equal(Money.FromRupees(1000m), r.ExemptNilNonGstValue);
    }

    [Fact]
    public void Gstr1_output_tax_reconciles_to_the_output_tax_ledgers()
    {
        var f = Build();
        var r = Gstr1.Build(f.Company, From, To);

        // Σ GSTR-1 output tax == Σ Output tax-ledger postings for the period.
        var cgst = -LedgerBalances.SignedClosing(f.Company, f.OutCgst, To);
        var sgst = -LedgerBalances.SignedClosing(f.Company, f.OutSgst, To);
        var igst = -LedgerBalances.SignedClosing(f.Company, f.OutIgst, To);
        Assert.Equal(new Money(cgst), r.TotalCgst);
        Assert.Equal(new Money(sgst), r.TotalSgst);
        Assert.Equal(new Money(igst), r.TotalIgst);
    }

    [Fact]
    public void Gstr1_excludes_cancelled_and_post_dated_after_to()
    {
        var f = Build();
        var c = f.Company;
        var gst = f.Gst;
        var ledgers = new LedgerService(c);
        var sales = c.FindLedgerByName("Sales")!;
        var debtor = c.FindLedgerByName("Local Debtor")!;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // A cancelled sale and a post-dated (after To) sale must not appear.
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(9999m), 1800) },
            interState: false, GstTaxDirection.Output);
        var cancLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(11798.82m), DrCr.Debit), new(sales.Id, Money.FromRupees(9999m), DrCr.Credit) };
        cancLines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, cancLines, partyId: debtor.Id, cancelled: true));

        var pdLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(11798.82m), DrCr.Debit), new(sales.Id, Money.FromRupees(9999m), DrCr.Credit) };
        pdLines.AddRange(gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(9999m), 1800) }, false, GstTaxDirection.Output).TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, new DateOnly(2024, 5, 15), pdLines, partyId: debtor.Id, postDated: true));

        var r = Gstr1.Build(c, From, To);
        // The 18% rate total is unchanged (still ₹3000 taxable) — cancelled/post-dated excluded.
        var rate18 = r.RateSummary.Single(x => x.RateBasisPoints == 1800);
        Assert.Equal(Money.FromRupees(3000m), rate18.TaxableValue);
    }

    // ================================================================ GSTR-3B (RQ-22 summary)

    [Fact]
    public void Gstr3b_31_outward_tax_by_head_reconciles_to_output_ledgers()
    {
        var f = Build();
        var r = Gstr3b.Build(f.Company, From, To);

        Assert.Equal(Money.FromRupees(115m), r.OutwardCgst);
        Assert.Equal(Money.FromRupees(115m), r.OutwardSgst);
        Assert.Equal(Money.FromRupees(360m), r.OutwardIgst);

        Assert.Equal(-115m, LedgerBalances.SignedClosing(f.Company, f.OutCgst, To));
        Assert.Equal(-360m, LedgerBalances.SignedClosing(f.Company, f.OutIgst, To));
    }

    [Fact]
    public void Gstr3b_shows_exempt_nil_non_gst_outward_value()
    {
        var f = Build();
        var r = Gstr3b.Build(f.Company, From, To);
        Assert.Equal(Money.FromRupees(1000m), r.ExemptNilNonGstOutward);
        Assert.Equal(Money.FromRupees(4000m), r.TaxableOutwardValue); // 1000 + 2000 + 1000
    }

    [Fact]
    public void Gstr3b_4_eligible_itc_by_head_reconciles_to_input_ledgers()
    {
        var f = Build();
        var r = Gstr3b.Build(f.Company, From, To);

        Assert.Equal(Money.FromRupees(450m), r.ItcCgst);
        Assert.Equal(Money.FromRupees(450m), r.ItcSgst);
        Assert.Equal(Money.Zero, r.ItcIgst);

        Assert.Equal(450m, LedgerBalances.SignedClosing(f.Company, f.InCgst, To));
        Assert.Equal(450m, LedgerBalances.SignedClosing(f.Company, f.InSgst, To));
    }

    [Fact]
    public void Gstr3b_net_payable_is_output_minus_itc_per_head()
    {
        var f = Build();
        var r = Gstr3b.Build(f.Company, From, To);

        // CGST: 115 output − 450 ITC = −335 (credit balance carried forward).
        Assert.Equal(-335m, r.NetCgst.Amount);
        Assert.Equal(-335m, r.NetSgst.Amount);
        // IGST: 360 output − 0 ITC = 360 payable.
        Assert.Equal(360m, r.NetIgst.Amount);

        // A net-negative head means a carried-forward credit, not a payable (display-only).
        Assert.True(r.NetCgst.Amount < 0m);
        Assert.False(r.NetIgst.Amount < 0m);
    }

    [Fact]
    public void Gstr3b_intra_head_amounts_foot_cgst_equals_sgst()
    {
        var f = Build();
        var r = Gstr3b.Build(f.Company, From, To);
        // The intra CGST and SGST always foot equal (RQ-12 parity).
        Assert.Equal(r.OutwardCgst, r.OutwardSgst);
        Assert.Equal(r.ItcCgst, r.ItcSgst);
    }

    // ================================================================ Non-GST company (empty, no crash)

    [Fact]
    public void Non_gst_company_yields_empty_reports_without_crashing()
    {
        var c = CompanyFactory.CreateSeeded("Plain Co", FyStart);
        Assert.False(c.GstEnabled);

        var ta = TaxAnalysis.Build(c, From, To);
        Assert.Equal(Money.Zero, ta.Outward.TotalTax);
        Assert.Equal(Money.Zero, ta.Inward.TotalTax);
        Assert.Empty(ta.Outward.RateRows);

        var g1 = Gstr1.Build(c, From, To);
        Assert.Empty(g1.B2B);
        Assert.Empty(g1.B2C);
        Assert.Empty(g1.HsnSummary);
        Assert.Equal(Money.Zero, g1.TotalCgst);

        var g3 = Gstr3b.Build(c, From, To);
        Assert.Equal(Money.Zero, g3.OutwardCgst);
        Assert.Equal(Money.Zero, g3.ItcCgst);
        Assert.Equal(Money.Zero, g3.NetIgst);
    }

    // ================================================================ Façade wrappers

    [Fact]
    public void Facade_wrappers_delegate_to_the_report_builders()
    {
        var f = Build();
        Assert.Equal(TaxAnalysis.Build(f.Company, From, To).Outward.TotalIgst,
            Report.BuildTaxAnalysis(f.Company, From, To).Outward.TotalIgst);
        Assert.Equal(Gstr1.Build(f.Company, From, To).B2B.Count,
            Report.BuildGstr1(f.Company, From, To).B2B.Count);
        Assert.Equal(Gstr3b.Build(f.Company, From, To).NetIgst,
            Report.BuildGstr3b(f.Company, From, To).NetIgst);
    }
}
