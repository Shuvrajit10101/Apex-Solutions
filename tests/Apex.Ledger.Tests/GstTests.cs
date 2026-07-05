using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Core GST engine tests (phase4-gst-requirements RQ-1..RQ-19; DP-1..DP-11). All pure, deterministic,
/// paisa-exact. Proves: enable + auto-create tax ledgers (idempotent) + seed slabs 0/5/18/40; GSTIN
/// validation; 3-level rate resolution; intra CGST/SGST split and inter IGST; additive tax preserves the
/// item-invoice pairing invariant; Input GST on a purchase; exempt/nil/non-GST zero tax; invoice round-off;
/// as-voucher GST; and that a non-GST company is unaffected (Robert/Bright covered by their own suites).
/// </summary>
public class GstTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);

    // Valid GSTINs (correct Luhn-mod-36 check digit) per state.
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private const string GstinDelhi = "07AAACA1111A1ZU";
    private const string GstinKarnataka = "29AAGCB7383J1Z4";

    private static Company NewGstCompany(string homeState = "27", string? gstin = GstinMaharashtra)
    {
        var c = CompanyFactory.CreateSeeded("GST Trading Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = homeState,
            Gstin = gstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ---------------------------------------------------------------- RQ-1/RQ-5/RQ-25: enable

    [Fact]
    public void Enabling_gst_auto_creates_the_six_tax_ledgers_and_seeds_slabs()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);

        Assert.True(c.GstEnabled);
        // 6 tax ledgers, each tagged with head + direction, all under Duties & Taxes.
        foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
            foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
            {
                var l = gst.FindTaxLedger(head, direction);
                Assert.NotNull(l);
                Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(l!, c));
            }
        Assert.Equal("Output CGST", gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Name);
        Assert.Equal("Input IGST", gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!.Name);

        // Slabs seeded 0/5/18/40 (in basis points).
        var bps = c.Gst!.RateSlabs.Select(s => s.RateBasisPoints).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 0, 500, 1800, 4000 }, bps);

        // Round-Off ledger created.
        Assert.NotNull(c.FindLedgerByName(GstService.RoundOffLedgerName));
    }

    [Fact]
    public void Enabling_gst_twice_is_idempotent_no_duplicate_ledgers_or_slabs()
    {
        var c = NewGstCompany();
        var before = c.Ledgers.Count;
        var slabsBefore = c.Gst!.RateSlabs.Count;

        // Re-enable with the same (already-populated) config.
        new GstService(c).EnableGst(c.Gst!);

        Assert.Equal(before, c.Ledgers.Count);
        Assert.Equal(slabsBefore, c.Gst!.RateSlabs.Count);
        // Exactly one Output CGST ledger.
        Assert.Single(c.Ledgers, l => l.Name == "Output CGST");
    }

    // ---------------------------------------------------------------- RQ-3: GSTIN validation

    [Theory]
    [InlineData(GstinMaharashtra)]
    [InlineData(GstinGujarat)]
    [InlineData(GstinDelhi)]
    [InlineData(GstinKarnataka)]
    public void Valid_gstins_pass(string gstin) => Assert.True(Gstin.IsValid(gstin));

    [Theory]
    [InlineData(null)]                       // null
    [InlineData("")]                          // empty
    [InlineData("27AAPFU0939F1Z")]            // 14 chars (too short)
    [InlineData("27AAPFU0939F1ZVX")]          // 16 chars (too long)
    [InlineData("99AAPFU0939F1ZV")]           // bad state code (99 not in list)
    [InlineData("27AAPFU0939F1ZW")]           // wrong check digit
    [InlineData("2XAAPFU0939F1ZV")]           // non-numeric state code
    [InlineData("27AAPFU0939F1YV")]           // 14th char not 'Z'
    public void Invalid_gstins_are_rejected(string? gstin) => Assert.False(Gstin.IsValid(gstin));

    [Fact]
    public void Bad_gstin_on_config_throws_fail_fast()
    {
        var c = CompanyFactory.CreateSeeded("X", FyStart);
        Assert.Throws<ArgumentException>(() => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZW", // wrong checksum
            RegistrationType = GstRegistrationType.Regular,
        }));
    }

    [Fact]
    public void Bad_home_state_code_on_config_throws()
    {
        var c = CompanyFactory.CreateSeeded("X", FyStart);
        Assert.Throws<ArgumentException>(() => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "99", // not a valid state code
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
        }));
    }

    // ---------------------------------------------------------------- RQ-10: rate resolution

    [Fact]
    public void Rate_resolves_item_over_ledger_over_company_most_granular_wins()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "1234", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var salesLedger = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        salesLedger.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };

        // Item wins (1800), not the ledger (500).
        var r = gst.ResolveRate(item, salesLedger);
        Assert.True(r.IsTaxable);
        Assert.Equal(1800, r.RateBasisPoints);

        // No item ⇒ ledger wins (500).
        var r2 = gst.ResolveRate(null, salesLedger);
        Assert.Equal(500, r2.RateBasisPoints);

        // Neither ⇒ unresolved (fail-fast sentinel).
        Assert.True(GstService.IsUnresolved(gst.ResolveRate(null, AddLedger(c, "PlainSales", "Sales Accounts", false))));
    }

    [Fact]
    public void Exempt_item_short_circuits_to_non_taxable()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Exempt", inv.CreateStockGroup("G").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Exempt };
        var r = gst.ResolveRate(item, null);
        Assert.False(r.IsTaxable);
        Assert.Equal(GstTaxability.Exempt, r.Taxability);
        Assert.False(GstService.IsUnresolved(r));
    }

    // ---------------------------------------------------------------- RQ-12: intra split

    [Fact]
    public void Intra_state_18pct_splits_into_cgst_9_and_sgst_9_paisa_exact()
    {
        // V = 1000, 18% ⇒ CGST 90 + SGST 90, IGST 0.
        var lt = GstService.ComputeLineTax(Money.FromRupees(1000m), 1800, interState: false);
        Assert.Equal(Money.FromRupees(90m), lt.Cgst);
        Assert.Equal(Money.FromRupees(90m), lt.Sgst);
        Assert.Equal(Money.Zero, lt.Igst);
        Assert.Equal(lt.Cgst, lt.Sgst); // CGST == SGST always
        Assert.Equal(Money.FromRupees(180m), lt.Total);
    }

    [Fact]
    public void Inter_state_18pct_is_full_igst_and_equals_cgst_plus_sgst()
    {
        var intra = GstService.ComputeLineTax(Money.FromRupees(1000m), 1800, interState: false);
        var inter = GstService.ComputeLineTax(Money.FromRupees(1000m), 1800, interState: true);
        Assert.Equal(Money.FromRupees(180m), inter.Igst);
        Assert.Equal(Money.Zero, inter.Cgst);
        Assert.Equal(Money.Zero, inter.Sgst);
        // IGST == CGST + SGST for the same value/rate (law L-4).
        Assert.Equal(inter.Igst, intra.Total);
    }

    [Fact]
    public void Odd_paisa_line_rounds_per_line_away_from_zero_and_keeps_cgst_equal_sgst()
    {
        // V = 999.99 @ 18% ⇒ head 9% each. 999.99 * 900/10000 = 89.9991 ⇒ 90.00 each (away-from-zero).
        var lt = GstService.ComputeLineTax(Money.FromRupees(999.99m), 1800, interState: false);
        Assert.True(lt.Cgst.IsPaisaExact);
        Assert.Equal(lt.Cgst, lt.Sgst);
        Assert.Equal(Money.FromRupees(90.00m), lt.Cgst);
    }

    // ---------------------------------------------------------------- RQ-16: item-invoice additive, pairing holds

    [Fact]
    public void Intra_state_gst_sales_item_invoice_is_additive_and_pairing_invariant_holds()
    {
        var c = NewGstCompany(); // home 27 (Maharashtra)
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "1234", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var debtor = AddLedger(c, "Local Debtor", "Sundry Debtors", openingIsDebit: true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // Buy first so we can sell (stock on hand).
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", openingIsDebit: false);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(500m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(50m)) }));

        // Sale: 10 @ ₹100 = ₹1000 taxable. Intra ⇒ CGST 90 + SGST 90. Party total = 1180.
        var interState = gst.IsInterState(debtor.PartyGst.StateCode);
        Assert.False(interState);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, interState, GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(180m), tax.TotalTax);

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),   // party = taxable + tax
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),   // stock/sales leg = taxable value
        };
        lines.AddRange(tax.TaxLines); // additive Output CGST/SGST credits

        var v = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, lines,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(100m)) }));

        // Balanced (RQ-17).
        Assert.True(VoucherValidator.IsBalanced(v));
        // Pairing invariant held (ER-8): sales leg == Σ item value == 1000; tax excluded.
        Assert.Equal(Money.FromRupees(1000m), v.InventoryLinesValue);
        // Tax posted to Output CGST + SGST.
        Assert.Equal(-90m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, D2));
        Assert.Equal(-90m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!, D2));
        // Party carries taxable + tax.
        Assert.Equal(1180m, LedgerBalances.SignedClosing(c, debtor, D2));
    }

    [Fact]
    public void Inter_state_gst_sales_posts_output_igst()
    {
        var c = NewGstCompany("27"); // home Maharashtra
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "1234", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Gujarat Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(500m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(50m)) }));

        var interState = gst.IsInterState(debtor.PartyGst.StateCode);
        Assert.True(interState); // 27 vs 24
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, interState, GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(180m), tax.TotalIgst);
        Assert.Equal(Money.Zero, tax.TotalCgst);

        var lines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit), new(sales.Id, Money.FromRupees(1000m), DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D2, lines,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(100m)) }));

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(-180m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!, D2));
    }

    // ---------------------------------------------------------------- RQ-18: purchase Input GST

    [Fact]
    public void Gst_purchase_item_invoice_posts_input_tax_ledgers()
    {
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "1234", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Local Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var interState = gst.IsInterState(creditor.PartyGst.StateCode); // false (intra)
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, interState, GstTaxDirection.Input);

        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(1000m), DrCr.Debit),  // stock leg = taxable
            new(creditor.Id, Money.FromRupees(1180m), DrCr.Credit),  // supplier = taxable + tax
        };
        lines.AddRange(tax.TaxLines); // Input CGST/SGST debits
        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(100m)) }));

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.FromRupees(1000m), v.InventoryLinesValue); // pairing holds
        // Input CGST/SGST are debits (ITC assets).
        Assert.Equal(90m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!, D1));
        Assert.Equal(90m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!, D1));
    }

    // ---------------------------------------------------------------- RQ-15: exempt/nil/non-GST zero tax

    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    [InlineData(GstTaxability.NonGst)]
    public void Non_taxable_lines_attract_no_tax(GstTaxability taxability)
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("X", inv.CreateStockGroup("G").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { Taxability = taxability };
        var r = gst.ResolveRate(item, null);
        Assert.False(r.IsTaxable);
        // No taxable lines ⇒ no tax lines produced.
        var tax = gst.ComputeInvoiceTax(Array.Empty<GstService.TaxableLine>(), interState: false, GstTaxDirection.Output);
        Assert.Empty(tax.TaxLines);
        Assert.Equal(Money.Zero, tax.TotalTax);
    }

    // ---------------------------------------------------------------- RQ-19: invoice round-off

    [Fact]
    public void Invoice_round_off_rounds_grand_total_to_nearest_rupee_and_balances()
    {
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        // As-voucher sale of a service: taxable ₹1000.50 @ 18% ⇒ tax 180.09 ⇒ grand 1180.59 ⇒ rounds to 1181.
        var sales = AddLedger(c, "Service Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services };
        var debtor = AddLedger(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000.50m), 1800) },
            interState: false, GstTaxDirection.Output, applyInvoiceRoundOff: true);

        // CGST 90.045 -> 90.05 (away from zero), SGST 90.05; tax 180.10; grand = 1000.50 + 180.10 = 1180.60 -> 1181.
        var grand = 1000.50m + tax.TotalTax.Amount;
        var rounded = Math.Round(grand, 0, MidpointRounding.AwayFromZero);
        Assert.Equal(1181m, rounded);
        Assert.NotNull(tax.RoundOffLine);

        // Assemble the balanced voucher: party at the rounded total.
        var lines = new List<EntryLine>
        {
            new(debtor.Id, new Money(rounded), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000.50m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(tax.RoundOffLine!);
        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D2, lines));
        Assert.True(VoucherValidator.IsBalanced(v));
    }

    // ---------------------------------------------------------------- DP-10: as-voucher GST

    [Fact]
    public void As_voucher_gst_sale_computes_tax_from_the_sales_ledger()
    {
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        var sales = AddLedger(c, "Consulting", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services };
        var debtor = AddLedger(c, "Client", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var rate = gst.ResolveRate(item: null, salesPurchaseLedger: sales);
        Assert.True(rate.IsTaxable);
        Assert.Equal(1800, rate.RateBasisPoints);

        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2000m), rate.RateBasisPoints) },
            gst.IsInterState(debtor.PartyGst.StateCode), GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(360m), tax.TotalTax); // 18% of 2000

        var lines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(2360m), DrCr.Debit), new(sales.Id, Money.FromRupees(2000m), DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        // As-voucher: NO inventory lines.
        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D2, lines));
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.False(v.HasInventoryLines);
        Assert.Equal(-180m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, D2));
    }

    // ---------------------------------------------------------------- mixed-rate per line (RQ-13)

    [Fact]
    public void Mixed_rate_invoice_taxes_each_line_at_its_own_rate()
    {
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        // Line A: 1000 @ 18% ⇒ 180; Line B: 1000 @ 5% ⇒ 50. Intra.
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(1000m), 500),
        }, interState: false, GstTaxDirection.Output);

        // CGST = 90 + 25 = 115; SGST = 115; total 230.
        Assert.Equal(Money.FromRupees(115m), tax.TotalCgst);
        Assert.Equal(Money.FromRupees(115m), tax.TotalSgst);
        Assert.Equal(Money.FromRupees(230m), tax.TotalTax);
        Assert.Equal(2, tax.LineBreakdown.Count);
    }

    // ---------------------------------------------------------------- ER-10: non-GST company unaffected

    [Fact]
    public void A_company_without_gst_enabled_has_no_gst_state()
    {
        var c = CompanyFactory.CreateSeeded("Plain Co", FyStart);
        Assert.False(c.GstEnabled);
        Assert.Null(c.Gst);
        // No tax ledgers exist.
        Assert.DoesNotContain(c.Ledgers, l => l.GstClassification is not null);
    }

    // ---------------------------------------------------------------- UTGST parity (RQ-6)

    [Fact]
    public void Union_territory_home_state_still_uses_the_single_state_head()
    {
        // Home = Delhi (UT). An intra-UT supply still posts to the State head (UTGST folds into SGST head).
        var c = NewGstCompany("07", GstinDelhi);
        var gst = new GstService(c);
        Assert.True(c.Gst!.HomeState!.IsUnionTerritory);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(90m), tax.TotalCgst);
        Assert.Equal(Money.FromRupees(90m), tax.TotalSgst); // the one State/UT head
    }

    // ---------------------------------------------------------------- Fix 1: CGST+SGST == IGST parity (RQ-12/L-4)
    // The engine computes the line's total tax ONCE (== the IGST amount) then splits CGST/SGST from it, so the
    // intra total ALWAYS equals the inter total to the paisa. Independent per-head rounding drifted ±0.01 on odd tails.

    /// <summary>The mathematically correct total tax on V at a rate = round_paisa(V × rate) — this is the IGST.</summary>
    private static Money ExpectedTotal(decimal rupees, int integratedBp) =>
        new Money(rupees * integratedBp / 10000m).RoundToPaisa();

    [Theory]
    [InlineData(1.05, 500)]     // 0.0525 → total 0.05; old CGST+SGST = 0.03+0.03 = 0.06 (drift +0.01)
    [InlineData(0.15, 1800)]    // 0.027  → total 0.03; old CGST+SGST = 0.01+0.01 = 0.02 (drift −0.01)
    [InlineData(100.10, 500)]   // 5.005  → total 5.01; old CGST+SGST = 2.50+2.50 = 5.00 (drift −0.01)
    [InlineData(333.33, 1800)]  // 59.9994 → total 60.00 (already matched, kept as a guard)
    public void Intra_split_totals_exactly_equal_igst_and_round_paisa_of_v_times_rate(decimal rupees, int integratedBp)
    {
        var v = Money.FromRupees(rupees);
        var intra = GstService.ComputeLineTax(v, integratedBp, interState: false);
        var inter = GstService.ComputeLineTax(v, integratedBp, interState: true);
        var expectedTotal = ExpectedTotal(rupees, integratedBp);

        // The heart of the invariant: CGST + SGST == IGST == round_paisa(V × rate).
        Assert.Equal(expectedTotal, inter.Igst);
        Assert.Equal(expectedTotal, intra.Total);
        Assert.Equal(inter.Igst, intra.Total);
        Assert.Equal(expectedTotal.Amount, intra.Cgst.Amount + intra.Sgst.Amount);

        // CGST and SGST differ by at most 1 paisa (SGST carries the odd-total remainder).
        Assert.True(Math.Abs(intra.Cgst.Amount - intra.Sgst.Amount) <= 0.01m);
        Assert.True(intra.Cgst.IsPaisaExact && intra.Sgst.IsPaisaExact);
    }

    [Fact]
    public void Exhaustive_paise_sweep_cgst_plus_sgst_equals_round_paisa_of_v_times_rate_at_5_18_40()
    {
        // Sweep V from 0.01 to 5.00 (in paise) at 5/18/40% and assert the footing/parity invariant for EVERY value.
        foreach (var bp in new[] { 500, 1800, 4000 })
            for (var paise = 1; paise <= 500; paise++)
            {
                var rupees = paise / 100m;
                var v = Money.FromRupees(rupees);
                var intra = GstService.ComputeLineTax(v, bp, interState: false);
                var expectedTotal = ExpectedTotal(rupees, bp);

                Assert.Equal(expectedTotal.Amount, intra.Cgst.Amount + intra.Sgst.Amount);
                Assert.Equal(expectedTotal, intra.Total);
                Assert.Equal(expectedTotal, GstService.ComputeLineTax(v, bp, interState: true).Igst);
                Assert.True(Math.Abs(intra.Cgst.Amount - intra.Sgst.Amount) <= 0.01m,
                    $"CGST/SGST diverged >1 paisa at V={rupees} bp={bp}");
            }
    }

    [Fact]
    public void Invoice_aggregation_preserves_cgst_plus_sgst_equals_igst_per_line()
    {
        // The invoice path (ComputeInvoiceTax) must use the same total-then-split, so intra totals == inter totals.
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        var lines = new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1.05m), 500),
            new GstService.TaxableLine(Money.FromRupees(0.15m), 1800),
            new GstService.TaxableLine(Money.FromRupees(100.10m), 500),
        };
        var intra = gst.ComputeInvoiceTax(lines, interState: false, GstTaxDirection.Output);
        var inter = gst.ComputeInvoiceTax(lines, interState: true, GstTaxDirection.Output);

        // Σ (round_paisa(V × rate)) per line — the correct aggregate tax.
        var expected = ExpectedTotal(1.05m, 500).Amount + ExpectedTotal(0.15m, 1800).Amount + ExpectedTotal(100.10m, 500).Amount;
        Assert.Equal(expected, intra.TotalCgst.Amount + intra.TotalSgst.Amount);
        Assert.Equal(expected, inter.TotalIgst.Amount);
        Assert.Equal(inter.TotalIgst.Amount, intra.TotalCgst.Amount + intra.TotalSgst.Amount);
    }

    // ---------------------------------------------------------------- Fix 2: blank/unknown party state ⇒ intra (B2C)
    // Place of supply for an unregistered/unrecorded recipient = supplier's home State (DP-8) ⇒ CGST+SGST, NOT IGST.

    [Fact]
    public void Blank_party_state_defaults_to_home_state_intra_not_igst()
    {
        var c = NewGstCompany("27"); // home Maharashtra
        var gst = new GstService(c);
        Assert.False(gst.IsInterState(null));   // B2C walk-in, no recorded state ⇒ intra
        Assert.False(gst.IsInterState(""));      // blank
        Assert.False(gst.IsInterState("   "));   // whitespace
    }

    [Fact]
    public void Same_state_party_is_intra_and_different_recorded_state_is_inter()
    {
        var c = NewGstCompany("27"); // home Maharashtra
        var gst = new GstService(c);
        Assert.False(gst.IsInterState("27")); // same State ⇒ intra
        Assert.True(gst.IsInterState("24"));  // Gujarat ⇒ inter
    }

    [Fact]
    public void Union_territory_recorded_party_state_intra_when_it_is_the_home_ut()
    {
        var c = NewGstCompany("07", GstinDelhi); // home Delhi (UT)
        var gst = new GstService(c);
        Assert.False(gst.IsInterState("07")); // same UT ⇒ intra (SGST/UTGST head)
        Assert.False(gst.IsInterState(null)); // blank ⇒ home UT ⇒ intra
        Assert.True(gst.IsInterState("27"));  // different State ⇒ inter
    }

    [Fact]
    public void Blank_state_b2c_sale_computes_cgst_sgst_not_igst()
    {
        var c = NewGstCompany("27");
        var gst = new GstService(c);
        // A cash/B2C consumer with no party GST details at all ⇒ blank state ⇒ intra ⇒ CGST+SGST.
        var interState = gst.IsInterState(partyStateCode: null);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            interState, GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(90m), tax.TotalCgst);
        Assert.Equal(Money.FromRupees(90m), tax.TotalSgst);
        Assert.Equal(Money.Zero, tax.TotalIgst);
    }
}
