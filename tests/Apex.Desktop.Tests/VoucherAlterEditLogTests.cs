using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// The <b>voucher edit log</b> (schema v52) on the ALTERATION route, driven through the real
/// <c>VoucherEntryViewModel.ForAlter</c> → <c>AcceptAlteration</c> screen path.
///
/// <para><b>Why the alteration route needs its own wiring test even though the engine has one.</b> Alter is the
/// verb that left the LEAST evidence: Cancel at least flagged the voucher and Delete at least removed a row a
/// reader might miss, but Replace overwrote the voucher in place, so a book that had been altered was
/// indistinguishable from one that had always said what it now says. The engine records it; this proves the
/// screen that operators actually use goes through that engine and not around it.</para>
/// </summary>
public sealed class VoucherAlterEditLogTests
{
    [Fact]
    public void An_accepted_alteration_records_the_figure_the_book_no_longer_shows()
    {
        using var book = AlterationBook.New("editlog");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 4321.09m, "as first keyed");
        Assert.Empty(book.Company.VoucherEditLog);

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "5678.91";
        open.Entry.Lines[1].AmountText = "5678.91";
        open.Entry.Narration = "as corrected";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var entry = Assert.Single(book.Company.VoucherEditLog);
        Assert.Equal(VoucherEditVerb.Alter, entry.Verb);
        Assert.Equal(posted.Id, entry.VoucherId);

        // 🔴 The log holds what the book no longer does, and the book holds what the log does not.
        Assert.Contains("4321.09", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("as first keyed", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("5678.91", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Equal("as corrected", book.Company.FindVoucher(posted.Id)!.Narration);

        // …and it is persisted, because AcceptAlteration saves and the store is a snapshot.
        var reopened = book.Storage.Load(book.Storage.ListCompanies()
            .Single(e => e.Name == book.Company.Name));
        Assert.Equal(entry.BeforeSnapshot, Assert.Single(reopened.VoucherEditLog).BeforeSnapshot);
    }

    /// <summary>
    /// A REFUSED alteration records nothing. The engine appends its entry past every guard and every throw, so a
    /// refusal cannot leave a line claiming the book moved when it did not.
    /// </summary>
    [Fact]
    public void A_refused_alteration_records_nothing()
    {
        using var book = AlterationBook.New("editlognope");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 4321.09m, "as first keyed");

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "5678.91";      // …and the credit side left alone: out of balance.
        Assert.False(open.Entry.AcceptAlteration());

        Assert.Empty(book.Company.VoucherEditLog);
        Assert.Equal("as first keyed", book.Company.FindVoucher(posted.Id)!.Narration);
    }
}
