namespace Apex.Ledger.Domain;

/// <summary>
/// 🔴 <b>THE CLASS-OF-GOODS GATE FOR CENTRAL EXCISE</b> (census area 15, row 15.8). Answers, for a stock item's
/// <see cref="NonGstGoodsClass"/>, whether <i>central</i> excise reaches goods of that class — and states the
/// position in words an operator can act on.
///
/// <para>🔴 <b>WHY THIS IS A SEPARATE TYPE FROM THE VAT GATE, AND WHY FOLDING THEM WOULD BE A REAL DEFECT.</b>
/// The two levies reach <b>different sets of goods</b>, and the difference is not a detail — it is the whole
/// shape of the area. <see cref="NonGstGoods.IsOutsideGst"/> answers <i>"may State VAT / CST apply?"</i> and is
/// true for alcoholic liquor and the five petroleum products. This type answers <i>"does central excise
/// apply?"</i> and is true for the five petroleum products <b>and tobacco</b> — which is <b>inside GST at the
/// same time</b> — and <b>false for alcoholic liquor</b>, which the Union's excise entry does not list at all.
/// An implementation that assumed one set would be wrong for two of the eight classes in opposite
/// directions.</para>
///
/// <para><b>Statutory basis, quoted from the official source rather than asserted (R7).</b> The Constitution
/// (One Hundred and First Amendment) Act, 2016, s.17(a)(i) substituted Union List entry 84, which now reads
/// verbatim: <i>"84. Duties of excise on the following goods manufactured or produced in India, namely:—
/// (a) petroleum crude; (b) high speed diesel; (c) motor spirit (commonly known as petrol); (d) natural gas;
/// (e) aviation turbine fuel; and (f) tobacco and tobacco products."</i>
/// (<c>taxinformation.cbic.gov.in</c>, Constitution (One Hundred And First Amendment) Act, 2016, s.17.)
/// <b>That list is CLOSED and it is the whole basis of this type</b>: everything not in it lost central excise
/// when GST arrived on 01-Jul-2017, and <b>alcoholic liquor for human consumption is not in it</b> — which is
/// precisely why the VAT gate and this gate disagree about liquor.</para>
///
/// <para>⚠️ <b>WHAT THIS TYPE DOES NOT DO, STATED SO IT IS NOT MISTAKEN FOR EXCISE SUPPORT.</b> It classifies
/// and it explains. It computes no duty, posts no entry, and carries no rate — <b>no excise rate is asserted
/// anywhere in this build</b>, because a duty rate is exactly the kind of figure this project has already had
/// to strip out of shipped code when the citation did not hold up. Neither half of census row 15.8 — the
/// excise invoice format at the voucher, and Excise for Dealers (RG 23D / Form 2) — ships here: both require
/// storage this slice had no budget for (an excise registration and ECC number on the company, and per-line
/// duty on the purchase, without which a statutory register would have seven of its twelve columns
/// empty).</para>
/// </summary>
public static class ExciseApplicability
{
    /// <summary>
    /// <b>Does central excise reach goods of this class?</b> True for the five petroleum products and for
    /// tobacco; false for everything else, <b>alcoholic liquor included</b>.
    ///
    /// <para>🔴 <b>Deliberately a one-line delegation to <see cref="NonGstGoods.AttractsCentralExcise"/> rather
    /// than a second <c>switch</c> over the same enum.</b> Two independently-written switches over one enum is
    /// how the excise set and the VAT set get quietly conflated: a member added later gets a case in one and
    /// not the other, both compile, and nothing fails. The predicate is published beside the enum it
    /// interprets; this type consumes it and never re-derives it.
    /// <c>CentralExciseApplicabilityTests.The_gate_never_disagrees_with_the_published_predicate</c> holds the
    /// delegation over <b>every</b> member, so re-deriving it here would fail rather than drift.</para>
    /// </summary>
    public static bool ReachesGoods(NonGstGoodsClass goodsClass) =>
        NonGstGoods.AttractsCentralExcise(goodsClass);

    /// <summary>
    /// <b>The excise position of goods of this class</b>, in one sentence an operator can act on — for every
    /// class, whether excise reaches it or not.
    ///
    /// <para>🔴 <b>A STATEMENT, NOT A GUARD, AND THAT IS THE HONEST SHAPE HERE.</b> The VAT gate's counterpart
    /// (<see cref="NonGstGoods.VatRefusalReason"/>) returns a refusal because there is a VAT rate setter for it
    /// to refuse. <b>There is no excise setter in this build</b>, so a refusal API would be a guard that can
    /// never refuse anything — the dead-guard shape this project has filed defects for. What an operator can
    /// actually use today is being TOLD where their goods stand, so that is what this returns.</para>
    ///
    /// <para>🔴 <b>The two sentences that matter most are the two that surprise people</b>, and they are
    /// written out rather than folded into a default: tobacco bears GST <i>and</i> central excise at once, and
    /// alcoholic liquor — outside GST, and the one class an operator is most likely to assume is excisable —
    /// is <b>not</b> reached by central excise.</para>
    /// </summary>
    public static string PositionStatement(NonGstGoodsClass goodsClass) => goodsClass switch
    {
        NonGstGoodsClass.Tobacco =>
            "Central excise applies to these goods AS WELL AS GST. Tobacco is inside GST and is also named in "
            + "the Union's excise entry — Constitution, Seventh Schedule, List I entry 84(f), \"tobacco and "
            + "tobacco products\". Both levies run at once.",

        NonGstGoodsClass.PetroleumCrude =>
            "Central excise applies to these goods. Petroleum crude is Union List entry 84(a), and it is "
            + "outside GST for now under CGST Act s.9(2).",

        NonGstGoodsClass.HighSpeedDiesel =>
            "Central excise applies to these goods. High speed diesel is Union List entry 84(b), and it is "
            + "outside GST for now under CGST Act s.9(2).",

        NonGstGoodsClass.MotorSpirit =>
            "Central excise applies to these goods. Motor spirit (petrol) is Union List entry 84(c), and it is "
            + "outside GST for now under CGST Act s.9(2).",

        NonGstGoodsClass.NaturalGas =>
            "Central excise applies to these goods. Natural gas is Union List entry 84(d), and it is outside "
            + "GST for now under CGST Act s.9(2).",

        NonGstGoodsClass.AviationTurbineFuel =>
            "Central excise applies to these goods. Aviation turbine fuel is Union List entry 84(e), and it is "
            + "outside GST for now under CGST Act s.9(2).",

        NonGstGoodsClass.AlcoholicLiquorForHumanConsumption =>
            "Central excise does NOT apply to these goods, even though they are outside GST. Union List entry "
            + "84 lists only the five petroleum products and tobacco, and alcoholic liquor is not among them — "
            + "excise on it is a State subject, not a central one.",

        _ =>
            "Central excise does NOT apply to these goods. It was subsumed by GST on 01-Jul-2017 for "
            + "everything outside Union List entry 84 — the five petroleum products and tobacco.",
    };

    /// <summary>
    /// A short badge for the same fact, for a screen that has room for a label but not a sentence:
    /// <c>"Excise: applies"</c> / <c>"Excise: does not apply"</c>. Reads
    /// <see cref="ReachesGoods"/> so it can never disagree with <see cref="PositionStatement"/>.
    /// </summary>
    public static string PositionBadge(NonGstGoodsClass goodsClass) =>
        ReachesGoods(goodsClass) ? "Excise: applies" : "Excise: does not apply";
}
