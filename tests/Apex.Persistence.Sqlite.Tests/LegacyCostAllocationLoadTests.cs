using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The G-2 migration contract. Cost categories are parallel allocation axes, not a partition (spec §4.2
/// rule C-27), so the posting invariant became per-category. That is a <b>breaking change to a persisted
/// validation contract</b>: a company saved under the old partition rule can hold a line whose axes foot
/// only when added together, and <see cref="SqliteCompanyStore"/>'s <c>Load</c> re-posts every stored
/// voucher through the engine — so under the strict rule such a file would throw on OPEN and the whole
/// company would be unreachable.
/// <para>These tests pin the resolution: <b>no schema change</b> (the stored shape — one
/// <c>cost_allocations</c> row per (category, centre, amount) with no uniqueness constraint — already
/// expresses parallel sets), <c>Load</c> validates with <see cref="CostAllocationStrictness.Legacy"/> so old
/// books still open verbatim, and <see cref="CostAllocationDiagnostics"/> lists what a human needs to
/// re-allocate. Odd paisa throughout.</para>
/// </summary>
public sealed class LegacyCostAllocationLoadTests
{
    private static readonly Money Travel = Money.FromRupees(5000.37m);

    private static Company SeedBooks(
        out Domain.Ledger travelling, out CostCategory branch, out CostCentre kolkata,
        out CostCategory department, out CostCentre marketing, out VoucherType journal)
    {
        var c = CompanyFactory.CreateSeeded("Legacy Cost Co", new DateOnly(2024, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        travelling = new Domain.Ledger(Guid.NewGuid(), "Travelling Expenses",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(travelling);

        branch = new CostCategory(Guid.NewGuid(), "Branch");
        department = new CostCategory(Guid.NewGuid(), "Department");
        c.AddCostCategory(branch);
        c.AddCostCategory(department);
        kolkata = new CostCentre(Guid.NewGuid(), "Kolkata", branch.Id);
        marketing = new CostCentre(Guid.NewGuid(), "Marketing", department.Id);
        c.AddCostCentre(kolkata);
        c.AddCostCentre(marketing);

        journal = c.FindVoucherTypeByName("Journal")!;
        return c;
    }

    private static Voucher TravelVoucher(
        Company c, Guid journalId, Guid travellingId, Money amount, params CostAllocation[] allocations) =>
        new(Guid.NewGuid(), journalId, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(travellingId, amount, DrCr.Debit, billAllocations: null, costAllocations: allocations),
            new EntryLine(c.FindLedgerByName("Cash")!.Id, amount, DrCr.Credit),
        });

    /// <summary>
    /// A company holding exactly what the old partition rule produced: a ₹5,000.37 line "split" ₹3,000.19 to
    /// Branch → Kolkata and ₹2,000.18 to Department → Marketing. Built through
    /// <see cref="CostAllocationStrictness.Legacy"/> because the strict engine — correctly — will no longer
    /// accept it from an entry path.
    /// </summary>
    private static Company SeedLegacyBooks(out Guid voucherId, out Guid branchId, out Guid departmentId)
    {
        var c = SeedBooks(out var travelling, out var branch, out var kolkata,
            out var department, out var marketing, out var journal);

        var legacy = TravelVoucher(c, journal.Id, travelling.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));
        new LedgerService(c).Post(legacy, CostAllocationStrictness.Legacy);

        voucherId = legacy.Id;
        branchId = branch.Id;
        departmentId = department.Id;
        return c;
    }

    [Fact]
    public void A_company_saved_under_the_old_partition_rule_still_opens()
    {
        var dbPath = TempDbFile.NewPath("apex-legacycost");
        try
        {
            var original = SeedLegacyBooks(out var voucherId, out var branchId, out var departmentId);

            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;   // must NOT throw — this is the migration guarantee

            var line = reloaded.Vouchers.Single(v => v.Id == voucherId)
                .Lines.Single(l => l.HasCostAllocations);
            Assert.Equal(Travel, line.Amount);
            // The stored allocations are preserved verbatim — nothing is silently rescaled or dropped.
            Assert.Equal(2, line.CostAllocations.Count);
            Assert.Equal(Money.FromRupees(3000.19m), line.CostAllocationTotalFor(branchId));
            Assert.Equal(Money.FromRupees(2000.18m), line.CostAllocationTotalFor(departmentId));
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    public void The_reloaded_legacy_line_is_listed_for_human_re_allocation()
    {
        var dbPath = TempDbFile.NewPath("apex-legacycost-diag");
        try
        {
            var original = SeedLegacyBooks(out var voucherId, out _, out _);
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            var flagged = Assert.Single(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(reloaded));
            Assert.Equal(voucherId, flagged.VoucherId);
            Assert.Equal(Travel, flagged.LineAmount);
            Assert.Equal(2, flagged.ShortCategories.Count);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    public void A_conforming_parallel_set_company_round_trips_and_is_not_flagged()
    {
        var dbPath = TempDbFile.NewPath("apex-parallelcost");
        try
        {
            var c = SeedBooks(out var travelling, out var branch, out var kolkata,
                out var department, out var marketing, out var journal);

            new LedgerService(c).Post(TravelVoucher(c, journal.Id, travelling.Id, Travel,
                new CostAllocation(branch.Id, kolkata.Id, Travel),
                new CostAllocation(department.Id, marketing.Id, Travel)));

            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(c);

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(c.Id)!;

            // Two rows under two different categories survive the round trip on the SAME line — proof the
            // existing cost_allocations shape needs no schema change to express parallel sets.
            var line = reloaded.Vouchers.SelectMany(v => v.Lines).Single(l => l.HasCostAllocations);
            Assert.Equal(2, line.CostAllocations.Count);
            Assert.Equal(Travel, line.CostAllocationTotalFor(branch.Id));
            Assert.Equal(Travel, line.CostAllocationTotalFor(department.Id));
            Assert.Empty(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(reloaded));

            // The report reads one ₹5,000.37 of cost, not ₹10,000.74.
            var summary = CostReports.BuildCategorySummary(
                reloaded, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));
            Assert.Equal(Travel, summary.Categories.Single(r => r.CategoryId == branch.Id).Total);
            Assert.Equal(Travel, summary.Categories.Single(r => r.CategoryId == department.Id).Total);
            Assert.Equal(Travel, summary.GrandTotal);
            Assert.True(summary.CategoryTotalsOverlap);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    public void The_schema_version_is_unchanged_by_the_parallel_set_fix()
    {
        // G-2 is a validation-contract change, NOT a storage change: cost_allocations already stores one row
        // per (category, centre, amount) with no uniqueness constraint across categories, so a parallel set
        // is simply two rows. Nothing to migrate, no version bump.
        //
        // The literal below is a deliberate tripwire against an ACCIDENTAL bump, so it moves only when a slice
        // knowingly owns a version. It was 50; v51 was owned by WF-1 (the GST five-level hierarchy masters);
        // **v52 is owned by the VOUCHER EDIT LOG**, which adds ONE new table (`voucher_edit_log`) and no column
        // to any existing table — so it touches nothing in cost_allocations either, and the G-2 contract this
        // test guards is still storage-free.
        Assert.Equal(52, Schema.CurrentVersion);
    }
}
