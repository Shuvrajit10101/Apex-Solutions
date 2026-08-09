using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Order fulfilment tracking (Phase 10.10 / WF-8) — the tests that retire an order.
/// <para><b>The defect these pin.</b> Before WF-8 <see cref="InventoryRegisters.BuildOrders"/> hard-coded
/// <c>FulfilledQuantity: 0m, OutstandingQuantity: line.Quantity</c>: an order in this product was NEVER
/// retired. A sales order delivered in full still reported its whole quantity outstanding, for ever, and the
/// error was the entire delivered-order history — unbounded. WF-7 then nets Sales Orders Due into Nett
/// Available, so that stale figure stops being a wrong display column and becomes the shortfall arithmetic
/// behind a real supplier purchase order.</para>
/// <para><b>The attribution rule under test</b> is documented in full on
/// <see cref="OrderFulfilment"/>: the cohort is the pair <b>(PartyId, StockItemId)</b>, and inside it a
/// fulfilling movement retires the earliest still-open order line (FIFO), never an order dated after the
/// movement, never across base types, and never below zero outstanding. <b>A blank party is a cohort of its
/// own, not a wildcard</b>: a note left on "(none)" retires only orders raised on "(none)", and a note naming a
/// party never retires a blank order.</para>
/// <para>🔴 <b>Read <see cref="ShellNote"/> before adding a fixture.</b> It mirrors
/// <c>InventoryVoucherEntryViewModel.BuildMovementNote</c> argument for argument, so a fixture built with it is
/// a shape the shipped screen can actually post. That mattered once and could matter again: this suite's first
/// 18 tests all handed a party to a movement through the raw <see cref="InventoryVoucher"/> constructor, at a
/// time when the shell could not put a party on a note at all — so every one of them passed while the feature
/// was a no-op on every real book.</para>
/// </summary>
public class OrderFulfilmentTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly D3 = new(2024, 4, 15);
    private static readonly DateOnly D4 = new(2024, 4, 20);
    private static readonly DateOnly D5 = new(2024, 4, 25);

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required Guid GroupId { get; init; }
        public required Guid UnitId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid SecondGodownId { get; init; }
    }

    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Fulfilment Co", FyStart);
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
            GodownId = c.MainLocation!.Id,
            SecondGodownId = wh2.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private static Guid Party(Kit k, string name, string groupName = "Sundry Debtors")
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, k.Company.FindGroupByName(groupName)!.Id, Money.Zero, true);
        k.Company.AddLedger(l);
        return l.Id;
    }

    private Guid Item(Kit k, string name)
        => k.Masters.CreateStockItem(name, k.GroupId, k.UnitId).Id;

    /// <summary>Opening stock, so a delivery does not have to be preceded by a Receipt Note that would itself
    /// be a candidate fulfilment of a purchase order and confuse the fixture.</summary>
    private void Opening(Kit k, Guid item, decimal qty)
        => k.Masters.AddOpeningBalance(item, k.GodownId, qty, Money.FromRupees(10.37m));

    private InventoryVoucher Order(Kit k, VoucherBaseType baseType, Guid item, DateOnly date, decimal qty,
        Guid? partyId, bool cancelled = false, int number = 0)
    {
        var v = InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, baseType), date,
            new[] { new OrderLine(item, k.GodownId, qty, Money.FromRupees(47.33m)) },
            number: number, partyId: partyId);
        k.Posting.Post(v);
        if (cancelled) k.Posting.Cancel(v.Id);
        return v;
    }

    /// <summary>A MULTI-LINE order — the shape that exercises the <c>LineIndex</c> half of the fulfilment map's
    /// key. A single-line-only suite proves nothing about a map being keyed by line.</summary>
    private InventoryVoucher OrderOf(Kit k, VoucherBaseType baseType, DateOnly date, Guid? partyId,
        params OrderLine[] lines)
    {
        var v = InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, baseType), date, lines, partyId: partyId);
        k.Posting.Post(v);
        return v;
    }

    private OrderLine Line(Kit k, Guid item, decimal qty) => new(item, k.GodownId, qty, Money.FromRupees(47.33m));

    private InventoryVoucher Movement(Kit k, VoucherBaseType baseType, Guid item, DateOnly date, decimal qty,
        Guid? partyId, bool cancelled = false, StockDirection? asDirection = null)
    {
        var direction = asDirection
            ?? (baseType == VoucherBaseType.ReceiptNote ? StockDirection.Inward : StockDirection.Outward);
        var v = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, baseType), date,
            new[] { new InventoryAllocation(item, k.GodownId, qty, direction, Money.FromRupees(10.37m)) },
            partyId: partyId);
        k.Posting.Post(v);
        if (cancelled) k.Posting.Cancel(v.Id);
        return v;
    }

    /// <summary>
    /// 🔴 A movement note posted <b>EXACTLY as the shipped shell posts one</b>, and the reason this helper
    /// exists rather than a plain <see cref="Movement"/> call: it is a named, greppable statement that this is
    /// the shape the product can actually key, mirroring
    /// <c>InventoryVoucherEntryViewModel.BuildMovementNote</c> argument for argument — the only
    /// <c>new InventoryVoucher(</c> in the Desktop project.
    /// <para><b>The party argument is the WF-8 root-cause fix.</b> Until it landed, <c>BuildMovementNote</c>
    /// omitted <c>partyId</c> entirely and the picker was gated <c>IsVisible="{Binding IsOrder}"</c>, so a note
    /// in this product could not name a party at all — which made a <c>(PartyId, StockItemId)</c> cohort miss on
    /// every real book. It now passes <c>partyId: SelectedParty?.Ledger?.Id</c> exactly as <c>BuildOrder</c>
    /// does, and the picker is shown for movement notes. <c>partyId: null</c> here is the operator leaving the
    /// picker on "(none)", which is a real and reachable shape — not the unreachable one it used to be.</para>
    /// </summary>
    private InventoryVoucher ShellNote(Kit k, VoucherBaseType baseType, Guid item, DateOnly date, decimal qty,
        Guid? partyId = null)
    {
        var direction = baseType is VoucherBaseType.ReceiptNote or VoucherBaseType.RejectionIn
            ? StockDirection.Inward
            : StockDirection.Outward;
        var v = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, baseType), date,
            new[] { new InventoryAllocation(item, k.GodownId, qty, direction, Money.FromRupees(10.37m)) },
            number: 0, narration: null, partyId: partyId, postDated: false);
        k.Posting.Post(v);
        return v;
    }

    /// <summary>
    /// 🔴 The ACCOUNTING side of the fixture — everything an <b>item invoice</b> needs, and the reason it is a
    /// second kit rather than fields on <see cref="Kit"/>: an item invoice is not an
    /// <see cref="InventoryVoucher"/> at all. It moves stock through <c>Voucher.InventoryLines</c> and posts a
    /// balanced Dr/Cr at the same time, so it needs a <see cref="LedgerService"/>, a stock-leg ledger on each
    /// arm and a settlement ledger — none of which the pure-stock fixture above has any use for.
    /// </summary>
    private sealed class Books
    {
        public required LedgerService Ledgers { get; init; }
        public required Guid SalesAccount { get; init; }
        public required Guid PurchaseAccount { get; init; }
        public required Guid Cash { get; init; }
        public required Guid SalesTypeId { get; init; }
        public required Guid PurchaseTypeId { get; init; }
    }

    private static Books NewBooks(Kit k)
    {
        var c = k.Company;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        var purchases = new Domain.Ledger(
            Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(sales);
        c.AddLedger(purchases);
        var cashGroup = c.FindGroupByName("Cash-in-Hand")!.Id;
        return new Books
        {
            Ledgers = new LedgerService(c),
            SalesAccount = sales.Id,
            PurchaseAccount = purchases.Id,
            Cash = c.Ledgers.First(l => l.GroupId == cashGroup).Id,
            SalesTypeId = TypeId(c, VoucherBaseType.Sales),
            PurchaseTypeId = TypeId(c, VoucherBaseType.Purchase),
        };
    }

    /// <summary>
    /// 🔴 <b>THE THIRD AND FOURTH DOORS.</b> An <b>item invoice</b> — a Purchase/Sales accounting voucher run in
    /// item-invoice mode — which moves stock through <c>Voucher.InventoryLines</c> and
    /// <c>Services.ItemInvoiceStock</c>, <b>never through an <see cref="InventoryVoucher"/></b>. This is the
    /// ordinary retail and trading path: a shop that bills its goods directly and raises no Delivery Note at all.
    /// <para>The party rides on the ACCOUNTING voucher (<see cref="Voucher.PartyId"/>), not on an inventory
    /// voucher header — which is the whole reason the cohort key has to be resolved per door rather than read off
    /// one type. <c>partyId: null</c> is the walk-in/cash sale, and it sits in the blank cohort exactly as a
    /// blank note does.</para>
    /// <para>The settlement leg is deliberately CASH rather than the party ledger, so that
    /// <see cref="Voucher.PartyId"/> is the ONLY thing carrying the counterparty. An implementation that
    /// back-derived the party from the debtor/creditor entry line instead would pass with a party leg and fail
    /// here — the fixture must not hand it two ways to be right.</para>
    /// </summary>
    private Voucher ItemInvoice(Kit k, Books b, VoucherBaseType baseType, Guid item, DateOnly date, decimal qty,
        Guid? partyId, decimal rate = 47.36m, bool cancelled = false, bool optional = false, Guid? unitId = null)
    {
        var isPurchase = baseType == VoucherBaseType.Purchase;
        var line = new VoucherInventoryLine(item, k.GodownId, qty, Money.FromRupees(rate),
            isPurchase ? StockDirection.Inward : StockDirection.Outward, unitId: unitId);
        // line.Value is already paisa-exact (Money.ForexBase rounds), so the pairing invariant — item-lines total
        // == the stock-leg accounting amount — foots for ANY rate/quantity pair without hand arithmetic.
        var legs = isPurchase
            ? new[]
            {
                new EntryLine(b.PurchaseAccount, line.Value, DrCr.Debit),
                new EntryLine(b.Cash, line.Value, DrCr.Credit),
            }
            : new[]
            {
                new EntryLine(b.Cash, line.Value, DrCr.Debit),
                new EntryLine(b.SalesAccount, line.Value, DrCr.Credit),
            };
        var v = new Voucher(Guid.NewGuid(), isPurchase ? b.PurchaseTypeId : b.SalesTypeId, date, legs,
            partyId: partyId, optional: optional, inventoryLines: new[] { line });
        b.Ledgers.Post(v);
        if (cancelled) b.Ledgers.Cancel(v.Id);
        return v;
    }

    private static OrderRegisterRow Row(Company c, DateOnly to, int number)
        => InventoryRegisters.BuildOrders(c, FyStart, to).Single(r => r.Number == number);

    private static OrderRegisterRow Row(Company c, DateOnly to, int number, string itemName)
        => InventoryRegisters.BuildOrders(c, FyStart, to).Single(r => r.Number == number && r.ItemName == itemName);

    // ================================================================ the five locked cases

    /// <summary>A sales order delivered in full is RETIRED — outstanding 0, not the ordered quantity for ever.
    /// This is the ordinary order-to-delivery cycle and the case that made the pre-WF-8 error unbounded.</summary>
    [Fact]
    public void A_fully_delivered_sales_order_reports_zero_outstanding()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(60.125m, row.OrderedQuantity);
        Assert.Equal(60.125m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
    }

    /// <summary>A half-received purchase order reports EXACTLY the un-received remainder — 47.330 ordered less
    /// 19.875 received leaves 27.455, to the six-dp quantity precision the engine keeps.</summary>
    [Fact]
    public void A_half_received_purchase_order_reports_exactly_the_unreceived_remainder()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, item, D2, 19.875m, bimal);

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(47.330m, row.OrderedQuantity);
        Assert.Equal(19.875m, row.FulfilledQuantity);
        Assert.Equal(27.455m, row.OutstandingQuantity);
    }

    /// <summary>Over-delivery CLAMPS: outstanding is 0, never negative, and the surplus is not carried anywhere
    /// (there is no order line to carry it on), so Fulfilled stops at the ordered quantity.</summary>
    [Fact]
    public void Over_delivery_clamps_outstanding_at_zero_and_never_goes_negative()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D3, 75.500m, ashok);   // 15.375 MORE than ordered

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(60.125m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
        Assert.True(row.OutstandingQuantity >= 0m, "outstanding must never go negative");
    }

    /// <summary>A CANCELLED fulfilling voucher moved no stock, so it retires nothing — the same rule
    /// <c>JobWorkReports.FulfilledQuantity</c> applies to its material movements.</summary>
    [Fact]
    public void A_cancelled_fulfilling_voucher_does_not_retire_the_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, ashok, cancelled: true);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>One order fulfilled by SEVERAL later movements sums them: 30.125 + 25.250 + 12.500 = 67.875
    /// against 90.625 ordered leaves 22.750.</summary>
    [Fact]
    public void Fulfilment_sums_across_several_later_movements()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 90.625m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, item, D2, 30.125m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, item, D3, 25.250m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, item, D4, 12.500m, bimal);

        var row = Row(k.Company, D5, po.Number);
        Assert.Equal(67.875m, row.FulfilledQuantity);
        Assert.Equal(22.750m, row.OutstandingQuantity);
    }

    // ================================================================ 🔴 the SHIPPED path

    /// <summary>
    /// 🔴 <b>THE DRIVING CASE — the whole slice is worthless without it.</b> The ordinary order-to-delivery
    /// cycle as the product actually keys it: a Sales Order that NAMES a customer, retired by a Delivery Note
    /// that names the SAME customer — and the note is built exactly as <c>BuildMovementNote</c> builds one, so
    /// this is the shipped path and not a shape only a test can make.
    /// <para><b>This case is the reason the fix went to the root.</b> Until Phase 10.10 a note could not name a
    /// party at all: <c>BuildMovementNote</c> omitted <c>partyId</c> and the picker was gated
    /// <c>IsVisible="{Binding IsOrder}"</c>. So the order sat in cohort <c>(Ashok, Widget)</c>, the note in
    /// <c>(null, Widget)</c>, the lookup missed, and the register reported the full 60.125 outstanding —
    /// <b>byte-identical to the pre-WF-8 defect</b>. Every one of the original 18 tests missed it because every
    /// one of them handed a party to the movement, a shape the application could not then produce.</para>
    /// </summary>
    [Fact]
    public void A_delivery_note_posted_the_way_the_shell_posts_one_retires_a_party_named_sales_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(60.125m, row.OrderedQuantity);
        Assert.Equal(60.125m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
        // The aggregate WF-7 consumes must agree, or DD-5 survives the fix that was meant to kill it.
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>The inward twin of the driving case: a Receipt Note naming the supplier partly retires the
    /// purchase order raised on him — 47.330 ordered less 19.875 received leaves 27.455.</summary>
    [Fact]
    public void A_receipt_note_posted_the_way_the_shell_posts_one_retires_a_party_named_purchase_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        ShellNote(k, VoucherBaseType.ReceiptNote, item, D2, 19.875m, partyId: bimal);

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(19.875m, row.FulfilledQuantity);
        Assert.Equal(27.455m, row.OutstandingQuantity);
        Assert.Equal(27.455m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D4));
    }

    /// <summary>The POSITIVE null-to-null case: an order raised with the picker on "(none)" retired by a note
    /// left on "(none)". Blankness costs nothing as long as it is CONSISTENT, which is the ordinary small-book
    /// shape and the shape every WF-7/DD-5 fixture uses. It had no fixture before: "(none)" appeared only inside
    /// tests whose expected result was "nothing retired", so they passed whether the null arm matched or
    /// not.</summary>
    [Fact]
    public void A_note_left_blank_retires_an_order_left_blank()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, partyId: null);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(60.125m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    // ================================================================ the attribution rule

    /// <summary>FIFO within the item cohort: the EARLIEST still-open order line absorbs the movement first, and
    /// the spill goes to the next. 55.500 delivered against a 40.375 order then a 60.125 order retires the first
    /// in full and 15.125 of the second.</summary>
    [Fact]
    public void Fulfilment_is_attributed_to_the_earliest_open_order_first()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var first = Order(k, VoucherBaseType.SalesOrder, item, D1, 40.375m, ashok);
        var second = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D3, 55.500m, ashok);

        var a = Row(k.Company, D4, first.Number);
        Assert.Equal(40.375m, a.FulfilledQuantity);
        Assert.Equal(0m, a.OutstandingQuantity);

        var b = Row(k.Company, D4, second.Number);
        Assert.Equal(15.125m, b.FulfilledQuantity);
        Assert.Equal(45.000m, b.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 FIFO among orders raised on the SAME DAY — the ordinary case, not an edge case, and the one that pins
    /// the tie-breaks in <c>CompareOpenLines</c>. <see cref="List{T}.Sort(Comparison{T})"/> is UNSTABLE, so a
    /// date-only comparison lets the same book produce two different Order Registers.
    /// <para>The two orders are posted with EXPLICIT numbers in the reverse of their sequence order (7 keyed
    /// first, then 3) precisely so insertion order and voucher-number order disagree: with the number tie-break
    /// removed, the 55.500 delivery retires order 7 (Fulfilled 55.500 / Outstanding 4.625) and leaves order 3
    /// untouched, and this test goes red.</para>
    /// </summary>
    [Fact]
    public void Two_orders_raised_on_the_same_day_are_retired_in_voucher_number_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Opening(k, item, 180.875m);
        Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, partyId: null, number: 7);   // keyed FIRST
        Order(k, VoucherBaseType.SalesOrder, item, D2, 40.375m, partyId: null, number: 3);   // keyed SECOND
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 55.500m);

        var earlier = Row(k.Company, D4, 3);
        Assert.Equal(40.375m, earlier.FulfilledQuantity);
        Assert.Equal(0m, earlier.OutstandingQuantity);

        var later = Row(k.Company, D4, 7);
        Assert.Equal(15.125m, later.FulfilledQuantity);
        Assert.Equal(45.000m, later.OutstandingQuantity);

        // Determinism: the same book must answer the same figures on a second build, not whichever pair the
        // sort happened to produce.
        var again = InventoryRegisters.BuildOrders(k.Company, FyStart, D4);
        Assert.Equal(40.375m, again.Single(r => r.Number == 3).FulfilledQuantity);
        Assert.Equal(15.125m, again.Single(r => r.Number == 7).FulfilledQuantity);
    }

    /// <summary>A CANCELLED order is void: it must not absorb FIFO capacity that belongs to the live order
    /// behind it, or a delivered live order would stay outstanding for ever.</summary>
    [Fact]
    public void A_cancelled_order_does_not_absorb_a_live_orders_fulfilment()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok, cancelled: true);   // earlier, but void
        var live = Order(k, VoucherBaseType.SalesOrder, item, D2, 47.330m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D3, 47.330m, ashok);

        var row = Row(k.Company, D4, live.Number);
        Assert.Equal(47.330m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
    }

    /// <summary>A movement dated BEFORE the order cannot have fulfilled it — goods shipped in April do not
    /// retire an order placed in May.</summary>
    [Fact]
    public void A_movement_dated_before_the_order_does_not_retire_it()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        Movement(k, VoucherBaseType.DeliveryNote, item, D1, 60.125m, ashok);   // BEFORE the order
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>A movement AFTER the register's as-of date does not retire the order — the register must not
    /// restate itself retroactively when a later delivery is posted.</summary>
    [Fact]
    public void A_movement_after_the_as_of_date_does_not_retire_the_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, item, D4, 60.125m, ashok);

        Assert.Equal(60.125m, Row(k.Company, D3, so.Number).OutstandingQuantity);   // as of D3 — still open
        Assert.Equal(0m, Row(k.Company, D4, so.Number).OutstandingQuantity);        // as of D4 — retired
    }

    /// <summary>
    /// 🔴 The fulfilling voucher is pinned to its BASE TYPE, <b>not merely to the line direction</b> — the rule
    /// <c>JobWorkReports.FulfilledQuantity</c> states in its own words. A <b>Rejection In</b> carries an INWARD
    /// line, so a direction-only match would let a customer's return retire a supplier purchase order and report
    /// goods as received that were never bought.
    /// <para>This test and its Stock-Journal twin below are the two that BITE the base-type pin: relax
    /// <c>CountsAsOf</c> to accept any base type and both go red. The cross-arm case
    /// (<see cref="A_delivery_note_never_retires_a_purchase_order"/>) is double-guarded and bites neither guard
    /// alone.</para>
    /// </summary>
    [Fact]
    public void A_rejection_in_does_not_retire_a_purchase_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        // Inward, same party, same item, after the order — everything but the base type matches a Receipt Note.
        Movement(k, VoucherBaseType.RejectionIn, item, D2, 47.330m, bimal, asDirection: StockDirection.Inward);

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(47.330m, row.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 The base-type pin again, on the outward arm: an inter-godown <b>Stock Journal</b> carries an OUTWARD
    /// source line, so a direction-only match would let moving stock between our own warehouses retire a sales
    /// order — nothing left the building. The order is left party-less here and a Stock Journal names no party
    /// either, so both sit in the SAME <c>(null, Widget)</c> cohort and nothing but the base type stands between
    /// this transfer and a wrongly retired order.
    /// </summary>
    [Fact]
    public void A_stock_journal_transfer_does_not_retire_a_sales_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, partyId: null);
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.StockJournal), D2,
            source: new[] { new InventoryAllocation(item, k.GodownId, 60.125m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item, k.SecondGodownId, 60.125m, StockDirection.Inward) }));

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>
    /// A cross-arm smoke case: a Delivery Note is outward and a Receipt Note inward, so a delivery never retires
    /// a purchase order.
    /// <para>🔴 <b>Recorded so no one mistakes this for a guard test:</b> it is DOUBLE-guarded and bites neither
    /// guard alone — remove the base-type pin and the direction filter still excludes it; remove the direction
    /// filter and the base-type pin still does. It was previously named
    /// <c>The_fulfilling_voucher_is_pinned_to_its_base_type</c>, which claimed a bite it never had; the tests
    /// that really pin the base type are <see cref="A_rejection_in_does_not_retire_a_purchase_order"/> and
    /// <see cref="A_stock_journal_transfer_does_not_retire_a_sales_order"/>. The direction filter itself cannot
    /// be bitten by ANY fixture: <c>InventoryPostingService.RequireDirection</c> makes a wrong-direction line on
    /// a note unpostable, which is stated at the filter in <see cref="OrderFulfilment"/>.</para>
    /// </summary>
    [Fact]
    public void A_delivery_note_never_retires_a_purchase_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        Opening(k, item, 180.875m);
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        Movement(k, VoucherBaseType.DeliveryNote, item, D2, 47.330m, bimal);   // outward — not a receipt

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(47.330m, row.OutstandingQuantity);
    }

    /// <summary>Two DIFFERENT named parties never fulfil one another: goods shipped to Chandra do not retire
    /// Ashok's order. The two orders live in different cohorts, so this is structural rather than a
    /// tie-break — and the shell can now key it, because a delivery note carries a party.</summary>
    [Fact]
    public void A_movement_for_another_named_party_does_not_retire_this_partys_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        var chandra = Party(k, "Chandra Stores");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: chandra);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>Fulfilment never crosses stock items: delivering a Gadget does not retire a Widget order.</summary>
    [Fact]
    public void A_movement_of_another_item_does_not_retire_this_items_order()
    {
        var k = NewKit();
        var widget = Item(k, "Widget");
        var gadget = Item(k, "Gadget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, gadget, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, widget, D2, 60.125m, ashok);
        Movement(k, VoucherBaseType.DeliveryNote, gadget, D3, 60.125m, ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 The fulfilment map is keyed by (voucher, LINE INDEX), and this is the only fixture that proves it: a
    /// SINGLE purchase order carrying two lines, of which only the second is received. Every other fixture uses
    /// a one-line order, so <c>fulfilled[(voucherId, 0)]</c> would satisfy them all.
    /// <para>Under that mutation the Widget row reads Fulfilled 90.625 / Outstanding 0 (the −43.295 swallowed by
    /// the register's floor) and the Gadget row Fulfilled 0 / Outstanding 90.625: the genuine 47.330 Widget
    /// commitment vanishes from the outstanding book and 90.625 of already-received Gadgets stays in it, and
    /// <see cref="OrderFulfilment.OutstandingByItem"/> hands WF-7 the inverted pair.</para>
    /// </summary>
    [Fact]
    public void A_multi_line_order_retires_only_the_line_whose_item_moved()
    {
        var k = NewKit();
        var widget = Item(k, "Widget");
        var gadget = Item(k, "Gadget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = OrderOf(k, VoucherBaseType.PurchaseOrder, D1, bimal,
            Line(k, widget, 47.330m),        // line 0 — never received
            Line(k, gadget, 90.625m));       // line 1 — received in full
        ShellNote(k, VoucherBaseType.ReceiptNote, gadget, D2, 90.625m, partyId: bimal);

        var widgetRow = Row(k.Company, D4, po.Number, "Widget");
        Assert.Equal(0m, widgetRow.FulfilledQuantity);
        Assert.Equal(47.330m, widgetRow.OutstandingQuantity);

        var gadgetRow = Row(k.Company, D4, po.Number, "Gadget");
        Assert.Equal(90.625m, gadgetRow.FulfilledQuantity);
        Assert.Equal(0m, gadgetRow.OutstandingQuantity);

        var totals = OrderFulfilment.OutstandingByItem(k.Company, VoucherBaseType.PurchaseOrder, D4);
        Assert.Equal(47.330m, totals[widget]);
        Assert.False(totals.ContainsKey(gadget));
    }

    /// <summary>
    /// 🔴 <b>The per-line contract INSIDE one cohort — the half no other fixture reaches.</b>
    /// <see cref="A_multi_line_order_retires_only_the_line_whose_item_moved"/> puts its two lines on DIFFERENT
    /// items, so they land in different cohorts and never compete: a map keyed
    /// <c>(voucherId, stockItemId)</c> would satisfy it. Here BOTH lines carry the SAME item on the SAME voucher,
    /// so they share a cohort and share every field <see cref="OrderFulfilment"/> orders on — date, voucher number
    /// and voucher id are all identical — and <b>only the line index separates them</b>. A 55.500 delivery must
    /// therefore fill line 0 (40.375) in full and spill 15.125 onto line 1.
    /// <para>Read off the fulfilment map by <c>(voucherId, lineIndex)</c> rather than off the register, because
    /// the register's rows are indistinguishable by number AND item name — which is itself the point: the line
    /// index is the only thing in the system that tells these commitments apart.</para>
    /// <para>🔴 <b>WHY 40 LINES AND NOT 2 — measured, not guessed.</b> The obvious two-line fixture pins the
    /// contract but <b>bites nothing</b>: dropping the <c>byLine</c> clause from <c>CompareOpenLines</c> leaves it
    /// GREEN, because <see cref="List{T}.Sort(Comparison{T})"/> falls back to an insertion sort below 17 elements
    /// and that sort happens to preserve insertion order — which IS line order. Verified by making that edit
    /// against a two-line version of this test: 43/43 still passed. Above the threshold the introsort partitions
    /// and equal elements are genuinely reordered, so 40 lines is what makes the tie-break load-bearing rather
    /// than decorative.</para>
    /// </summary>
    [Fact]
    public void Two_lines_of_one_order_for_the_same_item_are_retired_in_line_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 500.875m);
        // FORTY lines of the SAME item on ONE order: same voucher id, same date, same number — only the line
        // index separates them, and 40 is past the sort's insertion-sort threshold (see the doc above).
        var lines = new OrderLine[40];
        for (var i = 0; i < lines.Length; i++) lines[i] = Line(k, item, 10.125m);
        var so = OrderOf(k, VoucherBaseType.SalesOrder, D1, ashok, lines);
        // Exactly TEN lines' worth: 10 × 10.125 = 101.250, so the walk must retire lines 0..9 and no others.
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D2, 101.250m, partyId: ashok);

        var map = OrderFulfilment.Build(k.Company, D4);
        for (var i = 0; i < 10; i++)
            Assert.Equal(10.125m, map[(so.Id, i)]);    // the FIRST ten lines, in line order
        for (var i = 10; i < 40; i++)
            Assert.Equal(0m, map[(so.Id, i)]);         // and not one of the other thirty

        // Components before the total: 30 lines × 10.125 still open.
        Assert.Equal(303.750m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
        var rows = InventoryRegisters.BuildOrders(k.Company, FyStart, D4).Where(r => r.Number == so.Number).ToList();
        Assert.Equal(40, rows.Count);
        Assert.Equal(10, rows.Count(r => r.OutstandingQuantity == 0m && r.FulfilledQuantity == 10.125m));
        Assert.Equal(30, rows.Count(r => r.OutstandingQuantity == 10.125m && r.FulfilledQuantity == 0m));
    }

    /// <summary>
    /// 🔴 <b>WHAT PER-LINE ATTRIBUTION CAN AND CANNOT MOVE — settled by construction, because a review lens
    /// called the looser version of this claim false and a guarantee of this shape was relayed to the user.</b>
    /// Two books identical in every respect except the ORDER of two same-item lines on one order voucher. The
    /// per-line figures SWAP; the item-level total <see cref="OrderFulfilment.OutstandingByItem"/> hands WF-7 is
    /// <b>identical</b>.
    /// <para><b>Why, and the exact scope of the invariance</b> (the reasoning is stated on
    /// <see cref="OrderFulfilment"/> and this fixture is the check on it): the FIFO walk caps every allocation at
    /// the line's own remainder, so <c>done ≤ Quantity</c> on every line and the zero-floor in
    /// <c>OutstandingByItem</c> never fires — the total is exactly <c>Σ ordered − Σ absorbed</c>. Lines are sorted
    /// by DATE, so the line index only ever decides between lines of the SAME date, and a movement that can reach
    /// one of those can reach all of them; absorption is then bounded by the movement quantity and the cohort's
    /// total remainder, neither of which the tie-break touches.
    /// <para>🔴 <b>This is NOT a claim that the item total is attribution-proof in general.</b> It moves with the
    /// COHORT (a movement credited to the wrong party changes it — pinned by
    /// <see cref="A_delivery_note_naming_a_customer_retires_that_customers_order_and_not_anothers"/>) and it moves
    /// with the DATE ordering (a movement barred from a later line drops its surplus — pinned by
    /// <see cref="A_movement_dated_before_the_order_does_not_retire_it"/>). Only the same-date line tie-break is
    /// total-neutral.</para></para>
    /// </summary>
    [Fact]
    public void Swapping_two_same_day_order_lines_moves_the_per_line_figures_but_never_the_item_total()
    {
        static (decimal Line0, decimal Line1, decimal ItemTotal) Run(bool smallLineFirst)
        {
            var k = NewKit();
            var item = k.Masters.CreateStockItem("Widget", k.GroupId, k.UnitId).Id;
            var ashok = new Domain.Ledger(Guid.NewGuid(), "Ashok Traders",
                k.Company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
            k.Company.AddLedger(ashok);
            k.Masters.AddOpeningBalance(item, k.GodownId, 180.875m, Money.FromRupees(10.37m));

            var lines = smallLineFirst
                ? new[]
                {
                    new OrderLine(item, k.GodownId, 40.375m, Money.FromRupees(47.33m)),
                    new OrderLine(item, k.GodownId, 60.125m, Money.FromRupees(47.33m)),
                }
                : new[]
                {
                    new OrderLine(item, k.GodownId, 60.125m, Money.FromRupees(47.33m)),
                    new OrderLine(item, k.GodownId, 40.375m, Money.FromRupees(47.33m)),
                };
            var so = InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.SalesOrder), D1,
                lines, partyId: ashok.Id);
            k.Posting.Post(so);
            k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D2,
                new[] { new InventoryAllocation(item, k.GodownId, 55.500m, StockDirection.Outward,
                    Money.FromRupees(10.37m)) },
                partyId: ashok.Id));

            var map = OrderFulfilment.Build(k.Company, D4);
            return (map[(so.Id, 0)], map[(so.Id, 1)],
                OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
        }

        var small = Run(smallLineFirst: true);
        var large = Run(smallLineFirst: false);

        // The COMPONENTS really do move — otherwise this fixture would be proving nothing.
        Assert.Equal(40.375m, small.Line0);
        Assert.Equal(15.125m, small.Line1);
        Assert.Equal(55.500m, large.Line0);   // the 60.125 line, now first, swallows the whole delivery
        Assert.Equal(0m, large.Line1);
        Assert.NotEqual(small.Line0, large.Line0);

        // The TOTAL does not: 100.500 ordered − 55.500 delivered, under either arrangement.
        Assert.Equal(45.000m, small.ItemTotal);
        Assert.Equal(45.000m, large.ItemTotal);
        Assert.Equal(small.ItemTotal, large.ItemTotal);
    }

    /// <summary>
    /// A movement stated in a COMPOUND unit is converted to the item's base unit before it retires anything.
    /// A Box of 12 Nos: 4 Boxes delivered is 48 Nos, retiring 48 of a 60.125 Nos order and leaving 12.125.
    /// <para>🔴 This doc used to say the conversion reused "<c>the register's own QuantityInBase</c>, so the two
    /// cannot drift". <b>That named the wrong mechanism</b>: <c>OrderFulfilment</c> never calls
    /// <c>InventoryRegisters.QuantityInBase</c>. It normalises through <c>InventoryMovements.Between</c>, which
    /// owns its own base-unit conversion for the pure-stock AND the item-invoice path — which is the stronger
    /// guarantee, because that is the same enumeration the on-hand and valuation engines read.</para>
    /// </summary>
    [Fact]
    public void A_movement_in_a_compound_unit_is_converted_to_the_items_base_unit()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var boxUnit = k.Masters.CreateSimpleUnit("Box", "Box");
        var box = k.Masters.CreateCompoundUnit("Box-12", "Box of 12", boxUnit.Id, k.UnitId, 12).Id;
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D3,
            new[] { new InventoryAllocation(item, k.GodownId, 4m, StockDirection.Outward, Money.FromRupees(10.37m), unitId: box) },
            partyId: ashok));

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(48m, row.FulfilledQuantity);       // 4 Boxes x 12 — NOT 4
        Assert.Equal(12.125m, row.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 <b>THE LAST TIE-BREAK, and the one that makes the ordering TOTAL.</b>
    /// <see cref="List{T}.Sort(Comparison{T})"/> is <b>unstable</b>, so <c>CompareOpenLines</c> is only
    /// deterministic if no two open lines ever compare EQUAL. Date is pinned by
    /// <see cref="Fulfilment_is_attributed_to_the_earliest_open_order_first"/>, number by
    /// <see cref="Two_orders_raised_on_the_same_day_are_retired_in_voucher_number_order"/> and line index by
    /// <see cref="Two_lines_of_one_order_for_the_same_item_are_retired_in_line_order"/>. This is the fourth and
    /// final one: the voucher id.
    /// <para><b>Why it needs a second voucher TYPE.</b> Voucher numbers are unique per type —
    /// <c>InventoryPostingService</c> auto-numbers <c>max+1</c> and its Prevent-Duplicate guard scans only
    /// <c>other.TypeId == voucher.TypeId</c> — so two orders on ONE type can never share a number, and the id
    /// tie-break is unreachable through them. Two types of the same BASE type is the reachable shape (an operator
    /// adding "Export Sales Order" alongside the predefined one), and it is the only way to drive the comparator
    /// past date, number and line index.</para>
    /// <para>Which order wins is DERIVED from the ids rather than hard-coded, because <see cref="Guid.NewGuid"/>
    /// is not ordered — hard-coding one would make the fixture pass or fail by chance.</para>
    /// <para>🔴 <b>WHY TWELVE INDEPENDENT BOOKS — measured, not guessed.</b> A single pair bites the mutation
    /// only about half the time: with the id clause dropped the two lines compare EQUAL and insertion order (the
    /// order they were posted in) decides, so the fixture still passes whenever <c>a.Id</c> happened to sort
    /// first anyway. Measured on a one-pair version: dropping the clause gave <b>4 red out of 5 runs</b> — a
    /// coin-flip lock, which is no lock. Twelve independently-seeded pairs make an all-in-insertion-order run
    /// about 1 in 4096, so the mutation is caught essentially always while correct code passes always. The
    /// flakiness of the one-pair form IS the defect being pinned: without the clause the answer is decided by
    /// Guid chance.</para>
    /// </summary>
    [Fact]
    public void Two_orders_alike_in_date_and_number_are_retired_in_a_deterministic_total_order()
    {
        for (var book = 0; book < 12; book++)
        {
            var k = NewKit();
            var item = Item(k, "Widget");
            var ashok = Party(k, "Ashok Traders");
            Opening(k, item, 180.875m);

            var exportType = new VoucherType(Guid.NewGuid(), "Export Sales Order", VoucherBaseType.SalesOrder);
            k.Company.AddVoucherType(exportType);

            var a = InventoryVoucher.Order(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.SalesOrder), D1,
                new[] { new OrderLine(item, k.GodownId, 47.331m, Money.FromRupees(47.33m)) },
                number: 1, partyId: ashok);
            var b = InventoryVoucher.Order(Guid.NewGuid(), exportType.Id, D1,
                new[] { new OrderLine(item, k.GodownId, 60.125m, Money.FromRupees(47.33m)) },
                number: 1, partyId: ashok);
            k.Posting.Post(a);
            k.Posting.Post(b);
            // The fixture is only meaningful while date and number really do tie — assert that, don't assume it.
            Assert.Equal(a.Number, b.Number);
            Assert.Equal(a.Date, b.Date);

            // 30.125 is smaller than EITHER order, so exactly one line absorbs all of it and the other gets
            // nothing — which makes the winner observable rather than masked by a spill.
            ShellNote(k, VoucherBaseType.DeliveryNote, item, D2, 30.125m, partyId: ashok);

            var first = a.Id.CompareTo(b.Id) < 0 ? a : b;    // what CompareOpenLines' final clause says
            var second = ReferenceEquals(first, a) ? b : a;

            var map = OrderFulfilment.Build(k.Company, D4);
            Assert.Equal(30.125m, map[(first.Id, 0)]);
            Assert.Equal(0m, map[(second.Id, 0)]);

            // Determinism: the same book must answer the same way twice, not whichever pair a sort produced.
            var again = OrderFulfilment.Build(k.Company, D4);
            Assert.Equal(map[(first.Id, 0)], again[(first.Id, 0)]);
            Assert.Equal(map[(second.Id, 0)], again[(second.Id, 0)]);
            // 107.456 ordered − 30.125 delivered, and the total is the same whichever id won.
            Assert.Equal(77.331m,
                OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
        }
    }

    // ============================================ 🔴 AffectsStock — which doors it gates, and which it does NOT

    /// <summary>
    /// 🔴 <b>AffectsStock DOES NOT GATE THE ITEM-INVOICE DOOR, and this fixture is what stops that being written
    /// down wrongly again.</b> A Sales voucher type with <see cref="VoucherType.AffectsStock"/> switched <b>off</b>
    /// still moves stock when it carries item lines — <c>ItemInvoiceStock.Counts</c> keys item-invoice mode on the
    /// PRESENCE of those lines, explicitly "not the type's <c>AffectsStock</c> flag" — and it therefore still
    /// retires the sales order.
    /// <para><b>The invariant that actually matters is asserted alongside it:</b> fulfilment and the on-hand
    /// engine give the SAME answer. Closing stock drops by the invoiced quantity and the order is retired by the
    /// same quantity. A "persisted flag the compute ignores" divergence would be one engine seeing the goods and
    /// the other not; here both see them, which is the correct outcome and the reason
    /// <see cref="OrderFulfilment"/> reads <c>InventoryMovements</c> rather than re-deriving the doors.</para>
    /// <para><b>Reachable, not theoretical.</b> <c>AffectsStock</c> is a settable property that round-trips
    /// through <c>CanonicalMapper</c>/<c>CanonicalXml</c> and is replayed verbatim by <c>ImportPlan</c>
    /// (<c>affectsStock: vt.AffectsStock</c>), so an imported book can carry exactly this type.</para>
    /// </summary>
    [Fact]
    public void An_item_invoice_retires_an_order_even_when_its_type_has_affects_stock_off()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        k.Company.FindVoucherType(b.SalesTypeId)!.AffectsStock = false;

        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 47.331m, partyId: ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(47.331m, row.FulfilledQuantity);
        Assert.Equal(12.794m, row.OutstandingQuantity);
        // The on-hand engine agrees to the same quantity — the two cannot diverge, which is the point.
        Assert.Equal(133.544m,
            StockSummary.Build(k.Company, D4).Rows.Single(r => r.ItemName == "Widget").ClosingQuantity);
    }

    /// <summary>
    /// The PURE-STOCK twin, and the arm where the flag does bite: a Delivery Note type with
    /// <see cref="VoucherType.AffectsStock"/> off moves no stock (<c>InventoryMovements.CountsPureStock</c>
    /// mirrors <c>InventoryLedger</c> and requires the flag) and therefore retires nothing — and, again, the two
    /// engines agree: closing stock is untouched by the same voucher that retired nothing.
    /// <para>So the honest rule is per-DOOR, not global: the flag gates the pure-stock doors and is ignored on the
    /// item-invoice doors. That asymmetry is not this slice's to remove — it is <c>ItemInvoiceStock</c>'s stated
    /// contract and the on-hand engine already lives by it — but it must be written down where a reader of
    /// <see cref="OrderFulfilment"/> will meet it.</para>
    /// </summary>
    [Fact]
    public void A_delivery_note_whose_type_has_affects_stock_off_retires_nothing_and_moves_no_stock()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        k.Company.FindVoucherType(TypeId(k.Company, VoucherBaseType.DeliveryNote))!.AffectsStock = false;

        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
        // Nothing moved, so closing stock is the opening balance untouched.
        Assert.Equal(180.875m,
            StockSummary.Build(k.Company, D4).Rows.Single(r => r.ItemName == "Widget").ClosingQuantity);
    }

    // ================================================================ the FIFO cursor

    /// <summary>
    /// 🔴 The FIFO walk carries a per-cohort cursor so it does not rescan exhausted lines (the walk is
    /// O(movements × order-lines), and one supplier's fast-moving item is a single long cohort). The cursor may
    /// only pass a line that is EXHAUSTED — never one left open by the <b>date bound</b>, which is now the only
    /// non-exhausting reason a walk stops early: since the cohort key carries the party, a wrong-party line is
    /// not in the list at all rather than skipped inside it.
    /// <para><b>The mutation this bites, stated so the claim can be checked.</b> Move the cursor forward as the
    /// walk proceeds — <c>cohort.Cursor = i + 1;</c> as the first statement of the <c>for</c> body in
    /// <c>OrderFulfilment.Accumulate</c>. Movement 1 (D2) exhausts o1, then <i>examines</i> o2 and breaks on the
    /// date bound with o2 still fully open; the greedy cursor has already stepped past it, so movement 2 (D3)
    /// skips to o3 and o2 is never retired. Verified by making that edit and running this test:
    /// <c>Assert.Equal() Failure: Values differ / Expected: 60.125 / Actual: 0</c> on o2's
    /// <c>FulfilledQuantity</c>. The edit was reverted.</para>
    /// </summary>
    [Fact]
    public void The_fifo_cursor_passes_exhausted_lines_only_never_a_line_left_open_by_the_date_bound()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var o1 = Order(k, VoucherBaseType.SalesOrder, item, D1, 40.375m, ashok);
        var o2 = Order(k, VoucherBaseType.SalesOrder, item, D3, 60.125m, ashok);   // examined, then date-bound
        var o3 = Order(k, VoucherBaseType.SalesOrder, item, D4, 47.330m, ashok);
        // 55.500 exhausts o1 (40.375) and carries 15.125 into a walk that breaks at o2 — not yet placed on D2.
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D2, 55.500m, partyId: ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);   // must still reach o2
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D4, 47.330m, partyId: ashok);

        Assert.Equal(40.375m, Row(k.Company, D5, o1.Number).FulfilledQuantity);
        Assert.Equal(0m, Row(k.Company, D5, o1.Number).OutstandingQuantity);
        Assert.Equal(60.125m, Row(k.Company, D5, o2.Number).FulfilledQuantity);
        Assert.Equal(0m, Row(k.Company, D5, o2.Number).OutstandingQuantity);
        Assert.Equal(47.330m, Row(k.Company, D5, o3.Number).FulfilledQuantity);
        Assert.Equal(0m, Row(k.Company, D5, o3.Number).OutstandingQuantity);
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D5));
    }

    /// <summary>Figure-neutrality of the cursor at scale — 90 purchase orders and 60 receipts in ONE
    /// (supplier, item) cohort, the shape a distributor with one fast-moving line actually has. 60 orders retire
    /// in full and 30 stay open: 30 × 47.330 = 1419.900.</summary>
    [Fact]
    public void A_large_single_item_cohort_reports_the_same_figures_line_by_line()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        for (var i = 0; i < 90; i++) Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        for (var i = 0; i < 60; i++) ShellNote(k, VoucherBaseType.ReceiptNote, item, D2, 47.330m, partyId: bimal);

        var rows = InventoryRegisters.BuildOrders(k.Company, FyStart, D4);
        Assert.Equal(90, rows.Count);
        Assert.Equal(60, rows.Count(r => r.OutstandingQuantity == 0m && r.FulfilledQuantity == 47.330m));
        Assert.Equal(30, rows.Count(r => r.OutstandingQuantity == 47.330m && r.FulfilledQuantity == 0m));
        Assert.Equal(1419.900m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D4));
    }

    // ================================================================ the WF-7 aggregate (DD-5's fix)

    /// <summary>
    /// The item-level aggregate WF-7 nets into Nett Available, on the very fixture that used to pin DD-5's WRONG
    /// figures (the pre-S2 <c>Reorder_status_still_counts_fully_fulfilled_orders_as_outstanding_DD5_documented_defect</c>,
    /// now inverted into <c>Reorder_status_retires_a_fulfilled_order_in_both_directions</c>). A 60.125 sales
    /// order delivered in full and a 40.375 purchase order received in full are both RETIRED: outstanding 0 on
    /// each side, where the raw ordered quantity WF-7 used to sum answered 60.125 and 40.375.
    /// </summary>
    [Fact]
    public void Outstanding_by_item_retires_the_fully_fulfilled_orders_DD5_would_still_count()
    {
        var k = NewKit();
        var sold = Item(k, "Sold");
        var bought = Item(k, "Bought");
        var ashok = Party(k, "Ashok Traders");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        Opening(k, sold, 180.875m);
        Order(k, VoucherBaseType.SalesOrder, sold, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, sold, D3, 60.125m, partyId: ashok);    // fully delivered
        Order(k, VoucherBaseType.PurchaseOrder, bought, D2, 40.375m, bimal);
        ShellNote(k, VoucherBaseType.ReceiptNote, bought, D3, 40.375m, partyId: bimal);   // fully received

        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, sold, VoucherBaseType.SalesOrder, D4));
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, bought, VoucherBaseType.PurchaseOrder, D4));
        // The raw order book these replaced answered 60.125 / 40.375. WF-7/S2 now consumes the figures above, so
        // the corresponding Reorder Status row is pinned to the CORRECT quantities by
        // Reorder_status_retires_a_fulfilled_order_in_both_directions.
        Assert.Equal(120.750m, StockSummary.Build(k.Company, D4).Rows.Single(r => r.ItemName == "Sold").ClosingQuantity);
    }

    /// <summary>
    /// An over-delivery's surplus is DROPPED, not leaked onto another party's order line for the same item.
    /// 75.500 delivered to Ashok against his 60.125 order leaves a 15.375 surplus, and Chandra's 47.330 order —
    /// raised the SAME day, so the date bound does not stop it — is untouched. The only thing between the
    /// surplus and that order is the party half of the cohort key, which is what this test bites: collapsing the
    /// key back to the item alone was tried, and Chandra's row reads <c>Expected: 0 / Actual: 15.375</c> — goods
    /// he never received. The edit was reverted.
    /// <para>🔴 <b>This test does NOT pin the negative clamp, and its predecessor's claim that it did was
    /// arithmetically impossible.</b> The old comment said "an unclamped sum would answer 31.955", which needs a
    /// per-line <c>done</c> of 75.500 against a 60.125 line; the FIFO walk caps every allocation at the line's
    /// own remaining quantity, so a per-line negative cannot occur at all. The disclosure now lives on the guard
    /// itself in <see cref="OrderFulfilment.OutstandingByItem"/>, where a future reviewer will read it.</para>
    /// <para>The assertions are per-order COMPONENTS as well as the item total: a total alone cannot tell a
    /// wrongly-attributed pair from a correct one.</para>
    /// </summary>
    [Fact]
    public void Outstanding_by_item_drops_an_over_deliverys_surplus_instead_of_leaking_it_to_another_party()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        var chandra = Party(k, "Chandra Stores");
        Opening(k, item, 180.875m);
        var ashoks = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok);
        var chandras = Order(k, VoucherBaseType.SalesOrder, item, D1, 47.330m, chandra);   // SAME day
        Movement(k, VoucherBaseType.DeliveryNote, item, D2, 75.500m, ashok);               // over by 15.375

        var a = Row(k.Company, D4, ashoks.Number);
        Assert.Equal(60.125m, a.FulfilledQuantity);
        Assert.Equal(0m, a.OutstandingQuantity);

        var c = Row(k.Company, D4, chandras.Number);
        Assert.Equal(0m, c.FulfilledQuantity);       // the 15.375 surplus did NOT leak here
        Assert.Equal(47.330m, c.OutstandingQuantity);

        Assert.Equal(47.330m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>An item with nothing outstanding is absent from the dictionary, and a partially fulfilled order
    /// reports exactly its remainder.</summary>
    [Fact]
    public void Outstanding_by_item_omits_items_with_nothing_outstanding()
    {
        var k = NewKit();
        var retired = Item(k, "Retired");
        var open = Item(k, "Open");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        Order(k, VoucherBaseType.PurchaseOrder, retired, D1, 47.330m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, retired, D2, 47.330m, bimal);
        Order(k, VoucherBaseType.PurchaseOrder, open, D1, 90.625m, bimal);
        Movement(k, VoucherBaseType.ReceiptNote, open, D2, 30.125m, bimal);

        var totals = OrderFulfilment.OutstandingByItem(k.Company, VoucherBaseType.PurchaseOrder, D4);
        Assert.False(totals.ContainsKey(retired));
        Assert.Equal(60.500m, totals[open]);
    }

    // ============================================ the cohort is (PartyId, StockItemId) — the WF-8 root-cause fix

    /// <summary>
    /// 🔴 <b>THE DRIVING CASE.</b> A delivery note naming Ashok retires <b>Ashok's</b> open order and leaves
    /// Chandra's alone, and the note is built the way the shipped shell now builds one — the picker is on a note
    /// screen and <c>BuildMovementNote</c> passes what it captures.
    /// <para>Chandra's order is deliberately the <b>EARLIER</b> of the two, so a cohort keyed on the item alone
    /// hands the goods to Chandra by FIFO. Party is half the cohort KEY, not a tie-break. Verified by collapsing
    /// the key to <c>new CohortKey(null, …)</c> on both sides: Chandra's 47.330 order absorbs most of the
    /// delivery and Ashok's row reads <c>Expected: 60.125 / Actual: 12.795</c>. The edit was reverted.</para>
    /// <para>Components, not the total: Σ outstanding is 47.330 under either attribution, so a total-only
    /// assertion would pass on the wrong pair.</para>
    /// </summary>
    [Fact]
    public void A_delivery_note_naming_a_customer_retires_that_customers_order_and_not_anothers()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        var chandra = Party(k, "Chandra Stores");
        Opening(k, item, 180.875m);
        var chandras = Order(k, VoucherBaseType.SalesOrder, item, D1, 47.330m, chandra);   // EARLIER
        var ashoks = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);

        var a = Row(k.Company, D4, ashoks.Number);
        Assert.Equal(60.125m, a.FulfilledQuantity);
        Assert.Equal(0m, a.OutstandingQuantity);

        var c = Row(k.Company, D4, chandras.Number);
        Assert.Equal(0m, c.FulfilledQuantity);
        Assert.Equal(47.330m, c.OutstandingQuantity);

        Assert.Equal(47.330m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>
    /// 🔴 <b>The blank-party rule, stated and pinned.</b> A note whose party picker was left on "(none)" retires
    /// only orders that were themselves raised without a party. It does <b>not</b> retire Ashok's order: the
    /// product does not know whose goods left the godown, and guessing is exactly the invented rule this fix
    /// deleted.
    /// <para>The direction of the residual error is stated rather than left to be discovered: the order stays
    /// open, which is the <b>pre-WF-8</b> figure for that one order — never worse than what shipped before, and
    /// now fixable by the operator, who has a control to name the party.</para>
    /// </summary>
    [Fact]
    public void A_note_left_blank_does_not_retire_a_party_named_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m);   // picker left on "(none)"

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
        Assert.Equal(60.125m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>
    /// 🔴 The other asymmetric arm, and it must fail too: a note naming Bimal does not retire an order raised
    /// with the picker blank. Both halves of the key must match, so "(none)" is a cohort of its own on the ORDER
    /// side as well — otherwise the wildcard survives inverted and a supplier's receipt silently closes an order
    /// that was never his.
    /// </summary>
    [Fact]
    public void A_note_naming_a_supplier_does_not_retire_an_order_left_blank()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, partyId: null);
        ShellNote(k, VoucherBaseType.ReceiptNote, item, D2, 47.330m, partyId: bimal);

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(47.330m, row.OutstandingQuantity);
        Assert.Equal(47.330m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D4));
    }

    // ==================================== 🔴 THE THIRD AND FOURTH DOORS — item invoice / POS (Phase 10.10 / WF-8)

    /// <summary>
    /// 🔴 <b>THE DRIVING CASE OF THIS STEP.</b> A shop that <b>invoices its goods directly</b> — the ordinary
    /// retail and trading path — retires its sales order. Before this step
    /// <c>OrderFulfilment.Accumulate</c> pinned the fulfilling voucher to the <b>Delivery Note</b> base type and
    /// therefore saw exactly one of the three outward doors: WF-8 worked only for businesses that raise a
    /// separate Delivery Note, and every trading book — the majority case — still reported the whole ordered
    /// quantity outstanding for ever, which is byte-identical to the pre-WF-8 defect WF-8 exists to delete.
    /// <para><b>R7 — this door is CORPUS-SOURCED, not inferred.</b> TallyPrime's own order cycle shows the
    /// <b>Sales Voucher (F8)</b> carrying "<c>Tracking No : 1  Order No : 1</c>" against Sales Order 1
    /// [CORPUS-TALLY-BOOK(719244897) p.18], and its purchase twin shows the <b>Purchase Voucher (F9)</b> doing
    /// the same against Purchase Order 1 [p.15]. An invoice IS a fulfilment document there.</para>
    /// </summary>
    [Fact]
    public void An_item_invoice_sale_retires_the_matching_sales_order()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 60.125m, partyId: ashok);   // NO delivery note at all

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(60.125m, row.OrderedQuantity);
        Assert.Equal(60.125m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);
        // The aggregate WF-7 nets into Nett Available must agree, or the shortfall arithmetic still reads the
        // stale ordered quantity for every trading book.
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>
    /// The inward twin, and the answer to "does a purchase INVOICE retire a purchase order, or only a Receipt
    /// Note?" — <b>it does</b>, partly here: 47.331 ordered less 19.875 invoiced leaves 27.456.
    /// <para><b>Why YES, stated so the choice is reviewable.</b> (a) A Purchase item-invoice moves stock inward
    /// through <c>ItemInvoiceStock</c> exactly as a Receipt Note does — the on-hand engine, the valuation engine
    /// and the negative-stock detector all already count it, so treating it as a non-event for orders alone would
    /// make fulfilment the one engine that disagrees about what arrived. (b) The corpus shows the Purchase
    /// Voucher (F9) carrying "<c>Tracking No : 1  Order No : 1</c>" against Purchase Order 1
    /// [CORPUS-TALLY-BOOK(719244897) p.15], and the GST notes describe raising the purchase invoice "against
    /// RN/OO1" [CORPUS-GST-NOTES(703679456) p.24]. (c) A shop that books the supplier's bill without a separate
    /// GRN has genuinely received the goods; leaving the PO open would over-state Purc Orders Pending and, after
    /// WF-7, over-state Nett Available — telling the buyer stock is on its way when it is already on the shelf.
    /// An asymmetry between the two arms would be a newly invented rule, which is what this phase exists to
    /// delete.</para>
    /// </summary>
    [Fact]
    public void A_purchase_invoice_retires_the_matching_purchase_order()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.331m, bimal);
        ItemInvoice(k, b, VoucherBaseType.Purchase, item, D2, 19.875m, partyId: bimal);   // no receipt note

        var row = Row(k.Company, D4, po.Number);
        Assert.Equal(47.331m, row.OrderedQuantity);
        Assert.Equal(19.875m, row.FulfilledQuantity);
        Assert.Equal(27.456m, row.OutstandingQuantity);
        Assert.Equal(27.456m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D4));
    }

    /// <summary>
    /// 🔴 <b>THE DOUBLE-COUNT LOCK — the highest-risk assertion in this step.</b> A book that raises BOTH a
    /// Delivery Note and an item invoice for the same goods retires the order <b>exactly once</b>: Fulfilled
    /// stops at 60.125, not 120.250, and Outstanding is 0, never negative. A double-retire under-states
    /// shortfall, which is the direction that leaves a customer unserved.
    ///
    /// <para><b>How it is prevented, exactly.</b> The FIFO walk caps every allocation at the order line's OWN
    /// remaining quantity (<c>take = Math.Min(open.Remaining, remaining)</c>), so once a line reaches zero no
    /// later movement — from any door — can take against it again. The second document's quantity finds no
    /// capacity on that line and is dropped, which is the same clamp an over-delivery meets. This is a
    /// <b>per-order-line</b> guarantee and it is what makes opening two doors safe: the doors add candidates, the
    /// cap bounds the result. Removing the <c>Math.Min</c> is what this test bites.</para>
    ///
    /// <para>🔴 <b>WHICH HALF ACTUALLY BITES — measured, not assumed.</b> The first scenario below (note and
    /// invoice for the SAME quantity) is <b>triple-guarded and bites nothing on its own</b>: with the second
    /// document exactly equal to the order line, the cursor has already passed the exhausted line, the
    /// <c>open.Remaining &lt;= 0m</c> skip would stop it anyway, and even reaching the arithmetic
    /// <c>Math.Min(0, 60.125)</c> is 0. Replacing the cap with <c>take = remaining</c> was tried and this test
    /// still PASSED — recorded here rather than quietly dropped, because its predecessor in this suite
    /// (<see cref="A_delivery_note_never_retires_a_purchase_order"/>) once claimed a bite it did not have.
    /// The SECOND scenario is the one with teeth: a <b>partial</b> delivery note followed by a <b>full</b>
    /// invoice makes the second document exceed the line's remainder, so only the cap can stop it. Under
    /// <c>take = remaining</c> that scenario reads <c>Expected: 60.125 / Actual: 100.500</c>. It is also the
    /// likelier real shape — the corpus's own cycle bills 8 against a 10 note [CORPUS-TALLY-BOOK(719244897)
    /// pp.14-15].</para>
    /// <para>🔴 <b>What it deliberately does NOT claim.</b> It does not claim the engine de-duplicated the
    /// STOCK. It did not, and the assertion below says so out loud: on-hand drops <b>twice</b>, 180.875 − 60.125
    /// − 60.125 = 60.625. In TallyPrime the second document would not move stock either, because the invoice is
    /// raised against the note's <b>Tracking Number</b> and "on the Basis of tracking DN/OO1, it takes the items
    /// with Quantity, Rate &amp; Amount automatically" [CORPUS-GST-NOTES(703679456) p.31; the purchase twin at
    /// p.24]. <b>The Tracking Number IS TallyPrime's anti-double-count mechanism, and this product has no such
    /// field on any master, line or voucher.</b> So a book that keys both documents has already double-moved its
    /// stock before fulfilment is consulted; fulfilment mirrors that book rather than inventing a link the
    /// product cannot store. Deduplicating here by guessing (same party + item + quantity ⇒ one shipment) would
    /// silently swallow a genuine second shipment, which is a worse error in the same direction.</para>
    /// </summary>
    [Fact]
    public void A_delivery_note_and_an_invoice_for_the_same_goods_retire_the_order_exactly_once()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        // 🔴 The note is dated BEFORE the invoice deliberately. Movements are ordered by (date, number, id), and
        // a note and an invoice both numbered 1 on the SAME date fall through to a Guid tie-break — i.e. to
        // chance. Ship-then-bill is also the real sequence and the corpus's own (Delivery Note 01/07, sales
        // invoice 31/07 against it) [CORPUS-GST-NOTES(703679456) p.31].
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D4, 60.125m, partyId: ashok);   // the SAME goods, billed

        var row = Row(k.Company, D5, so.Number);
        Assert.Equal(60.125m, row.FulfilledQuantity);        // NOT 120.250
        Assert.Equal(0m, row.OutstandingQuantity);           // NOT −60.125
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D5));

        // 🔴 The stock engine's own verdict, asserted so nobody reads "retired once" as "stock deduplicated":
        // the book really did move the goods twice, and closing stock says so.
        Assert.Equal(60.625m, StockSummary.Build(k.Company, D5).Rows.Single(r => r.ItemName == "Widget").ClosingQuantity);

        // 🔴 THE BITING HALF — a PARTIAL note then a FULL invoice, on a separate book so the two scenarios
        // cannot mask one another. 40.375 shipped leaves 19.750 open; the 60.125 invoice exceeds that remainder,
        // so only the per-line cap keeps Fulfilled at the ordered 60.125 instead of 100.500.
        var k2 = NewKit();
        var b2 = NewBooks(k2);
        var item2 = Item(k2, "Widget");
        var ashok2 = Party(k2, "Ashok Traders");
        Opening(k2, item2, 180.875m);
        var so2 = Order(k2, VoucherBaseType.SalesOrder, item2, D2, 60.125m, ashok2);
        ShellNote(k2, VoucherBaseType.DeliveryNote, item2, D3, 40.375m, partyId: ashok2);   // partial
        ItemInvoice(k2, b2, VoucherBaseType.Sales, item2, D4, 60.125m, partyId: ashok2);    // billed in FULL

        var row2 = Row(k2.Company, D5, so2.Number);
        Assert.Equal(60.125m, row2.FulfilledQuantity);       // NOT 100.500
        Assert.Equal(0m, row2.OutstandingQuantity);
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k2.Company, item2, VoucherBaseType.SalesOrder, D5));
    }

    /// <summary>
    /// The companion to the lock above, pinning what happens to the <b>surplus</b> when a second open order is
    /// standing behind the first: it spills by FIFO, exactly as any over-delivery does
    /// (<see cref="Fulfilment_is_attributed_to_the_earliest_open_order_first"/>), and the leftover 12.794 is
    /// dropped for want of a line to carry it.
    /// <para><b>Why the ENGINE is not guessing.</b> Without a tracking number a duplicate document and a genuine
    /// second shipment are indistinguishable, and a heuristic ("same party + item + quantity ⇒ one shipment")
    /// would silently swallow real second shipments. So the walk mirrors the stock engine, which really did move
    /// the goods twice.</para>
    /// <para>🔴 <b>BUT THIS IS A STATED COST, NOT A CORRECT ANSWER — read the name again.</b> An earlier revision
    /// defended this fixture with an error-direction proof about <c>NettAvailable</c>. Two things were wrong with
    /// that defence and both are corrected on <see cref="OrderFulfilment"/>: (a) the proof was derived for the
    /// OUTWARD arm only and <b>inverts on the purchase arm</b>, where the duplicate pushes closing stock HIGH and
    /// under-states shortfall — see
    /// <see cref="A_duplicate_purchase_document_pushes_nett_available_high_and_under_states_shortfall"/>; and
    /// (b) even where it holds it is an argument about the item AGGREGATE and says <b>nothing</b> about the
    /// per-ORDER column asserted below. The second order here has been shipped <b>nothing</b> and prints
    /// Fulfilled 47.331 / Outstanding 0. Before WF-8 that row read Outstanding 47.331 — the truth — so this
    /// specific row is strictly worse than the code being replaced, and the purchase-arm twin
    /// (<see cref="A_duplicate_purchase_invoice_retires_a_second_open_order_nothing_was_received_against"/>)
    /// carries the same cost. It is pinned here so the residual is <b>visible rather than green</b>, and it is
    /// owed to the user as a go/no-go.</para>
    /// <para>Components, never the total: Σ outstanding is 0 under several different attributions, so only the
    /// per-order rows can tell a correct spill from a wrong one.</para>
    /// </summary>
    [Fact]
    public void A_second_document_for_the_same_goods_spills_by_fifo_exactly_as_an_over_delivery_does()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var first = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok);
        var second = Order(k, VoucherBaseType.SalesOrder, item, D2, 47.331m, ashok);
        // Ship, then bill — dated apart so the walk order is the book's, not a Guid tie-break's.
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D3, 60.125m, partyId: ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D4, 60.125m, partyId: ashok);   // duplicate document

        var a = Row(k.Company, D5, first.Number);
        Assert.Equal(60.125m, a.FulfilledQuantity);      // retired ONCE by the note; the invoice cannot re-take
        Assert.Equal(0m, a.OutstandingQuantity);

        var c = Row(k.Company, D5, second.Number);
        Assert.Equal(47.331m, c.FulfilledQuantity);      // the spill fills it; 12.794 is dropped
        Assert.Equal(0m, c.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 <b>The door set is a CLOSED allow-list, not "any outward movement".</b> A <b>Rejection Out</b> is goods
    /// going BACK TO A SUPPLIER: it carries an outward line, it names a party, and it is dated after the order —
    /// everything a Delivery Note has except the door. Nothing was sold, so it must retire no sales order.
    /// <para>This is the outward twin of <see cref="A_rejection_in_does_not_retire_a_purchase_order"/> and it
    /// BITES the new door set directly: add <c>Source.RejectionOut</c> to
    /// <c>OrderFulfilment.IsFulfillingDoor</c>'s sales arm and this goes red. Verified by making that edit;
    /// see the restoration note in the slice report.</para>
    /// <para><b>Rejections are neither retired nor UN-retired by this slice, and that is a stated scope
    /// boundary with a known fidelity gap.</b> TallyPrime does thread them into the chain — its Rejection Out
    /// and Rejection In both carry "<c>Tracking No : 1  Order No : 1</c>"
    /// [CORPUS-TALLY-BOOK(719244897) pp.14, 18] — so a return there reduces the delivered quantity and re-opens
    /// the commitment. Reproducing that needs (a) a NEGATIVE fulfilment arm, which the FIFO machine has no
    /// representation for and whose absence both clamp comments currently rely on, and (b) an attribution rule
    /// for WHICH movement a rejection reverses, which without a tracking number is a second guess on top of the
    /// first. Left to its own slice. Direction of the residual, stated rather than discovered: an un-netted
    /// Rejection <b>Out</b> leaves a purchase order retired ⇒ Purc Orders Pending low ⇒ Nett Available low ⇒
    /// shortfall OVER-stated (safe); an un-netted Rejection <b>In</b> leaves a sales order retired ⇒ Sales Orders
    /// Due low ⇒ Nett Available high ⇒ shortfall UNDER-stated (<b>unsafe</b>) — which is why it is surfaced as an
    /// open question rather than left implicit.</para>
    /// </summary>
    [Fact]
    public void A_rejection_out_does_not_retire_a_sales_order()
    {
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok);
        // Outward, same party, same item, after the order — everything but the door matches a Delivery Note.
        ShellNote(k, VoucherBaseType.RejectionOut, item, D2, 60.125m, partyId: ashok);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
        Assert.Equal(60.125m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>
    /// 🔴 <b>POS is the SAME door, and this proves the cohort key resolves on it.</b> A POS bill is not a fourth
    /// mechanism: <c>VoucherType.IsPosSales</c> is <c>BaseType == Sales &amp;&amp; UseForPos</c>, so a POS sale
    /// moves its stock through <c>Voucher.InventoryLines</c> exactly like any item invoice and the tender split
    /// only changes the DEBIT side. The interesting half is the party: a walk-in cash bill names nobody, so it
    /// lands in the <c>(null, item)</c> cohort and retires an order raised with the picker on "(none)" — the
    /// small-book shape — while leaving Ashok's order alone, because a blank counterparty is a cohort of its own
    /// and never a wildcard.
    /// <para>Components, not the total: Σ outstanding is 60.125 whichever order the POS sale retired.</para>
    /// </summary>
    [Fact]
    public void A_pos_cash_sale_retires_the_blank_order_and_not_a_party_named_one()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        k.Company.FindVoucherType(b.SalesTypeId)!.UseForPos = true;

        var ashoks = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok);        // EARLIER
        var counter = Order(k, VoucherBaseType.SalesOrder, item, D2, 47.331m, partyId: null);

        var line = new VoucherInventoryLine(item, k.GodownId, 47.331m, Money.FromRupees(47.36m),
            StockDirection.Outward);
        b.Ledgers.Post(new Voucher(Guid.NewGuid(), b.SalesTypeId, D3, new[]
            {
                new EntryLine(b.Cash, line.Value, DrCr.Debit),
                new EntryLine(b.SalesAccount, line.Value, DrCr.Credit),
            },
            partyId: null,
            inventoryLines: new[] { line },
            posTenders: new[]
            {
                new PosTender(PosTenderType.Cash, b.Cash, line.Value,
                    Tendered: line.Value + Money.FromRupees(2.75m), Change: Money.FromRupees(2.75m)),
            }));

        var blank = Row(k.Company, D4, counter.Number);
        Assert.Equal(47.331m, blank.FulfilledQuantity);
        Assert.Equal(0m, blank.OutstandingQuantity);

        var named = Row(k.Company, D4, ashoks.Number);
        Assert.Equal(0m, named.FulfilledQuantity);
        Assert.Equal(60.125m, named.OutstandingQuantity);
    }

    /// <summary>
    /// 🔴 The party half of the cohort key on the ITEM-INVOICE door, where the counterparty lives on the
    /// accounting <see cref="Voucher.PartyId"/> rather than on an inventory-voucher header. Chandra's order is
    /// deliberately the EARLIER of the two, so an implementation that reads no party (or the wrong one) on this
    /// door hands the goods to Chandra by FIFO and Ashok's row reads 0 fulfilled.
    /// <para>The invoice settles to CASH, never to the party ledger, so <see cref="Voucher.PartyId"/> is the only
    /// carrier of the counterparty — a fixture that also posted a debtor leg would let a party-blind
    /// implementation pass by accident.</para>
    /// </summary>
    [Fact]
    public void An_item_invoice_naming_a_customer_retires_that_customers_order_and_not_anothers()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        var chandra = Party(k, "Chandra Stores");
        Opening(k, item, 180.875m);
        var chandras = Order(k, VoucherBaseType.SalesOrder, item, D1, 47.331m, chandra);   // EARLIER
        var ashoks = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 60.125m, partyId: ashok);

        var a = Row(k.Company, D4, ashoks.Number);
        Assert.Equal(60.125m, a.FulfilledQuantity);
        Assert.Equal(0m, a.OutstandingQuantity);

        var c = Row(k.Company, D4, chandras.Number);
        Assert.Equal(0m, c.FulfilledQuantity);
        Assert.Equal(47.331m, c.OutstandingQuantity);

        Assert.Equal(47.331m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));
    }

    /// <summary>A CANCELLED item invoice moved no stock, so it retires nothing — the same rule the note door
    /// obeys (<see cref="A_cancelled_fulfilling_voucher_does_not_retire_the_order"/>), and it must hold on the
    /// new door too or a cancelled bill would silently close a live order.</summary>
    [Fact]
    public void A_cancelled_item_invoice_does_not_retire_the_order()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 60.125m, partyId: ashok, cancelled: true);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    /// <summary>An <b>OPTIONAL</b> item invoice is an un-posted draft: it books nothing and moves nothing, so it
    /// retires nothing. Optionality is a flag the pure-stock door does not even have, so it exists only on this
    /// door and could only be got right here.</summary>
    [Fact]
    public void An_optional_item_invoice_does_not_retire_the_order()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 60.125m, partyId: ashok, optional: true);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(0m, row.FulfilledQuantity);
        Assert.Equal(60.125m, row.OutstandingQuantity);
    }

    // ================================================================ 🔴 the THREE STATED RESIDUALS
    //
    // Each of the next three fixtures asserts a figure the engine gets WRONG, on purpose. They are named so the
    // name says "stated residual", they assert COMPONENTS (a total is satisfied by several wrong attributions),
    // and they exist because prose stating a residual is not evidence — twice on this slice a residual paragraph
    // said one thing and the engine did another, and only a measurement found it.

    /// <summary>
    /// 🔴 <b>THE ERROR DIRECTION INVERTS ON THE PURCHASE ARM, AND ON THIS ARM IT IS UNSAFE.</b>
    /// <see cref="OrderFulfilment"/> once claimed, unqualified, that a duplicate document leaves
    /// <c>NettAvailable</c> "<c>(D − A)</c> LOW — never high" so the residual "can never UNDER-state" the
    /// shortfall. This fixture is the refutation, on the corpus's own purchase cycle
    /// (PO → Receipt Note → Purchase Voucher [CORPUS-TALLY-BOOK(719244897) pp.14-15]) — the very example the
    /// claim's own paragraph cites.
    /// <para>The book: one physical receipt of 47.330 keyed TWICE, once as a Receipt Note and once as the
    /// supplier's bill. Truth — closing 47.330, nothing pending, Nett Available 47.330, so a level of 60.125
    /// leaves a real shortfall of 12.795 and an order of that size (the 5.375 MOQ does not floor it). Engine —
    /// the goods came IN twice, so closing is 94.660 and Nett Available is 94.660, <b>HIGH by D = 47.330</b>,
    /// and the shortfall prints <b>0</b>. The buyer is told he needs nothing while he is 12.795 short: shortfall
    /// UNDER-stated, a customer left unserved. The outward arm really does err the other (safe) way; the mistake
    /// was generalising one arm's derivation to both.</para>
    /// <para>Asserted on <see cref="ReorderStatusRow"/> rather than on the per-order columns because the refuted
    /// claim was about exactly this figure, and because this is the number Ctrl+F9 carries into a real supplier
    /// purchase order. <b>Handed to the user as an open question beside the un-netted Rejection In, which errs
    /// in the same direction.</b></para>
    /// </summary>
    [Fact]
    public void A_duplicate_purchase_document_pushes_nett_available_high_and_under_states_shortfall()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = k.Masters.CreateStockItem("Widget", k.GroupId, k.UnitId,
            reorderLevel: 60.125m, minimumOrderQuantity: 5.375m).Id;
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 47.330m, bimal);
        ShellNote(k, VoucherBaseType.ReceiptNote, item, D2, 47.330m, partyId: bimal);      // the goods arrive
        ItemInvoice(k, b, VoucherBaseType.Purchase, item, D3, 47.330m, partyId: bimal);    // …and are billed

        // The order itself is retired exactly once — the per-line cap holds, as on the sales arm.
        var order = Row(k.Company, D5, po.Number);
        Assert.Equal(47.330m, order.FulfilledQuantity);
        Assert.Equal(0m, order.OutstandingQuantity);

        var row = ReorderStatus.Build(k.Company, D5).Rows.Single(r => r.ItemName == "Widget");
        // Components, never the total: closing is what carries the doubling, and pendingPO is what the refuted
        // proof assumed would offset it. Neither can be inferred from Nett Available alone.
        Assert.Equal(94.660m, row.ClosingQuantity);            // 47.330 keyed twice — the stock engine agrees
        Assert.Equal(0m, row.PendingPurchaseOrders);           // the PO is fully retired
        Assert.Equal(0m, row.SalesOrdersDue);
        Assert.Equal(94.660m, row.NettAvailable);              // truth 47.330 — HIGH by D, NOT low
        Assert.Equal(0m, row.Shortfall);                       // truth 12.795 — UNDER-stated, not over
        Assert.Equal(0m, row.OrderToBePlaced);                 // …so nothing is ordered, and 12.795 is missing
    }

    /// <summary>
    /// 🔴 <b>THE PURCHASE-ARM TWIN of the per-order spill</b>, and the half no aggregate argument covers. Two
    /// open purchase orders on one supplier; the first is received in full and then billed for the same goods.
    /// The bill finds the cursor past PO#1, reaches PO#2 and takes 47.331 of it — so <b>PO#2, against which the
    /// supplier has shipped nothing at all, prints Fulfilled 47.331 / Outstanding 0</b>, and Purc Orders Pending
    /// for the item reads 0 against a true 47.331.
    /// <para><b>This is a REGRESSION on this row, stated as one.</b> Before WF-8 the register hard-coded
    /// Outstanding = ordered, so this row read 47.331 — accidentally right, because nothing was ever retired.
    /// The note-door-only revision of this slice also read 47.331. Only opening the item-invoice door produces
    /// the 0. It is not fixable without TallyPrime's Tracking Number: bounding a movement to lines an earlier
    /// movement already touched is the "same party + item + quantity ⇒ one shipment" guess under another name,
    /// and it would silently swallow a genuine second shipment — which is indistinguishable from this book.</para>
    /// <para>Aggregate consequence, for completeness: closing 120.250 against a true 60.125 and pendingPO 0
    /// against a true 47.331 leave Nett Available 120.250 against a true 107.456 — HIGH by 12.794, the dropped
    /// surplus, so shortfall is under-stated by that much. <b>Owed to the user as a go/no-go.</b></para>
    /// </summary>
    [Fact]
    public void A_duplicate_purchase_invoice_retires_a_second_open_order_nothing_was_received_against()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var bimal = Party(k, "Bimal Supplies", "Sundry Creditors");
        var po1 = Order(k, VoucherBaseType.PurchaseOrder, item, D1, 60.125m, bimal);
        var po2 = Order(k, VoucherBaseType.PurchaseOrder, item, D2, 47.331m, bimal);   // nothing received on it
        ShellNote(k, VoucherBaseType.ReceiptNote, item, D3, 60.125m, partyId: bimal);
        ItemInvoice(k, b, VoucherBaseType.Purchase, item, D4, 60.125m, partyId: bimal); // the SAME goods, billed

        // PO#1 is retired once and only once — the per-line cap, same as the sales arm.
        var first = Row(k.Company, D5, po1.Number);
        Assert.Equal(60.125m, first.FulfilledQuantity);
        Assert.Equal(0m, first.OutstandingQuantity);

        // 🔴 PO#2 — the residual. The correct figures are Fulfilled 0 / Outstanding 47.331.
        var second = Row(k.Company, D5, po2.Number);
        Assert.Equal(47.331m, second.FulfilledQuantity);   // the spill; 12.794 of the bill is dropped
        Assert.Equal(0m, second.OutstandingQuantity);      // an order with no receipt, reading fully received
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.PurchaseOrder, D5));
        // The stock engine's own verdict, so nobody reads "retired once" as "stock deduplicated".
        Assert.Equal(120.250m,
            StockSummary.Build(k.Company, D5).Rows.Single(r => r.ItemName == "Widget").ClosingQuantity);
    }

    /// <summary>
    /// 🔴 <b>A CANCELLED ORDER RELEASES ITS FIFO CAPACITY TO THE LIVE ORDER BEHIND IT.</b>
    /// <see cref="OrderFulfilment"/> excludes a cancelled order from the cohort, which is the right answer on
    /// the ORDINARY cancellation book (an order that will never be fulfilled must not soak up the movement that
    /// belongs to the live order after it). This fixture pins the mirror book, where the same rule costs:
    /// <b>deliver, then cancel</b>. <see cref="InventoryVoucher.Cancelled"/> is a boolean with no cancellation
    /// date, so the engine cannot tell the two books apart.
    /// <para>Both arms, because the DIRECTION differs and only one of them is dangerous. SALES: the delivery
    /// that went out against the cancelled SO#1 retires live SO#2 instead, so Sales Orders Due reads 0 against a
    /// true 47.330 ⇒ Nett Available HIGH ⇒ shortfall <b>UNDER-stated</b> ⇒ the buyer's Ctrl+F9 purchase order is
    /// 47.330 short of already-committed customer demand — a customer left unserved, the exact harm WF-7/WF-8
    /// exist to remove. PURCHASE: Purc Orders Pending reads 0 against a true 47.330 ⇒ Nett Available LOW ⇒
    /// shortfall OVER-stated (safe, merely wasteful).</para>
    /// <para><b>It is a REGRESSION on this book</b> — the deleted pre-WF-8 raw sum also skipped cancelled orders
    /// and therefore happened to report the true 47.330 here — which is why the class doc's "never a regression
    /// on any" claim is now scoped to the blank-party rule that earned it. Components, not totals: Σ outstanding
    /// is 47.330 either way, so only the per-order attribution can tell the right answer from the wrong one.
    /// <b>Owed to the user as a go/no-go</b>; keeping cancelled lines as non-reported absorbers does not remove
    /// the error, it swaps which book is wrong and inverts the direction on both arms.</para>
    /// </summary>
    [Fact]
    public void A_movement_booked_against_a_cancelled_order_retires_the_live_order_behind_it_a_stated_residual()
    {
        // ---- SALES arm: the unsafe one.
        var k = NewKit();
        var item = Item(k, "Widget");
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so1 = Order(k, VoucherBaseType.SalesOrder, item, D1, 60.125m, ashok, cancelled: true);  // number 1
        var so2 = Order(k, VoucherBaseType.SalesOrder, item, D1, 47.330m, ashok);                   // number 2, LIVE
        Assert.Equal(1, so1.Number);   // the fixture only bites while the cancelled order sorts FIRST
        Assert.Equal(2, so2.Number);
        ShellNote(k, VoucherBaseType.DeliveryNote, item, D2, 60.125m, partyId: ashok);   // shipped for SO#1

        var map = OrderFulfilment.Build(k.Company, D4);
        Assert.Equal(47.330m, map[(so2.Id, 0)]);   // the residual — the correct figure is 0
        Assert.False(map.ContainsKey((so1.Id, 0)), "a cancelled order is not a counting order line");
        var row = Row(k.Company, D4, so2.Number);
        Assert.Equal(47.330m, row.FulfilledQuantity);
        Assert.Equal(0m, row.OutstandingQuantity);  // correct figure 47.330 — an order nothing shipped against
        Assert.Equal(0m, OrderFulfilment.OutstandingForItem(k.Company, item, VoucherBaseType.SalesOrder, D4));

        // ---- PURCHASE arm: the same mechanism, the opposite (safe) direction. Separate book so neither masks
        //      the other.
        var k2 = NewKit();
        var item2 = Item(k2, "Widget");
        var bimal = Party(k2, "Bimal Supplies", "Sundry Creditors");
        Order(k2, VoucherBaseType.PurchaseOrder, item2, D1, 60.125m, bimal, cancelled: true);
        var po2 = Order(k2, VoucherBaseType.PurchaseOrder, item2, D1, 47.330m, bimal);
        ShellNote(k2, VoucherBaseType.ReceiptNote, item2, D2, 60.125m, partyId: bimal);

        var row2 = Row(k2.Company, D4, po2.Number);
        Assert.Equal(47.330m, row2.FulfilledQuantity);   // the residual — the correct figure is 0
        Assert.Equal(0m, row2.OutstandingQuantity);      // correct figure 47.330
        Assert.Equal(0m,
            OrderFulfilment.OutstandingForItem(k2.Company, item2, VoucherBaseType.PurchaseOrder, D4));
    }

    /// <summary>
    /// The unit-conversion twin of <see cref="A_movement_in_a_compound_unit_is_converted_to_the_items_base_unit"/>
    /// on the item-invoice door — a genuinely separate risk, because an item-invoice line carries its own
    /// <c>UnitId</c> and its own normalisation path. 4 Boxes of 12 is 48 Nos, retiring 48 of a 60.125 order and
    /// leaving 12.125; an unconverted line would retire 4 and leave 56.125.
    /// </summary>
    [Fact]
    public void An_item_invoice_in_a_compound_unit_is_converted_to_the_items_base_unit()
    {
        var k = NewKit();
        var b = NewBooks(k);
        var item = Item(k, "Widget");
        var boxUnit = k.Masters.CreateSimpleUnit("Box", "Box");
        var box = k.Masters.CreateCompoundUnit("Box-12", "Box of 12", boxUnit.Id, k.UnitId, 12).Id;
        var ashok = Party(k, "Ashok Traders");
        Opening(k, item, 180.875m);
        var so = Order(k, VoucherBaseType.SalesOrder, item, D2, 60.125m, ashok);
        ItemInvoice(k, b, VoucherBaseType.Sales, item, D3, 4m, partyId: ashok, rate: 568.32m, unitId: box);

        var row = Row(k.Company, D4, so.Number);
        Assert.Equal(48m, row.FulfilledQuantity);       // 4 Boxes x 12 — NOT 4
        Assert.Equal(12.125m, row.OutstandingQuantity);
    }
}
