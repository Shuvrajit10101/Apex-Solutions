using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Reorder Status row (catalog §11; requirements RQ-37; Tally-Book p.161): a stock item resolved to an
/// effective reorder level, with its closing quantity, the pending purchase orders, the sales orders due, the
/// derived <see cref="NettAvailable"/>, the shortfall and the load-bearing <see cref="OrderToBePlaced"/>
/// quantity (which pre-fills a real Purchase Order from the report).
/// <para><see cref="NettAvailable"/> is appended LAST deliberately: the record is positional, so appending
/// keeps every existing reader source-compatible. Presenting it at the published column position (between
/// Sales Orders Due and Re-order Level) is a presentation concern, not an engine one.</para>
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
    decimal OrderToBePlaced,
    decimal NettAvailable);

/// <summary>
/// The Reorder Status report (catalog §11; requirements RQ-32..RQ-37; Tally-Book pp.158–164) — <b>every</b>
/// stock item that resolves to an <b>effective reorder level</b> as of <c>asOf</c>, with the quantity it still
/// needs ordered. Compared with the Phase-3 basic report this refactor adds the full master model (ER-5, one
/// engine, not a parallel one):
/// <list type="bullet">
///   <item><b>Master definitions (RQ-32/RQ-36).</b> A <see cref="ReorderDefinition"/> may be attached per Item,
///     Group or Category. Each item resolves the most-specific one: an Item definition wins, else the nearest
///     ancestor Group definition, else the nearest ancestor Category definition, else the legacy per-item
///     <see cref="StockItem.ReorderLevel"/>/<see cref="StockItem.MinimumOrderQuantity"/> (backward-compat, ER-13),
///     else the item is excluded.</item>
///   <item><b>Simple vs Advanced (RQ-33/34/35).</b> Each figure is a fixed typed quantity (Simple) or derived
///     from the item's <see cref="InventoryLedger.Consumption"/> over a rolling period reconciled Higher/Lower
///     against the fixed figure (Advanced).</item>
///   <item><b>Nett Available (RQ-37).</b> <c>Closing + PendingPurchaseOrders − SalesOrdersDue</c>. Verbatim,
///     TallyPrime's Reorder Status help page: Nett Available "displays the stock available for each stock item
///     after considering the purchase orders and sales orders. It is basically derived from adding the pending
///     purchase order to the closing stock and minusing the sales order."
///     [help.tallysolutions.com/reorder-stock-items-reorder-status-and-reorder-quantity/]
///     <para>🔴 Both order figures are the quantity <b>still outstanding</b>
///     (<see cref="OrderFulfilment.OutstandingByItem"/>), never the raw ordered quantity — "pending" and "due"
///     are what the help page's own column names say. A delivered sales order is retired and stops suppressing
///     availability; a received purchase order is retired and stops being counted a second time on top of the
///     closing stock it is already inside. Reading the raw quantities here was DD-5, and it moved a real
///     supplier purchase order (Ctrl+F9) by the whole fulfilled history of the item.</para></item>
///   <item><b>Shortfall (RQ-37).</b> Measured against Nett Available, never against closing stock alone: "Only
///     when the quantity in Re-order Level column is more than the Nett Available column, the difference
///     appears as Shortfall." [ibid.] So an item sitting above its level is still short if open sales orders
///     have committed the stock away — that item is exactly the one a buyer must see.</item>
///   <item><b>Order to be Placed (RQ-37).</b> Both published branches, verbatim: "When the Shortfall is more
///     than the Min Order Quantity, the quantity displayed in Shortfall column appears under Order to be
///     Placed" and "When the Shortfall is less than the Min Order Quantity, the quantity displayed in Min Order
///     Quantity appears under Order to be Placed." [ibid.] Both branches presuppose a shortfall, so a <b>nil
///     shortfall orders nothing</b>: [CORPUS-BOOK p.164] shows an item whose MOQ is 25 printing an EMPTY Order
///     to be Placed once its requirement is covered — "Because We Have ordered already". Pending purchase
///     orders are <b>not</b> subtracted here; they are already inside Nett Available.
///     <para>🔴 This retires the former ER-13 / hard-gate PR-8 rule ("at Closing == Level the order is the
///     MinOrderQty, not zero") and the former DD-4 rule ("Sales Orders Due is shown for context but is not
///     netted"). Both were invented — neither appears in any Tally source — and both were RETIRED BY USER
///     DECISION under Phase 10.10 / WF-7 (register row IV-10) on the citations above. Do not restore either
///     without a new decision.</para></item>
/// </list>
/// <b>No listing filter beyond the reorder level itself.</b> An item that resolves no reorder level is excluded;
/// everything else is listed, whatever its closing quantity and whether or not it needs ordering — "By default,
/// all stock items from the selected stock group or category display… press F8 (Reorder Only)" [ibid.], and
/// [CORPUS-BOOK pp.163–164] shows a fully-covered item still on screen with F8 active. Narrowing the list is the
/// operator's F8, not the engine's.
/// Rows are sorted by item name. Quantities are exact (micros, ER-3). A <b>pure</b> projection — no UI, no DB.
/// </summary>
public sealed record ReorderStatus(DateOnly AsOf, IReadOnlyList<ReorderStatusRow> Rows)
{
    /// <summary>Builds the Reorder Status report for the whole company as of <paramref name="asOf"/>.</summary>
    public static ReorderStatus Build(Company company, DateOnly asOf)
    {
        var ledger = new InventoryLedger(company);
        var rows = new List<ReorderStatusRow>();

        // Both order books, netted to what is STILL OUTSTANDING (WF-8). Built ONCE for the whole report rather
        // than per item, because OrderFulfilment.OutstandingByItem rebuilds the WHOLE fulfilment map on every
        // call — one call enumerates every movement in the book once and scans the order book three times (twice
        // inside Build, once per order arm, then again to total the remainders). Hoisting makes that two map
        // rebuilds for the report; calling it inside the item loop below would make it 2 × N.
        var pendingPurchaseByItem =
            OrderFulfilment.OutstandingByItem(company, VoucherBaseType.PurchaseOrder, asOf);
        var salesDueByItem =
            OrderFulfilment.OutstandingByItem(company, VoucherBaseType.SalesOrder, asOf);

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
            // A missing key means "nothing outstanding for this item", which is 0 — see
            // OrderFulfilment.OutstandingByItem, which omits fully-retired items rather than carrying zeroes.
            var pendingPO = pendingPurchaseByItem.TryGetValue(item.Id, out var po) ? po : 0m;
            var soDue = salesDueByItem.TryGetValue(item.Id, out var so) ? so : 0m;

            // Nett Available: the stock actually available once both order books are taken into account.
            var nettAvailable = closing + pendingPO - soDue;

            // Shortfall: only a level ABOVE the nett available produces one.
            var shortfall = reorderLevel > nettAvailable ? reorderLevel - nettAvailable : 0m;

            // Order to be Placed: nothing at all when there is no shortfall; otherwise the shortfall, floored by
            // the minimum order quantity. Pending purchase orders are NOT subtracted again here — they are
            // already inside nettAvailable, and subtracting them twice is the pre-10.10 double-count.
            var orderToBePlaced = shortfall <= 0m
                ? 0m
                : minQty is { } mq && mq > shortfall ? mq : shortfall;

            rows.Add(new ReorderStatusRow(item.Id, item.Name, closing, reorderLevel, minQty,
                pendingPO, soDue, shortfall, orderToBePlaced, nettAvailable));
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

    // ------------------------------------------------------------------ the order books
    //
    // 🔴 THE PER-ITEM ORDER SUM USED TO LIVE HERE, AS `internal static decimal PendingOrderQty(...)`, AND IT WAS
    // THE DD-5 DEFECT. It summed the RAW ordered quantity of every counting order, so nothing ever retired an
    // order: a sales order delivered in full still reported its whole quantity due, for ever, and the error was
    // the entire delivered-order history — unbounded, and growing with every ordinary order-to-delivery cycle.
    // Pre-10.10 that produced two wrong display columns; post-10.10 both books feed NettAvailable → Shortfall →
    // OrderToBePlaced, which the shell carries straight into a real supplier Purchase Order on Ctrl+F9, so the
    // stale sum moved money. Direction of the error, for anyone reading an old book: a stale SALES order
    // OVERSTATED the shortfall and re-ordered stock already shipped; a stale PURCHASE order double-counted goods
    // already on the shelf and UNDER-ordered by the received quantity.
    //
    // It is gone. Build reads OrderFulfilment.OutstandingByItem for both base types instead, which is the figure
    // TallyPrime's "Purc Orders Pending" and "Sales Orders Due" columns mean. Two consequences worth stating:
    //
    //   * ER-4 is now STRUCTURAL, not conventional. The Order Register (InventoryRegisters.BuildOrders) derives
    //     its Outstanding column from the same OrderFulfilment map, so the two reports cannot drift apart on
    //     either WHICH ORDERS COUNT (OrderFulfilment.CountsAsOf carries the cancelled/post-dated predicate) or
    //     HOW MUCH IS LEFT. Pinned by Order_register_and_reorder_status_agree_on_a_partly_delivered_order, which
    //     reads both projections off one book at a PARTLY delivered order, so the agreement cannot be an
    //     all-or-nothing coincidence at zero.
    //   * The attribution behind "outstanding" is a DERIVED, STATED ENGINEERING CHOICE (FIFO within a
    //     (party, stock item) cohort, with a blank party a cohort of its own rather than a wildcard), not a
    //     sourced TallyPrime behaviour, and its limits — a note left blank does not retire a party-named order,
    //     REJECTIONS do not retire or un-retire an order, godown is not matched — are enumerated on
    //     OrderFulfilment. 🔴 An earlier revision of this note also listed "item-invoices" among the documents
    //     that do not retire an order. That is NO LONGER TRUE and the clause has been deleted rather than
    //     softened: a Purchase or Sales ITEM INVOICE is a fulfilling door on both arms (corpus-sourced —
    //     [CORPUS-TALLY-BOOK(719244897) pp.15, 18]), which is what makes these two columns correct for a trading
    //     book that bills its goods directly and raises no Delivery Note at all.
    //     Read that class doc before trusting a figure in either column. Still owed to the post-merge
    //     documentation slice: a register row for the derivation, and the user go/no-go on whether a derived
    //     match may retire an order at all.
}
