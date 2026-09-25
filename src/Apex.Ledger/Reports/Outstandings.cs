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

    /// <summary>
    /// Whether this bill has <b>fallen due on or before</b> <paramref name="asOf"/> — the "due till today"
    /// test behind <see cref="OutstandingsReport.ReceivableDueTillToday"/> /
    /// <see cref="OutstandingsReport.PayableDueTillToday"/>.
    ///
    /// <para>🔴 <b>This is NOT the complement of "Not due" in the ageing buckets, and the difference is one
    /// day.</b> <see cref="OverdueDays"/> floors at 0, so a bill falling due exactly on <paramref name="asOf"/>
    /// scores 0 overdue days and lands in the <c>"Not due"</c> bucket — correct, because it is not yet
    /// <i>overdue</i>. It has still <i>fallen due</i> today, so it counts here. Ageing asks "how late is it";
    /// this asks "has its date arrived".</para>
    /// </summary>
    public bool IsDueBy(DateOnly asOf) => DueDate.DayNumber <= asOf.DayNumber;
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

    /// <summary>Σ pending across the receivable bills that have fallen due on or before <see cref="AsOf"/>.</summary>
    public Money ReceivableDueTillToday => SumDue(Receivables, AsOf);

    /// <summary>Σ pending across the payable bills that have fallen due on or before <see cref="AsOf"/>.</summary>
    public Money PayableDueTillToday => SumDue(Payables, AsOf);

    private static Money SumDue(IReadOnlyList<OutstandingBill> bills, DateOnly asOf)
    {
        var s = 0m;
        foreach (var b in bills)
            if (b.IsDueBy(asOf)) s += b.Pending.Amount;
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

    /// <summary>
    /// Builds the Outstandings projection for the whole company as of <paramref name="asOf"/>, optionally
    /// under a <paramref name="scenario"/> (catalog §7). Passing <c>null</c> reproduces the actual books
    /// exactly, so a report builder can thread its <c>options.Scenario</c> through unconditionally — the same
    /// optional-trailing-parameter shape <see cref="BalanceSheet"/> and
    /// <see cref="LedgerBalances.SignedClosing(Company, Domain.Ledger, DateOnly, Scenario?)"/> already use.
    /// </summary>
    public static OutstandingsReport Build(Company company, DateOnly asOf, Scenario? scenario = null)
    {
        // 🔴 ONE voucher pass for the WHOLE company. This used to call OpenBillsFor per bill-wise ledger, and
        // each of those calls walked every voucher — so a report that merely opens this projection paid
        // O(parties × vouchers × lines × allocations). Accumulate takes the whole ledger set at once.
        // MEASURED, not estimated (Release, this machine): 100 parties × 2,000 vouchers 14.2 ms → 1.8 ms;
        // 200 parties × 4,000 vouchers 50.9 ms → 2.9 ms. The old shape grew with the PRODUCT (3.6× for a 4×
        // product), the new one grows with the vouchers alone (1.6×), so the gap widens with book size.
        // The emission below still walks company.Ledgers in order, and each ledger's own first-opened order,
        // so the row sequence is identical to the per-ledger loop this replaced — pinned by
        // Outstandings_build_emits_exactly_the_per_ledger_projection_in_the_same_order, which spells the
        // expected sequence out independently because comparing the two code paths shares this emitter.
        var billWise = new List<Domain.Ledger>();
        foreach (var ledger in company.Ledgers)
            if (ledger.MaintainBillByBill) billWise.Add(ledger);

        var sets = Accumulate(company, billWise, asOf, scenario);

        var receivables = new List<OutstandingBill>();
        var payables = new List<OutstandingBill>();
        foreach (var ledger in billWise)
        {
            foreach (var bill in BillsOf(ledger, sets[ledger.Id], KindOf(company, ledger)))
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
    /// ≤ 0) are excluded; the remainder are returned in first-opened order. Optionally projected under
    /// a <paramref name="scenario"/>; <c>null</c> means the actual books.
    /// <para>The ledger's own <see cref="Domain.Ledger.MaintainBillByBill"/> flag is deliberately NOT
    /// consulted here — callers ask about a specific ledger and get whatever allocations it carries.
    /// <see cref="Build"/> is the one that selects the bill-wise ledgers.</para>
    /// </summary>
    public static IReadOnlyList<OutstandingBill> OpenBillsFor(
        Company company, Domain.Ledger ledger, DateOnly asOf, Scenario? scenario = null)
    {
        var sets = Accumulate(company, new[] { ledger }, asOf, scenario);
        return BillsOf(ledger, sets[ledger.Id], KindOf(company, ledger));
    }

    /// <summary>
    /// The money under <paramref name="groupName"/> that the bill-wise projection <b>structurally cannot
    /// see</b> as of <paramref name="asOf"/>: Σ |closing| over the ledgers under that group which do NOT
    /// maintain bill-wise details. Zero means every rupee on that side is represented by bills, so a
    /// "due till today" figure derived from them is a complete measurement.
    ///
    /// <para>🔴 <b>This exists so a caller can tell "nothing has fallen due" apart from "this book cannot
    /// answer the question".</b> <see cref="Domain.Ledger.MaintainBillByBill"/> defaults to <c>false</c>, so a
    /// book whose debtors carry real balances and no bills at all is the DEFAULT shape, not an edge case — and
    /// on it every due-till-today total is 0 while the Balance Sheet shows the money. A ratio built on such a
    /// numerator must be published as unavailable, never as a confident zero. Magnitudes are summed (not
    /// netted) so two opposite-signed blind ledgers cannot cancel each other into a false "covered".</para>
    /// </summary>
    public static Money BillWiseBlindClosing(
        Company company, DateOnly asOf, string groupName, Scenario? scenario = null)
    {
        // Candidate set first, then ONE voucher pass over it — never one pass per ledger.
        var blind = new Dictionary<Guid, decimal>();
        foreach (var ledger in company.Ledgers)
        {
            if (ledger.MaintainBillByBill) continue;
            if (!ClassificationRules.GroupIsUnder(ledger.GroupId, groupName, company)) continue;
            // Mirrors LedgerBalances.SignedClosing under a scenario: the actual-books opening is only in the
            // scenario column when the scenario includes actuals.
            blind[ledger.Id] = scenario is { } s && !s.IncludeActuals ? 0m : ledger.SignedOpening;
        }
        if (blind.Count == 0) return Money.Zero;

        foreach (var v in company.Vouchers)
        {
            if (!CountsUnder(company, v, asOf, scenario)) continue;
            foreach (var line in v.Lines)
                if (blind.TryGetValue(line.LedgerId, out var running))
                    blind[line.LedgerId] = running + line.Signed;
        }

        var total = 0m;
        foreach (var signed in blind.Values) total += Math.Abs(signed);
        return new Money(total);
    }

    /// <summary>Whether a voucher counts as of a date, under a scenario when one is given.</summary>
    private static bool CountsUnder(Company company, Voucher v, DateOnly asOf, Scenario? scenario)
        => scenario is { } s
            ? LedgerBalances.CountsAsOf(v, asOf, s, company)
            : LedgerBalances.CountsAsOf(v, asOf);

    /// <summary>
    /// Accumulates bill state for <paramref name="ledgers"/> in a <b>single</b> pass over the voucher set.
    /// Every ledger asked for gets an entry, empty or not, so callers can index without a null check.
    /// </summary>
    private static Dictionary<Guid, LedgerBillSet> Accumulate(
        Company company, IReadOnlyList<Domain.Ledger> ledgers, DateOnly asOf, Scenario? scenario)
    {
        var sets = new Dictionary<Guid, LedgerBillSet>();
        var byId = new Dictionary<Guid, Domain.Ledger>();
        var kinds = new Dictionary<Guid, OutstandingKind>();
        foreach (var l in ledgers)
        {
            if (sets.ContainsKey(l.Id)) continue;
            sets[l.Id] = new LedgerBillSet();
            byId[l.Id] = l;
            kinds[l.Id] = KindOf(company, l);
        }
        if (sets.Count == 0) return sets;

        foreach (var v in company.Vouchers)
        {
            if (!CountsUnder(company, v, asOf, scenario)) continue;
            foreach (var line in v.Lines)
            {
                if (!line.HasBillAllocations) continue;
                if (!sets.TryGetValue(line.LedgerId, out var set)) continue;
                var ledger = byId[line.LedgerId];
                var kind = kinds[line.LedgerId];
                foreach (var a in line.BillAllocations)
                {
                    // On-Account is unallocated/suspense — it never opens or settles a named bill.
                    if (a.RefType == BillRefType.OnAccount) continue;

                    var key = a.Name;
                    if (!set.Bills.TryGetValue(key, out var state))
                    {
                        state = new BillState
                        {
                            Reference = a.Name,
                            OpenedAs = a.RefType,
                            Date = v.Date,
                            DueDate = a.EffectiveDueDate(v.Date, ledger.DefaultCreditPeriodDays),
                        };
                        set.Bills[key] = state;
                        set.Order.Add(key);
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
        return sets;
    }

    /// <summary>Emits one ledger's accumulated state as bills, first-opened order, dropping settled ones.</summary>
    private static List<OutstandingBill> BillsOf(Domain.Ledger ledger, LedgerBillSet set, OutstandingKind kind)
    {
        var result = new List<OutstandingBill>();
        foreach (var key in set.Order)
        {
            var s = set.Bills[key];
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

    /// <summary>One ledger's bills, keyed by reference name, with first-seen order preserved.</summary>
    private sealed class LedgerBillSet
    {
        public readonly List<string> Order = new();
        public readonly Dictionary<string, BillState> Bills = new(StringComparer.OrdinalIgnoreCase);
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
