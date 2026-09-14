using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census rows 4.9–4.16 — the pure-stock aggregate becomes a first-class citizen.</b> The engine half.
///
/// <para><b>THE DEFECT THESE CLOSE.</b> <c>DayBook.Build</c> iterated <c>company.Vouchers</c> alone, so a posted
/// Stock Journal, Physical Stock, Delivery/Receipt Note, Sales/Purchase Order or Rejection was <b>invisible in
/// the one report an operator opens to find a transaction</b> — and because the Day Book is the surface the
/// lifecycle verbs act on, a mis-keyed stock movement had no remedy in the product but posting an equal and
/// opposite one. Separately, <c>InventoryPostingService.Cancel</c>/<c>.Delete</c> existed but wrote NOTHING to
/// the voucher edit log; the service's own summary declared that gap in writing. A cancelled stock movement
/// that leaves no record is worse than one that cannot be cancelled, so the log had to be closed in the same
/// slice that gave the verbs a keyboard route.</para>
///
/// <para><b>FIDELITY (R7; RULING 14 — the vendor's own documentation).</b>
/// <i>help.tallysolutions.com/tally-prime/accounting-reports-tally/day-book-tally/</i>, fetched 2026-09-14:
/// the Day Book's "All Vouchers" view <i>"displays Day Book for all the vouchers, irrespective of the type of
/// voucher"</i>, and its "Inventory Entries Only" view <i>"displays Day Book for only inventory vouchers such as
/// Journal Vouchers for stock items, Delivery Note, Physical Stock Voucher, and others"</i> — so the vendor's
/// Day Book demonstrably covers this aggregate. The same page attests the two verbs on this report:
/// <i>"Press Alt+D to delete"</i> and <i>"Press Alt+X to cancel"</i>. The two NARROWING views are not built and
/// are an honest, named divergence; our default is the vendor's "All Vouchers".</para>
/// </summary>
public class InventoryVoucherLifecycleTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly On = new(2025, 4, 10);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private sealed record Book(Company Company, Guid ItemId, Guid GodownId);

    /// <summary>A seeded company with one stock item and the main godown — the smallest book in which a stock
    /// movement can be posted and its consequence measured.</summary>
    private static Book Seed(string name = "Stock Lifecycle Co")
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, unit.Id);
        return new Book(c, item.Id, c.MainLocation!.Id);
    }

    private static InventoryVoucher PostReceipt(Book b, decimal qty, decimal rate, DateOnly? date = null)
    {
        var type = b.Company.FindVoucherTypeByName("Receipt Note")!;
        var v = new InventoryVoucher(
            Guid.NewGuid(), type.Id, date ?? On,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, qty, StockDirection.Inward, Money.FromRupees(rate)) });
        return new InventoryPostingService(b.Company).Post(v);
    }

    // =============================================================== the Day Book listing

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> A posted Receipt Note appears in the Day Book at all. On <c>main</c> this
    /// fails on the very first assertion — the row does not exist, because <c>Build</c> never looked at
    /// <c>company.InventoryVouchers</c>.
    /// </summary>
    [Fact]
    public void A_posted_stock_voucher_appears_in_the_Day_Book_and_is_drillable()
    {
        var b = Seed();
        var receipt = PostReceipt(b, qty: 10m, rate: 50m);

        var row = Assert.Single(DayBook.Build(b.Company, FyStart, AsOf), r => r.VoucherId == receipt.Id);

        Assert.True(row.IsInventory,
            "The Day Book row for a pure-stock voucher does not declare which aggregate its id addresses, so "
            + "every consumer that resolves through Company.FindVoucher will silently drop it.");
        Assert.True(row.IsDrillable, "A Day Book row nobody can open is only half a listing.");
        Assert.Equal("Receipt Note", row.VoucherTypeName);
        Assert.Equal(On, row.Date);
        Assert.False(row.IsCancelled);
    }

    /// <summary>
    /// The Day Book lists BOTH aggregates together, in date order — which is the whole point of the report.
    /// </summary>
    [Fact]
    public void The_Day_Book_lists_accounting_and_stock_vouchers_together_in_date_order()
    {
        var b = Seed();
        var c = b.Company;

        var cash = c.FindLedgerByName("Cash")!;
        var capital = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Receipt")!.Id, new DateOnly(2025, 4, 5),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(c.FindLedgerByName("Profit & Loss A/c")?.Id ?? cash.Id, Money.FromRupees(5000m), DrCr.Credit),
            }));

        var receipt = PostReceipt(b, qty: 4m, rate: 25m, date: new DateOnly(2025, 4, 12));

        var rows = DayBook.Build(c, FyStart, AsOf);
        var ids = rows.Select(r => r.VoucherId).ToList();

        Assert.Contains(capital.Id, ids);
        Assert.Contains(receipt.Id, ids);
        Assert.True(ids.IndexOf(capital.Id) < ids.IndexOf(receipt.Id),
            "The Day Book is not in date order once both aggregates list — the 5 April accounting voucher must "
            + "precede the 12 April stock movement.");
    }

    /// <summary>
    /// 🔴 <b>THE ORDER MUST BE TOTAL, and this is the case that proves it.</b> The two aggregates number
    /// INDEPENDENTLY, so an accounting Receipt No. 1 and a Delivery Note No. 1 on one date are ordinary. Sorting
    /// by (Date, Number) alone leaves those two rows tied and <c>List.Sort</c> is UNSTABLE, so what an operator
    /// sees would be an artefact of which loop happened to append first rather than anything they could predict.
    ///
    /// <para>🔴 <b>THIS TEST REPLACES ONE THAT COULD NOT FAIL, and the replacement is the whole point.</b> The
    /// version written here first built the book, rebuilt it twelve times and asserted the id order matched. It
    /// SURVIVED deleting the entire tie-break (measured: comparator reduced to <c>return 0</c>, suite still
    /// green), because .NET's introsort is deterministic for one identical input — repeating the same call in
    /// one process can never detect an unstable sort. It was a test that asserted a property of the runtime, not
    /// of this code.</para>
    ///
    /// <para><b>What discriminates instead: a tie whose tie-break DISAGREES with insertion order.</b> The
    /// accounting loop appends first, so insertion order is Receipt-then-Delivery Note; ordinal type name puts
    /// "Delivery Note" BEFORE "Receipt". Asserting the stock row comes first therefore fails on any comparator
    /// that drops the third key, and would fail again on one that sorted by aggregate instead of by name.</para>
    /// </summary>
    [Fact]
    public void A_tie_on_date_and_number_is_broken_by_type_name_not_by_which_loop_ran_first()
    {
        var b = Seed("Tie Break Co");
        var c = b.Company;

        // Stock to deliver, dated a day earlier so it is not itself part of the tie.
        PostReceipt(b, qty: 50m, rate: 10m, date: On.AddDays(-1));

        var cash = c.FindLedgerByName("Cash")!;
        var accounting = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Receipt")!.Id, On,
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(100m), DrCr.Credit),
            }));

        var deliveryType = c.FindVoucherTypeByName("Delivery Note")!;
        var stock = new InventoryPostingService(c).Post(new InventoryVoucher(
            Guid.NewGuid(), deliveryType.Id, On,
            new[] { new InventoryAllocation(b.ItemId, b.GodownId, 5m, StockDirection.Outward, Money.FromRupees(10m)) }));

        // The premise: they really do tie. If seeding ever stops producing No. 1 for both, this test is measuring
        // nothing and says so here rather than passing quietly.
        Assert.Equal(accounting.Number, stock.Number);

        var ids = DayBook.Build(c, FyStart, AsOf).Select(r => r.VoucherId).ToList();
        Assert.True(ids.IndexOf(stock.Id) < ids.IndexOf(accounting.Id),
            "The (Date, Number) tie was not broken by type name: 'Delivery Note' sorts before 'Receipt' "
            + "ordinally, so dropping the third sort key leaves the order at whichever loop appended first.");
    }

    /// <summary>
    /// A Physical Stock count carries no rate — it is a statement about the shelf, not a valuation — so its Day
    /// Book amount is zero rather than a fabricated figure.
    /// </summary>
    [Fact]
    public void A_physical_count_lists_at_zero_rather_than_at_an_invented_valuation()
    {
        var b = Seed("Physical Co");
        var type = b.Company.FindVoucherTypeByName("Physical Stock")!;
        var v = new InventoryPostingService(b.Company).Post(InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), type.Id, On,
            new[] { new PhysicalStockLine(b.ItemId, b.GodownId, 7m, null) }));

        var row = Assert.Single(DayBook.Build(b.Company, FyStart, AsOf), r => r.VoucherId == v.Id);
        Assert.Equal(Money.Zero, row.Amount);
        Assert.True(row.IsInventory);
    }

    /// <summary>The movement value is rate x quantity — the SAME derivation the inventory registers ship, so the
    /// Day Book and the registers cannot state two different figures for one voucher.</summary>
    [Fact]
    public void A_stock_movements_Day_Book_amount_is_the_value_of_the_stock_it_moved()
    {
        var b = Seed("Value Co");
        var receipt = PostReceipt(b, qty: 10m, rate: 50m);

        var row = Assert.Single(DayBook.Build(b.Company, FyStart, AsOf), r => r.VoucherId == receipt.Id);
        Assert.Equal(Money.FromRupees(500m), row.Amount);
    }

    // =============================================================== the stock consequence

    /// <summary>
    /// 🔴 <b>THE CONSEQUENCE TEST THE WHOLE FEATURE EXISTS FOR: cancelling a receipt moves closing stock back.</b>
    /// Asserting the flag alone would pass over an engine that flagged the voucher and left the stock on the
    /// shelf — which is the shape of every "shipped green, held defects" slice this project has filed.
    /// </summary>
    [Fact]
    public void Cancelling_a_receipt_moves_closing_stock_back()
    {
        var b = Seed("Stock Consequence Co");
        var receipt = PostReceipt(b, qty: 10m, rate: 50m);

        var ledger = new InventoryLedger(b.Company);
        Assert.Equal(10m, ledger.OnHand(b.ItemId, AsOf));

        new InventoryPostingService(b.Company).Cancel(receipt.Id);

        Assert.Equal(0m, ledger.OnHand(b.ItemId, AsOf));
        Assert.True(b.Company.FindInventoryVoucher(receipt.Id)!.Cancelled);
    }

    /// <summary>Deleting a receipt likewise un-moves the stock, and the voucher leaves the book entirely.</summary>
    [Fact]
    public void Deleting_a_receipt_moves_closing_stock_back_and_removes_the_voucher()
    {
        var b = Seed("Delete Consequence Co");
        var receipt = PostReceipt(b, qty: 6m, rate: 20m);
        var ledger = new InventoryLedger(b.Company);
        Assert.Equal(6m, ledger.OnHand(b.ItemId, AsOf));

        new InventoryPostingService(b.Company).Delete(receipt.Id);

        Assert.Equal(0m, ledger.OnHand(b.ItemId, AsOf));
        Assert.Null(b.Company.FindInventoryVoucher(receipt.Id));
        Assert.DoesNotContain(DayBook.Build(b.Company, FyStart, AsOf), r => r.VoucherId == receipt.Id);
    }

    /// <summary>A cancelled stock voucher STILL LISTS in the Day Book, flagged — that is the whole evidence value
    /// of Cancel over Delete, and it keeps its number.</summary>
    [Fact]
    public void A_cancelled_stock_voucher_still_lists_flagged_and_keeps_its_number()
    {
        var b = Seed("Cancelled Listing Co");
        var receipt = PostReceipt(b, qty: 3m, rate: 10m);
        var numberBefore = receipt.Number;

        new InventoryPostingService(b.Company).Cancel(receipt.Id);

        var row = Assert.Single(DayBook.Build(b.Company, FyStart, AsOf), r => r.VoucherId == receipt.Id);
        Assert.True(row.IsCancelled);
        Assert.Equal(numberBefore, b.Company.FindInventoryVoucher(receipt.Id)!.Number);
    }

    // =============================================================== the edit log

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR THE DECLARED GAP.</b> Cancelling a stock voucher records an audit entry whose
    /// snapshot is the state BEFORE the flag moved. On <c>main</c> the log is empty after this call.
    /// </summary>
    [Fact]
    public void Cancelling_a_stock_voucher_records_an_edit_log_entry_of_the_state_before()
    {
        var b = Seed("Stock Audit Co");
        var receipt = PostReceipt(b, qty: 10m, rate: 50m);

        var entry = new InventoryPostingService(b.Company).Cancel(receipt.Id);

        Assert.Same(entry, Assert.Single(b.Company.VoucherEditLog));
        Assert.Equal(receipt.Id, entry.VoucherId);
        Assert.Equal(VoucherEditVerb.Cancel, entry.Verb);

        // The voucher IS cancelled now; the snapshot says it was not. That ordering is the whole content of a
        // before-state — a snapshot taken one line later would record the after-state instead.
        Assert.True(b.Company.FindInventoryVoucher(receipt.Id)!.Cancelled);
        Assert.Contains("\"Cancelled\":false", entry.BeforeSnapshot, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deleting records the voucher before it is removed, and the entry OUTLIVES it — for a delete this entry is
    /// the only remaining evidence the movement was ever posted.
    /// </summary>
    [Fact]
    public void Deleting_a_stock_voucher_records_an_entry_that_outlives_the_voucher()
    {
        var b = Seed("Stock Delete Audit Co");
        var receipt = PostReceipt(b, qty: 4m, rate: 12m);

        var entry = new InventoryPostingService(b.Company).Delete(receipt.Id);

        Assert.Null(b.Company.FindInventoryVoucher(receipt.Id));
        Assert.Same(entry, Assert.Single(b.Company.VoucherEditLog));
        Assert.Equal(VoucherEditVerb.Delete, entry.Verb);
        // The snapshot carries the content, so an auditor can see WHAT was destroyed and not merely that
        // something was. 12 x 4 = the line that is gone.
        Assert.Contains("\"Quantity\":4", entry.BeforeSnapshot, StringComparison.Ordinal);
    }

    /// <summary>A verb that THROWS leaves no entry behind — a log line for an act that did not happen is the one
    /// lie an append-only audit record must not tell.</summary>
    [Fact]
    public void A_verb_on_an_unknown_voucher_records_nothing()
    {
        var b = Seed("No Phantom Entry Co");
        var service = new InventoryPostingService(b.Company);

        Assert.Throws<InvalidOperationException>(() => service.Cancel(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => service.Delete(Guid.NewGuid()));

        Assert.Empty(b.Company.VoucherEditLog);
    }

    /// <summary>
    /// The compensating undo for a cancel whose save did not commit clears BOTH halves — the flag and the log
    /// line. Undoing only the flag would leave the log asserting a cancellation that never reached disk.
    /// </summary>
    [Fact]
    public void Discarding_an_uncommitted_cancel_undoes_the_flag_AND_the_log_line()
    {
        var b = Seed("Rollback Co");
        var receipt = PostReceipt(b, qty: 9m, rate: 11m);
        var service = new InventoryPostingService(b.Company);

        var entry = service.Cancel(receipt.Id);
        Assert.True(b.Company.FindInventoryVoucher(receipt.Id)!.Cancelled);
        Assert.Single(b.Company.VoucherEditLog);

        service.DiscardUncommittedCancel(receipt.Id, entry);

        Assert.False(b.Company.FindInventoryVoucher(receipt.Id)!.Cancelled);
        Assert.Empty(b.Company.VoucherEditLog);
        // …and the stock is back on the shelf, which is the half a flag-only rollback would have missed.
        Assert.Equal(9m, new InventoryLedger(b.Company).OnHand(b.ItemId, AsOf));
    }

    /// <summary>The rollback refuses an entry that does not describe this voucher's cancel, rather than removing
    /// whatever happens to be last.</summary>
    [Fact]
    public void Discarding_refuses_an_entry_that_describes_a_different_act()
    {
        var b = Seed("Rollback Guard Co");
        var first = PostReceipt(b, qty: 2m, rate: 3m);
        var second = PostReceipt(b, qty: 5m, rate: 7m, date: On.AddDays(1));
        var service = new InventoryPostingService(b.Company);

        var entry = service.Cancel(first.Id);

        Assert.Throws<InvalidOperationException>(() => service.DiscardUncommittedCancel(second.Id, entry));
        Assert.Single(b.Company.VoucherEditLog);
        Assert.True(b.Company.FindInventoryVoucher(first.Id)!.Cancelled);
    }
}
