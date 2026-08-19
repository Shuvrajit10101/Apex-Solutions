namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Nature of Payment</b> master — a TDS section under which a payment is liable to withholding (Phase 7
/// slice 1; mirrors <see cref="GstRateSlab"/>). It is <b>seeded configuration</b>, not a hard-coded constant, so
/// the FY-specific rate/threshold table can be maintained without a code change. Each carries the income-tax
/// section code (e.g. <c>194J(b)</c>), the with-PAN rate and the no-PAN §206AA rate in <b>basis points</b>
/// (ER-2: 10% = 1000 bp), the single-transaction and cumulative-FY thresholds (money, paisa-exact), the Form
/// 26Q / FVU section code (e.g. <c>94J-B</c>) and the effective-from date. Rates are stored so a future FA change
/// is a data edit. No computation lives here — <c>TdsService</c> (Phase 7 slice 2) resolves and applies the rate.
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Section codes are unique
/// within a company.</remarks>
public sealed class NatureOfPayment
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The income-tax section code (e.g. "194J(b)", "194Q"); required, unique within the company.</summary>
    public string SectionCode { get; }

    /// <summary>A human label (e.g. "Fees for professional services"); required.</summary>
    public string Name { get; }

    /// <summary>The with-PAN TDS rate in basis points (100 bp = 1%). ≥ 0. 1000 = 10%.</summary>
    public int RateWithPanBp { get; }

    /// <summary>The no-PAN §206AA rate in basis points (usually 2000 = 20%; 500 for §194Q's special cap). ≥ 0.</summary>
    public int RateWithoutPanBp { get; }

    /// <summary>Single-transaction threshold below which no TDS applies; <c>null</c> ⇒ no single-txn threshold.</summary>
    public Money? SingleTransactionThreshold { get; }

    /// <summary>Cumulative-per-FY threshold below which no TDS applies; <c>null</c> ⇒ none.</summary>
    public Money? CumulativeThreshold { get; }

    /// <summary>The Form 26Q / FVU section code (e.g. "94J-B", "4IA", "94Q"); required.</summary>
    public string FvuSectionCode { get; }

    /// <summary>The date this rate/threshold applies from; <c>null</c> when unset.</summary>
    public DateOnly? EffectiveFrom { get; }

    /// <summary>True for a Phase-7-seeded predefined nature.</summary>
    public bool IsPredefined { get; }

    public NatureOfPayment(
        Guid id, string sectionCode, string name, int rateWithPanBp, int rateWithoutPanBp,
        string fvuSectionCode, Money? singleTransactionThreshold = null, Money? cumulativeThreshold = null,
        DateOnly? effectiveFrom = null, bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(sectionCode))
            throw new ArgumentException("Nature-of-Payment section code is required.", nameof(sectionCode));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nature-of-Payment name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(fvuSectionCode))
            throw new ArgumentException("Nature-of-Payment FVU section code is required.", nameof(fvuSectionCode));
        if (rateWithPanBp < 0) throw new ArgumentException("Rate (with PAN) basis points must be ≥ 0.", nameof(rateWithPanBp));
        if (rateWithoutPanBp < 0) throw new ArgumentException("Rate (without PAN) basis points must be ≥ 0.", nameof(rateWithoutPanBp));
        if (singleTransactionThreshold is { Amount: < 0m })
            throw new ArgumentException("Single-transaction threshold must be ≥ 0 when set.", nameof(singleTransactionThreshold));
        if (cumulativeThreshold is { Amount: < 0m })
            throw new ArgumentException("Cumulative threshold must be ≥ 0 when set.", nameof(cumulativeThreshold));

        Id = id;
        SectionCode = sectionCode.Trim();
        Name = name.Trim();
        RateWithPanBp = rateWithPanBp;
        RateWithoutPanBp = rateWithoutPanBp;
        FvuSectionCode = fvuSectionCode.Trim();
        SingleTransactionThreshold = singleTransactionThreshold;
        CumulativeThreshold = cumulativeThreshold;
        EffectiveFrom = effectiveFrom;
        IsPredefined = isPredefined;
    }

    /// <summary>The with-PAN rate as a percentage (e.g. 10.00 for 1000 bp).</summary>
    public decimal RateWithPanPercent => RateWithPanBp / 100m;

    /// <summary>
    /// 🔴 <b>T0-1.</b> True iff this section charges the tax only on the value <b>EXCEEDING</b> its
    /// <see cref="CumulativeThreshold"/>, rather than on the whole transaction once the gate is crossed.
    /// <para><b>Statutory ground.</b> Income-tax Act 1961 <b>§194Q(1)</b>: a buyer whose purchases from a resident
    /// seller exceed fifty lakh rupees in a previous year deducts "0.1 per cent. of such sum <b>exceeding fifty lakh
    /// rupees</b>". Contrast §194C / §194J / §194H / §194I / §194A, which are qualifying gates — once crossed the
    /// <b>whole</b> credit or payment bears the tax. The product's TCS twin already encodes exactly this distinction
    /// for the mirror section §206C(1H) (see <c>TcsService.ChargeableBase</c>, whose comment names §194Q as the
    /// mirror); before T0-1 the two sibling engines disagreed about the same statutory shape.</para>
    ///
    /// <para>🔴 <b>DERIVED FROM <see cref="SectionCode"/>, NOT STORED — and that is a deliberate, temporary
    /// NARROWING of an ATTESTED TallyPrime behaviour, not a "corpus silent, ours by design" choice.</b>
    /// `plan.md`'s WF-2 R7 source of record is TallyHelp's §194Q option <i>"Calculate tax on value exceeding the
    /// threshold"</i> — i.e. in TallyPrime this is a <b>user-settable field on the Nature of Payment master</b>.
    /// Storing it needs a `natures_of_payment` column and therefore a schema migration, and the next schema
    /// versions are allocated elsewhere, so this build derives the flag instead. Deriving it from the section code
    /// round-trips exactly (the code is persisted, unique per company and already the lookup key), so no book can
    /// load back with a different answer than it saved — which a non-persisted settable property could not promise.
    /// WF-2 promotes this to the stored, user-settable flag; its back-fill is precisely this predicate, so the
    /// promotion is figure-for-figure identical for every existing book.</para>
    ///
    /// <para><b>Scope of the match.</b> §194Q only, compared case-insensitively against the trimmed code, so a
    /// hand-authored "194q" master behaves identically to the seeded row. A nature with no
    /// <see cref="CumulativeThreshold"/> has nothing to carve against and is never excess-charging.</para>
    /// </summary>
    public bool ChargesOnlyExcessOverCumulativeThreshold =>
        CumulativeThreshold is not null && string.Equals(SectionCode, "194Q", StringComparison.OrdinalIgnoreCase);
}
