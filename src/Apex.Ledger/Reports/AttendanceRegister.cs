using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One column of an <see cref="AttendanceRegister"/> — an attendance/production type recorded for at least
/// one employee in the period, with its <see cref="Name"/> and <see cref="Kind"/>.</summary>
public sealed record AttendanceRegisterColumn(Guid AttendanceTypeId, string Name, AttendanceTypeKind Kind);

/// <summary>One employee row of an <see cref="AttendanceRegister"/> — the per-type values (aligned to
/// <see cref="AttendanceRegister.Types"/>) plus the summary <see cref="DaysPaid"/> (Σ attendance-with-pay) and
/// <see cref="DaysLop"/> (Σ leave-without-pay).</summary>
public sealed record AttendanceRegisterRow(
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    IReadOnlyList<decimal> Values,
    decimal DaysPaid,
    decimal DaysLop);

/// <summary>
/// The <b>Attendance / Production Register</b> (Phase 8 slice 8; RQ-16; catalog §14) — a matrix of employees (rows)
/// against attendance/production types (columns) for one period, read off the S3 attendance data with each entry
/// clipped to the period exactly as the pay engine clips On-Attendance heads (so the register agrees with the pay).
/// A <b>pure, deterministic</b> projection. Columns are the types actually recorded in the period (ordered by name);
/// per-type column totals foot the matrix.
/// </summary>
public sealed record AttendanceRegister(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<AttendanceRegisterColumn> Types,
    IReadOnlyList<AttendanceRegisterRow> Rows,
    IReadOnlyList<decimal> TypeTotals)
{
    /// <summary>Builds the attendance register for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>.
    /// Rows are ordered by employee name then number, and type columns by name, so the register is byte-stable
    /// regardless of input order.</summary>
    public static AttendanceRegister Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var employeeSet = new HashSet<Guid>(employeeIds);

        // Sum the clipped value per (employee, attendance type) over the period.
        var perEmployee = new Dictionary<Guid, Dictionary<Guid, decimal>>();
        var seenTypes = new HashSet<Guid>();
        foreach (var e in company.AttendanceEntries)
        {
            if (!employeeSet.Contains(e.EmployeeId)) continue;
            var value = PayrollReportSupport.ClippedValue(e, periodFrom, periodTo);
            if (value == 0m) continue;
            if (company.FindAttendanceType(e.AttendanceTypeId) is null) continue;

            if (!perEmployee.TryGetValue(e.EmployeeId, out var byType))
                perEmployee[e.EmployeeId] = byType = new Dictionary<Guid, decimal>();
            byType.TryGetValue(e.AttendanceTypeId, out var running);
            byType[e.AttendanceTypeId] = running + value;
            seenTypes.Add(e.AttendanceTypeId);
        }

        var types = seenTypes
            .Select(id => company.FindAttendanceType(id)!)
            .Select(t => new AttendanceRegisterColumn(t.Id, t.Name, t.Kind))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.AttendanceTypeId)
            .ToList();
        var typeIndex = new Dictionary<Guid, int>();
        for (int j = 0; j < types.Count; j++) typeIndex[types[j].AttendanceTypeId] = j;

        var rows = new List<AttendanceRegisterRow>();
        var typeTotals = new decimal[types.Count];

        foreach (var employeeId in employeeIds)
        {
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

            var values = new decimal[types.Count];
            decimal paid = 0m, lop = 0m;
            if (perEmployee.TryGetValue(employeeId, out var byType))
            {
                foreach (var (typeId, value) in byType)
                {
                    values[typeIndex[typeId]] = value;
                    switch (types[typeIndex[typeId]].Kind)
                    {
                        case AttendanceTypeKind.AttendancePaid: paid += value; break;
                        case AttendanceTypeKind.LeaveWithoutPay: lop += value; break;
                    }
                }
            }
            for (int j = 0; j < types.Count; j++) typeTotals[j] += values[j];

            rows.Add(new AttendanceRegisterRow(employee.Id, employee.Name, employee.EmployeeNumber, values, paid, lop));
        }

        var orderedRows = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new AttendanceRegister(periodFrom, periodTo, types, orderedRows, typeTotals);
    }
}
