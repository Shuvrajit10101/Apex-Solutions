using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The ENGINE CONTRACT of <see cref="LedgerService.Replace(Guid, Voucher)"/> — phase 10.11 slice S5a,
/// design §6.5. One test per contract clause, plus the §3.4 bank-date carry-forward and the §7.3 T-2
/// rejected-replacement guarantee.
/// </summary>
public class VoucherReplaceEngineTests
{
    // -------------------------------------------------------------------------------------------------
    // The snapshot helper's own precondition: two books built the same way must snapshot IDENTICALLY,
    // or every equivalence assertion below would be vacuous (or impossible).
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Two_independently_built_identical_books_snapshot_identically()
    {
        var a = LifecycleBook.Build(LifecycleBook.RightTotal);
        var b = LifecycleBook.Build(LifecycleBook.RightTotal);

        Assert.Equal(
            DerivedStateSnapshot.Snapshot(a.Company, LifecycleBook.AsOf),
            DerivedStateSnapshot.Snapshot(b.Company, LifecycleBook.AsOf));
    }

    [Fact]
    public void The_snapshot_sees_a_one_paisa_difference()
    {
        var a = LifecycleBook.Build(LifecycleBook.RightTotal);
        var b = LifecycleBook.Build(LifecycleBook.RightTotal + Money.FromRupees(0.01m));

        Assert.NotEqual(
            DerivedStateSnapshot.Snapshot(a.Company, LifecycleBook.AsOf),
            DerivedStateSnapshot.Snapshot(b.Company, LifecycleBook.AsOf));
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 1 — validate the replacement BEFORE removing the original (§6.5(1), §7.3 T-2).
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_rejected_replacement_leaves_the_book_byte_identical_and_the_original_at_its_index()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var before = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var originalIndex = book.Company.Vouchers.ToList().FindIndex(v => v.Id == book.TenthId);

        // Dr 1,84,731.95 / Cr 1,84,730.95 — off by exactly ₹1.
        var unbalanced = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal - Money.FromRupees(1m), DrCr.Credit),
            },
            narration: LifecycleBook.TenthNarration);

        Assert.Throws<UnbalancedVoucherException>(() => book.Service.Replace(book.TenthId, unbalanced));

        // Not "still present" — byte-identical, string-for-string, across the ENTIRE derived surface.
        Assert.Equal(before, DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf));
        Assert.Same(original, book.Company.Vouchers[originalIndex]);
        Assert.Equal(11, book.Company.Vouchers.Count);
        Assert.Equal(LifecycleBook.WrongTotal, book.Company.Vouchers[originalIndex].TotalDebit);
    }

    [Fact]
    public void A_replacement_rejected_for_a_non_balance_reason_also_leaves_the_book_untouched()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var before = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        // Balanced, but references a ledger that does not exist.
        var unknownLedger = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(Guid.NewGuid(), LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            });

        Assert.Throws<InvalidVoucherException>(() => book.Service.Replace(book.TenthId, unknownLedger));
        Assert.Equal(before, DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf));
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 2 — the Guid is preserved.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_preserves_the_voucher_Guid()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var accepted = book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        Assert.Equal(book.TenthId, accepted.Id);
        Assert.NotNull(book.Company.FindVoucher(book.TenthId));
        Assert.Same(accepted, book.Company.FindVoucher(book.TenthId));
    }

    [Fact]
    public void Replace_refuses_a_replacement_that_carries_a_different_Guid()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var before = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);

        var reKeyed = LifecycleBook.SalesVoucher(
            book, Guid.NewGuid(), original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, reKeyed));
        Assert.Contains("must preserve the voucher's identity", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf));
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 3 — the Number is preserved.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_preserves_the_number_of_a_mid_sequence_voucher_when_the_replacement_carries_zero()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        Assert.Equal(10, original.Number);

        var replacement = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);
        Assert.Equal(0, replacement.Number);   // exactly the shape Post would renumber to max+1

        var accepted = book.Service.Replace(book.TenthId, replacement);

        Assert.Equal(10, accepted.Number);
        Assert.Equal(12, book.Service.NextNumber(book.SalesType.Id));   // 11 is still the top of the book
    }

    [Fact]
    public void Replace_refuses_a_renumber_rather_than_discarding_it()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var renumbered = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);
        renumbered.Number = 99;

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, renumbered));
        Assert.Contains("preserves the voucher number", ex.Message, StringComparison.Ordinal);
        Assert.Equal(10, book.Company.FindVoucher(book.TenthId)!.Number);
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 4 — the list index is preserved.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_keeps_the_voucher_at_its_own_index_instead_of_moving_it_to_the_end()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var indexBefore = book.Company.Vouchers.ToList().FindIndex(v => v.Id == book.TenthId);
        Assert.Equal(9, indexBefore);
        var original = book.Company.Vouchers[indexBefore];

        book.Service.Replace(book.TenthId, LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration));

        Assert.Equal(9, book.Company.Vouchers.ToList().FindIndex(v => v.Id == book.TenthId));
        Assert.Equal(11, book.Company.Vouchers.Count);

        // And the neighbours did not shuffle.
        Assert.Equal(9, book.Company.Vouchers[8].Number);
        Assert.Equal(11, book.Company.Vouchers[10].Number);
    }

    [Fact]
    public void The_half_rupee_correction_is_equally_visible()
    {
        // §7.5 requires the −₹0.50 variant alongside the −₹1.50 one: a rupee-rounded assertion sees the first
        // and not the second, so both are exercised.
        var a = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var b = LifecycleBook.Build(LifecycleBook.HalfRupeeTotal);
        var original = a.Company.Vouchers.Single(v => v.Id == a.TenthId);

        a.Service.Replace(a.TenthId, LifecycleBook.SalesVoucher(
            a, a.TenthId, original.Date, LifecycleBook.HalfRupeeTotal, LifecycleBook.TenthNarration));

        Assert.Equal(
            DerivedStateSnapshot.Snapshot(b.Company, LifecycleBook.AsOf),
            DerivedStateSnapshot.Snapshot(a.Company, LifecycleBook.AsOf));
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 5 / scope refusals — Date and TypeId are get-only, which is WHY the signature takes a voucher.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_changes_the_date_and_warns_rather_than_refusing()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var newDate = original.Date.AddDays(3);

        var accepted = book.Service.Replace(
            book.TenthId,
            LifecycleBook.SalesVoucher(book, book.TenthId, newDate, LifecycleBook.RightTotal, LifecycleBook.TenthNarration),
            out var warnings);

        Assert.Equal(newDate, accepted.Date);
        Assert.Equal(10, accepted.Number);
        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.DateChanged, warning.Code);
    }

    [Fact]
    public void Replace_refuses_a_voucher_type_change()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var journal = book.Company.FindVoucherTypeByName("Journal")!;

        var retyped = new Voucher(
            book.TenthId, journal.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            });

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, retyped));
        Assert.Contains("does not change a voucher's type", ex.Message, StringComparison.Ordinal);
        Assert.Equal(book.SalesType.Id, book.Company.FindVoucher(book.TenthId)!.TypeId);
    }

    [Fact]
    public void Replace_refuses_to_become_a_back_door_un_cancel()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.Service.Cancel(book.TenthId);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        Assert.True(original.Cancelled);

        // cancelled defaults to false on a freshly built replacement — i.e. the accidental un-cancel.
        var unCancelling = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, unCancelling));
        Assert.Contains("cancelled status", ex.Message, StringComparison.Ordinal);
        Assert.True(book.Company.FindVoucher(book.TenthId)!.Cancelled);
    }

    [Fact]
    public void Replace_of_an_unknown_voucher_throws_and_touches_nothing()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var before = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);
        var strangerId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => book.Service.Replace(
            strangerId,
            LifecycleBook.SalesVoucher(
                book, strangerId, LifecycleBook.BooksBegin.AddDays(1), LifecycleBook.RightTotal, null)));

        Assert.Equal(before, DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf));
    }

    // -------------------------------------------------------------------------------------------------
    // §3.4 — T-6: the bank reconciliation date. The defect that is not in plan.md and that no existing
    // test would notice.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_narration_only_alteration_keeps_the_bank_reconciliation_date()
    {
        var book = BankBook.Build(out var paymentId);
        Assert.True(BankReconciliation.SetBankDate(
            book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9)));

        var original = book.Company.FindVoucher(paymentId)!;
        var accepted = book.Service.Replace(
            paymentId,
            BankBook.Payment(book, paymentId, original.Date, BankBook.Amount, "narration changed, nothing else"),
            out var warnings);

        var bankLine = accepted.Lines.Single(l => l.LedgerId == book.Bank.Id);
        Assert.Equal(new DateOnly(2024, 4, 9), bankLine.BankAllocation!.BankDate);
        Assert.True(bankLine.BankAllocation.IsReconciled);
        Assert.Empty(warnings);
    }

    [Fact]
    public void An_amount_change_clears_the_bank_reconciliation_date_and_says_so()
    {
        var book = BankBook.Build(out var paymentId);
        Assert.True(BankReconciliation.SetBankDate(
            book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9)));

        var original = book.Company.FindVoucher(paymentId)!;
        var accepted = book.Service.Replace(
            paymentId,
            BankBook.Payment(book, paymentId, original.Date, BankBook.AmendedAmount, "amount corrected"),
            out var warnings);

        var bankLine = accepted.Lines.Single(l => l.LedgerId == book.Bank.Id);
        Assert.Null(bankLine.BankAllocation!.BankDate);
        Assert.False(bankLine.BankAllocation.IsReconciled);

        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateCleared, warning.Code);
        Assert.Equal(book.Bank.Id, warning.LedgerId);
        Assert.Equal(new DateOnly(2024, 4, 9), warning.BankDate);
        Assert.Contains("Bank reconciliation date", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropping_the_reconciled_bank_line_entirely_warns_too()
    {
        var book = BankBook.Build(out var paymentId);
        Assert.True(BankReconciliation.SetBankDate(
            book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9)));
        var original = book.Company.FindVoucher(paymentId)!;

        // Same payment, settled out of Cash instead of the bank — the reconciled line is gone.
        var viaCash = new Voucher(
            paymentId, book.PaymentType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Supplier.Id, BankBook.Amount, DrCr.Debit),
                new EntryLine(book.Cash.Id, BankBook.Amount, DrCr.Credit),
            });

        book.Service.Replace(paymentId, viaCash, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, warning.Code);
        Assert.Equal(book.Bank.Id, warning.LedgerId);
    }

    [Fact]
    public void A_changed_cheque_number_is_a_different_instrument_and_does_not_inherit_the_reconciliation()
    {
        var book = BankBook.Build(out var paymentId);
        Assert.True(BankReconciliation.SetBankDate(
            book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9)));
        var original = book.Company.FindVoucher(paymentId)!;

        var reissued = BankBook.Payment(
            book, paymentId, original.Date, BankBook.Amount, "cheque re-issued", instrumentNumber: "778899");

        var accepted = book.Service.Replace(paymentId, reissued, out var warnings);

        Assert.Null(accepted.Lines.Single(l => l.LedgerId == book.Bank.Id).BankAllocation!.BankDate);
        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, warning.Code);
    }

    [Fact]
    public void A_bank_date_the_caller_states_is_never_overwritten_by_the_carry_forward()
    {
        var book = BankBook.Build(out var paymentId);
        Assert.True(BankReconciliation.SetBankDate(
            book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9)));
        var original = book.Company.FindVoucher(paymentId)!;

        var stated = BankBook.Payment(
            book, paymentId, original.Date, BankBook.Amount, "re-reconciled", bankDate: new DateOnly(2024, 4, 12));

        var accepted = book.Service.Replace(paymentId, stated, out var warnings);

        Assert.Equal(new DateOnly(2024, 4, 12), accepted.Lines.Single(l => l.LedgerId == book.Bank.Id).BankAllocation!.BankDate);
        Assert.Empty(warnings);
    }

    [Fact]
    public void An_unreconciled_bank_line_carries_nothing_and_warns_about_nothing()
    {
        var book = BankBook.Build(out var paymentId);
        var original = book.Company.FindVoucher(paymentId)!;

        var accepted = book.Service.Replace(
            paymentId,
            BankBook.Payment(book, paymentId, original.Date, BankBook.AmendedAmount, "amount corrected"),
            out var warnings);

        Assert.Null(accepted.Lines.Single(l => l.LedgerId == book.Bank.Id).BankAllocation!.BankDate);
        Assert.Empty(warnings);
    }

    [Fact]
    public void The_bank_reconciliation_statement_follows_the_carried_date_through_the_alteration()
    {
        var book = BankBook.Build(out var paymentId);
        BankReconciliation.SetBankDate(book.Company, paymentId, book.Bank.Id, new DateOnly(2024, 4, 9));
        var original = book.Company.FindVoucher(paymentId)!;

        var before = BankReconciliation.Build(book.Company, book.Bank, new DateOnly(2024, 4, 30));
        book.Service.Replace(paymentId, BankBook.Payment(
            book, paymentId, original.Date, BankBook.Amount, "narration changed, nothing else"));
        var after = BankReconciliation.Build(book.Company, book.Bank, new DateOnly(2024, 4, 30));

        Assert.Equal(before.BalanceAsPerBank.Signed, after.BalanceAsPerBank.Signed);
        Assert.Equal(before.BalanceAsPerBooks.Signed, after.BalanceAsPerBooks.Signed);
        Assert.Equal(before.Reconciled.Count, after.Reconciled.Count);
        Assert.Equal(before.AmountNotReflectedInBank, after.AmountNotReflectedInBank);
    }
}

/// <summary>A one-payment bank book: Dr a supplier / Cr a bank ledger through a cheque.</summary>
public sealed class BankBook
{
    public static readonly Money Amount = Money.FromRupees(47239.55m);
    public static readonly Money AmendedAmount = Money.FromRupees(47241.05m);

    public required Company Company { get; init; }
    public required LedgerService Service { get; init; }
    public required Domain.Ledger Bank { get; init; }
    public required Domain.Ledger Cash { get; init; }
    public required Domain.Ledger Supplier { get; init; }
    public required VoucherType PaymentType { get; init; }

    public static BankBook Build(out Guid paymentId)
    {
        var company = CompanyFactory.CreateSeeded("Bank Co", LifecycleBook.BooksBegin, LifecycleBook.BooksBegin);

        var bankGroup = company.FindGroupByName("Bank Accounts")!;
        var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Current", bankGroup.Id, Money.FromRupees(500000m), openingIsDebit: true);
        company.AddLedger(bank);

        var creditorGroup = company.FindGroupByName("Sundry Creditors")!;
        var supplier = new Domain.Ledger(Guid.NewGuid(), "A Supplier", creditorGroup.Id, Money.FromRupees(100000m), openingIsDebit: false);
        company.AddLedger(supplier);

        var book = new BankBook
        {
            Company = company,
            Service = new LedgerService(company),
            Bank = bank,
            Cash = company.FindLedgerByName("Cash")!,
            Supplier = supplier,
            PaymentType = company.FindVoucherTypeByName("Payment")!,
        };

        paymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, paymentId, LifecycleBook.BooksBegin.AddDays(4), Amount, "cheque to supplier"));
        return book;
    }

    public static Voucher Payment(
        BankBook book,
        Guid id,
        DateOnly date,
        Money amount,
        string? narration,
        string instrumentNumber = "445566",
        DateOnly? bankDate = null)
        => new(
            id,
            book.PaymentType.Id,
            date,
            new[]
            {
                new EntryLine(book.Supplier.Id, amount, DrCr.Debit),
                new EntryLine(
                    book.Bank.Id, amount, DrCr.Credit,
                    bankAllocation: new BankAllocation(
                        BankTransactionType.ChequeOrDD, instrumentNumber, new DateOnly(2024, 4, 5), bankDate)),
            },
            narration: narration);
}
