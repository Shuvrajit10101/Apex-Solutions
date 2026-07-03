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
    /// <summary>Σ of every category total (== Σ of all cost allocations in the window).</summary>
    public Money GrandTotal
    {
        get
        {
            var s = 0m;
            foreach (var t in Categories) s += t.Total.Amount;
            return new Money(s);
        }
    }
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
    /// <summary>Σ of the OWN totals of every centre (== Σ of all cost allocations in the window).</summary>
    public Money GrandTotal
    {
        get
        {
            var s = 0m;
            foreach (var c in Centres) s += c.OwnTotal.Amount;
            return new Money(s);
        }
    }
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

        return new CategorySummaryReport(from, to, rows);
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

        return new CostCentreBreakupReport(from, to, lines);
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

        return new LedgerBreakupReport(from, to, rows);
    }
}
