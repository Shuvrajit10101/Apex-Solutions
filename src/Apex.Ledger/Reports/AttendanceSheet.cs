using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One employee row of an <see cref="AttendanceSheet"/> — the four summary figures the vendor's page
/// names, each already clipped to the report period exactly as the pay engine clips an On-Attendance head.</summary>
/// <param name="DaysPresent">Σ of every <see cref="AttendanceTypeKind.AttendancePaid"/> type's clipped value.</param>
/// <param name="DaysAbsent">Σ of every <see cref="AttendanceTypeKind.LeaveWithoutPay"/> type's clipped value.</param>
/// <param name="UnitsProduced">Σ of the <see cref="AttendanceTypeKind.Production"/> types that are <b>not</b>
/// overtime (see <see cref="AttendanceSheet.OvertimeTypeIds"/>).</param>
/// <param name="OvertimeWorked">Σ of the production types this book's own masters mark as overtime.</param>
public sealed record AttendanceSheetRow(
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    decimal DaysPresent,
    decimal DaysAbsent,
    decimal UnitsProduced,
    decimal OvertimeWorked)
{
    /// <summary>True when this employee recorded nothing at all in the period — the row the vendor's F12
    /// <i>Remove zero-valued transactions</i> option hides.</summary>
    public bool IsZeroValued =>
        DaysPresent == 0m && DaysAbsent == 0m && UnitsProduced == 0m && OvertimeWorked == 0m;
}

/// <summary>
/// The <b>Attendance Sheet</b> (census row 7.22) — the per-employee <b>summary</b> of attendance for one period:
/// <i>"the total days an employee was present or absent, along with the number of units produced and the overtime
/// hours worked"</i> (help.tallysolutions.com/tally-prime/payroll-reports/attendance-sheet-payroll/). A <b>pure,
/// deterministic</b> projection over the same S3 attendance data, with each entry clipped to the period by
/// <see cref="PayrollReportSupport.ClippedValue"/> so the sheet agrees with the pay to the same fraction of a day.
///
/// <para>🔴 <b>THIS IS NOT THE ATTENDANCE REGISTER, AND FOLDING THE TWO IS THE TRAP CENSUS ROW 7.22 EXISTS TO
/// CLOSE.</b> The vendor publishes them as two pages. <see cref="AttendanceRegister"/> (row 7.15) is the wide
/// <b>matrix</b> — one column per attendance/production type actually recorded, so its width changes with the
/// masters. The Attendance Sheet is the fixed <b>four-figure summary</b> named above, and it is the report an
/// operator reads to answer "how many days was this person here, and what did they produce". They read the same
/// entries; they are not the same report and neither can answer for the other.</para>
///
/// <para><b>How "overtime" is decided, stated rather than assumed.</b> This book carries no overtime flag on an
/// <see cref="AttendanceType"/>. It carries one on a <see cref="PayHead"/> — <see cref="PayHead.IsOvertime"/> —
/// and an On-Production pay head names the type it is calculated on through
/// <see cref="PayHead.AttendanceTypeId"/>. So a production type is treated as overtime <b>iff some pay head that
/// is flagged overtime is calculated on it</b>. That is a derivation from this book's own masters, not an
/// invention: nothing is inferred from a type's NAME. When no pay head is flagged,
/// <see cref="AttendanceSheetRow.OvertimeWorked"/> is zero for every row and every production figure lands in
/// <see cref="AttendanceSheetRow.UnitsProduced"/>.</para>
///
/// <para><b>Units.</b> Production types are recorded in their own <see cref="PayrollUnit"/>, so a Σ across two
/// types measured in different units would be a meaningless number wearing a total's clothes.
/// <see cref="ProductionUnits"/> and <see cref="OvertimeUnits"/> report the distinct unit symbols actually behind
/// each column, so the caller can caption the column with its unit when there is exactly one and footnote the
/// mixture when there is more than one. Nothing is converted between units.</para>
/// </summary>
public sealed record AttendanceSheet(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<AttendanceSheetRow> Rows,
    decimal TotalDaysPresent,
    decimal TotalDaysAbsent,
    decimal TotalUnitsProduced,
    decimal TotalOvertimeWorked,
    IReadOnlyList<Guid> OvertimeTypeIds,
    IReadOnlyList<string> ProductionUnits,
    IReadOnlyList<string> OvertimeUnits,
    IReadOnlyList<string> UncountedTypeNames)
{
    /// <summary>
    /// Builds the attendance sheet for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>.
    /// Rows are ordered by employee name then number, so the sheet is byte-stable regardless of input order.
    /// </summary>
    /// <param name="removeZeroValued">The vendor's F12 <i>Remove zero-valued transactions</i> option — when true,
    /// an employee with nothing recorded in the period is dropped from <see cref="Rows"/>. The totals are the
    /// totals of the rows that remain (dropping empty rows changes no figure, since they contribute zero).</param>
    public static AttendanceSheet Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo,
        bool removeZeroValued = false)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var overtimeTypes = OvertimeAttendanceTypes(company);
        var employeeSet = new HashSet<Guid>(employeeIds);

        var present = new Dictionary<Guid, decimal>();
        var absent = new Dictionary<Guid, decimal>();
        var produced = new Dictionary<Guid, decimal>();
        var overtime = new Dictionary<Guid, decimal>();

        var productionUnits = new SortedSet<string>(StringComparer.Ordinal);
        var overtimeUnits = new SortedSet<string>(StringComparer.Ordinal);
        var uncounted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in company.AttendanceEntries)
        {
            if (!employeeSet.Contains(entry.EmployeeId)) continue;
            if (company.FindAttendanceType(entry.AttendanceTypeId) is not { } type) continue;
            var value = PayrollReportSupport.ClippedValue(entry, periodFrom, periodTo);
            if (value == 0m) continue;

            switch (type.Kind)
            {
                case AttendanceTypeKind.AttendancePaid:
                    Bump(present, entry.EmployeeId, value);
                    break;
                case AttendanceTypeKind.LeaveWithoutPay:
                    Bump(absent, entry.EmployeeId, value);
                    break;
                case AttendanceTypeKind.Production when overtimeTypes.Contains(type.Id):
                    Bump(overtime, entry.EmployeeId, value);
                    AddUnit(company, type, overtimeUnits);
                    break;
                case AttendanceTypeKind.Production:
                    Bump(produced, entry.EmployeeId, value);
                    AddUnit(company, type, productionUnits);
                    break;
                default:
                    // A User-defined type is not one of the four figures the vendor's page names. It is neither
                    // silently folded into "present" (which would overstate paid days) nor silently dropped: it
                    // is NAMED, so the operator can see that the sheet does not account for it.
                    uncounted.Add(type.Name);
                    break;
            }
        }

        var rows = new List<AttendanceSheetRow>();
        foreach (var employeeId in employeeIds)
        {
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");
            rows.Add(new AttendanceSheetRow(
                employee.Id,
                employee.Name,
                employee.EmployeeNumber,
                Read(present, employeeId),
                Read(absent, employeeId),
                Read(produced, employeeId),
                Read(overtime, employeeId)));
        }

        if (removeZeroValued) rows.RemoveAll(r => r.IsZeroValued);

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new AttendanceSheet(
            periodFrom,
            periodTo,
            ordered,
            ordered.Sum(r => r.DaysPresent),
            ordered.Sum(r => r.DaysAbsent),
            ordered.Sum(r => r.UnitsProduced),
            ordered.Sum(r => r.OvertimeWorked),
            overtimeTypes.OrderBy(id => id).ToList(),
            productionUnits.ToList(),
            overtimeUnits.ToList(),
            uncounted.ToList());
    }

    /// <summary>
    /// The attendance/production types this book's own masters mark as <b>overtime</b> — every
    /// <see cref="PayHead.AttendanceTypeId"/> named by a pay head whose <see cref="PayHead.IsOvertime"/> is set.
    /// Public so a test can pin the derivation rather than re-deriving it from a type's name.
    /// </summary>
    public static HashSet<Guid> OvertimeAttendanceTypes(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        var ids = new HashSet<Guid>();
        foreach (var payHead in company.PayHeads)
            if (payHead.IsOvertime && payHead.AttendanceTypeId is { } typeId)
                ids.Add(typeId);
        return ids;
    }

    private static void AddUnit(Company company, AttendanceType type, SortedSet<string> into)
    {
        if (type.PayrollUnitId is not { } unitId) return;
        if (company.FindPayrollUnit(unitId) is { } unit && !string.IsNullOrWhiteSpace(unit.Symbol))
            into.Add(unit.Symbol.Trim());
    }

    private static void Bump(Dictionary<Guid, decimal> map, Guid key, decimal value)
    {
        map.TryGetValue(key, out var running);
        map[key] = running + value;
    }

    private static decimal Read(Dictionary<Guid, decimal> map, Guid key)
        => map.TryGetValue(key, out var v) ? v : 0m;
}
