using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 5.7 — moving <see cref="Voucher.Optional"/> on a POSTED voucher.</b>
///
/// <para><b>The defect these close.</b> The census recorded "a posted Optional voucher can never be regularised
/// (zero post-construction writers)". The engine had exactly ONE such writer,
/// <c>MarkOptionalAsRegular</c>, reachable only from a bank-statement import path, and no mirror for the other
/// direction; <c>Replace</c> refuses the change by name (§7.4). So a provisional entry could be listed by the
/// Optional Vouchers Register forever with no verb in the product able to resolve it.</para>
///
/// <para><b>R7 grounding, opened by content 2026-10-09.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c> attests BOTH directions on an
/// already-saved voucher: <i>"Any voucher in TallyPrime can be marked as Optional by pressing Ctrl+L (Optional)
/// during voucher entry or in alteration mode"</i> and <i>"Once the actual date of such transaction occurs you
/// can regularise the transaction by opening it and pressing Ctrl+L (Regular)."</i></para>
///
/// <para>🔴 <b>EVERY TEST HERE ASSERTS ON A FIGURE READ OFF THE BOOK</b> — the ledger closing balance that the
/// flag governs — and not on the flag it just set. <c>LedgerBalances.CountsAsOf</c> opens with
/// <c>if (v.Cancelled || v.Optional) return false;</c>, so the flag's whole meaning is "does this voucher's money
/// exist", and that is the thing worth pinning. A test that read <c>voucher.Optional</c> back would pass against
/// a verb that moved the flag and nothing else.</para>
/// </summary>
public class VoucherOptionalStateVerbTests
{
    private static readonly DateTimeOffset Instant =
        new(2026, 10, 9, 11, 22, 33, TimeSpan.FromHours(5.5));

    /// <summary>₹7,431.65 — odd paise (§7.5), so a rupee-rounded assertion would still see it move.</summary>
    private static readonly Money Provisional = Money.FromRupees(7431.65m);

    private const string ProvisionalNarration = "Rent accrual - provisional until the cheque is drawn";

    /// <summary>
    /// The eleven-invoice book plus ONE extra Sales voucher posted with <paramref name="optional"/>. Returns the
    /// book, a fixed-clock service, and the extra voucher's id.
    /// </summary>
    private static (LifecycleBook Book, LedgerService Service, Guid Id) BookWith(bool optional)
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = new LedgerService(book.Company, () => Instant);

        var id = Guid.NewGuid();
        service.Post(new Voucher(
            id,
            book.SalesType.Id,
            LifecycleBook.BooksBegin.AddDays(12),
            new[]
            {
                new EntryLine(book.Customer.Id, Provisional, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, Provisional, DrCr.Credit),
            },
            narration: ProvisionalNarration,
            partyId: book.Customer.Id,
            optional: optional));

        return (book, service, id);
    }

    /// <summary>The customer's closing balance as the books report it — the figure the Optional flag governs.</summary>
    private static decimal Closing(LifecycleBook book)
        => LedgerBalances.SignedClosing(book.Company, book.Customer, LifecycleBook.AsOf);

    // -------------------------------------------------------------------------------------------------
    // Regularising: Optional -> live. The vendor's "Ctrl+L (Regular)".
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Regularising_a_posted_Optional_voucher_puts_its_money_ON_the_books()
    {
        var (book, service, id) = BookWith(optional: true);

        // 🔴 THE PRECONDITION IS ITSELF THE DEFECT'S SHAPE: the voucher is posted and on the book, and its money
        // is NOT in the closing balance. That is what "provisional" means.
        var whileOptional = Closing(book);
        Assert.NotNull(book.Company.FindVoucher(id));
        Assert.DoesNotContain(
            book.Company.Vouchers.Where(v => LedgerBalances.CountsAsOf(v, LifecycleBook.AsOf)),
            v => v.Id == id);

        service.SetOptional(id, false);

        // The observable artefact: the books moved by the WHOLE voucher, and by exactly its amount.
        Assert.Equal(whileOptional + Provisional.Amount, Closing(book));
        Assert.Contains(
            book.Company.Vouchers.Where(v => LedgerBalances.CountsAsOf(v, LifecycleBook.AsOf)),
            v => v.Id == id);
    }

    [Fact]
    public void Regularising_records_an_edit_log_entry_whose_snapshot_says_the_voucher_WAS_Optional()
    {
        var (book, service, id) = BookWith(optional: true);

        service.SetOptional(id, false);

        // 🔴 THIS IS THE AUDIT-TRAIL ASSERTION, AND IT IS WHY THE VERB TAKES THE SNAPSHOT BEFORE IT MUTATES.
        // A past wave shipped an alteration that did not record what it changed; a log line that said
        // "Optional":false here would describe the AFTER state and be worthless to an auditor.
        var entry = Assert.Single(book.Company.VoucherEditLog);
        Assert.Equal(id, entry.VoucherId);
        Assert.Equal(VoucherEditVerb.Alter, entry.Verb);
        Assert.Equal(Instant, entry.RecordedAt);
        Assert.Contains("\"Optional\":true", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains(ProvisionalNarration, entry.BeforeSnapshot, StringComparison.Ordinal);

        // And the AFTER state is on the book, which is the pair the log's own design relies on.
        Assert.False(book.Company.FindVoucher(id)!.Optional);
    }

    [Fact]
    public void MarkOptionalAsRegular_still_regularises_after_delegating_to_SetOptional()
    {
        // The banking path (BankStatementVoucherCreation's "Mark as Regular & Reconcile") calls the older name.
        // Delegation must not have changed what it does.
        var (book, service, id) = BookWith(optional: true);
        var whileOptional = Closing(book);

        service.MarkOptionalAsRegular(id);

        Assert.Equal(whileOptional + Provisional.Amount, Closing(book));
        Assert.Contains("\"Optional\":true", Assert.Single(book.Company.VoucherEditLog).BeforeSnapshot,
            StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------
    // The other direction: live -> Optional, "in alteration mode".
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Marking_a_posted_live_voucher_Optional_takes_its_money_OFF_the_books()
    {
        var (book, service, id) = BookWith(optional: false);
        var whileLive = Closing(book);

        service.SetOptional(id, true);

        Assert.Equal(whileLive - Provisional.Amount, Closing(book));

        // It is still ON the book — Optional is not a deletion. That distinction is the point of the register.
        Assert.NotNull(book.Company.FindVoucher(id));
        Assert.Contains("\"Optional\":false", Assert.Single(book.Company.VoucherEditLog).BeforeSnapshot,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_the_flag_leaves_the_voucher_number_and_every_neighbour_untouched()
    {
        var (book, service, id) = BookWith(optional: true);

        var numbersBefore = book.Company.Vouchers.Select(v => (v.Id, v.Number)).ToList();
        var orderBefore = book.Company.Vouchers.Select(v => v.Id).ToList();

        service.SetOptional(id, false);

        // 🔴 NOTHING SILENTLY RENUMBERS NEIGHBOURS, and the voucher keeps its place in the book — the list index
        // is load-bearing (Company.ReplaceVoucherInternal) and a flag move has no business touching either.
        Assert.Equal(numbersBefore, book.Company.Vouchers.Select(v => (v.Id, v.Number)).ToList());
        Assert.Equal(orderBefore, book.Company.Vouchers.Select(v => v.Id).ToList());
    }

    // -------------------------------------------------------------------------------------------------
    // Refusals and no-ops.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Setting_the_flag_to_what_it_already_holds_appends_NO_log_entry()
    {
        var (book, service, id) = BookWith(optional: true);

        service.SetOptional(id, true);

        // An edit log that records non-events is as misleading as one that misses real ones.
        Assert.Empty(book.Company.VoucherEditLog);
    }

    [Fact]
    public void A_cancelled_voucher_is_refused_BY_NAME_and_its_flag_does_not_move()
    {
        var (book, service, id) = BookWith(optional: true);
        service.Cancel(id);
        var logAfterCancel = book.Company.VoucherEditLog.Count;
        var closing = Closing(book);

        var ex = Assert.Throws<InvalidOperationException>(() => service.SetOptional(id, false));
        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The refusal left everything exactly as it found it — no flag move, no log line, no balance move.
        Assert.True(book.Company.FindVoucher(id)!.Optional);
        Assert.Equal(logAfterCancel, book.Company.VoucherEditLog.Count);
        Assert.Equal(closing, Closing(book));
    }

    [Fact]
    public void An_unknown_voucher_is_refused()
        => Assert.Throws<InvalidOperationException>(
            () => BookWith(optional: true).Service.SetOptional(Guid.NewGuid(), false));

    // -------------------------------------------------------------------------------------------------
    // 🔴 THE GUARD THIS WORK ROUTES AROUND IS NOT WEAKENED. Pinned here so a later change that "simplifies"
    // CommitAlteration by relaxing §7.4 goes red in the ENGINE suite, where the guard lives.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_still_REFUSES_a_replacement_that_moves_the_Optional_flag()
    {
        var (book, service, id) = BookWith(optional: true);
        var live = book.Company.FindVoucher(id)!;

        var movesTheFlag = new Voucher(
            id, live.TypeId, live.Date,
            live.Lines.Select(l => new EntryLine(l.LedgerId, l.Amount, l.Side)).ToArray(),
            number: live.Number, narration: live.Narration, partyId: live.PartyId,
            optional: false);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Replace(id, movesTheFlag));
        Assert.Contains("provisional state", ex.Message, StringComparison.Ordinal);

        // Still Optional, still off the books: the refusal is real, not advisory.
        Assert.True(book.Company.FindVoucher(id)!.Optional);
    }

    // -------------------------------------------------------------------------------------------------
    // The compensating undo, for an alteration whose save did not commit.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Discarding_an_uncommitted_change_puts_the_money_back_AND_drops_the_log_line()
    {
        var (book, service, id) = BookWith(optional: true);
        var whileOptional = Closing(book);

        service.SetOptional(id, false);
        var entry = Assert.Single(book.Company.VoucherEditLog);

        service.DiscardUncommittedOptionalChange(id, entry, restoreTo: true);

        // Both halves undone: the books are back where they were, and no line claims an edit that never
        // reached disk.
        Assert.Equal(whileOptional, Closing(book));
        Assert.Empty(book.Company.VoucherEditLog);
    }

    [Fact]
    public void Discarding_refuses_a_restoreTo_that_describes_no_change_so_it_cannot_be_used_as_a_setter()
    {
        var (book, service, id) = BookWith(optional: true);
        service.SetOptional(id, false);
        var entry = Assert.Single(book.Company.VoucherEditLog);
        var closing = Closing(book);

        // The flag is already false; asking to "restore" it to false describes no rollback at all.
        var ex = Assert.Throws<InvalidOperationException>(
            () => service.DiscardUncommittedOptionalChange(id, entry, restoreTo: false));
        Assert.Contains("does not set the flag", ex.Message, StringComparison.Ordinal);

        // 🔴 AND THE REFUSAL DID NOT EAT THE LOG LINE. The engine discards the entry FIRST only once it has
        // decided to proceed; a refusal that removed the evidence would be an audit-erasure API.
        Assert.Single(book.Company.VoucherEditLog);
        Assert.Equal(closing, Closing(book));
    }

    [Fact]
    public void Discarding_refuses_an_entry_that_is_not_the_most_recent()
    {
        var (book, service, id) = BookWith(optional: true);
        service.SetOptional(id, false);
        var optionalEntry = Assert.Single(book.Company.VoucherEditLog);

        // A later, unrelated verb lands on top of it.
        service.Cancel(book.TenthId);

        Assert.Throws<InvalidOperationException>(
            () => service.DiscardUncommittedOptionalChange(id, optionalEntry, restoreTo: true));

        // Nothing moved: the log is append-only apart from the bounded most-recent discard.
        Assert.Equal(2, book.Company.VoucherEditLog.Count);
        Assert.False(book.Company.FindVoucher(id)!.Optional);
    }
}
