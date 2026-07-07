using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// BOM master + Manufacturing-Journal posting tests (Phase 6 Cluster 2; requirements RQ-9..RQ-15, DP-3, PR-4).
/// Covers the BOM master + multiple-per-item + per-item-unique name (RQ-9/10), the Manufacturing-Journal
/// voucher type factory (base Stock Journal + Use as Manufacturing Journal, RQ-11), auto-scaled consumption
/// with unit-of-manufacture &gt; 1 (RQ-12), finished-good valuation = Σ components + Σ additional cost −
/// Σ carve-outs with additional cost booked into STOCK value not P&amp;L (RQ-13/DP-3), inter-godown movement
/// (RQ-14), Stock-Summary/Balance-Sheet reflection (RQ-15), and the PR-4 hard gate (manufacture N units,
/// components scaled, FG valued exactly, reconciles into Stock Summary + Balance Sheet Stock-in-Hand to the
/// paisa). ER-13: a non-manufacturing item is byte-identical to today. Pure, deterministic, paisa-exact —
/// exactly like the accounting core.
/// </summary>
public class ManufacturingJournalTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly D3 = new(2024, 4, 15);

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required BomService Boms { get; init; }
        public required ManufacturingJournalService Mfg { get; init; }
        public required StockValuationService Valuation { get; init; }
        public required InventoryLedger OnHand { get; init; }
        public required Guid GroupId { get; init; }
        public required Guid UnitId { get; init; }
        public required Guid GodownId { get; init; }
    }

    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Mfg Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            Boms = new BomService(c),
            Mfg = new ManufacturingJournalService(c),
            Valuation = new StockValuationService(c),
            OnHand = new InventoryLedger(c),
            GroupId = grp.Id,
            UnitId = nos.Id,
            GodownId = c.MainLocation!.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private Guid Item(Kit k, string name, Money? standardCost = null,
        StockValuationMethod method = StockValuationMethod.AverageCost)
        => k.Masters.CreateStockItem(name, k.GroupId, k.UnitId, valuationMethod: method,
            standardCost: standardCost).Id;

    /// <summary>Creates a batch-tracked component (FIFO-by-inward; expiry off ⇒ FIFO order, DP-1).</summary>
    private Guid BatchItem(Kit k, string name, StockValuationMethod method = StockValuationMethod.AverageCost,
        bool useExpiry = false)
    {
        var item = k.Masters.CreateStockItem(name, k.GroupId, k.UnitId, valuationMethod: method);
        item.MaintainInBatches = true;
        item.UseExpiryDates = useExpiry;
        return item.Id;
    }

    /// <summary>Stocks a component in at a godown via a Receipt Note (inward, rated).</summary>
    private void Receive(Kit k, Guid item, DateOnly date, decimal qty, Money rate, Guid? godown = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), date,
            new[] { new InventoryAllocation(item, godown ?? k.GodownId, qty, StockDirection.Inward, rate) }));

    /// <summary>Stocks a batch-labelled lot in via a Receipt Note (inward, rated, batch).</summary>
    private void ReceiveBatch(Kit k, Guid item, DateOnly date, decimal qty, Money rate, string batch, Guid? godown = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), date,
            new[] { new InventoryAllocation(item, godown ?? k.GodownId, qty, StockDirection.Inward, rate, batch) }));

    // ================================================================ RQ-11 Manufacturing-Journal voucher type

    [Fact]
    public void Manufacturing_journal_type_is_stock_journal_base_with_the_flag_on()
    {
        var k = NewKit();
        var mj = k.Mfg.CreateManufacturingJournalType("Manufacturing Journal");

        Assert.Equal(VoucherBaseType.StockJournal, mj.BaseType);
        Assert.True(mj.UseAsManufacturingJournal);
        Assert.True(mj.IsManufacturingJournal);
        Assert.False(mj.IsPredefined);              // user-created, not one of the 24 seeds (RQ-11)
        Assert.True(mj.AffectsStock);
        Assert.False(mj.AffectsAccounts);           // pure inventory voucher — no P&L at manufacture
        Assert.Same(mj, k.Company.FindVoucherType(mj.Id));
    }

    [Fact]
    public void Manufacturing_journal_type_rejects_a_non_stock_journal_base()
    {
        var k = NewKit();
        // A manufacturing journal MUST derive from Stock Journal (RQ-11); any other base is rejected.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            k.Mfg.CreateManufacturingJournalType("Bad", VoucherBaseType.Sales));
        Assert.Contains("Stock Journal", ex.Message);
    }

    // ================================================================ RQ-12 auto-scaled consumption

    [Fact]
    public void Producing_n_units_consumes_per_block_over_unit_of_manufacture_times_n()
    {
        // BOM: per 10 units (unit-of-manufacture = 10) consume 20 A + 5 B. Producing 30 units ⇒ ×3 blocks.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var b = Item(k, "Comp B");
        Receive(k, a, D1, 1000m, Money.FromRupees(10m));
        Receive(k, b, D1, 1000m, Money.FromRupees(4m));

        var bom = k.Boms.CreateBom(fg, "Standard", unitOfManufacture: 10m, new[]
        {
            new BomLine(BomLineType.Component, a, quantityPerBlock: 20m),
            new BomLine(BomLineType.Component, b, quantityPerBlock: 5m),
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        k.Mfg.Manufacture(mjType.Id, bom.Id, quantity: 30m, D2, k.GodownId, k.GodownId);

        // (20 ÷ 10) × 30 = 60 A consumed; (5 ÷ 10) × 30 = 15 B consumed.
        Assert.Equal(1000m - 60m, k.OnHand.OnHand(a, D2));
        Assert.Equal(1000m - 15m, k.OnHand.OnHand(b, D2));
        Assert.Equal(30m, k.OnHand.OnHand(fg, D2)); // 30 finished units produced
    }

    // ================================================================ RQ-13 FG valuation = components + add'l − carve-outs

    [Fact]
    public void Finished_good_value_is_sum_of_component_costs()
    {
        // Per-1 BOM: 2 A @ ₹10 + 3 B @ ₹4 = ₹32 per finished unit. Produce 5 ⇒ FG value ₹160.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var b = Item(k, "Comp B");
        Receive(k, a, D1, 100m, Money.FromRupees(10m));
        Receive(k, b, D1, 100m, Money.FromRupees(4m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, 2m),
            new BomLine(BomLineType.Component, b, 3m),
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId);

        Assert.Equal(Money.FromRupees(160m), result.FinishedGoodValue);
        Assert.Equal(Money.FromRupees(32m), result.FinishedGoodUnitRate);
        Assert.Equal(Money.FromRupees(160m), k.Valuation.ClosingValue(fg, D2).Value);
    }

    [Fact]
    public void Additional_cost_adds_to_finished_good_stock_value_not_pnl()
    {
        // Components ₹160; additional cost (labour ₹40) ⇒ FG value ₹200. No accounting voucher is booked, so
        // additional cost lives entirely in the FG STOCK value (RQ-13), never a separate P&L expense.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 100m, Money.FromRupees(16m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var addl = new[] { new ManufacturingAdditionalCost("Labour", Money.FromRupees(40m)) };
        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId,
            additionalCosts: addl);

        // 5 × (2 × 16) = ₹160 components + ₹40 labour = ₹200.
        Assert.Equal(Money.FromRupees(200m), result.FinishedGoodValue);
        Assert.Equal(Money.FromRupees(40m), result.AdditionalCostTotal);
        Assert.Equal(Money.FromRupees(200m), k.Valuation.ClosingValue(fg, D2).Value);

        // No accounting voucher exists — additional cost never hit P&L (it is baked into stock).
        Assert.Empty(k.Company.Vouchers);
        var pl = ProfitAndLoss.Build(k.Company, D2, ClosingStockMode.InventoryDerived);
        Assert.DoesNotContain(pl.Expenses, r => r.LedgerName.Contains("Labour"));
    }

    [Fact]
    public void By_product_and_scrap_value_is_carved_out_of_finished_good_cost()
    {
        // Components ₹300; by-product carved at ₹50 (rate ₹25 × 2), scrap carved at ₹10 ⇒ FG value ₹240.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var byp = Item(k, "By-Product");
        var scr = Item(k, "Scrap");
        Receive(k, a, D1, 100m, Money.FromRupees(30m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, 2m),                                          // 2 A @ ₹30 = ₹60/unit
            new BomLine(BomLineType.ByProduct, byp, 0.4m, rate: Money.FromRupees(25m)),          // 0.4 × ₹25 = ₹10/unit
            new BomLine(BomLineType.Scrap, scr, 1m, rate: Money.FromRupees(2m)),                 // 1 × ₹2 = ₹2/unit
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId);

        // 5 units: components 5×₹60 = ₹300; carve-out by-product 5×₹10 = ₹50, scrap 5×₹2 = ₹10 ⇒ ₹300−₹60 = ₹240.
        Assert.Equal(Money.FromRupees(300m), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(60m), result.CarveOutTotal);
        Assert.Equal(Money.FromRupees(240m), result.FinishedGoodValue);
        Assert.Equal(Money.FromRupees(240m), k.Valuation.ClosingValue(fg, D2).Value);

        // The by-product and scrap are ADDED to stock at their carve-out value.
        Assert.Equal(2m, k.OnHand.OnHand(byp, D2));   // 0.4 × 5
        Assert.Equal(5m, k.OnHand.OnHand(scr, D2));   // 1 × 5
        Assert.Equal(Money.FromRupees(50m), k.Valuation.ClosingValue(byp, D2).Value); // 2 × ₹25
        Assert.Equal(Money.FromRupees(10m), k.Valuation.ClosingValue(scr, D2).Value); // 5 × ₹2
    }

    [Fact]
    public void Carve_out_percent_basis_uses_percentage_of_pre_carve_cost()
    {
        // Components ₹200; scrap carved at 5% of pre-carve cost ⇒ ₹10 carved ⇒ FG value ₹190.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var scr = Item(k, "Scrap");
        Receive(k, a, D1, 100m, Money.FromRupees(20m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, 2m),                                     // 2 × ₹20 = ₹40/unit
            new BomLine(BomLineType.Scrap, scr, 1m, percentOfFinishedGoodCost: 5m),        // 5% of pre-carve
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId);

        Assert.Equal(Money.FromRupees(200m), result.ComponentCostTotal); // 5 × ₹40
        Assert.Equal(Money.FromRupees(10m), result.CarveOutTotal);       // 5% of ₹200
        Assert.Equal(Money.FromRupees(190m), result.FinishedGoodValue);
    }

    [Fact]
    public void Carve_out_defaults_to_component_standard_cost_when_no_rate_or_percent()
    {
        // A scrap line with NO rate/% defaults to the scrap item's standard cost (DP-3).
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var scr = Item(k, "Scrap", standardCost: Money.FromRupees(3m));
        Receive(k, a, D1, 100m, Money.FromRupees(20m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, 2m),        // 2 × ₹20 = ₹40/unit
            new BomLine(BomLineType.Scrap, scr, 1m),          // no rate/% ⇒ std cost ₹3
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId);

        Assert.Equal(Money.FromRupees(200m), result.ComponentCostTotal); // 5 × ₹40
        Assert.Equal(Money.FromRupees(15m), result.CarveOutTotal);       // 5 units × ₹3 std
        Assert.Equal(Money.FromRupees(185m), result.FinishedGoodValue);
    }

    // ================================================================ RQ-13/DP-8/ER-5 batch-aware component consumption

    [Fact]
    public void Manufacture_consumes_a_batch_tracked_component_drawing_down_the_fefo_fifo_batches_at_their_own_rates()
    {
        // A10 finding #1 (CRITICAL): a batch-tracked component was posted with NO batch label, so the outward hit
        // the empty-batch bucket (zero inward) and the no-negative guard blocked EVERY such manufacture. The fix
        // emits one outward per FIFO/FEFO batch pick, carrying that lot's label + its own inward rate.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = BatchItem(k, "Comp A");                               // batch-tracked, expiry off ⇒ FIFO-by-inward
        ReceiveBatch(k, a, D1, 100m, Money.FromRupees(10m), "B1");     // 100 @ ₹10 (earliest)
        ReceiveBatch(k, a, D2, 100m, Money.FromRupees(20m), "B2");     // 100 @ ₹20 (later)

        // BOM consumes 50 A per finished unit; produce 1 unit ⇒ 50 A drawn FIFO from B1 first (all @ ₹10).
        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 50m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 1m, D3, k.GodownId, k.GodownId);

        // Batch on-hand is drawn down correctly: B1 loses 50, B2 untouched.
        Assert.Equal(50m, k.OnHand.OnHand(a, k.GodownId, "B1", D3));
        Assert.Equal(100m, k.OnHand.OnHand(a, k.GodownId, "B2", D3));
        Assert.Equal(150m, k.OnHand.OnHand(a, D3)); // 200 − 50

        // Valued at B1's OWN inward rate (₹10), not the item average (₹15): FG value = 50 × ₹10 = ₹500.
        Assert.Equal(Money.FromRupees(500m), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(500m), result.FinishedGoodValue);
        Assert.Equal(1m, k.OnHand.OnHand(fg, D3));
        Assert.Equal(Money.FromRupees(500m), k.Valuation.ClosingValue(fg, D3).Value);
    }

    [Fact]
    public void Manufacture_consumes_across_two_batches_when_the_first_is_short_valuing_each_lot_at_its_own_rate()
    {
        // 150 A consumed spans B1 (100 @ ₹10) then B2 (50 @ ₹20): value = 100×₹10 + 50×₹20 = ₹2000. Each lot is
        // drawn down and priced at its OWN rate (DP-8), and batch on-hand reports the exact remainder.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = BatchItem(k, "Comp A");
        ReceiveBatch(k, a, D1, 100m, Money.FromRupees(10m), "B1");
        ReceiveBatch(k, a, D2, 100m, Money.FromRupees(20m), "B2");

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 150m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 1m, D3, k.GodownId, k.GodownId);

        Assert.Equal(0m, k.OnHand.OnHand(a, k.GodownId, "B1", D3));    // B1 fully consumed
        Assert.Equal(50m, k.OnHand.OnHand(a, k.GodownId, "B2", D3));   // B2 partially consumed
        Assert.Equal(Money.FromRupees(2000m), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(2000m), k.Valuation.ClosingValue(fg, D3).Value);
    }

    [Fact]
    public void Manufacture_is_blocked_when_a_batch_tracked_component_is_short_across_all_batches()
    {
        // ER-7 still applies to batch-tracked components: 60 on hand across two lots, consume 100 ⇒ rejected,
        // nothing persisted (the residual line over-draws and the no-negative guard fires — as it must).
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = BatchItem(k, "Comp A");
        ReceiveBatch(k, a, D1, 40m, Money.FromRupees(10m), "B1");
        ReceiveBatch(k, a, D2, 20m, Money.FromRupees(20m), "B2");

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 100m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        Assert.Throws<InvalidOperationException>(() =>
            k.Mfg.Manufacture(mjType.Id, bom.Id, 1m, D3, k.GodownId, k.GodownId));
        Assert.Equal(40m, k.OnHand.OnHand(a, k.GodownId, "B1", D3)); // untouched
        Assert.Equal(20m, k.OnHand.OnHand(a, k.GodownId, "B2", D3));
        Assert.Equal(0m, k.OnHand.OnHand(fg, D3));
    }

    // ================================================================ RQ-14 inter-godown movement

    [Fact]
    public void Components_leave_the_consumption_godown_and_fg_enters_the_production_godown()
    {
        var k = NewKit();
        var consume = k.GodownId;
        var produce = k.Masters.CreateGodown("Finished Goods Store").Id;

        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 100m, Money.FromRupees(10m), godown: consume);

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, consumptionGodownId: consume, productionGodownId: produce);

        // Component A left the consumption godown; none entered the production godown.
        Assert.Equal(100m - 10m, k.OnHand.OnHand(a, consume, D2));
        Assert.Equal(0m, k.OnHand.OnHand(a, produce, D2));
        // Finished good entered the production godown; none in the consumption godown.
        Assert.Equal(5m, k.OnHand.OnHand(fg, produce, D2));
        Assert.Equal(0m, k.OnHand.OnHand(fg, consume, D2));
    }

    [Fact]
    public void Bom_line_godown_overrides_the_journal_default_godown()
    {
        // A BOM line may pin its own consumption godown; it wins over the journal's default consumption godown.
        var k = NewKit();
        var dflt = k.GodownId;
        var special = k.Masters.CreateGodown("Special Store").Id;

        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 100m, Money.FromRupees(10m), godown: special);

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, 2m, godownId: special),
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, consumptionGodownId: dflt, productionGodownId: dflt);

        Assert.Equal(100m - 10m, k.OnHand.OnHand(a, special, D2)); // consumed from the pinned godown
        Assert.Equal(0m, k.OnHand.OnHand(a, dflt, D2));
    }

    // ================================================================ RQ-15 + PR-4 reconciliation hard gate

    [Fact]
    public void Pr4_manufacture_reconciles_into_stock_summary_and_balance_sheet_to_the_paisa()
    {
        // A BOM per 10 units (unit-of-manufacture 10): 20 A @ ₹10 + 40 B @ ₹5 = ₹400/block. Additional cost
        // ₹100; by-product carved at ₹5/unit. Produce 20 units (×2 blocks) — the classic scale guard (RQ-12).
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var b = Item(k, "Comp B");
        var byp = Item(k, "By-Product");
        // Book components + opening stock to Stock-in-Hand so the derived-closing invariant holds (BR-1).
        Receive(k, a, D1, 1000m, Money.FromRupees(10m));
        Receive(k, b, D1, 1000m, Money.FromRupees(5m));

        var bom = k.Boms.CreateBom(fg, "Std", unitOfManufacture: 10m, new[]
        {
            new BomLine(BomLineType.Component, a, quantityPerBlock: 20m),
            new BomLine(BomLineType.Component, b, quantityPerBlock: 40m),
            new BomLine(BomLineType.ByProduct, byp, quantityPerBlock: 4m, rate: Money.FromRupees(5m)),
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var addl = new[] { new ManufacturingAdditionalCost("Overhead", Money.FromRupees(100m)) };
        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 20m, D2, k.GodownId, k.GodownId, additionalCosts: addl);

        // Scaling: ×2 blocks. A: 40 @ ₹10 = ₹400; B: 80 @ ₹5 = ₹400 ⇒ components ₹800.
        // Additional ₹100. By-product: 8 @ ₹5 = ₹40 carved out. FG = 800 + 100 − 40 = ₹860 (₹43.00/unit).
        Assert.Equal(Money.FromRupees(800m), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(100m), result.AdditionalCostTotal);
        Assert.Equal(Money.FromRupees(40m), result.CarveOutTotal);
        Assert.Equal(Money.FromRupees(860m), result.FinishedGoodValue);
        Assert.Equal(Money.FromRupees(43m), result.FinishedGoodUnitRate);

        // Components consumed (scaled by N and unit-of-manufacture).
        Assert.Equal(1000m - 40m, k.OnHand.OnHand(a, D2));
        Assert.Equal(1000m - 80m, k.OnHand.OnHand(b, D2));
        Assert.Equal(20m, k.OnHand.OnHand(fg, D2));
        Assert.Equal(8m, k.OnHand.OnHand(byp, D2));

        // Stock Summary reconciliation: A ₹9600 + B ₹4600 + FG ₹860 + by-product ₹40 = ₹15,100.
        Assert.Equal(Money.FromRupees(9600m), k.Valuation.ClosingValue(a, D2).Value);   // 960 × ₹10
        Assert.Equal(Money.FromRupees(4600m), k.Valuation.ClosingValue(b, D2).Value);   // 920 × ₹5
        Assert.Equal(Money.FromRupees(860m), k.Valuation.ClosingValue(fg, D2).Value);
        Assert.Equal(Money.FromRupees(40m), k.Valuation.ClosingValue(byp, D2).Value);   // 8 × ₹5
        Assert.Equal(Money.FromRupees(15100m), k.Valuation.TotalClosingStockValue(D2));

        // Manufacturing conserves stock VALUE to the paisa: inputs bought = ₹10,000 (A) + ₹5,000 (B) = ₹15,000;
        // + ₹100 additional cost baked into stock = ₹15,100 total. Nothing lost, nothing double-counted.
        Assert.Equal(Money.FromRupees(15100m), k.Valuation.TotalClosingStockValue(D2));
    }

    [Fact]
    public void Finished_good_stock_value_is_conserved_to_the_paisa_when_value_does_not_divide_evenly()
    {
        // A10 finding #2 (HIGH): a rounded per-unit rate (₹1240 ÷ 30 = ₹41.3333… → ₹41.33) booked 41.33 × 30 =
        // ₹1239.90 — a 10-paisa loss that never reconciles, violating PR-4/ER-2 conservation. The fix books the FG
        // inward so Σ line values = the EXACT finished-good value (₹1240.00), reconciling to the paisa.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 1000m, Money.FromRupees(10m));

        var bom = k.Boms.CreateBom(fg, "Std", unitOfManufacture: 10m, new[]
        {
            new BomLine(BomLineType.Component, a, quantityPerBlock: 40m), // 40/block → ×3 = 120 @ ₹10 = ₹1200
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var addl = new[] { new ManufacturingAdditionalCost("Overhead", Money.FromRupees(40m)) };
        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 30m, D2, k.GodownId, k.GodownId, additionalCosts: addl);

        Assert.Equal(Money.FromRupees(1240m), result.FinishedGoodValue);   // exact target value (₹1200 + ₹40)
        Assert.Equal(30m, k.OnHand.OnHand(fg, D2));                        // all 30 units produced
        // Conservation: the FG stock value booked EQUALS components + additional − carve-outs, to the paisa.
        var booked = k.Valuation.ClosingValue(fg, D2).Value;
        Assert.Equal(Money.FromRupees(1240m), booked);
        // And it reconciles into the total closing stock value: 880 A @ ₹10 = ₹8800 remaining + ₹1240 FG = ₹10,040.
        Assert.Equal(Money.FromRupees(8800m), k.Valuation.ClosingValue(a, D2).Value);
        Assert.Equal(Money.FromRupees(10040m), k.Valuation.TotalClosingStockValue(D2));
    }

    [Fact]
    public void Finished_good_stock_value_is_conserved_under_fifo_when_value_does_not_divide_evenly()
    {
        // The same conservation must hold under a layer method (FIFO), which values surviving layers directly —
        // a per-unit-rate rounding would otherwise short the FG value. ₹1240 across 7 units (₹177.142857…/unit).
        var k = NewKit();
        var fg = Item(k, "Widget", method: StockValuationMethod.Fifo);
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 1000m, Money.FromRupees(10m));

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[]
        {
            new BomLine(BomLineType.Component, a, quantityPerBlock: 120m), // 120 @ ₹10 = ₹1200 for 1 block
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        // Produce 7 units from 7 blocks-worth: 7 × 120 = 840 A @ ₹10 = ₹8400 + ₹40 add'l = ₹8440 over 7 units.
        var addl = new[] { new ManufacturingAdditionalCost("Overhead", Money.FromRupees(40m)) };
        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, 7m, D2, k.GodownId, k.GodownId, additionalCosts: addl);

        Assert.Equal(Money.FromRupees(8440m), result.FinishedGoodValue);
        Assert.Equal(7m, k.OnHand.OnHand(fg, D2));
        Assert.Equal(Money.FromRupees(8440m), k.Valuation.ClosingValue(fg, D2).Value); // exact under FIFO too
    }

    // ================================================================ REGRESSION: paisa-conservation across ALL lines

    [Fact]
    public void Regression_percent_basis_carve_out_conserves_every_paisa_across_all_inventory_lines()
    {
        // REGRESSION (percent-basis carve-out remainder leak). A carve-out priced by PercentOfFinishedGoodCost
        // yields a total value (₹10.00) that does NOT divide evenly across the produced scrap quantity (3), so the
        // back-derived per-unit rate re-rounds (₹10.00 ÷ 3 = ₹3.3333… → ₹3.33). Pre-fix the carve-out was booked as
        // a SINGLE inward line at that rate: round(₹3.33 × 3) = ₹9.99 — a 1-paisa leak with no counter-entry, so the
        // net stock-value delta across all lines was −₹0.01 (a paisa vanished). The fix splits the carve-out inward
        // (2 @ ₹3.33 + 1 @ ₹3.34) so its booked stock value equals the ₹10.00 carved out, to the paisa (DP-3/PR-4).
        var k = NewKit();
        var fg = Item(k, "Widget");
        var steel = Item(k, "Steel");
        var scrap = Item(k, "Scrap");
        // Component Steel opening 200 @ ₹1.00 = ₹200.00 of stock value in the system before any manufacture.
        Receive(k, steel, D1, 200m, Money.FromRupees(1m));
        var totalBefore = k.Valuation.TotalClosingStockValue(D1);
        Assert.Equal(Money.FromRupees(200m), totalBefore);

        var bom = k.Boms.CreateBom(fg, "Std", unitOfManufacture: 1m, new[]
        {
            new BomLine(BomLineType.Component, steel, quantityPerBlock: 100m),   // 100 Steel @ ₹1 = ₹100 pre-carve
            new BomLine(BomLineType.Scrap, scrap, quantityPerBlock: 3m,          // percent basis (NOT a rate)
                percentOfFinishedGoodCost: 10m),                                 // 10% of ₹100 = ₹10.00 carved out
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, quantity: 1m, D2, k.GodownId, k.GodownId);

        // Components out ₹100.00 == FG ₹90.00 + carve-out ₹10.00 — figures reconcile exactly.
        Assert.Equal(Money.FromRupees(100m), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(10m), result.CarveOutTotal);
        Assert.Equal(Money.FromRupees(90m), result.FinishedGoodValue);

        // FG stock value = ₹90.00; the carve-out (scrap) stock value booked = ₹10.00 EXACTLY (₹9.99 pre-fix).
        Assert.Equal(Money.FromRupees(90m), k.Valuation.ClosingValue(fg, D2).Value);
        Assert.Equal(Money.FromRupees(10m), k.Valuation.ClosingValue(scrap, D2).Value);
        Assert.Equal(3m, k.OnHand.OnHand(scrap, D2)); // 3 scrap produced, on-hand intact
        // Steel drained by exactly the consumed value: 100 remaining @ ₹1 = ₹100.00.
        Assert.Equal(Money.FromRupees(100m), k.Valuation.ClosingValue(steel, D2).Value);

        // THE LOCK: net stock-value delta across ALL inventory lines = 0 paisa. Nothing vanished, nothing appeared.
        // components out ₹100.00 == FG ₹90.00 + carve-out ₹10.00 ⇒ total after == total before (₹200.00). Pre-fix
        // this was ₹199.99 (a paisa leaked), so this assertion FAILS on the pre-fix code and PASSES now.
        var totalAfter = k.Valuation.TotalClosingStockValue(D2);
        Assert.Equal(Money.FromRupees(200m), totalAfter);
        Assert.Equal(totalBefore, totalAfter); // net delta == exactly 0 paisa
    }

    [Theory]
    // FIFO issues the OLDEST layer (10 @ ₹10 = ₹100), leaving 10 @ ₹20 = ₹200 of Bolt stock.
    [InlineData(StockValuationMethod.Fifo, 100, 200)]
    // LIFO issues the NEWEST layer (10 @ ₹20 = ₹200), leaving 10 @ ₹10 = ₹100 of Bolt stock.
    [InlineData(StockValuationMethod.Lifo, 200, 100)]
    public void Regression_non_batch_layer_component_absorbs_the_true_issue_cost_not_the_average(
        StockValuationMethod method, decimal expectedIssueRupees, decimal expectedRemainingBoltRupees)
    {
        // REGRESSION (non-batch FIFO/LIFO component costed at the AVERAGE instead of the layer issue cost). A
        // FIFO/LIFO component's outward drains cost LAYERS and the valuation engine IGNORES the outward line's own
        // rate — so the finished good must absorb the method-consistent ISSUE cost, not the surviving-layer average.
        // Bolt (2 layers, 10 @ ₹10 then 10 @ ₹20) has a closing average of ₹15.00; pre-fix the FG absorbed 10 × ₹15
        // = ₹150.00 while the stock actually drained at the FIFO issue cost ₹100.00 — injecting ₹50 of phantom stock
        // value with no counter-entry (PR-4). The fix values the consumption through IssueValue, so the value booked
        // into the FG equals exactly what the component's stock loses, to the paisa.
        var k = NewKit();
        var fg = Item(k, "Widget");                                   // FG default (Average) — only the component method matters
        var bolt = Item(k, "Bolt", method: method);
        Receive(k, bolt, D1, 10m, Money.FromRupees(10m));             // layer 1: 10 @ ₹10 (oldest)
        Receive(k, bolt, D2, 10m, Money.FromRupees(20m));             // layer 2: 10 @ ₹20 (newest)

        // Sanity: closing average would be ₹15.00 (₹300 ÷ 20) — the WRONG number the FG must NOT absorb.
        var boltBefore = k.Valuation.ClosingValue(bolt, D2);
        Assert.Equal(20m, boltBefore.Quantity);
        Assert.Equal(Money.FromRupees(300m), boltBefore.Value);
        var totalBefore = k.Valuation.TotalClosingStockValue(D2);
        Assert.Equal(Money.FromRupees(300m), totalBefore);

        var bom = k.Boms.CreateBom(fg, "Std", unitOfManufacture: 1m, new[]
        {
            new BomLine(BomLineType.Component, bolt, quantityPerBlock: 10m),
        });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        var result = k.Mfg.Manufacture(mjType.Id, bom.Id, quantity: 1m, D3, k.GodownId, k.GodownId);

        // The FG absorbs the true FIFO/LIFO issue cost (₹100 / ₹200), NOT the ₹150 average.
        Assert.Equal(Money.FromRupees(expectedIssueRupees), result.ComponentCostTotal);
        Assert.Equal(Money.FromRupees(expectedIssueRupees), result.FinishedGoodValue);
        Assert.Equal(Money.FromRupees(expectedIssueRupees), k.Valuation.ClosingValue(fg, D3).Value);

        // The Bolt's surviving layer values to the expected remainder (₹200 / ₹100).
        var boltAfter = k.Valuation.ClosingValue(bolt, D3);
        Assert.Equal(10m, boltAfter.Quantity);
        Assert.Equal(Money.FromRupees(expectedRemainingBoltRupees), boltAfter.Value);

        // THE LOCK: the component's stock-value reduction == the value booked into the FG, to the paisa — no phantom
        // stock. Pre-fix the reduction (₹100 FIFO) ≠ the value booked (₹150 average), leaking ₹50; PASSES now.
        var boltReduction = boltBefore.Value - boltAfter.Value;
        Assert.Equal(result.FinishedGoodValue, boltReduction);
        // And total stock value is conserved end-to-end: Bolt-out == FG-in ⇒ total unchanged at ₹300.00.
        Assert.Equal(totalBefore, k.Valuation.TotalClosingStockValue(D3));
    }

    // ================================================================ posting-path guards (RQ-15 / ER-7)

    [Fact]
    public void Manufacture_is_blocked_when_a_component_is_short()
    {
        // No-negative-stock guard (DP-7/ER-7) applies on the consumption path exactly like a Delivery Note.
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 5m, Money.FromRupees(10m)); // only 5 on hand

        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        // Producing 5 needs 10 A, but only 5 exist ⇒ rejected, nothing persisted.
        Assert.Throws<InvalidOperationException>(() =>
            k.Mfg.Manufacture(mjType.Id, bom.Id, 5m, D2, k.GodownId, k.GodownId));
        Assert.Equal(5m, k.OnHand.OnHand(a, D2)); // untouched
        Assert.Equal(0m, k.OnHand.OnHand(fg, D2));
    }

    [Fact]
    public void Manufacture_rejects_a_type_that_is_not_a_manufacturing_journal()
    {
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 100m, Money.FromRupees(10m));
        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });

        // A plain Stock Journal type (no Use-as-Manufacturing-Journal flag) cannot manufacture.
        var plainStockJournal = TypeId(k.Company, VoucherBaseType.StockJournal);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            k.Mfg.Manufacture(plainStockJournal, bom.Id, 5m, D2, k.GodownId, k.GodownId));
        Assert.Contains("Manufacturing Journal", ex.Message);
    }

    [Fact]
    public void Manufacture_rejects_a_non_positive_quantity()
    {
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        Receive(k, a, D1, 100m, Money.FromRupees(10m));
        var bom = k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        var mjType = k.Mfg.CreateManufacturingJournalType("Mfg");

        Assert.Throws<ArgumentException>(() =>
            k.Mfg.Manufacture(mjType.Id, bom.Id, 0m, D2, k.GodownId, k.GodownId));
    }

    // ================================================================ RQ-9/10 BOM master (the domain built earlier)

    [Fact]
    public void Bom_master_supports_multiple_named_boms_per_item()
    {
        var k = NewKit();
        var fg = Item(k, "Widget");
        var a = Item(k, "Comp A");
        var b = Item(k, "Comp B");

        var std = k.Boms.CreateBom(fg, "Standard", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        var eco = k.Boms.CreateBom(fg, "Economy", 1m, new[] { new BomLine(BomLineType.Component, b, 3m) });

        Assert.NotEqual(std.Id, eco.Id);
        Assert.Equal(2, k.Company.BomsFor(fg).Count());
        Assert.True(k.Company.FindStockItem(fg)!.SetComponents); // RQ-10: item flagged as manufactured
    }

    [Fact]
    public void Bom_name_is_unique_within_the_item_but_reusable_across_items()
    {
        var k = NewKit();
        var fg1 = Item(k, "Widget");
        var fg2 = Item(k, "Gadget");
        var a = Item(k, "Comp A");

        k.Boms.CreateBom(fg1, "Standard", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        // Duplicate name for the SAME item ⇒ rejected.
        Assert.Throws<InvalidOperationException>(() =>
            k.Boms.CreateBom(fg1, "Standard", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) }));
        // Same name for a DIFFERENT item ⇒ allowed.
        var ok = k.Boms.CreateBom(fg2, "Standard", 1m, new[] { new BomLine(BomLineType.Component, a, 2m) });
        Assert.Equal("Standard", ok.Name);
    }

    [Fact]
    public void Bom_requires_at_least_one_component_line()
    {
        var k = NewKit();
        var fg = Item(k, "Widget");
        var scr = Item(k, "Scrap");
        // A BOM with only a carve-out line and no Component is invalid.
        Assert.Throws<InvalidOperationException>(() =>
            k.Boms.CreateBom(fg, "Std", 1m, new[] { new BomLine(BomLineType.Scrap, scr, 1m) }));
    }

    // ================================================================ ER-13 zero regression

    [Fact]
    public void A_plain_stock_journal_still_requires_source_equals_destination()
    {
        // ER-13: the manufacturing relaxation does NOT loosen a plain Stock Journal — it still must balance.
        var k = NewKit();
        var a = Item(k, "Comp A");
        var b = Item(k, "Comp B");
        Receive(k, a, D1, 100m, Money.FromRupees(10m));

        var sj = TypeId(k.Company, VoucherBaseType.StockJournal);
        var unbalanced = InventoryVoucher.StockJournal(Guid.NewGuid(), sj, D2,
            source: new[] { new InventoryAllocation(a, k.GodownId, 10m, StockDirection.Outward, Money.FromRupees(10m)) },
            destination: new[] { new InventoryAllocation(b, k.GodownId, 5m, StockDirection.Inward, Money.FromRupees(20m)) });
        Assert.Throws<InvalidOperationException>(() => k.Posting.Post(unbalanced));
    }
}
