using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>8.10 — e-Payments / the bank payment-instruction file</b>
/// (<c>help.tallysolutions.com/e-payments-report/</c>, <c>.../export-upload-e-payments/</c>).
///
/// <para>The report's job is to answer one question per payment — <i>can this go to the bank, and if not, what is
/// missing and on whose master</i> — and the file's job is to carry only the answers that were yes. Both halves
/// are pinned here, including the one that matters most: a payment with no beneficiary account number must never
/// reach the file.</para>
/// </summary>
public class EPaymentsTests
{
    private static readonly PeriodRange Year =
        new(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));

    private static Company Seed(out Domain.Ledger hdfc, out Domain.Ledger acme, out VoucherType payment)
    {
        var c = CompanyFactory.CreateSeeded("EPay Co", new DateOnly(2024, 4, 1));

        hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(1000000m), openingIsDebit: true)
        {
            BankAccountNumber = "50200012345678",
            BankIfsc = "HDFC0000123",
            BankBranch = "MG Road",
        };
        c.AddLedger(hdfc);

        acme = new Domain.Ledger(Guid.NewGuid(), "Acme Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        {
            BankAccountNumber = "9876543210",
            BankIfsc = "ICIC0000456",
        };
        c.AddLedger(acme);

        payment = c.FindVoucherTypeByName("Payment")!;
        return c;
    }

    private static Voucher Pay(
        Company c, VoucherType type, Domain.Ledger party, Domain.Ledger bank, decimal amount,
        BankTransactionType mode, string reference = "REF1", int day = 15)
        => new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), type.Id, new DateOnly(2024, 5, day),
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                    bankAllocation: new BankAllocation(mode, reference)),
            },
            partyId: party.Id));

    // ------------------------------------------------------------------ the buckets

    /// <summary>A payment with both masters complete is "Ready for Sending to Bank" and carries both sides'
    /// account numbers, which is what an instruction file needs.</summary>
    [Fact]
    public void A_complete_neft_payment_is_ready_for_sending_to_bank()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        Pay(c, payment, acme, hdfc, 125000m, BankTransactionType.NEFT, "NEFT-77");

        var report = EPayments.Build(c, hdfc, Year);
        var row = Assert.Single(report.ReadyForSendingToBank);

        Assert.Equal(EPaymentStatus.ReadyForSendingToBank, row.Status);
        Assert.Equal(string.Empty, row.Reason);
        Assert.Equal("Acme Supplies", row.PayeeName);
        Assert.Equal("9876543210", row.PayeeAccountNumber);
        Assert.Equal("ICIC0000456", row.PayeeIfsc);
        Assert.Equal("50200012345678", row.BankAccountNumber);
        Assert.Equal(BankTransactionType.NEFT, row.TransactionType);
        Assert.Equal(Money.FromRupees(125000m), row.Amount);
        Assert.Equal(Money.FromRupees(125000m), report.ReadyTotal);
    }

    /// <summary>
    /// A beneficiary with no account number lands in "Incomplete/Incorrect Transaction Details" — the vendor's
    /// bucket for <i>"Missing or incorrect party's bank details such as Account No. or IFS Code"</i> — and the
    /// reason names the party and the field, so the operator knows which master to open.
    /// </summary>
    [Fact]
    public void A_beneficiary_without_an_account_number_is_an_incomplete_transaction()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        acme.BankAccountNumber = null;
        Pay(c, payment, acme, hdfc, 50000m, BankTransactionType.RTGS);

        var report = EPayments.Build(c, hdfc, Year);
        Assert.Empty(report.ReadyForSendingToBank);
        var row = Assert.Single(report.IncompleteTransactionDetails);

        Assert.Contains("Acme Supplies", row.Reason);
        Assert.Contains("account number", row.Reason);
        Assert.Equal(Money.Zero, report.ReadyTotal);
    }

    /// <summary>
    /// An incomplete BANK ledger wins over an incomplete payee, because one bad bank master blocks every payment
    /// drawn on it — telling the operator about the payee first would send them to the wrong screen once per row.
    /// </summary>
    [Fact]
    public void An_incomplete_bank_master_is_reported_before_an_incomplete_payee()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        hdfc.BankIfsc = null;
        acme.BankAccountNumber = null;                  // both sides broken
        Pay(c, payment, acme, hdfc, 50000m, BankTransactionType.NEFT);

        var report = EPayments.Build(c, hdfc, Year);
        var row = Assert.Single(report.IncompleteBankLedgerMaster);
        Assert.Empty(report.IncompleteTransactionDetails);
        Assert.Contains("HDFC Bank", row.Reason);
        Assert.Contains("IFS code", row.Reason);
    }

    /// <summary>An entry that splits across several ledgers has no ONE beneficiary; the report says so instead of
    /// picking one of them and paying it.</summary>
    [Fact]
    public void A_payment_with_no_single_beneficiary_is_not_ready()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        var other = new Domain.Ledger(Guid.NewGuid(), "Beta Traders",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(other);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 20),
            new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(30000m), DrCr.Debit),
                new EntryLine(other.Id, Money.FromRupees(20000m), DrCr.Debit),
                new EntryLine(hdfc.Id, Money.FromRupees(50000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "SPLIT")),
            }));

        var report = EPayments.Build(c, hdfc, Year);
        var row = Assert.Single(report.IncompleteTransactionDetails);
        Assert.Contains("no single beneficiary", row.Reason);
    }

    // ------------------------------------------------------------------ what is and is not an e-payment

    /// <summary>
    /// Only NEFT and RTGS. A cheque is row 8.4's document, a cash withdrawal is not a transfer at all, and
    /// <c>Other</c> is excluded on purpose — its own definition conflates IMPS/UPI with a book adjustment, so
    /// including it would put adjustments in front of a bank.
    /// </summary>
    [Fact]
    public void Only_neft_and_rtgs_are_e_payments()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        Pay(c, payment, acme, hdfc, 1000m, BankTransactionType.NEFT, "A", day: 1);
        Pay(c, payment, acme, hdfc, 2000m, BankTransactionType.RTGS, "B", day: 2);
        Pay(c, payment, acme, hdfc, 4000m, BankTransactionType.ChequeOrDD, "C", day: 3);
        Pay(c, payment, acme, hdfc, 8000m, BankTransactionType.Cash, "D", day: 4);
        Pay(c, payment, acme, hdfc, 16000m, BankTransactionType.Other, "E", day: 5);

        var report = EPayments.Build(c, hdfc, Year);
        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(Money.FromRupees(3000m), report.ReadyTotal);
    }

    /// <summary>Money coming IN is somebody else's instruction file. Only credits to the bank are collected.</summary>
    [Fact]
    public void A_receipt_into_the_bank_is_not_an_e_payment()
    {
        var c = Seed(out var hdfc, out var acme, out _);
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), receipt.Id, new DateOnly(2024, 5, 12),
            new[]
            {
                new EntryLine(hdfc.Id, Money.FromRupees(90000m), DrCr.Debit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "IN-1")),
                new EntryLine(acme.Id, Money.FromRupees(90000m), DrCr.Credit),
            },
            partyId: acme.Id));

        Assert.Empty(EPayments.Build(c, hdfc, Year).Rows);
    }

    /// <summary>
    /// 🔴 An <b>Optional</b> payment — an auto-created entry (census 8.13) nobody has reviewed — must not reach
    /// the e-Payments report, because a row there is one keystroke from a real bank instruction. The exclusion
    /// comes from the shared <c>CountsAsOf</c> predicate, so the two rows in this track are consistent by
    /// construction rather than by coincidence.
    /// </summary>
    [Fact]
    public void An_unreviewed_optional_payment_never_reaches_the_e_payments_report()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 15),
            new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(70000m), DrCr.Debit),
                new EntryLine(hdfc.Id, Money.FromRupees(70000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "PROV")),
            },
            partyId: acme.Id,
            optional: true));

        Assert.Empty(EPayments.Build(c, hdfc, Year).Rows);
    }

    /// <summary>With no bank picked the report spans every bank account, which is the vendor's default view.</summary>
    [Fact]
    public void All_banks_is_the_unscoped_view()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        var sbi = new Domain.Ledger(Guid.NewGuid(), "SBI", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(200000m), openingIsDebit: true)
        { BankAccountNumber = "111222333", BankIfsc = "SBIN0000999" };
        c.AddLedger(sbi);

        Pay(c, payment, acme, hdfc, 1000m, BankTransactionType.NEFT, "H", day: 3);
        Pay(c, payment, acme, sbi, 2000m, BankTransactionType.NEFT, "S", day: 4);

        Assert.Equal(2, EPayments.Build(c, bankLedger: null, Year).Rows.Count);
        Assert.Single(EPayments.Build(c, hdfc, Year).Rows);
        Assert.Single(EPayments.Build(c, sbi, Year).Rows);
    }

    // ------------------------------------------------------------------ the instruction file

    /// <summary>
    /// 🔴 <b>THE FILE CARRIES ONLY WHAT IS READY.</b> A payment whose beneficiary has no account number is on the
    /// report as an exception and is NOT in the file — a file that quietly carried it would either be rejected at
    /// the bank or, worse, processed against a blank field.
    /// </summary>
    [Fact]
    public void The_instruction_file_carries_the_ready_rows_and_only_those()
    {
        var c = Seed(out var hdfc, out var acme, out var payment);
        var broke = new Domain.Ledger(Guid.NewGuid(), "No Details Ltd",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(broke);

        Pay(c, payment, acme, hdfc, 125000m, BankTransactionType.NEFT, "NEFT-77", day: 10);
        Pay(c, payment, broke, hdfc, 999m, BankTransactionType.NEFT, "NEFT-78", day: 11);

        var report = EPayments.Build(c, hdfc, Year);
        var file = EPayments.BuildPaymentInstructionFile(c, report);

        Assert.Contains("Acme Supplies", file);
        Assert.Contains("9876543210", file);
        Assert.Contains("ICIC0000456", file);
        Assert.Contains("125000.00", file);
        Assert.Contains("2024-05-10", file);            // ISO, invariant — the gate also runs on ubuntu/macos
        Assert.Contains("NEFT", file);

        Assert.DoesNotContain("No Details Ltd", file);
        Assert.DoesNotContain("999.00", file);

        // Header row present, one data row, and the layout names itself as ours rather than as a bank's.
        Assert.Contains("Beneficiary Name,Beneficiary Account Number,Beneficiary IFSC", file);
        Assert.Contains("not a bank-specific format", file);
        var dataLines = file.Split('\n')
            .Where(l => l.Length > 0 && l.StartsWith('"'))
            .ToList();
        Assert.Single(dataLines);
    }

    /// <summary>
    /// 🔴 A ledger named as a spreadsheet formula cannot execute when the clerk opens the file before uploading
    /// it. The value stays legible; only its leading character is neutralised.
    /// </summary>
    [Fact]
    public void A_ledger_name_that_looks_like_a_formula_is_neutralised_in_the_file()
    {
        var c = Seed(out var hdfc, out _, out var payment);
        var evil = new Domain.Ledger(Guid.NewGuid(), "=cmd|'/c calc'!A1",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false)
        { BankAccountNumber = "1", BankIfsc = "X" };
        c.AddLedger(evil);

        Pay(c, payment, evil, hdfc, 100m, BankTransactionType.NEFT, "F");

        var file = EPayments.BuildPaymentInstructionFile(c, EPayments.Build(c, hdfc, Year));
        Assert.Contains("\"'=cmd|'/c calc'!A1\"", file);
        Assert.DoesNotContain("\"=cmd", file);
    }

    /// <summary>
    /// The guard skips leading SPACES before deciding, exactly as <c>DelimitedText.Quote</c> does. Testing
    /// <c>field[0]</c> alone would let a single leading space carry a live formula straight through — and the two
    /// guards in this product must not disagree about what is dangerous.
    /// </summary>
    [Fact]
    public void The_formula_guard_looks_past_leading_spaces_like_the_shared_one_does()
    {
        Assert.Equal("\"'  =SUM(A1:A9)\"", EPayments.Csv("  =SUM(A1:A9)"));
        Assert.Equal("\"'+44 20 7946\"", EPayments.Csv("+44 20 7946"));
        Assert.Equal("\"Acme Supplies\"", EPayments.Csv("Acme Supplies"));
        Assert.Equal("\"\"", EPayments.Csv(null));
    }

    /// <summary>A quote or a comma in a ledger name cannot shift a column — the beneficiary of the next row must
    /// not become the account number of this one.</summary>
    [Fact]
    public void A_comma_or_a_quote_in_a_name_cannot_shift_a_column()
    {
        var c = Seed(out var hdfc, out _, out var payment);
        var awkward = new Domain.Ledger(Guid.NewGuid(), "Smith, \"Bob\" & Co",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false)
        { BankAccountNumber = "42", BankIfsc = "Y" };
        c.AddLedger(awkward);

        Pay(c, payment, awkward, hdfc, 100m, BankTransactionType.NEFT, "G");

        var file = EPayments.BuildPaymentInstructionFile(c, EPayments.Build(c, hdfc, Year));
        Assert.Contains("\"Smith, \"\"Bob\"\" & Co\"", file);
    }

    /// <summary>An empty report yields a header-only file rather than a throw — the caller decides whether to
    /// write it, and the page refuses to.</summary>
    [Fact]
    public void An_empty_report_yields_a_header_only_file()
    {
        var c = Seed(out var hdfc, out _, out _);
        var file = EPayments.BuildPaymentInstructionFile(c, EPayments.Build(c, hdfc, Year));
        Assert.DoesNotContain("\"", file.Split('\n').Last());
        Assert.Contains("Beneficiary Name", file);
    }
}
