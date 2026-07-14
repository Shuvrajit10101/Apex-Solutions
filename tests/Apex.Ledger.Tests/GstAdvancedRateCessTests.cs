using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 1 — GST 2.0 dated rate framework + Compensation-Cess seam (RQ-1/RQ-2/RQ-9; ER-1/ER-2/ER-13). Proves:
/// voucher-date rate resolution (pre/post 22-Sep-2025 pair); the three cess valuation modes (ad-valorem / specific /
/// RSP-factor); the 40%-de-merit ≠ cess guard; the cess ring-fence (own ledger, out of TotalTax); a cess-bearing
/// voucher balancing to the paisa; and that a plain GST company (no history/cess) creates NO Cess ledger and resolves
/// exactly as Phase-4/8 (ER-13). All pure, deterministic, paisa-exact.
/// </summary>
public sealed class GstAdvancedRateCessTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // The regime cut-over: legacy/cess rows end 21-Sep-2025 (inclusive); new-rate/40% start 22-Sep-2025.
    private static readonly DateOnly PreCutover = new(2025, 9, 20);
    private static readonly DateOnly PostCutover = new(2025, 9, 25);

    private static Company NewAdvancedGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("GST 2.0 Traders", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst(); // opt-in advanced GST: dated rate history + cess windows + Cess ledgers
        return c;
    }

    private static StockItem NewItem(Company c, string name, string hsn, int rateBp)
    {
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem(name, inv.CreateStockGroup(name + "-grp").Id, inv.CreateSimpleUnit(name + "-u", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = hsn, Taxability = GstTaxability.Taxable, RateBasisPoints = rateBp };
        return item;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ---------------------------------------------------------------- 1. Rate resolution by voucher date (RQ-1)

    [Fact]
    public void Car_hsn_resolves_28pct_before_cutover_and_40pct_after()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var car = NewItem(c, "Luxury Car", "8703", 2800); // base scalar 28%

        Assert.Equal(2800, gst.ResolveRate(car, null, PreCutover).RateBasisPoints);  // legacy window
        Assert.Equal(4000, gst.ResolveRate(car, null, PostCutover).RateBasisPoints); // GST 2.0 de-merit
    }

    [Fact]
    public void Cutover_boundary_is_inclusive_no_off_by_one()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var car = NewItem(c, "Car", "8703", 2800);

        // Legacy ends 21-Sep (inclusive); new starts 22-Sep (inclusive).
        Assert.Equal(2800, gst.ResolveRate(car, null, new DateOnly(2025, 9, 21)).RateBasisPoints);
        Assert.Equal(4000, gst.ResolveRate(car, null, new DateOnly(2025, 9, 22)).RateBasisPoints);
    }

    [Fact]
    public void Item_with_no_history_row_resolves_via_scalar_rate_unchanged()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        // HSN 9999 has NO dated history row → the override must not fire; the base scalar (1800) wins on any date.
        var plain = NewItem(c, "Plain Widget", "9999", 1800);

        Assert.Equal(1800, gst.ResolveRate(plain, null, PreCutover).RateBasisPoints);
        Assert.Equal(1800, gst.ResolveRate(plain, null, PostCutover).RateBasisPoints);
        // And with no date at all — pure Phase-4 behaviour.
        Assert.Equal(1800, gst.ResolveRate(plain, null).RateBasisPoints);
    }

    [Fact]
    public void Tobacco_carveout_resolves_rsp_valuation_basis()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var panMasala = NewItem(c, "Pan Masala", "21069020", 2800);

        var r = gst.ResolveRate(panMasala, null, PostCutover);
        Assert.Equal(2800, r.RateBasisPoints); // stayed 28% (did NOT move to 40%)
        Assert.Equal(GstValuationBasis.RetailSalePrice, r.ValuationBasis);
    }

    // ---------------------------------------------------------------- 2. Cess valuation modes (RQ-2/RQ-9)

    [Fact]
    public void Ad_valorem_cess_is_taxable_value_times_rate()
    {
        // Aerated waters (HSN 2202) @ 12% cess, window (a). V = 100000 → cess 12000.
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var soda = NewItem(c, "Soda", "2202", 1800);

        var cess = gst.ResolveCess(soda, null, PreCutover, quantity: 1m)!.Value;
        Assert.Equal(CessValuationMode.AdValorem, cess.Mode);
        Assert.Equal(Money.FromRupees(12000m), cess.ComputeCess(Money.FromRupees(100000m)));
    }

    [Fact]
    public void Specific_cess_is_quantity_times_per_unit()
    {
        // Coal (HSN 2701) ₹400/tonne, window (a). qty 10 → cess 4000.
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var coal = NewItem(c, "Coal", "2701", 1800);

        var cess = gst.ResolveCess(coal, null, PreCutover, quantity: 10m)!.Value;
        Assert.Equal(CessValuationMode.Specific, cess.Mode);
        Assert.Equal(Money.FromRupees(4000m), cess.ComputeCess(Money.FromRupees(50000m))); // taxable value irrelevant
    }

    [Fact]
    public void Rsp_factor_cess_is_quantity_times_rsp_times_factor()
    {
        // Pan masala (HSN 21069020) factor 0.32R. qty 5, RSP 100 → 5 × 100 × 0.32 = 160.
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var pan = NewItem(c, "Pan Masala", "21069020", 2800);
        pan.Gst!.RetailSalePrice = Money.FromRupees(100m);

        var cess = gst.ResolveCess(pan, null, PostCutover, quantity: 5m)!.Value;
        Assert.Equal(CessValuationMode.RetailSalePriceFactor, cess.Mode);
        Assert.Equal(Money.FromRupees(160m), cess.ComputeCess(Money.FromRupees(500m)));
    }

    [Fact]
    public void Car_cess_positive_before_cutover_and_zero_after()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var car = NewItem(c, "Car", "8703", 2800);

        Assert.NotNull(gst.ResolveCess(car, null, PreCutover, 1m));                 // window (a) — cess applies
        var pre = gst.ResolveCess(car, null, PreCutover, 1m)!.Value;
        Assert.True(pre.ComputeCess(Money.FromRupees(1_000_000m)).Amount > 0m);
        Assert.Null(gst.ResolveCess(car, null, PostCutover, 1m));                    // window (b) — car cess withdrawn
    }

    [Fact]
    public void Forty_percent_demerit_item_bears_no_cess_after_cutover()
    {
        // ER-1 guard: a 40%-slab de-merit item that is NOT a cess HSN has zero cess (40% is ordinary GST, not cess).
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var suv = NewItem(c, "SUV Accessory", "8708", 4000); // 40% slab, no cess row

        Assert.Null(gst.ResolveCess(suv, null, PostCutover, 1m));
    }

    // ---------------------------------------------------------------- 3. Cess ring-fence (ER-2)

    [Fact]
    public void Cess_posts_to_output_cess_only_never_to_gst_heads()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var soda = NewItem(c, "Soda", "2202", 1800);
        var cess = gst.ResolveCess(soda, null, PreCutover, 1m);

        // Intra-state sale: V = 1000 @ 18% + 12% cess.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800, cess) },
            interState: false, GstTaxDirection.Output);

        Assert.Equal(Money.FromRupees(180m), tax.TotalTax);        // CGST 90 + SGST 90 — cess NOT included
        Assert.Equal(Money.FromRupees(120m), tax.TotalCess);       // 1000 × 12%
        Assert.Equal(Money.FromRupees(90m), tax.TotalCgst);
        Assert.Equal(Money.FromRupees(90m), tax.TotalSgst);

        // Cess landed on the Output Cess ledger, and no cess amount on CGST/SGST/IGST.
        var cessLedger = gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output)!;
        var cessLine = Assert.Single(tax.TaxLines, l => l.LedgerId == cessLedger.Id);
        Assert.Equal(Money.FromRupees(120m), cessLine.Amount);
        Assert.Equal(GstTaxHead.Cess, cessLine.Gst!.TaxHead);
        Assert.DoesNotContain(tax.TaxLines, l => l.Gst!.TaxHead != GstTaxHead.Cess && l.LedgerId == cessLedger.Id);
    }

    // ---------------------------------------------------------------- 4. Cess-bearing voucher balances to the paisa

    [Fact]
    public void Cess_bearing_sales_voucher_balances_and_posts_cess()
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var main = c.MainLocation!.Id;

        var soda = inv.CreateStockItem("Soda", inv.CreateStockGroup("Bev").Id, inv.CreateSimpleUnit("Case", "Cases").Id);
        soda.Gst = new StockItemGstDetails { HsnSac = "2202", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);

        // Stock on hand.
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, PreCutover,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(500m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(soda.Id, main, 10m, Money.FromRupees(50m)) }));

        // Sale: 10 @ ₹100 = ₹1000; 18% ⇒ CGST 90 + SGST 90; 12% cess = 120. Party = 1000 + 180 + 120 = 1300.
        var cess = gst.ResolveCess(soda, null, PreCutover, quantity: 10m);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800, cess) },
            interState: false, GstTaxDirection.Output);

        Assert.Equal(Money.FromRupees(120m), tax.TotalCess);

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1300m), DrCr.Debit),  // party = taxable + tax + cess
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, PreCutover, lines,
            inventoryLines: new[] { new VoucherInventoryLine(soda.Id, main, 10m, Money.FromRupees(100m)) }));

        Assert.True(VoucherValidator.IsBalanced(v)); // Σ Dr = Σ Cr to the paisa
        // Σ posted cess lines == computed TotalCess.
        var cessLedger = gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output)!;
        var postedCess = v.Lines.Where(l => l.LedgerId == cessLedger.Id).Sum(l => l.Amount.Amount);
        Assert.Equal(120m, postedCess);
        Assert.Equal(-120m, LedgerBalances.SignedClosing(c, cessLedger, PreCutover)); // Output cess = credit
    }

    // ---------------------------------------------------------------- 5. ER-13: plain GST company, no cess ledger

    [Fact]
    public void Plain_gst_company_creates_no_cess_ledger_and_no_history()
    {
        var c = CompanyFactory.CreateSeeded("Plain GST Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        // No advanced-GST opt-in ⇒ empty history/cess, and NO Cess ledger (only 6 GST heads + Round Off).
        Assert.Empty(c.Gst!.RateHistory);
        Assert.Empty(c.Gst!.CessRates);
        Assert.Null(gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output));
        Assert.Null(gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Input));
        Assert.DoesNotContain(c.Ledgers, l => l.Name is "Output Cess" or "Input Cess");

        // And rate resolution with a date but no history behaves exactly as Phase-4 (the base scalar wins).
        var item = NewItem(c, "Widget", "1234", 1800);
        Assert.Equal(1800, gst.ResolveRate(item, null, PostCutover).RateBasisPoints);
    }

    [Fact]
    public void Advanced_seed_is_idempotent()
    {
        var c = NewAdvancedGstCompany();
        var historyBefore = c.Gst!.RateHistory.Count;
        var cessBefore = c.Gst!.CessRates.Count;
        var ledgersBefore = c.Ledgers.Count;

        new GstService(c).SeedAdvancedGst(); // second call must not duplicate

        Assert.Equal(historyBefore, c.Gst!.RateHistory.Count);
        Assert.Equal(cessBefore, c.Gst!.CessRates.Count);
        Assert.Equal(ledgersBefore, c.Ledgers.Count);
        Assert.Single(c.Ledgers, l => l.Name == "Output Cess");
    }

    // ---------------------------------------------------------------- 6. A10 S1 regressions: cess vs report projections

    /// <summary>
    /// Posts a plain stock-seeding purchase then one <b>intra-state B2B</b> sale of <paramref name="qty"/> ×
    /// <paramref name="unitPrice"/> of a fresh <paramref name="hsn"/> item (at <paramref name="rateBp"/> GST, plus the
    /// dated cess when <paramref name="withCess"/>), on <paramref name="date"/>, to a registered Maharashtra debtor.
    /// Returns the company so the GST reports can be projected over it.
    /// </summary>
    private static Company CompanyWithB2BSale(string hsn, int rateBp, decimal qty, decimal unitPrice, DateOnly date, bool withCess)
    {
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var main = c.MainLocation!.Id;

        var item = inv.CreateStockItem("Item-" + hsn, inv.CreateStockGroup("Grp-" + hsn).Id, inv.CreateSimpleUnit("U-" + hsn, "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = hsn, Taxability = GstTaxability.Taxable, RateBasisPoints = rateBp };

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        var debtor = AddLedger(c, "Reg Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        // Seed stock (plain purchase, no GST) so the sale never risks negative stock.
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(qty * unitPrice), DrCr.Debit),
                    new EntryLine(creditor.Id, Money.FromRupees(qty * unitPrice), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, qty, Money.FromRupees(unitPrice)) }));

        var value = qty * unitPrice;
        var cess = withCess ? gst.ResolveCess(item, null, date, qty) : null;
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), rateBp, cess) },
            interState: false, GstTaxDirection.Output);

        var partyTotal = value + tax.TotalTax.Amount + tax.TotalCess.Amount;
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(partyTotal), DrCr.Debit), // party = taxable + GST + cess
            new(sales.Id, Money.FromRupees(value), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, date, lines,
            partyId: debtor.Id, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, qty, Money.FromRupees(unitPrice)) }));

        return c;
    }

    [Fact]
    public void Cess_invoice_does_not_double_count_taxable_or_inject_phantom_rate_in_gstr1_gstr3b()
    {
        // #1: an intra-state B2B sale of a cess-HSN item — ₹1000 @18% GST + 12% cess (HSN 2202, window (a)).
        var c = CompanyWithB2BSale("2202", 1800, qty: 10m, unitPrice: 100m, date: PreCutover, withCess: true);

        // GSTR-3B taxable outward value must be the TRUE ₹1000 — not doubled to ₹2000 by the cess line.
        var r3b = Gstr3b.Build(c, FyStart, PostCutover);
        Assert.Equal(Money.FromRupees(1000m), r3b.TaxableOutwardValue);
        Assert.Equal(Money.FromRupees(90m), r3b.OutwardCgst);
        Assert.Equal(Money.FromRupees(90m), r3b.OutwardSgst);

        // GSTR-1 rate summary must have NO phantom cess-rate row (the 12% cess bp doubled = 2400) — only the real 18%.
        var r1 = Gstr1.Build(c, FyStart, PostCutover);
        Assert.DoesNotContain(r1.RateSummary, row => row.RateBasisPoints == 2400);
        var rateRow = Assert.Single(r1.RateSummary);
        Assert.Equal(1800, rateRow.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1000m), rateRow.TaxableValue);

        // The single B2B invoice row carries the true ₹1000 taxable (not the doubled ₹2000).
        var b2b = Assert.Single(r1.B2B);
        Assert.Equal(Money.FromRupees(1000m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), b2b.Cgst);
        Assert.Equal(Money.FromRupees(90m), b2b.Sgst);
    }

    [Fact]
    public void Plain_no_cess_invoice_gstr_projections_are_unchanged()
    {
        // #1 no-regression control: a plain (no-cess) invoice must project exactly ₹1000 taxable, one 18% rate row.
        var c = CompanyWithB2BSale("9999", 1800, qty: 10m, unitPrice: 100m, date: PreCutover, withCess: false);

        var r3b = Gstr3b.Build(c, FyStart, PostCutover);
        Assert.Equal(Money.FromRupees(1000m), r3b.TaxableOutwardValue);

        var r1 = Gstr1.Build(c, FyStart, PostCutover);
        var rateRow = Assert.Single(r1.RateSummary);
        Assert.Equal(1800, rateRow.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1000m), rateRow.TaxableValue);
        var b2b = Assert.Single(r1.B2B);
        Assert.Equal(Money.FromRupees(1000m), b2b.TaxableValue);
    }

    [Fact]
    public void Exempt_item_sharing_a_cess_hsn_bears_no_cess_and_posts_no_cess_line()
    {
        // #2: HSN 2202 IS a cess HSN (aerated waters 12%), but this item is Exempt → cess must NOT over-collect.
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var main = c.MainLocation!.Id;

        var exemptSoda = inv.CreateStockItem("Exempt Soda", inv.CreateStockGroup("Bev").Id, inv.CreateSimpleUnit("Case", "Cases").Id);
        exemptSoda.Gst = new StockItemGstDetails { HsnSac = "2202", Taxability = GstTaxability.Exempt };

        Assert.Null(gst.ResolveCess(exemptSoda, null, PreCutover, 1m)); // taxability short-circuits cess

        // A posted exempt sale carries NO tax lines → nothing lands on the Output Cess ledger.
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        var debtor = AddLedger(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        // Seed stock so the sale never risks negative stock.
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, PreCutover,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(1000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(1000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(exemptSoda.Id, main, 10m, Money.FromRupees(100m)) }));

        var v = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, PreCutover,
            new[] { new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit), new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit) },
            partyId: debtor.Id, inventoryLines: new[] { new VoucherInventoryLine(exemptSoda.Id, main, 10m, Money.FromRupees(100m)) }));

        var cessLedger = gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output)!;
        Assert.DoesNotContain(v.Lines, l => l.LedgerId == cessLedger.Id);
    }

    [Fact]
    public void Inherited_rsp_factor_cess_with_no_declared_rsp_fails_fast_not_silent_zero()
    {
        // #3: pan masala (HSN 21069020) inherits an RSP-factor cess row, but this item declares NO Retail Sale Price.
        // The engine must FAIL FAST rather than silently valuing the cess at ₹0 (which would under-collect).
        var c = NewAdvancedGstCompany();
        var gst = new GstService(c);
        var pan = NewItem(c, "Pan Masala", "21069020", 2800); // RetailSalePrice deliberately left null

        var ex = Assert.Throws<InvalidOperationException>(() => gst.ResolveCess(pan, null, PostCutover, 5m));
        Assert.Contains("Retail Sale Price", ex.Message);
    }
}
