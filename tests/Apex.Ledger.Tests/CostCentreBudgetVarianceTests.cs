using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// v67 census 2.9 — the COST-CENTRE arm of the budget-variance projection, against POSTED cost
/// allocations.
///
/// <para>🔴 <b>WHY THIS FILE EXISTS.</b> When the cost-centre budget target shipped, nothing anywhere
/// posted an allocation against a cost-centre budget: the only variance test asserted <c>Actual = 0</c>
/// while its own doc comment claimed it "reports a real actual taken from a posted allocation". The whole
/// new money arithmetic was untested, and under that cover a real wrong figure shipped green —
/// <b>BF-2</b>: a budget on a PARENT centre reported an actual that excluded every rupee booked to its
/// children. Every test here posts real vouchers and reads the figure off the report.</para>
///
/// <para><b>Grounding.</b> help.tallysolutions.com/cost-centre-or-profit-centre-tally/, opened by content
/// 2026-10-18: "Such Cost Centres can be created in a hierarchical structure to get a consolidated view of
/// the allocation", and of the Sales Department worked example, "Once the hierarchy is created whenever you
/// allocate an expense to any of the employees, it would also become part of the relevant department."
/// help.tallysolutions.com/budgets-tally/, same date: "Set Cost Centres to Yes", and the Cost Centre Budget
/// screen's "Cost Centre, Expenses, Income and Closing Balance".</para>
/// </summary>
public class CostCentreBudgetVarianceTests
{
    // Primary Cost Category, with a two-level hierarchy:
    //   Delhi            (primary centre)
    //     Delhi-North    (child of Delhi)
    //   Mumbai           (primary centre, no children)
    //
    // Postings, all inside the Apr–Jun 2024 budget window unless marked:
    //   2024-03-20  Salaries 1,000 Dr → Delhi-North      (BEFORE the window)
    //   2024-04-05  Salaries 6,000 Dr → Delhi
    //   2024-04-10  Salaries 3,000 Dr → Delhi-North
    //   2024-04-20  Salaries 4,000 Dr → Mumbai
    //   2024-05-15  Sales    5,000 Cr → Mumbai
    private static Company Seed(
        out CostCentre delhi,
        out CostCentre delhiNorth,
        out CostCentre mumbai)
    {
        var c = CompanyFactory.CreateSeeded("Centre Budget Co", new DateOnly(2024, 3, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(salaries);
        c.AddLedger(sales);

        var cat = c.FindCostCategoryByName("Primary Cost Category")!;
        delhi = new CostCentre(Guid.NewGuid(), "Delhi", cat.Id);
        mumbai = new CostCentre(Guid.NewGuid(), "Mumbai", cat.Id);
        c.AddCostCentre(delhi);
        c.AddCostCentre(mumbai);
        delhiNorth = new CostCentre(Guid.NewGuid(), "Delhi-North", cat.Id, parentId: delhi.Id);
        c.AddCostCentre(delhiNorth);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        void Expense(decimal amt, CostCentre centre, DateOnly date) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
            {
                new EntryLine(salaries.Id, Money.FromRupees(amt), DrCr.Debit, billAllocations: null,
                    costAllocations: new[] { new CostAllocation(cat.Id, centre.Id, Money.FromRupees(amt)) }),
                new EntryLine(cash.Id, Money.FromRupees(amt), DrCr.Credit),
            }));

        Expense(1000m, delhiNorth, new DateOnly(2024, 3, 20)); // before the window
        Expense(6000m, delhi, new DateOnly(2024, 4, 5));
        Expense(3000m, delhiNorth, new DateOnly(2024, 4, 10));
        Expense(4000m, mumbai, new DateOnly(2024, 4, 20));

        // Mumbai also earns: 5,000 Cr revenue allocated to the same centre, inside the window.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 5, 15), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(5000m)) }),
        }));

        return c;
    }

    private static Budget AprToJun(Company c, params BudgetLine[] lines) =>
        new(Guid.NewGuid(), "Q1 Budget", new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), lines: lines);

    /// <summary>
    /// 🔴 <b>THE BF-2 REGRESSION.</b> Delhi's OWN April spend is 6,000 and its child Delhi-North's is 3,000.
    /// Budgeted 9,000, the parent's actual must be the consolidated 9,000 and the variance zero.
    /// Before the fix this reported actual 6,000 / variance −3,000 — a 3,000 under-spend that did not exist.
    /// Reverting <c>CostCentreIsUnder</c> to <c>a.CentreId == centreId</c> reddens exactly here.
    /// </summary>
    [Fact]
    public void A_parent_centre_budget_consolidates_every_centre_under_it()
    {
        var c = Seed(out var delhi, out _, out _);
        var budget = AprToJun(c,
            BudgetLine.ForCostCentre(delhi.Id, BudgetType.OnNettTransactions, Money.FromRupees(9000m)));

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);

        Assert.Equal(BudgetTargetKind.CostCentre, line.TargetKind);
        Assert.Equal("Cost Centre", line.TargetKindLabel);
        Assert.Equal("Delhi", line.TargetName);
        Assert.Equal(Money.FromRupees(9000m), line.Actual);   // 6,000 own + 3,000 child — NOT 6,000
        Assert.Equal(Money.Zero, line.Variance);
        Assert.Equal(0m, line.VariancePercent);
    }

    /// <summary>
    /// The consolidated figure is the one the product's own Cost Centre Break-up already publishes for the
    /// same window. Both are asserted in one test so the two reports cannot drift apart on the roll-up —
    /// which is the half of the comparison that IS settled (the sign half is not; see
    /// <see cref="A_mixed_sign_centre_nets_while_the_Break_up_sums_magnitudes"/>).
    /// </summary>
    [Fact]
    public void The_parent_actual_equals_the_Cost_Centre_Break_up_rolled_up_total()
    {
        var c = Seed(out var delhi, out _, out _);

        var breakup = CostReports.BuildCostCentreBreakup(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30));
        var delhiRow = Assert.Single(breakup.Centres, l => l.CentreId == delhi.Id);
        Assert.Equal(Money.FromRupees(6000m), delhiRow.OwnTotal);
        Assert.Equal(Money.FromRupees(9000m), delhiRow.RolledUpTotal);

        var budget = AprToJun(c,
            BudgetLine.ForCostCentre(delhi.Id, BudgetType.OnNettTransactions, Money.FromRupees(9000m)));
        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);

        Assert.Equal(delhiRow.RolledUpTotal, line.Actual);
    }

    /// <summary>
    /// The walk is DIRECTIONAL: a budget on the CHILD sees only the child's own subtree, never the parent's
    /// other spend. Without this, "roll up" could have been implemented as "every centre in the category"
    /// and the test above would still pass.
    /// </summary>
    [Fact]
    public void A_child_centre_budget_sees_only_its_own_subtree_not_its_parent()
    {
        var c = Seed(out _, out var delhiNorth, out _);
        var budget = AprToJun(c,
            BudgetLine.ForCostCentre(delhiNorth.Id, BudgetType.OnNettTransactions, Money.FromRupees(3000m)));

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);
        Assert.Equal("Delhi-North", line.TargetName);
        Assert.Equal(Money.FromRupees(3000m), line.Actual);   // its own 3,000; NOT Delhi's 6,000 as well
        Assert.Equal(Money.Zero, line.Variance);
    }

    /// <summary>
    /// A sibling's spend never leaks in. Mumbai has no children, so its consolidated figure is its own —
    /// and it is NOT 9,000 (which is what matching "every centre" would have produced).
    /// </summary>
    [Fact]
    public void A_childless_centre_reports_only_its_own_allocations()
    {
        var c = Seed(out _, out _, out var mumbai);
        var budget = AprToJun(c,
            BudgetLine.ForCostCentre(mumbai.Id, BudgetType.OnNettTransactions, Money.FromRupees(1000m)));

        var line = Assert.Single(BudgetVarianceReport.Build(c, budget).Lines);
        Assert.Equal("Mumbai", line.TargetName);
        // 4,000 Dr − 5,000 Cr = −1,000 signed; the caller reports the magnitude.
        Assert.Equal(Money.FromRupees(1000m), line.Actual);
    }

    /// <summary>
    /// The window still bounds the consolidated figure. OnNettTransactions excludes the child's 1,000 booked
    /// on 2024-03-20, before PeriodFrom; OnClosingBalance has no lower bound (a cost centre carries no
    /// opening balance in this model) and so picks it up: 9,000 + 1,000 = 10,000.
    /// </summary>
    [Fact]
    public void OnClosingBalance_consolidates_the_subtree_without_a_lower_bound()
    {
        var c = Seed(out var delhi, out _, out _);

        var nett = Assert.Single(BudgetVarianceReport.Build(c, AprToJun(c,
            BudgetLine.ForCostCentre(delhi.Id, BudgetType.OnNettTransactions, Money.Zero))).Lines);
        Assert.Equal(Money.FromRupees(9000m), nett.Actual);

        var closing = Assert.Single(BudgetVarianceReport.Build(c, AprToJun(c,
            BudgetLine.ForCostCentre(delhi.Id, BudgetType.OnClosingBalance, Money.Zero))).Lines);
        Assert.Equal(Money.FromRupees(10000m), closing.Actual);   // + the child's pre-window 1,000
    }

    /// <summary>
    /// 🔴 <b>A DIVERGENCE TRIPWIRE, NOT A FIDELITY ASSERTION (BF-3).</b> This test does NOT claim the vendor
    /// behaves this way. It pins a measured disagreement between two of our own reports so that it cannot be
    /// changed silently, and so the next reader finds the gap instead of the false claim that used to sit in
    /// <c>BudgetVariance.CostCentreActualSigned</c> ("Using a different filter here would make the budget's
    /// actual disagree with the Cost Centre Summary for the same window").
    ///
    /// <para>The filter IS shared — both reports see the same allocations. The SIGN RULE is not: Budget
    /// Variance nets Dr − Cr then takes the magnitude; the Break-up sums magnitudes and never reads the
    /// parent line's side. Mumbai carries 4,000 Dr and 5,000 Cr in one window, so the Break-up says 9,000 and
    /// the budget's actual says 1,000 — 8,000 apart.</para>
    ///
    /// <para>Which convention the vendor wants is UNKNOWN: help.tallysolutions.com/budgets-tally/ shows a
    /// Cost Centre Budget screen with THREE figures ("Expenses, Income and Closing Balance") where this
    /// product carries one measure per line, so no vendor sentence maps onto either rule. Recorded as ours in
    /// docs/invented-vs-cloned.md as IV-101. A later wave that rules on it must change this test deliberately.
    /// </para>
    /// </summary>
    [Fact]
    public void A_mixed_sign_centre_nets_while_the_Break_up_sums_magnitudes()
    {
        var c = Seed(out _, out _, out var mumbai);
        var from = new DateOnly(2024, 4, 1);
        var to = new DateOnly(2024, 6, 30);

        var breakup = CostReports.BuildCostCentreBreakup(c, from, to);
        var mumbaiRow = Assert.Single(breakup.Centres, l => l.CentreId == mumbai.Id);
        Assert.Equal(Money.FromRupees(9000m), mumbaiRow.OwnTotal);        // 4,000 + 5,000, magnitudes

        var line = Assert.Single(BudgetVarianceReport.Build(c, AprToJun(c,
            BudgetLine.ForCostCentre(mumbai.Id, BudgetType.OnNettTransactions, Money.Zero))).Lines);
        Assert.Equal(Money.FromRupees(1000m), line.Actual);               // 4,000 − 5,000, netted

        // The gap is 8,000, on the same centre, the same window and the same voucher filter.
        Assert.Equal(8000m, mumbaiRow.OwnTotal.Amount - line.Actual.Amount);
    }

    /// <summary>
    /// A cost-centre budget line whose centre was deleted out from under it is a NAMED failure, not a null
    /// dereference — the shape the whole report used to take before the cost-centre arm existed.
    /// </summary>
    [Fact]
    public void A_budget_line_naming_an_unknown_cost_centre_is_a_named_error()
    {
        var c = Seed(out _, out _, out _);
        var budget = AprToJun(c,
            BudgetLine.ForCostCentre(Guid.NewGuid(), BudgetType.OnNettTransactions, Money.FromRupees(100m)));

        var ex = Assert.Throws<InvalidOperationException>(() => BudgetVarianceReport.Build(c, budget));
        Assert.Contains("unknown cost centre", ex.Message);
    }
}
