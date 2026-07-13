using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>employee row</b> of the <b>gratuity provision register</b> (Phase 8 slice 9; catalog §14) — a single active
/// employee's accrued gratuity as-on the provision date: the join date, completed years (with the ≥6-month round-up),
/// the Basic + DA wage base, the accrued provision and whether the member has <b>vested</b> (≥ 5 years). Wages /
/// gratuity are whole rupees (the accrual rounds to the rupee).
/// </summary>
public sealed record GratuityProvisionRow(
    string EmployeeName,
    string? EmployeeNumber,
    DateOnly? DateOfJoining,
    int CompletedYears,
    long BasicPlusDa,
    long AccruedGratuity,
    bool Vested);

/// <summary>
/// The complete <b>gratuity provision register</b> projection (Phase 8 slice 9): the provision-as-on date, the
/// statutory cap in force, the ordered active-employee <see cref="Rows"/> and the <see cref="TotalLiability"/>. Built
/// by <see cref="GratuityProvisionRegister.Build"/> from the same pure <see cref="GratuityProvision"/> accrual the
/// provision voucher posts against, so the register reconciles to the Gratuity Provision ledger by construction.
/// </summary>
public sealed record GratuityProvisionRegisterReturn(
    DateOnly AsOn,
    long CapAmount,
    IReadOnlyList<GratuityProvisionRow> Rows,
    long TotalLiability);

/// <summary>
/// Builds a <see cref="GratuityProvisionRegisterReturn"/> as-on a date (Phase 8 slice 9; RQ-14) — a <b>pure,
/// deterministic</b> projection over the company masters + dated salary structures, computed with the
/// <see cref="Gratuity"/> engine exactly as the gratuity provision voucher accrues, so the register total equals the
/// posted provision liability by construction. One row per included active employee, ordered by name.
/// </summary>
public static class GratuityProvisionRegister
{
    /// <summary>
    /// Builds the gratuity provision register for <paramref name="employeeIds"/> as-on <paramref name="asOn"/>. Only
    /// active employees with a salary structure in force (and, when the config population is vested-only, vested
    /// members) yield a row. Returns an empty register when the establishment is not enrolled for gratuity.
    /// </summary>
    public static GratuityProvisionRegisterReturn Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly asOn,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var cap = WholeRupee(company.GratuityConfig?.CapAmount ?? new Money(GratuityConfig.DefaultCapAmount));
        var rows = new List<GratuityProvisionRow>();
        long total = 0L;

        foreach (var accrual in GratuityProvision.Accruals(company, employeeIds, asOn, userDefinedAmountsByEmployee))
        {
            var accrued = WholeRupee(accrual.Accrued);
            rows.Add(new GratuityProvisionRow(
                EmployeeName: accrual.Name,
                EmployeeNumber: accrual.EmployeeNumber,
                DateOfJoining: accrual.DateOfJoining,
                CompletedYears: accrual.CompletedYears,
                BasicPlusDa: WholeRupee(accrual.BasicPlusDa),
                AccruedGratuity: accrued,
                Vested: accrual.Vested));
            total += accrued;
        }

        return new GratuityProvisionRegisterReturn(asOn, cap, rows, total);
    }

    /// <summary>The whole-rupee integer of a money field (half-up); gratuity figures are whole rupees.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);
}
