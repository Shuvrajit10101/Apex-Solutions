using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Item-invoice-mode tests (catalog §10; phase3-inventory-requirements RQ-16/RQ-17, BR-1; slice 3.3b). An
/// Item-Invoice Purchase/Sales voucher posts BOTH its balanced Dr/Cr accounting legs AND a stock movement
/// <b>atomically</b> in one voucher, so a stock inward/outward is always backed by an accounting posting and
/// derived closing stock can never invent phantom profit. Covered: Purchase inward + valuation, Sales outward
/// + the atomic no-negative block, the pairing invariant, and the full-trading precondition proof (derived
/// closing stock ⇒ correct COGS/Gross Profit, Balance Sheet balances to the paisa). All pure, deterministic,
/// paisa-exact — exactly like the accounting core.
/// </summary>
public class ItemInvoiceTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly D3 = new(2024, 4, 15);
    private static readonly DateOnly D4 = new(2024, 4, 20);

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required LedgerService Ledgers { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Domain.Ledger Purchases { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Domain.Ledger Creditor { get; init; }
        public required Domain.Ledger Debtor { get; init; }
        public required Guid PurchaseTypeId { get; init; }
        public required Guid SalesTypeId { get; init; }
    }

    private static Domain.Ledger AddLedger(Company c, string name, Guid groupId, Money opening, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, opening, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Kit NewKit(StockValuationMethod method = StockValuationMethod.Fifo)
    {
        var c = CompanyFactory.CreateSeeded("Item Invoice Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: method);

        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;
        var salesGrp = c.FindGroupByName("Sales Accounts")!;
        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var debtorsGrp = c.FindGroupByName("Sundry Debtors")!;

        return new Kit
        {
            Company = c,
            Ledgers = new LedgerService(c),
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            Purchases = AddLedger(c, "Purchases", purchasesGrp.Id, Money.Zero, openingIsDebit: true),
            Sales = AddLedger(c, "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false),
            Creditor = AddLedger(c, "Creditor", creditorsGrp.Id, Money.Zero, openingIsDebit: false),
            Debtor = AddLedger(c, "Debtor", debtorsGrp.Id, Money.Zero, openingIsDebit: true),
            PurchaseTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            SalesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
        };
    }

    private static VoucherInventoryLine Item(Kit k, decimal qty, decimal rate, string? batch = null)
        => new(k.ItemId, k.GodownId, qty, Money.FromRupees(rate), batchLabel: batch);

    // ---------------------------------------------------------------- Purchase item-invoice

    [Fact]
    public void Item_invoice_purchase_posts_accounts_and_increases_on_hand()
    {
        var k = NewKit();
        // Purchase 10 @ ₹100 = ₹1000: Dr Purchases 1000 / Cr Creditor 1000, stock inward 10.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));

        // Accounting: Purchases Dr 1000, Creditor Cr 1000.
        Assert.Equal(1000m, LedgerBalances.SignedClosing(k.Company, k.Purchases, D4));
        Assert.Equal(-1000m, LedgerBalances.SignedClosing(k.Company, k.Creditor, D4));

        // Stock: on-hand +10; valuation = ₹1000.
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
        Assert.Equal(Money.FromRupees(1000m), new StockValuationService(k.Company).ClosingValue(k.ItemId, D4).Value);
    }

    // ---------------------------------------------------------------- Sales item-invoice

    [Fact]
    public void Item_invoice_sales_posts_accounts_and_decreases_on_hand()
    {
        var k = NewKit();
        // Buy 10 @ ₹100 first.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));

        // Sell 6 @ ₹150 = ₹900: Dr Debtor 900 / Cr Sales 900, stock outward 6.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.SalesTypeId, D2, new[]
        {
            new EntryLine(k.Debtor.Id, Money.FromRupees(900m), DrCr.Debit),
            new EntryLine(k.Sales.Id, Money.FromRupees(900m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 6m, 150m) }));

        Assert.Equal(900m, LedgerBalances.SignedClosing(k.Company, k.Debtor, D4));
        Assert.Equal(-900m, LedgerBalances.SignedClosing(k.Company, k.Sales, D4));

        // On-hand 10 − 6 = 4; FIFO closing value = 4 @ ₹100 = ₹400.
        Assert.Equal(4m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
        Assert.Equal(Money.FromRupees(400m), new StockValuationService(k.Company).ClosingValue(k.ItemId, D4).Value);
    }

    [Fact]
    public void Item_invoice_sales_that_would_go_negative_is_blocked_atomically()
    {
        var k = NewKit();
        // Buy 5 @ ₹100.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(500m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(500m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 5m, 100m) }));

        var salesId = Guid.NewGuid();
        // Attempt to sell 8 (only 5 on hand) — must fail and persist NOTHING (no accounting leg, no stock).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            k.Ledgers.Post(new Voucher(salesId, k.SalesTypeId, D2, new[]
            {
                new EntryLine(k.Debtor.Id, Money.FromRupees(1200m), DrCr.Debit),
                new EntryLine(k.Sales.Id, Money.FromRupees(1200m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 8m, 150m) })));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The whole voucher rolled back: no accounting movement, on-hand still 5, valuation still ₹500.
        Assert.Null(k.Company.FindVoucher(salesId));
        Assert.Equal(0m, LedgerBalances.SignedClosing(k.Company, k.Debtor, D4)); // unchanged
        Assert.Equal(0m, LedgerBalances.SignedClosing(k.Company, k.Sales, D4));
        Assert.Equal(5m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
        Assert.Equal(Money.FromRupees(500m), new StockValuationService(k.Company).ClosingValue(k.ItemId, D4).Value);
    }

    // ---------------------------------------------------------------- pairing invariant

    [Fact]
    public void Item_invoice_purchase_rejects_when_stock_line_total_does_not_match_accounting_amount()
    {
        var k = NewKit();
        // Item lines total ₹1000 but Purchases posted only ₹900 → unbacked stock → rejected.
        var ex = Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(900m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(900m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 10m, 100m) })));
        Assert.Contains("item", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing persisted; on-hand untouched.
        Assert.Equal(0m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }

    [Fact]
    public void Item_invoice_sales_rejects_when_stock_line_total_does_not_match_sales_amount()
    {
        var k = NewKit();
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));

        // Sales item lines total ₹900 (6 @ 150) but the Sales leg is ₹800 → rejected.
        Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.SalesTypeId, D2, new[]
            {
                new EntryLine(k.Debtor.Id, Money.FromRupees(800m), DrCr.Debit),
                new EntryLine(k.Sales.Id, Money.FromRupees(800m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 6m, 150m) })));
    }

    [Fact]
    public void Item_invoice_lines_on_a_non_purchase_non_sales_voucher_are_rejected()
    {
        var k = NewKit();
        var journalType = k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(Guid.NewGuid(), journalType, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 10m, 100m) })));
    }

    // ---------------------------------------------------------------- zero-rate line guard (CRITICAL)

    [Fact]
    public void Item_invoice_purchase_with_a_zero_rate_line_is_rejected_and_persists_nothing()
    {
        var k = NewKit();
        var purchaseId = Guid.NewGuid();
        // A zero-rate item line adds real quantity to stock but ₹0 to the value sum — it would slip through
        // the pairing check while injecting stock no accounting amount backs (phantom on-hand). On a NORMAL
        // Purchase type (zero-valued transactions NOT enabled — slice 4) the validator rejects it with a clean
        // InvalidVoucherException before anything persists. (The domain ctor now permits a ₹0 rate so the
        // zero-valued feature can use it; the per-type flag decides whether it is allowed.)
        var ex = Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(purchaseId, k.PurchaseTypeId, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 10m, 100m), Item(k, 10m, 0m) })));
        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing persisted; on-hand unchanged (still zero).
        Assert.Null(k.Company.FindVoucher(purchaseId));
        Assert.Equal(0m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }

    [Fact]
    public void Item_invoice_sales_with_a_zero_rate_line_is_rejected_and_persists_nothing()
    {
        var k = NewKit();
        // Stock the shelf first (10 @ ₹100).
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));

        var salesId = Guid.NewGuid();
        // A Sales zero-rate line would move units out at zero revenue (symmetric phantom). On a normal Sales type
        // (zero-valued transactions NOT enabled) the validator rejects it with a clean InvalidVoucherException.
        var ex = Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(salesId, k.SalesTypeId, D2, new[]
            {
                new EntryLine(k.Debtor.Id, Money.FromRupees(600m), DrCr.Debit),
                new EntryLine(k.Sales.Id, Money.FromRupees(600m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 4m, 150m), Item(k, 2m, 0m) })));
        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing persisted; on-hand still 10 from the purchase.
        Assert.Null(k.Company.FindVoucher(salesId));
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }

    [Fact]
    public void Item_invoice_zero_rate_phantom_profit_scenario_is_now_blocked()
    {
        // The exact repro the review found: a Purchase item-invoice with lines 10 @ ₹100 + 10 @ ₹0 and
        // accounting Dr Purchases 1000 / Cr Creditor 1000. Σ item value = 1000 = accounting leg, so it slipped
        // through the pairing check yet injected 10 unbacked units → on-hand 20; under Standard Cost ₹100 that is
        // ₹2000 of closing stock vs ₹1000 spent → ₹1000 phantom profit on a "balanced" Balance Sheet.
        // With the guard, the zero-rate line is rejected at construction and NONE of that can happen.
        var k = NewKit(StockValuationMethod.StandardCost);
        // Give the item a ₹100 standard rate so the phantom valuation would have been 20 × ₹100 = ₹2000.
        var item = k.Company.FindStockItem(k.ItemId)!;
        item.StandardCost = Money.FromRupees(100m);

        var purchaseId = Guid.NewGuid();
        Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(purchaseId, k.PurchaseTypeId, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, inventoryLines: new[] { Item(k, 10m, 100m), Item(k, 10m, 0m) })));

        // Proof the phantom can no longer arise: nothing persisted, on-hand is 0 (NOT 20), and closing stock
        // value is ₹0 (NOT ₹2000) — no phantom profit is possible.
        Assert.Null(k.Company.FindVoucher(purchaseId));
        Assert.Equal(0m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
        Assert.NotEqual(20m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
        Assert.Equal(Money.Zero, new StockValuationService(k.Company).ClosingValue(k.ItemId, D4).Value);
    }

    // ---------------------------------------------------------------- cancel reverses stock

    [Fact]
    public void Cancelling_an_item_invoice_reverses_its_stock_effect()
    {
        var k = NewKit();
        var purchaseId = Guid.NewGuid();
        k.Ledgers.Post(new Voucher(purchaseId, k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));

        k.Ledgers.Cancel(purchaseId);
        Assert.Equal(0m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }

    [Fact]
    public void Cancelling_an_item_invoice_purchase_is_blocked_when_it_would_retro_drive_negative()
    {
        var k = NewKit();
        var purchaseId = Guid.NewGuid();
        // Purchase 10 inward (item-invoice), then a later Delivery Note draws 8 out.
        k.Ledgers.Post(new Voucher(purchaseId, k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { Item(k, 10m, 100m) }));
        new InventoryPostingService(k.Company).Post(new InventoryVoucher(
            Guid.NewGuid(), k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote).Id, D2,
            new[] { new InventoryAllocation(k.ItemId, k.GodownId, 8m, StockDirection.Outward) }));

        // Cancelling the purchase would leave 0 − 8 = −8 as of D2 → blocked; the voucher stays live.
        Assert.Throws<InvalidOperationException>(() => k.Ledgers.Cancel(purchaseId));
        Assert.False(k.Company.FindVoucher(purchaseId)!.Cancelled);
        Assert.Equal(2m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4)); // 10 − 8 intact
    }

    // ---------------------------------------------------------------- PRECONDITION PROOF

    /// <summary>
    /// The precondition A10 flagged: without pairing the two arms, someone can move stock without the
    /// accounting posting (or vice versa) and derived closing stock invents phantom profit. Item-invoice mode
    /// posts both arms atomically in one voucher, so derived-closing-stock P&amp;L shows the CORRECT COGS /
    /// Gross Profit with NO phantom profit and the Balance Sheet BALANCES TO THE PAISA.
    /// </summary>
    [Fact]
    public void Item_invoice_full_trading_round_trip_has_no_phantom_profit_and_balance_sheet_balances_to_the_paisa()
    {
        var c = CompanyFactory.CreateSeeded("Trading Co", FyStart);
        var ledgers = new LedgerService(c);
        var masters = new InventoryService(c);

        // Masters.
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;

        var stockInHandGrp = c.FindGroupByName("Stock-in-Hand")!;
        var capitalGrp = c.FindGroupByName("Capital Account")!;
        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var debtorsGrp = c.FindGroupByName("Sundry Debtors")!;
        var salesGrp = c.FindGroupByName("Sales Accounts")!;
        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;

        // Opening stock ₹1000 (100 @ ₹10): booked to a Stock-in-Hand ledger opening debit AND as an inventory
        // opening balance — the reconciliation precondition (BR-1). Balanced by ₹1000 opening capital credit.
        var stock = AddLedger(c, "Stock-in-Hand", stockInHandGrp.Id, Money.FromRupees(1000m), openingIsDebit: true);
        var capital = AddLedger(c, "Capital", capitalGrp.Id, Money.FromRupees(1000m), openingIsDebit: false);
        var creditor = AddLedger(c, "Creditor", creditorsGrp.Id, Money.Zero, openingIsDebit: false);
        var debtor = AddLedger(c, "Debtor", debtorsGrp.Id, Money.Zero, openingIsDebit: true);
        var sales = AddLedger(c, "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false);
        var purchases = AddLedger(c, "Purchases", purchasesGrp.Id, Money.Zero, openingIsDebit: true);
        masters.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(10m));

        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // Item-invoice PURCHASE: 50 @ ₹12 = ₹600. Dr Purchases 600 / Cr Creditor 600 + stock inward 50.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, D2, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(600m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(600m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 50m, Money.FromRupees(12m)) }));

        // Item-invoice SALES: 80 @ ₹20 = ₹1600. Dr Debtor 1600 / Cr Sales 1600 + stock outward 80.
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D3, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(1600m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(1600m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 80m, Money.FromRupees(20m)) }));

        var asOf = D4;

        // Derived closing stock (FIFO): 100@10 + 50@12 − 80 = 70 units = 20@10 + 50@12 = ₹800.
        var valuation = new StockValuationService(c);
        Assert.Equal(70m, valuation.ClosingValue(item.Id, asOf).Quantity);
        Assert.Equal(Money.FromRupees(800m), valuation.TotalClosingStockValue(asOf));

        // P&L under InventoryDerived — no phantom profit:
        // COGS = Opening 1000 + Purchases 600 − Closing 800 = 800. Gross = Sales 1600 − COGS 800 = 800.
        var pl = ProfitAndLoss.Build(c, asOf, ClosingStockMode.InventoryDerived);
        Assert.Equal(Money.FromRupees(1000m), pl.OpeningStock);
        Assert.Equal(Money.FromRupees(800m), pl.ClosingStock);
        Assert.Equal(Money.FromRupees(800m), pl.GrossProfit);
        Assert.Equal(Money.FromRupees(800m), pl.NetProfit); // no other expenses

        // Balance Sheet under InventoryDerived — balances to the paisa.
        var bs = BalanceSheet.Build(c, asOf, ClosingStockMode.InventoryDerived);
        Assert.True(bs.Balanced, $"BS must balance: assets {bs.TotalAssets} vs liab {bs.TotalLiabilities}");
        var stockInHand = bs.Assets.Single(a => a.Name == "Stock-in-Hand");
        Assert.Equal(Money.FromRupees(800m), stockInHand.Amount);
        // Assets: Stock-in-Hand 800 + Debtor 1600 = 2400. Liab: Capital 1000 + Creditor 600 + Net Profit 800 = 2400.
        Assert.Equal(Money.FromRupees(2400m), bs.TotalAssets);
        Assert.Equal(Money.FromRupees(2400m), bs.TotalLiabilities);
    }

    // ---------------------------------------------------------------- non-item vouchers unaffected

    [Fact]
    public void A_purchase_voucher_without_item_lines_moves_no_stock()
    {
        var k = NewKit();
        // Plain (accounts-only) purchase — no item lines. No stock effect at all.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(500m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(500m), DrCr.Credit),
        }));
        Assert.Equal(500m, LedgerBalances.SignedClosing(k.Company, k.Purchases, D4));
        Assert.Equal(0m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }

    // ---------------------------------------------------------------- hardening

    [Fact]
    public void Item_invoice_purchase_pairs_against_a_stock_in_hand_debit_leg()
    {
        // A Bright-style integrated purchase can debit Stock-in-Hand directly (no separate Purchases ledger).
        // The pairing invariant accepts the Stock-in-Hand leg as the backing accounting posting.
        var c = CompanyFactory.CreateSeeded("SIH Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;

        var stock = AddLedger(c, "Stock-in-Hand", c.FindGroupByName("Stock-in-Hand")!.Id, Money.Zero, true);
        var creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        var ledgers = new LedgerService(c);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, D1, new[]
        {
            new EntryLine(stock.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(100m)) }));

        Assert.Equal(10m, new InventoryLedger(c).OnHand(item.Id, main, D4));
    }

    [Fact]
    public void The_voucher_nature_stamps_the_direction_even_if_the_caller_sets_it_wrong()
    {
        var k = NewKit();
        // Caller wrongly marks a Purchase item line Outward; the posting service stamps the nature-implied
        // Inward, so on-hand still INCREASES (the direction is derived from the voucher, not the caller).
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseTypeId, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ItemId, k.GodownId, 10m, Money.FromRupees(100m), StockDirection.Outward),
        }));
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, D4));
    }
}
