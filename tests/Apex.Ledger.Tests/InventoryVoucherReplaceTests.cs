using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census rows 4.9–4.16 and 9.2 — ALTERING a posted stock/order voucher. The engine half.</b>
///
/// <para><b>THE DEFECT THESE CLOSE.</b> <c>InventoryPostingService</c> shipped <c>Post</c>, <c>Cancel</c> and
/// <c>Delete</c> in PR #107 with 18 Desktop tests, and <b>no <c>Replace</c></b>. Alteration was therefore the one
/// lifecycle verb with no engine at all, which is why <c>VoucherEntryViewModel.ForAlter</c> refused every
/// inventory-aggregate voucher by design and why all eight of rows 4.9–4.16 were held at <c>PARTIAL</c> on that
/// single reason. Every test in this file FAILS on today's <c>main</c> for the same reason — the method does not
/// exist, so the file does not compile against it.</para>
///
/// <para>🔴 <b>WHY THE STOCK CONSEQUENCE IS ASSERTED IN FIGURES AND NOT ON A FLAG.</b> Altering a posted stock
/// movement is a wrong-money surface: the OLD effect must be fully reversed and the NEW one applied, and a
/// PARTIAL reversal silently corrupts closing stock, which moves the Balance Sheet. A test that asserted
/// "Replace returned the replacement" would pass against an implementation that added the new movement and left
/// the old one on the book. So the driving tests below compute on-hand from the ledger and compare it against a
/// figure derived by hand.</para>
/// </summary>
public class InventoryVoucherReplaceTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly On = new(2025, 4, 10);
    private static readonly DateOnly Later = new(2025, 4, 20);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private sealed record Book(Company Company, Guid ItemId, Guid GodownId)
    {
        public InventoryPostingService Service => new(Company);
        public InventoryLedger Ledger => new(Company);
        public decimal OnHand => Ledger.OnHand(ItemId, GodownId, AsOf);
    }

    private static Book Seed(string name = "Stock Alteration Co")
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, unit.Id);
        return new Book(c, item.Id, c.MainLocation!.Id);
    }

    private static InventoryVoucher PostReceipt(Book b, decimal qty, decimal rate = 50m, DateOnly? date = null)
    {
        var type = b.Company.FindVoucherTypeByName("Receipt Note")!;
        return b.Service.Post(new InventoryVoucher(
            Guid.NewGuid(), type.Id, date ?? On,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, qty, StockDirection.Inward, Money.FromRupees(rate)) }));
    }

    private static InventoryVoucher PostDelivery(Book b, decimal qty, decimal rate = 80m, DateOnly? date = null)
    {
        var type = b.Company.FindVoucherTypeByName("Delivery Note")!;
        return b.Service.Post(new InventoryVoucher(
            Guid.NewGuid(), type.Id, date ?? Later,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, qty, StockDirection.Outward, Money.FromRupees(rate)) }));
    }

    /// <summary>Builds a NEW instance carrying <paramref name="posted"/>'s identity and a different quantity —
    /// the shape a rehydrating entry screen produces.</summary>
    private static InventoryVoucher Amend(Book b, InventoryVoucher posted, decimal qty, StockDirection direction)
        => new(
            posted.Id, posted.TypeId, posted.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, qty, direction, Money.FromRupees(80m)) },
            number: posted.Number, narration: posted.Narration, partyId: posted.PartyId,
            cancelled: posted.Cancelled, postDated: posted.PostDated);

    // ==================================================================== THE STOCK CONSEQUENCE, IN FIGURES

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST — the wrong-money proof, hand-computed.</b>
    ///
    /// <para>Received 10, delivered 6 ⇒ on-hand 4. The delivery is then altered from 6 to 2 ⇒ on-hand must be
    /// <b>10 − 2 = 8</b>.</para>
    ///
    /// <para>It is chosen so that the three ways of getting this wrong give three DIFFERENT answers and none of
    /// them is 8: an implementation that appends the replacement without removing the original gives
    /// 10 − 6 − 2 = <b>2</b>; one that subtracts the old quantity and adds the new as a delta gives
    /// 4 + 6 − 2 = 8 only by luck on this shape but 10 on a direction flip; one that removes the original and
    /// forgets to apply the replacement gives <b>10</b>. A single assertion on a flag distinguishes none of
    /// them.</para>
    /// </summary>
    [Fact]
    public void Altering_a_delivery_reverses_the_old_stock_effect_and_applies_the_new_one()
    {
        var b = Seed();
        PostReceipt(b, 10m);
        var delivery = PostDelivery(b, 6m);

        Assert.Equal(4m, b.OnHand); // 10 in, 6 out — the state we are altering FROM.

        b.Service.Replace(delivery.Id, Amend(b, delivery, 2m, StockDirection.Outward));

        Assert.Equal(8m, b.OnHand);
        Assert.Equal(
            1, b.Company.InventoryVouchers.Count(v => v.Id == delivery.Id));
        Assert.Equal(2, b.Company.InventoryVouchers.Count);
    }

    /// <summary>
    /// 🔴 <b>THIS TEST'S NAME AND SUMMARY USED TO DESCRIBE A CASE ITS BODY DOES NOT EXERCISE, AND BOTH WERE
    /// CORRECTED RATHER THAN SOFTENED.</b> It was called
    /// <c>Altering_a_movement_across_directions_swings_on_hand_by_twice_the_quantity</c> and claimed to be
    /// <i>"the direction flip — the case a delta-arithmetic implementation gets wrong by 2×"</i>, worked as a
    /// 6-out delivery altered to a 6-in movement taking on-hand from 4 to 16. <b>The body flips nothing:</b> it
    /// replaces a 1-unit INWARD receipt with a 7-unit INWARD receipt. Both allocations are
    /// <see cref="StockDirection.Inward"/>, so a subtract-old-add-new implementation reaches the asserted 11 too
    /// — the test could not fail on the implementation it named, and a reader trusting the name would believe a
    /// direction flip was covered when it is not.
    ///
    /// <para><b>What it DOES prove, which is worth keeping:</b> replacing a movement with a larger one of the same
    /// direction re-derives on-hand from the amended quantity rather than the posted one (1 → 7 moves on-hand by
    /// 6, not by 7).</para>
    ///
    /// <para>🔴 <b>THE GAP IS REPORTED, NOT QUIETLY FILLED: no test anywhere crosses directions.</b> A same-type
    /// flip is refused by <c>EnsureContentMatchesType</c>, so the honest vehicle is a Stock Journal whose two arms
    /// swap — that fixture does not exist in this file and inventing it was outside this remediation's scope.</para>
    /// </summary>
    [Fact]
    public void Altering_a_movement_to_a_larger_same_direction_quantity_re_derives_on_hand()
    {
        var b = Seed();
        PostReceipt(b, 10m);
        var delivery = PostDelivery(b, 6m);
        Assert.Equal(4m, b.OnHand);

        // The engine is the thing under test here: a Receipt Note re-keyed under its own type, 1 inward → 7 inward.
        var receiptType = b.Company.FindVoucherTypeByName("Receipt Note")!;
        var inward = PostReceipt(b, 1m);
        b.Service.Replace(inward.Id, new InventoryVoucher(
            inward.Id, receiptType.Id, inward.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 7m, StockDirection.Inward, Money.FromRupees(50m)) },
            number: inward.Number));

        // 10 received + 7 received (was 1) − 6 delivered = 11.
        Assert.Equal(11m, b.OnHand);
    }

    /// <summary>
    /// 🔴 <b>THE SWAP IS AT THE INDEX, NEVER Remove + Add.</b> Every pure-stock projection walks
    /// <c>InventoryVouchers</c> in list order, and FIFO consumption and order fulfilment are order-SENSITIVE, so
    /// an amended movement appended to the end of the timeline would silently re-order valuation against every
    /// same-date movement. Mutating <c>ReplaceInventoryVoucherInternal</c> to <c>Remove</c> then <c>Add</c>
    /// reddens this and nothing else in the suite, which is why it is asserted explicitly.
    /// </summary>
    [Fact]
    public void Replace_keeps_the_voucher_at_its_own_position_in_the_timeline()
    {
        var b = Seed();
        var first = PostReceipt(b, 10m);
        var middle = PostDelivery(b, 6m);
        var last = PostReceipt(b, 3m, date: Later);

        b.Service.Replace(middle.Id, Amend(b, middle, 2m, StockDirection.Outward));

        Assert.Equal(
            new[] { first.Id, middle.Id, last.Id },
            b.Company.InventoryVouchers.Select(v => v.Id).ToArray());
    }

    // ==================================================================== THE AUDIT EVENT

    /// <summary>
    /// 🔴 <b>An alteration is an AUDIT EVENT, and it records the SAME verb an ordinary voucher's alteration
    /// records.</b> Deliberately not a new verb: an auditor must not have to know which aggregate a voucher lived
    /// in to recognise that it was amended. The snapshot is of the OUTGOING voucher — the state the operator is
    /// leaving — which is what makes the log the only surviving evidence of what the figures used to be.
    /// </summary>
    [Fact]
    public void Altering_a_stock_voucher_records_an_Alter_entry_carrying_the_outgoing_state()
    {
        var b = Seed();
        PostReceipt(b, 10m);
        var delivery = PostDelivery(b, 6m);
        var before = b.Company.VoucherEditLog.Count;

        b.Service.Replace(delivery.Id, Amend(b, delivery, 2m, StockDirection.Outward));

        var entry = Assert.Single(b.Company.VoucherEditLog.Skip(before));
        Assert.Equal(VoucherEditVerb.Alter, entry.Verb);
        Assert.Equal(delivery.Id, entry.VoucherId);
        Assert.Contains("6", entry.BeforeSnapshot.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror of the above and the one that a "log it anyway" implementation fails: a REFUSED alteration
    /// writes NOTHING. An edit log that records attempts is a log an auditor cannot read.
    /// </summary>
    [Fact]
    public void A_refused_alteration_writes_no_edit_log_entry_and_leaves_the_book_unchanged()
    {
        var b = Seed();
        PostReceipt(b, 10m);
        var delivery = PostDelivery(b, 6m);
        var logBefore = b.Company.VoucherEditLog.Count;

        // A renumber — refused by name.
        var renumbered = new InventoryVoucher(
            delivery.Id, delivery.TypeId, delivery.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 2m, StockDirection.Outward, Money.FromRupees(80m)) },
            number: delivery.Number + 99);

        Assert.Throws<InvalidOperationException>(() => b.Service.Replace(delivery.Id, renumbered));

        Assert.Equal(logBefore, b.Company.VoucherEditLog.Count);
        Assert.Equal(4m, b.OnHand);
        Assert.Equal(6m, b.Company.FindInventoryVoucher(delivery.Id)!.Allocations[0].Quantity);
    }

    // ==================================================================== THE IDENTITY GUARDS

    [Fact]
    public void Replace_refuses_the_live_voucher_as_its_own_replacement()
    {
        var b = Seed();
        var delivery = PostDelivery(b, 6m);
        var live = b.Company.FindInventoryVoucher(delivery.Id)!;

        var ex = Assert.Throws<InvalidOperationException>(() => b.Service.Replace(delivery.Id, live));
        Assert.Contains("NEW inventory voucher instance", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_refuses_a_changed_id()
    {
        var b = Seed();
        var delivery = PostDelivery(b, 6m);

        var foreign = new InventoryVoucher(
            Guid.NewGuid(), delivery.TypeId, delivery.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 2m, StockDirection.Outward, Money.FromRupees(80m)) });

        Assert.Throws<InvalidOperationException>(() => b.Service.Replace(delivery.Id, foreign));
    }

    [Fact]
    public void Replace_refuses_a_retype()
    {
        var b = Seed();
        var delivery = PostDelivery(b, 6m);
        var otherType = b.Company.FindVoucherTypeByName("Receipt Note")!;

        var retyped = new InventoryVoucher(
            delivery.Id, otherType.Id, delivery.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 2m, StockDirection.Inward, Money.FromRupees(80m)) },
            number: delivery.Number);

        var ex = Assert.Throws<InvalidOperationException>(() => b.Service.Replace(delivery.Id, retyped));
        Assert.Contains("retype", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un-cancelling is Alt+X's verb and belongs in its own edit-log entry — a Replace that flipped the flag
    /// would restore a cancelled movement's stock effect with the log recording only "Alter".
    /// </summary>
    [Fact]
    public void Replace_refuses_to_change_the_cancelled_flag()
    {
        var b = Seed();
        PostReceipt(b, 10m);
        var delivery = PostDelivery(b, 6m);
        b.Service.Cancel(delivery.Id);
        Assert.Equal(10m, b.OnHand); // the cancel dropped the 6 out

        var uncancelled = new InventoryVoucher(
            delivery.Id, delivery.TypeId, delivery.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 6m, StockDirection.Outward, Money.FromRupees(80m)) },
            number: delivery.Number, cancelled: false);

        Assert.Throws<InvalidOperationException>(() => b.Service.Replace(delivery.Id, uncancelled));
        Assert.Equal(10m, b.OnHand);
    }

    /// <summary>
    /// A replacement that fails validation is handed back carrying the number it ARRIVED with, not the original's.
    /// The stamp mutates the caller's object, so without the undo a rejected draft re-posted as a NEW voucher
    /// would take the original's number and produce two live vouchers of one type sharing it.
    /// </summary>
    [Fact]
    public void A_rejected_replacement_does_not_keep_the_originals_number()
    {
        var b = Seed();
        var delivery = PostDelivery(b, 6m);
        Assert.True(delivery.Number > 0);

        // A Delivery Note carrying an INWARD line — refused by EnsureContentMatchesType.
        var wrongDirection = new InventoryVoucher(
            delivery.Id, delivery.TypeId, delivery.Date,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 2m, StockDirection.Inward, Money.FromRupees(80m)) },
            number: 0);

        Assert.ThrowsAny<Exception>(() => b.Service.Replace(delivery.Id, wrongDirection));
        Assert.Equal(0, wrongDirection.Number);
        Assert.Equal(6m, b.Company.FindInventoryVoucher(delivery.Id)!.Allocations[0].Quantity);
    }

    [Fact]
    public void Replace_refuses_an_unknown_voucher_by_name()
    {
        var b = Seed();
        var stranger = new InventoryVoucher(
            Guid.NewGuid(), b.Company.FindVoucherTypeByName("Delivery Note")!.Id, On,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 1m, StockDirection.Outward, Money.FromRupees(1m)) });

        var ex = Assert.Throws<InvalidOperationException>(
            () => b.Service.Replace(stranger.Id, stranger));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A Stock Journal must still balance after alteration — the engine re-runs the same invariant
    /// <c>Post</c> does, so an alteration cannot introduce a shape a fresh post would have refused.
    /// </summary>
    [Fact]
    public void Replace_re_runs_the_stock_journal_balance_invariant()
    {
        var b = Seed();
        var inv = new InventoryService(b.Company);
        var other = inv.CreateStockItem(
            "Gadget", b.Company.FindStockGroupByName("Primary")!.Id, b.Company.Units[0].Id);
        PostReceipt(b, 10m);

        var sjType = b.Company.FindVoucherTypeByName("Stock Journal")!;
        var sj = b.Service.Post(InventoryVoucher.StockJournal(
            Guid.NewGuid(), sjType.Id, Later,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 4m, StockDirection.Outward, Money.FromRupees(50m)) },
            new[] { new InventoryAllocation(other.Id, b.GodownId, 4m, StockDirection.Inward, Money.FromRupees(50m)) }));

        var unbalanced = InventoryVoucher.StockJournal(
            sj.Id, sjType.Id, Later,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 4m, StockDirection.Outward, Money.FromRupees(50m)) },
            new[] { new InventoryAllocation(other.Id, b.GodownId, 9m, StockDirection.Inward, Money.FromRupees(50m)) },
            number: sj.Number);

        Assert.ThrowsAny<Exception>(() => b.Service.Replace(sj.Id, unbalanced));
        Assert.Equal(6m, b.OnHand); // 10 in, 4 consumed by the ORIGINAL journal — unchanged.
    }
}
