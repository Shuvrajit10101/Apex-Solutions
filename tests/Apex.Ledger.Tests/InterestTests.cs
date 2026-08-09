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
        => SeedFrom(new DateOnly(2024, 1, 1), out loan, out customer, out cash);

    /// <summary>The same fixture with an explicit books-begin date, for windows that start earlier.</summary>
    private static Company SeedFrom(
        DateOnly booksBegin,
        out Domain.Ledger loan,
        out Domain.Ledger customer,
        out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("Interest Co", booksBegin);

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
    public void Calendar_month_divisor_is_left_exactly_as_it_shipped_PROVISIONAL_PENDING_T8()
    {
        // ⚠ THE T8 FLIP SITE (2 of 2). The Calendar-Month divisor is work item WF-6 = slice **S3**, which
        // plan.md gates verbatim: "DO NOT MERGE THE CONSTANTS UNTIL T8 LANDS". S1 is WF-4 + WF-5 — the
        // running-balance accrual and the per-SEGMENT resolution of whatever divisor is in force — so it
        // leaves the divisor itself untouched: January 2024 ⇒ 31 × 12 = 372, exactly as before the slice.
        //
        // That ×12 is known to be wrong under BOTH answers to T8 (IV-8b). It is still not S1's to replace,
        // because the two answers prescribe DIFFERENT replacements — per-period ⇒ DaysInMonth (28-31),
        // per-annum ⇒ DaysInYear (365/366) — and neither is a safe interim: 336 is arithmetically NEARER
        // 28 than 365 is, so "hedge toward per-annum" moves the figure further from the corpus-supported
        // answer, not nearer. S3 flips this method's two blocked arms and this expectation together.
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 1, 1), cash, loan, 100000.41m);

        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarMonth);
        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)).Lines);

        Assert.Equal(372, line.Basis);
        Assert.Equal(new Money(100000.41m * 0.12m * 30m / 372m), line.Interest);
        Assert.Equal(967.75m, Paisa(line.Interest.Amount));
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

    // ================================================================================================
    // Phase 10.10 · S1 (WF-4 / WF-5) — IV-7 running-balance accrual + IV-8a per-segment basis.
    //
    // Day convention (pinned, unchanged by this slice): a segment [a, b) is charged b − a days, so a
    // movement dated d creates a boundary AT d and its own accrual is charged from d+1 counted
    // inclusively — which IS TallyPrime's "Always … calculate interest from next day of transaction"
    // [CORPUS-BOOK printed p.118, extracted line 4264].
    // ================================================================================================

    /// <summary>Posts a balanced Dr/Cr journal carrying the Optional (Ctrl+L) / PostDated (Ctrl+T) flags.</summary>
    private static void PostFlagged(Company c, DateOnly date, Domain.Ledger dr, Domain.Ledger cr, decimal amt,
        bool optional = false, bool postDated = false)
    {
        var journal = c.FindVoucherTypeByName("Journal")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(dr.Id, Money.FromRupees(amt), DrCr.Debit),
            new EntryLine(cr.Id, Money.FromRupees(amt), DrCr.Credit),
        }, optional: optional, postDated: postDated));
    }

    private static decimal Paisa(decimal raw) => Math.Round(raw, 2, MidpointRounding.AwayFromZero);

    // ---------------------------------------------------------------- IV-7 · running-balance accrual

    [Fact]
    public void Always_accrues_on_a_bill_raised_inside_the_window_from_a_nil_opening()
    {
        var c = Seed(out _, out var customer, out var cash);
        Assert.Equal(Money.Zero, customer.OpeningBalance);      // nil opening — no balance carried in

        // Invoiced 1,23,456.78 on 10-Apr; the operator runs 01-Apr → 30-Apr.
        Post(c, new DateOnly(2025, 4, 10), customer, cash, 123456.78m);
        customer.Interest = new InterestParameters(true, 18m, InterestPer.ThreeSixtyFiveDayYear);

        var report = InterestCalculation.Build(c, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        var line = Assert.Single(report.Lines);

        // Accrual starts at the transaction date boundary: [10-Apr, 30-Apr) = 20 days.
        Assert.Equal(new DateOnly(2025, 4, 10), line.From);
        Assert.Equal(20, line.Days);
        Assert.Equal(365, line.Basis);
        Assert.True(line.PrincipalIsDebit);
        Assert.Equal(new Money(123456.78m * 0.18m * 20m / 365m), line.Interest);
        Assert.Equal(1217.66m, Paisa(line.Interest.Amount));   // hand-computed, independent of the code
    }

    [Fact]
    public void Always_stops_accruing_on_money_already_repaid()
    {
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 50000.33m);   // carried in: 50,000.33 Dr
        Post(c, new DateOnly(2025, 4, 15), cash, customer, 20000.11m);   // repaid mid-window

        customer.Interest = new InterestParameters(true, 18m, InterestPer.ThreeSixtyFiveDayYear);

        var report = InterestCalculation.Build(c, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        var line = Assert.Single(report.Lines);

        // Two segments: [01-Apr, 15-Apr) = 14 days on 50,000.33 and [15-Apr, 30-Apr) = 15 days on 30,000.22.
        var expected = 50000.33m * 0.18m * 14m / 365m + 30000.22m * 0.18m * 15m / 365m;
        Assert.Equal(29, line.Days);
        Assert.Equal(new Money(expected), line.Interest);
        Assert.Equal(567.13m, Paisa(line.Interest.Amount));    // NOT 715.07 — that is the flat-principal figure

        // The reported Principal is the time-weighted average, so the printed row re-derives itself.
        var weightedAverage = (50000.33m * 14m + 30000.22m * 15m) / 29m;
        Assert.Equal(new Money(weightedAverage), line.Principal);
    }

    [Fact]
    public void Printed_row_reproduces_its_own_interest_from_principal_rate_days_and_basis()
    {
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 99999.33m);
        Post(c, new DateOnly(2025, 4, 12), customer, cash, 1000.63m);    // a second bill mid-window
        Post(c, new DateOnly(2025, 4, 21), cash, customer, 40000.41m);   // a part-payment mid-window

        customer.Interest = new InterestParameters(true, 13.75m, InterestPer.ThreeSixtyFiveDayYear);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines);

        // Three segments: 11 days on 99,999.33 · 9 days on 1,00,999.96 · 9 days on 60,999.55.
        var expected = 99999.33m * 0.1375m * 11m / 365m
                     + 100999.96m * 0.1375m * 9m / 365m
                     + 60999.55m * 0.1375m * 9m / 365m;
        Assert.Equal(new Money(expected), line.Interest);
        Assert.Equal(963.63m, Paisa(line.Interest.Amount));    // 1,092.46 on the flat opening principal

        // An auditor holding only the printed columns must land on the printed interest.
        var reDerived = line.Principal.Amount * (line.RatePercent / 100m) * line.Days / line.Basis;
        Assert.Equal(Paisa(line.Interest.Amount), Paisa(reDerived));
        Assert.True(Math.Abs(reDerived - line.Interest.Amount) < 0.000001m,
            $"re-derived {reDerived} vs printed {line.Interest.Amount}");
    }

    [Fact]
    public void DebitOnly_accrues_on_the_portion_of_the_window_the_balance_is_debit()
    {
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), cash, customer, 10000.07m);   // carried in: 10,000.07 Cr
        Post(c, new DateOnly(2025, 4, 11), customer, cash, 60000.29m);   // flips to 50,000.22 Dr

        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines);

        Assert.True(line.PrincipalIsDebit);
        Assert.Equal(new DateOnly(2025, 4, 11), line.From);
        Assert.Equal(19, line.Days);
        Assert.Equal(new Money(50000.22m * 0.12m * 19m / 365m), line.Interest);
        Assert.Equal(312.33m, Paisa(line.Interest.Amount));
    }

    [Fact]
    public void A_sign_flipping_window_emits_one_line_per_contiguous_same_side_run()
    {
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 20000.51m);   // carried in: 20,000.51 Dr
        Post(c, new DateOnly(2025, 4, 11), cash, customer, 50000.77m);   // flips to 30,000.26 Cr

        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear);

        var lines = InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines;

        // Dr interest (receivable) and Cr interest (payable) are different money and must never net.
        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].PrincipalIsDebit);
        Assert.Equal(10, lines[0].Days);
        Assert.Equal(new Money(20000.51m * 0.12m * 10m / 365m), lines[0].Interest);

        Assert.False(lines[1].PrincipalIsDebit);
        Assert.Equal(new DateOnly(2025, 4, 11), lines[1].From);
        Assert.Equal(19, lines[1].Days);
        Assert.Equal(new Money(30000.26m * 0.12m * 19m / 365m), lines[1].Interest);
    }

    [Fact]
    public void Running_balance_excludes_optional_and_includes_postdated_vouchers()
    {
        var c = Seed(out _, out var customer, out var cash);
        PostFlagged(c, new DateOnly(2025, 4, 12), customer, cash, 99999.99m, optional: true);
        PostFlagged(c, new DateOnly(2025, 4, 12), customer, cash, 40000.13m, postDated: true);

        customer.Interest = new InterestParameters(true, 18m, InterestPer.ThreeSixtyFiveDayYear);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines);

        // Only the post-dated voucher is on the real books; the Optional one never is.
        Assert.Equal(18, line.Days);
        Assert.Equal(new Money(40000.13m * 0.18m * 18m / 365m), line.Interest);
        Assert.Equal(355.07m, Paisa(line.Interest.Amount));   // 1,242.74 if the Optional voucher leaked in
    }

    // ---------------------------------------------------------------- IV-8a · per-segment basis

    [Fact]
    public void Calendar_year_basis_follows_the_year_each_segment_falls_in()
    {
        var c = SeedFrom(new DateOnly(2023, 1, 1), out var loan, out _, out var cash);
        Post(c, new DateOnly(2023, 11, 20), cash, loan, 100000.77m);

        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarYear);

        // 01-Dec-2023 → 31-Jan-2025 = 427 days: 31 in 2023 (365), 366 in leap 2024 (366), 30 in 2025 (365).
        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2023, 12, 1), new DateOnly(2025, 1, 31)).Lines);

        var expected = 100000.77m * 0.12m * 31m / 365m
                     + 100000.77m * 0.12m * 366m / 366m
                     + 100000.77m * 0.12m * 30m / 365m;
        Assert.Equal(427, line.Days);
        Assert.Equal(new Money(expected), line.Interest);
        Assert.Equal(14005.59m, Paisa(line.Interest.Amount));  // NOT 14,038.46 — leap-2024 priced as 365 days

        // ⚠ PINNED, NOT ACCIDENTAL: on a MULTI-BASIS row the printed columns do NOT foot. `Basis` is a
        // single `int` and this row genuinely spans two divisors (365 and 366), so no integer can make
        // Principal × Rate% × Days / Basis reproduce the interest — the exact arithmetic lives in
        // `Segments`, and `InterestLine.Basis` is documented as indicative for exactly this case.
        // An auditor re-deriving from the grid lands ₹32.87 high; that number is asserted here so the day
        // the grid changes (a blended basis, a dropped column, a per-segment drill-down) the test names
        // the decision instead of silently flipping.
        var reDerived = line.Principal.Amount * (line.RatePercent / 100m) * line.Days / line.Basis;
        Assert.Equal(365, line.Basis);                          // the FIRST segment's basis
        Assert.Equal(14038.46m, Paisa(reDerived));
        Assert.Equal(32.87m, Paisa(reDerived) - Paisa(line.Interest.Amount));
        Assert.NotEqual(Paisa(line.Interest.Amount), Paisa(reDerived));

        // Every segment individually DOES foot, which is why Segments is the audit trail.
        Assert.Equal(3, line.Segments.Count);
        Assert.Equal(new[] { 365, 366, 365 }, line.Segments.Select(s => s.Basis));
    }

    [Fact]
    public void Calendar_month_and_calendar_year_must_never_resolve_to_the_same_divisor()
    {
        // T8-INDEPENDENT, and the guard that keeps an unmeasured guess out of the divisor table.
        // "Calendar Month" and "Calendar Year" are two SEPARATE user-selectable styles, persisted
        // (Schema.cs `interest_per`), round-tripped through Io, and defined by the corpus as different
        // conventions: "Calendar Month … Month-wise (28, 29, 30 or 31 Days)" vs "Calendar Year …
        // Year-wise (365 or 366)" [CORPUS-BOOK printed p.117]. If BasisFor ever resolves them to the same
        // number, switching the ledger master's Per moves the report by zero paisa and one of the two
        // options is a lie — which is exactly what an interim "CalendarMonth ⇒ DaysInYear" hedge does.
        //
        // This holds for the shipped ×12 divisor (336/348/360/372 vs 365/366) AND for the corpus
        // per-period table (28-31 vs 365/366). It fails only for a per-annum CalendarMonth arm.
        foreach (var date in new[]
                 {
                     new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1),
                     new DateOnly(2024, 2, 1), new DateOnly(2024, 12, 1),
                 })
        {
            Assert.NotEqual(
                InterestCalculation.BasisFor(InterestPer.CalendarMonth, date),
                InterestCalculation.BasisFor(InterestPer.CalendarYear, date));
        }
    }

    [Fact]
    public void Calendar_month_january_and_february_keep_their_shipped_figures_PROVISIONAL_PENDING_T8()
    {
        // ⚠ THE T8 FLIP SITE (1 of 2). S1 owns WF-4/WF-5 and moves NO Calendar-Month figure: February
        // still divides by 28 × 12 = 336 and January by 31 × 12 = 372, so the two windows still differ
        // by the 10.7% swing IV-8b records. That swing is the defect slice S3 removes once T8 lands; this
        // method exists so the pre-slice numbers are pinned to the paisa in the meantime and S3's flip
        // shows up as a deliberate, reviewed change rather than a silent drift.
        //
        // WHEN T8 LANDS, this becomes one of:
        //   per-period (divisors 28 / 31): ₹12,000.06 vs ₹10,838.77 — still different, and correctly so,
        //     because 28 days IS a whole February;
        //   per-annum  (divisor 365 both): ₹920.55 for both — but see
        //     Calendar_month_and_calendar_year_must_never_resolve_to_the_same_divisor, which that table
        //     cannot satisfy, and which the corpus's own two definitions require.
        var c = Seed(out var loan, out _, out var cash);
        Post(c, new DateOnly(2024, 12, 20), cash, loan, 100000.53m);
        loan.Interest = new InterestParameters(true, 12m, InterestPer.CalendarMonth);

        var february = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1)).Lines);
        var january = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 29)).Lines);

        Assert.Equal(28, february.Days);
        Assert.Equal(28, january.Days);
        Assert.Equal(336, february.Basis);
        Assert.Equal(372, january.Basis);
        Assert.NotEqual(february.Interest, january.Interest);
        Assert.Equal(new Money(100000.53m * 0.12m * 28m / 336m), february.Interest);
        Assert.Equal(new Money(100000.53m * 0.12m * 28m / 372m), january.Interest);
        Assert.Equal(1000.01m, Paisa(february.Interest.Amount));
        Assert.Equal(903.23m, Paisa(january.Interest.Amount));
    }

    // ---------------------------------------------------------------- Compound regression lock

    [Fact]
    public void Compound_with_no_in_window_movement_reproduces_the_month_by_month_figure()
    {
        // GREEN BEFORE AND AFTER. Locks the shipped compound arithmetic to the paisa so the running-balance
        // rewrite cannot silently move it: 24% on 1,00,000.63 Cr, capitalised at each calendar-month boundary.
        var c = SeedFrom(new DateOnly(2023, 1, 1), out var loan, out _, out var cash);
        Post(c, new DateOnly(2023, 12, 20), cash, loan, 100000.63m);
        loan.Interest = new InterestParameters(true, 24m, InterestPer.ThreeSixtyFiveDayYear,
            style: InterestStyle.Compound);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 4, 1)).Lines);

        var p0 = 100000.63m;
        var i1 = p0 * 0.24m * 31m / 365m;              // Jan
        var p1 = p0 + i1;
        var i2 = p1 * 0.24m * 29m / 365m;              // Feb (leap)
        var p2 = p1 + i2;
        var i3 = p2 * 0.24m * 31m / 365m;              // Mar
        Assert.Equal(91, line.Days);
        Assert.Equal(new Money(i1 + i2 + i3), line.Interest);
        Assert.Equal(6103.68m, Paisa(line.Interest.Amount));

        // The PRINTED PRINCIPAL of a compound row is the time-weighted average of the CAPITALISED
        // principal, so it necessarily EXCEEDS the balance the borrower actually owes — here ₹1,02,007.44
        // against a ₹1,00,000.63 loan that never moved. That is a user-visible money column, and it is
        // asserted here because the lock is worthless if it watches only the interest.
        Assert.Equal(new Money((p0 * 31m + p1 * 29m + p2 * 31m) / 91m), line.Principal);
        Assert.Equal(102007.44m, Paisa(line.Principal.Amount));
        Assert.True(line.Principal.Amount > 100000.63m,
            "a compound row's Principal is the capitalised weighted average, not the ledger balance");
    }

    [Fact]
    public void Compound_capitalises_at_month_ends_only_while_a_movement_changes_the_balance()
    {
        // Hand-derived from the rule, not from the implementation: a movement changes the balance the
        // next segment accrues on, but capitalisation happens ONLY at a calendar-month boundary.
        var c = SeedFrom(new DateOnly(2023, 1, 1), out var loan, out _, out var cash);
        Post(c, new DateOnly(2023, 12, 20), cash, loan, 100000.63m);
        Post(c, new DateOnly(2024, 1, 15), cash, loan, 50000.29m);   // borrows more mid-January

        loan.Interest = new InterestParameters(true, 24m, InterestPer.ThreeSixtyFiveDayYear,
            style: InterestStyle.Compound);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1)).Lines);

        var i1 = 100000.63m * 0.24m * 14m / 365m;                    // [01-Jan, 15-Jan): 1,00,000.63
        var i2 = 150000.92m * 0.24m * 17m / 365m;                    // [15-Jan, 01-Feb): 1,50,000.92
        var i3 = (150000.92m + (i1 + i2)) * 0.24m * 29m / 365m;      // [01-Feb, 01-Mar): + January capitalised
        Assert.Equal(60, line.Days);
        Assert.Equal(new Money(i1 + i2 + i3), line.Interest);
        Assert.Equal(5507.09m, Paisa(line.Interest.Amount));

        // Three segments, and the last one accrues on MORE than the ledger balance — that is the capitalisation.
        Assert.Equal(3, line.Segments.Count);
        Assert.True(line.Segments[2].AccrualPrincipal.Amount > 150000.92m);
    }

    // ---------------------------------------------------------------- structural invariants

    [Fact]
    public void Segment_principals_equal_SignedClosing_at_every_boundary()
    {
        // The incremental running-balance fold must be provably identical to the vetted balance function,
        // or a later change to the counting predicate would silently desynchronise the interest report
        // from the Balance Sheet. Deliberately adversarial: an Optional, a PostDated, a Cancelled and a
        // Memorandum voucher all land inside the window.
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 70000.19m);
        PostFlagged(c, new DateOnly(2025, 4, 12), customer, cash, 99999.99m, optional: true);
        PostFlagged(c, new DateOnly(2025, 4, 12), customer, cash, 40000.13m, postDated: true);
        Post(c, new DateOnly(2025, 4, 18), cash, customer, 15000.77m);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var memo = c.FindVoucherTypeByName("Memorandum")!;
        var svc = new LedgerService(c);

        var doomed = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 4, 5), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(88888.11m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(88888.11m), DrCr.Credit),
        });
        svc.Post(doomed);
        svc.Cancel(doomed.Id);

        svc.Post(new Voucher(Guid.NewGuid(), memo.Id, new DateOnly(2025, 4, 20), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(77777.31m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(77777.31m), DrCr.Credit),
        }));

        customer.Interest = new InterestParameters(true, 15m, InterestPer.ThreeSixtyFiveDayYear);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines);

        Assert.NotEmpty(line.Segments);
        foreach (var segment in line.Segments)
            Assert.Equal(
                LedgerBalances.SignedClosing(c, customer, segment.From),
                segment.SignedPrincipal);

        // Only the real voucher set moves the balance: 70,000.19 + 40,000.13 (post-dated) − 15,000.77.
        Assert.Equal(94999.55m, LedgerBalances.SignedClosing(c, customer, new DateOnly(2025, 4, 30)));
    }

    [Fact]
    public void A_line_always_foots_to_the_sum_of_its_segments()
    {
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 88888.29m);
        Post(c, new DateOnly(2025, 4, 9), customer, cash, 11111.83m);    // → 1,00,000.12 (odd paise)
        Post(c, new DateOnly(2025, 4, 23), cash, customer, 33333.13m);   // → 66,666.99

        // A Calendar-Month accrual across a month boundary — the case where the printed Basis alone is
        // not enough and the Segments are the truth. April and May resolve to DIFFERENT divisors, which
        // is what makes this a genuine multi-basis row rather than a four-way split of one basis.
        customer.Interest = new InterestParameters(true, 16.5m, InterestPer.CalendarMonth);

        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 5, 20)).Lines);

        // 1. The EXTERNAL oracle. Hand-computed, not re-derived from the line's own fields: an assertion
        //    that only recomputes the implementation's own numbers survives a wrong basis, a wrong
        //    day-count and a wrong principal alike, because every term moves together.
        //    0.165 × [88,888.29×8/360 + 1,00,000.12×14/360 + 66,666.99×8/360 + 66,666.99×19/372]
        Assert.Equal(1773.87m, Paisa(line.Interest.Amount));
        Assert.Equal(new Money((88888.29m * 8m + 100000.12m * 14m + 66666.99m * 8m + 66666.99m * 19m) / 49m),
            line.Principal);

        // 2. The exact segment shape, stated rather than recomputed: 4 segments, cut at the two movement
        //    dates and at the 01-May month boundary, with April's divisor ≠ May's.
        Assert.Equal(4, line.Segments.Count);
        var expected = new (DateOnly From, DateOnly To, decimal Principal, int Days, int Basis)[]
        {
            (new DateOnly(2025, 4, 1),  new DateOnly(2025, 4, 9),  88888.29m,  8,  360),
            (new DateOnly(2025, 4, 9),  new DateOnly(2025, 4, 23), 100000.12m, 14, 360),
            (new DateOnly(2025, 4, 23), new DateOnly(2025, 5, 1),  66666.99m,  8,  360),
            (new DateOnly(2025, 5, 1),  new DateOnly(2025, 5, 20), 66666.99m,  19, 372),
        };
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].From, line.Segments[i].From);
            Assert.Equal(expected[i].To, line.Segments[i].To);
            Assert.Equal(new Money(expected[i].Principal), line.Segments[i].AccrualPrincipal);
            Assert.Equal(expected[i].Days, line.Segments[i].Days);
            Assert.Equal(expected[i].Basis, line.Segments[i].Basis);
        }

        // 3. And only then the internal footing identity.
        var sum = 0m;
        var days = 0;
        foreach (var s in line.Segments)
        {
            Assert.Equal(new Money(s.AccrualPrincipal.Amount * (line.RatePercent / 100m) * s.Days / s.Basis),
                s.Interest);
            sum += s.Interest.Amount;
            days += s.Days;
        }
        Assert.Equal(49, days);
        Assert.Equal(line.Days, days);
        Assert.Equal(new Money(sum), line.Interest);
    }

    [Fact]
    public void BasisFor_resolves_the_exact_divisor_in_force_for_every_style_and_date()
    {
        // A hand-written table with NO arithmetic in it, so the assertion cannot re-derive itself from the
        // implementation. A RANGE guard is useless here and was actively misleading: `InRange(basis, 28,
        // 366)` admits 28 × 12 = 336, 29 × 12 = 348 and 30 × 12 = 360 — three of the four ×12 values the
        // guard was supposed to forbid — so it would have passed against the very defect it named.
        //
        // Every value below is the divisor the engine had BEFORE Phase 10.10 · S1; S1 changes only WHERE
        // the date comes from (the segment's start, not the window's — WF-5), never the divisor itself.
        // Slice S3 / WF-6 owns the two Calendar-Month / 30-Day-Month rows and is blocked on T8.
        var table = new (InterestPer Per, DateOnly Date, int Expected)[]
        {
            // final under both answers to T8
            (InterestPer.ThreeSixtyFiveDayYear, new DateOnly(2025, 2, 1), 365),
            (InterestPer.ThreeSixtyFiveDayYear, new DateOnly(2024, 2, 1), 365),
            (InterestPer.CalendarYear,          new DateOnly(2025, 2, 1), 365),
            (InterestPer.CalendarYear,          new DateOnly(2025, 1, 1), 365),
            (InterestPer.CalendarYear,          new DateOnly(2024, 2, 1), 366),   // leap
            (InterestPer.CalendarYear,          new DateOnly(2024, 12, 1), 366),
            // T8-BLOCKED (S3 / WF-6) — pinned at the shipped values so S1 cannot move them
            (InterestPer.ThirtyDayMonth,        new DateOnly(2025, 2, 1), 360),
            (InterestPer.ThirtyDayMonth,        new DateOnly(2024, 12, 1), 360),
            (InterestPer.CalendarMonth,         new DateOnly(2025, 2, 1), 336),   // 28 × 12
            (InterestPer.CalendarMonth,         new DateOnly(2025, 1, 1), 372),   // 31 × 12
            (InterestPer.CalendarMonth,         new DateOnly(2024, 2, 1), 348),   // 29 × 12, leap February
            (InterestPer.CalendarMonth,         new DateOnly(2024, 12, 1), 372),  // 31 × 12
        };

        foreach (var (per, date, expected) in table)
            Assert.Equal(expected, InterestCalculation.BasisFor(per, date));

        // WF-5's actual contract: the Calendar styles read the SEGMENT's own date, so the same style
        // resolves differently for two different dates. (The ×12 arm makes this visible per month; the
        // Calendar-Year arm per year. A resolver that ignored its date argument would pass a single-date
        // table and fail here.)
        Assert.NotEqual(
            InterestCalculation.BasisFor(InterestPer.CalendarMonth, new DateOnly(2025, 2, 1)),
            InterestCalculation.BasisFor(InterestPer.CalendarMonth, new DateOnly(2025, 1, 1)));
        Assert.NotEqual(
            InterestCalculation.BasisFor(InterestPer.CalendarYear, new DateOnly(2024, 6, 1)),
            InterestCalculation.BasisFor(InterestPer.CalendarYear, new DateOnly(2025, 6, 1)));
    }

    // ---------------------------------------------------------------- PostDue · the same segment walk

    [Fact]
    public void PostDue_basis_follows_the_year_each_segment_falls_in()
    {
        // The PostDue twin of Calendar_year_basis_follows_the_year_each_segment_falls_in. S1 routed
        // PostDue through the same boundary walk so its basis is resolved per segment too — and BOTH
        // PostDue tests in the repo (and the only PostDue fixture in Apex.Persistence.Sqlite.Tests) use a
        // CONSTANT-basis style (365-day / 30-day), so that change was shipped with no coverage at all:
        // deleting the Boundaries call from PostDueLines left every existing test green.
        var c = SeedFrom(new DateOnly(2023, 1, 1), out _, out var customer, out var cash);
        customer.MaintainBillByBill = true;

        var newRef = new BillAllocation(BillRefType.NewRef, "INV1", Money.FromRupees(100000.77m),
            dueDate: new DateOnly(2023, 11, 30));
        Post(c, new DateOnly(2023, 11, 1), customer, cash, 100000.77m, drBill: newRef);

        customer.Interest = new InterestParameters(true, 12m, InterestPer.CalendarYear,
            onBalance: InterestOnBalance.DebitOnly, applicability: InterestApplicability.PostDue);

        // Accrual starts the day after the due date = 01-Dec-2023, and runs to 31-Jan-2025 = 427 days:
        // 31 in 2023 (365), 366 in leap 2024 (366), 30 in 2025 (365).
        var line = Assert.Single(InterestCalculation.Build(c,
            new DateOnly(2023, 12, 1), new DateOnly(2025, 1, 31)).Lines);

        Assert.Equal("INV1", line.BillReference);
        Assert.Equal(new DateOnly(2023, 12, 1), line.From);
        Assert.Equal(427, line.Days);
        Assert.Equal(3, line.Segments.Count);
        Assert.Equal(new[] { 365, 366, 365 }, line.Segments.Select(s => s.Basis));
        Assert.Equal(14005.59m, Paisa(line.Interest.Amount));   // NOT 14,038.46 — one basis for all 427 days
    }

    [Fact]
    public void PostDue_segments_carry_the_bill_pending_not_the_ledger_balance()
    {
        // The contract on InterestSegment.SignedPrincipal is path-dependent and MUST stay documented that
        // way: on the Always path it is the ledger's signed balance (and equals SignedClosing at From —
        // see Segment_principals_equal_SignedClosing_at_every_boundary); on the PostDue path it is the
        // BILL's pending amount, signed by the bill's side. A consumer that reads it as a ledger balance
        // on a bill-wise party gets the wrong magnitude on every row, and the wrong SIGN on a payable.
        var c = Seed(out _, out var customer, out var cash);
        customer.MaintainBillByBill = true;

        Post(c, new DateOnly(2024, 1, 2), customer, cash, 50000.37m,
            drBill: new BillAllocation(BillRefType.NewRef, "INV1", Money.FromRupees(50000.37m),
                dueDate: new DateOnly(2024, 1, 31)));
        Post(c, new DateOnly(2024, 1, 3), customer, cash, 30000.29m,
            drBill: new BillAllocation(BillRefType.NewRef, "INV2", Money.FromRupees(30000.29m),
                dueDate: new DateOnly(2024, 1, 31)));

        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly, applicability: InterestApplicability.PostDue);

        var lines = InterestCalculation.Build(c,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1)).Lines;

        // One row per open bill; the ledger's own balance is the SUM of the two and is never a segment
        // principal on this path.
        var ledgerBalance = LedgerBalances.SignedClosing(c, customer, new DateOnly(2024, 2, 1));
        Assert.Equal(80000.66m, ledgerBalance);
        Assert.Equal(2, lines.Count);

        var expected = new (string Ref, decimal Pending, decimal Paise)[]
        {
            ("INV1", 50000.37m, 476.72m),
            ("INV2", 30000.29m, 286.03m),
        };
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Ref, lines[i].BillReference);
            Assert.Equal(29, lines[i].Days);                              // 01-Feb → 01-Mar, leap February
            Assert.Equal(expected[i].Paise, Paisa(lines[i].Interest.Amount));
            foreach (var s in lines[i].Segments)
            {
                Assert.Equal(expected[i].Pending, s.SignedPrincipal);     // the BILL, not the ledger
                Assert.NotEqual(ledgerBalance, s.SignedPrincipal);
            }
        }
    }

    // ---------------------------------------------------------------- rounding & the report total

    [Fact]
    public void Rounding_is_applied_once_per_printed_row_and_the_total_never_nets_the_two_sides()
    {
        // Two decisions, both previously unpinned, both newly reachable within ONE ledger because S1
        // splits a sign-flipping window into one row per contiguous same-side run:
        //   (a) directional rounding runs once per PRINTED ROW, not once per ledger — TallyPrime's
        //       Rounding is a per-row presentation setting, and a Dr row and a Cr row are different
        //       money that must not be pooled before rounding;
        //   (b) InterestReport.TotalInterest is a sum of MAGNITUDES across both sides, not a net
        //       position — the per-side figures come off Lines via PrincipalIsDebit.
        // The fixture is chosen so the two conventions DISAGREE: ceil(65.10) + ceil(187.20) = 254, while
        // ceiling the pooled ₹252.30 once would give 253. A fixture where they agree asserts nothing.
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), customer, cash, 19801.33m);   // carried in: 19,801.33 Dr
        Post(c, new DateOnly(2025, 4, 11), cash, customer, 49769.94m);   // flips to 29,968.61 Cr

        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            roundingMethod: InterestRoundingMethod.Upward, roundingDecimals: 0);

        var report = InterestCalculation.Build(c, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        var lines = report.Lines;
        Assert.Equal(2, lines.Count);

        // raw 65.10026… → 66 (receivable) and raw 187.20118… → 188 (payable)
        Assert.True(lines[0].PrincipalIsDebit);
        Assert.Equal(10, lines[0].Days);
        Assert.Equal(Money.FromRupees(66m), lines[0].Interest);
        Assert.False(lines[1].PrincipalIsDebit);
        Assert.Equal(19, lines[1].Days);
        Assert.Equal(Money.FromRupees(188m), lines[1].Interest);

        var raw = 19801.33m * 0.12m * 10m / 365m + 29968.61m * 0.12m * 19m / 365m;
        Assert.Equal(252.30m, Paisa(raw));
        Assert.Equal(Money.FromRupees(254m), report.TotalInterest);      // NOT 253 (pooled-then-ceiled)
        Assert.NotEqual(Money.FromRupees(253m), report.TotalInterest);

        // The total is 66 + 188, never 66 − 188: the two sides are separately recoverable from Lines.
        var receivable = lines.Where(l => l.PrincipalIsDebit).Sum(l => l.Interest.Amount);
        var payable = lines.Where(l => !l.PrincipalIsDebit).Sum(l => l.Interest.Amount);
        Assert.Equal(66m, receivable);
        Assert.Equal(188m, payable);
        Assert.Equal(receivable + payable, report.TotalInterest.Amount);
    }

    [Fact]
    public void A_window_with_no_accruing_balance_produces_no_line_at_all()
    {
        // The report's "nothing to show" path must survive the per-segment On-filter: a ledger that sits
        // on the disallowed side for the WHOLE window still emits nothing.
        var c = Seed(out _, out var customer, out var cash);
        Post(c, new DateOnly(2025, 3, 25), cash, customer, 44444.61m);   // a credit balance
        customer.Interest = new InterestParameters(true, 12m, InterestPer.ThreeSixtyFiveDayYear,
            onBalance: InterestOnBalance.DebitOnly);

        Assert.Empty(InterestCalculation.Build(c,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines);
    }
}
