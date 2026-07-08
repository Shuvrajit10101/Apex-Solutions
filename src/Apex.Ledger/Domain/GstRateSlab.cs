namespace Apex.Ledger.Domain;

/// <summary>
/// A configurable GST rate slab (catalog §12; phase4 RQ-25/DP-2). Slabs are <b>seeded configuration</b>,
/// not hard-coded constants, so the council-set slab list can be maintained without a code change. Phase 4
/// seeds the live GST 2.0 set <b>0 / 5 / 18 / 40 %</b> (law L-1/L-2). The rate is stored as an exact scaled
/// integer in <b>basis points</b> (ER-2: 18% = 1800 bp, 2.5% = 250 bp) so the half-rate split
/// <c>CGST = SGST = V × rate/2</c> stays paisa-exact.
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic.</remarks>
public sealed class GstRateSlab
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The integrated GST rate in basis points (100 bp = 1%). ≥ 0. 1800 = 18%.</summary>
    public int RateBasisPoints { get; }

    /// <summary>A human label for the slab (e.g. "18%"); required.</summary>
    public string Label { get; set; }

    /// <summary>True for a Phase-4-seeded slab.</summary>
    public bool IsPredefined { get; }

    public GstRateSlab(Guid id, int rateBasisPoints, string label, bool isPredefined = false)
    {
        if (rateBasisPoints < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("GST rate slab label is required.", nameof(label));

        Id = id;
        RateBasisPoints = rateBasisPoints;
        Label = label.Trim();
        IsPredefined = isPredefined;
    }

    /// <summary>The rate as a percentage (e.g. 18.00 for 1800 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;
}
