namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Rounding Method</b> applied to a computed <see cref="PayHead"/> amount (Phase 8 slice 2; catalog §14) —
/// Tally's Not Applicable / Normal / Upward / Downward, paired with a rounding limit (the multiple to round to,
/// e.g. ₹1). <b>No rounding is performed in this slice</b> (the slice-3 engine applies it); the pay head captures
/// the method + limit. Stored as the enum ordinal (0 = NotApplicable).
/// </summary>
public enum PayHeadRoundingMethod
{
    /// <summary>Not Applicable — the amount is used to the paisa, unrounded.</summary>
    NotApplicable = 0,

    /// <summary>Normal Rounding — to the nearest multiple of the rounding limit (half away from zero).</summary>
    Normal = 1,

    /// <summary>Upward Rounding — always up to the next multiple of the rounding limit.</summary>
    Upward = 2,

    /// <summary>Downward Rounding — always down to the previous multiple of the rounding limit.</summary>
    Downward = 3,
}
