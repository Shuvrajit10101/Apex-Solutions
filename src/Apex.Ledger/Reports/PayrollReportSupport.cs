using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One pay head's amount as posted for an employee in a wage month — the pay head that produced it and the
/// Σ of its posted line amounts (paisa-exact). The unit the slice-8 presentation reports read.</summary>
internal readonly record struct PostedPayHeadAmount(Guid PayHeadId, Money Amount);

/// <summary>
/// An employee's payroll <b>as posted</b> for a wage month (Phase 8 slice 8; F1/F2) — the aggregation of the
/// self-describing <see cref="PayrollLineDetail"/> lines carried on the <b>non-cancelled</b> Payroll voucher(s)
/// whose date falls in the wage window. The slice-8 presentation reports (Payslip / Pay Sheet / Payroll Register /
/// Payment Advice) project this rather than recomputing from masters, so every figure reflects <b>what was actually
/// posted</b>: it already carries any As-User-Defined-Value amount (it is in the posted line — F1), it excludes a
/// cancelled or never-posted month (no lines ⇒ no entry — F2), and it reconciles to the voucher by construction.
/// Earnings / deductions / employer contributions are aggregated per pay head in first-posted order; the net is
/// gross − deductions (= the posted Salary-Payable credit).
/// </summary>
internal sealed class PostedPayroll
{
    public required Guid EmployeeId { get; init; }
    public required IReadOnlyList<PostedPayHeadAmount> Earnings { get; init; }
    public required IReadOnlyList<PostedPayHeadAmount> Deductions { get; init; }
    public required IReadOnlyList<PostedPayHeadAmount> EmployerContributions { get; init; }
    public required Money GrossEarnings { get; init; }
    public required Money TotalDeductions { get; init; }
    public required Money EmployerContributionsTotal { get; init; }

    /// <summary>Net payable = gross earnings − total deductions (the posted Cr Salary-Payable amount).</summary>
    public Money NetPayable => GrossEarnings - TotalDeductions;
}

/// <summary>
/// Shared, deterministic helpers for the Phase-8 slice-8 payroll presentation reports (Payslip / Pay Sheet /
/// Payroll Register / Attendance Register / Payment Advice). Pure and culture-invariant — no clock, no RNG.
/// </summary>
internal static class PayrollReportSupport
{
    /// <summary>
    /// Projects, for each requested employee that has any posted payroll in <c>[from, to]</c>, their payroll
    /// <b>as posted</b> — the aggregation of the <see cref="PayrollLineDetail"/> lines carried on the
    /// <b>non-cancelled</b> Payroll voucher(s) whose date falls in the wage month (F1/F2). Deterministic: vouchers
    /// are walked in date-then-id order and lines aggregated per pay head in first-posted order. An employee not in
    /// <paramref name="employeeFilter"/>, or with no posted line in the window, yields no entry — so a cancelled /
    /// never-posted month produces no phantom figures. The establishment-level EPF-admin charge carries no
    /// per-member <see cref="PayrollLineDetail"/>, so it is naturally absent (as with the master recompute).
    /// </summary>
    public static IReadOnlyDictionary<Guid, PostedPayroll> PostedPayrollByEmployee(
        Company company, IEnumerable<Guid> employeeFilter, DateOnly from, DateOnly to)
    {
        var filter = employeeFilter as HashSet<Guid> ?? new HashSet<Guid>(employeeFilter);
        var accumulators = new Dictionary<Guid, Accumulator>();

        foreach (var voucher in company.Vouchers.OrderBy(v => v.Date).ThenBy(v => v.Id))
        {
            if (voucher.Cancelled) continue;                       // a cancelled run paid nothing (F2)
            if (voucher.Date < from || voucher.Date > to) continue; // outside the wage month
            foreach (var line in voucher.Lines)
            {
                if (line.Payroll is not { } pd) continue;          // only Payroll vouchers carry payroll details
                if (!filter.Contains(pd.EmployeeId)) continue;
                if (!accumulators.TryGetValue(pd.EmployeeId, out var acc))
                    accumulators[pd.EmployeeId] = acc = new Accumulator(pd.EmployeeId);
                acc.Add(pd);
            }
        }

        var result = new Dictionary<Guid, PostedPayroll>(accumulators.Count);
        foreach (var (id, acc) in accumulators) result[id] = acc.Build();
        return result;
    }

    /// <summary>The pay head's display label for a posted line, by id — its <see cref="Label(PayHead)"/> when the
    /// head still exists, else a stable placeholder (a referenced head is not deletable, so this is defensive).</summary>
    public static string LabelForPayHead(Company company, Guid payHeadId)
    {
        var payHead = company.FindPayHead(payHeadId);
        return payHead is not null ? Label(payHead) : "(unknown pay head)";
    }

    /// <summary>Accumulates one employee's posted payroll lines per pay head, preserving first-posted order and
    /// counting each employer contribution once (the expense leg; its payable leg mirrors it).</summary>
    private sealed class Accumulator
    {
        private readonly Guid _employeeId;
        private readonly List<Guid> _earnOrder = new();
        private readonly Dictionary<Guid, decimal> _earn = new();
        private readonly List<Guid> _dedOrder = new();
        private readonly Dictionary<Guid, decimal> _ded = new();
        private readonly List<Guid> _erOrder = new();
        private readonly Dictionary<Guid, decimal> _er = new();

        public Accumulator(Guid employeeId) => _employeeId = employeeId;

        public void Add(PayrollLineDetail pd)
        {
            switch (pd.Category)
            {
                case PayrollLineCategory.Earning:
                    Bump(_earn, _earnOrder, pd.PayHeadId!.Value, pd.Amount.Amount);
                    break;
                case PayrollLineCategory.Deduction:
                    Bump(_ded, _dedOrder, pd.PayHeadId!.Value, pd.Amount.Amount);
                    break;
                case PayrollLineCategory.EmployerContributionExpense:
                    // The expense leg carries the contribution once; its payable leg mirrors it, so it is skipped.
                    Bump(_er, _erOrder, pd.PayHeadId!.Value, pd.Amount.Amount);
                    break;
                // EmployerContributionPayable: skipped (mirror of the expense leg).
                // NetPayable: derived (gross − deductions), not accumulated as a pay head.
            }
        }

        private static void Bump(Dictionary<Guid, decimal> map, List<Guid> order, Guid key, decimal amount)
        {
            if (map.TryGetValue(key, out var running)) map[key] = running + amount;
            else { map[key] = amount; order.Add(key); }
        }

        public PostedPayroll Build()
        {
            var earnings = _earnOrder.Select(k => new PostedPayHeadAmount(k, new Money(_earn[k]))).ToList();
            var deductions = _dedOrder.Select(k => new PostedPayHeadAmount(k, new Money(_ded[k]))).ToList();
            var employer = _erOrder.Select(k => new PostedPayHeadAmount(k, new Money(_er[k]))).ToList();
            return new PostedPayroll
            {
                EmployeeId = _employeeId,
                Earnings = earnings,
                Deductions = deductions,
                EmployerContributions = employer,
                GrossEarnings = new Money(earnings.Sum(l => l.Amount.Amount)),
                TotalDeductions = new Money(deductions.Sum(l => l.Amount.Amount)),
                EmployerContributionsTotal = new Money(employer.Sum(l => l.Amount.Amount)),
            };
        }
    }

    /// <summary>The share of an attendance/production entry's <see cref="AttendanceEntry.Value"/> that falls inside
    /// <c>[from, to]</c> (inclusive), pro-rated by the overlapping fraction of the entry's own day-span — the
    /// <b>same clipping</b> the salary-computation engine applies to On-Attendance heads (so a straddling weekly
    /// record contributes its correct share to each period it touches, and the register agrees with the pay). An
    /// entry fully inside the period contributes its whole value; a non-overlapping entry contributes zero.</summary>
    public static decimal ClippedValue(AttendanceEntry e, DateOnly from, DateOnly to)
    {
        var overlapFrom = e.FromDate > from ? e.FromDate : from;
        var overlapTo = e.ToDate < to ? e.ToDate : to;
        if (overlapTo < overlapFrom) return 0m; // no overlap with the period
        var overlapDays = overlapTo.DayNumber - overlapFrom.DayNumber + 1;
        var spanDays = e.ToDate.DayNumber - e.FromDate.DayNumber + 1;
        return overlapDays == spanDays ? e.Value : e.Value * overlapDays / spanDays;
    }

    /// <summary>The pay head's display label — its <see cref="PayHead.DisplayName"/> if set, else its
    /// <see cref="PayHead.Name"/>.</summary>
    public static string Label(PayHead payHead) =>
        string.IsNullOrWhiteSpace(payHead.DisplayName) ? payHead.Name : payHead.DisplayName!.Trim();
}
