using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// Which <b>rate limb</b> of the Kerala Flood Cess a supply falls in — the levy has exactly two, plus "no limb at
/// all", and the third is not a rounding of the other two but a genuine non-levy.
/// </summary>
public enum KfcRateLimb
{
    /// <summary>The supply is not in any Kerala Flood Cess schedule — <b>no cess at all</b>. This is NOT "0%
    /// cess on a leviable supply"; it is a supply the levy never reached (5% and 0.25% GST supplies, and every
    /// exempt/nil supply).</summary>
    NotLeviable = 0,

    /// <summary>The <b>1%</b> limb — Schedules II, III and IV of S.R.O. 360/2017 (12%, 18% and 28% GST).</summary>
    Standard = 1,

    /// <summary>The <b>0.25%</b> limb — the Fifth Schedule of S.R.O. 360/2017, "gold, diamond etc." (3% GST).</summary>
    Reduced = 2,
}

/// <summary>
/// The facts about one outward supply that decide whether the Kerala Flood Cess is <b>leviable</b> on it. Every
/// field maps to a numbered answer in the Kerala GST Department's own FAQ (see <see cref="KeralaFloodCess"/> for the
/// citation), so the predicate can be read against the source clause by clause.
/// </summary>
/// <param name="SupplyDate">The date of the supply — the levy is a dated window and this is what gates it.</param>
/// <param name="SupplierIsRegisteredInKerala">True iff the SUPPLIER holds a Kerala GST registration (home State 32).
/// The cess is a Kerala State levy collected by Kerala registrants; FAQ Q11/Q21.</param>
/// <param name="IsInterStateSupply">True iff the supply is inter-State. FAQ Q11 — the cess is intra-State only.</param>
/// <param name="RecipientIsRegisteredInKerala">True iff the RECIPIENT is a taxable person holding a GST registration
/// <b>in Kerala</b>. FAQ Q21: the exemption is available only to a Kerala registrant, so a party registered in
/// another State is NOT exempt.</param>
/// <param name="RecipientSupplyIsInFurtheranceOfBusiness">True iff the supply to that Kerala-registered recipient is
/// made <b>in furtherance of business</b>. FAQ Q12/Q18/Q19 — a registered recipient buying otherwise (the FAQ's own
/// example is a motor vehicle for own use) DOES bear the cess. 🔴 The book carries no field for this; see
/// <see cref="KeralaFloodCess"/>'s remarks on why it is a parameter and not a lookup.</param>
/// <param name="SupplyIsTaxable">True iff the supply bears GST at all. FAQ Q13 — an exempted supply bears no cess.</param>
/// <param name="SupplierIsCompositionDealer">True iff the supplier has opted for composition. FAQ Q14 — composition
/// taxpayers are exempt from the levy.</param>
public readonly record struct KfcSupply(
    DateOnly SupplyDate,
    bool SupplierIsRegisteredInKerala,
    bool IsInterStateSupply,
    bool RecipientIsRegisteredInKerala,
    bool RecipientSupplyIsInFurtheranceOfBusiness,
    bool SupplyIsTaxable,
    bool SupplierIsCompositionDealer);

/// <summary>
/// The <b>Kerala Flood Cess</b> engine (census row 6.26) — a <b>pure, deterministic</b>, framework-/DB-/clock-/
/// RNG-free calculator for a <b>State</b> levy that is <b>NO LONGER IN FORCE</b>. It exists so a book kept during
/// the levy can still be projected, reconciled and reprinted; a supply dated today computes ₹0 <b>by construction</b>,
/// and that is pinned by test, not asserted by comment.
///
/// <para>🔴 <b>THE LEVY LAPSED ON 31 JULY 2021, AND THE WINDOW IS THE FIRST PARAMETER OF EVERY ENTRY POINT.</b>
/// It is deliberately <b>not</b> a bare constant multiplied into a total somewhere: the recurring defect class in
/// this product is date-blind statutory data (a rate that keeps applying after the statute that carried it moved),
/// so here the date is required to obtain a rate at all and <see cref="RateBasisPointsOn"/> answers <b>0</b> outside
/// <c>[<see cref="Commencement"/>, <see cref="Cessation"/>]</c> with no way to bypass it.</para>
///
/// <h3>Sources — primary, read in full, quoted where a figure turns on the words</h3>
/// <list type="bullet">
///   <item><b>[KFC-FAQ]</b> Kerala GST Department, <i>FAQ on Kerala Flood Cess</i> (official) —
///     <c>https://keralataxes.gov.in/wp-content/uploads/2019/07/FINAL-FAQ-ON-FLOOD-CESS-29-07-2019.pdf</c>,
///     read 2026-09-06. Questions are cited below as "Q<i>n</i>".</item>
///   <item><b>[SRO-436]</b> Government of Kerala, <b>S.R.O. No. 436/2019</b> dated 29 June 2019, Kerala Gazette
///     Extraordinary No. 1446 of 01-07-2019 —
///     <c>https://keralataxes.gov.in/wp-content/uploads/2019/07/436-19.pdf</c>, read 2026-09-06.</item>
///   <item><b>[SRO-360]</b> Government of Kerala, <b>S.R.O. No. 360/2017</b> (the KGST rate schedules for goods) —
///     <c>https://keralataxes.gov.in/wp-content/uploads/2017/10/SRO.360.17_GSTRateSchedule.pdf</c>, read 2026-09-06.
///     This is what turns the FAQ's SCHEDULE names into GST RATES; see <see cref="LimbFor"/>.</item>
/// </list>
///
/// <h3>The statutory shape, each attribute against its clause</h3>
/// <list type="bullet">
///   <item><b>Charging provision:</b> §14 of the Kerala Finance Act, 2019 — [KFC-FAQ] Q3.</item>
///   <item><b>Rules:</b> the Kerala Flood Cess Rules, published vide S.R.O. No. 359/2019, G.O.(P) No. 80/2019/TD
///     dated 25-05-2019 — [KFC-FAQ] Q3.</item>
///   <item><b>Commencement:</b> [SRO-436] substitutes, in the earlier appointing notification (S.R.O. 358/2019),
///     "the words, letters, symbol and figures '1st day of July, 2019'" with "'1st day of August, 2019'", and its
///     explanatory note records that Government "decided to levy and collect the cess with effect from the 1st day
///     of August, 2019". [KFC-FAQ] Q2 states the same date and adds the duration: "Kerala Flood Cess is applicable
///     from the 1st August, 2019 onwards as per notification No. S.R.O. 436/2019 dated 29/06/2019 <b>for a period of
///     two years</b>." Q5 repeats it: "Kerala Flood Cess will be in force for a period two years from the date of
///     commencement."</item>
///   <item>🔴 <b>Cessation — DERIVED, and the derivation is stated so it can be checked.</b> 01-08-2019 plus two
///     years is <b>31-07-2021</b>. That is arithmetic on [KFC-FAQ] Q2/Q5, not a secondary claim, and it is the ONLY
///     inference in this file that is not a direct quotation. No extending notification was located on
///     <c>keralataxes.gov.in</c>. If one is ever produced, <see cref="Cessation"/> is the single line that moves.</item>
///   <item><b>Intra-State only:</b> [KFC-FAQ] Q11 — "Kerala Flood Cess is applicable only for intra-state supply."</item>
///   <item><b>The exemption is for Kerala-registered business buyers:</b> Q12 — "Supply of Goods or Services or both
///     made by a taxable person in the State to another taxable person having Goods and Service Tax Registration in
///     the State shall be leviable to Cess, <b>if the supply is made not in furtherance of business</b>"; Q19 — "If a
///     supply is made to an unregistered tax payer, Kerala Flood Cess is to be levied"; Q18 — a motor vehicle bought
///     for the buyer's own use IS liable, "Since the supply is not in furtherance of business"; Q21 — "Exemption is
///     eligible only for registered taxable person having GST registration in Kerala GST."</item>
///   <item><b>Exempt supplies:</b> Q13 — cess is not applicable to exempted goods or services.</item>
///   <item><b>Composition:</b> Q14 — "Composition tax payers are exempted from the levy of Kerala Flood Cess".</item>
///   <item><b>The base excludes CGST and SGST:</b> Q10 — "Kerala Flood Cess is to be calculated on the value of
///     supply. <b>The CGST and SGST collection shall not be included in the value of supply.</b>" The same answer
///     cites Rule 32A of the KGST Rules 2017, inserted by S.R.O. No. 434/2019 dated 28-06-2019, under which the value
///     "shall be deemed to be the value determined in terms of Section 15 of the Act, but shall not include the said
///     cess" — so the cess is not on its own base either.</item>
///   <item><b>Invoice presentation:</b> Q16/Q17 — collectable from customers "by showing separately in the invoices",
///     with SGST Rule 46(l) &amp; (m) applying, so the invoice states the cess rate and amount separately.</item>
///   <item><b>Registration:</b> Q6 — none; "GSTIN will be treated as Registration Number for Kerala Flood Cess."</item>
///   <item><b>Return and due date:</b> Q7 — "Due date for filing GSTR 3B shall be applicable for the Kerala Flood
///     Cess return", i.e. the 20th of the succeeding month. Q8 states what the return contains: "select the return
///     period and enter the details of <b>turnover of outward supply leviable under Kerala Flood Cess based on GST
///     tax rates</b>" — which is exactly the projection <c>KeralaFloodCessReturn</c> builds.</item>
///   <item><b>Interest:</b> Q20 — 18% on delayed payment.</item>
/// </list>
///
/// <h3>🔴 Two boundaries stated rather than faked</h3>
/// <list type="number">
///   <item><b>"In furtherance of business" is a PARAMETER, not a lookup, because the book has no such field.</b>
///     Whether a particular sale to a registered buyer was in furtherance of that buyer's business is a fact about
///     the transaction that no master or voucher in this application records. Inventing a default that silently
///     levied the cess on every B2B sale — or silently exempted every one — would move money either way. The
///     predicate therefore takes it explicitly, callers state which reading they are taking, and
///     <c>KeralaFloodCessReturn</c> takes the ordinary reading (a registered Kerala buyer is buying for its business,
///     the rule in Q12) and <b>says so on the screen</b> rather than in a comment. Persisting the exception needs a
///     column, i.e. a schema migration, which this work does not have and does not fake.</item>
///   <item><b>This is NOT GST Compensation Cess and must never be posted as it.</b> <see cref="GstCessRate"/> is
///     ring-fenced to the Output/Input Cess ledgers, which are Central heads reported in the cess column of
///     GSTR-1/3B. The Kerala Flood Cess is a State levy under a Kerala Act with its own separate return
///     ([KFC-FAQ] Q7/Q8). Routing it through the Compensation-Cess ledgers would put a Kerala levy into a Central
///     return — a misstated return, not a cosmetic slip. Nothing in this file touches a cess ledger.</item>
/// </list>
/// </summary>
public static class KeralaFloodCess
{
    /// <summary>The GST State code for Kerala — <c>"32"</c>; see <see cref="IndianState"/>.</summary>
    public const string KeralaStateCode = "32";

    /// <summary>The first day the levy was in force: <b>1 August 2019</b> ([SRO-436]; [KFC-FAQ] Q2). Inclusive.</summary>
    public static readonly DateOnly Commencement = new(2019, 8, 1);

    /// <summary>
    /// The last day the levy was in force: <b>31 July 2021</b>. Inclusive. <b>Derived</b> — two years from
    /// <see cref="Commencement"/> per [KFC-FAQ] Q2/Q5; see the class remarks for why this one figure is an inference
    /// and what would move it.
    /// </summary>
    public static readonly DateOnly Cessation = new(2021, 7, 31);

    /// <summary>The <b>1%</b> limb in basis points ([KFC-FAQ] Q5: "imposed @ 1% on the value of supply").</summary>
    public const int StandardRateBasisPoints = 100;

    /// <summary>The <b>0.25%</b> limb in basis points ([KFC-FAQ] Q5: "the Kerala Flood Cess is applicable at the
    /// rate of 0.25%").</summary>
    public const int ReducedRateBasisPoints = 25;

    /// <summary>
    /// True iff the levy was in force on <paramref name="supplyDate"/> — both bounds <b>inclusive</b>. This is the
    /// gate every rate goes through; there is no entry point that returns a rate without consulting it.
    /// </summary>
    public static bool IsInForceOn(DateOnly supplyDate) =>
        supplyDate >= Commencement && supplyDate <= Cessation;

    /// <summary>
    /// 🔴 <b>The schedule limb a supply taxed at <paramref name="gstRateBasisPoints"/> (the INTEGRATED rate, so 1800
    /// for an 18% supply whether it posted as IGST or as CGST+SGST) falls in — and this mapping is the one place the
    /// two primary sources have to be joined, so the join is written out rather than assumed.</b>
    ///
    /// <para>[KFC-FAQ] Q5 names SCHEDULES, not rates: the cess is "imposed @ 1% on the value of supply of goods or
    /// services or both coming under <b>Schedule II, III &amp; IV</b> of SRO.No.360/2017 Dt.30.06.2017. But in the
    /// case of goods coming under <b>Fifth Schedule</b> of SRO.No.360/2017 (gold, diamond etc.), the Kerala Flood
    /// Cess is applicable at the rate of 0.25%."</para>
    ///
    /// <para>[SRO-360] supplies the other half, in its own operative sentence, which lists the State-tax rate for
    /// each schedule verbatim: <c>"(i) 2.5 per cent in respect of goods specified in Schedule I; (ii) 6 per cent in
    /// respect of goods specified in Schedule II; (iii) 9 per cent in respect of goods specified in Schedule III;
    /// ... (v) 1.5 per cent in respect of goods specified in Schedule V, and; (vi) 0.125 per cent in respect of goods
    /// specified in Schedule VI"</c>, and its schedule headings read <c>SCHEDULE I – 2.5%</c>, <c>SCHEDULE II – 6%</c>,
    /// <c>SCHEDULE III – 9%</c>, <c>SCHEDULE IV – 14%</c>, <c>SCHEDULE V – 1.5%</c>, <c>SCHEDULE VI – 0.125%</c>.
    /// Those are <b>State</b> tax rates — half the integrated rate — so the schedules are the 5%, 12%, 18%, 28%, 3%
    /// and 0.25% GST rates in that order.</para>
    ///
    /// <para>Joining the two: <b>Schedules II, III, IV ⇒ 12%, 18%, 28% ⇒ 1% cess</b>; <b>Schedule V ⇒ 3% ⇒ 0.25%
    /// cess</b>.</para>
    ///
    /// <para>🔴 <b>AND WHAT IS NOT IN EITHER LIST IS NOT A ROUNDING — IT IS A NON-LEVY, AND IT IS THE FIGURE MOST
    /// EASILY GOT WRONG.</b> Schedule I (<b>5% GST</b>) and Schedule VI (<b>0.25% GST</b>) appear in NEITHER limb of
    /// Q5, so a 5% supply bears <b>no</b> Kerala Flood Cess at all. Charging the "obvious" 1% on it — the shape a
    /// reader gets by assuming the cess is a flat 1% on everything taxable — would over-collect on the single
    /// commonest rate slab in an Indian trading book. That is why this returns <see cref="KfcRateLimb.NotLeviable"/>
    /// by name rather than 0 basis points: a caller cannot mistake "the levy does not reach this supply" for
    /// "the levy reaches it at zero".</para>
    ///
    /// <para><b>Date-blind on purpose.</b> This answers only "which schedule", which is a property of the goods, not
    /// of the calendar. The window is applied by <see cref="RateBasisPointsOn"/>, which is the entry point every
    /// caller uses; nothing in this file multiplies a rate into a value without going through it.</para>
    /// </summary>
    public static KfcRateLimb LimbFor(int gstRateBasisPoints) => gstRateBasisPoints switch
    {
        1200 or 1800 or 2800 => KfcRateLimb.Standard,   // [SRO-360] Schedules II (6%), III (9%), IV (14%) State tax
        300 => KfcRateLimb.Reduced,                     // [SRO-360] Schedule V (1.5% State tax) — gold, diamond etc.
        _ => KfcRateLimb.NotLeviable,                   // Schedule I (2.5%), Schedule VI (0.125%), exempt, nil, 0%
    };

    /// <summary>The cess rate in basis points for a limb: 100 / 25 / 0.</summary>
    public static int RateBasisPointsOf(KfcRateLimb limb) => limb switch
    {
        KfcRateLimb.Standard => StandardRateBasisPoints,
        KfcRateLimb.Reduced => ReducedRateBasisPoints,
        _ => 0,
    };

    /// <summary>
    /// 🔴 <b>The Kerala Flood Cess rate in basis points for a supply taxed at
    /// <paramref name="gstRateBasisPoints"/> made on <paramref name="supplyDate"/> — the window FIRST, then the
    /// schedule.</b> Answers <b>0</b> for every date outside <c>[<see cref="Commencement"/>,
    /// <see cref="Cessation"/>]</c>, so a supply made today, or made on 31 July 2019, or on 1 August 2021, cannot
    /// acquire a rate however the schedules read.
    /// </summary>
    public static int RateBasisPointsOn(DateOnly supplyDate, int gstRateBasisPoints) =>
        IsInForceOn(supplyDate) ? RateBasisPointsOf(LimbFor(gstRateBasisPoints)) : 0;

    /// <summary>
    /// 🔴 <b>Whether the Kerala Flood Cess is LEVIABLE on <paramref name="supply"/> at all</b> — every conjunct is a
    /// numbered answer in [KFC-FAQ] and is named in <see cref="KfcSupply"/>. Being leviable does not by itself
    /// produce a rate: a leviable supply outside the schedules still bears nothing (see <see cref="LimbFor"/>).
    /// </summary>
    public static bool IsLeviable(KfcSupply supply) =>
        IsInForceOn(supply.SupplyDate)                       // Q2/Q5 — the dated window
        && supply.SupplierIsRegisteredInKerala               // Q11/Q21 — a Kerala State levy on a Kerala registrant
        && !supply.IsInterStateSupply                        // Q11 — intra-State only
        && supply.SupplyIsTaxable                            // Q13 — an exempted supply bears no cess
        && !supply.SupplierIsCompositionDealer               // Q14 — composition taxpayers are exempt
        && !(supply.RecipientIsRegisteredInKerala            // Q12/Q18/Q19/Q21 — the exemption is for a Kerala-
             && supply.RecipientSupplyIsInFurtheranceOfBusiness); //   registered buyer buying for its business

    /// <summary>
    /// 🔴 <b>The cess on <paramref name="taxableValue"/> BEFORE the paisa snap</b> — the figure a caller that
    /// aggregates several supplies into one slab total must accumulate, so the slab is rounded <b>once</b> rather
    /// than each supply being rounded and the roundings summed.
    ///
    /// <para>This mirrors <c>GstService.CessCharge.CessBeforeRounding</c> deliberately and for the same measured
    /// reason: rounding per line makes <c>Σ round(line)</c> the answer where the heads beside it use
    /// <c>round(Σ line)</c>, so re-deriving the same period from a different but value-identical line partition
    /// moves the figure. No statutory claim is made by this choice — [KFC-FAQ] states no rounding rule — it is a
    /// rounding <b>boundary</b> chosen to match the one the GST heads in this product already use.</para>
    ///
    /// <para><paramref name="taxableValue"/> is the <b>GST-exclusive</b> value of supply: [KFC-FAQ] Q10, "The CGST
    /// and SGST collection shall not be included in the value of supply." Callers must pass the taxable value, never
    /// an invoice total.</para>
    /// </summary>
    public static decimal CessBeforeRounding(Money taxableValue, DateOnly supplyDate, int gstRateBasisPoints) =>
        taxableValue.Amount * RateBasisPointsOn(supplyDate, gstRateBasisPoints) / 10_000m;

    /// <summary>
    /// The paisa-exact Kerala Flood Cess on a single supply of <paramref name="taxableValue"/> (GST-exclusive) taxed
    /// at <paramref name="gstRateBasisPoints"/> and made on <paramref name="supplyDate"/> — computed once and rounded
    /// once, away-from-zero to the paisa, the same snap <see cref="Money.RoundToPaisa"/> applies everywhere else.
    ///
    /// <para>The worked example in [KFC-FAQ] Q10 is the fixture: a value of supply of ₹100 at 12% GST bears CGST ₹6,
    /// SGST ₹6 and <b>Cess ₹1</b>, for a total sales value of ₹113 — i.e. the cess is 1% of ₹100 and not of ₹112.</para>
    /// </summary>
    public static Money On(Money taxableValue, DateOnly supplyDate, int gstRateBasisPoints) =>
        new Money(CessBeforeRounding(taxableValue, supplyDate, gstRateBasisPoints)).RoundToPaisa();
}
