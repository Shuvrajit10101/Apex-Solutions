namespace Apex.Ledger.Domain;

/// <summary>
/// How a <see cref="StockItem"/>'s closing value is derived from its movement history (catalog §9
/// clone-note; requirements RQ-21). The method is stored per item.
///
/// <para><b>This enum is the COSTING dimension only.</b> Its counterpart, <see cref="MarketValuationMethod"/>,
/// derives a selling price and never touches closing stock. The two were one list until schema v65; splitting
/// them is user ruling 26. See <see cref="LastSaleCost"/> for the defect that forced it.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c>
/// ("Costing Methods and Market Valuation Methods | Stock Valuation Methods"), retrieved and read 2026-09-21,
/// documents <b>nine</b> costing methods: At Zero Cost, Average Cost, FIFO (First In, First Out), FIFO
/// Perpetual, Last Purchase Cost, LIFO Annual (Last In, First Out), LIFO Perpetual, Standard Cost, Monthly
/// Average Cost. This build offers <b>six</b> of the nine. The three it does not ship, and the reason, are
/// recorded in the remarks — they are reported divergences, not silent omissions.</para>
/// </summary>
/// <remarks>
/// <para>The ordinals are stable (persisted as an INTEGER column), so append new methods at the end — never
/// renumber. <see cref="AverageCost"/> is deliberately ordinal 0 so it is the natural default.</para>
///
/// <para>🔴 <b>THE THREE VENDOR COSTING METHODS THIS BUILD STILL DOES NOT SHIP, and why each is withheld
/// rather than approximated.</b>
/// <list type="bullet">
///   <item><b>FIFO Perpetual</b> and <b>LIFO Perpetual</b> — the vendor distinguishes these from plain FIFO /
///   LIFO Annual by the span the layers are replayed over: a Perpetual method values "<i>from the time the
///   Company was created</i>", whereas the annual forms reset at the financial year. Shipping the perpetual
///   pair honestly requires a year-boundary reset on the ANNUAL pair — and see the next paragraph for why that
///   is a user decision, not an implementation detail.</item>
///   <item><b>Monthly Average Cost</b> — "<i>the closing value of each month will be treated as an opening for
///   the next month</i>". Implementable, but it is a genuinely different average from
///   <see cref="AverageCost"/>'s perpetual moving average, and adding it belongs with the FIFO/LIFO span
///   question rather than ahead of it.</item>
/// </list></para>
///
/// <para>🔴 <b>A DEFECT FOUND WHILE SOURCING THE ABOVE, REPORTED AND DELIBERATELY NOT FIXED HERE.</b>
/// <see cref="Fifo"/> and <see cref="Lifo"/> are labelled as the vendor's plain "FIFO" and "LIFO Annual", but
/// <c>StockValuationService</c> replays <b>the entire movement history from the opening balances</b> with no
/// financial-year reset — i.e. they behave as the vendor's <b>Perpetual</b> forms. So the two methods this
/// build already ships are arguably mislabelled, and "fixing" the label or the behaviour would move the closing
/// stock value of every book using FIFO or LIFO. That is a second wrong-money event of exactly the class ruling
/// 26 was called to settle, and it needs its own ruling. It is recorded here and in the track report; NOTHING in
/// this change alters FIFO or LIFO behaviour.</para>
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
    /// 🔴 <b>RETIRED ORDINAL — NOT A COSTING METHOD, NOT OFFERED ANYWHERE, AND NO LONGER ABLE TO VALUE STOCK
    /// (census 3.4, defect T0-2, register IV-6; closed by user ruling 26 at schema v65).</b>
    ///
    /// <para><b>What it did.</b> It valued closing stock at the most recent <i>sale</i> rate — our own selling
    /// price. Buy 100 @ ₹100, sell 40 @ ₹150, closing 60: it reported Stock-in-Hand ₹9,000 where every real
    /// costing method reports ₹6,000. The Balance Sheet was overstated by ₹3,000, COGS understated by the same,
    /// and the entire unrealised margin was booked as profit. The vendor files this basis under <b>Market
    /// Valuation</b> (as "Last Sales Price"), where it auto-fills a selling price and touches no asset value;
    /// this application filed it under costing.</para>
    ///
    /// <para><b>What ruling 26 decided.</b> Books that had already chosen it are <b>migrated to a real costing
    /// method</b> and their operator is <b>warned on open</b>; the alternative — grandfathering two valuation
    /// models forever — was put to the user and declined. The target is
    /// <see cref="LastPurchaseCost"/>; <c>Schema.MigrateV64ToV65</c> records why that basis and not another.</para>
    ///
    /// <para>🔴 <b>WHY THE MEMBER SURVIVES AT ALL.</b> Ordinals are persisted, so the value 5 may still be sitting
    /// in a column somewhere the migration did not reach — a database restored from an external archive, a file
    /// hand-edited, a future code path that forgets. Deleting the member would make such a value deserialize to an
    /// unnamed enum and fall through to a <c>default</c> arm. Keeping it named lets
    /// <c>StockValuationService</c> map it <b>explicitly</b> to <see cref="LastPurchaseCost"/>, so stock cannot be
    /// valued at a sale price even by a book that escaped the migration. It appears in no picker and no label.</para>
    /// </summary>
    LastSaleCost = 5,

    /// <summary>
    /// "<i>The value of stock items will always be zero, irrespective of the cost incurred.</i>" — the vendor's
    /// <b>At Zero Cost</b> (same cited page as the enum's own remarks).
    ///
    /// <para>Appended at ordinal 6 because the ordinals are stable; no existing item can carry it, so no existing
    /// book's closing value moves (ER-13). It is exact rather than approximated: closing value is ₹0 with no
    /// fallback chain, which is the one thing the definition leaves no room to interpret.</para>
    /// </summary>
    AtZeroCost = 6,
}
