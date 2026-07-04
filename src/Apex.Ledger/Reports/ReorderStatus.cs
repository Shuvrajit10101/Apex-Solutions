using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Reorder Status row (catalog §16; requirements RQ-33): a stock item whose closing quantity is at/below
/// its reorder level, with the shortfall and the suggested order quantity.
/// </summary>
public sealed record ReorderStatusRow(
    Guid StockItemId,
    string ItemName,
    decimal ClosingQuantity,
    decimal ReorderLevel,
    decimal? MinimumOrderQuantity,
    decimal Shortfall,
    decimal SuggestedOrderQuantity);

/// <summary>
/// The Reorder Status report (catalog §16; requirements RQ-33) — the items whose closing quantity as of
/// <c>asOf</c> is at or below their <see cref="StockItem.ReorderLevel"/>. Items with no reorder level set are
/// skipped. <see cref="ReorderStatusRow.Shortfall"/> = reorder level − closing (floored at 0); the suggested
/// order quantity is the greater of the shortfall and the item's minimum order quantity (simple reorder,
/// DP-9). Rows are sorted by item name. A <b>pure</b> projection — no UI, no DB.
/// </summary>
public sealed record ReorderStatus(DateOnly AsOf, IReadOnlyList<ReorderStatusRow> Rows)
{
    /// <summary>Builds the Reorder Status report for the whole company as of <paramref name="asOf"/>.</summary>
    public static ReorderStatus Build(Company company, DateOnly asOf)
    {
        var onHand = new InventoryLedger(company);
        var rows = new List<ReorderStatusRow>();

        foreach (var item in company.StockItems)
        {
            if (item.ReorderLevel is not { } level) continue; // no reorder level set ⇒ excluded
            var closing = onHand.OnHand(item.Id, asOf);
            if (closing > level) continue; // above the reorder level ⇒ not flagged

            var shortfall = level - closing;
            if (shortfall < 0m) shortfall = 0m;
            var minOrder = item.MinimumOrderQuantity;
            var suggested = minOrder is { } mq && mq > shortfall ? mq : shortfall;

            rows.Add(new ReorderStatusRow(item.Id, item.Name, closing, level, minOrder, shortfall, suggested));
        }

        rows.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase));
        return new ReorderStatus(asOf, rows);
    }
}
