using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 5.7 (schema v69) — an Optional stock/order voucher, walk by walk.</b> Wave-10 review (track gd).
///
/// <para><b>Why this file exists.</b> IV-311 states that every stock-quantity, order, bills-pending and
/// negative-stock walk skips an Optional pure-stock voucher, and the branch added <c>|| v.Optional</c> to eleven
/// such walks. Only two of them (<c>InventoryLedger.Counts</c> and <c>StockValuationService.Counts</c>) were pinned:
/// removing the skip from any of the others left the whole gate green, so the claim was a dead guard in all but
/// name. Each assertion below reads the REPORT the walk feeds, first with the vouchers Optional and then after
/// <c>InventoryPostingService.SetOptional(.., false)</c> regularises them, so neither half can pass vacuously.</para>
///
/// <para>Vendor (R7, ruling 14), help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/, opened by
/// content 2026-11-02: <i>"Any voucher in TallyPrime can be marked as Optional"</i> and <i>"Marking voucher as
/// Optional will remove the accounting impact of the transactions from the books of account."</i> The stock-side
/// reading is ours (IV-311).</para>
/// </summary>
public class OptionalStockVoucherWalkTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    private sealed record Book(
        Company Company, InventoryPostingService Posting, Guid ItemId, Guid GodownId,
        Guid ReceiptId, Guid OrderId, Guid DeliveryId);

    /// <summary>Opening 10 @ 10.00; an Optional Receipt Note of 5 (tracking no. TRK-OPT-1), an Optional Purchase
    /// Order of 3 placed after it, and an Optional Delivery Note of 30 that would drive the item to -15.</summary>
    private static Book Seed()
    {
        var c = CompanyFactory.CreateSeeded("Optional Walks Co", FyStart);
        c.UseTrackingNumbers = true;
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Walk Bolt", masters.CreateStockGroup("Walk Goods").Id,
            masters.CreateSimpleUnit("WNos", "Walk Numbers").Id);
        var godown = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, godown, 10m, Money.FromRupees(10.00m));

        var supplier = new Domain.Ledger(Guid.NewGuid(), "Walk Supplier", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false);
        c.AddLedger(supplier);

        VoucherType T(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b);
        var posting = new InventoryPostingService(c);

        var receipt = new InventoryVoucher(Guid.NewGuid(), T(VoucherBaseType.ReceiptNote).Id, FyStart.AddDays(4),
            new[]
            {
                new InventoryAllocation(item.Id, godown, 5m, StockDirection.Inward, Money.FromRupees(20.00m),
                    trackingNumber: "TRK-OPT-1"),
            },
            partyId: supplier.Id) { Optional = true };
        posting.Post(receipt);

        var order = InventoryVoucher.Order(Guid.NewGuid(), T(VoucherBaseType.PurchaseOrder).Id, FyStart.AddDays(9),
            new[] { new OrderLine(item.Id, godown, 3m, Money.FromRupees(11.00m)) }, partyId: supplier.Id);
        order.Optional = true;
        posting.Post(order);

        var delivery = new InventoryVoucher(Guid.NewGuid(), T(VoucherBaseType.DeliveryNote).Id, FyStart.AddDays(19),
            new[] { new InventoryAllocation(item.Id, godown, 30m, StockDirection.Outward, Money.FromRupees(15.00m)) })
        { Optional = true };
        posting.Post(delivery);

        return new Book(c, posting, item.Id, godown, receipt.Id, order.Id, delivery.Id);
    }

    private static void Regularise(Book b)
    {
        b.Posting.SetOptional(b.ReceiptId, false);
        b.Posting.SetOptional(b.OrderId, false);
        b.Posting.SetOptional(b.DeliveryId, false);
    }

    /// <summary><b>Mutation:</b> removing <c>|| iv.Optional</c> from <c>NegativeStock.Build</c>'s rate walk alone
    /// does not move the row set (that walk only prices it), so this pins the shared on-hand rule as it reaches the
    /// exception report; the row must appear the moment the Delivery Note is regularised.</summary>
    [Fact]
    public void An_optional_delivery_note_drives_no_item_negative_until_it_is_regularised()
    {
        var b = Seed();
        Assert.Empty(NegativeStock.Build(b.Company, FyEnd).Rows);

        Regularise(b);

        var row = Assert.Single(NegativeStock.Build(b.Company, FyEnd).Rows);
        Assert.Equal(-15m, row.Quantity);
    }

    /// <summary><b>Mutation:</b> removing <c>|| v.Optional</c> from <c>InventoryRegisters.BuildOrders</c> reddens the
    /// first assertion; from the movement-register walk (<c>InventoryRegisters</c> :177), the second.</summary>
    [Fact]
    public void An_optional_order_and_an_optional_receipt_note_are_off_the_order_and_receipt_registers()
    {
        var b = Seed();
        Assert.DoesNotContain(InventoryRegisters.BuildOrders(b.Company, FyStart, FyEnd),
            r => r.StockItemId == b.ItemId);
        Assert.DoesNotContain(InventoryRegisters.BuildReceiptNotes(b.Company, FyStart, FyEnd),
            r => r.StockItemId == b.ItemId);

        Regularise(b);

        Assert.Contains(InventoryRegisters.BuildOrders(b.Company, FyStart, FyEnd), r => r.StockItemId == b.ItemId);
        Assert.Contains(InventoryRegisters.BuildReceiptNotes(b.Company, FyStart, FyEnd),
            r => r.StockItemId == b.ItemId);
    }

    /// <summary><b>Mutation:</b> removing <c>|| v.Optional</c> from <c>OrderFulfilment.CountsAsOf</c> reddens it.</summary>
    [Fact]
    public void An_optional_purchase_order_leaves_nothing_outstanding_until_it_is_regularised()
    {
        var b = Seed();
        var before = OrderFulfilment.OutstandingByItem(b.Company, VoucherBaseType.PurchaseOrder, FyEnd);
        Assert.Equal(0m, before.TryGetValue(b.ItemId, out var q0) ? q0 : 0m);

        Regularise(b);

        // The receipt (day 4) predates the order (day 9), so it cannot retire it: all 3 stay outstanding.
        var after = OrderFulfilment.OutstandingByItem(b.Company, VoucherBaseType.PurchaseOrder, FyEnd);
        Assert.Equal(3m, after[b.ItemId]);
    }

    /// <summary><b>Mutation:</b> removing <c>|| v.Optional</c> from the goods side of <c>BillsPending.Build</c>
    /// reddens the first assertion.</summary>
    [Fact]
    public void An_optional_receipt_note_is_not_a_purchase_bill_pending_until_it_is_regularised()
    {
        var b = Seed();
        Assert.DoesNotContain(BillsPending.BuildPurchase(b.Company, FyEnd), r => r.TrackingNumber == "TRK-OPT-1");

        Regularise(b);

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, FyEnd), r => r.TrackingNumber == "TRK-OPT-1");
        Assert.Equal(5m, row.PendingQuantity);
    }

    /// <summary><b>Mutation:</b> removing <c>|| v.Optional</c> from <c>BatchStockService</c>'s on-hand walk reddens
    /// the first assertion.</summary>
    [Fact]
    public void The_batch_on_hand_ignores_optional_stock_vouchers_until_they_are_regularised()
    {
        var b = Seed();
        var batches = new BatchStockService(b.Company);
        Assert.Equal(10m, batches.BatchOnHands(b.ItemId, FyEnd).Sum(x => x.Quantity));

        Regularise(b);

        Assert.Equal(-15m, new BatchStockService(b.Company).BatchOnHands(b.ItemId, FyEnd).Sum(x => x.Quantity));
    }

    /// <summary><b>Mutation:</b> removing <c>|| v.Optional</c> from <c>InventoryLedger.Consumption</c>'s walk
    /// reddens the first assertion.</summary>
    [Fact]
    public void An_optional_delivery_note_is_not_consumption_until_it_is_regularised()
    {
        var b = Seed();
        var ledger = new InventoryLedger(b.Company);
        Assert.Equal(0m, ledger.Consumption(b.ItemId, FyStart.AddDays(-1), FyEnd));

        Regularise(b);

        Assert.Equal(30m, new InventoryLedger(b.Company).Consumption(b.ItemId, FyStart.AddDays(-1), FyEnd));
    }

    // ═══════════════════════════════════════════════ continuing reviewer (wave 10, track gd)
    //
    // The first reviewer's six facts above left SEVEN of the sixteen added skips dead: dropping `|| v.Optional`
    // from InventoryMovements.CountsPureStock, ItemCostAnalysis's pure-stock walk, the three JobWorkReports walks,
    // NegativeStock.ReferenceUnitCost and BatchStockService's two batch walks (LastRatedInwardRate,
    // FirstInwardDate) left Apex.Ledger.Tests at 3215/3215. Each fact below reads the report one of those walks
    // feeds and reddens when its skip is removed. (InventoryPostingService.DetectNegativeStockAnywhere's skip only
    // narrows the key/date candidate set that InventoryLedger.Counts then evaluates, so dropping it changes no
    // output — an equivalent mutant, recorded rather than pinned.)

    private static Guid TypeOf(Company c, VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

    private static InventoryVoucher Note(Company c, VoucherBaseType b, DateOnly on, InventoryAllocation a,
        bool optional) => new(Guid.NewGuid(), TypeOf(c, b), on, new[] { a }) { Optional = optional };

    /// <summary>Batch "A": an Optional receipt on day 2 (4 @ 7.00) and a live one on day 10 (6 @ 10.00). Batch "B":
    /// a live receipt on day 5 (3 @ 12.00) and an Optional one on day 15 (2 @ 99.00).
    /// <para><b>Mutations:</b> <c>InventoryMovements.CountsPureStock</c> without the skip makes A's inward 10 and
    /// B's 5; <c>BatchStockService.LastRatedInwardRate</c> without it prices B at 99.00; <c>FirstInwardDate</c>
    /// without it dates A's first lot to day 2 and so lists A before B.</para></summary>
    [Fact]
    public void An_optional_receipt_is_no_batch_inward_no_batch_cost_and_no_batch_age_until_it_is_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Batch Co", FyStart);
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Lot Bolt", masters.CreateStockGroup("Lot Goods").Id,
            masters.CreateSimpleUnit("LNos", "Lot Numbers").Id);
        item.MaintainInBatches = true;
        var godown = c.MainLocation!.Id;
        var posting = new InventoryPostingService(c);
        InventoryAllocation In(decimal q, decimal r, string batch) =>
            new(item.Id, godown, q, StockDirection.Inward, Money.FromRupees(r), batch);

        var optA = posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(1), In(4m, 7.00m, "A"), true));
        posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(9), In(6m, 10.00m, "A"), false));
        posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(4), In(3m, 12.00m, "B"), false));
        var optB = posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(14), In(2m, 99.00m, "B"), true));

        var rows = BatchwiseReport.Build(c, FyEnd, FyStart, item.Id).Rows;
        Assert.Equal(new[] { "B", "A" }, rows.Select(r => r.Batch).ToArray());
        Assert.Equal(6m, rows.Single(r => r.Batch == "A").InwardQuantity);
        Assert.Equal(3m, rows.Single(r => r.Batch == "B").InwardQuantity);
        Assert.Equal(Money.FromRupees(12.00m), rows.Single(r => r.Batch == "B").UnitCost);

        posting.SetOptional(optA.Id, false);
        posting.SetOptional(optB.Id, false);

        var after = BatchwiseReport.Build(c, FyEnd, FyStart, item.Id).Rows;
        Assert.Equal(new[] { "A", "B" }, after.Select(r => r.Batch).ToArray());
        Assert.Equal(10m, after.Single(r => r.Batch == "A").InwardQuantity);
        Assert.Equal(Money.FromRupees(99.00m), after.Single(r => r.Batch == "B").UnitCost);
    }

    /// <summary>An item driven to -3 by a live Delivery Note, with an Optional Receipt Note of 1 @ 77.00 dated after
    /// the opening 5 @ 10.00. The shortfall is valued at the opening rate (-30.00), not the Optional note's (-231.00).
    /// <b>Mutation:</b> removing the skip from <c>NegativeStock.ReferenceUnitCost</c> reddens the first value.</summary>
    [Fact]
    public void An_optional_receipt_does_not_price_a_negative_stock_shortfall_until_it_is_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Shortfall Co", FyStart);
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Short Bolt", masters.CreateStockGroup("Short Goods").Id,
            masters.CreateSimpleUnit("SNos", "Short Numbers").Id);
        var godown = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, godown, 5m, Money.FromRupees(10.00m));
        var posting = new InventoryPostingService(c);
        posting.Post(Note(c, VoucherBaseType.DeliveryNote, FyStart.AddDays(2),
            new InventoryAllocation(item.Id, godown, 8m, StockDirection.Outward, Money.FromRupees(15.00m)), false));
        var opt = posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(5),
            new InventoryAllocation(item.Id, godown, 1m, StockDirection.Inward, Money.FromRupees(77.00m)), true));

        var row = Assert.Single(NegativeStock.Build(c, FyEnd).Rows);
        Assert.Equal(-3m, row.Quantity);
        Assert.Equal(Money.FromRupees(-30.00m), row.Value);

        posting.SetOptional(opt.Id, false);

        var after = Assert.Single(NegativeStock.Build(c, FyEnd).Rows);
        Assert.Equal(-2m, after.Quantity);
        Assert.Equal(Money.FromRupees(-154.00m), after.Value);
    }

    /// <summary>A live Receipt Note of 10 @ 10.00 and an Optional Delivery Note of 4 @ 15.00 on one cost-tracking
    /// lot. <b>Mutation:</b> removing the skip from <c>ItemCostAnalysis</c>'s pure-stock walk books the Optional
    /// note's 4 / 60.00 as outward quantity and revenue.</summary>
    [Fact]
    public void An_optional_delivery_note_is_no_cost_tracking_revenue_until_it_is_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Cost Lot Co", FyStart);
        c.EnableCostTracking = true;
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Lot Widget", masters.CreateStockGroup("Lot Widgets").Id,
            masters.CreateSimpleUnit("WLNos", "Widget Numbers").Id);
        var godown = c.MainLocation!.Id;
        var posting = new InventoryPostingService(c);
        posting.Post(Note(c, VoucherBaseType.ReceiptNote, FyStart.AddDays(2),
            new InventoryAllocation(item.Id, godown, 10m, StockDirection.Inward, Money.FromRupees(10.00m),
                costTrackingNumber: "LOT/OPT"), false));
        var opt = posting.Post(Note(c, VoucherBaseType.DeliveryNote, FyStart.AddDays(6),
            new InventoryAllocation(item.Id, godown, 4m, StockDirection.Outward, Money.FromRupees(15.00m),
                costTrackingNumber: "LOT/OPT"), true));

        var row = Assert.Single(ItemCostAnalysis.BuildItems(c, FyEnd));
        Assert.Equal(0m, row.OutwardQuantity);
        Assert.Equal(Money.Zero, row.Revenue);
        Assert.Equal(Money.FromRupees(100.00m), row.Cost);

        posting.SetOptional(opt.Id, false);

        var after = Assert.Single(ItemCostAnalysis.BuildItems(c, FyEnd));
        Assert.Equal(4m, after.OutwardQuantity);
        Assert.Equal(Money.FromRupees(60.00m), after.Revenue);
    }

    /// <summary>An Optional Job Work Out Order, a live one, and an Optional Material Out of 4 linked to the live one.
    /// <b>Mutations:</b> <c>JobWorkReports.BuildOrderBook</c> without the skip lists the Optional order;
    /// <c>FulfilledQuantity</c> without it counts the Optional dispatch (pending 6, not 10);
    /// <c>BuildMaterialRegister</c> without it lists the Optional dispatch.</summary>
    [Fact]
    public void Optional_job_work_orders_and_material_movements_are_off_the_job_work_registers_until_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Job Work Co", FyStart);
        new JobWorkService(c).SetEnabled(true);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("JW Goods");
        var nos = masters.CreateSimpleUnit("JNos", "JW Numbers");
        var main = c.MainLocation!.Id;
        var worker = masters.CreateGodown("Optional Worker Site", thirdParty: true).Id;
        var comp = masters.CreateStockItem("JW Board", grp.Id, nos.Id);
        masters.AddOpeningBalance(comp.Id, main, 10m, Money.FromRupees(5.00m));
        var fg = masters.CreateStockItem("JW Panel", grp.Id, nos.Id);
        var posting = new InventoryPostingService(c);

        JobWorkOrder Order(string no) => new(JobWorkDirection.Out, no, fg.Id, 10m,
            lines: new[] { new JobWorkOrderLine(comp.Id, JobWorkComponentTrack.PendingToIssue, 10m, godownId: main) },
            finishedGoodGodownId: main);

        var optOrder = InventoryVoucher.JobWork(Guid.NewGuid(), TypeOf(c, VoucherBaseType.JobWorkOutOrder),
            FyStart.AddDays(9), Order("JW/OPT"));
        optOrder.Optional = true;
        posting.Post(optOrder);
        var liveOrder = posting.Post(InventoryVoucher.JobWork(Guid.NewGuid(),
            TypeOf(c, VoucherBaseType.JobWorkOutOrder), FyStart.AddDays(9), Order("JW/LIVE")));
        var dispatch = InventoryVoucher.MaterialMovement(Guid.NewGuid(), TypeOf(c, VoucherBaseType.MaterialOut),
            FyStart.AddDays(14),
            source: new[] { new InventoryAllocation(comp.Id, main, 4m, StockDirection.Outward, Money.FromRupees(5.00m)) },
            destination: new[] { new InventoryAllocation(comp.Id, worker, 4m, StockDirection.Inward, Money.FromRupees(5.00m)) },
            orderLinks: new[] { liveOrder.Id });
        dispatch.Optional = true;
        posting.Post(dispatch);

        var book = Assert.Single(JobWorkReports.BuildOutOrderBook(c, FyStart, FyEnd));
        Assert.Equal("JW/LIVE", book.OrderNo);
        var line = Assert.Single(book.Components);
        Assert.Equal(0m, line.FulfilledQuantity);
        Assert.Equal(10m, line.PendingQuantity);
        Assert.Empty(JobWorkReports.BuildMaterialOutRegister(c, FyStart, FyEnd));

        posting.SetOptional(optOrder.Id, false);
        posting.SetOptional(dispatch.Id, false);

        var books = JobWorkReports.BuildOutOrderBook(c, FyStart, FyEnd);
        Assert.Equal(2, books.Count);
        Assert.Equal(4m, Assert.Single(books.Single(r => r.OrderNo == "JW/LIVE").Components).FulfilledQuantity);
        Assert.NotEmpty(JobWorkReports.BuildMaterialOutRegister(c, FyStart, FyEnd));
    }
}
