using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Employees'-State-Insurance contribution engine</b> (Phase 8 slice 5; RQ-9; ESIC — ESI Central Rules 1950,
/// Rule 51) — a <b>pure, deterministic</b>, framework-/DB-/clock-/RNG-free calculator for the statutory ESI split.
/// It exists as dedicated logic (rather than the generic As-Computed-Value slabs) because the statutory figures
/// cannot be expressed as ordinary slabs:
/// <list type="bullet">
///   <item>each side is rounded <b>UP</b> (ceiling) to the next whole rupee <b>independently</b> — the employee
///     0.75% and employer 3.25% are each ceiled, never a single 4% then split (differs from PF's nearest-rupee, and
///     is why ₹17,500 → EE 132 + ER 569 = <b>701</b>, not <c>ceil(700)</c>);</item>
///   <item>the employee share is <b>waived</b> (0) when the member's average daily wage is ≤ ₹176, yet the employer
///     3.25% is still paid;</item>
///   <item>coverage is decided <b>once</b> at the start of the contribution period from the coverage-test wages
///     (≤ ₹21,000, excluding overtime) and <b>frozen</b> for the whole period — contribution is then charged on the
///     <b>actual</b> wages with <b>no ₹21,000 cap</b> on the base.</item>
/// </list>
/// All figures are exact <see cref="Money"/>. The two wage figures are kept distinct: <c>coverageTestWages</c>
/// (excludes overtime; drives the ₹21,000 eligibility test) and <c>contributionWages</c> (includes overtime;
/// drives the amount).
/// </summary>
public static class EsiContribution
{
    /// <summary>The monthly gross-wage <b>coverage ceiling</b> (₹21,000): a member whose coverage-test wages are at
    /// or below this at the start of a contribution period is covered for the whole period.</summary>
    public const decimal CoverageCeiling = 21000m;

    /// <summary>The higher coverage ceiling for a person with disability (₹25,000).</summary>
    public const decimal DisabilityCoverageCeiling = 25000m;

    /// <summary>The employee ESI rate (0.75%) in basis points (100 bp = 1%).</summary>
    public const int DefaultEmployeeRateBasisPoints = 75;

    /// <summary>The employer ESI rate (3.25%) in basis points.</summary>
    public const int DefaultEmployerRateBasisPoints = 325;

    /// <summary>The <b>employee-share exemption</b> threshold: a member whose <b>average daily wage</b> is at or
    /// below ₹176 pays no employee share (the employer 3.25% is still due).</summary>
    public const decimal EmployeeExemptionDailyWage = 176m;

    /// <summary>
    /// Whether a member is <b>covered</b> for a contribution period, decided from their <paramref name="coverageTestWages"/>
    /// (the monthly wages <b>excluding overtime</b>) at the period's start: covered iff at or below the ₹21,000
    /// ceiling (₹25,000 for <paramref name="personWithDisability"/>). This is evaluated <b>once</b> per period and
    /// frozen — a mid-period rise above the ceiling does not remove coverage; it takes effect only at the next
    /// period's re-evaluation.
    /// </summary>
    public static bool IsCovered(decimal coverageTestWages, bool personWithDisability = false)
        => coverageTestWages <= (personWithDisability ? DisabilityCoverageCeiling : CoverageCeiling);

    /// <summary>
    /// Computes one covered member's ESI contribution for the month from their <paramref name="contributionWages"/>
    /// (ESI wages <b>including</b> overtime — the <b>actual</b> wages, with <b>no ₹21,000 cap</b> on the base) and
    /// their <paramref name="averageDailyWage"/> (for the ≤ ₹176 employee-share waiver):
    /// <list type="bullet">
    ///   <item><c>employee = ceil(0.75% × contributionWages)</c>, or <b>0</b> when the average daily wage ≤ ₹176;</item>
    ///   <item><c>employer = ceil(3.25% × contributionWages)</c> — rounded up <b>independently</b> of the employee
    ///     side (never 4%-then-split).</item>
    /// </list>
    /// This assumes the member is already covered (the caller decides coverage via <see cref="IsCovered"/> at the
    /// period start); a not-covered member contributes nothing.
    /// </summary>
    public static EsiMemberContribution ComputeMember(
        decimal contributionWages,
        decimal averageDailyWage,
        int employeeRateBasisPoints = DefaultEmployeeRateBasisPoints,
        int employerRateBasisPoints = DefaultEmployerRateBasisPoints)
    {
        if (contributionWages < 0m)
            throw new ArgumentOutOfRangeException(nameof(contributionWages), "ESI contribution wages cannot be negative.");
        if (employeeRateBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(employeeRateBasisPoints), "ESI employee rate cannot be negative.");
        if (employerRateBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(employerRateBasisPoints), "ESI employer rate cannot be negative.");

        var employee = averageDailyWage <= EmployeeExemptionDailyWage
            ? Money.Zero
            : new Money(CeilRupee(employeeRateBasisPoints / 10000m * contributionWages));
        var employer = new Money(CeilRupee(employerRateBasisPoints / 10000m * contributionWages));

        return new EsiMemberContribution(
            ContributionWages: new Money(contributionWages),
            EmployeeContribution: employee,
            EmployerContribution: employer);
    }

    /// <summary>Rounds a rupee amount <b>up</b> (ceiling) to a whole rupee — the ESIC per-contribution rounding
    /// (Rule 51), applied to each side independently.</summary>
    public static decimal CeilRupee(decimal value) => Math.Ceiling(value);

    // ------------------------------------------------------------------ contribution periods (Apr–Sep / Oct–Mar)

    /// <summary>
    /// The first day of the ESI <b>contribution period</b> that contains <paramref name="date"/>: <b>1 April</b> for
    /// a date in April–September (CP1), <b>1 October</b> for October–March (CP2, whose October–December falls in the
    /// same calendar year and January–March in the next). Coverage is decided from the wages in force on this day.
    /// </summary>
    public static DateOnly ContributionPeriodStart(DateOnly date) => date.Month switch
    {
        >= 4 and <= 9 => new DateOnly(date.Year, 4, 1),
        >= 10 => new DateOnly(date.Year, 10, 1),
        _ => new DateOnly(date.Year - 1, 10, 1), // Jan–Mar belong to the CP that started the previous October
    };

    /// <summary>The last day of the ESI contribution period that contains <paramref name="date"/>: <b>30 September</b>
    /// for CP1, <b>31 March</b> for CP2.</summary>
    public static DateOnly ContributionPeriodEnd(DateOnly date) => date.Month switch
    {
        >= 4 and <= 9 => new DateOnly(date.Year, 9, 30),
        >= 10 => new DateOnly(date.Year + 1, 3, 31),
        _ => new DateOnly(date.Year, 3, 31),
    };
}

/// <summary>
/// One member's computed ESI breakdown for the month (Phase 8 slice 5). <see cref="ContributionWages"/> is the
/// ESI base (actual wages incl. overtime, uncapped); <see cref="EmployeeContribution"/> (0.75%, or 0 when the
/// ≤ ₹176 daily-wage waiver applies) reduces net pay; <see cref="EmployerContribution"/> (3.25%, rounded up
/// independently) is the employer cost. Both contributions are whole-rupee <see cref="Money"/>.
/// </summary>
public readonly record struct EsiMemberContribution(
    Money ContributionWages,
    Money EmployeeContribution,
    Money EmployerContribution);
