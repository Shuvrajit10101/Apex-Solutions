namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Slab Type</b> of a <see cref="PayHeadComputationSlab"/> (Phase 8 slice 2; catalog §14) — Tally's
/// "Percentage" vs "Value". A percentage slab multiplies the computed basis (that falls in the slab's band) by a
/// rate; a value slab contributes a flat amount for that band. Stored as the enum ordinal (0 = Percentage).
/// </summary>
public enum PayHeadComputationSlabType
{
    /// <summary>Percentage — the slab contributes <c>rate% × (basis within the band)</c>.</summary>
    Percentage = 0,

    /// <summary>Value — the slab contributes a flat monetary amount.</summary>
    FlatValue = 1,
}
