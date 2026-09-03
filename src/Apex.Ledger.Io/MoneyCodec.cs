using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// Exact conversion between <see cref="Money"/> (decimal rupees in memory) and the canonical wire form,
/// <b>integer paisa</b> (RQ-19). Rupees × 100 is the exact paisa value; a paisa-exact amount round-trips
/// with zero loss. A sub-paisa amount would truncate — the domain guarantees exported amounts are
/// paisa-exact (every <see cref="Money"/> that persists is), so this codec throws if handed a sub-paisa
/// amount rather than silently losing precision.
/// </summary>
public static class MoneyCodec
{
    /// <summary>The exact integer-paisa value of <paramref name="money"/> (rupees × 100). Delegates to
    /// <see cref="PaisaConversion.ToPaisaExact(Money)"/> — the ONE rupees→paisa rule (drift lock D3). This is the
    /// EXACT semantics: a sub-paisa amount throws rather than serialise a silently altered value.</summary>
    public static long ToPaisa(Money money) => PaisaConversion.ToPaisaExact(money);

    /// <summary>The <see cref="Money"/> for an integer-paisa value (paisa ÷ 100, exact).</summary>
    public static Money FromPaisa(long paisa) => PaisaConversion.ToMoney(paisa);

    /// <summary>Nullable variant for optional monetary fields.</summary>
    public static long? ToPaisa(Money? money) => money is { } m ? ToPaisa(m) : null;

    /// <summary>Nullable variant for optional monetary fields.</summary>
    public static Money? FromPaisaNullable(long? paisa) => paisa is { } p ? FromPaisa(p) : null;
}
