using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One employee row of a <see cref="PaymentAdvice"/> — the employee's bank details (from the Employee
/// master) and the net amount to transfer for the period.</summary>
public sealed record PaymentAdviceRow(
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    string? BankName,
    string? BankAccountNumber,
    string? BankIfsc,
    Money NetPayable);

/// <summary>
/// The <b>Payment / Bank Advice</b> (Phase 8 slice 8; RQ-16; catalog §14) — the net pay per employee for a bank
/// salary transfer, with the employee, bank account / IFSC and net amount, plus the run total. A <b>pure,
/// deterministic</b> projection over the <b>posted Payroll voucher</b> for the wage month (F1/F2), so each net
/// equals the payslip net and the total equals the posted voucher's Σ net-payable credit to the paisa — and a
/// cancelled / never-posted run is never advised to the bank. Only employees with a positive posted net appear
/// (there is nothing to remit for a zero net).
/// </summary>
public sealed record PaymentAdvice(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<PaymentAdviceRow> Rows,
    Money TotalNetPayable)
{
    /// <summary>Builds the payment advice for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c> from
    /// the posted Payroll voucher(s) for the month. Rows are ordered by employee name then number so the advice is
    /// byte-stable regardless of input order; only employees with a positive posted net are advised.</summary>
    public static PaymentAdvice Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var posted = PayrollReportSupport.PostedPayrollByEmployee(company, employeeIds, periodFrom, periodTo);
        var rows = new List<PaymentAdviceRow>();
        decimal total = 0m;

        foreach (var employeeId in employeeIds)
        {
            if (!posted.TryGetValue(employeeId, out var pp)) continue;
            if (pp.NetPayable.Amount <= 0m) continue; // nothing to remit
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

            rows.Add(new PaymentAdviceRow(
                employee.Id, employee.Name, employee.EmployeeNumber,
                employee.BankName, employee.BankAccountNumber, employee.BankIfsc,
                pp.NetPayable));
            total += pp.NetPayable.Amount;
        }

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new PaymentAdvice(periodFrom, periodTo, ordered, new Money(total));
    }
}
