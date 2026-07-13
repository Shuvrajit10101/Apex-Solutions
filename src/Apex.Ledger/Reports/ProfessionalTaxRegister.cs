using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>employee row</b> of the monthly <b>Professional-Tax deduction register</b> (Phase 8 slice 6; catalog §14) —
/// a single employee's PT figures for the wage month, read off the deterministic PT computation the payroll voucher
/// posts. Wages / PT are whole rupees (PT carries no paisa).
/// </summary>
public sealed record ProfessionalTaxRow(
    string EmployeeName,
    string? EmployeeNumber,
    long PtWages,
    long ProfessionalTax);

/// <summary>
/// A complete monthly <b>PT deduction register</b> projection (Phase 8 slice 6): the establishment PT state +
/// registration number and wage month, the ordered employee <see cref="Rows"/> and the <see cref="TotalPt"/> to
/// remit. Deterministic and byte-stable — built by <see cref="ProfessionalTaxRegister.Build"/> from the same pure
/// <see cref="PayrollComputationService"/> the payroll voucher posts, so it reconciles to the "Professional Tax
/// Payable" ledger by construction.
/// </summary>
public sealed record ProfessionalTaxRegisterReturn(
    string? StateCode,
    string? RegistrationNumber,
    DateOnly WageMonth,
    IReadOnlyList<ProfessionalTaxRow> Rows,
    long TotalPt);

/// <summary>
/// Builds a <see cref="ProfessionalTaxRegisterReturn"/> for a wage month (Phase 8 slice 6; RQ-11) — a <b>pure,
/// deterministic</b> projection over the company masters + posted payroll history, computed exactly as the payroll
/// voucher posts (the same <see cref="PayrollComputationService"/> band selection + February over-charge + ₹2,500
/// annual cap), so the register reconciles to the books by construction. One row per employee with a positive PT for
/// the month.
/// </summary>
public static class ProfessionalTaxRegister
{
    /// <summary>
    /// Builds the monthly PT register for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>. Only
    /// employees whose PT for the month is &gt; 0 yield a row (a Nil-band / exempt member is omitted). Rows are ordered
    /// by employee name so the register is byte-stable regardless of input order. Returns an empty register when the
    /// establishment is not enrolled for PT.
    /// </summary>
    public static ProfessionalTaxRegisterReturn Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var computation = new PayrollComputationService(company);
        var rows = new List<ProfessionalTaxRow>();
        long total = 0L;

        if (company.PtConfig is not null)
        {
            foreach (var employeeId in employeeIds)
            {
                var employee = company.FindEmployee(employeeId)
                    ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

                IReadOnlyDictionary<Guid, Money>? userDefined = null;
                userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

                var result = computation.Compute(employeeId, periodFrom, periodTo, userDefined);
                var pt = WholeRupee(result.ProfessionalTaxDeducted);
                if (pt <= 0L) continue; // exempt / Nil band this month

                rows.Add(new ProfessionalTaxRow(
                    EmployeeName: employee.Name,
                    EmployeeNumber: employee.EmployeeNumber,
                    PtWages: WholeRupee(GrossEarnings(result)),
                    ProfessionalTax: pt));
                total += pt;
            }
        }

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new ProfessionalTaxRegisterReturn(
            company.PtConfig?.StateCode, company.PtConfig?.RegistrationNumber, periodTo, ordered, total);
    }

    /// <summary>The month's gross earnings (the PT wage basis) for a computed result.</summary>
    private static Money GrossEarnings(PayrollComputationResult result) => result.GrossEarnings;

    /// <summary>The whole-rupee integer of a PT money field (half-up); PT figures are whole rupees.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);
}
