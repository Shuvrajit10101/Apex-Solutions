using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// <b>The cost-centre budget roll-up, measured off a RELOADED book.</b> Originated as a wave-3 reviewer
/// probe for BF-2 and KEPT DELIBERATELY at merge (A12, 2026-10-18) rather than deleted with its two
/// siblings, because it is the only cover in the tree for this leg.
///
/// <para><b>Why it survives where the other two probes did not.</b> BF-2's roll-up is already pinned
/// in-memory by <c>CostCentreBudgetVarianceTests</c>, and BF-8's FK regression by
/// <c>VoucherTypeDefaultBankResaveTests</c> — so those probes were redundant and were removed. This one is
/// not: it measures the parent-centre consolidation at depth TWO off a book SAVED to SQLite and REOPENED
/// through a fresh <c>SqliteCompanyStore</c> handle, so the <c>budget_lines.cost_centre_id</c> round trip
/// and the consolidation are proved TOGETHER. <c>BudgetRoundTripTests</c> is the nearest neighbour and does
/// reload and does call <c>BudgetVarianceReport.Build</c>, but all three of its lines are LEDGER and GROUP
/// targets (<c>IsLedgerTarget</c>, <c>IsGroupTarget</c>) — it carries no cost-centre line and no parent
/// roll-up at all. Its second test is likewise the only cover for a cost-centre budget line surviving
/// REPEATED saves, which is BF-8's shape applied to the other v67 foreign key.</para>
/// </summary>
public sealed class CostCentreBudgetReloadTests
{
    //  Primary Cost Category:  Zone-North (root) > Area-Delhi (child) > Branch-Karol (grandchild)
    //  Postings in the budget window: Zone 1,500 Dr · Area 2,500 Dr · Branch 5,000 Dr = 9,000
    private static Company Seed(out Guid zoneId, out Guid areaId, out Guid branchId)
    {
        var c = CompanyFactory.CreateSeeded("Reviewer BF2 Reload Co", new DateOnly(2024, 3, 1));
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var primary = c.FindCostCategoryByName("Primary Cost Category")!;
        var zone = new CostCentre(Guid.NewGuid(), "Zone-North", primary.Id);
        c.AddCostCentre(zone);
        var area = new CostCentre(Guid.NewGuid(), "Area-Delhi", primary.Id, parentId: zone.Id);
        c.AddCostCentre(area);
        var branch = new CostCentre(Guid.NewGuid(), "Branch-Karol", primary.Id, parentId: area.Id);
        c.AddCostCentre(branch);
        zoneId = zone.Id; areaId = area.Id; branchId = branch.Id;

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);
        void Spend(decimal amt, CostCentre centre, DateOnly d) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, d, new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(amt), DrCr.Debit, billAllocations: null,
                    costAllocations: new[] { new CostAllocation(primary.Id, centre.Id, Money.FromRupees(amt)) }),
                new EntryLine(cash.Id, Money.FromRupees(amt), DrCr.Credit),
            }));

        Spend(1500m, zone, new DateOnly(2024, 4, 2));
        Spend(2500m, area, new DateOnly(2024, 4, 3));
        Spend(5000m, branch, new DateOnly(2024, 4, 4));

        c.AddBudget(new Budget(Guid.NewGuid(), "Reload Probe",
            new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), lines: new[]
            {
                BudgetLine.ForCostCentre(zone.Id, BudgetType.OnNettTransactions, Money.FromRupees(9000m)),
            }));
        return c;
    }

    /// <summary>
    /// Save, reopen through a FRESH store handle, and read the figure off the reloaded aggregate: the
    /// root centre's actual must be 9,000 (its own 1,500 plus a child 2,500 plus a GRANDCHILD 5,000) and
    /// the variance must be zero. Before the BF-2 fix this read 1,500 / −7,500.
    /// </summary>
    [Fact]
    public void The_rollup_holds_off_a_reloaded_book_at_depth_two()
    {
        var dbPath = TempDbFile.NewPath("apex-rev-bf2-reload");
        try
        {
            var company = Seed(out var zoneId, out var areaId, out _);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            using var fresh = new SqliteCompanyStore(dbPath);
            var reloaded = fresh.Load(fresh.ListCompanies().Single().Id);

            // The v67 target itself survived the round trip — not a group, not a ledger, a cost centre.
            var line = Assert.Single(Assert.Single(reloaded.Budgets).Lines);
            Assert.Equal(zoneId, line.CostCentreId);
            Assert.Null(line.GroupId);
            Assert.Null(line.LedgerId);

            var row = Assert.Single(BudgetVarianceReport.Build(reloaded, Assert.Single(reloaded.Budgets)).Lines);
            Assert.Equal("Cost Centre", row.TargetKindLabel);
            Assert.Equal(Money.FromRupees(9000m), row.Actual);
            Assert.Equal(Money.Zero, row.Variance);

            // And the middle node sees its own subtree only: 2,500 + 5,000.
            var midBudget = new Budget(Guid.NewGuid(), "Mid",
                new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), lines: new[]
                {
                    BudgetLine.ForCostCentre(areaId, BudgetType.OnNettTransactions, Money.FromRupees(7500m)),
                });
            Assert.Equal(Money.FromRupees(7500m),
                Assert.Single(BudgetVarianceReport.Build(reloaded, midBudget).Lines).Actual);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// A book carrying a cost-centre budget line must stay savable — the BF-8 shape applied to the OTHER
    /// FK v67 added (budget_lines.cost_centre_id → cost_centres(id)). Save twice, reload, save twice more.
    /// </summary>
    [Fact]
    public void A_cost_centre_budget_line_survives_repeated_saves()
    {
        var dbPath = TempDbFile.NewPath("apex-rev-bf2-resave");
        try
        {
            var company = Seed(out var zoneId, out _, out _);
            using (var store = new SqliteCompanyStore(dbPath))
            {
                store.Save(company);
                store.Save(company);
            }
            using var fresh = new SqliteCompanyStore(dbPath);
            var reloaded = fresh.Load(fresh.ListCompanies().Single().Id);
            fresh.Save(reloaded);
            fresh.Save(reloaded);
            var again = fresh.Load(fresh.ListCompanies().Single().Id);
            Assert.Equal(zoneId, Assert.Single(Assert.Single(again.Budgets).Lines).CostCentreId);
            Assert.Equal(3, again.Vouchers.Count);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
