using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// The two pure projections this wave adds: the <b>Cheque Register</b> (census row 8.5) and the
/// <b>Deposit Slip</b> (census row 8.6).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-register/</c> — the six status buckets
/// (<i>Available</i> = "still unused and can be issued", <i>Unreconciled</i> = "transactions that are not
/// complete", <i>Reconciled</i>, <i>Blank</i> = "physical inventory of the cheques", <i>Cancelled</i> = "voided
/// due to errors"), the per-bank scope, the range scope, the drill, and the case of cheques that belong to no
/// range. <c>help.tallysolutions.com/docs/te9rel65/Banking/Cheque_Register.htm</c> for the <i>Out of Period</i>
/// bucket. <c>help.tallysolutions.com/deposit-slips/</c> — the Cash and the Cheque Deposit Slip, their header
/// fields, and the F5 switch between them.</para>
///
/// <para><b>Every test here fails on today's <c>main</c> by construction</b> — neither <c>ChequeRegister</c> nor
/// <c>DepositSlip</c> exists there, and neither does the <c>ChequeBook</c> the register buckets against.</para>
///
/// <para>🔴 <b>THE TEST THAT CARRIES THE ARGUMENT FOR THE WHOLE SCHEMA CHANGE IS
/// <see cref="A_spent_leaf_is_never_reported_available_even_when_the_operator_marked_it_blank"/>.</b> Available /
/// Blank / Cancelled are facts about paper you are holding and cannot be derived from any posting — that is why
/// row 8.5 needed storage — but the moment a leaf IS spent, the books outrank the operator's note, or the
/// register would report an issued cheque as still issuable, which is the one error here that costs money.</para>
/// </summary>
public class ChequeRegisterAndDepositSlipTests
{
    private static readonly PeriodRange Year =
        new(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));

    private static Company Seed(
        out Domain.Ledger hdfc, out Domain.Ledger axis, out Domain.Ledger acme, out Domain.Ledger cust,
        out VoucherType payment, out VoucherType receipt)
    {
        var c = CompanyFactory.CreateSeeded("Register Co", new DateOnly(2024, 4, 1));

        hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        {
            EnableChequePrinting = true,
            BankAccountNumber = "50100123456789",
            BankBranch = "MG Road",
            BankIfsc = "HDFC0000123",
        };
        c.AddLedger(hdfc);

        axis = new Domain.Ledger(Guid.NewGuid(), "Axis Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(200000m), openingIsDebit: true);
        c.AddLedger(axis);

        acme = new Domain.Ledger(Guid.NewGuid(), "Acme Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(acme);

        cust = new Domain.Ledger(Guid.NewGuid(), "Cust Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(cust);

        payment = c.FindVoucherTypeByName("Payment")!;
        receipt = c.FindVoucherTypeByName("Receipt")!;
        return c;
    }

    private static Voucher PayByCheque(
        Company c, VoucherType type, Domain.Ledger party, Domain.Ledger bank,
        decimal amount, string instrument, DateOnly date, DateOnly? bankDate = null)
        => new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type.Id, date, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit),
            new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.ChequeOrDD, instrument, date, bankDate)),
        }, partyId: party.Id));

    private static Voucher BankReceipt(
        Company c, VoucherType type, Domain.Ledger party, Domain.Ledger bank,
        decimal amount, BankTransactionType mode, string instrument, DateOnly date)
        => new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type.Id, date, new[]
        {
            new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Debit,
                bankAllocation: new BankAllocation(mode, instrument, date, null)),
            new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Credit),
        }, partyId: party.Id));

    // ================================================================ 8.5 Cheque Register

    /// <summary>
    /// The six buckets, on one ten-leaf book: an unreconciled issue, a reconciled issue, an out-of-period issue,
    /// an operator-cancelled leaf, an operator-blank leaf, and the untouched remainder as Available.
    /// </summary>
    [Fact]
    public void The_register_buckets_every_leaf_of_a_cheque_book()
    {
        var c = Seed(out var hdfc, out _, out var acme, out _, out var payment, out _);
        var book = new ChequeBook(Guid.NewGuid(), hdfc.Id, "HDFC book 1", "000101", "000110");
        c.AddChequeBook(book);

        PayByCheque(c, payment, acme, hdfc, 1000m, "000101", new DateOnly(2024, 6, 1));                    // unreconciled
        PayByCheque(c, payment, acme, hdfc, 2000m, "000102", new DateOnly(2024, 6, 2),
                    bankDate: new DateOnly(2024, 6, 9));                                                   // reconciled
        PayByCheque(c, payment, acme, hdfc, 3000m, "000103", new DateOnly(2025, 6, 1));                    // out of period
        c.SetChequeStatus(book.Id, "000104", ChequeStatus.Cancelled);
        c.SetChequeStatus(book.Id, "000105", ChequeStatus.Blank);

        var rows = ChequeRegister.Build(c, Year);
        Assert.Equal(10, rows.Count);   // every leaf of the range is reported, spent or not

        Assert.Equal(ChequeRegisterStatus.Unreconciled, rows.Single(r => r.ChequeNumber == "000101").Status);
        Assert.Equal(ChequeRegisterStatus.Reconciled, rows.Single(r => r.ChequeNumber == "000102").Status);
        Assert.Equal(ChequeRegisterStatus.OutOfPeriod, rows.Single(r => r.ChequeNumber == "000103").Status);
        Assert.Equal(ChequeRegisterStatus.Cancelled, rows.Single(r => r.ChequeNumber == "000104").Status);
        Assert.Equal(ChequeRegisterStatus.Blank, rows.Single(r => r.ChequeNumber == "000105").Status);
        Assert.Equal(5, rows.Count(r => r.Status == ChequeRegisterStatus.Available));

        // The issued leaves carry their voucher, which is the drill; the unissued ones carry none, so Enter on
        // them is a no-op rather than a call into the engine with an empty Guid.
        Assert.NotNull(rows.Single(r => r.ChequeNumber == "000101").VoucherId);
        Assert.Null(rows.Single(r => r.ChequeNumber == "000106").VoucherId);
        Assert.Equal("Acme Supplies", rows.Single(r => r.ChequeNumber == "000101").FavouringName);
        Assert.Equal(Money.FromRupees(2000m), rows.Single(r => r.ChequeNumber == "000102").Amount);
    }

    /// <summary>
    /// 🔴 A leaf that has actually been PAID OUT is never reported Available or Blank, whatever the operator
    /// ticked in the physical inventory. The books outrank the note: reporting a spent cheque as issuable is the
    /// one error in this report that lets somebody write the same leaf twice.
    /// </summary>
    [Fact]
    public void A_spent_leaf_is_never_reported_available_even_when_the_operator_marked_it_blank()
    {
        var c = Seed(out var hdfc, out _, out var acme, out _, out var payment, out _);
        var book = new ChequeBook(Guid.NewGuid(), hdfc.Id, "HDFC book 1", "000101", "000105");
        c.AddChequeBook(book);

        c.SetChequeStatus(book.Id, "000101", ChequeStatus.Blank);
        PayByCheque(c, payment, acme, hdfc, 1000m, "000101", new DateOnly(2024, 6, 1));

        var row = ChequeRegister.Build(c, Year).Single(r => r.ChequeNumber == "000101");
        Assert.Equal(ChequeRegisterStatus.Unreconciled, row.Status);
        Assert.NotEqual(ChequeRegisterStatus.Blank, row.Status);
        Assert.NotEqual(ChequeRegisterStatus.Available, row.Status);
    }

    /// <summary>
    /// A cheque whose number falls in no book is reported in its own <b>not in range</b> section rather than
    /// dropped — <c>help.tallysolutions.com/cheque-register/</c> names this case ("View Cheques Based on Range").
    /// A cheque the operator actually wrote that the register cannot see is worse than an untidy register.
    /// </summary>
    [Fact]
    public void A_cheque_outside_every_book_range_is_reported_not_silently_dropped()
    {
        var c = Seed(out var hdfc, out _, out var acme, out _, out var payment, out _);
        c.AddChequeBook(new ChequeBook(Guid.NewGuid(), hdfc.Id, "HDFC book 1", "000101", "000105"));
        PayByCheque(c, payment, acme, hdfc, 7500m, "999999", new DateOnly(2024, 6, 1));

        var rows = ChequeRegister.Build(c, Year);
        var stray = rows.Single(r => r.ChequeNumber == "999999");
        Assert.Equal(Guid.Empty, stray.ChequeBookId);
        Assert.Equal(ChequeRegister.NotInRangeCaption, stray.ChequeBookName);
        Assert.Equal(ChequeRegisterStatus.Unreconciled, stray.Status);
        Assert.Equal(Money.FromRupees(7500m), stray.Amount);
    }

    /// <summary>
    /// 🔴 The register counts a spent leaf on a bank whose <c>Enable Cheque Printing</c> is OFF. That flag scopes
    /// the Cheque PRINTING report, where it is right; here it would report a cheque you actually wrote as still
    /// available, because you never asked this product to ink it.
    /// </summary>
    [Fact]
    public void A_bank_that_does_not_print_cheques_still_has_its_leaves_counted_as_spent()
    {
        var c = Seed(out _, out var axis, out var acme, out _, out var payment, out _);
        Assert.False(axis.EnableChequePrinting);   // the precondition this test turns on
        var book = new ChequeBook(Guid.NewGuid(), axis.Id, "Axis book 1", "000201", "000205");
        c.AddChequeBook(book);
        PayByCheque(c, payment, acme, axis, 1200m, "000202", new DateOnly(2024, 6, 3));

        var rows = ChequeRegister.Build(c, Year);
        Assert.Equal(ChequeRegisterStatus.Unreconciled, rows.Single(r => r.ChequeNumber == "000202").Status);
    }

    /// <summary>The per-bank scope ("View Cheques with Status for Specific Banks") and the summary counts.</summary>
    [Fact]
    public void The_summary_counts_each_book_and_the_bank_scope_narrows_it()
    {
        var c = Seed(out var hdfc, out var axis, out var acme, out _, out var payment, out _);
        var h = new ChequeBook(Guid.NewGuid(), hdfc.Id, "HDFC book 1", "000101", "000105");
        var a = new ChequeBook(Guid.NewGuid(), axis.Id, "Axis book 1", "000201", "000203");
        c.AddChequeBook(h);
        c.AddChequeBook(a);
        PayByCheque(c, payment, acme, hdfc, 1000m, "000101", new DateOnly(2024, 6, 1));
        c.SetChequeStatus(h.Id, "000102", ChequeStatus.Cancelled);

        var all = ChequeRegister.Summary(c, Year);
        Assert.Equal(2, all.Count);
        var hdfcRow = all.Single(r => r.ChequeBookId == h.Id);
        Assert.Equal(5, hdfcRow.Total);
        Assert.Equal(3, hdfcRow.Available);
        Assert.Equal(1, hdfcRow.Unreconciled);
        Assert.Equal(1, hdfcRow.Cancelled);
        Assert.Equal(0, hdfcRow.Reconciled);

        var narrowed = ChequeRegister.Summary(c, Year, axis.Id);
        Assert.Equal(a.Id, Assert.Single(narrowed).ChequeBookId);
    }

    /// <summary>
    /// The status filter (<b>F8</b>, "View Cheques with Specific Statuses") narrows the leaf list, and the
    /// counting is unaffected by it — a filtered view is a view, not a different register.
    /// </summary>
    [Fact]
    public void The_status_filter_narrows_the_leaf_list()
    {
        var c = Seed(out var hdfc, out _, out var acme, out _, out var payment, out _);
        var book = new ChequeBook(Guid.NewGuid(), hdfc.Id, "HDFC book 1", "000101", "000105");
        c.AddChequeBook(book);
        PayByCheque(c, payment, acme, hdfc, 1000m, "000101", new DateOnly(2024, 6, 1));

        var onlyAvailable = ChequeRegister.Build(
            c, Year, null, new[] { ChequeRegisterStatus.Available });
        Assert.Equal(4, onlyAvailable.Count);
        Assert.All(onlyAvailable, r => Assert.Equal(ChequeRegisterStatus.Available, r.Status));
        Assert.DoesNotContain(onlyAvailable, r => r.ChequeNumber == "000101");
    }

    /// <summary>
    /// Leading zeros are part of the leaf number, and the range test is NUMERIC where it can be — so a book
    /// declared 000101–000110 contains the cheque keyed "000105", and the derived count is the vendor's
    /// auto-calculated "Number of Cheques".
    /// </summary>
    [Fact]
    public void A_cheque_book_range_is_numeric_and_keeps_its_leading_zeros()
    {
        var book = new ChequeBook(Guid.NewGuid(), Guid.NewGuid(), "Book", "000101", "000110");
        Assert.Equal(10, book.Count);
        Assert.True(book.Contains("000105"));
        Assert.True(book.Contains("101"));      // same NUMBER, keyed without the padding
        Assert.False(book.Contains("000111"));
        Assert.Equal("000101", book.LeafNumbers().First());
        Assert.Equal("000110", book.LeafNumbers().Last());

        // A lettered series is not countable, and the honest answer is 0 rather than a guess — a fabricated leaf
        // total in front of an operator doing a physical stock-take of their cheques is worse than none.
        var lettered = new ChequeBook(Guid.NewGuid(), Guid.NewGuid(), "Book", "AA01", "AA10");
        Assert.Equal(0, lettered.Count);
        Assert.Empty(lettered.LeafNumbers());
        Assert.True(lettered.Contains("AA05"));   // …but the ordinal range test still works
    }

    // ================================================================ 8.6 Deposit Slip

    /// <summary>
    /// The Cheque Deposit Slip lists the instruments banked, with the bank's own header block, and the Cash slip
    /// lists the cash — the vendor's two modes (F5), which is why this is a mode and not a filter.
    /// </summary>
    [Fact]
    public void The_deposit_slip_separates_the_cash_and_the_cheque_modes()
    {
        var c = Seed(out var hdfc, out _, out _, out var cust, out _, out var receipt);
        BankReceipt(c, receipt, cust, hdfc, 5000m, BankTransactionType.ChequeOrDD, "778899", new DateOnly(2024, 7, 1));
        BankReceipt(c, receipt, cust, hdfc, 1500m, BankTransactionType.Cash, "", new DateOnly(2024, 7, 2));
        BankReceipt(c, receipt, cust, hdfc, 9000m, BankTransactionType.NEFT, "N-1", new DateOnly(2024, 7, 3));

        var cheque = DepositSlip.Build(c, hdfc, Year, DepositSlipKind.Cheque);
        var line = Assert.Single(cheque.Lines);
        Assert.Equal("778899", line.InstrumentNumber);
        Assert.Equal("Cust Ltd", line.ReceivedFrom);
        Assert.Equal(Money.FromRupees(5000m), cheque.Total);

        var cash = DepositSlip.Build(c, hdfc, Year, DepositSlipKind.Cash);
        Assert.Equal(Money.FromRupees(1500m), cash.Total);
        // The cash slip carries no instrument number, because cash has none — printing a blank "Cheque No."
        // column on a cash slip is the caption-over-nothing defect.
        Assert.Equal(string.Empty, Assert.Single(cash.Lines).InstrumentNumber);

        // The NEFT receipt is on neither slip: a transfer is not something you carry to a teller.
        Assert.DoesNotContain(cheque.Lines, l => l.Amount == Money.FromRupees(9000m));
        Assert.DoesNotContain(cash.Lines, l => l.Amount == Money.FromRupees(9000m));
    }

    /// <summary>
    /// 🔴 The slip is RECEIPT-side only. A payment out of the same bank account by cheque must never appear on a
    /// pay-in slip — the mirror of the Cheque Printing report's credit-only rule, and the difference between a
    /// document a teller accepts and one they hand back.
    /// </summary>
    [Fact]
    public void A_payment_out_of_the_bank_never_appears_on_a_pay_in_slip()
    {
        var c = Seed(out var hdfc, out _, out var acme, out var cust, out var payment, out var receipt);
        PayByCheque(c, payment, acme, hdfc, 4000m, "000101", new DateOnly(2024, 7, 1));
        BankReceipt(c, receipt, cust, hdfc, 5000m, BankTransactionType.ChequeOrDD, "778899", new DateOnly(2024, 7, 1));

        var slip = DepositSlip.Build(c, hdfc, Year, DepositSlipKind.Cheque);
        Assert.Equal("778899", Assert.Single(slip.Lines).InstrumentNumber);
        Assert.Equal(Money.FromRupees(5000m), slip.Total);
    }

    /// <summary>
    /// The four header fields the vendor prints: <i>Account Number · Account Holder Name · Bank Name · Branch
    /// Name</i>. Three come from the v57 <c>ledgers</c> columns; the account holder is the COMPANY's mailing
    /// name, deliberately not a fourth stored field that could contradict the company master.
    ///
    /// <para>And when the bank identity was never captured, the fields are BLANK rather than fabricated — the
    /// report layer suppresses the caption instead of printing "A/c No.:" over nothing.</para>
    /// </summary>
    [Fact]
    public void The_slip_header_comes_from_the_bank_ledger_and_the_company_and_is_blank_when_uncaptured()
    {
        var c = Seed(out var hdfc, out var axis, out _, out var cust, out _, out var receipt);
        BankReceipt(c, receipt, cust, hdfc, 5000m, BankTransactionType.ChequeOrDD, "778899", new DateOnly(2024, 7, 1));

        var slip = DepositSlip.Build(c, hdfc, Year, DepositSlipKind.Cheque);
        Assert.Equal("50100123456789", slip.AccountNumber);
        Assert.Equal("MG Road", slip.BranchName);
        Assert.Equal("HDFC Bank", slip.BankName);
        Assert.Equal(c.MailingName, slip.AccountHolderName);

        var uncaptured = DepositSlip.Build(c, axis, Year, DepositSlipKind.Cheque);
        Assert.Equal(string.Empty, uncaptured.AccountNumber);
        Assert.Equal(string.Empty, uncaptured.BranchName);
        Assert.Equal("Axis Bank", uncaptured.BankName);   // the ledger's own name when no stationery name is set
    }

    /// <summary>The cheque-stationery name wins over the ledger name on the slip, because that is the name the
    /// bank knows the account by ("Name of Bank", <c>help.tallysolutions.com/cheque-payments-set-up/</c>).</summary>
    [Fact]
    public void The_slip_prefers_the_cheque_stationery_bank_name()
    {
        var c = Seed(out var hdfc, out _, out _, out var cust, out _, out var receipt);
        hdfc.ChequePrintingBankName = "HDFC Bank Ltd., MG Road";
        BankReceipt(c, receipt, cust, hdfc, 5000m, BankTransactionType.ChequeOrDD, "778899", new DateOnly(2024, 7, 1));

        Assert.Equal("HDFC Bank Ltd., MG Road",
            DepositSlip.Build(c, hdfc, Year, DepositSlipKind.Cheque).BankName);
    }
}
