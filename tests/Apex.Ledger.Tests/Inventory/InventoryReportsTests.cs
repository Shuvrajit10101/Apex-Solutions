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
/// Reorder-Status lists <b>every</b> item that resolves a reorder level — no closing-stock filter (IV-10/WF-7,
/// Phase 10.10) — with the shortfall measured against Nett Available (Closing + Pending POs − Sales Orders Due),
/// never against closing stock alone. Pure, deterministic, paisa-exact — like the accounting core.
/// <para>🔴 <b>Do not "restore" a listing predicate to this report.</b> The pre-10.10 rule ("flags exactly the
/// below-level items") was invented — it appears in no Tally source — and was retired by user decision. If this
/// summary and <see cref="Apex.Ledger.Reports.ReorderStatus"/> ever disagree again, the code is authoritative and
/// this sentence is the bug: a doc restating an invented rule is exactly how DD-4/ER-13 survived four reviews.</para>
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

    private void Order(Kit k, VoucherBaseType baseType, Guid item, DateOnly date, decimal qty)
        => k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, baseType), date,
            new[] { new OrderLine(item, k.MainGodownId, qty, null) }));

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

    /// <summary>
    /// 🔴 <b>This fixture was VACUOUS until Phase 10.10 / WF-8 and is repaired here.</b> It posted two orders and
    /// <b>no fulfilling movement of any kind</b>, so its <c>Assert.Equal(0m, po.FulfilledQuantity)</c> /
    /// <c>Assert.Equal(100m, po.OutstandingQuantity)</c> were byte-identical to the deleted hard-code
    /// <c>FulfilledQuantity: 0m, OutstandingQuantity: line.Quantity</c> — measured: replacing
    /// <c>InventoryRegisters.BuildOrders</c>' fulfilment lookup with <c>var done = 0m</c> left it
    /// <b>green</b>, so the one test in this file whose NAME claims the outstanding column could not detect
    /// the entire defect WF-8 exists to fix. It also asserted only <c>OrderedQuantity</c> on the sales row and
    /// used round 100/50 quantities throughout.
    /// <para>Both arms now carry a partial movement on <b>odd-valued</b> quantities and both rows assert their
    /// own Fulfilled/Outstanding <b>components</b>: 90.625 ordered less 30.125 received leaves 60.500;
    /// 47.331 ordered less 15.375 delivered leaves 31.956. Under the hard-code this reads 0 / 90.625 and
    /// 0 / 47.331 and goes red. The order and the notes all leave the party on "(none)", which is the
    /// consistent-blank small book <see cref="Apex.Ledger.Reports.OrderFulfilment"/> documents as working —
    /// the cross-party attribution rules are pinned in <c>OrderFulfilmentTests</c>.</para>
    /// </summary>
    [Fact]
    public void Order_register_lists_purchase_and_sales_orders_with_outstanding_quantity()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder), D1,
            new[] { new OrderLine(item, k.MainGodownId, 90.625m, Money.FromRupees(90m)) }));
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.SalesOrder), D2,
            new[] { new OrderLine(item, k.MainGodownId, 47.331m, Money.FromRupees(150m)) }));
        // Part-received against the PO, part-delivered against the SO — without these the assertions below are
        // satisfied by the pre-WF-8 hard-code and lock nothing.
        Receive(k, item, k.MainGodownId, D2, 30.125m, Money.FromRupees(90m));
        Deliver(k, item, k.MainGodownId, D3, 15.375m);

        var reg = InventoryRegisters.BuildOrders(k.Company, FyStart, D4);
        Assert.Equal(2, reg.Count);
        var po = reg.Single(r => r.OrderedQuantity == 90.625m);
        Assert.Equal(30.125m, po.FulfilledQuantity);
        Assert.Equal(60.500m, po.OutstandingQuantity);
        var so = reg.Single(r => r.VoucherTypeName.Contains("Sales"));
        Assert.Equal(47.331m, so.OrderedQuantity);
        Assert.Equal(15.375m, so.FulfilledQuantity);
        Assert.Equal(31.956m, so.OutstandingQuantity);
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

    /// <summary>
    /// The listing rule (IV-10 / WF-7, Phase 10.10). The report lists <b>every</b> item that resolves to a
    /// reorder level, whatever its closing quantity — TallyHelp's Reorder Status page: "By default, all stock
    /// items from the selected stock group or category display… press F8 (Reorder Only)". The only engine-side
    /// exclusion is "no reorder level resolved". <b>Supersedes</b> the pre-10.10 test whose very name —
    /// <c>Reorder_status_flags_exactly_the_items_at_or_below_reorder_level</c> — asserted the invented
    /// closing-stock filter this slice deletes.
    /// </summary>
    [Fact]
    public void Reorder_status_lists_every_item_carrying_a_reorder_level_regardless_of_closing_quantity()
    {
        var k = NewKit();
        // Alpha is ABOVE its raw closing level yet short once its open sales orders are netted.
        var alpha = Item(k, "Alpha", reorderLevel: 100.75m, minOrder: 25.5m);
        // Beta is genuinely covered — listed, but with nothing to order.
        var beta = Item(k, "Beta", reorderLevel: 50.25m);
        // Gamma resolves no reorder level at all — the ONE surviving exclusion.
        var gamma = Item(k, "Gamma");
        Receive(k, alpha, k.MainGodownId, D1, 120.375m, Money.FromRupees(10.37m));
        Receive(k, beta, k.MainGodownId, D1, 200.625m, Money.FromRupees(8.19m));
        Receive(k, gamma, k.MainGodownId, D1, 7.875m, Money.FromRupees(13.41m));
        Order(k, VoucherBaseType.SalesOrder, alpha, D2, 60.125m);

        var rows = ReorderStatus.Build(k.Company, D4).Rows;
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.ItemName == "Gamma");

        var a = rows.Single(r => r.ItemName == "Alpha");
        Assert.Equal(120.375m, a.ClosingQuantity);
        Assert.Equal(60.125m, a.SalesOrdersDue);
        Assert.Equal(60.250m, a.NettAvailable);      // 120.375 + 0 − 60.125
        Assert.Equal(40.50m, a.Shortfall);           // 100.75 − 60.250
        Assert.Equal(40.50m, a.OrderToBePlaced);     // shortfall 40.50 > MOQ 25.5

        var b = rows.Single(r => r.ItemName == "Beta");
        Assert.Equal(200.625m, b.NettAvailable);
        Assert.Equal(0m, b.Shortfall);
        Assert.Equal(0m, b.OrderToBePlaced);
    }

    [Fact]
    public void Reorder_status_order_quantity_is_the_shortfall_when_it_exceeds_min_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100m, minOrder: 10m);
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(95m, row.Shortfall);          // 100 − 5
        Assert.Equal(95m, row.OrderToBePlaced);    // max(95, 10) = 95
    }

    /// <summary>
    /// 🔴 <b>RENAMED — the old name asserted the OPPOSITE of the body.</b> It was
    /// <c>Reorder_status_at_exactly_the_level_is_flagged</c>, a pre-10.10 name kept through the WF-7 rewrite even
    /// though the assertions below say the item is <b>NOT</b> flagged. TallyHelp's rule is strict: "Only when the
    /// quantity in Re-order Level column is <i>more than</i> the Nett Available column, the difference appears as
    /// Shortfall" — <b>at</b> the level is not <b>above</b> it, so there is no shortfall and (PR-8 having been
    /// retired by user decision) nothing to order. A name that contradicts its own assertions is worse than no
    /// name: the next reader "fixes" the code to match the name.
    /// <para>Re-fixtured off round numbers at the same time. The old 5/5 pair could not distinguish the boundary
    /// from any of the arithmetic around it — 5 − 5 = 0 is true under a great many wrong rules. Nett Available is
    /// pinned alongside Shortfall so the zero is shown to come from <c>level == nett</c> and not from an
    /// unpinned closing quantity that happens to cancel.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_at_exactly_the_level_has_no_shortfall_and_orders_nothing()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 47.331m);
        Receive(k, item, k.MainGodownId, D1, 47.331m, Money.FromRupees(10.37m));
        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(47.331m, row.ClosingQuantity);
        Assert.Equal(47.331m, row.ReorderLevel);
        Assert.Equal(47.331m, row.NettAvailable);   // no orders either way ⇒ nett == closing
        Assert.Equal(0m, row.Shortfall);            // AT the level, not below it
        Assert.Equal(0m, row.OrderToBePlaced);
    }

    // ---- Slice 6 + Phase 10.10: master definitions, rollup, Advanced consumption, Nett Available netting ----
    // (The former "the PR-8 gate" in this banner named the MOQ-floor-at-zero-shortfall rule, RETIRED by user
    //  decision under IV-10/WF-7 — see Reorder_status_places_no_order_when_the_shortfall_is_nil_….)

    [Fact]
    public void Reorder_status_group_definition_applies_to_items_and_nested_child_group()
    {
        var k = NewKit();
        var parent = k.Masters.CreateStockGroup("Beverages");
        var child = k.Masters.CreateStockGroup("Juices", parent.Id);
        var directItem = k.Masters.CreateStockItem("Cola", parent.Id, k.UnitId).Id;
        var nestedItem = k.Masters.CreateStockItem("Mango", child.Id, k.UnitId).Id;

        // A single Group-scoped definition on the PARENT covers the parent's item and the nested child's item.
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Group, parent.Id, reorderQuantity: 20m);
        Receive(k, directItem, k.MainGodownId, D1, 5m, Money.FromRupees(10m));
        Receive(k, nestedItem, k.MainGodownId, D1, 8m, Money.FromRupees(10m));

        var rows = ReorderStatus.Build(k.Company, D4).Rows;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(20m, r.ReorderLevel));
    }

    [Fact]
    public void Reorder_status_item_definition_overrides_the_group_one()
    {
        var k = NewKit();
        var group = k.Masters.CreateStockGroup("Snacks");
        var item = k.Masters.CreateStockItem("Chips", group.Id, k.UnitId).Id;
        var svc = new ReorderLevelsService(k.Company);
        svc.CreateOrUpdate(ReorderScope.Group, group.Id, reorderQuantity: 20m);
        svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 50m);   // most-specific wins
        Receive(k, item, k.MainGodownId, D1, 5m, Money.FromRupees(10m));

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(50m, row.ReorderLevel);
    }

    [Fact]
    public void Reorder_status_falls_back_to_category_when_no_item_or_group_definition()
    {
        var k = NewKit();
        var parentCat = k.Masters.CreateStockCategory("Perishable");
        var childCat = k.Masters.CreateStockCategory("Dairy", parentCat.Id);
        var item = k.Masters.CreateStockItem("Milk", k.GroupId, k.UnitId, categoryId: childCat.Id).Id;
        // Definition on the PARENT category — resolved via the nearest-ancestor category walk.
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Category, parentCat.Id, reorderQuantity: 12m);
        Receive(k, item, k.MainGodownId, D1, 3m, Money.FromRupees(10m));

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(12m, row.ReorderLevel);
    }

    [Fact]
    public void Reorder_status_group_beats_category_when_both_apply()
    {
        var k = NewKit();
        var group = k.Masters.CreateStockGroup("Hardware");
        var cat = k.Masters.CreateStockCategory("Metal");
        var item = k.Masters.CreateStockItem("Bolt", group.Id, k.UnitId, categoryId: cat.Id).Id;
        var svc = new ReorderLevelsService(k.Company);
        svc.CreateOrUpdate(ReorderScope.Category, cat.Id, reorderQuantity: 5m);
        svc.CreateOrUpdate(ReorderScope.Group, group.Id, reorderQuantity: 30m);  // Group beats Category (DD-2)
        Receive(k, item, k.MainGodownId, D1, 2m, Money.FromRupees(10m));

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(30m, row.ReorderLevel);
    }

    [Fact]
    public void Reorder_status_advanced_higher_takes_max_of_fixed_and_consumption()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        // Consume 70 over the 1-month window ending D4 (an issue inside the window); closing 30.
        Receive(k, item, k.MainGodownId, D1, 100m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 70m);
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Item, item,
            reorderAdvanced: true, reorderQuantity: 25m, periodCount: 1, periodUnit: ExpiryPeriodUnit.Months,
            criteria: ReorderCriteria.Higher);

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(70m, row.ReorderLevel);   // max(fixed 25, consumption 70)
        Assert.Equal(30m, row.ClosingQuantity);
    }

    [Fact]
    public void Reorder_status_advanced_lower_takes_min_of_fixed_and_consumption()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D1, 100m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 40m);   // consumption 40, closing 60
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Item, item,
            reorderAdvanced: true, reorderQuantity: 25m, periodCount: 1, periodUnit: ExpiryPeriodUnit.Months,
            criteria: ReorderCriteria.Lower);

        // Effective level = min(25, 40) = 25. Post-10.10 the row is still LISTED (the closing-stock filter is
        // gone); what the Lower criterion controls is the LEVEL, and at nett available 60 there is no shortfall.
        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(25m, row.ReorderLevel);   // min(fixed 25, consumption 40)
        Assert.Equal(60m, row.NettAvailable);
        Assert.Equal(0m, row.Shortfall);
        Assert.Equal(0m, row.OrderToBePlaced);
    }

    [Fact]
    public void Reorder_status_consumption_window_excludes_issues_before_the_window_start()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, FyStart, 20m, Money.FromRupees(10m));
        // A 5-day window ending D4 (2024-04-20) → windowStart 2024-04-15 (D3), half-open (D3, D4].
        Deliver(k, item, k.MainGodownId, D3, 7m);    // ON the window start (excluded from consumption)
        Deliver(k, item, k.MainGodownId, D4, 9m);    // inside the window (included) → closing 4
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Item, item,
            reorderAdvanced: true, periodCount: 5, periodUnit: ExpiryPeriodUnit.Days,
            criteria: ReorderCriteria.Higher);   // null fixed ⇒ consumption alone

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(9m, row.ReorderLevel);      // only the D4 issue is in (D3, D4]
        Assert.Equal(4m, row.ClosingQuantity);   // 20 − 7 − 9 (both issues move stock)
    }

    [Fact]
    public void Reorder_status_consumption_is_deterministic_for_a_report_date()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D1, 100m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 70m);   // consumption 70, closing 30
        new ReorderLevelsService(k.Company).CreateOrUpdate(ReorderScope.Item, item,
            reorderAdvanced: true, periodCount: 1, periodUnit: ExpiryPeriodUnit.Months,
            criteria: ReorderCriteria.Higher);

        var a = ReorderStatus.Build(k.Company, D4).Rows.Single().ReorderLevel;
        var b = ReorderStatus.Build(k.Company, D4).Rows.Single().ReorderLevel;
        Assert.Equal(a, b);
        Assert.Equal(70m, a);
    }

    // ================================================================================================
    // 🔴 WHY THE PURCHASE-ORDER REORDER FIXTURES BELOW STOCK UP VIA AN **OPENING BALANCE**, NOT A RECEIPT NOTE.
    //
    // Post-WF-8 a Receipt Note is a FULFILMENT DOCUMENT. These fixtures used to receive stock at D1 and then
    // raise the purchase order at D2, and — because the Order and Receive helpers both leave the party blank —
    // the receipt and the order landed in the SAME (null, item) cohort. Their "Pending Purchase Orders" figure
    // was therefore correct only because OrderFulfilment refuses to retire an order dated AFTER the movement.
    // They passed for a reason none of them named, and a reader could not tell the intended assertion from the
    // accident. MEASURED, not assumed: deleting `if (open.Date > mv.Date) break;` from OrderFulfilment.Accumulate
    // turned five fixtures in this file red —
    //     …counts_a_post_dated_order_once_its_date_has_arrived      Expected 30.375  Actual 10.250
    //     …does_not_double_count_a_pending_purchase_order           Expected 30.375  Actual 10.250
    //     …lists_an_item_whose_pending_order_already_covers…        Expected 110.625 Actual 90.500
    //     …min_order_quantity_floors_the_order_while_a_PO_pending   Expected 40.375  Actual 9.500
    //     …nett_available_goes_negative_when_sales_orders_exceed…   Expected 8.375   Actual 0
    // (The edit was reverted.) An opening balance is not an inventory voucher and can never fulfil an order —
    // the same technique …nets_only_the_unreceived_remainder_of_a_partly_received_purchase_order already relies
    // on — so the four converted fixtures now hold their figures because nothing COULD have retired the order.
    //
    // ONE fixture deliberately keeps the receipt-before-order shape: the first one below, which asserts the date
    // bound explicitly. That keeps a single, named owner of the rule in this file instead of five silent ones.
    // (…cancelled_and_post_dated_purchase_orders_do_not_net also receives at D1 but was NOT converted: both its
    //  orders are excluded by cancellation and by the as-of bound, so it never depended on the date bound — it
    //  was the only one of the six that already passed for its stated reason.)
    // ================================================================================================

    /// <summary>
    /// A pending purchase order lifts Nett Available, and the Minimum Order Quantity still floors what remains —
    /// the "MOQ branch WITH a live purchase order" combination, which nothing else in this file exercises.
    /// <para>🔴 <b>This is the file's ONE deliberate date-bound fixture</b> — it keeps the D1-receipt /
    /// D2-order shape and pins the consequence outright (the <c>OutstandingForItem</c> assertion at the end).
    /// The four sibling fixtures that used to share the shape by accident were converted to opening balances;
    /// see the banner above.</para>
    /// <para><b>Re-fixtured (adversarial review).</b> The pre-review fixture (level 100, closing 20, PO 30, MOQ 10)
    /// pinned Nett Available, Shortfall AND Order to be Placed all to the same round <c>50</c>, so it discriminated
    /// nothing between three different columns — the project's round-number trap in its quantity form. Every figure
    /// here is odd and fractional and all four differ: closing 30.875, pending PO 40.375, Nett Available 71.250,
    /// Shortfall 49.375, Order to be Placed 75.25 (the MOQ, because 75.25 &gt; 49.375).</para>
    /// </summary>
    [Fact]
    public void Reorder_status_min_order_quantity_floors_the_order_while_a_purchase_order_is_pending()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 120.625m, minOrder: 75.25m);
        Receive(k, item, k.MainGodownId, D1, 30.875m, Money.FromRupees(11.43m));
        Order(k, VoucherBaseType.PurchaseOrder, item, D2, 40.375m);   // incoming, but not enough

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(30.875m, row.ClosingQuantity);
        Assert.Equal(40.375m, row.PendingPurchaseOrders);
        Assert.Equal(71.250m, row.NettAvailable);   // 30.875 + 40.375 − 0 (the PO is INSIDE availability)
        Assert.Equal(49.375m, row.Shortfall);       // 120.625 − 71.250; pre-10.10 this printed 89.75
        // The MOQ is compared against the SHORTFALL alone. Comparing it against shortfall + pendingPO instead
        // (49.375 + 40.375 = 89.75 > 75.25) would return 49.375 here — the pre-10.10 double-count creeping back
        // into the MOQ branch. No other fixture in this file catches that, because every other MOQ fixture has
        // no pending purchase order.
        Assert.Equal(75.25m, row.OrderToBePlaced);
        // 🔴 Post-WF-8 this row survives on a rule that used to be irrelevant here: the D1 receipt PRE-DATES the
        // D2 order, and goods already on the shelf cannot retire an order raised afterwards. Pinned explicitly
        // rather than left as the incidental reason a raw-quantity assertion still passes — if
        // OrderFulfilment ever dropped its date bound, this fixture's 40.375 would silently become 9.5.
        Assert.Equal(40.375m,
            OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D4));
    }

    /// <summary>
    /// 🔴 BOTH order books live at once, driving Nett Available NEGATIVE — stock committed away past what is
    /// incoming, which is the single case a buyer most needs the report to get right, and the case no other
    /// fixture in this file reaches (every other one has either no purchase order or no sales order, and none
    /// pushes Nett Available below zero).
    /// <para>Level 100.875, closing 20.125, pending PO 8.375, sales orders due 60.625, MOQ 12.5 ⇒ Nett Available
    /// <b>−32.125</b>, Shortfall 133.000, Order to be Placed 133.000. A variant that clamped Nett Available at
    /// zero — <c>Math.Max(closing + pendingPO − soDue, 0m)</c> — passes every other test in this file (measured:
    /// 46/46 green) and would print Shortfall 100.875 and pre-fill the buyer's Ctrl+F9 purchase order 32.125 Nos
    /// short of already-committed customer demand. That is IV-10's own harm re-created inside the fix.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_nett_available_goes_negative_when_sales_orders_exceed_stock_plus_incoming()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 12.5m);
        // 🔴 OPENING BALANCE, not a Receipt Note — see the banner above Reorder_status_… (the date-bound note).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 20.125m, Money.FromRupees(9.37m));
        Order(k, VoucherBaseType.PurchaseOrder, item, D2, 8.375m);
        Order(k, VoucherBaseType.SalesOrder, item, D2, 60.625m);

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(20.125m, row.ClosingQuantity);
        Assert.Equal(8.375m, row.PendingPurchaseOrders);
        Assert.Equal(60.625m, row.SalesOrdersDue);
        Assert.Equal(-32.125m, row.NettAvailable);    // 20.125 + 8.375 − 60.625
        Assert.True(row.NettAvailable < 0m, "Nett Available must be allowed to go negative — never clamped at 0.");
        Assert.Equal(133.000m, row.Shortfall);        // 100.875 − (−32.125)
        Assert.Equal(133.000m, row.OrderToBePlaced);  // shortfall 133.000 exceeds the MOQ 12.5
        // Reconciles from its own columns even below zero.
        Assert.Equal(row.NettAvailable,
            row.ClosingQuantity + row.PendingPurchaseOrders - row.SalesOrdersDue);
    }

    /// <summary>
    /// A purchase order that already covers the requirement leaves nothing to order — and the row is STILL
    /// LISTED, per [CORPUS-BOOK p.164], which shows the covered item on screen with an empty "Order to be
    /// Placed" column. <b>Renamed and re-fixtured</b> from the pre-10.10
    /// <c>…_yields_zero_order_even_with_a_shortfall</c>: once the PO is inside Nett Available there is no
    /// shortfall left to have, so the old name asserted the double-count this slice removes.
    /// </summary>
    [Fact]
    public void Reorder_status_lists_an_item_whose_pending_order_already_covers_the_requirement()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 10.5m);
        // 🔴 OPENING BALANCE, not a Receipt Note — see the banner above Reorder_status_… (the date-bound note).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 20.125m, Money.FromRupees(10.51m));
        Order(k, VoucherBaseType.PurchaseOrder, item, D2, 90.5m);   // incoming covers the 80.75 gap

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);   // listed, not filtered away
        Assert.Equal(110.625m, row.NettAvailable);   // 20.125 + 90.5 − 0
        Assert.Equal(0m, row.Shortfall);             // not short at all; pre-10.10 this printed 80.75
        Assert.Equal(0m, row.OrderToBePlaced);       // MOQ 10.5 must NOT floor a nil shortfall
    }

    [Fact]
    public void Reorder_status_cancelled_and_post_dated_purchase_orders_do_not_net()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100m);
        Receive(k, item, k.MainGodownId, D1, 20m, Money.FromRupees(10m));
        // Cancelled PO — excluded.
        var cancelled = InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder), D2,
            new[] { new OrderLine(item, k.MainGodownId, 50m, null) });
        k.Posting.Post(cancelled);
        k.Posting.Cancel(cancelled.Id);
        // Post-dated PO after the as-of date — excluded.
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder),
            new DateOnly(2024, 5, 1), new[] { new OrderLine(item, k.MainGodownId, 50m, null) }, postDated: true));

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(0m, row.PendingPurchaseOrders);
        Assert.Equal(20m, row.ClosingQuantity);   // pinned, so the NettAvailable assertion below is not a
        Assert.Equal(20m, row.NettAvailable);     // restatement of an unpinned value: neither order counts
        Assert.Equal(80m, row.Shortfall);
        Assert.Equal(80m, row.OrderToBePlaced);   // 100 − 20 − 0
    }

    /// <summary>
    /// A post-dated order whose date has ARRIVED (Date ≤ asOf) <b>counts</b>. This is the arm the
    /// cancelled/post-dated test above cannot reach: its post-dated order is dated after asOf, so the plain
    /// <c>v.Date &gt; asOf</c> bound already excludes it and the <c>PostDated</c> flag never decides anything
    /// there. Post-10.10 the flag moves real money — both order books feed Nett Available, which feeds the
    /// Ctrl+F9 purchase-order prefill — so the decided behaviour is pinned explicitly rather than left implied.
    /// <para>Decided behaviour and its grounding: identical to
    /// <see cref="Apex.Ledger.Reports.InventoryRegisters.BuildOrders"/> (<c>if (v.PostDated &amp;&amp; v.Date &gt; to)
    /// continue;</c>) and to <see cref="Apex.Ledger.Reports.LedgerBalance"/>'s <c>CountsAsOf</c> — a post-dated
    /// voucher is provisional only UNTIL its date arrives, then it is an ordinary voucher. Reorder Status must
    /// agree with the Order Register or the two reports disagree on the same order book (ER-4). If that is ever
    /// changed, it is a behaviour change needing its own register row — not a quiet edit here.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_counts_a_post_dated_order_once_its_date_has_arrived()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m);
        // 🔴 OPENING BALANCE, not a Receipt Note — see the banner above Reorder_status_… (the date-bound note).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 20.125m, Money.FromRupees(10.79m));
        // Post-dated, but dated D2 which is ON OR BEFORE the as-of date D4 — its date has arrived.
        k.Posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.PurchaseOrder),
            D2, new[] { new OrderLine(item, k.MainGodownId, 30.375m, null) }, postDated: true));

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(30.375m, row.PendingPurchaseOrders);   // counted — the date has arrived
        Assert.Equal(20.125m, row.ClosingQuantity);
        Assert.Equal(50.500m, row.NettAvailable);           // 20.125 + 30.375 − 0
        Assert.Equal(50.375m, row.Shortfall);               // 100.875 − 50.500
        Assert.Equal(50.375m, row.OrderToBePlaced);
        // ER-4: the Order Register must see the very same order over the same window.
        var register = InventoryRegisters.BuildOrders(k.Company, FyStart, D4);
        Assert.Equal(30.375m, Assert.Single(register).OrderedQuantity);
    }

    /// <summary>
    /// Sales Orders Due IS netted into Nett Available (IV-10 / WF-7). <b>Inverts</b> the pre-10.10
    /// <c>Reorder_status_sales_orders_due_is_shown_but_not_netted</c>, whose name carried the invented "DD-4"
    /// rule. TallyHelp, Reorder Status: Nett Available "is basically derived from adding the pending purchase
    /// order to the closing stock and minusing the sales order".
    /// </summary>
    [Fact]
    public void Reorder_status_nets_sales_orders_due_into_nett_available()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m);
        Receive(k, item, k.MainGodownId, D1, 20.125m, Money.FromRupees(10.53m));
        Order(k, VoucherBaseType.SalesOrder, item, D2, 15.375m);

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(15.375m, row.SalesOrdersDue);
        Assert.Equal(0m, row.PendingPurchaseOrders);
        Assert.Equal(4.750m, row.NettAvailable);     // 20.125 + 0 − 15.375
        Assert.Equal(96.125m, row.Shortfall);        // 100.875 − 4.750; pre-10.10 this printed 80.75
        Assert.Equal(96.125m, row.OrderToBePlaced);  // no MOQ ⇒ the shortfall itself
    }

    /// <summary>
    /// 🔴 THE HEADLINE HARM of IV-10, reproduced. Reorder level 100.75, closing 120.375, no pending purchase
    /// order, 60.125 committed on open sales orders, MOQ 25.5. TallyPrime shows Nett Available 60.250,
    /// Shortfall 40.50 and Order to be Placed 40.50 so the buyer raises a purchase order. Pre-10.10 the engine
    /// dropped the item from the report entirely (120.375 &gt; 100.75), nothing was ordered, and 40.50 Nos of
    /// already-committed customer demand went unfilled. Every quantity is odd and fractional and the inward
    /// rate carries odd paise, so no round figure can make an assertion vacuous.
    /// </summary>
    [Fact]
    public void Reorder_status_nets_sales_orders_due_and_lists_an_item_whose_closing_exceeds_its_level()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.75m, minOrder: 25.5m);
        Receive(k, item, k.MainGodownId, D1, 120.375m, Money.FromRupees(10.37m));   // ABOVE the level
        Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m);                    // but committed away

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(120.375m, row.ClosingQuantity);
        Assert.Equal(0m, row.PendingPurchaseOrders);
        Assert.Equal(60.125m, row.SalesOrdersDue);
        Assert.Equal(60.250m, row.NettAvailable);
        Assert.Equal(40.50m, row.Shortfall);
        Assert.Equal(40.50m, row.OrderToBePlaced);   // shortfall 40.50 exceeds the MOQ 25.5
        // 🔴 Post-WF-8 the netted figure is the OUTSTANDING quantity, not the raw ordered one. Here they
        // coincide because nothing has shipped — pinned explicitly so this fixture cannot be read as evidence
        // that the raw quantity is what feeds the column. Its pair,
        // Reorder_status_orders_nothing_once_the_sales_order_it_netted_has_been_delivered, is the same book
        // after delivery and is the fixture that separates the two.
        Assert.Equal(60.125m,
            OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>
    /// The Shortfall column is measured against Nett Available, not closing stock — TallyHelp: "Only when the
    /// quantity in Re-order Level column is more than the Nett Available column, the difference appears as
    /// Shortfall." The fixture is chosen so Order to be Placed is the MOQ 25.5 under BOTH the old and the new
    /// rule, which isolates the Shortfall column: a round-number fixture would have let the order quantity
    /// mask the shortfall defect.
    /// </summary>
    [Fact]
    public void Reorder_status_shortfall_is_measured_against_nett_available_not_closing_stock()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 25.5m);
        Receive(k, item, k.MainGodownId, D1, 95.625m, Money.FromRupees(9.83m));
        Order(k, VoucherBaseType.SalesOrder, item, D2, 3.125m);

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(92.500m, row.NettAvailable);    // 95.625 + 0 − 3.125
        Assert.Equal(8.375m, row.Shortfall);         // 100.875 − 92.500; pre-10.10 printed 5.25
        Assert.Equal(25.5m, row.OrderToBePlaced);    // MOQ floors a REAL shortfall — unchanged either way
    }

    /// <summary>
    /// A pending purchase order is counted ONCE, inside Nett Available, and never subtracted a second time off
    /// the order quantity. Pre-10.10 the row printed Shortfall 80.75 beside Order to be Placed 50.375, two
    /// figures no operator could reconcile from any pair of columns on the row. Order to be Placed is
    /// value-preserving across the fix, which is precisely what makes dropping the separate subtraction safe.
    /// </summary>
    [Fact]
    public void Reorder_status_does_not_double_count_a_pending_purchase_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 10.5m);
        // 🔴 OPENING BALANCE, not a Receipt Note — see the banner above Reorder_status_… (the date-bound note).
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 20.125m, Money.FromRupees(11.09m));
        Order(k, VoucherBaseType.PurchaseOrder, item, D2, 30.375m);

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(30.375m, row.PendingPurchaseOrders);
        Assert.Equal(50.500m, row.NettAvailable);    // 20.125 + 30.375 − 0
        Assert.Equal(50.375m, row.Shortfall);        // 100.875 − 50.500; pre-10.10 printed 80.75
        Assert.Equal(50.375m, row.OrderToBePlaced);  // max(50.375, MOQ 10.5)
        // The row reconciles from its own columns, which is the whole point of the Nett Available column.
        Assert.Equal(row.NettAvailable,
            row.ClosingQuantity + row.PendingPurchaseOrders - row.SalesOrdersDue);
        Assert.Equal(row.Shortfall, row.ReorderLevel - row.NettAvailable);
    }

    /// <summary>
    /// PR-8 exit gate (Tally-Book pp.159–161): Reorder Level 20 (Simple), Minimum Order Quantity 25 (Simple);
    /// stock sold below 20 with NO pending purchase order ⇒ Order to be Placed = 25 (the MOQ floor), Shortfall =
    /// 20 − closing.
    /// </summary>
    [Fact]
    public void Reorder_status_order_to_be_placed_matches_book_example()
    {
        var k = NewKit();
        var item = Item(k, "Nike T-shirt", reorderLevel: 20m, minOrder: 25m);
        Receive(k, item, k.MainGodownId, D1, 30m, Money.FromRupees(10m));
        Deliver(k, item, k.MainGodownId, D2, 22m);   // closing 8 (below 20), no pending PO

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(8m, row.ClosingQuantity);
        Assert.Equal(0m, row.PendingPurchaseOrders);
        Assert.Equal(8m, row.NettAvailable);     // no orders either way ⇒ nett == closing
        Assert.Equal(12m, row.Shortfall);        // 20 − 8
        Assert.Equal(25m, row.OrderToBePlaced);  // max(netRequirement 12, MOQ 25) = 25 — SURVIVES 10.10
    }

    /// <summary>
    /// 🔴 <b>HARD GATE PR-8 / ER-13 — "the MOQ floor fires even at zero shortfall" — is RETIRED BY USER
    /// DECISION</b> (Phase 10.10, WF-7, R12). This test is the deliberate INVERSION of the pre-10.10
    /// <c>Reorder_status_at_exactly_the_level_with_min_order_qty_orders_the_min_order_qty</c>, which a slice-6
    /// review had added as a regression lock. <b>It is not a regression — do not "restore" it.</b>
    /// <para>Citation for the reversal: <b>Tally-Prime-Book p.164</b> — an item whose Minimum Order Quantity is
    /// 25 (p.162), once its requirement is already covered, prints "You can see 'Order to be Placed' Column is
    /// Empty, Because We Have ordered already". <i>Empty</i>, not 25. TallyHelp's Reorder Status page states the
    /// MOQ branch only for a REAL shortfall: "When the Shortfall is less than the Min Order Quantity, the
    /// quantity displayed in Min Order Quantity appears under Order to be Placed." With no shortfall there is no
    /// branch to take, so the guard <c>shortfall &lt;= 0 ⇒ 0</c> is load-bearing.</para>
    /// <para>The doc amendments this decision owes — <c>docs/phase6-advanced-inventory-requirements.md:598-601</c>
    /// (gate PR-8) and the <c>memory.md</c> restatement — are DEFERRED to the post-merge documentation slice.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_places_no_order_when_the_shortfall_is_nil_even_with_a_min_order_quantity()
    {
        var k = NewKit();
        var item = Item(k, "Nike T-shirt", reorderLevel: 100.875m, minOrder: 25.5m);
        Receive(k, item, k.MainGodownId, D1, 100.875m, Money.FromRupees(12.61m));   // exactly at the level, no PO

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);   // still LISTED
        Assert.Equal(100.875m, row.ClosingQuantity);
        Assert.Equal(100.875m, row.ReorderLevel);
        Assert.Equal(100.875m, row.NettAvailable);
        Assert.Equal(0m, row.Shortfall);            // at the level, not below it
        Assert.Equal(0m, row.PendingPurchaseOrders);
        // PR-8 retired (user decision, Tally-Prime-Book p.164): a nil shortfall orders NOTHING, not the MOQ.
        Assert.Equal(0m, row.OrderToBePlaced);
    }

    /// <summary>
    /// 🔴 <b>DD-5 CLOSED — both order books are now netted at their OUTSTANDING quantity, so a fulfilled order
    /// is retired.</b> This is the deliberate INVERSION of the pre-S2
    /// <c>Reorder_status_still_counts_fully_fulfilled_orders_as_outstanding_DD5_documented_defect</c>, which
    /// asserted the wrong figures on purpose so the defect could not ship unnoticed. <b>The figures below are the
    /// ones that test documented as "should be" — they are not re-baselined to whatever the engine prints.</b>
    /// <para><b>Mechanism of the fix (WF-8 → WF-7).</b> WF-8 landed <see cref="OrderFulfilment"/>, which matches
    /// an order to the movements that fulfil it; S2 rewires <see cref="ReorderStatus"/> onto
    /// <see cref="OrderFulfilment.OutstandingByItem"/> so "Purc Orders Pending" and "Sales Orders Due" mean what
    /// TallyPrime means by them — what is still OUTSTANDING, not everything ever ordered.</para>
    /// <para><b>"Sold" — the ordinary order-to-delivery cycle, which is where the harm landed.</b> 180.875
    /// received, a 60.125 sales order raised, then fully delivered. 120.750 Nos remain and nothing is
    /// outstanding, so Sales Orders Due 0, Nett Available 120.750, Shortfall 0, Order to be Placed 0 — the item
    /// is comfortably above its level of 100.875. Pre-fix the engine printed Sales Orders Due 60.125, Nett
    /// Available 60.625, Shortfall 40.250 and pre-filled a purchase order for the MOQ 55.5 that was not needed,
    /// for ever, growing with every delivered order.</para>
    /// <para><b>"Bought" — the mirror, which erred the other way.</b> A 40.375 purchase order raised and fully
    /// received: the goods are in closing stock, so nothing is still incoming. Nett Available 40.375, Shortfall
    /// 60.500, Order to be Placed 60.500. Pre-fix the engine counted the goods twice (80.750 / 20.125) and
    /// UNDER-ordered by 40.375.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_retires_a_fulfilled_order_in_both_directions()
    {
        var k = NewKit();
        var sold = Item(k, "Sold", reorderLevel: 100.875m, minOrder: 55.5m);
        Receive(k, sold, k.MainGodownId, D1, 180.875m, Money.FromRupees(10.37m));
        Order(k, VoucherBaseType.SalesOrder, sold, D2, 60.125m);
        Deliver(k, sold, k.MainGodownId, D3, 60.125m);              // the order is FULLY delivered

        var bought = Item(k, "Bought", reorderLevel: 100.875m);
        Order(k, VoucherBaseType.PurchaseOrder, bought, D2, 40.375m);
        Receive(k, bought, k.MainGodownId, D3, 40.375m, Money.FromRupees(8.91m));   // FULLY received

        var rows = ReorderStatus.Build(k.Company, D4).Rows;

        var s = rows.Single(r => r.ItemName == "Sold");
        Assert.Equal(120.750m, s.ClosingQuantity);     // the delivery reduced stock
        Assert.Equal(0m, s.SalesOrdersDue);            // delivered in full ⇒ nothing due (pre-fix: 60.125)
        Assert.Equal(120.750m, s.NettAvailable);       // pre-fix: 60.625
        Assert.Equal(0m, s.Shortfall);                 // 120.750 > level 100.875 (pre-fix: 40.250)
        Assert.Equal(0m, s.OrderToBePlaced);           // pre-fix: 55.5 — a real PO the buyer did not need
        // The row is STILL LISTED even though it needs nothing — [CORPUS-BOOK p.164].
        Assert.Contains(rows, r => r.ItemName == "Sold");

        var b = rows.Single(r => r.ItemName == "Bought");
        Assert.Equal(40.375m, b.ClosingQuantity);      // the receipt raised stock
        Assert.Equal(0m, b.PendingPurchaseOrders);     // fully received ⇒ nothing incoming (pre-fix: 40.375)
        Assert.Equal(40.375m, b.NettAvailable);        // pre-fix: 80.750 — the goods were counted twice
        Assert.Equal(60.500m, b.Shortfall);            // 100.875 − 40.375 (pre-fix: 20.125)
        Assert.Equal(60.500m, b.OrderToBePlaced);      // pre-fix: 20.125 — under-ordered by 40.375

        // Each row still reconciles from its own printed columns.
        Assert.Equal(s.NettAvailable, s.ClosingQuantity + s.PendingPurchaseOrders - s.SalesOrdersDue);
        Assert.Equal(b.NettAvailable, b.ClosingQuantity + b.PendingPurchaseOrders - b.SalesOrdersDue);
    }

    /// <summary>
    /// 🔴 <b>THE PAIR TO <see cref="Reorder_status_nets_sales_orders_due_and_lists_an_item_whose_closing_exceeds_its_level"/>
    /// — the same worked case, after the goods have shipped.</b> The two fixtures are built so that the book
    /// here has EXACTLY the closing quantity (120.375), reorder level (100.75) and MOQ (25.5) of the open-order
    /// case, and differ only by the delivery. Reading the RAW order book — the pre-WF-8 behaviour — both books
    /// therefore print the identical row: Nett Available 60.250, Shortfall 40.50, Order to be Placed 40.50. One
    /// of those two answers is right and the other tells the buyer to re-order stock that has already shipped,
    /// <b>for ever</b>. Only the outstanding quantity can tell them apart, which is what WF-8 exists for.
    /// <para>Correct here: the order is delivered, so nothing is due, Nett Available is the whole 120.375, there
    /// is no shortfall and — this is the load-bearing half — <b>no purchase order is suggested at all</b>.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_orders_nothing_once_the_sales_order_it_netted_has_been_delivered()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.75m, minOrder: 25.5m);
        // 180.500 is forced, not chosen: it is the receipt that leaves closing at the open-order case's 120.375
        // once 60.125 has shipped, which is what makes the two fixtures differ by the delivery alone.
        Receive(k, item, k.MainGodownId, D1, 180.500m, Money.FromRupees(10.37m));
        Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m);
        Deliver(k, item, k.MainGodownId, D3, 60.125m);              // shipped — the order is retired

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(120.375m, row.ClosingQuantity);   // identical to the open-order case's closing
        Assert.Equal(0m, row.PendingPurchaseOrders);
        Assert.Equal(0m, row.SalesOrdersDue);          // raw would be 60.125
        Assert.Equal(120.375m, row.NettAvailable);     // raw would be 60.250
        Assert.Equal(0m, row.Shortfall);               // raw would be 40.50
        Assert.Equal(0m, row.OrderToBePlaced);         // raw would be 40.50 — ordered again every single day
    }

    /// <summary>
    /// A PARTLY delivered sales order nets only its undelivered remainder. Neither the raw ordered quantity
    /// (60.125) nor zero is right — the fixture is chosen so all three answers differ in every column, which a
    /// fully-delivered fixture alone cannot prove: closing 130.250, still due 39.750, Nett Available 90.500,
    /// Shortfall 10.375, and the MOQ 12.5 floors the order. Reading the raw book instead would print Nett
    /// Available 70.125 / Shortfall 30.750 / Order 30.750.
    /// </summary>
    [Fact]
    public void Reorder_status_nets_only_the_undelivered_remainder_of_a_partly_delivered_sales_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 12.5m);
        Receive(k, item, k.MainGodownId, D1, 150.625m, Money.FromRupees(9.83m));
        Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m);
        Deliver(k, item, k.MainGodownId, D3, 20.375m);              // part shipment

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(130.250m, row.ClosingQuantity);   // 150.625 − 20.375
        Assert.Equal(39.750m, row.SalesOrdersDue);     // 60.125 ordered − 20.375 shipped
        Assert.Equal(90.500m, row.NettAvailable);      // 130.250 + 0 − 39.750
        Assert.Equal(10.375m, row.Shortfall);          // 100.875 − 90.500
        Assert.Equal(12.5m, row.OrderToBePlaced);      // MOQ 12.5 floors the 10.375 shortfall
        Assert.Equal(row.NettAvailable, row.ClosingQuantity + row.PendingPurchaseOrders - row.SalesOrdersDue);
    }

    /// <summary>
    /// The mirror: a PARTLY received purchase order counts only its unreceived remainder as incoming. The
    /// opening balance (12.750, which is not an inventory voucher and so can never fulfil an order) exists to
    /// stop closing + outstanding collapsing onto the ordered quantity, which would have let two different
    /// columns share one figure. Reading the raw book instead would print Nett Available 68.250 / Shortfall
    /// 32.625 / Order 32.625, double-counting the 15.125 already on the shelf.
    /// </summary>
    [Fact]
    public void Reorder_status_nets_only_the_unreceived_remainder_of_a_partly_received_purchase_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget", reorderLevel: 100.875m, minOrder: 10.5m);
        k.Masters.AddOpeningBalance(item, k.MainGodownId, 12.750m, Money.FromRupees(7.31m));
        Order(k, VoucherBaseType.PurchaseOrder, item, D1, 40.375m);
        Receive(k, item, k.MainGodownId, D2, 15.125m, Money.FromRupees(11.09m));   // part receipt

        var row = Assert.Single(ReorderStatus.Build(k.Company, D4).Rows);
        Assert.Equal(27.875m, row.ClosingQuantity);        // 12.750 opening + 15.125 received
        Assert.Equal(25.250m, row.PendingPurchaseOrders);  // 40.375 ordered − 15.125 received
        Assert.Equal(0m, row.SalesOrdersDue);
        Assert.Equal(53.125m, row.NettAvailable);          // 27.875 + 25.250 − 0
        Assert.Equal(47.750m, row.Shortfall);              // 100.875 − 53.125
        Assert.Equal(47.750m, row.OrderToBePlaced);        // exceeds the MOQ 10.5
        Assert.Equal(row.NettAvailable, row.ClosingQuantity + row.PendingPurchaseOrders - row.SalesOrdersDue);
    }

    /// <summary>
    /// 🔴 <b>ER-4 — the Order Register and Reorder Status read the SAME order book and now agree on it.</b> This
    /// is the deliberate inversion of the pre-S2 tripwire
    /// <c>Order_register_and_reorder_status_disagree_on_a_delivered_order_until_WF7_rewires_it</c>, which existed
    /// only to keep a stated, temporary divergence visible while WF-8 shipped ahead of WF-7's rewiring. Both
    /// projections now derive their figure from <see cref="OrderFulfilment"/>, so the agreement is structural
    /// rather than coincidental — and this test is the only fixture that reads both off one book, which is what
    /// would catch either one being re-pointed at the raw ordered quantity again.
    /// </summary>
    [Fact]
    public void Order_register_and_reorder_status_agree_on_a_partly_delivered_order()
    {
        var k = NewKit();
        var sold = Item(k, "Sold", reorderLevel: 100.875m, minOrder: 55.5m);
        Receive(k, sold, k.MainGodownId, D1, 180.875m, Money.FromRupees(10.37m));
        Order(k, VoucherBaseType.SalesOrder, sold, D2, 60.125m);
        Deliver(k, sold, k.MainGodownId, D3, 40.625m);              // PARTLY delivered — 19.500 still due

        // The Order Register: the order is retired down to its remainder.
        var registerOutstanding = InventoryRegisters.BuildOrders(k.Company, D1, D4)
            .Where(r => r.StockItemId == sold)
            .Sum(r => r.OutstandingQuantity);
        Assert.Equal(19.500m, registerOutstanding);
        Assert.Equal(19.500m, OrderFulfilment.OutstandingForItem(k.Company, sold, VoucherBaseType.SalesOrder, D4));

        // Reorder Status reads the same book to the same figure.
        var row = ReorderStatus.Build(k.Company, D4).Rows.Single(r => r.ItemName == "Sold");
        Assert.Equal(19.500m, row.SalesOrdersDue);
        Assert.Equal(0m, row.SalesOrdersDue - registerOutstanding);
        // Partly delivered, so the figure is neither the raw 60.125 nor 0 — the divergence cannot be masked by
        // an all-or-nothing fixture in which "retired" and "agreeing" happen to coincide at zero.
        Assert.NotEqual(60.125m, row.SalesOrdersDue);
        Assert.NotEqual(0m, row.SalesOrdersDue);
    }

    /// <summary>
    /// Consumption regression (slice-6 review): a pure inter-godown Stock-Journal transfer of an item (source
    /// Outward at one godown + destination Inward at another, same item) is <b>not</b> an issue and must not
    /// inflate the Advanced-reorder consumption — its outward leg nets against its same-voucher inward leg. Only
    /// the genuine delivery counts. The pre-fix code counted the transfer's outward leg, over-stating consumption.
    /// </summary>
    [Fact]
    public void Consumption_excludes_inter_godown_stock_journal_transfers()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Receive(k, item, k.MainGodownId, D1, 100m, Money.FromRupees(10m));
        // Pure inter-godown transfer of the SAME item — moves stock, does not consume it.
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D2,
            source: new[] { new InventoryAllocation(item, k.MainGodownId, 40m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item, k.SecondGodownId, 40m, StockDirection.Inward) }));
        Deliver(k, item, k.MainGodownId, D3, 15m);   // a genuine issue of 15

        var consumption = new InventoryLedger(k.Company).Consumption(item, FyStart, D4);
        Assert.Equal(15m, consumption);   // only the delivery; the 40-unit transfer nets to zero
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
