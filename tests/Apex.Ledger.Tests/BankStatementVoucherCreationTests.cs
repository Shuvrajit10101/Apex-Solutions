using Apex.Ledger.Banking;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>8.13 — auto-create vouchers from an imported bank statement</b>
/// (<c>help.tallysolutions.com/auto-create-vouchers/</c>).
///
/// <para>🔴 <b>The first test in this file is the one that matters.</b> The vendor marks every auto-created
/// voucher <b>Optional</b> so a human reviews it before it counts, and this file pins that as an arithmetic fact
/// about the books rather than as a flag on an object: after creating vouchers from a statement, the bank
/// ledger's closing balance, the trial balance and the BRS are all byte-identical to what they were before.
/// Asserting <c>voucher.Optional == true</c> alone is exactly the test that passes on a build where nothing reads
/// the flag.</para>
/// </summary>
public class BankStatementVoucherCreationTests
{
    private static Company Seed(out Domain.Ledger hdfc, out Domain.Ledger rent, out Domain.Ledger sales)
    {
        var c = CompanyFactory.CreateSeeded("Auto Vch Co", new DateOnly(2024, 4, 1));

        hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true);
        c.AddLedger(hdfc);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        sales = new Domain.Ledger(Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Direct Incomes")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        return c;
    }

    private static BankStatementRow Out(string d, string desc, decimal amount, string instr = "") =>
        new(DateOnly.Parse(d), desc, new Money(-amount), instr);

    private static BankStatementRow In(string d, string desc, decimal amount, string instr = "") =>
        new(DateOnly.Parse(d), desc, new Money(amount), instr);

    // ------------------------------------------------------------------ THE SAFETY PROPERTY

    /// <summary>
    /// 🔴 Auto-created vouchers do NOT reach the books. Three independent readings are checked, because each is a
    /// different door money could leak through: the bank ledger's own closing balance, the whole trial balance,
    /// and the Bank Reconciliation transaction list. All three are driven by
    /// <c>LedgerBalances.CountsAsOf</c>, which excludes an Optional voucher — remove <c>optional: true</c> from
    /// <c>BankStatementVoucherCreation.CreateOne</c> and every one of these three assertions fails.
    /// </summary>
    [Fact]
    public void Auto_created_vouchers_do_not_touch_the_books_until_regularised()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 6, 30);

        var beforeBank = LedgerBalances.SignedClosing(c, hdfc, asOf);
        var beforeBrsRows = BankReconciliation.Transactions(c, hdfc, asOf).Count;
        var beforeBrs = BankReconciliation.Build(c, hdfc, asOf);

        var created = BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[]
            {
                new BankStatementVoucherRequest(Out("2024-05-02", "OFFICE RENT MAY", 40000m, "UTR9001"), rent.Id),
                new BankStatementVoucherRequest(Out("2024-06-03", "OFFICE RENT JUN", 40000m, "UTR9002"), rent.Id),
            },
            BankStatementVoucherMode.Multiple);

        Assert.Equal(2, created.Count);
        Assert.All(created, r => Assert.Equal("Payment", r.VoucherTypeName));

        // They ARE on the book as objects...
        Assert.Equal(2, c.Vouchers.Count(v => v.Optional));
        // ...and they contribute exactly nothing to any figure a user reads.
        Assert.Equal(beforeBank, LedgerBalances.SignedClosing(c, hdfc, asOf));
        Assert.Equal(beforeBrsRows, BankReconciliation.Transactions(c, hdfc, asOf).Count);
        var afterBrs = BankReconciliation.Build(c, hdfc, asOf);
        Assert.Equal(beforeBrs.BalanceAsPerBooks.Signed, afterBrs.BalanceAsPerBooks.Signed);
        Assert.Equal(beforeBrs.BalanceAsPerBank.Signed, afterBrs.BalanceAsPerBank.Signed);
    }

    /// <summary>
    /// R — "Mark as Regular &amp; Reconcile" — is the moment the money lands, and it lands ONCE and RECONCILED.
    /// The bank balance moves by exactly the statement amount and the row appears on the BRS carrying the Bank
    /// Date the statement supplied.
    /// </summary>
    [Fact]
    public void Mark_as_regular_and_reconcile_puts_the_entry_on_the_books_with_its_bank_date()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 6, 30);

        var before = LedgerBalances.SignedClosing(c, hdfc, asOf);

        var created = BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[] { new BankStatementVoucherRequest(Out("2024-05-02", "OFFICE RENT MAY", 40000m, "UTR9001"), rent.Id) },
            BankStatementVoucherMode.Multiple);

        Assert.True(BankStatementVoucherCreation.MarkAsRegularAndReconcile(svc, c, created[0].VoucherId, hdfc.Id));

        // Money out of the bank: the closing balance falls by exactly 40,000.
        Assert.Equal(before - 40000m, LedgerBalances.SignedClosing(c, hdfc, asOf));

        var row = Assert.Single(BankReconciliation.Transactions(c, hdfc, asOf));
        Assert.Equal(DrCr.Credit, row.Side);
        Assert.Equal(Money.FromRupees(40000m), row.Amount);
        Assert.Equal(new DateOnly(2024, 5, 2), row.BankDate);   // reconciled, with the statement's date
        Assert.True(row.IsReconciled);

        // The expense reached its ledger too — the entry is whole, not just a bank movement.
        Assert.Equal(40000m, LedgerBalances.SignedClosing(c, rent, asOf));

        // And the voucher is no longer Optional, so it cannot be regularised a second time.
        Assert.False(c.FindVoucher(created[0].VoucherId)!.Optional);
        Assert.False(BankStatementVoucherCreation.MarkAsRegularAndReconcile(svc, c, created[0].VoucherId, hdfc.Id));
        Assert.Equal(before - 40000m, LedgerBalances.SignedClosing(c, hdfc, asOf));
    }

    // ------------------------------------------------------------------ direction

    /// <summary>
    /// Direction comes from the statement's sign, not from the operator: money in becomes a <b>Receipt</b> that
    /// debits the bank, money out a <b>Payment</b> that credits it. Getting this backwards is a silent
    /// sign-flip on every imported line.
    /// </summary>
    [Fact]
    public void Money_in_creates_a_receipt_and_money_out_creates_a_payment()
    {
        var c = Seed(out var hdfc, out var rent, out var income);
        var svc = new LedgerService(c);

        var created = BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[]
            {
                new BankStatementVoucherRequest(In("2024-05-10", "NEFT FROM CLIENT", 125000m, "UTR7"), income.Id),
                new BankStatementVoucherRequest(Out("2024-05-11", "RENT", 40000m, "UTR8"), rent.Id),
            },
            BankStatementVoucherMode.Multiple);

        var receipt = created.Single(r => r.VoucherTypeName == "Receipt");
        Assert.Equal(DrCr.Debit, receipt.BankSide);
        Assert.True(receipt.IsMoneyIn);
        Assert.Equal(Money.FromRupees(125000m), receipt.Amount);

        var payment = created.Single(r => r.VoucherTypeName == "Payment");
        Assert.Equal(DrCr.Credit, payment.BankSide);
        Assert.False(payment.IsMoneyIn);
        Assert.Equal(Money.FromRupees(40000m), payment.Amount);

        // Each voucher balances and carries the bank allocation the reconcile step needs.
        foreach (var r in created)
        {
            var v = c.FindVoucher(r.VoucherId)!;
            Assert.Equal(2, v.Lines.Count);
            Assert.Equal(
                v.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount),
                v.Lines.Where(l => l.Side == DrCr.Credit).Sum(l => l.Amount.Amount));
            var bankLine = v.Lines.Single(l => l.LedgerId == hdfc.Id);
            Assert.NotNull(bankLine.BankAllocation);
            Assert.Null(bankLine.BankAllocation!.BankDate);      // created UNreconciled
        }
    }

    // ------------------------------------------------------------------ consolidate (Alt+F7)

    /// <summary>
    /// Alt+F7 "Create Voucher(Consolidate)": ONE voucher whose <i>"Amount is the combined amount of the bank
    /// entries selected"</i>. Its date is the latest constituent — ours, and documented as ours.
    /// </summary>
    [Fact]
    public void Consolidate_creates_one_voucher_for_the_combined_amount_dated_at_the_latest_line()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);

        var created = BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[]
            {
                new BankStatementVoucherRequest(Out("2024-05-02", "RENT PART 1", 15000m, "A1"), rent.Id),
                new BankStatementVoucherRequest(Out("2024-05-09", "RENT PART 2", 25000m, "A2"), rent.Id),
            },
            BankStatementVoucherMode.Consolidate);

        var only = Assert.Single(created);
        Assert.Equal(Money.FromRupees(40000m), only.Amount);
        Assert.Equal(new DateOnly(2024, 5, 9), only.Date);
        Assert.Equal(2, only.SourceRowCount);
        Assert.Equal(string.Empty, only.InstrumentNumber);      // several references, none describes the whole
        Assert.Equal("RENT PART 1 | RENT PART 2", c.FindVoucher(only.VoucherId)!.Narration);
        Assert.True(c.FindVoucher(only.VoucherId)!.Optional);
    }

    /// <summary>A consolidation that mixed inbound and outbound lines would net two real movements into one
    /// entry of the wrong size and the wrong sign. It is refused.</summary>
    [Fact]
    public void Consolidate_refuses_a_selection_that_mixes_directions()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);

        var ex = Assert.Throws<ArgumentException>(() =>
            BankStatementVoucherCreation.CreateOptionalVouchers(
                svc, c, hdfc,
                new[]
                {
                    new BankStatementVoucherRequest(Out("2024-05-02", "OUT", 15000m), rent.Id),
                    new BankStatementVoucherRequest(In("2024-05-03", "IN", 25000m), rent.Id),
                },
                BankStatementVoucherMode.Consolidate));

        Assert.Contains("cannot mix money coming in with money going out", ex.Message);
        Assert.Empty(c.Vouchers);       // nothing half-created
    }

    /// <summary>Two different Ledger Names cannot share one consolidated entry — the vendor's consolidation has a
    /// single Ledger Name, and splitting the combined amount between two ledgers is not something the statement
    /// says how to do.</summary>
    [Fact]
    public void Consolidate_refuses_a_selection_that_mixes_ledgers()
    {
        var c = Seed(out var hdfc, out var rent, out var income);
        var svc = new LedgerService(c);

        var ex = Assert.Throws<ArgumentException>(() =>
            BankStatementVoucherCreation.CreateOptionalVouchers(
                svc, c, hdfc,
                new[]
                {
                    new BankStatementVoucherRequest(Out("2024-05-02", "A", 15000m), rent.Id),
                    new BankStatementVoucherRequest(Out("2024-05-03", "B", 25000m), income.Id),
                },
                BankStatementVoucherMode.Consolidate));

        Assert.Contains("ONE Ledger Name", ex.Message);
        Assert.Empty(c.Vouchers);
    }

    // ------------------------------------------------------------------ guards

    /// <summary>The Ledger Name column is mandatory and is validated BEFORE anything is posted, so a selection
    /// with one bad row creates nothing rather than a partial batch.</summary>
    [Fact]
    public void An_unknown_ledger_name_creates_nothing_at_all()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);

        Assert.Throws<ArgumentException>(() =>
            BankStatementVoucherCreation.CreateOptionalVouchers(
                svc, c, hdfc,
                new[]
                {
                    new BankStatementVoucherRequest(Out("2024-05-02", "GOOD", 15000m), rent.Id),
                    new BankStatementVoucherRequest(Out("2024-05-03", "BAD", 25000m), Guid.NewGuid()),
                },
                BankStatementVoucherMode.Multiple));

        Assert.Empty(c.Vouchers);
    }

    /// <summary>Naming the bank account as its own other side would post a self-cancelling entry that moves no
    /// money while looking like it did.</summary>
    [Fact]
    public void The_bank_account_cannot_be_its_own_contra_ledger()
    {
        var c = Seed(out var hdfc, out _, out _);
        var svc = new LedgerService(c);

        var ex = Assert.Throws<ArgumentException>(() =>
            BankStatementVoucherCreation.CreateOptionalVouchers(
                svc, c, hdfc,
                new[] { new BankStatementVoucherRequest(Out("2024-05-02", "X", 15000m), hdfc.Id) },
                BankStatementVoucherMode.Multiple));

        Assert.Contains("must be a different ledger", ex.Message);
        Assert.Empty(c.Vouchers);
    }

    /// <summary>An empty selection is an operator slip, not a request to create nothing quietly.</summary>
    [Fact]
    public void An_empty_selection_is_refused()
    {
        var c = Seed(out var hdfc, out _, out _);
        var svc = new LedgerService(c);

        Assert.Throws<ArgumentException>(() =>
            BankStatementVoucherCreation.CreateOptionalVouchers(
                svc, c, hdfc, Array.Empty<BankStatementVoucherRequest>(), BankStatementVoucherMode.Multiple));
    }

    // ------------------------------------------------------------------ the review screen

    /// <summary>
    /// The "Bank Reconciliation – Optional Vouchers" list: the Optional vouchers touching this bank account,
    /// oldest first, and it EMPTIES as each is regularised — which is what makes it a review queue rather than a
    /// log.
    /// </summary>
    [Fact]
    public void The_optional_vouchers_queue_lists_pending_entries_and_empties_as_they_are_regularised()
    {
        var c = Seed(out var hdfc, out var rent, out var income);
        var svc = new LedgerService(c);

        BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[]
            {
                new BankStatementVoucherRequest(Out("2024-05-20", "LATER", 1000m), rent.Id),
                new BankStatementVoucherRequest(In("2024-05-02", "EARLIER", 2000m), income.Id),
            },
            BankStatementVoucherMode.Multiple);

        var queue = BankStatementVoucherCreation.OptionalVouchersFor(c, hdfc);
        Assert.Equal(2, queue.Count);
        Assert.Equal(new DateOnly(2024, 5, 2), queue[0].Date);      // oldest first
        Assert.Equal("Consultancy Income", queue[0].ContraLedgerName);
        Assert.Equal("Rent", queue[1].ContraLedgerName);

        Assert.True(BankStatementVoucherCreation.MarkAsRegularAndReconcile(svc, c, queue[0].VoucherId, hdfc.Id));
        var after = BankStatementVoucherCreation.OptionalVouchersFor(c, hdfc);
        Assert.Equal(new[] { "Rent" }, after.Select(r => r.ContraLedgerName));
    }

    /// <summary>A regularise records an edit-log line, so the flag change is auditable. The BEFORE snapshot is
    /// the Optional state it left.</summary>
    [Fact]
    public void Regularising_writes_an_edit_log_line_whose_snapshot_is_the_optional_state()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);

        var created = BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[] { new BankStatementVoucherRequest(Out("2024-05-02", "RENT", 40000m), rent.Id) },
            BankStatementVoucherMode.Multiple);

        Assert.Empty(c.VoucherEditLog);
        BankStatementVoucherCreation.MarkAsRegularAndReconcile(svc, c, created[0].VoucherId, hdfc.Id);

        var entry = Assert.Single(c.VoucherEditLog);
        Assert.Equal(created[0].VoucherId, entry.VoucherId);
        Assert.Equal(VoucherEditVerb.Alter, entry.Verb);
        // The snapshot is the serialised pre-change voucher, so the flag it left is legible in it.
        Assert.Contains("\"Optional\":true", entry.BeforeSnapshot);
        Assert.False(c.FindVoucher(created[0].VoucherId)!.Optional);
    }

    // ------------------------------------------------------------------ 8.13 is not 8.3

    /// <summary>
    /// The two rows are genuinely different capabilities and this pins the boundary: <c>MatchAndReconcile</c>
    /// reconciles what is already on the books and creates NOTHING, so an unmatched statement line stays unmatched
    /// until someone chooses a ledger for it. Row 8.13 is what turns that leftover into an entry.
    /// </summary>
    [Fact]
    public void Matching_alone_never_creates_a_voucher_which_is_why_8_13_exists()
    {
        var c = Seed(out var hdfc, out var rent, out _);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 6, 30);

        var rows = new[] { Out("2024-05-02", "OFFICE RENT MAY", 40000m, "UTR9001") };
        var result = BankStatementImport.MatchAndReconcile(c, hdfc, asOf, rows);

        Assert.Equal(0, result.MatchedCount);
        Assert.Single(result.UnmatchedStatementRows);
        Assert.Empty(c.Vouchers);                   // reconciliation created nothing

        BankStatementVoucherCreation.CreateOptionalVouchers(
            svc, c, hdfc,
            new[] { new BankStatementVoucherRequest(result.UnmatchedStatementRows[0], rent.Id) },
            BankStatementVoucherMode.Multiple);

        Assert.Single(c.Vouchers);                  // 8.13 did
    }
}
