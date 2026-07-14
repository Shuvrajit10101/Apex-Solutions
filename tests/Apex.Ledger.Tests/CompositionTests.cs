using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 3 — Composition scheme + Bill of Supply + CMP-08 / GSTR-4 (RQ-4/RQ-10/RQ-16; ER-4/ER-9/ER-10; DP-9).
/// All pure, deterministic, paisa-exact. Proves: a composition dealer issues a Bill of Supply (no output tax) and
/// claims no ITC; EnableGst gates the six GST ledgers off; tax-on-turnover base per sub-type (Manufacturer/Restaurant
/// TOTAL vs Trader/ServiceProvider TAXABLE) split C+S paisa-exact; inward RCM still at normal rate but ITC-blocked
/// (routed to cost); CMP-08 / GSTR-4 / GSTR-9A projections; GSTR-1/3B suppressed for composition; threshold/
/// special-state resolver. A Regular company is byte-identical (never enters a composition branch).
/// </summary>
public sealed class CompositionTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly Q1From = new(2025, 4, 1);
    private static readonly DateOnly Q1To = new(2025, 6, 30);
    private static readonly DateOnly D1 = new(2025, 4, 5);
    private static readonly DateOnly D2 = new(2025, 4, 10);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static Company NewCompositionCompany(
        CompositionSubType subType, string home = "27", string gstin = GstinMaharashtra)
    {
        var c = CompanyFactory.CreateSeeded("Composition Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = home, Gstin = gstin, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = subType, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        return c;
    }

    private static Company NewRegularCompany(string home = "27", string gstin = GstinMaharashtra)
    {
        var c = CompanyFactory.CreateSeeded("Regular Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = home, Gstin = gstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ---------------------------------------------------------------- Test 1: Bill of Supply — no output tax

    [Fact]
    public void Composition_sale_issues_a_bill_of_supply_with_no_output_tax()
    {
        var c = NewCompositionCompany(CompositionSubType.Trader);
        var gst = new GstService(c);

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            interState: false, GstTaxDirection.Output);

        Assert.Empty(tax.TaxLines);
        Assert.Equal(Money.Zero, tax.TotalTax);
        Assert.Equal(Money.Zero, tax.TotalCess);
        Assert.Null(tax.RoundOffLine);

        // The assembled sale posts party Dr = supply value, sales Cr = supply value, balanced, with NO tax leg.
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(1000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, D1, lines, partyId: party.Id));

        Assert.Equal(v.TotalDebit, v.TotalCredit);
        Assert.DoesNotContain(v.Lines, l => ClassificationRules.IsDutiesAndTaxesLedger(c.FindLedger(l.LedgerId)!, c));

        // The Regular twin still posts tax (proves the guard is reg-type-scoped).
        var reg = NewRegularCompany();
        var regTax = new GstService(reg).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.NotEmpty(regTax.TaxLines);
        Assert.Equal(Money.FromRupees(180m), regTax.TotalTax);
    }

    [Fact]
    public void Is_bill_of_supply_is_true_only_for_a_composition_sales_voucher()
    {
        var c = NewCompositionCompany(CompositionSubType.Restaurant);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var v = new Voucher(Guid.NewGuid(), salesType, D1, new[]
        {
            new EntryLine(Add(c, "Cust", "Sundry Debtors", true).Id, Money.FromRupees(500m), DrCr.Debit),
            new EntryLine(Add(c, "Sales", "Sales Accounts", false).Id, Money.FromRupees(500m), DrCr.Credit),
        });
        Assert.True(GstReportSupport.IsBillOfSupply(c, v));

        var reg = NewRegularCompany();
        var regSalesType = reg.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var rv = new Voucher(Guid.NewGuid(), regSalesType, D1, new[]
        {
            new EntryLine(Add(reg, "Cust", "Sundry Debtors", true).Id, Money.FromRupees(500m), DrCr.Debit),
            new EntryLine(Add(reg, "Sales", "Sales Accounts", false).Id, Money.FromRupees(500m), DrCr.Credit),
        });
        Assert.False(GstReportSupport.IsBillOfSupply(reg, rv));
        Assert.DoesNotContain("Tally", GstReportSupport.BillOfSupplyDeclaration, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- Test 2: ITC blocked (inward)

    [Fact]
    public void Composition_purchase_claims_no_itc()
    {
        var c = NewCompositionCompany(CompositionSubType.Manufacturer);
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) },
            interState: false, GstTaxDirection.Input);
        Assert.Empty(tax.TaxLines);
        Assert.Equal(Money.Zero, tax.TotalTax);
    }

    // ---------------------------------------------------------------- Test 3: EnableGst gates the six ledgers off

    [Fact]
    public void EnableGst_creates_no_gst_tax_ledgers_for_a_composition_company()
    {
        var c = NewCompositionCompany(CompositionSubType.Trader);
        var gst = new GstService(c);

        foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
            foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
                Assert.Null(gst.FindTaxLedger(head, direction));

        // Round-Off is still created (harmless), and there are no Output/Input GST ledgers under Duties & Taxes.
        Assert.NotNull(c.FindLedgerByName(GstService.RoundOffLedgerName));
        Assert.DoesNotContain(c.Ledgers, l => l.GstClassification is not null);

        // The Regular twin creates all six.
        var reg = NewRegularCompany();
        var regGst = new GstService(reg);
        foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
            foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
                Assert.NotNull(regGst.FindTaxLedger(head, direction));
    }

    // ---------------------------------------------------------------- Test 4: tax-on-turnover per sub-type + base

    /// <summary>Posts a taxable sale of ₹1,00,001 + an exempt sale of ₹50,000, then checks the composition tax base per
    /// sub-type (total-turnover vs taxable-only) and the paisa-exact C+S split.</summary>
    private static Company BuildTurnoverFixture(CompositionSubType subType, string home = "27")
    {
        var c = NewCompositionCompany(subType, home);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };
        inv.AddOpeningBalance(widget.Id, main, 10m, Money.FromRupees(50m));
        inv.AddOpeningBalance(book.Id, main, 10m, Money.FromRupees(50m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var ledgers = new LedgerService(c);

        // Taxable sale ₹1,00,001 (1 Widget @ ₹1,00,001) — a Bill of Supply, no tax.
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D1, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(100001m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100001m), DrCr.Credit),
        }, partyId: party.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(100001m)) }));

        // Exempt sale ₹50,000 (1 Book @ ₹50,000).
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(50000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        }, partyId: party.Id, inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 1m, Money.FromRupees(50000m)) }));

        return c;
    }

    [Fact]
    public void Trader_taxes_taxable_turnover_only_split_paisa_exact()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Trader);
        var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);

        Assert.Equal(Money.FromRupees(150001m), t.TotalTurnover);
        Assert.Equal(Money.FromRupees(100001m), t.TaxableTurnover);
        Assert.Equal(Money.FromRupees(100001m), t.TurnoverBase);   // Trader ⇒ taxable-only
        Assert.Equal(100, t.RateBasisPoints);

        // 1,00,001 @ 1% = 1000.01 → CGST round(500.005)=500.01, SGST 500.00 (CGST carries the odd-paisa remainder).
        Assert.Equal(Money.FromRupees(1000.01m), t.CompositionTaxAmount);
        Assert.Equal(Money.FromRupees(500.01m), t.Cgst);
        Assert.Equal(Money.FromRupees(500.00m), t.Sgst);
        Assert.Equal(t.CompositionTaxAmount, new Money(t.Cgst.Amount + t.Sgst.Amount)); // parity (L-4)
    }

    [Fact]
    public void Manufacturer_taxes_total_turnover()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Manufacturer);
        var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);
        Assert.Equal(Money.FromRupees(150001m), t.TurnoverBase);    // Manufacturer ⇒ total turnover
        Assert.Equal(100, t.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1500.01m), t.CompositionTaxAmount); // 1,50,001 @ 1%
    }

    [Fact]
    public void Restaurant_taxes_total_turnover_at_5_percent()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Restaurant);
        var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);
        Assert.Equal(Money.FromRupees(150001m), t.TurnoverBase);
        Assert.Equal(500, t.RateBasisPoints);
        Assert.Equal(Money.FromRupees(7500.05m), t.CompositionTaxAmount); // 1,50,001 @ 5%
        Assert.Equal(t.CompositionTaxAmount, new Money(t.Cgst.Amount + t.Sgst.Amount));
    }

    [Fact]
    public void ServiceProvider_taxes_taxable_turnover_at_6_percent()
    {
        var c = BuildTurnoverFixture(CompositionSubType.ServiceProvider);
        var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);
        Assert.Equal(Money.FromRupees(100001m), t.TurnoverBase);    // §10(2A) ⇒ taxable-only
        Assert.Equal(600, t.RateBasisPoints);
        Assert.Equal(Money.FromRupees(6000.06m), t.CompositionTaxAmount); // 1,00,001 @ 6%
    }

    // ---------------------------------------------------------------- Test 5: inward RCM still normal-rate, no ITC

    private static (Company Company, Domain.Ledger Expense, Domain.Ledger Party) BuildRcmScene(Company c)
    {
        var gst = new GstService(c);
        gst.SeedAdvancedGst();
        var legal = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal");
        var expense = new Domain.Ledger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true)
        {
            SalesPurchaseGst = new StockItemGstDetails
            {
                Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
                ReverseChargeApplicable = true, RcmCategoryId = legal.Id,
            },
        };
        c.AddLedger(expense);
        var party = new Domain.Ledger(Guid.NewGuid(), "Advocate", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27", IsBodyCorporate = true },
        };
        c.AddLedger(party);
        return (c, expense, party);
    }

    [Fact]
    public void Composition_inward_rcm_posts_the_cash_liability_but_blocks_itc()
    {
        var (c, expense, party) = BuildRcmScene(NewCompositionCompany(CompositionSubType.Manufacturer));
        var rcm = new RcmService(c);

        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies);

        // Output liability posts to the dedicated RCM Output ledgers (CGST + SGST, 900 each).
        var outputs = posting.Lines.Where(l => l.Side == DrCr.Credit).ToList();
        Assert.Equal(2, outputs.Count);
        Assert.All(outputs, l => Assert.True(c.FindLedger(l.LedgerId)!.GstClassification is { IsReverseCharge: true }));
        Assert.Equal(Money.FromRupees(1800m), new Money(outputs.Sum(l => l.Amount.Amount)));

        // No ITC: no line is rcm-scheme tagged, and the balancing debit is the non-creditable cost ledger.
        Assert.DoesNotContain(posting.Lines, l => l.Gst is { RcmScheme: not null });
        var cost = c.FindLedgerByName(GstService.RcmNonCreditableCostLedgerName);
        Assert.NotNull(cost);
        var debits = posting.Lines.Where(l => l.Side == DrCr.Debit).ToList();
        Assert.All(debits, l => Assert.Equal(cost!.Id, l.LedgerId));
        Assert.All(debits, l => Assert.False(l.HasGst)); // untagged ⇒ never an ITC line

        // The full voucher balances: Dr expense + Dr cost = Cr party + Cr RCM output.
        var lines = new List<EntryLine> { new(expense.Id, Money.FromRupees(10000m), DrCr.Debit), new(party.Id, Money.FromRupees(10000m), DrCr.Credit) };
        lines.AddRange(posting.Lines);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines));
        Assert.Equal(v.TotalDebit, v.TotalCredit);

        // The Regular twin's RCM dual leg still grants ITC (unchanged).
        var (rc, rexp, rparty) = BuildRcmScene(NewRegularCompany());
        var rposting = new RcmService(rc).BuildReverseCharge(Money.FromRupees(10000m), null, rexp, rparty.PartyGst, D1, RcmService.SupplyKind.Domestic);
        Assert.Contains(rposting.Lines, l => l.Gst is { IsReverseCharge: true, RcmScheme: RcmItcScheme.OtherRcm } && l.Side == DrCr.Debit);
    }

    // ---------------------------------------------------------------- Test 6: CMP-08 reconciles

    [Fact]
    public void Cmp08_reconciles_turnover_tax_plus_inward_rcm()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Trader);
        var (_, expense, party) = BuildRcmScene(c);
        var rcm = new RcmService(c);
        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D2, RcmService.SupplyKind.Domestic);
        var lines = new List<EntryLine> { new(expense.Id, Money.FromRupees(10000m), DrCr.Debit), new(party.Id, Money.FromRupees(10000m), DrCr.Credit) };
        lines.AddRange(posting.Lines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D2, lines));

        var cmp = Cmp08.Build(c, Q1From, Q1To);
        Assert.True(cmp.Applicable);
        Assert.Equal(CompositionSubType.Trader, cmp.SubType);
        Assert.Equal(Money.FromRupees(1000.01m), cmp.OutwardTurnoverTax); // 1,00,001 @ 1%
        Assert.Equal(Money.FromRupees(1800m), cmp.InwardRcmTax);          // 10,000 @ 18% RCM (cash)
        Assert.Equal(new Money(cmp.OutwardTurnoverTax.Amount + cmp.InwardRcmTax.Amount), cmp.TotalTaxPayable);
        Assert.Equal(Money.Zero, cmp.Interest);

        // A non-composition company yields a not-applicable CMP-08.
        var reg = NewRegularCompany();
        var empty = Cmp08.Build(reg, Q1From, Q1To);
        Assert.False(empty.Applicable);
        Assert.Equal(Money.Zero, empty.TotalTaxPayable);
    }

    // ---------------------------------------------------------------- Test 7: GSTR-4 / GSTR-9A

    [Fact]
    public void Gstr4_rolls_up_four_quarters_and_light_inward_tables()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Trader);
        var g4 = Gstr4.Build(c, FyStart, FyEnd);

        Assert.True(g4.Applicable);
        Assert.Equal(4, g4.Quarters.Count);
        Assert.Equal(CompositionSubType.Trader, g4.SubType);
        // Q1 carries the whole (April/May) turnover; the annual roll-up equals it (all sales in Q1).
        Assert.Equal(Money.FromRupees(1000.01m), g4.AnnualCompositionTax);
        Assert.Equal(g4.AnnualCompositionTax, g4.Quarters[0].OutwardTurnoverTax);
        Assert.Equal(Money.Zero, g4.Quarters[1].OutwardTurnoverTax);

        var reg = NewRegularCompany();
        Assert.False(Gstr4.Build(reg, FyStart, FyEnd).Applicable);
    }

    [Fact]
    public void Gstr9a_is_a_light_fy_roll_up()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Manufacturer);
        var g9 = Gstr9a.Build(c, FyStart, FyEnd);
        Assert.True(g9.Applicable);
        Assert.Equal(Money.FromRupees(150001m), g9.TotalTurnover);
        Assert.Equal(Money.FromRupees(1500.01m), g9.CompositionTaxPaid);
        Assert.Equal(Money.Zero, g9.LateFee);

        Assert.False(Gstr9a.Build(NewRegularCompany(), FyStart, FyEnd).Applicable);
    }

    [Fact]
    public void Gstr4_inward_tables_project_reverse_charge_from_purchases()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Trader);
        var (_, expense, party) = BuildRcmScene(c);
        var posting = new RcmService(c).BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D2, RcmService.SupplyKind.Domestic);
        var lines = new List<EntryLine> { new(expense.Id, Money.FromRupees(10000m), DrCr.Debit), new(party.Id, Money.FromRupees(10000m), DrCr.Credit) };
        lines.AddRange(posting.Lines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D2, lines));

        var g4 = Gstr4.Build(c, FyStart, FyEnd);
        Assert.Equal(Money.FromRupees(10000m), g4.Inward.ReverseChargeValue);
        Assert.Equal(Money.FromRupees(1800m), g4.Inward.ReverseChargeTax);
    }

    // ---------------------------------------------------------------- Test 8: GSTR-1 / GSTR-3B suppressed

    [Fact]
    public void Gstr1_and_gstr3b_are_empty_for_a_composition_company()
    {
        var c = BuildTurnoverFixture(CompositionSubType.Trader);
        var g1 = Gstr1.Build(c, Q1From, Q1To);
        Assert.Empty(g1.B2B);
        Assert.Empty(g1.B2C);
        Assert.Empty(g1.RateSummary);
        Assert.Empty(g1.HsnSummary);
        Assert.Equal(Money.Zero, g1.ExemptNilNonGstValue);
        Assert.Equal(Money.Zero, g1.TotalTax);

        var g3 = Gstr3b.Build(c, Q1From, Q1To);
        Assert.Equal(Money.Zero, g3.TotalOutwardTax);
        Assert.Equal(Money.Zero, g3.TaxableOutwardValue);
        Assert.Equal(Money.Zero, g3.ExemptNilNonGstOutward);

        // A Regular company with the same posted supplies still produces a full GSTR-1 (guard is reg-type-scoped).
        var reg = BuildRegularSalesFixture();
        Assert.NotEmpty(Gstr1.Build(reg, Q1From, Q1To).RateSummary);
    }

    private static Company BuildRegularSalesFixture()
    {
        var c = NewRegularCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(50m));
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var party = Add(c, "Cust", "Sundry Debtors", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, lines,
            partyId: party.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 10m, Money.FromRupees(100m)) }));
        return c;
    }

    // ---------------------------------------------------------------- Test 9: threshold / special-state

    [Fact]
    public void Threshold_resolver_matches_law()
    {
        // General state (Maharashtra 27): ₹1.5 cr for a goods dealer.
        Assert.Equal(new Money(15_000_000m), CompositionThreshold.Threshold(CompositionSubType.Trader, "27"));
        Assert.Equal(new Money(15_000_000m), CompositionThreshold.Threshold(CompositionSubType.Manufacturer, "18")); // Assam not special
        // Special-category state (Tripura 16): ₹75 L for a goods dealer.
        Assert.Equal(new Money(7_500_000m), CompositionThreshold.Threshold(CompositionSubType.Trader, "16"));
        foreach (var s in new[] { "05", "11", "12", "13", "14", "15", "16", "17" })
            Assert.Equal(new Money(7_500_000m), CompositionThreshold.Threshold(CompositionSubType.Manufacturer, s));
        // §10(2A) service provider: ₹50 L regardless of state.
        Assert.Equal(new Money(5_000_000m), CompositionThreshold.Threshold(CompositionSubType.ServiceProvider, "27"));
        Assert.Equal(new Money(5_000_000m), CompositionThreshold.Threshold(CompositionSubType.ServiceProvider, "16"));

        // Rates + base rule.
        Assert.Equal(100, CompositionThreshold.RateBasisPoints(CompositionSubType.Trader));
        Assert.Equal(500, CompositionThreshold.RateBasisPoints(CompositionSubType.Restaurant));
        Assert.Equal(600, CompositionThreshold.RateBasisPoints(CompositionSubType.ServiceProvider));
        Assert.True(CompositionThreshold.TaxesTotalTurnover(CompositionSubType.Manufacturer));
        Assert.True(CompositionThreshold.TaxesTotalTurnover(CompositionSubType.Restaurant));
        Assert.False(CompositionThreshold.TaxesTotalTurnover(CompositionSubType.Trader));
        Assert.False(CompositionThreshold.TaxesTotalTurnover(CompositionSubType.ServiceProvider));
    }

    // ---------------------------------------------------------------- EnsureValid guards

    [Fact]
    public void Composition_config_requires_a_sub_type_and_a_gstin()
    {
        var c = CompanyFactory.CreateSeeded("X", FyStart);
        Assert.Throws<ArgumentException>(() => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            // no sub-type
        }));
        var c2 = CompanyFactory.CreateSeeded("Y", FyStart);
        Assert.Throws<ArgumentException>(() => new GstService(c2).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = null, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader,
        }));
    }

    // ---------------------------------------------------------------- Fix #1: taxable-only base excludes an exempt as-voucher sale

    /// <summary>An unclassified (no GST block) EXEMPT as-voucher sale must NOT inflate the TAXABLE-ONLY base (Trader /
    /// §10(2A)) — pre-fix the <c>?? true</c> default folded it in (over-collection). The TOTAL-turnover sub-types
    /// (Manufacturer / Restaurant) read total turnover, so it still counts for them (base-rule-aware, not a blanket
    /// flip). Fails pre-fix on the <c>TaxableTurnover</c> assertion.</summary>
    [Fact]
    public void Taxable_only_base_excludes_an_unclassified_exempt_as_voucher_sale_while_total_base_includes_it()
    {
        foreach (var (subType, taxableOnly) in new[]
        {
            (CompositionSubType.Trader, true),
            (CompositionSubType.ServiceProvider, true),
            (CompositionSubType.Manufacturer, false),
            (CompositionSubType.Restaurant, false),
        })
        {
            var c = NewCompositionCompany(subType);
            var party = Add(c, "Walk-in", "Sundry Debtors", true);
            var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
            var ledgers = new LedgerService(c);

            // Taxable as-voucher sale ₹40,000 — the sales ledger carries an EXPLICITLY-taxable GST block.
            var taxableSales = new Domain.Ledger(Guid.NewGuid(), "Taxable Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
            {
                SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
            };
            c.AddLedger(taxableSales);
            ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D1, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(40000m), DrCr.Debit),
                new EntryLine(taxableSales.Id, Money.FromRupees(40000m), DrCr.Credit),
            }, partyId: party.Id));

            // Exempt as-voucher sale ₹10,000 — booked on an UNCLASSIFIED sales ledger (SalesPurchaseGst == null).
            var exemptSales = Add(c, "Exempt Sales", "Sales Accounts", false);
            ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(10000m), DrCr.Debit),
                new EntryLine(exemptSales.Id, Money.FromRupees(10000m), DrCr.Credit),
            }, partyId: party.Id));

            var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);
            Assert.Equal(Money.FromRupees(50000m), t.TotalTurnover);   // total always includes the exempt sale
            Assert.Equal(Money.FromRupees(40000m), t.TaxableTurnover); // taxable EXCLUDES the unclassified/exempt sale
            Assert.Equal(taxableOnly ? Money.FromRupees(40000m) : Money.FromRupees(50000m), t.TurnoverBase);
        }
    }

    // ---------------------------------------------------------------- Fix #2/#3: GSTR-4 Table 6 == Σ quarterly CMP-08 Table 5

    /// <summary>With odd-paisa turnover of ₹1,250.50 in EACH quarter, a whole-FY re-round gives round(5,002.00 × 1%) =
    /// ₹50.02, but each quarter self-assesses round(1,250.50 × 1%) = ₹12.51 ⇒ Σ = ₹50.04. GSTR-4 Table 6 must equal the
    /// sum of the four rounded CMP-08 Table 5 figures (reconciles by construction). Fails pre-fix (₹50.02 ≠ ₹50.04).</summary>
    [Fact]
    public void Gstr4_annual_table6_equals_the_sum_of_the_four_quarterly_cmp08_on_odd_paisa()
    {
        var c = NewCompositionCompany(CompositionSubType.Trader);
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        foreach (var date in new[]
        {
            new DateOnly(2025, 4, 15), new DateOnly(2025, 8, 15), new DateOnly(2025, 11, 15), new DateOnly(2026, 2, 15),
        })
            ledgers.Post(new Voucher(Guid.NewGuid(), salesType, date, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(1250.50m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1250.50m), DrCr.Credit),
            }, partyId: party.Id));

        var g4 = Gstr4.Build(c, FyStart, FyEnd);
        var sumOfQuarters = new Money(g4.Quarters.Sum(q => q.OutwardTurnoverTax.Amount));

        Assert.Equal(Money.FromRupees(12.51m), g4.Quarters[0].OutwardTurnoverTax); // each quarter self-assesses ₹12.51
        Assert.Equal(Money.FromRupees(50.04m), sumOfQuarters);                     // Σ Table 5
        Assert.Equal(sumOfQuarters, g4.AnnualCompositionTax);                      // Table 6 == Σ Table 5 (reconciles)
        Assert.Equal(Money.FromRupees(50.04m), g4.AnnualCompositionTax);           // NOT the ₹50.02 whole-FY re-round
    }

    // ---------------------------------------------------------------- Fix #4: sale-return credit note nets down turnover

    /// <summary>A composition Trader's turnover is NET of sales returns: a ₹1,00,000 Bill-of-Supply sale then a ₹20,000
    /// sale-return Credit Note ⇒ tax on ₹80,000, not the gross ₹1,00,000. Pre-fix the outward loop skipped every
    /// non-Sales voucher, so the return never netted down (over-collection). Fails pre-fix.</summary>
    [Fact]
    public void Composition_sale_return_credit_note_nets_down_the_turnover_base()
    {
        var c = NewCompositionCompany(CompositionSubType.Trader);
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var ledgers = new LedgerService(c);

        // Bill of Supply sale ₹1,00,000 (Dr Party / Cr Sales, no tax leg).
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100000m), DrCr.Credit),
        }, partyId: party.Id));

        // Sale-return Credit Note ₹20,000 (Dr Sales / Cr Party — the sales ledger is on the DEBIT side of a return).
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id, D2, new[]
        {
            new EntryLine(sales.Id, Money.FromRupees(20000m), DrCr.Debit),
            new EntryLine(party.Id, Money.FromRupees(20000m), DrCr.Credit),
        }, partyId: party.Id));

        var t = new CompositionTaxService(c).ComputeForPeriod(Q1From, Q1To);
        Assert.Equal(Money.FromRupees(80000m), t.TotalTurnover);       // 1,00,000 − 20,000
        Assert.Equal(Money.FromRupees(80000m), t.TaxableTurnover);     // both legs are the same taxable sales ledger
        Assert.Equal(Money.FromRupees(80000m), t.TurnoverBase);        // Trader ⇒ taxable-only, net of the return
        Assert.Equal(Money.FromRupees(800m), t.CompositionTaxAmount);  // 80,000 @ 1% (NOT ₹1,000 on the gross)

        var cmp = Cmp08.Build(c, Q1From, Q1To);
        Assert.Equal(Money.FromRupees(800m), cmp.OutwardTurnoverTax);  // CMP-08 reflects the net
    }
}
