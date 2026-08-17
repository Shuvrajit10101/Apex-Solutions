using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
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
    /// 🔴 <b>THIS TEST WAS INVERTED, AND THE INVERSION IS THE FIX.</b> It used to assert that a §34 link whose note
    /// voucher had been CANCELLED blocks nothing — a "liveness" exemption borrowed from
    /// <c>ChallanReconciliation.ChallanHasLiveVoucher</c>, justified by "the guard would refuse forever on a note
    /// the operator had already voided, with no route out".
    ///
    /// <para><b>What that green test was actually pinning.</b> <c>gst_cdn_links.original_invoice_voucher_id</c> is
    /// <c>REFERENCES vouchers(id)</c>, the store runs <c>PRAGMA foreign_keys = ON</c>, and Save is delete-all +
    /// re-insert. Cancelling a note sets a flag on the note VOUCHER; it does not remove the LINK row. So the
    /// exemption let the operator delete the original invoice, the deletion committed to memory, and the next Save
    /// raised <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> with the invoice already gone — after which the open company
    /// could never be saved again by any screen. The borrowed analogy was wrong because
    /// <c>ChallanReconciliation</c> is a REPORT that looks a voucher up and skips it; a foreign key cannot skip
    /// anything.</para>
    ///
    /// <para><b>The "no route out" problem was real and is answered differently:</b> the remedy is to cancel the
    /// INVOICE (Alt+X), which the refusal now names, rather than to delete it out from under a live row.</para>
    /// </summary>
    [Fact]
    public void A_section34_link_blocks_the_original_invoice_even_when_the_note_is_cancelled()
    {
        var c = Seed();
        var invoice = PostJournal(c);
        var note = PostJournal(c, 500m, On.AddDays(5));
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, invoice.Id, null, On, "01"));

        new LedgerService(c).Cancel(note.Id);
        Assert.True(c.FindVoucher(note.Id)!.Cancelled);          // the note is voided…
        Assert.Single(c.CreditDebitNoteLinks);                    // …and the LINK ROW is still there

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));
        Assert.Contains("1 credit/debit note issued against it", ex.Message);
        Assert.Equal(1, MasterDeletionRules.CountVoucherReferences(c, invoice.Id));
    }

    /// <summary>
    /// The NOTE side of the same link — <c>gst_cdn_links.cdn_voucher_id</c>, which is <c>NOT NULL</c> — blocks
    /// deleting the note itself, at BOTH note statuses. This category had no test at all: it could be deleted
    /// outright and both the engine and the desktop suites stayed green, while removing it silently produced the
    /// same unsavable company.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_section34_link_on_a_note_blocks_deleting_the_note_at_either_status(bool cancelTheNote)
    {
        var c = Seed();
        var invoice = PostJournal(c);
        var note = PostJournal(c, 500m, On.AddDays(5));
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, invoice.Id, null, On, "01"));
        if (cancelTheNote) new LedgerService(c).Cancel(note.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, note));
        Assert.Contains("1 §34 credit/debit-note link on it", ex.Message);
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
        // 🔴 The e-invoice is GENERATED, not Pending. It used to be Pending and the guard-order assertion below
        // still passed — because the old e-Way half read ANY non-Cancelled record as filed, including the Pending
        // one added on the next line. That was the defect (a merely-staged EWB-01, and a portal REJECTION, were
        // both reported as "a filed statutory document"). With the e-Way half fixed, the FILED signal has to come
        // from a document that genuinely reached the portal, which is what this record now is.
        AttachGeneratedIrn(c, invoice.Id, "INV/7");
        c.AddEWayBillRecord(new EWayBillRecord(
            Guid.NewGuid(), invoice.Id, "INV/7", "Outward", "Supply", "INV", 500000L, "27", "29"));

        var described = MasterDeletionRules.DescribeVoucherReferences(c, invoice.Id);
        var counted = MasterDeletionRules.CountVoucherReferences(c, invoice.Id);

        // Five categories hold this voucher (it is the original of the note, not the note itself), one row each.
        Assert.Equal(5, described.Count);
        Assert.Equal(5, counted);

        // 🔴 GUARD ORDER, asserted here because this setup is the one that exposes it: the voucher carries a
        // GENERATED e-invoice, so it is a FILED document and the NUMBERING guard answers first — with the remedy,
        // not with the count. This assertion replaced one that expected the referential message and went red, which
        // is the ordering working as designed rather than a defect.
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

    // =====================================================================================================
    //  🔴 THE FOREIGN-KEY SURFACE — every sibling column, one case each
    //
    //  These exist because the first cut of the guard counted FIVE of the sixteen columns that reference
    //  vouchers(id) and FIVE of the eleven that reference stock_items(id). Every omission was reachable, and
    //  every one of them produced the SAME failure: the master leaves memory, the next Save raises
    //  SQLITE_CONSTRAINT_FOREIGNKEY, and the open company can never be saved again by any screen. A category
    //  with no test of its own can be deleted outright with both suites green — which is exactly how the two
    //  categories S4 originally ADDED came to be dead guards.
    // =====================================================================================================

    private static Voucher SecondJournal(Company c) => PostJournal(c, 250m, On.AddDays(3));

    /// <summary>Attaches exactly ONE row of the named sibling category against <paramref name="v"/>.</summary>
    private static void AttachVoucherReference(Company c, Voucher v, string category)
    {
        switch (category)
        {
            case "rcm_documents.source_voucher_id":
                c.AddRcmDocument(new RcmDocument(Guid.NewGuid(), RcmDocumentKind.SelfInvoice, v.Id, 1, On));
                break;
            case "gst_setoff_lines.voucher_id":
                c.AddGstSetoffLine(new GstSetoffLine(
                    Guid.NewGuid(), v.Id, "042024", GstTaxHead.Integrated, GstTaxHead.Central, isCash: false, 10_000L));
                break;
            case "itc_reversals.source_voucher_id":
                c.AddItcReversal(new ItcReversal(
                    Guid.NewGuid(), ItcReversalRule.Rule42, "042024", 100L, 100L, 0L, 0L, null, null,
                    v.Id, null, SecondJournal(c).Id, null, null, Table4bBucket.Table4B1, DateTimeOffset.UnixEpoch));
                break;
            case "itc_reversals.reversal_voucher_id":
                c.AddItcReversal(new ItcReversal(
                    Guid.NewGuid(), ItcReversalRule.Rule42, "042024", 100L, 100L, 0L, 0L, null, null,
                    null, null, v.Id, null, null, Table4bBucket.Table4B1, DateTimeOffset.UnixEpoch));
                break;
            case "gstr2b_recon.matched_voucher_id":
                c.AddGstr2bReconResult(new Gstr2bReconResult(
                    Guid.NewGuid(), Guid.NewGuid(), ReconBucket.Matched, v.Id, 0L, 0L));
                break;
            case "gst_advance_receipts.receipt_voucher_id":
                c.AddAdvanceReceipt(new GstAdvanceReceipt(
                    Guid.NewGuid(), v.Id, isService: true, Money.FromRupees(1000m), 1800, interState: false, "19", Money.FromRupees(180m)));
                break;
            case "gst_advance_receipts.adjusted_against_invoice_vid":
                c.AddAdvanceReceipt(new GstAdvanceReceipt(
                    Guid.NewGuid(), SecondJournal(c).Id, isService: true, Money.FromRupees(1000m), 1800,
                    interState: false, "19", Money.FromRupees(180m), adjustedAgainstInvoiceVoucherId: v.Id));
                break;
            case "gst_advance_receipts.refund_voucher_id":
                c.AddAdvanceReceipt(new GstAdvanceReceipt(
                    Guid.NewGuid(), SecondJournal(c).Id, isService: true, Money.FromRupees(1000m), 1800,
                    interState: false, "19", Money.FromRupees(180m), refundVoucherId: v.Id));
                break;
            case "gst_challans.voucher_id":
                c.AddGstChallan(new GstChallan(
                    Guid.NewGuid(), "24010700012345", null, null, On, GstTaxHead.Central, GstMinorHead.Tax,
                    Money.FromRupees(500m), v.Id));
                break;
            case "gst_drc03.voucher_id":
                c.AddGstDrc03(new GstDrc03(
                    Guid.NewGuid(), null, "Voluntary", "042024", 100L, 100L, 0L, 0L, 0L, null, v.Id,
                    DateTimeOffset.UnixEpoch));
                break;
            default: throw new InvalidOperationException($"Unknown category '{category}'.");
        }
    }

    /// <summary>
    /// 🔴 <b>ONE CASE PER PREVIOUSLY-UNCOUNTED COLUMN.</b> Ten columns across seven tables — every one of them
    /// named in the design's own §3.3 CARRY table, every one of them a real <c>REFERENCES vouchers(id)</c>, and
    /// every one of them formerly a PERMITTED delete. Each case is independently falsifiable: drop its tally entry
    /// and exactly this case reddens.
    /// </summary>
    [Theory]
    [InlineData("rcm_documents.source_voucher_id", "1 RCM self-invoice / payment voucher")]
    [InlineData("gst_setoff_lines.voucher_id", "1 GST set-off line")]
    [InlineData("itc_reversals.source_voucher_id", "1 ITC reversal taken against it")]
    [InlineData("itc_reversals.reversal_voucher_id", "1 ITC reversal posted by it")]
    [InlineData("gstr2b_recon.matched_voucher_id", "1 GSTR-2B reconciliation match")]
    [InlineData("gst_advance_receipts.receipt_voucher_id", "1 GST advance receipt")]
    [InlineData("gst_advance_receipts.adjusted_against_invoice_vid", "1 GST advance adjusted against it")]
    [InlineData("gst_advance_receipts.refund_voucher_id", "1 GST advance refund")]
    [InlineData("gst_challans.voucher_id", "1 GST challan (PMT-06)")]
    [InlineData("gst_drc03.voucher_id", "1 DRC-03 payment")]
    public void Every_sibling_row_that_holds_a_voucher_Guid_by_foreign_key_refuses_the_delete(
        string category, string expectedPart)
    {
        var c = Seed();
        var v = PostJournal(c);
        AttachVoucherReference(c, v, category);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, v));

        Assert.Contains("1 document references this voucher", ex.Message);
        Assert.Contains(expectedPart, ex.Message);
        Assert.Equal(1, MasterDeletionRules.CountVoucherReferences(c, v.Id));
    }

    /// <summary>
    /// The refusal must name a remedy the product can actually PERFORM. It used to open with "Remove or re-link
    /// them first" — and a machine sweep of <c>src/</c> shows the six removers that exist have exactly one caller
    /// each (the canonical-import rollback journal) and the §34 remover one more (the undo of the entry that
    /// created it). No screen, report or master surface can detach ANY of the categories, so the instruction sent
    /// the operator looking for a screen that does not exist.
    /// </summary>
    [Fact]
    public void The_referential_refusal_never_names_an_action_no_screen_can_perform()
    {
        var c = Seed();
        var v = PostJournal(c);
        AttachVoucherReference(c, v, "gst_challans.voucher_id");

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, v));

        Assert.DoesNotContain("Remove or re-link", ex.Message);
        Assert.Contains("No screen can detach them", ex.Message);
        Assert.Contains("cancel the voucher instead (Alt+X)", ex.Message);
    }

    // =====================================================================================================
    //  🔴 M3-01 — THE ONE REFERENCE CLASS WITH NO FOREIGN KEY, AND THE ONLY WRONG FIGURE IN THE SURFACE
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>THE WRONG-FIGURE CASE.</b> Deleting the invoice that OPENED a bill, while a later receipt still knocks
    /// it off, was PERMITTED — a bill reference is a free string with no foreign key, so neither SQLite nor a
    /// Guid-shaped rule can see it. The save COMMITTED and survived a reopen, and the party's ₹3,000 then sat on
    /// neither Outstandings total while <c>BillWiseTests.Sum_of_open_bills_equals_ledger_closing_balance</c> — which
    /// asserts that equality as a property of the product — stayed green because nothing there deletes.
    ///
    /// <para>The test states the invariant BEFORE the delete, so the reader can see what is being protected, then
    /// asserts the refusal. It fails without the bill-wise tally category.</para>
    /// </summary>
    [Fact]
    public void Deleting_the_invoice_that_opened_a_still_settled_bill_is_refused()
    {
        var c = Seed();
        var debtor = AddParty(c, "Bill Party");
        debtor.MaintainBillByBill = true;
        var sales = c.FindLedgerByName("Sales") ?? AddSales(c, "Sales");
        var cash = c.FindLedgerByName("Cash")!;
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        var invoice = new Voucher(Guid.NewGuid(), journal.Id, On, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        });
        svc.Post(invoice);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, On.AddDays(4), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(3000m), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(3000m)) }),
        }));

        var asOf = On.AddDays(30);
        var openBefore = Outstandings.OpenBillsFor(c, debtor, asOf);
        Assert.Equal(7000m, openBefore.Sum(b => b.Pending.Amount));
        Assert.Equal(7000m, Math.Abs(LedgerBalances.SignedClosing(c, debtor, asOf)));   // the invariant, holding

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, invoice));
        Assert.Contains("1 settlement against a bill it opened", ex.Message);
    }

    /// <summary>
    /// The two halves the bill-wise category must NOT block, so the guard is a rule and not a blanket refusal:
    /// deleting the SETTLING voucher is fine (the bill simply re-opens to its full amount), and an opened bill that
    /// nothing has knocked off yet is fine too.
    /// </summary>
    [Fact]
    public void An_unsettled_bill_and_the_settling_voucher_itself_are_both_deletable()
    {
        var c = Seed();
        var debtor = AddParty(c, "Bill Party");
        debtor.MaintainBillByBill = true;
        var sales = c.FindLedgerByName("Sales") ?? AddSales(c, "Sales");
        var cash = c.FindLedgerByName("Cash")!;
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        var invoice = new Voucher(Guid.NewGuid(), journal.Id, On, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        });
        svc.Post(invoice);

        // Nothing settles it yet.
        MasterDeletionRules.EnsureVoucherDeletable(c, invoice);   // does not throw

        var receipt = new Voucher(Guid.NewGuid(), journal.Id, On.AddDays(4), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(3000m), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(3000m)) }),
        });
        svc.Post(receipt);

        // The settling side is still free to go — the bill just re-opens.
        MasterDeletionRules.EnsureVoucherDeletable(c, receipt);   // does not throw
    }

    // =====================================================================================================
    //  🔴 M3-02 — WHICH e-WAY STATUSES ARE "FILED"
    // =====================================================================================================

    /// <summary>
    /// 🔴 The e-Way half of <see cref="MasterDeletionRules.IsFiledStatutoryDocument"/> used to lean on
    /// <c>FindEWayBillRecordForVoucher</c>, whose only exclusion is <c>Cancelled</c> — so <c>Pending</c> (the
    /// status EVERY record is constructed in), <c>Failed</c> (how a portal REJECTION is recorded) and
    /// <c>NotApplicable</c> were all reported as "a filed statutory document, and a filed document number can never
    /// be reissued". A Pending record has no EWB number at all; there is nothing to burn. Only a status that
    /// received a portal number freezes anything.
    /// </summary>
    [Theory]
    [InlineData(EWayStatus.NotApplicable, false)]
    [InlineData(EWayStatus.Pending, false)]
    [InlineData(EWayStatus.Failed, false)]
    [InlineData(EWayStatus.Generated, true)]
    [InlineData(EWayStatus.Cancelled, true)]
    public void Only_an_eway_bill_that_reached_the_portal_freezes_the_number(EWayStatus status, bool expectFiled)
    {
        var c = Seed();
        var v = PostJournal(c);
        c.AddEWayBillRecord(EWayBillRecord.Rehydrate(
            Guid.NewGuid(), v.Id, "INV/1", status, "Outward", "Supply", "INV", 600000L,
            transporterId: null, mode: null, vehicleNumber: null, distanceKm: 0, transportDocNo: null,
            shipFromStateCode: "19", shipToStateCode: "19", isOverDimensionalCargo: false, shipToGstin: null,
            closureRequested: false, closedOn: null,
            ewbNumber: status is EWayStatus.Generated or EWayStatus.Cancelled ? "123456789012" : null,
            generatedAt: null,
            validUpto: status == EWayStatus.Generated ? DateTimeOffset.UnixEpoch.AddDays(1) : null,
            cancelledOn: status == EWayStatus.Cancelled ? On : null,
            cancelReasonCode: status == EWayStatus.Cancelled ? "1" : null));

        Assert.Equal(expectFiled, MasterDeletionRules.IsFiledStatutoryDocument(c, v.Id));

        // Either way the ROW still blocks — the foreign key does not care whether the portal ever answered.
        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, v));
        Assert.Equal(expectFiled, ex.Message.Contains("filed statutory document"));
        if (!expectFiled) Assert.Contains("1 e-Way Bill record", ex.Message);
    }

    // =====================================================================================================
    //  🔴 M1-04 — THE MASTER GUARDS' OWN FOREIGN-KEY SURFACE
    // =====================================================================================================

    /// <summary>
    /// 🔴 Seven ledger columns that are NOT transactions and were NOT counted. Deleting an "unused" ledger that a
    /// pay head, a POS till, a budget line, an inventory voucher, an additional-cost line or an RCM document still
    /// named emptied the master from memory and made the open company unsavable.
    /// </summary>
    [Theory]
    [InlineData("inventory_vouchers.party_id", "1 inventory voucher")]
    [InlineData("additional_cost_lines.ledger_id", "1 additional-cost line")]
    [InlineData("pos_voucher_type_config.default_party_id", "1 POS till default party")]
    [InlineData("pos_tender_ledger_defaults.ledger_id", "1 POS tender default")]
    [InlineData("pay_heads.ledger_id", "1 pay head")]
    [InlineData("pay_heads.employer_expense_ledger_id", "1 pay head")]
    [InlineData("budget_lines.ledger_id", "1 budget line")]
    [InlineData("rcm_documents.supplier_ledger_id", "1 RCM document")]
    public void A_ledger_named_by_another_master_or_setting_is_refused(string category, string expectedPart)
    {
        var c = Seed();
        var spare = AddParty(c, "Named Elsewhere");
        var sj = c.FindVoucherTypeByName("Journal")!;

        switch (category)
        {
            case "inventory_vouchers.party_id":
                c.AddInventoryVoucher(InventoryVoucher.Order(
                    Guid.NewGuid(), sj.Id, On, Array.Empty<OrderLine>(), partyId: spare.Id));
                break;
            case "additional_cost_lines.ledger_id":
                c.AddInventoryVoucher(InventoryVoucher.StockJournal(
                    Guid.NewGuid(), sj.Id, On, Array.Empty<InventoryAllocation>(),
                    Array.Empty<InventoryAllocation>(),
                    additionalCostLines: new[] { new AdditionalCostLine(spare.Id, Money.FromRupees(10m)) }));
                break;
            case "pos_voucher_type_config.default_party_id":
                c.VoucherTypes[0].PosConfig = new PosConfig { DefaultPartyId = spare.Id };
                break;
            case "pos_tender_ledger_defaults.ledger_id":
            {
                var cfg = new PosConfig();
                cfg.SetTenderLedgerDefault(PosTenderType.Cash, spare.Id);
                c.VoucherTypes[0].PosConfig = cfg;
                break;
            }
            case "pay_heads.ledger_id":
                c.AddPayHead(new PayHead(Guid.NewGuid(), "Basic", PayHeadType.Earnings,
                    PayHeadCalculationType.OnAttendance) { LedgerId = spare.Id });
                break;
            case "pay_heads.employer_expense_ledger_id":
                c.AddPayHead(new PayHead(Guid.NewGuid(), "PF Employer", PayHeadType.EmployersStatutoryContributions,
                    PayHeadCalculationType.AsComputedValue) { EmployerExpenseLedgerId = spare.Id });
                break;
            case "budget_lines.ledger_id":
                c.AddBudget(new Budget(Guid.NewGuid(), "FY", FyStart, FyStart.AddYears(1), null,
                    new[] { BudgetLine.ForLedger(spare.Id, BudgetType.OnClosingBalance, Money.FromRupees(1m)) }));
                break;
            case "rcm_documents.supplier_ledger_id":
                c.AddRcmDocument(new RcmDocument(
                    Guid.NewGuid(), RcmDocumentKind.SelfInvoice, PostJournal(c).Id, 1, On, spare.Id));
                break;
            default: throw new InvalidOperationException($"Unknown category '{category}'.");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, spare));
        Assert.Contains("names it", ex.Message);
        Assert.Contains(expectedPart, ex.Message);
        // It must NOT be dressed up as the attested transaction refusal.
        Assert.DoesNotContain("has already been posted against it", ex.Message);
    }

    /// <summary>
    /// 🔴 A ledger whose ONLY vouchers are CANCELLED is still refused, and the count still includes them. Excluding
    /// cancelled vouchers is what the rest of the codebase does everywhere (<c>LedgerBalances.CountsAsOf</c>,
    /// <c>ItemInvoiceStock.Counts</c>, <c>ChallanReconciliation.ChallanHasLiveVoucher</c>), so it is the edit this
    /// line attracts — and it would be a defect: cancelling sets a flag, while the voucher row and its
    /// <c>entry_lines.ledger_id</c> foreign key survive untouched.
    /// </summary>
    [Fact]
    public void A_ledger_whose_only_vouchers_are_cancelled_is_still_refused_and_they_still_count()
    {
        var c = Seed();
        var v = PostJournal(c);
        new LedgerService(c).Cancel(v.Id);
        var party = c.FindLedgerByName("Acme Traders")!;

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, party));
        Assert.Contains("1 voucher has already been posted against it", ex.Message);
    }

    /// <summary>
    /// A voucher that names the ledger only as its PARTY — <c>vouchers.party_id</c>, a foreign key that appears in
    /// no disclosure at all — counts as a transaction against it.
    /// </summary>
    [Fact]
    public void A_ledger_that_is_only_a_vouchers_party_is_refused()
    {
        var c = Seed();
        var spare = AddParty(c, "Party Only");
        var v = PostJournal(c);
        v.PartyId = spare.Id;

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, spare));
        Assert.Contains("1 voucher has already been posted against it", ex.Message);
    }

    /// <summary>
    /// 🔴 THE ATTESTED REMEDY, restored. The corpus sentence is TWO sentences — <i>"You cannot delete any ledger,
    /// if any transaction(s) has been already made with that ledger. To delete the ledger, delete all the
    /// transactions related to that ledger and then you can delete the ledger."</i> (STUDY-GUIDE PDF p.67) — and the
    /// second half was dropped in the very slice that makes the remedy executable, leaving the ledger refusal the
    /// only one of the four that named no way forward.
    /// </summary>
    [Fact]
    public void The_ledger_refusal_carries_the_corpus_remedy_and_it_is_now_an_action_that_exists()
    {
        var c = Seed();
        PostJournal(c);
        var party = c.FindLedgerByName("Acme Traders")!;

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureLedgerDeletable(c, party));
        Assert.Contains("Delete those vouchers first", ex.Message);
        Assert.Contains("Alt+D", ex.Message);
    }

    /// <summary>Four group columns that were not counted: the company's Profit &amp; Loss head, a pay head filed
    /// under the group, a budget filed under it, and a budget line naming it.</summary>
    [Theory]
    [InlineData("pay_heads.under_group_id", "1 pay head")]
    [InlineData("budgets.under_id", "1 budget")]
    [InlineData("budget_lines.group_id", "1 budget line")]
    public void A_group_named_by_another_master_is_refused(string category, string expectedPart)
    {
        var c = Seed();
        var spare = new Group(Guid.NewGuid(), "Spare Head", GroupNature.Asset,
                              c.FindGroupByName("Current Assets")!.Id);
        c.AddGroup(spare);

        switch (category)
        {
            case "pay_heads.under_group_id":
                c.AddPayHead(new PayHead(Guid.NewGuid(), "Basic", PayHeadType.Earnings,
                    PayHeadCalculationType.OnAttendance) { UnderGroupId = spare.Id });
                break;
            case "budgets.under_id":
                c.AddBudget(new Budget(Guid.NewGuid(), "FY", FyStart, FyStart.AddYears(1), spare.Id));
                break;
            case "budget_lines.group_id":
                c.AddBudget(new Budget(Guid.NewGuid(), "FY", FyStart, FyStart.AddYears(1), null,
                    new[] { BudgetLine.ForGroup(spare.Id, BudgetType.OnClosingBalance, Money.FromRupees(1m)) }));
                break;
            default: throw new InvalidOperationException($"Unknown category '{category}'.");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureGroupDeletable(c, spare));
        Assert.Contains(expectedPart, ex.Message);
    }

    /// <summary>
    /// The company's Profit &amp; Loss head (<c>companies.profit_and_loss_head_id</c>) is refused outright, with no
    /// count — it is a single reserved slot and a number would tell the operator nothing.
    ///
    /// <para>The seeded head carries <c>IsPredefined</c>, so the predefined refusal already covers the default
    /// book. This clause exists for the case that flag does NOT cover: <c>Company.SetProfitAndLossHead</c> is
    /// public and takes any group, so a custom group can occupy the slot — and then the FOREIGN KEY
    /// <c>companies.profit_and_loss_head_id</c> is all that stands between Delete and a company row pointing at a
    /// group that is gone. The test sets it exactly that way, which is what makes the clause falsifiable rather
    /// than shadowed by its neighbour.</para>
    /// </summary>
    [Fact]
    public void A_custom_group_serving_as_the_profit_and_loss_head_cannot_be_deleted()
    {
        var c = Seed();
        var custom = new Group(Guid.NewGuid(), "Trading Result", GroupNature.Income,
                               c.FindGroupByName("Primary")?.Id);
        c.AddGroup(custom);
        Assert.False(custom.IsPredefined);
        MasterDeletionRules.EnsureGroupDeletable(c, custom);       // deletable before it takes the slot

        c.SetProfitAndLossHead(custom);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureGroupDeletable(c, custom));
        Assert.Contains("Profit & Loss head and cannot be deleted", ex.Message);
    }

    /// <summary>The group refusal's SINGULAR head, which had no test while the ledger, voucher-tally and
    /// stock-item singulars all had one — "1 masters are filed under it" is the sentence an operator remembers
    /// about the quality of a product.</summary>
    [Fact]
    public void The_single_child_group_refusal_reads_as_singular()
    {
        var c = Seed();
        var parent = new Group(Guid.NewGuid(), "Regional", GroupNature.Asset,
                               c.FindGroupByName("Current Assets")!.Id);
        c.AddGroup(parent);
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Only Child", parent.Id, Money.Zero, openingIsDebit: true));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureGroupDeletable(c, parent));
        Assert.Contains("1 master is filed under it", ex.Message);
        Assert.DoesNotContain("1 masters", ex.Message);
    }

    /// <summary>
    /// 🔴 Six stock-item columns that were not counted. The disclosure that named them called the consequence "a
    /// dangling BOM line"; measured, deleting a component named only by a BOM emptied the master from memory,
    /// threw <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> and left the open company permanently unsavable.
    /// </summary>
    [Theory]
    [InlineData("batch_masters.stock_item_id", "1 batch")]
    [InlineData("bill_of_materials.stock_item_id", "1 bill of materials")]
    [InlineData("bom_lines.component_stock_item_id", "1 bill-of-materials component line")]
    [InlineData("price_lists.stock_item_id", "1 price list")]
    [InlineData("job_work_orders.fg_stock_item_id", "1 job-work order")]
    [InlineData("job_work_order_lines.component_stock_item_id", "1 job-work component line")]
    public void A_stock_item_named_by_a_descriptive_master_is_refused(string category, string expectedPart)
    {
        var c = Seed();
        var (item, other) = TwoStockItems(c);
        var vt = c.FindVoucherTypeByName("Journal")!;

        switch (category)
        {
            case "batch_masters.stock_item_id":
                c.AddBatchMaster(new BatchMaster(Guid.NewGuid(), item.Id, "B-001"));
                break;
            case "bill_of_materials.stock_item_id":
                c.AddBillOfMaterials(new BillOfMaterials(Guid.NewGuid(), item.Id, "BOM-1", 1m,
                    new[] { new BomLine(BomLineType.Component, other.Id, 4m) }));
                break;
            case "bom_lines.component_stock_item_id":
                c.AddBillOfMaterials(new BillOfMaterials(Guid.NewGuid(), other.Id, "BOM-1", 1m,
                    new[] { new BomLine(BomLineType.Component, item.Id, 4m) }));
                break;
            case "price_lists.stock_item_id":
            {
                var level = new PriceLevel(Guid.NewGuid(), "Retail");
                c.AddPriceLevel(level);
                c.AddPriceList(new PriceList(Guid.NewGuid(), level.Id, item.Id, On,
                    new[] { new PriceListSlab(1m, null, Money.FromRupees(10m), 0) }));
                break;
            }
            case "job_work_orders.fg_stock_item_id":
                c.AddInventoryVoucher(InventoryVoucher.JobWork(Guid.NewGuid(), vt.Id, On,
                    new JobWorkOrder(JobWorkDirection.Out, "JW/1", item.Id, 1m,
                        new[] { new JobWorkOrderLine(other.Id, JobWorkComponentTrack.PendingToIssue, 2m) })));
                break;
            case "job_work_order_lines.component_stock_item_id":
                c.AddInventoryVoucher(InventoryVoucher.JobWork(Guid.NewGuid(), vt.Id, On,
                    new JobWorkOrder(JobWorkDirection.Out, "JW/1", other.Id, 1m,
                        new[] { new JobWorkOrderLine(item.Id, JobWorkComponentTrack.PendingToIssue, 2m) })));
                break;
            default: throw new InvalidOperationException($"Unknown category '{category}'.");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureStockItemDeletable(c, item));
        Assert.Contains("names it", ex.Message);
        Assert.Contains(expectedPart, ex.Message);
        Assert.DoesNotContain("entries reference it", ex.Message);
    }

    /// <summary>
    /// 🔴 The two ENTRY categories S4 itself added were each individually deletable with both suites green — only
    /// the pre-existing opening-balance category was pinned. One case each, so a category can no longer be zeroed
    /// in silence.
    /// </summary>
    [Theory]
    [InlineData("voucher_inventory_lines", "1 invoice line")]
    [InlineData("allocations", "1 inventory-voucher line")]
    [InlineData("destination_allocations", "1 inventory-voucher line")]
    [InlineData("order_lines", "1 inventory-voucher line")]
    [InlineData("physical_lines", "1 inventory-voucher line")]
    public void Every_entry_category_of_the_stock_item_guard_is_pinned(string category, string expectedPart)
    {
        var c = Seed();
        var (item, _) = TwoStockItems(c);
        var godown = c.Godowns.First();
        var vt = c.FindVoucherTypeByName("Journal")!;

        switch (category)
        {
            case "voucher_inventory_lines":
            {
                var party = c.FindLedgerByName("Acme Traders") ?? AddParty(c, "Acme Traders");
                var sales = c.FindLedgerByName("Sales") ?? AddSales(c, "Sales");
                var sales2 = c.FindVoucherTypeByName("Sales")!;
                new LedgerService(c).Post(new Voucher(Guid.NewGuid(), sales2.Id, On, new[]
                {
                    new EntryLine(party.Id, Money.FromRupees(100m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(100m), DrCr.Credit),
                }, inventoryLines: new[]
                {
                    new VoucherInventoryLine(item.Id, godown.Id, 1m, Money.FromRupees(100m)),
                }));
                break;
            }
            case "allocations":
                c.AddInventoryVoucher(new InventoryVoucher(Guid.NewGuid(), vt.Id, On,
                    new[] { new InventoryAllocation(item.Id, godown.Id, 1m, StockDirection.Outward) }));
                break;
            case "destination_allocations":
                c.AddInventoryVoucher(InventoryVoucher.StockJournal(Guid.NewGuid(), vt.Id, On,
                    Array.Empty<InventoryAllocation>(),
                    new[] { new InventoryAllocation(item.Id, godown.Id, 1m, StockDirection.Inward) }));
                break;
            case "order_lines":
                c.AddInventoryVoucher(InventoryVoucher.Order(Guid.NewGuid(), vt.Id, On,
                    new[] { new OrderLine(item.Id, godown.Id, 1m, Money.FromRupees(100m)) }));
                break;
            case "physical_lines":
                c.AddInventoryVoucher(InventoryVoucher.PhysicalStock(Guid.NewGuid(), vt.Id, On,
                    new[] { new PhysicalStockLine(item.Id, godown.Id, 1m, null) }));
                break;
            default: throw new InvalidOperationException($"Unknown category '{category}'.");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureStockItemDeletable(c, item));
        Assert.Contains("1 entry references it", ex.Message);
        Assert.Contains(expectedPart, ex.Message);
    }

    /// <summary>Two stock items sharing a group and a unit — a "subject" and a "neighbour", so a BOM or a job-work
    /// order can name one from the other.</summary>
    private static (StockItem Subject, StockItem Other) TwoStockItems(Company c)
    {
        var group = new StockGroup(Guid.NewGuid(), "Hardware");
        c.AddStockGroup(group);
        var unit = Unit.Simple(Guid.NewGuid(), "Nos", "Numbers");
        c.AddUnit(unit);
        var subject = new StockItem(Guid.NewGuid(), "Widget", group.Id, unit.Id);
        var other = new StockItem(Guid.NewGuid(), "Bolt", group.Id, unit.Id);
        c.AddStockItem(subject);
        c.AddStockItem(other);
        return (subject, other);
    }

    // =====================================================================================================
    //  THE OPERATOR-FACING IDENTITY
    // =====================================================================================================

    /// <summary>
    /// The refusal names the voucher NUMBER, not just its type and date. The number is the half that tells the
    /// operator WHICH document, and the delete prompt is built from the same string — yet nothing asserted it, so
    /// forcing the number part to empty left every refusal reading "Receipt dated 15-Apr-2024" with the whole
    /// engine suite green.
    /// </summary>
    [Fact]
    public void A_refusal_names_the_voucher_number_and_not_only_its_type_and_date()
    {
        var c = Seed();
        var first = PostJournal(c);
        var second = PostJournal(c, 600m, On.AddDays(1));
        AttachGeneratedIrn(c, second.Id, "INV/2");
        Assert.Equal(2, second.Number);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureVoucherDeletable(c, second));

        Assert.Contains("No. 2", ex.Message);
        Assert.DoesNotContain($"Journal dated", ex.Message);
        Assert.NotEqual(first.Number, second.Number);
    }

    // =====================================================================================================
    //  THE COVERAGE DECLARATION ITSELF
    // =====================================================================================================

    /// <summary>
    /// The declared coverage list is well-formed: no duplicates, no overlap with the "dies with its parent" list,
    /// and every entry in <c>table.column</c> shape. The list is compared against the SCHEMA in the persistence
    /// suite (<c>MasterDeletionForeignKeyCoverageTests</c>); this is the cheap structural half that belongs beside
    /// the rules themselves.
    /// </summary>
    [Fact]
    public void The_declared_foreign_key_coverage_is_well_formed()
    {
        var guarded = MasterDeletionRules.GuardedForeignKeyColumns;
        var children = MasterDeletionRules.ForeignKeyColumnsThatDieWithTheirParent;

        Assert.Equal(guarded.Count, guarded.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(children.Count, children.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(guarded.Intersect(children, StringComparer.Ordinal));
        Assert.All(guarded.Concat(children), s => Assert.Equal(2, s.Split('.').Length));
    }
}
