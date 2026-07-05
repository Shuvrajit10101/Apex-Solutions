using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the config-driven GST rate slabs (catalog §12; phase4 RQ-25/DP-2). Phase 4 seeds the <b>live GST 2.0
/// set 0 / 5 / 18 / 40 %</b> (law L-1/L-2, effective 22-Sep-2025) — <b>not</b> the legacy 0/5/12/18/28. Slabs
/// are ordinary editable data (each a <see cref="GstRateSlab"/> in basis points), so a future council change
/// is a data edit, not a code change. Mirrors the <c>SeedCurrencies</c> reference-data pattern.
/// </summary>
public static class SeedGstRates
{
    /// <summary>The Phase-4 default slab set in basis points: 0, 500 (5%), 1800 (18%), 4000 (40%).</summary>
    public static readonly IReadOnlyList<(int RateBasisPoints, string Label)> DefaultSlabs = new[]
    {
        (0, "0%"),
        (500, "5%"),
        (1800, "18%"),
        (4000, "40%"),
    };

    /// <summary>Builds the seeded default GST rate slabs (fresh ids each call).</summary>
    public static IReadOnlyList<GstRateSlab> BuildDefaults() =>
        DefaultSlabs
            .Select(s => new GstRateSlab(Guid.NewGuid(), s.RateBasisPoints, s.Label, isPredefined: true))
            .ToList();
}
