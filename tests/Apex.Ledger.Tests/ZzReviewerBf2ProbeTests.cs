using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// REVIEWER PROBE (wave-3 adversarial review of claude/apex-bf-masters-v67). NOT part of the branch's own
/// tests and NOT intended to ship: an independent fixture of my own, three levels deep and with a second
/// cost CATEGORY, used to reproduce BF-2 from outside the branch's test file and to prove the fix by
/// mutation. Delete before merge.
/// </summary>
public class ZzReviewerBf2ProbeTests
{
    //  Primary Cost Category:
    //    HQ                      (root)
    //      Region-West           (child)
    //        Branch-Pune         (grandchild)
    //      Region-East           (child, nothing posted)
    //  Projects (a SECOND category):
    //    Proj-Alpha
    //
    //  Postings inside Apr-Jun 2024:
    //    HQ            1,000 Dr
    //    Region-West   2,000 Dr
    //    Branch-Pune   4,000 Dr      <-- two levels below HQ
    //    Proj-Alpha    7,000 Dr      <-- a different category entirely
    private static Company Seed(
        out CostCentre hq, out CostCentre west, out CostCentre pune,
        out CostCentre east, out CostCentre alpha)
    {
        var c = CompanyFactory.CreateSeeded("Reviewer Probe Co", new DateOnly(2024, 3, 1));
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(salaries);

        var primary = c.FindCostCategoryByName("Primary Cost Category")!;
        var projects = new CostCategory(Guid.NewGuid(), "Projects");
        c.AddCostCategory(projects);

        hq = new CostCentre(Guid.NewGuid(), "HQ", primary.Id);
        c.AddCostCentre(hq);
        west = new CostCentre(Guid.NewGuid(), "Region-West", primary.Id, parentId: hq.Id);
        c.AddCostCentre(west);
        east = new CostCentre(Guid.NewGuid(), "Region-East", primary.Id, parentId: hq.Id);
        c.AddCostCentre(east);
        pune = new CostCentre(Guid.NewGuid(), "Branch-Pune", primary.Id, parentId: west.Id);
        c.AddCostCentre(pune);
        alpha = new CostCentre(Guid.NewGuid(), "Proj-Alpha", projects.Id);
        c.AddCostCentre(alpha);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);
        void Spend(decimal amt, Guid catId, CostCentre centre, DateOnly d) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, d, new[]
            {
                new EntryLine(salaries.Id, Money.FromRupees(amt), DrCr.Debit, billAllocations: null,
                    costAllocations: new[] { new CostAllocation(catId, centre.Id, Money.FromRupees(amt)) }),
                new EntryLine(cash.Id, Money.FromRupees(amt), DrCr.Credit),
            }));

        Spend(1000m, primary.Id, hq, new DateOnly(2024, 4, 2));
        Spend(2000m, primary.Id, west, new DateOnly(2024, 4, 3));
        Spend(4000m, primary.Id, pune, new DateOnly(2024, 4, 4));
        Spend(7000m, projects.Id, alpha, new DateOnly(2024, 4, 5));
        return c;
    }

    private static Budget Window(params BudgetLine[] lines) =>
        new(Guid.NewGuid(), "Probe", new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), lines: lines);

    private static BudgetVarianceLine Row(Company c, Guid centreId, decimal budgeted) =>
        Assert.Single(BudgetVarianceReport.Build(c, Window(
            BudgetLine.ForCostCentre(centreId, BudgetType.OnNettTransactions,
                Money.FromRupees(budgeted)))).Lines);

    /// <summary>Three levels: HQ must see 1,000 + 2,000 + 4,000 = 7,000, not 1,000 and not 3,000.</summary>
    [Fact]
    public void Reviewer_a_root_centre_consolidates_two_levels_below_it()
    {
        var c = Seed(out var hq, out _, out _, out _, out _);
        var row = Row(c, hq.Id, 7000m);
        Assert.Equal(Money.FromRupees(7000m), row.Actual);
        Assert.Equal(Money.Zero, row.Variance);
        Assert.Equal("Cost Centre", row.TargetKindLabel);
    }

    /// <summary>The MIDDLE node sees itself + its own grandchild subtree only: 2,000 + 4,000 = 6,000.</summary>
    [Fact]
    public void Reviewer_a_middle_centre_sees_its_own_subtree_only()
    {
        var c = Seed(out _, out var west, out _, out _, out _);
        Assert.Equal(Money.FromRupees(6000m), Row(c, west.Id, 6000m).Actual);
    }

    /// <summary>A childless SIBLING with nothing posted stays at zero — the walk is not "whole category".</summary>
    [Fact]
    public void Reviewer_an_empty_sibling_stays_at_zero()
    {
        var c = Seed(out _, out _, out _, out var east, out _);
        Assert.Equal(Money.Zero, Row(c, east.Id, 1000m).Actual);
    }

    /// <summary>A centre in ANOTHER cost category never leaks into HQ's consolidated figure.</summary>
    [Fact]
    public void Reviewer_a_second_category_never_leaks_into_the_roll_up()
    {
        var c = Seed(out var hq, out _, out _, out _, out var alpha);
        Assert.Equal(Money.FromRupees(7000m), Row(c, hq.Id, 0m).Actual);     // HQ subtree only
        Assert.Equal(Money.FromRupees(7000m), Row(c, alpha.Id, 0m).Actual);  // and Alpha only itself
        // Identical magnitudes by construction, so also assert they are DIFFERENT centres' figures.
        Assert.NotEqual(hq.Id, alpha.Id);
    }
}
