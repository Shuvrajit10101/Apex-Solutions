using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Phase 10.11 S4 — the DELETE verb's shared engine guards (<see cref="MasterDeletionRules"/>).</b>
///
/// <para><b>Why these tests exist at all.</b> <c>LedgerService.Delete</c> has been in this codebase since Phase 1
/// with <b>no caller anywhere in the application</b>. S4 is the slice that makes it reachable, so every
/// consequence of removing a posted voucher arrives now. Two of them are guarded, and the third is accepted
/// deliberately — the last one is the pair of tests at the bottom of this file and it is the reason to read the
/// whole class rather than skim it.</para>
///
/// <para><b>🔴 R7 — FIDELITY, and the two categories are kept strictly apart.</b>
/// <list type="bullet">
///   <item><b>ATTESTED:</b> that a ledger carrying transactions cannot be deleted (STUDY-GUIDE PDF p.67:
///     <i>"You cannot delete any ledger, if any transaction(s) has been already made with that ledger"</i>). What
///     is OURS is the <i>count</i> in the refusal and the extension of the same shape to groups and stock
///     items.</item>
///   <item><b>UNVERIFIED-BY-DESIGN — ours, corpus silent:</b> the referential guard, the numbering guard, the
///     accepted residual, offering Cancel as the remedy, and every message string. The corpus says nothing about
///     what deleting a voucher does to a linked statutory document, and nothing about its number.</item>
/// </list>
/// No test here may be re-labelled as fidelity to any other product.</para>
/// </summary>
public class MasterDeletionRulesTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly On = new(2024, 4, 10);

    private static Company Seed(string name = "Delete Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    /// <summary>A balanced Journal posted through the real engine, so the voucher under test is genuinely posted
    /// and genuinely numbered.</summary>
    private static Voucher PostJournal(Company c, decimal rupees = 5000m, DateOnly? on = null)
    {
        var party = c.FindLedgerByName("Acme Traders") ?? AddParty(c, "Acme Traders");
        var sales = c.FindLedgerByName("Sales") ?? AddSales(c, "Sales");
        var journal = c.FindVoucherTypeByName("Journal")!;

        var v = new Voucher(Guid.NewGuid(), journal.Id, on ?? On, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
        });
        new LedgerService(c).Post(v);
        return v;
    }

    private static DomainLedger AddParty(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
                                Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger AddSales(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sales Accounts")!.Id,
                                Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Attaches a <c>Generated</c> e-invoice record — the state a book reopened after an IRP round-trip is
    /// in, and the state that makes <see cref="MasterDeletionRules.IsFiledStatutoryDocument"/> true. Rehydration is
    /// used rather than the prepare/record flow because the guard reads the RECORD's status and nothing about how
    /// it got there.</summary>
    private static EInvoiceRecord AttachGeneratedIrn(Company c, Guid voucherId, string docNo = "INV/1")
    {
        var record = EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), voucherId, docNo, EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112210000123456", ackDate: On,
            signedQr: "eyJhbGciOi", signedJson: Array.Empty<byte>(),
            cancelledOn: null, cancelReasonCode: null);
        c.AddEInvoiceRecord(record);
        return record;
    }

    // =====================================================================================================
    //  THE GUARDS ARE PURE — they throw and never mutate (the MasterAlterationRules shape)
    // =====================================================================================================

    /// <summary>
    /// The shape contract, asserted rather than assumed: a refused deletion leaves the company EXACTLY as it was.
    /// A guard that half-removed something before throwing would be the worst possible failure mode for a
    /// destructive verb, and nothing else in this file would notice.
    /// </summary>
    [Fact]
    public void A_refused_deletion_mutates_nothing()
    {
        var c = Seed();
        var v = PostJournal(c);
        AttachGeneratedIrn(c, v.Id);

        var vouchersBefore = c.Vouchers.Count;
        var ledgersBefore = c.Ledgers.Count;
        var recordsBefore = c.EInvoiceRecords.Count;

        Assert.Throws<InvalidOperationException>(() => MasterDeletionRules.EnsureVoucherDeletable(c, v));

        Assert.Equal(vouchersBefore, c.Vouchers.Count);
        Assert.Equal(ledgersBefore, c.Ledgers.Count);
        Assert.Equal(recordsBefore, c.EInvoiceRecords.Count);
        Assert.NotNull(c.FindVoucher(v.Id));
    }

    /// <summary>A voucher nothing points at, carrying no statutory document, is deletable — the positive control
    /// without which every refusal below could be a guard that refuses everything.</summary>
    [Fact]
    public void A_plain_unreferenced_voucher_is_deletable()
    {
        var c = Seed();
        var v = PostJournal(c);

        MasterDeletionRules.EnsureVoucherDeletable(c, v);   // does not throw

        Assert.Empty(MasterDeletionRules.DescribeVoucherReferences(c, v.Id));
        Assert.Equal(0, MasterDeletionRules.CountVoucherReferences(c, v.Id));
        Assert.False(MasterDeletionRules.IsFiledStatutoryDocument(c, v.Id));
    }

    // =====================================================================================================
    //  ITEM 3 — THE REFERENTIAL GUARD, REFUSING WITH THE COUNT
    // =====================================================================================================

    /// <summary>
    /// A voucher that is the <c>OriginalInvoiceVoucherId</c> of a LIVE §34 credit/debit note cannot be deleted, and
    /// the refusal NAMES THE COUNT. Deleting it would leave the note's link pointing at an invoice that is not on
    /// the books, with the §34(2) 30-Nov cut-off computed from a date whose document no longer exists.
    /// </summary>
    [Fact]
    public void Deleting_the_original_invoice_of_a_live_section34_note_is_refused_with_the_count()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        var note = PostJournal(c, 500m, On.AddDays(5));
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, invoice.Id, null, On, "01"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));

        Assert.Contains("1 document references this voucher", ex.Message);
        Assert.Contains("1 credit/debit note issued against it", ex.Message);
        Assert.Equal(1, MasterDeletionRules.CountVoucherReferences(c, invoice.Id));
    }

    /// <summary>
    /// A §34 link whose note voucher has been CANCELLED is not live and blocks nothing — the same liveness test
    /// <c>ChallanReconciliation.ChallanHasLiveVoucher</c> applies to a challan's booking voucher. Without this the
    /// guard would refuse forever on a note the operator had already voided, with no route out.
    /// </summary>
    [Fact]
    public void A_section34_link_whose_note_is_cancelled_does_not_block_the_original_invoice()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        var note = PostJournal(c, 500m, On.AddDays(5));
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, invoice.Id, null, On, "01"));

        new LedgerService(c).Cancel(note.Id);

        MasterDeletionRules.EnsureVoucherDeletable(c, invoice);   // does not throw
        Assert.Equal(0, MasterDeletionRules.CountVoucherReferences(c, invoice.Id));
    }

    /// <summary>
    /// The count is a SUM ACROSS CATEGORIES, and the message enumerates each one. This is the case a
    /// count-of-categories implementation gets wrong: three blocking rows of two kinds must read "3 documents", not
    /// "2".
    /// </summary>
    [Fact]
    public void The_refusal_sums_blockers_across_categories_and_names_each_one()
    {
        var c = Seed();
        var booking = PostJournal(c);

        var tds = new TdsChallan(Guid.NewGuid(), "0001234", "0510308", On, Money.FromRupees(100m), "194C", "200");
        c.AddTdsChallan(tds);
        c.LinkChallanToVoucher(tds.Id, booking.Id);

        var tcsA = new TcsChallan(Guid.NewGuid(), "0002222", "0510308", On, Money.FromRupees(50m), "206C(1H)", "200");
        var tcsB = new TcsChallan(Guid.NewGuid(), "0003333", "0510308", On, Money.FromRupees(60m), "206C(1H)", "200");
        c.AddTcsChallan(tcsA);
        c.AddTcsChallan(tcsB);
        c.LinkTcsChallanToVoucher(tcsA.Id, booking.Id);
        c.LinkTcsChallanToVoucher(tcsB.Id, booking.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, booking));

        Assert.Equal(3, MasterDeletionRules.CountVoucherReferences(c, booking.Id));
        Assert.Contains("3 documents reference this voucher", ex.Message);
        Assert.Contains("1 TDS challan link", ex.Message);
        Assert.Contains("2 TCS challan links", ex.Message);
    }

    /// <summary>
    /// 🔴 <b>THE COUNT AND ITS BREAKDOWN CAN NEVER DISAGREE.</b> Across a voucher carrying every blocker category
    /// at once, the number the refusal leads with must equal the sum of the categories it then enumerates.
    ///
    /// <para><b>Why this test exists.</b> The first cut of <c>MasterDeletionRules</c> computed the count and the
    /// breakdown in two separate methods. A category added to one and not the other would have produced a refusal
    /// contradicting itself inside one sentence — "2 documents reference this voucher (1 X, 1 Y, 1 Z)" — and no
    /// test asserting either half alone could see it. The implementation now derives both from one tally; this is
    /// the test that keeps it that way.</para>
    /// </summary>
    [Fact]
    public void ReferenceCountsAlwaysAgreeWithTheirBreakdown()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        var note = PostJournal(c, 500m, On.AddDays(5));

        // Every category at once: §34 as original, §34 as the note itself, TDS, TCS, e-invoice, e-Way Bill.
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, invoice.Id, null, On, "01"));
        var tds = new TdsChallan(Guid.NewGuid(), "0001234", "0510308", On, Money.FromRupees(100m), "194C", "200");
        c.AddTdsChallan(tds);
        c.LinkChallanToVoucher(tds.Id, invoice.Id);
        var tcs = new TcsChallan(Guid.NewGuid(), "0009999", "0510308", On, Money.FromRupees(50m), "206C(1H)", "200");
        c.AddTcsChallan(tcs);
        c.LinkTcsChallanToVoucher(tcs.Id, invoice.Id);
        c.AddEInvoiceRecord(new EInvoiceRecord(Guid.NewGuid(), invoice.Id, "INV/7"));
        c.AddEWayBillRecord(new EWayBillRecord(
            Guid.NewGuid(), invoice.Id, "INV/7", "Outward", "Supply", "INV", 500000L, "27", "29"));

        var described = MasterDeletionRules.DescribeVoucherReferences(c, invoice.Id);
        var counted = MasterDeletionRules.CountVoucherReferences(c, invoice.Id);

        // Five categories hold this voucher (it is the original of the note, not the note itself), one row each.
        Assert.Equal(5, described.Count);
        Assert.Equal(5, counted);

        // 🔴 GUARD ORDER, asserted here because this setup is the one that exposes it: the voucher carries a live
        // e-Way Bill, so it is a FILED document and the NUMBERING guard answers first — with the remedy, not with
        // the count. This assertion replaced one that expected the referential message and went red, which is the
        // ordering working as designed rather than a defect.
        var filedEx = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));
        Assert.Contains("filed statutory document", filedEx.Message);

        // Strip the two statutory artefacts and the REFERENTIAL guard takes over. Its leading figure is the sum of
        // the categories it enumerates — not a count of categories, and not a stale second computation of either.
        foreach (var r in c.EInvoiceRecords.Where(r => r.SourceVoucherId == invoice.Id).ToList())
            c.RemoveEInvoiceRecord(r);
        foreach (var r in c.EWayBillRecords.Where(r => r.SourceVoucherId == invoice.Id).ToList())
            c.RemoveEWayBillRecord(r);
        Assert.False(MasterDeletionRules.IsFiledStatutoryDocument(c, invoice.Id));

        var remaining = MasterDeletionRules.DescribeVoucherReferences(c, invoice.Id);
        var remainingCount = MasterDeletionRules.CountVoucherReferences(c, invoice.Id);
        Assert.Equal(3, remaining.Count);
        Assert.Equal(3, remainingCount);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));
        Assert.Contains($"{remainingCount} documents reference this voucher", ex.Message);
        foreach (var part in remaining) Assert.Contains(part, ex.Message);
    }

    /// <summary>
    /// A <c>Pending</c> e-invoice record blocks the delete through the REFERENTIAL guard even though it is NOT a
    /// filed document — it never reached the IRP, so its number is not burned, but the row still holds the
    /// voucher's Guid and would be orphaned. This is the case that proves the two guards are not the same guard
    /// wearing two names.
    /// </summary>
    [Fact]
    public void A_pending_einvoice_blocks_referentially_but_is_not_a_filed_document()
    {
        var c = Seed();
        var v = PostJournal(c);
        c.AddEInvoiceRecord(new EInvoiceRecord(Guid.NewGuid(), v.Id, "INV/9"));   // ctor status = Pending

        Assert.False(MasterDeletionRules.IsFiledStatutoryDocument(c, v.Id));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, v));
        Assert.Contains("1 document references this voucher", ex.Message);
        Assert.Contains("1 e-invoice record", ex.Message);
        // NOT the numbering refusal — that one names the remedy.
        Assert.DoesNotContain("filed statutory document", ex.Message);
    }

    // =====================================================================================================
    //  ITEM 4 — THE MASTER-SIDE GUARD, REFUSING WITH THE COUNT
    // =====================================================================================================

    /// <summary>
    /// 🔴 THE CORPUS RULE (STUDY-GUIDE PDF p.67): a ledger with transactions cannot be deleted — refused with the
    /// count of vouchers. The count is what makes it actionable: "3 vouchers" tells the operator there is work to
    /// do; a bare "cannot delete" does not.
    /// </summary>
    [Fact]
    public void A_ledger_carrying_transactions_is_refused_with_the_voucher_count()
    {
        var c = Seed();
        PostJournal(c);
        PostJournal(c, 700m, On.AddDays(1));
        PostJournal(c, 900m, On.AddDays(2));
        var party = c.FindLedgerByName("Acme Traders")!;

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, party));

        Assert.Contains("3 vouchers have already been posted against it", ex.Message);
        Assert.Contains("Acme Traders", ex.Message);
    }

    /// <summary>The singular reading is spelled out separately, because "1 vouchers have" is the sentence an
    /// operator remembers about the quality of a product.</summary>
    [Fact]
    public void The_single_voucher_refusal_reads_as_singular()
    {
        var c = Seed();
        PostJournal(c);
        var party = c.FindLedgerByName("Acme Traders")!;

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, party));

        Assert.Contains("1 voucher has already been posted against it", ex.Message);
    }

    /// <summary>An unused custom ledger IS deletable — the positive control for the ledger guard.</summary>
    [Fact]
    public void An_unused_custom_ledger_is_deletable()
    {
        var c = Seed();
        var spare = AddParty(c, "Never Used Traders");

        MasterDeletionRules.EnsureLedgerDeletable(c, spare);   // does not throw
    }

    /// <summary>A predefined ledger (Cash, Profit &amp; Loss A/c) is refused outright, transactions or not.</summary>
    [Fact]
    public void A_predefined_ledger_cannot_be_deleted()
    {
        var c = Seed();
        var cash = c.FindLedgerByName("Cash")!;
        Assert.True(cash.IsPredefined);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, cash));
        Assert.Contains("predefined ledger and cannot be deleted", ex.Message);
    }

    /// <summary>
    /// 🔴 A ledger carrying a <see cref="MasterAlterationRules.WellKnownLedgerNames"/> name is refused even when
    /// NOTHING is posted against it and it is not flagged predefined. ~14 engine sites resolve those by hardcoded
    /// string and fail <b>silently</b> — <c>B2cQrService</c>'s round-off lookup just returns zero. If renaming one
    /// is refused because the engine would stop finding it, deleting one is strictly worse, and "Round Off" is the
    /// exact ledger that is created WITHOUT the predefined flag, so an <c>IsPredefined</c>-only guard misses it.
    /// </summary>
    [Fact]
    public void A_reserved_by_name_ledger_cannot_be_deleted_even_when_unused()
    {
        var c = Seed();
        var roundOff = new DomainLedger(Guid.NewGuid(), "Round Off",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(roundOff);

        Assert.False(roundOff.IsPredefined);            // the trap: not predefined, still load-bearing
        Assert.Empty(c.Vouchers);                       // and entirely unused

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, roundOff));
        Assert.Contains("reserved ledger that the engine resolves by name", ex.Message);
    }

    /// <summary>A group with children is refused, with the count broken down into sub-groups and ledgers.</summary>
    [Fact]
    public void A_group_with_children_is_refused_with_the_master_count()
    {
        var c = Seed();
        var parent = new Group(Guid.NewGuid(), "Regional", GroupNature.Asset,
                               c.FindGroupByName("Current Assets")!.Id);
        c.AddGroup(parent);
        c.AddGroup(new Group(Guid.NewGuid(), "North", GroupNature.Asset, parent.Id));
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Regional Debtor", parent.Id, Money.Zero, openingIsDebit: true));
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Regional Advance", parent.Id, Money.Zero, openingIsDebit: true));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureGroupDeletable(c, parent));

        Assert.Contains("3 masters are filed under it", ex.Message);
        Assert.Contains("1 sub-group", ex.Message);
        Assert.Contains("2 ledgers", ex.Message);
    }

    /// <summary>An empty custom group IS deletable; a predefined one never is.</summary>
    [Fact]
    public void An_empty_custom_group_is_deletable_and_a_predefined_group_is_not()
    {
        var c = Seed();
        var empty = new Group(Guid.NewGuid(), "Spare", GroupNature.Asset, c.FindGroupByName("Current Assets")!.Id);
        c.AddGroup(empty);

        MasterDeletionRules.EnsureGroupDeletable(c, empty);   // does not throw

        var predefined = c.FindGroupByName("Sundry Debtors")!;
        Assert.True(predefined.IsPredefined);
        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureGroupDeletable(c, predefined));
        Assert.Contains("predefined group and cannot be deleted", ex.Message);
    }

    /// <summary>
    /// A stock item is refused while any entry names it, and the refusal counts the categories separately. An
    /// item-invoice line and an opening balance are both Guid pointers into the item and both would be orphaned.
    /// </summary>
    [Fact]
    public void A_stock_item_in_use_is_refused_with_the_entry_count()
    {
        var c = Seed();
        var group = new StockGroup(Guid.NewGuid(), "Hardware");
        c.AddStockGroup(group);
        var unit = Unit.Simple(Guid.NewGuid(), "Nos", "Numbers");
        c.AddUnit(unit);
        var godown = c.Godowns.First();
        var item = new StockItem(Guid.NewGuid(), "Widget", group.Id, unit.Id);
        c.AddStockItem(item);

        c.AddStockOpeningBalance(new StockOpeningBalance(
            Guid.NewGuid(), item.Id, godown.Id, 10m, Money.FromRupees(100m)));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureStockItemDeletable(c, item));

        Assert.Contains("1 entry references it", ex.Message);
        Assert.Contains("1 opening balance", ex.Message);
        Assert.Contains("Widget", ex.Message);
    }

    /// <summary>An unused stock item IS deletable — the positive control for the stock-item guard.</summary>
    [Fact]
    public void An_unused_stock_item_is_deletable()
    {
        var c = Seed();
        var group = new StockGroup(Guid.NewGuid(), "Hardware");
        c.AddStockGroup(group);
        var unit = Unit.Simple(Guid.NewGuid(), "Nos", "Numbers");
        c.AddUnit(unit);
        var item = new StockItem(Guid.NewGuid(), "Spare Widget", group.Id, unit.Id);
        c.AddStockItem(item);

        MasterDeletionRules.EnsureStockItemDeletable(c, item);   // does not throw
    }

    // =====================================================================================================
    //  🔴🔴 ITEM 5 — NUMBERING. THE TWO TESTS THAT MUST BE READ TOGETHER.
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>THE RED-PROOF FOR THE NUMBERING GUARD (plan.md §5, decision D-3).</b>
    ///
    /// <para><b>The defect this slice would otherwise CREATE.</b> <c>LedgerService.NextNumber</c> is
    /// <c>max + 1</c> computed by SCANNING the vouchers — there is no stored counter and no
    /// <c>last_used_number</c> column anywhere in the schema. So deleting the highest-numbered voucher of a type
    /// makes the next post REUSE its number. The project's own shipped numbering doctrine
    /// (<c>VoucherNumberingConfigViewModel.IsFiledDocument</c>) already holds that a filed document number is
    /// permanently burned and never reusable. Until this slice the two could not collide, because
    /// <b>nothing called <c>Delete</c></b>. This slice is what makes them collide.</para>
    ///
    /// <para><b>The fix under test:</b> Delete is REFUSED on a filed statutory document and <b>Cancel is
    /// OFFERED</b> — no numbering floor, no counter table, no schema change. Cancel keeps the voucher in
    /// <c>Company.Vouchers</c>, so it keeps counting toward <c>max</c> and the number is never handed out twice.
    /// The test asserts the remedy is named, because a refusal with no way forward is what makes an operator
    /// reach for a workaround.</para>
    ///
    /// <para><b>Order matters and is asserted here.</b> A filed document ALSO carries an e-invoice record, so the
    /// referential guard would refuse it too — with a count and no remedy. If the guards were ordered the other
    /// way this test goes red on the "Cancel it instead" assertion. That is deliberate: it is what makes the
    /// numbering guard falsifiable rather than shadowed by its neighbour.</para>
    /// </summary>
    [Fact]
    public void Deleting_a_FILED_statutory_document_is_refused_and_Cancel_is_offered_instead()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        AttachGeneratedIrn(c, invoice.Id);

        Assert.True(MasterDeletionRules.IsFiledStatutoryDocument(c, invoice.Id));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));

        Assert.Contains("filed statutory document", ex.Message);
        Assert.Contains("Cancel it instead (Alt+X)", ex.Message);
        // The voucher is untouched and its number is still in sequence.
        Assert.NotNull(c.FindVoucher(invoice.Id));
        Assert.Equal(1, invoice.Number);
    }

    /// <summary>
    /// A <c>Cancelled</c> e-invoice is ALSO a filed document for numbering purposes, even though the standing
    /// ruling lets ALTERATION proceed on one. The IRN was reported to the IRP and the document number is
    /// permanently burned either way; the voucher's CONTENT is what stops being filed when the IRN is cancelled,
    /// not its number. The difference between the two verbs is deliberate and this test is where it is recorded.
    /// </summary>
    [Fact]
    public void A_CANCELLED_einvoice_still_counts_as_filed_for_numbering()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), invoice.Id, "INV/1", EInvoiceStatus.Cancelled,
            irn: new string('b', 64), ackNo: "112210000123456", ackDate: On,
            signedQr: null, signedJson: null, cancelledOn: On.AddDays(1), cancelReasonCode: "1"));

        Assert.True(MasterDeletionRules.IsFiledStatutoryDocument(c, invoice.Id));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));
        Assert.Contains("filed statutory document", ex.Message);
    }

    /// <summary>
    /// 🔴🔴 <b>THE ACCEPTED RESIDUAL — THIS TEST PINS A BEHAVIOUR WE HAVE DECIDED TO SHIP, NOT A DEFECT.</b>
    ///
    /// <para><b>What it asserts:</b> post 1…10 of a voucher type, delete #10 (the highest-numbered, and NOT a
    /// filed document), post again — and the new voucher takes <b>10</b>. The number is REUSED.</para>
    ///
    /// <para><b>Why that is correct behaviour and not a bug.</b> Standing decision D-3 protects a deleted
    /// voucher's number by REFUSING THE DELETE on a filed statutory document, and by nothing else. The residual
    /// was put to the user, adopted as recommended, and recorded: <i>deleting the highest-numbered voucher that is
    /// NOT filed still reuses its number.</i> It is defensible because an unfiled document number has no statutory
    /// life, and it is what the engine's own "may leave a gap" behaviour implies for the mid-sequence case. It is
    /// written into the census as a stated behaviour so a reader meets it here rather than discovering it in a
    /// customer's book.</para>
    ///
    /// <para><b>🔴 DO NOT "FIX" THIS TEST BY INVERTING IT.</b> A test asserting that a deleted non-filed top
    /// number is never reused would assert the OPPOSITE of the ruling and cannot pass against this design.</para>
    ///
    /// <para><b>What would change it, named so the next slice knows the price:</b> teaching
    /// <c>LedgerService.NextNumber</c> a stored per-type numbering FLOOR (a <c>last_used_number</c> column or a
    /// counter table). There is nowhere to keep one without a schema version, and D-3 explicitly forbids that in
    /// this pass. If a later slice adds the floor, THIS test is the one that must change, and changing it is then
    /// the correct act rather than a regression.</para>
    /// </summary>
    [Fact]
    public void Deleting_the_highest_NON_FILED_number_REUSES_it_which_is_accepted_behaviour_under_D3()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        for (var i = 0; i < 10; i++) PostJournal(c, 100m + i, On.AddDays(i));

        var tenth = c.Vouchers.Single(v => v.TypeId == journal.Id && v.Number == 10);
        Assert.Equal(11, svc.NextNumber(journal.Id));

        // It is NOT filed — which is exactly why the guard lets it go.
        Assert.False(MasterDeletionRules.IsFiledStatutoryDocument(c, tenth.Id));
        MasterDeletionRules.EnsureVoucherDeletable(c, tenth);   // accepted, deliberately

        svc.Delete(tenth.Id);

        // max drops to 9, so the next number is 10 again — the accepted residual.
        Assert.Equal(10, svc.NextNumber(journal.Id));
        var reused = PostJournal(c, 999m, On.AddDays(20));
        Assert.Equal(10, reused.Number);
        Assert.NotEqual(tenth.Id, reused.Id);
    }

    /// <summary>
    /// The MID-sequence half of the same story, for contrast: deleting #5 of 1…10 leaves <c>max</c> at 10, so the
    /// next post takes 11 and the gap at 5 is permanent. This is the case the engine's doc comment always
    /// described; the test above is the case it silently missed.
    /// </summary>
    [Fact]
    public void Deleting_a_MID_sequence_voucher_leaves_a_permanent_gap_and_no_reuse()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        for (var i = 0; i < 10; i++) PostJournal(c, 100m + i, On.AddDays(i));

        var fifth = c.Vouchers.Single(v => v.TypeId == journal.Id && v.Number == 5);
        MasterDeletionRules.EnsureVoucherDeletable(c, fifth);
        svc.Delete(fifth.Id);

        Assert.Equal(11, svc.NextNumber(journal.Id));
        var next = PostJournal(c, 999m, On.AddDays(20));
        Assert.Equal(11, next.Number);
        Assert.DoesNotContain(c.Vouchers.Where(v => v.TypeId == journal.Id).Select(v => v.Number), n => n == 5);
    }

    /// <summary>
    /// And the CANCEL contrast that makes D-3's remedy real rather than rhetorical: cancelling the highest-numbered
    /// voucher keeps it in <c>Company.Vouchers</c>, so it keeps counting toward <c>max</c> and the next post takes
    /// 11. This is why "Cancel it instead" is a sufficient answer to the numbering problem and no counter table is
    /// needed.
    /// </summary>
    [Fact]
    public void Cancelling_the_highest_numbered_voucher_never_reuses_its_number()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        for (var i = 0; i < 10; i++) PostJournal(c, 100m + i, On.AddDays(i));
        var tenth = c.Vouchers.Single(v => v.TypeId == journal.Id && v.Number == 10);

        svc.Cancel(tenth.Id);

        Assert.Equal(11, svc.NextNumber(journal.Id));
        Assert.Equal(10, tenth.Number);          // the number is KEPT
        Assert.True(tenth.Cancelled);
    }
}
