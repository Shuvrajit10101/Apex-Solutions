using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the default <b>notified reverse-charge categories</b> (Phase 9 slice 2; RQ-3/RQ-7). These are the §9(3)/§5(3)
/// notified supplies (Notn 13/2017-CT(R) &amp; 10/2017-IGST(R): GTA, legal, arbitral, sponsorship, director, security,
/// motor-vehicle renting, copyright; plus the Notn 09/2024 renting of commercial immovable property) and the surviving
/// §9(4) promoter rows (Notn 7/2019-CT(R): cement-from-unregistered, the &lt;80%-input shortfall, capital-goods-from-
/// unregistered). They are ordinary editable data (each a <see cref="RcmCategory"/>), so a future council change is a
/// data edit, not a code change. Mirrors the <c>SeedGstRates</c> reference-data pattern.
/// <para>
/// These are the <b>ADVANCED-GST defaults</b> — <b>not</b> seeded by <c>GstService.EnableGst</c> (which stays
/// byte-identical, ER-13). They are applied only via the explicit advanced-GST opt-in
/// (<c>GstService.SeedAdvancedGst</c>) / an import. A14 re-verifies every notification number, rate and effective date
/// against CBIC (R7). The blanket §9(4) is <b>rescinded</b> (2019) — only the promoter rows survive, so a company with
/// no promoter profile leaves §9(4) OFF by default.
/// </para>
/// </summary>
public static class SeedRcmCategories
{
    private static readonly DateOnly Notn2017 = new(2017, 7, 1);   // 13/2017-CT(R) & 10/2017-IGST(R) rollout
    private static readonly DateOnly Notn7Of2019 = new(2019, 4, 1); // 7/2019-CT(R) promoter §9(4)
    private static readonly DateOnly Notn9Of2024 = new(2024, 10, 10); // 09/2024-CT(R) renting of commercial property

    /// <summary>
    /// The seeded default reverse-charge categories (fresh ids each call, mirror
    /// <see cref="SeedGstRates.BuildDefaultRateHistory"/>). Goods categories (cement, HSN 2523) leave the rate as a
    /// fallback and resolve the <b>dated</b> HSN rate through the S1 rate history (28% ≤ 21-Sep-2025, 18% from
    /// 22-Sep-2025); service categories carry their rate here.
    /// </summary>
    public static IReadOnlyList<RcmCategory> BuildDefaults()
    {
        RcmCategory C(string notn, RcmStream stream, string nature, GstSupplyType supplyType, string? hsn, int bp,
            RcmParty supplier, RcmParty recipient, DateOnly from, DateOnly? to, string label) =>
            new(Guid.NewGuid(), notn, stream, nature, supplyType, hsn, bp, supplier, recipient, from, to, label,
                isPredefined: true);

        return new List<RcmCategory>
        {
            // ---- §9(3) / §5(3) notified supplies (Notn 13/2017-CT(R) & 10/2017-IGST(R)) ----
            C("13/2017-CT(R)", RcmStream.Section9_3, "GTA", GstSupplyType.Services, null, 500,
                RcmParty.Any, RcmParty.RegisteredPerson, Notn2017, null, "GTA (5% no-ITC, RCM)"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Legal", GstSupplyType.Services, null, 1800,
                RcmParty.Any, RcmParty.RegisteredPerson, Notn2017, null, "Legal services (advocate) 18% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Arbitral", GstSupplyType.Services, null, 1800,
                RcmParty.Any, RcmParty.RegisteredPerson, Notn2017, null, "Arbitral tribunal 18% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Sponsorship", GstSupplyType.Services, null, 1800,
                RcmParty.Any, RcmParty.BodyCorporate, Notn2017, null, "Sponsorship 18% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Director", GstSupplyType.Services, null, 1800,
                RcmParty.Any, RcmParty.BodyCorporate, Notn2017, null, "Director's services 18% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Security", GstSupplyType.Services, null, 1800,
                RcmParty.NonBodyCorporate, RcmParty.BodyCorporate, Notn2017, null, "Security services 18% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Renting-motor-vehicle", GstSupplyType.Services, null, 500,
                RcmParty.NonBodyCorporate, RcmParty.BodyCorporate, Notn2017, null, "Renting of motor vehicle 5% RCM"),
            C("13/2017-CT(R)", RcmStream.Section9_3, "Copyright", GstSupplyType.Services, null, 1800,
                RcmParty.Any, RcmParty.RegisteredPerson, Notn2017, null, "Copyright (author/artist) 18% RCM"),
            C("09/2024-CT(R)", RcmStream.Section9_3, "Renting-commercial", GstSupplyType.Services, null, 1800,
                RcmParty.Unregistered, RcmParty.RegisteredPerson, Notn9Of2024, null, "Renting of commercial property 18% RCM"),

            // ---- §9(4) surviving promoter rows (Notn 7/2019-CT(R)) — fire ONLY for the promoter recipient ----
            C("7/2019-CT(R)", RcmStream.Section9_4, "Cement", GstSupplyType.Goods, "2523", 2800,
                RcmParty.Unregistered, RcmParty.Promoter, Notn7Of2019, null, "Cement from unregistered (dated HSN rate)"),
            C("7/2019-CT(R)", RcmStream.Section9_4, "Input-shortfall", GstSupplyType.Services, null, 1800,
                RcmParty.Unregistered, RcmParty.Promoter, Notn7Of2019, null, "Promoter <80% input shortfall 18% RCM"),
            C("7/2019-CT(R)", RcmStream.Section9_4, "Capital-goods", GstSupplyType.Goods, null, 1800,
                RcmParty.Unregistered, RcmParty.Promoter, Notn7Of2019, null, "Capital goods from unregistered (promoter) RCM"),
        };
    }
}
