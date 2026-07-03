using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The nature of an outstanding line: money the company is owed vs money it owes
/// (catalog §5 Outstandings → Receivables / Payables).
/// </summary>
public enum OutstandingKind
{
    /// <summary>Owed to the company (debit-nature party — Sundry Debtors). A Receivable.</summary>
    Receivable,

    /// <summary>Owed by the company (credit-nature party — Sundry Creditors). A Payable.</summary>
    Payable,
}

/// <summary>
/// One open bill in the Outstandings projection (catalog §5). A bill is opened by a
/// New-Ref/Advance allocation and reduced ("knocked off") by later Agst-Ref allocations on the
/// same party ledger and reference name; <see cref="Pending"/> is the still-unsettled magnitude.
/// </summary>
public sealed record OutstandingBill(
    Guid LedgerId,
    string LedgerName,
    string Reference,
    BillRefType OpenedAs,
    DateOnly Date,
    DateOnly DueDate,
    Money Original,
    Money Pending,
    OutstandingKind Kind)
{
    /// <summary>
    /// Days overdue as of a report date: <c>asOf − DueDate</c>, floored at 0 (not yet due ⇒ 0).
    /// </summary>
    public int OverdueDays(DateOnly asOf)
    {
        var days = asOf.DayNumber - DueDate.DayNumber;
        return days > 0 ? days : 0;
    }
}

/// <summary>
/// An ageing bucket (overdue-days range) and the pending total that falls in it.
/// The last bucket is open-ended (<see cref="UpperInclusive"/> = null).
/// </summary>
public sealed record AgeingBucket(string Label, int LowerInclusive, int? UpperInclusive, Money Pending);

/// <summary>
/// The whole Outstandings result as of a date (catalog §5): the open bills split into
/// <see cref="Receivables"/> and <see cref="Payables"/>, plus simple ageing buckets over each.
/// </summary>
public sealed record OutstandingsReport(
    DateOnly AsOf,
    IReadOnlyList<OutstandingBill> Receivables,
    IReadOnlyList<OutstandingBill> Payables,
    IReadOnlyList<AgeingBucket> ReceivableAgeing,
    IReadOnlyList<AgeingBucket> PayableAgeing)
{
    /// <summary>Σ pending across all receivable bills.</summary>
    public Money TotalReceivable => Sum(Receivables);

    /// <summary>Σ pending across all payable bills.</summary>
    public Money TotalPayable => Sum(Payables);

    private static Money Sum(IReadOnlyList<OutstandingBill> bills)
    {
        var s = 0m;
        foreach (var b in bills) s += b.Pending.Amount;
        return new Money(s);
    }
}

/// <summary>
/// Pure bill-wise Outstandings projection over the posted voucher set (catalog §5; plan.md §5).
/// No UI, no DB. For each bill-by-bill ledger it accumulates allocations by reference name,
/// nets New/Advance opens against Agst knock-offs, keeps the bills with a non-zero pending, and
/// classifies each ledger as a Receivable (debit-nature / Sundry Debtors) or a Payable
/// (credit-nature / Sundry Creditors) by its group's primary nature and closing sign.
/// </summary>
public static class Outstandings
{
    /// <summary>Default ageing bucket edges (upper-inclusive day counts); the tail is open-ended.</summary>
    public static readonly IReadOnlyList<(string Label, int Lower, int? Upper)> DefaultBuckets = new[]
    {
        ("Not due", int.MinValue, 0),
        ("0-30 days", 1, 30),
        ("31-60 days", 31, 60),
        ("61-90 days", 61, 90),
        ("90+ days", 91, (int?)null),
    };

    /// <summary>Builds the Outstandings projection for the whole company as of <paramref name="asOf"/>.</summary>
    public static OutstandingsReport Build(Company company, DateOnly asOf)
    {
        var receivables = new List<OutstandingBill>();
        var payables = new List<OutstandingBill>();

        foreach (var ledger in company.Ledgers)
        {
            if (!ledger.MaintainBillByBill) continue;
            var bills = OpenBillsFor(company, ledger, asOf);
            foreach (var bill in bills)
            {
                if (bill.Kind == OutstandingKind.Receivable) receivables.Add(bill);
                else payables.Add(bill);
            }
        }

        return new OutstandingsReport(
            asOf,
            receivables,
            payables,
            AgeingOf(receivables, asOf),
            AgeingOf(payables, asOf));
    }

    /// <summary>
    /// The open bills for a single bill-by-bill ledger as of <paramref name="asOf"/> — the
    /// building block the UI Outstandings/Ctrl+B screen binds to. Bills fully knocked off (pending
    /// ≤ 0) are excluded; the remainder are returned in first-opened order.
    /// </summary>
    public static IReadOnlyList<OutstandingBill> OpenBillsFor(Company company, Domain.Ledger ledger, DateOnly asOf)
    {
        var kind = KindOf(company, ledger);

        // Accumulate per reference name, preserving first-seen order.
        var order = new List<string>();
        var acc = new Dictionary<string, BillState>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, asOf)) continue;
            foreach (var line in v.Lines)
            {
                if (line.LedgerId != ledger.Id || !line.HasBillAllocations) continue;
                foreach (var a in line.BillAllocations)
                {
                    // On-Account is unallocated/suspense — it never opens or settles a named bill.
                    if (a.RefType == BillRefType.OnAccount) continue;

                    var key = a.Name;
                    if (!acc.TryGetValue(key, out var state))
                    {
                        state = new BillState
                        {
                            Reference = a.Name,
                            OpenedAs = a.RefType,
                            Date = v.Date,
                            DueDate = a.EffectiveDueDate(v.Date, ledger.DefaultCreditPeriodDays),
                        };
                        acc[key] = state;
                        order.Add(key);
                    }

                    // Signed contribution toward the bill's OWN natural side, apportioned to THIS
                    // allocation's amount (a split line carries several allocations, each toward a
                    // different bill). For a receivable ledger a debit (invoice) increases the bill and
                    // a credit (receipt) reduces it; for a payable ledger the natural side is credit so
                    // the sign flips. New/Advance open, Agst settles — but we net purely by the
                    // accounting sign so over-settlement / re-open behave correctly.
                    var allocSigned = line.Side == DrCr.Debit ? a.Amount.Amount : -a.Amount.Amount;
                    var signedTowardBill = kind == OutstandingKind.Receivable ? allocSigned : -allocSigned;
                    state.Pending += signedTowardBill;

                    if (a.RefType is BillRefType.NewRef or BillRefType.Advance)
                    {
                        state.Original += a.Amount.Amount;
                        // Prefer the opening allocation's own date/due for the bill's identity.
                        state.Date = v.Date;
                        state.DueDate = a.EffectiveDueDate(v.Date, ledger.DefaultCreditPeriodDays);
                        state.OpenedAs = a.RefType;
                    }
                }
            }
        }

        var result = new List<OutstandingBill>();
        foreach (var key in order)
        {
            var s = acc[key];
            if (s.Pending <= 0m) continue; // fully settled (or net-advance already consumed)
            result.Add(new OutstandingBill(
                ledger.Id,
                ledger.Name,
                s.Reference,
                s.OpenedAs,
                s.Date,
                s.DueDate,
                new Money(s.Original == 0m ? s.Pending : s.Original),
                new Money(s.Pending),
                kind));
        }
        return result;
    }

    /// <summary>
    /// Classifies a bill-by-bill ledger as a Receivable or Payable. Sundry-Debtors-style
    /// (asset/debit-nature) parties are receivables; Sundry-Creditors-style (liability/credit)
    /// parties are payables. Falls back to the group's primary nature so the rule is rename-safe.
    /// </summary>
    public static OutstandingKind KindOf(Company company, Domain.Ledger ledger)
    {
        var group = company.FindGroup(ledger.GroupId)
            ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");
        var nature = ClassificationRules.PrimaryNatureOf(group, company);
        return nature == GroupNature.Liability || nature == GroupNature.Income
            ? OutstandingKind.Payable
            : OutstandingKind.Receivable;
    }

    private static IReadOnlyList<AgeingBucket> AgeingOf(IReadOnlyList<OutstandingBill> bills, DateOnly asOf)
    {
        var totals = new decimal[DefaultBuckets.Count];
        foreach (var b in bills)
        {
            var overdue = b.OverdueDays(asOf);
            var idx = BucketIndex(overdue);
            totals[idx] += b.Pending.Amount;
        }

        var result = new List<AgeingBucket>(DefaultBuckets.Count);
        for (var i = 0; i < DefaultBuckets.Count; i++)
        {
            var (label, lower, upper) = DefaultBuckets[i];
            result.Add(new AgeingBucket(label, lower, upper, new Money(totals[i])));
        }
        return result;
    }

    /// <summary>Index of the ageing bucket an overdue-day count falls in.</summary>
    public static int BucketIndex(int overdueDays)
    {
        for (var i = 0; i < DefaultBuckets.Count; i++)
        {
            var (_, lower, upper) = DefaultBuckets[i];
            var lo = lower == int.MinValue ? int.MinValue : lower;
            if (overdueDays >= lo && (upper is null || overdueDays <= upper.Value))
                return i;
        }
        return DefaultBuckets.Count - 1;
    }

    private sealed class BillState
    {
        public string Reference = string.Empty;
        public BillRefType OpenedAs;
        public DateOnly Date;
        public DateOnly DueDate;
        public decimal Original;
        public decimal Pending;
    }
}
