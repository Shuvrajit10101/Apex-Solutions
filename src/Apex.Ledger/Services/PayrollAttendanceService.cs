using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Attendance / Production voucher</b> service (Phase 8 slice 3; RQ-6). Records per-employee attendance /
/// leave / production values as <see cref="AttendanceEntry"/> rows — the data of a <b>non-accounting</b>
/// Attendance voucher (it books no ledger entry, so it is stored as entries, not a posted
/// <see cref="Voucher"/>). Pure, deterministic mutation over the <see cref="Company"/> aggregate — framework-,
/// DB-, clock- and RNG-free — enforcing the slice's guards: the employee + attendance type exist, the value is
/// non-negative, and the dates are ordered. The salary-computation engine reads these entries back to pro-rate
/// On-Attendance heads and value On-Production heads. Throws <see cref="InvalidOperationException"/> on any
/// violation, never mutating the company.
/// </summary>
public sealed class PayrollAttendanceService
{
    private readonly Company _company;

    public PayrollAttendanceService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Records an attendance / production value for an employee against an attendance type over
    /// <c>[fromDate, toDate]</c>. The employee and attendance type must exist; the value must be ≥ 0; the end
    /// date must be on or after the start date. Returns the recorded entry.
    /// </summary>
    public AttendanceEntry Record(
        Guid employeeId,
        Guid attendanceTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        decimal value)
    {
        if (_company.FindEmployee(employeeId) is null)
            throw new InvalidOperationException($"Employee {employeeId} not found.");
        if (_company.FindAttendanceType(attendanceTypeId) is null)
            throw new InvalidOperationException($"Attendance type {attendanceTypeId} not found.");
        if (toDate < fromDate)
            throw new InvalidOperationException("Attendance period end must be on or after its start.");
        if (value < 0m)
            throw new InvalidOperationException("An attendance value must be ≥ 0.");

        var entry = new AttendanceEntry(Guid.NewGuid(), employeeId, attendanceTypeId, fromDate, toDate, value);
        _company.AddAttendanceEntry(entry);
        return entry;
    }

    /// <summary>Deletes a recorded attendance entry.</summary>
    public void Delete(Guid attendanceEntryId)
    {
        var entry = _company.FindAttendanceEntry(attendanceEntryId)
            ?? throw new InvalidOperationException($"Attendance entry {attendanceEntryId} not found.");
        _company.RemoveAttendanceEntry(entry);
    }
}
