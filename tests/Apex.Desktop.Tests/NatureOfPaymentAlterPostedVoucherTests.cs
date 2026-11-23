using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 6.28 (wave 12, track hf) — review finding: there is no dated rate history (IV-343), so once a Nature of
/// Payment's rate or threshold is ALTERED, every voucher already posted under the old figures keeps them and any
/// alteration whose re-computation would restate that reported deduction is refused. These tests pin the two
/// observable halves: the master's own save says so at the moment of the act, and the voucher's refusal names the
/// threshold alteration as a cause rather than only a cancelled/deleted voucher. The posted deduction read back off
/// the reloaded book is unchanged in both cases (a reported deduction is never restated).
/// </summary>
public sealed class NatureOfPaymentAlterPostedVoucherTests
{
    private static Voucher Post(AlterationBook book, Apex.Ledger.Domain.Ledger expense, Apex.Ledger.Domain.Ledger party, string gross) =>
        book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) }, "posted before the alteration");

    private static NatureOfPaymentMasterViewModel AlterJ(AlterationBook book) =>
        NatureOfPaymentMasterViewModel.ForAlter(
            book.Company, book.Storage, book.Company.FindNatureOfPaymentByCode("194J(b)")!.Id, () => { })!;

    private static TdsLineTax ReloadedTds(AlterationBook book, System.Guid voucherId)
    {
        var fresh = new Apex.Desktop.Services.CompanyStorage(book.Dir);
        var c = fresh.Load(fresh.ListCompanies().Single());
        return c.Vouchers.Single(v => v.Id == voucherId).Lines.Single(l => l.HasTds).Tds!;
    }

    [Fact]
    public void Altering_the_rate_warns_that_posted_vouchers_keep_the_old_rate_and_their_alteration_is_refused()
    {
        using var book = AlterationBook.New("hf-rev-rate");
        var (expense, party) = book.EnableTds("194J(b)");
        book.Storage.Save(book.Company);
        var posted = Post(book, expense, party, "120000.30");
        Assert.Equal(12_000m, posted.Lines.Single(l => l.HasTds).Tds!.TdsAmount.Amount);

        var m = AlterJ(book);
        m.RateWithPanText = "7.5";
        Assert.True(m.Alter(), m.Message);
        Assert.Contains("1 voucher already posted under this section", m.Message);
        Assert.Contains("refused", m.Message);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        open.Entry!.Narration = "narration only";
        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("10%", open.Entry.Message);
        Assert.Contains("7.5%", open.Entry.Message);

        var tds = ReloadedTds(book, posted.Id);
        Assert.Equal(1000, tds.RateBasisPoints);
        Assert.Equal(12_000m, tds.TdsAmount.Amount);
    }

    [Fact]
    public void Altering_the_threshold_names_that_cause_in_the_posted_vouchers_refusal()
    {
        using var book = AlterationBook.New("hf-rev-thr");
        var (expense, party) = book.EnableTds("194J(b)");
        book.Storage.Save(book.Company);
        var posted = Post(book, expense, party, "60000.30");
        Assert.Equal(6_000m, posted.Lines.Single(l => l.HasTds).Tds!.TdsAmount.Amount);

        var m = AlterJ(book);
        m.CumulativeThresholdText = "100000";
        Assert.True(m.Alter(), m.Message);
        Assert.Contains("1 voucher already posted under this section", m.Message);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        open.Entry!.Narration = "narration only";
        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("threshold has been altered", open.Entry.Message);

        Assert.Equal(6_000m, ReloadedTds(book, posted.Id).TdsAmount.Amount);
    }

    [Fact]
    public void A_name_only_alteration_carries_no_posted_voucher_warning()
    {
        using var book = AlterationBook.New("hf-rev-name");
        var (expense, party) = book.EnableTds("194J(b)");
        book.Storage.Save(book.Company);
        var posted = Post(book, expense, party, "120000.30");

        var m = AlterJ(book);
        m.Name = "Professional fees (renamed)";
        Assert.True(m.Alter(), m.Message);
        Assert.DoesNotContain("already posted", m.Message);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        open.Entry!.Narration = "narration only";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);
    }
}
