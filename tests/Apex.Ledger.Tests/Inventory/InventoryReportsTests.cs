using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Inventory report-projection tests (catalog §16; phase3-inventory-requirements RQ-28..RQ-33; slice 3.4a).
/// Each report is exercised over a small synthetic company (masters via <see cref="InventoryService"/>,
/// movements via <see cref="InventoryPostingService"/> and item-invoices via <see cref="LedgerService"/>) and
/// asserted for the reconciliation identities the requirements state: Stock-Summary opening + inward − outward
/// = closing and total = Σ items; Godown-Summary sums per location; the movement journal's running balance ties
/// to on-hand; each register lists the right vouchers over the period (excluding cancelled/post-dated-after);
/// Reorder-Status flags exactly the below-level items. Pure, deterministic, paisa-exact — like the accounting core.
/// </summary>
public class InventoryReportsTests
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
        public required Guid GroupId { get; init; }
        public required Guid UnitId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SecondGodownId { get; init; }
    }

    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Reports Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var wh2 = masters.CreateGodown("Warehouse 2");
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            GroupId = grp.Id,
            UnitId = nos.Id,
            MainGodownId = c.MainLocation!.Id,
            SecondGodownId = wh2.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private Guid Item(Kit k, string name, StockValuationMethod method = StockValuationMethod.Fifo,
        decimal? reorderLevel = null, decimal? minOrder = null)
        => k.Masters.CreateStockItem(name, k.GroupId, k.UnitId, valuationMethod: method,
            reorderLevel: reorderLevel, minimumOrderQuantity: minOrder).Id;

    private void Receive(Kit k, Guid item, Guid godown, DateOnly date, decimal qty, Money? rate)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), date,
            new[] { new InventoryAllocation(item, godown, qty, StockDirection.Inward, rate) }));

    private void Deliver(Kit k, Guid item, Guid godown, DateOnly date, decimal qty, Money? rate = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), date,
            new[] { new InventoryAllocation(item, godown, qty, StockDirection.Outward, rate) }));

    private void PhysicalCount(Kit k, Guid item, Guid godown, DateOnly date, decimal countedQty)
        => k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), date,
            new[] { new PhysicalStockLine(item, godown, countedQty, null) }));

    // ================================================================ StockSummary (RQ-28)

    [Fact]
    public void Stock_summary_reconciles_opening_inward_outward_and_closing_per_item()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40 @ ₹5, buy 60 @ ₹10, sell 50 → closing 50; FIFO value ₹500 (from valuation tests).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 50m);

        var summary = StockSummary.Build(k.Company, D4);
        var row = Assert.Single(summary.Rows);
        Assert.Equal("Widget", row.ItemName);
        Assert.Equal(40m, row.OpeningQuantity);   // opening balance carried into the period
        Assert.Equal(60m, row.InwardQuantity);
        Assert.Equal(50m, row.OutwardQuantity);
        Assert.Equal(50m, row.ClosingQuantity);
        // The reconciliation identity holds.
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
        Assert.Equal(Money.FromRupees(500m), row.ClosingValue);
        Assert.Equal(StockValuationMethod.Fifo, row.Method);
    }

    [Fact]
    public void Stock_summary_total_equals_sum_of_item_closing_values()
    {
        var k = NewKit();
        var a = Item(k, "A", StockValuationMethod.Fifo);
        var b = Item(k, "B", StockValuationMethod.LastPurchaseCost);
        // A: buy 100@10, buy 50@12, sell 80 → closing 70, FIFO ₹800.
        Receive(k, a, k.MainGodownId, D1, 100m, Money.FromRupees(10m));
        Receive(k, a, k.MainGodownId, D2, 50m, Money.FromRupees(12m));
        Deliver(k, a, k.MainGodownId, D3, 80m);
        // B: buy 5@20 → closing 5 × last purchase ₹20 = ₹100.
        Receive(k, b, k.MainGodownId, D1, 5m, Money.FromRupees(20m));

        var summary = StockSummary.Build(k.Company, D4);
        Assert.Equal(2, summary.Rows.Count);
        Assert.Equal(Money.FromRupees(900m), summary.TotalClosingValue);
        var valuation = new StockValuationService(k.Company);
        Assert.Equal(valuation.TotalClosingStockValue(D4), summary.TotalClosingValue);
    }

    [Fact]
    public void Stock_summary_inward_outward_include_stock_journal_both_arms()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(100m));
        // Stock journal: transfer 6 from Main to WH2 — source (outward) + destination (inward) both count.
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { new InventoryAllocation(item, k.MainGodownId, 6m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item, k.SecondGodownId, 6m, StockDirection.Inward) }));

        var row = Assert.Single(StockSummary.Build(k.Company, D4).Rows);
        Assert.Equal(6m, row.InwardQuantity);   // the destination arm
        Assert.Equal(6m, row.OutwardQuantity);  // the source arm
        Assert.Equal(10m, row.ClosingQuantity); // net unchanged
        Assert.Equal(10m, row.OpeningQuantity);
    }

    [Fact]
    public void Stock_summary_includes_item_invoice_movements()
    {
        var built = BuildIntegratedCompany();
        // Opening 100 @ ₹10, item-invoice purchase +50 @ ₹12, item-invoice sale −80 → closing 70, FIFO ₹800.
        var summary = StockSummary.Build(built.Company, D4);
        var row = Assert.Single(summary.Rows);
        Assert.Equal(100m, row.OpeningQuantity);
        Assert.Equal(50m, row.InwardQuantity);
        Assert.Equal(80m, row.OutwardQuantity);
        Assert.Equal(70m, row.ClosingQuantity);
        Assert.Equal(Money.FromRupees(800m), row.ClosingValue);
    }

    [Fact]
    public void Stock_summary_reconciles_when_a_physical_count_records_shrinkage_mid_period()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 (book 100), physical count 90 mid-period → shrinkage −10.
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 90m);

        var row = Assert.Single(StockSummary.Build(k.Company, D4).Rows);
        Assert.Equal(40m, row.OpeningQuantity);
        Assert.Equal(60m, row.InwardQuantity);
        Assert.Equal(10m, row.OutwardQuantity);   // shrinkage folded into outward
        Assert.Equal(90m, row.ClosingQuantity);   // on-hand honours the count
        // The flagship identity foots WITH a mid-period count.
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
        Assert.Equal(new InventoryLedger(k.Company).OnHand(item, D4), row.ClosingQuantity);
    }

    [Fact]
    public void Stock_summary_reconciles_when_a_physical_count_records_found_stock_mid_period()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 (book 100), physical count 120 → found stock +20.
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 120m);

        var row = Assert.Single(StockSummary.Build(k.Company, D4).Rows);
        Assert.Equal(40m, row.OpeningQuantity);
        Assert.Equal(80m, row.InwardQuantity);    // 60 GRN + 20 found-stock adjustment
        Assert.Equal(0m, row.OutwardQuantity);
        Assert.Equal(120m, row.ClosingQuantity);
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
    }

    [Fact]
    public void Stock_summary_zero_variance_count_adds_no_spurious_adjustment_and_still_foots()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 (book 100), count 100 → zero variance, nothing to fold.
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 100m);

        var row = Assert.Single(StockSummary.Build(k.Company, D4).Rows);
        Assert.Equal(40m, row.OpeningQuantity);
        Assert.Equal(60m, row.InwardQuantity);   // no phantom adjustment
        Assert.Equal(0m, row.OutwardQuantity);
        Assert.Equal(100m, row.ClosingQuantity);
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
    }

    [Fact]
    public void Stock_summary_reconciles_with_multiple_counts_in_one_period()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 (book 100), count 90 (−10), GRN +30 (book 120), count 115 (−5).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 90m);
        Receive(k, item, k.MainGodownId, D3, 30m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D4, 115m);

        var row = Assert.Single(StockSummary.Build(k.Company, D4).Rows);
        Assert.Equal(40m, row.OpeningQuantity);
        Assert.Equal(90m, row.InwardQuantity);   // 60 + 30 GRNs
        Assert.Equal(15m, row.OutwardQuantity);  // 10 + 5 shrinkage adjustments
        Assert.Equal(115m, row.ClosingQuantity);
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
    }

    [Fact]
    public void Stock_summary_count_on_period_start_is_a_period_adjustment_not_opening()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 before the window, count 90 (−10) exactly on the window's first day.
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 90m);

        // Window [D2, D4]: opening is the day-before-D2 book (100), the D2 count is an in-period adjustment.
        var row = Assert.Single(StockSummary.Build(k.Company, D4, from: D2).Rows);
        Assert.Equal(100m, row.OpeningQuantity);
        Assert.Equal(0m, row.InwardQuantity);
        Assert.Equal(10m, row.OutwardQuantity);  // the count on `from` is a period adjustment
        Assert.Equal(90m, row.ClosingQuantity);
        Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);
    }

    // ================================================================ GodownSummary (RQ-29)

    [Fact]
    public void Godown_summary_sums_closing_quantity_per_location()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(100m));
        // Transfer 4 to WH2 → Main 6, WH2 4.
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { new InventoryAllocation(item, k.MainGodownId, 4m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item, k.SecondGodownId, 4m, StockDirection.Inward) }));

        var gs = GodownSummary.Build(k.Company, D4);
        Assert.Equal(2, gs.Rows.Count);
        var main = gs.Rows.Single(r => r.GodownName == "Main Location");
        var wh2 = gs.Rows.Single(r => r.GodownName == "Warehouse 2");
        Assert.Equal(6m, main.ClosingQuantity);
        Assert.Equal(4m, wh2.ClosingQuantity);
        // Σ godown quantities == item closing quantity.
        Assert.Equal(10m, main.ClosingQuantity + wh2.ClosingQuantity);
    }

    [Fact]
    public void Godown_summary_apportioned_values_sum_to_the_item_company_wide_closing_value()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 10 @ ₹100 = ₹1000; move 3 to WH2. Total closing value ₹1000 (avg method after no sale).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(100m));
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D1,
            source: new[] { new InventoryAllocation(item, k.MainGodownId, 3m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item, k.SecondGodownId, 3m, StockDirection.Inward) }));

        var valuation = new StockValuationService(k.Company);
        var itemValue = valuation.ClosingValue(item, D4).Value;
        var gs = GodownSummary.Build(k.Company, D4);
        var sum = gs.Rows.Aggregate(Money.Zero, (acc, r) => acc + r.ClosingValue);
        Assert.Equal(itemValue, sum);
        Assert.Equal(itemValue, gs.TotalClosingValue);
        // 7 @ ₹100 = ₹700 in Main, 3 @ ₹100 = ₹300 in WH2.
        Assert.Equal(Money.FromRupees(700m), gs.Rows.Single(r => r.GodownName == "Main Location").ClosingValue);
        Assert.Equal(Money.FromRupees(300m), gs.Rows.Single(r => r.GodownName == "Warehouse 2").ClosingValue);
    }

    [Fact]
    public void Godown_summary_omits_locations_with_zero_stock()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 5m, Money.FromRupees(10m));
        var gs = GodownSummary.Build(k.Company, D4);
        // WH2 has nothing ⇒ not listed.
        Assert.Single(gs.Rows);
        Assert.Equal("Main Location", gs.Rows[0].GodownName);
    }

    // ================================================================ StockItemMovement (RQ-28 drill)

    [Fact]
    public void Item_movement_running_balance_is_correct_and_ties_to_on_hand()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 50m);

        var mv = StockItemMovement.Build(k.Company, item, D4);
        Assert.Equal(40m, mv.OpeningQuantity);
        Assert.Equal(50m, mv.ClosingQuantity);
        Assert.Equal(2, mv.Rows.Count);
        // Row 1: GRN +60 → running 100. Row 2: Delivery −50 → running 50.
        Assert.Equal(60m, mv.Rows[0].InwardQuantity);
        Assert.Equal(100m, mv.Rows[0].RunningQuantity);
        Assert.Equal(50m, mv.Rows[1].OutwardQuantity);
        Assert.Equal(50m, mv.Rows[1].RunningQuantity);
        // Last running balance == closing == on-hand.
        var onHand = new InventoryLedger(k.Company).OnHand(item, D4);
        Assert.Equal(onHand, mv.Rows[^1].RunningQuantity);
        Assert.Equal(onHand, mv.ClosingQuantity);
        // Closing value ties to valuation engine.
        Assert.Equal(new StockValuationService(k.Company).ClosingValue(item, D4).Value, mv.ClosingValue);
    }

    [Fact]
    public void Item_movement_rows_are_chronological()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D3, 5m, Money.FromRupees(10m));
        Receive(k, item, k.MainGodownId, D1, 10m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 4m);

        var mv = StockItemMovement.Build(k.Company, item, D4);
        Assert.Equal(3, mv.Rows.Count);
        Assert.Equal(D1, mv.Rows[0].Date);
        Assert.Equal(D2, mv.Rows[1].Date);
        Assert.Equal(D3, mv.Rows[2].Date);
        Assert.Equal(11m, mv.ClosingQuantity); // 10 − 4 + 5
    }

    [Fact]
    public void Item_movement_emits_a_physical_stock_row_that_steps_running_to_the_counted_on_hand()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Opening 40, GRN +60 (running 100), physical count 90 → a "Physical Stock" −10 row lands running at 90.
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 90m);

        var mv = StockItemMovement.Build(k.Company, item, D4);
        Assert.Equal(40m, mv.OpeningQuantity);
        Assert.Equal(90m, mv.ClosingQuantity);
        Assert.Equal(2, mv.Rows.Count);
        // Row 1: GRN +60 → running 100.
        Assert.Equal(60m, mv.Rows[0].InwardQuantity);
        Assert.Equal(100m, mv.Rows[0].RunningQuantity);
        // Row 2: the Physical-Stock shrinkage row −10 → running 90.
        var count = mv.Rows[1];
        Assert.Equal("Physical Stock", count.VoucherTypeName);
        Assert.Equal(0m, count.InwardQuantity);
        Assert.Equal(10m, count.OutwardQuantity);
        Assert.Equal(90m, count.RunningQuantity);
        // Running ends at the counted on-hand.
        var onHand = new InventoryLedger(k.Company).OnHand(item, D4);
        Assert.Equal(onHand, mv.Rows[^1].RunningQuantity);
        Assert.Equal(onHand, mv.ClosingQuantity);
    }

    [Fact]
    public void Item_movement_physical_stock_row_is_inward_when_stock_is_found()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 120m); // found +20

        var mv = StockItemMovement.Build(k.Company, item, D4);
        var count = mv.Rows[^1];
        Assert.Equal("Physical Stock", count.VoucherTypeName);
        Assert.Equal(20m, count.InwardQuantity);
        Assert.Equal(0m, count.OutwardQuantity);
        Assert.Equal(120m, count.RunningQuantity);
        Assert.Equal(120m, mv.ClosingQuantity);
    }

    [Fact]
    public void Item_movement_zero_variance_count_produces_no_row()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 40m, Money.FromRupees(5m));
        Receive(k, item, k.MainGodownId, D1, 60m, Money.FromRupees(10m));
        PhysicalCount(k, item, k.MainGodownId, D2, 100m); // exactly the book → no adjustment

        var mv = StockItemMovement.Build(k.Company, item, D4);
        Assert.Single(mv.Rows); // only the GRN; the zero-variance count adds nothing
        Assert.Equal(100m, mv.Rows[^1].RunningQuantity);
        Assert.Equal(100m, mv.ClosingQuantity);
    }

    // ================================================================ Registers (RQ-31)

    [Fact]
    public void Receipt_note_register_lists_grn_lines_only()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 2m); // a delivery must NOT appear in the GRN register

        var reg = InventoryRegisters.BuildReceiptNotes(k.Company, FyStart, D4);
        var row = Assert.Single(reg);
        Assert.Equal("Widget", row.ItemName);
        Assert.Equal(5m, row.Quantity);
        Assert.Equal(StockDirection.Inward, row.Direction);
        Assert.Equal(Money.FromRupees(50m), row.Value); // 5 × ₹10
    }

    [Fact]
    public void Delivery_note_register_lists_delivery_lines_only()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 4m);
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m)); // not in delivery register

        var reg = InventoryRegisters.BuildDeliveryNotes(k.Company, FyStart, D4);
        var row = Assert.Single(reg);
        Assert.Equal(4m, row.Quantity);
        Assert.Equal(StockDirection.Outward, row.Direction);
    }

    [Fact]
    public void Rejection_register_lists_both_in_and_out_with_direction()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(10m));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionIn), D1,
            new[] { new InventoryAllocation(item, k.MainGodownId, 3m, StockDirection.Inward) }));
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.RejectionOut), D2,
            new[] { new InventoryAllocation(item, k.MainGodownId, 2m, StockDirection.Outward) }));

        var reg = InventoryRegisters.BuildRejections(k.Company, FyStart, D4);
        Assert.Equal(2, reg.Count);
        Assert.Contains(reg, r => r.Direction == StockDirection.Inward && r.Quantity == 3m);
        Assert.Contains(reg, r => r.Direction == StockDirection.Outward && r.Quantity == 2m);
    }

    [Fact]
    public void Physical_stock_register_shows_counted_vs_book_and_variance()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 10m, Money.FromRupees(10m));
        k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
            new[] { new PhysicalStockLine(item, k.MainGodownId, 8m, null) }));

        var reg = InventoryRegisters.BuildPhysicalStock(k.Company, FyStart, D4);
        var row = Assert.Single(reg);
        Assert.Equal(10m, row.BookQuantity);
        Assert.Equal(8m, row.CountedQuantity);
        Assert.Equal(-2m, row.Variance);
    }

    [Fact]
    public void Order_register_lists_purchase_and_sales_orders_with_outstanding_quantity()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder), D1,
            new[] { new OrderLine(item, k.MainGodownId, 100m, Money.FromRupees(90m)) }));
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.SalesOrder), D2,
            new[] { new OrderLine(item, k.MainGodownId, 50m, Money.FromRupees(150m)) }));

        var reg = InventoryRegisters.BuildOrders(k.Company, FyStart, D4);
        Assert.Equal(2, reg.Count);
        var po = reg.Single(r => r.OrderedQuantity == 100m);
        Assert.Equal(0m, po.FulfilledQuantity);
        Assert.Equal(100m, po.OutstandingQuantity);
        Assert.Equal(50m, reg.Single(r => r.VoucherTypeName.Contains("Sales")).OrderedQuantity);
    }

    [Fact]
    public void Registers_exclude_cancelled_and_out_of_period_and_post_dated_after()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // In-period GRN.
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        // Cancelled GRN — excluded.
        var cancelled = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { new InventoryAllocation(item, k.MainGodownId, 99m, StockDirection.Inward, Money.FromRupees(10m)) });
        k.Posting.Post(cancelled);
        k.Posting.Cancel(cancelled.Id);
        // Out-of-period GRN (after D4) — excluded from a [FyStart, D4] window.
        Receive(k, item, k.MainGodownId, new DateOnly(2024, 5, 1), 7m, Money.FromRupees(10m));

        var reg = InventoryRegisters.BuildReceiptNotes(k.Company, FyStart, D4);
        var row = Assert.Single(reg);
        Assert.Equal(5m, row.Quantity);
    }

    [Fact]
    public void Register_period_window_bounds_are_inclusive()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D2, 5m, Money.FromRupees(10m));
        // Window [D2, D2] includes the D2 GRN; window [D3, D4] excludes it.
        Assert.Single(InventoryRegisters.BuildReceiptNotes(k.Company, D2, D2));
        Assert.Empty(InventoryRegisters.BuildReceiptNotes(k.Company, D3, D4));
    }

    // ================================================================ ReorderStatus (RQ-33)

    [Fact]
    public void Reorder_status_flags_exactly_the_items_at_or_below_reorder_level()
    {
        var k = NewKit();
        var low = Item(k, "Low", reorderLevel: 20m, minOrder: 50m);   // closing 5 ≤ 20 → flagged
        var ok = Item(k, "Ok", reorderLevel: 20m);                    // closing 30 > 20 → not flagged
        var noLevel = Item(k, "NoLevel");                             // no reorder level → skipped
        Receive(k, low, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        Receive(k, ok, k.MainGodownId, D1, 30m, Money.FromRupees(10m));
        Receive(k, noLevel, k.MainGodownId, D1, 1m, Money.FromRupees(10m));

        var report = ReorderStatus.Build(k.Company, D4);
        var row = Assert.Single(report.Rows);
        Assert.Equal("Low", row.ItemName);
        Assert.Equal(5m, row.ClosingQuantity);
        Assert.Equal(20m, row.ReorderLevel);
        Assert.Equal(15m, row.Shortfall);              // 20 − 5
        Assert.Equal(50m, row.SuggestedOrderQuantity); // max(shortfall 15, min-order 50)
    }

    [Fact]
    public void Reorder_status_suggested_quantity_is_the_shortfall_when_it_exceeds_min_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100m, minOrder: 10m);
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(95m, row.Shortfall);              // 100 − 5
        Assert.Equal(95m, row.SuggestedOrderQuantity); // max(95, 10) = 95
    }

    [Fact]
    public void Reorder_status_at_exactly_the_level_is_flagged()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 5m);
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(0m, row.Shortfall);
    }

    // ================================================================ Reports façade

    [Fact]
    public void Report_facade_wrappers_delegate_to_the_projections()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 20m);
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 5m, Money.FromRupees(10m));

        Assert.Single(Report.BuildStockSummary(k.Company, D4).Rows);
        Assert.Single(Report.BuildGodownSummary(k.Company, D4).Rows);
        Assert.Equal("Widget", Report.BuildStockItemMovement(k.Company, item, D4).ItemName);
        Assert.Single(Report.BuildReorderStatus(k.Company, D4).Rows);
        Assert.Empty(Report.BuildReceiptNoteRegister(k.Company, FyStart, D4));
        Assert.Empty(Report.BuildDeliveryNoteRegister(k.Company, FyStart, D4));
        Assert.Empty(Report.BuildRejectionRegister(k.Company, FyStart, D4));
        Assert.Empty(Report.BuildPhysicalStockRegister(k.Company, FyStart, D4));
        Assert.Empty(Report.BuildOrderRegister(k.Company, FyStart, D4));
    }

    // ---------------------------------------------------------------- integrated item-invoice company

    /// <summary>
    /// A minimal accounts↔inventory-integrated company mirroring the valuation-test fixture: opening stock
    /// 100 @ ₹10 (FIFO), an item-invoice credit purchase (+50 @ ₹12) and an item-invoice credit sale (−80).
    /// Closing stock derives to ₹800 by FIFO. Used to prove the Stock Summary folds item-invoice movements.
    /// </summary>
    private static (Company Company, Guid ItemId) BuildIntegratedCompany()
    {
        var c = CompanyFactory.CreateSeeded("Integrated Reports Co", FyStart);
        var ledgers = new LedgerService(c);

        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var debtorsGrp = c.FindGroupByName("Sundry Debtors")!;
        var salesGrp = c.FindGroupByName("Sales Accounts")!;
        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;

        var debtor = AddLedger(c, "Debtor", debtorsGrp.Id, Money.Zero, openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", creditorsGrp.Id, Money.Zero, openingIsDebit: false);
        var sales = AddLedger(c, "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false);
        var purchases = AddLedger(c, "Purchases", purchasesGrp.Id, Money.Zero, openingIsDebit: true);

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(10m));

        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        // Item-invoice purchase: Dr Purchases 600 / Cr Creditor 600, stock inward 50 @ ₹12.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D2, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(600m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(600m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 50m, Money.FromRupees(12m)) }));
        // Item-invoice sale: Dr Debtor 1600 / Cr Sales 1600, stock outward 80 @ ₹20.
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType.Id, D3, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(1600m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(1600m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 80m, Money.FromRupees(20m)) }));

        return (c, item.Id);
    }

    private static Domain.Ledger AddLedger(Company c, string name, Guid groupId, Money opening, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, opening, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
