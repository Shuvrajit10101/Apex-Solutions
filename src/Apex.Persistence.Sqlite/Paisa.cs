using Apex.Ledger;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// Exact conversion between a <see cref="Money"/> (decimal rupees) and its paisa representation
/// (a <see cref="long"/> integer = rupees × 100). Persisting money as INTEGER paisa keeps the
/// double-entry math exact — no binary-float rounding (NFR-3; accounting-core §6.4). Phase-1
/// amounts are 2-dp; a value carrying more than 2 decimal places would lose precision on the
/// round-trip, so the conversion asserts it is paisa-exact.
/// </summary>
internal static class Paisa
{
    /// <summary>Rupees → paisa. Throws if the amount is not exact to 2 decimal places.</summary>
    public static long FromMoney(Money money) => FromDecimal(money.Amount);

    /// <summary>Rupees → paisa. Throws if the amount is not exact to 2 decimal places.</summary>
    public static long FromDecimal(decimal rupees)
    {
        var scaled = rupees * 100m;
        var rounded = decimal.Truncate(scaled);
        if (scaled != rounded)
            throw new InvalidOperationException(
                $"Amount {rupees} is not paisa-exact (more than 2 decimal places); cannot persist without loss.");
        return (long)rounded;
    }

    /// <summary>Paisa → rupees as an exact decimal.</summary>
    public static decimal ToDecimal(long paisa) => paisa / 100m;

    /// <summary>Paisa → <see cref="Money"/>.</summary>
    public static Money ToMoney(long paisa) => Money.FromRupees(ToDecimal(paisa));
}
