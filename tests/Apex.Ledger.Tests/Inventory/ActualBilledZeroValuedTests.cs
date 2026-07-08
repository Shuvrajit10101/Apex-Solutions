using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// <b>Zero-valued transactions &amp; separate Actual-vs-Billed quantity</b> engine tests (Book pp.142–147;
/// catalog §11; Phase 6 slice 4 RQ-21..RQ-25; PR-6; ER-7/ER-13). The load-bearing fidelity rule: <b>stock moves
/// off Actual, value &amp; GST off Billed</b> (no <c>value = qty × rate</c> shortcut), the inward valuation unit =
/// Value ÷ Actual (so free / short-billed goods drag the moving average down), zero-valued is Sales/Purchase-only
/// and only when the type flag is on, and both flags off ⇒ byte-identical. All pure, deterministic, paisa-exact.
/// </summary>
public class ActualBilledZeroValuedTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    // ---------------------------------------------------------------- fixture

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
        public required VoucherType PurchaseType { get; init; }
        public required VoucherType SalesType { get; init; }
    }

    private static Domain.Ledger AddLedger(Company c, string name, Guid groupId, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Kit NewKit(StockValuationMethod method = StockValuationMethod.AverageCost, string itemName = "Widget")
    {
        var c = CompanyFactory.CreateSeeded("Actual/Billed Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem(itemName, grp.Id, nos.Id, valuationMethod: method);
        return new Kit
        {
            Company = c,
            Ledgers = new LedgerService(c),
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            Purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, openingIsDebit: true),
            Sales = AddLedger(c, "Sales", c.FindGroupByName("Sales Accounts")!.Id, openingIsDebit: false),
            Creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, openingIsDebit: false),
            Debtor = AddLedger(c, "Debtor", c.FindGroupByName("Sundry Debtors")!.Id, openingIsDebit: true),
            PurchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase),
            SalesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales),
        };
    }

    // ---------------------------------------------------------------- PR-6 core (60 Actual / 50 Billed @ ₹70)

    [Fact]
    public void Pr6_purchase_60_actual_50_billed_moves_60_stock_values_and_pairs_on_billed_3500()
    {
        var k = NewKit(StockValuationMethod.AverageCost);
        k.Company.UseSeparateActualBilledQuantity = true;

        // Rohan buys 60 kg but is billed for only 50 kg @ ₹70. Value & accounting leg = 50 × 70 = ₹3,500; stock = 60.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(3500m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(3500m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ItemId, k.GodownId, 60m, Money.FromRupees(70m), billedQuantity: 50m),
        }));

        // Accounting: Creditor / Purchase leg = ₹3,500 (billed).
        Assert.Equal(3500m, LedgerBalances.SignedClosing(k.Company, k.Purchases, AsOf));
        Assert.Equal(-3500m, LedgerBalances.SignedClosing(k.Company, k.Creditor, AsOf));

        // Stock: on-hand = +60 (Actual). Closing value = ₹3,500 for 60 units (billed value; avg unit 58.3333…
        // snaps to ₹3,500 to the paisa — ER-4).
        Assert.Equal(60m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, AsOf));
        var closing = new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf);
        Assert.Equal(60m, closing.Quantity);
        Assert.Equal(Money.FromRupees(3500m), closing.Value);
    }

    [Fact]
    public void Pr6_line_value_is_billed_times_rate_not_actual_times_rate()
    {
        // TOP RISK #1: the value must derive from Billed, never from the stock (Actual) quantity.
        var line = new VoucherInventoryLine(Guid.NewGuid(), Guid.NewGuid(), 60m, Money.FromRupees(70m), billedQuantity: 50m);
        Assert.Equal(Money.FromRupees(3500m), line.Value);            // 50 × 70, NOT 60 × 70 = 4200
        Assert.NotEqual(Money.FromRupees(4200m), line.Value);
        Assert.Equal(60m, line.Quantity);                            // Actual drives stock
        Assert.Equal(50m, line.BilledQuantity);
    }

    // ---------------------------------------------------------------- RQ-21/RQ-24 zero-valued free goods

    [Fact]
    public void Zero_valued_free_goods_move_stock_post_zero_value_and_drag_the_average_to_zero()
    {
        var c = CompanyFactory.CreateSeeded("Free Goods Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var m31 = masters.CreateStockItem("Samsung M31", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var ear = masters.CreateStockItem("Samsung Earphone", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var main = c.MainLocation!.Id;

        var purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, openingIsDebit: false);
        var ledgers = new LedgerService(c);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        purchaseType.AllowZeroValuedTransactions = true;   // the voucher-type flag (RQ-21)

        // Buy 3 M31 @ ₹15,000 = ₹45,000 and get 3 Earphone FREE (₹0). Dr Purchases 45,000 / Cr Creditor 45,000.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D1, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(45000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(45000m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(m31.Id, main, 3m, Money.FromRupees(15000m)),
            new VoucherInventoryLine(ear.Id, main, 3m, Money.Zero),   // free-goods, ₹0
        }));

        var valuation = new StockValuationService(c);
        // M31 unaffected: 3 units @ ₹15,000 = ₹45,000.
        Assert.Equal(3m, new InventoryLedger(c).OnHand(m31.Id, main, AsOf));
        Assert.Equal(Money.FromRupees(45000m), valuation.ClosingValue(m31.Id, AsOf).Value);
        // Earphone: on-hand +3, value ₹0, moving average dragged to 0 (first inward at ₹0).
        Assert.Equal(3m, new InventoryLedger(c).OnHand(ear.Id, main, AsOf));
        Assert.Equal(Money.Zero, valuation.ClosingValue(ear.Id, AsOf).Value);
    }

    [Fact]
    public void Zero_valued_free_inward_pulls_a_positive_average_down_not_to_zero()
    {
        // Two items: the free earphones ride ALONGSIDE a paid line in the same invoice (the realistic free-goods
        // case — the voucher still carries a positive accounting leg, so no degenerate ₹0/₹0 voucher is needed).
        var c = CompanyFactory.CreateSeeded("Drag Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var phone = masters.CreateStockItem("Phone", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var ear = masters.CreateStockItem("Earphone", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var main = c.MainLocation!.Id;
        var purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, openingIsDebit: false);
        var ledgers = new LedgerService(c);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        purchaseType.AllowZeroValuedTransactions = true;

        // First a normal purchase of earphones: 10 @ ₹100 = ₹1,000 → earphone average ₹100.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D1, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(ear.Id, main, 10m, Money.FromRupees(100m)) }));

        // Next: buy 2 phones @ ₹15,000 (paid) AND get 3 earphones FREE (₹0) on the same invoice.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D2, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(30000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(30000m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(phone.Id, main, 2m, Money.FromRupees(15000m)),
            new VoucherInventoryLine(ear.Id, main, 3m, Money.Zero),   // free earphones
        }));

        // Earphone on-hand 13, value STILL ₹1,000 → average pulled DOWN to ₹76.92… (not replaced by 0).
        var closing = new StockValuationService(c).ClosingValue(ear.Id, AsOf);
        Assert.Equal(13m, closing.Quantity);
        Assert.Equal(Money.FromRupees(1000m), closing.Value);          // value unchanged by the free inward
        var avg = closing.Value.Amount / closing.Quantity;            // ≈ 76.92 — below ₹100, above ₹0
        Assert.True(avg > 0m && avg < 100m, $"average {avg} must be dragged down, not to zero and not unchanged");
    }

    // ---------------------------------------------------------------- RQ-21 zero-valued rejected without the flag

    [Fact]
    public void Zero_valued_line_is_rejected_on_a_type_without_the_flag()
    {
        var k = NewKit();   // PurchaseType.AllowZeroValuedTransactions defaults false
        var ex = Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, inventoryLines: new[]
            {
                new VoucherInventoryLine(k.ItemId, k.GodownId, 10m, Money.FromRupees(100m)),
                new VoucherInventoryLine(k.ItemId, k.GodownId, 3m, Money.Zero),   // ₹0 line — rejected (no flag)
            })));
        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- RQ-21 zero-valued rejected outside Sales/Purchase

    [Fact]
    public void Allow_zero_valued_on_a_journal_type_is_rejected_at_post()
    {
        var k = NewKit();
        var journalType = k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal);
        journalType.AllowZeroValuedTransactions = true;   // illegal: only Sales/Purchase may carry it

        var ex = Assert.Throws<InvalidVoucherException>(() =>
            k.Ledgers.Post(new Voucher(Guid.NewGuid(), journalType.Id, D1, new[]
            {
                new EntryLine(k.Purchases.Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(k.Creditor.Id, Money.FromRupees(100m), DrCr.Credit),
            })));
        Assert.Contains("Purchase or Sales", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Distinct feature: an ordinary rate-less Stock-Journal TRANSFER is NOT the zero-valued feature — it moves
        // stock at a fallback cost with no flag involved, and posts fine.
        var masters = new InventoryService(k.Company);
        var dest = masters.CreateGodown("Godown 2").Id;
        var sjType = k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal).Id;
        // Seed stock at Main so the transfer has something to move.
        masters.AddOpeningBalance(k.ItemId, k.GodownId, 5m, Money.FromRupees(10m));
        new InventoryPostingService(k.Company).Post(InventoryVoucher.StockJournal(Guid.NewGuid(), sjType, D2,
            source: new[] { new InventoryAllocation(k.ItemId, k.GodownId, 5m, StockDirection.Outward, Money.FromRupees(10m)) },
            destination: new[] { new InventoryAllocation(k.ItemId, dest, 5m, StockDirection.Inward) }));
        Assert.Equal(5m, new InventoryLedger(k.Company).OnHand(k.ItemId, dest, AsOf));
    }

    // ---------------------------------------------------------------- RQ-25 Actual < Billed and Billed > Actual

    [Fact]
    public void Sales_70_actual_50_billed_moves_70_stock_posts_50_value()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        k.Company.UseSeparateActualBilledQuantity = true;

        // Stock the shelf: buy 100 @ ₹30.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.GodownId, 100m, Money.FromRupees(30m)) }));

        // Sell 70 Actual / 50 Billed @ ₹40 → sales value on 50 = ₹2,000; stock −70.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.SalesType.Id, D2, new[]
        {
            new EntryLine(k.Debtor.Id, Money.FromRupees(2000m), DrCr.Debit),
            new EntryLine(k.Sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ItemId, k.GodownId, 70m, Money.FromRupees(40m), billedQuantity: 50m),
        }));

        Assert.Equal(-2000m, LedgerBalances.SignedClosing(k.Company, k.Sales, AsOf));
        // On-hand 100 − 70 = 30 (Actual out); FIFO closing 30 @ ₹30 = ₹900.
        Assert.Equal(30m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, AsOf));
        Assert.Equal(Money.FromRupees(900m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    [Fact]
    public void Billed_greater_than_actual_is_accepted()
    {
        var k = NewKit(StockValuationMethod.AverageCost);
        k.Company.UseSeparateActualBilledQuantity = true;

        // Quality shortfall billed in full: receive 40 Actual but bill 50 @ ₹70 → value ₹3,500; stock +40.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(3500m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(3500m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ItemId, k.GodownId, 40m, Money.FromRupees(70m), billedQuantity: 50m),
        }));

        Assert.Equal(40m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, AsOf));
        // Closing value = billed ₹3,500 spread over 40 Actual units (unit ₹87.50).
        Assert.Equal(Money.FromRupees(3500m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    // ---------------------------------------------------------------- composition: A/B + Additional Cost of Purchase

    [Fact]
    public void Composition_actual_billed_plus_additional_cost_lands_deterministically()
    {
        var k = NewKit(StockValuationMethod.AverageCost);
        k.Company.UseSeparateActualBilledQuantity = true;
        k.PurchaseType.TrackAdditionalCosts = true;

        var freight = new Domain.Ledger(Guid.NewGuid(), "Freight", k.Company.FindGroupByName("Direct Expenses")!.Id,
            Money.Zero, openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByQuantity);
        k.Company.AddLedger(freight);

        // 60 Actual / 50 Billed @ ₹70 = ₹3,500 billed value; + ₹600 freight (by quantity). Dr Purchases 3,500 +
        // Dr Freight 600 / Cr Creditor 4,100. Pairing = item billed value ₹3,500 == Purchases leg ₹3,500.
        var v = new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(3500m), DrCr.Debit),
            new EntryLine(freight.Id, Money.FromRupees(600m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(4100m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ItemId, k.GodownId, 60m, Money.FromRupees(70m), billedQuantity: 50m),
        });
        k.Ledgers.Post(v);

        // Landed value = billed value ₹3,500 + freight ₹600 = ₹4,100, over 60 Actual units (unit ₹68.333…).
        var landed = AdditionalCostApportionment.ForPurchase(k.Company, v);
        Assert.Single(landed);
        Assert.Equal(Money.FromRupees(600m), landed[0].QtyShare);
        Assert.Equal(Money.FromRupees(4100m), landed[0].LandedValue);
        Assert.Equal(60m, landed[0].Quantity);

        // Deterministic, paisa-reconciling: closing 60 units valued ₹4,100 (billed value + freight).
        var closing = new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf);
        Assert.Equal(60m, closing.Quantity);
        Assert.Equal(Money.FromRupees(4100m), closing.Value);
    }

    // ---------------------------------------------------------------- ER-13 byte-identical (feature off)

    [Fact]
    public void Feature_off_line_is_byte_identical_billed_equals_actual()
    {
        // Both flags off (the default): Billed ≡ Actual, Value = Actual × Rate, valuation unchanged.
        var line = new VoucherInventoryLine(Guid.NewGuid(), Guid.NewGuid(), 10m, Money.FromRupees(100m));
        Assert.Equal(10m, line.BilledQuantity);
        Assert.Equal(Money.FromRupees(1000m), line.Value);   // 10 × 100 — unchanged

        var k = NewKit(StockValuationMethod.Fifo);
        Assert.False(k.Company.UseSeparateActualBilledQuantity);
        Assert.False(k.PurchaseType.AllowZeroValuedTransactions);

        // A normal item-invoice purchase reproduces the exact prior figures.
        k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PurchaseType.Id, D1, new[]
        {
            new EntryLine(k.Purchases.Id, Money.FromRupees(1200m), DrCr.Debit),
            new EntryLine(k.Creditor.Id, Money.FromRupees(1200m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.GodownId, 10m, Money.FromRupees(120m)) }));
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, AsOf));
        Assert.Equal(Money.FromRupees(1200m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }
}
