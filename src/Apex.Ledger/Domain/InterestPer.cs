namespace Apex.Ledger.Domain;

/// <summary>
/// The "Per" (rate basis) for interest calculation (catalog §7): the period the annual/periodic rate is
/// quoted against, which fixes the day-count <b>basis</b> the simple/compound formula divides by.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="ThirtyDayMonth"/> — a 30-day month; a year is treated as 360 days.</item>
/// <item><see cref="ThreeSixtyFiveDayYear"/> — a fixed 365-day year.</item>
/// <item><see cref="CalendarMonth"/> — the actual number of days in the accrual's calendar month(s).</item>
/// <item><see cref="CalendarYear"/> — the actual number of days in the accrual's calendar year (365/366).</item>
/// </list>
/// The concrete day-count basis for a given accrual window is resolved by
/// <see cref="Apex.Ledger.Reports.InterestCalculation"/>; the enum only records the user's choice.
/// </remarks>
public enum InterestPer
{
    /// <summary>30-day month / 360-day year.</summary>
    ThirtyDayMonth = 0,

    /// <summary>Fixed 365-day year.</summary>
    ThreeSixtyFiveDayYear = 1,

    /// <summary>Actual days in the calendar month(s) the accrual spans.</summary>
    CalendarMonth = 2,

    /// <summary>Actual days in the calendar year the accrual spans (365 or 366).</summary>
    CalendarYear = 3,
}
