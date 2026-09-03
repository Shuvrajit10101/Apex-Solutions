using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Design §7.4 — <b>"Memorandum, Optional, Post-dated, Reversing Journal … the test is that altering one still
/// changes NOTHING in the snapshot except the voucher identity vector"</b>. The design named this family as
/// non-negotiable; the slice shipped without a single test combining <c>Replace</c> with any of the four, and
/// every one of them was a live figure move.
///
/// <para><b>What was measured before these tests existed.</b> An Optional voucher altered by a replacement
/// carrying BYTE-IDENTICAL amounts became a real posting and swung the Sales closing by ₹1,84,733.45. A live
/// voucher altered by a replacement built <c>optional: true</c> dropped the same ₹1,84,733.45 out of the live
/// books. A Reversing Journal accruing ₹3,000 "applicable upto 30-Apr" lost its <c>ApplicableUpto</c> to a
/// narration-only alteration, and the accrual NEVER LAPSED — the scenario figure at 01-May moved 0 → 3,000.
/// All three raised ZERO warnings.</para>
///
/// <para><b>The contract these tests pin — ORCHESTRATOR RULING, superseding the warn-and-proceed these tests
/// originally asserted.</b> <c>Replace</c> <b>REFUSES</b> a change to the provisional-state vector
/// (<c>Optional</c>, <c>PostDated</c>, <c>ApplicableUpto</c>), by name, exactly as it already refuses a
/// <c>Cancelled</c> change and for the identical reason: <c>Replace</c> is for CONTENT, not for lifecycle state,
/// and it must not be a back door to Ctrl+L any more than to Cancel. A replacement that CARRIES the vector is
/// accepted silently, and that half is unchanged — the four tests asserting it are the §7.4 family proper.</para>
///
/// <para><b>Why not carry-when-default</b> (the alternative both review lenses offered): <c>Optional</c> and
/// <c>PostDated</c> are BOOLS, so "left at default" and "explicitly set to false" are indistinguishable, and
/// carrying-when-default would make it impossible to turn an Optional voucher live — silently ignoring a real
/// operator intent. <c>ApplicableUpto</c> is nullable and could express it, but split behaviour across one
/// conceptual vector is worse than one rule.</para>
///
/// <para><b>Why refuse rather than warn:</b> warn-and-proceed cannot distinguish "the operator pressed Ctrl+L"
/// from "the caller forgot to carry the flag" — and S5b's <c>ForAlter</c> rehydration is precisely the caller
/// that will produce the second shape. The refusal turns a silent balance corruption into a test-time failure.
/// <b>R7:</b> this is OUR DELIBERATE NARROWING of an attested TallyPrime behaviour (Ctrl+L / Ctrl+T genuinely
/// ARE alteration-time verbs), not a corpus-silent case — see design §12.8.</para>
/// </summary>
public class VoucherReplaceProvisionalFamilyTests
{
    // -------------------------------------------------------------------------------------------------
    // Optional — the flag LedgerBalance.cs line 47 treats identically to Cancelled.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// §7.4 proper: altering an Optional voucher with a replacement that carries the flag changes NOTHING in the
    /// derived surface except the voucher's own figures. The book must not silently gain ₹1,84,733.45.
    /// </summary>
    [Fact]
    public void Altering_an_Optional_voucher_that_carries_its_flag_leaves_it_out_of_the_live_books()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        original.Optional = true;

        var closingBefore = LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf);

        var replacement = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            },
            narration: LifecycleBook.TenthNarration, partyId: book.Customer.Id, optional: true);

        var accepted = book.Service.Replace(book.TenthId, replacement, out var warnings);

        Assert.True(accepted.Optional);
        Assert.Empty(warnings);
        Assert.Equal(closingBefore, LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf));
    }

    /// <summary>
    /// The silent-regularisation case, now REFUSED. A freshly built replacement leaves <c>Optional</c> at its
    /// default, which used to turn an Optional voucher into a REAL posting — with byte-identical figures, so
    /// nothing else in the alteration hinted at it. That is indistinguishable from an S5b <c>ForAlter</c> that
    /// forgot to carry the flag, so <c>Replace</c> refuses it by name and the ₹1,84,733.45 never arrives.
    /// <para><b>Was:</b> <c>Turning_an_Optional_voucher_live_moves_the_books_and_warns_by_name</c> — asserted the
    /// move HAPPENED (<c>Assert.False(accepted.Optional)</c>, a <c>ProvisionalStateChanged</c> warning, and
    /// <c>closingAfter - closingBefore == -WrongTotal</c>). The harm it documented is now the thing refused.</para>
    /// </summary>
    [Fact]
    public void Turning_an_Optional_voucher_live_is_refused_by_name_and_the_books_do_not_move()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        original.Optional = true;
        var closingBefore = LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf);

        // Same figures in, same figures out — only the flag differs.
        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(
            book.TenthId,
            LifecycleBook.SalesVoucher(
                book, book.TenthId, original.Date, LifecycleBook.WrongTotal, LifecycleBook.TenthNarration)));

        Assert.Contains("provisional state", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Optional changed from Optional to live", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Ctrl+L", ex.Message, StringComparison.Ordinal);      // the remedy names the right verb

        // A refusal, not a partial application: the posted voucher is still Optional and the books never moved.
        Assert.True(book.Company.Vouchers.Single(v => v.Id == book.TenthId).Optional);
        Assert.Equal(
            closingBefore, LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf));
    }

    /// <summary>The MIRROR — a LIVE voucher made Optional by a replacement carrying byte-identical figures used to
    /// drop its whole value out of the real books. Refused by name, and the value stays.
    /// <para><b>Was:</b> <c>Turning_a_live_voucher_Optional_drops_it_from_the_books_and_warns_by_name</c> —
    /// asserted a <c>ProvisionalStateChanged</c> warning and a closing move of exactly +₹1,84,733.45.</para></summary>
    [Fact]
    public void Making_a_live_voucher_Optional_is_refused_by_name_and_its_value_stays_in_the_books()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var closingBefore = LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf);

        var madeOptional = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.WrongTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.WrongTotal, DrCr.Credit),
            },
            narration: LifecycleBook.TenthNarration, partyId: book.Customer.Id, optional: true);

        var ex = Assert.Throws<InvalidOperationException>(
            () => book.Service.Replace(book.TenthId, madeOptional));

        Assert.Contains("Optional changed from live to Optional", ex.Message, StringComparison.Ordinal);
        Assert.False(book.Company.Vouchers.Single(v => v.Id == book.TenthId).Optional);
        Assert.Equal(
            closingBefore, LedgerBalances.SignedClosing(book.Company, book.SalesLedger, LifecycleBook.AsOf));
    }

    /// <summary>
    /// All THREE at once — the refusal names every member of the vector that moved, not merely the first one it
    /// trips over. A caller told only "Optional changed" would carry that flag, re-run, and be refused again on
    /// <c>PostDated</c>; the message has to be actionable in one read.
    /// </summary>
    [Fact]
    public void The_refusal_names_every_member_of_the_vector_that_moved()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        original.Optional = true;
        original.PostDated = true;
        original.ApplicableUpto = new DateOnly(2026, 4, 30);

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(
            book.TenthId,
            LifecycleBook.SalesVoucher(
                book, book.TenthId, original.Date, LifecycleBook.WrongTotal, LifecycleBook.TenthNarration)));

        Assert.Contains("Optional changed from Optional to live", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Post-dated changed from post-dated to live", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Applicable Upto changed from 30-Apr-2026 to (none)", ex.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------
    // Post-dated — Ctrl+T. Same shape, same silence.
    // -------------------------------------------------------------------------------------------------

    /// <summary><b>Was:</b> <c>Dropping_the_post_dated_flag_warns_by_name</c> — asserted
    /// <c>Assert.False(accepted.PostDated)</c> plus a <c>ProvisionalStateChanged</c> warning, i.e. that the drop
    /// went through. Ctrl+T is its own verb; <c>Replace</c> refuses it and the voucher stays post-dated.</summary>
    [Fact]
    public void Dropping_the_post_dated_flag_is_refused_by_name()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        original.PostDated = true;

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(
            book.TenthId,
            LifecycleBook.SalesVoucher(
                book, book.TenthId, original.Date, LifecycleBook.WrongTotal, LifecycleBook.TenthNarration)));

        Assert.Contains("Post-dated changed from post-dated to live", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Ctrl+T", ex.Message, StringComparison.Ordinal);
        Assert.True(book.Company.Vouchers.Single(v => v.Id == book.TenthId).PostDated);
    }

    [Fact]
    public void A_post_dated_voucher_that_carries_its_flag_is_altered_silently()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        original.PostDated = true;

        var carried = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            },
            narration: LifecycleBook.TenthNarration, partyId: book.Customer.Id, postDated: true);

        var accepted = book.Service.Replace(book.TenthId, carried, out var warnings);

        Assert.True(accepted.PostDated);
        Assert.Empty(warnings);
    }

    // -------------------------------------------------------------------------------------------------
    // Reversing Journal — the ApplicableUpto FIELD, not a flag, and the one no fix note exercised.
    // -------------------------------------------------------------------------------------------------

    private sealed class ReversingBook
    {
        public required Company Company { get; init; }
        public required LedgerService Service { get; init; }
        public required Domain.Ledger Rent { get; init; }
        public required Domain.Ledger Outstanding { get; init; }
        public required VoucherType ReversingType { get; init; }
        public required Scenario Scenario { get; init; }
        public required Guid AccrualId { get; init; }
    }

    private static readonly DateOnly Books = new(2024, 4, 1);
    private static readonly Money Accrual = Money.FromRupees(3000m);

    private static ReversingBook BuildReversing(DateOnly? applicableUpto)
    {
        var company = CompanyFactory.CreateSeeded("Reversing Co", Books, Books);

        Domain.Ledger Add(string name, string groupName, bool debit)
        {
            var l = new Domain.Ledger(
                Guid.NewGuid(), name, company.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
            company.AddLedger(l);
            return l;
        }

        var rent = Add("Rent", "Indirect Expenses", true);
        var outstanding = Add("Outstanding Rent", "Current Liabilities", false);
        var reversingType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReversingJournal);

        var scenario = new Scenario(
            Guid.NewGuid(), "With Reversing", includeActuals: true, includedTypeIds: new[] { reversingType.Id });
        company.AddScenario(scenario);

        var service = new LedgerService(company);
        var accrualId = Guid.NewGuid();
        service.Post(new Voucher(
            accrualId, reversingType.Id, Books.AddDays(9),
            new[]
            {
                new EntryLine(rent.Id, Accrual, DrCr.Debit),
                new EntryLine(outstanding.Id, Accrual, DrCr.Credit),
            },
            narration: "rent accrual",
            applicableUpto: applicableUpto));

        return new ReversingBook
        {
            Company = company, Service = service, Rent = rent, Outstanding = outstanding,
            ReversingType = reversingType, Scenario = scenario, AccrualId = accrualId,
        };
    }

    private static Voucher Accruals(ReversingBook b, string narration, DateOnly? applicableUpto) =>
        new(
            b.AccrualId, b.ReversingType.Id, Books.AddDays(9),
            new[]
            {
                new EntryLine(b.Rent.Id, Accrual, DrCr.Debit),
                new EntryLine(b.Outstanding.Id, Accrual, DrCr.Credit),
            },
            narration: narration,
            applicableUpto: applicableUpto);

    /// <summary>
    /// THE test that would have caught the whole family. A Reversing Journal's <c>ApplicableUpto</c> is what makes
    /// it lapse (<c>LedgerBalance.cs line 78</c>); a plain narration-only alteration built by an entry screen
    /// leaves it blank, and the accrual then never reverses out. §7.4's row for this family is precisely
    /// "altering it changes nothing" — asserted here at a date AFTER the lapse.
    /// </summary>
    [Fact]
    public void A_narration_only_alteration_of_a_Reversing_Journal_keeps_it_lapsing_on_time()
    {
        var b = BuildReversing(new DateOnly(2024, 4, 30));

        decimal RentAt(DateOnly asOf) =>
            LedgerBalances.SignedClosing(b.Company, b.Rent, asOf, b.Scenario);

        Assert.Equal(Accrual.Amount, RentAt(new DateOnly(2024, 4, 30)));
        Assert.Equal(0m, RentAt(new DateOnly(2024, 5, 1)));         // lapsed

        b.Service.Replace(b.AccrualId, Accruals(b, "rent accrual (corrected wording)", new DateOnly(2024, 4, 30)),
            out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(Accrual.Amount, RentAt(new DateOnly(2024, 4, 30)));
        Assert.Equal(0m, RentAt(new DateOnly(2024, 5, 1)));         // STILL lapses
    }

    /// <summary>
    /// And when the replacement DOES drop <c>ApplicableUpto</c> — the shape an entry screen that does not carry
    /// the field produces, and exactly the shape S5b's <c>ForAlter</c> will produce if it gets this wrong — the
    /// accrual used to stop lapsing and the scenario figure at 01-May moved 0 → 3,000. Refused by name, so it
    /// still lapses. <b>Note this is the member of the vector for which carry-when-default WOULD have been
    /// expressible</b> (<c>ApplicableUpto</c> is nullable); it is refused with the other two so that one
    /// conceptual vector has one rule.
    /// <para><b>Was:</b> <c>Dropping_ApplicableUpto_stops_the_accrual_lapsing_and_warns_by_name</c> — asserted
    /// <c>Assert.Null(accepted.ApplicableUpto)</c>, a <c>ProvisionalStateChanged</c> warning, and the 01-May
    /// figure standing at ₹3,000 (the accrual NOT lapsing). That figure move is now what is prevented.</para>
    /// </summary>
    [Fact]
    public void Dropping_ApplicableUpto_is_refused_by_name_so_the_accrual_still_lapses()
    {
        var b = BuildReversing(new DateOnly(2024, 4, 30));
        var mayFirst = new DateOnly(2024, 5, 1);
        Assert.Equal(0m, LedgerBalances.SignedClosing(b.Company, b.Rent, mayFirst, b.Scenario));

        var ex = Assert.Throws<InvalidOperationException>(
            () => b.Service.Replace(b.AccrualId, Accruals(b, "rent accrual", applicableUpto: null)));

        Assert.Contains(
            "Applicable Upto changed from 30-Apr-2024 to (none)", ex.Message, StringComparison.Ordinal);

        var posted = b.Company.Vouchers.Single(v => v.Id == b.AccrualId);
        Assert.Equal(new DateOnly(2024, 4, 30), posted.ApplicableUpto);
        Assert.Equal(0m, LedgerBalances.SignedClosing(b.Company, b.Rent, mayFirst, b.Scenario));   // STILL lapses
    }

    /// <summary>Moving the lapse date is refused too, with both dates in the message — a reader has to be able to
    /// tell "the accrual would now have run a month longer" from the refusal alone.
    /// <para><b>Was:</b> <c>Moving_ApplicableUpto_names_both_dates</c> — same message assertion, but on a
    /// <c>ProvisionalStateChanged</c> warning raised while the move was applied.</para></summary>
    [Fact]
    public void Moving_ApplicableUpto_is_refused_and_the_refusal_names_both_dates()
    {
        var b = BuildReversing(new DateOnly(2024, 4, 30));

        var ex = Assert.Throws<InvalidOperationException>(
            () => b.Service.Replace(b.AccrualId, Accruals(b, "rent accrual", new DateOnly(2024, 5, 31))));

        Assert.Contains("30-Apr-2024 to 31-May-2024", ex.Message, StringComparison.Ordinal);
        Assert.Equal(
            new DateOnly(2024, 4, 30), b.Company.Vouchers.Single(v => v.Id == b.AccrualId).ApplicableUpto);
    }

    // -------------------------------------------------------------------------------------------------
    // Memorandum — §7.4's fourth row. A memo never affects the real books; altering it must not change that.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Altering_a_Memorandum_leaves_the_real_books_exactly_where_they_were()
    {
        var company = CompanyFactory.CreateSeeded("Memo Co", Books, Books);
        var expense = new Domain.Ledger(
            Guid.NewGuid(), "Sundry Expenses", company.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        company.AddLedger(expense);
        var cash = company.FindLedgerByName("Cash")!;
        var memoType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Memorandum);

        var service = new LedgerService(company);
        var memoId = Guid.NewGuid();
        Voucher Memo(Money amount, string narration) => new(
            memoId, memoType.Id, Books.AddDays(3),
            new[]
            {
                new EntryLine(expense.Id, amount, DrCr.Debit),
                new EntryLine(cash.Id, amount, DrCr.Credit),
            },
            narration: narration);

        service.Post(Memo(Money.FromRupees(1234.55m), "unvouched petty spend"));
        var before = DerivedStateSnapshot.Snapshot(company, LifecycleBook.AsOf);

        service.Replace(memoId, Memo(Money.FromRupees(9876.45m), "unvouched petty spend, corrected"),
            out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(0m, LedgerBalances.SignedClosing(company, expense, LifecycleBook.AsOf));

        // §7.4 words this as "changes NOTHING in the snapshot except the voucher identity vector". Measured, that
        // phrasing predates the Day Book being IN the instrument: a Memorandum is a real row of the Day Book
        // register, so its own amount and narration move there — correctly. What must NOT move is any balance,
        // valuation, outstanding, cost or return figure, and this assertion is the strict form of that: the only
        // sections allowed to differ are the two that describe the VOUCHER ITSELF.
        var after = DerivedStateSnapshot.Snapshot(company, LifecycleBook.AsOf);
        var changed = Diff(before, after);
        Assert.NotEmpty(changed);
        Assert.All(changed, line => Assert.True(
            line.StartsWith("12.VoucherIdentity", StringComparison.Ordinal)
            || line.StartsWith("14.DayBook", StringComparison.Ordinal),
            $"A Memorandum alteration moved a derived figure outside the voucher's own registers: {line}"));
    }

    private static IReadOnlyList<string> Diff(string before, string after)
    {
        var b = before.Split('\n');
        var a = after.Split('\n');
        var changed = new List<string>();
        for (var i = 0; i < Math.Max(b.Length, a.Length); i++)
        {
            var lb = i < b.Length ? b[i] : "";
            var la = i < a.Length ? a[i] : "";
            if (!string.Equals(lb, la, StringComparison.Ordinal)) changed.Add(la.Length > 0 ? la : lb);
        }

        return changed;
    }
}
