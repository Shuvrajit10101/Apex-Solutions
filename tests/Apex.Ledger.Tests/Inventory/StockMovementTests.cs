using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Stock & order voucher + stock-movement-engine tests (catalog §10; phase3-inventory-requirements
/// RQ-8..RQ-20, ER-4/ER-5, DP-3/DP-4/DP-5/DP-6/DP-7). Covers, per voucher type, the correct effect on
/// accounts vs stock; the on-hand engine (opening + inward − outward as-of a date, honouring the
/// post-dated/as-of convention); the no-negative-stock hard block on every outward path; the Stock-Journal
/// balance rule; compound-unit normalisation; and the Physical-Stock implicit adjustment. These are pure,
/// framework-agnostic engine tests, exactly like the accounting core.
/// </summary>
public class StockMovementTests
{
    // ---------------------------------------------------------------- test scaffolding

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required InventoryLedger Ledger { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SecondGodownId { get; init; }
    }

    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 20);
    private static readonly DateOnly D3 = new(2024, 5, 1);

    // A trading company with one Nos item, two godowns, and 10 units opening in Main Location.
    private static Kit NewKit(decimal openingQty = 10m)
    {
        var c = CompanyFactory.CreateSeeded("Stock Move Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("Warehouse 2");
        if (openingQty > 0m)
            masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(100m));
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            Ledger = new InventoryLedger(c),
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            SecondGodownId = wh2.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private static InventoryAllocation Line(Guid item, Guid godown, decimal qty, StockDirection dir,
        Money? rate = null, string? batch = null) =>
        new(item, godown, qty, dir, rate, batch);

    // ---------------------------------------------------------------- effect flags per type

    [Fact]
    public void Order_voucher_types_affect_neither_stock_nor_accounts()
    {
        var c = NewKit().Company;
        foreach (var bt in new[] { VoucherBaseType.PurchaseOrder, VoucherBaseType.SalesOrder })
        {
            var vt = c.VoucherTypes.First(t => t.BaseType == bt);
            Assert.False(vt.AffectsStock, $"{vt.Name} should not affect stock");
            Assert.False(vt.AffectsAccounts, $"{vt.Name} should not affect accounts");
        }
    }

    [Theory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.DeliveryNote)]
    [InlineData(VoucherBaseType.RejectionIn)]
    [InlineData(VoucherBaseType.RejectionOut)]
    [InlineData(VoucherBaseType.StockJournal)]
    [InlineData(VoucherBaseType.PhysicalStock)]
    public void Stock_only_voucher_types_affect_stock_but_not_accounts(VoucherBaseType bt)
    {
        var c = NewKit().Company;
        var vt = c.VoucherTypes.First(t => t.BaseType == bt);
        Assert.True(vt.AffectsStock, $"{vt.Name} should affect stock");
        Assert.False(vt.AffectsAccounts, $"{vt.Name} should not affect accounts (Phase 3)");
    }

    [Fact]
    public void Order_and_stock_voucher_types_carry_the_documented_shortcuts()
    {
        var c = NewKit().Company;
        (VoucherBaseType Bt, string Sc)[] expected =
        {
            (VoucherBaseType.PurchaseOrder, "Ctrl+F9"),
            (VoucherBaseType.SalesOrder,    "Ctrl+F8"),
            (VoucherBaseType.ReceiptNote,   "Alt+F9"),
            (VoucherBaseType.DeliveryNote,  "Alt+F8"),
            (VoucherBaseType.RejectionOut,  "Ctrl+F5"),
            (VoucherBaseType.RejectionIn,   "Ctrl+F6"),
            (VoucherBaseType.StockJournal,  "Alt+F7"),
            // Physical Stock is Ctrl+F7, NOT F10 — TallyPrime's official keyboard-shortcut reference gives
            // "To open Physical Stock | Ctrl+F7", and reserves F10 for the voucher/master list. The seed said
            // "F10", which in this app opens the Other Vouchers menu: an advertised key that did something else,
            // while Ctrl+F7 was bound to nothing (decision X1).
            (VoucherBaseType.PhysicalStock, "Ctrl+F7"),
        };
        foreach (var (bt, sc) in expected)
            Assert.Equal(sc, c.VoucherTypes.First(t => t.BaseType == bt).DefaultShortcut);
    }

    // ---------------------------------------------------------------- on-hand opening

    [Fact]
    public void On_hand_starts_from_the_opening_balance()
    {
        var k = NewKit(openingQty: 10m);
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Equal(0m, k.Ledger.OnHand(k.ItemId, k.SecondGodownId, D1));
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, D1)); // across all godowns
    }

    // ---------------------------------------------------------------- GRN / Delivery

    [Fact]
    public void Receipt_note_increases_on_hand_at_the_receiving_godown()
    {
        var k = NewKit();
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward, Money.FromRupees(120m)) }));
        Assert.Equal(15m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Delivery_note_decreases_on_hand_at_the_issuing_godown()
    {
        var k = NewKit();
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 4m, StockDirection.Outward) }));
        Assert.Equal(6m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    // ---------------------------------------------------------------- Rejection In / Out

    [Fact]
    public void Rejection_in_increases_and_rejection_out_decreases_on_hand()
    {
        var k = NewKit();
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionIn), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 3m, StockDirection.Inward) }));
        Assert.Equal(13m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));

        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionOut), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 2m, StockDirection.Outward) }));
        Assert.Equal(11m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
    }

    // ---------------------------------------------------------------- Order vouchers = no effect

    [Fact]
    public void Purchase_and_sales_orders_do_not_move_stock()
    {
        var k = NewKit();
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder), D1,
            new[] { new OrderLine(k.ItemId, k.MainGodownId, 100m, Money.FromRupees(90m)) }));
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.SalesOrder), D1,
            new[] { new OrderLine(k.ItemId, k.MainGodownId, 50m, Money.FromRupees(150m)) }));
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D3));
    }

    [Fact]
    public void Order_voucher_rejects_inventory_allocations_and_vice_versa()
    {
        var k = NewKit();
        // An order type cannot carry stock-moving allocations.
        Assert.Throws<InvalidOperationException>(() =>
            k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder), D1,
                new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward) })));
        // A stock-moving type cannot carry order lines.
        Assert.Throws<InvalidOperationException>(() =>
            k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
                new[] { new OrderLine(k.ItemId, k.MainGodownId, 5m, null) })));
    }

    // ---------------------------------------------------------------- Stock Journal

    [Fact]
    public void Stock_journal_transfers_between_godowns_source_down_dest_up()
    {
        var k = NewKit();
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { Line(k.ItemId, k.MainGodownId, 6m, StockDirection.Outward) },
            destination: new[] { Line(k.ItemId, k.SecondGodownId, 6m, StockDirection.Inward) }));
        Assert.Equal(4m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Equal(6m, k.Ledger.OnHand(k.ItemId, k.SecondGodownId, D1));
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, D1)); // total unchanged
    }

    [Fact]
    public void Stock_journal_rejects_when_source_and_destination_quantities_do_not_balance()
    {
        var k = NewKit();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
                source: new[] { Line(k.ItemId, k.MainGodownId, 6m, StockDirection.Outward) },
                destination: new[] { Line(k.ItemId, k.SecondGodownId, 5m, StockDirection.Inward) })));
        Assert.Contains("balance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stock_journal_balances_after_compound_unit_conversion()
    {
        // Item measured in a compound "Doz of 12 Nos": consume 1 Dozen (source, in Dozen) and produce
        // 12 Nos (destination, in the base) — quantities normalise to the base and balance.
        var c = CompanyFactory.CreateSeeded("Compound Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var doz = masters.CreateSimpleUnit("Doz", "Dozens");
        // Compound, built the corpus way: FIRST = Doz (the larger unit), TAIL = Nos (the smaller/base
        // measure), factor 12 ⇒ 1 Doz = 12 Nos. Scaling by the factor therefore lands in the TAIL (Nos),
        // so the compound's BaseMeasureUnitId is Nos — which is exactly this item's base unit.
        var dozen = masters.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        Assert.Equal(nos.Id, dozen.BaseMeasureUnitId);
        // Item held in Nos (base); source line is expressed in the item's Dozen unit for the transfer.
        var item = masters.CreateStockItem("Egg", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("WH2");
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 24m, Money.FromRupees(5m)); // 24 Nos

        var posting = new InventoryPostingService(c);
        var ledger = new InventoryLedger(c);

        // Transfer 1 Dozen (= 12 Nos) from Main to WH2. Source line uses the Dozen unit; destination uses Nos.
        posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(c, VoucherBaseType.StockJournal), D1,
            source: new[] { new InventoryAllocation(item.Id, c.MainLocation.Id, 1m, StockDirection.Outward, null, null, dozen.Id) },
            destination: new[] { new InventoryAllocation(item.Id, wh2.Id, 12m, StockDirection.Inward, null, null, nos.Id) }));

        Assert.Equal(12m, ledger.OnHand(item.Id, c.MainLocation.Id, D1)); // 24 − 12
        Assert.Equal(12m, ledger.OnHand(item.Id, wh2.Id, D1));
    }

    // ---------------------------------------------------------------- Physical Stock (DP-3)

    [Fact]
    public void Physical_stock_sets_on_hand_to_the_counted_quantity_up_or_down()
    {
        var k = NewKit(openingQty: 10m);
        // Count says 8 → book qty becomes 8 (implicit downward adjustment).
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 8m, null) }));
        Assert.Equal(8m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));

        // A later count says 30 → book qty becomes 30 (implicit upward adjustment).
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D2,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 30m, null) }));
        Assert.Equal(30m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
    }

    [Fact]
    public void Physical_stock_implied_adjustment_is_reported()
    {
        var k = NewKit(openingQty: 10m);
        var adj = k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 8m, null) }));
        // The implied adjustment (counted − book-before) = 8 − 10 = −2, visible on the posted voucher.
        var line = k.Ledger.PhysicalStockAdjustments(D1).Single();
        Assert.Equal(k.ItemId, line.StockItemId);
        Assert.Equal(-2m, line.AdjustmentQuantity);
        Assert.NotNull(adj);
    }

    [Fact]
    public void Physical_stock_rejects_a_negative_counted_quantity()
    {
        var k = NewKit();
        Assert.Throws<ArgumentException>(() =>
            new PhysicalStockLine(k.ItemId, k.MainGodownId, -1m, null));
    }

    // ---------------------------------------------------------------- Negative stock: DETECTED, never blocked (NS-3)
    //
    // ⚠️ These tests previously asserted a HARD BLOCK (ER-5/DP-7). That block was removed at plan.md NS-3 —
    // TallyPrime has no built-in block anywhere; its only reaction is an advisory F12 warning after which the
    // voucher still saves (official TallyHelp, "Configuring an Invoice" + the Sales FAQ; the licensed corpus is
    // SILENT — 0 hits across all ten PDFs). They are re-pointed rather than deleted, because the guard's genuinely
    // hard-won mechanics — batch-awareness and pre-count (DP-3) sampling — moved into the DETECTOR and would
    // otherwise lose all coverage silently.

    [Fact]
    public void Delivery_that_drives_on_hand_negative_now_posts_and_is_detected()
    {
        var k = NewKit(openingQty: 3m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward) }));

        Assert.Equal(-2m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(-2m, s.OnHand);
        Assert.Contains("negative", s.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejection_out_over_the_on_hand_now_posts_and_is_detected()
    {
        var k = NewKit(openingQty: 1m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionOut), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 2m, StockDirection.Outward) }));

        Assert.Equal(-1m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Equal(-1m, Assert.Single(k.Posting.DetectNegativeStock()).OnHand);
    }

    [Fact]
    public void Stock_journal_source_over_the_on_hand_now_posts_and_is_detected()
    {
        var k = NewKit(openingQty: 2m);
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { Line(k.ItemId, k.MainGodownId, 3m, StockDirection.Outward) },
            destination: new[] { Line(k.ItemId, k.SecondGodownId, 3m, StockDirection.Inward) }));

        Assert.Equal(-1m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Equal(3m, k.Ledger.OnHand(k.ItemId, k.SecondGodownId, D1));
        // Only the SOURCE key is short — the destination gained stock and must not be reported.
        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(k.MainGodownId, s.GodownId);
    }

    [Fact]
    public void Negative_detection_is_batch_aware()
    {
        var k = NewKit(openingQty: 0m);
        // Two batches at Main: A has 5, B has 0. Delivering 1 from batch B goes negative in B even though the
        // item/godown TOTAL (5) is positive — the detector must key on the batch, not net it away.
        k.Masters.AddOpeningBalance(k.ItemId, k.MainGodownId, 5m, Money.FromRupees(100m), batchLabel: "A");
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 1m, StockDirection.Outward, batch: "B") }));

        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal("B", s.Batch);
        Assert.Equal(-1m, s.OnHand);
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, "A", D1));   // batch A untouched

        // Delivering from batch A is fine.
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 1m, StockDirection.Outward, batch: "A") }));
        Assert.Equal(4m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, "A", D1));
    }

    // ---------------------------------------------------------------- As-of / post-dated

    [Fact]
    public void On_hand_as_of_excludes_movements_dated_after_the_as_of()
    {
        var k = NewKit();
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward) }));
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1)); // GRN dated D2 not yet counted
        Assert.Equal(15m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
    }

    [Fact]
    public void Post_dated_stock_movement_is_excluded_until_its_date_is_reached()
    {
        var k = NewKit();
        var pd = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward) }, postDated: true);
        k.Posting.Post(pd);
        // Even at D3 (> its date D2) a post-dated GRN only counts once as-of ≥ its date; before D2 excluded.
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Equal(15m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
    }

    [Fact]
    public void Post_dated_outward_does_not_block_a_present_dated_delivery()
    {
        // A post-dated GRN dated in the future must not be counted as available today; and a delivery
        // today is guarded only against on-hand available today.
        var k = NewKit(openingQty: 4m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 4m, StockDirection.Outward) }));
        Assert.Equal(0m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    // ---------------------------------------------------------------- Delete re-negativity guard

    [Fact]
    public void Physical_count_then_over_draw_now_posts_and_is_detected()
    {
        var k = NewKit(openingQty: 10m);
        // Count on D1 resets to 2; a D2 delivery of 3 drives the bucket to −1. Both post.
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 2m, null) }));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 3m, StockDirection.Outward) }));

        Assert.Equal(-1m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(D2, s.AsOf);           // NOT D1 — the count date itself was never short
        Assert.Equal(-1m, s.OnHand);
    }

    // ---------------------------------------------------------------- Same-date Physical-Stock masking (DP-3)

    // A Physical-Stock count is applied LAST within its date (DP-3 checkpoint) and SETS on-hand to the counted
    // quantity, so END-OF-DATE sampling alone masks any outward movement on that same date that drove true
    // on-hand negative. The DETECTOR must read the running balance BEFORE the same-date count checkpoint —
    // exactly the property the old hard guard needed, and the reason these four tests survive NS-3 rather than
    // being deleted with the block. A detector that sampled end-of-date would report NOTHING in any of them.

    [Fact]
    public void Same_date_delivery_after_a_physical_count_that_over_draws_pre_count_stock_is_still_detected()
    {
        // Opening 10 → Physical count 5 on D1 (legal, 10→5) → Delivery 100 on D1.
        // Pre-count running before the count is 10 − 100 = −90; end-of-date on-hand is the count's 5.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 100m, StockDirection.Outward) }));

        Assert.Equal(-90m, Assert.Single(k.Posting.DetectNegativeStock()).OnHand);
        // DP-3 is untouched: the count checkpoint still SETS reported on-hand to 5.
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Same_date_delivery_then_count_then_over_drawing_delivery_is_still_detected()
    {
        // Opening 10 → Delivery 8 (D1) [running 2] → Physical count 5 (D1) → Delivery 50 (D1).
        // Pre-count running before the count = 10 − 8 − 50 = −48.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 8m, StockDirection.Outward) }));
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 50m, StockDirection.Outward) }));

        Assert.Equal(-48m, Assert.Single(k.Posting.DetectNegativeStock()).OnHand);
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Same_date_rejection_out_over_pre_count_stock_on_a_count_date_is_still_detected()
    {
        // Opening 10 → Physical count 5 (D1) → Rejection Out 40 (D1). Pre-count 10 − 40 = −30.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionOut), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 40m, StockDirection.Outward) }));

        Assert.Equal(-30m, Assert.Single(k.Posting.DetectNegativeStock()).OnHand);
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Same_date_stock_journal_source_over_pre_count_stock_on_a_count_date_is_still_detected()
    {
        // Opening 10 → Physical count 5 (D1) → Stock-Journal source 40 (D1). Pre-count 10 − 40 = −30.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { Line(k.ItemId, k.MainGodownId, 40m, StockDirection.Outward) },
            destination: new[] { Line(k.ItemId, k.SecondGodownId, 40m, StockDirection.Inward) }));

        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(k.MainGodownId, s.GodownId);
        Assert.Equal(-30m, s.OnHand);
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    // No false positives: legitimate same-date count patterns must report NOTHING, and the DP-3 checkpoint
    // (on-hand reporting/carry-forward) must be unchanged — a count still SETS on-hand.

    [Fact]
    public void Same_date_count_with_no_outward_is_accepted_and_sets_on_hand()
    {
        // Opening 10 → Physical count 5 (D1), no outward ⇒ accepted; on-hand reports 5 (DP-3 checkpoint).
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Same_date_delivery_within_pre_count_stock_then_count_is_accepted()
    {
        // Opening 10 → Delivery 8 (D1) [pre-count running 2 ≥ 0] → Physical count 5 (D1).
        // The count legitimately raises book to 5; the delivery never over-drew ⇒ accepted; on-hand reports 5.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 8m, StockDirection.Outward) }));
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 5m, null) }));
        Assert.Equal(5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Same_date_count_higher_than_book_is_accepted_and_sets_on_hand()
    {
        // Opening 10 → Physical count 20 (D1) [found stock, count > book] ⇒ accepted; on-hand reports 20.
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, 20m, null) }));
        Assert.Equal(20m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Cancelling_a_stock_voucher_drops_its_effect()
    {
        var k = NewKit(openingQty: 10m);
        var grn = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward) });
        k.Posting.Post(grn);
        Assert.Equal(15m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        k.Posting.Cancel(grn.Id);
        Assert.Equal(10m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void Deleting_a_receipt_that_retro_negatives_a_later_delivery_now_succeeds_and_is_detected()
    {
        var k = NewKit(openingQty: 0m);
        var grn = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward) });
        k.Posting.Post(grn);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward) }));
        Assert.Equal(0m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D3));

        // Removing the GRN leaves the D2 delivery driving on-hand to −5. That used to be BLOCKED (NS-3);
        // the delete now applies and the retro-shortfall is reported instead.
        k.Posting.Delete(grn.Id);

        Assert.Null(k.Company.FindInventoryVoucher(grn.Id));
        Assert.Equal(-5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D3));
        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(D2, s.AsOf);
        Assert.Equal(-5m, s.OnHand);
    }
}
