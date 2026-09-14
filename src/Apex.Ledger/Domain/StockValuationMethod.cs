namespace Apex.Ledger.Domain;

/// <summary>
/// How a <see cref="StockItem"/>'s closing value is derived from its movement history (catalog §9
/// clone-note; requirements RQ-21). The method is stored per item; the full valuation <i>computation</i>
/// (FIFO/LIFO layering, running weighted average, etc.) is a later slice — this slice only persists the
/// chosen method. A new item defaults to <see cref="AverageCost"/> (DP-1, user-approved).
/// </summary>
/// <remarks>
/// The ordinals are stable (persisted as an INTEGER column), so append new methods at the end — never
/// renumber. <see cref="AverageCost"/> is deliberately ordinal 0 so it is the natural default.
/// </remarks>
public enum StockValuationMethod
{
    /// <summary>Running weighted average cost — the order-insensitive default (DP-1).</summary>
    AverageCost = 0,

    /// <summary>First-In-First-Out: consume the oldest cost layers first.</summary>
    Fifo = 1,

    /// <summary>Last-In-First-Out: consume the newest cost layers first.</summary>
    Lifo = 2,

    /// <summary>A fixed standard rate carried on the item, independent of actual movements.</summary>
    StandardCost = 3,

    /// <summary>The most recent purchase rate.</summary>
    LastPurchaseCost = 4,

    /// <summary>
    /// 🔴 <b>NOT A COSTING METHOD. This is a MARKET VALUATION method, misfiled here — census 3.4, defect T0-2,
    /// register IV-6.</b> It values closing stock at the most recent SALE rate, i.e. at our own selling price.
    ///
    /// <para><b>The vendor's taxonomy, measured 2026-09-15 and cited (ruling 14).</b> help.tallysolutions.com,
    /// "How to Apply Stock Valuation Methods in TallyPrime", offers <b>two separate fields</b>:
    /// <b>nine Costing Methods</b> — At Zero Cost, Average Cost, FIFO (First In, First Out), FIFO Perpetual,
    /// Last Purchase Cost, LIFO Annual, LIFO Perpetual, Standard Cost, Monthly Average Cost — and
    /// <b>four Market Valuation Methods</b> — At Zero Price, Average Price, <b>Last Sales Price</b>, Standard
    /// Price. Market valuation exists to auto-fill a SELLING price, never to value stock on the Balance Sheet.
    /// This enum carries six values, all in the costing slot, and one of them is a market-valuation method.</para>
    ///
    /// <para><b>What it costs the operator.</b> Buy 100 @ ₹100, sell 40 @ ₹150, closing 60: this method reports
    /// Stock-in-Hand ₹9,000 where every real costing method reports ₹6,000 — the Balance Sheet overstated ₹3,000,
    /// COGS understated by the same, and unrealised margin booked as profit.</para>
    ///
    /// <para>🔴 <b>WHY IT IS STILL HERE, and what it is waiting on.</b> Moving it out is not a code edit: there is
    /// no Market Valuation field to move it TO (<c>grep -rn "MarketValuation" src/</c> → zero), so the fix needs a
    /// stored column AND a user ruling on what happens to books that already chose it — their closing stock would
    /// change the day it moves. Both are out of scope here and are reported for a later wave. What IS done without
    /// storage is the label: the picker no longer calls it a <i>cost</i>, so an operator can see it is a
    /// selling-price basis before choosing it (<c>StockItemMasterViewModel.ValuationMethodDisplay</c>).</para>
    /// </summary>
    LastSaleCost = 5,
}
