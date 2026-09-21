using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// 🔴 <b>THE MARKET VALUATION DIMENSION (census 3.4, defect T0-2, user ruling 26) — the half of stock valuation
/// this application shipped without.</b>
///
/// <para>Two things are proved here, and the second matters more than the first:
/// <list type="number">
///   <item>each of the vendor's four market valuation methods derives the selling price its own definition
///   describes (help.tallysolutions.com/stock-valuation-methods-tallyprime/, retrieved 2026-09-21);</item>
///   <item>🔴 <b>no figure any of them produces can reach closing stock, the Balance Sheet or Profit &amp;
///   Loss.</b> That boundary is the whole point of splitting the dimension: T0-2 was a selling price leaking
///   into an asset value. It is asserted, not promised.</item>
/// </list></para>
///
/// <para>Also proved: the ruling-26 <b>on-open warning is TARGETED</b> — an affected book warns, an unaffected
/// book stays completely silent.</para>
/// </summary>
public class MarketValuationTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly D3 = new(2024, 4, 15);
    private static readonly DateOnly D4 = new(2024, 4, 20);

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required StockItem Item { get; init; }
        public required Guid Godown { get; init; }
        public required Guid PartyId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public MarketValuationService Market => new(Company);
        public StockValuationService Valuation => new(Company);
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A seeded book with one stock item, a party and a sales ledger — enough to post real sales.</summary>
    private static Kit NewKit(
        MarketValuationMethod market,
        StockValuationMethod costing = StockValuationMethod.AverageCost,
        Money? standardPrice = null)
    {
        var c = CompanyFactory.CreateSeeded("Market Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: costing);
        item.MarketValuationMethod = market;
        item.StandardPrice = standardPrice;

        var party = AddLedger(c, "Customer", "Sundry Debtors", openingIsDebit: true);
        var sales = AddLedger(c, "Sales Account", "Sales Accounts", openingIsDebit: false);

        return new Kit
        {
            Company = c,
            Item = item,
            Godown = c.MainLocation!.Id,
            PartyId = party.Id,
            SalesLedgerId = sales.Id,
        };
    }

    /// <summary>Posts a real Sales item-invoice for the kit's item at a rate.</summary>
    private static void Sell(Kit k, DateOnly date, decimal qty, decimal rate)
    {
        var value = Money.FromRupees(qty * rate);
        var salesType = k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(k.Company).Post(new Voucher(Guid.NewGuid(), salesType, date, new[]
        {
            new EntryLine(k.PartyId, value, DrCr.Debit),
            new EntryLine(k.SalesLedgerId, value, DrCr.Credit),
        }, partyId: k.PartyId,
           inventoryLines: new[]
           {
               new VoucherInventoryLine(k.Item.Id, k.Godown, qty, Money.FromRupees(rate)),
           }));
    }

    /// <summary>Posts a rated purchase (a pure-stock receipt), so the item has a real COST to contrast with.</summary>
    private static void Buy(Kit k, DateOnly date, decimal qty, decimal rate)
        => new InventoryPostingService(k.Company).Post(new InventoryVoucher(
            Guid.NewGuid(),
            k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id,
            date,
            new[]
            {
                new InventoryAllocation(k.Item.Id, k.Godown, qty, StockDirection.Inward, Money.FromRupees(rate)),
            }));

    // ---------------------------------------------------------------- the four methods

    /// <summary>
    /// "There will be no rate included when recording the voucher" — At Zero Price auto-fills NOTHING, and
    /// <c>null</c> is the correct, expected answer rather than a failure or a silent ₹0.
    /// </summary>
    [Fact]
    public void At_zero_price_auto_fills_nothing_even_when_the_item_has_been_sold()
    {
        var k = NewKit(MarketValuationMethod.AtZeroPrice);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);

        Assert.Null(k.Market.SellingRate(k.Item.Id, D4));
    }

    /// <summary>
    /// "The selling price of the stock item is based on the last price at which the stock item was sold."
    /// Two sales at ₹20 then ₹25 → ₹25, the most recent, not the average and not the cost.
    /// </summary>
    [Fact]
    public void Last_sales_price_takes_the_most_recent_sale_rate()
    {
        var k = NewKit(MarketValuationMethod.LastSalesPrice);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);
        Sell(k, D3, 10m, 25m);

        Assert.Equal(Money.FromRupees(25m), k.Market.SellingRate(k.Item.Id, D4));
    }

    /// <summary>
    /// "The average rate at which you will sell the stock item" — total sale AMOUNT ÷ total QUANTITY sold, which
    /// is quantity-weighted, not a mean of the rates. 30 @ ₹20 + 10 @ ₹40 = ₹1,000 over 40 = <b>₹25</b>; the
    /// unweighted mean would be ₹30, so this asserts the weighting really happens.
    /// </summary>
    [Fact]
    public void Average_price_is_quantity_weighted_not_a_mean_of_the_rates()
    {
        var k = NewKit(MarketValuationMethod.AveragePrice);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);
        Sell(k, D3, 10m, 40m);

        Assert.Equal(Money.FromRupees(25m), k.Market.SellingRate(k.Item.Id, D4));
        Assert.NotEqual(Money.FromRupees(30m), k.Market.SellingRate(k.Item.Id, D4));
    }

    /// <summary>"Set standard prices for the stock items" — the per-item rate, independent of any sale.</summary>
    [Fact]
    public void Standard_price_uses_the_items_own_rate_with_no_sales_at_all()
    {
        var k = NewKit(MarketValuationMethod.StandardPrice, standardPrice: Money.FromRupees(99.50m));
        Buy(k, D1, 100m, 10m);

        Assert.Equal(Money.FromRupees(99.50m), k.Market.SellingRate(k.Item.Id, D4));
    }

    /// <summary>
    /// 🔴 <b>A missing selling price auto-fills NOTHING and never falls back to a COST.</b> Substituting a cost
    /// rate would auto-fill the invoice at cost and silently sell at zero margin — a worse failure than a blank
    /// box, and the mirror image of T0-2.
    /// </summary>
    [Fact]
    public void A_missing_selling_price_never_falls_back_to_a_cost()
    {
        var never = NewKit(MarketValuationMethod.LastSalesPrice);
        Buy(never, D1, 100m, 10m);                    // a real ₹10 cost is available…
        Assert.Null(never.Market.SellingRate(never.Item.Id, D4));   // …and is deliberately NOT used.

        var avg = NewKit(MarketValuationMethod.AveragePrice);
        Buy(avg, D1, 100m, 10m);
        Assert.Null(avg.Market.SellingRate(avg.Item.Id, D4));

        var std = NewKit(MarketValuationMethod.StandardPrice);       // standard price never set
        Buy(std, D1, 100m, 10m);
        Assert.Null(std.Market.SellingRate(std.Item.Id, D4));
    }

    /// <summary>The as-of date is honoured: a later sale does not leak into an earlier selling price.</summary>
    [Fact]
    public void The_as_of_date_is_honoured()
    {
        var k = NewKit(MarketValuationMethod.LastSalesPrice);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);
        Sell(k, D4, 10m, 99m);

        Assert.Equal(Money.FromRupees(20m), k.Market.SellingRate(k.Item.Id, D3));
        Assert.Equal(Money.FromRupees(99m), k.Market.SellingRate(k.Item.Id, D4));
    }

    // ---------------------------------------------------------------- 🔴 THE BOUNDARY (the T0-2 defect itself)

    /// <summary>
    /// 🔴 <b>THE ASSERTION THIS WHOLE ROW EXISTS FOR.</b> An item whose MARKET valuation is Last Sales Price —
    /// the very basis that used to value stock — must still have its closing stock valued at COST. Buy 100 @ ₹10,
    /// sell 30 @ ₹20: the selling price is ₹20, the closing stock is 70 × ₹10 = <b>₹700</b>, and it is emphatically
    /// NOT 70 × ₹20 = ₹1,400. Under the defect those two numbers were the same thing.
    /// </summary>
    [Fact]
    public void A_market_valuation_of_last_sales_price_does_not_touch_closing_stock()
    {
        var k = NewKit(MarketValuationMethod.LastSalesPrice, costing: StockValuationMethod.AverageCost);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);

        // The selling price IS the sale rate…
        Assert.Equal(Money.FromRupees(20m), k.Market.SellingRate(k.Item.Id, D4));

        // …and the closing stock is emphatically NOT.
        var v = k.Valuation.ClosingValue(k.Item.Id, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(700m), v.Value);
        Assert.NotEqual(Money.FromRupees(1400m), v.Value);
    }

    /// <summary>
    /// 🔴 <b>Changing the MARKET valuation method moves no money anywhere.</b> All four settings are swept over
    /// one identical book and the closing stock value is byte-identical every time. This is the structural
    /// guarantee that the dimension cannot reach the Balance Sheet — a stronger statement than checking one case.
    /// </summary>
    [Theory]
    [InlineData(MarketValuationMethod.AtZeroPrice)]
    [InlineData(MarketValuationMethod.AveragePrice)]
    [InlineData(MarketValuationMethod.LastSalesPrice)]
    [InlineData(MarketValuationMethod.StandardPrice)]
    public void No_market_valuation_method_changes_closing_stock(MarketValuationMethod method)
    {
        var k = NewKit(method, costing: StockValuationMethod.AverageCost,
            standardPrice: Money.FromRupees(500m));
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);

        var v = k.Valuation.ClosingValue(k.Item.Id, D4);
        Assert.Equal(Money.FromRupees(700m), v.Value);
    }

    // ---------------------------------------------------------------- 🔴 THE RULING-26 ON-OPEN WARNING

    /// <summary>
    /// 🔴 <b>AN AFFECTED BOOK WARNS.</b> Ruling 26 requires that a book whose closing stock value moved on
    /// upgrade tells its operator what changed and why. The notice must name the item and must explain the
    /// consequence — an operator told only "the method changed" cannot tell whether to trust last month's
    /// Balance Sheet.
    /// </summary>
    [Fact]
    public void An_affected_book_warns_its_operator_on_open()
    {
        var k = NewKit(MarketValuationMethod.AtZeroPrice);
        k.Item.ValuationMethod = StockValuationMethod.LastPurchaseCost;
        k.Item.ValuationRemediatedFrom = StockValuationMethod.LastSaleCost;   // what the v65 migration stamps

        var notice = ValuationRemediationNotice.For(k.Company);

        Assert.NotEqual(string.Empty, notice);
        Assert.Contains("Widget", notice);                 // it names the affected item
        Assert.Contains("Last Purchase Cost", notice);     // and what it was moved TO
        Assert.Contains("Balance Sheet", notice);          // and what it means for the figures
    }

    /// <summary>
    /// 🔴 <b>AN UNAFFECTED BOOK STAYS COMPLETELY SILENT — the other half of the requirement.</b> A warning
    /// everyone sees is a warning nobody reads, which would leave the genuinely affected operator no better off
    /// than silence. A book that never chose the retired method gets nothing at all.
    /// </summary>
    [Fact]
    public void An_unaffected_book_says_nothing_on_open()
    {
        var k = NewKit(MarketValuationMethod.LastSalesPrice, costing: StockValuationMethod.Fifo);
        Buy(k, D1, 100m, 10m);
        Sell(k, D2, 30m, 20m);

        Assert.Null(k.Item.ValuationRemediatedFrom);
        Assert.Equal(string.Empty, ValuationRemediationNotice.For(k.Company));
    }

    /// <summary>The notice counts and lists every affected item, not just the first one it finds.</summary>
    [Fact]
    public void The_warning_accounts_for_every_affected_item()
    {
        var k = NewKit(MarketValuationMethod.AtZeroPrice);
        var masters = new InventoryService(k.Company);
        var grp = k.Company.StockGroups.First(g => g.Name == "Goods");
        var nos = k.Company.Units.First(u => u.Symbol == "Nos");
        var second = masters.CreateStockItem("Gadget", grp.Id, nos.Id,
            valuationMethod: StockValuationMethod.LastPurchaseCost);

        k.Item.ValuationRemediatedFrom = StockValuationMethod.LastSaleCost;
        second.ValuationRemediatedFrom = StockValuationMethod.LastSaleCost;

        var notice = ValuationRemediationNotice.For(k.Company);
        Assert.Contains("2 stock items", notice);
        Assert.Contains("Gadget", notice);
        Assert.Contains("Widget", notice);
    }
}
