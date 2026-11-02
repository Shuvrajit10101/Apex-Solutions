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
}
