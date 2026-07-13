using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One printed line of a <see cref="Payslip"/> (an earning, a deduction or an employer contribution) —
/// its display <see cref="Name"/> and computed <see cref="Amount"/> (paisa-exact).</summary>
public sealed record PayslipLine(string Name, Money Amount);

/// <summary>
/// A single employee's <b>payslip</b> for one payroll period (Phase 8 slice 8; RQ-16; catalog §14) — a <b>pure,
/// deterministic</b> projection over the <b>posted Payroll voucher</b> for the wage month (F1/F2): it reads the
/// self-describing <see cref="PayrollLineDetail"/> lines the payroll run posted, so every figure reflects what was
/// actually paid — it carries any As-User-Defined-Value amount and shows nothing for a cancelled / never-posted
/// month, and it reconciles to the books to the paisa by construction. Carries the employer + employee identity, the
/// posted earnings and deduction lines (which foot to <see cref="GrossEarnings"/> / <see cref="TotalDeductions"/>),
/// the derived <see cref="NetPayable"/>, the employer contributions shown <b>informationally</b> (not in net), the
/// period attendance summary and the year-to-date figures. Empty earnings/deductions ⇒ no payroll was posted for the
/// month.
/// </summary>
public sealed record Payslip(
    // ---- employer ----
    string EmployerName,
    string? EmployerAddress,
    // ---- employee identity ----
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    string? Designation,
    string? Department,
    DateOnly? DateOfJoining,
    string? Pan,
    string? Uan,
    string? EsiNumber,
    string? BankName,
    string? BankAccountNumber,
    string? BankIfsc,
    // ---- period ----
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    // ---- lines ----
    IReadOnlyList<PayslipLine> Earnings,
    IReadOnlyList<PayslipLine> Deductions,
    IReadOnlyList<PayslipLine> EmployerContributions,
    // ---- aggregates ----
    Money GrossEarnings,
    Money TotalDeductions,
    Money NetPayable,
    // ---- attendance summary ----
    decimal DaysPaid,
    decimal DaysLop,
    // ---- year-to-date (prior posted months + this month) ----
    Money YtdGrossEarnings,
    Money YtdTotalDeductions,
    Money YtdNetPayable)
{
    /// <summary>Builds the payslip for <paramref name="employeeId"/> over <c>[periodFrom, periodTo]</c> (Phase 8
    /// slice 8; RQ-16). Earnings / deductions are the <b>posted</b> lines (which carry any As-User-Defined-Value
    /// amount and foot to gross / net); employer contributions are shown informationally. When no non-cancelled
    /// Payroll voucher was posted for the month the earnings/deductions are empty and the aggregates are zero (a
    /// "not posted" payslip). The YTD figures telescope the employee's posted, non-cancelled payroll vouchers in
    /// <c>[FY-start, periodTo]</c> (prior months + this month), so YTD net = YTD gross − YTD deductions by
    /// construction.</summary>
    public static Payslip Build(
        Company company,
        Guid employeeId,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);

        var employee = company.FindEmployee(employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        var posted = PayrollReportSupport.PostedPayrollByEmployee(company, new[] { employeeId }, periodFrom, periodTo);
        posted.TryGetValue(employeeId, out var pp);

        var earnings = new List<PayslipLine>();
        var deductions = new List<PayslipLine>();
        var employerContributions = new List<PayslipLine>();
        if (pp is not null)
        {
            foreach (var l in pp.Earnings)
                earnings.Add(new PayslipLine(PayrollReportSupport.LabelForPayHead(company, l.PayHeadId), l.Amount));
            foreach (var l in pp.Deductions)
                deductions.Add(new PayslipLine(PayrollReportSupport.LabelForPayHead(company, l.PayHeadId), l.Amount));
            foreach (var l in pp.EmployerContributions)
                employerContributions.Add(new PayslipLine(PayrollReportSupport.LabelForPayHead(company, l.PayHeadId), l.Amount));
        }

        var gross = pp?.GrossEarnings ?? Money.Zero;
        var totalDeductions = pp?.TotalDeductions ?? Money.Zero;
        var net = pp?.NetPayable ?? Money.Zero;

        var (daysPaid, daysLop) = AttendanceSummary(company, employeeId, periodFrom, periodTo);

        var (ytdGross, ytdDeductions, ytdNet) = PostedYtd(company, employeeId, periodTo);

        var group = company.FindEmployeeGroup(employee.EmployeeGroupId);

        return new Payslip(
            EmployerName: company.Name,
            EmployerAddress: company.Address,
            EmployeeId: employee.Id,
            EmployeeName: employee.Name,
            EmployeeNumber: employee.EmployeeNumber,
            Designation: employee.Designation,
            Department: group?.Name,
            DateOfJoining: employee.DateOfJoining,
            Pan: employee.Pan,
            Uan: employee.Uan,
            EsiNumber: employee.EsiNumber,
            BankName: employee.BankName,
            BankAccountNumber: employee.BankAccountNumber,
            BankIfsc: employee.BankIfsc,
            PeriodFrom: periodFrom,
            PeriodTo: periodTo,
            Earnings: earnings,
            Deductions: deductions,
            EmployerContributions: employerContributions,
            GrossEarnings: gross,
            TotalDeductions: totalDeductions,
            NetPayable: net,
            DaysPaid: daysPaid,
            DaysLop: daysLop,
            YtdGrossEarnings: ytdGross,
            YtdTotalDeductions: ytdDeductions,
            YtdNetPayable: ytdNet);
    }

    /// <summary>The employee's <b>days paid</b> (Σ Attendance-with-pay entries) and <b>LOP days</b> (Σ
    /// Leave-without-pay entries) over the period, each entry clipped to <c>[from, to]</c> exactly as the pay
    /// engine clips On-Attendance heads (so the payslip agrees with the pay).</summary>
    private static (decimal DaysPaid, decimal DaysLop) AttendanceSummary(
        Company company, Guid employeeId, DateOnly from, DateOnly to)
    {
        decimal paid = 0m, lop = 0m;
        foreach (var e in company.AttendanceEntries)
        {
            if (e.EmployeeId != employeeId) continue;
            var type = company.FindAttendanceType(e.AttendanceTypeId);
            if (type is null) continue;
            var value = PayrollReportSupport.ClippedValue(e, from, to);
            if (value == 0m) continue;
            if (type.Kind == AttendanceTypeKind.AttendancePaid) paid += value;
            else if (type.Kind == AttendanceTypeKind.LeaveWithoutPay) lop += value;
        }
        return (paid, lop);
    }

    /// <summary>The Σ of the employee's posted, non-cancelled Payroll earning / deduction / net-payable lines dated
    /// in <c>[FY-start, periodTo]</c> — the year-to-date figures, telescoping the prior months <b>and</b> this
    /// month (both now read from the posted vouchers). A cancelled month never accrued salary, so it is excluded.</summary>
    private static (Money Gross, Money Deductions, Money Net) PostedYtd(
        Company company, Guid employeeId, DateOnly periodTo)
    {
        var fyStart = ProfessionalTax.FinancialYearStart(periodTo);
        decimal gross = 0m, deductions = 0m, net = 0m;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < fyStart || v.Date > periodTo) continue; // FY-to-date, INCLUDING this period (posted)
            foreach (var line in v.Lines)
            {
                if (line.Payroll is not { } pd || pd.EmployeeId != employeeId) continue;
                switch (pd.Category)
                {
                    case PayrollLineCategory.Earning: gross += pd.Amount.Amount; break;
                    case PayrollLineCategory.Deduction: deductions += pd.Amount.Amount; break;
                    case PayrollLineCategory.NetPayable: net += pd.Amount.Amount; break;
                }
            }
        }
        return (new Money(gross), new Money(deductions), new Money(net));
    }
}
