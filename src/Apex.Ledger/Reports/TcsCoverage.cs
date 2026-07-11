using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One posted TCS collection (a <see cref="TcsLineTax"/> with TCS &gt; 0) as seen by the exception/outstanding
/// projections (R5/R7/R8), decorated with the FIFO-matched deposit <see cref="Portions"/> that discharge it. The
/// exact mirror of <see cref="TdsCoverageUnit"/> for the collector's side.
/// </summary>
internal sealed class TcsCoverageUnit
{
    public required DateOnly Date { get; init; }          // collection date = voucher date
    public required int Order { get; init; }              // deterministic voucher order (FIFO tie-break)
    public required string Code { get; init; }            // §206C collection code (e.g. "6CE")
    public required TcsLineTax Tax { get; init; }         // the posted collection line (full detail)
    public readonly List<TcsCoveredPortion> Portions = new();

    /// <summary>The full collected TCS (paisa-exact).</summary>
    public decimal Tcs => Tax.TcsAmount.Amount;

    /// <summary>Σ of the FIFO-matched deposit portions discharging this collection (≤ <see cref="Tcs"/>).</summary>
    public decimal Covered
    {
        get { var s = 0m; foreach (var p in Portions) s += p.Take; return s; }
    }

    /// <summary>The still-undeposited TCS as of the report date (= <see cref="Tcs"/> − <see cref="Covered"/>, ≥ 0).</summary>
    public decimal Residual => Tcs - Covered;
}

/// <summary>The portion (<see cref="Take"/>) of a collection discharged by a challan deposited on
/// <see cref="DepositDate"/> — the unit the interest projection charges late months against.</summary>
internal readonly record struct TcsCoveredPortion(decimal Take, DateOnly DepositDate);

/// <summary>
/// The shared internal FIFO <b>coverage</b> matcher for the TCS exception/outstanding reports (Phase 7 slice 8) —
/// the exact mirror of <see cref="TdsCoverage"/>, keyed by §206C collection code, using the same period-attributed
/// FIFO logic <see cref="Form27EQ"/> uses, as an as-of view (units + challans capped at <paramref name="asOf"/>,
/// cancelled Stat-Payment challans dropped). Factored so R5/R7/R8 reuse one source of truth.
/// </summary>
internal static class TcsCoverage
{
    /// <summary>Collects the posted collections (TCS &gt; 0, non-cancelled, dated ≤ <paramref name="asOf"/>) and
    /// FIFO-matches the live challans (deposited ≤ <paramref name="asOf"/>) against them, returning the units in
    /// deterministic (date, voucher order) order with their coverage portions filled in.</summary>
    public static IReadOnlyList<TcsCoverageUnit> Match(Company company, DateOnly asOf)
    {
        var units = CollectUnits(company, asOf);

        var queues = new Dictionary<string, Queue<Pending>>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            if (!queues.TryGetValue(u.Code, out var q)) { q = new Queue<Pending>(); queues[u.Code] = q; }
            q.Enqueue(new Pending(u));
        }

        var challansByCode = company.TcsChallans
            .Where(ch => ch.DepositDate <= asOf && ChallanIsLive(company, ch.Id))
            .GroupBy(ch => ch.CollectionCode, StringComparer.Ordinal);

        foreach (var group in challansByCode)
        {
            if (!queues.TryGetValue(group.Key, out var queue)) continue; // orphan deposit — no collection to cover
            foreach (var ch in group.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
            {
                var remaining = ch.Amount.Amount;
                while (remaining > 0m && queue.Count > 0)
                {
                    var p = queue.Peek();
                    var take = Math.Min(remaining, p.Remaining);
                    p.Unit.Portions.Add(new TcsCoveredPortion(take, ch.DepositDate));
                    p.Remaining -= take;
                    remaining -= take;
                    if (p.Remaining <= 0m) queue.Dequeue();
                }
            }
        }

        return units;
    }

    // F6 (LOW carry-forward): the FIFO queue enqueues units in company.Vouchers <b>insertion order</b> and assumes
    // that order is chronological — the same assumption <see cref="Form27EQ"/>/<see cref="Form26Q"/> make, kept
    // deliberately identical so the returns and these reports stay mutually consistent. A backdated same-code
    // collection can therefore mis-attribute the §206C(7) interest MONTHS on a partial-challan split (a per-row effect
    // only — the code outstanding/collected TOTALS are unaffected). Sorting the queue by (Voucher.Date, insertion-index)
    // would fix it, but only if applied identically to Form27EQ/Form26Q to avoid report/return divergence and FVU
    // byte-drift; deferred to a slice that can re-verify the returns' output rather than forced into this read-only slice.
    private static List<TcsCoverageUnit> CollectUnits(Company company, DateOnly asOf)
    {
        var units = new List<TcsCoverageUnit>();
        var order = 0;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) { order++; continue; }
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t) continue;
                if (t.TcsAmount.Amount <= 0m) continue; // a below-threshold assessment carries no deposit obligation
                units.Add(new TcsCoverageUnit { Date = v.Date, Order = order, Code = t.CollectionCode, Tax = t });
            }
            order++;
        }
        return units;
    }

    /// <summary>True iff the challan's booking Stat-Payment voucher still exists and is not cancelled (mirrors
    /// <see cref="TcsChallanReconciliation"/> / <see cref="Form27EQ"/>).</summary>
    internal static bool ChallanIsLive(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToTcsChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }

    private sealed class Pending
    {
        public TcsCoverageUnit Unit { get; }
        public decimal Remaining;
        public Pending(TcsCoverageUnit unit) { Unit = unit; Remaining = unit.Tcs; }
    }
}
