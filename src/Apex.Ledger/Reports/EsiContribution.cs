using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>Insured-Person (IP) row</b> of the ESIC <b>monthly contribution</b> file (Phase 8 slice 5; catalog §14) —
/// a single covered employee's ESI figures for the wage month, read off the deterministic ESI computation. The
/// field order mirrors the ESIC monthly-contribution layout: IP Number (10 digits) · IP Name · No. of Days ·
/// Total Monthly Wages (the ESI contribution base) · Reason for 0 wages (only when days/wages are 0) · Last Working
/// Day (only on exit). Wages are whole rupees (the file carries no paisa).
/// </summary>
public sealed record EsiContributionRow(
    string IpNumber,
    string IpName,
    int NoOfDays,
    long TotalMonthlyWages,
    string? ReasonForZeroWages,
    string? LastWorkingDay);

/// <summary>
/// A complete ESIC <b>monthly contribution</b> return projection (Phase 8 slice 5): the establishment employer code
/// + wage month and the ordered IP <see cref="Rows"/>. Deterministic and byte-stable — built by
/// <see cref="EsiMonthlyContribution.Build"/> from the same pure ESI computation the payroll voucher posts, then
/// serialised by the hand-rolled writer in <c>Apex.Ledger.Io</c>.
/// </summary>
public sealed record EsiContributionReturn(
    string? EmployerCode,
    DateOnly WageMonth,
    IReadOnlyList<EsiContributionRow> Rows);

/// <summary>
/// Builds an <see cref="EsiContributionReturn"/> for a wage month (Phase 8 slice 5; RQ-9) — a <b>pure,
/// deterministic</b> projection over the company masters + posted attendance, computed exactly as the payroll
/// voucher posts (the same <see cref="PayrollComputationService"/> coverage decision + ESI contribution base), so
/// the file reconciles to the books by construction. One IP row per ESI-applicable employee (with a valid 10-digit
/// IP number).
/// </summary>
public static class EsiMonthlyContribution
{
    /// <summary>The default reason emitted for an IP with zero ESI wages this month (out of coverage / no pay).</summary>
    public const string DefaultZeroWageReason = "Out of Coverage";

    /// <summary>
    /// Builds the ESI monthly-contribution return for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>.
    /// Only ESI-applicable employees yield a row; a member is charged on their <b>actual</b> ESI wages (no ₹21,000
    /// cap) when covered for the period, else 0 wages with a reason. <paramref name="paidDaysByEmployee"/> optionally
    /// supplies each member's paid days (fractions rounded <b>up</b>); when absent the calendar days of the period
    /// are used for a covered member and 0 for an uncovered one. Rows are ordered by IP number then name so the file
    /// is byte-stable regardless of input order. Throws if a member's IP number is not a valid 10-digit value.
    /// </summary>
    public static EsiContributionReturn Build(
        Company company,
        IReadOnlyList<Guid> employeeIds,
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyDictionary<Guid, decimal>? paidDaysByEmployee = null,
        IReadOnlyDictionary<Guid, string>? reasonByEmployee = null,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var computation = new PayrollComputationService(company);
        var periodDays = periodTo.DayNumber - periodFrom.DayNumber + 1;

        var rows = new List<EsiContributionRow>();
        foreach (var employeeId in employeeIds)
        {
            var employee = company.FindEmployee(employeeId)
                ?? throw new InvalidOperationException($"Employee {employeeId} not found.");
            if (!employee.EsiApplicable) continue; // only ESI members appear on the monthly file
            // The file keys the member on a 10-digit IP number. A blank OR malformed value (only reachable via an
            // imported / hand-edited company — the domain guard rejects it) would emit an ESIC-invalid line, so reject.
            if (!IsValidIpNumber(employee.EsiNumber))
                throw new InvalidOperationException(
                    $"ESI-applicable employee '{employee.Name}' has no valid 10-digit IP number; the monthly file keys the member on it.");

            IReadOnlyDictionary<Guid, Money>? userDefined = null;
            userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

            var result = computation.Compute(employeeId, periodFrom, periodTo, userDefined);
            var covered = computation.IsEsiCovered(employeeId, periodTo);
            var wages = covered ? WholeRupee(result.EsiContributionWages) : 0L;

            int days;
            if (paidDaysByEmployee is not null && paidDaysByEmployee.TryGetValue(employeeId, out var d))
                days = (int)Math.Ceiling(d);                 // fractions round UP
            else
                days = wages > 0L ? periodDays : 0;

            string? reason = wages > 0L
                ? null
                : (reasonByEmployee is not null && reasonByEmployee.TryGetValue(employeeId, out var r) && !string.IsNullOrWhiteSpace(r)
                    ? r.Trim()
                    : DefaultZeroWageReason);

            string? lastWorkingDay = employee.DateOfLeaving is { } lv
                ? lv.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : null;

            rows.Add(new EsiContributionRow(
                IpNumber: employee.EsiNumber!.Trim(),
                IpName: employee.Name,
                NoOfDays: days,
                TotalMonthlyWages: wages,
                ReasonForZeroWages: reason,
                LastWorkingDay: lastWorkingDay));
        }

        var ordered = rows
            .OrderBy(r => r.IpNumber, StringComparer.Ordinal)
            .ThenBy(r => r.IpName, StringComparer.Ordinal)
            .ToList();

        return new EsiContributionReturn(company.EsiConfig?.EmployerCode, periodTo, ordered);
    }

    /// <summary>The whole-rupee integer for an ESI money field (half-up), since the file carries no paisa. The ESI
    /// wage base is whole rupees in practice.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Whether <paramref name="ip"/> is a valid 10-digit IP number (<c>^\d{10}$</c>) — the same rule the
    /// domain enforces at the master-save boundary (<c>PayrollService</c>).</summary>
    private static bool IsValidIpNumber(string? ip)
    {
        if (ip is null) return false;
        var trimmed = ip.Trim();
        if (trimmed.Length != 10) return false;
        foreach (var ch in trimmed) if (ch is < '0' or > '9') return false;
        return true;
    }
}
