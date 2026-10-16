using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 11.10 — the <b>Cost Centre Monthly Summary</b> primitive the census recorded as absent "because that
/// primitive does not exist (T1-32)".
///
/// <para><b>Vendor grounding (R7 source 1), opened BY CONTENT 2026-10-13:</b>
/// <c>help.tallysolutions.com/tally-prime/financial-controlling/cost-centre-or-profit-centre-tally/</c> —
/// <i>"You can view the monthly breakup of income and expense details along with the closing balance for the
/// selected Cost Centre."</i> Three facts to pin, and each test below pins one of them: it is MONTHLY, it
/// separates INCOME from EXPENSE, and it carries a CLOSING BALANCE.</para>
///
/// <para>🔴 Every figure here is asserted against a book posted through <see cref="LedgerService.Post"/>, never
/// against a hand-built report object.</para>
/// </summary>
public class CostCentreMonthlySummaryTests
{
    private static readonly DateOnly BooksBegin = new(2024, 4, 1);

    /// <summary>A company with a Salaries expense ledger, a Commission income ledger, Cash, and two centres
    /// (Delhi, Mumbai) under the seeded Primary Cost Category.</summary>
    private static Company Seed(
        out Domain.Ledger cash,
        out Domain.Ledger salaries,
        out Domain.Ledger commission,
        out CostCategory category,
        out CostCentre delhi,
        out CostCentre mumbai,
        out VoucherType journal)
    {
        var c = CompanyFactory.CreateSeeded("Cost Monthly Co", BooksBegin);

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(salaries);

        commission = new Domain.Ledger(Guid.NewGuid(), "Commission Received",
            c.FindGroupByName("Indirect Incomes")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(commission);

        category = c.FindCostCategoryByName("Primary Cost Category")!;
        delhi = new CostCentre(Guid.NewGuid(), "Delhi", category.Id);
        mumbai = new CostCentre(Guid.NewGuid(), "Mumbai", category.Id);
        c.AddCostCentre(delhi);
        c.AddCostCentre(mumbai);

        journal = c.FindVoucherTypeByName("Journal")!;
        return c;
    }

    /// <summary>Posts "Dr expense / Cr cash" with the whole expense allocated to one centre.</summary>
    private static void PostExpense(
        Company c, VoucherType journal, DateOnly date, Domain.Ledger expense, Domain.Ledger cash,
        CostCategory cat, CostCentre centre, decimal rupees)
    {
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(expense.Id, Money.FromRupees(rupees), DrCr.Debit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(cat.Id, centre.Id, Money.FromRupees(rupees)) }),
            new EntryLine(cash.Id, Money.FromRupees(rupees), DrCr.Credit),
        }));
    }

    /// <summary>Posts "Dr cash / Cr income" with the whole income allocated to one centre.</summary>
    private static void PostIncome(
        Company c, VoucherType journal, DateOnly date, Domain.Ledger income, Domain.Ledger cash,
        CostCategory cat, CostCentre centre, decimal rupees)
    {
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(income.Id, Money.FromRupees(rupees), DrCr.Credit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(cat.Id, centre.Id, Money.FromRupees(rupees)) }),
        }));
    }

    // ------------------------------------------------------------------ it is MONTHLY

    [Fact]
    public void One_row_per_month_of_the_window_even_for_a_month_with_no_allocations()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out _, out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostExpense(c, journal, new DateOnly(2024, 6, 20), salaries, cash, cat, delhi, 4000m);

        var ms = CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, new DateOnly(2024, 6, 30));

        Assert.Equal(3, ms.Rows.Count);
        Assert.Equal(new[] { "Apr-2024", "May-2024", "Jun-2024" }, ms.Rows.Select(r => r.Month.Label));

        // May has no allocation at all, and still gets a row that carries April's closing unchanged.
        Assert.Equal(Money.Zero, ms.Rows[1].Debit);
        Assert.Equal(Money.Zero, ms.Rows[1].Credit);
        Assert.Equal(0, ms.Rows[1].VoucherCount);
        Assert.Equal(Money.FromRupees(6000m), ms.Rows[1].ClosingAmount);
        Assert.Equal(DrCr.Debit, ms.Rows[1].ClosingSide);
    }

    // ------------------------------------------------------------------ it separates INCOME from EXPENSE

    [Fact]
    public void Expense_allocations_land_on_debit_and_income_allocations_on_credit()
    {
        var c = Seed(out var cash, out var salaries, out var commission, out var cat, out var delhi, out _,
            out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostIncome(c, journal, new DateOnly(2024, 4, 20), commission, cash, cat, delhi, 2500m);

        var ms = CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, new DateOnly(2024, 4, 30));

        var april = Assert.Single(ms.Rows);
        // 🔴 A CostAllocation is a positive magnitude; the side comes off the parent EntryLine. If the side were
        // read off the allocation, BOTH figures would land in one column and this assertion would fail.
        Assert.Equal(Money.FromRupees(6000m), april.Debit);
        Assert.Equal(Money.FromRupees(2500m), april.Credit);
        Assert.Equal(2, april.VoucherCount);
    }

    [Fact]
    public void A_centre_sees_only_its_own_allocations()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out var mumbai,
            out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostExpense(c, journal, new DateOnly(2024, 4, 11), salaries, cash, cat, mumbai, 9000m);

        var delhiMs = CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, new DateOnly(2024, 4, 30));
        var mumbaiMs = CostCentreMonthlySummary.Build(c, mumbai.Id, BooksBegin, new DateOnly(2024, 4, 30));

        Assert.Equal(Money.FromRupees(6000m), Assert.Single(delhiMs.Rows).Debit);
        Assert.Equal(Money.FromRupees(9000m), Assert.Single(mumbaiMs.Rows).Debit);
        Assert.Equal("Delhi", delhiMs.CentreName);
        Assert.Equal("Mumbai", mumbaiMs.CentreName);
    }

    [Fact]
    public void A_line_split_across_two_centres_gives_each_centre_only_its_slice()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out var mumbai,
            out var journal);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null,
                costAllocations: new[]
                {
                    new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(7000m)),
                    new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(3000m)),
                }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        var to = new DateOnly(2024, 4, 30);
        Assert.Equal(Money.FromRupees(7000m),
            Assert.Single(CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, to).Rows).Debit);
        Assert.Equal(Money.FromRupees(3000m),
            Assert.Single(CostCentreMonthlySummary.Build(c, mumbai.Id, BooksBegin, to).Rows).Debit);
    }

    // ------------------------------------------------------------------ it carries a CLOSING BALANCE

    [Fact]
    public void The_closing_balance_accumulates_down_the_column_and_the_last_row_is_the_reports_closing()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out _, out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostExpense(c, journal, new DateOnly(2024, 5, 10), salaries, cash, cat, delhi, 4000m);

        var ms = CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, new DateOnly(2024, 5, 31));

        Assert.Equal(Money.FromRupees(6000m), ms.Rows[0].ClosingAmount);
        Assert.Equal(Money.FromRupees(10000m), ms.Rows[1].ClosingAmount);
        Assert.Equal(Money.FromRupees(10000m), ms.ClosingAmount);
        Assert.Equal(DrCr.Debit, ms.ClosingSide);
        // The report's own closing IS the last month row's closing — they cannot disagree.
        Assert.Equal(ms.Rows[^1].ClosingAmount, ms.ClosingAmount);
        Assert.Equal(ms.Rows[^1].ClosingSide, ms.ClosingSide);
    }

    [Fact]
    public void A_net_income_centre_closes_on_the_credit_side()
    {
        var c = Seed(out var cash, out var salaries, out var commission, out var cat, out var delhi, out _,
            out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 2000m);
        PostIncome(c, journal, new DateOnly(2024, 4, 20), commission, cash, cat, delhi, 9000m);

        var ms = CostCentreMonthlySummary.Build(c, delhi.Id, BooksBegin, new DateOnly(2024, 4, 30));

        // A profit centre that earned more than it spent closes Cr 7,000 — not Dr 7,000, and not Dr -7,000.
        Assert.Equal(DrCr.Credit, ms.ClosingSide);
        Assert.Equal(Money.FromRupees(7000m), ms.ClosingAmount);
    }

    [Fact]
    public void The_opening_carries_in_everything_allocated_before_the_window_starts()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out _, out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostExpense(c, journal, new DateOnly(2024, 5, 10), salaries, cash, cat, delhi, 4000m);

        // Window = May only. April's 6,000 is not a MAY movement, but it is carried in as the opening.
        var ms = CostCentreMonthlySummary.Build(c, delhi.Id, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31));

        Assert.Equal(Money.FromRupees(6000m), ms.OpeningAmount);
        Assert.Equal(DrCr.Debit, ms.OpeningSide);
        Assert.Equal(Money.FromRupees(4000m), Assert.Single(ms.Rows).Debit);
        Assert.Equal(Money.FromRupees(10000m), ms.ClosingAmount);
    }

    // ------------------------------------------------------------------ what counts

    [Fact]
    public void A_cancelled_voucher_is_excluded_exactly_as_the_other_cost_reports_exclude_it()
    {
        var c = Seed(out var cash, out var salaries, out _, out var cat, out var delhi, out _, out var journal);
        PostExpense(c, journal, new DateOnly(2024, 4, 10), salaries, cash, cat, delhi, 6000m);
        PostExpense(c, journal, new DateOnly(2024, 4, 11), salaries, cash, cat, delhi, 4000m);

        var from = BooksBegin;
        var to = new DateOnly(2024, 4, 30);
        Assert.Equal(Money.FromRupees(10000m),
            Assert.Single(CostCentreMonthlySummary.Build(c, delhi.Id, from, to).Rows).Debit);

        c.Vouchers.First(v => v.TotalDebit == Money.FromRupees(4000m)).Cancelled = true;

        var after = Assert.Single(CostCentreMonthlySummary.Build(c, delhi.Id, from, to).Rows);
        Assert.Equal(Money.FromRupees(6000m), after.Debit);
        Assert.Equal(1, after.VoucherCount);
        // 🔴 The cross-check that matters: this report and the Cost Centre Break-up must agree about which
        // entries exist, because they run the SAME CountsAsOf filter. A divergence here means one of the two is
        // reporting a cancelled voucher.
        var breakup = CostReports.BuildCostCentreBreakup(c, from, to);
        Assert.Equal(Money.FromRupees(6000m),
            breakup.Centres.Single(l => l.CentreId == delhi.Id).OwnTotal);
    }

    [Fact]
    public void An_unknown_or_empty_centre_renders_an_empty_summary_rather_than_throwing()
    {
        var c = Seed(out _, out _, out _, out _, out _, out _, out _);

        var empty = CostCentreMonthlySummary.Build(c, Guid.Empty, BooksBegin, new DateOnly(2024, 4, 30));
        Assert.Empty(empty.Rows);
        Assert.Equal(string.Empty, empty.CentreName);

        var unknown = CostCentreMonthlySummary.Build(c, Guid.NewGuid(), BooksBegin, new DateOnly(2024, 4, 30));
        Assert.Empty(unknown.Rows);
        Assert.Equal(string.Empty, unknown.CentreName);
    }
}
