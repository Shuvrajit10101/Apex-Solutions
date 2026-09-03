using System.Reflection;
using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The <b>voucher edit log</b> (schema v52) at the engine level.
///
/// <para><b>The defect this closes.</b> Of the three lifecycle verbs only <c>Cancel</c> left any evidence — it
/// sets a flag and the voucher stays on the book. <c>Delete</c> removed the row and <c>Replace</c> overwrote it
/// in place, after which nothing anywhere in the product could tell an auditor the book had been edited. These
/// tests pin what is recorded, when nothing is recorded, and the one bounded way an entry can leave the log.</para>
/// </summary>
public class VoucherEditLogTests
{
    private static readonly DateTimeOffset Instant =
        new(2026, 8, 19, 14, 5, 6, TimeSpan.FromHours(5.5));

    /// <summary>A service over <paramref name="book"/>'s company with a FIXED clock, so a recorded timestamp is
    /// an assertion rather than a wall-clock read.</summary>
    private static LedgerService Timed(LifecycleBook book, DateTimeOffset? at = null)
        => new(book.Company, () => at ?? Instant);

    // -------------------------------------------------------------------------------------------------
    // What each verb records.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Cancel_records_one_entry_whose_snapshot_is_the_voucher_BEFORE_the_flag_moved()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);

        var entry = service.Cancel(book.TenthId);

        Assert.Same(entry, Assert.Single(book.Company.VoucherEditLog));
        Assert.Equal(book.TenthId, entry.VoucherId);
        Assert.Equal(VoucherEditVerb.Cancel, entry.Verb);
        Assert.Equal(Instant, entry.RecordedAt);

        // The voucher IS cancelled now; the snapshot says it was not. That ordering is the whole content of a
        // before-state, and a snapshot taken one line later would silently record the after-state instead.
        Assert.True(book.Company.FindVoucher(book.TenthId)!.Cancelled);
        Assert.Contains("\"Cancelled\":false", entry.BeforeSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_records_the_voucher_before_it_is_removed_and_the_entry_outlives_it()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);

        var entry = service.Delete(book.TenthId);

        // 🔴 The voucher is gone from the book, so this entry is the ONLY remaining evidence of what was posted.
        Assert.Null(book.Company.FindVoucher(book.TenthId));
        Assert.Equal(VoucherEditVerb.Delete, entry.Verb);
        Assert.Contains(LifecycleBook.TenthNarration, entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains("184733.45", entry.BeforeSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Alter_records_the_OUTGOING_voucher_not_the_replacement()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var replacement = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, "corrected");
        service.Replace(book.TenthId, replacement);

        var entry = Assert.Single(book.Company.VoucherEditLog);
        Assert.Equal(VoucherEditVerb.Alter, entry.Verb);

        // The wrong figure is in the log; the right one is on the book. Neither is in the other.
        Assert.Contains("184733.45", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("184731.95", entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Contains(LifecycleBook.TenthNarration, entry.BeforeSnapshot, StringComparison.Ordinal);
        Assert.Equal(
            LifecycleBook.RightTotal, book.Company.FindVoucher(book.TenthId)!.TotalDebit);
    }

    /// <summary>
    /// The property that makes before-only recording COMPLETE rather than a narrowing: alteration N's
    /// before-state is alteration N−1's after-state, so the chain of snapshots plus the live voucher reconstructs
    /// every intermediate state of the voucher. Recording an after-state as well would duplicate the book.
    /// </summary>
    [Fact]
    public void Successive_alterations_chain_each_before_state_onto_the_previous_after_state()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var date = book.Company.Vouchers.Single(v => v.Id == book.TenthId).Date;

        service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, date, LifecycleBook.HalfRupeeTotal, "first correction"));

        // Capture the book's state between the two alterations, by the same instrument the log uses.
        var betweenTheTwo = VoucherSnapshot.Of(book.Company.FindVoucher(book.TenthId)!);

        service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, date, LifecycleBook.RightTotal, "second correction"));

        Assert.Equal(2, book.Company.VoucherEditLog.Count);
        Assert.Equal(betweenTheTwo, book.Company.VoucherEditLog[1].BeforeSnapshot);
    }

    [Fact]
    public void ConvertToRegular_records_its_own_verb_rather_than_masquerading_as_a_Delete()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var memoType = book.Company.FindVoucherTypeByName("Memorandum")!;

        var memoId = Guid.NewGuid();
        service.Post(new Voucher(
            memoId, memoType.Id, LifecycleBook.BooksBegin.AddDays(20),
            new[]
            {
                new EntryLine(book.Customer.Id, Money.FromRupees(999.99m), DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, Money.FromRupees(999.99m), DrCr.Credit),
            },
            narration: "a memo that becomes real"));

        Assert.Empty(book.Company.VoucherEditLog);      // Post never logs — it edits nothing.

        service.ConvertToRegular(memoId, book.SalesType.Id);

        var entry = Assert.Single(book.Company.VoucherEditLog);
        Assert.Equal(VoucherEditVerb.ConvertMemorandum, entry.Verb);
        Assert.Equal(memoId, entry.VoucherId);
        Assert.Contains("a memo that becomes real", entry.BeforeSnapshot, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------
    // When NOTHING is recorded. A log that records refusals would be as wrong as one that misses edits.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_book_that_never_uses_a_verb_has_an_empty_log()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        Assert.Empty(book.Company.VoucherEditLog);
        Assert.Null(book.Company.LastVoucherEditLogEntry);
    }

    [Fact]
    public void A_REFUSED_alteration_records_nothing()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        // Refused by the clause-2 identity guard: a replacement carrying a different Guid.
        var wrongId = LifecycleBook.SalesVoucher(
            book, Guid.NewGuid(), original.Date, LifecycleBook.RightTotal, "nope");
        Assert.Throws<InvalidOperationException>(() => service.Replace(book.TenthId, wrongId));
        Assert.Empty(book.Company.VoucherEditLog);

        // Refused by the §7.4 provisional-state guard.
        var flipped = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, "nope");
        flipped.Optional = true;
        Assert.Throws<InvalidOperationException>(() => service.Replace(book.TenthId, flipped));
        Assert.Empty(book.Company.VoucherEditLog);

        // Refused by the balance invariant.
        var unbalanced = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, Money.FromRupees(90m), DrCr.Credit),
            });
        Assert.ThrowsAny<Exception>(() => service.Replace(book.TenthId, unbalanced));
        Assert.Empty(book.Company.VoucherEditLog);
    }

    [Fact]
    public void A_verb_aimed_at_an_unknown_voucher_records_nothing()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);

        Assert.Throws<InvalidOperationException>(() => service.Cancel(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => service.Delete(Guid.NewGuid()));
        Assert.Empty(book.Company.VoucherEditLog);
    }

    // -------------------------------------------------------------------------------------------------
    // The one bounded removal — the compensating undo for a verb whose SAVE did not commit.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void DiscardUncommittedCancel_undoes_BOTH_halves_of_a_cancel()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);

        var entry = service.Cancel(book.TenthId);
        service.DiscardUncommittedCancel(book.TenthId, entry);

        Assert.False(book.Company.FindVoucher(book.TenthId)!.Cancelled);
        Assert.Empty(book.Company.VoucherEditLog);
    }

    /// <summary>
    /// 🔴 The bound that stops the undo being an audit-erasure API: only the MOST RECENT entry can go. Without
    /// it, a caller holding any entry could delete evidence of any past edit.
    /// </summary>
    [Fact]
    public void Only_the_most_recent_entry_can_be_discarded()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var ninthId = book.Company.Vouchers.Single(v => v.Number == 9).Id;

        var first = service.Cancel(ninthId);
        var second = service.Cancel(book.TenthId);

        var ex = Assert.Throws<InvalidOperationException>(
            () => service.DiscardUncommittedEditLogEntry(first));
        Assert.Contains("most recent", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, book.Company.VoucherEditLog.Count);

        // Newest first is accepted, and only then does the older one become discardable — which is exactly the
        // LIFO order an interactive rollback unwinds in.
        service.DiscardUncommittedEditLogEntry(second);
        service.DiscardUncommittedEditLogEntry(first);
        Assert.Empty(book.Company.VoucherEditLog);
    }

    [Fact]
    public void DiscardUncommittedCancel_refuses_an_entry_that_does_not_describe_that_cancel()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var service = Timed(book);
        var ninthId = book.Company.Vouchers.Single(v => v.Number == 9).Id;

        var entry = service.Cancel(book.TenthId);

        // Right entry, wrong voucher.
        Assert.Throws<InvalidOperationException>(() => service.DiscardUncommittedCancel(ninthId, entry));
        // …and the cancel is untouched by the refusal.
        Assert.True(book.Company.FindVoucher(book.TenthId)!.Cancelled);
        Assert.Single(book.Company.VoucherEditLog);

        // Wrong VERB: a Delete's entry cannot roll back a cancel.
        var deleteEntry = service.Delete(ninthId);
        Assert.Throws<InvalidOperationException>(
            () => service.DiscardUncommittedCancel(ninthId, deleteEntry));
    }

    // -------------------------------------------------------------------------------------------------
    // Honesty locks — what the record deliberately does NOT say, and what it must never silently stop saying.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 <b>ATTRIBUTION.</b> This application has no user, no actor, no login and no session identity, so an
    /// entry cannot say WHO. This test pins the record's shape so that adding a <c>ModifiedBy</c> / <c>Actor</c>
    /// / <c>UserName</c> member goes RED and forces the question "what would it hold?" — whose only honest answer
    /// today is "unknown", and whose dishonest answer is a fabricated name. When a real identity model lands,
    /// this test is the place the decision gets recorded.
    /// </summary>
    [Fact]
    public void The_entry_records_no_actor_because_nothing_in_this_application_knows_who()
    {
        var actual = typeof(VoucherEditLogEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "BeforeSnapshot", "Id", "RecordedAt", "Verb", "VoucherId" },
            actual);
    }

    /// <summary>
    /// 🔴 <b>THE SNAPSHOT MAY NOT SILENTLY NARROW.</b> A hand-picked field list would drop every property added
    /// to <see cref="Voucher"/> or <see cref="EntryLine"/> after it was written, with nothing going red — the
    /// before-state would quietly stop describing the voucher. <see cref="VoucherSnapshot"/> serialises the whole
    /// object graph so completeness is structural; this test is the tripwire against a <c>[JsonIgnore]</c>, an
    /// explicit converter or a hand-rolled projection reintroducing the narrowing.
    /// </summary>
    [Fact]
    public void The_before_snapshot_carries_every_public_member_of_Voucher_and_EntryLine()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var voucher = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var snapshot = VoucherSnapshot.Of(voucher);

        using var doc = JsonDocument.Parse(snapshot);
        var top = doc.RootElement;

        foreach (var name in Readable(typeof(Voucher)))
            Assert.True(top.TryGetProperty(name, out _), $"Voucher.{name} is missing from the before-snapshot.");

        var line = top.GetProperty(nameof(Voucher.Lines)).EnumerateArray().First();
        foreach (var name in Readable(typeof(EntryLine)))
            Assert.True(line.TryGetProperty(name, out _), $"EntryLine.{name} is missing from the before-snapshot.");

        static IEnumerable<string> Readable(Type t) => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name);
    }

    /// <summary>
    /// 🔴 <b>THE PROVISIONAL-STATE VECTOR IS NO LONGER PUBLICLY WRITABLE.</b> Each of these four decides whether
    /// a voucher affects live balances AT ALL, so writing one moves the books by a whole voucher with no verb and
    /// no warning. <c>Replace</c> refuses a change to all four by name — but that refusal bound <c>Replace</c>
    /// and nothing else while the setters were public, which is this project's standing finding that "a guard
    /// that exists only in one caller is a guard that is already half missing". Making the setters
    /// <c>internal</c> is what turns the refusal into a property of the domain.
    /// </summary>
    [Theory]
    [InlineData(nameof(Voucher.Cancelled))]
    [InlineData(nameof(Voucher.Optional))]
    [InlineData(nameof(Voucher.PostDated))]
    [InlineData(nameof(Voucher.ApplicableUpto))]
    public void A_posted_vouchers_lifecycle_state_cannot_be_moved_from_outside_the_engine(string property)
    {
        var setter = typeof(Voucher).GetProperty(property)!.SetMethod;
        Assert.NotNull(setter);
        Assert.False(setter!.IsPublic, $"Voucher.{property} has a PUBLIC setter again — see the banner on Voucher.cs.");
    }
}
