using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Reorder Status row (catalog §11; requirements RQ-37; Tally-Book p.161): a stock item resolved to an
/// effective reorder level whose closing quantity is at/below that level (or which still needs ordering after
/// netting incoming purchase orders), with the pending purchase orders, sales orders due, the shortfall and the
/// load-bearing <see cref="OrderToBePlaced"/> quantity.
/// </summary>
public sealed record ReorderStatusRow(
    Guid StockItemId,
    string ItemName,
    decimal ClosingQuantity,
    decimal ReorderLevel,
    decimal? MinimumOrderQuantity,
    decimal PendingPurchaseOrders,
    decimal SalesOrdersDue,
    decimal Shortfall,
    decimal OrderToBePlaced);

/// <summary>
/// The Reorder Status report (catalog §11; requirements RQ-32..RQ-37; Tally-Book pp.158–162) — the items whose
/// closing quantity as of <c>asOf</c> is at or below their <b>effective reorder level</b>, or which still need
/// ordering once incoming purchase orders are netted. Compared with the Phase-3 basic report this refactor adds
/// the full master model (ER-5, one engine, not a parallel one):
/// <list type="bullet">
///   <item><b>Master definitions (RQ-32/RQ-36).</b> A <see cref="ReorderDefinition"/> may be attached per Item,
///     Group or Category. Each item resolves the most-specific one: an Item definition wins, else the nearest
///     ancestor Group definition, else the nearest ancestor Category definition, else the legacy per-item
///     <see cref="StockItem.ReorderLevel"/>/<see cref="StockItem.MinimumOrderQuantity"/> (backward-compat, ER-13),
///     else the item is excluded.</item>
///   <item><b>Simple vs Advanced (RQ-33/34/35).</b> Each figure is a fixed typed quantity (Simple) or derived
///     from the item's <see cref="InventoryLedger.Consumption"/> over a rolling period reconciled Higher/Lower
///     against the fixed figure (Advanced).</item>
///   <item><b>Order to be Placed (RQ-37).</b> For an item at or below its level, <c>netShortfall =
///     max(ReorderLevel − Closing, 0) − PendingPOs</c> and the quantity is <c>max(netShortfall, MinOrderQty)</c>
///     — bounded <b>below</b> by the minimum order quantity AND net of pending purchase orders — dropping to 0
///     only when incoming purchase orders actually cover the shortfall. So at <c>Closing == Level</c> with no
///     pending PO the order is the MinOrderQty (ER-13 / Phase-3 parity), not zero. Sales Orders Due is shown for
///     context but is <b>not</b> netted (DD-4).</item>
/// </list>
/// Rows are sorted by item name. Quantities are exact (micros, ER-3). A <b>pure</b> projection — no UI, no DB.
/// </summary>
public sealed record ReorderStatus(DateOnly AsOf, IReadOnlyList<ReorderStatusRow> Rows)
{
    /// <summary>Builds the Reorder Status report for the whole company as of <paramref name="asOf"/>.</summary>
    public static ReorderStatus Build(Company company, DateOnly asOf)
    {
        var ledger = new InventoryLedger(company);
        var rows = new List<ReorderStatusRow>();

        foreach (var item in company.StockItems)
        {
            // Resolve the effective reorder level + min order qty for this item (RQ-36): a master definition
            // (item/group/category, most-specific) else the legacy per-item fields (ER-13).
            decimal? level;
            decimal? minQty;
            if (ResolveDefinition(company, item) is { } def)
            {
                level = EffectiveFigure(def.ReorderAdvanced, def.ReorderQuantity, def, ledger, item, asOf);
                minQty = EffectiveFigure(def.MinQtyAdvanced, def.MinOrderQuantity, def, ledger, item, asOf);
            }
            else
            {
                level = item.ReorderLevel;             // legacy Simple reorder level (Phase 3)
                minQty = item.MinimumOrderQuantity;    // legacy Simple min order qty
            }

            if (level is not { } reorderLevel) continue; // no reorder level resolved ⇒ excluded

            var closing = ledger.OnHand(item.Id, asOf);
            var pendingPO = PendingOrderQty(company, item.Id, VoucherBaseType.PurchaseOrder, asOf);
            var soDue = PendingOrderQty(company, item.Id, VoucherBaseType.SalesOrder, asOf);

            var shortfall = reorderLevel - closing;
            if (shortfall < 0m) shortfall = 0m;

            // Order to be Placed (RQ-37): net incoming purchase orders off the shortfall to the reorder level,
            // then floor by the minimum order quantity. An item AT or below its level still orders at least the
            // MOQ (ER-13 / Phase-3 parity: at closing == level the order is the MOQ, not zero); only genuine
            // incoming purchase orders can pull that below the MOQ, down to 0 when they cover the shortfall. An
            // item above its level with nothing incoming needs nothing ordered.
            decimal orderToBePlaced;
            if (closing > reorderLevel)
                orderToBePlaced = 0m;                  // above the level — nothing due (Phase-3 excluded these)
            else
            {
                var netShortfall = shortfall - pendingPO;   // shortfall = max(level − closing, 0) ≥ 0
                orderToBePlaced = netShortfall <= 0m && pendingPO > 0m
                    ? 0m                                // incoming purchase orders already cover the shortfall
                    : minQty is { } mq && mq > netShortfall ? mq : netShortfall;
            }

            // Show every item at/below its level; also show an item that still needs ordering. Skip an item above
            // its level with nothing to order.
            if (closing > reorderLevel && orderToBePlaced <= 0m) continue;

            rows.Add(new ReorderStatusRow(item.Id, item.Name, closing, reorderLevel, minQty,
                pendingPO, soDue, shortfall, orderToBePlaced));
        }

        rows.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase));
        return new ReorderStatus(asOf, rows);
    }

    /// <summary>
    /// Resolves the effective <see cref="ReorderDefinition"/> for an item by specificity (RQ-36): an Item-scoped
    /// definition wins; else the nearest ancestor Group-scoped definition (walk the group tree up to Primary);
    /// else the nearest ancestor Category-scoped definition; else <c>null</c> (the caller falls back to the
    /// legacy per-item fields). Group beats Category (DD-2).
    /// </summary>
    private static ReorderDefinition? ResolveDefinition(Company company, StockItem item)
    {
        if (company.FindReorderDefinition(ReorderScope.Item, item.Id) is { } itemDef)
            return itemDef;

        var groupId = (Guid?)item.StockGroupId;
        while (groupId is { } gid)
        {
            if (company.FindReorderDefinition(ReorderScope.Group, gid) is { } groupDef)
                return groupDef;
            groupId = company.FindStockGroup(gid)?.ParentId;
        }

        var categoryId = item.CategoryId;
        while (categoryId is { } cid)
        {
            if (company.FindReorderDefinition(ReorderScope.Category, cid) is { } catDef)
                return catDef;
            categoryId = company.FindStockCategory(cid)?.ParentId;
        }

        return null;
    }

    /// <summary>
    /// The effective value of one reorder figure (RQ-34/35). Simple ⇒ the fixed typed quantity (may be
    /// <c>null</c> = unset). Advanced ⇒ the item's consumption over the definition's rolling window reconciled
    /// against the fixed quantity by <see cref="ReorderDefinition.Criteria"/> (Higher = max, Lower = min); a null
    /// fixed quantity in Advanced mode yields the consumption figure alone.
    /// </summary>
    private static decimal? EffectiveFigure(bool advanced, decimal? fixedQuantity, ReorderDefinition def,
        InventoryLedger ledger, StockItem item, DateOnly asOf)
    {
        if (!advanced) return fixedQuantity;

        var consumption = ledger.Consumption(item.Id, def.WindowStart(asOf), asOf);
        if (fixedQuantity is not { } fixedQty) return consumption;
        return def.Criteria == ReorderCriteria.Lower
            ? Math.Min(fixedQty, consumption)
            : Math.Max(fixedQty, consumption);
    }

    /// <summary>
    /// Σ ordered quantity (item base unit) of the counting orders of a given base type for an item as of
    /// <paramref name="asOf"/> (RQ-37) — the same counting predicate <see cref="InventoryRegisters.BuildOrders"/>
    /// uses (cancelled never counts; a post-dated order only counts once its date ≤ <paramref name="asOf"/>), so
    /// the figures cannot disagree (ER-4). <b>Known limitation (DD-5):</b> Phase-3 orders carry no fulfilment
    /// link, so a partially-received purchase order still counts its full ordered quantity — identical to the
    /// Order Register's stance. <see cref="OrderLine.Quantity"/> is already in the item's base unit.
    /// </summary>
    internal static decimal PendingOrderQty(Company company, Guid stockItemId, VoucherBaseType baseType, DateOnly asOf)
    {
        var total = 0m;
        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date > asOf) continue;
            if (v.PostDated && v.Date > asOf) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != baseType) continue;
            foreach (var line in v.OrderLines)
                if (line.StockItemId == stockItemId)
                    total += line.Quantity;
        }
        return total;
    }
}
