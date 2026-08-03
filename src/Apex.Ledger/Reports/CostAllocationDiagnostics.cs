using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One entry line whose cost allocations were written under the superseded <b>partition</b> rule: the
/// allocations foot only when the categories are added together, so at least one axis does not total the
/// line amount on its own.
/// </summary>
/// <param name="VoucherId">The voucher the line belongs to.</param>
/// <param name="VoucherDate">Its date, so the review list can be ordered and scoped.</param>
/// <param name="LedgerId">The ledger the line posts to.</param>
/// <param name="LineAmount">The line amount every axis ought to total.</param>
/// <param name="ShortCategories">
/// The categories that do NOT total <paramref name="LineAmount"/>, each with what they actually total.
/// </param>
public sealed record LegacyCostAllocationLine(
    Guid VoucherId,
    DateOnly VoucherDate,
    Guid LedgerId,
    Money LineAmount,
    IReadOnlyList<(Guid CategoryId, Money Allocated)> ShortCategories);

/// <summary>
/// Finds cost allocations that predate the parallel-set rule (spec §4.2 rule C-27; gap G-2).
/// <para>Books written before the fix can contain a line "split" across two axes — e.g. ₹3,000 to
/// Branch → Kolkata and ₹2,000 to Department → Marketing on a ₹5,000 line. Those still load (the two
/// rehydration paths validate with <see cref="CostAllocationStrictness.Legacy"/>) but they are
/// <b>misleading data</b>: the Category Summary shows Branch ₹3,000 when the branch actually bore the whole
/// ₹5,000. Only a human can say what was meant — the intent may have been ₹5,000 on each axis, or a genuine
/// single-axis ₹3,000 with the second row entered by mistake — so this class <b>repairs nothing</b>. It
/// lists the lines that need a person to re-allocate them.</para>
/// </summary>
public static class CostAllocationDiagnostics
{
    /// <summary>
    /// Every line in the company whose cost allocations satisfy the old partition rule but not the
    /// per-category rule, ordered by voucher date then voucher id. Empty for books that only ever allocated
    /// along one axis per line — i.e. for every company this product has shipped to date.
    /// <para>Cancelled and optional vouchers are included deliberately: they are still stored data a user
    /// may later un-cancel, and this is a review list rather than a financial report.</para>
    /// </summary>
    public static IReadOnlyList<LegacyCostAllocationLine> FindLegacyCrossCategoryLines(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<LegacyCostAllocationLine>();
        foreach (var v in company.Vouchers)
        {
            foreach (var line in v.Lines)
            {
                if (!line.HasCostAllocations) continue;

                var categories = line.CostAllocationCategoryIds;
                if (categories.Count < 2) continue;   // a single axis is either correct or was rejected

                var shortfalls = new List<(Guid CategoryId, Money Allocated)>();
                foreach (var categoryId in categories)
                {
                    var allocated = line.CostAllocationTotalFor(categoryId);
                    if (allocated != line.Amount)
                        shortfalls.Add((categoryId, allocated));
                }

                if (shortfalls.Count > 0)
                    rows.Add(new LegacyCostAllocationLine(
                        v.Id, v.Date, line.LedgerId, line.Amount, shortfalls));
            }
        }

        return rows
            .OrderBy(r => r.VoucherDate)
            .ThenBy(r => r.VoucherId)
            .ToList();
    }
}
