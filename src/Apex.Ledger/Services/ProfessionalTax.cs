using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Professional-Tax engine</b> (Phase 8 slice 6; RQ-11; Article 276(2)) — a <b>pure, deterministic</b>,
/// framework-/DB-/clock-/RNG-free calculator for the state PT slab deduction. It exists as dedicated logic (rather
/// than the generic As-Computed-Value slabs) because PT cannot be expressed as ordinary slabs:
/// <list type="bullet">
///   <item>it is <b>flat-amount-by-band</b>, not a percentage — the single band containing the monthly PT-wages
///     contributes its fixed rupee amount;</item>
///   <item>a state may charge a higher amount in a single <b>balancing month</b> (Maharashtra/Karnataka: ₹300 in
///     February) via a per-band month override;</item>
///   <item>only <b>Maharashtra</b> differentiates by gender (women exempt to ₹25,000) — handled by resolving the
///     gender-scoped slab table before this engine is reached;</item>
///   <item>a <b>constitutional hard cap of ₹2,500 per person per financial year</b> trims the last deduction so the
///     cumulative FY PT never exceeds ₹2,500, regardless of the configured slabs.</item>
/// </list>
/// All figures are exact whole-rupee <see cref="Money"/> (PT is whole rupees; PT-wages are compared in whole rupees).
/// </summary>
public static class ProfessionalTax
{
    /// <summary>The constitutional <b>annual cap</b> on PT per person per financial year (Article 276(2)): ₹2,500.</summary>
    public const decimal AnnualCap = 2500m;

    // GST state codes for the seeded PT states.
    private const string Maharashtra = "27";
    private const string Karnataka = "29";
    private const string WestBengal = "19";

    /// <summary>February — the balancing month some states over-charge on the top band so twelve months total ₹2,500.</summary>
    public const int FebruaryOverrideMonth = 2;

    /// <summary>
    /// The <b>monthly PT before the annual cap</b> for a member whose whole-rupee PT-wages select a band of
    /// <paramref name="slab"/> in calendar <paramref name="month"/> (1–12): the band amount for the month (applying a
    /// February/any-month override when present), or ₹0 when no band contains the wages. PT-wages are rounded to whole
    /// rupees <b>half-up (away from zero)</b> before band selection — the <b>same</b> whole-rupee rounding the PT
    /// register displays the PT-wage with (F1) — so the band selected always agrees with the wage shown against it (a
    /// fractional gross of ₹10,000.50 rounds to ₹10,001 and selects the &gt;₹10,000 band, never the ₹10,000 band), and
    /// a fractional wage never falls into a boundary gap between contiguous integer bands.
    /// </summary>
    public static Money MonthlyBeforeCap(PtSlab slab, decimal ptWages, int month)
    {
        ArgumentNullException.ThrowIfNull(slab);
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "PT month must be a calendar month 1–12.");
        if (ptWages < 0m) return Money.Zero;
        var wholeRupee = Math.Round(ptWages, 0, MidpointRounding.AwayFromZero);
        var band = slab.SelectBand(wholeRupee);
        return band is null ? Money.Zero : band.AmountForMonth(month);
    }

    /// <summary>
    /// Trims <paramref name="monthlyBeforeCap"/> so that <paramref name="priorFyCumulative"/> + the result never
    /// exceeds the ₹2,500 annual cap (Article 276(2)): the remaining head-room is <c>2500 − prior</c>; the deduction
    /// is <c>min(monthly, remaining)</c>, and ₹0 once the cap is already reached. This is the safety net that bounds
    /// even a mis-configured over-₹2,500 slab.
    /// </summary>
    public static Money ApplyAnnualCap(Money monthlyBeforeCap, Money priorFyCumulative)
    {
        var remaining = AnnualCap - priorFyCumulative.Amount;
        if (remaining <= 0m) return Money.Zero;
        return monthlyBeforeCap.Amount <= remaining ? monthlyBeforeCap : new Money(remaining);
    }

    /// <summary>The capped monthly PT: <see cref="MonthlyBeforeCap"/> trimmed by <see cref="ApplyAnnualCap"/> against
    /// the member's PT already deducted this financial year.</summary>
    public static Money ComputeMonthly(PtSlab slab, decimal ptWages, int month, Money priorFyCumulative)
        => ApplyAnnualCap(MonthlyBeforeCap(slab, ptWages, month), priorFyCumulative);

    /// <summary>The first day (1 April) of the PT financial year (Apr–Mar) containing <paramref name="date"/> — the
    /// window the ₹2,500 cumulative cap resets on.</summary>
    public static DateOnly FinancialYearStart(DateOnly date) =>
        date.Month >= 4 ? new DateOnly(date.Year, 4, 1) : new DateOnly(date.Year - 1, 4, 1);

    /// <summary>
    /// The <b>seeded PT slab tables</b> (Phase 8 slice 6; A14-verified FY 2025-26) — Maharashtra (men + women,
    /// gender-scoped), Karnataka and West Bengal — with fresh ids. Editable per company; seed values are a starting
    /// point, not law. Maharashtra/Karnataka carry the ₹300 February over-charge on their ₹200 top band so twelve
    /// months total exactly ₹2,500; West Bengal has no February quirk (12 × ₹200 = ₹2,400 ≤ cap).
    /// </summary>
    public static IReadOnlyList<PtSlab> SeedSlabTables()
    {
        Money R(decimal v) => new(v);
        PtMonthOverride feb300 = new(FebruaryOverrideMonth, R(300m));

        // Maharashtra (Act 1975) — MEN: ≤7,500 Nil · 7,501–10,000 ₹175 · >10,000 ₹200 (₹300 Feb).
        var mhMale = new PtSlab(Guid.NewGuid(), Maharashtra, PtGenderScope.Male, new[]
        {
            new PtSlabBand(R(0m), R(7500m), R(0m)),
            new PtSlabBand(R(7501m), R(10000m), R(175m)),
            new PtSlabBand(R(10001m), null, R(200m), new[] { feb300 }),
        });

        // Maharashtra — WOMEN: ≤25,000 Nil · >25,000 ₹200 (₹300 Feb). Women exemption ₹25,000/mo (2023 amendment).
        var mhFemale = new PtSlab(Guid.NewGuid(), Maharashtra, PtGenderScope.Female, new[]
        {
            new PtSlabBand(R(0m), R(25000m), R(0m)),
            new PtSlabBand(R(25001m), null, R(200m), new[] { new PtMonthOverride(FebruaryOverrideMonth, R(300m)) }),
        });

        // Karnataka (amended, threshold ₹25,000) — no gender: ≤24,999 Nil · ≥25,000 ₹200 (₹300 Feb).
        var ka = new PtSlab(Guid.NewGuid(), Karnataka, PtGenderScope.Any, new[]
        {
            new PtSlabBand(R(0m), R(24999m), R(0m)),
            new PtSlabBand(R(25000m), null, R(200m), new[] { new PtMonthOverride(FebruaryOverrideMonth, R(300m)) }),
        });

        // West Bengal (Act 1979) — no gender, NO Feb override: 0–10,000 Nil · 10,001–15,000 ₹110 ·
        // 15,001–25,000 ₹130 · 25,001–40,000 ₹150 · >40,000 ₹200.
        var wb = new PtSlab(Guid.NewGuid(), WestBengal, PtGenderScope.Any, new[]
        {
            new PtSlabBand(R(0m), R(10000m), R(0m)),
            new PtSlabBand(R(10001m), R(15000m), R(110m)),
            new PtSlabBand(R(15001m), R(25000m), R(130m)),
            new PtSlabBand(R(25001m), R(40000m), R(150m)),
            new PtSlabBand(R(40001m), null, R(200m)),
        });

        return new[] { mhMale, mhFemale, ka, wb };
    }
}
