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
    /// <summary>
    /// The financial-year start a creation with NO typed date gets: 1-Apr of the current calendar year.
    ///
    /// <para><b>Exposed because a screen has to be able to predict it.</b> The company profile screen refuses
    /// <c>BooksBeginFrom &lt; FinancialYearStart</c> before anything is created — but on the CREATION path
    /// there is no aggregate to read the year start from, so the screen has to compare the typed books date
    /// against the value <see cref="CreateSeeded"/> is about to substitute. Reading it from here rather than
    /// re-deriving it means the guard and the factory cannot drift apart; when they did, typing only a books
    /// date earlier than 1-Apr of this year sailed past the screen guard and threw
    /// <see cref="ArgumentException"/> out of <see cref="Company"/>'s constructor, unhandled, to the UI
    /// dispatcher.</para>
    /// </summary>
    public static DateOnly DefaultFinancialYearStart => new(DateTime.Today.Year, 4, 1);

    /// <summary>Creates a fully seeded company.</summary>
    /// <param name="name">Company name (required).</param>
    /// <param name="financialYearStart">Defaults to <see cref="DefaultFinancialYearStart"/>.</param>
    /// <param name="booksBeginFrom">Defaults to <paramref name="financialYearStart"/>.</param>
    public static Company CreateSeeded(
        string name,
        DateOnly? financialYearStart = null,
        DateOnly? booksBeginFrom = null)
    {
        var fyStart = financialYearStart ?? DefaultFinancialYearStart;
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

        // Base currency ₹/INR as a first-class Currency (catalog §2/§20 Multi-currency).
        company.AddCurrency(SeedCurrencies.BuildBaseCurrency(company));

        // Default godown "Main Location" (catalog §9 Inventory). No sample stock items are seeded.
        company.AddGodown(SeedGodowns.BuildMainLocation(company.MainLocationName));

        return company;
    }

    /// <summary>The canonical seed group set, for the seed-verification test.</summary>
    public static IReadOnlyList<Group> SeedGroupSet() => SeedGroups.Build();

    /// <summary>The canonical seed voucher-type set, for the seed-verification test.</summary>
    public static IReadOnlyList<VoucherType> SeedVoucherTypeSet() => SeedVoucherTypes.Build();
}
