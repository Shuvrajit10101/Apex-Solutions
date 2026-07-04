using Apex.Ledger.Banking;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Banking tests (catalog §8; plan.md §5, §8): bank-allocation capture on a bank-ledger line
/// (transaction type / instrument / bank date), bank-ledger detection + validation, post-dated future
/// vouchers excluded from balances until their date, the Bank Reconciliation book-vs-bank pair with an
/// uncleared cheque, the SetBankDate reconcile API, and the CSV statement import + auto-match (by
/// amount + instrument, and by amount + nearby date), reporting unmatched rows.
/// </summary>
public class BankingTests
{
    // A company with Cash (opening 10,00,000 Dr), an "HDFC Bank" ledger under Bank Accounts, a "Bank
    // OD" ledger under Bank OD A/c, a Sundry-Creditor party "Acme", and a Rent expense ledger.
    private static Company Seed(
        out Domain.Ledger cash,
        out Domain.Ledger hdfc,
        out Domain.Ledger bankOd,
        out Domain.Ledger rent,
        out Domain.Ledger acme,
        out VoucherType payment,
        out VoucherType receipt,
        out VoucherType contra)
    {
        var c = CompanyFactory.CreateSeeded("Bank Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true);
        c.AddLedger(hdfc);

        bankOd = new Domain.Ledger(Guid.NewGuid(), "Bank OD", c.FindGroupByName("Bank OD A/c")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(bankOd);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        acme = new Domain.Ledger(Guid.NewGuid(), "Acme", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(acme);

        payment = c.FindVoucherTypeByName("Payment")!;
        receipt = c.FindVoucherTypeByName("Receipt")!;
        contra = c.FindVoucherTypeByName("Contra")!;

        return c;
    }

    // ---------------------------------------------------------------- bank-ledger detection

    [Fact]
    public void Bank_ledgers_are_detected_under_both_bank_groups()
    {
        var c = Seed(out var cash, out var hdfc, out var bankOd, out _, out _, out _, out _, out _);
        Assert.True(ClassificationRules.IsBankLedger(hdfc, c));
        Assert.True(ClassificationRules.IsBankLedger(bankOd, c));
        Assert.False(ClassificationRules.IsBankLedger(cash, c));
    }

    // ---------------------------------------------------------------- 1. bank allocation capture

    [Fact]
    public void Bank_allocation_on_a_bank_line_captures_type_instrument_and_starts_unreconciled()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        var v = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(20000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(20000m), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.ChequeOrDD,
                    instrumentNumber: "100123",
                    instrumentDate: new DateOnly(2024, 4, 10))),
        }));

        var bankLine = v.Lines.Single(l => l.LedgerId == hdfc.Id);
        Assert.True(bankLine.HasBankAllocation);
        Assert.Equal(BankTransactionType.ChequeOrDD, bankLine.BankAllocation!.TransactionType);
        Assert.Equal("100123", bankLine.BankAllocation.InstrumentNumber);
        Assert.Equal(new DateOnly(2024, 4, 10), bankLine.BankAllocation.InstrumentDate);
        Assert.False(bankLine.BankAllocation.IsReconciled);
        Assert.Null(bankLine.BankAllocation.BankDate);
    }

    [Fact]
    public void Bank_allocation_on_a_non_bank_ledger_is_rejected()
    {
        var c = Seed(out var cash, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        // Attach a bank allocation to a non-bank (Rent) line → must be rejected on posting.
        var ex = Assert.Throws<InvalidVoucherException>(() => svc.Post(
            new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 10), new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(20000m), DrCr.Debit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT)),
                new EntryLine(hdfc.Id, Money.FromRupees(20000m), DrCr.Credit),
            })));
        Assert.Contains("not a bank account", ex.Message);
    }

    // ---------------------------------------------------------------- 2. post-dated semantics

    [Fact]
    public void Post_dated_future_voucher_is_excluded_from_balance_then_included_on_its_date()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        // A post-dated cheque payment dated 2024-05-01, entered "today" 2024-04-15.
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 1), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(30000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(30000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "200055")),
        })
        { PostDated = true });

        // Before the cheque date: the bank ledger is unaffected — still the opening 5,00,000 Dr.
        var before = LedgerBalances.Closing(c, hdfc, new DateOnly(2024, 4, 20));
        Assert.Equal(DrCr.Debit, before.Side);
        Assert.Equal(Money.FromRupees(500000m), before.Amount);

        // Also confirmed by the generic CountsAsOf helper.
        var pd = c.Vouchers.Single();
        Assert.False(LedgerBalances.CountsAsOf(pd, new DateOnly(2024, 4, 20)));

        // On/after the cheque date: it takes effect → 5,00,000 − 30,000 = 4,70,000 Dr.
        var onDate = LedgerBalances.Closing(c, hdfc, new DateOnly(2024, 5, 1));
        Assert.Equal(DrCr.Debit, onDate.Side);
        Assert.Equal(Money.FromRupees(470000m), onDate.Amount);
        Assert.True(LedgerBalances.CountsAsOf(pd, new DateOnly(2024, 5, 1)));
    }

    // ---------------------------------------------------------------- 3. BRS book-vs-bank

    [Fact]
    public void Brs_book_vs_bank_differs_by_an_uncleared_cheque()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        // Cheque 100200 issued for rent 40,000 on 2024-04-05 (a credit to the bank). Not yet cleared.
        var cheque = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(40000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(40000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "100200",
                    instrumentDate: new DateOnly(2024, 4, 5))),
        }));

        var asOf = new DateOnly(2024, 4, 30);
        var brs = BankReconciliation.Build(c, hdfc, asOf);

        // Books already reflect the cheque: 5,00,000 − 40,000 = 4,60,000 Dr.
        Assert.Equal(DrCr.Debit, brs.BalanceAsPerBooks.Side);
        Assert.Equal(Money.FromRupees(460000m), brs.BalanceAsPerBooks.Amount);

        // The bank has NOT seen the cheque yet → bank balance is still 5,00,000 Dr (books + 40,000 back).
        Assert.Equal(DrCr.Debit, brs.BalanceAsPerBank.Side);
        Assert.Equal(Money.FromRupees(500000m), brs.BalanceAsPerBank.Amount);

        Assert.Single(brs.Transactions);
        Assert.Single(brs.Unreconciled);
        Assert.Empty(brs.Reconciled);
        // Difference = the uncleared −40,000 movement (signed books − signed bank).
        Assert.Equal(Money.FromRupees(-40000m), brs.AmountNotReflectedInBank);

        // Reconcile: the cheque clears on 2024-04-28.
        var chequeLine = cheque.Lines.Single(l => l.LedgerId == hdfc.Id);
        Assert.True(BankReconciliation.SetBankDate(c, cheque.Id, hdfc.Id, new DateOnly(2024, 4, 28)));
        Assert.Equal(new DateOnly(2024, 4, 28), chequeLine.BankAllocation!.BankDate);

        var brs2 = BankReconciliation.Build(c, hdfc, asOf);
        // Now both balances agree at 4,60,000 Dr and nothing is outstanding.
        Assert.Equal(Money.FromRupees(460000m), brs2.BalanceAsPerBank.Amount);
        Assert.Equal(DrCr.Debit, brs2.BalanceAsPerBank.Side);
        Assert.Empty(brs2.Unreconciled);
        Assert.Single(brs2.Reconciled);
        Assert.Equal(Money.Zero, brs2.AmountNotReflectedInBank);
    }

    [Fact]
    public void Brs_treats_a_cheque_cleared_after_the_asof_date_as_still_uncleared()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        var cheque = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(10000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(10000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "100201")),
        }));

        // Cleared 2024-05-10, but the BRS is as of 2024-04-30 → the clearance is in the future.
        BankReconciliation.SetBankDate(c, cheque.Id, hdfc.Id, new DateOnly(2024, 5, 10));
        var brs = BankReconciliation.Build(c, hdfc, new DateOnly(2024, 4, 30));

        Assert.Single(brs.Unreconciled);   // not cleared as of the report date
        Assert.Equal(Money.FromRupees(500000m), brs.BalanceAsPerBank.Amount); // bank still shows opening
    }

    // ---------------------------------------------------------------- 4. statement import + auto-match

    [Fact]
    public void Csv_statement_parses_rows_skipping_the_header()
    {
        var csv = """
            Date,Description,Amount,Instrument
            2024-04-06,Cheque 100200 cleared,-40000,100200
            2024-04-08,NEFT from customer,25000,UTR9931
            """;

        var rows = BankStatementImport.ParseCsv(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2024, 4, 6), rows[0].Date);
        Assert.Equal(Money.FromRupees(-40000m), rows[0].Amount);
        Assert.Equal("100200", rows[0].InstrumentNumber);
        Assert.Equal(Money.FromRupees(40000m), rows[0].Magnitude);
        Assert.Equal("UTR9931", rows[1].InstrumentNumber);
    }

    [Fact]
    public void Statement_import_auto_matches_by_amount_and_instrument_and_reports_unmatched()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out var receipt, out _);
        var svc = new LedgerService(c);

        // Two book transactions on HDFC: a 40,000 cheque OUT (credit) and a 25,000 NEFT IN (debit).
        var cheque = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(40000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(40000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "100200")),
        }));
        var neft = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 7), new[]
        {
            new EntryLine(hdfc.Id, Money.FromRupees(25000m), DrCr.Debit,
                bankAllocation: new BankAllocation(BankTransactionType.NEFT, "UTR9931")),
            new EntryLine(rent.Id, Money.FromRupees(25000m), DrCr.Credit),
        }));
        // A third book transaction (7,000 cheque) that the statement will NOT contain.
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 9), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(7000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(7000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "100201")),
        }));

        // Statement: the cheque (matches by amount+instrument), the NEFT (matches), and an unrelated row.
        var csv = """
            Date,Description,Amount,Instrument
            2024-04-06,Cheque 100200,-40000,100200
            2024-04-08,NEFT credit,25000,UTR9931
            2024-04-08,Unknown bank charge,-150,
            """;
        var rows = BankStatementImport.ParseCsv(csv);

        var result = BankStatementImport.MatchAndReconcile(c, hdfc, new DateOnly(2024, 4, 30), rows);

        Assert.Equal(2, result.MatchedCount);
        // The cheque and NEFT book lines now carry a Bank Date from the statement.
        Assert.Equal(new DateOnly(2024, 4, 6),
            cheque.Lines.Single(l => l.LedgerId == hdfc.Id).BankAllocation!.BankDate);
        Assert.Equal(new DateOnly(2024, 4, 8),
            neft.Lines.Single(l => l.LedgerId == hdfc.Id).BankAllocation!.BankDate);

        // The 150 charge row could not be matched; the 7,000 cheque book line stays unmatched.
        var unmatchedRow = Assert.Single(result.UnmatchedStatementRows);
        Assert.Equal(Money.FromRupees(-150m), unmatchedRow.Amount);
        var unmatchedBook = Assert.Single(result.UnmatchedBookTransactions);
        Assert.Equal(Money.FromRupees(7000m), unmatchedBook.Amount);
        Assert.Equal("100201", unmatchedBook.InstrumentNumber);

        // After reconciliation the BRS shows only the 7,000 cheque outstanding.
        var brs = BankReconciliation.Build(c, hdfc, new DateOnly(2024, 4, 30));
        Assert.Single(brs.Unreconciled);
        Assert.Equal(Money.FromRupees(-7000m), brs.AmountNotReflectedInBank);
    }

    [Fact]
    public void Statement_import_matches_by_amount_and_nearby_date_when_no_instrument()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        // A cash-deposit style bank line with NO instrument number.
        var dep = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(9000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(9000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.Cash)),
        }));

        // Statement row: same signed amount, no instrument, dated two days later → within tolerance.
        var rows = BankStatementImport.ParseCsv("2024-04-14,ATM cash,-9000,");
        var result = BankStatementImport.MatchAndReconcile(c, hdfc, new DateOnly(2024, 4, 30), rows, dateToleranceDays: 3);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(new DateOnly(2024, 4, 14),
            dep.Lines.Single(l => l.LedgerId == hdfc.Id).BankAllocation!.BankDate);

        // Same scenario but 10 days apart → outside tolerance, no match.
        var c2 = Seed(out _, out var hdfc2, out _, out var rent2, out _, out var payment2, out _, out _);
        var svc2 = new LedgerService(c2);
        svc2.Post(new Voucher(Guid.NewGuid(), payment2.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent2.Id, Money.FromRupees(9000m), DrCr.Debit),
            new EntryLine(hdfc2.Id, Money.FromRupees(9000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.Cash)),
        }));
        var rows2 = BankStatementImport.ParseCsv("2024-04-25,ATM cash,-9000,");
        var result2 = BankStatementImport.MatchAndReconcile(c2, hdfc2, new DateOnly(2024, 4, 30), rows2, dateToleranceDays: 3);
        Assert.Equal(0, result2.MatchedCount);
        Assert.Single(result2.UnmatchedStatementRows);
    }

    [Fact]
    public void Statement_import_does_not_match_opposite_direction_of_same_magnitude()
    {
        var c = Seed(out _, out var hdfc, out _, out var rent, out _, out var payment, out _, out _);
        var svc = new LedgerService(c);

        // A DEBIT to the bank of 5,000 (money IN); the statement row is a −5,000 (money OUT).
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(hdfc.Id, Money.FromRupees(5000m), DrCr.Debit,
                bankAllocation: new BankAllocation(BankTransactionType.NEFT, "IN5000")),
            new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        var rows = BankStatementImport.ParseCsv("2024-04-12,Outgoing,-5000,IN5000");
        var result = BankStatementImport.MatchAndReconcile(c, hdfc, new DateOnly(2024, 4, 30), rows);
        // Same magnitude but opposite sign → must NOT match.
        Assert.Equal(0, result.MatchedCount);
    }

    // ---------------------------------------------------------------- cheque printing config

    [Fact]
    public void Cheque_printing_config_is_captured_on_a_bank_ledger()
    {
        var c = Seed(out _, out var hdfc, out _, out _, out _, out _, out _, out _);
        hdfc.EnableChequePrinting = true;
        hdfc.ChequePrintingBankName = "HDFC Bank";
        Assert.True(hdfc.EnableChequePrinting);
        Assert.Equal("HDFC Bank", hdfc.ChequePrintingBankName);
    }
}
