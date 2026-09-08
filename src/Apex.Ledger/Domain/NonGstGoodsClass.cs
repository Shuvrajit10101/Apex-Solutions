namespace Apex.Ledger.Domain;

/// <summary>
/// 🔴 <b>THE GATE THE WHOLE PRE-GST TAX FAMILY HANGS ON</b> (census area 15; schema v59). A stock item's class of
/// goods for the purpose of deciding whether the <i>pre-GST</i> levies — State VAT and Central Sales Tax — can
/// lawfully apply to it at all.
///
/// <para>🔴 <b>WHY THIS TYPE EXISTS AND WHY NOTHING IN AREA 15 MAY BE REACHED WITHOUT IT.</b> VAT and CST were
/// subsumed by GST for ordinary goods on 01-Jul-2017. They survive on a NARROW set of goods that GST never
/// absorbed, and on nothing else. A VAT rate field offered on an ordinary GST item is not a harmless extra
/// field — it invites an operator to compute a tax that was abolished for their trade, and then to file it. So
/// applicability is not a free-standing "VAT applicable" tick an operator can set on anything: it is a
/// CONSEQUENCE of what the goods are, which is what this enum records. <see cref="None"/> — the default, and
/// what every item in every existing book is — means ordinary GST goods, and for those the VAT block is refused
/// rather than merely hidden.</para>
///
/// <para><b>Statutory basis, quoted from the official source rather than asserted.</b>
/// <list type="bullet">
/// <item><b>Alcoholic liquor for human consumption</b> is outside GST <i>permanently</i> and by the Constitution
/// itself — Art. 366(12A) defines GST as a tax on supply "<i>except taxes on the supply of the alcoholic liquor
/// for human consumption</i>". CGST Act 2017 s.9(1) carries the same carve-out verbatim: a tax "<i>on all
/// intra-State supplies of goods or services or both, <b>except on the supply of alcoholic liquor for human
/// consumption</b></i>" (cbic-gst.gov.in, <c>CGST-Act-Updated-31082021.pdf</c>, s.9(1)).</item>
/// <item><b>The five petroleum products</b> are outside GST <i>for now</i>, at the GST Council's discretion —
/// CGST Act 2017 s.9(2), verbatim: "<i>The central tax on the supply of <b>petroleum crude, high speed diesel,
/// motor spirit (commonly known as petrol), natural gas and aviation turbine fuel</b> shall be levied with
/// effect from such date as may be notified by the Government on the recommendations of the Council.</i>"
/// (same source). The five members below ARE that sentence's list, in its order.</item>
/// <item><b>Tobacco</b> is the odd one out and is deliberately modelled as such — see
/// <see cref="Tobacco"/>.</item>
/// </list></para>
///
/// <para>🔴 <b>THE ENUM IS NOT A LIST OF "TAX-FREE" GOODS AND MUST NOT BE READ AS ONE.</b> Two different
/// questions are answered by two different predicates, and folding them would be a real defect:
/// <see cref="NonGstGoods.IsOutsideGst"/> answers "may VAT/CST apply?" and is true for liquor and the five
/// petroleum products only; <see cref="NonGstGoods.AttractsCentralExcise"/> answers "does central excise
/// apply?" and is ALSO true for tobacco, which is inside GST at the same time.</para>
///
/// <para>Stored as the ordinal in <c>stock_items.non_gst_goods_class</c>, NULLable, NULL ⇒ <see cref="None"/>,
/// so every pre-v59 item is byte-identical (ER-13).</para>
/// </summary>
public enum NonGstGoodsClass
{
    /// <summary>
    /// Ordinary goods, inside GST. <b>The default, and what every item in every existing book is.</b> VAT and
    /// CST do not apply and this product refuses to record a VAT rate against such an item — see
    /// <see cref="NonGstGoods.IsOutsideGst"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// Alcoholic liquor for human consumption. Outside GST by Art. 366(12A) of the Constitution and by CGST Act
    /// s.9(1), <b>permanently</b> — not at the Council's discretion, unlike the five below. State VAT and CST
    /// are the live levies.
    /// </summary>
    AlcoholicLiquorForHumanConsumption = 1,

    /// <summary>Petroleum crude — CGST Act s.9(2), first of the five.</summary>
    PetroleumCrude = 2,

    /// <summary>High speed diesel — CGST Act s.9(2), second of the five.</summary>
    HighSpeedDiesel = 3,

    /// <summary>Motor spirit (commonly known as petrol) — CGST Act s.9(2), third of the five.</summary>
    MotorSpirit = 4,

    /// <summary>Natural gas — CGST Act s.9(2), fourth of the five.</summary>
    NaturalGas = 5,

    /// <summary>Aviation turbine fuel — CGST Act s.9(2), fifth of the five.</summary>
    AviationTurbineFuel = 6,

    /// <summary>
    /// 🔴 <b>Tobacco — INSIDE GST, and therefore NOT a VAT/CST good.</b> Central excise under the Central Excise
    /// Act's Fourth Schedule did not go away when GST arrived; both levies apply to tobacco at once. This member
    /// exists so an operator can classify a tobacco item HONESTLY and be told plainly that the VAT block is
    /// refused (GST applies to it) while central excise would apply — rather than being left to conclude from a
    /// missing option that tobacco is an ordinary good.
    ///
    /// <para>⚠️ <b>Nothing in this build acts on it beyond that refusal.</b> Census row 15.8 (Excise) is NOT
    /// built, so there is no excise register, no RG 23D and no Form 2 for this member to feed. It is a truthful
    /// classification and an honest refusal, not a half-built excise feature.</para>
    /// </summary>
    Tobacco = 7,
}

/// <summary>
/// The two questions a <see cref="NonGstGoodsClass"/> answers, and the operator-facing captions for it. Kept
/// beside the enum so the VAT masters, the VAT Computation report and the UI all read the SAME predicate and
/// cannot drift into disagreeing about which goods a levy reaches.
/// </summary>
public static class NonGstGoods
{
    /// <summary>
    /// <b>May State VAT / CST apply to goods of this class?</b> True for alcoholic liquor for human consumption
    /// (Constitution Art. 366(12A); CGST s.9(1)) and for the five petroleum products (CGST s.9(2)) — and for
    /// nothing else.
    ///
    /// <para>🔴 <b><see cref="NonGstGoodsClass.Tobacco"/> is FALSE here on purpose.</b> Tobacco is inside GST;
    /// only central excise runs alongside. Returning true for it would let an operator raise a VAT invoice on
    /// goods GST already taxes.</para>
    /// </summary>
    public static bool IsOutsideGst(NonGstGoodsClass goodsClass) => goodsClass switch
    {
        NonGstGoodsClass.AlcoholicLiquorForHumanConsumption => true,
        NonGstGoodsClass.PetroleumCrude => true,
        NonGstGoodsClass.HighSpeedDiesel => true,
        NonGstGoodsClass.MotorSpirit => true,
        NonGstGoodsClass.NaturalGas => true,
        NonGstGoodsClass.AviationTurbineFuel => true,
        _ => false,
    };

    /// <summary>
    /// <b>Does central excise reach goods of this class?</b> True for the five petroleum products AND for
    /// tobacco (Central Excise Act, Fourth Schedule). Deliberately a DIFFERENT set from
    /// <see cref="IsOutsideGst"/>: tobacco is excisable and inside GST, alcoholic liquor is outside GST and is
    /// a State excise subject rather than a central one.
    ///
    /// <para>⚠️ <b>Nothing in this build consumes this predicate yet</b> — census row 15.8 (Excise) is not
    /// built. It is published here, next to the class it interprets, so that the excise slice cannot re-derive
    /// a DIFFERENT set from the same enum; the alternative was leaving the distinction implicit in a comment,
    /// which is how the two sets get conflated.</para>
    /// </summary>
    public static bool AttractsCentralExcise(NonGstGoodsClass goodsClass) => goodsClass switch
    {
        NonGstGoodsClass.PetroleumCrude => true,
        NonGstGoodsClass.HighSpeedDiesel => true,
        NonGstGoodsClass.MotorSpirit => true,
        NonGstGoodsClass.NaturalGas => true,
        NonGstGoodsClass.AviationTurbineFuel => true,
        NonGstGoodsClass.Tobacco => true,
        _ => false,
    };

    /// <summary>
    /// The operator-facing caption for a class. The six statutory members are named in the WORDS OF THE STATUTE
    /// (CGST s.9(1)/s.9(2)) rather than in trade slang, so an operator can match the caption to the Act.
    /// </summary>
    public static string Caption(NonGstGoodsClass goodsClass) => goodsClass switch
    {
        NonGstGoodsClass.None => "Ordinary goods (inside GST)",
        NonGstGoodsClass.AlcoholicLiquorForHumanConsumption => "Alcoholic liquor for human consumption",
        NonGstGoodsClass.PetroleumCrude => "Petroleum crude",
        NonGstGoodsClass.HighSpeedDiesel => "High speed diesel",
        NonGstGoodsClass.MotorSpirit => "Motor spirit (petrol)",
        NonGstGoodsClass.NaturalGas => "Natural gas",
        NonGstGoodsClass.AviationTurbineFuel => "Aviation turbine fuel",
        NonGstGoodsClass.Tobacco => "Tobacco (inside GST; central excise also applies)",
        _ => goodsClass.ToString(),
    };

    /// <summary>
    /// Why a VAT rate cannot be recorded against goods of this class, in words an operator can act on — or
    /// <c>null</c> when it can. The single place that sentence is written, so the master validator, the service
    /// and the screen all say the same thing.
    /// </summary>
    public static string? VatRefusalReason(NonGstGoodsClass goodsClass) => goodsClass switch
    {
        _ when IsOutsideGst(goodsClass) => null,
        NonGstGoodsClass.Tobacco =>
            "Tobacco is inside GST, so State VAT does not apply to it. Central excise applies as well as GST, "
            + "but excise is not in this build.",
        _ =>
            "State VAT and CST were subsumed by GST for ordinary goods. Set the item's class of goods to "
            + "alcoholic liquor for human consumption or to one of the five petroleum products first.",
    };

    /// <summary>Every class, in statutory order — what a picker binds to.</summary>
    public static IReadOnlyList<NonGstGoodsClass> All { get; } = new[]
    {
        NonGstGoodsClass.None,
        NonGstGoodsClass.AlcoholicLiquorForHumanConsumption,
        NonGstGoodsClass.PetroleumCrude,
        NonGstGoodsClass.HighSpeedDiesel,
        NonGstGoodsClass.MotorSpirit,
        NonGstGoodsClass.NaturalGas,
        NonGstGoodsClass.AviationTurbineFuel,
        NonGstGoodsClass.Tobacco,
    };
}
