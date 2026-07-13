using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>gratuity-provision accrual projection</b> (Phase 8 slice 9; RQ-14) — a <b>pure, deterministic</b> per-employee
/// accrual over the company masters + the dated salary structure, computed with the <see cref="Gratuity"/> engine. It
/// is the single source of truth both the <c>Reports.GratuityProvisionRegister</c> (display) and the
/// <c>PayrollVoucherService</c> gratuity-provision posting (the delta over the prior balance) read, so the register
/// reconciles to the posting by construction. Lives in the services layer so the posting can consume it without a
/// dependency on the reports layer.
/// </summary>
public static class GratuityProvision
{
    /// <summary>One employee's accrued gratuity at the provision-as-on date.</summary>
    public sealed record EmployeeAccrual(
        Guid EmployeeId,
        string Name,
        string? EmployeeNumber,
        DateOnly? DateOfJoining,
        int MonthsOfService,
        int CompletedYears,
        bool Vested,
        Money BasicPlusDa,
        Money Accrued);

    /// <summary>
    /// The per-employee accruals for <paramref name="employeeIds"/> at <paramref name="asOn"/> (the provision date).
    /// An employee is skipped when the establishment is not enrolled for gratuity, the member is <b>not active</b> on
    /// <paramref name="asOn"/> (no join date, joined after it, or already left on/before it), has no salary structure
    /// in force there, or — when <see cref="GratuityConfig.Population"/> is
    /// <see cref="GratuityProvisionPopulation.VestedOnly"/> — has not yet vested. Basic + DA is the Σ of the structure's
    /// <see cref="PayHead.UseForGratuity"/> earnings for the month containing <paramref name="asOn"/>. Rows are ordered
    /// by employee name so the projection is byte-stable regardless of input order.
    /// </summary>
    public static IReadOnlyList<EmployeeAccrual> Accruals(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly asOn,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var rows = new List<EmployeeAccrual>();
        if (company.GratuityConfig is not { } config) return rows;

        var computation = new PayrollComputationService(company);
        foreach (var employeeId in employeeIds)
        {
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

            if (employee.DateOfJoining is not { } doj || doj > asOn) continue;   // not yet joined
            if (employee.DateOfLeaving is { } dol && dol <= asOn) continue;      // already left ⇒ not active

            if (computation.ResolveStructureInForce(employee, asOn) is null) continue; // no structure to value

            IReadOnlyDictionary<Guid, Money>? userDefined = null;
            userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

            var monthStart = new DateOnly(asOn.Year, asOn.Month, 1);
            var result = computation.Compute(employeeId, monthStart, asOn, userDefined);
            var basicPlusDa = result.GratuityWages;

            var months = Gratuity.WholeMonthsBetween(doj, asOn);
            var vested = Gratuity.IsVested(months);
            if (config.Population == GratuityProvisionPopulation.VestedOnly && !vested) continue;

            var completedYears = Gratuity.CompletedYears(months);
            var accrued = Gratuity.Accrued(basicPlusDa, completedYears, config.CapAmount);

            rows.Add(new EmployeeAccrual(
                employee.Id, employee.Name, employee.EmployeeNumber, doj,
                months, completedYears, vested, basicPlusDa, accrued));
        }

        return rows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The total accrued gratuity liability across <paramref name="employeeIds"/> at <paramref name="asOn"/>
    /// — the Σ of every included member's accrued provision (whole rupees). The figure the provision voucher targets
    /// (posting only the delta over the prior provision balance).</summary>
    public static Money TotalLiability(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly asOn,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        var sum = 0m;
        foreach (var accrual in Accruals(company, employeeIds, asOn, userDefinedAmountsByEmployee))
            sum += accrual.Accrued.Amount;
        return new Money(sum);
    }
}
