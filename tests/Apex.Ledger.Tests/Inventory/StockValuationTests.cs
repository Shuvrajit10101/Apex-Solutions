using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Stock-valuation-engine tests (catalog §9 clone-note; phase3-inventory-requirements RQ-21..RQ-27,
/// ER-2/ER-3/ER-4; slice 3.3a). Each of the six methods (Average Cost, FIFO, LIFO, Last Purchase, Last
/// Sale, Standard Cost) is exercised against a worked example — the FIFO ₹800 case being the canonical
/// one from the requirements — plus the no-rate inward fallback, the per-company aggregate, the as-of
/// date, and the accounts↔inventory integration (Stock-in-Hand derived + P&amp;L COGS + the Balance Sheet
/// still balancing to the paisa under <see cref="ClosingStockMode.InventoryDerived"/>). All computations
/// are pure, deterministic, framework-agnostic and paisa-exact — exactly like the accounting core.
/// </summary>
public class StockValuationTests
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
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required StockValuationService Valuation { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
    }

    private static Kit NewKit(StockValuationMethod method, Money? standardCost = null)
    {
        var c = CompanyFactory.CreateSeeded("Valuation Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: method,
            standardCost: standardCost);
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            Valuation = new StockValuationService(c),
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private void Receive(Kit k, DateOnly date, decimal qty, Money? rate)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), date,
            new[] { new InventoryAllocation(k.ItemId, k.GodownId, qty, StockDirection.Inward, rate) }));

    private void Deliver(Kit k, DateOnly date, decimal qty, Money? rate = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), date,
            new[] { new InventoryAllocation(k.ItemId, k.GodownId, qty, StockDirection.Outward, rate) }));

    // ---------------------------------------------------------------- FIFO (the canonical ₹800 case)

    [Fact]
    public void Fifo_worked_example_buy_100_at_10_buy_50_at_12_sell_80_closes_at_800()
    {
        // Requirements worked example: buy 100 @ ₹10, buy 50 @ ₹12, sell 80 →
        // closing 70 = 20@₹10 + 50@₹12 = ₹800.
        var k = NewKit(StockValuationMethod.Fifo);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));
        Deliver(k, D3, 80m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(800m), v.Value);
    }

    [Fact]
    public void Lifo_worked_example_buy_100_at_10_buy_50_at_12_sell_80_closes_at_700()
    {
        // LIFO consumes newest first: sell 80 eats the 50@₹12 lot then 30@₹10 → closing 70 = 70@₹10 = ₹700.
        var k = NewKit(StockValuationMethod.Lifo);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));
        Deliver(k, D3, 80m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(700m), v.Value);
    }

    [Fact]
    public void Average_cost_uses_the_weighted_average_over_inward_lots()
    {
        // Buy 100 @ ₹10 (avg 10), buy 50 @ ₹12 → running total (1000 + 600)/150 = ₹10.6667/unit.
        // Sell 80 at avg → remaining 70 units × 10.6666… . Perpetual moving average.
        // 70 × 1600/150 = 70 × 10.6666… = 746.6666… → paisa-snapped to ₹746.67.
        var k = NewKit(StockValuationMethod.AverageCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));
        Deliver(k, D3, 80m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(746.67m), v.Value);
    }

    [Fact]
    public void Average_cost_is_paisa_exact_and_snaps_the_closing_value()
    {
        // Buy 3 @ ₹10 → avg 10; closing 3 × 10 = 30 exactly.
        var k = NewKit(StockValuationMethod.AverageCost);
        Receive(k, D1, 3m, Money.FromRupees(10m));
        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(3m, v.Quantity);
        Assert.Equal(Money.FromRupees(30m), v.Value);
        Assert.True(v.Value.IsPaisaExact);
    }

    [Fact]
    public void Last_purchase_cost_uses_the_most_recent_inward_rate()
    {
        // Closing 70 × most-recent purchase rate ₹12 = ₹840.
        var k = NewKit(StockValuationMethod.LastPurchaseCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));
        Deliver(k, D3, 80m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(840m), v.Value);
    }

    [Fact]
    public void Last_sale_cost_uses_the_most_recent_outward_rate()
    {
        // Closing 70 × most-recent sale rate ₹20 = ₹1400.
        var k = NewKit(StockValuationMethod.LastSaleCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Deliver(k, D2, 30m, Money.FromRupees(20m));

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(1400m), v.Value);
    }

    [Fact]
    public void Standard_cost_uses_the_items_standard_rate()
    {
        // Closing 70 × standard rate ₹11 = ₹770.
        var k = NewKit(StockValuationMethod.StandardCost, standardCost: Money.FromRupees(11m));
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Deliver(k, D2, 30m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(770m), v.Value);
    }

    [Fact]
    public void Standard_cost_without_a_standard_rate_falls_back_to_last_purchase_cost()
    {
        // No standard rate set → fall back to the most-recent inward rate (₹10) × closing 70 = ₹700.
        var k = NewKit(StockValuationMethod.StandardCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Deliver(k, D2, 30m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, v.Quantity);
        Assert.Equal(Money.FromRupees(700m), v.Value);
    }

    // ---------------------------------------------------------------- opening stock as a cost lot

    [Fact]
    public void Opening_balance_is_the_earliest_cost_lot_for_fifo()
    {
        // Opening 40 @ ₹5, buy 60 @ ₹10, sell 50 → FIFO consumes the opening 40 then 10 of the ₹10 lot →
        // closing 50 = 50@₹10 = ₹500.
        var k = NewKit(StockValuationMethod.Fifo);
        k.Masters.AddOpeningBalance(k.ItemId, k.GodownId, 40m, Money.FromRupees(5m));
        Receive(k, D1, 60m, Money.FromRupees(10m));
        Deliver(k, D2, 50m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(50m, v.Quantity);
        Assert.Equal(Money.FromRupees(500m), v.Value);
    }

    // ---------------------------------------------------------------- no-rate inward fallback

    [Fact]
    public void Inward_with_no_rate_carries_the_running_average_cost_and_stays_paisa_exact()
    {
        // Buy 100 @ ₹10 (avg 10). A stock-journal-style inward of 20 with NO rate must not crash and must
        // stay paisa-exact — it carries the running average (₹10). Closing 120 × ₹10 = ₹1200.
        var k = NewKit(StockValuationMethod.AverageCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 20m, rate: null); // no-rate inward (e.g. a destination with no source cost)

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(120m, v.Quantity);
        Assert.Equal(Money.FromRupees(1200m), v.Value);
        Assert.True(v.Value.IsPaisaExact);
    }

    [Fact]
    public void Fifo_inward_with_no_rate_uses_the_running_average_as_that_lots_cost()
    {
        // Buy 100 @ ₹10, then a no-rate inward of 50 (costed at the running avg ₹10), then sell 120.
        // FIFO: consume 100@₹10 then 20@₹10 → closing 30 @ ₹10 = ₹300.
        var k = NewKit(StockValuationMethod.Fifo);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, rate: null);
        Deliver(k, D3, 120m);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(30m, v.Quantity);
        Assert.Equal(Money.FromRupees(300m), v.Value);
    }

    // ---------------------------------------------------------------- no-rate inward best-available-cost chain

    [Fact]
    public void No_rate_first_inward_with_a_standard_cost_uses_the_standard_cost()
    {
        // A no-rate FIRST inward (no running average yet) falls back to the item's StandardCost, NOT ₹0.
        // Inward 20 with no rate + StandardCost ₹7 → closing 20 × ₹7 = ₹140 (Average method).
        var k = NewKit(StockValuationMethod.AverageCost, standardCost: Money.FromRupees(7m));
        Receive(k, D1, 20m, rate: null);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(20m, v.Quantity);
        Assert.Equal(Money.FromRupees(140m), v.Value);
        Assert.True(v.Value.IsPaisaExact);
    }

    [Fact]
    public void No_rate_first_inward_falls_back_to_the_last_rated_inward_rate_when_no_running_average_or_standard()
    {
        // A no-rate FIRST inward with no running average and no standard cost, but a LATER rated inward
        // exists → the no-rate lot borrows the most-recent rated inward rate (₹12) rather than costing ₹0.
        // Average: inward 20 @ (fallback ₹12) + inward 10 @ ₹12 → 30 × ₹12 = ₹360.
        var k = NewKit(StockValuationMethod.AverageCost);
        Receive(k, D1, 20m, rate: null);
        Receive(k, D2, 10m, Money.FromRupees(12m));

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(30m, v.Quantity);
        Assert.Equal(Money.FromRupees(360m), v.Value);
        Assert.True(v.Value.IsPaisaExact);
    }

    [Fact]
    public void No_rate_inward_with_a_prior_running_average_still_uses_the_running_average_not_the_standard()
    {
        // The running average (if > 0) wins over StandardCost: buy 100 @ ₹10 (avg 10) then a no-rate inward
        // of 20; StandardCost ₹99 is set but MUST be ignored while a positive running average exists →
        // FIFO closing 120: 100@₹10 + 20@₹10 = ₹1200 (the no-rate lot carries the ₹10 average, not ₹99).
        var k = NewKit(StockValuationMethod.Fifo, standardCost: Money.FromRupees(99m));
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 20m, rate: null);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(120m, v.Quantity);
        Assert.Equal(Money.FromRupees(1200m), v.Value);
    }

    [Fact]
    public void No_rate_inward_with_no_cost_signal_at_all_still_values_at_zero()
    {
        // Truly no cost signal: a no-rate first inward, no running average, no StandardCost, no other rated
        // inward anywhere → last-resort ₹0 (documented). Closing 20 × ₹0 = ₹0.
        var k = NewKit(StockValuationMethod.AverageCost);
        Receive(k, D1, 20m, rate: null);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(20m, v.Quantity);
        Assert.Equal(Money.Zero, v.Value);
    }

    // ---------------------------------------------------------------- LastSale / LastPurchase graceful fallback

    [Fact]
    public void Last_sale_cost_with_no_sale_falls_back_to_last_purchase_cost()
    {
        // LastSaleCost method but the item was NEVER sold → fall back to the last purchase rate (₹12),
        // not ₹0. Closing 150 × ₹12 = ₹1800.
        var k = NewKit(StockValuationMethod.LastSaleCost);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(150m, v.Quantity);
        Assert.Equal(Money.FromRupees(1800m), v.Value);
    }

    [Fact]
    public void Last_sale_cost_with_no_sale_and_no_purchase_falls_back_to_the_standard_cost()
    {
        // LastSaleCost, no sale AND no rated purchase (a no-rate inward only), but a StandardCost ₹9 is set →
        // fall through LastPurchase (none) and running average (0) to StandardCost. Closing 20 × ₹9 = ₹180.
        var k = NewKit(StockValuationMethod.LastSaleCost, standardCost: Money.FromRupees(9m));
        Receive(k, D1, 20m, rate: null);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(20m, v.Quantity);
        Assert.Equal(Money.FromRupees(180m), v.Value);
    }

    [Fact]
    public void Last_purchase_cost_with_no_purchase_falls_back_to_the_running_average()
    {
        // LastPurchaseCost but the only inward carried no rate (never a rated purchase). The running average
        // is driven by the no-rate lot's own fallback: with a StandardCost ₹8 the no-rate lot costs ₹8, so
        // the running average is ₹8 and LastPurchase falls back to it. Closing 20 × ₹8 = ₹160.
        var k = NewKit(StockValuationMethod.LastPurchaseCost, standardCost: Money.FromRupees(8m));
        Receive(k, D1, 20m, rate: null);

        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(20m, v.Quantity);
        Assert.Equal(Money.FromRupees(160m), v.Value);
    }

    // ---------------------------------------------------------------- as-of date

    [Fact]
    public void Closing_value_as_of_an_intermediate_date_respects_the_as_of_cut()
    {
        // As of D2 (before the D3 sale) closing = 150 units; FIFO value = 100@10 + 50@12 = 1600.
        var k = NewKit(StockValuationMethod.Fifo);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        Receive(k, D2, 50m, Money.FromRupees(12m));
        Deliver(k, D3, 80m);

        var atD2 = k.Valuation.ClosingValue(k.ItemId, D2);
        Assert.Equal(150m, atD2.Quantity);
        Assert.Equal(Money.FromRupees(1600m), atD2.Value);

        var atD4 = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(70m, atD4.Quantity);
        Assert.Equal(Money.FromRupees(800m), atD4.Value);
    }

    [Fact]
    public void Post_dated_inward_is_excluded_until_its_date_is_reached()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        Receive(k, D1, 100m, Money.FromRupees(10m));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D3,
            new[] { new InventoryAllocation(k.ItemId, k.GodownId, 50m, StockDirection.Inward, Money.FromRupees(12m)) },
            postDated: true));
        // As of D2, only the D1 lot counts → 100 @ ₹10 = ₹1000.
        var v = k.Valuation.ClosingValue(k.ItemId, D2);
        Assert.Equal(100m, v.Quantity);
        Assert.Equal(Money.FromRupees(1000m), v.Value);
    }

    // ---------------------------------------------------------------- zero on-hand

    [Fact]
    public void Zero_on_hand_closes_at_zero_value()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        Receive(k, D1, 10m, Money.FromRupees(10m));
        Deliver(k, D2, 10m);
        var v = k.Valuation.ClosingValue(k.ItemId, D4);
        Assert.Equal(0m, v.Quantity);
        Assert.Equal(Money.Zero, v.Value);
    }

    // ---------------------------------------------------------------- TotalClosingStockValue (Σ, mixed methods)

    [Fact]
    public void Total_closing_stock_value_sums_each_item_by_its_own_method()
    {
        var c = CompanyFactory.CreateSeeded("Mixed Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var posting = new InventoryPostingService(c);
        var valuation = new StockValuationService(c);
        var main = c.MainLocation!.Id;

        // Item A — FIFO: the ₹800 case.
        var a = masters.CreateStockItem("A", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        // Item B — LastPurchaseCost: closing 5 × ₹20 = ₹100.
        var b = masters.CreateStockItem("B", grp.Id, nos.Id, valuationMethod: StockValuationMethod.LastPurchaseCost);

        var grn = TypeId(c, VoucherBaseType.ReceiptNote);
        var del = TypeId(c, VoucherBaseType.DeliveryNote);
        posting.Post(new InventoryVoucher(Guid.NewGuid(), grn, D1,
            new[] { new InventoryAllocation(a.Id, main, 100m, StockDirection.Inward, Money.FromRupees(10m)) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), grn, D2,
            new[] { new InventoryAllocation(a.Id, main, 50m, StockDirection.Inward, Money.FromRupees(12m)) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), del, D3,
            new[] { new InventoryAllocation(a.Id, main, 80m, StockDirection.Outward) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), grn, D1,
            new[] { new InventoryAllocation(b.Id, main, 5m, StockDirection.Inward, Money.FromRupees(20m)) }));

        // A = ₹800, B = ₹100 → total ₹900.
        Assert.Equal(Money.FromRupees(800m), valuation.ClosingValue(a.Id, D4).Value);
        Assert.Equal(Money.FromRupees(100m), valuation.ClosingValue(b.Id, D4).Value);
        Assert.Equal(Money.FromRupees(900m), valuation.TotalClosingStockValue(D4));
    }

    // ---------------------------------------------------------------- integrated: BS balances, P&L COGS

    [Fact]
    public void Inventory_derived_closing_stock_balances_the_balance_sheet_and_drives_pl_cogs()
    {
        // A tiny integrated trading company. Opening stock ₹1000 (100 @ ₹10) is posted BOTH as an inventory
        // opening balance AND as the accounting "Opening Stock" Stock-in-Hand ledger opening debit (₹1000),
        // exactly as a Bright-style integrated company reconciles (BR-1). A purchase (Item-Invoice: Dr
        // Purchases 600 / Cr Creditor 600, and stock inward 50 @ ₹12) and a sale (Dr Debtor 1600 / Cr Sales
        // 1600, stock outward 80) move the accounts and the stock. Closing stock is DERIVED by FIFO = ₹800.
        var built = BuildIntegratedCompany();
        var c = built.Company;
        var asOf = D4;

        var valuation = new StockValuationService(c);
        var closing = valuation.TotalClosingStockValue(asOf);
        Assert.Equal(Money.FromRupees(800m), closing);

        // P&L under InventoryDerived: closing stock is the derived ₹800.
        var pl = ProfitAndLoss.Build(c, asOf, ClosingStockMode.InventoryDerived);
        Assert.Equal(Money.FromRupees(1000m), pl.OpeningStock);
        Assert.Equal(Money.FromRupees(800m), pl.ClosingStock);

        // COGS = Opening (1000) + Purchases (600) − Closing (800) = 800. Gross = Sales 1600 − COGS 800 = 800.
        Assert.Equal(Money.FromRupees(800m), pl.GrossProfit);

        // Balance Sheet under InventoryDerived: Stock-in-Hand asset = derived ₹800, and it BALANCES.
        var bs = BalanceSheet.Build(c, asOf, ClosingStockMode.InventoryDerived);
        Assert.True(bs.Balanced, $"BS must balance: assets {bs.TotalAssets} vs liab {bs.TotalLiabilities}");
        var stockInHand = bs.Assets.FirstOrDefault(a => a.Name == "Stock-in-Hand");
        Assert.NotNull(stockInHand);
        Assert.Equal(Money.FromRupees(800m), stockInHand!.Amount);
    }

    /// <summary>
    /// Builds a minimal accounts↔inventory-integrated company:
    /// opening capital 1000 + opening stock 1000 (self-balanced via a matching capital credit),
    /// a credit purchase (600, stock +50@12) and a credit sale (1600, stock −80). Closing stock derives to
    /// ₹800 by FIFO. All accounting vouchers balance Σ Dr = Σ Cr; the Stock-in-Hand group's single ledger is
    /// the derived closing-stock line.
    /// </summary>
    private static (Company Company, Guid ItemId) BuildIntegratedCompany()
    {
        var c = CompanyFactory.CreateSeeded("Integrated Co", FyStart);
        var ledgers = new LedgerService(c);

        // Accounting masters: find the seeded groups by name.
        var stockInHandGrp = c.FindGroupByName("Stock-in-Hand")!;
        var capitalGrp = c.FindGroupByName("Capital Account")!;
        var debtorsGrp = c.FindGroupByName("Sundry Debtors")!;
        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var salesGrp = c.FindGroupByName("Sales Accounts")!;
        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;

        // Stock-in-Hand ledger: opening debit ₹1000 (the opening stock). Its closing figure is DERIVED.
        var stock = AddLedger(c, "Stock-in-Hand", stockInHandGrp.Id, Money.FromRupees(1000m), openingIsDebit: true);
        var capital = AddLedger(c, "Capital", capitalGrp.Id, Money.FromRupees(1000m), openingIsDebit: false);
        var debtor = AddLedger(c, "Debtor", debtorsGrp.Id, Money.Zero, openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", creditorsGrp.Id, Money.Zero, openingIsDebit: false);
        var sales = AddLedger(c, "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false);
        var purchases = AddLedger(c, "Purchases", purchasesGrp.Id, Money.Zero, openingIsDebit: true);

        // Inventory masters + opening balance mirroring the ₹1000 opening stock (100 @ ₹10) FIFO.
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(10m));

        // Accounting vouchers (balanced): a credit purchase and a credit sale.
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D2, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(600m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(600m), DrCr.Credit),
        }));
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType.Id, D3, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(1600m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(1600m), DrCr.Credit),
        }));

        // Stock movements: inward 50 @ ₹12 (matches the ₹600 purchase), outward 80 (the sale).
        var posting = new InventoryPostingService(c);
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D2,
            new[] { new InventoryAllocation(item.Id, main, 50m, StockDirection.Inward, Money.FromRupees(12m)) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.DeliveryNote), D3,
            new[] { new InventoryAllocation(item.Id, main, 80m, StockDirection.Outward) }));

        return (c, item.Id);
    }

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, Guid groupId, Money opening, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, groupId, opening, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
