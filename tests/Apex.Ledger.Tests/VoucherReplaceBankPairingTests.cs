using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Design §3.4 — the bank reconciliation date, <b>the pairing half</b>. The shipped carry-forward paired old and
/// new bank lines on ledger + instrument identity alone, first-match-wins, and the review took that apart from
/// four directions at once. Every test here is one of those directions, and every one of them corresponds to a
/// source mutant that used to survive Ledger 1768 + Io 414 + Sqlite 241.
///
/// <para><b>The four measured defects.</b> (1) When two bank lines share a ledger AND an instrument identity and
/// one is removed, the WRONG old line was consumed — so the surviving, byte-identical, genuinely reconciled line
/// lost its human-entered tick, and both warnings the operator saw were factually false. (2) Merely REORDERING
/// two such lines destroyed both ticks the same way. (3) The <c>LedgerId</c> clause of the pairing was a dead
/// guard: deleting it let one bank account's reconcile tick migrate onto a DIFFERENT bank account's line.
/// (4) Two of <c>SameBankInstrument</c>'s three clauses were dead guards, so a cheque re-keyed as an NEFT — or
/// re-issued under the same number on a later date — inherited a clearance it never had.</para>
/// </summary>
public class VoucherReplaceBankPairingTests
{
    private static readonly DateOnly Books = new(2024, 4, 1);
    private static readonly DateOnly PaymentDate = Books.AddDays(4);
    private static readonly DateOnly InstrumentDate = Books.AddDays(4);
    private static readonly DateOnly Ticked = Books.AddDays(10);
    private static readonly DateOnly AsOf = new(2025, 3, 31);

    private sealed class Book
    {
        public required Company Company { get; init; }
        public required LedgerService Service { get; init; }
        public required Domain.Ledger Hdfc { get; init; }
        public required Domain.Ledger Icici { get; init; }
        public required Domain.Ledger Supplier { get; init; }
        public required VoucherType PaymentType { get; init; }
        public Guid PaymentId { get; set; }
    }

    private static Book Build()
    {
        var company = CompanyFactory.CreateSeeded("Two Bank Co", Books, Books);

        Domain.Ledger Add(string name, string groupName, decimal opening, bool debit)
        {
            var l = new Domain.Ledger(
                Guid.NewGuid(), name, company.FindGroupByName(groupName)!.Id, Money.FromRupees(opening),
                openingIsDebit: debit);
            company.AddLedger(l);
            return l;
        }

        return new Book
        {
            Company = company,
            Service = new LedgerService(company),
            Hdfc = Add("HDFC Current", "Bank Accounts", 500000m, true),
            Icici = Add("ICICI Current", "Bank Accounts", 500000m, true),
            Supplier = Add("A Supplier", "Sundry Creditors", 100000m, false),
            PaymentType = company.FindVoucherTypeByName("Payment")!,
        };
    }

    private sealed record BankLeg(
        Domain.Ledger Ledger,
        decimal Amount,
        string InstrumentNumber = "445566",
        BankTransactionType Type = BankTransactionType.ChequeOrDD,
        DrCr Side = DrCr.Credit,
        DateOnly? InstrumentOn = null,
        DateOnly? BankDate = null);

    /// <summary>Dr the supplier for the total / Cr each bank leg.</summary>
    private static Voucher Payment(Book book, Guid id, string? narration, params BankLeg[] legs)
    {
        var total = legs.Sum(l => l.Side == DrCr.Credit ? l.Amount : -l.Amount);
        var lines = new List<EntryLine> { new(book.Supplier.Id, Money.FromRupees(total), DrCr.Debit) };
        foreach (var leg in legs)
            lines.Add(new EntryLine(
                leg.Ledger.Id, Money.FromRupees(leg.Amount), leg.Side,
                bankAllocation: new BankAllocation(
                    leg.Type, leg.InstrumentNumber, leg.InstrumentOn ?? InstrumentDate, leg.BankDate)));

        return new Voucher(id, book.PaymentType.Id, PaymentDate, lines, narration: narration);
    }

    private static DateOnly? BankDateOf(Voucher v, Domain.Ledger ledger, decimal amount) =>
        v.Lines.Single(l => l.LedgerId == ledger.Id && l.Amount == Money.FromRupees(amount))
            .BankAllocation!.BankDate;

    // =================================================================================================
    // (1) + (2) — two lines, one ledger, one instrument identity. THE case first-match-wins gets wrong.
    // =================================================================================================

    /// <summary>
    /// The reorder case: nothing added, nothing removed, no amount and no side changed — only the ORDER of two
    /// identical-instrument bank lines. Measured on the shipped code: BOTH reconcile ticks destroyed, and TWO
    /// warnings emitted that each quoted an amount change that did not happen ("from 100.00 to 200.00" and "from
    /// 200.00 to 100.00").
    /// </summary>
    [Fact]
    public void Reordering_two_identical_instrument_bank_lines_keeps_both_reconcile_ticks()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(
            book, book.PaymentId, "two cheques, one number",
            new BankLeg(book.Hdfc, 100m, "555000"),
            new BankLeg(book.Hdfc, 200m, "555000")));

        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "two cheques, one number",
                new BankLeg(book.Hdfc, 200m, "555000"),          // SWAPPED
                new BankLeg(book.Hdfc, 100m, "555000")),
            out var warnings);

        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 100m));
        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 200m));
        Assert.Empty(warnings);
    }

    /// <summary>
    /// The removal case (review finding C, widened). Drop the ₹100 line and keep the ₹200 line byte-identical:
    /// the survivor must keep its tick, and the warning must be about the line that actually went.
    /// </summary>
    [Fact]
    public void Removing_one_of_two_identical_instrument_lines_leaves_the_survivors_tick_alone()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(
            book, book.PaymentId, "two cheques, one number",
            new BankLeg(book.Hdfc, 100m, "555000"),
            new BankLeg(book.Hdfc, 200m, "555000")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(book, book.PaymentId, "one cheque now", new BankLeg(book.Hdfc, 200m, "555000")),
            out var warnings);

        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 200m));

        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, warning.Code);
        Assert.Equal(book.Hdfc.Id, warning.LedgerId);
        Assert.Equal(Ticked, warning.BankDate);
    }

    /// <summary>
    /// The MIRROR of the removal case, and the reason the fix is EXACT-FIRST rather than "flip the scan to
    /// last-match-wins". The review measured that last-match-wins produces the right answer on the reorder and
    /// removal cases above — which invites exactly that one-character "fix". It is wrong: keep the FIRST of two
    /// identical-instrument lines and drop the second, and last-match-wins pairs the surviving ₹100 line with the
    /// departed ₹200 one, destroying a tick and misreporting both halves. Neither single-pass ordering is correct
    /// in general; only pairing exactly first is.
    /// </summary>
    [Fact]
    public void Keeping_the_FIRST_of_two_identical_instrument_lines_also_keeps_its_tick()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(
            book, book.PaymentId, "two cheques, one number",
            new BankLeg(book.Hdfc, 100m, "555000"),
            new BankLeg(book.Hdfc, 200m, "555000")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(book, book.PaymentId, "the 200 line went", new BankLeg(book.Hdfc, 100m, "555000")),
            out var warnings);

        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 100m));

        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, warning.Code);
    }

    // =================================================================================================
    // (3) — the LedgerId clause of the pairing. Deleting it passed the whole gate.
    // =================================================================================================

    /// <summary>
    /// Two DIFFERENT bank accounts, the SAME instrument identity, only one of them reconciled. The tick must stay
    /// on the account that earned it — without the ledger clause it migrated to the other bank.
    /// </summary>
    [Fact]
    public void One_banks_reconcile_tick_never_migrates_to_another_bank()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(
            book, book.PaymentId, "split across two banks",
            new BankLeg(book.Hdfc, 5000m, "777777"),
            new BankLeg(book.Icici, 5000m, "777777")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "split across two banks",
                new BankLeg(book.Icici, 5000m, "777777"),        // ICICI listed FIRST this time
                new BankLeg(book.Hdfc, 5000m, "777777")),
            out var warnings);

        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 5000m));
        Assert.Null(BankDateOf(accepted, book.Icici, 5000m));
        Assert.Empty(warnings);
    }

    // =================================================================================================
    // (4) — SameBankInstrument's other two clauses.
    // =================================================================================================

    /// <summary>A cheque re-keyed as an NEFT under the same number is a DIFFERENT instrument; it must not inherit
    /// the cheque's clearance.</summary>
    [Fact]
    public void A_changed_bank_transaction_type_is_a_different_instrument()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "re-keyed as NEFT",
                new BankLeg(book.Hdfc, 47239.55m, Type: BankTransactionType.NEFT)),
            out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 47239.55m));
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, Assert.Single(warnings).Code);
    }

    /// <summary>A cheque re-issued under the same number on a LATER instrument date is a different instrument
    /// too — same number, same type, different cheque.</summary>
    [Fact]
    public void A_changed_instrument_date_is_a_different_instrument()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "cheque re-issued a fortnight later",
                new BankLeg(book.Hdfc, 47239.55m, InstrumentOn: InstrumentDate.AddDays(15))),
            out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 47239.55m));
        Assert.Equal(VoucherAlterationWarningCode.BankDateLineRemoved, Assert.Single(warnings).Code);
    }

    // =================================================================================================
    // The carry-vs-clear condition, and the message that describes it.
    // =================================================================================================

    /// <summary>
    /// A SIDE flip with an unchanged amount used to be reported as <i>"the line amount changed from 47239.55 to
    /// 47239.55"</i> — a message quoting the same figure twice, which an operator cannot act on. The side clause
    /// itself was also a dead guard: dropping it let the tick survive a Cr → Dr flip.
    /// </summary>
    [Fact]
    public void A_side_flip_clears_the_tick_and_the_warning_names_the_SIDE_not_the_amount()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        // Same ledger, same instrument, same amount, opposite side. The voucher still balances because the
        // supplier leg follows the sign.
        var flipped = new Voucher(
            book.PaymentId, book.PaymentType.Id, PaymentDate,
            new[]
            {
                new EntryLine(book.Supplier.Id, Money.FromRupees(47239.55m), DrCr.Credit),
                new EntryLine(
                    book.Hdfc.Id, Money.FromRupees(47239.55m), DrCr.Debit,
                    bankAllocation: new BankAllocation(
                        BankTransactionType.ChequeOrDD, "445566", InstrumentDate, null)),
            },
            narration: "side flipped");

        var accepted = book.Service.Replace(book.PaymentId, flipped, out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 47239.55m));
        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateCleared, warning.Code);
        Assert.Contains("side Credit -> Debit", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("47239.55 -> 47239.55", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>An amount change names the amount, and only the amount.</summary>
    [Fact]
    public void An_amount_change_names_the_amount()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        book.Service.Replace(
            book.PaymentId,
            Payment(book, book.PaymentId, "amount corrected", new BankLeg(book.Hdfc, 47241.05m)),
            out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("amount 47239.55 -> 47241.05", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("side", warning.Message, StringComparison.Ordinal);
    }

    // =================================================================================================
    // THE ECHO RULE — the defect S5b would otherwise have reintroduced (review finding L1-07).
    // =================================================================================================

    /// <summary>
    /// A rehydrating caller reads the posted line back — <c>BankDate</c> included — so the replacement arrives
    /// carrying the OLD date. Treating that as "the caller stated it" defeated the entire §3.4 guard: measured on
    /// the shipped code, the amount moved ₹47,239.55 → ₹47,241.05 and the tick stood, with ZERO warnings.
    /// </summary>
    [Fact]
    public void An_echoed_bank_date_does_not_survive_an_amount_change()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        // Verbatim rehydration: same instrument, same carried bank date, but the amount corrected.
        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "amount corrected",
                new BankLeg(book.Hdfc, 47241.05m, BankDate: Ticked)),
            out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 47241.05m));
        Assert.Equal(VoucherAlterationWarningCode.BankDateCleared, Assert.Single(warnings).Code);
    }

    /// <summary>An echoed date on an OTHERWISE UNCHANGED line is simply carried — the echo rule must not turn a
    /// faithful rehydration into a clear.</summary>
    [Fact]
    public void An_echoed_bank_date_on_an_unchanged_line_is_carried()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "narration only",
                new BankLeg(book.Hdfc, 47239.55m, BankDate: Ticked)),
            out var warnings);

        Assert.Equal(Ticked, BankDateOf(accepted, book.Hdfc, 47239.55m));
        Assert.Empty(warnings);
    }

    /// <summary>A date the caller genuinely STATES — different from the posted one — is still honoured untouched,
    /// even when the amount moved. The echo rule discriminates on the VALUE, not on presence.</summary>
    [Fact]
    public void A_genuinely_stated_new_bank_date_is_honoured_even_when_the_amount_moves()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));
        var reReconciled = Ticked.AddDays(3);

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "amount corrected and re-reconciled",
                new BankLeg(book.Hdfc, 47241.05m, BankDate: reReconciled)),
            out var warnings);

        Assert.Equal(reReconciled, BankDateOf(accepted, book.Hdfc, 47241.05m));
        Assert.Empty(warnings);
    }

    // =================================================================================================
    // SPLIT and MERGE — measured as defensible, and locked so they stay that way. The only blemish was the
    // MESSAGE, which used to assert a bare "the line amount changed from 100.00 to 300.00" for what was really
    // two lines becoming one; the wording now describes the PAIRING, which is what actually happened.
    // =================================================================================================

    [Fact]
    public void Splitting_a_reconciled_line_in_two_clears_one_tick_and_invents_none()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "one cheque", new BankLeg(book.Hdfc, 300m, "606060")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "split in two",
                new BankLeg(book.Hdfc, 100m, "606060"),
                new BankLeg(book.Hdfc, 200m, "606060")),
            out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 100m));
        Assert.Null(BankDateOf(accepted, book.Hdfc, 200m));

        var warning = Assert.Single(warnings);
        Assert.Equal(VoucherAlterationWarningCode.BankDateCleared, warning.Code);
        Assert.Contains("no longer matches the reconciled one", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merging_two_reconciled_lines_reports_the_pairing_not_an_amount_change_that_never_happened()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(
            book, book.PaymentId, "two cheques",
            new BankLeg(book.Hdfc, 100m, "707070"),
            new BankLeg(book.Hdfc, 200m, "707070")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));

        var accepted = book.Service.Replace(
            book.PaymentId,
            Payment(book, book.PaymentId, "merged", new BankLeg(book.Hdfc, 300m, "707070")),
            out var warnings);

        Assert.Null(BankDateOf(accepted, book.Hdfc, 300m));
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Code == VoucherAlterationWarningCode.BankDateCleared);
        Assert.Contains(warnings, w => w.Code == VoucherAlterationWarningCode.BankDateLineRemoved);

        // The old wording asserted a line "amount changed from 100.00 to 300.00"; two lines became one, and the
        // message now says that rather than quoting a pairing the operator never made.
        var cleared = warnings.Single(w => w.Code == VoucherAlterationWarningCode.BankDateCleared);
        Assert.Contains("the replacement's matching bank line no longer matches", cleared.Message,
            StringComparison.Ordinal);
    }

    /// <summary>Two DIFFERENT vouchers sharing one instrument number never reach each other — the carry is scoped
    /// to the outgoing voucher's own lines, and this locks that scoping.</summary>
    [Fact]
    public void Two_vouchers_sharing_an_instrument_number_do_not_reach_each_others_reconciliations()
    {
        var book = Build();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        book.PaymentId = aId;
        book.Service.Post(Payment(book, aId, "voucher A", new BankLeg(book.Hdfc, 100m, "999000")));
        book.Service.Post(Payment(book, bId, "voucher B", new BankLeg(book.Hdfc, 200m, "999000")));
        Assert.True(BankReconciliation.SetBankDate(book.Company, aId, book.Hdfc.Id, Ticked));
        Assert.True(BankReconciliation.SetBankDate(book.Company, bId, book.Hdfc.Id, Ticked.AddDays(1)));

        book.Service.Replace(
            aId, Payment(book, aId, "A corrected", new BankLeg(book.Hdfc, 150m, "999000")), out var warnings);

        Assert.Single(warnings);
        Assert.Equal(
            Ticked.AddDays(1),
            book.Company.FindVoucher(bId)!.Lines.Single(l => l.BankAllocation is not null).BankAllocation!.BankDate);
    }

    /// <summary>The clear is an ASSIGNMENT, not a hope that the replacement already carried null: the warning and
    /// the state can never disagree. Asserted through the report the tick actually drives.</summary>
    [Fact]
    public void The_bank_reconciliation_statement_agrees_with_the_warning()
    {
        var book = Build();
        book.PaymentId = Guid.NewGuid();
        book.Service.Post(Payment(book, book.PaymentId, "cheque", new BankLeg(book.Hdfc, 47239.55m)));
        Assert.True(BankReconciliation.SetBankDate(book.Company, book.PaymentId, book.Hdfc.Id, Ticked));
        Assert.Single(BankReconciliation.Build(book.Company, book.Hdfc, AsOf).Reconciled);

        book.Service.Replace(
            book.PaymentId,
            Payment(
                book, book.PaymentId, "amount corrected",
                new BankLeg(book.Hdfc, 47241.05m, BankDate: Ticked)),
            out var warnings);

        Assert.Single(warnings);
        Assert.Empty(BankReconciliation.Build(book.Company, book.Hdfc, AsOf).Reconciled);
    }
}
