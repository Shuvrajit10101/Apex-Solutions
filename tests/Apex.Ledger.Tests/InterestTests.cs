using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Interest Calculation tests (catalog §7; plan.md §5): ledger interest parameters (Rate / Per / On /
/// Applicability / Calculate-From / Rounding / Style) and the pure interest projection over a period —
/// simple interest on a 365-day and a 30-day (360) basis; Debit-only vs Credit-only filtering; PostDue
/// accruing only after the bill due date; rounding; compound capitalisation; and the report shape/total.
/// </summary>
public class InterestTests
{
    // A loan ledger sitting on the credit side and a customer on the debit side, both under groups whose
    // nature we can rely on. Cash funds the postings.
    private static Company Seed(
        out Domain.Ledger loan,
        out Domain.Ledger customer,
        out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("Interest Co", new DateOnly(2024, 1, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        // Loan (a liability, credit balance) under "Loans (Liability)".
        var loansGroup = c.FindGroupByName("Loans (Liability)")!;
        loan = new Domain.Ledger(Guid.NewGuid(), "Bank Loan", loansGroup.Id, Money.Zero, openingIsDebit: false);

        // Customer (an asset, debit balance) under "Sundry Debtors".
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        customer = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", debtors.Id, Money.Zero, openingIsDebit: true);

        c.AddLedger(loan);
        c.AddLedger(customer);
        return c;
    }

    private static void Post(Company c, DateOnly date, Domain.Ledger dr, Domain.Ledger cr, decimal amt,
        BillAllocation? drBill = null, BillAllocation? crBill = null)
    {
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(dr.Id, Money.FromRupees(amt), DrCr.Debit,
                billAllocations: drBill is null ? null : new[] { drBill }),
            new EntryLine(cr.Id, Money.FromRupees(amt), DrCr.Credit,
                billAllocations: crBill is null ? null : new[] { crBill }),
        }));
    }

    // -------------------------------------------------------------------- parameter validation

    [Fact]
    public void Interest_parameters_reject_negative_rate_and_negative_decimals()
    {
        Assert.Throws<ArgumentException>(() =>
            new InterestParameters(enabled: true, ratePercent: -1m, per: InterestPer.ThreeSixtyFiveDayYear));
        Assert.Throws<ArgumentException>(() =>
            new InterestParameters(enabled: true, ratePercent: 10m, per: InterestPer.ThreeSixtyFiveDayYear,
                roundingMethod: InterestRoundingMethod.Normal, roundingDecimals: -2));
    }

    [Fact]
    public void Ledger_without_interest_block_is_disabled_and_produces_no_lines()
    {
        var c = Seed(out var loan, out _, out _);
        Assert.False(loan.InterestEnabled);

        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        Assert.Empty(report.Lines);
    }

    // -------------------------------------------------------------------- simple interest, 365 basis

    [Fact]
    public void Simple_interest_on_365_day_basis()
    {
        var c = Seed(out var loan, out _, out var cash);
        // Take a 1,00,000 loan on 2024-01-01 (Cash Dr / Loan Cr). Loan closing = 1,00,000 Cr.
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        // 18% p.a., 365-day basis, on all balances, simple.
        loan.Interest = new InterestParameters(
            enabled: true, ratePercent: 18m, per: InterestPer.ThreeSixtyFiveDayYear);

        // Accrue over exactly 365 days: 2024-01-01 → 2024-12-31 is 365 days.
        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var line = Assert.Single(report.Lines);

        Assert.Equal(365, line.Days);
        Assert.Equal(365, line.Basis);
        Assert.Equal(Money.FromRupees(100000m), line.Principal);
        Assert.False(line.PrincipalIsDebit); // credit balance
        // 100000 × 18% × 365/365 = 18,000.
        Assert.Equal(Money.FromRupees(18000m), line.Interest);
        Assert.Equal(Money.FromRupees(18000m), report.TotalInterest);
    }

    // -------------------------------------------------------------------- simple interest, 30-day (360) basis

    [Fact]
    public void Simple_interest_on_30_day_month_uses_360_basis()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        loan.Interest = new InterestParameters(
            enabled: true, ratePercent: 12m, per: InterestPer.ThirtyDayMonth);

        // 2024-01-01 → 2024-01-31 = 30 days; basis 360; 100000 × 12% × 30/360 = 1,000.
        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var line = Assert.Single(report.Lines);

        Assert.Equal(30, line.Days);
        Assert.Equal(360, line.Basis);
        Assert.Equal(Money.FromRupees(1000m), line.Interest);
    }

    [Fact]
    public void Calendar_year_basis_uses_actual_days_in_the_year()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        // 2024 is a leap year → 366-day basis.
        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarYear);
        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)).Lines);
        Assert.Equal(366, line.Basis);

        // 2025 is not a leap year → 365-day basis.
        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarYear,
            calculateFrom: new DateOnly(2025, 1, 1));
        var line2 = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31)).Lines);
        Assert.Equal(365, line2.Basis);
    }

    [Fact]
    public void Calendar_month_basis_annualises_the_actual_days_in_the_starting_month()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        // January has 31 days → annualised basis = 31 × 12 = 372.
        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarMonth);
        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)).Lines);
        Assert.Equal(372, line.Basis);
        // 100000 × 12% × 30/372.
        Assert.Equal(new Money(100000m * 0.12m * 30m / 372m), line.Interest);
    }

    // -------------------------------------------------------------------- On Debit-only / Credit-only

    [Fact]
    public void CreditOnly_accrues_on_a_credit_balance_but_not_a_debit_one()
    {
        var c = Seed(out var loan, out var customer, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);        // loan = Cr 1,00,000
        Post(c, new DateOnly(2024, 1, 1), customer, cash, 50000m);      // customer = Dr 50,000

        // Credit-only on both ledgers.
        loan.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.CreditOnly);
        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.CreditOnly);

        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        // Only the loan (credit balance) accrues; the customer (debit balance) is filtered out.
        var line = Assert.Single(report.Lines);
        Assert.Equal("Bank Loan", line.LedgerName);
    }

    [Fact]
    public void DebitOnly_accrues_on_a_debit_balance_but_not_a_credit_one()
    {
        var c = Seed(out var loan, out var customer, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);        // loan = Cr
        Post(c, new DateOnly(2024, 1, 1), customer, cash, 50000m);      // customer = Dr

        loan.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly);
        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly);

        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var line = Assert.Single(report.Lines);
        Assert.Equal("Acme Ltd", line.LedgerName);
        Assert.True(line.PrincipalIsDebit);
    }

    // -------------------------------------------------------------------- PostDue

    [Fact]
    public void PostDue_accrues_only_after_the_bill_due_date()
    {
        var c = Seed(out _, out var customer, out var cash);
        customer.MaintainBillByBill = true;

        // A sale on 2024-01-01, due 2024-01-31 (30-day credit): customer Dr 1,00,000 against New-Ref "INV1".
        var newRef = new BillAllocation(BillRefType.NewRef, "INV1", Money.FromRupees(100000m),
            dueDate: new DateOnly(2024, 1, 31));
        Post(c, new DateOnly(2024, 1, 1), customer, cash, 100000m, drBill: newRef);

        // 12% p.a., 365 basis, Debit-only, PostDue.
        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly, applicability: InterestApplicability.PostDue);

        // Report to 2024-03-01. Due date 2024-01-31 → interest starts 2024-02-01 → 2024-03-01 = 29 days.
        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1));
        var line = Assert.Single(report.Lines);

        Assert.Equal("INV1", line.BillReference);
        Assert.Equal(new DateOnly(2024, 2, 1), line.From);       // day after due date
        Assert.Equal(29, line.Days);                              // 2024-02-01 → 2024-03-01
        // 100000 × 12% × 29/365 = 953.4246...
        var expected = 100000m * 0.12m * 29m / 365m;
        Assert.Equal(new Money(expected), line.Interest);
    }

    [Fact]
    public void PostDue_yields_nothing_before_the_due_date()
    {
        var c = Seed(out _, out var customer, out var cash);
        customer.MaintainBillByBill = true;
        var newRef = new BillAllocation(BillRefType.NewRef, "INV1", Money.FromRupees(100000m),
            dueDate: new DateOnly(2024, 1, 31));
        Post(c, new DateOnly(2024, 1, 1), customer, cash, 100000m, drBill: newRef);

        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly, applicability: InterestApplicability.PostDue);

        // Report ends 2024-01-20 — before the due date. No interest yet.
        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20));
        Assert.Empty(report.Lines);
    }

    // -------------------------------------------------------------------- Rounding

    [Fact]
    public void Rounding_normal_to_whole_rupees_is_applied()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        // 953.4246... rounds to 953 (Normal, 0 decimals).
        loan.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            roundingMethod: InterestRoundingMethod.Normal, roundingDecimals: 0);

        // 29-day window: 2024-01-01 → 2024-01-30.
        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 30));
        var line = Assert.Single(report.Lines);
        Assert.Equal(29, line.Days);
        // raw = 100000 × 12% × 29/365 = 953.42...; Normal → 953.
        Assert.Equal(Money.FromRupees(953m), line.Interest);
    }

    [Fact]
    public void Rounding_upward_always_ceils()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);
        loan.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            roundingMethod: InterestRoundingMethod.Upward, roundingDecimals: 0);

        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 30));
        var line = Assert.Single(report.Lines);
        Assert.Equal(Money.FromRupees(954m), line.Interest); // 953.42 → 954
    }

    // -------------------------------------------------------------------- CalculateFrom

    [Fact]
    public void CalculateFrom_delays_the_accrual_start()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        // Calculate from 2024-01-16; report window is the whole of January.
        loan.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            calculateFrom: new DateOnly(2024, 1, 16));

        var report = InterestCalculation.Build(c, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var line = Assert.Single(report.Lines);
        Assert.Equal(new DateOnly(2024, 1, 16), line.From);
        Assert.Equal(15, line.Days); // 2024-01-16 → 2024-01-31
    }

    // -------------------------------------------------------------------- Compound

    [Fact]
    public void Compound_capitalises_more_than_simple_over_the_same_window()
    {
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);

        var simpleParams = new InterestParameters(true, 24m, InterestPer.ThreeSixtyFiveDayYear,
            style: InterestStyle.Simple);
        var compoundParams = new InterestParameters(true, 24m, InterestPer.ThreeSixtyFiveDayYear,
            style: InterestStyle.Compound);

        loan.Interest = simpleParams;
        var simple = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 4, 1)).Lines).Interest.Amount;

        loan.Interest = compoundParams;
        var compound = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 4, 1)).Lines).Interest.Amount;

        Assert.True(compound > simple,
            $"compound {compound} should exceed simple {simple} over a multi-month window");
    }

    // -------------------------------------------------------------------- report shape

    [Fact]
    public void Report_carries_the_period_and_a_row_per_enabled_balance()
    {
        var c = Seed(out var loan, out var customer, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000m);
        Post(c, new DateOnly(2024, 1, 1), customer, cash, 40000m);

        loan.Interest = new InterestParameters(true, 10m, InterestPer.ThreeSixtyFiveDayYear);
        customer.Interest = new InterestParameters(true, 10m, InterestPer.ThreeSixtyFiveDayYear);

        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);
        var report = InterestCalculation.Build(c, from, to);

        Assert.Equal(from, report.From);
        Assert.Equal(to, report.To);
        Assert.Equal(2, report.Lines.Count);
        // Total = 100000×10%×365/365 + 40000×10%×365/365 = 10,000 + 4,000 = 14,000.
        Assert.Equal(Money.FromRupees(14000m), report.TotalInterest);
    }

    [Fact]
    public void Build_rejects_an_inverted_period()
    {
        var c = Seed(out _, out _, out _);
        Assert.Throws<ArgumentException>(() =>
            InterestCalculation.Build(c, new DateOnly(2024, 12, 31), new DateOnly(2024, 1, 1)));
    }
}
