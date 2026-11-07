using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Wave-10 independent verification (track gd) — the accounting screen's REFUSAL arms after Ctrl+T, which the
/// review pass left unpinned.</b> <c>ProvisionalFlagRollbackTests</c> drove the accounting rollback only through a
/// failed SAVE, with Post-Dated alone. <c>VoucherEntryViewModel.CommitAlteration</c> also undoes the Ctrl+T move in
/// the <c>Replace</c> refusal arms (out of balance; invalid), and when Ctrl+L moved too it must undo the two in LIFO
/// order — <c>DiscardUncommittedOptionalChange</c> refuses an entry that is not the newest, and
/// <c>RollBackOptional</c> / <c>RollBackPostDated</c> swallow that refusal, so a wrong order is SILENT: a refused
/// alteration would leave the voucher regularised and a fictitious SetOptional line in the edit log.
///
/// <para>Measured before this file: removing <c>RollBackPostDated</c> from the out-of-balance catch, or swapping it
/// below <c>RollBackOptional</c> there, left Apex.Desktop.Tests green. Each assertion reads the flags off the book,
/// the edit log by identity and a byte-identical canonical export.</para>
/// </summary>
public sealed class ProvisionalFlagRefusalRollbackTests
{
    private static Voucher PostJournal(AlterationBook book, bool optional)
    {
        var dr = book.Ledger("Refusal Rent", "Indirect Expenses");
        var cr = book.Ledger("Refusal Income", "Indirect Incomes");
        return book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "3170.43"), (cr, DrCr.Credit, "3170.43") }, "pdc refused",
            configure: e =>
            {
                e.TogglePostDated();                 // Ctrl+T at entry
                if (optional) e.ToggleOptional();    // Ctrl+L at entry
            });
    }

    /// <summary>
    /// A post-dated Journal, Ctrl+T (Current) in alteration, and an out-of-balance edit that <c>Replace</c> refuses.
    /// <b>Mutation:</b> deleting <c>RollBackPostDated</c> from the <c>UnbalancedVoucherException</c> catch leaves the
    /// voucher current with a SetPostDated line on the book.
    /// </summary>
    [AvaloniaFact]
    public void An_unbalanced_alteration_after_Ctrl_T_puts_the_post_dated_flag_back_and_records_nothing()
    {
        using var book = AlterationBook.New("pdrefuse");
        var posted = PostJournal(book, optional: false);
        Assert.True(book.Company.FindVoucher(posted.Id)!.PostDated);
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Select(e => e.Id).ToList();

        var entry = book.ForAlter(posted.Id).Entry!;
        entry.TogglePostDated();                      // Ctrl+T (Current)
        entry.Lines[0].AmountText = "9999.99";        // credit side left alone: out of balance
        Assert.False(entry.AcceptAlteration());

        var onBook = book.Company.FindVoucher(posted.Id)!;
        Assert.True(onBook.PostDated);
        Assert.False(onBook.Optional);
        Assert.Equal("pdc refused", onBook.Narration);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// An Optional AND post-dated Journal, Ctrl+L and Ctrl+T in alteration, then the same refusal: two flag moves,
    /// two log lines, undone newest first. <b>Mutation:</b> calling <c>RollBackOptional</c> before
    /// <c>RollBackPostDated</c> in the out-of-balance catch makes the Optional discard refuse (its entry is not the
    /// newest) and the refusal is swallowed, so the voucher stays regularised with a SetOptional line left behind.
    /// </summary>
    [AvaloniaFact]
    public void An_unbalanced_alteration_after_Ctrl_L_and_Ctrl_T_puts_both_flags_back_newest_first()
    {
        using var book = AlterationBook.New("pdoptrefuse");
        var posted = PostJournal(book, optional: true);
        var found = book.Company.FindVoucher(posted.Id)!;
        Assert.True(found.PostDated);
        Assert.True(found.Optional);
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Select(e => e.Id).ToList();

        var entry = book.ForAlter(posted.Id).Entry!;
        Assert.True(entry.IsOptional);
        Assert.True(entry.IsPostDated);
        entry.ToggleOptional();                       // Ctrl+L (Regular)
        entry.TogglePostDated();                      // Ctrl+T (Current)
        entry.Lines[0].AmountText = "9999.99";
        Assert.False(entry.AcceptAlteration());

        var onBook = book.Company.FindVoucher(posted.Id)!;
        Assert.True(onBook.Optional);
        Assert.True(onBook.PostDated);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }
}
