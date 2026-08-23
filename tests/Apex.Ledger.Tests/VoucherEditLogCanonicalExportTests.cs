using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE CANONICAL EXPORT DELIBERATELY DOES NOT CARRY THE VOUCHER EDIT LOG, and this file is where that
/// decision is written down and pinned.</b> It lives beside <c>CompanyImportRoundTripTests</c>, which is the suite
/// that owns the import contract.
///
/// <para><b>The reason is structural, not laziness.</b> <c>ImportPlan.BuildVoucher</c> posts every incoming
/// voucher under <c>Guid.NewGuid()</c> — the canonical model is an INTERCHANGE format that re-keys what it
/// imports, precisely so a batch can land in a company that already holds other documents. An edit-log entry is
/// nothing but a <see cref="VoucherEditLogEntry.VoucherId"/> plus a snapshot of the voucher that id named. Carried
/// across an import, every one of those ids would point at a voucher that does not exist in the target company —
/// and, for the <see cref="VoucherEditVerb.Delete"/> entries, at a voucher that was never going to. The log would
/// arrive as a page of assertions about somebody else's book.</para>
///
/// <para><b>What this costs, stated rather than glossed:</b> exporting a company and re-importing it loses the
/// recorded edit history. The company's own <c>.db</c> file keeps it (that is what schema v52 is), and a file-copy
/// BACKUP keeps it; only the canonical export–import route drops it. If interchange ever needs to carry the log,
/// the entries must be re-keyed through the same voucher-id map <c>ImportPlan</c> already builds — and the Delete
/// entries, which map to nothing, need an explicit answer first.</para>
/// </summary>
public class VoucherEditLogCanonicalExportTests
{
    [Fact]
    public void The_canonical_export_is_byte_identical_before_and_after_the_book_is_edited()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = new LedgerService(book.Company, () => DateTimeOffset.UnixEpoch);

        var beforeAnyEdit = Encoding.UTF8.GetString(CanonicalJson.Export(book.Company));

        // Cancel #9, then alter #10 back to the figure it should always have carried, then put both back — the
        // BOOK ends where it started, so a canonical export that carried the log would now differ and one that
        // does not carry it cannot.
        var ninth = book.Company.Vouchers.Single(v => v.Number == 9);
        var cancelEntry = service.Cancel(ninth.Id);
        service.DiscardUncommittedCancel(ninth.Id, cancelEntry);

        var tenth = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, tenth.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));
        service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, tenth.Date, LifecycleBook.WrongTotal, LifecycleBook.TenthNarration));

        // Two alterations really did happen and really are on the log …
        Assert.Equal(2, book.Company.VoucherEditLog.Count);

        // … and the interchange format says nothing about them, because it re-keys every voucher it imports.
        Assert.Equal(beforeAnyEdit, Encoding.UTF8.GetString(CanonicalJson.Export(book.Company)));
        Assert.DoesNotContain("editLog", beforeAnyEdit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("beforeSnapshot", beforeAnyEdit, StringComparison.OrdinalIgnoreCase);
    }
}
