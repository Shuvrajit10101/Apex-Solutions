namespace Apex.Ledger.Reports;

/// <summary>
/// An inclusive reporting window <c>[From, To]</c> (RQ-1 period/date-range selection).
/// Flow reports (P&amp;L, Day Book, Trial-Balance movement) count only movements dated within
/// the window; balance reports (Trial Balance as-of, Balance Sheet) are as-of <see cref="To"/>.
/// All dates are UI-independent <see cref="DateOnly"/> values; the engine never persists a
/// chosen window.
/// </summary>
public readonly record struct PeriodRange(DateOnly From, DateOnly To)
{
    /// <summary>Guards that the window is well-formed (<see cref="From"/> ≤ <see cref="To"/>).</summary>
    public bool IsValid => From <= To;
}
