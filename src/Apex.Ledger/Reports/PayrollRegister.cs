using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One employee row of a <see cref="PayrollRegister"/> — a columnar salary summary for the period: gross
/// earnings, the statutory deductions broken out (Professional Tax, employee PF, employee ESI, §192 income-tax),
/// any other deductions (advances / general), the total deductions, the net payable and the employer
/// contributions (shown informationally, not in net). Every money field is paisa-exact.</summary>
public sealed record PayrollRegisterRow(
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    Money GrossEarnings,
    Money ProfessionalTax,
    Money EmployeePf,
    Money EmployeeEsi,
    Money IncomeTax,
    Money OtherDeductions,
    Money TotalDeductions,
    Money NetPayable,
    Money EmployerContributions);

/// <summary>
/// The <b>Payroll Register / Statement</b> (Phase 8 slice 8; RQ-16; catalog §14) — a columnar per-employee salary
/// summary for one period with the statutory deductions broken out, plus the period totals. A <b>pure,
/// deterministic</b> projection over the <b>posted Payroll voucher</b> for the wage month (F1/F2), so it reflects
/// what was actually posted (it carries As-User-Defined-Value amounts and omits a cancelled / never-posted run):
/// each row's deduction columns foot to its total deductions, gross − deductions = net, and the register reconciles
/// to the books to the paisa. An employee with no posted payroll for the month yields no row.
/// </summary>
public sealed record PayrollRegister(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<PayrollRegisterRow> Rows,
    Money TotalGrossEarnings,
    Money TotalProfessionalTax,
    Money TotalEmployeePf,
    Money TotalEmployeeEsi,
    Money TotalIncomeTax,
    Money TotalOtherDeductions,
    Money TotalDeductions,
    Money TotalNetPayable,
    Money TotalEmployerContributions)
{
    /// <summary>Builds the payroll register for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>
    /// from the posted Payroll voucher(s) for the month. Rows are ordered by employee name then number so the
    /// register is byte-stable regardless of input order; an employee with no posted payroll yields no row.</summary>
    public static PayrollRegister Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var posted = PayrollReportSupport.PostedPayrollByEmployee(company, employeeIds, periodFrom, periodTo);
        var rows = new List<PayrollRegisterRow>();

        foreach (var employeeId in employeeIds)
        {
            if (!posted.TryGetValue(employeeId, out var pp)) continue;
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

            decimal pt = 0m, pf = 0m, esi = 0m, incomeTax = 0m, other = 0m;
            foreach (var l in pp.Deductions)
            {
                var amount = l.Amount.Amount;
                var ph = company.FindPayHead(l.PayHeadId);
                if (ph is null) { other += amount; continue; } // defensive: a referenced head is not deletable
                if (ph.PtComponent != PtStatutoryComponent.None) pt += amount;
                else if (ph.PfComponent == PfStatutoryComponent.EmployeeProvidentFund) pf += amount;
                else if (ph.EsiComponent == EsiStatutoryComponent.EmployeeStateInsurance) esi += amount;
                else if (ph.IncomeTaxComponent == IncomeTaxComponent.TaxDeductedAtSource) incomeTax += amount;
                else other += amount;
            }

            rows.Add(new PayrollRegisterRow(
                employee.Id, employee.Name, employee.EmployeeNumber,
                GrossEarnings: pp.GrossEarnings,
                ProfessionalTax: new Money(pt),
                EmployeePf: new Money(pf),
                EmployeeEsi: new Money(esi),
                IncomeTax: new Money(incomeTax),
                OtherDeductions: new Money(other),
                TotalDeductions: pp.TotalDeductions,
                NetPayable: pp.NetPayable,
                EmployerContributions: pp.EmployerContributionsTotal));
        }

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new PayrollRegister(
            periodFrom, periodTo, ordered,
            TotalGrossEarnings: Sum(ordered, r => r.GrossEarnings),
            TotalProfessionalTax: Sum(ordered, r => r.ProfessionalTax),
            TotalEmployeePf: Sum(ordered, r => r.EmployeePf),
            TotalEmployeeEsi: Sum(ordered, r => r.EmployeeEsi),
            TotalIncomeTax: Sum(ordered, r => r.IncomeTax),
            TotalOtherDeductions: Sum(ordered, r => r.OtherDeductions),
            TotalDeductions: Sum(ordered, r => r.TotalDeductions),
            TotalNetPayable: Sum(ordered, r => r.NetPayable),
            TotalEmployerContributions: Sum(ordered, r => r.EmployerContributions));
    }

    private static Money Sum(IEnumerable<PayrollRegisterRow> rows, Func<PayrollRegisterRow, Money> pick)
    {
        decimal sum = 0m;
        foreach (var r in rows) sum += pick(r).Amount;
        return new Money(sum);
    }
}
