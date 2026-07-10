namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Nature of Goods</b> master — a §206C category under which a sale attracts TCS (Phase 7 slice 1; mirrors
/// <see cref="GstRateSlab"/>). Seeded configuration (not a hard-coded constant). Each carries the Form 27EQ / FVU
/// <see cref="CollectionCode"/> (e.g. <c>6CE</c> scrap), the with-PAN rate and the no-PAN §206CC rate in basis
/// points, an optional value threshold, whether the assessable base <b>includes GST</b> (true for all §206C rows
/// per CBDT Circular 17/2020), the effective-from date, and — for §206C(1H) sale-of-goods — a <b>legacy
/// year-gate</b> (<see cref="IsLegacy"/> + <see cref="LegacyCutoff"/>): default OFF and non-selectable for
/// transaction dates on/after the cutoff (01-Apr-2025, FA2025), retained selectable for prior-FY dates so
/// historical returns still compute. No computation lives here — <c>TcsService</c> (Phase 7 slice 5) applies it.
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Collection codes are unique
/// within a company.</remarks>
public sealed class NatureOfGoods
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The §206C Form-27EQ collection code (e.g. "6CE", "6CA", "6CR"); required, unique within the company.</summary>
    public string CollectionCode { get; }

    /// <summary>A human label (e.g. "Scrap", "Tendu leaves"); required.</summary>
    public string Name { get; }

    /// <summary>The with-PAN TCS rate in basis points (100 bp = 1%). ≥ 0.</summary>
    public int RateWithPanBp { get; }

    /// <summary>The no-PAN §206CC rate in basis points (higher of 2× or 5%; §206C(1H) special cap = 1%). ≥ 0.</summary>
    public int RateWithoutPanBp { get; }

    /// <summary>Value threshold above which TCS applies (e.g. §206C(1F) ₹10,00,000); <c>null</c> ⇒ collected on full value.</summary>
    public Money? Threshold { get; }

    /// <summary>Whether the assessable base includes GST (true for §206C — on gross sale consideration).</summary>
    public bool BaseIncludesGst { get; }

    /// <summary>The Form 27EQ / FVU collection code used on the return (same family as <see cref="CollectionCode"/>).</summary>
    public string FvuCode { get; }

    /// <summary>The date this rate/threshold applies from; <c>null</c> when unset.</summary>
    public DateOnly? EffectiveFrom { get; }

    /// <summary>True for a Phase-7-seeded predefined nature.</summary>
    public bool IsPredefined { get; }

    /// <summary>
    /// True for the legacy §206C(1H) sale-of-goods entry — year-gated: non-operative from
    /// <see cref="LegacyCutoff"/> (01-Apr-2025). A non-legacy nature is always selectable.
    /// </summary>
    public bool IsLegacy { get; }

    /// <summary>
    /// The cut-off date on/after which a <see cref="IsLegacy"/> nature is non-selectable (default OFF), or
    /// <c>null</c> for a non-legacy nature.
    /// </summary>
    public DateOnly? LegacyCutoff { get; }

    public NatureOfGoods(
        Guid id, string collectionCode, string name, int rateWithPanBp, int rateWithoutPanBp, string fvuCode,
        Money? threshold = null, bool baseIncludesGst = true, DateOnly? effectiveFrom = null,
        bool isPredefined = false, bool isLegacy = false, DateOnly? legacyCutoff = null)
    {
        if (string.IsNullOrWhiteSpace(collectionCode))
            throw new ArgumentException("Nature-of-Goods collection code is required.", nameof(collectionCode));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nature-of-Goods name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(fvuCode))
            throw new ArgumentException("Nature-of-Goods FVU code is required.", nameof(fvuCode));
        if (rateWithPanBp < 0) throw new ArgumentException("Rate (with PAN) basis points must be ≥ 0.", nameof(rateWithPanBp));
        if (rateWithoutPanBp < 0) throw new ArgumentException("Rate (without PAN) basis points must be ≥ 0.", nameof(rateWithoutPanBp));
        if (threshold is { Amount: < 0m })
            throw new ArgumentException("Threshold must be ≥ 0 when set.", nameof(threshold));

        Id = id;
        CollectionCode = collectionCode.Trim();
        Name = name.Trim();
        RateWithPanBp = rateWithPanBp;
        RateWithoutPanBp = rateWithoutPanBp;
        FvuCode = fvuCode.Trim();
        Threshold = threshold;
        BaseIncludesGst = baseIncludesGst;
        EffectiveFrom = effectiveFrom;
        IsPredefined = isPredefined;
        IsLegacy = isLegacy;
        LegacyCutoff = legacyCutoff;
    }

    /// <summary>The with-PAN rate as a percentage (e.g. 1.00 for 100 bp).</summary>
    public decimal RateWithPanPercent => RateWithPanBp / 100m;

    /// <summary>
    /// True iff this nature is selectable for a transaction dated <paramref name="date"/>: a non-legacy nature is
    /// always selectable; a legacy §206C(1H) nature is selectable only <b>before</b> its <see cref="LegacyCutoff"/>
    /// (so it is off/non-selectable for dates ≥ 01-Apr-2025 but retained for FY 2024-25 historical returns).
    /// </summary>
    public bool IsSelectableOn(DateOnly date) =>
        !IsLegacy || LegacyCutoff is not { } cutoff || date < cutoff;
}
