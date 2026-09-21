using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// 🔴 <b>THE MARKET VALUATION ENGINE (census 3.4, user ruling 26) — it derives a SELLING PRICE and nothing
/// else.</b>
///
/// <para>Given a stock item's <see cref="MarketValuationMethod"/>, this produces the rate that should auto-fill
/// on a sales line. <b>No figure it returns reaches closing stock, the Balance Sheet or Profit &amp; Loss.</b>
/// That boundary is the entire reason the dimension exists, and it is asserted by a dedicated test rather than
/// merely promised here: the defect this closes (T0-2) was exactly a selling price leaking into an asset value.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c>
/// ("Costing Methods and Market Valuation Methods | Stock Valuation Methods"), retrieved and read 2026-09-21:
/// market valuation methods "<i>help you to auto-fill the selling price of the items while recording sales</i>".
/// Each method below implements that page's own definition, quoted at its call site in
/// <see cref="MarketValuationMethod"/>.</para>
///
/// <para><b>What counts as a sale here, stated because it is a judgement and not a quotation.</b> The rates are
/// taken from <b>Sales</b> vouchers' item lines only — outward lines that carry an explicit rate, on vouchers
/// that are not cancelled, not optional, and dated on or before the as-of date (the same counting rule the books
/// themselves use, mirrored from <see cref="ItemInvoiceStock"/>). Two exclusions are deliberate:
/// <list type="bullet">
///   <item><b>Stock-journal and other pure-stock outwards are NOT sales.</b> An issue to production has a cost
///   rate on it, not a selling price; averaging it into a selling price would quietly drag the auto-fill down
///   toward cost. This is the same category error as T0-2 running in the opposite direction.</item>
///   <item><b>Credit Notes (sales returns) are not netted off.</b> The cited page defines Average Price as total
///   sale amount over total quantity sold and says nothing about returns, so this build does not invent a
///   netting rule. The consequence is stated rather than hidden: a heavily-returned item's Average Price reads
///   higher than the quantity actually retained by the customer would give. It is an auto-filled suggestion the
///   operator can overtype, never a posted figure.</item>
/// </list></para>
///
/// <para>Pure and deterministic — no Avalonia, DB, clock or RNG — and paisa-exact, like the accounting core.</para>
/// </summary>
public sealed class MarketValuationService
{
    private readonly Company _company;

    public MarketValuationService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// The selling rate to auto-fill for an item as of a date, under the item's own
    /// <see cref="StockItem.MarketValuationMethod"/> — or <c>null</c> when the method yields no price.
    ///
    /// <para><b><c>null</c> is a first-class, expected answer, not a failure.</b>
    /// <see cref="MarketValuationMethod.AtZeroPrice"/> always returns it (that IS the method: the operator types
    /// the price), and the other three return it when the book holds nothing to compute from — an item never
    /// sold, or a standard price never set. 🔴 <b>Nothing here falls back to a COST.</b> Substituting a cost rate
    /// for a missing selling price would auto-fill an invoice at cost and silently sell at zero margin, which is
    /// a worse failure than auto-filling nothing at all.</para>
    /// </summary>
    public Money? SellingRate(Guid stockItemId, DateOnly asOf)
    {
        var item = _company.FindStockItem(stockItemId)
            ?? throw new InvalidOperationException($"Stock item {stockItemId} not found.");

        return item.MarketValuationMethod switch
        {
            MarketValuationMethod.AtZeroPrice => null,
            MarketValuationMethod.LastSalesPrice => LastSalesPrice(stockItemId, asOf),
            MarketValuationMethod.AveragePrice => AveragePrice(stockItemId, asOf),
            MarketValuationMethod.StandardPrice => item.StandardPrice,
            _ => null,
        };
    }

    /// <summary>
    /// "<i>The selling price of the stock item is based on the last price at which the stock item was sold.</i>"
    /// The most recent rated sale in replay order, or <c>null</c> when the item was never sold at a rate.
    /// </summary>
    private Money? LastSalesPrice(Guid stockItemId, DateOnly asOf)
    {
        Money? last = null;
        foreach (var s in SaleLines(stockItemId, asOf))
            last = new Money(s.Rate).RoundToPaisa();
        return last;
    }

    /// <summary>
    /// "<i>The average rate at which you will sell the stock item</i>" — total sale amount ÷ total quantity sold,
    /// paisa-exact. <c>null</c> when nothing was sold at a rate (never a divide-by-zero, never a silent ₹0).
    /// </summary>
    private Money? AveragePrice(Guid stockItemId, DateOnly asOf)
    {
        var amount = 0m;
        var quantity = 0m;
        foreach (var s in SaleLines(stockItemId, asOf))
        {
            amount += s.Rate * s.Quantity;
            quantity += s.Quantity;
        }
        if (quantity <= 0m) return null;
        return new Money(amount / quantity).RoundToPaisa();
    }

    /// <summary>
    /// The item's rated <b>Sales</b> outward lines as of a date, in the books' own replay order
    /// (date → voucher number → id), with quantity and rate both re-expressed per the item's BASE unit.
    ///
    /// <para>🔴 <b>Quantity and rate are converted through the SAME factor</b>, exactly as
    /// <c>StockValuationService</c> does. Normalising one and not the other is the 12× error that WI-10 Gap 2
    /// records; here it would mis-scale an auto-filled invoice rate rather than a stock value, which an operator
    /// is even less likely to catch.</para>
    /// </summary>
    private IEnumerable<(decimal Quantity, decimal Rate)> SaleLines(Guid stockItemId, DateOnly asOf)
    {
        var rows = new List<(DateOnly Date, int Number, Guid Id, decimal Quantity, decimal Rate)>();

        foreach (var v in _company.Vouchers)
        {
            if (!ItemInvoiceStock.Counts(_company, v, asOf)) continue;

            // Sales only — see this class's remarks for why a stock-journal issue and a Credit Note are both
            // excluded. FindVoucherType is re-read here rather than trusted from Counts, which does not expose it.
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != VoucherBaseType.Sales) continue;

            foreach (var line in v.InventoryLines)
            {
                if (line.StockItemId != stockItemId) continue;
                if (line.Direction != StockDirection.Outward) continue;

                // "Carries a selling rate" is `> 0`, not `is not null`: a VoucherInventoryLine's Rate is
                // NON-nullable (unlike a pure-stock InventoryAllocation's), so "unrated" can only mean ZERO.
                //
                // ⚠️ THIS GUARD IS DEFENSIVE AND IS NOT REACHABLE THROUGH THE POSTING PATH — said plainly rather
                // than left to look like tested behaviour. VoucherValidator.EnsureItemInvoiceValid already
                // refuses any item-invoice line whose rate is not > 0 ("a zero-rate line would move stock with no
                // accounting backing"), so no posted Sales voucher can contain one. It is kept for data that did
                // not come through the validator — an imported or hand-edited book — where a ₹0 line would drag
                // Average Price toward zero and could make Last Sales Price auto-fill ₹0 on the next invoice,
                // i.e. give the goods away: the opposite-signed twin of the T0-2 defect this class closes.
                // There is deliberately NO test asserting it, because no fixture can legitimately construct one.
                if (line.Rate.Amount <= 0m) continue;

                var qty = line.Quantity;
                var unitRate = line.Rate.Amount;
                if (line.UnitId is { } unitId && _company.FindUnit(unitId) is { } unit)
                {
                    qty = unit.QuantityInBaseMeasure(qty);
                    unitRate = unit.RateInBaseMeasure(unitRate);
                }
                if (qty <= 0m) continue;

                rows.Add((v.Date, v.Number, v.Id, qty, unitRate));
            }
        }

        return rows
            .OrderBy(r => r.Date).ThenBy(r => r.Number).ThenBy(r => r.Id)
            .Select(r => (r.Quantity, r.Rate));
    }
}
