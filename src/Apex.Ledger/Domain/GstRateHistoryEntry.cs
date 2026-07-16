namespace Apex.Ledger.Domain;

/// <summary>
/// One dated GST rate-applicability window (Phase 9 slice 1; RQ-1; ER-1). GST 2.0 (eff. 22-Sep-2025) rationalised the
/// slabs mid-FY, so the same HSN can carry different rates on either side of the cut-over (e.g. a car at 28% on
/// 20-Sep-2025 and 40% on 25-Sep-2025). This companion master to the flat <see cref="GstRateSlab"/> pick-list records
/// each rate as a dated window keyed (optionally) by HSN/SAC, so <c>GstService.ResolveRate(item, ledger, voucherDate)</c>
/// can select the rate in force on the voucher date. A company with <b>no</b> rate-history rows resolves exactly as
/// Phase-4/8 does (the override only fires on a match — ER-13 byte-identical when off).
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Both effective bounds are
/// <b>inclusive</b>: a legacy row ends <c>2025-09-21</c>, the new-rate row starts <c>2025-09-22</c>.</remarks>
public sealed class GstRateHistoryEntry
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The HSN/SAC this window applies to; <c>null</c> = a generic slab row (0/5/18/40 + specials).</summary>
    public string? HsnSac { get; }

    /// <summary>The integrated GST rate in basis points (100 bp = 1%). ≥ 0. 4000 = 40%.</summary>
    public int RateBasisPoints { get; }

    /// <summary>The advisory rate class (Standard/Merit/Special/DeMerit/CarveOut/Legacy).</summary>
    public GstRateClass RateClass { get; }

    /// <summary>The window start (ISO date), <b>inclusive</b>.</summary>
    public DateOnly EffectiveFrom { get; }

    /// <summary>The window end (ISO date), <b>inclusive</b>; <c>null</c> = open-ended.</summary>
    public DateOnly? EffectiveTo { get; }

    /// <summary>The valuation basis for this window (Transaction-Value default, or RSP for the tobacco carve-out).</summary>
    public GstValuationBasis ValuationBasis { get; }

    /// <summary>A human label for the window (e.g. "40% (GST 2.0)"); required.</summary>
    public string Label { get; }

    /// <summary>True for a predefined (seeded) row.</summary>
    public bool IsPredefined { get; }

    public GstRateHistoryEntry(
        Guid id, string? hsnSac, int rateBasisPoints, GstRateClass rateClass,
        DateOnly effectiveFrom, DateOnly? effectiveTo, GstValuationBasis valuationBasis,
        string label, bool isPredefined = false)
    {
        if (rateBasisPoints < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("GST rate-history label is required.", nameof(label));
        if (effectiveTo is { } to && to < effectiveFrom)
            throw new ArgumentException("GST rate-history effective-to must not precede effective-from.", nameof(effectiveTo));

        Id = id;
        HsnSac = hsnSac;
        RateBasisPoints = rateBasisPoints;
        RateClass = rateClass;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        ValuationBasis = valuationBasis;
        Label = label.Trim();
        IsPredefined = isPredefined;
    }

    /// <summary>True iff <paramref name="date"/> falls in this window — both bounds <b>inclusive</b>.</summary>
    public bool IsEffectiveOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}
