using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One column of a <see cref="PaySheet"/> — an affecting pay head (an earning or a deduction) that at
/// least one employee in the run carries, with its display <see cref="Name"/> and posting <see cref="Role"/>.</summary>
public sealed record PaySheetColumn(Guid PayHeadId, string Name, PayHeadPostingRole Role);

/// <summary>One employee row of a <see cref="PaySheet"/> — the per-column amounts (aligned to
/// <see cref="PaySheet.Columns"/>, zero where the employee has no such head) plus the row's gross / total
/// deductions / net.</summary>
public sealed record PaySheetRow(
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    IReadOnlyList<Money> Values,
    Money GrossEarnings,
    Money TotalDeductions,
    Money NetPayable);

/// <summary>
/// The <b>Pay Sheet</b> (Phase 8 slice 8; RQ-16; catalog §14) — a matrix of employees (rows) against pay heads
/// (columns) for one payroll period, with per-column totals and per-row gross / deductions / net that <b>foot</b>
/// to the same grand total. A <b>pure, deterministic</b> projection over the <b>posted Payroll voucher</b> for the
/// wage month (F1/F2), so the sheet reflects what was actually posted (it carries As-User-Defined-Value amounts and
/// omits a cancelled / never-posted run) and reconciles to the books to the paisa. Columns are the posted earnings
/// (then deductions) across the run, ordered by role then name; employer contributions and tracked-but-not-paid
/// heads are not columns (they do not enter gross / net). An employee with no posted payroll for the month yields
/// no row.
/// </summary>
public sealed record PaySheet(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<PaySheetColumn> Columns,
    IReadOnlyList<PaySheetRow> Rows,
    IReadOnlyList<Money> ColumnTotals,
    Money TotalGrossEarnings,
    Money TotalDeductions,
    Money TotalNetPayable)
{
    /// <summary>Builds the pay sheet for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c> from the
    /// posted Payroll voucher(s) for the month. Rows are ordered by employee name then number so the sheet is
    /// byte-stable regardless of input order; an employee with no posted payroll yields no row.</summary>
    public static PaySheet Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var posted = PayrollReportSupport.PostedPayrollByEmployee(company, employeeIds, periodFrom, periodTo);

        // Pass 1: gather the distinct posted earning/deduction columns across the run.
        var columnByHead = new Dictionary<Guid, PaySheetColumn>();
        foreach (var employeeId in employeeIds)
        {
            if (!posted.TryGetValue(employeeId, out var pp)) continue;
            foreach (var l in pp.Earnings)
                columnByHead.TryAdd(l.PayHeadId, new PaySheetColumn(
                    l.PayHeadId, PayrollReportSupport.LabelForPayHead(company, l.PayHeadId), PayHeadPostingRole.Earning));
            foreach (var l in pp.Deductions)
                columnByHead.TryAdd(l.PayHeadId, new PaySheetColumn(
                    l.PayHeadId, PayrollReportSupport.LabelForPayHead(company, l.PayHeadId), PayHeadPostingRole.Deduction));
        }

        // Earnings first, then deductions; within a role by display name then head id (stable).
        var columns = columnByHead.Values
            .OrderBy(col => col.Role == PayHeadPostingRole.Earning ? 0 : 1)
            .ThenBy(col => col.Name, StringComparer.Ordinal)
            .ThenBy(col => col.PayHeadId)
            .ToList();
        var columnIndex = new Dictionary<Guid, int>();
        for (int j = 0; j < columns.Count; j++) columnIndex[columns[j].PayHeadId] = j;

        // Pass 2: rows aligned to the columns (only employees with a posted payroll for the month).
        var rows = new List<PaySheetRow>();
        var columnTotals = new decimal[columns.Count];
        decimal grandGross = 0m, grandDeductions = 0m, grandNet = 0m;

        foreach (var employeeId in employeeIds)
        {
            if (!posted.TryGetValue(employeeId, out var pp)) continue;
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

            var values = new decimal[columns.Count];
            foreach (var l in pp.Earnings)
                if (columnIndex.TryGetValue(l.PayHeadId, out var j)) values[j] += l.Amount.Amount;
            foreach (var l in pp.Deductions)
                if (columnIndex.TryGetValue(l.PayHeadId, out var j)) values[j] += l.Amount.Amount;
            for (int j = 0; j < columns.Count; j++) columnTotals[j] += values[j];

            var gross = pp.GrossEarnings;
            var deductions = pp.TotalDeductions;
            var net = pp.NetPayable;
            grandGross += gross.Amount;
            grandDeductions += deductions.Amount;
            grandNet += net.Amount;

            rows.Add(new PaySheetRow(
                employee.Id, employee.Name, employee.EmployeeNumber,
                values.Select(v => new Money(v)).ToList(), gross, deductions, net));
        }

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new PaySheet(
            periodFrom, periodTo, columns, ordered,
            columnTotals.Select(v => new Money(v)).ToList(),
            new Money(grandGross), new Money(grandDeductions), new Money(grandNet));
    }
}
