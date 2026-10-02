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
/// ("How to Apply Stock Valuation Methods in TallyPrime | TallyHelp"), retrieved and read 2026-09-21,
/// re-opened and re-verified by content 2026-10-01,
/// documents <b>nine</b> costing methods: At Zero Cost, Average Cost, FIFO (First In, First Out), FIFO
/// Perpetual, Last Purchase Cost, LIFO Annual (Last In, First Out), LIFO Perpetual, Standard Cost, Monthly
/// Average Cost. This build offers <b>six</b> members by name. 🔴 <b>Counted honestly against the page, that is
/// FOUR exact matches</b> (At Zero Cost, Average Cost, Last Purchase Cost, Standard Cost), <b>two documented
/// approximations</b> (<see cref="Fifo"/> and <see cref="Lifo"/> — see the reconciliation in the remarks) and
/// <b>three absent</b>. All five divergences are reported, not silently omitted.</para>
/// </summary>
/// <remarks>
/// <para>The ordinals are stable (persisted as an INTEGER column), so append new methods at the end — never
/// renumber. <see cref="AverageCost"/> is deliberately ordinal 0 so it is the natural default.</para>
///
/// <para>🔴 <b>THE THREE VENDOR COSTING METHODS THIS BUILD STILL DOES NOT SHIP, and why each is withheld
/// rather than approximated.</b>
/// <list type="bullet">
///   <item><b>FIFO Perpetual</b> and <b>LIFO Perpetual</b> — both value "<i>from the time the Company was
///   created</i>", whereas the annual forms reset at the financial year. 🔴 <b>But the span is not the only
///   difference, and the earlier wording here got that wrong.</b> Read by content, the page gives the two
///   perpetual methods DIFFERENT bases: for FIFO Perpetual "<i>the leftover stock will be valued using the last
///   purchase price as the reference</i>" — not the surviving layers' own costs at all — while for LIFO Perpetual
///   "<i>the leftover stock will be valued using the respective purchase cost as the reference</i>". So FIFO
///   Perpetual is nearer to <see cref="LastPurchaseCost"/> than to <see cref="Fifo"/>. Shipping the pair honestly
///   therefore needs both the year-boundary reset on the annual pair AND that basis distinction — see the next
///   paragraph for why that is a user decision, not an implementation detail.</item>
///   <item><b>Monthly Average Cost</b> — "<i>the closing value of each month will be treated as an opening for
///   the next month</i>". Implementable, but it is a genuinely different average from
///   <see cref="AverageCost"/>'s perpetual moving average, and adding it belongs with the FIFO/LIFO span
///   question rather than ahead of it.</item>
/// </list></para>
///
/// <para>🔴 <b>THE FIFO / LIFO RECONCILIATION — THE DOC IS RECONCILED TO THE CODE AND TO THE PAGE; THE CODE IS
/// DELIBERATELY NOT TOUCHED.</b>
/// <see cref="Fifo"/> and <see cref="Lifo"/> are labelled as the vendor's plain "FIFO" and "LIFO Annual", and
/// <c>StockValuationService</c> replays <b>the entire movement history from the opening balances</b> with no
/// financial-year reset.
/// <list type="bullet">
///   <item><b>What that makes <see cref="Lifo"/>:</b> exactly the vendor's <b>LIFO Perpetual</b> basis —
///   full-span replay valued at "<i>the respective purchase cost</i>". It is shipped under the annual name.</item>
///   <item><b>What that does NOT make <see cref="Fifo"/>, correcting the earlier claim in this file that BOTH
///   "behave as the vendor's Perpetual forms":</b> vendor FIFO Perpetual values at "<i>the last purchase
///   price</i>", which a layer replay never produces. Our <see cref="Fifo"/> is the vendor's <b>FIFO (annual)
///   basis</b> ("<i>the respective purchase cost</i>") run over the whole history — so it matches the vendor
///   exactly for a book inside its first financial year, and diverges only across a year boundary, where the page
///   collapses the layers into one average opening cost ("<i>the cost of the opening stock in the new year will be
///   calculated as an average</i>": its own worked example, 51,600 ÷ 100 = 516). The same year-boundary divergence
///   applies to <see cref="Lifo"/> against LIFO Annual (29,500 ÷ 150 = 196.67).</item>
/// </list>
/// So the divergence is <b>precisely the missing financial-year opening-average reset</b>, not a wholesale
/// mislabelling. 🔴 <b>Implementing that reset would move the closing stock value of every multi-year book using
/// FIFO or LIFO</b> — a second wrong-money event of exactly the class ruling 26 was called to settle — so it
/// needs its own user ruling. Recorded here and in the track report; <b>NOTHING in this change alters FIFO or
/// LIFO behaviour.</b></para>
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
