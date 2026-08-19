using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S5b — <c>VoucherEntryViewModel.ForAlter</c> rehydration, the ELEVEN SIMPLE families.</b>
///
/// <para>🔴 <b>The round trip is the heart of the slice, and its instrument is the CANONICAL EXPORT — never raw
/// <c>.db</c> bytes.</b> <c>entry_lines.id</c> is <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> and
/// <c>sqlite_sequence</c> never reuses ids, so a save that deletes and re-inserts renumbers those surrogate keys:
/// an UNTOUCHED book's bytes differ from themselves and the comparison proves nothing. The export carries the
/// semantic model and no surrogate ids (design §8.3).</para>
///
/// <para><b>What "SIMPLE" means, and where it comes from.</b> Design §6.6a.3 enumerates thirty
/// (base kind x predicate) rows over the accounting aggregate and returns eleven SIMPLE, eight DEFER, ten REFUSE
/// and one UNDETERMINED. SIMPLE = the posted lines equal the keyed lines, so the inverse of the four line writers
/// can rebuild them. The refusals are locked in <see cref="VoucherAlterRefusalTests"/>; this file proves the
/// eleven that are supposed to WORK actually do, one test per row, each ending in a byte-identical book.</para>
/// </summary>
public sealed class VoucherAlterForAlterTests
{
    // ================================================================ the shared round-trip assertion

    /// <summary>
    /// Opens <paramref name="voucherId"/> for alteration, re-accepts it UNCHANGED, and proves the book did not
    /// move — in memory AND on disk. Returns the screen so a caller can assert what it was pre-filled with.
    /// </summary>
    private static VoucherEntryViewModel AssertUnchangedRoundTrip(AlterationBook book, Guid voucherId)
    {
        var beforeMemory = book.Export();
        var beforeDisk = book.ExportReloaded();
        var voucherCount = book.Company.Vouchers.Count;

        var open = book.ForAlter(voucherId);
        Assert.Null(open.Refusal);
        Assert.False(open.IsRefused);
        var entry = open.Entry!;
        Assert.True(entry.IsAltering);
        Assert.Equal(voucherId, entry.AlteringVoucherId);

        Assert.True(entry.AcceptAlteration(), entry.Message);

        Assert.Equal(beforeMemory, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
        // Replace SWAPS at the index; a Post would have appended a second voucher and left the original standing.
        Assert.Equal(voucherCount, book.Company.Vouchers.Count);
        return entry;
    }

    // ================================================================ the eleven SIMPLE rows (§6.6a.3)

    /// <summary>Row 1 — Contra. Every appender is gated off for Contra, so posted lines == keyed lines.</summary>
    [Fact]
    public void Row01_a_Contra_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row01");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var bank = book.Ledger("HDFC Current", "Bank Accounts");

        var posted = book.Post(VoucherBaseType.Contra, book.On(),
            new[] { (bank, DrCr.Debit, "24680.35"), (cash, DrCr.Credit, "24680.35") });

        AssertUnchangedRoundTrip(book, posted.Id);
    }

    /// <summary>Row 2 — a plain Payment: no TDS line, no advance link, an ordinary Payment type.</summary>
    [Fact]
    public void Row02_a_plain_Payment_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row02");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var rent = book.Ledger("Rent", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "18500.75"), (cash, DrCr.Credit, "18500.75") });

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.Equal("18500.75", entry.Lines[0].ParsedAmount.ToString("0.00"));
    }

    /// <summary>Row 7 — a plain Receipt with no advance opt-in.</summary>
    [Fact]
    public void Row07_a_plain_Receipt_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row07");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var fees = book.Ledger("Consultancy Income", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "9999.99"), (fees, DrCr.Credit, "9999.99") });

        AssertUnchangedRoundTrip(book, posted.Id);
    }

    /// <summary>Row 10 — a plain Journal.</summary>
    [Fact]
    public void Row10_a_plain_Journal_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row10");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 4321.09m, "reclassification");

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.Equal("reclassification", entry.Narration);
    }

    /// <summary>
    /// Row 15 — a Journal posted by a SERVICE rather than keyed: the shape
    /// <c>PayrollVoucherService.PostGratuityProvision</c> produces (a balanced two-leg Journal with no line-level
    /// detail at all). Posted through <see cref="LedgerService"/> directly, exactly as that service does, so the
    /// "never keyed on a screen" half of the row is genuinely exercised.
    ///
    /// <para>🔴 <b>The row's OTHER arm — the forex revaluation Journal — does NOT round-trip, and its refusal is
    /// locked in <see cref="VoucherAlterRefusalTests"/>.</b> §6.6a row 15 names both posters as one SIMPLE row;
    /// measured, they differ.</para>
    /// </summary>
    [Fact]
    public void Row15_a_service_posted_two_leg_Journal_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row15");
        var expense = book.Ledger("Gratuity Expense", "Indirect Expenses");
        var provision = book.Ledger("Provision for Gratuity", "Provisions");
        var journalType = book.Type(VoucherBaseType.Journal);

        var magnitude = new Money(77777.77m);
        var voucher = new Voucher(
            Guid.NewGuid(), journalType.Id, book.On(),
            new[]
            {
                new EntryLine(expense.Id, magnitude, DrCr.Debit),
                new EntryLine(provision.Id, magnitude, DrCr.Credit),
            },
            narration: "Gratuity provision for the period");
        var posted = new LedgerService(book.Company).Post(voucher);
        book.Storage.Save(book.Company);

        AssertUnchangedRoundTrip(book, posted.Id);
    }

    /// <summary>Row 16 — a Sales voucher in AS-VOUCHER mode (plain grid), including its captured Reference No.
    /// which only a Purchase/Sales screen states.</summary>
    [Fact]
    public void Row16_a_plain_grid_Sales_round_trips_unchanged_with_its_reference()
    {
        using var book = AlterationBook.New("row16");
        var debtor = book.Ledger("Bright Traders", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "55555.55"), (sales, DrCr.Credit, "55555.55") },
            configure: e =>
            {
                e.ReferenceNo = "PO-4471";
                e.ReferenceDateText = ApexDate.Format(book.On(2));
            });

        Assert.Equal("PO-4471", posted.ReferenceNo);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.Equal("PO-4471", entry.ReferenceNo);
        Assert.Equal("PO-4471", book.Company.FindVoucher(posted.Id)!.ReferenceNo);
        Assert.Equal(book.On(2), book.Company.FindVoucher(posted.Id)!.ReferenceDate);
    }

    /// <summary>Row 20 — a Purchase voucher in AS-VOUCHER mode, on ledgers that fire neither TDS nor RCM.</summary>
    [Fact]
    public void Row20_a_plain_grid_Purchase_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row20");
        var creditor = book.Ledger("Steady Supplies", "Sundry Creditors");
        var purchases = book.Ledger("Purchase A/c", "Purchase Accounts");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (purchases, DrCr.Debit, "31415.92"), (creditor, DrCr.Credit, "31415.92") });

        AssertUnchangedRoundTrip(book, posted.Id);
    }

    /// <summary>
    /// Row 24 — a plain Credit Note (<c>ShowSection34Details</c> false). Its GST legs, when it has any, are
    /// HAND-KEYED and carry no <c>GstLineTax</c> — which is precisely why the tag filter cannot be relied on for
    /// this family, and why the keyed legs here round-trip verbatim rather than needing a re-stamp.
    /// </summary>
    [Fact]
    public void Row24_a_plain_Credit_Note_with_hand_keyed_tax_legs_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row24");
        var debtor = book.Ledger("Returning Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales Returns", "Sales Accounts");
        var outputTax = book.Ledger("Output CGST (keyed)", "Duties & Taxes");

        var posted = book.Post(VoucherBaseType.CreditNote, book.On(),
            new[]
            {
                (sales, DrCr.Debit, "10000.50"),
                (outputTax, DrCr.Debit, "900.05"),
                (debtor, DrCr.Credit, "10900.55"),
            });

        // The hand-keyed tax leg carries no engine stamp — nothing here is a derived figure.
        Assert.All(posted.Lines, l => Assert.False(l.HasGst));

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.Equal(3, entry.Lines.Count);
        Assert.All(book.Company.FindVoucher(posted.Id)!.Lines, l => Assert.False(l.HasGst));
    }

    /// <summary>Row 26 — a plain Debit Note.</summary>
    [Fact]
    public void Row26_a_plain_Debit_Note_round_trips_unchanged()
    {
        using var book = AlterationBook.New("row26");
        var creditor = book.Ledger("Rejected Supplier", "Sundry Creditors");
        var purchases = book.Ledger("Purchase Returns", "Purchase Accounts");

        var posted = book.Post(VoucherBaseType.DebitNote, book.On(),
            new[] { (creditor, DrCr.Debit, "6543.21"), (purchases, DrCr.Credit, "6543.21") });

        AssertUnchangedRoundTrip(book, posted.Id);
    }

    /// <summary>
    /// Row 28 — a Memorandum, <b>carrying its Post-Dated flag</b>. Memorandum is provisional by base kind, so
    /// <c>PostAndSave</c> forces <c>Optional</c> to false and leaves <c>ApplicableUpto</c> null — but
    /// <c>PostDated</c> can still be true, and <c>Replace</c> REFUSES a change to it. A rehydration that dropped
    /// it would fail the refusal rather than a balance check, which is the intended outcome; this proves it is
    /// carried instead.
    /// </summary>
    [Fact]
    public void Row28_a_post_dated_Memorandum_round_trips_with_its_flag()
    {
        using var book = AlterationBook.New("row28");
        var dr = book.Ledger("Memo Dr", "Indirect Expenses");
        var cr = book.Ledger("Memo Cr", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Memorandum, book.On(),
            new[] { (dr, DrCr.Debit, "2500.25"), (cr, DrCr.Credit, "2500.25") },
            configure: e => e.IsPostDated = true);

        Assert.True(posted.PostDated);
        Assert.False(posted.Optional);
        Assert.Null(posted.ApplicableUpto);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.True(entry.IsPostDated);
        Assert.True(book.Company.FindVoucher(posted.Id)!.PostDated);
    }

    /// <summary>
    /// Row 29 — a Reversing Journal, whose <c>ApplicableUpto</c> is ALWAYS non-default: <c>IsReversing</c> makes
    /// the field mandatory and on/after the voucher date, so EVERY voucher of this family carries one. A
    /// <c>ForAlter</c> that did not rehydrate it would throw S5a's provisional-vector refusal on every voucher of
    /// the family — the loudest possible failure, and the reason this is the cheapest S5b smoke test.
    /// </summary>
    [Fact]
    public void Row29_a_Reversing_Journal_round_trips_with_its_applicable_upto()
    {
        using var book = AlterationBook.New("row29");
        var dr = book.Ledger("Accrued Expense", "Indirect Expenses");
        var cr = book.Ledger("Accrual Liability", "Current Liabilities");
        var upto = book.On(45);

        var posted = book.Post(VoucherBaseType.ReversingJournal, book.On(),
            new[] { (dr, DrCr.Debit, "3000.33"), (cr, DrCr.Credit, "3000.33") },
            configure: e => e.ApplicableUptoText = ApexDate.Format(upto));

        Assert.Equal(upto, posted.ApplicableUpto);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.Equal(ApexDate.Format(upto), entry.ApplicableUptoText);
        Assert.Equal(upto, book.Company.FindVoucher(posted.Id)!.ApplicableUpto);
    }

    // ================================================================ the line writers' inverses

    /// <summary>
    /// <c>ToBillAllocations()</c> inverted: every posted allocation is re-keyed with its type, name, due date and
    /// amount, and re-running the writer reproduces the line exactly.
    /// </summary>
    [Fact]
    public void The_bill_wise_inverse_re_keys_every_allocation_and_round_trips()
    {
        using var book = AlterationBook.New("billwise");
        var debtor = book.Ledger("Bill-wise Party", "Sundry Debtors", billWise: true);
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var due = book.On(35);

        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "40000.40"), (sales, DrCr.Credit, "40000.40") },
            configure: e =>
            {
                var line = e.Lines[0];
                line.BillAllocations[0].RefType = BillRefType.NewRef;
                line.BillAllocations[0].Name = "INV-9";
                line.BillAllocations[0].DueDateText = ApexDate.Format(due);
                line.BillAllocations[0].AmountText = "25000.15";
                var second = line.AddBillAllocation(BillRefType.NewRef);
                second.Name = "INV-10";
                second.AmountText = "15000.25";
            });

        var partyLine = posted.Lines.Single(l => l.LedgerId == debtor.Id);
        Assert.Equal(2, partyLine.BillAllocations.Count);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        var rehydrated = entry.Lines.Single(l => l.SelectedLedger!.Id == debtor.Id);
        Assert.Equal(2, rehydrated.BillAllocations.Count);
        Assert.Equal("INV-9", rehydrated.BillAllocations[0].Name);
        Assert.Equal(due, rehydrated.BillAllocations[0].ParsedDueDate);
        Assert.Equal(25000.15m, rehydrated.BillAllocations[0].ParsedAmount);
        Assert.Equal("INV-10", rehydrated.BillAllocations[1].Name);
        Assert.Null(rehydrated.BillAllocations[1].ParsedDueDate);
    }

    /// <summary>
    /// <c>ToCostAllocations()</c> inverted, across <b>two parallel cost categories</b> — rule C-27's shape, where
    /// the SAME amount is allocated in full under each axis. An inverse that summed across categories, or that
    /// dropped the row's <c>CategoryId</c>, would fail here.
    /// </summary>
    [Fact]
    public void The_cost_allocation_inverse_re_keys_parallel_axes_and_round_trips()
    {
        using var book = AlterationBook.New("cost");
        var (branch, kolkata) = book.CostAxis("Branch", "Kolkata");
        var (dept, marketing) = book.CostAxis("Department", "Marketing");
        var expense = book.Ledger("Advertising", "Indirect Expenses", costApplicable: true);
        var cash = book.Company.FindLedgerByName("Cash")!;

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (expense, DrCr.Debit, "5000.37"), (cash, DrCr.Credit, "5000.37") },
            configure: e =>
            {
                var line = e.Lines[0];
                line.CostAllocations[0].SelectedCategory = branch;
                line.CostAllocations[0].SelectedCentre = kolkata;
                line.CostAllocations[0].AmountText = "5000.37";
                var second = line.AddCostAllocation();
                second.SelectedCategory = dept;
                second.SelectedCentre = marketing;
                second.AmountText = "5000.37";
            });

        var expenseLine = posted.Lines.Single(l => l.LedgerId == expense.Id);
        Assert.Equal(2, expenseLine.CostAllocations.Count);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        var rehydrated = entry.Lines.Single(l => l.SelectedLedger!.Id == expense.Id);
        Assert.Equal(2, rehydrated.CostAllocations.Count);
        Assert.Equal(branch.Id, rehydrated.CostAllocations[0].SelectedCategory!.Id);
        Assert.Equal(kolkata.Id, rehydrated.CostAllocations[0].SelectedCentre!.Id);
        Assert.Equal(dept.Id, rehydrated.CostAllocations[1].SelectedCategory!.Id);
        Assert.Equal(marketing.Id, rehydrated.CostAllocations[1].SelectedCentre!.Id);
    }

    /// <summary><c>ToBankAllocation()</c> inverted: transaction type, instrument number and instrument date.</summary>
    [Fact]
    public void The_bank_allocation_inverse_re_keys_the_instrument_and_round_trips()
    {
        using var book = AlterationBook.New("bank");
        var bank = book.Ledger("ICICI Current", "Bank Accounts");
        var rent = book.Ledger("Rent", "Indirect Expenses");
        var instrumentDate = book.On(3);

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "12000.60"), (bank, DrCr.Credit, "12000.60") },
            configure: e =>
            {
                var line = e.Lines[1];
                line.BankTransactionType = BankTransactionType.NEFT;
                line.InstrumentNumber = "UTR-88213";
                line.InstrumentDateText = ApexDate.Format(instrumentDate);
            });

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        var rehydrated = entry.Lines.Single(l => l.SelectedLedger!.Id == bank.Id);
        Assert.Equal(BankTransactionType.NEFT, rehydrated.BankTransactionType);
        Assert.Equal("UTR-88213", rehydrated.InstrumentNumber);
        Assert.Equal(instrumentDate, rehydrated.ParsedInstrumentDate);
    }

    /// <summary>
    /// 🔴 <c>ToForexInfo()</c> inverted, on a rate carrying SIX decimal places — the case the screen's own
    /// <c>"0.####"</c> formatter truncates. <c>ForexInfo.Rate</c> persists at <c>Schema.ForexScale</c> = 1,000,000
    /// and <c>Money.ForexBase</c> snaps forex x rate to the paisa, so a truncated rate yields a base amount
    /// <c>VoucherValidator</c> rejects. The rehydration widens the format instead of refusing the family.
    /// </summary>
    [Fact]
    public void The_forex_inverse_keeps_a_six_decimal_rate_and_round_trips()
    {
        using var book = AlterationBook.New("forex");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        // 1,234.50 x 83.123456 = 102,615.90... — six places, and a rate the "0.####" formatter would render
        // 83.1235, which produces a DIFFERENT paisa-snapped base.
        const decimal forexAmount = 1234.50m;
        const decimal rate = 83.123456m;
        var baseAmount = Money.ForexBase(new Money(forexAmount), rate).Amount;
        Assert.NotEqual(baseAmount, Money.ForexBase(new Money(forexAmount), 83.1235m).Amount);

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[]
            {
                (purchases, DrCr.Debit, baseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (creditor, DrCr.Credit, "0"),
            },
            configure: e =>
            {
                var line = e.Lines[1];
                line.ForexAmountText = forexAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                line.ForexRateText = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });

        var forexLine = posted.Lines.Single(l => l.LedgerId == creditor.Id);
        Assert.Equal(rate, forexLine.Forex!.Rate);
        Assert.Equal(baseAmount, forexLine.Amount.Amount);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        var rehydrated = entry.Lines.Single(l => l.SelectedLedger!.Id == creditor.Id);
        Assert.Equal(rate, rehydrated.ParsedForexRate);
        Assert.Equal(forexAmount, rehydrated.ParsedForexAmount);
        Assert.Equal(baseAmount, book.Company.FindVoucher(posted.Id)!
            .Lines.Single(l => l.LedgerId == creditor.Id).Amount.Amount);
    }

    /// <summary>
    /// 🔴 <b>The forex guard that is actually load-bearing — and it is NOT the format string.</b> Design §6.6a.5
    /// prescribes "format the rate at six or more places"; that alone still truncates a rate carrying MORE than
    /// six (reachable in memory, before a save rounds it to <c>Schema.ForexScale</c>), and a truncated rate makes
    /// <c>Money.ForexBase</c> land on a different paisa — which <c>VoucherValidator</c> rejects. The inverse
    /// therefore VERIFIES its own rendering by re-parsing it, and falls back to the decimal's exact round-trip
    /// form when the tidy one would lose a digit. Deleting that verification is what reddens this test; widening
    /// or narrowing the tidy format alone does not, because the fallback already covers it.
    /// </summary>
    [Fact]
    public void The_forex_inverse_survives_a_rate_carrying_more_than_six_decimal_places()
    {
        using var book = AlterationBook.New("forex7");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        const decimal forexAmount = 1234.50m;
        const decimal rate = 83.1234567m; // SEVEN places
        var baseAmount = Money.ForexBase(new Money(forexAmount), rate).Amount;

        // Posted through the engine and deliberately NOT saved: the store rounds the rate to six places, and the
        // question here is what the REHYDRATION does with the value the book is actually holding.
        var voucher = new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Purchase).Id, book.On(),
            new[]
            {
                new EntryLine(purchases.Id, new Money(baseAmount), DrCr.Debit),
                new EntryLine(creditor.Id, new Money(baseAmount), DrCr.Credit,
                    forex: new ForexInfo(usd.Id, new Money(forexAmount), rate)),
            });
        var posted = new LedgerService(book.Company).Post(voucher);

        var open = book.ForAlter(posted.Id);
        Assert.Null(open.Refusal);

        var rehydrated = open.Entry!.Lines.Single(l => l.SelectedLedger!.Id == creditor.Id);
        Assert.Equal(rate, rehydrated.ParsedForexRate);
        Assert.Equal(forexAmount, rehydrated.ParsedForexAmount);
        // …and the derived base landed back on the posted paisa, which is the invariant the engine enforces.
        Assert.Equal(baseAmount, rehydrated.ParsedAmount);
    }

    // ================================================================ CONSTRAINT 1 — ends in Replace, never Post

    /// <summary>
    /// 🔴 <b>The reconcile tick survives an alteration.</b> <c>BankAllocation.BankDate</c> is written onto a POSTED
    /// voucher by a later human action and exists NOWHERE on the entry screen, so
    /// <see cref="VoucherLineViewModel.ToBankAllocation"/> never writes one. Through <c>Post</c> the tick would be
    /// destroyed silently; through <c>Replace</c> it is carried — which is why the constraint exists and is not a
    /// stylistic preference.
    /// </summary>
    [Fact]
    public void Constraint1_a_bank_reconciliation_date_survives_an_unchanged_alteration()
    {
        using var book = AlterationBook.New("banktick");
        var bank = book.Ledger("Axis Current", "Bank Accounts");
        var rent = book.Ledger("Rent", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "7500.15"), (bank, DrCr.Credit, "7500.15") },
            configure: e => e.Lines[1].InstrumentNumber = "CHQ-4410");

        var tick = book.On(9);
        Assert.True(BankReconciliation.SetBankDate(book.Company, posted.Id, bank.Id, tick));
        book.Storage.Save(book.Company);

        AssertUnchangedRoundTrip(book, posted.Id);

        var after = book.Company.FindVoucher(posted.Id)!.Lines.Single(l => l.LedgerId == bank.Id);
        Assert.Equal(tick, after.BankAllocation!.BankDate);
        Assert.True(after.BankAllocation.IsReconciled);
    }

    /// <summary>
    /// The other half of constraint 1: a genuine CONTENT change still ends in <c>Replace</c>, so the voucher keeps
    /// its Guid, its number and its list position — and the engine's own clearing rule (not a silent overwrite)
    /// decides what happens to the tick.
    /// </summary>
    [Fact]
    public void Constraint1_an_altered_voucher_keeps_its_id_number_and_position()
    {
        using var book = AlterationBook.New("identity");
        var dr = book.Ledger("Dr Leg", "Indirect Expenses");
        var cr = book.Ledger("Cr Leg", "Indirect Incomes");

        book.PostPlainPair(VoucherBaseType.Journal, 100.10m);
        var target = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "200.20"), (cr, DrCr.Credit, "200.20") });
        book.PostPlainPair(VoucherBaseType.Journal, 300.30m);

        var index = book.Company.Vouchers.ToList().FindIndex(v => v.Id == target.Id);
        var number = target.Number;

        var open = book.ForAlter(target.Id);
        open.Entry!.Lines[0].AmountText = "250.25";
        open.Entry.Lines[1].AmountText = "250.25";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var altered = book.Company.FindVoucher(target.Id)!;
        Assert.Equal(target.Id, altered.Id);
        Assert.Equal(number, altered.Number);
        Assert.Equal(index, book.Company.Vouchers.ToList().FindIndex(v => v.Id == target.Id));
        Assert.Equal(250.25m, altered.TotalDebit.Amount);
        Assert.Equal(3, book.Company.Vouchers.Count);
    }

    // ================================================================ CONSTRAINT 2 — the provisional vector

    /// <summary>An Optional voucher stays Optional across an unchanged alteration — carried, not dropped.</summary>
    [Fact]
    public void Constraint2_an_Optional_voucher_carries_its_flag_through_an_alteration()
    {
        using var book = AlterationBook.New("optional");
        var dr = book.Ledger("Dr Leg", "Indirect Expenses");
        var cr = book.Ledger("Cr Leg", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "84321.55"), (cr, DrCr.Credit, "84321.55") },
            configure: e => e.IsOptional = true);
        Assert.True(posted.Optional);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.True(entry.IsOptional);
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);
    }

    /// <summary>
    /// 🔴 And the mirror, which is what makes the carry provable rather than incidental: an operator who presses
    /// Ctrl+L on an altering screen is REFUSED BY NAME, and the books do not move. Design §12.8 is explicit that
    /// this must not be silently ignored either — the refusal names the verb that owns the toggle.
    /// </summary>
    [Fact]
    public void Constraint2_turning_an_Optional_voucher_live_during_an_alteration_is_refused_by_name()
    {
        using var book = AlterationBook.New("optionalmove");
        var dr = book.Ledger("Dr Leg", "Indirect Expenses");
        var cr = book.Ledger("Cr Leg", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "84321.55"), (cr, DrCr.Credit, "84321.55") },
            configure: e => e.IsOptional = true);

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        open.Entry!.ToggleOptional();
        Assert.False(open.Entry.IsOptional);

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("provisional state", open.Entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Optional", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
        Assert.True(book.Company.FindVoucher(posted.Id)!.Optional);
    }

    /// <summary>Dropping a Memorandum's Post-Dated flag mid-alteration is refused by name (row 28's stated
    /// outcome: it fails the refusal, not a balance check).</summary>
    [Fact]
    public void Constraint2_dropping_the_post_dated_flag_during_an_alteration_is_refused_by_name()
    {
        using var book = AlterationBook.New("postdated");
        var dr = book.Ledger("Memo Dr", "Indirect Expenses");
        var cr = book.Ledger("Memo Cr", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Memorandum, book.On(),
            new[] { (dr, DrCr.Debit, "2500.25"), (cr, DrCr.Credit, "2500.25") },
            configure: e => e.IsPostDated = true);

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        open.Entry!.TogglePostDated();

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("Post-dated", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
    }

    /// <summary>Moving a Reversing Journal's Applicable Upto mid-alteration is refused, and the refusal names
    /// both dates — so the accrual still lapses when it was set to lapse.</summary>
    [Fact]
    public void Constraint2_moving_applicable_upto_during_an_alteration_is_refused_by_name()
    {
        using var book = AlterationBook.New("upto");
        var dr = book.Ledger("Accrued Expense", "Indirect Expenses");
        var cr = book.Ledger("Accrual Liability", "Current Liabilities");
        var upto = book.On(45);

        var posted = book.Post(VoucherBaseType.ReversingJournal, book.On(),
            new[] { (dr, DrCr.Debit, "3000.33"), (cr, DrCr.Credit, "3000.33") },
            configure: e => e.ApplicableUptoText = ApexDate.Format(upto));

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        open.Entry!.ApplicableUptoText = ApexDate.Format(book.On(90));

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("Applicable Upto", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
        Assert.Equal(upto, book.Company.FindVoucher(posted.Id)!.ApplicableUpto);
    }

    // ================================================================ CONSTRAINT 3 — never echo a stamped tax

    /// <summary>
    /// The lines an alteration builds carry NO stamped <c>Gst</c>/<c>Tds</c>/<c>Tcs</c> — there is no code path in
    /// <c>BuildPlainEntryLines</c> that could supply one, and every voucher that HAS one is refused at the door
    /// (locked family by family in <see cref="VoucherAlterRefusalTests"/>). Refusing is the only form of
    /// "never echo" available to a slice that does not re-derive.
    /// </summary>
    [Fact]
    public void Constraint3_an_altered_voucher_carries_no_engine_stamped_tax_on_any_line()
    {
        using var book = AlterationBook.New("nostamp");
        var debtor = book.Ledger("Plain Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var tax = book.Ledger("Output CGST (keyed)", "Duties & Taxes");

        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[]
            {
                (debtor, DrCr.Debit, "11800.90"),
                (sales, DrCr.Credit, "10000.90"),
                (tax, DrCr.Credit, "1800.00"),
            });

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "11801.90";
        open.Entry.Lines[1].AmountText = "10001.90";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var altered = book.Company.FindVoucher(posted.Id)!;
        Assert.All(altered.Lines, l => Assert.False(l.HasGst));
        Assert.All(altered.Lines, l => Assert.False(l.HasTds));
        Assert.All(altered.Lines, l => Assert.False(l.HasTcs));
    }

    // ================================================================ CONSTRAINT 4 — ForAlter cannot reuse Accept

    /// <summary>
    /// 🔴 <c>Accept</c> HARD-REFUSES on an altering screen. Without this guard it would mint a fresh
    /// <see cref="Guid"/> and post a SECOND voucher — leaving the original standing, so the book would hold the
    /// entry twice — and it would re-run TDS / RCM / advance DETECTION against today's masters, so a
    /// narration-only alteration could acquire or lose a carve.
    /// </summary>
    [Fact]
    public void Constraint4_Accept_is_refused_on_an_altering_screen_and_posts_nothing()
    {
        using var book = AlterationBook.New("acceptguard");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 909.09m);
        var before = book.Export();
        var count = book.Company.Vouchers.Count;

        var open = book.ForAlter(posted.Id);
        Assert.False(open.Entry!.Accept());
        Assert.Contains("altering a posted voucher", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(count, book.Company.Vouchers.Count);
        Assert.Equal(before, book.Export());
    }

    /// <summary>The mirror: <c>AcceptAlteration</c> is refused on a FRESH entry screen, so the two verbs cannot be
    /// swapped in either direction.</summary>
    [Fact]
    public void Constraint4_AcceptAlteration_is_refused_on_a_fresh_entry_screen()
    {
        using var book = AlterationBook.New("freshguard");
        var entry = book.Entry(VoucherBaseType.Journal);
        Assert.False(entry.IsAltering);
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("entering a new voucher", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(book.Company.Vouchers);
    }

    // ================================================================ CONSTRAINT 5 — the mode comes from the voucher

    /// <summary>
    /// A Payment whose posted lines ARE the Single-Entry shape (one account-side line first, the rest opposite)
    /// re-opens in Single Entry — and the mode's own stamp is provably a no-op on it, so the amounts survive.
    /// </summary>
    [Fact]
    public void Constraint5_a_single_entry_shaped_Payment_re_opens_in_Single_Entry()
    {
        using var book = AlterationBook.New("mode-se");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var rent = book.Ledger("Rent", "Indirect Expenses");
        var power = book.Ledger("Electricity", "Indirect Expenses");

        // Payment: the ACCOUNT side is CREDIT (BOOK p.32), so line 0 must be the Cr cash leg.
        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[]
            {
                (cash, DrCr.Credit, "9000.90"),
                (rent, DrCr.Debit, "5000.50"),
                (power, DrCr.Debit, "4000.40"),
            });

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.True(entry.IsSingleEntry);
        Assert.Equal(cash.Id, entry.SingleEntryAccount!.Id);
        Assert.Equal(9000.90m, entry.Lines[0].ParsedAmount);
    }

    /// <summary>
    /// 🔴 And the shape that must NOT open in Single Entry: a Payment keyed in the double-entry grid with the
    /// account leg NOT first. <c>SyncSingleEntrySides</c> stamps line 0 to the account side, every other line to
    /// the opposite, and REWRITES line 0's amount to Σ of the rest — so opening this in Single Entry would flip a
    /// side and rewrite an amount silently. It opens in the plain grid instead, and round-trips.
    /// </summary>
    [Fact]
    public void Constraint5_a_double_entry_shaped_Payment_re_opens_in_the_plain_grid_unrewritten()
    {
        using var book = AlterationBook.New("mode-de");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var bank = book.Ledger("Kotak Current", "Bank Accounts");
        var rent = book.Ledger("Rent", "Indirect Expenses");

        // Line 0 is a DEBIT on a Payment, whose account side is Credit — so this is not the Single-Entry shape.
        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[]
            {
                (rent, DrCr.Debit, "8000.80"),
                (cash, DrCr.Credit, "3000.30"),
                (bank, DrCr.Credit, "5000.50"),
            });

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.False(entry.IsSingleEntry);
        Assert.True(entry.IsAsVoucherMode);
        Assert.Equal(DrCr.Debit, entry.Lines[0].Side);
        Assert.Equal(8000.80m, entry.Lines[0].ParsedAmount);
        Assert.Equal(3000.30m, entry.Lines[1].ParsedAmount);
        Assert.Equal(5000.50m, entry.Lines[2].ParsedAmount);
    }

    // ================================================================ the alteration actually alters

    /// <summary>A real content change moves the books by exactly the difference, and persists.</summary>
    [Fact]
    public void An_altered_amount_moves_the_books_and_persists()
    {
        using var book = AlterationBook.New("moves");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var fees = book.Ledger("Consultancy Income", "Indirect Incomes");

        var posted = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "1000.10"), (fees, DrCr.Credit, "1000.10") });

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "1500.15";
        open.Entry.Lines[1].AmountText = "1500.15";
        open.Entry.Narration = "corrected";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var reloaded = book.Storage.Load(book.Storage.ListCompanies().Single(e => e.Name == book.Company.Name));
        var altered = reloaded.FindVoucher(posted.Id)!;
        Assert.Equal(1500.15m, altered.TotalDebit.Amount);
        Assert.Equal("corrected", altered.Narration);
        Assert.Single(reloaded.Vouchers);
    }

    /// <summary>A line can be REMOVED by an alteration; the voucher stays balanced and the removed leg is gone.</summary>
    [Fact]
    public void An_alteration_can_remove_a_line()
    {
        using var book = AlterationBook.New("removeline");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var rent = book.Ledger("Rent", "Indirect Expenses");
        var power = book.Ledger("Electricity", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[]
            {
                (cash, DrCr.Credit, "9000.90"),
                (rent, DrCr.Debit, "5000.50"),
                (power, DrCr.Debit, "4000.40"),
            });
        Assert.Equal(3, posted.Lines.Count);

        var open = book.ForAlter(posted.Id);
        var entry = open.Entry!;
        entry.Mode = VoucherEntryMode.AsVoucher; // leave Single Entry so the removal is a plain line operation
        entry.RemoveLine(entry.Lines.Single(l => l.SelectedLedger!.Id == power.Id));
        entry.Lines.Single(l => l.SelectedLedger!.Id == cash.Id).AmountText = "5000.50";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var altered = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(2, altered.Lines.Count);
        Assert.DoesNotContain(altered.Lines, l => l.LedgerId == power.Id);
        Assert.Equal(5000.50m, altered.TotalDebit.Amount);
    }

    /// <summary>An alteration of a CANCELLED voucher carries the flag — <c>Replace</c> refuses a change to it, and
    /// un-cancel is out of scope, so the carry is what makes the alteration possible at all.</summary>
    [Fact]
    public void A_cancelled_voucher_can_be_altered_and_stays_cancelled()
    {
        using var book = AlterationBook.New("cancelled");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 4444.44m);
        new LedgerService(book.Company).Cancel(posted.Id);
        book.Storage.Save(book.Company);

        var entry = AssertUnchangedRoundTrip(book, posted.Id);
        Assert.True(book.Company.FindVoucher(posted.Id)!.Cancelled);
        Assert.True(entry.IsAltering);
    }

    /// <summary>
    /// Three facts the plain grid can never re-key and must therefore CARRY from the posted voucher: its
    /// <c>PartyId</c>, its <c>ReferenceNo</c>/<c>ReferenceDate</c> when it is not on a Purchase/Sales (where the
    /// screen never captures them), and its DATE when it is not the latest voucher in the book.
    ///
    /// <para>All three are reachable: the canonical-XML import and the SQLite read path both build vouchers this
    /// way, and every one of them would be dropped SILENTLY by a rehydration that read the screen instead of the
    /// voucher — <c>TryResolveReferenceCapture</c> hands back null/null off a Purchase/Sales, and the constructor's
    /// default date is the book's LAST voucher date.</para>
    /// </summary>
    [Fact]
    public void The_party_the_reference_and_the_date_are_carried_from_the_posted_voucher()
    {
        using var book = AlterationBook.New("carries");
        var party = book.Ledger("Named Party", "Sundry Debtors");
        var dr = book.Ledger("Dr Leg", "Indirect Expenses");
        var cr = book.Ledger("Cr Leg", "Indirect Incomes");
        var early = book.On(2);

        var amount = new Money(6600.66m);
        var voucher = new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Journal).Id, early,
            new[]
            {
                new EntryLine(dr.Id, amount, DrCr.Debit),
                new EntryLine(cr.Id, amount, DrCr.Credit),
            },
            partyId: party.Id,
            referenceNo: "IMPORTED-REF-3",
            referenceDate: book.On(1));
        var posted = new LedgerService(book.Company).Post(voucher);

        // A LATER voucher, so the entry screen's default date is NOT the one under test.
        book.PostPlainPair(VoucherBaseType.Journal, 10.10m);
        book.Storage.Save(book.Company);

        var open = book.ForAlter(posted.Id);
        Assert.Null(open.Refusal);
        Assert.Equal(early, open.Entry!.Date);          // carried, not the constructor's "latest voucher" default
        Assert.False(open.Entry.ShowReferenceCapture);  // a Journal never shows the reference field at all

        AssertUnchangedRoundTrip(book, posted.Id);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(party.Id, after.PartyId);
        Assert.Equal("IMPORTED-REF-3", after.ReferenceNo);
        Assert.Equal(book.On(1), after.ReferenceDate);
        Assert.Equal(early, after.Date);
    }

    // ================================================================ what happens between opening and accepting

    /// <summary>
    /// 🔴 <b>Eligibility is re-checked at ACCEPT, not only at open.</b> A screen can sit open while the book moves
    /// underneath it — here an advance record is registered against the very voucher being altered, which is one
    /// of the five OFF-LINE side effects no test of <c>EntryLine</c> contents can see. Without the re-check the
    /// alteration would go through and the record would be left naming a voucher that no longer claims it.
    /// </summary>
    [Fact]
    public void An_off_line_record_registered_while_the_screen_is_open_refuses_the_accept()
    {
        using var book = AlterationBook.New("raceadvance");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var debtor = book.Ledger("Late Advance Client", "Sundry Debtors");
        var posted = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "20000.20"), (debtor, DrCr.Credit, "20000.20") });

        var open = book.ForAlter(posted.Id);
        Assert.Null(open.Refusal); // it was eligible when the screen opened…

        book.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), posted.Id, isService: false, new Money(20000.20m), rateBasisPoints: 0,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: Money.Zero));

        var before = book.Export();
        Assert.False(open.Entry!.AcceptAlteration());          // …and is not, by the time it is accepted
        Assert.Contains("advance record", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, book.Export());
    }

    /// <summary>A voucher DELETED while its alteration screen was open refuses the accept rather than re-posting
    /// the deleted entry through the back door.</summary>
    [Fact]
    public void A_voucher_deleted_while_the_screen_is_open_refuses_the_accept()
    {
        using var book = AlterationBook.New("racedelete");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 5150.51m);

        var open = book.ForAlter(posted.Id);
        Assert.True(book.Company.RemoveVoucher(book.Company.FindVoucher(posted.Id)!));

        Assert.False(open.Entry!.AcceptAlteration());
        Assert.Contains("no longer in this company's books", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(book.Company.Vouchers);
    }

    /// <summary>
    /// 🔴 <b>A FAILED SAVE ROLLS THE SWAP BACK.</b> The engine mutates the in-memory aggregate and the save happens
    /// after it, so without the rollback the books would hold the amended voucher, the <c>.db</c> the original, and
    /// every later save would carry that divergence forward. Provoked the way the Alt+X arm's own note describes:
    /// <c>CompanyStorage.Save</c> opens with <c>Company.EnsureValid()</c>, which throws <b>ArgumentException</b> on
    /// a bad PIN — a genuinely reachable state, and one the narrow old catch filters used to miss entirely.
    /// </summary>
    [Fact]
    public void A_failed_save_puts_the_original_voucher_back()
    {
        using var book = AlterationBook.New("savefail");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 6100.61m);
        var before = book.Export();

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "9999.99";
        open.Entry.Lines[1].AmountText = "9999.99";

        book.Company.Pin = "NOT-A-PIN"; // the next Save will throw out of EnsureValid

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("Could not save the company", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);

        book.Company.Pin = null; // undo the provocation so the export is comparable again
        Assert.Equal(before, book.Export());
        Assert.Equal(6100.61m, book.Company.FindVoucher(posted.Id)!.TotalDebit.Amount);
    }

    /// <summary>A half-filled row is refused with the same up-front message a fresh entry gets — the alteration
    /// path makes exactly the same plain-grid checks, not a laxer set.</summary>
    [Fact]
    public void A_half_filled_row_refuses_the_alteration_with_the_ordinary_message()
    {
        using var book = AlterationBook.New("halffilled");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 700.70m);
        var before = book.Export();

        var open = book.ForAlter(posted.Id);
        open.Entry!.AddLine().AmountText = "5"; // a row with an amount and no ledger

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("needs a ledger and a positive amount", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
    }

    /// <summary>An unbalanced alteration is refused by the engine and nothing is swapped.</summary>
    [Fact]
    public void An_unbalanced_alteration_is_refused_and_the_book_does_not_move()
    {
        using var book = AlterationBook.New("unbalanced");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 800.80m);
        var before = book.Export();

        var open = book.ForAlter(posted.Id);
        open.Entry!.Lines[0].AmountText = "900.90"; // Dr moves, Cr does not

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("out of balance", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, book.Export());
        Assert.Equal(800.80m, book.Company.FindVoucher(posted.Id)!.TotalDebit.Amount);
    }

    /// <summary>
    /// The warnings <c>Replace</c> raises actually reach the operator. They are the only signal for the things an
    /// alteration silently diverges — a moved date, a cleared bank reconciliation, a statutory record that no
    /// longer matches — so a caller that discarded them would leave the engine warning into a void.
    /// </summary>
    [Fact]
    public void The_warnings_Replace_raises_are_surfaced_on_the_screen()
    {
        using var book = AlterationBook.New("warnings");
        var bank = book.Ledger("Warned Bank", "Bank Accounts");
        var rent = book.Ledger("Rent", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "1000.10"), (bank, DrCr.Credit, "1000.10") },
            configure: e => e.Lines[1].InstrumentNumber = "CHQ-9");

        var tick = book.On(12);
        Assert.True(BankReconciliation.SetBankDate(book.Company, posted.Id, bank.Id, tick));
        book.Storage.Save(book.Company);

        var open = book.ForAlter(posted.Id);
        open.Entry!.Date = book.On(20);                       // a moved date
        open.Entry.Lines[0].AmountText = "1500.15";           // and a moved amount, which clears the tick
        open.Entry.Lines[1].AmountText = "1500.15";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        Assert.Contains("Voucher date changed", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bank reconciliation date", open.Entry.Message!, StringComparison.OrdinalIgnoreCase);
        // …and the tick really was cleared, not merely warned about.
        Assert.Null(book.Company.FindVoucher(posted.Id)!
            .Lines.Single(l => l.LedgerId == bank.Id).BankAllocation!.BankDate);
    }

    /// <summary>
    /// 🔴 Ctrl+H is still live on an altering screen, so it must not become a back door into a family S5b refuses.
    /// Both invoice modes key their voucher from a DIFFERENT collection than the plain grid, so accepting in one
    /// of them would post the old Dr/Cr lines while the operator was looking at — and possibly keying — an invoice
    /// grid whose contents this path never reads.
    /// </summary>
    [Fact]
    public void Switching_an_altering_screen_into_an_invoice_mode_refuses_the_accept()
    {
        using var book = AlterationBook.New("modeswitch");
        var debtor = book.Ledger("Mode Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "3300.33"), (sales, DrCr.Credit, "3300.33") });

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        open.Entry!.ToggleItemInvoice();
        Assert.True(open.Entry.IsItemInvoice);

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("plain Dr/Cr grid", open.Entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());

        // …and switching back lets the ordinary alteration through, so the guard is a gate and not a dead end.
        open.Entry.ToggleItemInvoice();
        Assert.True(open.Entry.IsAsVoucherMode);
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);
        Assert.Equal(before, book.Export());
    }

    /// <summary>The screen shows the voucher's OWN number, not the next number in the sequence — a fresh entry
    /// would preview max+1, which is a different voucher entirely.</summary>
    [Fact]
    public void The_altering_screen_shows_the_vouchers_own_number()
    {
        using var book = AlterationBook.New("number");
        var first = book.PostPlainPair(VoucherBaseType.Journal, 111.11m);
        book.PostPlainPair(VoucherBaseType.Journal, 222.22m);
        book.PostPlainPair(VoucherBaseType.Journal, 333.33m);

        var open = book.ForAlter(first.Id);
        Assert.Equal(first.Number, open.Entry!.VoucherNumber);
        Assert.NotEqual(4, open.Entry.VoucherNumber);
    }

    // ================================================================ 🔴 THE FIX PASS — S5b review findings

    /// <summary>
    /// 🔴 <b>CONSTRAINT 5's other half — Ctrl+H into SINGLE ENTRY</b> (finding L1-02, a measured BLOCKER).
    ///
    /// <para>The shipped gate refused only the two INVOICE modes, and Single Entry deliberately sits INSIDE
    /// <c>IsAsVoucherMode</c> (it is a re-render of the same lines, which is what keeps Accept routing to the plain
    /// path) — so one <c>ChangeMode()</c> on an altering Payment walked straight past it. Entering the mode runs
    /// <c>SyncSingleEntrySides</c>, which stamps line 0 to the account side, every other line to the opposite side,
    /// and rewrites line 0's amount to Σ of the rest. On this voucher — keyed in the Dr/Cr grid with TWO credits —
    /// that flips every side: measured before the fix, Rent went from Dr 100.11 to Cr 100.11 (an expense became an
    /// income), Cash went UP on a payment, the replacement still balanced, and the alteration reported
    /// "Payment No. 1 altered." with no warning of any kind.</para>
    /// </summary>
    [Fact]
    public void Constraint5_pressing_Ctrl_H_into_Single_Entry_on_a_grid_keyed_voucher_refuses_the_accept()
    {
        using var book = AlterationBook.New("ctrlh");
        var rent = book.Ledger("Rent", "Indirect Expenses");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var bank = book.Ledger("HDFC Current", "Bank Accounts");

        // THREE legs, two of them on the credit side — the exact shape SeedAlterationMode's own doc comment names
        // as the one Single Entry would corrupt.
        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "100.11"), (cash, DrCr.Credit, "60.06"), (bank, DrCr.Credit, "40.05") });

        var before = book.Export();
        var beforeDisk = book.ExportReloaded();
        var open = book.ForAlter(posted.Id);
        var entry = open.Entry!;
        Assert.False(entry.IsSingleEntry);            // the seed is right: it re-opened in the Dr/Cr grid

        entry.ChangeMode();                            // Ctrl+H
        Assert.True(entry.IsSingleEntry);
        Assert.True(entry.IsAsVoucherMode);            // …and the invoice gate still does not see it

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("keyed in the Dr/Cr grid", entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());

        // The book is what matters, so assert the two legs the flip inverted are still where they were posted.
        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(DrCr.Debit, after.Lines.Single(l => l.LedgerId == rent.Id).Side);
        Assert.Equal(DrCr.Credit, after.Lines.Single(l => l.LedgerId == cash.Id).Side);

        // …and switching back lets the ordinary alteration through, so this is a gate and not a dead end.
        entry.ChangeMode();
        Assert.False(entry.IsSingleEntry);
        Assert.True(entry.AcceptAlteration(), entry.Message);
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// 🔴 <b>The bill-allocation <c>RefType</c> carry — the one line-writer field with no lock</b> (finding L2-02).
    /// Hardcoding <c>AddBillAllocation(a.RefType)</c> to <c>NewRef</c> survived the entire S5b suite, because every
    /// allocation in it was a New Reference. <c>PreloadSettlement</c> pre-loads <c>AgstRef</c> rows onto the plain
    /// grid straight from the Outstandings report, so a settlement is the ORDINARY shape, not an exotic one — and a
    /// regression that turned one into a fresh bill would change the persisted allocation type and the canonical
    /// export with it, while the accept reported plain success.
    /// </summary>
    [Fact]
    public void An_AgstRef_settlement_round_trips_with_its_reference_type()
    {
        using var book = AlterationBook.New("agstref");
        var debtor = book.Ledger("Bill-wise Customer", "Sundry Debtors", billWise: true);
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var cash = book.Company.FindLedgerByName("Cash")!;

        // The opening bill…
        book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "40000.40"), (sales, DrCr.Credit, "40000.40") },
            configure: e =>
            {
                var row = e.Lines[0].BillAllocations[0];
                row.RefType = BillRefType.NewRef;
                row.Name = "INV-77";
                row.AmountText = "40000.40";
            });

        // …and the receipt that SETTLES it (Against Reference), which is what has never been round-tripped.
        var settlement = book.Post(VoucherBaseType.Receipt, book.On(9),
            new[] { (cash, DrCr.Debit, "40000.40"), (debtor, DrCr.Credit, "40000.40") },
            configure: e =>
            {
                var row = e.Lines[1].BillAllocations[0];
                row.RefType = BillRefType.AgstRef;
                row.Name = "INV-77";
                row.AmountText = "40000.40";
            });

        var postedAllocation = settlement.Lines.Single(l => l.LedgerId == debtor.Id).BillAllocations.Single();
        Assert.Equal(BillRefType.AgstRef, postedAllocation.RefType);

        var entry = AssertUnchangedRoundTrip(book, settlement.Id);

        // The REHYDRATED row carries the settlement type — the assertion that dies when the carry is hardcoded…
        var rehydrated = entry.Lines.Single(l => l.SelectedLedger!.Id == debtor.Id).BillAllocations.Single();
        Assert.Equal(BillRefType.AgstRef, rehydrated.RefType);
        Assert.Equal("INV-77", rehydrated.Name);
        // …and so does the PERSISTED one, which is what the canonical export emits.
        Assert.Equal(
            BillRefType.AgstRef,
            book.Company.FindVoucher(settlement.Id)!
                .Lines.Single(l => l.LedgerId == debtor.Id).BillAllocations.Single().RefType);
    }

    /// <summary>
    /// 🔴 <b>A LEGACY cross-category cost voucher — refused with the rule it actually breaks</b> (finding L3-03).
    ///
    /// <para><c>CostAllocationStrictness.Legacy</c> exists because books on disk hold lines whose axes foot only
    /// when ADDED TOGETHER, under the partition rule C-27 abolished; <c>SqliteCompanyStore.Load</c> and the
    /// canonical import both re-post through it, so this population is exactly what the tolerance admits.
    /// <c>Replace</c> validates with <c>Strict</c>, so such a voucher opens and cannot be accepted — and the
    /// message it used to get said the allocations "must sum to the line amount (5,000.00)" while they summed to
    /// exactly 5,000.00. The operator was told to satisfy a rule the voucher already satisfied, on the only screen
    /// that can remediate it. The wording now mirrors <c>VoucherValidator</c>'s own C-27 sentence and names the
    /// short AXIS.</para>
    /// </summary>
    [Fact]
    public void A_legacy_cross_category_cost_voucher_is_refused_with_the_per_axis_rule()
    {
        using var book = AlterationBook.New("legacycost");
        var (branch, kolkata) = book.CostAxis("Branch", "Kolkata");
        var (dept, marketing) = book.CostAxis("Department", "Marketing");
        var travel = book.Ledger("Travel", "Indirect Expenses", costApplicable: true);
        var cash = book.Company.FindLedgerByName("Cash")!;

        // 3,000 under Branch + 2,000 under Department: no single axis foots, the cross-axis sum does. Posted
        // through the door SqliteCompanyStore.Load uses on every open — the only door that accepts this shape.
        var voucher = new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Payment).Id, book.On(),
            new[]
            {
                new EntryLine(travel.Id, new Money(5000m), DrCr.Debit, costAllocations: new[]
                {
                    new CostAllocation(branch.Id, kolkata.Id, new Money(3000m)),
                    new CostAllocation(dept.Id, marketing.Id, new Money(2000m)),
                }),
                new EntryLine(cash.Id, new Money(5000m), DrCr.Credit),
            });
        var posted = new LedgerService(book.Company).Post(voucher, CostAllocationStrictness.Legacy);
        book.Storage.Save(book.Company);

        var open = book.ForAlter(posted.Id);
        Assert.Null(open.Refusal);                       // it opens — this IS the remediation screen
        Assert.False(open.Entry!.AcceptAlteration());

        var message = open.Entry.Message!;
        Assert.Contains("under cost category", message, StringComparison.Ordinal);
        Assert.Contains("each cost category must be allocated in full", message, StringComparison.Ordinal);
        Assert.Contains("parallel axes", message, StringComparison.Ordinal);
        // The abolished partition rule must not be quoted back at the operator ever again.
        Assert.DoesNotContain("must sum to the line amount", message, StringComparison.Ordinal);

        // Re-allocating each axis in FULL — the remediation the message now describes — is accepted.
        var line = open.Entry.Lines.Single(l => l.SelectedLedger!.Id == travel.Id);
        line.CostAllocations[0].AmountText = "5000";
        line.CostAllocations[1].AmountText = "5000";
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);
    }

    /// <summary>
    /// 🔴 <b>A forex amount finer than two decimal places is refused AT THE KEYBOARD</b> (finding L2-03). Before
    /// this guard it posted, passed <c>VoucherValidator.EnsureForexValid</c> and SAVED — SQLite carries the
    /// magnitude at 1,000,000 scale — and then <c>CanonicalXml.Export</c> threw "is not paisa-exact", because the
    /// canonical model carries <c>ForexAmountPaisa</c> at two places. That is a company the app itself produced and
    /// cannot export, and Export Data → XML is the only door out of it. The base amount cannot catch it:
    /// <c>RecomputeForexBase</c> snaps forex × rate to the paisa, so the derived line amount is paisa-exact however
    /// fine the forex figure is.
    /// </summary>
    [Fact]
    public void A_forex_amount_finer_than_a_paisa_is_refused_before_it_can_be_posted()
    {
        using var book = AlterationBook.New("fxsubpaisa");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        var entry = book.Entry(VoucherBaseType.Purchase);
        entry.Lines[0].SelectedLedger = purchases;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[1].SelectedLedger = creditor;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].ForexAmountText = "1234.567";     // THREE places
        entry.Lines[1].ForexRateText = "83.25";
        entry.Lines[0].AmountText = entry.Lines[1].ParsedAmount
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        entry.Recalculate();

        Assert.False(entry.Accept());
        Assert.Contains("1234.567", entry.Message!, StringComparison.Ordinal);
        Assert.Contains("USD", entry.Message!, StringComparison.Ordinal);
        Assert.Empty(book.Company.Vouchers);

        // Two places posts, saves AND exports — the guard is a floor on precision, not a ban on forex.
        entry.Lines[1].ForexAmountText = "1234.56";
        entry.Lines[0].AmountText = entry.Lines[1].ParsedAmount
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        entry.Recalculate();
        Assert.True(entry.Accept(), entry.Message);
        Assert.NotEmpty(book.Export());
    }

    /// <summary>
    /// The <c>ApplicableUpto</c>-before-the-date guard in <c>AcceptAlteration</c> (finding L2-11, item F08 — whose
    /// stated reason this corrects: the guard is not unreachable, it runs BEFORE the replacement is built and long
    /// before <c>Replace</c> sees anything. It was simply untested, so deleting it left the suite green while the
    /// operator's message became an engine sentence about a provisional-state change instead).
    /// </summary>
    [Fact]
    public void An_applicable_upto_before_the_voucher_date_is_refused_by_the_screen_not_by_the_engine()
    {
        using var book = AlterationBook.New("uptoback");
        var dr = book.Ledger("Accrued Expense", "Indirect Expenses");
        var cr = book.Ledger("Accrual Liability", "Current Liabilities");

        var posted = book.Post(VoucherBaseType.ReversingJournal, book.On(10),
            new[] { (dr, DrCr.Debit, "3000.33"), (cr, DrCr.Credit, "3000.33") },
            configure: e => e.ApplicableUptoText = ApexDate.Format(book.On(45)));

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        open.Entry!.ApplicableUptoText = ApexDate.Format(book.On(2));   // BEFORE the voucher date

        Assert.False(open.Entry.AcceptAlteration());
        Assert.Equal("Applicable Upto must be on or after the voucher date.", open.Entry.Message);
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// Blanking a line away until only one remains is refused by the screen's own sentence, not by the engine's
    /// unbalanced exception (finding L2-11, item F06 — untested, so the guard survived deletion).
    /// </summary>
    [Fact]
    public void An_alteration_left_with_a_single_line_is_refused_by_name()
    {
        using var book = AlterationBook.New("oneline");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 1234.56m);

        var before = book.Export();
        var open = book.ForAlter(posted.Id);
        var entry = open.Entry!;
        entry.Lines[1].SelectedLedger = null;
        entry.Lines[1].AmountText = string.Empty;        // now BLANK, so it is dropped rather than half-filled
        entry.Recalculate();

        Assert.False(entry.AcceptAlteration());
        Assert.Equal("A voucher needs at least two lines.", entry.Message);
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// The two things a successful alteration owes its CALLER (finding L2-11, items F25 and C24): the navigation
    /// callback fires, and <c>SavedNumber</c> holds the number the replacement kept. Both survived deletion because
    /// nothing asserted them, and the shell reads both.
    /// </summary>
    [Fact]
    public void A_successful_alteration_notifies_its_caller_and_records_the_saved_number()
    {
        using var book = AlterationBook.New("onsaved");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 8888.88m);

        var saved = 0;
        var open = VoucherEntryViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => saved++, onCancelled: () => { });

        Assert.Equal(0, saved);
        Assert.True(open.Entry!.AcceptAlteration(), open.Entry.Message);
        Assert.Equal(1, saved);
        Assert.Equal(posted.Number, open.Entry.SavedNumber);
    }

    /// <summary>
    /// The rehydrated screen is RECALCULATED and its panels re-summarised before the operator sees it (finding
    /// L2-11, items B09, B41 and B42). All three survived deletion: nothing asserted the totals or the panel
    /// summary strings after a rehydration, so a screen that opened showing ₹0.00 totals under correctly-filled
    /// lines would have shipped green.
    /// </summary>
    [Fact]
    public void The_rehydrated_screen_shows_the_posted_totals_and_its_panel_summaries()
    {
        using var book = AlterationBook.New("recalc");
        var (branch, kolkata) = book.CostAxis("Branch", "Kolkata");
        var debtor = book.Ledger("Summary Customer", "Sundry Debtors", billWise: true);
        var travel = book.Ledger("Travel", "Indirect Expenses", costApplicable: true);

        var posted = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (travel, DrCr.Debit, "5000.55"), (debtor, DrCr.Credit, "5000.55") },
            configure: e =>
            {
                var cost = e.Lines[0].CostAllocations[0];
                cost.SelectedCategory = branch;
                cost.SelectedCentre = kolkata;
                cost.AmountText = "5000.55";
                var bill = e.Lines[1].BillAllocations[0];
                bill.Name = "REF-9";
                bill.AmountText = "5000.55";
            });

        var entry = book.ForAlter(posted.Id).Entry!;

        // The final Recalculate: the totals are the voucher's, not a blank screen's.
        Assert.Contains("5,000.55", entry.TotalDebitText, StringComparison.Ordinal);
        Assert.Contains("5,000.55", entry.TotalCreditText, StringComparison.Ordinal);
        // …and each panel re-summarised itself from the rows the rehydration put in it.
        Assert.Contains("fully allocated", entry.Lines[0].CostSummary, StringComparison.Ordinal);
        Assert.Contains("fully allocated", entry.Lines[1].BillSummary, StringComparison.Ordinal);
    }
}
