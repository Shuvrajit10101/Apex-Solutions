namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Calculation Period / basis</b> a <see cref="PayHead"/> amount is stated over (Phase 8 slice 2;
/// catalog §14) — a per-month figure (the common case for Flat Rate / As Computed Value) or a per-day figure
/// (relevant to On Attendance, where the daily rate is grossed up by attended days against the per-day basis).
/// Stored as the enum ordinal (0 = Month).
/// </summary>
public enum PayHeadCalculationPeriod
{
    /// <summary>Per month — the value is a monthly figure.</summary>
    Month = 0,

    /// <summary>Per day — the value is a daily figure (grossed up by attendance in the slice-3 engine).</summary>
    Day = 1,
}
