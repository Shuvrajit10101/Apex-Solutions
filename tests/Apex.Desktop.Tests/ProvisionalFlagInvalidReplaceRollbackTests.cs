using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Wave-10 independent verification, second pass (track gd): the accounting screen's INVALID-replace arm after
/// Ctrl+T, which no test reached.</b> <c>VoucherEntryViewModel.CommitAlteration</c> has three refusal arms after the
/// Ctrl+T move: out of balance (pinned by <c>ProvisionalFlagRefusalRollbackTests</c>), a failed save (pinned by
/// <c>ProvisionalFlagRollbackTests</c>), and the <c>InvalidVoucherException</c> / <c>InvalidOperationException</c>
/// catch, which <c>LedgerService.Replace</c> reaches for a balanced replacement it still refuses. That third arm was
/// unpinned.
///
/// <para>Measured before this file: deleting <c>RollBackPostDated</c> from that catch
/// (<c>VoucherEntryViewModel.cs</c>, the second catch after <c>Replace</c>) left the 218 provisional-flag, alteration and exception-report Desktop tests green. The voucher
/// would then stay CURRENT on the book after a refused alteration, with a SetPostDated line the .db never saw.
/// A Reversing Journal cannot also be Optional, so this arm is pinned with Post-Dated alone; the two-flag ORDER is
/// pinned on the out-of-balance arm (<c>ProvisionalFlagRefusalRollbackTests</c>) and on the save-failure arm
/// (below).</para>
///
/// <para>The trigger is a real operator move: a post-dated Reversing Journal opened in alteration, Ctrl+T (Current),
/// and the Applicable Upto date moved — which <c>Replace</c> refuses by name (the provisional-state vector). Every
/// assertion reads the flag off the book, the edit log by identity and a byte-identical canonical export.</para>
/// </summary>
public sealed class ProvisionalFlagInvalidReplaceRollbackTests
{
    private static Voucher PostReversing(AlterationBook book)
    {
        var dr = book.Ledger("Invalid Accrued", "Indirect Expenses");
        var cr = book.Ledger("Invalid Liability", "Current Liabilities");
        return book.Post(VoucherBaseType.ReversingJournal, book.On(),
            new[] { (dr, DrCr.Debit, "2710.29"), (cr, DrCr.Credit, "2710.29") }, "pdc accrual",
            configure: e =>
            {
                e.ApplicableUptoText = ApexDate.Format(book.On(45));
                e.TogglePostDated();                 // Ctrl+T at entry
            });
    }

    /// <summary>
    /// Ctrl+T then a moved Applicable Upto: Replace refuses, and the Post-Dated flag goes back.
    /// <b>Mutation:</b> deleting <c>RollBackPostDated</c> from the invalid-replace catch reddens this test.
    /// </summary>
    [AvaloniaFact]
    public void A_refused_replace_after_Ctrl_T_puts_the_post_dated_flag_back_and_records_nothing()
    {
        using var book = AlterationBook.New("pdinvalid");
        var posted = PostReversing(book);
        Assert.True(book.Company.FindVoucher(posted.Id)!.PostDated);
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Select(e => e.Id).ToList();

        var entry = book.ForAlter(posted.Id).Entry!;
        Assert.True(entry.IsPostDated);
        entry.TogglePostDated();                                     // Ctrl+T (Current)
        entry.ApplicableUptoText = ApexDate.Format(book.On(90));     // Replace refuses this by name
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Cannot alter", entry.Message!, StringComparison.Ordinal);

        var onBook = book.Company.FindVoucher(posted.Id)!;
        Assert.True(onBook.PostDated);
        Assert.False(onBook.Optional);
        Assert.Equal(book.On(45), onBook.ApplicableUpto);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// The SAVE-failure arm with BOTH flags moved on the accounting screen. <c>ProvisionalFlagRollbackTests</c> pins
    /// it with Post-Dated alone, so the ORDER of the two rollbacks there was unpinned. Measured before this test:
    /// moving <c>RollBackPostDated</c> below <c>RollBackOptional</c> in the save-failure catch left the 218 provisional-flag, alteration and exception-report Desktop
    /// tests green. <b>Mutation:</b> that swap makes the Optional discard refuse (its entry is not the newest), the
    /// refusal is swallowed, and the voucher stays regularised in memory with a SetOptional line the .db never
    /// saw, which the NEXT successful save would then persist. This test reddens.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_save_after_Ctrl_L_and_Ctrl_T_in_accounting_alteration_puts_both_flags_back_newest_first()
    {
        using var book = AlterationBook.New("pdoptsavefail");
        var dr = book.Ledger("Savefail Rent", "Indirect Expenses");
        var cr = book.Ledger("Savefail Income", "Indirect Incomes");
        var posted = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "1903.61"), (cr, DrCr.Credit, "1903.61") }, "pdc optional",
            configure: e =>
            {
                e.TogglePostDated();
                e.ToggleOptional();
            });
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Select(e => e.Id).ToList();

        var entry = book.ForAlter(posted.Id).Entry!;
        entry.ToggleOptional();                      // Ctrl+L (Regular)
        entry.TogglePostDated();                     // Ctrl+T (Current)
        book.Company.Pin = "NOT-A-PIN";              // the next Save throws out of EnsureValid
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Could not save the company", entry.Message!, StringComparison.Ordinal);
        book.Company.Pin = null;

        var onBook = book.Company.FindVoucher(posted.Id)!;
        Assert.True(onBook.Optional);
        Assert.True(onBook.PostDated);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }
}
