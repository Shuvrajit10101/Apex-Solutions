using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Budgets tests (catalog §7; plan.md §5): a Budget master with Name / optional Under (Primary group) /
/// Period From–To and lines set On Groups and On Ledgers; the budget-variance projection for both
/// OnClosingBalance and OnNettTransactions; group roll-up over member ledgers; period windowing (nett is
/// bounded by the period, closing is as-of period-end); and variance / variance% arithmetic including the
/// zero-budget (undefined %) case.
/// </summary>
public class BudgetTests
{
    // A company with:
    //   Salaries, Rent under Indirect Expenses (Expense/Dr nature)
    //   Sales under Sales Accounts (Income/Cr nature)
    //   Cash (opening 10,00,000 Dr)
    // Postings:
    //   2024-03-20 (before the Apr–Jun budget): Salaries 1,000 Dr / Cash Cr   → in closing, NOT in Apr nett
    //   2024-04-05: Salaries 6,000 Dr / Cash Cr
    //   2024-04-10: Rent 3,000 Dr / Cash Cr
    //   2024-05-15: Salaries 2,000 Dr / Cash Cr
    //   2024-04-20: Cash 8,000 Dr / Sales Cr
    //   2024-07-05 (after the budget): Salaries 500 Dr / Cash Cr → excluded from both nett and closing(PeriodTo=Jun 30)
    private static Company Seed(
        out Domain.Ledger salaries,
        out Domain.Ledger rent,
        out Domain.Ledger sales,
        out Group indirectExpenses)
    {
        // Books begin 2024-03-01 so the pre-period (Mar 20) voucher is valid; the budget window is Apr–Jun.
        var c = CompanyFactory.CreateSeeded("Budget Co", new DateOnly(2024, 3, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        indirectExpenses = c.FindGroupByName("Indirect Expenses")!;
        salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries", indirectExpenses.Id, Money.Zero, openingIsDebit: true);
        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", indirectExpenses.Id, Money.Zero, openingIsDebit: true);
        sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(salaries);
        c.AddLedger(rent);
        c.AddLedger(sales);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        void Expense(Domain.Ledger led, decimal amt, DateOnly date) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
            {
                new EntryLine(led.Id, Money.FromRupees(amt), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(amt), DrCr.Credit),
            }));

        Expense(salaries, 1000m, new DateOnly(2024, 3, 20)); // before period
        Expense(salaries, 6000m, new DateOnly(2024, 4, 5));
        Expense(rent, 3000m, new DateOnly(2024, 4, 10));
        Expense(salaries, 2000m, new DateOnly(2024, 5, 15));
        Expense(salaries, 500m, new DateOnly(2024, 7, 5)); // after period

        // Sales 8,000 Cr in April.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(8000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit),
        }));

        return c;
    }

    private static Budget AprToJun(Company c, IEnumerable<BudgetLine> lines) =>
        new(Guid.NewGuid(), "Q1 Budget", new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), lines: lines);

    [Fact]
    public void Budget_master_validates_period_and_line_targeting()
    {
        // PeriodTo before PeriodFrom is rejected.
        Assert.Throws<ArgumentException>(() =>
            new Budget(Guid.NewGuid(), "Bad", new DateOnly(2024, 6, 30), new DateOnly(2024, 4, 1)));

        // A line must target exactly one of group/ledger; negative amounts rejected.
        Assert.Throws<ArgumentException>(() =>
            BudgetLine.ForLedger(Guid.NewGuid(), BudgetType.OnClosingBalance, new Money(-1m)));

        var b = new Budget(Guid.NewGuid(), "Ok", new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30));
        b.AddLine(BudgetLine.ForLedger(Guid.NewGuid(), BudgetType.OnClosingBalance, Money.FromRupees(100m)));
        Assert.Single(b.Lines);
        Assert.True(b.Lines[0].IsLedgerTarget);
        Assert.False(b.Lines[0].IsGroupTarget);
    }

    [Fact]
    public void Ledger_line_OnNettTransactions_uses_only_movements_inside_the_period()
    {
        var c = Seed(out var salaries, out _, out _, out _);
        // Salaries nett Apr–Jun = 6,000 (Apr) + 2,000 (May) = 8,000.
        // The 1,000 in March and 500 in July are OUTSIDE the window and excluded.
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForLedger(salaries.Id, BudgetType.OnNettTransactions, Money.FromRupees(10000m)),
        });

        var report = BudgetVarianceReport.Build(c, budget);
        var line = Assert.Single(report.Lines);

        Assert.Equal("Salaries", line.TargetName);
        Assert.Equal(BudgetType.OnNettTransactions, line.Type);
        Assert.Equal(Money.FromRupees(10000m), line.Budget);
        Assert.Equal(Money.FromRupees(8000m), line.Actual);
        Assert.Equal(Money.FromRupees(-2000m), line.Variance); // 8000 − 10000
        Assert.Equal(-20m, line.VariancePercent);              // −2000 / 10000 × 100
    }

    [Fact]
    public void Ledger_line_OnClosingBalance_is_as_of_period_end_and_includes_pre_period_movements()
    {
        var c = Seed(out var salaries, out _, out _, out _);
        // Salaries closing at 2024-06-30 = 1,000 (Mar) + 6,000 (Apr) + 2,000 (May) = 9,000.
        // The July 500 is AFTER PeriodTo and excluded.
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForLedger(salaries.Id, BudgetType.OnClosingBalance, Money.FromRupees(9000m)),
        });

        var report = BudgetVarianceReport.Build(c, budget);
        var line = Assert.Single(report.Lines);

        Assert.Equal(Money.FromRupees(9000m), line.Actual);
        Assert.Equal(Money.Zero, line.Variance); // on budget
        Assert.Equal(0m, line.VariancePercent);
    }

    [Fact]
    public void Group_line_rolls_up_every_ledger_under_it()
    {
        var c = Seed(out _, out _, out _, out var indirectExpenses);
        // Indirect Expenses group nett Apr–Jun = Salaries (8,000) + Rent (3,000) = 11,000.
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForGroup(indirectExpenses.Id, BudgetType.OnNettTransactions, Money.FromRupees(12000m)),
        });

        var report = BudgetVarianceReport.Build(c, budget);
        var line = Assert.Single(report.Lines);

        Assert.True(line.IsGroup);
        Assert.Equal("Indirect Expenses", line.TargetName);
        Assert.Equal(Money.FromRupees(11000m), line.Actual);
        Assert.Equal(Money.FromRupees(-1000m), line.Variance);
    }

    [Fact]
    public void Group_line_OnClosingBalance_rolls_up_closing_of_member_ledgers()
    {
        var c = Seed(out _, out _, out _, out var indirectExpenses);
        // Closing at Jun 30: Salaries 9,000 + Rent 3,000 = 12,000.
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForGroup(indirectExpenses.Id, BudgetType.OnClosingBalance, Money.FromRupees(10000m)),
        });

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);
        Assert.Equal(Money.FromRupees(12000m), line.Actual);
        Assert.Equal(Money.FromRupees(2000m), line.Variance);   // over budget
        Assert.Equal(20m, line.VariancePercent);
    }

    [Fact]
    public void Income_ledger_actual_is_reported_as_a_magnitude()
    {
        var c = Seed(out _, out _, out var sales, out _);
        // Sales sits on the credit side; its nett magnitude in Apr–Jun is 8,000 (signed −8,000).
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForLedger(sales.Id, BudgetType.OnNettTransactions, Money.FromRupees(8000m)),
        });

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);
        Assert.Equal(Money.FromRupees(8000m), line.Actual); // magnitude, not −8000
        Assert.Equal(Money.Zero, line.Variance);
    }

    [Fact]
    public void Zero_budget_amount_yields_null_variance_percent()
    {
        var c = Seed(out var salaries, out _, out _, out _);
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForLedger(salaries.Id, BudgetType.OnNettTransactions, Money.Zero),
        });

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);
        Assert.Equal(Money.FromRupees(8000m), line.Actual);
        Assert.Equal(Money.FromRupees(8000m), line.Variance);
        Assert.Null(line.VariancePercent); // divide-by-zero is undefined, reported as null
    }

    [Fact]
    public void Report_carries_the_budget_identity_and_period_and_one_row_per_line()
    {
        var c = Seed(out var salaries, out var rent, out _, out var indirectExpenses);
        var budget = AprToJun(c, new[]
        {
            BudgetLine.ForLedger(salaries.Id, BudgetType.OnNettTransactions, Money.FromRupees(8000m)),
            BudgetLine.ForLedger(rent.Id, BudgetType.OnClosingBalance, Money.FromRupees(3000m)),
            BudgetLine.ForGroup(indirectExpenses.Id, BudgetType.OnNettTransactions, Money.FromRupees(11000m)),
        });

        var report = BudgetVarianceReport.Build(c, budget);
        Assert.Equal(budget.Id, report.BudgetId);
        Assert.Equal("Q1 Budget", report.BudgetName);
        Assert.Equal(new DateOnly(2024, 4, 1), report.PeriodFrom);
        Assert.Equal(new DateOnly(2024, 6, 30), report.PeriodTo);
        Assert.Equal(3, report.Lines.Count);
    }
}
