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

    /// <summary>
    /// The Form 26Q / FVU section code <b>as stored in this book</b> (e.g. "94J-B", "94Q"); required.
    /// <para>🔴 <b>DO NOT EMIT THIS FIELD INTO A RETURN. EMIT <see cref="NotifiedFvuSectionCode"/>.</b> An existing
    /// book can hold a superseded spelling of a §194-I code here — see that property for the defect, the notified
    /// codes and why the stored value is deliberately left alone.</para>
    /// </summary>
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
    /// 🔴 <b>THE FORM-26Q SECTION CODE THIS NATURE ACTUALLY FILES UNDER — the one every emitter must use.</b> It is
    /// <see cref="FvuSectionCode"/> for every nature in the seed but the two §194-I rows, where a <b>superseded
    /// spelling persisted in existing books</b> is normalised to the notified one.
    ///
    /// <para>🔴 <b>THE DEFECT THIS CLOSES, PLAINLY: THE PRODUCT WAS EMITTING A SECTION CODE THAT IS NOT IN THE
    /// NOTIFIED FORM, INTO RETURNS FILED WITH THE DEPARTMENT.</b> The seed shipped <c>"4IA"</c> and <c>"4IB"</c>.
    /// The notified codes are <c>"4-IA"</c> and <c>"4-IB"</c>, <b>with a hyphen</b>. This is not cosmetic:
    /// <c>FvuWriter</c> writes the code into the FVU flat file, <c>Form26Q</c> reports it and <c>Form16APdf</c>
    /// prints it on the certificate issued to the deductee, so every §194-I rent deduction was being reported under
    /// a code the validator does not recognise.</para>
    ///
    /// <para><b>[FORM-26Q] — the source, read first-hand 2026-09-08.</b> Income-tax Department, notified <b>FORM
    /// NO. 26Q</b>, "Quarterly statement of deduction of tax under sub-section (3) of section 200 in respect of
    /// payments other than salary",
    /// <c>https://www.incometaxindia.gov.in/documents/d/guest/103120000000007861-pdf-2</c>, the official
    /// "Section | Nature of Payment | Section Code" table under note 16 ("List of section codes is as under"). The
    /// two rows read verbatim: <c>194-I(a) Rent <b>4-IA</b></c> and <c>194-I (b) Rent <b>4-IB</b></c>. The same
    /// table confirms every other code in the seed unchanged — 94A, 94C, 94H, 94J-A, 94J-B, 94Q, 94T, 94R, 94S —
    /// so §194-I is the only one that moves.</para>
    ///
    /// <para>🔴 <b>WHY THE STORED VALUE IS NOT REWRITTEN, AND WHAT A MIGRATION WOULD HAVE TO DO.</b> The code is
    /// persisted per nature (<c>natures_of_payment.fvu_section_code</c>), so correcting the seed alone would leave
    /// every book created before this change still holding <c>"4IA"</c> — and still filing it. Rewriting those rows
    /// is a data migration, and this pass has <b>no schema budget</b> (v61 belongs to the GST track). Normalising at
    /// the point of emission fixes the filed figure for <b>every</b> book, old and new, with no migration at all,
    /// and it is total: a legacy book and a freshly seeded book emit the identical code. The stored value is left
    /// untouched and inert, exactly as the superseded §194-I ₹6,00,000 <see cref="CumulativeThreshold"/> is.
    /// <b>What a migration would still be needed for, if the user wants the stored values normalised too:</b> a
    /// single <c>UPDATE natures_of_payment SET fvu_section_code = '4-IA' WHERE fvu_section_code = '4IA'</c> (and
    /// '4IB' → '4-IB'), guarded to the rows whose <c>section_code</c> is in the §194-I family so a hand-authored
    /// nature that happens to reuse the string is not caught; plus the same rewrite inside the canonical
    /// XML/JSON importer for books restored from a backup taken before this change. Nothing else reads the field.
    /// <b>That decision is the user's and is reported, not taken here.</b></para>
    ///
    /// <para><b>THE MATCH IS TOLERANT IN BOTH DIRECTIONS, WHICH IS THE POINT.</b> A book already holding the
    /// notified <c>"4-IA"</c> is returned unchanged; a legacy <c>"4IA"</c>, <c>"4ia"</c> or <c>" 4 I A "</c> maps to
    /// <c>"4-IA"</c>. Comparison strips hyphens and whitespace and folds case, so no existing book — however its
    /// row was authored — can file the wrong code, and re-running the normaliser on its own output is a no-op.
    /// <b>It is scoped to the §194-I family alone</b>: every other code is returned verbatim, so this can never
    /// invent a code for a section the notified table spells differently.</para>
    /// </summary>
    public string NotifiedFvuSectionCode => NormalizeFvuSectionCode(FvuSectionCode);

    /// <summary>
    /// Maps a stored Form-26Q section code to the spelling in the notified form. See
    /// <see cref="NotifiedFvuSectionCode"/> for the source and the reasoning; this is the same rule exposed for the
    /// import path, which sees raw codes before a <see cref="NatureOfPayment"/> exists to ask.
    /// </summary>
    public static string NormalizeFvuSectionCode(string? fvuSectionCode)
    {
        if (string.IsNullOrWhiteSpace(fvuSectionCode)) return string.Empty;
        var trimmed = fvuSectionCode.Trim();
        var key = new string(trimmed.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return key switch
        {
            "4IA" => "4-IA",
            "4IB" => "4-IB",
            _ => trimmed,
        };
    }

    /// <summary>
    /// 🔴 <b>Whether this section's aggregate limb is crossed AT the threshold or only ABOVE it — the one flag that
    /// separates a statute reading "does not exceed X" from one reading "is less than X".</b>
    ///
    /// <para><b>Why it exists.</b> <c>TdsService.ThresholdCrossed</c> tests <c>(prior + current) &gt; threshold</c>,
    /// which is exactly "no deduction where the aggregate <b>does not exceed</b> X" — the shape of every section in
    /// the seed before this. A section whose proviso instead exempts a payment that "<b>is less than</b> X" is
    /// liable <b>at exactly X</b>, and testing it with the strictly-greater rule under-deducts on the boundary
    /// rupee: on §192A at exactly ₹50,000 that is <b>₹5,000.00</b> withheld nowhere, which the deductor answers for
    /// under §201 with interest under §201(1A). Both boundaries now exist and each section says which it has.</para>
    ///
    /// <para><b>The two sections, with the operative words quoted from the bare Act at the FY 2025-26 vintage
    /// (the page's own "Year" field reads 2025, which is the only reliable discriminator on this site):</b>
    /// <list type="bullet">
    ///   <item><b>§192A</b> — <c>https://www.incometaxindia.gov.in/w/section-192a-11</c>, Year 2025: "Provided that
    ///     no deduction under this section shall be made where the amount of such payment or, as the case may be,
    ///     the aggregate amount of such payment to the payee <b>is less than fifty thousand rupees</b>."</item>
    ///   <item><b>§194EE</b> — <c>https://www.incometaxindia.gov.in/w/section-194ee-35</c>, Year 2025: "Provided
    ///     that no deduction shall be made under this section where the amount of such payment or, as the case may
    ///     be, the aggregate amount of such payments to the payee during the financial year <b>is less than two
    ///     thousand five hundred rupees</b>."</item>
    /// </list></para>
    ///
    /// <para>✅ <b>AND THE AUDIT THAT CAME WITH IT, BECAUSE ADDING THE FLAG IS ONLY HALF THE JOB.</b> Every section
    /// already shipped was re-read at its own cited slug on 2026-09-08 looking for an "is less than" limb that this
    /// engine had been treating as strictly-greater — a live under-deduction if one existed. <b>None does.</b>
    /// §194A, §194C, §194H, §194-I, §194J, §194T, §194R and §194S all read "does not exceed"; §194Q reads
    /// "exceeding". The shipped set was correct before this change and is unmoved by it, and
    /// <c>TdsInclusiveThresholdTests</c> pins that over the whole seed so a future row cannot quietly acquire the
    /// wrong boundary.</para>
    ///
    /// <para>🔴 <b>THE SINGLE-TRANSACTION LIMB IS DELIBERATELY NOT COVERED.</b> Neither §192A nor §194EE has one,
    /// and no shipped section has an inclusive single-transaction limb, so widening the flag to
    /// <see cref="SingleTransactionThreshold"/> would be untested reach. Whoever seeds a section that needs it must
    /// extend this rather than assume it already applies.</para>
    ///
    /// <para><b>Derived from <see cref="SectionCode"/>, not stored</b> — the same reason and the same precedent as
    /// <see cref="MonthlyThreshold"/> and <see cref="RateWithPanOtherThanIndividualBp"/>: a stored flag needs a
    /// <c>natures_of_payment</c> column and therefore a schema migration, and v61 is allocated to the GST track.
    /// The section code is persisted, unique per company and already the lookup key, so deriving it round-trips
    /// exactly and a future promotion to a stored column back-fills figure-for-figure from this predicate.</para>
    /// </summary>
    public bool AggregateThresholdIsInclusive =>
        NormalizedSectionCode is "192A" or "194EE";

    /// <summary>
    /// 🔴 <b>§194-O's aggregate exemption is CONDITIONAL ON WHO THE PAYEE IS — it is not a threshold every
    /// deductee gets.</b> True only on §194-O; <c>false</c> everywhere else, where the threshold is unconditional.
    ///
    /// <para><b>Statutory ground.</b> §194-O(2), bare Act at
    /// <c>https://www.incometaxindia.gov.in/w/section-194-o-6</c>, <b>Year 2025</b> (= FY 2025-26): "No deduction
    /// under sub-section (1) shall be made from any sum credited or paid ... to the account of an e-commerce
    /// participant, <b>being an individual or Hindu undivided family</b>, where the gross amount of such sale or
    /// services or both during the previous year <b>does not exceed five lakh rupees</b> and such e-commerce
    /// participant <b>has furnished his Permanent Account Number or Aadhaar number</b> to the e-commerce
    /// operator."</para>
    ///
    /// <para>🔴 <b>THE MONEY, AND WHY A FLAT THRESHOLD WOULD HAVE BEEN A DEFECT IN EITHER DIRECTION.</b> The
    /// exemption needs <b>all three</b> conditions. A <b>company</b> e-commerce participant gets no exemption at
    /// all: it is liable from the first rupee, so seeding §194-O with a plain ₹5,00,000 cumulative threshold would
    /// have withheld <b>₹0.00</b> on a ₹4,00,000 payout that owes <b>₹400.00</b>. Seeding it with no threshold
    /// instead would have over-withheld on exactly the individual sellers the sub-section protects. Neither is the
    /// statute, so the condition is modelled rather than approximated.</para>
    ///
    /// <para>⚠️ <b>AADHAAR IS A DOCUMENTED NARROWING, STATED SO IT IS NOT MISTAKEN FOR AN OVERSIGHT.</b> The
    /// sub-section accepts a PAN <b>or</b> an Aadhaar number; the ledger master carries only
    /// <c>Domain.Ledger.PartyPan</c> and there is no Aadhaar field (adding one is storage). A participant who
    /// furnished only an Aadhaar is therefore treated as not having furnished, which withholds where the statute
    /// exempts — the conservative direction, and the deductee recovers it on assessment. Recorded as ours, not
    /// claimed as fidelity.</para>
    /// </summary>
    public bool AggregateThresholdAppliesOnlyToIndividualHufWithPan => NormalizedSectionCode is "194O";

    /// <summary>
    /// The section code folded for comparison: hyphens and whitespace removed, upper-cased — so a hand-authored
    /// "194-O", "194 o" and the seeded "194-O" are one entry, exactly as <see cref="IsSection194I"/> already folds
    /// its family. Parentheses are <b>kept</b>, because "194I(a)" and "194I" must stay distinguishable.
    /// </summary>
    private string NormalizedSectionCode =>
        SectionCode.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

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
    private bool IsSection194I => NormalizedSectionCode is "194I" or "194I(A)" or "194I(B)";
}
