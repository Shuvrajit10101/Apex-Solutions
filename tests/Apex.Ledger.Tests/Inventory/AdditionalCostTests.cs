using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// <b>Additional Cost of Purchase</b> engine tests (Book pp.133–141; catalog §11; Phase 6 slice 3 RQ-16..RQ-20;
/// PR-5). Covers the deterministic paisa-exact largest-remainder allocator, the Book worked example, the
/// by-quantity vs by-value divergence (a single item can't distinguish the methods), 3-line indivisible-paisa
/// determinism, ForPurchase feeding the stock valuation (landed closing value/rate + FIFO issue at landed cost),
/// the RQ-19 fidelity trap (a plain freight ledger with no method never touches a stock rate) and the RQ-20
/// inter-godown transfer (the one apportionment engine loads the destination's landed rate). All pure,
/// deterministic, paisa-exact.
/// </summary>
public class AdditionalCostTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    // ---------------------------------------------------------------- Allocate: Σ == pool, determinism

    [Fact]
    public void Allocate_by_any_weights_sums_exactly_to_the_pool()
    {
        // by-quantity style (equal weights) and by-value style (unequal), both paisa-exact and summing to pool.
        foreach (var weights in new[]
                 {
                     new decimal[] { 5m, 15m },        // by-qty divergence weights
                     new decimal[] { 50000m, 15000m }, // by-value divergence weights
                     new decimal[] { 1m, 1m, 1m },
                     new decimal[] { 1m, 2m, 3m },
                 })
        {
            var shares = AdditionalCostApportionment.Allocate(weights, Money.FromRupees(1500m));
            var sum = Money.Zero;
            foreach (var s in shares) sum += s;
            Assert.Equal(Money.FromRupees(1500m), sum);
            foreach (var s in shares) Assert.True(s.IsPaisaExact);
        }
    }

    [Fact]
    public void Allocate_zero_or_negative_pool_and_zero_weight_yield_zero_shares()
    {
        var zeroPool = AdditionalCostApportionment.Allocate(new decimal[] { 1m, 2m }, Money.Zero);
        Assert.All(zeroPool, s => Assert.Equal(Money.Zero, s));

        var negPool = AdditionalCostApportionment.Allocate(new decimal[] { 1m, 2m }, Money.FromRupees(-5m));
        Assert.All(negPool, s => Assert.Equal(Money.Zero, s));

        // A zero-weight line gets a zero share; the positive line absorbs the whole pool.
        var mixed = AdditionalCostApportionment.Allocate(new decimal[] { 0m, 4m }, Money.FromRupees(100m));
        Assert.Equal(Money.Zero, mixed[0]);
        Assert.Equal(Money.FromRupees(100m), mixed[1]);
    }

    [Fact]
    public void Allocate_distributes_indivisible_paisa_by_largest_remainder_then_ascending_index()
    {
        // ₹1.00 across three EQUAL weights: 33.33 each floors to 99 paisa, the single leftover paisa goes to the
        // largest fractional remainder — all tie, so ascending index wins (line 0 gets ₹0.34).
        var equal = AdditionalCostApportionment.Allocate(new decimal[] { 1m, 1m, 1m }, Money.FromRupees(1m));
        Assert.Equal(Money.FromRupees(0.34m), equal[0]);
        Assert.Equal(Money.FromRupees(0.33m), equal[1]);
        Assert.Equal(Money.FromRupees(0.33m), equal[2]);

        // ₹1.00 across weights 1:2:3 — line 0 has the largest remainder (.666) so it takes the leftover paisa.
        var skewed = AdditionalCostApportionment.Allocate(new decimal[] { 1m, 2m, 3m }, Money.FromRupees(1m));
        Assert.Equal(Money.FromRupees(0.17m), skewed[0]);
        Assert.Equal(Money.FromRupees(0.33m), skewed[1]);
        Assert.Equal(Money.FromRupees(0.50m), skewed[2]);
        Assert.Equal(Money.FromRupees(1m), skewed[0] + skewed[1] + skewed[2]);
    }

    // ---------------------------------------------------------------- Book worked example (PR-5 anchor)

    [Fact]
    public void Book_example_single_item_5_units_at_10000_plus_1500_lands_at_10300()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        // 1 item, 5 units @ ₹10,000 = ₹50,000; Packing ₹500 + Freight ₹1,000 = ₹1,500 (by quantity).
        k.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        var v = TrackedPurchase(k, D1,
            items: new[] { (Qty: 5m, Rate: 10000m) },
            additional: new[] { (k.Freight, 1500m) },
            itemLedgerAmount: 50000m);
        new LedgerService(k.Company).Post(v);

        var landed = AdditionalCostApportionment.ForPurchase(k.Company, v);
        Assert.Single(landed);
        Assert.Equal(Money.FromRupees(1500m), landed[0].QtyShare);
        Assert.Equal(Money.Zero, landed[0].ValueShare);
        Assert.Equal(Money.FromRupees(51500m), landed[0].LandedValue);
        Assert.Equal(10300m, landed[0].LandedUnitRate);

        // Valuation: closing rises by EXACTLY the additional cost; closing rate == landed ₹10,300.
        var val = new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf);
        Assert.Equal(5m, val.Quantity);
        Assert.Equal(Money.FromRupees(51500m), val.Value);
    }

    [Fact]
    public void Book_example_5_units_at_2000_plus_1500_by_quantity_lands_at_2300()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        k.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        var v = TrackedPurchase(k, D1,
            items: new[] { (Qty: 5m, Rate: 2000m) },
            additional: new[] { (k.Freight, 1500m) },
            itemLedgerAmount: 10000m);
        new LedgerService(k.Company).Post(v);

        var landed = AdditionalCostApportionment.ForPurchase(k.Company, v);
        Assert.Equal(Money.FromRupees(11500m), landed[0].LandedValue);
        Assert.Equal(2300m, landed[0].LandedUnitRate);
        Assert.Equal(Money.FromRupees(11500m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    // ---------------------------------------------------------------- by-qty vs by-value divergence

    [Fact]
    public void By_quantity_spreads_a_flat_rupee_per_unit()
    {
        var k = NewTwoItemKit(StockValuationMethod.Fifo);
        k.Kit.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        // Line A 5 @ ₹10,000 = ₹50,000; Line B 15 @ ₹1,000 = ₹15,000; ΣQty 20, additional ₹1,500.
        var v = TrackedPurchase(k.Kit, D1,
            items: new[] { (5m, 10000m), (15m, 1000m) },
            additional: new[] { (k.Kit.Freight, 1500m) },
            itemLedgerAmount: 65000m,
            itemIds: new[] { k.ItemA, k.ItemB });

        var landed = AdditionalCostApportionment.ForPurchase(k.Kit.Company, v);
        // ₹1,500 / 20 = ₹75.00/unit flat.
        Assert.Equal(Money.FromRupees(375m), landed[0].QtyShare);   // 5 × 75
        Assert.Equal(Money.FromRupees(1125m), landed[1].QtyShare);  // 15 × 75
        Assert.Equal(10075m, landed[0].LandedUnitRate);
        Assert.Equal(1075m, landed[1].LandedUnitRate);
        Assert.Equal(Money.FromRupees(1500m), landed[0].QtyShare + landed[1].QtyShare);
    }

    [Fact]
    public void By_value_makes_the_dearer_line_absorb_more_and_stays_paisa_exact()
    {
        var k = NewTwoItemKit(StockValuationMethod.Fifo);
        k.Kit.Freight.MethodOfAppropriation = MethodOfAppropriation.ByValue;
        var v = TrackedPurchase(k.Kit, D1,
            items: new[] { (5m, 10000m), (15m, 1000m) },
            additional: new[] { (k.Kit.Freight, 1500m) },
            itemLedgerAmount: 65000m,
            itemIds: new[] { k.ItemA, k.ItemB });

        var landed = AdditionalCostApportionment.ForPurchase(k.Kit.Company, v);
        // A = 1500 × 50000/65000 = ₹1,153.85 (largest remainder rounds up), B = residual ₹346.15 → Σ = ₹1,500.00.
        Assert.Equal(Money.FromRupees(1153.85m), landed[0].ValueShare);
        Assert.Equal(Money.FromRupees(346.15m), landed[1].ValueShare);
        Assert.Equal(Money.FromRupees(1500m), landed[0].ValueShare + landed[1].ValueShare);
        Assert.Equal(Money.FromRupees(51153.85m), landed[0].LandedValue); // 50000 + 1153.85
        Assert.Equal(Money.FromRupees(15346.15m), landed[1].LandedValue); // 15000 + 346.15
    }

    // ---------------------------------------------------------------- ForPurchase feeds valuation

    [Fact]
    public void Landed_cost_flows_into_fifo_closing_value_and_issue_value()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        k.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        // Buy 5 @ ₹10,000 + ₹1,500 additional → 5 units land at ₹10,300 (₹51,500 total).
        TrackedPurchasePosted(k, D1, items: new[] { (5m, 10000m) }, additional: new[] { (k.Freight, 1500m) },
            itemLedgerAmount: 50000m);

        var valuation = new StockValuationService(k.Company);
        // Issue 2 units — FIFO drains the landed layer at ₹10,300, so COGS = 2 × ₹10,300 = ₹20,600.
        Assert.Equal(Money.FromRupees(20600m), valuation.IssueValue(k.ItemId, 2m, AsOf));
        // Closing (still 5 on hand) = ₹51,500 at landed cost.
        Assert.Equal(Money.FromRupees(51500m), valuation.ClosingValue(k.ItemId, AsOf).Value);
    }

    [Fact]
    public void An_untracked_purchase_is_byte_identical_to_the_pre_v19_path()
    {
        // ER-13: a Purchase whose type does NOT track additional costs values at the bare purchase rate even when a
        // Direct-Expenses additional-cost ledger (with a method!) is posted on the same voucher.
        var k = NewKit(StockValuationMethod.Fifo);
        k.PurchaseType.TrackAdditionalCosts = false;           // tracking OFF
        k.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        TrackedPurchasePosted(k, D1, items: new[] { (5m, 10000m) }, additional: new[] { (k.Freight, 1500m) },
            itemLedgerAmount: 50000m);

        // No load applied: closing = 5 × ₹10,000 = ₹50,000 (the freight stays purely P&L).
        Assert.Equal(Money.FromRupees(50000m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    // ---------------------------------------------------------------- RQ-19 (the fidelity trap)

    [Fact]
    public void Rq19_a_freight_ledger_without_a_method_never_touches_a_stock_rate_even_on_a_tracked_purchase()
    {
        var k = NewKit(StockValuationMethod.Fifo);
        // Freight ledger has NO Method of Appropriation → it is a plain P&L Direct-Expenses ledger.
        k.Freight.MethodOfAppropriation = null;
        var v = TrackedPurchase(k, D1, items: new[] { (5m, 10000m) }, additional: new[] { (k.Freight, 1500m) },
            itemLedgerAmount: 50000m);
        new LedgerService(k.Company).Post(v);

        var landed = AdditionalCostApportionment.ForPurchase(k.Company, v);
        Assert.False(landed[0].HasLoad);                       // out of both pools (RQ-19)
        // Closing stays the bare ₹50,000 — the freight did NOT capitalise into the stock rate.
        Assert.Equal(Money.FromRupees(50000m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);

        // The SAME ledger WITH a method now DOES load the landed rate — the difference is the method, not the ledger.
        k.Freight.MethodOfAppropriation = MethodOfAppropriation.ByQuantity;
        Assert.True(AdditionalCostApportionment.ForPurchase(k.Company, v)[0].HasLoad);
        Assert.Equal(Money.FromRupees(51500m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    // ---------------------------------------------------------------- RQ-20 inter-godown transfer

    [Fact]
    public void Rq20_a_stock_journal_transfer_loads_the_destination_landed_rate_via_the_same_engine()
    {
        var c = CompanyFactory.CreateSeeded("Transfer Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;
        var dest = masters.CreateGodown("Godown 2").Id;
        masters.AddOpeningBalance(item.Id, main, 10m, Money.FromRupees(100m)); // ₹1,000 opening at Main

        var freight = AddDirectExpenseLedger(c, "Freight-In", MethodOfAppropriation.ByQuantity);
        var sjType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal).Id;

        // Transfer 10 units Main → Godown 2, with ₹200 freight loaded on the destination (₹20/unit by quantity).
        var transfer = InventoryVoucher.StockJournal(Guid.NewGuid(), sjType, D2,
            source: new[] { new InventoryAllocation(item.Id, main, 10m, StockDirection.Outward, Money.FromRupees(100m)) },
            destination: new[] { new InventoryAllocation(item.Id, dest, 10m, StockDirection.Inward, Money.FromRupees(100m)) },
            additionalCostLines: new[] { new AdditionalCostLine(freight.Id, Money.FromRupees(200m)) });
        c.AddInventoryVoucher(transfer);

        var landed = AdditionalCostApportionment.ForTransfer(c, transfer);
        Assert.Single(landed);                                 // one destination line (the source is untouched)
        Assert.Equal(Money.FromRupees(1200m), landed[0].LandedValue);
        Assert.Equal(120m, landed[0].LandedUnitRate);

        // Whole-item closing value rose by EXACTLY the ₹200 freight: 10 units now valued at ₹120 = ₹1,200.
        Assert.Equal(Money.FromRupees(1200m), new StockValuationService(c).ClosingValue(item.Id, AsOf).Value);
    }

    [Fact]
    public void Rq20_by_value_freight_on_an_all_rateless_transfer_loads_the_whole_pool_not_a_single_paisa_lost()
    {
        // Money-conservation regression: an Appropriate-BY-VALUE additional cost on an inter-godown transfer whose
        // destination allocations are ALL rateless (a legal stock-journal line — Rate is optional). The by-value
        // basis is then all-zero; without the by-quantity fallback the whole ₹200 pool would silently vanish
        // (Σ shares = ₹0, in NEITHER stock NOR P&L). It must instead land in full on the destination stock (Σ==pool).
        var c = CompanyFactory.CreateSeeded("Transfer Co Rateless", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;
        var dest = masters.CreateGodown("Godown 2").Id;
        masters.AddOpeningBalance(item.Id, main, 10m, Money.FromRupees(100m)); // ₹1,000 opening at Main

        var freight = AddDirectExpenseLedger(c, "Freight-In", MethodOfAppropriation.ByValue); // BY VALUE
        var sjType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal).Id;

        var transfer = InventoryVoucher.StockJournal(Guid.NewGuid(), sjType, D2,
            source: new[] { new InventoryAllocation(item.Id, main, 10m, StockDirection.Outward, Money.FromRupees(100m)) },
            destination: new[] { new InventoryAllocation(item.Id, dest, 10m, StockDirection.Inward) }, // RATELESS
            additionalCostLines: new[] { new AdditionalCostLine(freight.Id, Money.FromRupees(200m)) });
        c.AddInventoryVoucher(transfer);

        var landed = AdditionalCostApportionment.ForTransfer(c, transfer);
        var loaded = Money.Zero;
        foreach (var l in landed) loaded += l.QtyShare + l.ValueShare;
        Assert.Equal(Money.FromRupees(200m), loaded);       // the entire pool is conserved (was ₹0.00 before the fix)
    }

    // ---------------------------------------------------------------- fixtures

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Domain.Ledger Purchases { get; init; }
        public required Domain.Ledger Creditor { get; init; }
        public required Domain.Ledger Freight { get; init; }
        public required VoucherType PurchaseType { get; init; }
    }

    private static Kit NewKit(StockValuationMethod method)
    {
        var c = CompanyFactory.CreateSeeded("Additional Cost Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: method);
        return BuildKit(c, item.Id);
    }

    private sealed class TwoItemKit
    {
        public required Kit Kit { get; init; }
        public required Guid ItemA { get; init; }
        public required Guid ItemB { get; init; }
    }

    private static TwoItemKit NewTwoItemKit(StockValuationMethod method)
    {
        var c = CompanyFactory.CreateSeeded("Additional Cost Co 2", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var a = masters.CreateStockItem("Item A", grp.Id, nos.Id, valuationMethod: method);
        var b = masters.CreateStockItem("Item B", grp.Id, nos.Id, valuationMethod: method);
        var kit = BuildKit(c, a.Id);
        return new TwoItemKit { Kit = kit, ItemA = a.Id, ItemB = b.Id };
    }

    private static Kit BuildKit(Company c, Guid itemId)
    {
        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;
        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        purchaseType.TrackAdditionalCosts = true; // enable the additional-cost path for the tests

        return new Kit
        {
            Company = c,
            ItemId = itemId,
            GodownId = c.MainLocation!.Id,
            Purchases = AddLedger(c, "Purchases", purchasesGrp.Id, openingIsDebit: true),
            Creditor = AddLedger(c, "Creditor", creditorsGrp.Id, openingIsDebit: false),
            Freight = AddDirectExpenseLedger(c, "Freight & Packing", MethodOfAppropriation.ByQuantity),
            PurchaseType = purchaseType,
        };
    }

    private static Domain.Ledger AddLedger(Company c, string name, Guid groupId, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger AddDirectExpenseLedger(Company c, string name, MethodOfAppropriation? method)
    {
        var grp = c.FindGroupByName("Direct Expenses")!;
        var l = new Domain.Ledger(Guid.NewGuid(), name, grp.Id, Money.Zero, openingIsDebit: true,
            methodOfAppropriation: method);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Builds a tracked Purchase item-invoice: Dr item-ledger (stock leg) + Dr each additional-cost
    /// ledger, Cr Creditor for the total. The item lines pair against the stock-leg (Purchases) amount.</summary>
    private static Voucher TrackedPurchase(Kit k, DateOnly date,
        (decimal Qty, decimal Rate)[] items,
        (Domain.Ledger Ledger, decimal Amount)[] additional,
        decimal itemLedgerAmount,
        Guid[]? itemIds = null)
    {
        var lines = new List<EntryLine> { new(k.Purchases.Id, Money.FromRupees(itemLedgerAmount), DrCr.Debit) };
        var total = itemLedgerAmount;
        foreach (var (led, amt) in additional)
        {
            lines.Add(new EntryLine(led.Id, Money.FromRupees(amt), DrCr.Debit));
            total += amt;
        }
        lines.Add(new EntryLine(k.Creditor.Id, Money.FromRupees(total), DrCr.Credit));

        var invLines = new List<VoucherInventoryLine>();
        for (var i = 0; i < items.Length; i++)
        {
            var id = itemIds is null ? k.ItemId : itemIds[i];
            invLines.Add(new VoucherInventoryLine(id, k.GodownId, items[i].Qty, Money.FromRupees(items[i].Rate)));
        }

        return new Voucher(Guid.NewGuid(), k.PurchaseType.Id, date, lines, inventoryLines: invLines);
    }

    private static void TrackedPurchasePosted(Kit k, DateOnly date,
        (decimal Qty, decimal Rate)[] items,
        (Domain.Ledger Ledger, decimal Amount)[] additional,
        decimal itemLedgerAmount)
        => new LedgerService(k.Company).Post(TrackedPurchase(k, date, items, additional, itemLedgerAmount));
}
