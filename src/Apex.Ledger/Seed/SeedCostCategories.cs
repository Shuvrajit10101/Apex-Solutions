using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The default cost category seeded on create (catalog §6/§22): a single <b>"Primary Cost Category"</b>
/// that allocates revenue items (the common default) and is predefined (cannot be deleted). Its name is
/// taken from <see cref="Company.PrimaryCostCategoryName"/> so a renamed default stays consistent.
/// </summary>
public static class SeedCostCategories
{
    public const int Count = 1;

    /// <summary>Builds the single predefined "Primary Cost Category" for <paramref name="name"/>.</summary>
    public static CostCategory BuildPrimary(string name) =>
        new(Guid.NewGuid(), name, allocateRevenueItems: true, allocateNonRevenueItems: false, isPredefined: true);
}
