using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One category's total allocation (catalog §6 → Category Summary).</summary>
public sealed record CostCategoryTotal(Guid CategoryId, string CategoryName, Money Total);

/// <summary>
/// The Category Summary (catalog §6): total allocated amount per cost category over the posted voucher
/// set within a date window. Categories with no allocations are omitted; the order follows the company's
/// category order.
/// </summary>
public sealed record CategorySummaryReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CostCategoryTotal> Categories)
{
    /// <summary>
    /// The cost actually allocated in the window, <b>counted once per entry line</b>.
    /// <para>It is deliberately NOT Σ of the category totals. Cost categories are parallel allocation axes
    /// (spec §4.2 rule C-27): a ₹5,000 travelling expense classified by Branch, Department and Executive
    /// appears in full under all three, so adding the axes would report ₹15,000 of cost for one ₹5,000
    /// expense. On books that only ever use one axis per line the two definitions coincide, which is why
    /// this figure is unchanged for every existing single-category company.</para>
    /// </summary>
    public Money GrandTotal { get; init; }

    /// <summary>
    /// True when the category totals in this window <b>overlap</b> — i.e. some line carries the same money
    /// under more than one category, so Σ of the rows exceeds <see cref="GrandTotal"/> and the rows must not
    /// be read as addends.
    /// <para>Note what this is NOT: it is not "some line names two categories". A book saved under the
    /// superseded partition rule also names two categories on one line (₹3,000.19 Branch + ₹2,000.18
    /// Department on a ₹5,000.37 line) yet its rows add up to the total exactly — nothing is double-counted
    /// and nothing on screen has changed for that user, so this flag is FALSE there. The flag fires on the
    /// arithmetic fact (cross-category allocation exceeds the line amount), never on the shape.</para>
    /// </summary>
    public bool CategoryTotalsOverlap { get; init; }
}

/// <summary>
/// One cost-centre line in the Cost Centre Break-up (catalog §6). <see cref="OwnTotal"/> is the amount
/// allocated directly to this centre; <see cref="RolledUpTotal"/> adds every descendant centre's own
/// total (the hierarchical roll-up). <see cref="Depth"/> is the nesting level (0 = primary centre).
/// </summary>
public sealed record CostCentreLine(
    Guid CentreId,
    string CentreName,
    Guid CategoryId,
    Guid? ParentId,
    int Depth,
    Money OwnTotal,
    Money RolledUpTotal);

/// <summary>
/// The Cost Centre Break-up (catalog §6): every cost centre with its own and rolled-up totals, ordered
/// as a depth-first hierarchy (parents before their children) within each category. A parent's
/// <see cref="CostCentreLine.RolledUpTotal"/> includes all of its descendants.
/// </summary>
public sealed record CostCentreBreakupReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CostCentreLine> Centres)
{
    /// <summary>
    /// The cost actually allocated in the window, <b>counted once per entry line</b> — the same figure the
    /// Category Summary reports. It is NOT Σ of the centres' own totals: under parallel categories one
    /// amount is carried in full by one centre per axis, so summing centres across categories would
    /// double-count. On single-axis books the two definitions coincide.
    /// </summary>
    public Money GrandTotal { get; init; }

    /// <summary>
    /// True when the centres' own totals in this window <b>overlap</b> — i.e. some line's money is carried
    /// in full by one centre in each of two or more categories, so Σ of the own totals exceeds
    /// <see cref="GrandTotal"/>. See <see cref="CategorySummaryReport.CategoryTotalsOverlap"/> for why this
    /// is an arithmetic test and not "some line names two categories".
    /// </summary>
    public bool CategoryTotalsOverlap { get; init; }
}

/// <summary>One (centre, ledger) total in the Ledger Break-up (catalog §6).</summary>
public sealed record CostCentreLedgerTotal(
    Guid CentreId,
    string CentreName,
    Guid LedgerId,
    string LedgerName,
    Money Total);

/// <summary>
/// The Ledger Break-up (catalog §6): for each cost centre, the amount allocated to it broken down by the
/// ledger the allocation's line posts to. Rows are ordered by centre (company order) then ledger.
/// </summary>
public sealed record LedgerBreakupReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CostCentreLedgerTotal> Rows)
{
    /// <summary>The (centre, ledger) totals for one centre.</summary>
    public IReadOnlyList<CostCentreLedgerTotal> ForCentre(Guid centreId) =>
        Rows.Where(r => r.CentreId == centreId).ToList();

    /// <summary>
    /// The cost actually allocated in the window, <b>counted once per entry line</b> — the same figure the
    /// other two cost reports carry. Deliberately NOT Σ of <see cref="Rows"/>: a line classified under three
    /// categories produces three (centre, ledger) rows each carrying the whole amount, so summing them would
    /// report ₹15,000 of cost for one ₹5,000 expense (spec §4.2 rule C-27).
    /// </summary>
    public Money GrandTotal { get; init; }

    /// <summary>
    /// True when the rows overlap — same meaning and same computation as
    /// <see cref="CategorySummaryReport.CategoryTotalsOverlap"/>. Carried here so the three reports of this
    /// family cannot drift apart: whoever adds a footer to the Ledger Break-up gets the correct total and
    /// the warning without having to rediscover why Σ of the rows is the wrong number.
    /// </summary>
    public bool CategoryTotalsOverlap { get; init; }
}

/// <summary>
/// Pure cost-centre reports over the posted voucher set (catalog §6; plan.md §5). No UI, no DB. Each
/// method walks the posted vouchers within a date window (honouring the same Cancelled/Optional/
/// PostDated exclusions as the balance reports via <see cref="LedgerBalances.CountsAsOf"/>) and totals
/// the <see cref="CostAllocation"/>s hung off the entry lines.
/// </summary>
public static class CostReports
{
    /// <summary>
    /// The posted-and-counted cost allocations in <c>[from, to]</c>, each paired with the ledger its
    /// line posts to. A voucher counts iff it is not Cancelled/Optional, is not a not-yet-due PostDated,
    /// and its date is within the window.
    /// </summary>
    private static IEnumerable<(CostAllocation Alloc, Guid LedgerId)> Allocations(
        Company company, DateOnly from, DateOnly to)
    {
        foreach (var v in company.Vouchers)
        {
            // CountsAsOf(to) applies the Cancelled/Optional/PostDated + date≤to filter; then bound below.
            if (!LedgerBalances.CountsAsOf(v, to)) continue;
            if (v.Date < from) continue;

            foreach (var line in v.Lines)
                foreach (var a in line.CostAllocations)
                    yield return (a, line.LedgerId);
        }
    }

    /// <summary>
    /// The window's cost totalled <b>once per allocated line</b> (not once per axis), plus whether the
    /// per-category row totals <b>overlap</b>. Same voucher filter as <see cref="Allocations"/>. See
    /// <see cref="CategorySummaryReport.GrandTotal"/> for why the naive Σ over allocations is wrong once
    /// categories are parallel sets.
    /// <para>The overlap test is arithmetic, not structural: a line double-counts iff its allocations
    /// across all categories exceed the line amount. A conforming parallel set of N axes allocates N ×
    /// amount and therefore overlaps; a line saved under the superseded partition rule allocates exactly
    /// amount across its categories and therefore does NOT — its rows really do add up to the total, so no
    /// report may tell the reader otherwise. Testing <c>CostAllocationCategoryIds.Count &gt; 1</c> instead
    /// would conflate the two, and would fire on precisely the pre-fix books whose numbers did not
    /// change.</para>
    /// </summary>
    private static (Money AllocatedBase, bool CategoryTotalsOverlap) AllocatedBase(
        Company company, DateOnly from, DateOnly to)
    {
        var total = 0m;
        var overlaps = false;

        foreach (var v in company.Vouchers)
        {
            // CountsAsOf(to) applies the Cancelled/Optional/PostDated + date≤to filter; then bound below.
            if (!LedgerBalances.CountsAsOf(v, to)) continue;
            if (v.Date < from) continue;

            foreach (var line in v.Lines)
            {
                if (!line.HasCostAllocations) continue;
                total += line.Amount.Amount;
                if (line.CostAllocationTotal.Amount > line.Amount.Amount) overlaps = true;
            }
        }

        return (new Money(total), overlaps);
    }

    /// <summary>Category Summary — total allocated amount per cost category (catalog §6).</summary>
    public static CategorySummaryReport BuildCategorySummary(Company company, DateOnly from, DateOnly to)
    {
        var totals = new Dictionary<Guid, decimal>();
        foreach (var (a, _) in Allocations(company, from, to))
            totals[a.CategoryId] = totals.GetValueOrDefault(a.CategoryId) + a.Amount.Amount;

        var rows = new List<CostCategoryTotal>();
        foreach (var cat in company.CostCategories)
            if (totals.TryGetValue(cat.Id, out var sum) && sum != 0m)
                rows.Add(new CostCategoryTotal(cat.Id, cat.Name, new Money(sum)));

        var (allocatedBase, overlaps) = AllocatedBase(company, from, to);
        return new CategorySummaryReport(from, to, rows)
        {
            GrandTotal = allocatedBase,
            CategoryTotalsOverlap = overlaps,
        };
    }

    /// <summary>
    /// Cost Centre Break-up — every centre's own and rolled-up totals, depth-first per category
    /// (catalog §6). The roll-up sums each centre's own total plus every descendant's own total.
    /// </summary>
    public static CostCentreBreakupReport BuildCostCentreBreakup(Company company, DateOnly from, DateOnly to)
    {
        // Own totals per centre.
        var own = new Dictionary<Guid, decimal>();
        foreach (var (a, _) in Allocations(company, from, to))
            own[a.CentreId] = own.GetValueOrDefault(a.CentreId) + a.Amount.Amount;

        // Children index for the hierarchy walk (Guid.Empty keys the "no parent" bucket).
        var childrenOf = new Dictionary<Guid, List<CostCentre>>();
        foreach (var centre in company.CostCentres)
        {
            var parentKey = centre.ParentId ?? Guid.Empty;
            (childrenOf.TryGetValue(parentKey, out var kids) ? kids : childrenOf[parentKey] = new())
                .Add(centre);
        }

        decimal RolledUp(Guid centreId)
        {
            var total = own.GetValueOrDefault(centreId);
            if (childrenOf.TryGetValue(centreId, out var kids))
                foreach (var kid in kids)
                    total += RolledUp(kid.Id);
            return total;
        }

        var lines = new List<CostCentreLine>();

        void Emit(CostCentre centre, int depth)
        {
            lines.Add(new CostCentreLine(
                centre.Id, centre.Name, centre.CategoryId, centre.ParentId, depth,
                OwnTotal: new Money(own.GetValueOrDefault(centre.Id)),
                RolledUpTotal: new Money(RolledUp(centre.Id))));
            if (childrenOf.TryGetValue(centre.Id, out var kids))
                foreach (var kid in kids)
                    Emit(kid, depth + 1);
        }

        // Walk per category, primary centres first, preserving company order.
        foreach (var cat in company.CostCategories)
            foreach (var centre in company.CostCentres)
                if (centre.CategoryId == cat.Id && centre.IsPrimary)
                    Emit(centre, 0);

        var (allocatedBase, overlaps) = AllocatedBase(company, from, to);
        return new CostCentreBreakupReport(from, to, lines)
        {
            GrandTotal = allocatedBase,
            CategoryTotalsOverlap = overlaps,
        };
    }

    /// <summary>
    /// Ledger Break-up — per centre, the amount allocated to it split by the ledger its line posts to
    /// (catalog §6). Rows follow company centre order then company ledger order.
    /// </summary>
    public static LedgerBreakupReport BuildLedgerBreakup(Company company, DateOnly from, DateOnly to)
    {
        // (centreId, ledgerId) -> total.
        var totals = new Dictionary<(Guid Centre, Guid Ledger), decimal>();
        foreach (var (a, ledgerId) in Allocations(company, from, to))
        {
            var key = (a.CentreId, ledgerId);
            totals[key] = totals.GetValueOrDefault(key) + a.Amount.Amount;
        }

        var rows = new List<CostCentreLedgerTotal>();
        foreach (var centre in company.CostCentres)
            foreach (var ledger in company.Ledgers)
                if (totals.TryGetValue((centre.Id, ledger.Id), out var sum) && sum != 0m)
                    rows.Add(new CostCentreLedgerTotal(
                        centre.Id, centre.Name, ledger.Id, ledger.Name, new Money(sum)));

        var (allocatedBase, overlaps) = AllocatedBase(company, from, to);
        return new LedgerBreakupReport(from, to, rows)
        {
            GrandTotal = allocatedBase,
            CategoryTotalsOverlap = overlaps,
        };
    }
}
