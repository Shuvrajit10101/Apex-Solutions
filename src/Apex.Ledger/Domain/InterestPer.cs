namespace Apex.Ledger.Domain;

/// <summary>
/// The "Per" (rate basis) for interest calculation (catalog §7): the period the annual/periodic rate is
/// quoted against, which fixes the day-count <b>basis</b> the simple/compound formula divides by.
/// </summary>
/// <remarks>
/// The corpus defines the four styles as day-count <b>conventions</b> [CORPUS-BOOK printed p.117]:
/// "30 Day Month … on the basis of 30 Day in one Month", "365 Day Month … on the basis of 365 Day in one
/// Year", "Calendar Month … Month-wise (28, 29, 30 or 31 Days)", "Calendar Year … Year-wise (365 or 366)".
/// <para>
/// ⚠ What it does <b>not</b> settle is whether the ledger's Rate% is quoted against that period or against
/// a year — the difference between a "30 Day Month" rate earning its full percentage every thirty days and
/// earning 30/360 of it. That is measurement <b>T8</b> (plan.md Phase 10.10 · WF-6 / slice S3), and until it
/// lands <b>two of the four divisors below are known to be wrong and are deliberately left wrong</b>: the
/// two answers prescribe different replacements and there is no safe value between them. Each member states
/// the divisor the engine <b>actually</b> uses today — no member describes an intention. That rule exists
/// because the previous version of this file described a divisor the code did not implement, and that
/// disagreement is how the defect (IV-8) went unnoticed.
/// </para>
/// <para>
/// The divisor is resolved <b>per accrual segment</b> — from the segment's own start date, not the report
/// window's — by
/// <see cref="Apex.Ledger.Reports.InterestCalculation.BasisFor(InterestPer, System.DateOnly)"/>. This enum
/// only records the user's choice; the resolver is the single source of the values.
/// </para>
/// </remarks>
public enum InterestPer
{
    /// <summary>
    /// A month of exactly 30 days. ⚠ <b>Resolves to 360 today</b> (a 360-day year), not 30 — the divisor is
    /// annualised. Whether that is right is measurement T8; slice S3 owns this arm.
    /// </summary>
    ThirtyDayMonth = 0,

    /// <summary>A year of exactly 365 days. Resolves to 365. Final under either answer to T8.</summary>
    ThreeSixtyFiveDayYear = 1,

    /// <summary>
    /// The calendar month the accrual segment falls in. ⚠ <b>Resolves to that month's length × 12 today</b>
    /// — 336, 348, 360 or 372 — which is <b>not</b> a real period length and is the defect IV-8b records: a
    /// 28-day February accrual is priced 10.7% dearer than the same 28 days in January. It is left in place
    /// because slice S3 owns it and it is BLOCKED on measurement T8, which decides between the month's own
    /// length (28-31) and the year's (365/366). Do not compute an expected figure from the corpus wording
    /// above; read
    /// <see cref="Apex.Ledger.Reports.InterestCalculation.BasisFor(InterestPer, System.DateOnly)"/>.
    /// </summary>
    CalendarMonth = 2,

    /// <summary>
    /// The calendar year the accrual segment falls in — resolves to 365, or 366 in a leap year. Final under
    /// either answer to T8.
    /// </summary>
    CalendarYear = 3,
}
