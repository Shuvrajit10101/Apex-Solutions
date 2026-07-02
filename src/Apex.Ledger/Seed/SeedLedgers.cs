using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The 2 default ledgers seeded on create (design §5.2):
/// <list type="bullet">
/// <item><b>Cash</b> under Cash-in-Hand, opening 0 Dr.</item>
/// <item><b>Profit &amp; Loss A/c</b> under the reserved P&amp;L head, opening 0 Cr.</item>
/// </list>
/// Both are <c>IsPredefined</c> and cannot be deleted.
/// </summary>
public static class SeedLedgers
{
    public const string CashName = "Cash";
    public const string ProfitAndLossName = "Profit & Loss A/c";
    public const int Count = 2;

    /// <summary>Builds the reserved Profit &amp; Loss head group (NOT one of the 28).</summary>
    public static Group BuildProfitAndLossHead() =>
        new(Guid.NewGuid(), ProfitAndLossName, GroupNature.Liability, parentId: null, isPredefined: true);

    /// <summary>
    /// Builds the 2 predefined ledgers. Requires the Cash-in-Hand group id and the
    /// reserved P&amp;L head group id (from <see cref="BuildProfitAndLossHead"/>).
    /// </summary>
    public static IReadOnlyList<Domain.Ledger> Build(Guid cashInHandGroupId, Guid profitAndLossHeadGroupId)
    {
        return new List<Domain.Ledger>
        {
            new(Guid.NewGuid(), CashName, cashInHandGroupId, Money.Zero, openingIsDebit: true, isPredefined: true),
            new(Guid.NewGuid(), ProfitAndLossName, profitAndLossHeadGroupId, Money.Zero, openingIsDebit: false, isPredefined: true),
        };
    }
}
