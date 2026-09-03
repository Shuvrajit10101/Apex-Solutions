using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The <b>Prevent Duplicate × Replace</b> interaction — the coupling that makes §6.5 clause 3 ("the number is
/// preserved") safe, and which nothing in the repository used to pin.
///
/// <para><b>Why this file exists.</b> Replace copies the original's <c>Number</c> onto the replacement BEFORE
/// validating, so under Prevent Duplicates the replacement renders a number that is, by construction, already on
/// the book — the outgoing voucher's own. Only <c>VoucherValidator</c>'s <c>other.Id == v.Id</c> skip stops that
/// being a refusal. Measured: deleting that one line left <b>all four test projects green — 4,699 tests</b>. The
/// first test below kills that mutant.</para>
///
/// <para><b>And the trap on the other side.</b> A book can legitimately hold two same-numbered vouchers (posted
/// with the setting off, or under Manual numbering). With the setting switched on, the shipped code refused to
/// alter EITHER of them — and since Replace also refuses a renumber, Delete + re-Post was the only correction
/// left, which is the exact harm S5a exists to remove. The alteration did not create that collision; the
/// pre-existing-collision exemption is what stops it being punished for it, and a collision the alteration DOES
/// create is still refused (third test).</para>
/// </summary>
public class VoucherReplacePreventDuplicateTests
{
    private static LifecycleBook WithPreventDuplicate(Money tenthTotal)
    {
        var book = LifecycleBook.Build(tenthTotal);
        book.SalesType.PreventDuplicate = true;
        book.SalesType.Numbering = NumberingMethod.Automatic;
        return book;
    }

    /// <summary>
    /// (a) The self-skip. With Prevent Duplicates on, a plain alteration that keeps the number must be ACCEPTED —
    /// the voucher is not a duplicate of itself.
    /// </summary>
    [Fact]
    public void Replace_is_accepted_under_Prevent_Duplicates_when_the_number_is_merely_preserved()
    {
        var book = WithPreventDuplicate(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var accepted = book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        Assert.Equal(10, accepted.Number);
        Assert.Equal(LifecycleBook.RightTotal, accepted.TotalDebit);
    }

    /// <summary>The same, with the replacement carrying the number explicitly — the S5b rehydration shape, which
    /// must not take a different path through the duplicate scan.</summary>
    [Fact]
    public void A_rehydration_shaped_replacement_is_accepted_under_Prevent_Duplicates_too()
    {
        var book = WithPreventDuplicate(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var rehydrated = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);
        rehydrated.Number = original.Number;

        Assert.Equal(10, book.Service.Replace(book.TenthId, rehydrated).Number);
    }

    /// <summary>
    /// (b) The pre-existing-collision exemption. Two Sales #10 posted with the setting OFF; the setting is then
    /// switched ON. Altering either must still work — the alteration did not create the duplicate, and refusing
    /// here leaves Delete + re-Post as the only correction.
    /// </summary>
    [Fact]
    public void A_duplicate_that_predates_the_setting_does_not_make_the_voucher_unalterable()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.SalesType.Numbering = NumberingMethod.Manual;

        var twinId = Guid.NewGuid();
        var twin = LifecycleBook.SalesVoucher(
            book, twinId, LifecycleBook.BooksBegin.AddDays(15), Money.FromRupees(4321.05m), "the accidental twin");
        twin.Number = 10;                                     // a second #10, keyed while the setting was off
        book.Service.Post(twin);
        Assert.Equal(2, book.Company.Vouchers.Count(v => v.TypeId == book.SalesType.Id && v.Number == 10));

        book.SalesType.PreventDuplicate = true;               // the operator turns it on afterwards

        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var accepted = book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        Assert.Equal(10, accepted.Number);
        Assert.Equal(LifecycleBook.RightTotal, accepted.TotalDebit);

        // …and the OTHER one of the pair is alterable too.
        var amendedTwin = LifecycleBook.SalesVoucher(
            book, twinId, twin.Date, Money.FromRupees(4321.95m), "the accidental twin, corrected");
        Assert.Equal(10, book.Service.Replace(twinId, amendedTwin).Number);
    }

    /// <summary>
    /// The control: a collision the alteration ITSELF creates is still refused. Two date-effective prefixes mean a
    /// date change rewrites the rendered number — straight onto another live voucher's. That is a NEW duplicate
    /// and the exemption must not cover it.
    /// </summary>
    [Fact]
    public void A_collision_the_alteration_creates_is_still_refused()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.SalesType.Numbering = NumberingMethod.Manual;
        book.SalesType.PreventDuplicate = true;

        // Two DIFFERENT date-effective prefixes. #10 lives in the "A/" window and renders A/10; a second #10
        // lives in the "B/" window and renders B/10 — no collision today. Moving #10 into the "B/" window makes
        // it render B/10 for the first time, which is a duplicate THIS alteration creates.
        book.SalesType.SetAffixes(
            new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), LifecycleBook.BooksBegin, "A/"),
                new VoucherNumberAffix(Guid.NewGuid(), LifecycleBook.BooksBegin.AddDays(20), "B/"),
            },
            null);

        var laterTwinId = Guid.NewGuid();
        var laterTwin = LifecycleBook.SalesVoucher(
            book, laterTwinId, LifecycleBook.BooksBegin.AddDays(25), Money.FromRupees(4321.05m), "later #10");
        laterTwin.Number = 10;
        book.Service.Post(laterTwin);

        Assert.Equal("A/10", book.Company.FormatVoucherNumber(book.Company.FindVoucher(book.TenthId)!));
        Assert.Equal("B/10", book.Company.FormatVoucherNumber(laterTwin));

        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var moved = LifecycleBook.SalesVoucher(
            book, book.TenthId, LifecycleBook.BooksBegin.AddDays(26), LifecycleBook.RightTotal,
            LifecycleBook.TenthNarration);

        var ex = Assert.Throws<InvalidVoucherException>(() => book.Service.Replace(book.TenthId, moved));
        Assert.Contains("Prevent Duplicates is on", ex.Message, StringComparison.Ordinal);

        // Refused means refused: the original is still there, still #10, still on its own date.
        var still = book.Company.FindVoucher(book.TenthId)!;
        Assert.Equal(10, still.Number);
        Assert.Equal(original.Date, still.Date);
        Assert.Equal(LifecycleBook.WrongTotal, still.TotalDebit);
    }

    /// <summary>Post is untouched by the exemption — it passes no <c>replacing</c>, so a genuinely duplicate NEW
    /// voucher is refused exactly as before.</summary>
    [Fact]
    public void Post_still_refuses_a_duplicate_number()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.SalesType.Numbering = NumberingMethod.Manual;
        book.SalesType.PreventDuplicate = true;

        var duplicate = LifecycleBook.SalesVoucher(
            book, Guid.NewGuid(), LifecycleBook.BooksBegin.AddDays(15), Money.FromRupees(4321.05m), "a duplicate");
        duplicate.Number = 10;

        var ex = Assert.Throws<InvalidVoucherException>(() => book.Service.Post(duplicate));
        Assert.Contains("Prevent Duplicates is on", ex.Message, StringComparison.Ordinal);
    }
}
