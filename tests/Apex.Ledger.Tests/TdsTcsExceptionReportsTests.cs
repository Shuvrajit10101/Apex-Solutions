using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 8 — the <b>TDS/TCS exception &amp; outstanding reports</b> (the nine pure projections R1–R9 on
/// <see cref="Report"/>). All read-only over the already-posted v29 objects: no schema, no new persistence. These
/// tests are the TDD spec — every one fails before the reports exist. They pin the verified interest law
/// (§201(1A) 1.5%/month late-deposit limb; §206C(7) 1%/month), the FIFO coverage (period-attributed, capped at
/// <c>DepositDate ≤ asOf</c>, no double-count on a split challan, cancelled-deposit drop), the Σ relationship
/// (Σ R1 ≥ <see cref="TdsDepositService.OutstandingPayable"/>, Σ R5 ≥ <see cref="TcsDepositService.OutstandingPayable"/>
/// — equal in the normal one-liability-per-section regime, strictly greater under multi-section over-deposit where
/// the netted ledger figure under-reports), the below-threshold "not deducted/collected" projection with the binding
/// threshold limb, no-PAN detection, and deterministic ordering.
/// </summary>
public class TdsTcsExceptionReportsTests
{
    private const string ValidTan = "MUMA12345B";
    private const string VendorPan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly Fy2025 = new(2025, 4, 1);

    // =====================================================================================================
    //  Pure helpers: StatutoryDueDate, CalendarMonthsSpanned, LateMonths, LateInterest
    // =====================================================================================================

    [Fact]
    public void StatutoryDueDate_is_7th_of_next_month_and_30_april_for_march()
    {
        Assert.Equal(new DateOnly(2025, 5, 7), StatutoryInterest.StatutoryDueDate(new DateOnly(2025, 4, 30)));
        Assert.Equal(new DateOnly(2026, 1, 7), StatutoryInterest.StatutoryDueDate(new DateOnly(2025, 12, 15)));
        // A March deduction (non-government deductor): due 30-April of the same year.
        Assert.Equal(new DateOnly(2025, 4, 30), StatutoryInterest.StatutoryDueDate(new DateOnly(2025, 3, 20)));
        Assert.Equal(new DateOnly(2025, 4, 30), StatutoryInterest.StatutoryDueDate(new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void CalendarMonthsSpanned_counts_part_month_as_full_floored_at_one()
    {
        // The verified example: deduct 30-Apr, deposit 10-Jun ⇒ 3 calendar months.
        Assert.Equal(3, StatutoryInterest.CalendarMonthsSpanned(new DateOnly(2025, 4, 30), new DateOnly(2025, 6, 10)));
        // Same month ⇒ 1 (part of a month is a full month).
        Assert.Equal(1, StatutoryInterest.CalendarMonthsSpanned(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31)));
        // to before from ⇒ 0.
        Assert.Equal(0, StatutoryInterest.CalendarMonthsSpanned(new DateOnly(2025, 6, 1), new DateOnly(2025, 5, 1)));
        // Cross-year.
        Assert.Equal(3, StatutoryInterest.CalendarMonthsSpanned(new DateOnly(2025, 12, 5), new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void LateMonths_is_zero_below_due_date_and_counts_from_deduction_when_late()
    {
        // Deduct 10-Apr, due 7-May: a 5-May deposit is on time ⇒ 0 late months.
        Assert.Equal(0, StatutoryInterest.LateMonths(new DateOnly(2025, 4, 10), new DateOnly(2025, 5, 5)));
        // Exactly on the due date is still on time.
        Assert.Equal(0, StatutoryInterest.LateMonths(new DateOnly(2025, 4, 10), new DateOnly(2025, 5, 7)));
        // Deduct 30-Apr, deposit 10-Jun (past 7-May due date) ⇒ 3 months, counted FROM the deduction date.
        Assert.Equal(3, StatutoryInterest.LateMonths(new DateOnly(2025, 4, 30), new DateOnly(2025, 6, 10)));
    }

    // =====================================================================================================
    //  TDS fixtures
    // =====================================================================================================

    private static Company NewTdsCompany(DateOnly booksFrom)
    {
        var c = CompanyFactory.CreateSeeded("Exception Co", booksFrom);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c, string name = "Consultant", string? pan = VendorPan)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = pan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    /// <summary>Books a 194J(b) deduction of <paramref name="gross"/> to <paramref name="vendor"/> dated
    /// <paramref name="on"/>. Above ₹50,000 (single or cumulative) it withholds 10%; below, it posts full gross
    /// (TDS 0) so the Not-Deducted projection still sees it.</summary>
    private static TdsService.CarveOut BookDeduction(Company c, DateOnly on, Domain.Ledger vendor, decimal gross)
    {
        var fees = AddLedger(c, $"Professional Fees {Guid.NewGuid():N}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var g = Money.FromRupees(gross);
        var carve = new TdsService(c).BuildCarveOut(g, g, nop, vendor, on);
        var lines = new List<EntryLine> { new(fees.Id, g, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is not null) lines.Add(carve.TdsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on, lines));
        return carve;
    }

    private static Voucher DepositTds(Company c, Money amount, DateOnly on, string challanNo, string section = "194J(b)")
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, section, "200", posted);
        return posted;
    }

    // =====================================================================================================
    //  R1 TDS Outstanding + Σ-invariant
    // =====================================================================================================

    [Fact]
    public void R1_outstanding_is_deducted_minus_covered_and_ties_to_the_payable_ledger()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);          // TDS 10,000
        DepositTds(c, Money.FromRupees(6_000m), new DateOnly(2025, 6, 5), "AA"); // deposit 6,000 of it
        var asOf = new DateOnly(2025, 6, 30);

        var r1 = Report.BuildTdsOutstanding(c, asOf);
        var row = Assert.Single(r1.Rows);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal(Money.FromRupees(10_000m), row.Deducted);
        Assert.Equal(Money.FromRupees(6_000m), row.Deposited);
        Assert.Equal(Money.FromRupees(4_000m), row.Outstanding);

        // The Σ-invariant: total outstanding equals the "TDS Payable" ledger balance.
        Assert.Equal(new TdsDepositService(c).OutstandingPayable(asOf), r1.TotalOutstanding);
        Assert.Equal(Money.FromRupees(4_000m), r1.TotalOutstanding);
    }

    [Fact]
    public void R1_cancelled_stat_payment_restores_the_outstanding_and_keeps_the_invariant()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);
        var deposit = DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "AA");
        var asOf = new DateOnly(2025, 6, 30);

        // Fully deposited ⇒ nothing outstanding.
        Assert.Equal(Money.Zero, Report.BuildTdsOutstanding(c, asOf).TotalOutstanding);

        // Cancel the Stat-Payment ⇒ the challan drops from "deposited" and the liability is restored.
        c.FindVoucher(deposit.Id)!.Cancelled = true;
        var r1 = Report.BuildTdsOutstanding(c, asOf);
        Assert.Equal(Money.FromRupees(10_000m), r1.TotalOutstanding);
        Assert.Equal(Money.Zero, Assert.Single(r1.Rows).Deposited);
        Assert.Equal(new TdsDepositService(c).OutstandingPayable(asOf), r1.TotalOutstanding);
    }

    [Fact]
    public void R1_coverage_does_not_double_count_a_split_challan()
    {
        // Two ₹10,000 deductions; a ₹15,000 then a ₹5,000 challan. FIFO: D1 covered fully, D2 across both challans.
        // Total covered must be ₹20,000 (never ₹30,000), outstanding 0.
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);   // D1 ⇒ 10,000
        BookDeduction(c, new DateOnly(2025, 5, 11), vendor, 1_00_000m);   // D2 ⇒ 10,000
        DepositTds(c, Money.FromRupees(15_000m), new DateOnly(2025, 6, 5), "AA");
        DepositTds(c, Money.FromRupees(5_000m), new DateOnly(2025, 6, 6), "BB");
        var asOf = new DateOnly(2025, 6, 30);

        var r1 = Report.BuildTdsOutstanding(c, asOf);
        var row = Assert.Single(r1.Rows);
        Assert.Equal(Money.FromRupees(20_000m), row.Deducted);
        Assert.Equal(Money.FromRupees(20_000m), row.Deposited);   // not 30,000
        Assert.Equal(Money.Zero, row.Outstanding);
    }

    // =====================================================================================================
    //  R3 TDS Interest u/s 201(1A) — 1.5%/month late-deposit limb
    // =====================================================================================================

    [Fact]
    public void R3_interest_is_one_point_five_percent_per_month_over_three_months()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 4, 30), vendor, 1_00_000m);           // TDS 10,000, due 7-May
        DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 10), "AA"); // late by 3 months
        var asOf = new DateOnly(2025, 6, 30);

        var r3 = Report.BuildTdsInterest201(c, asOf);
        var row = Assert.Single(r3.Rows);
        Assert.Equal(Money.FromRupees(10_000m), row.Tds);
        Assert.Equal(new DateOnly(2025, 4, 30), row.DeductionDate);
        Assert.Equal(new DateOnly(2025, 6, 10), row.DepositDate);
        Assert.Equal(new DateOnly(2025, 5, 7), row.DueDate);
        Assert.Equal(3, row.Months);
        Assert.Equal(Money.FromRupees(450m), row.Interest);        // 10,000 × 1.5% × 3
        Assert.Equal(Money.FromRupees(450m), r3.TotalInterest);
        Assert.False(string.IsNullOrWhiteSpace(TdsInterestReport.Footnote));
    }

    [Fact]
    public void R3_a_below_due_date_deposit_accrues_no_interest()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 4, 10), vendor, 1_00_000m);           // due 7-May
        DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 5, 5), "AA");  // deposited before due date
        var asOf = new DateOnly(2025, 5, 31);

        Assert.Empty(Report.BuildTdsInterest201(c, asOf).Rows);
    }

    [Fact]
    public void R3_an_undeposited_deduction_accrues_interest_to_the_as_of_date()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 4, 30), vendor, 1_00_000m);   // due 7-May, never deposited
        var asOf = new DateOnly(2025, 6, 30);

        var r3 = Report.BuildTdsInterest201(c, asOf);
        var row = Assert.Single(r3.Rows);
        Assert.Null(row.DepositDate);                       // undeposited
        Assert.Equal(3, row.Months);                        // 30-Apr → 30-Jun (asOf)
        Assert.Equal(Money.FromRupees(450m), row.Interest);
    }

    // =====================================================================================================
    //  R2 TDS Not Deducted (below threshold)
    // =====================================================================================================

    [Fact]
    public void R2_lists_a_below_threshold_assessment_with_cumulative_threshold_and_shortfall()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 20_000m);   // 194J(b) cumulative ₹50,000 ⇒ TDS 0
        var asOf = new DateOnly(2025, 6, 30);

        var r2 = Report.BuildTdsNotDeducted(c, asOf);
        var row = Assert.Single(r2.Rows);
        Assert.Equal(new DateOnly(2025, 5, 10), row.Date);
        Assert.Equal("Consultant", row.Party);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal(Money.FromRupees(20_000m), row.Assessable);
        Assert.Equal(Money.FromRupees(20_000m), row.CumulativeInFy);
        Assert.Equal(Money.FromRupees(50_000m), row.Threshold);
        Assert.Equal(Money.FromRupees(30_000m), row.Shortfall);
    }

    [Fact]
    public void R2_excludes_a_deduction_that_crossed_the_threshold()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);   // crosses ⇒ TDS withheld, not a R2 row
        Assert.Empty(Report.BuildTdsNotDeducted(c, new DateOnly(2025, 6, 30)).Rows);
    }

    // =====================================================================================================
    //  R4 TDS Nature-of-Payment summary
    // =====================================================================================================

    [Fact]
    public void R4_aggregates_by_section_including_below_threshold_count()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);   // deducted 10,000
        BookDeduction(c, new DateOnly(2025, 5, 12), Vendor(c, "SmallCo"), 20_000m); // below threshold ⇒ 0
        DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "AA");
        var asOf = new DateOnly(2025, 6, 30);

        var r4 = Report.BuildTdsNatureSummary(c, asOf);
        var row = Assert.Single(r4.Rows);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal(Money.FromRupees(1_20_000m), row.Assessable);   // both assessable bases
        Assert.Equal(Money.FromRupees(10_000m), row.Deducted);
        Assert.Equal(Money.FromRupees(10_000m), row.Deposited);
        Assert.Equal(Money.Zero, row.Outstanding);
        Assert.Equal(1, row.BelowThresholdCount);
    }

    // =====================================================================================================
    //  R9 Ledgers / parties without PAN
    // =====================================================================================================

    [Fact]
    public void R9_flags_a_no_pan_deductee_by_pan_applied_false()
    {
        var c = NewTdsCompany(Fy2025);
        var noPan = Vendor(c, "No-PAN Vendor", pan: null);              // §206AA no-PAN rate applies
        BookDeduction(c, new DateOnly(2025, 5, 10), noPan, 1_00_000m);  // withheld at 20% ⇒ PanApplied false
        var asOf = new DateOnly(2025, 6, 30);

        var r9 = Report.BuildLedgersWithoutPan(c, asOf);
        var row = Assert.Single(r9.Rows);
        Assert.Equal("No-PAN Vendor", row.Party);
        Assert.False(row.PanPresent);
        Assert.Contains("194J(b)", row.Codes);
        Assert.Equal(Money.FromRupees(20_000m), row.TaxAtNoPanRate);   // 20% of 1,00,000
    }

    [Fact]
    public void R9_excludes_a_party_that_has_a_valid_pan()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);                                         // valid PAN
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m);
        Assert.Empty(Report.BuildLedgersWithoutPan(c, new DateOnly(2025, 6, 30)).Rows);
    }

    // =====================================================================================================
    //  Ordering determinism
    // =====================================================================================================

    [Fact]
    public void R3_rows_are_ordered_deterministically_by_section_then_date()
    {
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        // Two late, undeposited deductions booked out of date order.
        BookDeduction(c, new DateOnly(2025, 6, 1), vendor, 1_00_000m);
        BookDeduction(c, new DateOnly(2025, 4, 30), vendor, 1_00_000m);
        var asOf = new DateOnly(2025, 8, 31);

        var rows = Report.BuildTdsInterest201(c, asOf).Rows;
        Assert.Equal(2, rows.Count);
        // Sorted by section then deduction date: 30-Apr before 1-Jun.
        Assert.Equal(new DateOnly(2025, 4, 30), rows[0].DeductionDate);
        Assert.Equal(new DateOnly(2025, 6, 1), rows[1].DeductionDate);
    }

    // =====================================================================================================
    //  TCS fixtures + reports (mirror)
    // =====================================================================================================

    private static Company NewTcsCompany(DateOnly booksFrom)
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", booksFrom);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = booksFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private sealed record TcsScene(Company C, StockItem Scrap, Domain.Ledger Sales, Domain.Ledger Buyer, Guid Main);

    private static TcsScene BuildTcsScene(Company c, string buyerName = "Scrap Buyer", string? pan = BuyerPan)
    {
        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, buyerName, "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = pan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            c.BooksBeginFrom,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        return new TcsScene(c, scrap, sales, buyer, main);
    }

    /// <summary>Posts a scrap sale (₹1,00,000 + 18% GST ⇒ 1% TCS on ₹1,18,000 = ₹1,180) dated <paramref name="on"/>.</summary>
    private static void BookScrapSale(TcsScene s, DateOnly on)
    {
        var c = s.C;
        var gst = new GstService(c);
        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(s.Buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(s.Scrap, s.Sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, s.Buyer, on);

        // Above threshold: Dr Party = value + GST + TCS, and add the TCS-Payable credit leg. Below threshold: no TCS
        // is collected — Dr Party = value + GST, and the detail (TCS 0) rides the party leg for the Not-Collected report.
        var buyerDebit = col.Applies
            ? new EntryLine(s.Buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit)
            : new EntryLine(s.Buyer.Id, value + tax.TotalTax, DrCr.Debit, tcs: col.Detail);
        var lines = new List<EntryLine> { buyerDebit, new(s.Sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        if (col.TcsPayableLine is not null) lines.Add(col.TcsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(s.Scrap.Id, s.Main, 1000m, Money.FromRupees(100m)) }));
    }

    private static Voucher DepositTcs(Company c, Money amount, DateOnly on, string challanNo, string code = "6CE")
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, code, "200", posted);
        return posted;
    }

    [Fact]
    public void R5_tcs_outstanding_ties_to_the_tcs_payable_ledger()
    {
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        BookScrapSale(s, new DateOnly(2025, 5, 10));                                  // collects ₹1,180
        DepositTcs(s.C, Money.FromRupees(1_000m), new DateOnly(2025, 6, 5), "AA");    // deposit ₹1,000 of it
        var asOf = new DateOnly(2025, 6, 30);

        var r5 = Report.BuildTcsOutstanding(s.C, asOf);
        var row = Assert.Single(r5.Rows);
        Assert.Equal("6CE", row.Code);
        Assert.Equal(Money.FromRupees(1_180m), row.Collected);
        Assert.Equal(Money.FromRupees(1_000m), row.Deposited);
        Assert.Equal(Money.FromRupees(180m), row.Outstanding);
        Assert.Equal(new TcsDepositService(s.C).OutstandingPayable(asOf), r5.TotalOutstanding);
    }

    [Fact]
    public void R7_tcs_interest_is_one_percent_per_month_over_three_months()
    {
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        BookScrapSale(s, new DateOnly(2025, 4, 30));                                    // collects ₹1,180, due 7-May
        DepositTcs(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 10), "AA");     // 3 months late
        var asOf = new DateOnly(2025, 6, 30);

        var r7 = Report.BuildTcsInterest206C7(s.C, asOf);
        var row = Assert.Single(r7.Rows);
        Assert.Equal(Money.FromRupees(1_180m), row.Tcs);
        Assert.Equal(3, row.Months);
        // 1,180 × 1% × 3 = 35.40 ⇒ nearest rupee (half-up) = 35.
        Assert.Equal(Money.FromRupees(35m), row.Interest);
        Assert.Equal(Money.FromRupees(35m), r7.TotalInterest);
    }

    [Fact]
    public void R6_lists_a_below_threshold_collection()
    {
        // §206C(1F) motor-vehicle: threshold ₹10,00,000 — a small scrap sale is below the 6CL threshold. Use a
        // motor-vehicle nature on a small sale so TCS is 0 and it lands in Not-Collected.
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        s.Scrap.TcsNatureOfGoodsId = s.C.FindNatureOfGoodsByCode("6CL")!.Id;   // ₹10,00,000 threshold
        BookScrapSale(s, new DateOnly(2025, 5, 10));                            // ₹1,18,000 < ₹10,00,000 ⇒ TCS 0
        var asOf = new DateOnly(2025, 6, 30);

        var r6 = Report.BuildTcsNotCollected(s.C, asOf);
        var row = Assert.Single(r6.Rows);
        Assert.Equal("6CL", row.Code);
        Assert.Equal(Money.FromRupees(10_00_000m), row.Threshold);
        Assert.True(row.Assessable.Amount > 0m);
    }

    [Fact]
    public void R8_tcs_nature_summary_aggregates_by_collection_code()
    {
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        DepositTcs(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "AA");
        var asOf = new DateOnly(2025, 6, 30);

        var r8 = Report.BuildTcsNatureSummary(s.C, asOf);
        var row = Assert.Single(r8.Rows);
        Assert.Equal("6CE", row.Code);
        Assert.Equal(Money.FromRupees(1_180m), row.Collected);
        Assert.Equal(Money.FromRupees(1_180m), row.Deposited);
        Assert.Equal(Money.Zero, row.Outstanding);
    }

    [Fact]
    public void R5_and_R6_are_empty_for_a_company_with_no_tcs()
    {
        var c = NewTcsCompany(Fy2025);
        var asOf = new DateOnly(2025, 6, 30);
        Assert.Empty(Report.BuildTcsOutstanding(c, asOf).Rows);
        Assert.Empty(Report.BuildTcsNotCollected(c, asOf).Rows);
        Assert.Empty(Report.BuildTcsInterest206C7(c, asOf).Rows);
        Assert.Equal(Money.Zero, Report.BuildTcsOutstanding(c, asOf).TotalOutstanding);
    }

    // =====================================================================================================
    //  Adversarial-review regressions (Phase 7 slice 8 fix pass)
    // =====================================================================================================

    private static Domain.Ledger Vendor194C(Company c, string name, string? pan = VendorPan)
    {
        var nop = c.FindNatureOfPaymentByCode("194C")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Individual; v.PartyPan = pan;
        return v;
    }

    /// <summary>Books a §194C contract payment of <paramref name="gross"/> to <paramref name="vendor"/> (1% with-PAN
    /// Ind/HUF rate). Above the single ₹30,000 or cumulative ₹1,00,000 limb it withholds; below, it posts full gross
    /// (TDS 0) so the Not-Deducted projection still sees it.</summary>
    private static void BookDeduction194C(Company c, DateOnly on, Domain.Ledger vendor, decimal gross)
    {
        var work = AddLedger(c, $"Contract Work {Guid.NewGuid():N}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194C")!;
        var g = Money.FromRupees(gross);
        var carve = new TdsService(c).BuildCarveOut(g, g, nop, vendor, on);
        var lines = new List<EntryLine> { new(work.Id, g, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is not null) lines.Add(carve.TdsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on, lines));
    }

    /// <summary>Posts a §206C(1F)/6CL motor-vehicle sale of <paramref name="value"/> (+18% GST) — far below the
    /// ₹10,00,000 single gate ⇒ ₹0 collected — dated <paramref name="on"/>.</summary>
    private static void BookMotorSale(TcsScene s, DateOnly on, decimal value)
    {
        var c = s.C;
        var gst = new GstService(c);
        var v = Money.FromRupees(value);
        var interState = gst.IsInterState(s.Buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(v, 1800) }, interState, GstTaxDirection.Output);
        var nature = c.FindNatureOfGoodsByCode("6CL")!;
        var col = new TcsService(c).BuildCollection(v, tax.TotalTax, nature, s.Buyer, on);
        var buyerDebit = col.Applies
            ? new EntryLine(s.Buyer.Id, v + tax.TotalTax + col.TcsAmount, DrCr.Debit)
            : new EntryLine(s.Buyer.Id, v + tax.TotalTax, DrCr.Debit, tcs: col.Detail);
        var lines = new List<EntryLine> { buyerDebit, new(s.Sales.Id, v, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        if (col.TcsPayableLine is not null) lines.Add(col.TcsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(s.Scrap.Id, s.Main, 100m, Money.FromRupees(2000m)) }));
    }

    // ---- F1: R6/R2 shortfall must match the engine's per-nature ThresholdCrossed logic ----

    [Fact]
    public void R6_single_transaction_nature_shortfall_is_per_line_not_fy_cumulative()
    {
        // §206C(1F)/6CL is a PER-SINGLE-TRANSACTION ₹10,00,000 gate. Five ₹2,00,000 (+18% GST ⇒ ₹2,36,000 assessable)
        // motor-vehicle sales are each far below the single gate, so the engine correctly collects ₹0 on every one —
        // even though the FY cumulative (₹11,80,000) crosses ₹10,00,000 by the fifth. The advisory shortfall must
        // stay the per-line gap to the SINGLE threshold (₹10,00,000 − ₹2,36,000 = ₹7,64,000), never the cumulative
        // gap (which would falsely fall to ₹0 and imply TCS is now due).
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        for (var day = 10; day <= 14; day++)
            BookMotorSale(s, new DateOnly(2025, 5, day), 2_00_000m);
        var asOf = new DateOnly(2025, 6, 30);

        Assert.Empty(Report.BuildTcsOutstanding(s.C, asOf).Rows); // engine collected nothing

        var r6 = Report.BuildTcsNotCollected(s.C, asOf);
        Assert.Equal(5, r6.Rows.Count);
        Assert.All(r6.Rows, row =>
        {
            Assert.Equal("6CL", row.Code);
            Assert.Equal(Money.FromRupees(10_00_000m), row.Threshold);
            Assert.Equal(Money.FromRupees(2_36_000m), row.Assessable);
            Assert.Equal(Money.FromRupees(7_64_000m), row.Shortfall); // per-line single-transaction gap, constant
        });
        var last = r6.Rows[^1];
        Assert.True(last.CumulativeInFy.Amount > 10_00_000m);          // FY cumulative HAS crossed the threshold …
        Assert.Equal(Money.FromRupees(7_64_000m), last.Shortfall);    // … yet the shortfall is still per-line
    }

    [Fact]
    public void R2_surfaces_the_binding_threshold_limb_for_a_two_limb_section()
    {
        // §194C carries BOTH a single-transaction (₹30,000) and a cumulative-FY (₹1,00,000) limb; the engine deducts
        // once EITHER is crossed. R2 must surface the limb NEAREST to triggering, not silently drop the single one.
        var c = NewTdsCompany(Fy2025);

        // Vendor A — a single ₹28,000 contract payment: below both limbs, ₹2,000 short of the SINGLE ₹30,000 gate.
        var a = Vendor194C(c, "Contractor A");
        BookDeduction194C(c, new DateOnly(2025, 5, 10), a, 28_000m);

        // Vendor B — ₹75,000 of prior payments then a small ₹5,000 one: the FY aggregate (₹80,000) is now only
        // ₹20,000 short of the CUMULATIVE ₹1,00,000 gate, nearer than the ₹25,000 single gap, so the cumulative binds.
        var b = Vendor194C(c, "Contractor B");
        BookDeduction194C(c, new DateOnly(2025, 5, 1), b, 25_000m);
        BookDeduction194C(c, new DateOnly(2025, 5, 2), b, 25_000m);
        BookDeduction194C(c, new DateOnly(2025, 5, 3), b, 25_000m);
        BookDeduction194C(c, new DateOnly(2025, 5, 20), b, 5_000m);
        var asOf = new DateOnly(2025, 6, 30);

        var r2 = Report.BuildTdsNotDeducted(c, asOf);

        var rowA = Assert.Single(r2.Rows, r => r.Party == "Contractor A");
        Assert.Equal(Money.FromRupees(30_000m), rowA.Threshold);   // the SINGLE limb (nearest), not ₹1,00,000
        Assert.Equal(Money.FromRupees(2_000m), rowA.Shortfall);

        var rowB = Assert.Single(r2.Rows, r => r.Party == "Contractor B" && r.Assessable == Money.FromRupees(5_000m));
        Assert.Equal(Money.FromRupees(1_00_000m), rowB.Threshold);  // the CUMULATIVE limb binds here
        Assert.Equal(Money.FromRupees(20_000m), rowB.Shortfall);
    }

    // ---- F2: Σ R1/R5 ≥ OutstandingPayable (the netted ledger figure under-reports under multi-section over-deposit) ----

    [Fact]
    public void R1_sum_can_exceed_the_netted_payable_under_multi_section_over_deposit()
    {
        // 194J ₹10,000 + 194C ₹10,000 both owed; a single ₹20,000 challan booked against 194C over-deposits that
        // section and leaves 194J untouched. Section-aware R1 correctly still shows 194J ₹10,000 outstanding
        // (Σ = ₹10,000). The single "TDS Payable" ledger, however, nets Cr 20,000 − Dr 20,000 → 0, so the aggregate
        // OutstandingPayable UNDER-reports (₹0). Hence Σ R1 ≥ OutstandingPayable (equal only in the normal
        // one-liability-per-section regime); see the TdsOutstandingReport carry-forward note.
        var c = NewTdsCompany(Fy2025);
        BookDeduction(c, new DateOnly(2025, 5, 10), Vendor(c, "J Consultant"), 1_00_000m);   // 194J(b) ⇒ TDS 10,000
        BookDeduction194C(c, new DateOnly(2025, 5, 11), Vendor194C(c, "K Contractor"), 10_00_000m); // 194C ⇒ 10,000
        DepositTds(c, Money.FromRupees(20_000m), new DateOnly(2025, 6, 5), "CC", section: "194C"); // over-deposits 194C
        var asOf = new DateOnly(2025, 6, 30);

        var r1 = Report.BuildTdsOutstanding(c, asOf);
        Assert.Equal(Money.FromRupees(10_000m), Assert.Single(r1.Rows, r => r.Section == "194J(b)").Outstanding);
        Assert.Equal(Money.Zero, Assert.Single(r1.Rows, r => r.Section == "194C").Outstanding); // covered; surplus unused
        Assert.Equal(Money.FromRupees(10_000m), r1.TotalOutstanding);

        var netted = new TdsDepositService(c).OutstandingPayable(asOf);
        Assert.Equal(Money.Zero, netted);                            // the ledger-level figure under-reports
        Assert.True(r1.TotalOutstanding.Amount >= netted.Amount);    // the honest relationship
    }

    [Fact]
    public void R5_sum_can_exceed_the_netted_payable_under_multi_code_over_deposit()
    {
        // The TCS mirror of the R1 case: 6CE ₹1,180 + 6CA ₹1,180 both owed; a single ₹2,360 challan against 6CE
        // over-deposits it. Section-aware R5 still shows 6CA ₹1,180 outstanding while the netted "TCS Payable"
        // ledger reads ₹0 — so Σ R5 ≥ OutstandingPayable.
        var s = BuildTcsScene(NewTcsCompany(Fy2025));
        BookScrapSale(s, new DateOnly(2025, 5, 10));                          // 6CE collects ₹1,180

        // A synthetic 6CA collection of ₹1,180 (a second §206C code) posted directly against "TCS Payable".
        var nature6ca = s.C.FindNatureOfGoodsByCode("6CA")!;
        var payable = new TcsService(s.C).RequirePayableLedger();
        var detail = new TcsLineTax(nature6ca.Id, "6CA", Money.FromRupees(1_18_000m), 100, Money.FromRupees(1_180m), s.Buyer.Id, true);
        new LedgerService(s.C).Post(new Voucher(Guid.NewGuid(), JournalTypeId(s.C), new DateOnly(2025, 5, 11), new[]
        {
            new EntryLine(s.Buyer.Id, Money.FromRupees(1_180m), DrCr.Debit),
            new EntryLine(payable.Id, Money.FromRupees(1_180m), DrCr.Credit, tcs: detail),
        }));

        DepositTcs(s.C, Money.FromRupees(2_360m), new DateOnly(2025, 6, 5), "AA", code: "6CE"); // over-deposits 6CE
        var asOf = new DateOnly(2025, 6, 30);

        var r5 = Report.BuildTcsOutstanding(s.C, asOf);
        Assert.Equal(Money.Zero, Assert.Single(r5.Rows, r => r.Code == "6CE").Outstanding);
        Assert.Equal(Money.FromRupees(1_180m), Assert.Single(r5.Rows, r => r.Code == "6CA").Outstanding);
        Assert.Equal(Money.FromRupees(1_180m), r5.TotalOutstanding);

        var netted = new TcsDepositService(s.C).OutstandingPayable(asOf);
        Assert.Equal(Money.Zero, netted);
        Assert.True(r5.TotalOutstanding.Amount >= netted.Amount);
    }

    // ---- F3: R2/R6 need a unique, intrinsic final tie-break so equal-key rows are byte-stable ----

    [Fact]
    public void R2_orders_equal_key_rows_by_a_stable_intrinsic_tie_break()
    {
        // Two below-threshold §194J payments to the SAME vendor on the SAME day differ only in their assessable.
        // Without a unique final tie-break the sort is unstable; the fix orders equal (date, party, section) rows by
        // an intrinsic key (party id, then assessable), independent of the physical voucher order.
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 20_000m);   // inserted FIRST, larger
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 15_000m);   // inserted SECOND, smaller
        var asOf = new DateOnly(2025, 6, 30);

        var rows = Report.BuildTdsNotDeducted(c, asOf).Rows;
        Assert.Equal(2, rows.Count);
        // Ascending by assessable — the smaller (inserted second) sorts first, beating insertion order.
        Assert.Equal(Money.FromRupees(15_000m), rows[0].Assessable);
        Assert.Equal(Money.FromRupees(20_000m), rows[1].Assessable);
    }

    // ---- F4: R9 must be internally consistent with its "without PAN" title ----

    [Fact]
    public void R9_excludes_a_party_charged_the_no_pan_rate_that_has_since_added_a_pan()
    {
        // A vendor charged the §206AA no-PAN higher rate earlier in the FY (PanApplied == false) who has SINCE
        // recorded a valid PAN is no longer "without PAN": it must drop out of R9, whose title is exactly that.
        var c = NewTdsCompany(Fy2025);
        var vendor = Vendor(c, "Late-PAN Vendor", pan: null);           // no PAN at the time of the deduction
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor, 1_00_000m); // withheld at 20% ⇒ PanApplied false
        var asOf = new DateOnly(2025, 6, 30);

        Assert.Contains(Report.BuildLedgersWithoutPan(c, asOf).Rows, r => r.Party == "Late-PAN Vendor");

        vendor.PartyPan = VendorPan;                                    // records a valid PAN ⇒ now WITH PAN
        Assert.DoesNotContain(Report.BuildLedgersWithoutPan(c, asOf).Rows, r => r.Party == "Late-PAN Vendor");
    }
}
