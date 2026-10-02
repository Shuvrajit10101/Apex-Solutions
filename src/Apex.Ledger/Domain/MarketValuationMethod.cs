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
/// <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c> ("How to Apply Stock Valuation
/// Methods in TallyPrime | TallyHelp"), retrieved 2026-09-21, re-opened and re-verified by content 2026-10-01. It presents <b>two separate fields</b>
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
    /// "<i>If you select this valuation method, by default there will be no rate included when recording the
    /// voucher. You have to decide the price at which you should sell each item and key in the rate.</i>"
    ///
    /// <para>🔴 <b>Ordinal 0, and therefore the column default for every item that existed before v65.</b> Of the
    /// four methods this is the only one that needs no data and can state no wrong number: it auto-fills nothing,
    /// which is exactly what every pre-v65 book already did, since the dimension did not exist. So the upgrade
    /// changes no sales line anywhere (ER-13). Defaulting to any of the other three would have invented a selling
    /// price for every existing item in every existing book.</para>
    ///
    /// <para>🔴 <b>AND THIS IS A LABELLED DIVERGENCE, NOT A MATCH — THE EARLIER CLAIM THAT THE VENDOR'S DEFAULT
    /// "COULD NOT BE SOURCED" WAS WRONG AND IS CORRECTED HERE.</b> The cited page states it twice and plainly:
    /// "<i>In TallyPrime, the Average Price is used as the default market valuation method. You can change this as
    /// desired</i>", and again under Average Price itself, "<i>In TallyPrime, this is the default valuation method
    /// for deciding the price at which stock items may be sold</i>". So the vendor's default is
    /// <see cref="AveragePrice"/> and this build's is <c>AtZeroPrice</c>. The divergence is kept <b>for the
    /// migration</b> — a persisted column cannot default to a method that would retro-price every existing item —
    /// but <b>what a NEWLY created item should default to is a live user decision</b>, recorded in the track
    /// report rather than silently decided here. Note also, for the costing dimension, that this build's
    /// <see cref="StockValuationMethod.AverageCost"/> ordinal-0 default DOES match the page
    /// ("<i>In TallyPrime, Average Cost is selected as the default costing method</i>").</para>
    /// </summary>
    AtZeroPrice = 0,

    /// <summary>
    /// "<i>the average rate at which you will sell the stock item</i>", computed as the page computes it:
    /// "<i>dividing the total amount at which the stock items were sold by the total quantity of the item sold so
    /// far</i>".
    /// </summary>
    AveragePrice = 1,

    /// <summary>
    /// "<i>Under this method, the selling price of the stock item is based on the last price at which the stock
    /// item was sold.</i>" The page also attests the behaviour this build wires it to, in its own words:
    /// "<i>the price at which the sneakers were sold earlier will be prefilled in the voucher. You can change the
    /// selling price as required.</i>"
    ///
    /// <para>🔴 <b>THIS IS WHERE THE RETIRED <see cref="StockValuationMethod.LastSaleCost"/> BELONGED ALL
    /// ALONG.</b> The vendor files "Last Sales Price" here, under Market Valuation. This application filed the
    /// same idea under <i>costing</i> and let it value closing stock, which put the whole sales margin onto the
    /// Balance Sheet as asset value. The basis is unchanged and perfectly legitimate — the last rated sale rate;
    /// what changes is that it now auto-fills a <b>price</b> and can no longer value <b>stock</b>.</para>
    /// </summary>
    LastSalesPrice = 2,

    /// <summary>
    /// "<i>You can set standard prices for the stock items. The standard price of an item is set after adding
    /// charges like the cost of raw materials, production costs and other overhead charges that were required to
    /// make the final product.</i>" (The earlier paraphrase here said "production expenses", a phrase that appears
    /// nowhere on the page; it is replaced by the page's own wording.) The rate is the per-item figure carried in
    /// <see cref="StockItem.StandardPrice"/>. When that is unset this method auto-fills nothing, exactly as
    /// <see cref="AtZeroPrice"/> does, rather than substituting a cost.
    /// </summary>
    StandardPrice = 3,
}
