using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The default godown seeded on create (catalog §9/§22; requirements RQ-5): a single
/// <b>"Main Location"</b> flagged <see cref="Godown.IsMainLocation"/> (it cannot be deleted). Its name is
/// taken from <see cref="Company.MainLocationName"/> so a renamed default stays consistent. Sample stock
/// items are NOT seeded — a fresh company starts with masters only.
/// </summary>
public static class SeedGodowns
{
    public const int Count = 1;

    /// <summary>Builds the single predefined "Main Location" godown for <paramref name="name"/>.</summary>
    public static Godown BuildMainLocation(string name) =>
        new(Guid.NewGuid(), name, isMainLocation: true);
}
