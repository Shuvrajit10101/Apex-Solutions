using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>employee row</b> of the <b>statutory-bonus register</b> (Phase 8 slice 9; catalog §14) — a single employee's
/// bonus figures for the accounting year: whether the member is <see cref="Eligible"/> (worked ≥ 30 days), the actual
/// monthly Basic + DA, the §12-capped base, the applied rate and the annual bonus. Wages / bonus are whole rupees. A
/// member drawing Basic + DA above the ₹21,000 eligibility ceiling is <b>excluded</b> from the register entirely (no
/// row), per the Act.
/// </summary>
public sealed record BonusRow(
    string EmployeeName,
    string? EmployeeNumber,
    bool Eligible,
    long ActualBasicDa,
    long CappedBase,
    decimal RatePercent,
    long AnnualBonus);

/// <summary>
/// The complete <b>statutory-bonus register</b> projection (Phase 8 slice 9): the accounting-year window, the ordered
/// employee <see cref="Rows"/> (those within the ₹21,000 eligibility wage ceiling) and the <see cref="TotalBonus"/>.
/// Built by <see cref="BonusRegister.Build"/> from the pure <see cref="StatutoryBonus"/> engine over the dated salary
/// structure, so it is deterministic and byte-stable.
/// </summary>
public sealed record BonusRegisterReturn(
    DateOnly FinancialYearStart,
    DateOnly FinancialYearEnd,
    IReadOnlyList<BonusRow> Rows,
    long TotalBonus);

/// <summary>
/// Builds a <see cref="BonusRegisterReturn"/> for an accounting year (Phase 8 slice 9; RQ-15) — a <b>pure,
/// deterministic</b> projection over the company masters + the dated salary structure, computed with the
/// <see cref="StatutoryBonus"/> engine (eligibility ≤ ₹21,000 + ≥ 30 days, the §12 ₹7,000/min-wage calc ceiling, the
/// 8.33%–20% rate, mid-year proration, the ₹100 floor). One row per employee within the eligibility wage ceiling,
/// ordered by name; a member above ₹21,000 is excluded.
/// </summary>
public static class BonusRegister
{
    /// <summary>
    /// Builds the statutory-bonus register for <paramref name="employeeIds"/> over the accounting year beginning
    /// <paramref name="financialYearStart"/> (1 April). An employee not active in the year, without a salary structure
    /// in force, or drawing Basic + DA above ₹21,000/month yields no row. The annual bonus is prorated by months
    /// worked (per config) and is ₹0 for a member who worked fewer than 30 days. Returns an empty register when the
    /// establishment is not enrolled for statutory bonus.
    /// </summary>
    public static BonusRegisterReturn Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly financialYearStart,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var fyStart = financialYearStart;
        var fyEnd = fyStart.AddYears(1).AddDays(-1);
        var rows = new List<BonusRow>();
        long total = 0L;

        if (company.BonusConfig is { } config)
        {
            var computation = new PayrollComputationService(company);
            foreach (var employeeId in employeeIds)
            {
                var employee = company.FindEmployee(employeeId)
                    ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

                // Active window inside the accounting year: [max(join, fyStart) .. min(leave, fyEnd)].
                if (employee.DateOfJoining is { } doj && doj > fyEnd) continue;   // joined after the year
                if (employee.DateOfLeaving is { } dolChk && dolChk < fyStart) continue; // left before the year

                var effectiveStart = employee.DateOfJoining is { } dj && dj > fyStart ? dj : fyStart;
                var effectiveEnd = employee.DateOfLeaving is { } dl && dl < fyEnd ? dl : fyEnd;
                if (effectiveEnd < effectiveStart) continue;

                // Value Basic + DA over the member's last active month in the year.
                var monthStart = new DateOnly(effectiveEnd.Year, effectiveEnd.Month, 1);
                if (computation.ResolveStructureInForce(employee, effectiveEnd) is null) continue;

                IReadOnlyDictionary<Guid, Money>? userDefined = null;
                userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

                var result = computation.Compute(employeeId, monthStart, effectiveEnd, userDefined);
                var actualBasicDa = result.GratuityWages;
                if (actualBasicDa.Amount > StatutoryBonus.EligibilityWageCeiling) continue; // > ₹21,000 ⇒ excluded

                var daysWorked = effectiveEnd.DayNumber - effectiveStart.DayNumber + 1;
                var monthsWorked = (effectiveEnd.Year - effectiveStart.Year) * 12
                                   + (effectiveEnd.Month - effectiveStart.Month) + 1;
                monthsWorked = Math.Clamp(monthsWorked, 0, StatutoryBonus.MonthsPerYear);

                var eligible = StatutoryBonus.IsEligible(actualBasicDa, daysWorked);
                var cappedBase = StatutoryBonus.BonusBaseMonthly(actualBasicDa, config.CalculationCeiling, config.MinimumWage);
                var rate = StatutoryBonus.ClampRate(config.RateBasisPoints);
                var annualBonus = eligible
                    ? StatutoryBonus.AnnualBonus(cappedBase, config.RateBasisPoints, monthsWorked, config.Prorate)
                    : Money.Zero;

                var bonus = WholeRupee(annualBonus);
                rows.Add(new BonusRow(
                    EmployeeName: employee.Name,
                    EmployeeNumber: employee.EmployeeNumber,
                    Eligible: eligible,
                    ActualBasicDa: WholeRupee(actualBasicDa),
                    CappedBase: WholeRupee(cappedBase),
                    RatePercent: rate / 100m,
                    AnnualBonus: bonus));
                total += bonus;
            }
        }

        var ordered = rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        return new BonusRegisterReturn(fyStart, fyEnd, ordered, total);
    }

    /// <summary>The whole-rupee integer of a money field (half-up); bonus figures are whole rupees.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);
}
