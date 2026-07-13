namespace Apex.Ledger.Domain;

/// <summary>
/// The calendar / production nature of an <see cref="AttendanceType"/> (Phase 8 slice 1; Study Guide pp.196–197)
/// — the four catalog attendance/production types. Payroll masters only; <b>no computation ships in this
/// slice</b>, so the kind is a classification tag the later attendance/payroll engine reads back.
/// </summary>
public enum AttendanceTypeKind
{
    /// <summary>Attendance / Leave with pay — payable present days.</summary>
    AttendancePaid = 0,

    /// <summary>Leave without pay / absence — unpaid days.</summary>
    LeaveWithoutPay = 1,

    /// <summary>Production — piece/output-based, measured against a production <see cref="PayrollUnit"/>.</summary>
    Production = 2,

    /// <summary>User-defined calendar type.</summary>
    UserDefined = 3,
}
