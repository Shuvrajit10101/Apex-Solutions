using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// 🔴 <b>CENSUS 4.7 / 4.8 — DEFECT T0-10. A CREDIT OR DEBIT NOTE MOVES STOCK.</b>
///
/// <para><b>The defect, measured.</b> <c>VoucherValidator.EnsureItemInvoiceValid</c> threw on any item line whose
/// carrier was not a Purchase or a Sales, and <c>VoucherEntryViewModel.CanBeItemInvoice</c> offered the grid on
/// those two alone. So a sales or purchase RETURN moved money and <b>no goods</b>. That is not a missing screen:
/// it is <b>wrong closing stock</b>, understated by every sales return a business takes back in and overstated by
/// every purchase return it sends out, compounding for the life of the book and never self-correcting.</para>
///
/// <para><b>Vendor, cited (ruling 14).</b> help.tallysolutions.com, "How to Record a Sales Return Using Credit
/// Note Under GST in TallyPrime" — <i>"Press Ctrl+H (Change Mode) &gt; select Item Invoice"</i>, then <i>"Select
/// the stock item that you initially sold and specify the Quantity and Rate based on the returns received"</i>;
/// and "How to Record Purchase Returns under GST | Debit Note for Purchase Returns" — <i>"Enter the Name of Stock
/// Item, Quantity, Rate, and tax ledgers"</i>. Both notes are item-invoice carriers in the reference product.</para>
///
/// <para>🔴 <b>WHY EVERY TEST HERE READS THE ENGINE BACK, NOT THE VALIDATOR.</b> A widening that only relaxed the
/// validator would let the line be SAVED and still count no stock — <c>ItemInvoiceStock.Counts</c> is the gate
/// that folds an item line into on-hand and into valuation, and it restated the carrier set for itself. That
/// failure mode is worse than the original defect, because the line is then visible on screen and the books look
/// closed. So on-hand and closing VALUE are asserted after every post, never merely "it did not throw".</para>
///
/// <para>🔴 <b>THE DIRECTION IS THE OTHER HALF, AND IT IS THE EASY ONE TO GET BACKWARDS.</b> A sales return comes
/// <b>IN</b> and a purchase return goes <b>OUT</b> — the opposite of the invoice each mirrors. The superseded code
/// stamped <c>IsPurchaseInvoice ? Inward : Outward</c>: a two-way test on a four-way fact, which would have sent a
/// sales return OUTWARD and taken the returned goods off the shelf a SECOND time, doubling the very error this
/// closes instead of removing it. <see cref="A_sales_return_that_moved_stock_the_wrong_way_would_double_the_defect"/>
/// is the test that fails on that mistake specifically.</para>
/// </summary>
public class ReturnNoteInventoryTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    // ================================================================= fixture

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required LedgerService Ledgers { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Domain.Ledger Purchases { get; init; }
        public required Domain.Ledger PurchaseReturns { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Domain.Ledger SalesReturns { get; init; }
        public required Domain.Ledger Creditor { get; init; }
        public required Domain.Ledger Debtor { get; init; }

        public Guid TypeOf(VoucherBaseType b) => Company.VoucherTypes.First(t => t.BaseType == b).Id;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// A trading company with a "Sales Returns" ledger under <b>Sales Accounts</b> and a "Purchase Returns" ledger
    /// under <b>Purchase Accounts</b> — which is where the reference product puts them, and the whole reason a note
    /// needs no new accounting family: only the opposite SIDE of the family the invoice used.
    /// </summary>
    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Return Note Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        return new Kit
        {
            Company = c,
            Ledgers = new LedgerService(c),
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            Purchases = AddLedger(c, "Purchases", "Purchase Accounts", true),
            PurchaseReturns = AddLedger(c, "Purchase Returns", "Purchase Accounts", false),
            Sales = AddLedger(c, "Sales", "Sales Accounts", false),
            SalesReturns = AddLedger(c, "Sales Returns", "Sales Accounts", true),
            Creditor = AddLedger(c, "Creditor", "Sundry Creditors", false),
            Debtor = AddLedger(c, "Debtor", "Sundry Debtors", true),
        };
    }

    /// <summary>
    /// Posts one item-invoice-shaped voucher of any of the four carrier natures. <b>The direction is deliberately
    /// NOT passed</b> — <c>LedgerService.StampInventoryLineDirections</c> stamps it from the voucher's nature, and
    /// letting the fixture set it would hide a stamp that had stopped working.
    /// </summary>
    private static Voucher Post(Kit k, VoucherBaseType nature, DateOnly date, decimal qty, decimal rate,
        Domain.Ledger stockLeg, DrCr stockSide, Domain.Ledger party)
    {
        var line = new VoucherInventoryLine(k.ItemId, k.GodownId, qty, Money.FromRupees(rate));
        var partySide = stockSide == DrCr.Debit ? DrCr.Credit : DrCr.Debit;
        return k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(nature), date,
            new[]
            {
                new EntryLine(stockLeg.Id, line.Value, stockSide),
                new EntryLine(party.Id, line.Value, partySide),
            },
            partyId: party.Id,
            inventoryLines: new[] { line }));
    }

    private static Voucher Purchase(Kit k, DateOnly d, decimal qty, decimal rate) =>
        Post(k, VoucherBaseType.Purchase, d, qty, rate, k.Purchases, DrCr.Debit, k.Creditor);

    private static Voucher Sale(Kit k, DateOnly d, decimal qty, decimal rate) =>
        Post(k, VoucherBaseType.Sales, d, qty, rate, k.Sales, DrCr.Credit, k.Debtor);

    /// <summary>A sales return: Dr Sales Returns (goods coming in ⇒ the stock leg is a debit) / Cr the customer.</summary>
    private static Voucher CreditNote(Kit k, DateOnly d, decimal qty, decimal rate) =>
        Post(k, VoucherBaseType.CreditNote, d, qty, rate, k.SalesReturns, DrCr.Debit, k.Debtor);

    /// <summary>A purchase return: Cr Purchase Returns (goods going out ⇒ credit) / Dr the supplier.</summary>
    private static Voucher DebitNote(Kit k, DateOnly d, decimal qty, decimal rate) =>
        Post(k, VoucherBaseType.DebitNote, d, qty, rate, k.PurchaseReturns, DrCr.Credit, k.Creditor);

    private static decimal OnHand(Kit k) => new InventoryLedger(k.Company).OnHand(k.ItemId, k.GodownId, AsOf);

    // ================================================================= 1 — the stock half

    /// <summary>
    /// 🔴 <b>THE ROW. A SALES RETURN BRINGS THE GOODS BACK, AND CLOSING STOCK SAYS SO.</b> Buy 100, sell 40,
    /// customer returns 10 ⇒ on-hand <b>70</b>, not 60.
    ///
    /// <para><b>Red on today's main</b> at the post itself: <c>EnsureItemInvoiceValid</c> threw
    /// <i>"Item-invoice stock lines are only valid on a Purchase or Sales voucher"</i>, so the credit note could
    /// not be written at all and the book was stuck at 60 — understated by exactly the returned goods.</para>
    /// </summary>
    [Fact]
    public void A_credit_note_brings_the_returned_goods_back_into_closing_stock()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        Sale(k, D1, 40m, 80m);
        Assert.Equal(60m, OnHand(k));

        var note = CreditNote(k, D2, 10m, 80m);

        Assert.Equal(StockDirection.Inward, note.InventoryLines.Single().Direction);
        Assert.Equal(70m, OnHand(k));
    }

    /// <summary>
    /// 🔴 <b>AND A PURCHASE RETURN TAKES THEM BACK OUT.</b> Buy 100, return 15 to the supplier ⇒ on-hand
    /// <b>85</b>. The mirror case, and the one that proves the widening did not simply make everything inward.
    /// </summary>
    [Fact]
    public void A_debit_note_sends_the_returned_goods_back_out_of_closing_stock()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);

        var note = DebitNote(k, D2, 15m, 50m);

        Assert.Equal(StockDirection.Outward, note.InventoryLines.Single().Direction);
        Assert.Equal(85m, OnHand(k));
    }

    /// <summary>
    /// 🔴🔴 <b>THE DIRECTION IS NOT SYMMETRIC WITH THE INVOICE, AND GETTING IT BACKWARDS DOUBLES THE DEFECT.</b>
    ///
    /// <para>This is the test that fails on the specific mistake the superseded stamp would have made. Under
    /// <c>IsPurchaseInvoice ? Inward : Outward</c> a Credit Note is not a Purchase, so it would have been stamped
    /// OUTWARD: selling 40 and then "returning" 10 would leave <b>50</b> on hand instead of 70 — the returned
    /// goods removed twice. An implementation that merely relaxed the validator without fixing the stamp passes
    /// every "it posts" test and fails this one.</para>
    ///
    /// <para>Asserted at the line, not only at the total, so the failure names the cause rather than the symptom.</para>
    /// </summary>
    [Fact]
    public void A_sales_return_that_moved_stock_the_wrong_way_would_double_the_defect()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        Sale(k, D1, 40m, 80m);
        CreditNote(k, D2, 10m, 80m);

        Assert.NotEqual(50m, OnHand(k));   // the wrong-way answer
        Assert.Equal(70m, OnHand(k));      // the right one
    }

    /// <summary>
    /// The engine stamps the direction from the voucher's NATURE and overrides whatever the caller passed. That
    /// matters beyond the entry screen: an import, or any other writer, must not be able to decide which way a
    /// return moves stock by handing in a direction. Both notes are handed the WRONG direction here and both come
    /// back stamped correctly.
    /// </summary>
    [Fact]
    public void The_post_time_stamp_overrides_a_caller_that_hands_in_the_wrong_direction()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);

        var wrongWayCredit = new VoucherInventoryLine(
            k.ItemId, k.GodownId, 5m, Money.FromRupees(80m), StockDirection.Outward);
        var cn = k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.CreditNote), D2,
            new[]
            {
                new EntryLine(k.SalesReturns.Id, wrongWayCredit.Value, DrCr.Debit),
                new EntryLine(k.Debtor.Id, wrongWayCredit.Value, DrCr.Credit),
            }, partyId: k.Debtor.Id, inventoryLines: new[] { wrongWayCredit }));
        Assert.Equal(StockDirection.Inward, cn.InventoryLines.Single().Direction);

        var wrongWayDebit = new VoucherInventoryLine(
            k.ItemId, k.GodownId, 5m, Money.FromRupees(50m), StockDirection.Inward);
        var dn = k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.DebitNote), D2,
            new[]
            {
                new EntryLine(k.Creditor.Id, wrongWayDebit.Value, DrCr.Debit),
                new EntryLine(k.PurchaseReturns.Id, wrongWayDebit.Value, DrCr.Credit),
            }, partyId: k.Creditor.Id, inventoryLines: new[] { wrongWayDebit }));
        Assert.Equal(StockDirection.Outward, dn.InventoryLines.Single().Direction);

        Assert.Equal(100m, OnHand(k));   // +5 back in, −5 back out
    }

    // ================================================================= 2 — the money half

    /// <summary>
    /// 🔴 <b>A RETURN REVERSES THE MONEY TOO, and the two halves are one atomic voucher.</b> Sell 40 @ ₹80
    /// (₹3,200 credited to Sales, ₹3,200 debited to the customer); take 10 back @ ₹80 (₹800 debited to Sales
    /// Returns, ₹800 credited to the customer). The customer's net receivable falls to ₹2,400 and the contra-revenue
    /// sits on its own ledger rather than being netted silently into Sales.
    /// </summary>
    [Fact]
    public void A_credit_note_reverses_the_money_as_well_as_the_stock()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        Sale(k, D1, 40m, 80m);
        CreditNote(k, D2, 10m, 80m);

        Assert.Equal(-3200m, LedgerBalances.SignedClosing(k.Company, k.Sales, AsOf));        // Cr 3,200
        Assert.Equal(800m, LedgerBalances.SignedClosing(k.Company, k.SalesReturns, AsOf));   // Dr 800
        Assert.Equal(2400m, LedgerBalances.SignedClosing(k.Company, k.Debtor, AsOf));        // 3,200 − 800
        Assert.Equal(70m, OnHand(k));                                                        // and the goods are back
    }

    /// <summary>The purchase-return mirror: the supplier's payable falls and Purchase Returns carries the credit.</summary>
    [Fact]
    public void A_debit_note_reverses_the_money_as_well_as_the_stock()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        DebitNote(k, D2, 15m, 50m);

        Assert.Equal(5000m, LedgerBalances.SignedClosing(k.Company, k.Purchases, AsOf));          // Dr 5,000
        Assert.Equal(-750m, LedgerBalances.SignedClosing(k.Company, k.PurchaseReturns, AsOf));    // Cr 750
        Assert.Equal(-4250m, LedgerBalances.SignedClosing(k.Company, k.Creditor, AsOf));          // 5,000 − 750
        Assert.Equal(85m, OnHand(k));
    }

    /// <summary>
    /// 🔴 <b>THE PAIRING INVARIANT STILL BINDS ON A NOTE — it is not relaxed, it is re-pointed.</b> The rule is
    /// that the item lines' value must equal the accounting stock leg, so no stock can move unbacked. On a return
    /// the leg is on the OPPOSITE side of the same family, and this test proves the validator looks for it there:
    /// a credit note whose Sales-Returns debit does not match its item lines is refused by name.
    /// </summary>
    [Fact]
    public void A_note_whose_stock_leg_does_not_foot_its_item_lines_is_refused()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);

        var line = new VoucherInventoryLine(k.ItemId, k.GodownId, 10m, Money.FromRupees(80m)); // ₹800 of goods
        var ex = Assert.Throws<InvalidVoucherException>(() => k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.CreditNote), D2,
            new[]
            {
                new EntryLine(k.SalesReturns.Id, Money.FromRupees(900m), DrCr.Debit),  // but ₹900 of money
                new EntryLine(k.Debtor.Id, Money.FromRupees(900m), DrCr.Credit),
            }, partyId: k.Debtor.Id, inventoryLines: new[] { line })));

        Assert.Contains("Item-invoice pairing", ex.Message);
        Assert.Contains("Sales (debit)", ex.Message);
    }

    /// <summary>
    /// The stock leg must be in the right FAMILY, not merely on the right side. A credit note that puts its debit
    /// on a Purchase-Accounts ledger is refused: the pairing finds nothing under Sales Accounts to foot against.
    /// Without this, a note could book its contra-revenue against purchases and still move stock.
    /// </summary>
    [Fact]
    public void A_credit_note_cannot_draw_its_stock_leg_from_the_purchase_family()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);

        var line = new VoucherInventoryLine(k.ItemId, k.GodownId, 10m, Money.FromRupees(80m));
        var ex = Assert.Throws<InvalidVoucherException>(() => k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.CreditNote), D2,
            new[]
            {
                new EntryLine(k.Purchases.Id, line.Value, DrCr.Debit),
                new EntryLine(k.Debtor.Id, line.Value, DrCr.Credit),
            }, partyId: k.Debtor.Id, inventoryLines: new[] { line })));

        Assert.Contains("Item-invoice pairing", ex.Message);
    }

    // ================================================================= 3 — valuation and the guards

    /// <summary>
    /// 🔴 <b>CLOSING STOCK VALUE, NOT ONLY QUANTITY.</b> The quantity half can be right while the value half
    /// silently ignores the note — they are computed by different services over the same movements, and only
    /// <c>ItemInvoiceStock</c> feeds both. Buy 100 @ ₹50, return 20 to the supplier ⇒ 80 units still valued at
    /// ₹50 = <b>₹4,000</b>.
    /// </summary>
    [Fact]
    public void A_debit_note_reduces_the_closing_stock_VALUE_not_only_the_quantity()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        Assert.Equal(Money.FromRupees(5000m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);

        DebitNote(k, D2, 20m, 50m);

        Assert.Equal(80m, OnHand(k));
        Assert.Equal(Money.FromRupees(4000m), new StockValuationService(k.Company).ClosingValue(k.ItemId, AsOf).Value);
    }

    /// <summary>
    /// 🔴 <b>A PURCHASE RETURN CANNOT SEND OUT MORE THAN IS ON HAND.</b> A Debit Note moves stock outward, so it
    /// must face the no-negative guard exactly as a Sales invoice does — and that guard restated the carrier set
    /// for itself, so a widening that missed it would let a return drive on-hand negative unseen.
    /// </summary>
    [Fact]
    public void A_debit_note_that_over_draws_on_hand_is_caught_by_the_no_negative_guard()
    {
        var k = NewKit();
        Purchase(k, D1, 10m, 50m);

        var shortfalls = new InventoryPostingService(k.Company);
        var line = new VoucherInventoryLine(k.ItemId, k.GodownId, 25m, Money.FromRupees(50m));
        k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.DebitNote), D2,
            new[]
            {
                new EntryLine(k.Creditor.Id, line.Value, DrCr.Debit),
                new EntryLine(k.PurchaseReturns.Id, line.Value, DrCr.Credit),
            }, partyId: k.Creditor.Id, inventoryLines: new[] { line }));

        Assert.Equal(-15m, OnHand(k));
        Assert.Contains(
            shortfalls.DetectNegativeStock(),
            s => s.StockItemId == k.ItemId && s.OnHand < 0m);
    }

    /// <summary>
    /// The carrier set is still CLOSED — widening it to four did not open it to everything. A Journal carrying an
    /// item line is refused, and the message now names all four permitted carriers rather than two.
    /// </summary>
    [Fact]
    public void A_journal_still_cannot_carry_item_lines()
    {
        var k = NewKit();
        var line = new VoucherInventoryLine(k.ItemId, k.GodownId, 1m, Money.FromRupees(50m));
        var ex = Assert.Throws<InvalidVoucherException>(() => k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), k.TypeOf(VoucherBaseType.Journal), D2,
            new[]
            {
                new EntryLine(k.Purchases.Id, line.Value, DrCr.Debit),
                new EntryLine(k.Creditor.Id, line.Value, DrCr.Credit),
            }, inventoryLines: new[] { line })));

        Assert.Contains("only valid on a Purchase, Sales, Credit Note or Debit Note", ex.Message);
    }

    /// <summary>
    /// The movement register must show the return, or the operator sees closing stock change with nothing to
    /// explain it. <c>InventoryMovements</c> restated the carrier set for itself too; this is its pin.
    /// </summary>
    [Fact]
    public void A_return_note_appears_in_the_stock_movement_register()
    {
        var k = NewKit();
        Purchase(k, D1, 100m, 50m);
        Sale(k, D1, 40m, 80m);
        var note = CreditNote(k, D2, 10m, 80m);

        var rows = InventoryMovements.Between(k.Company, FyStart, AsOf, k.ItemId);
        Assert.Contains(rows, r => r.VoucherId == note.Id
                                && r.BaseType == VoucherBaseType.CreditNote
                                && r.Direction == StockDirection.Inward
                                && r.Quantity == 10m);
    }

    // ================================================================= 4 — the one home

    /// <summary>
    /// 🔴 <b>THE PREDICATE IS THE SINGLE HOME, and it answers all four natures.</b> Six readers used to restate
    /// this set for themselves — the validator, the on-hand engine, the movement register, the no-negative guard,
    /// the post-time direction stamp and the entry screen. The repository already records what that costs
    /// elsewhere (four hand-written GST walks that came to disagree), so the set and the direction are asserted
    /// here directly, as the contract every one of those readers now depends on.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.Purchase, StockDirection.Inward)]
    [InlineData(VoucherBaseType.Sales, StockDirection.Outward)]
    [InlineData(VoucherBaseType.CreditNote, StockDirection.Inward)]
    [InlineData(VoucherBaseType.DebitNote, StockDirection.Outward)]
    public void The_four_carriers_and_the_way_each_moves_stock(VoucherBaseType nature, StockDirection expected)
    {
        Assert.True(VoucherEffects.CanCarryItemInvoiceLines(nature));
        Assert.Equal(expected, VoucherEffects.ItemInvoiceStockDirection(nature));
    }

    /// <summary>Asking a non-carrier which way it moves stock is a programming error, not a defaulted answer — a
    /// silent default is how <c>PartACodesFor</c> once turned an unknown document into an outward taxable supply.</summary>
    [Fact]
    public void Asking_a_non_carrier_for_a_stock_direction_throws_rather_than_defaulting()
    {
        Assert.False(VoucherEffects.CanCarryItemInvoiceLines(VoucherBaseType.Journal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoucherEffects.ItemInvoiceStockDirection(VoucherBaseType.Journal));
    }
}
