using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 9.8 — the two clauses this wave closes on the engine side: the <b>tracking-number selection</b> a
/// Purchase/Sales item-invoice offers (<see cref="BillsPending.OpenTrackingNumbers"/>), and the <b>voucher
/// ids</b> a pending row now carries so the screen can drill from it.
///
/// <para>🔴 <b>WHAT THESE TESTS ARE GUARDING AGAINST, STATED SO THEY CANNOT BE WEAKENED BY ACCIDENT.</b> The
/// picker's whole value is that it offers exactly what the report calls pending. The way that goes wrong is
/// not a crash — it is a picker that keeps offering a note after the bill for it has landed, which invites the
/// operator to bill the same consignment twice. So the load-bearing cases here are the ones where a number
/// must <i>disappear</i>: fully billed, dated after the bill, and already over-billed.</para>
/// </summary>
public sealed class TrackingNumberSelectionTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    // ════════════════════════════════════════════ the selection offered on a bill

    /// <summary>A receipt nobody has billed is offered, carrying its own date, party and item.</summary>
    [Fact]
    public void An_unbilled_receipt_note_is_offered_to_the_purchase_invoice()
    {
        var b = new Fixture("Offer Co");
        b.ReceiptNote("TRK/1", 40m, new DateOnly(2025, 4, 5));

        var option = Assert.Single(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
        Assert.Equal("TRK/1", option.TrackingNumber);
        Assert.Equal(new DateOnly(2025, 4, 5), option.Date);
        Assert.Equal(40m, option.PendingQuantity);
        Assert.Equal("Widget", Assert.Single(option.PendingItemNames));
    }

    /// <summary>
    /// 🔴 <b>THE CASE THE WHOLE MECHANISM EXISTS TO PREVENT.</b> Once the bill covers the receipt in full the
    /// number must vanish from the picker. An implementation that listed every tracking number ever keyed
    /// would pass every other test in this file and still invite a second bill against settled goods.
    /// </summary>
    [Fact]
    public void A_fully_billed_receipt_note_is_no_longer_offered()
    {
        var b = new Fixture("Settled Co");
        b.ReceiptNote("TRK/1", 40m, new DateOnly(2025, 4, 5));
        b.PurchaseBill("TRK/1", 40m, new DateOnly(2025, 4, 9));

        Assert.Empty(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
    }

    /// <summary>A PART bill leaves the number offered, for the remainder only — not for the whole receipt.</summary>
    [Fact]
    public void A_part_billed_receipt_note_is_offered_for_the_remainder_only()
    {
        var b = new Fixture("Part Co");
        b.ReceiptNote("TRK/1", 40m, new DateOnly(2025, 4, 5));
        b.PurchaseBill("TRK/1", 25m, new DateOnly(2025, 4, 9));

        var option = Assert.Single(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
        Assert.Equal(15m, option.PendingQuantity);
    }

    /// <summary>
    /// 🔴 <b>THE OTHER DIRECTION, AND IT IS NOT SYMMETRIC.</b> A number already billed for MORE than the goods
    /// received sits in <c>BilledNotReceived</c>. It must NOT be offered: billing it again would compound an
    /// over-claim the report is trying to surface.
    /// </summary>
    [Fact]
    public void A_number_already_over_billed_is_not_offered()
    {
        var b = new Fixture("Over Co");
        b.PurchaseBill("TRK/9", 30m, new DateOnly(2025, 4, 9));

        // The report does see it — on the other side — so this is a deliberate exclusion, not an empty book.
        Assert.Equal(
            BillsPendingSection.BilledNotReceived,
            Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf)).Section);
        Assert.Empty(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
    }

    /// <summary>
    /// A note dated AFTER the bill being keyed is not offered. The bill's own date is the as-of, so this is
    /// what stops a 05-Apr invoice being raised against a 10-Apr receipt.
    /// </summary>
    [Fact]
    public void A_note_dated_after_the_bill_is_not_offered_but_is_once_the_bill_moves()
    {
        var b = new Fixture("Future Co");
        b.ReceiptNote("TRK/LATE", 40m, new DateOnly(2025, 4, 10));

        Assert.Empty(BillsPending.OpenTrackingNumbers(b.Company, new DateOnly(2025, 4, 5), purchase: true));

        var later = Assert.Single(
            BillsPending.OpenTrackingNumbers(b.Company, new DateOnly(2025, 4, 10), purchase: true));
        Assert.Equal("TRK/LATE", later.TrackingNumber);
    }

    /// <summary>
    /// The purchase and sales lists are separate books: a Delivery Note is never offered to a Purchase
    /// invoice. Keying the inward number on an outward bill is exactly the mix-up tracking numbers exist to
    /// make impossible.
    /// </summary>
    [Fact]
    public void A_delivery_note_is_offered_to_sales_and_never_to_purchase()
    {
        var b = new Fixture("Sides Co");
        b.DeliveryNote("DN/1", 12m, new DateOnly(2025, 4, 5));

        Assert.Equal("DN/1",
            Assert.Single(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: false)).TrackingNumber);
        Assert.Empty(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
    }

    /// <summary>
    /// One note covering three items is ONE entry in the list, not three — the vendor's list is a list of
    /// tracking numbers. The items still come through so two free-text numbers can be told apart, and they
    /// are ordinally ordered so the label is identical on every platform the gate runs on.
    /// </summary>
    [Fact]
    public void One_number_over_several_items_is_a_single_entry_naming_them_all()
    {
        var b = new Fixture("Multi Co");
        b.ReceiptNoteOfItems("TRK/M", new DateOnly(2025, 4, 5), ("Widget", 10m), ("Anvil", 5m));

        var option = Assert.Single(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
        Assert.Equal(new[] { "Anvil", "Widget" }, option.PendingItemNames);
        Assert.Equal(15m, option.PendingQuantity);
    }

    /// <summary>With the F11 feature off there is nothing to select — the same gate the report honours.</summary>
    [Fact]
    public void Nothing_is_offered_when_tracking_numbers_are_switched_off()
    {
        var b = new Fixture("Off Co");
        b.ReceiptNote("TRK/1", 40m, new DateOnly(2025, 4, 5));
        b.Company.UseTrackingNumbers = false;

        Assert.Empty(BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true));
    }

    /// <summary>Oldest first, so the operator clears the longest-outstanding receipt first.</summary>
    [Fact]
    public void The_list_is_ordered_oldest_first()
    {
        var b = new Fixture("Order Co");
        b.ReceiptNote("TRK/LATER", 10m, new DateOnly(2025, 4, 20));
        b.ReceiptNote("TRK/EARLY", 10m, new DateOnly(2025, 4, 2));

        Assert.Equal(
            new[] { "TRK/EARLY", "TRK/LATER" },
            BillsPending.OpenTrackingNumbers(b.Company, AsOf, purchase: true)
                .Select(o => o.TrackingNumber).ToArray());
    }

    // ════════════════════════════════════════════ the drill ids on a pending row

    /// <summary>
    /// A "goods received, not billed" row carries the id of the NOTE that received them — and the note's date,
    /// so the row's date and its drill target are provably the same document.
    /// </summary>
    [Fact]
    public void A_goods_not_billed_row_points_at_the_note_that_moved_the_goods()
    {
        var b = new Fixture("Drill Goods Co");
        var noteId = b.ReceiptNote("TRK/1", 40m, new DateOnly(2025, 4, 5));

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.GoodsNotBilled, row.Section);
        Assert.Equal(noteId, row.GoodsVoucherId);
        Assert.Equal(b.Company.InventoryVouchers.Single(v => v.Id == noteId).Date, row.Date);
    }

    /// <summary>A "bills received, goods not" row carries the id of the BILL — the other aggregate.</summary>
    [Fact]
    public void A_billed_not_received_row_points_at_the_bill()
    {
        var b = new Fixture("Drill Bill Co");
        var billId = b.PurchaseBill("TRK/9", 30m, new DateOnly(2025, 4, 9));

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.BilledNotReceived, row.Section);
        Assert.Equal(billId, row.BillVoucherId);
        Assert.Equal(b.Company.Vouchers.Single(v => v.Id == billId).Date, row.Date);
    }

    /// <summary>
    /// 🔴 <b>WHEN TWO NOTES SHARE A NUMBER THE ROW POINTS AT THE EARLIER ONE</b> — the same one its date comes
    /// from. Pointing at the later note would put the operator on a document whose date is not the date they
    /// just read off the row.
    /// </summary>
    [Fact]
    public void Two_notes_under_one_number_drill_to_the_earlier_note()
    {
        var b = new Fixture("Two Notes Co");
        var first = b.ReceiptNote("TRK/1", 10m, new DateOnly(2025, 4, 5));
        var second = b.ReceiptNote("TRK/1", 10m, new DateOnly(2025, 4, 20));

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(first, row.GoodsVoucherId);
        Assert.NotEqual(second, row.GoodsVoucherId);
        Assert.Equal(new DateOnly(2025, 4, 5), row.Date);
    }

    // ═════════════════════════════════════════════════════════════════════ fixture

    private sealed class Fixture
    {
        public Company Company { get; }

        private readonly StockItem _item;
        private readonly Godown _main;
        private readonly InventoryService _inventory;
        private readonly Guid _unitId;
        private readonly Domain.Ledger _supplier;
        private readonly Domain.Ledger _purchases;
        private int _number;

        public Fixture(string name)
        {
            Company = CompanyFactory.CreateSeeded(name, FyStart);
            Company.UseTrackingNumbers = true;

            _inventory = new InventoryService(Company);
            var unit = _inventory.CreateSimpleUnit("Nos", "Numbers");
            _unitId = unit.Id;
            var group = Company.FindStockGroupByName("Primary") ?? _inventory.CreateStockGroup("Primary");
            _item = _inventory.CreateStockItem("Widget", group.Id, unit.Id);
            _main = Company.MainLocation!;

            _supplier = Ledger("Supplier Ltd", "Sundry Creditors", debit: false);
            _purchases = Ledger("Purchases", "Purchase Accounts", debit: true);
        }

        private Domain.Ledger Ledger(string name, string group, bool debit)
        {
            var l = new Domain.Ledger(Guid.NewGuid(), name, Company.FindGroupByName(group)!.Id,
                Money.Zero, openingIsDebit: debit);
            Company.AddLedger(l);
            return l;
        }

        public Guid ReceiptNote(string tracking, decimal qty, DateOnly date) =>
            Note("Receipt Note", StockDirection.Inward, date,
                new[] { (_item.Id, tracking, qty) });

        public Guid DeliveryNote(string tracking, decimal qty, DateOnly date) =>
            Note("Delivery Note", StockDirection.Outward, date,
                new[] { (_item.Id, tracking, qty) });

        /// <summary>One note carrying several ITEMS under one tracking number.</summary>
        public Guid ReceiptNoteOfItems(string tracking, DateOnly date, params (string Item, decimal Qty)[] lines)
        {
            var group = Company.FindStockGroupByName("Primary")!;
            var resolved = lines
                .Select(l => (
                    Id: (Company.FindStockItemByName(l.Item)
                         ?? _inventory.CreateStockItem(l.Item, group.Id, _unitId)).Id,
                    Tracking: tracking,
                    l.Qty))
                .ToArray();
            return Note("Receipt Note", StockDirection.Inward, date, resolved);
        }

        private Guid Note(
            string typeName, StockDirection direction, DateOnly date,
            (Guid Item, string Tracking, decimal Qty)[] lines)
        {
            var id = Guid.NewGuid();
            Company.AddInventoryVoucher(new InventoryVoucher(
                id, Company.FindVoucherTypeByName(typeName)!.Id, date,
                lines.Select(l => new InventoryAllocation(
                        l.Item, _main.Id, l.Qty, direction,
                        rate: Money.FromRupees(10m), trackingNumber: l.Tracking))
                    .ToArray(),
                number: ++_number));
            return id;
        }

        public Guid PurchaseBill(string tracking, decimal qty, DateOnly date)
        {
            var value = Money.FromRupees(qty * 10m);
            var voucher = new Voucher(
                Guid.NewGuid(), Company.FindVoucherTypeByName("Purchase")!.Id, date,
                new[]
                {
                    new EntryLine(_purchases.Id, value, DrCr.Debit),
                    new EntryLine(_supplier.Id, value, DrCr.Credit),
                },
                number: ++_number,
                partyId: _supplier.Id,
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(_item.Id, _main.Id, qty, Money.FromRupees(10m),
                        StockDirection.Inward, trackingNumber: tracking),
                });

            new LedgerService(Company).Post(voucher);
            return voucher.Id;
        }
    }
}
