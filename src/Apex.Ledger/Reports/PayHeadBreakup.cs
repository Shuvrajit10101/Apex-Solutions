using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>Which way round a <see cref="PayHeadBreakup"/> is transposed.</summary>
public enum PayHeadBreakupOrientation
{
    /// <summary>Census 7.23 — <b>one employee</b>, broken up across the pay heads posted for them, grouped by the
    /// pay head's accounting group.</summary>
    ByPayHeadForOneEmployee = 0,

    /// <summary>Census 7.24 — <b>one pay head</b>, broken up across the employees it was posted for, grouped by the
    /// employee's employee group.</summary>
    ByEmployeeForOnePayHead = 1,
}

/// <summary>
/// One line of a <see cref="PayHeadBreakup"/> — a pay head (7.23) or an employee (7.24) with its opening balance,
/// the period's debits and credits, and its closing balance.
/// </summary>
/// <param name="Key">The pay head id (7.23) or employee id (7.24) this line is for.</param>
/// <param name="Name">The pay head's display label (7.23) or the employee's name (7.24).</param>
/// <param name="Opening">Balance before <see cref="PayHeadBreakup.PeriodFrom"/>, <b>signed debit-positive</b>.</param>
/// <param name="Debit">Σ debits posted inside the period (always ≥ 0).</param>
/// <param name="Credit">Σ credits posted inside the period (always ≥ 0).</param>
/// <param name="Closing"><c>Opening + Debit − Credit</c>, signed debit-positive.</param>
public sealed record PayHeadBreakupLine(
    Guid Key,
    string Name,
    decimal Opening,
    decimal Debit,
    decimal Credit,
    decimal Closing);

/// <summary>One group band of a <see cref="PayHeadBreakup"/> with its own subtotals — the accounting group of the
/// pay head's ledger (7.23) or the employee's employee group (7.24).</summary>
public sealed record PayHeadBreakupGroup(
    string GroupName,
    IReadOnlyList<PayHeadBreakupLine> Lines,
    decimal Opening,
    decimal Debit,
    decimal Credit,
    decimal Closing);

/// <summary>
/// The shared engine behind the vendor's two transposed payroll breakup reports (census rows 7.23 and 7.24) —
/// <b>Pay Head Employee Breakup</b> (help.tallysolutions.com/tally-prime/payroll-reports/pay-head-employee-breakup-tally/,
/// <i>"a group-wise summary of transactions along with the closing balance for the selected employee"</i>) and
/// <b>Employee Pay Head Breakup</b>
/// (help.tallysolutions.com/tally-prime/payroll-reports/payroll-employee-pay-head-breakup-tally/,
/// <i>"opening balance, credit/debit transactions and closing balance for the selected period"</i> for a chosen
/// pay head).
///
/// <para>🔴 <b>TWO REPORTS, ONE ENGINE, AND THE CENSUS SAYS WHY BOTH EXIST.</b> They transpose the SAME posted
/// data — one employee × many pay heads, versus one pay head × many employees — and the vendor gives each its own
/// page and its own Go-To entry. They share this engine so their figures can never disagree, and they are built as
/// two <see cref="PayHeadBreakupOrientation"/> values so neither can silently answer for the other.</para>
///
/// <para><b>Which posting is "the pay head".</b> A pay head owns exactly one ledger of its own —
/// <see cref="PayHead.LedgerId"/>: the expense ledger for an earning, the payable ledger for a deduction, and the
/// payable ledger for an employer contribution. An employer contribution ALSO posts a mirroring debit to
/// <see cref="PayHead.EmployerExpenseLedgerId"/>, a <b>different</b> ledger. Including both legs under one pay head
/// would net every employer contribution to a closing balance of zero — a figure that is arithmetically tidy and
/// factually wrong. So this engine reads the pay head's OWN ledger only, which is exactly
/// <see cref="PayrollLineCategory.Earning"/>, <see cref="PayrollLineCategory.Deduction"/> and
/// <see cref="PayrollLineCategory.EmployerContributionPayable"/>; the expense mirror is excluded and named in
/// <see cref="ExcludedLegNote"/> so the exclusion is visible on the report rather than buried here.</para>
///
/// <para><b>Balances.</b> Signed <b>debit-positive</b> throughout. <see cref="PayHeadBreakupLine.Opening"/> is the
/// cumulative Dr − Cr of every posted, non-cancelled payroll line <b>strictly before</b> <see cref="PeriodFrom"/>;
/// there is no separate opening-balance master for an (employee, pay head) pair in this book, so nothing is read
/// from one. A pure, deterministic, culture-invariant projection — no clock, no RNG.</para>
/// </summary>
public sealed record PayHeadBreakup(
    PayHeadBreakupOrientation Orientation,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    Guid ScopeId,
    string ScopeName,
    IReadOnlyList<PayHeadBreakupGroup> Groups,
    decimal TotalOpening,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal TotalClosing)
{
    /// <summary>The note every caller prints beneath the grid, so the excluded mirror leg is never a silent
    /// omission. Stated once here so both reports say the same thing.</summary>
    public const string ExcludedLegNote =
        "Figures are the pay head's own ledger. An employer contribution posts a mirroring debit to a separate "
        + "employer-expense ledger; that mirror leg is excluded here, because including it would net every "
        + "employer contribution to a closing balance of zero.";

    /// <summary>The caption for a line that has no group of its own.</summary>
    public const string UngroupedName = "(ungrouped)";

    /// <summary>True when nothing at all was posted in or before the period for this scope.</summary>
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>
    /// Census 7.23 — the breakup of <b>one employee</b> across the pay heads posted for them, grouped by the
    /// accounting group of each pay head's own ledger.
    /// </summary>
    public static PayHeadBreakup ForEmployee(Company company, Guid employeeId, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);
        var employee = company.FindEmployee(employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        var buckets = Collect(company, to, pd => pd.EmployeeId == employeeId, pd => pd.PayHeadId!.Value);

        var groups = BuildGroups(
            buckets,
            key => company.FindPayHead(key) is { } ph ? PayrollReportSupport.Label(ph) : "(unknown pay head)",
            key => AccountingGroupName(company, key),
            from);

        return Finish(PayHeadBreakupOrientation.ByPayHeadForOneEmployee, from, to, employeeId, employee.Name, groups);
    }

    /// <summary>
    /// Census 7.24 — the breakup of <b>one pay head</b> across the employees it was posted for, grouped by each
    /// employee's employee group.
    /// </summary>
    public static PayHeadBreakup ForPayHead(Company company, Guid payHeadId, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);
        var payHead = company.FindPayHead(payHeadId)
            ?? throw new InvalidOperationException($"Pay head {payHeadId} not found.");

        var buckets = Collect(company, to, pd => pd.PayHeadId == payHeadId, pd => pd.EmployeeId);

        var groups = BuildGroups(
            buckets,
            key => company.FindEmployee(key) is { } e ? e.Name : "(unknown employee)",
            key => company.FindEmployee(key) is { } e && company.FindEmployeeGroup(e.EmployeeGroupId) is { } g
                ? g.Name
                : UngroupedName,
            from);

        return Finish(
            PayHeadBreakupOrientation.ByEmployeeForOnePayHead, from, to, payHeadId,
            PayrollReportSupport.Label(payHead), groups);
    }

    /// <summary>The accounting group of the pay head's own ledger — the group band 7.23 nests under. Falls back to
    /// the head's configured <see cref="PayHead.UnderGroupId"/> when no ledger has been created yet (a pay head
    /// that has never posted), and to <see cref="UngroupedName"/> when neither resolves.</summary>
    private static string AccountingGroupName(Company company, Guid payHeadId)
    {
        if (company.FindPayHead(payHeadId) is not { } payHead) return UngroupedName;
        if (payHead.LedgerId is { } ledgerId
            && company.FindLedger(ledgerId) is { } ledger
            && company.FindGroup(ledger.GroupId) is { } ledgerGroup)
            return ledgerGroup.Name;
        if (payHead.UnderGroupId is { } underId && company.FindGroup(underId) is { } underGroup)
            return underGroup.Name;
        return UngroupedName;
    }

    /// <summary>
    /// Walks the posted, non-cancelled payroll lines up to <paramref name="to"/> and buckets each one by
    /// <paramref name="keyOf"/>, keeping the running (before-period, in-period-Dr, in-period-Cr) triple. Only the
    /// pay head's OWN ledger legs are counted — see the type doc.
    /// </summary>
    private static Dictionary<Guid, Bucket> Collect(
        Company company,
        DateOnly to,
        Func<PayrollLineDetail, bool> matches,
        Func<PayrollLineDetail, Guid> keyOf)
    {
        var buckets = new Dictionary<Guid, Bucket>();
        foreach (var voucher in company.Vouchers.OrderBy(v => v.Date).ThenBy(v => v.Id))
        {
            if (voucher.Cancelled) continue;
            if (voucher.Date > to) continue;                   // nothing after the period contributes
            foreach (var line in voucher.Lines)
            {
                if (line.Payroll is not { } pd) continue;
                if (pd.PayHeadId is null) continue;            // the net Salary-Payable residual is not a pay head
                if (pd.Category is not (PayrollLineCategory.Earning
                    or PayrollLineCategory.Deduction
                    or PayrollLineCategory.EmployerContributionPayable)) continue;
                if (!matches(pd)) continue;

                var key = keyOf(pd);
                if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new Bucket();
                bucket.Add(voucher.Date, line.Side, pd.Amount.Amount);
            }
        }
        return buckets;
    }

    /// <summary>Turns the buckets into named, group-banded, subtotalled lines. Groups and lines are both ordered
    /// by name (ordinal) then key, so the report is byte-stable regardless of posting order.</summary>
    private static List<PayHeadBreakupGroup> BuildGroups(
        Dictionary<Guid, Bucket> buckets,
        Func<Guid, string> nameOf,
        Func<Guid, string> groupOf,
        DateOnly from)
    {
        var byGroup = new Dictionary<string, List<PayHeadBreakupLine>>(StringComparer.Ordinal);
        foreach (var (key, bucket) in buckets)
        {
            var (opening, debit, credit) = bucket.Split(from);
            var line = new PayHeadBreakupLine(key, nameOf(key), opening, debit, credit, opening + debit - credit);
            var groupName = groupOf(key);
            if (!byGroup.TryGetValue(groupName, out var lines)) byGroup[groupName] = lines = new List<PayHeadBreakupLine>();
            lines.Add(line);
        }

        var groups = new List<PayHeadBreakupGroup>();
        foreach (var groupName in byGroup.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            var lines = byGroup[groupName]
                .OrderBy(l => l.Name, StringComparer.Ordinal)
                .ThenBy(l => l.Key)
                .ToList();
            groups.Add(new PayHeadBreakupGroup(
                groupName,
                lines,
                lines.Sum(l => l.Opening),
                lines.Sum(l => l.Debit),
                lines.Sum(l => l.Credit),
                lines.Sum(l => l.Closing)));
        }
        return groups;
    }

    private static PayHeadBreakup Finish(
        PayHeadBreakupOrientation orientation,
        DateOnly from,
        DateOnly to,
        Guid scopeId,
        string scopeName,
        List<PayHeadBreakupGroup> groups)
        => new(orientation, from, to, scopeId, scopeName, groups,
            groups.Sum(g => g.Opening),
            groups.Sum(g => g.Debit),
            groups.Sum(g => g.Credit),
            groups.Sum(g => g.Closing));

    /// <summary>The running (before-period, in-period) split for one key. Debit-positive.</summary>
    private sealed class Bucket
    {
        private readonly List<(DateOnly Date, decimal Signed)> _postings = new();

        public void Add(DateOnly date, DrCr side, decimal amount)
            => _postings.Add((date, side == DrCr.Debit ? amount : -amount));

        /// <summary>(opening, debit, credit) — opening is the signed cumulative before <paramref name="from"/>;
        /// debit and credit are the unsigned in-period sums.</summary>
        public (decimal Opening, decimal Debit, decimal Credit) Split(DateOnly from)
        {
            decimal opening = 0m, debit = 0m, credit = 0m;
            foreach (var (date, signed) in _postings)
            {
                if (date < from) { opening += signed; continue; }
                if (signed >= 0m) debit += signed; else credit += -signed;
            }
            return (opening, debit, credit);
        }
    }
}
