using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 5.7 — the vendor's Ctrl+L (Regular) route, driven through the real screen path.</b>
///
/// <para><b>The defect these close.</b> Both halves of the route were already built — <c>ForAlter</c> rehydrates
/// <c>IsOptional</c> from the posted voucher and <c>ToggleOptional</c> flips it on Ctrl+L — and then Ctrl+A
/// THREW, because §7.4 of <c>LedgerService.Replace</c> refuses a replacement that moves the provisional-state
/// vector. The operator pressed the vendor's documented keys in the vendor's documented order and got
/// <i>"Cannot alter: Replace does not change a voucher's provisional state (voucher …)"</i>. The census recorded
/// the consequence as "a posted Optional voucher can never be regularised (zero post-construction writers)".</para>
///
/// <para><b>R7 grounding, opened by content 2026-10-09.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c>: <i>"Any voucher in TallyPrime
/// can be marked as Optional by pressing Ctrl+L (Optional) during voucher entry or in alteration mode"</i> and
/// <i>"Once the actual date of such transaction occurs you can regularise the transaction by opening it and
/// pressing Ctrl+L (Regular)."</i> <c>help.tallysolutions.com/keyboard-shortcuts-tally-prime/</c> lists
/// <c>Ctrl+L</c> as <i>"To mark a voucher as Optional"</i> under Vouchers &amp; Masters.</para>
///
/// <para>🔴 <b>WHY THE ENGINE SUITE'S VERSION OF THIS IS NOT ENOUGH.</b> This project has filed a writer-only
/// feature three times — correct engine code with no route to it. <c>VoucherOptionalStateVerbTests</c> pins the
/// verb; these pin that the SCREEN reaches it, by going through <c>ForAlter</c> → <c>ToggleOptional</c> →
/// <c>AcceptAlteration</c> and then reading the figure back off the book.</para>
/// </summary>
public sealed class VoucherOptionalRegulariseRouteTests
{
    /// <summary>The Dr leg's closing balance as the books report it — the figure the Optional flag governs.</summary>
    private static decimal Closing(AlterationBook book)
    {
        var dr = book.Company.FindLedgerByName($"Dr Leg {VoucherBaseType.Journal}")!;
        return LedgerBalances.SignedClosing(
            book.Company, dr, book.Company.FinancialYearStart.AddYears(1));
    }

    /// <summary>Posts a two-leg Journal through the real entry screen with Ctrl+L pressed before accept.</summary>
    private static Voucher PostOptional(AlterationBook book, decimal amount, string narration)
    {
        var dr = book.Company.FindLedgerByName($"Dr Leg {VoucherBaseType.Journal}")
                 ?? book.Ledger($"Dr Leg {VoucherBaseType.Journal}", "Indirect Expenses");
        var cr = book.Company.FindLedgerByName($"Cr Leg {VoucherBaseType.Journal}")
                 ?? book.Ledger($"Cr Leg {VoucherBaseType.Journal}", "Indirect Incomes");
        var text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return book.Post(
            VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, text), (cr, DrCr.Credit, text) },
            narration,
            configure: e => e.ToggleOptional());   // Ctrl+L, at entry time
    }

    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 <b>THE ROW'S DEFECT, END TO END.</b> Before this work the <c>AcceptAlteration</c> below returned
    /// <c>false</c> with the engine's §7.4 refusal in <c>Message</c>, and the voucher stayed Optional for ever.
    /// </summary>
    [Fact]
    public void Ctrl_L_on_a_posted_Optional_voucher_regularises_it_and_puts_its_money_on_the_books()
    {
        using var book = AlterationBook.New("regularise");
        var posted = PostOptional(book, 7431.65m, "Rent accrual - provisional");

        // The precondition IS the defect's shape: posted, on the book, money not in the balance.
        var whileOptional = Closing(book);
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);

        var open = book.ForAlter(posted.Id);

        // 🔴 ForAlter must have REHYDRATED the flag, or Ctrl+L below would set it rather than clear it.
        Assert.True(open.Entry!.IsOptional);

        open.Entry.ToggleOptional();                                  // Ctrl+L (Regular)
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message); // Ctrl+A

        // The observable artefact: the voucher read back OFF THE BOOK, and the balance it now moves.
        var live = book.Company.FindVoucher(posted.Id)!;
        Assert.False(live.Optional);
        Assert.Equal(whileOptional + 7431.65m, Closing(book));

        // Its number was NOT reissued and the voucher did not move in the book.
        Assert.Equal(posted.Number, live.Number);
        Assert.Equal(posted.Id, live.Id);

        // The audit trail records the BEFORE state, which is the half a past wave shipped falsified.
        //
        // 🔴 TWO ENTRIES, AND THE COUNT IS ASSERTED RATHER THAN GLOSSED. The screen applies the flag through
        // `SetOptional` (entry 1) and then runs the content `Replace` (entry 2), and `Replace` records an Alter
        // unconditionally — it does not compare content first. This test was first written expecting ONE entry;
        // that was the test being wrong, not the code, so it is corrected here rather than the product bent to
        // it. The chain is still honest and still reconstructs: entry 1's before-state is the Optional voucher,
        // entry 2's is the regular voucher with the same content, and the live voucher matches entry 2.
        Assert.Equal(2, book.Company.VoucherEditLog.Count);
        var flagMove = book.Company.VoucherEditLog[0];
        Assert.Equal(VoucherEditVerb.Alter, flagMove.Verb);
        Assert.Equal(posted.Id, flagMove.VoucherId);
        Assert.Contains("\"Optional\":true", flagMove.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("\"Optional\":false",
            book.Company.VoucherEditLog[1].BeforeSnapshot, StringComparison.Ordinal);

        // …and it reached disk, because AcceptAlteration saves.
        var reopened = book.Storage.Load(book.Storage.ListCompanies()
            .Single(e => e.Name == book.Company.Name));
        Assert.False(reopened.FindVoucher(posted.Id)!.Optional);
        Assert.Contains("\"Optional\":true",
            reopened.VoucherEditLog[0].BeforeSnapshot, StringComparison.Ordinal);
    }

    /// <summary>The other direction the same page attests: "in alteration mode".</summary>
    [Fact]
    public void Ctrl_L_on_a_posted_live_voucher_marks_it_Optional_and_takes_its_money_off_the_books()
    {
        using var book = AlterationBook.New("provisionalise");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 5120.35m, "live when keyed");
        var whileLive = Closing(book);
        Assert.False(book.Company.FindVoucher(posted.Id)!.Optional);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.Entry!.IsOptional);
        open.Entry.ToggleOptional();
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var live = book.Company.FindVoucher(posted.Id)!;
        Assert.True(live.Optional);
        Assert.Equal(whileLive - 5120.35m, Closing(book));

        // 🔴 OPTIONAL IS NOT A DELETION — the voucher is still on the book under its own number, which is what
        // makes the Optional Vouchers Register able to list it and what distinguishes this from Alt+D.
        Assert.Equal(posted.Number, live.Number);

        // Two entries, for the reason the regularise test above states: the flag verb, then the content Replace.
        Assert.Equal(2, book.Company.VoucherEditLog.Count);
        Assert.Contains("\"Optional\":false",
            book.Company.VoucherEditLog[0].BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("\"Optional\":true",
            book.Company.VoucherEditLog[1].BeforeSnapshot, StringComparison.Ordinal);
    }

    /// <summary>
    /// A content change and the flag move in ONE accept. The screen applies the flag through its own verb and
    /// then lets Replace handle the content, so both have to land — and the log has to show both.
    /// </summary>
    [Fact]
    public void Regularising_and_amending_the_amount_in_the_same_accept_both_land()
    {
        using var book = AlterationBook.New("regularise2");
        var posted = PostOptional(book, 7431.65m, "as first keyed");
        var whileOptional = Closing(book);

        var open = book.ForAlter(posted.Id);
        open.Entry!.ToggleOptional();
        open.Entry.Lines[0].AmountText = "8888.11";
        open.Entry.Lines[1].AmountText = "8888.11";
        open.Entry.Narration = "as corrected and regularised";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var live = book.Company.FindVoucher(posted.Id)!;
        Assert.False(live.Optional);
        Assert.Equal("as corrected and regularised", live.Narration);

        // The books moved by the CORRECTED figure, not the originally keyed one.
        Assert.Equal(whileOptional + 8888.11m, Closing(book));

        // Two log lines, in the order the screen applied them: the flag move, then the content replace. Both
        // snapshots are before-states, so the first says Optional and carries the OLD amount.
        Assert.Equal(2, book.Company.VoucherEditLog.Count);
        var first = book.Company.VoucherEditLog[0];
        Assert.Contains("\"Optional\":true", first.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("7431.65", first.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("as first keyed", first.BeforeSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("8888.11", first.BeforeSnapshot, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>A REFUSED ALTERATION MUST NOT LEAVE THE FLAG MOVED.</b> The screen applies the flag BEFORE Replace
    /// (Replace reads the live flag to decide its guard), so an out-of-balance refusal lands AFTER the flag has
    /// already moved — and has to put it back. Without the rollback this test leaves a regularised voucher whose
    /// money is on the books although the operator's edit was rejected: a balance moved by a refusal.
    /// </summary>
    [Fact]
    public void A_refused_alteration_puts_the_Optional_flag_back_and_records_nothing()
    {
        using var book = AlterationBook.New("regulariseref");
        var posted = PostOptional(book, 7431.65m, "stays provisional");
        var whileOptional = Closing(book);

        var open = book.ForAlter(posted.Id);
        open.Entry!.ToggleOptional();                 // Ctrl+L (Regular)
        open.Entry.Lines[0].AmountText = "9999.99";   // …credit side left alone: out of balance.
        Assert.False(open.Entry.AcceptAlteration());

        // Everything exactly as it was found: still Optional, still off the books, nothing in the log.
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);
        Assert.Equal(whileOptional, Closing(book));
        Assert.Empty(book.Company.VoucherEditLog);
        Assert.Equal("stays provisional", book.Company.FindVoucher(posted.Id)!.Narration);
    }

    /// <summary>
    /// An alteration that does NOT touch Ctrl+L must behave exactly as it always did — one log line, no flag
    /// verb, flag preserved. The pre-step is additive and this is the regression pin that says so.
    /// </summary>
    [Fact]
    public void An_ordinary_alteration_of_an_Optional_voucher_leaves_it_Optional_and_logs_once()
    {
        using var book = AlterationBook.New("regularisekeep");
        var posted = PostOptional(book, 7431.65m, "as first keyed");
        var whileOptional = Closing(book);

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "8888.11";
        open.Entry.Lines[1].AmountText = "8888.11";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var live = book.Company.FindVoucher(posted.Id)!;
        Assert.True(live.Optional);

        // Still provisional, so the amended figure is STILL off the books — the balance did not move at all.
        Assert.Equal(whileOptional, Closing(book));
        Assert.Single(book.Company.VoucherEditLog);
    }
}
