namespace Apex.Ledger.Domain;

/// <summary>
/// How a computed interest amount is rounded to its configured number of decimal places (catalog §7
/// "Rounding") — Normal (nearest), Upward, or Downward.
/// </summary>
public enum InterestRoundingMethod
{
    /// <summary>No rounding — the exact computed amount is kept.</summary>
    None = 0,

    /// <summary>Round to the nearest unit at the configured decimals (ties away from zero).</summary>
    Normal = 1,

    /// <summary>Always round up (ceiling, away from zero) at the configured decimals.</summary>
    Upward = 2,

    /// <summary>Always round down (floor, toward zero) at the configured decimals.</summary>
    Downward = 3,
}
