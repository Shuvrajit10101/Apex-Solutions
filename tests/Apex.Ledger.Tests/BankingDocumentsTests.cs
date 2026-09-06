using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// The two pure projections behind the wave-7 banking documents (catalog §8):
/// <b>Cheque Printing</b> (census row 8.4) and the supplier <b>Payment Advice</b> (census row 8.7).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/print-cheques/</c>, section "Cheque Printing
/// Report" — the cheques <i>pending for printing</i>, showing the favouring name with the instrument number and
/// date, widened by F8 "Include Printed". <c>help.tallysolutions.com/payment-advice/</c> — "all payments made to
/// suppliers", each showing whether it is "matched (reconciled) or not", from which a letter carrying
/// "Invoice numbers, Amounts paid, Deductions (if any), TDS details, Payment mode (NEFT, RTGS, cheque, etc.)"
/// is printed.</para>
///
/// <para><b>Every test here fails on <c>main</c> by construction</b> — neither <c>ChequePrinting</c> nor
/// <c>SupplierPaymentAdvice</c> exists there.</para>
/// </summary>
public class BankingDocumentsTests
{
    private static readonly PeriodRange Year =
        new(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));

    /// <summary>
    /// A company with two banks — "HDFC Bank" with cheque printing ON and "Axis Bank" with it OFF — plus a
    /// supplier "Acme", a second supplier "Beta", a customer "Cust" (a Sundry DEBTOR, to prove the advice is a
    /// creditors-only document) and a Rent expense ledger.
    /// </summary>
    private static Company Seed(
        out Domain.Ledger hdfc,
        out Domain.Ledger axis,
        out Domain.Ledger rent,
        out Domain.Ledger acme,
        out Domain.Ledger cust,
        out VoucherType payment,
        out VoucherType receipt)
    {
        var c = CompanyFactory.CreateSeeded("Cheque Co", new DateOnly(2024, 4, 1));

        hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        { EnableChequePrinting = true };
        c.AddLedger(hdfc);

        // Cheque printing OFF — the vendor's report is scoped to banks that print cheques, so this bank's
        // cheque-bearing payments must NOT appear.
        axis = new Domain.Ledger(Guid.NewGuid(), "Axis Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(200000m), openingIsDebit: true);
        c.AddLedger(axis);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        // Bill-by-bill on, because the advice's whole point is the bill-wise invoice numbers the vendor's
        // configuration list names, and a ledger that does not maintain them cannot carry an allocation.
        acme = new Domain.Ledger(Guid.NewGuid(), "Acme Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        { MaintainBillByBill = true };
        c.AddLedger(acme);

        cust = new Domain.Ledger(Guid.NewGuid(), "Cust Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(cust);

        payment = c.FindVoucherTypeByName("Payment")!;
        receipt = c.FindVoucherTypeByName("Receipt")!;
        return c;
    }

    private static Voucher PostChequePayment(
        Company c, VoucherType type, Domain.Ledger debit, Domain.Ledger bank,
        decimal amount, string instrument, DateOnly date,
        BankTransactionType mode = BankTransactionType.ChequeOrDD,
        DateOnly? bankDate = null,
        Guid? partyId = null)
    {
        var svc = new LedgerService(c);
        return svc.Post(new Voucher(Guid.NewGuid(), type.Id, date, new[]
        {
            new EntryLine(debit.Id, Money.FromRupees(amount), DrCr.Debit),
            new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                bankAllocation: new BankAllocation(mode, instrument, date, bankDate)),
        }, partyId: partyId));
    }

    // ================================================================ 8.4 Cheque Printing

    /// <summary>
    /// The report is the cheques <b>drawn on a cheque-printing bank, by cheque, carrying a number</b> — and
    /// nothing else. Each exclusion here is one of the four conditions, and each has its own reason: an RTGS has
    /// no leaf, a receipt is somebody else's cheque, a numberless payment cannot be matched to a pre-numbered
    /// leaf, and a bank that does not print cheques is not in the vendor's List of Banks.
    /// </summary>
    [Fact]
    public void Cheque_printing_lists_only_numbered_cheques_drawn_on_a_cheque_printing_bank()
    {
        var c = Seed(out var hdfc, out var axis, out var rent, out var acme, out _, out var payment, out var receipt);

        PostChequePayment(c, payment, rent, hdfc, 20000m, "100123", new DateOnly(2024, 4, 10));   // ✔ listed
        PostChequePayment(c, payment, rent, hdfc, 5000m, "100124", new DateOnly(2024, 4, 12),
            mode: BankTransactionType.RTGS);                                                      // ✗ not a cheque
        PostChequePayment(c, payment, rent, hdfc, 6000m, "", new DateOnly(2024, 4, 13));          // ✗ no number
        PostChequePayment(c, payment, rent, axis, 7000m, "900001", new DateOnly(2024, 4, 14));    // ✗ bank off

        // A RECEIPT: the bank is DEBITED. That is a cheque somebody wrote to us, and it has no leaf of ours.
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 15), new[]
        {
            new EntryLine(hdfc.Id, Money.FromRupees(9000m), DrCr.Debit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.ChequeOrDD, "555555", new DateOnly(2024, 4, 15))),
            new EntryLine(acme.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var rows = ChequePrinting.Build(c, Year);

        var only = Assert.Single(rows);
        Assert.Equal("100123", only.InstrumentNumber);
        Assert.Equal("HDFC Bank", only.BankName);
        Assert.Equal(Money.FromRupees(20000m), only.Amount);
        Assert.Equal(new DateOnly(2024, 4, 10), only.InstrumentDate);
        Assert.False(only.Printed);
    }

    /// <summary>
    /// 🔴 <b>A cheque you have not actually issued must never queue for printing.</b> A <b>Memorandum</b> is a
    /// non-affecting suspense entry and an <b>Optional</b> voucher is not in the books either — neither is a
    /// payment that has happened, so neither has a leaf waiting in the printer. The first cut of the projection
    /// called <c>CountsAsOf(v, asOf)</c> without the base type, which excludes Cancelled and Optional but lets
    /// Memorandum and Reversing Journal straight through.
    /// </summary>
    [Fact]
    public void A_memorandum_or_optional_voucher_never_queues_a_cheque_for_printing()
    {
        var c = Seed(out var hdfc, out _, out var rent, out _, out _, out var payment, out _);

        PostChequePayment(c, payment, rent, hdfc, 20000m, "100123", new DateOnly(2024, 4, 10));   // ✔ listed

        // A memorandum drawing the very next leaf: recorded, but not a payment.
        var memo = c.FindVoucherTypeByName("Memorandum")!;
        PostChequePayment(c, memo, rent, hdfc, 30000m, "100124", new DateOnly(2024, 4, 11));

        // An OPTIONAL payment (Ctrl+L) drawing the leaf after that: keyed, but not posted to the books.
        var provisional = PostChequePayment(c, payment, rent, hdfc, 40000m, "100125", new DateOnly(2024, 4, 12));
        provisional.Optional = true;

        var rows = ChequePrinting.Build(c, Year);

        var only = Assert.Single(rows);
        Assert.Equal("100123", only.InstrumentNumber);
        Assert.DoesNotContain(rows, r => r.InstrumentNumber == "100124");
        Assert.DoesNotContain(rows, r => r.InstrumentNumber == "100125");
    }

    /// <summary>F8 "Include Printed" (<c>help.tallysolutions.com/print-cheques/</c>): the default list is the
    /// cheques PENDING for printing; F8 widens it to the ones already printed.</summary>
    [Fact]
    public void Include_printed_is_the_only_thing_that_brings_a_printed_cheque_back_onto_the_list()
    {
        var c = Seed(out var hdfc, out _, out var rent, out _, out _, out var payment, out _);
        PostChequePayment(c, payment, rent, hdfc, 20000m, "100123", new DateOnly(2024, 4, 10));
        PostChequePayment(c, payment, rent, hdfc, 30000m, "100124", new DateOnly(2024, 4, 11));

        bool IsPrinted(Guid bankId, string number) => number == "100123";

        var pending = ChequePrinting.Build(c, Year, isPrinted: IsPrinted);
        Assert.Equal(new[] { "100124" }, pending.Select(r => r.InstrumentNumber));

        var all = ChequePrinting.Build(c, Year, includePrinted: true, isPrinted: IsPrinted);
        Assert.Equal(new[] { "100123", "100124" }, all.Select(r => r.InstrumentNumber));
        Assert.True(all[0].Printed);
        Assert.False(all[1].Printed);
    }

    /// <summary>The favouring name is the party the voucher records; a cheque paying an expense direct still has
    /// to name someone, so it falls back to the non-bank ledger on the voucher.</summary>
    [Fact]
    public void Favouring_name_is_the_party_where_there_is_one_and_the_paid_ledger_otherwise()
    {
        var c = Seed(out var hdfc, out _, out var rent, out var acme, out _, out var payment, out _);

        PostChequePayment(c, payment, acme, hdfc, 11000m, "100200", new DateOnly(2024, 4, 10),
            partyId: acme.Id);
        PostChequePayment(c, payment, rent, hdfc, 12000m, "100201", new DateOnly(2024, 4, 11));

        var rows = ChequePrinting.Build(c, Year);
        Assert.Equal(new[] { "Acme Supplies", "Rent" }, rows.Select(r => r.FavouringName));
    }

    /// <summary>The bank filter is the vendor's "List of Banks" scoping.</summary>
    [Fact]
    public void The_bank_filter_narrows_the_list_to_one_bank()
    {
        var c = Seed(out var hdfc, out var axis, out var rent, out _, out _, out var payment, out _);
        axis.EnableChequePrinting = true;

        PostChequePayment(c, payment, rent, hdfc, 1000m, "100300", new DateOnly(2024, 4, 10));
        PostChequePayment(c, payment, rent, axis, 2000m, "900300", new DateOnly(2024, 4, 11));

        Assert.Equal(2, ChequePrinting.Build(c, Year).Count);
        var justAxis = ChequePrinting.Build(c, Year, bankLedgerId: axis.Id);
        Assert.Equal("900300", Assert.Single(justAxis).InstrumentNumber);
    }

    // ================================================================ 8.7 supplier Payment Advice

    /// <summary>
    /// One advice per (supplier, voucher), carrying the bill-wise invoice numbers and amounts the vendor's own
    /// configuration list asks the letter for, and the payment mode read off the bank allocation.
    /// </summary>
    [Fact]
    public void An_advice_carries_the_bill_wise_detail_and_the_payment_mode_from_the_bank_allocation()
    {
        var c = Seed(out var hdfc, out _, out _, out var acme, out _, out var payment, out _);
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 6), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(30000m), DrCr.Debit, billAllocations: new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-001", Money.FromRupees(18000m)),
                new BillAllocation(BillRefType.AgstRef, "INV-002", Money.FromRupees(12000m)),
            }),
            new EntryLine(hdfc.Id, Money.FromRupees(30000m), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.NEFT, "UTR77", new DateOnly(2024, 5, 6))),
        }, partyId: acme.Id));

        var advice = Assert.Single(SupplierPaymentAdvice.Build(c, Year));
        Assert.Equal("Acme Supplies", advice.PartyName);
        Assert.Equal(Money.FromRupees(30000m), advice.GrossAmount);
        Assert.Equal(Money.FromRupees(30000m), advice.NetPaid);
        Assert.Equal(BankTransactionType.NEFT, advice.PaymentMode);
        Assert.Equal("NEFT", SupplierPaymentAdvice.PaymentModeText(advice.PaymentMode));
        Assert.Equal("UTR77", advice.InstrumentNumber);
        Assert.Equal("HDFC Bank", advice.BankName);
        Assert.Equal(new[] { "INV-001", "INV-002" }, advice.Bills.Select(b => b.BillReference));
        Assert.Equal(
            new[] { Money.FromRupees(18000m), Money.FromRupees(12000m) },
            advice.Bills.Select(b => b.Amount));
    }

    /// <summary>
    /// The vendor's reconciled-only filter (F8): an advice is "matched" once the bank statement has cleared it,
    /// which is exactly <c>BankAllocation.BankDate</c> being set — nothing is stored to say so.
    /// </summary>
    [Fact]
    public void Reconciled_only_excludes_the_payment_whose_bank_date_is_not_yet_set()
    {
        var c = Seed(out var hdfc, out _, out _, out var acme, out _, out var payment, out _);

        PostChequePayment(c, payment, acme, hdfc, 4000m, "100400", new DateOnly(2024, 5, 1),
            partyId: acme.Id);
        PostChequePayment(c, payment, acme, hdfc, 5000m, "100401", new DateOnly(2024, 5, 2),
            bankDate: new DateOnly(2024, 5, 4), partyId: acme.Id);

        var all = SupplierPaymentAdvice.Build(c, Year);
        Assert.Equal(2, all.Count);
        Assert.False(all[0].IsReconciled);
        Assert.True(all[1].IsReconciled);

        var matched = SupplierPaymentAdvice.Build(c, Year, reconciledOnly: true);
        Assert.Equal("100401", Assert.Single(matched).InstrumentNumber);
    }

    /// <summary>A payment to a CUSTOMER is not a supplier advice. Sundry Debtors are excluded by the ancestry
    /// walk, not by the direct parent, so a sub-group under Sundry Creditors still counts.</summary>
    [Fact]
    public void Only_sundry_creditors_produce_an_advice_and_a_creditor_sub_group_still_counts()
    {
        var c = Seed(out var hdfc, out _, out _, out _, out var cust, out var payment, out _);

        var subGroup = new Group(Guid.NewGuid(), "Local Suppliers",
            GroupNature.Liability, c.FindGroupByName("Sundry Creditors")!.Id);
        c.AddGroup(subGroup);
        var local = new Domain.Ledger(Guid.NewGuid(), "Local Traders", subGroup.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(local);

        // A refund paid back to a customer — a debit to a Sundry DEBTOR. Not an advice.
        PostChequePayment(c, payment, cust, hdfc, 3000m, "100500", new DateOnly(2024, 5, 1),
            partyId: cust.Id);
        PostChequePayment(c, payment, local, hdfc, 8000m, "100501", new DateOnly(2024, 5, 2),
            partyId: local.Id);

        var advice = Assert.Single(SupplierPaymentAdvice.Build(c, Year));
        Assert.Equal("Local Traders", advice.PartyName);
    }

    /// <summary>
    /// 🔴 The supplier advice and the <b>payroll</b> Payment Advice are two different documents for two
    /// different counterparties — the conflation census row 8.7 exists to record. This pins that building one
    /// does not touch the other: the payroll advice keeps its own type, its own builder and its own shape.
    /// </summary>
    [Fact]
    public void The_payroll_payment_advice_is_a_separate_document_and_is_untouched()
    {
        Assert.NotEqual(typeof(PaymentAdvice), typeof(SupplierPaymentAdvice));

        // The payroll advice's row type carries an EMPLOYEE and an IFSC; the supplier one carries a PARTY and
        // bill-wise detail. If anyone ever "unifies" them, these two lookups stop compiling or stop resolving.
        Assert.NotNull(typeof(PaymentAdviceRow).GetProperty("EmployeeId"));
        Assert.Null(typeof(PaymentAdviceRow).GetProperty("Bills"));
        Assert.NotNull(typeof(SupplierPaymentAdviceRow).GetProperty("PartyLedgerId"));
        Assert.NotNull(typeof(SupplierPaymentAdviceRow).GetProperty("Bills"));
        Assert.Null(typeof(SupplierPaymentAdviceRow).GetProperty("EmployeeId"));
    }

    /// <summary>
    /// An on-account payment references no bill; the letter names it for what it is rather than printing a
    /// blank reference.
    ///
    /// <para>🔴 <b>The "(advance)" caption is unreachable and this test pins WHY, rather than pretending
    /// otherwise.</b> <c>BillAllocation</c>'s constructor requires a name for New / Agst / <i>Advance</i> — only
    /// On Account may be nameless — so no valid allocation can ever reach the Advance branch of
    /// <c>BillCaption</c>. The branch stays as a defensive default, and this assertion is what stops a future
    /// reader from "covering" it with a fixture that cannot exist in the books.</para>
    /// </summary>
    [Fact]
    public void An_unreferenced_allocation_is_captioned_rather_than_left_blank()
    {
        Assert.Equal("(on account)", SupplierPaymentAdvice.BillCaption(
            new BillAllocation(BillRefType.OnAccount, string.Empty, Money.FromRupees(100m))));
        Assert.Equal("INV-9", SupplierPaymentAdvice.BillCaption(
            new BillAllocation(BillRefType.AgstRef, "INV-9", Money.FromRupees(100m))));

        // A nameless Advance is refused by the domain, so the "(advance)" caption has no reachable fixture.
        Assert.Throws<ArgumentException>(() =>
            new BillAllocation(BillRefType.Advance, string.Empty, Money.FromRupees(100m)));
        Assert.Equal("ADV-1", SupplierPaymentAdvice.BillCaption(
            new BillAllocation(BillRefType.Advance, "ADV-1", Money.FromRupees(100m))));
    }

    // ================================================================ 🔴 what actually LEFT THE BANK

    /// <summary>
    /// 🔴 <b>THE FIGURE ON THE LETTER IS THE FIGURE THE SUPPLIER RECONCILES AGAINST.</b> "Net Amount Paid" was
    /// <c>gross − TDS</c>, which is not what left the bank the moment the voucher carries any other credit.
    ///
    /// <para>Measured on the shape below: ₹10,000 of bills settled by paying ₹9,950 and crediting ₹50 to
    /// Discount Received. The books are right and the advice said ₹10,000 — a supplier reconciling that letter
    /// against its own bank statement finds nothing that matches, and the difference reads to it as a short
    /// payment or a missing credit note. The net is now the party debit less every OTHER credit attributable to
    /// this supplier, which for a single-supplier payment is exactly the bank outflow.</para>
    /// </summary>
    [Fact]
    public void The_net_paid_is_what_left_the_bank_not_gross_minus_tds()
    {
        var c = Seed(out var hdfc, out _, out _, out var acme, out _, out var payment, out _);
        var discount = new Domain.Ledger(Guid.NewGuid(), "Discount Received",
            c.FindGroupByName("Indirect Incomes")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(discount);

        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 6), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-77", Money.FromRupees(10000m)),
            }),
            new EntryLine(hdfc.Id, Money.FromRupees(9950m), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.NEFT, "UTR9950", new DateOnly(2024, 5, 6))),
            new EntryLine(discount.Id, Money.FromRupees(50m), DrCr.Credit),
        }, partyId: acme.Id));

        var advice = Assert.Single(SupplierPaymentAdvice.Build(c, Year));

        // The bills still add up to the gross — the letter states what was settled...
        Assert.Equal(Money.FromRupees(10000m), advice.GrossAmount);
        // ...and the net is the ₹9,950 the bank actually released, NOT the ₹10,000 the old gross − TDS gave.
        Assert.Equal(Money.FromRupees(9950m), advice.NetPaid);
        Assert.NotEqual(advice.GrossAmount, advice.NetPaid);
    }

    /// <summary>
    /// The mirror of the same rule: a bank charge is the bank's fee, NOT a deduction from the supplier. A naive
    /// "net = sum of the bank credits" would tell Acme it was paid ₹10,100 — a figure nobody was ever paid. The
    /// net is derived from the party's own debit, so the charge cannot leak into it.
    /// </summary>
    [Fact]
    public void A_bank_charge_on_the_same_voucher_is_not_added_to_what_the_supplier_was_paid()
    {
        var c = Seed(out var hdfc, out _, out var rent, out var acme, out _, out var payment, out _);

        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 7), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(10000m), DrCr.Debit),
            new EntryLine(rent.Id, Money.FromRupees(100m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(10100m), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.RTGS, "UTR10100", new DateOnly(2024, 5, 7))),
        }, partyId: acme.Id));

        var advice = Assert.Single(SupplierPaymentAdvice.Build(c, Year));
        Assert.Equal(Money.FromRupees(10000m), advice.GrossAmount);
        Assert.Equal(Money.FromRupees(10000m), advice.NetPaid);
    }

    /// <summary>
    /// 🔴 The case that CANNOT be attributed, pinned so nobody "improves" it into an invented apportionment. One
    /// voucher settles two suppliers and carries a ₹100 discount naming neither of them. Nothing in the posting
    /// says whose discount it was, so it is left out of BOTH letters and each supplier is told the gross it was
    /// credited with. Splitting it 50/50 would put on a letter a figure the books never recorded.
    /// </summary>
    [Fact]
    public void An_unattributable_deduction_on_a_two_supplier_payment_is_left_out_of_both_advices()
    {
        var c = Seed(out var hdfc, out _, out _, out var acme, out _, out var payment, out _);
        var beta = new Domain.Ledger(Guid.NewGuid(), "Beta Traders",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(beta);
        var discount = new Domain.Ledger(Guid.NewGuid(), "Discount Received",
            c.FindGroupByName("Indirect Incomes")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(discount);

        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 5, 8), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(4000m), DrCr.Debit),
            new EntryLine(beta.Id, Money.FromRupees(6000m), DrCr.Debit),
            new EntryLine(discount.Id, Money.FromRupees(100m), DrCr.Credit),
            new EntryLine(hdfc.Id, Money.FromRupees(9900m), DrCr.Credit,
                bankAllocation: new BankAllocation(
                    BankTransactionType.NEFT, "UTR9900", new DateOnly(2024, 5, 8))),
        }));

        var advices = SupplierPaymentAdvice.Build(c, Year);
        Assert.Equal(2, advices.Count);
        foreach (var a in advices)
            Assert.Equal(a.GrossAmount, a.NetPaid);
        Assert.Equal(
            new[] { Money.FromRupees(4000m), Money.FromRupees(6000m) },
            advices.OrderBy(a => a.PartyName, StringComparer.Ordinal).Select(a => a.NetPaid));
    }

    // ================================================================ the shared group predicate

    /// <summary>
    /// The ledger master decides whether to offer the cheque-printing block from a GROUP (the ledger does not
    /// exist yet); Bank Reconciliation decides the same thing from a saved LEDGER. The two must never diverge,
    /// so they walk one predicate.
    /// </summary>
    [Fact]
    public void The_group_level_and_ledger_level_bank_tests_agree_including_through_a_sub_group()
    {
        var c = Seed(out var hdfc, out _, out var rent, out _, out _, out _, out _);

        var sub = new Group(Guid.NewGuid(), "Current Accounts",
            GroupNature.Asset, c.FindGroupByName("Bank Accounts")!.Id);
        c.AddGroup(sub);
        var nested = new Domain.Ledger(Guid.NewGuid(), "ICICI Current", sub.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(nested);

        foreach (var l in new[] { hdfc, rent, nested })
            Assert.Equal(ClassificationRules.IsBankLedger(l, c), ClassificationRules.IsBankGroup(l.GroupId, c));

        Assert.True(ClassificationRules.IsBankGroup(sub.Id, c));
        Assert.False(ClassificationRules.IsBankGroup(null, c));
    }
}
