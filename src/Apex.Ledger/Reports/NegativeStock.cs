using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Negative-Stock row (catalog §16 Exception Reports — "negative stock"; RQ-5 part 2): a stock item at a
/// godown whose on-hand quantity is <b>negative</b> as of the report date, with the negative quantity and its
/// (negative) value.
/// </summary>
public sealed record NegativeStockRow(
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    decimal Quantity,
    Money Value);

/// <summary>
/// The Negative Stock exception report (catalog §16; RQ-5 part 2). Lists every (item, godown) pair whose
/// on-hand <see cref="InventoryLedger.OnHand(Guid, Guid, DateOnly)"/> is strictly negative as of
/// <paramref name="asOf"/>. A well-formed book never carries negative stock (the posting services hard-block
/// it — DP-7), so this report is an <b>exception surfacer</b>: it flags data that slipped in via imports or a
/// "negative stock allowed" configuration, exactly as the reference app's Negative Stock report does.
/// <para><b>Valuation.</b> The core <see cref="StockValuationService"/> values only positive closing stock
/// (it returns ₹0 for a non-positive quantity, since FIFO/LIFO layers cannot be consumed below zero). A
/// negative on-hand is therefore valued here at the item's best-available <i>unit</i> cost — its
/// <see cref="StockItem.StandardCost"/> when set, else the most-recent rated inward rate in its movement
/// history (a rated pure-stock <see cref="InventoryVoucher"/> inward, an <b>item-invoice Purchase</b> stock
/// line, or the opening rate) — multiplied by the (negative) quantity and snapped to the paisa. The sign is
/// preserved (a shortfall shows a negative value), matching how the reference app shows a negative closing
/// value for negative stock. A <b>pure</b> projection — no UI, no DB.</para>
/// <para><b>Known limitation — batch-level detection (Phase 6).</b> On-hand is netted across batches within a
/// godown, so a negative batch masked by a larger positive batch in the <i>same</i> godown is not surfaced
/// here. Batch/lot &amp; expiry tracking is a Phase-6 feature; batch-level negative-stock detection is deferred
/// until that engine lands (per plan.md). Godown-level and item-level negatives are surfaced correctly today.</para>
/// Rows are sorted by item name then godown name.
/// </summary>
public sealed record NegativeStock(
    DateOnly AsOf,
    IReadOnlyList<NegativeStockRow> Rows)
{
    /// <summary>Builds the Negative Stock report for the whole company as of <paramref name="asOf"/>.</summary>
    public static NegativeStock Build(Company company, DateOnly asOf)
    {
        var onHand = new InventoryLedger(company);
        var rows = new List<NegativeStockRow>();

        foreach (var item in company.StockItems)
        {
            var unitCost = ReferenceUnitCost(company, item, asOf);
            foreach (var godown in company.Godowns)
            {
                var qty = onHand.OnHand(item.Id, godown.Id, asOf);
                if (qty >= 0m) continue; // only genuine shortfalls are exceptions

                // Value the shortfall at the reference unit cost, sign preserved (negative qty ⇒ negative value).
                var value = new Money(decimal.Round(qty * unitCost, 2, MidpointRounding.AwayFromZero));
                rows.Add(new NegativeStockRow(item.Id, item.Name, godown.Id, godown.Name, qty, value));
            }
        }

        rows.Sort((a, b) =>
        {
            var byItem = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
            return byItem != 0 ? byItem : string.Compare(a.GodownName, b.GodownName, StringComparison.OrdinalIgnoreCase);
        });
        return new NegativeStock(asOf, rows);
    }

    /// <summary>
    /// The best-available per-unit cost to value a negative on-hand: the item's <see cref="StockItem.StandardCost"/>
    /// when set, else its most-recent rated inward/purchase rate on/before <paramref name="asOf"/> — considering
    /// BOTH pure-stock <see cref="InventoryVoucher"/> inwards AND <b>item-invoice</b> (Purchase-voucher) stock
    /// lines, plus the opening rate — else ₹0. Considering item-invoice purchases matters: an item whose only
    /// inward movements are item-invoice purchases (Phase-3 item-invoice mode) would otherwise value its negative
    /// on-hand at ₹0. Deterministic; used only for the (rare) negative-stock exception case.
    /// </summary>
    private static decimal ReferenceUnitCost(Company company, StockItem item, DateOnly asOf)
    {
        if (item.StandardCost is { } sc) return sc.Amount;

        decimal? lastRate = null;
        DateOnly lastDate = DateOnly.MinValue;

        // Opening-balance rate (earliest cost signal).
        foreach (var ob in company.StockOpeningBalances)
            if (ob.StockItemId == item.Id && ob.Rate.Amount > 0m)
                lastRate = ob.Rate.Amount; // opening is the earliest; a later rated inward overrides below

        // Most-recent rated inward allocation on/before asOf (pure-stock inventory vouchers).
        foreach (var iv in company.InventoryVouchers)
        {
            if (iv.Cancelled || iv.Date > asOf) continue;
            foreach (var a in iv.Allocations.Concat(iv.DestinationAllocations))
            {
                if (a.StockItemId != item.Id || a.Direction != StockDirection.Inward) continue;
                if (a.Rate is { } rate && rate.Amount > 0m && iv.Date >= lastDate)
                {
                    // The allocation's rate is PER THE LINE'S OWN UNIT, while the quantity this cost is
                    // multiplied by is base-normalised (InventoryLedger.OnHand) — so it must be re-expressed
                    // per base unit or the shortfall inflates by the conversion factor ("2 Doz @ ₹10" would
                    // value each Nos at ₹10 instead of ₹0.8333…, a 12× overstatement).
                    lastRate = RateInBase(company, a, rate.Amount);
                    lastDate = iv.Date;
                }
            }
        }

        // Most-recent rated inward from an ITEM-INVOICE Purchase voucher on/before asOf. ItemInvoiceStock honours
        // the same as-of / cancelled / optional / post-dated conventions the valuation engine uses, and an
        // item-invoice line always carries a positive rate. This is the fix that stops an item-invoice-purchased
        // item from being valued at ₹0 when it goes negative.
        foreach (var m in Services.ItemInvoiceStock.Movements(company, asOf))
        {
            var a = m.Allocation;
            if (a.StockItemId != item.Id || a.Direction != StockDirection.Inward) continue;
            if (a.Rate is { } rate && rate.Amount > 0m && m.Date >= lastDate)
            {
                // WI-10 Gap 2: an item-invoice line can now carry its own unit, and the shortfall this rate
                // values is a BASE-unit quantity (InventoryLedger.OnHand) — so the rate is re-expressed per
                // base unit, exactly as the pure-stock branch above does. A unit-less line is unchanged (ER-13).
                lastRate = RateInBase(company, a, rate.Amount);
                lastDate = m.Date;
            }
        }

        return lastRate ?? 0m;
    }

    /// <summary>
    /// An allocation's rate re-expressed per the item's BASE unit (WI-10 slice C; see
    /// <see cref="Unit.RateInBaseMeasure"/>). A line states its rate per the unit the LINE is in, so pairing it
    /// with a base-normalised quantity requires dividing by exactly the factor the quantity was multiplied by.
    /// Opening balances carry no line unit, so they are already per-base. Since WI-10 Gap 2 an ITEM-INVOICE
    /// line can carry one too (ItemInvoiceStock stamps it on the synthetic allocation), so both callers below
    /// route through here.
    /// </summary>
    private static decimal RateInBase(Company company, InventoryAllocation a, decimal rate)
    {
        if (a.UnitId is not { } unitId) return rate;
        var unit = company.FindUnit(unitId);
        return unit is null ? rate : unit.RateInBaseMeasure(rate);
    }
}
