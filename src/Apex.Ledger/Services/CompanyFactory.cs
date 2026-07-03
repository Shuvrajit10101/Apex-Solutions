using Apex.Ledger.Domain;
using Apex.Ledger.Seed;

namespace Apex.Ledger.Services;

/// <summary>
/// Creates a fully seeded <see cref="Company"/>: exactly 28 groups, 2 ledgers, 24 voucher
/// types, Primary Cost Category, Main Location, ₹/INR 2-dp "Paisa", FY 1-Apr→31-Mar
/// (design §5; plan.md §4.4). The seed is itself a fixture-backed unit test.
/// </summary>
public static class CompanyFactory
{
    /// <summary>Creates a fully seeded company.</summary>
    /// <param name="name">Company name (required).</param>
    /// <param name="financialYearStart">Defaults to 1-Apr of the current working year.</param>
    /// <param name="booksBeginFrom">Defaults to <paramref name="financialYearStart"/>.</param>
    public static Company CreateSeeded(
        string name,
        DateOnly? financialYearStart = null,
        DateOnly? booksBeginFrom = null)
    {
        var fyStart = financialYearStart ?? new DateOnly(DateTime.Today.Year, 4, 1);
        var books = booksBeginFrom ?? fyStart;

        var company = new Company(Guid.NewGuid(), name, fyStart, books);

        // 28 groups.
        foreach (var g in SeedGroups.Build())
            company.AddGroup(g);

        // Reserved P&L head (not one of the 28) + 2 default ledgers.
        var plHead = SeedLedgers.BuildProfitAndLossHead();
        company.SetProfitAndLossHead(plHead);

        var cashInHand = company.FindGroupByName("Cash-in-Hand")
            ?? throw new InvalidOperationException("Seed missing 'Cash-in-Hand' group.");

        foreach (var l in SeedLedgers.Build(cashInHand.Id, plHead.Id))
            company.AddLedger(l);

        // 24 voucher types.
        foreach (var t in SeedVoucherTypes.Build())
            company.AddVoucherType(t);

        // Default "Primary Cost Category" (catalog §6).
        company.AddCostCategory(SeedCostCategories.BuildPrimary(company.PrimaryCostCategoryName));

        return company;
    }

    /// <summary>The canonical seed group set, for the seed-verification test.</summary>
    public static IReadOnlyList<Group> SeedGroupSet() => SeedGroups.Build();

    /// <summary>The canonical seed voucher-type set, for the seed-verification test.</summary>
    public static IReadOnlyList<VoucherType> SeedVoucherTypeSet() => SeedVoucherTypes.Build();
}
