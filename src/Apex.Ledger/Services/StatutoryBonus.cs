using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>statutory-bonus engine</b> (Phase 8 slice 9; RQ-15; Payment of Bonus Act 1965) — a <b>pure, deterministic</b>,
/// framework-/DB-/clock-/RNG-free calculator for an employee's annual statutory bonus. This is the <b>light</b>
/// deliverable (DP-4): the §10 minimum / §11 maximum and the §12 wage ceiling, <b>not</b> the full allocable-surplus
/// (set-on / set-off) computation. The rules it encodes:
/// <list type="bullet">
///   <item><b>eligibility</b> (§2(13), §8): the member draws Basic + DA <b>≤ ₹21,000/month</b> AND has worked
///     <b>≥ 30 days</b> in the accounting year;</item>
///   <item><b>calculation ceiling</b> (§12): the monthly bonus base is <c>min( actual Basic+DA , max(₹7,000,
///     stateMinimumWage) )</c> — a higher earner's bonus is computed on ₹7,000 (or the state minimum wage), not the
///     full salary;</item>
///   <item><b>rate</b> (§10–§11): between <b>8.33%</b> and <b>20%</b> of the annual base (12 × the monthly base),
///     clamped to that band;</item>
///   <item><b>proration</b> (DP-4): a mid-year joiner's bonus is prorated by the months actually worked;</item>
///   <item>a <b>₹100 minimum</b> annual bonus (§10) for an eligible member.</item>
/// </list>
/// All figures are whole-rupee <see cref="Money"/> (bonus rounds to the rupee).
/// </summary>
public static class StatutoryBonus
{
    /// <summary>The §2(13)/§8 eligibility wage ceiling in rupees: monthly Basic + DA <b>≤ ₹21,000</b>.</summary>
    public const decimal EligibilityWageCeiling = 21_000m;

    /// <summary>The §8 minimum days worked in the accounting year to be eligible: <b>30</b>.</summary>
    public const int MinDaysWorked = 30;

    /// <summary>The §12 default calculation ceiling in rupees: <b>₹7,000</b>/month.</summary>
    public const decimal DefaultCalculationCeiling = BonusConfig.DefaultCalculationCeiling;

    /// <summary>The §10 minimum bonus rate in basis points: <b>833</b> (8.33%).</summary>
    public const int MinRateBasisPoints = BonusConfig.MinRateBasisPoints;

    /// <summary>The §11 maximum bonus rate in basis points: <b>2000</b> (20%).</summary>
    public const int MaxRateBasisPoints = BonusConfig.MaxRateBasisPoints;

    /// <summary>Months a full accounting year comprises: <b>12</b>.</summary>
    public const int MonthsPerYear = 12;

    /// <summary>The §10 minimum annual bonus in rupees for an eligible member: <b>₹100</b>.</summary>
    public const decimal MinimumBonusFloor = 100m;

    /// <summary>Whether a member is <b>eligible</b> for statutory bonus (§2(13), §8): drawing Basic + DA
    /// <b>≤ ₹21,000/month</b> AND having worked <b>≥ 30 days</b> in the accounting year.</summary>
    public static bool IsEligible(Money basicPlusDaMonthly, int daysWorkedInYear) =>
        basicPlusDaMonthly.Amount <= EligibilityWageCeiling && daysWorkedInYear >= MinDaysWorked;

    /// <summary>The §12 <b>monthly bonus base</b>: <c>min( actualBasicPlusDa , max(calculationCeiling, minimumWage) )</c>.
    /// A member below the ceiling contributes their actual Basic + DA; a higher earner is capped at ₹7,000 (or the
    /// higher state minimum wage).</summary>
    public static Money BonusBaseMonthly(Money actualBasicPlusDa, Money calculationCeiling, Money minimumWage)
    {
        var ceiling = Math.Max(calculationCeiling.Amount, minimumWage.Amount);
        return new Money(Math.Min(actualBasicPlusDa.Amount, ceiling));
    }

    /// <summary>Clamps a bonus rate to the §10–§11 band <c>[833, 2000]</c> basis points (8.33%–20%).</summary>
    public static int ClampRate(int rateBasisPoints) =>
        Math.Clamp(rateBasisPoints, MinRateBasisPoints, MaxRateBasisPoints);

    /// <summary>
    /// The <b>annual statutory bonus</b>: <c>bonusBaseMonthly × months × rate</c>, rounded to the whole rupee
    /// (half-up), floored at ₹100 (§10). <paramref name="months"/> = 12 for a full-year member; a mid-year joiner is
    /// prorated by the months actually worked when <paramref name="prorate"/> is <c>true</c> (else always 12). The
    /// rate is clamped to the §10–§11 band. Zero when the base or months is non-positive (nothing to pay).
    /// </summary>
    public static Money AnnualBonus(Money bonusBaseMonthly, int rateBasisPoints, int monthsWorkedInYear, bool prorate)
    {
        var months = prorate ? Math.Clamp(monthsWorkedInYear, 0, MonthsPerYear) : MonthsPerYear;
        if (bonusBaseMonthly.Amount <= 0m || months <= 0) return Money.Zero;
        var rate = ClampRate(rateBasisPoints);
        var raw = bonusBaseMonthly.Amount * months * rate / 10_000m;
        var rounded = Math.Round(raw, 0, MidpointRounding.AwayFromZero);
        if (rounded < MinimumBonusFloor) rounded = MinimumBonusFloor;
        return new Money(rounded);
    }
}
