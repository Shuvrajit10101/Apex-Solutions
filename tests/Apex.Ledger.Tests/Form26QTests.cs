using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 4 — <b>Form 26Q</b> (<see cref="Form26Q"/>) as a pure quarterly projection over the posted TDS
/// withholdings + recorded challans. Proves the golden worked example (a 194J ₹1,00,000 deduction, ₹10,000 TDS,
/// plus its stat-payment challan) yields exactly one deductee row + one challan block that reconcile to the "TDS
/// Payable" postings and whose control totals tally; that a <b>cross-FY</b> deposit is attributed to the
/// <b>deduction</b> quarter (never double-windowed); and ER-13 (a non-TDS / empty quarter yields an empty return).
/// </summary>
public class Form26QTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewTdsCompany(DateOnly booksFrom)
    {
        var c = CompanyFactory.CreateSeeded("Return Co", booksFrom);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
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

    private static Domain.Ledger Vendor(Company c, string name = "Consultant")
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    /// <summary>Books a 194J ₹1,00,000 deduction (₹10,000 TDS) dated <paramref name="on"/>.</summary>
    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor)
    {
        var fees = AddLedger(c, $"Professional Fees {on:yyyyMMdd}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
    }

    /// <summary>Deposits <paramref name="amount"/> via a Stat Payment on <paramref name="on"/> and records + links an
    /// ITNS-281 challan for section 194J(b).</summary>
    private static TdsChallan Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        return dep.RecordChallan(challanNo, "0510308", on, amount, "194J(b)", "200", posted);
    }

    // ---- the golden worked example: one deductee row + one challan block, reconciled, totals tally ----

    [Fact]
    public void Golden_quarter_has_one_deductee_row_and_one_challan_reconciling_to_payable()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);   // Q1 deduction
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123"); // deposited in Q1

        var q1 = Form26Q.Build(c, 2025, 1);

        // Deductor block from F11.
        Assert.Equal(ValidTan, q1.Deductor.Tan);
        Assert.Equal(DeductorType.Company, q1.Deductor.DeductorType);
        Assert.Equal("A. Sharma", q1.Deductor.ResponsiblePersonName);
        Assert.Equal("2025-26", q1.FinancialYearLabel);
        Assert.Equal("Q1", q1.QuarterLabel);

        // Exactly one deductee row: ₹10,000 TDS on ₹1,00,000, FVU code 94J-B, 10%.
        var row = Assert.Single(q1.Deductees);
        Assert.Equal(DeducteePan, row.DeducteePan);
        Assert.Equal("Consultant", row.DeducteeName);
        Assert.Equal("194J(b)", row.SectionCode);
        Assert.Equal("94J-B", row.FvuSectionCode);
        Assert.Equal(new DateOnly(2025, 5, 10), row.DeductionDate);
        Assert.Equal(Money.FromRupees(1_00_000m), row.AmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), row.TdsAmount);
        Assert.Equal(1000, row.RateBasisPoints);
        Assert.Equal(10.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        // Exactly one challan block carrying that deductee row.
        var challan = Assert.Single(q1.Challans);
        Assert.Equal("00123", challan.ChallanNo);
        Assert.Equal("0510308", challan.BsrCode);
        Assert.Equal(Money.FromRupees(10_000m), challan.Amount);
        Assert.Equal("194J(b)", challan.Section);
        Assert.Same(row, Assert.Single(challan.DeducteeRows));

        // Reconciles to the "TDS Payable" credit postings for the quarter, by construction.
        Assert.Equal(Money.FromRupees(10_000m), q1.TotalTdsDeducted);
        Assert.Equal(PayableCreditsInQuarter(c, 2025, 1), q1.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(10_000m), q1.TotalDepositedAsPerChallans);
    }

    [Fact]
    public void Golden_control_totals_tally()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");

        var ct = Form26Q.Build(c, 2025, 1).ControlTotals;
        Assert.Equal(1, ct.DeducteeRecordCount);
        Assert.Equal(1, ct.ChallanRecordCount);
        Assert.Equal(Money.FromRupees(10_000m), ct.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(1_00_000m), ct.TotalAmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), ct.TotalDepositedAsPerChallans);
        Assert.True(ct.Tallies);
        Assert.Empty(ct.Validate());
    }

    [Fact]
    public void Control_totals_flag_an_undeposited_deduction()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        // No deposit at all — the ₹10,000 is undeposited.

        var q1 = Form26Q.Build(c, 2025, 1);
        Assert.Single(q1.Deductees);
        Assert.Empty(q1.Challans);
        var ct = q1.ControlTotals;
        Assert.False(ct.Tallies);
        var problem = Assert.Single(ct.Validate());
        Assert.Contains("undeposited", problem);
    }

    // ---- challan-boundary splits: a portion is counted once, never at full across two challans (Finding 1) ----

    [Fact]
    public void A_challan_boundary_split_across_two_deductions_is_not_double_counted()
    {
        // Two ₹10,000 Q1 deductions; a ₹15,000 challan then a ₹5,000 challan. FIFO: the first challan discharges
        // D1 in full and ₹5,000 of D2; the second discharges D2's remaining ₹5,000. D2 must be counted once at its
        // ₹10,000 (as two ₹5,000 portions), never twice at full ₹10,000.
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);   // D1
        BookDeduction(c, new DateOnly(2025, 5, 11), vendor);   // D2
        Deposit(c, Money.FromRupees(15_000m), new DateOnly(2025, 6, 5), "AA");
        Deposit(c, Money.FromRupees(5_000m), new DateOnly(2025, 6, 6), "BB");

        var q1 = Form26Q.Build(c, 2025, 1);

        Assert.Equal(Money.FromRupees(20_000m), q1.TotalTdsDeducted);
        // Deposited against the quarter's deductions equals the gross deducted — no phantom ₹30,000.
        Assert.Equal(Money.FromRupees(20_000m), q1.TotalTdsDepositedForQuarter);
        var perChallanTds = q1.Challans.Sum(ch => ch.DeducteeRows.Sum(r => r.TdsAmount.Amount));
        Assert.Equal(20_000m, perChallanTds);
        // The assessable amount-paid is attributed once per deduction (opening portion), never double-counted.
        var perChallanPaid = q1.Challans.Sum(ch => ch.DeducteeRows.Sum(r => r.AmountPaid.Amount));
        Assert.Equal(2_00_000m, perChallanPaid);
        Assert.True(q1.ControlTotals.Tallies);
        Assert.Empty(q1.ControlTotals.Validate());
    }

    [Fact]
    public void A_short_deposited_deduction_is_flagged_not_falsely_tallied()
    {
        // A ₹10,000 deduction with only ₹5,000 deposited: the challan block must reflect the ₹5,000 actually
        // covered (not the full ₹10,000), so the return correctly flags ₹5,000 undeposited.
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(5_000m), new DateOnly(2025, 6, 5), "AA");

        var q1 = Form26Q.Build(c, 2025, 1);
        Assert.Equal(Money.FromRupees(5_000m), q1.TotalTdsDepositedForQuarter);
        Assert.False(q1.ControlTotals.Tallies);
        Assert.Contains("undeposited", Assert.Single(q1.ControlTotals.Validate()));
    }

    // ---- the cross-FY carry: a March deduction deposited in April belongs to Q4 (deduction quarter) ----

    [Fact]
    public void Cross_fy_deposit_is_attributed_to_the_deduction_quarter_not_the_deposit_date()
    {
        // Books open in FY2024-25 so a 20-Mar-2025 (Q4) deduction is valid.
        var c = NewTdsCompany(new DateOnly(2024, 4, 1));
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 3, 20), vendor);                 // Q4 FY2024-25
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 4, 7), "00777"); // deposited 7-Apr (next FY window)

        // Q4 FY2024-25: the deduction is here AND the April challan (which covers it) is listed here.
        var q4 = Form26Q.Build(c, 2024, 4);
        var row = Assert.Single(q4.Deductees);
        Assert.Equal(new DateOnly(2025, 3, 20), row.DeductionDate);
        var ch = Assert.Single(q4.Challans);
        Assert.Equal("00777", ch.ChallanNo);
        Assert.Equal(new DateOnly(2025, 4, 7), ch.DepositDate);   // deposited in April but attributed to Q4
        Assert.Same(row, Assert.Single(ch.DeducteeRows));
        Assert.True(q4.ControlTotals.Tallies);

        // Q1 FY2025-26: NOT double-windowed — no deduction and the April challan is NOT re-listed here.
        var q1Next = Form26Q.Build(c, 2025, 1);
        Assert.Empty(q1Next.Deductees);
        Assert.Empty(q1Next.Challans);
        Assert.True(q1Next.IsEmpty);
    }

    // ---- ER-13: a non-TDS / empty return is inert ----

    [Fact]
    public void Empty_return_for_a_company_with_no_deductions()
    {
        var c = NewTdsCompany(FyStart);
        var q1 = Form26Q.Build(c, 2025, 1);
        Assert.True(q1.IsEmpty);
        Assert.Empty(q1.Deductees);
        Assert.Empty(q1.Challans);
        Assert.Equal(Money.Zero, q1.TotalTdsDeducted);
        Assert.True(q1.ControlTotals.Tallies);
    }

    [Fact]
    public void A_deduction_only_appears_in_its_own_quarter()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 8, 15), vendor);  // Q2

        Assert.Empty(Form26Q.Build(c, 2025, 1).Deductees);    // not in Q1
        Assert.Single(Form26Q.Build(c, 2025, 2).Deductees);   // in Q2
        Assert.Empty(Form26Q.Build(c, 2025, 3).Deductees);    // not in Q3
    }

    /// <summary>Σ "TDS Payable" credit postings on non-cancelled vouchers in the quarter window — the ledger figure
    /// the return must reconcile to.</summary>
    private static Money PayableCreditsInQuarter(Company c, int fy, int quarter)
    {
        var (from, to) = Form26Q.QuarterWindow(fy, quarter);
        var payable = new TdsService(c).RequirePayableLedger();
        var sum = 0m;
        foreach (var v in c.Vouchers)
        {
            if (v.Cancelled || v.Date < from || v.Date > to) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == payable.Id && line.Side == DrCr.Credit)
                    sum += line.Amount.Amount;
        }
        return new Money(sum);
    }
}
