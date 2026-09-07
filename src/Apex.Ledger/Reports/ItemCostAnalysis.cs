using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of an <see cref="ItemCostAnalysis"/> report (census 9.7) — the vendor's four columns for a stock
/// item, a stock group, or a single cost tracking number.
/// </summary>
/// <param name="Key">The row's stable identity: the stock item id, the stock group id, or
/// <see cref="Guid.Empty"/> for a Cost Track Break-up row (which is identified by its
/// <see cref="TrackingNumber"/> instead).</param>
/// <param name="Name">The display name — item name, group name, or the cost tracking number itself.</param>
/// <param name="TrackingNumber">The cost tracking number, on a Cost Track Break-up row only; <c>null</c>
/// elsewhere.</param>
/// <param name="Cost">The vendor's <b>"Cost (Expense)"</b> — the total value brought IN under this key.</param>
/// <param name="Revenue">The vendor's <b>"Revenue (Income)"</b> — the total value taken OUT under it.</param>
/// <param name="BalanceAtCost">The vendor's <b>"Balance at Cost"</b> — the unconsumed part of the cost, i.e.
/// the value of the quantity still on hand under this key, at the key's own average inward rate.</param>
/// <param name="InwardQuantity">Quantity brought in under this key, in the item's base unit.</param>
/// <param name="OutwardQuantity">Quantity taken out under this key, in the item's base unit.</param>
public sealed record ItemCostAnalysisRow(
    Guid Key,
    string Name,
    string? TrackingNumber,
    Money Cost,
    Money Revenue,
    Money BalanceAtCost,
    decimal InwardQuantity,
    decimal OutwardQuantity)
{
    /// <summary>
    /// The vendor's <b>"Profit/Loss"</b> column — Revenue − (Cost − Balance at Cost).
    /// <para>🔴 <b>The subtraction of the balance is the whole point and is easy to get wrong.</b> Profit is
    /// revenue less the cost of what was actually SOLD, not less everything ever bought. A lot bought for
    /// ₹1,000 of which half is still on the shelf and half sold for ₹700 has made ₹200, not lost ₹300. Omitting
    /// <see cref="BalanceAtCost"/> reports a loss on every lot that is not yet fully sold — which, in a live
    /// book, is most of them.</para>
    /// </summary>
    public Money ProfitOrLoss => Revenue - (Cost - BalanceAtCost);
}

/// <summary>
/// <b>Item Cost Analysis</b> (census 9.7) — the vendor's per-item, per-group and per-tracking-number
/// profitability reports, reached at <i>Statements of Inventory → Item Cost Analysis</i>.
/// </summary>
/// <remarks>
/// <para><b>What the vendor aggregates, and over what.</b> A <i>cost tracking number</i> is described as a
/// unique identifier that monitors the expenses attached to a specific stock quantity <b>throughout its
/// purchase-to-sales cycle</b>. So the unit of aggregation is the tracking number, not the voucher and not the
/// month: the same number is keyed on the purchase that brought a lot in and on the sale that took it out, and
/// the report subtracts one from the other. <see cref="BuildItems"/> and <see cref="BuildGroups"/> then roll
/// those lots up to the item and to the stock group; <see cref="BuildBreakup"/> lists them individually, which
/// is the vendor's <i>Cost Track Break-up</i>.</para>
///
/// <para><b>The period is a cut-off, not a window.</b> Everything is accumulated over vouchers dated on/before
/// <c>asOf</c>. A lot bought in one year and sold in the next must show BOTH halves or its profit is a
/// fiction, so restricting the inward side to a from-date would systematically overstate profit on every lot
/// carried across a year end. The vendor's own framing — the whole purchase-to-sales <i>cycle</i> — is a
/// lifetime, not a window, and that is why this report takes one date and not two.</para>
///
/// <para>🔴 <b>Balance at Cost uses the KEY'S OWN average inward rate, not the item's company-wide
/// valuation.</b> The question this report answers is "what did THIS lot cost me and what did it earn?", so
/// mixing in stock acquired outside the tracking number would make the answer depend on unrelated purchases.
/// Where a key has inward quantity, its unit cost is <see cref="ItemCostAnalysisRow.Cost"/> ÷ inward quantity;
/// the balance is that unit cost × the quantity still unsold under the key. A key that has sold more than it
/// bought (possible with negative stock, which this product permits) has its balance floored at zero rather
/// than reporting a negative asset.</para>
///
/// <para><b>Both stock-line tables contribute.</b> Cost tracking numbers may be keyed on an item-invoice line
/// (<see cref="VoucherInventoryLine.CostTrackingNumber"/>) and on a pure-inventory movement
/// (<see cref="InventoryAllocation.CostTrackingNumber"/>). Only the item-invoice side carries a mandatory
/// rate, so a movement with no rate contributes quantity but no value — which is correct, and is why the
/// quantity columns are reported beside the money rather than derived from it.</para>
///
/// <para><b>R7 — ATTESTED.</b>
/// <c>help.tallysolutions.com/tally-prime/inventory/track-item-cost-tally/</c>: the F11 gate <i>"Enable Cost
/// Tracking"</i>; the <i>List of Cost Tracking Numbers</i> with its <i>New Number</i> entry; the menu route
/// <i>Gateway → Display → More Reports → Statements of Inventory → Item Cost Analysis</i>; and the four column
/// captions <i>"Cost (Expense)"</i>, <i>"Revenue (Income)"</i>, <i>"Balance at Cost"</i> and
/// <i>"Profit/Loss"</i>, which are reproduced here verbatim.</para>
///
/// <para>A <b>pure</b> projection: no UI, no DB, no clock.</para>
/// </remarks>
public static class ItemCostAnalysis
{
    /// <summary>One accumulated cost tracking number, within one stock item.</summary>
    private sealed class Lot
    {
        public decimal InQty;
        public decimal OutQty;
        public Money Cost = Money.Zero;
        public Money Revenue = Money.Zero;

        /// <summary>The unconsumed part of this lot's cost — see the class remarks for why it is the lot's own
        /// average rate and why it is floored at zero.</summary>
        public Money BalanceAtCost
        {
            get
            {
                if (InQty <= 0m) return Money.Zero;
                var remaining = InQty - OutQty;
                if (remaining <= 0m) return Money.Zero;
                return Money.ForexBase(Money.FromRupees(Cost.Amount / InQty), remaining);
            }
        }
    }

    /// <summary><b>Stock Item Cost Analysis</b> — one row per stock item that has any cost-tracked movement.</summary>
    public static IReadOnlyList<ItemCostAnalysisRow> BuildItems(Company company, DateOnly asOf)
    {
        var lots = Accumulate(company, asOf);
        var byItem = new Dictionary<Guid, (decimal In, decimal Out, Money Cost, Money Rev, Money Bal)>();
        foreach (var ((itemId, _), lot) in lots)
            Fold(byItem, itemId, lot);

        var rows = new List<ItemCostAnalysisRow>();
        foreach (var (itemId, t) in byItem)
        {
            var item = company.FindStockItem(itemId);
            rows.Add(new ItemCostAnalysisRow(
                itemId, item?.Name ?? "(unknown)", null, t.Cost, t.Rev, t.Bal, t.In, t.Out));
        }
        return Sorted(rows);
    }

    /// <summary><b>Stock Group Cost Analysis</b> — the same figures rolled up to each item's stock group.
    /// <para>The roll-up is to the item's OWN group, not up the group tree: a nested group's figures are not
    /// added into its parent. Rolling up a hierarchy without also suppressing the children double-counts every
    /// figure, and the vendor page attests a group report, not a group tree, so the smaller honest thing is
    /// shipped.</para></summary>
    public static IReadOnlyList<ItemCostAnalysisRow> BuildGroups(Company company, DateOnly asOf)
    {
        var lots = Accumulate(company, asOf);
        var byGroup = new Dictionary<Guid, (decimal In, decimal Out, Money Cost, Money Rev, Money Bal)>();
        foreach (var ((itemId, _), lot) in lots)
        {
            var item = company.FindStockItem(itemId);
            if (item is null) continue;
            Fold(byGroup, item.StockGroupId, lot);
        }

        var rows = new List<ItemCostAnalysisRow>();
        foreach (var (groupId, t) in byGroup)
        {
            var group = company.FindStockGroup(groupId);
            rows.Add(new ItemCostAnalysisRow(
                groupId, group?.Name ?? "(unknown)", null, t.Cost, t.Rev, t.Bal, t.In, t.Out));
        }
        return Sorted(rows);
    }

    /// <summary><b>Cost Track Break-up</b> — one row per (stock item, cost tracking number), which is the
    /// finest grain the feature has. Optionally narrowed to a single item.</summary>
    public static IReadOnlyList<ItemCostAnalysisRow> BuildBreakup(
        Company company, DateOnly asOf, Guid? stockItemId = null)
    {
        var lots = Accumulate(company, asOf);
        var rows = new List<ItemCostAnalysisRow>();
        foreach (var ((itemId, tracking), lot) in lots)
        {
            if (stockItemId is { } only && itemId != only) continue;
            rows.Add(new ItemCostAnalysisRow(
                itemId, tracking, tracking, lot.Cost, lot.Revenue, lot.BalanceAtCost, lot.InQty, lot.OutQty));
        }
        return Sorted(rows);
    }

    /// <summary>
    /// Accumulates every cost-tracked line in the book, keyed by (stock item, cost tracking number).
    /// <para>Returns EMPTY when <see cref="Company.EnableCostTracking"/> is off. With the feature off the
    /// operator cannot key a number, so any value present is stale data from a period when the flag was on;
    /// reporting profit against it would put figures on screen the operator has no way to correct.</para>
    /// </summary>
    private static Dictionary<(Guid Item, string Tracking), Lot> Accumulate(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var lots = new Dictionary<(Guid Item, string Tracking), Lot>();
        if (!company.EnableCostTracking) return lots;

        // The item-invoice side — the only side that always carries a rate, so the only side that can produce
        // money. Direction decides which column: an inward line is cost, an outward line is revenue.
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var line in v.InventoryLines)
            {
                if (line.CostTrackingNumber is not { } tracking) continue;
                var lot = Slot(lots, line.StockItemId, tracking);
                var qty = BaseQuantity(company, line.StockItemId, line.UnitId, line.Quantity);
                // Value is BilledQuantity × Rate in the LINE's unit and needs no unit conversion — see
                // VoucherInventoryLine.Value, which spells out why converting it would misstate by the factor.
                if (line.Direction == StockDirection.Inward)
                {
                    lot.InQty += qty;
                    lot.Cost += line.Value;
                }
                else
                {
                    lot.OutQty += qty;
                    lot.Revenue += line.Value;
                }
            }
        }

        // The pure-inventory side. A movement's rate is OPTIONAL, so a rate-less movement contributes quantity
        // only; that is why the quantity columns are reported beside the money instead of being derived from it.
        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                if (a.CostTrackingNumber is not { } tracking) continue;
                var lot = Slot(lots, a.StockItemId, tracking);
                var qty = BaseQuantity(company, a.StockItemId, a.UnitId, a.Quantity);
                var value = a.Rate is { } rate ? Money.ForexBase(rate, a.Quantity) : Money.Zero;
                if (a.Direction == StockDirection.Inward)
                {
                    lot.InQty += qty;
                    lot.Cost += value;
                }
                else
                {
                    lot.OutQty += qty;
                    lot.Revenue += value;
                }
            }
        }

        return lots;
    }

    private static Lot Slot(Dictionary<(Guid, string), Lot> lots, Guid itemId, string tracking)
    {
        var key = (itemId, tracking);
        if (!lots.TryGetValue(key, out var lot)) lots[key] = lot = new Lot();
        return lot;
    }

    /// <summary>Adds one lot's figures into a roll-up bucket. 🔴 The BALANCE is folded from each lot's OWN
    /// balance rather than recomputed from the bucket's totals — recomputing would blend a sold-out lot's zero
    /// balance with an untouched lot's full one and silently reprice both at a blended rate.</summary>
    private static void Fold(
        Dictionary<Guid, (decimal In, decimal Out, Money Cost, Money Rev, Money Bal)> map, Guid key, Lot lot)
    {
        // Money is a STRUCT, so a missing key yields default(Money) == Money.Zero and needs no null coalesce.
        map.TryGetValue(key, out var t);
        map[key] = (t.In + lot.InQty, t.Out + lot.OutQty,
            t.Cost + lot.Cost,
            t.Rev + lot.Revenue,
            t.Bal + lot.BalanceAtCost);
    }

    /// <summary>Deterministic total order on every platform: name, then tracking number, then key. 🔴 Every
    /// string comparison is ORDINAL — a culture-sensitive compare orders the same two rows differently on the
    /// ubuntu, macos and windows legs of the gate.</summary>
    private static IReadOnlyList<ItemCostAnalysisRow> Sorted(List<ItemCostAnalysisRow> rows)
    {
        rows.Sort(static (a, b) =>
        {
            var byName = string.CompareOrdinal(a.Name, b.Name);
            if (byName != 0) return byName;
            var byTrack = string.CompareOrdinal(a.TrackingNumber ?? "", b.TrackingNumber ?? "");
            return byTrack != 0 ? byTrack : a.Key.CompareTo(b.Key);
        });
        return rows;
    }

    /// <summary>Converts a line quantity to the stock item's base unit; see
    /// <see cref="BillsPending"/> for why a mixed-unit netting without this manufactures figures.</summary>
    private static decimal BaseQuantity(Company company, Guid stockItemId, Guid? unitId, decimal quantity)
    {
        if (unitId is not { } uid) return quantity;
        var unit = company.FindUnit(uid);
        if (unit is null) return quantity;
        var item = company.FindStockItem(stockItemId);
        if (item is not null && item.BaseUnitId == uid) return quantity;
        return unit.QuantityInBaseMeasure(quantity);
    }
}
