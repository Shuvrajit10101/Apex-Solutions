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

    /// <summary>The most recent sale rate.</summary>
    LastSaleCost = 5,
}
