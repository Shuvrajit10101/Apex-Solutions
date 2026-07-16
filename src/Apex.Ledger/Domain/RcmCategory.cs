namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>notified reverse-charge category</b> master (Phase 9 slice 2; RQ-3/RQ-7). It records one dated GST reverse-charge
/// applicability window — the notification, the §9(3)/§9(4) <see cref="Stream"/>, the supply nature (GTA/legal/director/
/// cement/…), the supplier + recipient qualifiers, and the effective window — so <c>RcmService</c> can decide whether an
/// inward supply attracts reverse charge on a given voucher date. It is a companion dated master hung off
/// <see cref="GstConfig"/> exactly like S1's <see cref="GstRateHistoryEntry"/>, seeded via the advanced-GST opt-in.
/// <para>
/// The RCM rate carried here (<see cref="RateBasisPoints"/>) is used for the pure service categories that have no HSN;
/// goods categories (e.g. cement, HSN 2523) leave the rate here as a fallback and resolve the <b>dated</b> rate through
/// the S1 rate history by HSN (28% ≤ 21-Sep-2025, 18% from 22-Sep-2025) — a single source of truth for HSN rates. A
/// company with <b>no</b> rcm-category rows never raises an RCM leg (ER-13 byte-identical when off).
/// </para>
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Both effective bounds are
/// <b>inclusive</b> (mirrors <see cref="GstRateHistoryEntry"/>).</remarks>
public sealed class RcmCategory
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The notification the category is enacted under (e.g. "13/2017-CT(R)", "10/2017-IGST(R)", "7/2019-CT(R)"); required.</summary>
    public string Notification { get; }

    /// <summary>The legal limb — §9(3) notified supply, or §9(4) unregistered-to-promoter.</summary>
    public RcmStream Stream { get; }

    /// <summary>The supply nature label (e.g. "GTA", "Legal", "Director", "Security", "Cement"); required.</summary>
    public string SupplyNature { get; }

    /// <summary>Goods (HSN) or Services (SAC) — matches the item/ledger supply type.</summary>
    public GstSupplyType SupplyType { get; }

    /// <summary>The HSN/SAC this category applies to (set for goods categories, e.g. cement "2523"); <c>null</c> for services.</summary>
    public string? HsnSac { get; }

    /// <summary>The integrated RCM rate in basis points (1800 = 18%, GTA 500). Used for service categories; a goods
    /// category prefers the dated HSN rate history. ≥ 0.</summary>
    public int RateBasisPoints { get; }

    /// <summary>The qualifier the actual supplier must satisfy (Any / Unregistered / NonBodyCorporate).</summary>
    public RcmParty SupplierQualifier { get; }

    /// <summary>The qualifier the actual recipient must satisfy (Any / BodyCorporate / RegisteredPerson / Promoter).</summary>
    public RcmParty RecipientQualifier { get; }

    /// <summary>The window start (ISO date), <b>inclusive</b>.</summary>
    public DateOnly EffectiveFrom { get; }

    /// <summary>The window end (ISO date), <b>inclusive</b>; <c>null</c> = open-ended.</summary>
    public DateOnly? EffectiveTo { get; }

    /// <summary>A human label for the category (e.g. "GTA (5% no-ITC)"); required.</summary>
    public string Label { get; }

    /// <summary>True for a predefined (seeded) category.</summary>
    public bool IsPredefined { get; }

    public RcmCategory(
        Guid id, string notification, RcmStream stream, string supplyNature, GstSupplyType supplyType,
        string? hsnSac, int rateBasisPoints, RcmParty supplierQualifier, RcmParty recipientQualifier,
        DateOnly effectiveFrom, DateOnly? effectiveTo, string label, bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(notification))
            throw new ArgumentException("RCM category notification is required.", nameof(notification));
        if (string.IsNullOrWhiteSpace(supplyNature))
            throw new ArgumentException("RCM category supply nature is required.", nameof(supplyNature));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("RCM category label is required.", nameof(label));
        if (rateBasisPoints < 0)
            throw new ArgumentException("RCM rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (effectiveTo is { } to && to < effectiveFrom)
            throw new ArgumentException("RCM category effective-to must not precede effective-from.", nameof(effectiveTo));

        Id = id;
        Notification = notification.Trim();
        Stream = stream;
        SupplyNature = supplyNature.Trim();
        SupplyType = supplyType;
        HsnSac = hsnSac;
        RateBasisPoints = rateBasisPoints;
        SupplierQualifier = supplierQualifier;
        RecipientQualifier = recipientQualifier;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Label = label.Trim();
        IsPredefined = isPredefined;
    }

    /// <summary>True iff <paramref name="date"/> falls in this window — both bounds <b>inclusive</b>.</summary>
    public bool IsEffectiveOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}
