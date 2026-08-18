using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// ER-13 for the ALTER verb (design §8.3). §8.3's correction is load-bearing here: a raw <c>.db</c> byte
/// comparison is unachievable for ANY book, because <c>entry_lines.id</c> is
/// <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> and a delete-all + full re-insert renumbers those surrogate ids on
/// every save. <b>The correct instrument is the canonical export</b>, which carries the semantic model and no
/// surrogate ids.
///
/// <para>These two tests are the alter-shaped statement of that rule, and they run in
/// <c>Apex.Ledger.Tests</c> — which already references <c>Apex.Ledger.Io</c> — deliberately, so the Io suite's
/// own count stays exactly where it is. A moved Io count would be a red flag, not a pass.</para>
/// </summary>
public class VoucherReplaceCanonicalExportTests
{
    [Fact]
    public void Replacing_a_voucher_with_an_identical_one_leaves_the_canonical_export_byte_identical()
    {
        var book = LifecycleBook.Build(LifecycleBook.RightTotal);
        var before = CanonicalXml.Export(book.Company);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        Assert.Equal(before, CanonicalXml.Export(book.Company));
    }

    [Fact]
    public void Altering_a_voucher_and_altering_it_back_leaves_the_canonical_export_byte_identical()
    {
        var book = LifecycleBook.Build(LifecycleBook.RightTotal);
        var before = CanonicalXml.Export(book.Company);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.WrongTotal, "temporarily wrong"));
        Assert.NotEqual(before, CanonicalXml.Export(book.Company));

        book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        // The round trip through an alteration leaves NO residue — not in the number, not in the position,
        // not in any field the semantic model carries.
        Assert.Equal(before, CanonicalXml.Export(book.Company));
    }

    [Fact]
    public void A_refused_replacement_leaves_the_canonical_export_byte_identical()
    {
        var book = LifecycleBook.Build(LifecycleBook.RightTotal);
        var before = CanonicalXml.Export(book.Company);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var unbalanced = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.WrongTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            });

        Assert.Throws<UnbalancedVoucherException>(() => book.Service.Replace(book.TenthId, unbalanced));
        Assert.Equal(before, CanonicalXml.Export(book.Company));
    }
}
