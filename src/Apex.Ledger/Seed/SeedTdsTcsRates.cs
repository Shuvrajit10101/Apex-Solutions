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
///   <item><b>[CHART-TDS]</b> Income-tax Department, "TDS Rates", <i>[For Assessment year 2026-27]</i> —
///     <c>https://www.incometaxindia.gov.in/w/tds-rates-1</c></item>
///   <item><b>[CHART-TCS]</b> Income-tax Department, "TCS Rates" —
///     <c>https://www.incometaxindia.gov.in/w/tcs-rates</c></item>
///   <item><b>[194A]</b> <c>https://www.incometaxindia.gov.in/w/section-194a</c> ·
///     <b>[194C]</b> <c>https://www.incometaxindia.gov.in/w/section-194c</c> ·
///     <b>[194H]</b> <c>https://www.incometaxindia.gov.in/w/section-194h-34</c> ·
///     <b>[194I]</b> <c>https://www.incometaxindia.gov.in/w/section-194-i-19</c> ·
///     <b>[194J]</b> <c>https://www.incometaxindia.gov.in/w/section-194j-30</c> ·
///     <b>[194Q]</b> <c>https://www.incometaxindia.gov.in/w/section-194q-5</c> ·
///     <b>[206AA]</b> <c>https://www.incometaxindia.gov.in/w/section-206aa-16</c> ·
///     <b>[206CC]</b> <c>https://www.incometaxindia.gov.in/w/section-206cc-8</c>
///     — each the bare Act text for the version that governs FY 2025-26.</item>
/// </list>
/// <b>A trap in those URLs, recorded because it cost time:</b> the Department's site serves ARCHIVED versions of a
/// section under both the plain slug and the numbered ones, and which slug holds the current text is not
/// predictable — the plain <c>/w/section-194i</c> serves the <b>2009</b> text with its long-repealed ₹1,20,000
/// threshold, and the plain <c>/w/section-194h</c> serves the version <b>omitted in 1999</b>. Each page states the
/// year of the text it is showing; read that field before quoting the page.</para>
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
    /// 194I(b), 194J(a), 194J(b), 194Q — the Phase-7 approved set, FY 2025-26 rates/thresholds/FVU codes.
    /// </summary>
    public static IReadOnlyList<NatureOfPayment> BuildTdsDefaults()
    {
        Money? R(decimal rupees) => Money.FromRupees(rupees);
        NatureOfPayment N(string section, string name, int withPan, int withoutPan, string fvu,
            Money? single = null, Money? cumulative = null) =>
            new(Guid.NewGuid(), section, name, withPan, withoutPan, fvu, single, cumulative, Fy2025, isPredefined: true);

        return new[]
        {
            // §194A Interest other than interest on securities — AGREES with the statute.
            //   Rate 10% [CHART-TDS, "Section 194A: Income by way of interest other than 'Interest on securities' 10"];
            //   no-PAN 20% [206AA(1)(iii)]. Threshold: §194A(3)(i)(d) [194A] — no deduction where the FY aggregate
            //   "does not exceed ... ten thousand rupees IN ANY OTHER CASE". That is the GENERIC (non-bank) payer,
            //   which is this SMB clone's default deductor.
            //   The sibling limbs in the same sub-clause are DELIBERATELY NOT MODELLED and are a payer-type-aware
            //   refinement, not a defect in this row: ₹50,000 where the payer is a banking company, a co-operative
            //   society carrying on banking, or a post-office deposit scheme [194A(3)(i)(a)-(c)], and ₹1,00,000 in
            //   place of that ₹50,000 where the payee is a senior citizen [194A(3), third proviso].
            N("194A", "Interest other than interest on securities", 1000, 2000, "94A",
                cumulative: R(10_000m)),
            // §194C Payments to contractors — AGREES with the statute, and the missing second rate is now BUILT.
            //   §194C(1) [194C]: "deduct an amount equal to — (i) ONE PER CENT where the payment is being made or
            //   credit is being given to an individual or a Hindu undivided family; (ii) TWO PER CENT where the
            //   payment is being made or credit is being given to a person other than an individual or a Hindu
            //   undivided family". [CHART-TDS] states the same split: "a) HUF/Individuals 1 - b) Others 2".
            //   Thresholds §194C(5) [194C]: no deduction where the sum "does not exceed thirty thousand rupees";
            //   proviso: liable where the FY aggregate "exceeds one lakh rupees". No-PAN 20% [206AA(1)(iii)].
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
            N("194H", "Commission or brokerage", 200, 2000, "94H",
                cumulative: R(20_000m)),
            // §194I(a) Rent — plant/machinery/equipment. RATE AGREES; the THRESHOLD IS A PER-MONTH LIMB — SEE BELOW.
            //   §194-I(a) [194I]: "TWO per cent for the use of any machinery or plant or equipment".
            //   [CHART-TDS]: "Section 194-I: Rent a) Plant & Machinery 2". No-PAN 20% [206AA(1)(iii)].
            N("194I(a)", "Rent — plant/machinery/equipment", 200, 2000, "4IA"),
            // §194I(b) Rent — land/building/furniture/fittings. RATE AGREES; PER-MONTH THRESHOLD — SEE BELOW.
            //   §194-I(b) [194I]: "TEN per cent for the use of any land or building (including factory building) or
            //   land appurtenant to a building (including factory building) or furniture or fittings".
            //   [CHART-TDS]: "b) Land or building or furniture or fitting 10". No-PAN 20% [206AA(1)(iii)].
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
            N("194I(b)", "Rent — land/building/furniture/fittings", 1000, 2000, "4IB"),
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
            //   "Note: TDS is deductible on sum exceeding Rs. 50 lakhs". The excess-only carve is T0-1; see
            //   NatureOfPayment.ChargesOnlyExcessOverCumulativeThreshold.
            //   No-PAN 5%, NOT 20% — §206AA, second proviso [206AA]: "where the tax is required to be deducted under
            //   section 194Q, the provisions of clause (iii) shall apply as if for the words 'twenty per cent', the
            //   words 'FIVE PER CENT' had been substituted".
            N("194Q", "Purchase of goods", 10, 500, "94Q",
                cumulative: R(50_00_000m)),
        };
    }

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
