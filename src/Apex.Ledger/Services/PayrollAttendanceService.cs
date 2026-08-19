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
    ///
    /// <para>🔴 <b>T0-12 — an EXACT duplicate is refused, because the computation sums and the operator cannot
    /// undo.</b> This method used to append unconditionally, with no dedupe on employee × type × period, while
    /// <c>PayrollComputationService.SumAttendance</c> adds every matching entry. A re-key of the same month
    /// therefore paid an On-Attendance head <b>twice</b> — measured at ₹52,000 on a ₹26,000 / 26-day head — and
    /// nothing in the product could remove either entry (<see cref="Delete"/> had no desktop caller and there is no
    /// voucher alteration). Recording the same employee, the same attendance type and the SAME From and To is now
    /// refused with a message naming the value already on record; correct it by deleting that entry and recording
    /// again.</para>
    ///
    /// <para><b>This is a deliberate NARROWING of an attested behaviour, not a corpus-silent design choice.</b> In
    /// the reference application an Attendance voucher is a voucher: two for one period sum, and the operator alters
    /// or deletes one. We are narrower only because that compensating control is absent here (census T1-1). Lift the
    /// refusal when alteration reaches this surface.</para>
    ///
    /// <para><b>The refusal is EXACT, deliberately.</b> Only a coinciding From <i>and</i> To collides. Overlapping
    /// but non-identical spans still record, because the computation already pro-rates an entry by its overlap with
    /// the payroll period and splitting a month into genuine part-period records is ordinary practice.</para>
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

        if (FindExact(employeeId, attendanceTypeId, fromDate, toDate) is { } duplicate)
            throw new InvalidOperationException(
                $"{_company.FindAttendanceType(attendanceTypeId)!.Name} for " +
                $"{_company.FindEmployee(employeeId)!.Name} over {fromDate:dd-MM-yyyy} to {toDate:dd-MM-yyyy} is " +
                $"already recorded as {duplicate.Value:0.##}. Recording it again would ADD to that figure and pay " +
                $"twice — delete the existing entry first if you meant to correct it.");

        var entry = new AttendanceEntry(Guid.NewGuid(), employeeId, attendanceTypeId, fromDate, toDate, value);
        _company.AddAttendanceEntry(entry);
        return entry;
    }

    /// <summary>
    /// The already-recorded entry for exactly this employee × attendance type × period, or <c>null</c>. Public so
    /// the entry screen can warn before the operator commits a whole batch (T0-12).
    /// </summary>
    public AttendanceEntry? FindExact(Guid employeeId, Guid attendanceTypeId, DateOnly fromDate, DateOnly toDate)
    {
        foreach (var e in _company.AttendanceEntries)
            if (e.EmployeeId == employeeId && e.AttendanceTypeId == attendanceTypeId
                && e.FromDate == fromDate && e.ToDate == toDate)
                return e;
        return null;
    }

    /// <summary>Deletes a recorded attendance entry.</summary>
    public void Delete(Guid attendanceEntryId)
    {
        var entry = _company.FindAttendanceEntry(attendanceEntryId)
            ?? throw new InvalidOperationException($"Attendance entry {attendanceEntryId} not found.");
        _company.RemoveAttendanceEntry(entry);
    }
}
