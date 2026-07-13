using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>member (detail) row</b> of the EPFO <b>ECR 2.0</b> return (Phase 8 slice 4; catalog §14) — a single
/// employee's PF contribution for the wage month, read off the deterministic PF computation. Every money field is
/// a <b>whole rupee integer</b> (the ECR carries no paisa); the field order mirrors the ECR 2.0 layout:
/// UAN · name · gross wages · EPF wages · EPS wages · EDLI wages · employee EPF share · EPS contribution ·
/// employer EPF share · NCP days · refund of advances.
/// </summary>
public sealed record PfEcrMember(
    string Uan,
    string Name,
    long GrossWages,
    long EpfWages,
    long EpsWages,
    long EdliWages,
    long EmployeeShareEpf,
    long EpsContribution,
    long EmployerShareEpf,
    int NcpDays,
    long RefundOfAdvances);

/// <summary>
/// The establishment <b>challan account-head totals</b> of an ECR return (Phase 8 slice 4): A/c 1 (total EPF =
/// Σ employee + employer EPF share), A/c 2 (EPF admin charge), A/c 10 (Σ EPS), A/c 21 (Σ EDLI), A/c 22 (EDLI
/// admin — NIL). Whole rupees. A/c 1/10/21 are summed from the member rows; A/c 2 is the once-per-challan
/// establishment charge (floored).
/// </summary>
public sealed record PfChallanTotals(long Account1, long Account2, long Account10, long Account21, long Account22);

/// <summary>
/// A complete <b>ECR 2.0</b> return projection (Phase 8 slice 4): the establishment code + wage month, the ordered
/// member <see cref="Members"/> and the <see cref="Totals"/>. Deterministic and byte-stable — built by
/// <see cref="PfEcr.Build"/> from the same pure PF computation the payroll voucher posts, then serialised by the
/// hand-rolled ECR writer in <c>Apex.Ledger.Io</c>.
/// </summary>
public sealed record PfEcrReturn(
    string? EstablishmentCode,
    DateOnly WageMonth,
    IReadOnlyList<PfEcrMember> Members,
    PfChallanTotals Totals);

/// <summary>
/// Builds an <see cref="PfEcrReturn"/> for a wage month (Phase 8 slice 4; RQ-9) — a <b>pure, deterministic</b>
/// projection over the company masters + posted attendance, computed exactly as the payroll voucher posts (the
/// same <see cref="PayrollComputationService"/> + <see cref="PfContribution"/>), so the ECR reconciles to the
/// books by construction. One member row per PF-applicable employee; the challan A/c 2 admin charge is the
/// establishment aggregate (floored once).
/// </summary>
public static class PfEcr
{
    /// <summary>
    /// Builds the ECR return for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>. Only
    /// PF-applicable employees (with a valid UAN) yield a member row; a member with positive PF wages is
    /// contributory (feeds the A/c 2 aggregate). <paramref name="ncpDaysByEmployee"/> optionally supplies each
    /// member's Non-Contributory-Period (loss-of-pay) days (default 0). Member rows are ordered by UAN then name
    /// so the file is byte-stable regardless of input order.
    /// </summary>
    public static PfEcrReturn Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyDictionary<Guid, int>? ncpDaysByEmployee = null,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var computation = new PayrollComputationService(company);
        var epfRateBp = company.PfConfig?.EpfRateBasisPoints ?? PfContribution.DefaultEpfRateBasisPoints;

        var members = new List<PfEcrMember>();
        var contributoryEpfWages = new List<Money>();
        long ac1 = 0, ac10 = 0, ac21 = 0;

        foreach (var employeeId in employeeIds)
        {
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");
            if (!employee.PfApplicable) continue; // only PF members appear on the ECR
            // The ECR keys the member on a 12-digit UAN. A blank OR malformed value (only reachable via an imported /
            // hand-edited company — the domain guard rejects it) would emit an EPFO-invalid line, so reject it here.
            if (!IsValidUan(employee.Uan))
                throw new InvalidOperationException(
                    $"PF-applicable employee '{employee.Name}' has no valid 12-digit UAN; the ECR keys the member on it.");

            IReadOnlyDictionary<Guid, Money>? userDefined = null;
            userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

            var result = computation.Compute(employeeId, periodFrom, periodTo, userDefined);
            var pfWages = result.PfWages.Amount;
            var onHigherWages = PfContribution.ContributesOnHigherWages(
                employee.PfContributeOnHigherWages, company.PfConfig?.CapWagesAtCeiling ?? true);
            var contribution = PfContribution.ComputeMember(pfWages, onHigherWages, epfRateBp);

            var ncp = ncpDaysByEmployee is not null && ncpDaysByEmployee.TryGetValue(employeeId, out var n) ? n : 0;

            members.Add(new PfEcrMember(
                Uan: employee.Uan!.Trim(),
                Name: employee.Name,
                GrossWages: WholeRupee(result.GrossEarnings),
                EpfWages: WholeRupee(contribution.EpfWages),
                EpsWages: WholeRupee(contribution.EpsWages),
                EdliWages: WholeRupee(contribution.EdliWages),
                EmployeeShareEpf: WholeRupee(contribution.EmployeeEpf),
                EpsContribution: WholeRupee(contribution.EmployerPension),
                EmployerShareEpf: WholeRupee(contribution.EmployerEpf),
                NcpDays: ncp,
                RefundOfAdvances: 0));

            ac1 += WholeRupee(contribution.EmployeeEpf) + WholeRupee(contribution.EmployerEpf);
            ac10 += WholeRupee(contribution.EmployerPension);
            ac21 += WholeRupee(contribution.Edli);
            if (pfWages > 0m) contributoryEpfWages.Add(contribution.EpfWages);
        }

        var ordered = members
            .OrderBy(m => m.Uan, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var ac2 = WholeRupee(PfContribution.ComputeAdminCharge(contributoryEpfWages));
        var totals = new PfChallanTotals(Account1: ac1, Account2: ac2, Account10: ac10, Account21: ac21, Account22: 0);

        return new PfEcrReturn(company.PfConfig?.EstablishmentCode, periodTo, ordered, totals);
    }

    /// <summary>The whole-rupee integer for an ECR money field (half-up), since the ECR carries no paisa. The PF
    /// engine already produces whole-rupee contributions; the wage bases are whole rupees in practice.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Whether <paramref name="uan"/> is a valid 12-digit UAN (<c>^\d{12}$</c>) — the same rule the domain
    /// enforces at the master-save boundary (<c>PayrollService.SetEmployeePfDetails</c>).</summary>
    private static bool IsValidUan(string? uan)
    {
        if (uan is null) return false;
        var trimmed = uan.Trim();
        if (trimmed.Length != 12) return false;
        foreach (var ch in trimmed) if (ch is < '0' or > '9') return false;
        return true;
    }
}
