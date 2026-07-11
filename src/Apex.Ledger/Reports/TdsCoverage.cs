using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One posted TDS deduction (a <see cref="TdsLineTax"/> with TDS &gt; 0) as seen by the exception/outstanding
/// projections (R1/R3/R4), decorated with the FIFO-matched deposit <see cref="Portions"/> that discharge it. A
/// deduction may be discharged by several challans (a split), each portion carrying the challan's deposit date so
/// the interest projection measures late months per portion; the still-uncovered <see cref="Residual"/> is the
/// outstanding TDS as of the report date.
/// </summary>
internal sealed class TdsCoverageUnit
{
    public required DateOnly Date { get; init; }          // deduction date = voucher date
    public required int Order { get; init; }              // deterministic voucher order (FIFO tie-break)
    public required string Section { get; init; }         // income-tax section code (e.g. "194J(b)")
    public required TdsLineTax Tax { get; init; }         // the posted withholding line (full detail)
    public readonly List<TdsCoveredPortion> Portions = new();

    /// <summary>The full withheld TDS (paisa-exact).</summary>
    public decimal Tds => Tax.TdsAmount.Amount;

    /// <summary>Σ of the FIFO-matched deposit portions discharging this deduction (≤ <see cref="Tds"/>).</summary>
    public decimal Covered
    {
        get { var s = 0m; foreach (var p in Portions) s += p.Take; return s; }
    }

    /// <summary>The still-undeposited TDS as of the report date (= <see cref="Tds"/> − <see cref="Covered"/>, ≥ 0).</summary>
    public decimal Residual => Tds - Covered;
}

/// <summary>The portion (<see cref="Take"/>) of a deduction discharged by a challan deposited on
/// <see cref="DepositDate"/> — the unit the interest projection charges late months against.</summary>
internal readonly record struct TdsCoveredPortion(decimal Take, DateOnly DepositDate);

/// <summary>
/// The shared internal FIFO <b>coverage</b> matcher for the TDS exception/outstanding reports (Phase 7 slice 8) —
/// the same period-attributed logic <see cref="Form26Q"/> uses (FIFO per section: a section's challans, ordered by
/// deposit date, discharge that section's deductions, ordered by deduction date), factored so R1/R3/R4 reuse one
/// source of truth. Unlike the return (which matches over all history for quarter attribution), this is an
/// <b>as-of</b> view: deductions dated after <paramref name="asOf"/> and challans deposited after
/// <paramref name="asOf"/> are excluded, and a challan whose booking Stat-Payment voucher was cancelled is dropped
/// from "deposited" (mirrors <see cref="ChallanReconciliation"/>). A boundary-split deduction is never
/// double-counted (each portion is capped at the challan's remaining amount).
/// </summary>
internal static class TdsCoverage
{
    /// <summary>Collects the posted deductions (TDS &gt; 0, non-cancelled, dated ≤ <paramref name="asOf"/>) and
    /// FIFO-matches the live challans (deposited ≤ <paramref name="asOf"/>) against them, returning the units in
    /// deterministic (date, voucher order) order with their coverage portions filled in.</summary>
    public static IReadOnlyList<TdsCoverageUnit> Match(Company company, DateOnly asOf)
    {
        var units = CollectUnits(company, asOf);

        // FIFO queues per section (units are already in (date, order) order).
        var queues = new Dictionary<string, Queue<Pending>>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            if (!queues.TryGetValue(u.Section, out var q)) { q = new Queue<Pending>(); queues[u.Section] = q; }
            q.Enqueue(new Pending(u));
        }

        var challansBySection = company.TdsChallans
            .Where(ch => ch.DepositDate <= asOf && ChallanIsLive(company, ch.Id))
            .GroupBy(ch => ch.Section, StringComparer.Ordinal);

        foreach (var group in challansBySection)
        {
            if (!queues.TryGetValue(group.Key, out var queue)) continue; // orphan deposit — no deduction to cover
            foreach (var ch in group.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
            {
                var remaining = ch.Amount.Amount;
                while (remaining > 0m && queue.Count > 0)
                {
                    var p = queue.Peek();
                    var take = Math.Min(remaining, p.Remaining);
                    p.Unit.Portions.Add(new TdsCoveredPortion(take, ch.DepositDate));
                    p.Remaining -= take;
                    remaining -= take;
                    if (p.Remaining <= 0m) queue.Dequeue();
                }
            }
        }

        return units;
    }

    // F6 (LOW carry-forward): the FIFO queue enqueues units in company.Vouchers <b>insertion order</b> and assumes
    // that order is chronological — the same assumption <see cref="Form26Q"/>/<see cref="Form27EQ"/> make, kept
    // deliberately identical so the returns and these reports stay mutually consistent. A backdated same-section
    // deduction (inserted after a later-dated one) can therefore mis-attribute the §201(1A)/§206C(7) interest MONTHS
    // between the two on a partial-challan split (a per-row effect only — the section outstanding/deducted TOTALS are
    // unaffected). Sorting the queue by (Voucher.Date, insertion-index) would fix it, but only if applied identically
    // to Form26Q/Form27EQ to avoid report/return divergence and FVU byte-drift; deferred to a slice that can re-verify
    // the returns' output rather than forced into this read-only slice.
    private static List<TdsCoverageUnit> CollectUnits(Company company, DateOnly asOf)
    {
        var units = new List<TdsCoverageUnit>();
        var order = 0;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) { order++; continue; }
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t) continue;
                if (t.TdsAmount.Amount <= 0m) continue; // a below-threshold assessment carries no deposit obligation
                units.Add(new TdsCoverageUnit { Date = v.Date, Order = order, Section = t.SectionCode, Tax = t });
            }
            order++;
        }
        return units;
    }

    /// <summary>True iff the challan's booking Stat-Payment voucher still exists and is not cancelled (mirrors
    /// <see cref="ChallanReconciliation"/> / <see cref="Form26Q"/>).</summary>
    internal static bool ChallanIsLive(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }

    private sealed class Pending
    {
        public TdsCoverageUnit Unit { get; }
        public decimal Remaining;
        public Pending(TdsCoverageUnit unit) { Unit = unit; Remaining = unit.Tds; }
    }
}
