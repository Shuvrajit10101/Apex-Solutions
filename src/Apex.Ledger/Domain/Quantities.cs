namespace Apex.Ledger.Domain;

/// <summary>
/// The single source of truth for stock-quantity precision. Quantities persist as INTEGER "micros"
/// (value × 1,000,000) so a 0–6-decimal quantity round-trips exactly — no binary float (NFR-3). A domain
/// boundary uses <see cref="IsWithinPrecision"/> to reject a finer-than-6-dp value up front with a clean
/// domain error, instead of letting the persistence layer's <c>QtyMicroFromDecimal</c> raise a raw
/// exception at Save time. The 6-dp rule here MUST stay in step with that persistence conversion.
/// </summary>
public static class Quantities
{
    /// <summary>The number of decimal places a stored quantity can carry without loss (micros = ×10^6).</summary>
    public const int DecimalPlaces = 6;

    private const decimal Scale = 1_000_000m;

    /// <summary>
    /// True when <paramref name="quantity"/> is exact to <see cref="DecimalPlaces"/> (6) decimal places, i.e.
    /// it can be stored as quantity-micros without loss.
    /// </summary>
    public static bool IsWithinPrecision(decimal quantity)
    {
        var scaled = quantity * Scale;
        return scaled == decimal.Truncate(scaled);
    }
}
