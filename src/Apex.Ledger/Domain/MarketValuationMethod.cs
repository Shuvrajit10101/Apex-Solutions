namespace Apex.Ledger.Domain;

/// <summary>
/// 🔴 <b>THE MARKET VALUATION DIMENSION — the half of stock valuation this application shipped without
/// (census 3.4, defect T0-2, register IV-6; user ruling 26).</b>
///
/// <para><b>What it is, and what it emphatically is NOT.</b> A market valuation method derives a <b>selling
/// price</b> to auto-fill on a sales line. It <b>never</b> values closing stock and never reaches the Balance
/// Sheet. That separation is the whole point of the dimension: a <i>costing</i> method answers "what did this
/// stock cost us", a <i>market valuation</i> method answers "what do we sell it for". Conflating the two is
/// precisely the defect ruling 26 exists to close — see <see cref="StockValuationMethod"/>'s retired ordinal 5.</para>
///
/// <para><b>R7 — ATTESTED, and the page was opened and read rather than recalled.</b>
/// <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c> ("Costing Methods and Market
/// Valuation Methods | Stock Valuation Methods"), retrieved 2026-09-21. It presents <b>two separate fields</b>
/// and states the distinction in its own words: costing methods "<i>enable you to identify the worth of your
/// business inventory</i>", while market valuation methods "<i>help you to auto-fill the selling price of the
/// items while recording sales</i>". The four members below are that page's Market Valuation list, complete and
/// in its order. <b>Nothing here is invented</b>: every member carries the page's own definition in its doc
/// comment, because an invented valuation basis silently misstates every book that selects it.</para>
///
/// <para><b>The ordinals are stable and persisted</b> (<c>stock_items.market_valuation_method</c>, added by
/// schema v65), so append new members at the end and never renumber. <see cref="AtZeroPrice"/> is deliberately
/// ordinal 0 — see its remarks for why that makes it the only safe default.</para>
/// </summary>
public enum MarketValuationMethod
{
    /// <summary>
    /// "<i>There will be no rate included when recording the voucher</i>" — the operator types every selling
    /// price themselves.
    ///
    /// <para>🔴 <b>Ordinal 0, and therefore the column default for every item that existed before v65 — a
    /// deliberate choice, not a guess at the vendor's factory setting.</b> This build could not source what the
    /// reference product defaults this field to, so it does not assert one. Of the four real methods this is the
    /// only one that needs no data and can state no wrong number: it auto-fills nothing, which is exactly what
    /// every pre-v65 book already did, since the dimension did not exist. So the upgrade changes no sales line
    /// anywhere (ER-13). Defaulting to any of the other three would have invented a selling price for every
    /// existing item in every existing book.</para>
    /// </summary>
    AtZeroPrice = 0,

    /// <summary>
    /// "<i>The average rate at which you will sell the stock item</i>" — total sale amount divided by total
    /// quantity sold.
    /// </summary>
    AveragePrice = 1,

    /// <summary>
    /// "<i>The selling price of the stock item is based on the last price at which the stock item was sold.</i>"
    ///
    /// <para>🔴 <b>THIS IS WHERE THE RETIRED <see cref="StockValuationMethod.LastSaleCost"/> BELONGED ALL
    /// ALONG.</b> The vendor files "Last Sales Price" here, under Market Valuation. This application filed the
    /// same idea under <i>costing</i> and let it value closing stock, which put the whole sales margin onto the
    /// Balance Sheet as asset value. The basis is unchanged and perfectly legitimate — the last rated sale rate;
    /// what changes is that it now auto-fills a <b>price</b> and can no longer value <b>stock</b>.</para>
    /// </summary>
    LastSalesPrice = 2,

    /// <summary>
    /// "<i>Set standard prices for the stock items</i>" after accounting for material costs, production expenses
    /// and overheads — the per-item rate carried in <see cref="StockItem.StandardPrice"/>. When that is unset
    /// this method auto-fills nothing, exactly as <see cref="AtZeroPrice"/> does, rather than substituting a cost.
    /// </summary>
    StandardPrice = 3,
}
