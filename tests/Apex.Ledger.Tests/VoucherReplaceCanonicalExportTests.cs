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
///
/// <para><b>🔴 THE LIMIT OF THIS FILE, stated because it is easy to over-read.</b> ER-13 asks that <i>a book
/// which never uses these verbs</i> be unaffected. <b>No test in this repository can falsify that</b>: the
/// comparison needs the PRE-S5a binary, and every test here EXERCISES <c>Replace</c>. What these three prove is
/// therefore <b>residue-freedom</b> — an alteration and its inverse leave no trace, and a refused one leaves
/// none at all — not non-interference. Non-interference is argued structurally in §8.3 (no schema change, no new
/// field, an existing shape written into an existing list slot) and that argument is the evidence.</para>
///
/// <para><b>And a trap for whoever extends this file.</b> Two INDEPENDENTLY built copies of the same book do NOT
/// export identically — measured — because their masters carry different <see cref="Guid"/>s. The ER-13
/// instrument must compare one book against ITSELF across an operation, never one book against another. The
/// cross-book comparison is <c>DerivedStateSnapshot</c>'s job; it normalises Guids precisely so it can do it.</para>
///
/// <para><b>Also worth knowing:</b> the export faithfully carries a diverged statutory record forward (§3.3), so
/// a book amended today re-imports tomorrow with the divergence intact. That is correct — the export is a
/// faithful serialiser, not a validator — and it is why <c>Replace</c> raises
/// <see cref="VoucherAlterationWarningCode.StatutoryRecordDiverged"/> at the point the divergence is created.</para>
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
