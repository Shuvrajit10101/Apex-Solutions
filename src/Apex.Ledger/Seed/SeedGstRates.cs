using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the config-driven GST rate slabs (catalog §12; phase4 RQ-25/DP-2). Phase 4 seeds the <b>live GST 2.0
/// set 0 / 5 / 18 / 40 %</b> (law L-1/L-2, effective 22-Sep-2025) — <b>not</b> the legacy 0/5/12/18/28. Slabs
/// are ordinary editable data (each a <see cref="GstRateSlab"/> in basis points), so a future council change
/// is a data edit, not a code change. Mirrors the <c>SeedCurrencies</c> reference-data pattern.
/// </summary>
public static class SeedGstRates
{
    /// <summary>The Phase-4 default slab set in basis points: 0, 500 (5%), 1800 (18%), 4000 (40%).</summary>
    public static readonly IReadOnlyList<(int RateBasisPoints, string Label)> DefaultSlabs = new[]
    {
        (0, "0%"),
        (500, "5%"),
        (1800, "18%"),
        (4000, "40%"),
    };

    /// <summary>Builds the seeded default GST rate slabs (fresh ids each call).</summary>
    public static IReadOnlyList<GstRateSlab> BuildDefaults() =>
        DefaultSlabs
            .Select(s => new GstRateSlab(Guid.NewGuid(), s.RateBasisPoints, s.Label, isPredefined: true))
            .ToList();

    // ---- Phase 9 slice 1: dated rate-history + Compensation-Cess seeds (RQ-1/RQ-2; GST 2.0 eff. 22-Sep-2025). ----
    // These are the ADVANCED-GST defaults; they are NOT seeded by the base GstService.EnableGst (which stays
    // byte-identical, ER-13). They are applied only via the explicit advanced-GST opt-in (GstService.SeedAdvancedGst)
    // / the GST Rate Setup bulk screen / an import. A14 to re-verify exact numbers + dates against CBIC (R7).

    private static readonly DateOnly Gst2Start = new(2025, 9, 22);   // GST 2.0 cut-over
    private static readonly DateOnly Gst2End = new(2025, 9, 21);     // last day of the legacy/old regime (inclusive)
    private static readonly DateOnly LegacyStart = new(2017, 7, 1);  // original GST rollout
    private static readonly DateOnly Fy2526Start = new(2025, 4, 1);  // FY2025-26 opening
    private static readonly DateOnly CessTobaccoEnd = new(2026, 1, 31); // last day tobacco/pan-masala cess applies

    /// <summary>
    /// The GST 2.0 dated rate-history windows (fresh ids each call): the generic 0/5/18/40 slabs (from 22-Sep-2025,
    /// open) + surviving special rates 3/1.5/0.25% + the legacy 12/28% rows retained inactive-by-date (2017-07-01 …
    /// 2025-09-21) + HSN carve-outs for the car (8703: 28% legacy → 40% new) and RSP-valued tobacco/pan-masala.
    /// </summary>
    public static IReadOnlyList<GstRateHistoryEntry> BuildDefaultRateHistory()
    {
        GstRateHistoryEntry H(string? hsn, int bp, GstRateClass cls, DateOnly from, DateOnly? to,
            GstValuationBasis basis, string label) =>
            new(Guid.NewGuid(), hsn, bp, cls, from, to, basis, label, isPredefined: true);

        return new List<GstRateHistoryEntry>
        {
            // Generic GST 2.0 slabs (open-ended from the cut-over).
            H(null, 0,    GstRateClass.Standard, Gst2Start, null, GstValuationBasis.TransactionValue, "0% (GST 2.0)"),
            H(null, 500,  GstRateClass.Merit,    Gst2Start, null, GstValuationBasis.TransactionValue, "5% (GST 2.0)"),
            H(null, 1800, GstRateClass.Standard, Gst2Start, null, GstValuationBasis.TransactionValue, "18% (GST 2.0)"),
            H(null, 4000, GstRateClass.DeMerit,  Gst2Start, null, GstValuationBasis.TransactionValue, "40% (GST 2.0 de-merit)"),
            // Surviving special rates (in force alongside the slabs).
            H(null, 300, GstRateClass.Special, LegacyStart, null, GstValuationBasis.TransactionValue, "3% (bullion/jewellery)"),
            H(null, 150, GstRateClass.Special, LegacyStart, null, GstValuationBasis.TransactionValue, "1.5% (cut & polished diamonds)"),
            H(null, 25,  GstRateClass.Special, LegacyStart, null, GstValuationBasis.TransactionValue, "0.25% (rough diamonds)"),
            // Legacy generic rates — retained inactive-by-date so a pre-22-Sep voucher reprints correctly.
            H(null, 1200, GstRateClass.Legacy, LegacyStart, Gst2End, GstValuationBasis.TransactionValue, "12% (legacy)"),
            H(null, 2800, GstRateClass.Legacy, LegacyStart, Gst2End, GstValuationBasis.TransactionValue, "28% (legacy)"),
            // Car (HSN 8703): 28% legacy → 40% under GST 2.0.
            H("8703", 2800, GstRateClass.Legacy,  LegacyStart, Gst2End, GstValuationBasis.TransactionValue, "Car 28% (legacy)"),
            H("8703", 4000, GstRateClass.DeMerit, Gst2Start,   null,    GstValuationBasis.TransactionValue, "Car 40% (GST 2.0)"),
            // Tobacco / pan-masala carve-out — 28% + cess on RSP; did NOT move to 40% on 22-Sep-2025.
            H("2402",     2800, GstRateClass.CarveOut, LegacyStart, null, GstValuationBasis.RetailSalePrice, "Cigarettes 28% (RSP carve-out)"),
            H("2403",     2800, GstRateClass.CarveOut, LegacyStart, null, GstValuationBasis.RetailSalePrice, "Chewing tobacco 28% (RSP carve-out)"),
            H("21069020", 2800, GstRateClass.CarveOut, LegacyStart, null, GstValuationBasis.RetailSalePrice, "Pan masala 28% (RSP carve-out)"),
        };
    }

    /// <summary>
    /// The three dated FY2025-26 Compensation-Cess windows (fresh ids each call): window (a) de-merit goods
    /// 01-Apr-2025 … 21-Sep-2025 (car ad-valorem, aerated 12%, coal ₹400/tonne); window (b) tobacco/pan-masala only,
    /// 01-Apr-2025 … 31-Jan-2026 (RSP-factor / specific); window (c) from 01-Feb-2026 has <b>no rows</b> ⇒ nil cess on
    /// everything automatically (no open-ended cess row is seeded).
    /// </summary>
    public static IReadOnlyList<GstCessRate> BuildDefaultCessRates()
    {
        GstCessRate C(string? hsn, CessValuationMode mode, int rateBp, Money perUnit, int rspMillis,
            DateOnly from, DateOnly? to, string label) =>
            new(Guid.NewGuid(), hsn, mode, rateBp, perUnit, rspMillis, from, to, label, isPredefined: true);

        return new List<GstCessRate>
        {
            // Window (a): de-merit goods, ending on the GST 2.0 cut-over (21-Sep-2025 inclusive).
            C("8703", CessValuationMode.AdValorem, 2200, Money.Zero, 0, Fy2526Start, Gst2End, "Car cess 22% (to 21-Sep-2025)"),
            C("2202", CessValuationMode.AdValorem, 1200, Money.Zero, 0, Fy2526Start, Gst2End, "Aerated waters cess 12% (to 21-Sep-2025)"),
            C("2701", CessValuationMode.Specific,  0, new Money(400m), 0, Fy2526Start, Gst2End, "Coal cess ₹400/tonne (to 21-Sep-2025)"),
            // Window (b): only tobacco/pan-masala retain cess (through 31-Jan-2026). Cigarettes = specific per unit;
            // pan masala / chewing tobacco = RSP-factor (0.32R / 0.56R).
            C("2402", CessValuationMode.Specific, 0, new Money(4.17m), 0, Fy2526Start, CessTobaccoEnd, "Cigarettes cess ₹4.17/unit (to 31-Jan-2026)"),
            C("21069020", CessValuationMode.RetailSalePriceFactor, 0, Money.Zero, 320, Fy2526Start, CessTobaccoEnd, "Pan masala cess 0.32R (to 31-Jan-2026)"),
            C("2403", CessValuationMode.RetailSalePriceFactor, 0, Money.Zero, 560, Fy2526Start, CessTobaccoEnd, "Chewing tobacco cess 0.56R (to 31-Jan-2026)"),
        };
    }
}
