using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the config-driven TDS <see cref="NatureOfPayment"/> and TCS <see cref="NatureOfGoods"/> masters for
/// <b>FY 2025-26 (AY 2026-27)</b> (Phase 7 slice 1; mirrors <see cref="SeedGstRates"/>). Rates/thresholds are
/// A14-verified against official sources (indiapost / cleartax / disytax / Protean NSDL Form 26Q &amp; 27EQ
/// specs). Every figure is <b>editable data</b>, so a future Finance-Act change is a data edit, not a code change.
/// <para>
/// The seed reflects the Phase-7 approved decisions: §194I and §194J are <b>bifurcated</b> per Form-26Q section
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
            // §194A Interest (non-securities): 10% / 20% no-PAN; cumulative ₹50,000 (bank/co-op/PO threshold).
            N("194A", "Interest other than interest on securities", 1000, 2000, "94A",
                cumulative: R(50_000m)),
            // §194C Contractors: 1% (Ind/HUF base rate) / 20% no-PAN; single ₹30,000, cumulative ₹1,00,000.
            //   (The 2% "other than Ind/HUF" branch is applied at compute by deductee type — Phase 7 slice 2.)
            N("194C", "Payment to contractors/sub-contractors", 100, 2000, "94C",
                single: R(30_000m), cumulative: R(1_00_000m)),
            // §194H Commission/brokerage: 2% (w.e.f 01-Oct-2024) / 20% no-PAN; cumulative ₹20,000 (FA2025).
            N("194H", "Commission or brokerage", 200, 2000, "94H",
                cumulative: R(20_000m)),
            // §194I(a) Rent — plant/machinery/equipment: 2% / 20% no-PAN; cumulative ₹6,00,000/FY (FA2025).
            N("194I(a)", "Rent — plant/machinery/equipment", 200, 2000, "4IA",
                cumulative: R(6_00_000m)),
            // §194I(b) Rent — land/building/furniture/fittings: 10% / 20% no-PAN; cumulative ₹6,00,000/FY.
            N("194I(b)", "Rent — land/building/furniture/fittings", 1000, 2000, "4IB",
                cumulative: R(6_00_000m)),
            // §194J(a) Technical services / call-centre / certain royalty: 2% / 20% no-PAN; cumulative ₹50,000.
            N("194J(a)", "Fees for technical services / call-centre / certain royalty", 200, 2000, "94J-A",
                cumulative: R(50_000m)),
            // §194J(b) Professional services / royalty / non-compete: 10% / 20% no-PAN; cumulative ₹50,000.
            N("194J(b)", "Fees for professional services / royalty / non-compete", 1000, 2000, "94J-B",
                cumulative: R(50_000m)),
            // §194Q Purchase of goods: 0.1% on value over ₹50,00,000/FY; no-PAN = 5% (§206AA 2nd-proviso cap, NOT 20%).
            N("194Q", "Purchase of goods", 10, 500, "94Q",
                cumulative: R(50_00_000m)),
        };
    }

    /// <summary>
    /// Builds the seeded predefined TCS Nature-of-Goods (§206C) set (fresh ids each call): scrap, timber (lease /
    /// other mode), tendu leaves, alcoholic liquor, minerals, 206C(1F) motor vehicle, and the legacy year-gated
    /// 206C(1H) sale of goods. FY 2025-26 rates + Form 27EQ collection codes; every base includes GST.
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
            // Scrap 6CE: 1% / 5% no-PAN (§206CC higher of 2%/5%). 1% is correct for FY2025-26 (2% only from FY2026-27).
            G("6CE", "Scrap", 100, 500),
            // Timber obtained under a forest lease 6CB: 2% (reduced from 2.5%) / 5% no-PAN.
            G("6CB", "Timber obtained under forest lease", 200, 500),
            // Timber/forest produce obtained by any mode other than a forest lease 6CC: 2% / 5% no-PAN.
            G("6CC", "Timber/forest produce (other than forest lease)", 200, 500),
            // Tendu leaves 6CI: 5% / 10% no-PAN (§206CC higher of 2×5%=10% or 5%).
            G("6CI", "Tendu leaves", 500, 1000),
            // Alcoholic liquor for human consumption 6CA: 1% / 5% no-PAN.
            G("6CA", "Alcoholic liquor for human consumption", 100, 500),
            // Minerals — coal/lignite/iron ore 6CJ: 1% / 5% no-PAN.
            G("6CJ", "Minerals — coal / lignite / iron ore", 100, 500),
            // §206C(1F) Motor vehicle / notified luxury goods 6CL: 1% / 5% no-PAN; threshold value > ₹10,00,000.
            G("6CL", "Motor vehicle / notified luxury goods (206C(1F))", 100, 500, Money.FromRupees(10_00_000m)),
            // §206C(1H) Sale of goods 6CR: 0.1% on receipts over ₹50,00,000/FY; no-PAN = 1% (§206CC special cap).
            //   LEGACY — non-operative w.e.f 01-Apr-2025 (FA2025): default OFF/non-selectable for dates ≥ cutoff.
            G("6CR", "Sale of goods (206C(1H) — legacy)", 10, 100, Money.FromRupees(50_00_000m), legacy: true),
        };
    }
}
