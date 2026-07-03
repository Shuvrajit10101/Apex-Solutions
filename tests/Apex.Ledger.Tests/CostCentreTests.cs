using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Cost Categories &amp; Cost Centres tests (catalog §6; plan.md §5): the seeded Primary Cost Category,
/// category validation (≥1 allocate flag), hierarchical centres, cost-centres-applicable defaulting by
/// nature (overridable), cost allocations that must sum to the line and are rejected on a non-applicable
/// ledger, splitting across two centres, and the three cost reports (Category Summary, Cost Centre
/// Break-up with hierarchical roll-up, Ledger Break-up).
/// </summary>
public class CostCentreTests
{
    // A company with a Salaries expense ledger (cost centres auto-applicable), Cash, a Sales income
    // ledger, and one Furniture asset ledger (non-applicable). Plus a Primary Cost Category with two
    // top centres (Delhi, Mumbai) and a child of Delhi (Delhi-North).
    private static Company Seed(
        out Domain.Ledger cash,
        out Domain.Ledger salaries,
        out Domain.Ledger rent,
        out Domain.Ledger furniture,
        out CostCategory category,
        out CostCentre delhi,
        out CostCentre mumbai,
        out CostCentre delhiNorth,
        out VoucherType journal)
    {
        var c = CompanyFactory.CreateSeeded("Cost Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(salaries);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        furniture = new Domain.Ledger(Guid.NewGuid(), "Furniture", c.FindGroupByName("Fixed Assets")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(furniture);

        category = c.FindCostCategoryByName("Primary Cost Category")!;
        delhi = new CostCentre(Guid.NewGuid(), "Delhi", category.Id);
        mumbai = new CostCentre(Guid.NewGuid(), "Mumbai", category.Id);
        c.AddCostCentre(delhi);
        c.AddCostCentre(mumbai);
        delhiNorth = new CostCentre(Guid.NewGuid(), "Delhi-North", category.Id, parentId: delhi.Id);
        c.AddCostCentre(delhiNorth);

        journal = c.FindVoucherTypeByName("Journal")!;
        return c;
    }

    // ---- seed / master fields ----

    [Fact]
    public void Company_seeds_a_predefined_primary_cost_category()
    {
        var c = CompanyFactory.CreateSeeded("Seed Co", new DateOnly(2024, 4, 1));
        var primary = Assert.Single(c.CostCategories);
        Assert.Equal("Primary Cost Category", primary.Name);
        Assert.True(primary.IsPredefined);
        Assert.True(primary.AllocateRevenueItems);
    }

    [Fact]
    public void Cost_category_requires_at_least_one_allocate_flag()
    {
        Assert.Throws<ArgumentException>(() =>
            new CostCategory(Guid.NewGuid(), "Bad", allocateRevenueItems: false, allocateNonRevenueItems: false));

        // Either one alone is fine.
        var rev = new CostCategory(Guid.NewGuid(), "Rev", allocateRevenueItems: true, allocateNonRevenueItems: false);
        var nonrev = new CostCategory(Guid.NewGuid(), "NonRev", allocateRevenueItems: false, allocateNonRevenueItems: true);
        Assert.True(rev.AllocateRevenueItems);
        Assert.True(nonrev.AllocateNonRevenueItems);
    }

    [Fact]
    public void Cost_centre_hierarchy_records_parent_and_primary_flag()
    {
        var c = Seed(out _, out _, out _, out _, out var cat, out var delhi, out _, out var delhiNorth, out _);

        Assert.True(delhi.IsPrimary);
        Assert.Null(delhi.ParentId);
        Assert.False(delhiNorth.IsPrimary);
        Assert.Equal(delhi.Id, delhiNorth.ParentId);
        // Both belong to the same category.
        Assert.Equal(cat.Id, delhi.CategoryId);
        Assert.Equal(cat.Id, delhiNorth.CategoryId);
        Assert.Equal(3, c.CostCentres.Count);
    }

    // ---- cost-centres-applicable defaulting ----

    [Fact]
    public void Cost_centres_applicable_defaults_true_for_income_and_expense_ledgers()
    {
        var c = Seed(out _, out var salaries, out _, out var furniture, out _, out _, out _, out _, out _);

        // Salaries is an Indirect Expense (Expense nature) → auto-applicable.
        Assert.Null(salaries.CostCentresApplicable); // stored as auto
        Assert.True(ClassificationRules.CostCentresApplicableFor(salaries, c));

        // A Sales income ledger is also auto-applicable.
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        Assert.True(ClassificationRules.CostCentresApplicableFor(sales, c));
    }

    [Fact]
    public void Cost_centres_applicable_defaults_false_for_non_revenue_ledgers()
    {
        var c = Seed(out var cash, out _, out _, out var furniture, out _, out _, out _, out _, out _);

        // Cash (Cash-in-Hand → Asset) and Furniture (Fixed Assets → Asset) are non-revenue → not applicable.
        Assert.False(ClassificationRules.CostCentresApplicableFor(cash, c));
        Assert.False(ClassificationRules.CostCentresApplicableFor(furniture, c));
    }

    [Fact]
    public void Cost_centres_applicable_is_overridable_in_both_directions()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out _, out _, out _, out _, out _);

        // Force an expense ledger OFF.
        salaries.CostCentresApplicable = false;
        Assert.False(ClassificationRules.CostCentresApplicableFor(salaries, c));

        // Force an asset ledger ON.
        cash.CostCentresApplicable = true;
        Assert.True(ClassificationRules.CostCentresApplicableFor(cash, c));
    }

    // ---- allocation must sum to the line ----

    [Fact]
    public void Cost_allocation_must_sum_to_the_line_amount()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out var cat, out var delhi, out var mumbai, out _, out var journal);
        var svc = new LedgerService(c);

        // 10000 salary allocated 6000 Delhi + 3000 Mumbai = 9000 ≠ 10000 → rejected.
        var bad = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(6000m)),
                new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(3000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Cost_allocation_that_sums_to_the_line_posts()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out var cat, out var delhi, out var mumbai, out _, out var journal);
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(6000m)),
                new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(4000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        var v = Assert.Single(c.Vouchers);
        var line = v.Lines.Single(l => l.LedgerId == salaries.Id);
        Assert.Equal(2, line.CostAllocations.Count);
        Assert.Equal(Money.FromRupees(10000m), line.CostAllocationTotal);
    }

    // ---- allocation rejected on a non-applicable ledger ----

    [Fact]
    public void Cost_allocation_on_a_non_applicable_ledger_is_rejected()
    {
        var c = Seed(out var cash, out _, out _, out var furniture, out var cat, out var delhi, out _, out _, out var journal);
        var svc = new LedgerService(c);

        // Furniture is a fixed asset → cost centres NOT applicable by default → allocation rejected.
        var bad = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(furniture.Id, Money.FromRupees(5000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(5000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Cost_allocation_referencing_a_centre_outside_its_category_is_rejected()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out _, out var delhi, out _, out _, out var journal);
        var svc = new LedgerService(c);

        // A second category whose id does not own the Delhi centre.
        var other = new CostCategory(Guid.NewGuid(), "Departments");
        c.AddCostCategory(other);

        var bad = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(5000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(other.Id, delhi.Id, Money.FromRupees(5000m)), // Delhi belongs to Primary, not other
            }),
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
    }

    // ---- split across two centres ----

    [Fact]
    public void Split_across_two_centres_reports_each_centre()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out var cat, out var delhi, out var mumbai, out _, out var journal);
        var svc = new LedgerService(c);
        var from = new DateOnly(2024, 4, 1);
        var to = new DateOnly(2024, 4, 30);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(7000m)),
                new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(3000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        var breakup = CostReports.BuildCostCentreBreakup(c, from, to);
        Assert.Equal(Money.FromRupees(7000m), breakup.Centres.Single(l => l.CentreName == "Delhi").OwnTotal);
        Assert.Equal(Money.FromRupees(3000m), breakup.Centres.Single(l => l.CentreName == "Mumbai").OwnTotal);
    }

    // ---- reports ----

    private static Company SeedWithPostings(
        out CostCategory cat, out CostCentre delhi, out CostCentre mumbai, out CostCentre delhiNorth,
        out Domain.Ledger salaries, out Domain.Ledger rent)
    {
        var c = Seed(out var cash, out salaries, out rent, out _, out cat, out delhi, out mumbai, out delhiNorth, out var journal);
        var svc = new LedgerService(c);

        // Salaries 10000: Delhi 6000 (of which Delhi-North child gets a separate posting later), Mumbai 4000.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(6000m)),
                new CostAllocation(cat.Id, mumbai.Id, Money.FromRupees(4000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        // Rent 3000: all to Delhi-North (child of Delhi).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhiNorth.Id, Money.FromRupees(3000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Credit),
        }));

        return c;
    }

    [Fact]
    public void Category_summary_totals_all_allocations_under_the_category()
    {
        var c = SeedWithPostings(out var cat, out _, out _, out _, out _, out _);
        var report = CostReports.BuildCategorySummary(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));

        var row = Assert.Single(report.Categories);
        Assert.Equal(cat.Id, row.CategoryId);
        // 6000 + 4000 (salaries) + 3000 (rent) = 13000.
        Assert.Equal(Money.FromRupees(13000m), row.Total);
        Assert.Equal(Money.FromRupees(13000m), report.GrandTotal);
    }

    [Fact]
    public void Cost_centre_breakup_rolls_up_children_into_the_parent()
    {
        var c = SeedWithPostings(out _, out var delhi, out var mumbai, out var delhiNorth, out _, out _);
        var report = CostReports.BuildCostCentreBreakup(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));

        var delhiLine = report.Centres.Single(l => l.CentreName == "Delhi");
        var childLine = report.Centres.Single(l => l.CentreName == "Delhi-North");
        var mumbaiLine = report.Centres.Single(l => l.CentreName == "Mumbai");

        // Delhi has own 6000; Delhi-North own 3000; Delhi rolled-up = 6000 + 3000 = 9000.
        Assert.Equal(Money.FromRupees(6000m), delhiLine.OwnTotal);
        Assert.Equal(Money.FromRupees(9000m), delhiLine.RolledUpTotal);
        Assert.Equal(Money.FromRupees(3000m), childLine.OwnTotal);
        Assert.Equal(Money.FromRupees(3000m), childLine.RolledUpTotal);
        Assert.Equal(Money.FromRupees(4000m), mumbaiLine.OwnTotal);

        // Hierarchy: the child appears immediately after its parent, one level deeper.
        Assert.Equal(0, delhiLine.Depth);
        Assert.Equal(1, childLine.Depth);
        var delhiIndex = report.Centres.ToList().FindIndex(l => l.CentreName == "Delhi");
        Assert.Equal("Delhi-North", report.Centres[delhiIndex + 1].CentreName);

        // Grand total over OWN totals = 6000 + 3000 + 4000 = 13000 (no double-count).
        Assert.Equal(Money.FromRupees(13000m), report.GrandTotal);
    }

    [Fact]
    public void Ledger_breakup_totals_per_centre_per_ledger()
    {
        var c = SeedWithPostings(out _, out var delhi, out var mumbai, out var delhiNorth, out var salaries, out var rent);
        var report = CostReports.BuildLedgerBreakup(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));

        // Delhi ← Salaries 6000 only.
        var delhiRows = report.ForCentre(delhi.Id);
        var delhiRow = Assert.Single(delhiRows);
        Assert.Equal("Salaries", delhiRow.LedgerName);
        Assert.Equal(Money.FromRupees(6000m), delhiRow.Total);

        // Mumbai ← Salaries 4000.
        var mumbaiRow = Assert.Single(report.ForCentre(mumbai.Id));
        Assert.Equal("Salaries", mumbaiRow.LedgerName);
        Assert.Equal(Money.FromRupees(4000m), mumbaiRow.Total);

        // Delhi-North ← Rent 3000.
        var childRow = Assert.Single(report.ForCentre(delhiNorth.Id));
        Assert.Equal("Rent", childRow.LedgerName);
        Assert.Equal(Money.FromRupees(3000m), childRow.Total);
    }

    [Fact]
    public void Reports_exclude_cancelled_vouchers()
    {
        var c = Seed(out var cash, out var salaries, out _, out _, out var cat, out var delhi, out _, out _, out var journal);
        var svc = new LedgerService(c);

        var v = svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(5000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(cat.Id, delhi.Id, Money.FromRupees(5000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        // Before cancel: 5000 shows.
        var from = new DateOnly(2024, 4, 1);
        var to = new DateOnly(2024, 4, 30);
        Assert.Equal(Money.FromRupees(5000m), CostReports.BuildCategorySummary(c, from, to).GrandTotal);

        svc.Cancel(v.Id);
        // After cancel: nothing.
        Assert.Equal(Money.Zero, CostReports.BuildCategorySummary(c, from, to).GrandTotal);
        Assert.Empty(CostReports.BuildCategorySummary(c, from, to).Categories);
    }
}
