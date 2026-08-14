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

    /// <summary>Rupees → paisa. Throws if the amount is not exact to 2 decimal places. Delegates to
    /// <see cref="PaisaConversion.ToPaisaExact(decimal)"/> — the ONE rupees→paisa rule (drift lock D3). This is
    /// the EXACT semantics: the store is the system of record, so silent precision loss is unacceptable.</summary>
    public static long FromDecimal(decimal rupees) => PaisaConversion.ToPaisaExact(rupees);

    /// <summary>Paisa → rupees as an exact decimal.</summary>
    public static decimal ToDecimal(long paisa) => PaisaConversion.ToRupees(paisa);

    /// <summary>Paisa → <see cref="Money"/>.</summary>
    public static Money ToMoney(long paisa) => Money.FromRupees(ToDecimal(paisa));
}
