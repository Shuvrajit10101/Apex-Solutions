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
/// The <b>F6 Ageing Method</b> of the bills-outstanding views (census 11.13 / 11.9, T2-24). Vendor, TallyPrime,
/// help.tallysolutions.com/manage-receivables-outstanding-tally/ (opened by content 2026-10-29): <i>"Press F6
/// (Ageing Method) to configure the report view as per ageing by bill dates or by due dates and set the ageing
/// windows."</i>
/// </summary>
public enum AgeingMethod
{
    /// <summary>Age each bill from its due date — the shipped default, and the basis "overdue" is defined on.</summary>
    ByDueDate,

    /// <summary>Age each bill from its own bill (voucher) date.</summary>
    ByBillDate,
}

/// <summary>
/// The <b>Basis of Values &gt; Type of Bills</b> choice on Bills Receivable / Bills Payable (census 11.13). Vendor,
/// the same page: <i>"To view only overdue bills, use Basis of Values to select Overdue Bills in Type of
/// Bills."</i> Only the two values below are built; the vendor's other bill types are not (IV-287).
/// </summary>
public enum TypeOfBills
{
    /// <summary>Every open bill (the shipped default).</summary>
    AllBills,

    /// <summary>Only the bills <see cref="OutstandingBill.IsOverdue"/> as of the report date.</summary>
    OverdueBills,
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

    /// <summary>
    /// Whether this bill is <b>overdue</b> as of <paramref name="asOf"/>: its due date is STRICTLY before the
    /// report date, i.e. <see cref="OverdueDays"/> is at least 1. A bill falling due exactly on
    /// <paramref name="asOf"/> is due (<see cref="IsDueBy"/>) but NOT overdue — the same boundary the ageing
    /// buckets already draw ("Not due" holds zero overdue days). Census 11.13 Overdue Receivables / Payables
    /// read this one predicate; the boundary is recorded as ours in <c>docs/invented-vs-cloned.md</c> IV-245,
    /// because the vendor's TallyPrime page lists "overdue days" without defining the due-date day.
    /// </summary>
    public bool IsOverdue(DateOnly asOf) => OverdueDays(asOf) > 0;

    /// <summary>
    /// The age of this bill as of <paramref name="asOf"/> under the chosen F6 <paramref name="method"/>, floored
    /// at 0 — <see cref="AgeingMethod.ByDueDate"/> counts from <see cref="DueDate"/> (identical to
    /// <see cref="OverdueDays"/>), <see cref="AgeingMethod.ByBillDate"/> from the bill's own <see cref="Date"/>.
    /// Census 11.13 / 11.9 (T2-24). Vendor (help.tallysolutions.com/tally-prime/analysis-verification/outstandings-tally/,
    /// opened by content 2026-10-29): <i>"Select Ageing by Bill Date or Ageing by Due Date."</i>
    /// </summary>
    public int AgeDays(DateOnly asOf, AgeingMethod method)
    {
        if (method == AgeingMethod.ByDueDate) return OverdueDays(asOf);
        var days = asOf.DayNumber - Date.DayNumber;
        return days > 0 ? days : 0;
    }

    /// <summary>
    /// The voucher whose New-Ref / Advance allocation OPENED this bill (the latest such voucher when a bill was
    /// re-opened), or <see cref="Guid.Empty"/> when only Agst-Ref allocations name it. Census 11.13: the drill
    /// key the Overdue Receivables / Payables rows open, so Enter on a bill lands on the invoice that raised it.
    /// </summary>
    public Guid OpeningVoucherId { get; init; }
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
    /// <summary>The F6 basis <see cref="ReceivableAgeing"/> / <see cref="PayableAgeing"/> were bucketed on.</summary>
    public AgeingMethod AgeingMethod { get; init; } = AgeingMethod.ByDueDate;

    /// <summary>The Basis-of-Values Type of Bills the bill lists (and so every total and bucket) were filtered to.</summary>
    public TypeOfBills TypeOfBills { get; init; } = TypeOfBills.AllBills;

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
    /// <summary>Default ageing bucket edges (upper-inclusive day counts); the tail is open-ended. These are the
    /// <see cref="AgeingMethod.ByDueDate"/> windows: zero days past due is "Not due".</summary>
    public static readonly IReadOnlyList<(string Label, int Lower, int? Upper)> DefaultBuckets = new[]
    {
        ("Not due", int.MinValue, 0),
        ("0-30 days", 1, 30),
        ("31-60 days", 31, 60),
        ("61-90 days", 61, 90),
        ("90+ days", 91, (int?)null),
    };

    /// <summary>
    /// The <see cref="AgeingMethod.ByBillDate"/> windows. A bill's age from its own date is never "not due" — that
    /// is a due-date fact — so the first window starts at day 0 (a bill raised on the as-of date). The edges are
    /// the same 30/60/90 the due-date set uses, so the two bases differ only in the date counted from. OURS
    /// (IV-286): the vendor lets the operator "set the ageing windows"; these are fixed.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, int Lower, int? Upper)> BillDateBuckets = new[]
    {
        ("0-30 days", int.MinValue, 30),
        ("31-60 days", 31, 60),
        ("61-90 days", 61, 90),
        ("90+ days", 91, (int?)null),
    };

    /// <summary>The bucket windows the given F6 basis ages on.</summary>
    public static IReadOnlyList<(string Label, int Lower, int? Upper)> BucketsFor(AgeingMethod method)
        => method == AgeingMethod.ByBillDate ? BillDateBuckets : DefaultBuckets;

    /// <summary>The label of the bucket <paramref name="bill"/> falls in as of <paramref name="asOf"/> under
    /// <paramref name="method"/> — the per-bill "Ageing" cell, from the same windows as the summary band.</summary>
    public static string BucketLabel(OutstandingBill bill, DateOnly asOf, AgeingMethod method)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var buckets = BucketsFor(method);
        return buckets[BucketIndex(bill.AgeDays(asOf, method), buckets)].Label;
    }

    /// <summary>
    /// Builds the Outstandings projection for the whole company as of <paramref name="asOf"/>, optionally
    /// under a <paramref name="scenario"/> (catalog §7). Passing <c>null</c> reproduces the actual books
    /// exactly, so a report builder can thread its <c>options.Scenario</c> through unconditionally — the same
    /// optional-trailing-parameter shape <see cref="BalanceSheet"/> and
    /// <see cref="LedgerBalances.SignedClosing(Company, Domain.Ledger, DateOnly, Scenario?)"/> already use.
    /// </summary>
    ///
    /// <para><paramref name="ageingMethod"/> is the F6 basis the two ageing summaries bucket on, and
    /// <paramref name="typeOfBills"/> the Basis-of-Values filter: <see cref="TypeOfBills.OverdueBills"/> keeps only
    /// the bills <see cref="OutstandingBill.IsOverdue"/> — the ONE overdue predicate, which
    /// <see cref="OverdueBills"/> also reaches through this method — and the totals and buckets are taken over
    /// the kept bills. Neither changes the netting: every bill's pending amount is the one the unfiltered build
    /// computes.</para>
    public static OutstandingsReport Build(
        Company company,
        DateOnly asOf,
        Scenario? scenario = null,
        AgeingMethod ageingMethod = AgeingMethod.ByDueDate,
        TypeOfBills typeOfBills = TypeOfBills.AllBills)
    {
        // 🔴 ONE voucher pass for the WHOLE company. This used to call OpenBillsFor per bill-wise ledger, and
        // each of those calls walked every voucher — so a report that merely opens this projection paid
        // O(parties × vouchers × lines × allocations). Accumulate takes the whole ledger set at once.
        // MEASURED, not estimated, at 100 parties × 2,000 vouchers and 200 × 4,000: the old shape grew with the
        // PRODUCT of parties and vouchers, the new one grows with the vouchers alone, so the gap WIDENS with book
        // size — that is the claim, and it is the one that stays true. 🔴 The absolute milliseconds and the
        // speedup multiple deliberately do NOT live here: they are machine- and run-specific, CI cannot falsify
        // them, and re-running the same probe one wave later already produced a different multiple on the SAME
        // machine. The figures live in the evidence files (a5-r1-f9-measurement*.txt, a5-r1-p4-f9-reverify.txt).
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
                if (typeOfBills == TypeOfBills.OverdueBills && !bill.IsOverdue(asOf)) continue;
                if (bill.Kind == OutstandingKind.Receivable) receivables.Add(bill);
                else payables.Add(bill);
            }
        }

        return new OutstandingsReport(
            asOf,
            receivables,
            payables,
            AgeingOf(receivables, asOf, ageingMethod),
            AgeingOf(payables, asOf, ageingMethod))
        {
            AgeingMethod = ageingMethod,
            TypeOfBills = typeOfBills,
        };
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
    /// The money under <paramref name="groupName"/> that the bill-wise projection <b>cannot account for</b> as
    /// of <paramref name="asOf"/>: for every ledger under that group, |closing| minus the pending amount its own
    /// open bills carry, summed over the ledgers that fall short. Zero means every rupee on that side is
    /// represented by a bill, so a "due till today" figure derived from those bills is a complete measurement.
    ///
    /// <para>🔴 <b>THIS MEASURES THE MONEY, NOT THE <see cref="Domain.Ledger.MaintainBillByBill"/> FLAG, AND
    /// THE DIFFERENCE IS THE WHOLE POINT.</b> An earlier version summed the closing balances of the ledgers
    /// whose flag was <c>false</c>, i.e. it trusted the flag to imply that a bill-wise ledger's money is carried
    /// by bills. It is not: a bill-wise party's balance can arrive as an <b>opening balance</b> (this build has
    /// no opening bill-wise breakdown at all — <c>CompanySplit</c> refuses a split rather than produce a book
    /// whose party balances and Outstandings disagree), on a line posted with <b>no allocations</b>, or against
    /// an <b>On Account</b> allocation. On each of those the flag says "covered" while no bill exists, the
    /// due-till-today total is 0, and a ratio built on it published a confident "0 days". Comparing the
    /// projection's own pending amounts against the closing balances they claim to represent covers all three
    /// shapes in one rule, and needs no new knowledge of why a rupee has no bill.</para>
    ///
    /// <para>Two deliberate arithmetic choices: magnitudes are used (not signed netting) so two opposite-signed
    /// uncovered ledgers cannot cancel each other into a false "covered"; and each ledger's shortfall is floored
    /// at zero so a ledger carrying MORE bills than balance cannot mask another ledger's missing money.</para>
    /// </summary>
    public static Money ClosingNotCoveredByBills(
        Company company, DateOnly asOf, string groupName, Scenario? scenario = null)
    {
        // Candidate set first, then ONE voucher pass over it — never one pass per ledger (see Build).
        var candidates = new List<Domain.Ledger>();
        var closing = new Dictionary<Guid, decimal>();
        foreach (var ledger in company.Ledgers)
        {
            if (!ClassificationRules.GroupIsUnder(ledger.GroupId, groupName, company)) continue;
            candidates.Add(ledger);
            // Mirrors LedgerBalances.SignedClosing under a scenario: the actual-books opening is only in the
            // scenario column when the scenario includes actuals.
            closing[ledger.Id] = scenario is { } s && !s.IncludeActuals ? 0m : ledger.SignedOpening;
        }
        if (candidates.Count == 0) return Money.Zero;

        foreach (var v in company.Vouchers)
        {
            // 🔴 This is a CLOSING BALANCE, so it must drop the provisional base types (Memorandum / Reversing
            // Journal) exactly as LedgerBalances.SignedClosing does — CountsUnder carries the base type. See its
            // note for the rendered figures the looser rule produced.
            if (!CountsUnder(company, v, asOf, scenario)) continue;
            foreach (var line in v.Lines)
                if (closing.TryGetValue(line.LedgerId, out var running))
                    closing[line.LedgerId] = running + line.Signed;
        }

        // The cover: the pending amounts the projection itself holds for these ledgers, on the same basis.
        // One accumulation for the whole candidate set, exactly as Build does.
        //
        // 🔴 BOTH SIDES OF THIS COMPARISON MUST DROP THE SAME VOUCHERS, and now do by construction: the closing
        // side above and the cover side (Accumulate) share CountsUnder. Before the rule was unified the cover
        // side used a looser rule with no base type, so a single Memorandum
        // voucher CARRYING A BILL ALLOCATION manufactured cover that the closing balance it is measured against
        // does not contain — the shortfall collapsed to 0, the guard went blind, and the rendered cell published
        // "91 days" over 1,00,000 of debtor money with no posting bill behind it. It is wrong in the other
        // direction too: a memo AgstRef knocking a real bill off removed cover the closing balance still held,
        // inventing a shortfall and withholding a publishable ratio. A Memorandum is non-posting by definition,
        // so neither side of a closing-balance comparison may see it — and, since wave 8 (HIGH-1), neither may
        // the bill projection itself: a memo bill is not a bill on Bills Receivable / Payable or Overdue either.
        var billWise = new List<Domain.Ledger>();
        foreach (var ledger in candidates)
            if (ledger.MaintainBillByBill) billWise.Add(ledger);
        var sets = billWise.Count == 0 ? null : Accumulate(company, billWise, asOf, scenario);

        var total = 0m;
        foreach (var ledger in candidates)
        {
            var covered = 0m;
            if (sets is not null && ledger.MaintainBillByBill)
                foreach (var bill in BillsOf(ledger, sets[ledger.Id], KindOf(company, ledger)))
                    covered += bill.Pending.Amount;   // BillsOf drops settled bills, so Pending is > 0

            var shortfall = Math.Abs(closing[ledger.Id]) - covered;
            if (shortfall > 0m) total += shortfall;
        }
        return new Money(total);
    }

    /// <summary>
    /// Whether a voucher counts as of a date, under a scenario when one is given — byte-for-byte the rule
    /// <see cref="LedgerBalances.SignedClosing(Company, Domain.Ledger, DateOnly, Scenario?)"/> applies,
    /// <b>base type included</b>. This is the ONE voucher rule of the bill projection: <see cref="Build"/>,
    /// <see cref="OpenBillsFor"/> and both sides of <see cref="ClosingNotCoveredByBills"/> all use it.
    /// <para>🔴 <b>THE BASE TYPE IS THE POINT.</b> A <b>Memorandum</b> or <b>Reversing Journal</b> never touches
    /// the real books. Vendor (TallyPrime, help.tallysolutions.com/accounting-entry-tally/, opened by content
    /// 2026-10-27): <i>"Memorandum Vouchers are those vouchers that do not impact your books of accounts."</i>
    /// and, of Reversing Journals, <i>"These are not posted to any books of accounts and cannot be included in
    /// regular reports."</i> Their effect is seen only through a scenario, which the scenario branch keeps.</para>
    /// <para>Measured before this rule was unified (wave 8 review, track ed, HIGH-1): the projection used the
    /// looser rule with no base type, so one Memorandum Dr 50,000.01 carrying a NewRef against a bill-wise
    /// debtor was listed as an overdue bill, and Overdue Receivables totalled 57,901.62 against a true 7,901.61
    /// — more than five times the debtor's whole ledger balance. The same memo bill sat on Bills Receivable.
    /// Earlier still, the looser rule had to be bypassed by a separate closing-only rule for the ratio guard
    /// (a memo debit invented a 50,000 shortfall; a memo bill manufactured cover and published "91 days" over
    /// 1,00,000 of uncovered debtor money). One rule now serves every caller, so they cannot drift apart.</para>
    /// </summary>
    private static bool CountsUnder(Company company, Voucher v, DateOnly asOf, Scenario? scenario)
        => scenario is { } s
            ? LedgerBalances.CountsAsOf(v, asOf, s, company)
            : LedgerBalances.CountsAsOf(v, asOf, company.FindVoucherType(v.TypeId)?.BaseType);

    /// <summary>
    /// Accumulates bill state for <paramref name="ledgers"/> in a <b>single</b> pass over the voucher set.
    /// Every ledger asked for gets an entry, empty or not, so callers can index without a null check.
    /// The voucher rule is <see cref="CountsUnder"/> — non-posting Memorandum and Reversing Journal vouchers
    /// neither open nor settle a bill outside a scenario.
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
                        state.OpeningVoucherId = v.Id;
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
                kind) { OpeningVoucherId = s.OpeningVoucherId });
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

    /// <summary>Ages <paramref name="bills"/> into the windows of <paramref name="method"/> — the shared ageing
    /// summary of Bills Receivable / Payable and Overdue Receivables / Payables.</summary>
    public static IReadOnlyList<AgeingBucket> AgeingOf(
        IReadOnlyList<OutstandingBill> bills, DateOnly asOf, AgeingMethod method = AgeingMethod.ByDueDate)
    {
        ArgumentNullException.ThrowIfNull(bills);
        var buckets = BucketsFor(method);
        var totals = new decimal[buckets.Count];
        foreach (var b in bills)
            totals[BucketIndex(b.AgeDays(asOf, method), buckets)] += b.Pending.Amount;

        var result = new List<AgeingBucket>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
        {
            var (label, lower, upper) = buckets[i];
            result.Add(new AgeingBucket(label, lower, upper, new Money(totals[i])));
        }
        return result;
    }

    /// <summary>Index of the <see cref="DefaultBuckets"/> (by-due-date) bucket an overdue-day count falls in.</summary>
    public static int BucketIndex(int overdueDays) => BucketIndex(overdueDays, DefaultBuckets);

    /// <summary>Index of the bucket of <paramref name="buckets"/> a day count falls in (the tail if none).</summary>
    public static int BucketIndex(int days, IReadOnlyList<(string Label, int Lower, int? Upper)> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        for (var i = 0; i < buckets.Count; i++)
        {
            var (_, lower, upper) = buckets[i];
            if (days >= lower && (upper is null || days <= upper.Value))
                return i;
        }
        return buckets.Count - 1;
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
        public Guid OpeningVoucherId;
    }
}
