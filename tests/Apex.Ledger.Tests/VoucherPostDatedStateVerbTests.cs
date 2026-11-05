using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 5.8 — moving <see cref="Voucher.PostDated"/> / <see cref="InventoryVoucher.PostDated"/> on a POSTED
/// voucher, and census 5.7 — <see cref="InventoryVoucher.Optional"/> (schema v69).</b>
///
/// <para><b>The defect these close.</b> The census recorded "zero post-construction writers, so the flag can never
/// be cleared when the cheque clears": <c>LedgerService.Replace</c> refuses a provisional-state move by name and no
/// <c>SetPostDated</c> verb existed. On the pure-stock side <c>InventoryVoucher</c> had no Optional member at all.</para>
///
/// <para><b>R7 grounding, opened by content 2026-11-02.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c>: <i>"Mark vouchers Post-Dated by
/// pressing Ctrl+T (Post-Dated) button from the Button Bar."</i>, <i>"This button toggles with Current."</i>, and
/// <i>"Any voucher in TallyPrime can be marked as Optional by pressing Ctrl+L (Optional) during voucher entry or in
/// alteration mode"</i>. Offering Ctrl+T on an ALTERED voucher is partly ours (IV-310).</para>
///
/// <para>Every assertion is on an artefact the book reports — the Post-Dated / Optional exception register, the
/// edit log, or the on-hand quantity — never only on the flag the verb just wrote.</para>
/// </summary>
public class VoucherPostDatedStateVerbTests
{
    private static readonly DateTimeOffset Instant = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly Money Cheque = Money.FromRupees(4_321.07m);

    private static (LifecycleBook Book, LedgerService Service, Guid Id) BookWithPostDated()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = new LedgerService(book.Company, () => Instant);
        var id = Guid.NewGuid();
        service.Post(new Voucher(
            id, book.SalesType.Id, LifecycleBook.BooksBegin.AddDays(14),
            new[]
            {
                new EntryLine(book.Customer.Id, Cheque, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, Cheque, DrCr.Credit),
            },
            narration: "post-dated cheque", partyId: book.Customer.Id, postDated: true));
        return (book, service, id);
    }

    private static IReadOnlyList<DayBookRow> PostDatedRegister(Company c)
        => ExceptionVouchers.Build(c, ExceptionVoucherKind.PostDated,
            LifecycleBook.BooksBegin, LifecycleBook.BooksBegin.AddYears(1).AddDays(-1));

    /// <summary>
    /// Marking a post-dated voucher Current takes it off the Post-Dated register and logs a
    /// <see cref="VoucherEditVerb.SetPostDated"/> entry. <b>Mutation:</b> making <c>SetPostDated</c> return
    /// before writing the flag reddens the register assertion.
    /// </summary>
    [Fact]
    public void Marking_a_posted_post_dated_voucher_current_takes_it_off_the_post_dated_register_and_logs_it()
    {
        var (book, service, id) = BookWithPostDated();
        Assert.Contains(PostDatedRegister(book.Company), r => r.VoucherId == id);
        var logBefore = book.Company.VoucherEditLog.Count;

        service.SetPostDated(id, false);

        Assert.DoesNotContain(PostDatedRegister(book.Company), r => r.VoucherId == id);
        Assert.Equal(logBefore + 1, book.Company.VoucherEditLog.Count);
        var entry = book.Company.LastVoucherEditLogEntry!;
        Assert.Equal(VoucherEditVerb.SetPostDated, entry.Verb);
        Assert.Equal(id, entry.VoucherId);

        // And back: the vendor's button "toggles with Current".
        service.SetPostDated(id, true);
        Assert.Contains(PostDatedRegister(book.Company), r => r.VoucherId == id);
    }

    /// <summary>A move to the value already held is a no-op and appends NO log line.</summary>
    [Fact]
    public void Setting_post_dated_to_its_current_value_appends_no_log_line()
    {
        var (book, service, id) = BookWithPostDated();
        var logBefore = book.Company.VoucherEditLog.Count;
        service.SetPostDated(id, true);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Count);
    }

    /// <summary>A cancelled voucher is refused: it affects nothing whatever its date.</summary>
    [Fact]
    public void A_cancelled_voucher_cannot_be_marked_current()
    {
        var (book, service, id) = BookWithPostDated();
        service.Cancel(id);
        Assert.Throws<InvalidOperationException>(() => service.SetPostDated(id, false));
    }

    /// <summary>
    /// The compensating undo restores the flag and removes ITS entry only; it refuses to "undo" when the flag
    /// already holds the restore value, so it cannot be used as a setter.
    /// </summary>
    [Fact]
    public void Discarding_an_uncommitted_post_dated_change_restores_the_register_and_the_log()
    {
        var (book, service, id) = BookWithPostDated();
        var logBefore = book.Company.VoucherEditLog.Count;
        service.SetPostDated(id, false);
        var entry = book.Company.LastVoucherEditLogEntry!;

        service.DiscardUncommittedPostDatedChange(id, entry, restoreTo: true);

        Assert.Contains(PostDatedRegister(book.Company), r => r.VoucherId == id);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Count);
        Assert.Throws<InvalidOperationException>(
            () => service.DiscardUncommittedPostDatedChange(id, entry, restoreTo: true));
    }

    // ------------------------------------------------------------------------------------- the stock aggregate

    private sealed record StockBook(Company Company, InventoryPostingService Posting, Guid ItemId, Guid GodownId,
        Guid ReceiptId, DateOnly AsOf);

    private static StockBook StockBookWith(bool optional, bool postDated)
    {
        var c = CompanyFactory.CreateSeeded("Stock Flags Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Flag Widget", masters.CreateStockGroup("Flag Goods").Id,
            masters.CreateSimpleUnit("FlNos", "Flag Numbers").Id);
        var godown = c.MainLocation!;
        masters.AddOpeningBalance(item.Id, godown.Id, 4m, Money.FromRupees(10.01m));
        var posting = new InventoryPostingService(c);
        var receipt = new InventoryVoucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id, new DateOnly(2024, 4, 9),
            new[] { new InventoryAllocation(item.Id, godown.Id, 9m, StockDirection.Inward, Money.FromRupees(12.03m)) },
            number: 0, narration: null, partyId: null, cancelled: false, postDated: postDated) { Optional = optional };
        posting.Post(receipt);
        return new StockBook(c, posting, item.Id, godown.Id, receipt.Id, new DateOnly(2024, 6, 30));
    }

    private static decimal OnHand(StockBook b) => new InventoryLedger(b.Company).OnHand(b.ItemId, b.GodownId, b.AsOf);

    private static decimal ClosingValue(StockBook b)
        => new StockValuationService(b.Company).TotalClosingStockValue(b.AsOf).Amount;

    /// <summary>
    /// 🔴 <b>An Optional stock voucher moves NO stock and NO closing value, and regularising it puts both back.</b>
    /// Asserted on the on-hand quantity AND the closing-stock value, which feed the Stock Summary and the Balance
    /// Sheet. <b>Mutations:</b> removing <c>|| v.Optional</c> from <c>InventoryLedger.Counts</c> reddens the on-hand
    /// assertion; removing it from <c>StockValuationService.Counts</c> reddens the value assertion.
    /// </summary>
    [Fact]
    public void An_optional_stock_voucher_moves_no_stock_until_it_is_regularised()
    {
        var b = StockBookWith(optional: true, postDated: false);
        // Exactly the opening 4 @ 10.01: an Optional receipt that leaked into the cost events would average its
        // 12.03 rate into these 4 units even though the quantity stayed at 4 (that is what the value pin catches).
        Assert.Equal(4m, OnHand(b));
        Assert.Equal(40.04m, ClosingValue(b));

        b.Posting.SetOptional(b.ReceiptId, false);

        Assert.Equal(13m, OnHand(b));
        Assert.True(ClosingValue(b) > 140m, $"closing value {ClosingValue(b)} did not take in the 9 @ 12.03");
        Assert.Equal(VoucherEditVerb.SetOptional, b.Company.LastVoucherEditLogEntry!.Verb);
    }

    /// <summary>
    /// The stock verbs' two guards (wave-10 verification, second pass; the first was unpinned): a move to the value
    /// already held is a no-op with NO log line, and a cancelled stock voucher is refused in both directions.
    /// <b>Mutations:</b> deleting <c>if (read(v) == value) return v;</c> from <c>InventoryPostingService.SetFlag</c>
    /// (measured: the whole Apex.Ledger.Tests project stayed green) reddens the log-count assertions; deleting the
    /// cancelled refusal reddens the throws.
    /// </summary>
    [Fact]
    public void A_stock_flag_set_to_its_current_value_logs_nothing_and_a_cancelled_stock_voucher_is_refused()
    {
        var b = StockBookWith(optional: false, postDated: false);
        var logBefore = b.Company.VoucherEditLog.Count;

        b.Posting.SetOptional(b.ReceiptId, false);
        b.Posting.SetPostDated(b.ReceiptId, false);
        Assert.Equal(logBefore, b.Company.VoucherEditLog.Count);
        Assert.Equal(13m, OnHand(b));

        b.Posting.Cancel(b.ReceiptId);
        var logAfterCancel = b.Company.VoucherEditLog.Count;
        Assert.Throws<InvalidOperationException>(() => b.Posting.SetOptional(b.ReceiptId, true));
        Assert.Throws<InvalidOperationException>(() => b.Posting.SetPostDated(b.ReceiptId, true));
        var cancelled = b.Company.FindInventoryVoucher(b.ReceiptId)!;
        Assert.False(cancelled.Optional);
        Assert.False(cancelled.PostDated);
        Assert.Equal(logAfterCancel, b.Company.VoucherEditLog.Count);
    }

    /// <summary>The Optional register lists an Optional stock voucher (pre-v69 it read the accounting aggregate
    /// only). <b>Mutation:</b> restoring the <c>!row.IsInventory &amp;&amp;</c> arm reddens it.</summary>
    [Fact]
    public void The_optional_register_lists_an_optional_stock_voucher()
    {
        var b = StockBookWith(optional: true, postDated: false);
        var rows = ExceptionVouchers.Build(b.Company, ExceptionVoucherKind.Optional,
            new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));
        var row = Assert.Single(rows);
        Assert.True(row.IsInventory);
        Assert.Equal(b.ReceiptId, row.VoucherId);
    }

    /// <summary>
    /// <c>Replace</c> refuses a replacement that moves either provisional flag — the move must go through its own
    /// verb so it is logged as SetOptional / SetPostDated rather than as an Alter. <b>Mutation:</b> deleting the v69
    /// guard in <c>InventoryPostingService.Replace</c> reddens both throws.
    /// </summary>
    [Fact]
    public void Replace_refuses_a_stock_replacement_that_moves_optional_or_post_dated()
    {
        var b = StockBookWith(optional: false, postDated: true);
        var posted = b.Company.FindInventoryVoucher(b.ReceiptId)!;

        InventoryVoucher Copy(bool optional, bool postDated) => new(posted.Id, posted.TypeId, posted.Date,
            posted.Allocations.ToList(), number: posted.Number, narration: posted.Narration,
            partyId: posted.PartyId, cancelled: false, postDated: postDated) { Optional = optional };

        Assert.Throws<InvalidOperationException>(() => b.Posting.Replace(b.ReceiptId, Copy(true, true)));
        Assert.Throws<InvalidOperationException>(() => b.Posting.Replace(b.ReceiptId, Copy(false, false)));

        // Through the verbs instead, both moves land and are logged with their own verbs.
        b.Posting.SetPostDated(b.ReceiptId, false);
        Assert.Equal(VoucherEditVerb.SetPostDated, b.Company.LastVoucherEditLogEntry!.Verb);
        Assert.False(b.Company.FindInventoryVoucher(b.ReceiptId)!.PostDated);
    }
}
