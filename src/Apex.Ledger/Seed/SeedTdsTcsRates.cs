using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the config-driven TDS <see cref="NatureOfPayment"/> and TCS <see cref="NatureOfGoods"/> masters for
/// <b>FY 2025-26 (AY 2026-27)</b> (Phase 7 slice 1; mirrors <see cref="SeedGstRates"/>). Every figure is
/// <b>editable data</b>, so a future Finance-Act change is a data edit, not a code change — with one stated
/// exception, <see cref="NatureOfPayment.RateWithPanOtherThanIndividualBp"/>, which is derived because storing it
/// needs a schema migration.
///
/// <para>🔴 <b>T0-6 — SOURCING. THE RATES USED TO BE CITED TO COMMERCIAL BLOGS (cleartax, disytax), WHICH IS
/// NOT A SOURCE UNDER R7, AND THAT CITATION WAS ITSELF THE DEFECT — independent of whether the numbers were
/// right.</b> Every rate and threshold below is now cited to a <b>primary</b> source: the bare text of the
/// Income-tax Act 1961 as published by the Income-tax Department, and the Department's own rate charts. The
/// re-sourcing was done section by section against the version of each section that governs <b>FY 2025-26</b>,
/// and it found one figure that <b>DIFFERED</b> from the statute — the §194I threshold, whose WINDOW was wrong
/// and not merely its value; see the §194I rows for the statute, the money and what became of the annualised
/// figure that used to stand there.</para>
///
/// <para><b>The primary sources, once, so the rows below can cite them short:</b>
/// <list type="bullet">
///   <item>🔴 <b>[CHART-TDS]</b> Income-tax Department, "TDS Rates" —
///     <c>https://www.incometaxindia.gov.in/w/tds-rates-1</c>.
///     <b>THIS PAGE CONTRADICTS ITSELF ABOUT ITS OWN VINTAGE. DO NOT CITE IT AS "[For Assessment year 2026-27]"
///     UNQUALIFIED — THIS ROW USED TO, AND THAT WAS THE PAGE'S CLAIM RESTATED AS IF IT WERE SETTLED.</b>
///     Re-read 2026-08-20; it declares, in the same document, all three of:
///     <list type="bullet">
///       <item>“This document contains the provisions of the Income-tax Act, 1961, as amended by the Finance
///         Act, 2026.”</item>
///       <item><i>[For Assessment year 2026-27]</i> — immediately above the rate table</item>
///       <item><i>[As amended by Finance Act, 2026]</i> — the closing line; "Last reviewed and updated on:
///         30-Jul-2026"</item>
///     </list>
///     Those do not agree. <b>AY 2026-27 IS FY 2025-26, WHICH RESTS ON THE FINANCE ACT 2025, NOT 2026.</b> A table
///     built on Finance Act 2026 states the FY 2026-27 position. Which of the two the page actually shows is not
///     determinable from the page: unlike every bare-Act section page, <b>the chart carries NO "Year" metadata
///     field at all</b>, so the discriminator that resolves the section slugs does not exist here. No archived
///     correct-vintage chart was found either — <c>/w/tds-rates-2</c>, <c>-3</c> and <c>-4</c> all 404.
///     <para>The sibling slug is a DIFFERENT ACT, not an older copy, and reading one for the other is the easy
///     mistake: <c>https://www.incometaxindia.gov.in/w/tds-rates</c> is the <b>Income-tax Act 2025</b> chart —
///     “...the provisions of the Income-tax Act, 2025, as amended by the Finance Act, 2026”, <i>[For tax year
///     2026-27]</i>, sections renumbered to 392/393. Same review date, same Finance Act, different statute and a
///     different year. Only the <c>-1</c> slug is the 1961-Act chart this file is about.</para>
///     <para>✅ <b>WHY THIS IS SURVIVABLE, AND THE ONE PLACE IT IS NOT.</b> The ambiguity was chased through every
///     figure that cites this chart (§194A, §194C, §194H, §194I(a), §194I(b), §194Q). For all of them <b>except
///     one</b> the chart is <b>CORROBORATION ONLY</b> — the shipped rate and threshold are stated in the operative
///     sentence of the bare Act itself, quoted in each row below, so the chart could be wrong or mis-vintaged and
///     nothing shipped would move. <b>THE EXCEPTION IS §194A's 10%, WHICH RESTS ON THIS CHART ALONE</b>, because
///     §194A states no rate — see the §194A row.</para></item>
///   <item>⚠️ <b>[CHART-TCS]</b> Income-tax Department, "TCS Rates" —
///     <c>https://www.incometaxindia.gov.in/w/tcs-rates</c>. Not self-contradictory, but <b>UNDATED</b>: re-read
///     2026-08-20, it declares no assessment year, no tax year and no Finance Act anywhere — its only date-like
///     fields are "Upload Date 30/04/2026" and "Last reviewed and updated on: 30-Jul-2026", and it too has no
///     "Year" metadata field. <b>Unlike [CHART-TDS] this one IS load-bearing, for every with-PAN TCS rate in
///     BuildTcsDefaults</b>, because no §206C bare-Act page is cited anywhere in this file; see the note there.</item>
///   <item><b>[194A]</b> <c>https://www.incometaxindia.gov.in/w/section-194a</c> ·
///     <b>[194C]</b> <c>https://www.incometaxindia.gov.in/w/section-194c</c> ·
///     <b>[194H]</b> <c>https://www.incometaxindia.gov.in/w/section-194h-34</c> ·
///     <b>[194I]</b> <c>https://www.incometaxindia.gov.in/w/section-194-i-19</c> ·
///     <b>[194J]</b> <c>https://www.incometaxindia.gov.in/w/section-194j-30</c> ·
///     <b>[194Q]</b> <c>https://www.incometaxindia.gov.in/w/section-194q-5</c> ·
///     <b>[206AA]</b> <c>https://www.incometaxindia.gov.in/w/section-206aa-16</c> ·
///     <b>[206CC]</b> <c>https://www.incometaxindia.gov.in/w/section-206cc-8</c>
///     — the bare Act text. <b>🔴 THE SIX NUMBERED SLUGS ARE Year 2025 (= FY 2025-26). THE TWO PLAIN SLUGS ARE
///     NOT — RE-MEASURED 2026-08-20, <c>/w/section-194a</c> AND <c>/w/section-194c</c> BOTH NOW SERVE Year 2026.</b>
///     This row used to claim all eight were "the version that governs FY 2025-26"; that is no longer true and the
///     claim is withdrawn rather than restated. What the Year-2026 pages do establish is quoted in the §194A and
///     §194C rows; what they cannot establish on their own is the FY 2025-26 position, and no Year-2025 slug was
///     located for either (the section pages render no footnote definitions and offer no version picker, so the
///     substituting Act and w.e.f. date behind §194A's ₹10,000 could not be read off).</item>
/// </list>
/// <b>A trap in those URLs, recorded because it cost time — and it cuts BOTH WAYS:</b> the Department's site serves
/// ARCHIVED versions of a section under both the plain slug and the numbered ones, and which slug holds which text
/// is not predictable — the plain <c>/w/section-194i</c> serves the <b>2009</b> text with its long-repealed
/// ₹1,20,000 threshold, and the plain <c>/w/section-194h</c> serves the version <b>omitted in 1999</b>. <b>The
/// newly measured half: a plain slug also rolls FORWARD without notice.</b> <c>/w/section-194a</c> and
/// <c>/w/section-194c</c> were Year 2025 when this file was written and are Year 2026 now, so a plain slug that
/// verified correctly once can silently stop verifying. Prefer the numbered slug that pins the year. Each page
/// states the year of the text it is showing; read that field before quoting the page — and note that <b>the two
/// rate CHARTS have no such field</b>, which is exactly why [CHART-TDS]'s vintage cannot be resolved.</para>
///
/// <para>The seed reflects the Phase-7 approved decisions: §194I and §194J are <b>bifurcated</b> per Form-26Q section
/// codes (4IA/4IB, 94J-A/94J-B); §194Q no-PAN uses the special §206AA cap of 5% (not 20%); §206C(1H) sale-of-goods
/// is seeded as a <b>legacy year-gated</b> nature (default OFF for dates ≥ 01-Apr-2025) with the §206CC special
/// no-PAN cap of 1%; §206AB/§206CCA non-filer higher rates are <b>omitted</b> (FA2025). TDS base excludes
/// separately-stated GST (Circular 23/2017); every §206C TCS base includes GST (Circular 17/2020).
/// </para>
/// </summary>
public static class SeedTdsTcsRates
{
    /// <summary>The FA2025 §206C(1H) legacy cut-off: non-operative on/after this date (year-gate default OFF).</summary>
    public static readonly DateOnly LegacyGoodsCutoff = new(2025, 4, 1);

    private static readonly DateOnly Fy2025 = new(2025, 4, 1);

    /// <summary>
    /// Builds the seeded predefined TDS Nature-of-Payment set (fresh ids each call): 194A, 194C, 194H, 194I(a),
    /// 194I(b), 194J(a), 194J(b), 194Q — the Phase-7 approved set — plus the census's TDS long tail (row 6.35):
    /// <b>194T, 194R, 194S</b> (first instalment) and <b>192A, 194EE, 194G, 194K, 194LA, 194-O</b> (second
    /// instalment, 2026-09-08). Seventeen natures. FY 2025-26 rates / thresholds / notified FVU codes.
    ///
    /// <para>🔴 <b>THE TAIL IS NINE SECTIONS LONG, AND FIVE OF THE SIX ADDED IN THE SECOND INSTALMENT WERE
    /// PREVIOUSLY REJECTED ON EVIDENCE THAT TURNED OUT TO BE OURS TO FIX.</b> Four (§194-O, §194G, §194K, §194LA)
    /// had been read at slugs serving ARCHIVED text; at the slug whose "Year" field reads 2025 all four agree with
    /// the Department's rate chart, and two of them carried thresholds that would have shipped WRONG had the
    /// archived figure been believed (§194LA ₹1,00,000 where the FY 2025-26 figure is ₹5,00,000; §194-O 1% where
    /// it is 0.1%). Two (§192A, §194EE) needed an inclusive threshold boundary the engine could not express, which
    /// it now can. The remaining rejections stand and are recorded by name and reason in
    /// <see cref="LongTailSectionsNotSeeded"/>, which <c>TdsLongTailSeedTests</c> asserts against this seed so a
    /// rejected section cannot be quietly added later without someone clearing its gate.</para>
    /// </summary>
    public static IReadOnlyList<NatureOfPayment> BuildTdsDefaults()
    {
        Money? R(decimal rupees) => Money.FromRupees(rupees);
        NatureOfPayment N(string section, string name, int withPan, int withoutPan, string fvu,
            Money? single = null, Money? cumulative = null) =>
            new(Guid.NewGuid(), section, name, withPan, withoutPan, fvu, single, cumulative, Fy2025, isPredefined: true);

        return new[]
        {
            // §194A Interest other than interest on securities — THRESHOLD agrees with the statute; THE RATE IS NOT
            //   IN THE STATUTE AT ALL. Read the next paragraph before treating this row as sourced.
            //   🔴🔴 THE 1000bp BELOW IS THE ONE FIGURE IN THE TDS SET THAT RESTS ON [CHART-TDS] ALONE, AND
            //   [CHART-TDS] IS THE PAGE THAT CONTRADICTS ITSELF ABOUT ITS OWN VINTAGE (class doc). §194A DOES NOT
            //   STATE A RATE. Its operative sentence, §194A(1) [194A], ends "...deduct income-tax thereon AT THE
            //   RATES IN FORCE" — the phrase occurs exactly once in the section and no percentage appears anywhere
            //   in it. "Rates in force" is §2(37A), which points at Part II of the First Schedule to the annual
            //   Finance Act. So the true primary source for this 10% is the FINANCE ACT 2025, FIRST SCHEDULE,
            //   PART II — a document this file does not cite and which could NOT be retrieved: the Department's
            //   Finance Acts browser (/w/finance-acts) serves only "As amended by Finance Act 2026" and exposes
            //   sections, not schedules. [CHART-TDS] is a transcription of that Part II, and it is currently the
            //   only thing standing behind this figure.
            //   WHAT THAT DOES AND DOES NOT MEAN. It is NOT evidence the 10% is wrong — no source contradicts it,
            //   both live charts (1961-Act and 2025-Act) state 10%, and the figure is long-standing. It IS an
            //   unclosed R7 gap of exactly the kind T0-6 was opened for: a shipped rate with no retrievable
            //   primary basis, differing from the cleartax/disytax defect only in that the surviving source is at
            //   least a government one. THE FIGURE IS LEFT AS SHIPPED AND FLAGGED, NOT RE-ASSERTED. Closing it
            //   needs Part II of the First Schedule to the Finance Act 2025, not another chart.
            //   Chart text as read: [CHART-TDS, "Section 194A: Income by way of interest other than 'Interest on
            //   securities' 10"] — present in both the resident non-company and the domestic-company blocks.
            //   no-PAN 20% is NOT affected and is properly sourced: §206AA(1) [206AA, Year 2025] takes the HIGHER
            //   of "(i) at the rate specified in the relevant provision of this Act; or (ii) at the rate or rates
            //   in force; or (iii) at the rate of twenty per cent", and 20% is the higher whatever (ii) is.
            //   Threshold: §194A(3)(i)(d) [194A] — no deduction where the FY aggregate
            //   "does not exceed ... ten thousand rupees IN ANY OTHER CASE". That is the GENERIC (non-bank) payer,
            //   which is this SMB clone's default deductor.
            //   The sibling limbs in the same sub-clause are DELIBERATELY NOT MODELLED and are a payer-type-aware
            //   refinement, not a defect in this row: ₹50,000 where the payer is a banking company, a co-operative
            //   society carrying on banking, or a post-office deposit scheme [194A(3)(i)(a)-(c)], and ₹1,00,000 in
            //   place of that ₹50,000 where the payee is a senior citizen [194A(3), third proviso].
            //   ⚠️ VINTAGE CAVEAT ON THE THRESHOLD ONLY. Re-read 2026-08-20, [194A] now serves Year 2026,
            //   not the Year 2025 text this was originally verified against. Its §194A(3)(i)(d) reads "25[ten]
            //   thousand rupees in any other case" and (a)-(c) read "24[fifty] thousand rupees", so the ₹10,000
            //   below still matches the page — but the page now states the FY 2026-27 position, and the bracketed
            //   footnote markers 24/25 mean both figures were SUBSTITUTED. The substituting Act and its w.e.f.
            //   date could not be read: this page renders footnote MARKERS but not footnote DEFINITIONS, and no
            //   Year-2025 slug for §194A was located. So ₹10,000 is confirmed current and NOT confirmed to have
            //   been in force for FY 2025-26 by a source in this file. Left as shipped and flagged.
            N("194A", "Interest other than interest on securities", 1000, 2000, "94A",
                cumulative: R(10_000m)),
            // §194C Payments to contractors — AGREES with the statute, and the missing second rate is now BUILT.
            //   §194C(1) [194C]: "deduct an amount equal to — (i) ONE PER CENT where the payment is being made or
            //   credit is being given to an individual or a Hindu undivided family; (ii) TWO PER CENT where the
            //   payment is being made or credit is being given to a person other than an individual or a Hindu
            //   undivided family". [CHART-TDS] states the same split: "a) HUF/Individuals 1 - b) Others 2".
            //   Thresholds §194C(5) [194C]: no deduction where the sum "does not exceed thirty thousand rupees";
            //   proviso: liable where the FY aggregate "exceeds one lakh rupees". No-PAN 20% [206AA(1)(iii)].
            //   ✅ THE CHART IS CORROBORATION ONLY HERE — both rates and both thresholds are in the bare Act's own
            //   operative sentences, quoted above, so [CHART-TDS]'s vintage ambiguity cannot move this row.
            //   ⚠️ VINTAGE CAVEAT. Re-read 2026-08-20, [194C] now serves Year 2026, not Year 2025. All four
            //   figures re-verified verbatim on it and all four carry NO footnote marker — unsubstituted text, so
            //   nothing indicates a change between the two years — but as with §194A no Year-2025 slug was located
            //   and the FY 2025-26 position is therefore corroborated, not proved, by a source in this file.
            //   🔴 RateWithPanBp BELOW IS THE §194C(1)(i) INDIVIDUAL/HUF ARM. The §194C(1)(ii) 2% arm is
            //   NatureOfPayment.RateWithPanOtherThanIndividualBp — derived from this SectionCode rather than stored,
            //   because a stored second rate needs a nature_of_payment column and therefore a schema migration.
            //   TdsService.ResolveWithPanRate reads Ledger.DeducteeType to choose between them.
            //   🔴 WHAT THIS FIXED, AND WHAT USED TO STAND HERE. A comment claimed the branch existed — "(The 2%
            //   'other than Ind/HUF' branch is applied at compute by deductee type — Phase 7 slice 2.)" — naming the
            //   very method that would have had to implement it; a later comment struck that as false and left the
            //   split OPEN-ON-THE-USER pending an official-source verification. Both are now discharged. Measured on
            //   the seeded §194C with one party, one PAN and ₹50,000 assessable, varying only DeducteeType:
            //   Individual, Firm, Company and HinduUndividedFamily ALL resolved 100bp and ALL withheld ₹500.00,
            //   where a company or a firm owes ₹1,000.00. Two tests in the suite asserted that wrong figure against
            //   a company deductee and are corrected; see Tds194CDeducteeTypeTests.
            //   🔴 GRANDFATHERING travels with the branch, because ApplyReCarve pins RateBasisPoints off the posted
            //   voucher: without it, every already-posted non-Ind/HUF §194C voucher would become unalterable. The
            //   rule is explicit and pinned — TdsService.GrandfatheredRate, fed the posted voucher's own stamped
            //   rate — and never a date check.
            N("194C", "Payment to contractors/sub-contractors", 100, 2000, "94C",
                single: R(30_000m), cumulative: R(1_00_000m)),
            // §194H Commission or brokerage — AGREES with the statute.
            //   §194H [194H]: "deduct income-tax thereon at the rate of TWO per cent"; proviso: no deduction where the
            //   FY aggregate "does not exceed ... TWENTY thousand rupees". [CHART-TDS]: "Section 194H: Commission or
            //   brokerage 2". No-PAN 20% [206AA(1)(iii)].
            //   ✅ CHART IS CORROBORATION ONLY. Both figures re-verified 2026-08-20 in the operative sentence of
            //   [194H], which is Year 2025 — the right vintage — and reads "at the rate of 85[two] per cent"
            //   and "does not exceed 86-87[twenty] thousand rupees".
            N("194H", "Commission or brokerage", 200, 2000, "94H",
                cumulative: R(20_000m)),
            // §194I(a) Rent — plant/machinery/equipment. RATE AGREES; the THRESHOLD IS A PER-MONTH LIMB — SEE BELOW.
            //   §194-I(a) [194I]: "TWO per cent for the use of any machinery or plant or equipment".
            //   [CHART-TDS]: "Section 194-I: Rent a) Plant & Machinery 2". No-PAN 20% [206AA(1)(iii)].
            //   🔴 THE FVU CODE BELOW CHANGED FROM "4IA" TO "4-IA" ON 2026-09-08, AND IT IS A WRONG-FIGURE FIX,
            //   NOT A TIDY-UP. The notified Form 26Q spells the two §194-I codes WITH A HYPHEN — [FORM-26Q],
            //   note 16: "194-I(a) Rent 4-IA" and "194-I (b) Rent 4-IB", read first-hand from the Department's own
            //   PDF. The seed shipped the unhyphenated spelling, and FvuWriter, Form26Q and Form16APdf all emit it,
            //   so every §194-I rent deduction was reported to NSDL under a code the notified form does not carry.
            //   🔴 CORRECTING THE SEED IS ONLY HALF OF IT — the code is PERSISTED PER NATURE, so every book created
            //   before today still holds "4IA"/"4IB" and would keep filing it. There is no schema budget for a data
            //   migration (v61 belongs to the GST track), so the fix is applied where it reaches the return instead:
            //   NatureOfPayment.NotifiedFvuSectionCode normalises both spellings to the notified one, Form26Q emits
            //   THAT, and a legacy book and a book seeded from this file today now file the identical code. The
            //   stored value is left untouched and inert. What a migration would still have to do — if the user
            //   wants the stored values normalised as well — is written out on NotifiedFvuSectionCode.
            N("194I(a)", "Rent — plant/machinery/equipment", 200, 2000, "4-IA"),
            // §194I(b) Rent — land/building/furniture/fittings. RATE AGREES; PER-MONTH THRESHOLD — SEE BELOW.
            //   §194-I(b) [194I]: "TEN per cent for the use of any land or building (including factory building) or
            //   land appurtenant to a building (including factory building) or furniture or fittings".
            //   [CHART-TDS]: "b) Land or building or furniture or fitting 10". No-PAN 20% [206AA(1)(iii)].
            //   ✅ CHART IS CORROBORATION ONLY FOR BOTH §194I ROWS. Re-verified 2026-08-20: [194I] is
            //   Year 2025 — the right vintage — and its single operative sentence carries both rates as
            //   "(a) two per cent ... and (b) ten per cent ...". The chart's vintage ambiguity cannot move either.
            //
            //   🔴🔴 NEITHER §194I ROW ABOVE CARRIES A THRESHOLD ARGUMENT, AND THAT IS DELIBERATE. READ THIS BEFORE
            //   ADDING ONE BACK.
            //   THE STATUTE. §194-I, first proviso, as substituted for FY 2025-26 [194I]: "no deduction shall be made
            //   under this section, where the income by way of rent credited or paid FOR A MONTH OR PART OF A MONTH by
            //   such person to the account of, or to, the payee, DOES NOT EXCEED FIFTY THOUSAND RUPEES". That is a
            //   PER-MONTH limb, and §194-I carries NO ANNUAL-AGGREGATE LIMB AT ALL. The threshold therefore does not
            //   belong in either of this master's two stored threshold fields, both of which the engine reads as
            //   per-transaction and per-FINANCIAL-YEAR tests. It lives on NatureOfPayment.MonthlyThreshold, derived
            //   from this SectionCode exactly as RateWithPanOtherThanIndividualBp and
            //   ChargesOnlyExcessOverCumulativeThreshold are, and for the same reason: a third stored threshold needs
            //   a natures_of_payment column and therefore a schema migration, and the versions after 51 are
            //   allocated to other tracks. Deriving it round-trips exactly, because the section code is persisted.
            //   WHAT USED TO STAND HERE, AND THE MONEY IT COST. A CumulativeThreshold of ₹6,00,000 per FINANCIAL YEAR
            //   on both rows — the monthly figure annualised (50,000 x 12). The two are not the same test. One
            //   month's rent of ₹60,000 with nothing else in the year: the statute deducts, because ₹60,000 exceeds
            //   the monthly ₹50,000, and at §194-I(b) that is ₹6,000.00. The annualised rule deducted ₹0.00, because
            //   ₹60,000 is nowhere near ₹6,00,000 — ₹6,000.00 of UNDER-deduction on one ordinary rent bill, with the
            //   deductor answering for it under §201 and interest under §201(1A).
            //   🔴 AND THE ₹6,00,000 ALREADY PERSISTED IN EVERY EXISTING BOOK IS NOT MIGRATED, NOT RE-READ AND NOT
            //   DELETED — IT IS INERT. NatureOfPayment.AggregateThreshold never consults CumulativeThreshold on a
            //   per-month nature, so a book that persisted ₹6,00,000 and a book seeded from this file today compute
            //   the identical withholding on every input. That is what lets this ship with Schema.CurrentVersion
            //   unchanged; Tds194IMonthlyThresholdTests pins it against a nature carrying the legacy figure.
            //   🔴 GRANDFATHERING travels with the window, on the user's ruling. §194C's grandfathering absorbs a
            //   RATE disagreement; here the drift is in whether the threshold was CROSSED AT ALL, so what is pinned
            //   is the posted OUTCOME — TdsService.GrandfatheredLiability, fed the posted voucher's own stamped
            //   AssessableValue and TdsAmount, and never a date check.
            //   🔴 "4-IB", hyphenated — same fix, same source, same reasoning as the §194I(a) row above.
            N("194I(b)", "Rent — land/building/furniture/fittings", 1000, 2000, "4-IB"),
            // §194J(a) Technical services / call-centre / certain royalty — AGREES with the statute.
            //   §194J(1) [194J]: "TWO per cent of such sum in case of fees for technical services (not being a
            //   professional services), or royalty where such royalty is in the nature of consideration for sale,
            //   distribution or exhibition of cinematographic films"; further proviso: "as if for the words 'ten per
            //   cent', the words 'two per cent' had been substituted in the case of a payee, engaged only in the
            //   business of operation of call centre". Threshold — proviso (B)(ii): FY aggregate "does not exceed
            //   ... FIFTY thousand rupees, in the case of fees for technical services". No-PAN 20% [206AA(1)(iii)].
            N("194J(a)", "Fees for technical services / call-centre / certain royalty", 200, 2000, "94J-A",
                cumulative: R(50_000m)),
            // §194J(b) Professional services / royalty / non-compete — AGREES with the statute.
            //   §194J(1) [194J]: "... and TEN per cent of such sum in other cases". Threshold — proviso (B)(i), (iii)
            //   and (iv): FY aggregate "does not exceed ... FIFTY thousand rupees" for fees for professional services,
            //   for royalty, and for a sum referred to in §28(va). No-PAN 20% [206AA(1)(iii)].
            N("194J(b)", "Fees for professional services / royalty / non-compete", 1000, 2000, "94J-B",
                cumulative: R(50_000m)),
            // §194Q Purchase of goods — AGREES with the statute, including the excess-only base.
            //   §194Q(1) [194Q]: a buyer paying a resident seller "for purchase of any goods of the value or aggregate
            //   of such value EXCEEDING FIFTY LAKH RUPEES in any previous year ... shall ... deduct an amount equal to
            //   0.1 PER CENT OF SUCH SUM EXCEEDING FIFTY LAKH RUPEES". [CHART-TDS] repeats the excess-only rule:
            //   "Note: TDS is deductible on sum exceeding Rs. 50 lakhs".
            //   ✅ CHART IS CORROBORATION ONLY. Re-verified 2026-08-20: [194Q] is Year 2025 — the right
            //   vintage — and the rate, the ₹50,00,000 trigger AND the excess-only base are all in the one
            //   operative sentence of §194Q(1), so this row does not depend on the chart in any respect.
            //   The excess-only carve is T0-1; see
            //   NatureOfPayment.ChargesOnlyExcessOverCumulativeThreshold.
            //   No-PAN 5%, NOT 20% — §206AA, second proviso [206AA]: "where the tax is required to be deducted under
            //   section 194Q, the provisions of clause (iii) shall apply as if for the words 'twenty per cent', the
            //   words 'FIVE PER CENT' had been substituted".
            N("194Q", "Purchase of goods", 10, 500, "94Q",
                cumulative: R(50_00_000m)),

            // ═══════════════════════════════════════════════════════════════════════════════════════════════════
            //  CENSUS ROW 6.35 — THE TDS LONG TAIL, FIRST INSTALMENT (2026-09-08).
            //
            //  🔴 THREE SECTIONS, NOT FOURTEEN, AND THE SHORTFALL IS DELIBERATE. The census row names "~14
            //  further sections". Every candidate was taken to the bare Act on incometaxindia.gov.in and
            //  cross-read against [CHART-TDS]; only these three cleared ALL FOUR gates below. The twelve that
            //  did not are recorded — by name and by reason — in ShippableLongTailGates on this class, which is
            //  what the next seeding pass should read before re-attempting any of them. Shipping a rate that
            //  fails a gate is precisely the T0-6 defect this file was cleaned of.
            //
            //  THE FOUR GATES, each of which cost a real candidate:
            //   G1. THE BARE ACT MUST STATE THE RATE. A section whose operative sentence says "at the rates in
            //       force" (§2(37A) → Part II of the First Schedule to the Finance Act) leaves the figure resting
            //       on [CHART-TDS] alone — the page that contradicts itself about its own vintage. §194A already
            //       ships that way and is FLAGGED as an unclosed R7 gap; adding more would DEEPEN it, not close
            //       it. Rejected on G1: §193, §194, §194B, §194BB, §194D.
            //   G2. THE ACT TEXT MUST AGREE WITH [CHART-TDS]. Where the two disagree, the slug is serving an
            //       archived text and NEITHER source can be trusted for the FY 2025-26 figure.
            //       🔴 ALL FOUR G2 REJECTIONS RECORDED HERE WERE WITHDRAWN ON 2026-09-08 AND THE SECTIONS SEEDED —
            //       §194-O, §194G, §194K and §194LA. The paragraph that stood here read them as real
            //       disagreements; they were OUR VINTAGE ERROR. The rejections rested on /w/section-194-o and
            //       -194-o-1 ("one per cent"), /w/section-194g ("ten per cent" + ₹1,000), /w/section-194k (the
            //       version OMITTED in 1999) and a /w/section-194la serving ₹1,00,000 — and EVERY ONE of those
            //       slugs serves an archived text. The Department publishes each historical version under its own
            //       numbered slug in NO predictable order (for §194G the years run 2000, 2009, 2001, 2010, ...),
            //       so a slug's NUMBER means nothing and its "Year" field means everything. Enumerating the slugs
            //       and reading that field found the Year-2025 text of all four, and at that vintage all four
            //       agree with the chart. The right-vintage slug is named in each seeded row.
            //       🔴 THE LESSON, WHICH COST TWO WRONG FIGURES: A G2 "DISAGREEMENT" IS FAR MORE LIKELY TO BE A
            //       WRONG SLUG THAN A WRONG CHART. Enumerate the slugs and read the Year field BEFORE recording a
            //       rejection. Believing the archived §194LA would have shipped a ₹1,00,000 threshold where the
            //       statute says ₹5,00,000 — ₹10,000.00 over-deducted on a ₹1,00,001 compensation.
            //   G3. THE SECTION'S THRESHOLD SHAPE MUST BE ONE THE MASTER CAN EXPRESS. ✅ THE INCLUSIVE BOUNDARY
            //       THAT REJECTED §192A AND §194EE HERE IS NOW BUILT and both are seeded. The engine used to test
            //       `(prior + current) > AggregateThreshold` and nothing else, which is exactly "no deduction
            //       where the aggregate does not exceed X" but NOT "where the payment is LESS THAN X" — the
            //       latter deducts AT X, so a seeded row silently under-deducted on the boundary rupee (₹5,000.00
            //       on a §192A payment of exactly ₹50,000). NatureOfPayment.AggregateThresholdIsInclusive now
            //       carries the distinction and the test picks >= or > from it. Storage was not needed: it is
            //       derived from the section code like the three flags before it.
            //       🔴 AND EVERY ALREADY-SHIPPED SECTION WAS RE-READ WHEN THE FLAG WENT IN, because a flag that
            //       reveals a boundary error is worthless if nobody checks the rows that predate it. §194A, §194C,
            //       §194H, §194-I, §194J, §194T, §194R, §194S all read "does not exceed" and §194Q reads
            //       "exceeding" at their own cited slugs — NOT ONE of them is inclusive, so no shipped section was
            //       withholding the wrong amount at its boundary. TdsInclusiveThresholdTests pins that over the
            //       whole seed. What still fails G3 is a shape the MASTER cannot hold at all, not a boundary:
            //       §194N's banded, non-filer-gated rate needs two rates and a band edge on one nature.
            //   G4. 🔴🔴 WITHDRAWN 2026-09-08 — DO NOT RE-APPLY IT. It read "the deductor must plausibly be this
            //       product's user", and on that basis §194K (mutual funds), §192A (EPF trustees), §194EE (the
            //       post office), §194LA (an acquiring authority) and §194G (lottery stockists) were kept out of
            //       the product. R7 (ruling 14) makes the VENDOR'S published documentation the fidelity ground
            //       truth, and it ships every one of them — see the [VENDOR-NATURES] note below. The Nature of
            //       Payment master is a selectable list, not a prediction about who the operator is. G4 was a
            //       divergence from the clone dressed as caution, and it is the reason this row sat PARTIAL.
            //
            //  Every row below therefore has: a rate quoted from the operative sentence of the bare Act, read at
            //  BOTH the plain slug and a numbered slug on 2026-09-08; a threshold quoted from the section's own
            //  proviso in "does not exceed" form; a no-PAN rate derived from §206AA; and a Form-26Q section code
            //  taken from the NOTIFIED FORM ITSELF, not from a chart or a vendor utility — see [FORM-26Q].
            //
            //  🔴 [FORM-26Q] — THE SECTION CODES, AND WHY THIS SOURCE AND NOT ANOTHER. Income-tax Department,
            //  notified FORM NO. 26Q, "Quarterly statement of deduction of tax under sub-section (3) of section
            //  200 in respect of payments other than salary", at
            //  https://www.incometaxindia.gov.in/documents/d/guest/103120000000007861-pdf-2 — pages 7-8 carry the
            //  official three-column table "Section | Nature of Payment | Section Code". Read 2026-09-08. This
            //  matters because the FvuSectionCode is NOT cosmetic: FvuWriter, Form26Q and Form16APdf all emit it,
            //  so a code taken from a blog or guessed from a pattern would be a wrong figure in a filed return.
            //  ✅ THE CITATION GAP ON THE EIGHT ORIGINAL CODES IS NOW CLOSED (2026-09-08, second pass). The
            //  previous pass could only report that those eight carried no citation in this file. The notified
            //  table has since been read row by row and every one of them CONFIRMED verbatim against it:
            //    194A → 94A · 194C → 94C · 194H → 94H · 194J(a) → 94J-A · 194J(b) → 94J-B · 194Q → 94Q,
            //  and from the first long-tail pass 194T → 94T · 194R → 94R · 194S → 94S. All nine agree.
            //  🔴 EXACTLY ONE DID NOT, AND IT IS NOW FIXED RATHER THAN MERELY REPORTED: the notified form spells
            //  the §194-I codes "4-IA" and "4-IB", WITH A HYPHEN, where this seed shipped "4IA"/"4IB" — a wrong
            //  section code in filed returns. The seed rows are corrected above and, because the code is persisted
            //  per nature and there is no schema budget for a data migration, the value is ALSO normalised at the
            //  point of emission (NatureOfPayment.NotifiedFvuSectionCode → Form26Q), so existing books stop filing
            //  the wrong code without any migration. See that property for the full reasoning and for what a
            //  migration of the STORED values would additionally have to do, which is the user's call.
            //
            //  🔴🔴 [VENDOR-NATURES] — G4 IS WITHDRAWN AS A GATE. IT WAS OURS, NOT THE VENDOR'S, AND IT WAS
            //  KEEPING SOURCED SECTIONS OUT OF THE PRODUCT. G4 rejected a section when "the deductor must plausibly be this product's user"
            //  failed: §194K because the deductor is a Mutual Fund, §192A because it is the EPF trustees, §194EE
            //  the post office, §194LA an acquiring authority, §194G lottery stockists. R7 (ruling 14) makes the
            //  VENDOR'S OWN PUBLISHED DOCUMENTATION the fidelity ground truth, and it contradicts G4 flatly. The
            //  TallyPrime Nature-of-Payment helper list (Alt+H on the Nature of Payment screen) —
            //  https://help.tallysolutions.com/tds-nature-of-payment-income-tax-act/, read 2026-09-08 — ships ALL
            //  of them: "Payment of Accumulated Balance Due to an Employee" (§192A), "Payments in Respect of
            //  Deposits Under NSS u/s 80CCA(2) of IT Act, 1961" (§194EE), "Income from Stocking, Distributing,
            //  Purchasing or Selling Lottery Tickets" (§194G), "Income from Units of Specified Mutual Fund or
            //  Administrator of Undertaking or Company" (§194K), "Payment of Compensation on Acquisition of
            //  Certain Immovable Property" (§194LA) and "Payments to Any E-Commerce Participant" (§194-O). The
            //  Nature of Payment master is a SELECTABLE LIST, not a prediction about who the operator is; a firm
            //  that never pays lottery commission simply never picks §194G. Excluding them was a divergence from
            //  the clone dressed as caution. ⚠️ G1, G2 and G3 are UNTOUCHED and still bind — they are CITATION
            //  gates, and the vendor list attests that the product ships a nature, never what its rate is.
            //  (The vendor page is written against the Income-tax Act 2025's renumbered sections — §392/§393 with
            //  four-digit payment codes — so it is read for WHICH NATURES EXIST, by subject matter, and never for
            //  a 1961-Act rate, threshold or section code. Those come from the bare Act and [FORM-26Q] below.)

            // §194T Payments to partners of firms — 🔴 THE ONE AN INDIAN BUSINESS IS MOST LIKELY TO HIT, and the
            //   newest. Every partnership firm that pays a partner salary, remuneration, commission, bonus or
            //   interest is the deductor, and the obligation did not exist before this financial year.
            //   ✅ G1 RATE IS IN THE ACT, and the sentence carries the whole rule. §194T(1): "Any person, being a
            //   firm, responsible for paying any sum in the nature of salary, remuneration, commission, bonus or
            //   interest to a partner of the firm, shall, at the time of credit of such sum to the account of the
            //   partner (including the capital account) or at the time of payment thereof, whichever is earlier
            //   shall, deduct income-tax thereon AT THE RATE OF TEN PER CENT."
            //   ✅ G3 THRESHOLD IS IN "DOES NOT EXCEED" FORM. §194T(2): "No deduction shall be made under
            //   sub-section (1) where such sum or the aggregate of such sums credited or paid or likely to be
            //   credited or paid to the partner of the firm DOES NOT EXCEED TWENTY THOUSAND RUPEES DURING THE
            //   FINANCIAL YEAR." A financial-year aggregate, which is this engine's default window.
            //   ✅ VINTAGE IS EXPLICIT — no "Year"-field guesswork was needed for this one. The Department's page
            //   heads the text "Following section 194T shall be inserted after section 194S by the FINANCE (No. 2)
            //   ACT, 2024, W.E.F. 1-4-2025", which is the first day of FY 2025-26, the year this seed encodes.
            //   READ TWICE, 2026-09-08: https://www.incometaxindia.gov.in/w/section-194t AND the numbered slug
            //   https://www.incometaxindia.gov.in/w/section-194t-1 — identical operative text and identical
            //   figures. [CHART-TDS] corroborates and is not relied on: "Section 194T: Payments of any sum in the
            //   nature of salary, remuneration, commission, bonus or interest to a partner of the firm ... 10",
            //   with the notes "(1) This provision is effective from 01-04-2025" and "(2) No deduction if
            //   aggregate of such sum paid/payable does not exceed Rs. 20,000 during the financial year".
            //   Code 94T — [FORM-26Q]: "194T | Payment of salary, remuneration, commission, bonus or interest to
            //   a partner of firm | 94T", carrying the footnote "Inserted by the IT (Seventh Amdt.) Rules, 2025,
            //   w.e.f. 27-3-2025". So the RETURN could not carry this section before 27-3-2025 either.
            //   No-PAN 20% and NOT 5% — §206AA(1) [206AA] takes the higher of the section rate, the rates in
            //   force, or twenty per cent, and 20% beats 10%. The two provisos that substitute "five per cent"
            //   name ONLY §194-O and §194Q; §194T is in neither.
            N("194T", "Payment of salary/remuneration/commission/bonus/interest to a partner of a firm",
                1000, 2000, "94T", cumulative: R(20_000m)),

            // §194R Benefit or perquisite in respect of business or profession — the second most likely: free
            //   samples, sponsored trips, incentives in kind to dealers and distributors all fall here.
            //   ✅ G1 RATE IS IN THE ACT. §194R(1): "...shall, before providing such benefit or perquisite ...
            //   ensure that tax has been deducted in respect of such benefit or perquisite AT THE RATE OF TEN PER
            //   CENT of the value or aggregate of value of such benefit or perquisite".
            //   ✅ G3 THRESHOLD IS IN "DOES NOT EXCEED" FORM. §194R(1), second proviso: "...the provisions of this
            //   section shall not apply in case of a resident where the value or aggregate of value of the benefit
            //   or perquisite provided or likely to be provided to such resident during the financial year DOES
            //   NOT EXCEED TWENTY THOUSAND RUPEES".
            //   READ TWICE, 2026-09-08: https://www.incometaxindia.gov.in/w/section-194r AND the numbered slug
            //   https://www.incometaxindia.gov.in/w/section-194r-2 — same rate, same threshold. The numbered slug
            //   additionally carries "Explanation 2 ... the provisions of sub-section (1) shall apply to any
            //   benefit or perquisite, WHETHER IN CASH OR IN KIND or partly in cash and partly in kind", i.e. it
            //   is the LATER text of the two, and it did not move the rate or the threshold.
            //   🔴 CITATION CORRECTED 2026-09-08 (second pass): NEITHER slug above is the FY 2025-26 text. Their
            //   "Year" fields read 2022 and 2024. The FY 2025-26 text is
            //   https://www.incometaxindia.gov.in/w/section-194r-4, Year 2025, and it was read for this
            //   correction: "at the rate of ten per cent" and "does not exceed twenty thousand rupees", i.e. THE
            //   SHIPPED FIGURES ARE UNCHANGED AND REMAIN CORRECT. Only the citation was of the wrong vintage —
            //   recorded rather than silently swapped, because a right figure resting on a wrong-vintage page is
            //   exactly the shape that made four other sections look like G2 failures. ✅ G2: [CHART-TDS]
            //   agrees — "Section 194R: ... aggregate value of such benefit/perquisite exceeds Rs. 20,000 ... 10".
            //   Code 94R — [FORM-26Q]: "194R | Benefits or perquisites of business or profession | 94R".
            //   ⚠️ WHAT IS DELIBERATELY NOT MODELLED, stated so it is not mistaken for an oversight. (a) The THIRD
            //   proviso exempts a DEDUCTOR being an individual or HUF whose turnover does not exceed ₹1 crore
            //   (business) or ₹50 lakh (profession) in the preceding year. That is a property of the company, not
            //   of this master, and there is no company-level field for preceding-year turnover — a deductor
            //   inside that exemption must simply not tag the ledger with this nature. (b) The FIRST proviso's
            //   benefit-wholly-in-kind route, which the notified form gives its own code 94R-P, is a separate
            //   reporting row and is NOT seeded: this engine deducts from a money voucher, so the in-kind case has
            //   no voucher to deduct from. Both are honest divergences, not silent ones.
            N("194R", "Benefit or perquisite in respect of business or profession",
                1000, 2000, "94R", cumulative: R(20_000m)),

            // §194S Transfer of a virtual digital asset — smaller population than the two above, but it is the
            //   one section in the tail whose deductor genuinely is an ordinary buyer paying a resident seller.
            //   ✅ G1 RATE IS IN THE ACT. §194S(1): "...deduct an amount equal to ONE PER CENT of such sum as
            //   income-tax thereon".
            //   ✅ G3 THRESHOLD IS IN "DOES NOT EXCEED" FORM — and 🔴 THE SECTION HAS TWO LIMBS; THIS ROW IS THE
            //   ONE THE FORM-26Q CODE NAMES, WHICH IS WHY ₹10,000 AND NOT ₹50,000. §194S(3): "no tax shall be
            //   deducted in a case, where — (a) the consideration is payable by A SPECIFIED PERSON and the value
            //   or aggregate value of such consideration DOES NOT EXCEED FIFTY THOUSAND RUPEES during the
            //   financial year; or (b) the consideration is payable by ANY PERSON OTHER THAN A SPECIFIED PERSON
            //   and the value or aggregate value of such consideration DOES NOT EXCEED TEN THOUSAND RUPEES during
            //   the financial year." A "specified person" is, per the section's own Explanation, an individual or
            //   HUF below the ₹1 crore / ₹50 lakh turnover limits, or one with no business income at all — i.e.
            //   NOT the deductor this master serves. And [FORM-26Q] settles which limb the seeded code belongs
            //   to by naming it: "194S | Payment of consideration for transfer of virtual digital asset BY
            //   PERSONS OTHER THAN SPECIFIED PERSONS | 94S". Limb (b), threshold ₹10,000.
            //   READ TWICE, 2026-09-08: https://www.incometaxindia.gov.in/w/section-194s AND the numbered slug
            //   https://www.incometaxindia.gov.in/w/section-194s-2 — identical. ✅ G2: [CHART-TDS] agrees — "1",
            //   and restates both limbs and the "specified person" definition.
            //   🔴 CITATION CORRECTED 2026-09-08 (second pass), same as §194R above: those two slugs are Year 2022
            //   and Year 2024, not FY 2025-26. The FY 2025-26 text is
            //   https://www.incometaxindia.gov.in/w/section-194s-4, Year 2025, read for this correction — both
            //   limbs still read "does not exceed fifty thousand rupees" and "does not exceed ten thousand
            //   rupees", so THE SHIPPED FIGURES ARE UNCHANGED AND REMAIN CORRECT. Only the citation moved.
            //   No-PAN 20% — §206AA(1)(iii); §194S is named in neither five-per-cent proviso.
            //   ⚠️ The §194S(1) proviso's in-kind / asset-for-asset route (form code 94S-P) is NOT seeded, for the
            //   same reason as 94R-P: there is no money voucher to deduct from.
            N("194S", "Consideration for transfer of a virtual digital asset (payer other than a specified person)",
                100, 2000, "94S", cumulative: R(10_000m)),

            // ═══════════════════════════════════════════════════════════════════════════════════════════════════
            //  CENSUS ROW 6.35 — THE TDS LONG TAIL, SECOND INSTALMENT (2026-09-08). SIX MORE SECTIONS.
            //
            //  🔴 WHAT UNBLOCKED THEM, BECAUSE IT WAS NOT NEW LAW — IT WAS A SLUG-INDEXING TRICK AND ONE
            //  WITHDRAWN GATE. The previous pass rejected §194-O, §194G, §194K and §194LA on G2 (Act text
            //  disagrees with [CHART-TDS]) after reading the plain and low-numbered slugs. Those slugs were
            //  serving ARCHIVED texts. The Department publishes EVERY historical version of a section under its
            //  own numbered slug, in NO predictable order, and each page's "Year" metadata field is the only
            //  reliable discriminator. Enumerating the slugs and reading the Year field off each one located the
            //  FY 2025-26 (Year 2025) text for all four, and at that vintage ALL FOUR AGREE WITH THE CHART. G2 was
            //  never a real disagreement; it was a vintage error on our side. The right-vintage slug is recorded
            //  on every row below so the next reader does not repeat the enumeration.
            //  §192A and §194EE were blocked on G3 alone — an inclusive "is less than" boundary the engine could
            //  not express. It can now: NatureOfPayment.AggregateThresholdIsInclusive.
            //  G4 is withdrawn; see the block above BuildTdsDefaults's first long-tail instalment.
            //
            //  🔴 EFFECTIVE-FROM. Every row below carries EffectiveFrom = 01-Apr-2025 through the N() helper, as
            //  every seeded row does, so no rate is a bare undated constant. ⚠️ THE MODEL HAS NO EFFECTIVE-TO, and
            //  that is stated rather than implied: NatureOfPayment carries EffectiveFrom only, so a superseded
            //  rate cannot be closed off — a second row for the same section would be the only way to express a
            //  mid-year change, and adding the column is storage. This does NOT deepen defect T1-26 (the s.192
            //  salary engine's date-blind consts) — every figure here is stored, dated data, not a const — but it
            //  does not close that half of it either.

            // §192A Accumulated PF balance paid to an employee — 🔴 THE FIRST INCLUSIVE-BOUNDARY SECTION IN THE
            //   SEED, AND THE REASON THE FLAG EXISTS.
            //   ✅ G1 RATE IS IN THE ACT. §192A, Year 2025 — https://www.incometaxindia.gov.in/w/section-192a-11
            //   (the plain /w/section-192a slug serves Year 2026 and must not be read for this year): the trustees
            //   of the EPF Scheme 1952 "or any person authorised under the scheme to make payment of accumulated
            //   balance due to employees shall ... deduct income-tax thereon AT THE RATE OF TEN PER CENT".
            //   ✅ G2 [CHART-TDS] agrees: "Section 192A: Payment of accumulated balance of provident fund which is
            //   taxable in the hands of an employee 10".
            //   🔴 G3 — THE BOUNDARY IS INCLUSIVE, AND THIS IS THE WHOLE POINT OF THE ROW. The proviso reads
            //   "no deduction under this section shall be made where the amount of such payment or, as the case may
            //   be, the aggregate amount of such payment to the payee IS LESS THAN FIFTY THOUSAND RUPEES." "Is less
            //   than", NOT "does not exceed" — so a payment of EXACTLY ₹50,000 IS liable, and at 10% that is
            //   ₹5,000.00 the strictly-greater test would have withheld nowhere.
            //   AggregateThresholdIsInclusive carries it; TdsInclusiveThresholdTests measures both sides of the
            //   boundary rupee.
            //   ⚠️ ONE NARROWING, STATED. The proviso's aggregate carries NO "during the financial year" qualifier
            //   (unlike §194EE's, which does) — it is the aggregate of payments to that payee, period. The engine's
            //   aggregate window is the financial year, so instalments of one accumulated balance paid across an
            //   FY boundary are two windows here and one aggregate in the statute. A PF accumulated balance is paid
            //   out as a single settlement in practice, so the shapes coincide in the ordinary case; the divergence
            //   is ours and is recorded rather than hidden. Closing it needs a per-payee lifetime window, which is
            //   a fourth threshold window and therefore its own decision.
            //   Code 192A — [FORM-26Q]: "192A | Payment of accumulated balance due to an employee | 192A". The
            //   section code and the FVU code are the same string for this one section; that is what the form says.
            //   No-PAN 20% — §206AA(1)(iii); §192A is named in neither five-per-cent proviso.
            N("192A", "Accumulated provident-fund balance paid to an employee",
                1000, 2000, "192A", cumulative: R(50_000m)),

            // §194EE Payments out of a National Savings Scheme deposit (§80CCA(2)(a)) — the second inclusive row.
            //   ✅ G1 RATE IS IN THE ACT. §194EE, Year 2025 —
            //   https://www.incometaxindia.gov.in/w/section-194ee-35 (the plain slug serves Year 2026): "The person
            //   responsible for paying to any person any amount referred to in clause (a) of sub-section (2) of
            //   section 80CCA shall, at the time of payment thereof, deduct income-tax thereon AT THE RATE OF TEN
            //   PER CENT".
            //   ✅ G2 [CHART-TDS] agrees: "Section 194EE: Payment in respect of deposit under National Savings
            //   scheme 10".
            //   🔴 G3 INCLUSIVE, same shape as §192A: "no deduction shall be made under this section where the
            //   amount of such payment or, as the case may be, the aggregate amount of such payments to the payee
            //   DURING THE FINANCIAL YEAR IS LESS THAN TWO THOUSAND FIVE HUNDRED RUPEES". Note this one DOES say
            //   "during the financial year", so unlike §192A the window needs no narrowing — it is exactly the
            //   engine's default. At exactly ₹2,500 the deduction is ₹250.00.
            //   ⚠️ The second proviso — "nothing contained in this section shall apply to the payment of the said
            //   amount to the HEIRS of the assessee" — is NOT modelled: the ledger master has no
            //   payee-is-an-heir fact, and there is nowhere to record one without storage. A deductor paying an
            //   heir must not tag the voucher with this nature. Ours, and stated.
            //   Code 4EE — [FORM-26Q]: "194EE | Payments in respect of deposits under National Savings Schemes |
            //   4EE". No-PAN 20% [206AA(1)(iii)].
            N("194EE", "Payment out of a National Savings Scheme deposit (§80CCA(2)(a))",
                1000, 2000, "4EE", cumulative: R(2_500m)),

            // §194G Commission etc. on the sale of lottery tickets — 🔴 ITS LIMB IS PER-PAYMENT, NOT AN FY
            //   AGGREGATE, AND SEEDING IT AS A CUMULATIVE WOULD HAVE BEEN A DEFECT.
            //   ✅ G1 RATE IS IN THE ACT. §194G(1), Year 2025 —
            //   https://www.incometaxindia.gov.in/w/section-194g-34: "Any person who is responsible for paying ...
            //   to any person, who is or has been stocking, distributing, purchasing or selling lottery tickets,
            //   any income by way of commission, remuneration or prize (by whatever name called) on such tickets
            //   IN AN AMOUNT EXCEEDING 84[TWENTY] THOUSAND RUPEES shall ... deduct income-tax thereon AT THE RATE
            //   OF 85[TWO] PER CENT."
            //   ✅ G2 [CHART-TDS] agrees: "Section 194G: Commission, etc., on sale of lottery tickets 2". 🔴 THE
            //   PREVIOUS PASS REJECTED THIS ROW ON G2 having read "ten per cent" and "one thousand rupees" — that
            //   is the PRE-2016 text, served by the plain slug. Both figures were substituted (footnote markers 84
            //   and 85 above); the Year 2025 page reads 2% and ₹20,000 and agrees with the chart exactly.
            //   ✅ G3 — the boundary is "IN AN AMOUNT EXCEEDING twenty thousand rupees", i.e. strictly greater, and
            //   it qualifies THE INCOME BEING PAID, not a running total: §194G has no aggregate limb and no
            //   "during the financial year" anywhere in it. It is therefore seeded as a SINGLE-TRANSACTION
            //   threshold, which the engine already tests strictly. Seeding it as a cumulative would have made two
            //   ₹15,000 commissions liable, which the section does not.
            //   Code 94G — [FORM-26Q]: "194G | Commission, prize etc., on sale of lottery tickets | 94G".
            //   No-PAN 20% [206AA(1)(iii)].
            N("194G", "Commission/remuneration/prize on the sale of lottery tickets",
                200, 2000, "94G", single: R(20_000m)),

            // §194K Income in respect of units of a mutual fund / specified undertaking / specified company.
            //   ✅ G1 RATE IS IN THE ACT. §194K, Year 2025 —
            //   https://www.incometaxindia.gov.in/w/section-194k-30: any person paying a resident income in
            //   respect of units of a §10(23D) Mutual Fund, units from the Administrator of the specified
            //   undertaking, or units from the specified company "shall ... deduct income-tax thereon AT THE RATE
            //   OF TEN PER CENT".
            //   🔴 THIS IS THE RE-INSERTED (FA2020) §194K, NOT THE ONE OMITTED IN 1999 — and distinguishing them
            //   was the whole difficulty. The previous pass read the plain /w/section-194k slug, which serves the
            //   Year 2000 text: 20% for a company payee, 15% for others, and "no deduction shall be made ... on or
            //   after the 1st day of June, 1999". The Year 2025 page carries none of that; it carries the
            //   three-limb units definition and the flat ten per cent quoted above.
            //   ✅ G2 [CHART-TDS] agrees: "Section 194K: Income in respect of units payable to resident person 10".
            //   ✅ G3 "does not exceed" form, FY aggregate — proviso (i): the section does not apply "where the
            //   amount of such income or, as the case may be, the aggregate of the amounts of such income credited
            //   or paid or likely to be credited or paid DURING THE FINANCIAL YEAR ... DOES NOT EXCEED 92[TEN]
            //   THOUSAND RUPEES". Exactly the engine's default window and boundary.
            //   ⚠️ Proviso (ii) — the section does not apply "if the income is of the nature of CAPITAL GAINS" — is
            //   NOT modelled. It is a property of the payment, not of the master, and there is no capital-gains
            //   flag on a voucher line; a deductor paying out unit capital gains must not tag the line with this
            //   nature. Ours, and stated.
            //   Code 94K — [FORM-26Q]: "194K | Income in respect of units | 94K". No-PAN 20% [206AA(1)(iii)].
            N("194K", "Income in respect of units of a mutual fund / specified undertaking / specified company",
                1000, 2000, "94K", cumulative: R(10_000m)),

            // §194LA Compensation on the compulsory acquisition of immovable property (not agricultural land).
            //   ✅ G1 RATE IS IN THE ACT. §194LA, Year 2025 —
            //   https://www.incometaxindia.gov.in/w/section-194la-21: any person paying a resident compensation or
            //   enhanced compensation on the compulsory acquisition of immovable property other than agricultural
            //   land "shall ... deduct an amount equal to TEN PER CENT of such sum as income-tax thereon".
            //   ✅ G2 [CHART-TDS] agrees: "Section 194LA: Payment of compensation on acquisition of certain
            //   immovable property 10".
            //   🔴 THE THRESHOLD IS ₹5,00,000, NOT THE ₹1,00,000 THE PREVIOUS PASS READ — that figure is the
            //   pre-2020 text and it was the reason this row was rejected. The Year 2025 first proviso reads: "no
            //   deduction shall be made under this section where the amount of such payment or, as the case may
            //   be, the aggregate amount of such payments to a resident DURING THE FINANCIAL YEAR DOES NOT EXCEED
            //   93[FIVE LAKH] RUPEES" — the bracketed 93 marks it as substituted. Seeding ₹1,00,000 would have
            //   OVER-deducted ₹10,000.00 on a ₹1,00,001 compensation the statute exempts. ✅ G3: "does not exceed",
            //   FY aggregate — the engine's default on both counts. The right-vintage page carries no third-party
            //   copyright line either; that was an artefact of the slug the previous pass read.
            //   ⚠️ The second proviso — no deduction where the award or agreement is exempt from income-tax under
            //   §96 of the RFCTLARR Act 2013 — is NOT modelled: it is a property of the acquisition, not of the
            //   master, and there is nowhere to record it. Such a payment must not be tagged with this nature.
            //   Code 4LA — [FORM-26Q]: "194LA | Payment of Compensation on acquisition of certain immovable
            //   property | 4LA". No-PAN 20% [206AA(1)(iii)].
            N("194LA", "Compensation on compulsory acquisition of immovable property (not agricultural land)",
                1000, 2000, "4LA", cumulative: R(5_00_000m)),

            // §194-O Sums paid by an e-commerce operator to an e-commerce participant — 🔴 THE ONE ROW IN THIS
            //   INSTALMENT WHOSE THRESHOLD IS CONDITIONAL ON WHO THE PAYEE IS.
            //   ✅ G1 RATE IS IN THE ACT, AT 0.1% AND NOT 1%. §194-O(1), Year 2025 —
            //   https://www.incometaxindia.gov.in/w/section-194-o-6: the operator "shall ... deduct income-tax at
            //   the rate of 1[0.1] PER CENT of the gross amount of such sales or services or both". 🔴 THE
            //   PREVIOUS PASS REJECTED THIS ON G2 having read "one per cent" at /w/section-194-o and
            //   /w/section-194-o-1. Those slugs serve Year 2020 and Year 2021; the ORIGINAL rate was one per cent
            //   and the bracketed footnote marker 1 above shows it was substituted. Reading a §194-O slug without
            //   checking its Year field is exactly how a repealed rate gets shipped.
            //   ✅ G2 [CHART-TDS] agrees at the right vintage: "Section 194-O: Payment or credit of amount by the
            //   e-commerce operator to e-commerce participant 0.1". 10 bp below.
            //   🔴 G3 — THE EXEMPTION IS CONDITIONAL, AND SEEDING A FLAT ₹5,00,000 WOULD HAVE BEEN A WRONG FIGURE.
            //   §194-O(2): "No deduction under sub-section (1) shall be made from any sum credited or paid ... to
            //   the account of an e-commerce participant, BEING AN INDIVIDUAL OR HINDU UNDIVIDED FAMILY, where the
            //   gross amount of such sale or services or both during the previous year DOES NOT EXCEED FIVE LAKH
            //   RUPEES AND such e-commerce participant HAS FURNISHED HIS PERMANENT ACCOUNT NUMBER OR AADHAAR
            //   NUMBER to the e-commerce operator." Three conditions, all required. A COMPANY participant has no
            //   exemption at all and is liable from the first rupee, so a plain cumulative limb would have
            //   withheld ₹0.00 on a ₹4,00,000 payout owing ₹400.00; no limb at all would have over-withheld on the
            //   individual sellers the sub-section exists to protect.
            //   NatureOfPayment.AggregateThresholdAppliesOnlyToIndividualHufWithPan carries the condition and
            //   TdsService drops the limb entirely for anyone outside it. The boundary itself is "does not exceed"
            //   — strict — so it is NOT flagged inclusive. Aadhaar is a documented narrowing; see that property.
            //   Code 94O — [FORM-26Q]: "194-O | Payment of certain sums by e-commerce operator to e-commerce
            //   participant | 94O".
            //   🔴 NO-PAN 5%, NOT 20% — and §194-O is one of only TWO sections in the Act that get this. §206AA,
            //   FIRST proviso, Year 2025 — https://www.incometaxindia.gov.in/w/section-206aa-16: "Provided that
            //   where the tax is required to be deducted under SECTION 194-O, the provisions of clause (iii) shall
            //   apply as if for the words 'twenty per cent', the words 'FIVE PER CENT' had been substituted". (The
            //   second proviso does the same for §194Q, which is why that row also carries 500.)
            N("194-O", "Sums paid by an e-commerce operator to an e-commerce participant",
                10, 500, "94O", cumulative: R(5_00_000m)),
            // ═══════════════════════════════════════════════════════════════════════════════════════════════════

            // ───────────────────────────────────────────────────────────────────────────────────────────────────
            // 🔴 FORWARD NOTE FOR WHOEVER SEEDS §194N — READ BEFORE COPYING [CHART-TDS]. NOT A DEFECT TODAY:
            // §194N IS NOT SEEDED, so nothing shipped is wrong. This is here so the next seeding pass does not
            // ship the chart's error.
            // [CHART-TDS] STATES §194N INCONSISTENTLY ACROSS ITS OWN BLOCKS, and the block a reader is most
            // likely to copy is the WRONG one. Measured 2026-08-20:
            //   · §1.1 "where the person is resident in India" — WRONG. Non-filer limb: "a) 2% from the amount
            //     withdrawn in cash if the aggregate of the amount of withdrawal exceeds Rs. 20 lakhs during the
            //     previous year; or b) 5% ... exceeds Rs. 1 crore". THE 2% LIMB HAS NO UPPER BOUND, so read
            //     literally the two limbs OVERLAP above ₹1 crore and 2% appears to apply there too.
            //   · §1.2 "where the person is not resident in India" — WRONG, same missing bound.
            //   · §2.1 "where the company is a domestic company" — CORRECT: "a) 2% ... exceeds Rs. 20 lakhs BUT
            //     NOT EXCEEDING RS. 1 CRORE during the previous year; or b) 5% ... exceeds Rs. 1 crore".
            // THE STATUTE SETTLES IT, and the correct boundary is the domestic-company block's. §194N, first
            // proviso, clause (ii) — https://www.incometaxindia.gov.in/w/section-194n-7, Year 2025, so the
            // vintage is right — reads: "(a) an amount equal to two per cent of the sum where the amount or
            // aggregate of amounts, as the case may be, being paid in cash exceeds twenty lakh rupees during the
            // previous year BUT DOES NOT EXCEED ONE CRORE RUPEES; or (b) an amount equal to five per cent of the
            // sum where the amount or aggregate of amounts ... exceeds one crore rupees".
            // SO: 2% applies ONLY on the band ₹20 lakh → ₹1 crore, and 5% above ₹1 crore, and both limbs are
            // NON-FILER-ONLY — the first proviso applies solely to "a recipient who has not filed the returns of
            // income for all of the three assessment years ...". The ordinary (filer) case is the main sentence:
            // 2% on cash exceeding ₹1 crore. Two further limbs a seeding pass must not lose, both from the same
            // page: the co-operative-society substitution reading "one crore rupees" as "three crore rupees", and
            // the exemptions in the last proviso (Government, banks/co-operative banks/post office, business
            // correspondents, and the rest).
            // ⚠️ AND NOTE WHAT §194N IS: a rate borne by BANKS, CO-OPERATIVE BANKS AND POST OFFICES on cash
            // withdrawals. It is almost certainly out of scope for this SMB clone's deductor set; seed it only on
            // a deliberate decision, not because it appears on the chart.
            // ───────────────────────────────────────────────────────────────────────────────────────────────────
        };
    }

    /// <summary>
    /// 🔴 <b>THE LONG-TAIL SECTIONS THAT WERE EVALUATED AND NOT SEEDED, each with the gate it failed (census row
    /// 6.35).</b> This is data, not a comment, for one reason: a comment cannot stop the next pass from seeding a
    /// rate off the chart, and <c>TdsLongTailSeedTests</c> can — it asserts that no section named here appears in
    /// <see cref="BuildTdsDefaults"/>.
    ///
    /// <para>🔴 <b>SIX ENTRIES WERE REMOVED ON 2026-09-08 AND THE SECTIONS SEEDED</b> — §192A, §194EE, §194G,
    /// §194K, §194LA and §194-O. Each removal is a claim, discharged in the seed row itself: the four §194-O /
    /// §194G / §194K / §194LA rejections were <b>OUR VINTAGE ERROR, NOT A REAL G2 DISAGREEMENT</b> (the earlier
    /// pass read archived texts at plain and low-numbered slugs; the Year-2025 text of all four agrees with
    /// [CHART-TDS]), and §192A / §194EE were blocked only by the missing inclusive boundary, which
    /// <see cref="NatureOfPayment.AggregateThresholdIsInclusive"/> now expresses.</para>
    ///
    /// <para><b>Removing an entry is the ONLY way to seed the section, and removing it is a claim that the gate
    /// has been cleared with a fresh reading of a primary source — not that the section looks fine.</b> The gates
    /// are stated in full in the block comments inside <see cref="BuildTdsDefaults"/>; in short: <b>G1</b> the bare
    /// Act must state the rate (otherwise the figure rests on the self-contradicting [CHART-TDS] alone, which is
    /// the unclosed §194A gap and must not be widened); <b>G2</b> the Act text — <b>at the slug whose "Year" field
    /// reads 2025</b>, never a plain or arbitrarily-numbered one — must agree with [CHART-TDS]; <b>G3</b> the
    /// section's threshold shape must be one the master can express.</para>
    ///
    /// <para>🔴 <b>G4 ("the deductor must plausibly be this product's user") IS WITHDRAWN AND MUST NOT BE
    /// RE-APPLIED.</b> It was ours, it was never the vendor's, and it was holding fully-sourced sections out of
    /// the product. Under R7 (ruling 14) the vendor's published documentation is the fidelity ground truth, and
    /// TallyPrime's own Nature-of-Payment helper list ships every section G4 rejected. The evidence and the URL
    /// are in the block comment inside <see cref="BuildTdsDefaults"/>. Two entries below still <i>mention</i> G4
    /// in a trailing clause; those clauses are dead and each is marked — <b>G1 alone is what holds those two
    /// sections back</b>, and clearing G1 is sufficient to seed them.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> LongTailSectionsNotSeeded { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["193"] = "G1 — §193 deducts 'at the rates in force'. The Act states no percentage, so a seeded 10% "
                    + "would rest on [CHART-TDS] alone, exactly as §194A's does. That gap is flagged, not a "
                    + "precedent.",
            ["194"] = "G1 — §194 (Dividends) deducts 'at the rates in force'; no percentage in the operative "
                    + "sentence. ⚠️ AND A SLUG TRAP WORTH RECORDING: https://www.incometaxindia.gov.in/w/section-194 "
                    + "serves section 194 of the CODE OF CRIMINAL PROCEDURE ('Police to enquire and report on "
                    + "suicide, etc'), not the Income-tax Act. The known plain-slug hazard was wrong YEAR; this "
                    + "one is the wrong STATUTE. Read 2026-09-08.",
            ["194B"] = "G1 — 'deduct income-tax thereon at the rates in force'. Its ₹10,000 limb is also "
                     + "per-single-transaction, which this engine can express, but the rate cannot be sourced.",
            ["194BB"] = "G1 — §194BB carries the same 'deduct income-tax thereon at the rates in force' shape as "
                      + "§194B, so the 30% on [CHART-TDS] has no bare-Act basis to cite. (A trailing 'G4 as well: "
                      + "the deductor is a bookmaker or a race club' stood here and is DEAD — G4 is withdrawn. G1 "
                      + "alone holds this section back; clear it and §194BB is seedable.)",
            ["194D"] = "G1 — 'rates in force'. [CHART-TDS] additionally states TWO different figures for it in "
                     + "its own two blocks (5 for a non-company payee, 10 for a company), which is a second "
                     + "reason not to copy it. (A trailing 'G4 also: the deductor is an insurer' stood here and is "
                     + "DEAD — G4 is withdrawn, and the vendor's own helper list ships an insurance-commission "
                     + "nature. G1 alone holds this section back.)",
            ["194N"] = "🔴 NOT G4 ANY MORE — G4 IS WITHDRAWN (see BuildTdsDefaults). §194N is held back on a "
                     + "SOURCE-SHAPE problem the engine cannot express: its rate is BANDED and NON-FILER-GATED — "
                     + "2% only on the band ₹20 lakh→₹1 crore and 5% above ₹1 crore, both limbs applying solely "
                     + "to a recipient who has not filed returns for three assessment years, with the ordinary "
                     + "filer case at 2% on cash exceeding ₹1 crore and a co-operative-society substitution "
                     + "reading '₹1 crore' as '₹3 crore'. NatureOfPayment carries ONE with-PAN rate and no band "
                     + "boundaries, so no single row can state this section. [CHART-TDS] also states the bands "
                     + "INCONSISTENTLY across its own three blocks — see the forward note inside "
                     + "BuildTdsDefaults, which quotes the statute that settles them.",
        };

    /// <summary>
    /// Builds the seeded predefined TCS Nature-of-Goods (§206C) set (fresh ids each call): scrap, timber (lease /
    /// other mode), tendu leaves, alcoholic liquor, minerals, 206C(1F) motor vehicle, and the legacy year-gated
    /// 206C(1H) sale of goods. FY 2025-26 rates + Form 27EQ collection codes; every base includes GST.
    /// <para>🔴 <b>T0-6 sourcing.</b> Every collection rate below AGREES with the Income-tax Department's own
    /// <b>[CHART-TCS]</b> table (see the class doc for the URL), and every no-PAN rate is the §206CC(1) computation
    /// applied to it: <b>[206CC]</b> "tax shall be collected at the higher of the following rates, namely: (i) at
    /// TWICE the rate specified in the relevant provision of this Act; or (ii) at the rate of FIVE per cent",
    /// subject to "Provided that the rate of tax collection at source under this section shall not exceed twenty
    /// per cent." Each row states its own arithmetic. The blog citations these figures used to carry are gone.</para>
    /// <para>🔴 <b>BUT NOTE THE ASYMMETRY WITH THE TDS SET, MEASURED 2026-08-20. THERE, THE CHART IS
    /// CORROBORATION FOR ALL BUT ONE FIGURE, BECAUSE EVERY RATE IS ALSO IN A CITED BARE-ACT SECTION. HERE IT IS
    /// NOT: NO §206C BARE-ACT PAGE IS CITED ANYWHERE IN THIS FILE, SO EVERY WITH-PAN RATE IN THIS BUILDER RESTS
    /// ON [CHART-TCS] ALONE</b> — and the no-PAN rates are computed FROM those rates by §206CC, so they inherit
    /// the same single point of failure. That is not a new regression; it follows from what the §206C(1H) row
    /// below already records, that "the Department's site served only pre-2020 archived texts of §206C at every
    /// slug tried". It is written here because the reader of the class doc's [CHART-TCS] entry needs to know
    /// which of the two charts actually carries weight.
    /// <para>The chart's own vintage is <b>UNSTATED</b>, which is a different flaw from [CHART-TDS]'s
    /// self-contradiction and arguably a quieter one: re-read 2026-08-20, it declares no assessment year, no tax
    /// year and no Finance Act anywhere on the page, and carries no "Year" metadata field. Its only dates are
    /// "Upload Date 30/04/2026" and "Last reviewed and updated on: 30-Jul-2026". The rates read off it that day —
    /// alcoholic liquor 1, tendu leaves 5, timber under a forest lease 2, timber by any other mode 2, scrap 1,
    /// minerals 1 — all still match the figures seeded below, so nothing is asserted to have moved. What cannot
    /// be said is which year they are the rates FOR. (Cosmetic, noted so a future reader is not confused by it:
    /// the Category-1 table prints "Timber obtained by any mode other than a forest lease 2" TWICE.)</para>
    /// <b>To close this properly, cite §206C(1) itself</b>, not a chart — the same numbered-slug technique that
    /// resolved §194H and §194-I should be tried again on §206C, since the site has since been re-published and
    /// the earlier attempt predates that.</para>
    /// </summary>
    public static IReadOnlyList<NatureOfGoods> BuildTcsDefaults()
    {
        NatureOfGoods G(string code, string name, int withPan, int withoutPan, Money? threshold = null,
            bool legacy = false) =>
            new(Guid.NewGuid(), code, name, withPan, withoutPan, code, threshold, baseIncludesGst: true,
                effectiveFrom: Fy2025, isPredefined: true, isLegacy: legacy,
                legacyCutoff: legacy ? LegacyGoodsCutoff : null);

        return new[]
        {
            // Scrap 6CE — AGREES. [CHART-TCS] "Scrap 1". No-PAN: §206CC higher of 2 x 1% = 2% or 5% => 5%.
            //   ⚠️ AN UNVERIFIED CLAIM WAS REMOVED FROM THIS LINE, NOT CARRIED FORWARD. It read "1% is correct for
            //   FY2025-26 (2% only from FY2026-27)". No primary source was found for a 2% scrap rate in any year;
            //   [CHART-TCS], read after FY 2025-26 had closed, still states 1%. The FY 2025-26 figure below is
            //   sourced and unchanged; the forward-looking half of that sentence was not, so it is not asserted.
            G("6CE", "Scrap", 100, 500),
            // Timber under a forest lease 6CB — AGREES. [CHART-TCS] "Timber or any other forest produce (not being
            //   tendu leaves) obtained under a forest lease 2". No-PAN: higher of 2 x 2% = 4% or 5% => 5% [206CC].
            G("6CB", "Timber obtained under forest lease", 200, 500),
            // Timber obtained other than under a forest lease 6CC — AGREES. [CHART-TCS] "Timber obtained by any mode
            //   other than a forest lease 2". No-PAN: higher of 2 x 2% = 4% or 5% => 5% [206CC].
            G("6CC", "Timber/forest produce (other than forest lease)", 200, 500),
            // Tendu leaves 6CI — AGREES. [CHART-TCS] "Tendu leaves 5". No-PAN: higher of 2 x 5% = 10% or 5% => 10%,
            //   which is under the §206CC 20% ceiling [206CC].
            G("6CI", "Tendu leaves", 500, 1000),
            // Alcoholic liquor for human consumption 6CA — AGREES. [CHART-TCS] "Alcoholic liquor for human
            //   consumption 1". No-PAN: higher of 2 x 1% = 2% or 5% => 5% [206CC].
            G("6CA", "Alcoholic liquor for human consumption", 100, 500),
            // Minerals, being coal or lignite or iron ore 6CJ — AGREES. [CHART-TCS] "Minerals, being coal or lignite
            //   or iron ore 1". No-PAN: higher of 2 x 1% = 2% or 5% => 5% [206CC].
            G("6CJ", "Minerals — coal / lignite / iron ore", 100, 500),
            // §206C(1F) Motor vehicle / notified goods 6CL — AGREES, rate and threshold. [CHART-TCS], Category-3:
            //   "Every person, being a seller, who receives any amount as consideration for sale of a motor vehicle or
            //   any other notified goods (effective from 01-01-2025) of the value exceeding Rs. 10,00,000, shall, at
            //   the time of receipt of such amount, collect from the buyer, a sum equal to 1% of the sale
            //   consideration as income-tax." No-PAN: higher of 2 x 1% = 2% or 5% => 5% [206CC].
            //   The ten notified goods (wristwatches, art, collectibles, yachts, sunglasses, handbags, footwear,
            //   sportswear, home theatre systems, racing/polo horses) ride the same 6CL row per Notification 36/2025,
            //   cited on [CHART-TCS]; they are not separately seeded.
            G("6CL", "Motor vehicle / notified luxury goods (206C(1F))", 100, 500, Money.FromRupees(10_00_000m)),
            // §206C(1H) Sale of goods 6CR — LEGACY, and the year-gate AGREES. [CHART-TCS], Category-6: "Every
            //   person, being a seller who receives any amount as consideration for sale of any goods, shall collect
            //   tax at the rate of 0.1% if the aggregate value of such sale in any previous year exceeds Rs. 50 lakh.
            //   Note: this provision is not applicable w.e.f. 01-04-2025." Hence LegacyGoodsCutoff below, and the
            //   0.1% / ₹50,00,000 figures.
            //   ⚠️ THE NO-PAN 1% ON THIS ROW IS THE ONE FIGURE IN THIS FILE STILL WITHOUT A PRIMARY CITATION. It is
            //   the §206C(1H) fourth-proviso special substitution (§206CC's "five per cent" read as "one per cent"),
            //   and the Department's site served only pre-2020 archived texts of §206C at every slug tried, so the
            //   proviso could not be quoted. It is left as shipped and flagged rather than re-asserted from memory.
            //   Its blast radius is nil while the gate holds: the row is non-selectable for dates on or after the
            //   cutoff, so no FY 2025-26 or later collection can reach it.
            G("6CR", "Sale of goods (206C(1H) — legacy)", 10, 100, Money.FromRupees(50_00_000m), legacy: true),
        };
    }
}
