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

    /// <summary>
    /// 🔴 <b>§194C's second with-PAN rate — the one that applies when the deductee is NOT an individual or a Hindu
    /// undivided family.</b> <c>null</c> for every other section, whose with-PAN rate does not turn on who is being
    /// paid.
    ///
    /// <para><b>Statutory ground.</b> Income-tax Act 1961 <b>§194C(1)</b>, bare Act text as published by the
    /// Income-tax Department (<c>https://www.incometaxindia.gov.in/w/section-194c</c>): the deductor shall "deduct
    /// an amount equal to — (i) <b>one per cent</b> where the payment is being made or credit is being given to an
    /// individual or a Hindu undivided family; (ii) <b>two per cent</b> where the payment is being made or credit
    /// is being given to a person other than an individual or a Hindu undivided family". The Department's own rate
    /// chart for <b>Assessment Year 2026-27</b> (= FY 2025-26, the year this build's seed encodes) states the same
    /// split — "Section 194C: Payment to contractor/sub-contractor — a) HUF/Individuals 1 — b) Others 2"
    /// (<c>https://www.incometaxindia.gov.in/w/tds-rates-1</c>). <see cref="RateWithPanBp"/> is therefore the
    /// §194C(1)(i) <b>individual/HUF</b> arm on a §194C nature, and this is the §194C(1)(ii) arm.</para>
    ///
    /// <para>🔴 <b>DERIVED FROM <see cref="SectionCode"/>, NOT STORED — same reason, same precedent, as
    /// <see cref="ChargesOnlyExcessOverCumulativeThreshold"/> directly above.</b> A stored second rate needs a
    /// `nature_of_payment` column and therefore a schema migration, and the schema versions after 51 are allocated
    /// to other tracks. Deriving it from the section code round-trips exactly — the code is persisted, unique per
    /// company and already the lookup key — so no book can load back with a different answer than it saved, which
    /// is precisely what a non-persisted settable property could not promise. The trade-off is stated plainly: on a
    /// §194C nature this ONE figure is not editable data the way every other seeded figure is. When a schema version
    /// is available it becomes a stored column and its back-fill is exactly this predicate, so the promotion is
    /// figure-for-figure identical for every existing book.</para>
    ///
    /// <para><b>Scope of the match.</b> §194C only, compared case-insensitively against the trimmed code, so a
    /// hand-authored "194c" master behaves identically to the seeded row. The <b>no-PAN</b> rate is untouched:
    /// §206AA(1) charges the higher of the section rate, the rates in force, or 20% — one figure, with no
    /// individual/HUF concession to choose between.</para>
    /// </summary>
    public int? RateWithPanOtherThanIndividualBp =>
        string.Equals(SectionCode, "194C", StringComparison.OrdinalIgnoreCase) ? 200 : null;

    /// <summary>
    /// True iff this section's <b>with-PAN</b> rate turns on the deductee's legal status
    /// (<see cref="Ledger.DeducteeType"/>) — i.e. iff <see cref="RateWithPanOtherThanIndividualBp"/> is set. §194C
    /// is the only such section in the seeded set; see
    /// <c>Tds194CDeducteeTypeTests.Exactly_one_seeded_nature_of_payment_branches_on_deductee_type_and_it_is_194C</c>,
    /// which asserts that over the whole seed so a future row cannot quietly acquire the branch.
    /// </summary>
    public bool RateTurnsOnDeducteeType => RateWithPanOtherThanIndividualBp is not null;

    /// <summary>
    /// 🔴 <b>§194-I's threshold is a PER-MONTH limb — the engine's THIRD threshold window, not a different number
    /// in one of the two it already had (per-transaction and per-financial-year).</b>
    ///
    /// <para><b>Statutory ground.</b> Income-tax Act 1961 <b>§194-I, first proviso</b> as it stands for
    /// <b>FY 2025-26</b>, bare Act text as published by the Income-tax Department
    /// (<c>https://www.incometaxindia.gov.in/w/section-194-i-19</c>): "no deduction shall be made under this
    /// section, where the income by way of rent credited or paid <b>for a month or part of a month</b> by such
    /// person to the account of, or to, the payee, <b>does not exceed fifty thousand rupees</b>". §194-I carries
    /// <b>no annual-aggregate limb at all</b>: on a §194-I nature <see cref="CumulativeThreshold"/> is not a
    /// larger or smaller version of this test, it is <b>not the test</b>.</para>
    ///
    /// <para>🔴 <b>THE FIGURES THIS MOVES.</b> One month's rent of ₹60,000 with nothing else in the year. The
    /// statute deducts, because ₹60,000 exceeds the monthly ₹50,000, and at §194-I(b) that is <b>₹6,000.00</b>.
    /// The annualised rule this replaces deducted <b>₹0.00</b>, because ₹60,000 is nowhere near ₹6,00,000 — a
    /// ₹6,000.00 <b>under</b>-deduction on one ordinary rent bill, which the deductor answers for under §201 with
    /// interest under §201(1A).</para>
    ///
    /// <para>🔴 <b>"OR PART OF A MONTH" WIDENS THE LIMB, IT DOES NOT PRO-RATE IT.</b> A tenancy that runs for only
    /// part of a month is tested against the same whole ₹50,000, never against a fraction of it — the words are
    /// there so a part-month cannot escape the section, not so it gets a smaller allowance. The window is
    /// therefore the <b>calendar month containing the voucher date</b> and the figure is always ₹50,000; two
    /// part-months in one financial year are two separate windows, each with its own full ₹50,000. The model
    /// carries no rent-period field, only the date on which the rent is credited or paid — which is exactly the
    /// trigger the proviso names ("credited or paid").</para>
    ///
    /// <para>🔴 <b>DERIVED FROM <see cref="SectionCode"/>, NOT STORED — the same reason and the same precedent as
    /// <see cref="RateWithPanOtherThanIndividualBp"/> and <see cref="ChargesOnlyExcessOverCumulativeThreshold"/>
    /// above.</b> A stored monthly threshold needs a `natures_of_payment` column and therefore a schema migration,
    /// and the schema versions after 51 are allocated to other tracks. Deriving it round-trips exactly — the
    /// section code is persisted, unique per company and already the lookup key — so no book can load back with a
    /// different window than it saved.</para>
    ///
    /// <para>🔴 <b>AND WHAT BECOMES OF THE ₹6,00,000 ALREADY PERSISTED IN EVERY EXISTING BOOK</b> (the monthly
    /// figure annualised, 50,000 × 12, which the seed used to ship as <see cref="CumulativeThreshold"/> on both
    /// §194-I rows) <b>is stated here once and plainly: it is INERT.</b> It is <b>not</b> re-read as a monthly
    /// figure, <b>not</b> migrated and <b>not</b> deleted — <see cref="AggregateThreshold"/> simply never consults
    /// <see cref="CumulativeThreshold"/> on a per-month nature. A book that persisted ₹6,00,000 and a freshly
    /// seeded book that persists nothing therefore compute the identical withholding on every input, which is why
    /// this change needs no migration and <c>Schema.CurrentVersion</c> stays where it is.</para>
    ///
    /// <para>🔴 <b>THE MATCH IS EXACT, AND THE NEIGHBOURING SECTIONS ARE THE REASON.</b> §194-IA (1% on the
    /// purchase of immovable property, gated on a ₹50-lakh <i>consideration</i>), §194-IB and §194-IC are
    /// different sections with different tests, and every one of their codes begins with the same four characters
    /// as §194-I. The comparison is therefore against the <b>whole</b> code, normalised (hyphens and spaces
    /// removed, case-insensitive), and accepts exactly <c>194I</c>, <c>194I(a)</c> and <c>194I(b)</c> — never
    /// <c>194IA</c>, <c>194IB</c> or <c>194IC</c>. A <c>StartsWith("194I")</c> would have handed §194-IA a
    /// ₹50,000-a-month rent test.</para>
    ///
    /// <para>⚠️ <b>A FORWARD NOTE, BECAUSE THE EXCLUSION IS RIGHT TODAY AND WILL NOT STAY RIGHT.</b> <b>§194-IB</b>
    /// — rent paid by an individual or a HUF not liable to tax audit — carries a per-month limb <i>of its own</i>
    /// ("rent for a month or part of a month exceeds fifty thousand rupees"), at a different rate and under a
    /// different Form-26Q code. It is not in the seeded set (it sits in the census long tail with §194-IA, §194-IC,
    /// §194M and §194N), so excluding it here costs nothing now. Whoever seeds it must EXTEND this match rather
    /// than rely on it: a §194-IB row added without doing so gets the financial-year window by default, which is
    /// exactly the under-deduction §194-I has just been dug out of.</para>
    /// </summary>
    public Money? MonthlyThreshold => IsSection194I ? Money.FromRupees(50_000m) : null;

    /// <summary>
    /// True iff this section's aggregate threshold window is a <b>calendar month</b> rather than the financial
    /// year — i.e. iff <see cref="MonthlyThreshold"/> is set. §194-I is the only such section in the seeded set;
    /// see <c>Tds194IMonthlyThresholdTests.Exactly_one_seeded_nature_of_payment_has_a_per_month_window_and_it_is_194I</c>,
    /// which asserts that over the whole seed so a future row cannot quietly acquire the window.
    /// </summary>
    public bool ThresholdWindowIsPerMonth => MonthlyThreshold is not null;

    /// <summary>
    /// 🔴 <b>The aggregate limb the engine actually tests, and the single place the WINDOW is chosen.</b> A
    /// per-month section answers with its <see cref="MonthlyThreshold"/>; every other section answers with its
    /// stored <see cref="CumulativeThreshold"/>. Reading <see cref="CumulativeThreshold"/> directly for a
    /// threshold test is precisely the defect this property exists to make unreachable — on a §194-I nature that
    /// field holds a superseded annual figure that decides nothing.
    /// </summary>
    public Money? AggregateThreshold => MonthlyThreshold ?? CumulativeThreshold;

    /// <summary>§194-I itself, and neither §194-IA nor §194-IB nor §194-IC. See <see cref="MonthlyThreshold"/>.</summary>
    private bool IsSection194I =>
        SectionCode.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant()
            is "194I" or "194I(A)" or "194I(B)";
}
