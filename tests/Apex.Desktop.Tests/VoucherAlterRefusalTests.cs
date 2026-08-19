using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Tests.Fixtures;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S5b — EVERY NAMED REFUSAL, one test each.</b>
///
/// <para>🔴 <b>Why a refusal needs a test at all.</b> ORCHESTRATOR RULING 1: <i>"a silent no-op is the failure
/// mode being avoided"</i> — and a test that asserts "nothing happened" passes for a silent no-op too. So each
/// test below asserts the SENTENCE, not merely that the screen did not open: the family is named, the reason is
/// stated, and the message is non-empty. Design §6.6a.3 is the enumeration these implement.</para>
///
/// <para>🔴 <b>AND THE FIVE UNTAGGED FAMILIES ARE THE POINT OF THIS FILE.</b> §6.6a.6 answer 1 proves that a
/// filter looking only for <c>EntryLine.Gst</c>/<c>.Tds</c>/<c>.Tcs</c> is INSUFFICIENT: an advance refund, an
/// advance adjustment, a GOODS advance receipt, a §34 credit/debit note and the statutory-flagged types all carry
/// no tagged line and still fail to round-trip, because <c>Accept</c> had an OFF-LINE side effect. Those five
/// tests are the ones that would go green against a tag-only predicate and must not.</para>
/// </summary>
public sealed class VoucherAlterRefusalTests
{
    // ================================================================ helpers

    /// <summary>Posts a plain balanced two-leg voucher of <paramref name="type"/> straight through the engine —
    /// the door a SERVICE uses, and the only door available for a type no entry screen keys.</summary>
    private static Voucher PostRaw(
        AlterationBook book, VoucherType type, IEnumerable<EntryLine> lines,
        DateOnly? date = null, bool isAccountingInvoice = false,
        DateOnly? applicableUpto = null)
    {
        var voucher = new Voucher(
            Guid.NewGuid(), type.Id, date ?? book.On(), lines,
            applicableUpto: applicableUpto,
            isAccountingInvoice: isAccountingInvoice);
        var posted = new LedgerService(book.Company).Post(voucher);
        book.Storage.Save(book.Company);
        return posted;
    }

    private static (DomainLedger Dr, DomainLedger Cr) OrdinaryPair(AlterationBook book, string tag)
    {
        var dr = book.Ledger("Dr " + tag, "Indirect Expenses");
        var cr = book.Ledger("Cr " + tag, "Indirect Incomes");
        return (dr, cr);
    }

    private static EntryLine[] PlainLegs(DomainLedger dr, DomainLedger cr, decimal amount) => new[]
    {
        new EntryLine(dr.Id, new Money(amount), DrCr.Debit),
        new EntryLine(cr.Id, new Money(amount), DrCr.Credit),
    };

    private static VoucherType AddType(
        AlterationBook book, string name, VoucherBaseType baseType,
        bool useForPos = false, bool isStatPayment = false, bool isRcmPaymentVoucher = false,
        bool isGstStatAdjustment = false, bool useAsManufacturingJournal = false,
        bool allowConsumption = false)
    {
        var type = new VoucherType(
            Guid.NewGuid(), name, baseType,
            useAsManufacturingJournal: useAsManufacturingJournal,
            useForPos: useForPos,
            allowConsumption: allowConsumption,
            isStatPayment: isStatPayment,
            isRcmPaymentVoucher: isRcmPaymentVoucher,
            isGstStatAdjustment: isGstStatAdjustment);
        book.Company.AddVoucherType(type);
        return type;
    }

    /// <summary>Asserts the alteration was refused, that the sentence is real, and that it names
    /// <paramref name="mustMention"/> — so a family cannot be refused by somebody ELSE'S message.</summary>
    private static string AssertRefused(AlterationBook book, Guid voucherId, params string[] mustMention)
    {
        var open = book.ForAlter(voucherId);
        Assert.True(open.IsRefused, "Expected a refusal; the alteration screen opened instead.");
        Assert.Null(open.Entry);
        Assert.False(string.IsNullOrWhiteSpace(open.Refusal));
        foreach (var fragment in mustMention)
            Assert.Contains(fragment, open.Refusal!, StringComparison.OrdinalIgnoreCase);
        return open.Refusal!;
    }

    // ================================================================ (A) architectural — the inventory aggregate

    /// <summary>
    /// §6.6a.4 — all twelve inventory base kinds post an <c>InventoryVoucher</c> into a DIFFERENT list on
    /// <see cref="Company"/>, which <c>LedgerService.Replace</c> cannot see. The refusal is ARCHITECTURAL: their
    /// posted lines mostly DO equal their keyed lines, so a future slice could serve them cheaply once an
    /// <c>InventoryPostingService</c> counterpart of <c>Replace</c> exists.
    ///
    /// <para>🔴 <b>The assertions moved with the message</b> (finding L3-07). They used to require the words
    /// "inventory aggregate", which is the mechanism — and the sentence around them also handed an accountant a
    /// class name (<c>LedgerService.Replace</c>), the design record's own phrase "the refusal is architectural, not
    /// a judgement about its shape", and a promise about an unbuilt verb. The mechanism now lives in the code
    /// comment; what an operator reads names the family, the reason and the screen to go to, and THAT is what is
    /// asserted here.</para>
    /// </summary>
    [Fact]
    public void An_inventory_aggregate_voucher_is_refused_and_the_message_names_the_screen_to_use()
    {
        var company = PopulatedCompanyFixture.BuildRegular();
        var inventoryVoucher = company.InventoryVouchers.First();

        using var scratch = new ScratchStorage();
        var open = VoucherEntryViewModel.ForAlter(
            company, inventoryVoucher.Id, scratch.Storage, () => { }, () => { });

        Assert.True(open.IsRefused);
        Assert.Contains("inventory voucher", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inventory voucher screen", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        // No internals in a sentence an accountant reads.
        Assert.DoesNotContain("Replace", open.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain("aggregate", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("architectural", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every one of the twelve inventory base kinds the fixture posts is refused — not just the first.</summary>
    [Fact]
    public void Every_inventory_base_kind_in_the_populated_fixture_is_refused()
    {
        var company = PopulatedCompanyFixture.BuildRegular();
        using var scratch = new ScratchStorage();

        var seen = new HashSet<VoucherBaseType>();
        foreach (var iv in company.InventoryVouchers)
        {
            var open = VoucherEntryViewModel.ForAlter(company, iv.Id, scratch.Storage, () => { }, () => { });
            Assert.True(open.IsRefused);
            Assert.False(string.IsNullOrWhiteSpace(open.Refusal));
            if (company.FindVoucherType(iv.TypeId) is { } t) seen.Add(t.BaseType);
        }

        Assert.NotEmpty(seen);
        Assert.All(seen, b => Assert.True(VoucherEffects.IsInventoryBaseType(b)));
    }

    /// <summary>An id that is in neither aggregate is refused with a sentence about the BOOK, not a bare null.</summary>
    [Fact]
    public void An_unknown_voucher_id_is_refused_by_name_rather_than_returning_null()
    {
        using var book = AlterationBook.New("unknown");
        var open = book.ForAlter(Guid.NewGuid());
        Assert.True(open.IsRefused);
        Assert.Contains("no longer in this company's books", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ (B) the specialised TYPE flags (§6.6a.2)

    /// <summary>Row 19 — POS. Reachable by <c>Replace</c> (the POS screen posts into <c>Company.Vouchers</c>), so
    /// the refusal must be EXPLICIT rather than architectural.</summary>
    [Fact]
    public void A_POS_bill_is_refused_by_name()
    {
        using var book = AlterationBook.New("pos");
        var (dr, cr) = OrdinaryPair(book, "pos");
        var posType = AddType(book, "POS Till", VoucherBaseType.Sales, useForPos: true);

        var posted = PostRaw(book, posType, PlainLegs(dr, cr, 1234.56m));
        AssertRefused(book, posted.Id, "POS bill", "tender");
    }

    /// <summary>§6.6a.4's correction to §6.6 — the Manufacturing Journal is the FOURTH InventoryVoucher entry
    /// screen, and it is named before the base switch so an operator reads the screen they came from.</summary>
    [Fact]
    public void A_Manufacturing_Journal_type_is_refused_by_name_before_the_base_switch()
    {
        using var book = AlterationBook.New("mfg");
        var (dr, cr) = OrdinaryPair(book, "mfg");
        var mfgType = new VoucherType(
            Guid.NewGuid(), "Manufacturing Journal", VoucherBaseType.StockJournal,
            affectsAccounts: true, affectsStock: false, useAsManufacturingJournal: true);
        book.Company.AddVoucherType(mfgType);

        var posted = PostRaw(book, mfgType, PlainLegs(dr, cr, 999.99m));
        var message = AssertRefused(book, posted.Id, "Manufacturing Journal", "components");
        // The type-flag arm ran, not the base-kind arm: the message names the SCREEN, not the aggregate.
        Assert.DoesNotContain("Replace does not reach", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Row 5 — the GST/TDS/TCS challan Payment: posted by a deposit service, with a frozen challan.</summary>
    [Fact]
    public void A_statutory_deposit_Payment_type_is_refused_by_name()
    {
        using var book = AlterationBook.New("statpay");
        var (dr, cr) = OrdinaryPair(book, "statpay");
        var statType = AddType(book, "GST Challan Payment", VoucherBaseType.Payment, isStatPayment: true);

        var posted = PostRaw(book, statType, PlainLegs(dr, cr, 5000.05m));
        AssertRefused(book, posted.Id, "statutory deposit", "challan");
    }

    /// <summary>Row 6 — the Rule-52 RCM payment voucher: a Payment base kind wearing a flag, with its own
    /// document series.</summary>
    [Fact]
    public void An_RCM_payment_voucher_type_is_refused_by_name()
    {
        using var book = AlterationBook.New("rcmpay");
        var (dr, cr) = OrdinaryPair(book, "rcmpay");
        var rcmType = AddType(book, "RCM Payment Voucher", VoucherBaseType.Payment, isRcmPaymentVoucher: true);

        var posted = PostRaw(book, rcmType, PlainLegs(dr, cr, 700.70m));
        AssertRefused(book, posted.Id, "reverse-charge", "Rule 52");
    }

    /// <summary>Row 14 — Rule-88A set-off / ITC reversal, computed for a return period by the GST engine.</summary>
    [Fact]
    public void A_GST_statutory_adjustment_Journal_type_is_refused_by_name()
    {
        using var book = AlterationBook.New("gstadj");
        var (dr, cr) = OrdinaryPair(book, "gstadj");
        var adjType = AddType(book, "GST Set-off", VoucherBaseType.Journal, isGstStatAdjustment: true);

        var posted = PostRaw(book, adjType, PlainLegs(dr, cr, 8888.88m));
        AssertRefused(book, posted.Id, "statutory adjustment", "Re-run the period");
    }

    /// <summary>Row 30 — Payroll, refused on the base kind.</summary>
    [Fact]
    public void A_Payroll_voucher_is_refused_by_name()
    {
        using var book = AlterationBook.New("payroll");
        var (dr, cr) = OrdinaryPair(book, "payroll");
        var payrollType = book.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payroll);

        var posted = PostRaw(book, payrollType, PlainLegs(dr, cr, 45678.90m));
        AssertRefused(book, posted.Id, "payroll voucher", "employee");
    }

    /// <summary>…and on the LINE detail too, so a payroll line hiding under another base kind is still caught
    /// (the <c>EntryLine</c> constructor enforces <c>payroll.Amount == amount</c>, which the Dr/Cr grid cannot
    /// express).</summary>
    [Fact]
    public void A_voucher_carrying_a_payroll_line_detail_is_refused_even_off_the_Payroll_base_kind()
    {
        using var book = AlterationBook.New("payrollline");
        var (dr, cr) = OrdinaryPair(book, "payrollline");
        var employee = Guid.NewGuid();
        var payHead = Guid.NewGuid();
        var amount = new Money(2222.22m);

        var posted = PostRaw(book, book.Type(VoucherBaseType.Journal), new[]
        {
            new EntryLine(dr.Id, amount, DrCr.Debit,
                payroll: new PayrollLineDetail(employee, payHead, PayrollLineCategory.Earning, amount)),
            new EntryLine(cr.Id, amount, DrCr.Credit),
        });

        AssertRefused(book, posted.Id, "payroll voucher");
    }

    // ================================================================ (C) 🔴 THE FIVE UNTAGGED FAMILIES (§6.6a.6)

    /// <summary>
    /// Row 9 — 🔴 <b>a GOODS advance receipt: the second hole in the tag filter.</b> The advance is de-taxed
    /// (Notification 66/2017), so the engine appends NO tax lines at all: posted lines genuinely equal keyed lines
    /// and the voucher passes EVERY tag test — while <c>gst_advance_receipts.receipt_voucher_id</c> is
    /// <c>NOT NULL REFERENCES vouchers(id)</c> and still points at it.
    /// </summary>
    [Fact]
    public void A_goods_advance_receipt_is_refused_although_it_carries_no_tagged_line()
    {
        using var book = AlterationBook.New("advgoods");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var debtor = book.Ledger("Advance Customer", "Sundry Debtors");

        var posted = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "50000.50"), (debtor, DrCr.Credit, "50000.50") });

        // Exactly what AdvanceReceiptService.BuildAdvanceReceipt leaves behind for a GOODS advance: a record, and
        // not one tagged line anywhere on the voucher.
        book.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), posted.Id, isService: false, new Money(50000.50m), rateBasisPoints: 0,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: Money.Zero));

        Assert.All(posted.Lines, l => Assert.False(l.HasGst || l.HasTds || l.HasTcs));
        AssertRefused(book, posted.Id, "GOODS supply", "de-taxed", "advance record");
    }

    /// <summary>Row 8 — the SERVICE advance receipt. Its Output legs are tagged, so the tag filter would catch it
    /// too — but the suspense debit and the record registration would not have been.</summary>
    [Fact]
    public void A_service_advance_receipt_is_refused_by_its_own_name()
    {
        using var book = AlterationBook.New("advservice");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var debtor = book.Ledger("Advance Client", "Sundry Debtors");

        var posted = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "11800.10"), (debtor, DrCr.Credit, "11800.10") });

        book.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), posted.Id, isService: true, new Money(10000.10m), rateBasisPoints: 1800,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: new Money(1800.02m)));

        AssertRefused(book, posted.Id, "SERVICE supply", "Rule 50");
    }

    /// <summary>
    /// Row 4 — 🔴 <b>an advance REFUND on a Payment: the first hole in the tag filter.</b>
    /// <c>AdvanceReceiptService.BuildAdvanceReversalPair</c>'s own doc comment states the reversal legs carry NO
    /// <c>GstLineTax</c>, and <c>Refund</c> REPLACES the record on the company.
    /// </summary>
    [Fact]
    public void An_advance_refund_Payment_is_refused_although_it_carries_no_tagged_line()
    {
        using var book = AlterationBook.New("advrefund");
        var cash = book.Company.FindLedgerByName("Cash")!;
        var debtor = book.Ledger("Refunded Client", "Sundry Debtors");
        var receipt = book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "11800.10"), (debtor, DrCr.Credit, "11800.10") });

        var refund = book.Post(VoucherBaseType.Payment, book.On(6),
            new[] { (debtor, DrCr.Debit, "11800.10"), (cash, DrCr.Credit, "11800.10") });

        book.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), receipt.Id, isService: true, new Money(10000.10m), rateBasisPoints: 1800,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: new Money(1800.02m),
            refundVoucherId: refund.Id));

        Assert.All(refund.Lines, l => Assert.False(l.HasGst || l.HasTds || l.HasTcs));
        AssertRefused(book, refund.Id, "refunds a GST advance", "Rule 51");
    }

    /// <summary>
    /// Row 13 — 🔴 <b>an advance ADJUSTMENT on a Journal: the third hole — POSTED THROUGH THE REAL SCREENS.</b>
    ///
    /// <para>🔴 <b>THIS TEST REPLACES A DOCTORED ONE</b> (finding L1-01, a measured BLOCKER). The version that
    /// shipped hand-built <c>new GstAdvanceReceipt(…, adjustedAgainstInvoiceVoucherId: adjustment.Id)</c> — the
    /// JOURNAL's id in a field <c>AdvanceReceiptService.AdjustAgainstInvoice</c> only ever fills with the SALES
    /// INVOICE's id — and then asserted the refusal fired. It is a shape the screen cannot produce, so it proved a
    /// refusal that never fired on anything real: the actual adjusting Journal opened, and deleting its
    /// engine-built release pair declared the advance's output tax a SECOND time and stranded the suspense while
    /// the record still read adjusted. This file's own charter forbids exactly that fixture
    /// (<see cref="AlterationBook"/>: "a hand-built Voucher would let a test construct a shape the screen cannot
    /// produce and then 'prove' the inverse works on it"), so the whole family now goes out through Accept.</para>
    /// </summary>
    [Fact]
    public void An_advance_adjustment_Journal_is_refused_by_the_suspense_release_it_carries()
    {
        using var book = AlterationBook.New("advadjust");
        var (_, invoice, adjustment) = PostServiceAdvanceAndAdjustIt(book);

        // The whole point of the row: not one tagged line anywhere on the adjusting journal…
        Assert.All(adjustment.Lines, l => Assert.False(l.HasGst || l.HasTds || l.HasTcs));
        // …but the engine's untagged release pair IS there, and that is what selects it.
        var suspense = book.Company.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;
        Assert.Contains(adjustment.Lines, l => l.LedgerId == suspense.Id && l.Side == DrCr.Credit);

        AssertRefused(book, adjustment.Id, "advance-tax suspense", "Output Tax on Advances", "stand twice");

        // And the invoice is refused by ITS OWN sentence, not by the journal's — the mis-worded arm this replaces
        // aimed the "This journal releases a GST advance…" message at exactly this voucher.
        var invoiceRefusal = AssertRefused(book, invoice.Id, "released against this invoice", "11B");
        Assert.DoesNotContain("This journal", invoiceRefusal, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Row 16b — 🔴 <b>the SALES INVOICE a GST advance was released against</b> (finding L3-05). A plain-grid Sales
    /// voucher is otherwise SIMPLE (row 16a), and its own §6.6a evidence audits only what <c>Accept</c> APPENDS —
    /// which misses the three roles it plays in records OTHER vouchers created. This is the one that matters:
    /// <c>AdjustAgainstInvoice</c> re-read this invoice's posted taxable value to prove the advance was fully
    /// consumed, and froze the GSTR-1 11B figures against it.
    /// </summary>
    [Fact]
    public void The_sales_invoice_an_advance_was_released_against_is_refused_by_name()
    {
        using var book = AlterationBook.New("advanchor");
        var (advance, invoice, _) = PostServiceAdvanceAndAdjustIt(book);

        Assert.Equal(invoice.Id, book.Company.FindAdvanceReceipt(advance.Id)!.AdjustedAgainstInvoiceVoucherId);
        AssertRefused(book, invoice.Id, "advance was released against this invoice", "11B", "taxable value");
    }

    /// <summary>
    /// 🔴 <b>The declared LIMIT of the shape proxy, measured rather than asserted</b> (finding L3-04). A GOODS
    /// advance is de-taxed (Notn 66/2017), so <c>BuildAdvanceReversalPair</c> returns <c>Array.Empty</c> and its
    /// adjusting Journal carries NO engine line at all — the suspense ledger is never even created. The row-13 arm
    /// therefore cannot see it, and it does not need to: with nothing appended and no record mutation on the
    /// alteration path (<c>AcceptAlteration</c> never calls <c>AdjustAgainstInvoice</c>), the posted lines ARE the
    /// keyed lines. This test measures that, so the gap is a recorded fact and not a hope.
    /// </summary>
    [Fact]
    public void A_goods_advance_adjustment_Journal_opens_and_the_record_survives_the_alteration()
    {
        using var book = AlterationBook.New("advgoodsadj");
        book.EnableGst();
        var cash = book.Company.FindLedgerByName("Cash")!;
        var advLedger = book.Ledger("Advance from customer", "Current Liabilities");
        var customer = book.Ledger("Goods Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "50000.50"), (advLedger, DrCr.Credit, "50000.50") },
            configure: e =>
            {
                e.IsAdvanceReceipt = true;
                e.AdvanceIsService = false;          // GOODS — de-taxed, so no tax pair and no suspense ledger
                e.AdvanceAmountText = "50000.50";
            });
        Assert.Null(book.Company.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName));

        var invoice = book.Post(VoucherBaseType.Sales, book.On(3),
            new[] { (customer, DrCr.Debit, "50000.50"), (sales, DrCr.Credit, "50000.50") });

        var advance = book.Company.AdvanceReceipts.Single();
        var journal = book.Post(VoucherBaseType.Journal, book.On(6),
            new[] { (advLedger, DrCr.Debit, "50000.50"), (customer, DrCr.Credit, "50000.50") },
            configure: e =>
            {
                e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
                e.SelectedAdvanceInvoice = e.AdvanceInvoices.Single(o => o.Invoice?.Id == invoice.Id);
            });

        Assert.Equal(2, journal.Lines.Count);          // NOTHING was appended
        var settled = book.Company.AdvanceReceipts.Single();
        Assert.Equal(invoice.Id, settled.AdjustedAgainstInvoiceVoucherId);

        var open = book.ForAlter(journal.Id);
        Assert.Null(open.Refusal);
        Assert.True(open.Entry!.AcceptAlteration(), open.Entry.Message);
        // The record is untouched by Replace, which is the whole reason this shape needs no refusal.
        Assert.Equal(invoice.Id, book.Company.AdvanceReceipts.Single().AdjustedAgainstInvoiceVoucherId);
    }

    /// <summary>
    /// Books a ₹10,000 net inter-state SERVICE advance through the Receipt screen, posts the tax invoice that fully
    /// consumes it, and adjusts the advance against that invoice through the Journal screen — every step through
    /// the door the product uses. Returns (the advance record, the invoice, the adjusting journal).
    /// </summary>
    private static (GstAdvanceReceipt Advance, Voucher Invoice, Voucher Adjustment)
        PostServiceAdvanceAndAdjustIt(AlterationBook book)
    {
        book.EnableGst();
        var cash = book.Company.FindLedgerByName("Cash")!;
        var advLedger = book.Ledger("Advance from customer", "Current Liabilities");
        var customer = book.Ledger("Acme Ltd", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        book.Post(VoucherBaseType.Receipt, book.On(),
            new[] { (cash, DrCr.Debit, "11800.59"), (advLedger, DrCr.Credit, "11800.59") },
            configure: e =>
            {
                e.IsAdvanceReceipt = true;
                e.AdvanceIsService = true;
                e.AdvanceAmountText = "10000.50";   // 18% of 10,000.50 = 1,800.09 exactly (the house odd-value rule)
                e.AdvanceInterState = true;
            });
        var advance = book.Company.AdvanceReceipts.Single();

        // The tax invoice that fully consumes the advance. Posted through the engine with its GST stamps, exactly
        // as the shipped advance-engine tests do: the adjustment's full-consumption guard reads
        // GstReportSupport.InvoiceTaxableValue off the STAMPED lines, so an untagged invoice cannot serve here.
        var outputIgst = new GstService(book.Company)
            .FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var invoice = new LedgerService(book.Company).Post(new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Sales).Id, book.On(3),
            new[]
            {
                new EntryLine(customer.Id, new Money(11800.59m), DrCr.Debit),
                new EntryLine(sales.Id, new Money(10000.50m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(10000.50m))),
                new EntryLine(outputIgst.Id, new Money(1800.09m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(10000.50m))),
            },
            partyId: customer.Id));
        book.Storage.Save(book.Company);

        var adjustment = book.Post(VoucherBaseType.Journal, book.On(6),
            new[] { (advLedger, DrCr.Debit, "11800.59"), (customer, DrCr.Credit, "11800.59") },
            configure: e =>
            {
                e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
                e.SelectedAdvanceInvoice = e.AdvanceInvoices.Single(o => o.Invoice?.Id == invoice.Id);
            });

        return (advance, invoice, adjustment);
    }

    /// <summary>
    /// Rows 25 / 27 — 🔴 <b>a §34 credit or debit note: the fourth hole.</b> <c>RegisterSection34Link</c> mints a
    /// fresh <c>Guid.NewGuid()</c> link on EVERY accept, so a re-accept would leave one note carrying TWO links —
    /// and the note carries no tagged line, so nothing in a tag filter sees it.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.CreditNote, CdnType.Credit)]
    [InlineData(VoucherBaseType.DebitNote, CdnType.Debit)]
    public void A_section34_note_is_refused_although_it_carries_no_tagged_line(
        VoucherBaseType baseType, CdnType cdnType)
    {
        using var book = AlterationBook.New("s34_" + baseType);
        var party = book.Ledger("S34 Party", "Sundry Debtors");
        var revenue = book.Ledger("S34 Revenue", "Sales Accounts");

        var note = book.Post(baseType, book.On(),
            new[] { (revenue, DrCr.Debit, "5000.55"), (party, DrCr.Credit, "5000.55") });

        book.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, cdnType, originalInvoiceVoucherId: null,
            originalInvoiceNumber: "INV-1", originalInvoiceDate: book.On(1), reasonCode: "01"));

        Assert.All(note.Lines, l => Assert.False(l.HasGst || l.HasTds || l.HasTcs));
        AssertRefused(book, note.Id, "§34", "two links");
    }

    /// <summary>The fifth family's defensive twin: a challan link on ANY voucher, not just a stat-payment TYPE.
    /// The link is keyed on (ChallanId, VoucherId) and nothing structurally confines it to that flag.</summary>
    [Fact]
    public void A_voucher_linked_to_a_challan_is_refused_even_off_a_stat_payment_type()
    {
        using var book = AlterationBook.New("challan");
        var posted = book.PostPlainPair(VoucherBaseType.Payment, 3300.33m);
        book.Company.LinkChallanToVoucher(Guid.NewGuid(), posted.Id);

        AssertRefused(book, posted.Id, "challan", "reconciliation");
    }

    /// <summary>
    /// 🔴 <b>The TCS half of the same defence</b> (finding L2-07). Only the TDS collection was ever exercised:
    /// deleting the <c>TcsChallanVoucherLinks</c> clause survived the whole S5b suite, so half a defence shipped
    /// unlocked. Nothing structural confines a TCS challan link to a stat-payment TYPE either.
    /// </summary>
    [Fact]
    public void A_voucher_linked_to_a_TCS_challan_is_refused_even_off_a_stat_payment_type()
    {
        using var book = AlterationBook.New("tcschallan");
        var posted = book.PostPlainPair(VoucherBaseType.Payment, 4400.44m);
        book.Company.LinkTcsChallanToVoucher(Guid.NewGuid(), posted.Id);

        AssertRefused(book, posted.Id, "challan", "reconciliation");
    }

    /// <summary>
    /// 🔴 <b>And the two GST challan collections, which had no arm at all</b> (finding L1-05). The shipped comment
    /// justified the defensive twin on the ground that "the link is keyed on (ChallanId, VoucherId) and nothing
    /// structurally confines it to a stat-payment TYPE" — an argument that covers <c>GstChallan.VoucherId</c> and
    /// <c>GstDrc03.VoucherId</c> word for word. <c>GstDepositService</c> creates both against the stat-payment type
    /// today, but <c>ImportPlan</c> builds both straight from canonical XML with an arbitrary voucher id, which is
    /// the same reachability argument that justified the TDS/TCS twin in the first place.
    /// </summary>
    [Fact]
    public void A_voucher_named_by_a_GST_challan_is_refused_even_off_a_stat_payment_type()
    {
        using var book = AlterationBook.New("gstchallan");
        var posted = book.PostPlainPair(VoucherBaseType.Payment, 5500.55m);
        book.Company.AddGstChallan(new GstChallan(
            Guid.NewGuid(), cpin: "12345678901234", cin: null, brn: null, depositDate: book.On(),
            majorHead: GstTaxHead.Integrated, minorHead: GstMinorHead.Tax, amount: new Money(5500.55m),
            voucherId: posted.Id));

        AssertRefused(book, posted.Id, "challan", "reconciliation");
    }

    /// <summary>The DRC-03 twin — a nullable voucher id, and the same import reachability (finding L1-05).</summary>
    [Fact]
    public void A_voucher_named_by_a_GST_DRC03_is_refused_even_off_a_stat_payment_type()
    {
        using var book = AlterationBook.New("drc03");
        var posted = book.PostPlainPair(VoucherBaseType.Payment, 6600.66m);
        book.Company.AddGstDrc03(new GstDrc03(
            Guid.NewGuid(), drc03Ref: "AD2701240000001", cause: "voluntary", period: "2024-06",
            cgstPaisa: 0, sgstPaisa: 0, igstPaisa: 660066, cessPaisa: 0, interestPaisa: 0,
            drc03aDemandRef: null, voucherId: posted.Id, createdAt: DateTimeOffset.UnixEpoch));

        AssertRefused(book, posted.Id, "challan", "reconciliation");
    }

    // ================================================================ (D) ORCHESTRATOR RULING 2 — the live IRN

    /// <summary>A <c>Generated</c> e-invoice refuses the alteration: an IRN cannot be re-derived, and the app's
    /// only content check compares the document NUMBER, which an amount-only amendment leaves untouched.</summary>
    [Fact]
    public void A_voucher_carrying_a_live_IRN_is_refused_by_name()
    {
        using var book = AlterationBook.New("irn");
        var debtor = book.Ledger("E-Invoiced Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "77000.70"), (sales, DrCr.Credit, "77000.70") });

        book.Company.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), posted.Id, "SL/1", EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112233", ackDate: book.On(1),
            signedQr: null, signedJson: null, cancelledOn: null, cancelReasonCode: null));

        AssertRefused(book, posted.Id, "live IRN", "Cancel the IRN");
    }

    /// <summary>…and a <c>Pending</c> e-invoice does NOT refuse — it was never sent to the portal, so the design's
    /// disposition is warn-and-proceed. A refusal that was too broad would block ordinary corrections.</summary>
    [Fact]
    public void A_voucher_carrying_only_a_pending_e_invoice_still_opens()
    {
        using var book = AlterationBook.New("irnpending");
        var debtor = book.Ledger("Pending Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "1000.10"), (sales, DrCr.Credit, "1000.10") });

        book.Company.AddEInvoiceRecord(new EInvoiceRecord(Guid.NewGuid(), posted.Id, "SL/1"));

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
    }

    // ================================================================ (E) the two invoice entry modes

    /// <summary>Rows 17 / 22 — the item invoice: derived legs, one posted line PER BATCH, and a posted rate
    /// already net of the price-level discount that <c>VoucherInventoryLine</c> has no field to carry back.</summary>
    [Fact]
    public void An_item_invoice_is_refused_by_name()
    {
        using var book = AlterationBook.New("iteminv");
        var voucher = new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Sales).Id, book.On(),
            PlainLegs(book.Ledger("Item Dr", "Sundry Debtors"), book.Ledger("Item Cr", "Sales Accounts"), 100m),
            inventoryLines: new[]
            {
                new VoucherInventoryLine(Guid.NewGuid(), Guid.NewGuid(), 2m, new Money(50m)),
            });

        // The predicate is asserted directly: posting an item invoice needs stock masters and the pairing
        // invariant, and neither is what this refusal is about.
        var refusal = VoucherAlterationEligibility.RefusalFor(
            book.Company, voucher, book.Type(VoucherBaseType.Sales));

        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("ITEM INVOICE", refusal!, StringComparison.Ordinal);
        Assert.Contains("batch", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discount", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 Row 18 — <b>refused BY NAME, and the name changed</b> (finding L1-04, a PREMISE CORRECTION). The row
    /// shipped as UNDETERMINED with a refusal that said the round trip "has not been measured". It has been
    /// measured since — lift this arm, point <c>SeedAlterationMode</c> at the plain grid, and a wholly exempt Sales
    /// accounting invoice round-trips BYTE-IDENTICALLY in memory and on disk, with the flag, the party id and
    /// GSTR-1's exempt value all intact. The refusal survives on a different ground, which is the one this test now
    /// asserts: the party leg's DERIVED STATUS is what cannot be recovered. On the plain grid it becomes an
    /// ordinary editable row, nothing re-derives it, and the party total can therefore be moved off the sum of the
    /// service rows and still balance — an EDIT hazard, not a round-trip one.
    /// </summary>
    [Fact]
    public void A_Sales_accounting_invoice_with_no_tax_line_is_refused_for_its_derived_party_leg()
    {
        using var book = AlterationBook.New("acctinv-sales");
        var debtor = book.Ledger("Service Customer", "Sundry Debtors");
        var income = book.Ledger("Consulting Income", "Sales Accounts");

        // The zero-rated / exempt shape exactly: an accounting invoice carrying NOT ONE tax line.
        var posted = PostRaw(book, book.Type(VoucherBaseType.Sales),
            PlainLegs(income, debtor, 25000.25m), isAccountingInvoice: true);
        Assert.True(posted.IsAccountingInvoice);
        Assert.All(posted.Lines, l => Assert.False(l.HasGst));

        var refusal = AssertRefused(book, posted.Id, "ACCOUNTING (service) INVOICE", "DERIVED total");
        Assert.Contains("nothing re-derives", refusal, StringComparison.OrdinalIgnoreCase);
        // The old sentence claimed a measurement had not been taken. It has, so the claim must not come back.
        Assert.DoesNotContain("has not been measured", refusal, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Row 23 — the purchase arm of the accounting invoice, refused with its own (DEFER) sentence.</summary>
    [Fact]
    public void A_Purchase_accounting_invoice_is_refused_with_its_own_message()
    {
        using var book = AlterationBook.New("acctinv-purchase");
        var creditor = book.Ledger("Service Supplier", "Sundry Creditors");
        var expense = book.Ledger("Professional Fees", "Indirect Expenses");

        var posted = PostRaw(book, book.Type(VoucherBaseType.Purchase),
            PlainLegs(expense, creditor, 18000.18m), isAccountingInvoice: true);

        var refusal = AssertRefused(book, posted.Id, "ACCOUNTING (service) INVOICE", "Particulars");
        Assert.Contains("reverse-charge detection", refusal, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ (F) the stamped-tax families (DEFER)

    /// <summary>Rows 12 etc. — an engine-stamped <c>GstLineTax</c> is what GSTR-1 and GSTR-3B read, so it must be
    /// RE-DERIVED and never echoed; S5b has no re-derivation, so it refuses.</summary>
    [Fact]
    public void A_voucher_carrying_engine_stamped_GST_is_refused_by_name()
    {
        using var book = AlterationBook.New("gsttag");
        var debtor = book.Ledger("GST Customer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var output = book.Ledger("Output CGST", "Duties & Taxes");

        var posted = PostRaw(book, book.Type(VoucherBaseType.Sales), new[]
        {
            new EntryLine(debtor.Id, new Money(11800.10m), DrCr.Debit),
            new EntryLine(sales.Id, new Money(10000.10m), DrCr.Credit),
            new EntryLine(output.Id, new Money(1800.00m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, new Money(10000.10m))),
        });

        AssertRefused(book, posted.Id, "engine-stamped GST", "RE-DERIVED");
    }

    /// <summary>Rows 3 / 11 / 21 — the TDS carve: the party leg holds the DERIVED net, not the keyed gross.</summary>
    [Fact]
    public void A_voucher_carrying_a_TDS_carve_out_is_refused_by_name()
    {
        using var book = AlterationBook.New("tdstag");
        var creditor = book.Ledger("Consultant", "Sundry Creditors");
        var fees = book.Ledger("Professional Fees", "Indirect Expenses");
        var payable = book.Ledger("TDS Payable", "Duties & Taxes");

        var posted = PostRaw(book, book.Type(VoucherBaseType.Journal), new[]
        {
            new EntryLine(fees.Id, new Money(100000.10m), DrCr.Debit),
            new EntryLine(creditor.Id, new Money(90000.10m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(10000.00m), DrCr.Credit,
                tds: new TdsLineTax(Guid.NewGuid(), "194J", new Money(100000.10m), 1000,
                    new Money(10000.00m), creditor.Id, panApplied: true)),
        });

        AssertRefused(book, posted.Id, "TDS withholding carve-out", "gross");
    }

    /// <summary>The TCS arm — additive rather than withholding, and read by Form 27EQ.</summary>
    [Fact]
    public void A_voucher_carrying_a_TCS_collection_is_refused_by_name()
    {
        using var book = AlterationBook.New("tcstag");
        var debtor = book.Ledger("Bulk Buyer", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");
        var payable = book.Ledger("TCS Payable", "Duties & Taxes");

        var posted = PostRaw(book, book.Type(VoucherBaseType.Sales), new[]
        {
            new EntryLine(debtor.Id, new Money(100100.10m), DrCr.Debit),
            new EntryLine(sales.Id, new Money(100000.10m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(100.00m), DrCr.Credit,
                tcs: new TcsLineTax(Guid.NewGuid(), "206C(1H)", new Money(100000.10m), 10,
                    new Money(100.00m), debtor.Id, panApplied: true)),
        });

        AssertRefused(book, posted.Id, "TCS collection", "Form 27EQ");
    }

    // ================================================================ (G) MASTER DRIFT (§6.6a.5)

    /// <summary>
    /// 🔴 <b>The drift that would otherwise be SILENT.</b> <c>SyncBillWise</c> reads the ledger's LIVE
    /// <c>MaintainBillByBill</c>, so turning it off after posting hides the panel and makes
    /// <c>ToBillAllocations()</c> return EMPTY — the posted allocations would simply vanish on re-accept, with no
    /// message anywhere.
    /// </summary>
    [Fact]
    public void Bill_wise_turned_off_after_posting_is_refused_rather_than_dropping_the_allocations()
    {
        using var book = AlterationBook.New("driftbill");
        var debtor = book.Ledger("Drifting Party", "Sundry Debtors", billWise: true);
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "20000.20"), (sales, DrCr.Credit, "20000.20") },
            configure: e =>
            {
                e.Lines[0].BillAllocations[0].Name = "INV-77";
                e.Lines[0].BillAllocations[0].AmountText = "20000.20";
            });
        Assert.Single(posted.Lines.Single(l => l.LedgerId == debtor.Id).BillAllocations);

        debtor.MaintainBillByBill = false; // the master moved after the voucher was posted

        AssertRefused(book, posted.Id, "no longer maintains balances bill-by-bill", "vanish");
    }

    /// <summary>The other direction: bill-wise turned ON after posting would demand a split that was never keyed.</summary>
    [Fact]
    public void Bill_wise_turned_on_after_posting_is_refused_rather_than_demanding_a_split()
    {
        using var book = AlterationBook.New("driftbillon");
        var debtor = book.Ledger("Late Bill-wise Party", "Sundry Debtors");
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        var posted = book.Post(VoucherBaseType.Sales, book.On(),
            new[] { (debtor, DrCr.Debit, "20000.20"), (sales, DrCr.Credit, "20000.20") });

        debtor.MaintainBillByBill = true;

        AssertRefused(book, posted.Id, "now maintains balances bill-by-bill", "never keyed");
    }

    /// <summary>Cost centres switched off on the ledger after posting: the panel hides and the posted allocations
    /// would vanish.</summary>
    [Fact]
    public void Cost_centres_turned_off_after_posting_are_refused_rather_than_dropped()
    {
        using var book = AlterationBook.New("driftcost");
        var (branch, kolkata) = book.CostAxis("Branch", "Kolkata");
        var expense = book.Ledger("Advertising", "Indirect Expenses", costApplicable: true);
        var cash = book.Company.FindLedgerByName("Cash")!;

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (expense, DrCr.Debit, "5000.37"), (cash, DrCr.Credit, "5000.37") },
            configure: e =>
            {
                e.Lines[0].CostAllocations[0].SelectedCategory = branch;
                e.Lines[0].CostAllocations[0].SelectedCentre = kolkata;
                e.Lines[0].CostAllocations[0].AmountText = "5000.37";
            });
        Assert.Single(posted.Lines.Single(l => l.LedgerId == expense.Id).CostAllocations);

        expense.CostCentresApplicable = false;

        AssertRefused(book, posted.Id, "cost centres no longer apply", "vanish");
    }

    /// <summary>A cost CENTRE deleted from its category after posting cannot be re-keyed, and says so.</summary>
    [Fact]
    public void A_cost_centre_moved_out_of_its_category_after_posting_is_refused()
    {
        using var book = AlterationBook.New("driftcentre");
        var (branch, kolkata) = book.CostAxis("Branch", "Kolkata");
        var (other, _) = book.CostAxis("Other", "Elsewhere");
        var expense = book.Ledger("Advertising", "Indirect Expenses", costApplicable: true);
        var cash = book.Company.FindLedgerByName("Cash")!;

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (expense, DrCr.Debit, "5000.37"), (cash, DrCr.Credit, "5000.37") },
            configure: e =>
            {
                e.Lines[0].CostAllocations[0].SelectedCategory = branch;
                e.Lines[0].CostAllocations[0].SelectedCentre = kolkata;
                e.Lines[0].CostAllocations[0].AmountText = "5000.37";
            });
        Assert.NotNull(posted);

        kolkata.CategoryId = other.Id; // the centre now hangs off a different axis

        AssertRefused(book, posted.Id, "cost centre that is no longer under its category");
    }

    /// <summary>A bank ledger moved out of Bank Accounts after posting: the instrument detail would be lost.</summary>
    [Fact]
    public void A_bank_ledger_moved_out_of_Bank_Accounts_after_posting_is_refused()
    {
        using var book = AlterationBook.New("driftbank");
        var bank = book.Ledger("Yes Bank", "Bank Accounts");
        var rent = book.Ledger("Rent", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (rent, DrCr.Debit, "4000.40"), (bank, DrCr.Credit, "4000.40") },
            configure: e => e.Lines[1].InstrumentNumber = "CHQ-1");
        Assert.NotNull(posted.Lines.Single(l => l.LedgerId == bank.Id).BankAllocation);

        bank.GroupId = book.Company.FindGroupByName("Current Assets")!.Id;

        AssertRefused(book, posted.Id, "no longer a bank account", "instrument details");
    }

    /// <summary>
    /// 🔴 <b>A CORRECTION TO §6.6a ROW 15, measured while building this slice.</b> The row names two posters as one
    /// SIMPLE family — <c>PayrollVoucherService.PostGratuityProvision</c> and <c>ForexReportViewModel</c>'s
    /// revaluation. They differ. <c>ForexGainLoss.BuildAdjustingJournal</c> posts a leg to the FOREIGN-CURRENCY
    /// ledger carrying <b>no <c>ForexInfo</c> at all</b> (it is a pure base-currency adjustment), so the rehydrated
    /// line opens a forex panel the posted line never had — and <c>RecomputeForexBase</c> would BLANK the amount
    /// while demanding a forex amount and rate that do not exist. It is refused, correctly, and the gratuity arm
    /// (which round-trips) is proved separately in <see cref="VoucherAlterForAlterTests"/>.
    /// </summary>
    [Fact]
    public void A_base_only_leg_on_a_foreign_currency_ledger_is_refused_which_corrects_row_15()
    {
        using var book = AlterationBook.New("forexreval");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var forexGl = book.Ledger("Forex Gain/Loss", "Indirect Expenses");

        // Exactly the shape ForexGainLoss.BuildAdjustingJournal produces: EntryLine(ledgerId, magnitude, side)
        // with no forex argument, on a ledger that HOLDS a currency.
        var posted = PostRaw(book, book.Type(VoucherBaseType.Journal), new[]
        {
            new EntryLine(creditor.Id, new Money(1234.56m), DrCr.Debit),
            new EntryLine(forexGl.Id, new Money(1234.56m), DrCr.Credit),
        });
        Assert.False(posted.Lines.Single(l => l.LedgerId == creditor.Id).HasForex);

        AssertRefused(book, posted.Id, "now holds a foreign currency", "posted in base currency");
    }

    /// <summary>The mirror: a currency removed from the ledger after a forex line was posted.</summary>
    [Fact]
    public void A_currency_removed_from_a_ledger_after_posting_a_forex_line_is_refused()
    {
        using var book = AlterationBook.New("driftforex");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        var baseAmount = Money.ForexBase(new Money(100m), 83.25m).Amount;
        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[]
            {
                (purchases, DrCr.Debit, baseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (creditor, DrCr.Credit, "0"),
            },
            configure: e =>
            {
                e.Lines[1].ForexAmountText = "100";
                e.Lines[1].ForexRateText = "83.25";
            });
        Assert.True(posted.Lines.Single(l => l.LedgerId == creditor.Id).HasForex);

        creditor.CurrencyId = null;

        AssertRefused(book, posted.Id, "no longer holds one", "would be lost");
    }

    /// <summary>
    /// A bill-wise allocation carrying an explicit CREDIT PERIOD — a shape the canonical-XML import and the SQLite
    /// read path can both produce, and which <c>BillAllocationRowViewModel.ToAllocation()</c> has no field to
    /// re-state (it passes a due date only). Dropping it would move the bill's ageing silently.
    /// </summary>
    [Fact]
    public void A_bill_allocation_carrying_a_credit_period_is_refused_rather_than_losing_the_ageing()
    {
        using var book = AlterationBook.New("creditperiod");
        var debtor = book.Ledger("Imported Party", "Sundry Debtors", billWise: true);
        var sales = book.Ledger("Sales A/c", "Sales Accounts");

        var posted = PostRaw(book, book.Type(VoucherBaseType.Sales), new[]
        {
            new EntryLine(debtor.Id, new Money(9000.90m), DrCr.Debit, billAllocations: new[]
            {
                new BillAllocation(BillRefType.NewRef, "IMP-1", new Money(9000.90m), creditPeriodDays: 45),
            }),
            new EntryLine(sales.Id, new Money(9000.90m), DrCr.Credit),
        });

        AssertRefused(book, posted.Id, "credit period", "ageing");
    }

    /// <summary>A ledger the voucher posts to that has left the company cannot be shown, and says so.</summary>
    [Fact]
    public void A_line_posting_to_a_missing_ledger_is_refused_by_name()
    {
        using var book = AlterationBook.New("missingledger");
        var (dr, cr) = OrdinaryPair(book, "missingledger");
        var posted = PostRaw(book, book.Type(VoucherBaseType.Journal), PlainLegs(dr, cr, 1500.15m));

        // The voucher points at the ledger by Guid; a book whose master list no longer carries it (an import
        // rollback, a hand-edited file) reaches exactly this state.
        Assert.True(book.Company.RemoveLedger(dr));

        AssertRefused(book, posted.Id, "no longer in this company");
    }

    // ================================================================ (H) the provisional shape

    /// <summary>An "Applicable Upto" on anything but a Reversing Journal cannot be re-stated by the screen, so it
    /// is refused up front rather than arriving as an engine message about a field the operator never saw.</summary>
    [Fact]
    public void An_applicable_upto_on_a_non_reversing_type_is_refused_by_name()
    {
        using var book = AlterationBook.New("stray-upto");
        var (dr, cr) = OrdinaryPair(book, "strayupto");
        var posted = PostRaw(book, book.Type(VoucherBaseType.Journal), PlainLegs(dr, cr, 700.07m),
            applicableUpto: book.On(60));

        AssertRefused(book, posted.Id, "Applicable Upto", "Reversing Journal");
    }

    /// <summary>
    /// 🔴 <b>The MIRROR of the arm above, and §6.6a row 29's premise is what it refutes</b> (findings L1-03 and
    /// L3-02). Row 29 states that "every Reversing Journal carries a non-null <c>ApplicableUpto</c>", citing the
    /// ENTRY SCREEN's mandatory-field rule. That rule is not an invariant of the model: <c>VoucherValidator</c> has
    /// no ReversingJournal clause, so <c>LedgerService.Post</c> takes one straight through — and so does the
    /// product's own canonical import, with zero parse errors, on a file with the attribute stripped.
    ///
    /// <para>What such a voucher did before this arm: <c>ForAlter</c> OPENED it, seeded <c>ApplicableUptoText</c>
    /// from the CONSTRUCTOR's financial-year-end default, and <c>AcceptAlteration</c> could then never succeed —
    /// <c>Replace</c> refused a provisional-state change "from (none) to 31-Mar-…", an engine message about a field
    /// the operator never touched. A screen that can only ever be a dead end is refused at the door instead.</para>
    /// </summary>
    [Fact]
    public void A_reversing_journal_with_no_applicable_upto_is_refused_by_name()
    {
        using var book = AlterationBook.New("rj-noupto");
        var (dr, cr) = OrdinaryPair(book, "rjnoupto");

        // The door a service and the canonical import both use — and neither demands the date.
        var posted = PostRaw(book, book.Type(VoucherBaseType.ReversingJournal), PlainLegs(dr, cr, 909.09m));
        Assert.Null(posted.ApplicableUpto);

        AssertRefused(book, posted.Id, "Reversing Journal", "Applicable Upto", "financial year end");
    }

    /// <summary>
    /// 🔴 <b>The missing-CATEGORY half of the cost-drift gate</b> (finding L2-08). Of the three cost-side refusals
    /// two were locked and this one was not: neutering <c>if (category is null)</c> survived the whole S5b suite,
    /// so a posted allocation naming a category that had since left the company would have been silently
    /// mis-rehydrated onto whatever the row's default picker offered.
    /// </summary>
    [Fact]
    public void A_cost_allocation_whose_category_left_the_company_is_refused_by_name()
    {
        using var book = AlterationBook.New("costcatgone");
        var (category, centre) = book.CostAxis("Branch", "Kolkata");
        var travel = book.Ledger("Travel", "Indirect Expenses", costApplicable: true);
        var cash = book.Company.FindLedgerByName("Cash")!;

        var posted = book.Post(VoucherBaseType.Payment, book.On(),
            new[] { (travel, DrCr.Debit, "5000.55"), (cash, DrCr.Credit, "5000.55") },
            configure: e =>
            {
                var row = e.Lines[0].CostAllocations[0];
                row.SelectedCategory = category;
                row.SelectedCentre = centre;
                row.AmountText = "5000.55";
            });
        Assert.True(posted.Lines[0].HasCostAllocations);

        // The category leaves the company (the canonical import and a master screen can both do this).
        Assert.True(book.Company.RemoveCostCategory(category));

        AssertRefused(book, posted.Id, "cost category that is no longer in this company");
    }

    /// <summary>
    /// 🔴 <b>The seventh direction of master drift — the one that was NOT refused</b> (finding L3-01, measured).
    /// The inverse asked only WHETHER the line still holds a foreign currency, never WHICH: <c>ToForexInfo</c>
    /// rebuilds the <c>ForexInfo</c> from the LIVE ledger's <c>CurrencyId</c>, so a ledger repointed from USD to EUR
    /// after posting opened silently, accepted with a plain "altered.", and the posted line came out denominated in
    /// EUR — with the canonical export no longer identical and no message anywhere.
    /// <c>VoucherValidator.EnsureForexValid</c> cannot catch it: it checks only that the currency EXISTS and that
    /// base ≈ forex × rate, both of which survive the swap.
    /// </summary>
    [Fact]
    public void A_ledger_repointed_to_a_different_currency_after_posting_is_refused_by_name()
    {
        using var book = AlterationBook.New("fxdrift");
        var usd = book.ForeignCurrency("$", "USD");
        var eur = book.ForeignCurrency("€", "EUR");
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        const decimal forexAmount = 1000.25m;
        const decimal rate = 83.251111m;
        var baseAmount = Money.ForexBase(new Money(forexAmount), rate).Amount;

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[]
            {
                (purchases, DrCr.Debit, baseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (creditor, DrCr.Credit, "0"),
            },
            configure: e =>
            {
                e.Lines[1].ForexAmountText = forexAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                e.Lines[1].ForexRateText = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
        Assert.Equal(usd.Id, posted.Lines.Single(l => l.LedgerId == creditor.Id).Forex!.CurrencyId);

        // Exactly what LedgerMasterViewModel's write block does — unconditionally, with no transacted-ledger guard.
        creditor.CurrencyId = eur.Id;

        AssertRefused(book, posted.Id, "US Supplier", "USD", "EUR", "never denominated in");
    }

    /// <summary>
    /// 🔴 <b>The amount-equality guard the rehydration ends on, made falsifiable</b> (finding L2-09, whose stated
    /// premise this corrects). The lens reported that this guard "never fires and cannot be made to fire", and
    /// concluded the only shape that would reach it is a sub-paisa forex magnitude. It is not:
    /// <c>VoucherValidator.EnsureForexValid</c> deliberately tolerates a base amount up to ONE PAISA away from the
    /// paisa-snapped forex × rate (its own comment says so — a base carrying the unrounded tail must load), while
    /// <c>RecomputeForexBase</c> always DRIVES the rebuilt base to the snapped figure. A posted line one paisa off —
    /// reachable through <c>LedgerService.Post</c>, the canonical import and the SQLite read path alike — therefore
    /// cannot be re-keyed exactly, and this is the guard that says so instead of silently moving the line.
    /// </summary>
    [Fact]
    public void A_forex_base_the_screen_cannot_re_key_exactly_is_refused_by_name()
    {
        using var book = AlterationBook.New("fxonepaisa");
        var usd = book.ForeignCurrency();
        var creditor = book.Ledger("US Supplier", "Sundry Creditors", currencyId: usd.Id);
        var purchases = book.Ledger("Imports", "Purchase Accounts");

        const decimal forexAmount = 1000.25m;
        const decimal rate = 83.251111m;
        var snapped = Money.ForexBase(new Money(forexAmount), rate).Amount;
        var oneOff = snapped + 0.01m;   // inside EnsureForexValid's tolerance, outside what the screen can rebuild

        var posted = PostRaw(book, book.Type(VoucherBaseType.Purchase), new[]
        {
            new EntryLine(purchases.Id, new Money(oneOff), DrCr.Debit),
            new EntryLine(creditor.Id, new Money(oneOff), DrCr.Credit,
                forex: new ForexInfo(usd.Id, new Money(forexAmount), rate)),
        });
        Assert.Equal(oneOff, posted.Lines.Single(l => l.LedgerId == creditor.Id).Amount.Amount);

        AssertRefused(book, posted.Id, "cannot be re-keyed exactly", "US Supplier");
    }

    /// <summary>
    /// The voucher's TYPE has left the company (finding L2-11 item A03): the id-overload's second guard, which
    /// survived being replaced by a nonsense string because no test named it.
    /// </summary>
    [Fact]
    public void A_voucher_whose_type_left_the_company_is_refused_by_name()
    {
        using var book = AlterationBook.New("typegone");
        var posted = book.PostPlainPair(VoucherBaseType.Journal, 777.77m);
        var type = book.Company.FindVoucherType(posted.TypeId)!;
        Assert.True(book.Company.RemoveVoucherType(type));

        AssertRefused(book, posted.Id, "voucher's type is missing", "Restore the voucher type");
    }

    // ================================================================ (I) 🔴 §6.6a.7 — THE COVERAGE LOCK

    /// <summary>
    /// 🔴 <b>THE COVERAGE LOCK the plan's S5b blocking item asks for: "a test that fails when a newly seeded kind
    /// belongs to neither set."</b>
    ///
    /// <para><b>It lives HERE, in <c>Apex.Desktop.Tests</c>, and it cannot live anywhere else</b> (§6.6a.7):
    /// <c>Apex.Ledger.Tests</c> references only <c>Apex.Ledger</c> and <c>Apex.Ledger.Io</c>, so it can see neither
    /// <see cref="PopulatedCompanyFixture"/> nor <see cref="VoucherEntryViewModel.ForAlter"/>.</para>
    ///
    /// <para><b>What it asserts, phrased so a new seed row fails it:</b> for EVERY base kind the fixture's own
    /// voucher-type seed defines, the S5b dispatcher returns either a rehydrated view model or a refusal whose
    /// message is non-empty and family-specific — <b>never a null, never a silent no-op</b>. That is RULING 1's
    /// standard; a test asserting "nothing happened" would pass for a silent no-op too.</para>
    ///
    /// <para>The denominator is read from <c>Company.VoucherTypes</c> as data, exactly as
    /// <c>PopulatedFixtureCoverageTests</c> does, so adding a seeded type without a decision here fails on the day
    /// it is added rather than quietly widening the blind spot.</para>
    /// </summary>
    [Fact]
    public void Every_seeded_base_kind_yields_either_a_rehydrated_screen_or_a_named_refusal()
    {
        var company = PopulatedCompanyFixture.BuildRegular();
        using var scratch = new ScratchStorage();

        var seeded = company.VoucherTypes.Select(t => t.BaseType).ToHashSet();
        var undecided = new List<string>();
        var decided = new List<string>();

        foreach (var baseType in seeded.OrderBy(b => b.ToString()))
        {
            var specimen = FindSpecimen(company, baseType);
            if (specimen is null)
            {
                // Attendance posts AttendanceEntry rows, never a Voucher, and is the one seeded kind that may
                // legitimately have no specimen. Anything ELSE with no specimen means the fixture stopped covering
                // a family, which is PopulatedFixtureCoverageTests' own lock — recorded here rather than hidden.
                if (baseType != VoucherBaseType.Attendance) undecided.Add($"{baseType}: no posted specimen");
                continue;
            }

            var open = VoucherEntryViewModel.ForAlter(company, specimen.Value, scratch.Storage, () => { }, () => { });

            if (open.IsRefused)
            {
                // 🔴 There is deliberately NO "refused with an empty message" branch here (finding L2-06). One used
                // to sit at this spot and could never execute: VoucherAlterationOpen.Refused THROWS on a blank
                // refusal ("A refusal must name the family and the reason"), so an empty refusal fails this test
                // with an unhandled ArgumentException from inside ForAlter rather than with the named diagnosis the
                // branch advertised — decoration on top of a throw that already covers it. The reachable version of
                // that check lives in Every_specialised_type_flag_has_its_own_named_refusal_from_the_predicate,
                // which calls VoucherAlterationEligibility.RefusalFor directly (that CAN return "") and asserts the
                // sentence is real.
                decided.Add($"{baseType}: refused");
                continue;
            }

            Assert.NotNull(open.Entry);
            Assert.True(open.Entry!.IsAltering);
            decided.Add($"{baseType}: opened");
        }

        Assert.True(
            undecided.Count == 0,
            "The S5b dispatcher has no decision for these seeded base kinds — a silent no-op is exactly what "
            + "ORCHESTRATOR RULING 1 forbids:\n  " + string.Join("\n  ", undecided)
            + "\n\nDecided:\n  " + string.Join("\n  ", decided));

        // The lock is worth something only if it actually ran over the whole seed…
        var expected = seeded.Count - (seeded.Contains(VoucherBaseType.Attendance) ? 1 : 0);
        Assert.Equal(expected, decided.Count);

        // …and only if BOTH outcomes actually occur. A dispatcher that refused everything would satisfy every
        // assertion above while shipping nothing, and one that opened everything would have no refusals at all.
        //
        // 🔴 AND THAT IS ALL THIS TEST PROVES — stated here because it read as far more (finding L2-01, measured).
        // Replacing the ENTIRE body of VoucherAlterationEligibility.RefusalFor with `return null` left this test
        // GREEN while 23 other S5b tests died, and deleting a whole L5 type-flag arm left it green too. The reason
        // is the denominator: base kinds, decided by OTHER code. In this fixture seven of the ten SIMPLE families
        // are refused by MASTER DRIFT (a refusal VoucherLineViewModel.RehydrateFrom owns, in a different file), the
        // twelve inventory kinds by the architectural aggregate check upstream of the predicate, and Sales /
        // Purchase by the item-invoice arm — so the "refused" clause below is carried entirely by checks the
        // predicate does not own, and the "opened" clause by ONE base kind, Memorandum.
        //
        // The two tests that DO reach the predicate are below:
        //   Every_specialised_type_flag_has_its_own_named_refusal_from_the_predicate — the L5 layer, asserted
        //     against RefusalFor directly, with a reflection guard so a NEW derived type flag fails on the day it
        //     is added; and
        //   Every_SIMPLE_base_kind_opens_on_a_drift_free_book — all ten SIMPLE families on masters that have not
        //     moved, so the opened side is not carried by Memorandum alone.
        Assert.Contains(decided, d => d.EndsWith("opened", StringComparison.Ordinal));
        Assert.Contains(decided, d => d.EndsWith("refused", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔴 <b>THE LOCK AT THE LAYER THE DISPATCHER ACTUALLY SWITCHES ON</b> (finding L2-01). §6.6a.2 is explicit that
    /// the discriminator is <b>not</b> the base kind — a POS bill, a GST challan, an RCM payment voucher, a stat
    /// adjustment and a Manufacturing Journal each SHARE a base kind with an ordinary type and mean a different
    /// screen. The seeded-base-kind lock above cannot see that layer at all. This one iterates it.
    ///
    /// <para><b>Three things make it bite where the other does not.</b> (1) The subject is
    /// <see cref="VoucherAlterationEligibility.RefusalFor(Company, Voucher, VoucherType)"/> ITSELF, not
    /// <c>ForAlter</c>'s whole chain, so a neutered predicate cannot be covered for by master drift, by the
    /// aggregate check or by the entry-mode arm. (2) Every flag must produce a DISTINCT sentence, so one arm cannot
    /// answer for another. (3) The denominator is read by REFLECTION off <see cref="VoucherType"/>'s own derived
    /// (get-only) <c>Is…</c> predicates — the exact shape of an L5 flag, a conjunction of a base kind and a
    /// user-settable flag — so adding a new one without a decision here fails on the day it is added, which is what
    /// the plan's blocking item asks for.</para>
    /// </summary>
    [Fact]
    public void Every_specialised_type_flag_has_its_own_named_refusal_from_the_predicate()
    {
        using var book = AlterationBook.New("l5flags");
        var (dr, cr) = OrdinaryPair(book, "l5");

        // The (flag × base kind) cross-product §6.6a.2 says the dispatcher must switch on, keyed by the DERIVED
        // predicate each arm reads. Every row names the property the predicate consults.
        var rows = new (string Property, VoucherType Type)[]
        {
            (nameof(VoucherType.IsManufacturingJournal),
                AddType(book, "MFG", VoucherBaseType.StockJournal, useAsManufacturingJournal: true)),
            (nameof(VoucherType.IsPosSales),
                AddType(book, "POS Till (lock)", VoucherBaseType.Sales, useForPos: true)),
            (nameof(VoucherType.IsStatPaymentType),
                AddType(book, "Stat Pay (lock)", VoucherBaseType.Payment, isStatPayment: true)),
            (nameof(VoucherType.IsRcmPaymentVoucherType),
                AddType(book, "RCM Pay (lock)", VoucherBaseType.Payment, isRcmPaymentVoucher: true)),
            (nameof(VoucherType.IsGstStatAdjustmentType),
                AddType(book, "Stat Adj (lock)", VoucherBaseType.Journal, isGstStatAdjustment: true)),
            (nameof(VoucherType.IsConsumingMaterialIn),
                AddType(book, "Material In (lock)", VoucherBaseType.MaterialIn, allowConsumption: true)),
        };

        // 🔴 THE COMPLETENESS HALF. VoucherType's get-only bool properties ARE the L5 layer (each is a conjunction
        // of a base kind and a settable flag); IsPredefined is the one get-only bool that is not such a predicate.
        var derivedFlags = typeof(VoucherType).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.CanRead && !p.CanWrite)
            .Select(p => p.Name)
            .Where(n => n != nameof(VoucherType.IsPredefined))
            .ToList();
        Assert.Equal(
            derivedFlags.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            rows.Select(r => r.Property).OrderBy(n => n, StringComparer.Ordinal).ToList());

        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (property, type) in rows)
        {
            // A voucher of that type, hand-built ONLY because no entry screen keys these families at all — which is
            // the very reason they are refused. The predicate is then asked directly.
            var voucher = new Voucher(Guid.NewGuid(), type.Id, book.On(), PlainLegs(dr, cr, 4242.42m));
            var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, type);

            Assert.False(
                string.IsNullOrWhiteSpace(refusal),
                $"{property}: the predicate returned no refusal — a silent no-op is what RULING 1 forbids.");
            messages[property] = refusal!;
        }

        // Distinct sentences: one arm must not be answering for another (which is how a deleted arm hides).
        Assert.Equal(rows.Length, messages.Values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The Attendance arm (finding L2-11, item A11). Attendance is the 24th <see cref="VoucherBaseType"/> member,
    /// posts <c>AttendanceEntry</c> rows rather than vouchers, and is un-seeded — so the seeded-base-kind lock
    /// skips it by name and the arm survived its own deletion. It is still the sentence that must appear on the
    /// day something posts one, so it is asserted against the predicate directly rather than left to a fixture
    /// that will never contain the shape.
    /// </summary>
    [Fact]
    public void An_attendance_type_is_refused_by_name_although_the_fixture_never_seeds_one()
    {
        using var book = AlterationBook.New("attendance");
        var (dr, cr) = OrdinaryPair(book, "attend");
        var type = AddType(book, "Attendance (lock)", VoucherBaseType.Attendance);

        var voucher = new Voucher(Guid.NewGuid(), type.Id, book.On(), PlainLegs(dr, cr, 321.21m));
        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, type);

        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("attendance", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not as a Dr/Cr voucher", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>The OPENED side, carried by all ten SIMPLE families rather than by Memorandum alone</b> (findings
    /// L2-01 and L1-06). In <see cref="PopulatedCompanyFixture"/> seven of the ten are refused for MASTER DRIFT, so
    /// the lock above would stay green if the rehydration broke for every family but Memorandum. This book is
    /// drift-free by construction — every ledger is created here and never moved — so the ten SIMPLE base kinds
    /// §6.6a.8 enumerates must all open, and a rehydration regression on any one of them reddens it.
    /// </summary>
    [Fact]
    public void Every_SIMPLE_base_kind_opens_on_a_drift_free_book()
    {
        using var book = AlterationBook.New("simpleten");
        var simple = new[]
        {
            VoucherBaseType.Contra, VoucherBaseType.Payment, VoucherBaseType.Receipt, VoucherBaseType.Journal,
            VoucherBaseType.Sales, VoucherBaseType.Purchase, VoucherBaseType.CreditNote,
            VoucherBaseType.DebitNote, VoucherBaseType.Memorandum, VoucherBaseType.ReversingJournal,
        };

        var opened = new List<VoucherBaseType>();
        foreach (var baseType in simple)
        {
            // A Reversing Journal is the one family with a mandatory extra field; everything else is a plain pair.
            var posted = baseType == VoucherBaseType.ReversingJournal
                ? book.Post(baseType, book.On(),
                    new[]
                    {
                        (book.Ledger("RJ Dr", "Indirect Expenses"), DrCr.Debit, "3000.33"),
                        (book.Ledger("RJ Cr", "Current Liabilities"), DrCr.Credit, "3000.33"),
                    },
                    configure: e => e.ApplicableUptoText = ApexDate.Format(book.On(45)))
                : book.PostPlainPair(baseType, 1010.10m);

            var open = book.ForAlter(posted.Id);
            Assert.True(open.Entry is not null, $"{baseType}: expected the screen to open — {open.Refusal}");
            opened.Add(baseType);
        }

        Assert.Equal(simple.Length, opened.Count);
    }

    /// <summary>Any posted voucher of <paramref name="baseType"/>, from EITHER aggregate.</summary>
    private static Guid? FindSpecimen(Company company, VoucherBaseType baseType)
    {
        foreach (var v in company.Vouchers)
            if (company.FindVoucherType(v.TypeId)?.BaseType == baseType) return v.Id;
        foreach (var iv in company.InventoryVouchers)
            if (company.FindVoucherType(iv.TypeId)?.BaseType == baseType) return iv.Id;
        return null;
    }

    /// <summary>A throwaway <see cref="CompanyStorage"/> for the fixture-based tests, which never save.</summary>
    private sealed class ScratchStorage : IDisposable
    {
        private readonly string _dir;
        public CompanyStorage Storage { get; }

        public ScratchStorage()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ApexAlterScratch_" + Guid.NewGuid().ToString("N"));
            Storage = new CompanyStorage(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
