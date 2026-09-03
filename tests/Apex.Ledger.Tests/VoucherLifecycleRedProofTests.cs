using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The RED-PROOF of phase 10.11 S5a (design §7.1). Correcting a posted voucher must not cost the voucher its
/// identity — its <b>number</b>, its <b>position in the book</b>, and every derived figure that reads either.
///
/// <para><b>Kept green, never deleted</b> (§7.1: <i>"a red-proof that is deleted after it goes green proves
/// nothing about the next regression"</i>). The pair below is the point: one test asserts that
/// <see cref="LedgerService.Replace(Guid, Voucher)"/> DELIVERS the property, the other asserts that the only
/// correction available before S5a — Delete-then-rePost — still DESTROYS it, so the first test cannot be
/// passing for a trivial reason.</para>
/// </summary>
public class VoucherLifecycleRedProofTests
{
    /// <summary>
    /// §7.1 — the red-proof itself. Book A is corrected in place; book B is the same book keyed right the
    /// first time. The corrected voucher must keep its number, keep its list index, and the two books must
    /// agree on EVERY derived figure.
    /// </summary>
    [Fact]
    public void CorrectingAPostedVoucherLosesItsIdentity()
    {
        var a = LifecycleBook.Build(tenthTotal: LifecycleBook.WrongTotal);
        var b = LifecycleBook.Build(tenthTotal: LifecycleBook.RightTotal);

        // The correction S5a provides: swap the voucher in place, same Guid.
        var original = a.Company.Vouchers.Single(v => v.Id == a.TenthId);
        var correctedId = a.TenthId;
        a.Service.Replace(a.TenthId, LifecycleBook.SalesVoucher(
            a, correctedId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        var aTenth = a.Company.Vouchers.Single(v => v.Id == correctedId);
        var bTenth = b.Company.Vouchers.Single(v => v.Id == b.TenthId);

        Assert.Equal(bTenth.Number, aTenth.Number);
        Assert.Equal(
            b.Company.Vouchers.ToList().FindIndex(v => v.Id == b.TenthId),
            a.Company.Vouchers.ToList().FindIndex(v => v.Id == correctedId));

        Assert.Equal(
            DerivedStateSnapshot.Snapshot(b.Company, LifecycleBook.AsOf),
            DerivedStateSnapshot.Snapshot(a.Company, LifecycleBook.AsOf));
    }

    /// <summary>
    /// The harm this slice removes, pinned so the test above cannot pass vacuously: the pre-S5a correction —
    /// <c>Delete</c> then re-<c>Post</c> — renumbers the voucher to max+1, moves it to the end of the book,
    /// and diverges the derived surface from a book that was keyed right.
    /// </summary>
    [Fact]
    public void DeleteThenRePostStillLosesItsIdentity_theHarmThisSliceRemoves()
    {
        var a = LifecycleBook.Build(tenthTotal: LifecycleBook.WrongTotal);
        var b = LifecycleBook.Build(tenthTotal: LifecycleBook.RightTotal);

        var original = a.Company.Vouchers.Single(v => v.Id == a.TenthId);
        var date = original.Date;
        a.Service.Delete(a.TenthId);
        var rePosted = a.Service.Post(LifecycleBook.SalesVoucher(
            a, Guid.NewGuid(), date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        var bTenth = b.Company.Vouchers.Single(v => v.Id == b.TenthId);

        // 1 — the number is REUSED from the top of the sequence, not retained.
        Assert.Equal(10, bTenth.Number);
        Assert.Equal(12, rePosted.Number);

        // 2 — the corrected voucher lands LAST, not 10th.
        Assert.Equal(9, b.Company.Vouchers.ToList().FindIndex(v => v.Id == b.TenthId));
        Assert.Equal(10, a.Company.Vouchers.ToList().FindIndex(v => v.Id == rePosted.Id));

        // 3 — and both leak into the derived surface.
        //
        // 🔴 CORRECTED (S5a review). This comment used to claim the lost number and lost position "both leak into
        // the derived surface", which reads as "the financial reports disagree". Measured, they do NOT: the
        // corrected-in-place book and the Delete-then-rePost book agree on every balance, valuation, outstanding,
        // cost and return figure. What separates them is the VOUCHER IDENTITY VECTOR — the number, the rendered
        // number and the list index — plus the registers that show the voucher itself. That is a real and
        // sufficient divergence (an invoice that silently changes its number is the harm this slice removes), but
        // the wrong reason invites a maintainer to conclude the financial sections are carrying the proof, and to
        // "simplify" the identity section away. Suppressing section 12 used to leave the two books BYTE-IDENTICAL
        // with 1,767 tests still green; DerivedStateSnapshot now refuses to return a dump without it.
        var bookB = DerivedStateSnapshot.Snapshot(b.Company, LifecycleBook.AsOf);
        var bookA = DerivedStateSnapshot.Snapshot(a.Company, LifecycleBook.AsOf);
        Assert.NotEqual(bookB, bookA);

        var divergentSections = bookA.Split('\n')
            .Zip(bookB.Split('\n'), (x, y) => (A: x, B: y))
            .Where(p => !string.Equals(p.A, p.B, StringComparison.Ordinal))
            .Select(p => p.A.Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "12", "14" }, divergentSections.Order(StringComparer.Ordinal));
    }
}

/// <summary>
/// The eleven-invoice book §7.1 specifies, built twice so an altered book can be compared against a book that
/// was keyed right the first time. Every figure carries odd paise (§7.5).
/// </summary>
public sealed class LifecycleBook
{
    public const string TenthNarration = "Invoice 10 - the one that gets corrected";

    /// <summary>₹1,84,733.45 — the wrong total.</summary>
    public static readonly Money WrongTotal = Money.FromRupees(184733.45m);

    /// <summary>₹1,84,731.95 — the correction, −₹1.50 (§7.5: a delta a rupee-rounded assertion would still see).</summary>
    public static readonly Money RightTotal = Money.FromRupees(184731.95m);

    /// <summary>₹1,84,732.95 — the −₹0.50 variant §7.5 requires alongside the −₹1.50 one.</summary>
    public static readonly Money HalfRupeeTotal = Money.FromRupees(184732.95m);

    public static readonly DateOnly BooksBegin = new(2024, 4, 1);
    public static readonly DateOnly AsOf = new(2025, 3, 31);

    public required Company Company { get; init; }
    public required LedgerService Service { get; init; }
    public required Domain.Ledger Customer { get; init; }
    public required Domain.Ledger SalesLedger { get; init; }
    public required VoucherType SalesType { get; init; }
    public required Guid TenthId { get; init; }

    /// <summary>
    /// Sales #1…#9 (odd paise), then Sales #10 for <paramref name="tenthTotal"/>, then Sales #11 — a later,
    /// unrelated invoice whose presence is what makes #10 <b>mid-sequence</b>.
    /// </summary>
    public static LifecycleBook Build(Money tenthTotal)
    {
        var company = CompanyFactory.CreateSeeded("Lifecycle Co", BooksBegin, BooksBegin);

        var salesGroup = company.FindGroupByName("Sales Accounts")!;
        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales", salesGroup.Id, Money.Zero, openingIsDebit: false);
        company.AddLedger(salesLedger);

        var debtorGroup = company.FindGroupByName("Sundry Debtors")!;
        var customer = new Domain.Ledger(Guid.NewGuid(), "A Customer", debtorGroup.Id, Money.Zero, openingIsDebit: true);
        company.AddLedger(customer);

        var salesType = company.FindVoucherTypeByName("Sales")!;

        var book = new LifecycleBook
        {
            Company = company,
            Service = new LedgerService(company),
            Customer = customer,
            SalesLedger = salesLedger,
            SalesType = salesType,
            TenthId = Guid.Empty,
        };

        Guid tenthId = Guid.Empty;
        for (var n = 1; n <= 11; n++)
        {
            var date = BooksBegin.AddDays(n);
            var id = Guid.NewGuid();
            var amount = n switch
            {
                10 => tenthTotal,
                _ => Money.FromRupees(1234.55m + (n * 101.37m)),
            };
            var narration = n == 10 ? TenthNarration : $"Invoice {n}";
            book.Service.Post(SalesVoucher(book, id, date, amount, narration));
            if (n == 10) tenthId = id;
        }

        return new LifecycleBook
        {
            Company = company,
            Service = book.Service,
            Customer = customer,
            SalesLedger = salesLedger,
            SalesType = salesType,
            TenthId = tenthId,
        };
    }

    /// <summary>Dr the customer / Cr Sales — the same shape for every invoice in the book.</summary>
    public static Voucher SalesVoucher(LifecycleBook book, Guid id, DateOnly date, Money amount, string? narration)
        => new(
            id,
            book.SalesType.Id,
            date,
            new[]
            {
                new EntryLine(book.Customer.Id, amount, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, amount, DrCr.Credit),
            },
            narration: narration,
            partyId: book.Customer.Id);
}
