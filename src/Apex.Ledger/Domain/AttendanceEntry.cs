namespace Apex.Ledger.Domain;

/// <summary>
/// A recorded <b>attendance / production value</b> for one employee over a period (Phase 8 slice 3; RQ-6; Study
/// Guide pp.213–214) — the data of a <b>non-accounting</b> Attendance voucher. It links an
/// <see cref="EmployeeId"/> and an <see cref="AttendanceTypeId"/> (the Attendance/Leave/Production type it is
/// recorded against) to a <see cref="Value"/> — attended days, leave days, overtime hours or production units —
/// over <c>[FromDate, ToDate]</c>. The salary-computation engine reads these to pro-rate On-Attendance heads and
/// to value On-Production heads.
/// </summary>
/// <remarks>
/// The Attendance voucher is <b>non-accounting</b>: it books no ledger entry, so it is stored as
/// <see cref="AttendanceEntry"/> rows rather than a posted <see cref="Voucher"/> (unlike the balanced Payroll
/// voucher). The <see cref="Id"/> is the stable key. The <see cref="Value"/> is an exact decimal (a half-day or
/// fractional hour is representable); it is stored at micro scale (× 1,000,000) at the DB/Io boundary. Guards
/// (employee / attendance-type exist, non-negative value, ordered dates) live in <c>PayrollAttendanceService</c>.
/// Empty unless Payroll is used (ER-13). Framework- and DB-agnostic — no clock.
/// </remarks>
public sealed class AttendanceEntry
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The employee this value is recorded for.</summary>
    public Guid EmployeeId { get; }

    /// <summary>The <see cref="AttendanceType"/> the value is recorded against.</summary>
    public Guid AttendanceTypeId { get; }

    /// <summary>The period start (inclusive).</summary>
    public DateOnly FromDate { get; }

    /// <summary>The period end (inclusive); ≥ <see cref="FromDate"/>.</summary>
    public DateOnly ToDate { get; }

    /// <summary>The recorded value — attended/leave days, overtime hours, or production units; exact decimal, ≥ 0.</summary>
    public decimal Value { get; }

    public AttendanceEntry(Guid id, Guid employeeId, Guid attendanceTypeId, DateOnly fromDate, DateOnly toDate, decimal value)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("An attendance entry must reference an employee.", nameof(employeeId));
        if (attendanceTypeId == Guid.Empty)
            throw new ArgumentException("An attendance entry must reference an attendance type.", nameof(attendanceTypeId));
        if (toDate < fromDate)
            throw new ArgumentException("An attendance entry's end date must be on or after its start date.", nameof(toDate));
        if (value < 0m)
            throw new ArgumentException("An attendance entry value must be ≥ 0.", nameof(value));

        Id = id;
        EmployeeId = employeeId;
        AttendanceTypeId = attendanceTypeId;
        FromDate = fromDate;
        ToDate = toDate;
        Value = value;
    }
}
