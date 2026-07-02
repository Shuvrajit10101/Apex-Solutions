using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The 28 predefined groups (verification §A6/A7): 15 Primary (9 Balance-Sheet + 6 P&amp;L)
/// + 13 Sub-groups. Nature inherits from the primary ancestor. "Bank OCC A/c" is an
/// <b>alias</b> of Bank OD A/c (not a 29th group); P&amp;L A/c is a <b>ledger</b>, not a group.
/// </summary>
public static class SeedGroups
{
    /// <summary>Canonical names of the 6 Profit &amp; Loss primary groups.</summary>
    public static readonly IReadOnlyList<string> ProfitAndLossPrimaries = new[]
    {
        "Sales Accounts",
        "Purchase Accounts",
        "Direct Incomes",
        "Indirect Incomes",
        "Direct Expenses",
        "Indirect Expenses",
    };

    private readonly record struct Def(string Name, GroupNature Nature, string? Parent, string? Alias = null);

    // Order matters only for readability; parents are resolved by name after all are created.
    private static readonly Def[] Definitions =
    {
        // --- 15 Primary (9 Balance-Sheet) ---
        new("Capital Account",         GroupNature.Liability, null),
        new("Loans (Liability)",       GroupNature.Liability, null),
        new("Current Liabilities",     GroupNature.Liability, null),
        new("Fixed Assets",            GroupNature.Asset,     null),
        new("Investments",             GroupNature.Asset,     null),
        new("Current Assets",          GroupNature.Asset,     null),
        new("Branch / Divisions",      GroupNature.Asset,     null),
        new("Misc. Expenses (Asset)",  GroupNature.Asset,     null),
        new("Suspense A/c",            GroupNature.Liability, null),

        // --- 15 Primary (6 P&L) ---
        new("Sales Accounts",          GroupNature.Income,    null),
        new("Purchase Accounts",       GroupNature.Expense,   null),
        new("Direct Incomes",          GroupNature.Income,    null),
        new("Indirect Incomes",        GroupNature.Income,    null),
        new("Direct Expenses",         GroupNature.Expense,   null),
        new("Indirect Expenses",       GroupNature.Expense,   null),

        // --- 13 Sub-groups ---
        new("Reserves & Surplus",          GroupNature.Liability, "Capital Account"),
        new("Bank OD A/c",                 GroupNature.Liability, "Loans (Liability)", "Bank OCC A/c"),
        new("Secured Loans",               GroupNature.Liability, "Loans (Liability)"),
        new("Unsecured Loans",             GroupNature.Liability, "Loans (Liability)"),
        new("Duties & Taxes",              GroupNature.Liability, "Current Liabilities"),
        new("Provisions",                  GroupNature.Liability, "Current Liabilities"),
        new("Sundry Creditors",            GroupNature.Liability, "Current Liabilities"),
        new("Bank Accounts",               GroupNature.Asset,     "Current Assets"),
        new("Cash-in-Hand",                GroupNature.Asset,     "Current Assets"),
        new("Deposits (Asset)",            GroupNature.Asset,     "Current Assets"),
        new("Loans & Advances (Asset)",    GroupNature.Asset,     "Current Assets"),
        new("Stock-in-Hand",               GroupNature.Asset,     "Current Assets"),
        new("Sundry Debtors",              GroupNature.Asset,     "Current Assets"),
    };

    /// <summary>Count guard: exactly 28.</summary>
    public const int Count = 28;

    /// <summary>Builds the 28 predefined <see cref="Group"/> instances (parents resolved by name).</summary>
    public static IReadOnlyList<Group> Build()
    {
        // First pass: create every group with a fresh id, parent unresolved.
        var byName = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Group>(Definitions.Length);

        foreach (var d in Definitions)
        {
            var g = new Group(Guid.NewGuid(), d.Name, d.Nature, parentId: null, alias: d.Alias, isPredefined: true);
            byName[d.Name] = g;
            result.Add(g);
        }

        // Second pass: wire parents by name.
        for (var i = 0; i < Definitions.Length; i++)
        {
            var d = Definitions[i];
            if (d.Parent is null) continue;
            if (!byName.TryGetValue(d.Parent, out var parent))
                throw new InvalidOperationException($"Seed parent '{d.Parent}' not found for group '{d.Name}'.");
            result[i].ParentId = parent.Id;
        }

        if (result.Count != Count)
            throw new InvalidOperationException($"Seed produced {result.Count} groups; expected {Count}.");

        return result;
    }
}
