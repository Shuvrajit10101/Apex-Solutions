using System;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W45 REVIEW — THE SAVE-FAILURE ARM OF THE OPTIONAL SPLIT, WHICH THE BRANCH SHIPPED UNTESTED.</b>
/// <c>CommitAlteration</c> now appends THREE edit-log entries on a regularising alteration whose save fails
/// (SetOptional, the content Replace, the rollback Replace) and must discard all three in strict LIFO order while
/// also putting the flag back — and <c>RollBackOptional</c> SWALLOWS its exception, so a mistake here would be
/// silent: the in-memory book would hold a live voucher while the <c>.db</c> held an Optional one, and the whole
/// value of the voucher would be in the operator's balances with nothing saying so.
/// <para>Provoked the way <c>VoucherAlterForAlterTests.A_failed_save_puts_the_original_voucher_back</c> provokes
/// it — a bad PIN, which <c>CompanyStorage.Save</c> throws on out of <c>Company.EnsureValid()</c>. Asserts the
/// flag off the book, an edit log back at its starting length, and a byte-identical canonical export.</para>
/// <para><b>It bites.</b> Neutralising the <c>SetOptional</c> split in <c>CommitAlteration</c> fails it.</para>
/// </summary>
public sealed class VoucherOptionalSaveFailureRollbackTests
{
    private static Voucher PostOptional(AlterationBook book, decimal amount)
    {
        var dr = book.Ledger($"Dr Leg {VoucherBaseType.Journal}", "Indirect Expenses");
        var cr = book.Ledger($"Cr Leg {VoucherBaseType.Journal}", "Indirect Incomes");
        var text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return book.Post(
            VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, text), (cr, DrCr.Credit, text) },
            "provisional",
            configure: e => e.ToggleOptional());
    }

    /// <summary>
    /// Ctrl+L then Ctrl+A with a save that THROWS: the flag must go back, all THREE edit-log entries must be
    /// gone, and the canonical export must be byte-identical to before.
    /// </summary>
    [Fact]
    public void A_failed_save_on_a_regularising_alteration_puts_the_Optional_flag_back_and_leaves_no_log()
    {
        using var book = AlterationBook.New("optsavefail");
        var posted = PostOptional(book, 7431.65m);
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Count;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.Entry!.IsOptional);
        open.Entry.ToggleOptional();                 // Ctrl+L (Regular)
        Assert.False(open.Entry.IsOptional);

        book.Company.Pin = "NOT-A-PIN";              // the next Save throws out of EnsureValid

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("Could not save the company", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);

        book.Company.Pin = null;

        // 🔴 THE FLAG: still Optional, so the in-memory book and the .db agree.
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);
        // 🔴 THE LOG: no fictitious alteration survived — including SetOptional's own entry.
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Count);
        // 🔴 THE ARTEFACT.
        Assert.Equal(before, book.Export());
    }
}
