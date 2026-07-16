namespace Apex.Ledger.Domain;

/// <summary>
/// One dated GST Compensation-Cess window (Phase 9 slice 1; RQ-2/RQ-9; ER-2). The FY2025-26 cess schedule has three
/// dated windows (all de-merit goods → tobacco-only → nil), with three valuation modes (ad-valorem %, specific
/// per-unit/quantity, RSP-factor). A company with <b>no</b> cess rows computes zero cess automatically (ER-13
/// byte-identical when off). Cess is <b>ring-fenced</b>: it posts only to the Output/Input Cess ledgers, never to
/// CGST/SGST/IGST.
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Both effective bounds are
/// <b>inclusive</b>.</remarks>
public sealed class GstCessRate
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The HSN/SAC the cess applies to; <c>null</c> = a generic row.</summary>
    public string? HsnSac { get; }

    /// <summary>How the cess amount is valued (ad-valorem / specific / RSP-factor).</summary>
    public CessValuationMode ValuationMode { get; }

    /// <summary>Ad-valorem cess rate in basis points (0 unless <see cref="CessValuationMode.AdValorem"/>). ≥ 0.</summary>
    public int CessRateBasisPoints { get; }

    /// <summary>Specific per-unit cess amount (paisa-exact); zero unless <see cref="CessValuationMode.Specific"/>.</summary>
    public Money CessPerUnit { get; }

    /// <summary>RSP multiplier × 1000 (e.g. 0.32R → 320); 0 unless <see cref="CessValuationMode.RetailSalePriceFactor"/>. ≥ 0.</summary>
    public int CessRspFactorMillis { get; }

    /// <summary>The window start (ISO date), <b>inclusive</b>.</summary>
    public DateOnly EffectiveFrom { get; }

    /// <summary>The window end (ISO date), <b>inclusive</b>; <c>null</c> = open-ended.</summary>
    public DateOnly? EffectiveTo { get; }

    /// <summary>A human label for the window (e.g. "Coal ₹400/tonne"); required.</summary>
    public string Label { get; }

    /// <summary>True for a predefined (seeded) row.</summary>
    public bool IsPredefined { get; }

    public GstCessRate(
        Guid id, string? hsnSac, CessValuationMode valuationMode, int cessRateBasisPoints, Money cessPerUnit,
        int cessRspFactorMillis, DateOnly effectiveFrom, DateOnly? effectiveTo, string label, bool isPredefined = false)
    {
        if (cessRateBasisPoints < 0)
            throw new ArgumentException("Cess rate basis points must be ≥ 0.", nameof(cessRateBasisPoints));
        if (cessPerUnit.Amount < 0)
            throw new ArgumentException("Cess per-unit amount must be ≥ 0.", nameof(cessPerUnit));
        if (cessRspFactorMillis < 0)
            throw new ArgumentException("Cess RSP factor (millis) must be ≥ 0.", nameof(cessRspFactorMillis));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Cess-rate label is required.", nameof(label));
        if (effectiveTo is { } to && to < effectiveFrom)
            throw new ArgumentException("Cess-rate effective-to must not precede effective-from.", nameof(effectiveTo));

        Id = id;
        HsnSac = hsnSac;
        ValuationMode = valuationMode;
        CessRateBasisPoints = cessRateBasisPoints;
        CessPerUnit = cessPerUnit;
        CessRspFactorMillis = cessRspFactorMillis;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Label = label.Trim();
        IsPredefined = isPredefined;
    }

    /// <summary>True iff <paramref name="date"/> falls in this window — both bounds <b>inclusive</b>.</summary>
    public bool IsEffectiveOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}
