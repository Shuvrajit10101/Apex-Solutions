using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>gratuity engine</b> (Phase 8 slice 9; RQ-14; Payment of Gratuity Act 1972) — a <b>pure, deterministic</b>,
/// framework-/DB-/clock-/RNG-free calculator for the statutory gratuity <b>provision</b> accrued for one employee. It
/// exists as dedicated logic because the accrual is a distinct closed-form the generic pay-head slabs cannot express:
/// <list type="bullet">
///   <item><b>gratuity = (last-drawn Basic + DA) × 15 × completedYears ÷ 26</b> — 15 days' wages per completed year
///     over 26 deemed working days per month (§4(2));</item>
///   <item>the <b>completed years</b> round a part-year <b>up when ≥ 6 months</b>, dropped otherwise (§4(2)):
///     <c>floor(months/12) + (months%12 ≥ 6 ? 1 : 0)</c>;</item>
///   <item>the result is <b>capped at ₹20,00,000</b> (§4(3)) — a configurable constant on
///     <see cref="GratuityConfig"/>;</item>
///   <item><b>vesting</b> is five years' continuous service (§4(1)) — a flag the register shows; the provision itself
///     accrues for all active members before vesting (DP-4).</item>
/// </list>
/// All figures are whole-rupee <see cref="Money"/> (the accrued provision rounds to the rupee).
/// </summary>
public static class Gratuity
{
    /// <summary>Days' wages accrued per completed year (§4(2)): <b>15</b>.</summary>
    public const int DaysPerYear = 15;

    /// <summary>Deemed working days per month the 15 days are divided over (§4(2)): <b>26</b>.</summary>
    public const int DeemedDaysPerMonth = 26;

    /// <summary>Years of continuous service required to <b>vest</b> (§4(1)): <b>5</b>.</summary>
    public const int VestingYears = 5;

    /// <summary>The statutory cap in rupees (§4(3)): <b>₹20,00,000</b> — mirrors <see cref="GratuityConfig.DefaultCapAmount"/>.</summary>
    public const decimal DefaultCapAmount = GratuityConfig.DefaultCapAmount;

    /// <summary>
    /// The <b>whole months of service</b> from <paramref name="dateOfJoining"/> to the provision-as-on date
    /// <paramref name="asOn"/> (both inclusive of the join): the count of complete calendar months, dropping a
    /// trailing part-month (a member who joined on the 15th completes a further whole month only on the next 15th).
    /// Returns 0 when <paramref name="asOn"/> precedes joining. Deterministic, culture-invariant.
    /// </summary>
    public static int WholeMonthsBetween(DateOnly dateOfJoining, DateOnly asOn)
    {
        if (asOn < dateOfJoining) return 0;
        var months = (asOn.Year - dateOfJoining.Year) * 12 + (asOn.Month - dateOfJoining.Month);
        if (asOn.Day < dateOfJoining.Day) months--;
        return months < 0 ? 0 : months;
    }

    /// <summary>The <b>completed years</b> for the 15/26 formula from a whole-month service count (§4(2)): the whole
    /// years, plus one when the residual months are <b>≥ 6</b> (rounded up), dropped otherwise. So 5 years 7 months →
    /// 6 years; 5 years 5 months → 5 years.</summary>
    public static int CompletedYears(int monthsOfService)
    {
        if (monthsOfService <= 0) return 0;
        var years = monthsOfService / 12;
        if (monthsOfService % 12 >= 6) years++;
        return years;
    }

    /// <summary>Whether a member with <paramref name="monthsOfService"/> whole months has <b>vested</b> (§4(1)): five
    /// years' continuous service = 60 whole months. Independent of the ≥6-month formula rounding, so a member whose
    /// completed-years figure rounds up to 5 (e.g. 4 years 7 months) is <b>not</b> yet vested.</summary>
    public static bool IsVested(int monthsOfService) => monthsOfService >= VestingYears * 12;

    /// <summary>
    /// The <b>accrued gratuity provision</b> for a member: <c>min( (basicPlusDa × 15 × completedYears) ÷ 26 , cap )</c>,
    /// rounded to the whole rupee (half-up / away-from-zero). Zero when the wage or completed-years is non-positive.
    /// The cap (<paramref name="cap"/>, from <see cref="GratuityConfig.CapAmount"/>) trims a high-wage / long-service
    /// accrual to the §4(3) ceiling.
    /// </summary>
    public static Money Accrued(Money basicPlusDa, int completedYears, Money cap)
    {
        if (completedYears <= 0 || basicPlusDa.Amount <= 0m) return Money.Zero;
        var raw = basicPlusDa.Amount * DaysPerYear * completedYears / DeemedDaysPerMonth;
        var rounded = Math.Round(raw, 0, MidpointRounding.AwayFromZero);
        var capped = cap.Amount >= 0m && rounded > cap.Amount ? cap.Amount : rounded;
        return new Money(capped);
    }
}
