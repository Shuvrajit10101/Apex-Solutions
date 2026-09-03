using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The shared guards for the <b>Delete</b> verb (Phase 10.11 S4 / VL-2). Deliberately built on the
/// <see cref="MasterAlterationRules"/> shape — <b>every method THROWS <see cref="InvalidOperationException"/> and
/// none of them mutates the company</b> — rather than inventing a second convention for the same job. Pure,
/// framework- and DB-agnostic, and directly unit-tested.
///
/// <para><b>🔴 WHY THIS FILE IS THE DANGEROUS HALF OF S4.</b> <c>LedgerService.Delete</c> has existed since Phase 1
/// and, until this slice, <b>nothing in the application called it</b>. S4 is what makes it reachable, so every
/// consequence of removing a posted voucher arrives with this slice and not with the engine that has silently
/// carried it for ten phases. <b>Scope, corrected:</b> S4 reaches the ACCOUNTING half only.
/// <c>InventoryPostingService.Delete</c> — which carries a byte-identical copy of the numbering behaviour — is
/// <b>still unreachable</b>: the only two surfaces that hand a Guid to the delete verb are the Day Book row builder
/// and the ledger register, and both iterate <c>Company.Vouchers</c>. There is deliberately <b>no</b>
/// <c>EnsureInventoryVoucherDeletable</c> here, so the slice that adds an inventory register drill owes one before
/// it wires the key rather than inheriting a comment that implies it is already covered.</para>
///
/// <para><b>🔴 CONSEQUENCE 1 — NUMBER REUSE (the numbering guard).</b> <c>LedgerService.NextNumber</c> is
/// <c>max + 1</c> computed by <b>scanning the vouchers</b>; there is no stored counter and no
/// <c>last_used_number</c> column anywhere in the schema. So deleting the <b>highest-numbered</b> voucher of a type
/// makes the next post <b>REUSE its number</b> — two different documents, at different times, carrying the same
/// tax-invoice number, with the first no longer on the books to prove which was which. The engine's own doc comment
/// says Delete "may leave a gap in numbering", which describes the MID-sequence case and misses this one.
/// <br/><b>The ruling (plan.md §5, decision D-3): refuse Delete on a filed statutory document and offer Cancel
/// instead. No numbering floor, no counter table, no schema change.</b> Cancel keeps the voucher in
/// <c>Company.Vouchers</c>, so it keeps counting toward <c>max</c> and the number is never handed out twice.
/// See <see cref="EnsureVoucherDeletable"/> and <see cref="IsFiledStatutoryDocument"/>.</para>
///
/// <para><b>🔴 THE ACCEPTED RESIDUAL, RECORDED HERE SO IT IS NEVER DISCOVERED INSTEAD.</b> Refusing only the
/// FILED documents means <b>deleting the highest-numbered voucher that is NOT filed still reuses its number.</b>
/// That is a KNOWN AND ACCEPTED behaviour under D-3 — an unfiled document number has no statutory life — and it is
/// pinned by a test (<c>MasterDeletionRulesTests</c>, the "accepted residual" case) precisely so it cannot become
/// an unrecorded surprise. What would change it is a stored numbering floor, which needs a schema version and is
/// explicitly deferred.</para>
///
/// <para><b>🔴 CONSEQUENCE 2 — A COMPANY THAT CAN NEVER BE SAVED AGAIN (the referential guard). The first cut of
/// this file called this an "orphan pointer" and that was the wrong severity by a wide margin.</b> Every record
/// that lives BESIDE a voucher and points at it by <see cref="Guid"/> is persisted as a row with a real
/// <c>REFERENCES vouchers(id)</c> foreign key, the store runs <c>PRAGMA foreign_keys = ON</c> on every connection,
/// and Save is a <b>delete-all + full re-insert</b>. So a sibling row the guard fails to count does not dangle
/// quietly: the very next Save raises <c>SQLITE_CONSTRAINT_FOREIGNKEY</c>, the row is already gone from the
/// in-memory aggregate, and <b>every later save on the open company throws too</b>. The guard is not a nicety and
/// it is not about report cosmetics — it is the only thing standing between Delete and an unsavable book.
/// <br/><b>Therefore the tally is driven off the schema's FK inventory, not off a hand-picked list.</b>
/// <see cref="GuardedForeignKeyColumns"/> names every column, and a test in the persistence suite asserts that set
/// equals what the schema actually declares, so a table added later cannot slip past this file in silence.</para>
///
/// <para><b>🔴 AND ONE REFERENCE CLASS THAT NO FOREIGN KEY CAN SEE.</b> A bill-wise reference is a free
/// <b>string</b> (<c>bill_allocations.name</c>, no FK of any kind), so deleting the invoice that OPENED a bill
/// while a later receipt still knocks it off is invisible to both SQLite and to a Guid-shaped rule. It COMMITS —
/// and the party's money then appears on neither Outstandings total, with the suite's own documented
/// <i>Σ open bills == ledger closing balance</i> invariant silently false. It is the only route in this surface
/// that produces a wrong FIGURE with a successful save; every FK case fails loudly instead. Hence
/// <see cref="CountBillSettlementsAgainstBillsOpenedBy"/> is a tally category in its own right.</para>
///
/// <para><b>The master side (corpus-attested, unlike the voucher side).</b> The Study Guide states outright that
/// <i>"You cannot delete any ledger, if any transaction(s) has been already made with that ledger. To delete the
/// ledger, delete all the transactions related to that ledger and then you can delete the ledger."</i>
/// (STUDY-GUIDE PDF p.67 — both sentences, re-verified first-hand). <see cref="EnsureLedgerDeletable"/> is that
/// rule, refusing with the count AND carrying the attested remedy — which is a real instruction for the first time
/// precisely because this slice is the one that ships the delete verb the remedy names.
/// <see cref="EnsureGroupDeletable"/> and <see cref="EnsureStockItemDeletable"/> are the same shape for the two
/// other master kinds S4 routes Delete from.</para>
///
/// <para><b>🔴 R7 — FIDELITY, AND THE THREE CATEGORIES KEPT APART.</b>
/// <list type="bullet">
///   <item><b>ATTESTED, extended by us:</b> the ledger-with-transactions refusal above is a corpus rule, and so is
///     its remedy sentence. What is ours is the <i>count</i> in the message and the extension of the same shape to
///     groups and stock items.</item>
///   <item>🔴 <b>THE PROMPT COUNT — TWO SEPARATE R7 CLAIMS, SETTLED BY THE USER 2026-08-18. THE BEHAVIOUR IS
///     UNCHANGED (one prompt on all five routes); ONLY THE RECORD IS. Do not merge these two: conflating them is
///     the exact R7 defect a review lens caught on S3.</b>
///     <list type="bullet">
///       <item><b>(A) THE VOUCHER ROUTES — OUR DECISION AGAINST WEAK, SELF-CONTRADICTORY ATTESTATION.</b>
///         BOOK PDF <b>pp.22-23</b> carries a heading reading <i>"How to Delete Voucher …?"</i> directly over
///         <i>"Alt+D &gt; Press Two times Enter"</i>, and the same entry then contradicts itself — its path reads
///         <c>Alter &gt; Voucher type</c>. <b>The attestation is poor, and it EXISTS.</b> We keep ONE prompt and
///         record it as a decision taken <i>against</i> that attestation — explicitly <b>not</b> "corpus silent"
///         and <b>not</b> a decline-to-extend-an-unattested-behaviour.</item>
///       <item><b>(B) THE THREE MASTER ROUTES (ledger, group, stock item) — A DELIBERATE DIVERGENCE FROM AN
///         ATTESTED SCOPE.</b> Here the DOUBLE prompt is cleanly attested: BOOK PDF <b>p.21</b> for a ledger
///         (<i>"… &gt; Alt+D &gt; Press Two times Enter"</i>) and STUDY-GUIDE PDF <b>p.277</b>, with its wording,
///         for a Group Company (<i>"Delete Yes or No?"</i> then <i>"Are you sure Yes or No?"</i>). We ship ONE
///         prompt anyway and record it as a divergence from an attested scope — a different claim, on different
///         evidence, from (A). <i>(Recorded beside it because it narrows the divergence without changing its
///         category: STUDY-GUIDE PDF <b>p.67</b> attests a SINGLE prompt for the same ledger object —
///         <i>"Press Alt+D supply Yes to confirm Deletion"</i>. The ruling categorises this route as a divergence
///         rather than as "a conflict resolved in favour of p.67": we do not get to pick the friendly source and
///         call the result fidelity.)</i></item>
///     </list>
///     <b>SUPERSEDED, kept quoted so the category history is legible:</b> this bullet read
///     <i>"ATTESTED BUT IN CONFLICT — we ship one attested reading and cite it … a conflict resolved in favour of
///     one attested source — a THIRD R7 category"</i>. That single category is replaced by the two above.
///     See <c>MainWindowViewModel.RequestDeleteHighlighted</c>, which carries the same record for the
///     surfaces.</item>
///   <item><b>UNVERIFIED-BY-DESIGN — ours, corpus silent:</b> the referential guard, the numbering guard, the
///     bill-wise guard, the accepted residual, the decision to offer Cancel as the remedy, and every message string
///     in this file. The corpus is silent on what deleting a voucher does to a linked statutory document and silent
///     on what happens to its number.</item>
/// </list></para>
///
/// <para><b>Out of scope by ruling</b> (all recorded in plan.md §5): no audit trail of any kind — after this slice
/// an operator can delete a posted voucher and the books carry no record that it happened; no cascade that deletes
/// the blocking side records for the operator; no un-delete; no numbering floor.</para>
/// </summary>
public static class MasterDeletionRules
{
    // ==================================================================== the FK inventory these guards cover

    /// <summary>
    /// 🔴 <b>EVERY foreign-key column in the schema that points at a voucher, a ledger, a group or a stock item and
    /// that a guard in this file therefore has to count</b>, written as <c>table.column</c>.
    ///
    /// <para><b>Why this list is public and why it is not documentation.</b> The first cut of this file counted
    /// five of the twelve tables holding a voucher's Guid and five of the sixteen holding a stock item's, because
    /// both lists were transcribed by hand from a prose table. Every omission was a company that could never be
    /// saved again (see the summary). <c>MasterDeletionForeignKeyCoverageTests</c> in the persistence suite parses
    /// <c>Schema.CreateV1</c> and asserts the declared FK set equals this list union
    /// <see cref="ForeignKeyColumnsThatDieWithTheirParent"/> — so adding a table with a new
    /// <c>REFERENCES vouchers(id)</c> column turns RED here until somebody decides which bucket it belongs in.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> GuardedForeignKeyColumns =
    [
        // ---- pointing at a VOUCHER (EnsureVoucherDeletable / VoucherReferenceTally)
        "challan_voucher_links.voucher_id",
        "einvoice_records.source_voucher_id",
        "eway_bills.source_voucher_id",
        "gst_advance_receipts.adjusted_against_invoice_vid",
        "gst_advance_receipts.receipt_voucher_id",
        "gst_advance_receipts.refund_voucher_id",
        "gst_cdn_links.cdn_voucher_id",
        "gst_cdn_links.original_invoice_voucher_id",
        "gst_challans.voucher_id",
        "gst_drc03.voucher_id",
        "gst_setoff_lines.voucher_id",
        "gstr2b_recon.matched_voucher_id",
        "itc_reversals.reversal_voucher_id",
        "itc_reversals.source_voucher_id",
        "rcm_documents.source_voucher_id",
        "tcs_challan_voucher_links.voucher_id",

        // ---- pointing at a LEDGER (EnsureLedgerDeletable)
        "additional_cost_lines.ledger_id",
        "budget_lines.ledger_id",
        "entry_lines.ledger_id",
        "inventory_vouchers.party_id",
        "pay_heads.employer_expense_ledger_id",
        "pay_heads.ledger_id",
        "pos_tender_allocations.ledger_id",
        "pos_tender_ledger_defaults.ledger_id",
        "pos_voucher_type_config.default_party_id",
        "rcm_documents.supplier_ledger_id",
        "vouchers.party_id",

        // ---- pointing at a GROUP (EnsureGroupDeletable)
        "budget_lines.group_id",
        "budgets.under_id",
        "companies.profit_and_loss_head_id",
        "groups.parent_id",
        "ledgers.group_id",
        "pay_heads.under_group_id",

        // ---- pointing at a STOCK ITEM (EnsureStockItemDeletable)
        "batch_masters.stock_item_id",
        "bill_of_materials.stock_item_id",
        "bom_lines.component_stock_item_id",
        "inventory_allocations.stock_item_id",
        "job_work_order_lines.component_stock_item_id",
        "job_work_orders.fg_stock_item_id",
        "order_lines.stock_item_id",
        "physical_stock_lines.stock_item_id",
        "price_lists.stock_item_id",
        "stock_opening_balances.stock_item_id",
        "voucher_inventory_lines.stock_item_id",
    ];

    /// <summary>
    /// The FK columns that need <b>no</b> guard because the row is written from the parent's own object graph and
    /// therefore leaves with it on the next delete-all + re-insert. These are exactly the voucher's own children:
    /// its entry lines, its item-invoice lines and its POS tender rows. Nothing else in the schema qualifies —
    /// a stock item's opening balance, for instance, is a top-level row and IS guarded.
    /// </summary>
    public static readonly IReadOnlyList<string> ForeignKeyColumnsThatDieWithTheirParent =
    [
        "entry_lines.voucher_id",
        "pos_tender_allocations.voucher_id",
        "voucher_inventory_lines.voucher_id",
    ];

    // ==================================================================== voucher: the numbering guard (D-3)

    /// <summary>
    /// True when the voucher carries a <b>filed statutory document whose number is legally frozen</b>, and is
    /// therefore refused for deletion by <see cref="EnsureVoucherDeletable"/>.
    ///
    /// <para>The frozen signal is any status that <b>REACHED THE PORTAL AND RECEIVED A NUMBER</b>:
    /// <b>Generated</b> (an IRN / EWB number was issued) OR <b>Cancelled</b> (it was reported and the document
    /// number is permanently burned; a cancelled doc-no is never reusable). <c>Pending</c>, <c>Failed</c> and
    /// <c>NotApplicable</c> never reached the portal and freeze nothing.</para>
    ///
    /// <para>🔴 <b>THE e-WAY HALF WAS WRONG AND IS FIXED HERE.</b> It used to lean on
    /// <c>Company.FindEWayBillRecordForVoucher</c>, whose only exclusion is <c>Cancelled</c>, and treated any hit as
    /// live. But <c>EWayBillRecord</c>'s constructor sets <c>Pending</c> unconditionally, so <b>every</b> record
    /// starts there and <c>MarkFailed</c> is how a <b>portal REJECTION</b> is recorded — meaning a merely-staged
    /// EWB-01, and a request the portal threw out, were both refused with the sentence <i>"it is a filed statutory
    /// document, and a filed document number can never be reissued"</i>. A <c>Pending</c> record has no
    /// <c>EwbNumber</c> at all; there is no number to burn. The status is now tested explicitly rather than
    /// inferred from a finder, and the e-Way half reads exactly like the e-invoice half — which was already
    /// correct. (Those records still BLOCK, at any status, through the referential guard: the row holds the
    /// voucher's Guid and the foreign key does not care whether the portal ever answered.)</para>
    ///
    /// <para><b>Why the two-status test here even though alteration uses a one-status test.</b> The standing
    /// ruling for ALTERATION refuses only <c>Generated</c>, which is defensible — a cancelled IRN's voucher
    /// content is no longer filed. For NUMBERING it is not: the document number stays burned either way. The
    /// difference is deliberate, not an inconsistency.</para>
    /// </summary>
    public static bool IsFiledStatutoryDocument(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (company.EInvoiceRecords.Any(
                r => r.SourceVoucherId == voucherId
                  && r.Status is EInvoiceStatus.Generated or EInvoiceStatus.Cancelled)) return true;

        // Expired is a DERIVED terminal view of a Generated bill and is listed so the set reads as "reached the
        // portal", not as an enumeration of stored ordinals.
        if (company.EWayBillRecords.Any(
                r => r.SourceVoucherId == voucherId
                  && r.Status is EWayStatus.Generated or EWayStatus.Cancelled or EWayStatus.Expired)) return true;

        return false;
    }

    // ==================================================================== voucher: the referential guard

    /// <summary>
    /// The records that point at <paramref name="voucherId"/> and would be left holding a Guid that no longer
    /// resolves, described one category per element ("1 credit/debit-note link", "2 TDS challan links", …). An
    /// empty list means the voucher is referentially free.
    ///
    /// <para><b>Every category is a real foreign key.</b> See <see cref="GuardedForeignKeyColumns"/>: sixteen
    /// columns across <b>twelve</b> tables point at <c>vouchers(id)</c> and are not the voucher's own children. Any
    /// one of them left behind is <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> on the next Save, not a cosmetic dangler.
    /// <br/><b>Corrected 2026-08-18: this read "eleven".</b> Counted off the voucher block of
    /// <see cref="GuardedForeignKeyColumns"/> itself, the distinct tables are <c>challan_voucher_links</c>,
    /// <c>einvoice_records</c>, <c>eway_bills</c>, <c>gst_advance_receipts</c> (three columns),
    /// <c>gst_cdn_links</c> (two), <c>gst_challans</c>, <c>gst_drc03</c>, <c>gst_setoff_lines</c>,
    /// <c>gstr2b_recon</c>, <c>itc_reversals</c> (two), <c>rcm_documents</c> and
    /// <c>tcs_challan_voucher_links</c> — <b>twelve</b>, sixteen columns. <b>The doc comment on
    /// <see cref="GuardedForeignKeyColumns"/> already said "twelve"</b>, so this file disagreed with itself; and
    /// this is the same hand-transcription slip that produced the five-of-twelve blocker that list exists to
    /// prevent. <b>Count them off the list before writing a number here.</b></para>
    ///
    /// <para><b>The §34 link is counted on BOTH of its columns, at any note status.</b> The original-invoice side
    /// used to carry a "live note" exemption that let the invoice of a CANCELLED note be deleted. That exemption
    /// was indefensible: <c>gst_cdn_links.original_invoice_voucher_id</c> is a foreign key, and a cancelled note's
    /// row is still a row. Cancelling changes a flag on the note voucher; it does not remove the link. The note's
    /// OWN side (<c>cdn_voucher_id</c>, <c>NOT NULL</c>) is counted for the same reason and NOT — as an earlier
    /// comment claimed — because a deleted note would leave a dangling GSTR-1 Table-9B pointer. It would not:
    /// <c>Gstr1.BuildTable9B</c>, <c>Gstr1Amendments.BuildTable9C</c> and <c>Gstr3b.ReadCdn</c> each resolve the
    /// note first and <c>continue</c> when it is missing, exactly as <c>ChallanReconciliation</c> does. The report
    /// surface self-heals; the foreign key does not. Stating the real reason is what makes the sibling category's
    /// old exemption visibly wrong rather than merely inconsistent.</para>
    ///
    /// <para><b>e-invoice / e-Way records are counted at ANY status</b>, Cancelled and Failed included: a rejected
    /// or cancelled portal artefact is still a row holding this voucher's Guid.</para>
    /// </summary>
    public static IReadOnlyList<string> DescribeVoucherReferences(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        return VoucherReferenceTally(company, voucherId)
            .Where(t => t.Count > 0)
            .Select(t => Count(t.Count, t.Singular, t.Plural))
            .ToList();
    }

    /// <summary>The number of documents that block deleting <paramref name="voucherId"/> — the figure the refusal
    /// message names. Sums the same per-category tally <see cref="DescribeVoucherReferences"/> renders, so the
    /// count can never disagree with its own breakdown.</summary>
    public static int CountVoucherReferences(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return VoucherReferenceTally(company, voucherId).Sum(t => t.Count);
    }

    /// <summary>
    /// 🔴 <b>THE SINGLE SOURCE for both the count and the breakdown.</b> Written as one list rather than as two
    /// parallel methods on purpose: the first cut of this file counted the categories TWICE, once to describe them
    /// and once to sum them, and a category added to one and not the other would have printed
    /// <i>"2 documents reference this voucher (1 X, 1 Y, 1 Z)"</i> — a refusal contradicting itself in its own
    /// sentence, which no test asserting either half alone would catch.
    /// <see cref="ReferenceCountsAlwaysAgreeWithTheirBreakdown"/> in the test suite pins the agreement, and this
    /// shape is what makes it structurally true rather than merely currently true.
    /// </summary>
    private static IReadOnlyList<(int Count, string Singular, string Plural)> VoucherReferenceTally(
        Company company, Guid voucherId) =>
        new[]
        {
            // ---- gst_cdn_links.original_invoice_voucher_id
            (company.CreditDebitNoteLinks.Count(l => l.OriginalInvoiceVoucherId == voucherId),
             "credit/debit note issued against it", "credit/debit notes issued against it"),

            // ---- gst_cdn_links.cdn_voucher_id (the note's own side; NOT NULL)
            (company.CreditDebitNoteLinks.Count(l => l.CdnVoucherId == voucherId),
             "§34 credit/debit-note link on it", "§34 credit/debit-note links on it"),

            // ---- challan_voucher_links.voucher_id
            (company.ChallansLinkedToVoucher(voucherId).Count(),
             "TDS challan link", "TDS challan links"),

            // ---- tcs_challan_voucher_links.voucher_id
            (company.TcsChallansLinkedToVoucher(voucherId).Count(),
             "TCS challan link", "TCS challan links"),

            // ---- einvoice_records.source_voucher_id
            (company.EInvoiceRecords.Count(r => r.SourceVoucherId == voucherId),
             "e-invoice record", "e-invoice records"),

            // ---- eway_bills.source_voucher_id
            (company.EWayBillRecords.Count(r => r.SourceVoucherId == voucherId),
             "e-Way Bill record", "e-Way Bill records"),

            // ---- rcm_documents.source_voucher_id
            (company.RcmDocuments.Count(d => d.SourceVoucherId == voucherId),
             "RCM self-invoice / payment voucher", "RCM self-invoice / payment vouchers"),

            // ---- gst_setoff_lines.voucher_id
            (company.GstSetoffLines.Count(l => l.VoucherId == voucherId),
             "GST set-off line", "GST set-off lines"),

            // ---- itc_reversals.source_voucher_id
            (company.ItcReversals.Count(r => r.SourceVoucherId == voucherId),
             "ITC reversal taken against it", "ITC reversals taken against it"),

            // ---- itc_reversals.reversal_voucher_id
            (company.ItcReversals.Count(r => r.ReversalVoucherId == voucherId),
             "ITC reversal posted by it", "ITC reversals posted by it"),

            // ---- gstr2b_recon.matched_voucher_id
            (company.Gstr2bReconResults.Count(r => r.MatchedVoucherId == voucherId),
             "GSTR-2B reconciliation match", "GSTR-2B reconciliation matches"),

            // ---- gst_advance_receipts.receipt_voucher_id
            (company.AdvanceReceipts.Count(a => a.ReceiptVoucherId == voucherId),
             "GST advance receipt", "GST advance receipts"),

            // ---- gst_advance_receipts.adjusted_against_invoice_vid
            (company.AdvanceReceipts.Count(a => a.AdjustedAgainstInvoiceVoucherId == voucherId),
             "GST advance adjusted against it", "GST advances adjusted against it"),

            // ---- gst_advance_receipts.refund_voucher_id
            (company.AdvanceReceipts.Count(a => a.RefundVoucherId == voucherId),
             "GST advance refund", "GST advance refunds"),

            // ---- gst_challans.voucher_id
            (company.GstChallans.Count(g => g.VoucherId == voucherId),
             "GST challan (PMT-06)", "GST challans (PMT-06)"),

            // ---- gst_drc03.voucher_id
            (company.GstDrc03s.Count(d => d.VoucherId == voucherId),
             "DRC-03 payment", "DRC-03 payments"),

            // ---- NO FOREIGN KEY EXISTS FOR THIS ONE — see CountBillSettlementsAgainstBillsOpenedBy.
            (CountBillSettlementsAgainstBillsOpenedBy(company, voucherId),
             "settlement against a bill it opened", "settlements against bills it opened"),
        };

    /// <summary>
    /// 🔴 <b>THE ONE REFERENCE CLASS SQLITE CANNOT SEE, AND THE ONLY ONE THAT PRODUCES A WRONG FIGURE INSTEAD OF A
    /// LOUD FAILURE.</b> Counts the later allocations that KNOCK OFF a bill this voucher OPENED.
    ///
    /// <para>A bill reference is <c>BillAllocation.Name</c> — a free string, persisted as
    /// <c>bill_allocations.name TEXT NOT NULL</c> with <b>no foreign key of any kind</b>. So deleting the invoice
    /// that opened <i>INV-1</i> while a receipt still carries <c>AgstRef "INV-1"</c> is permitted by every
    /// Guid-shaped rule, <b>COMMITS</b>, and survives a reopen. What is left is a party whose ledger closing balance
    /// says one thing and whose Outstandings say another: <c>Outstandings.OpenBillsFor</c> drops the bill at
    /// <c>if (s.Pending &lt;= 0m) continue;</c> once the opening leg is gone, so the settled money is on neither
    /// total, and <c>BillWiseTests.Sum_of_open_bills_equals_ledger_closing_balance</c> — which asserts that
    /// equality as a property of the product — is silently false for that book. Nothing else in this surface can
    /// produce a wrong figure with a successful save.</para>
    ///
    /// <para><b>Matching, deliberately identical to the report's.</b> A bill's identity is
    /// (ledger, reference name) and <c>Outstandings</c> accumulates it with
    /// <c>StringComparer.OrdinalIgnoreCase</c>; this count uses the same key so the guard and the report can never
    /// disagree about which bill is which.</para>
    ///
    /// <para><b>Why CANCELLED settling vouchers are excluded here, and cancelled vouchers are NOT excluded from the
    /// ledger guard.</b> The two questions are different and the file now says so in both places. Here the harm is
    /// a FIGURE, and <c>LedgerBalances.CountsAsOf</c> already drops a cancelled voucher from every figure — so a
    /// cancelled receipt settles nothing and blocking on it would be an over-refusal. In
    /// <see cref="EnsureLedgerDeletable"/> the harm is a FOREIGN KEY, and cancelling changes a flag while the row
    /// (and its <c>entry_lines.ledger_id</c>) survives untouched.</para>
    /// </summary>
    private static int CountBillSettlementsAgainstBillsOpenedBy(Company company, Guid voucherId)
    {
        if (company.FindVoucher(voucherId) is not { } voucher) return 0;

        var opened = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in voucher.Lines)
        {
            if (!line.HasBillAllocations) continue;
            foreach (var a in line.BillAllocations)
            {
                if (a.RefType is BillRefType.NewRef or BillRefType.Advance)
                    opened.Add(BillKey(line.LedgerId, a.Name));
            }
        }

        if (opened.Count == 0) return 0;

        var settlements = 0;
        foreach (var v in company.Vouchers)
        {
            if (v.Id == voucherId || v.Cancelled) continue;
            foreach (var line in v.Lines)
            {
                if (!line.HasBillAllocations) continue;
                foreach (var a in line.BillAllocations)
                {
                    if (a.RefType == BillRefType.AgstRef && opened.Contains(BillKey(line.LedgerId, a.Name)))
                        settlements++;
                }
            }
        }

        return settlements;
    }

    /// <summary>The (ledger, reference) identity of a bill, case-folded exactly the way
    /// <c>Outstandings.OpenBillsFor</c> folds it.</summary>
    private static string BillKey(Guid ledgerId, string reference) =>
        $"{ledgerId:N}|{reference.ToUpperInvariant()}";

    /// <summary>
    /// <b>THE VOUCHER DELETE GUARD.</b> Throws with a named, count-bearing message when
    /// <paramref name="voucher"/> must not be deleted; returns silently when it may be.
    ///
    /// <para><b>Order is load-bearing.</b> The numbering guard runs FIRST. A filed document always also carries an
    /// e-invoice or e-Way record and would therefore be caught by the referential guard anyway — but with a
    /// message that names a count and no remedy. The numbering refusal is the more specific diagnosis of the same
    /// voucher and it is the one that names what to do instead (<b>Cancel</b>), so it must win. Swap the order and
    /// the operator is told "1 document references this voucher" and left to guess.</para>
    ///
    /// <para>🔴 <b>THE REFERENTIAL REFUSAL NAMES A REMEDY THE PRODUCT CAN ACTUALLY PERFORM.</b> It used to open with
    /// <i>"Remove or re-link them first"</i> — an instruction no screen in the application can carry out for ANY of
    /// the categories: the six removers that exist (<c>RemoveEInvoiceRecord</c>, <c>RemoveEWayBillRecord</c>, the
    /// four challan-link removers) have exactly one caller each, the canonical-import ROLLBACK journal, and
    /// <c>RemoveCreditDebitNoteLink</c>'s only other caller is the undo of the entry that created it. Telling an
    /// operator to do something impossible is worse than a bare refusal, because they will look for the screen. The
    /// wording now states what is true — the artefacts stay, so Cancel is the route — and the debt (no
    /// discard action for a portal artefact that never reached the portal) is recorded rather than papered over.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is a filed statutory document, or other records
    /// reference it.</exception>
    public static void EnsureVoucherDeletable(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var label = DescribeVoucher(company, voucher);

        // ---- guard 1: the numbering guard (D-3). Refuse, and OFFER CANCEL.
        if (IsFiledStatutoryDocument(company, voucher.Id))
            throw new InvalidOperationException(
                $"Cannot delete {label}: it is a filed statutory document, and a filed document number can never "
                + "be reissued. Deleting it would let the next entry take its number. Cancel it instead (Alt+X) — "
                + "a cancelled voucher keeps its number in sequence and stops counting toward every balance.");

        // ---- guard 2: the referential guard. Refuse, and NAME THE COUNT.
        var references = DescribeVoucherReferences(company, voucher.Id);
        if (references.Count > 0)
        {
            var total = CountVoucherReferences(company, voucher.Id);
            var head = total == 1
                ? "1 document references this voucher"
                : $"{total} documents reference this voucher";
            throw new InvalidOperationException(
                $"Cannot delete {label}: {head} ({string.Join(", ", references)}). "
                + "No screen can detach them, so cancel the voucher instead (Alt+X).");
        }
    }

    // ==================================================================== master side

    /// <summary>
    /// <b>THE LEDGER DELETE GUARD</b> — the corpus rule, refusing with the count and carrying the corpus's own
    /// remedy: <i>"You cannot delete any ledger, if any transaction(s) has been already made with that ledger. To
    /// delete the ledger, delete all the transactions related to that ledger and then you can delete the ledger."</i>
    /// (STUDY-GUIDE PDF p.67). The second sentence used to be dropped — in the very slice that makes the remedy
    /// executable — and is restored here, which also makes this the only master refusal that names a way forward.
    ///
    /// <para>Two refusals precede it and both are ours, carried over from <see cref="MasterAlterationRules"/>'s
    /// reasoning rather than re-derived: a <b>predefined</b> ledger (Cash, Profit &amp; Loss A/c) cannot be
    /// deleted, and neither can one carrying a
    /// <see cref="MasterAlterationRules.WellKnownLedgerNames"/> name — ~14 engine sites resolve those by hardcoded
    /// string and fail <b>silently</b> when the lookup misses (the worst returns zero rounding rather than an
    /// error). If renaming one is refused because the engine would stop finding it, deleting one is strictly
    /// worse.</para>
    ///
    /// <para>🔴 <b>CANCELLED VOUCHERS COUNT, AND THAT IS DELIBERATE.</b> Everything else in the codebase excludes
    /// them — <c>LedgerBalances.CountsAsOf</c>, <c>ItemInvoiceStock.Counts</c>,
    /// <c>ChallanReconciliation.ChallanHasLiveVoucher</c> — so "excluding cancelled here too" reads like a tidy
    /// consistency fix and is the edit this line is most likely to attract. It would be a defect: cancelling sets a
    /// flag, the voucher row and its <c>entry_lines.ledger_id</c> survive, and <c>vouchers.party_id</c> is a
    /// foreign key like any other. A ledger whose only vouchers are cancelled is exactly as undeletable as one
    /// whose vouchers are live. The idiom that DOES exclude cancelled in this file
    /// (<see cref="CountBillSettlementsAgainstBillsOpenedBy"/>) is about a FIGURE, not a key; the two are
    /// distinguished in both places rather than left to look like an oversight.</para>
    ///
    /// <para>🔴 <b>AND EIGHT MORE COLUMNS THAT ARE NOT TRANSACTIONS.</b> The corpus rule is about vouchers, so the
    /// vouchers refusal keeps the attested wording. But a ledger is also named by an inventory voucher's party, an
    /// additional-cost line, a POS till configuration and its tender defaults, a pay head's posting and
    /// employer-expense ledgers, a budget line, and an RCM document's supplier — all foreign keys, none of them a
    /// transaction, and every one of them formerly a permitted delete that poisoned the open company. They get a
    /// second, differently-worded refusal so a count of "masters and settings" is never dressed up as a count of
    /// transactions.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The ledger is predefined, reserved, carries transactions, or is
    /// named by another master or setting.</exception>
    public static void EnsureLedgerDeletable(Company company, Domain.Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledger);

        if (ledger.IsPredefined)
            throw new InvalidOperationException(
                $"'{ledger.Name}' is a predefined ledger and cannot be deleted.");

        if (MasterAlterationRules.WellKnownLedgerNames.Contains(ledger.Name))
            throw new InvalidOperationException(
                $"'{ledger.Name}' is a reserved ledger that the engine resolves by name (GST / round-off / payroll "
                + "posting would silently stop finding it). It cannot be deleted.");

        // vouchers.party_id + entry_lines.ledger_id + pos_tender_allocations.ledger_id — all three ride on a
        // posted accounting voucher, so all three are "a transaction has been made with that ledger".
        var vouchers = company.Vouchers.Count(
            v => v.PartyId == ledger.Id
              || v.Lines.Any(l => l.LedgerId == ledger.Id)
              || v.PosTenders.Any(t => t.LedgerId == ledger.Id));
        if (vouchers > 0)
        {
            var head = vouchers == 1
                ? "1 voucher has already been posted against it"
                : $"{vouchers} vouchers have already been posted against it";
            throw new InvalidOperationException(
                $"Cannot delete ledger '{ledger.Name}': {head}. "
                + "A ledger that carries transactions cannot be deleted. Delete those vouchers first — Alt+D on "
                + "the Day Book row — and then the ledger can go.");
        }

        var parts = new List<string>();
        // inventory_vouchers.party_id
        AddPart(parts, company.InventoryVouchers.Count(v => v.PartyId == ledger.Id),
                "inventory voucher", "inventory vouchers");
        // additional_cost_lines.ledger_id
        AddPart(parts, company.InventoryVouchers.Sum(
                    v => v.AdditionalCostLines.Count(a => a.LedgerId == ledger.Id)),
                "additional-cost line", "additional-cost lines");
        // pos_voucher_type_config.default_party_id
        AddPart(parts, company.VoucherTypes.Count(t => t.PosConfig is { } p && p.DefaultPartyId == ledger.Id),
                "POS till default party", "POS till default parties");
        // pos_tender_ledger_defaults.ledger_id
        AddPart(parts, company.VoucherTypes.Sum(
                    t => t.PosConfig is { } p ? p.TenderLedgerDefaults.Count(d => d.Value == ledger.Id) : 0),
                "POS tender default", "POS tender defaults");
        // pay_heads.ledger_id + pay_heads.employer_expense_ledger_id
        AddPart(parts, company.PayHeads.Count(
                    h => h.LedgerId == ledger.Id || h.EmployerExpenseLedgerId == ledger.Id),
                "pay head", "pay heads");
        // budget_lines.ledger_id
        AddPart(parts, company.Budgets.Sum(b => b.Lines.Count(l => l.LedgerId == ledger.Id)),
                "budget line", "budget lines");
        // rcm_documents.supplier_ledger_id
        AddPart(parts, company.RcmDocuments.Count(d => d.SupplierLedgerId == ledger.Id),
                "RCM document", "RCM documents");

        ThrowIfNamed(parts, $"ledger '{ledger.Name}'");
    }

    /// <summary>
    /// <b>THE GROUP DELETE GUARD.</b> A predefined group is refused outright — the 28 reserved groups are primary
    /// heads and sub-heads of the Balance Sheet and the catalogue states they cannot be deleted. A custom group is
    /// refused while anything is still filed under it, with the count broken down by kind, because deleting it
    /// would leave every child pointing at a parent that no longer exists (the report classification walks UP
    /// through <c>ParentId</c>, and a missing ancestor is how a ledger silently lands on the wrong side of the
    /// Balance Sheet) — and, on the persistence side, because <c>groups.parent_id</c>, <c>ledgers.group_id</c>,
    /// <c>budgets.under_id</c> and <c>pay_heads.under_group_id</c> are foreign keys that fail the next Save.
    ///
    /// <para>A group that is the company's <b>Profit &amp; Loss head</b> is refused first and separately: it is a
    /// single reserved slot (<c>companies.profit_and_loss_head_id</c>) and no count would be informative.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The group is predefined, is the P&amp;L head, still has
    /// children, or is named by a budget line.</exception>
    public static void EnsureGroupDeletable(Company company, Group group)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(group);

        if (group.IsPredefined)
            throw new InvalidOperationException(
                $"'{group.Name}' is a predefined group and cannot be deleted.");

        // companies.profit_and_loss_head_id
        if (company.ProfitAndLossHead is { } pl && pl.Id == group.Id)
            throw new InvalidOperationException(
                $"'{group.Name}' is the company's Profit & Loss head and cannot be deleted.");

        var subGroups = company.Groups.Count(g => g.ParentId == group.Id);
        var ledgers = company.Ledgers.Count(l => l.GroupId == group.Id);
        var payHeads = company.PayHeads.Count(h => h.UnderGroupId == group.Id);
        var budgets = company.Budgets.Count(b => b.UnderId == group.Id);
        var total = subGroups + ledgers + payHeads + budgets;
        if (total > 0)
        {
            var parts = new List<string>();
            AddPart(parts, subGroups, "sub-group", "sub-groups");
            AddPart(parts, ledgers, "ledger", "ledgers");
            AddPart(parts, payHeads, "pay head", "pay heads");
            AddPart(parts, budgets, "budget", "budgets");

            var head = total == 1 ? "1 master is filed under it" : $"{total} masters are filed under it";
            throw new InvalidOperationException(
                $"Cannot delete group '{group.Name}': {head} ({string.Join(", ", parts)}). "
                + "Move or delete them first.");
        }

        // budget_lines.group_id — a reference, not a child, so it gets the reference wording.
        var referenceParts = new List<string>();
        AddPart(referenceParts, company.Budgets.Sum(b => b.Lines.Count(l => l.GroupId == group.Id)),
                "budget line", "budget lines");
        ThrowIfNamed(referenceParts, $"group '{group.Name}'");
    }

    /// <summary>
    /// <b>THE STOCK-ITEM DELETE GUARD</b> — the master-side rule applied to the inventory master, refusing with
    /// the count. An item is in use when any item-invoice line on an accounting voucher, any inventory-voucher
    /// line (stock-journal allocation, order line or physical-stock count) or any opening balance names it.
    ///
    /// <para>🔴 <b>THE DESCRIPTIVE MASTERS ARE NOW COUNTED TOO, AND THE OLD NOTE UNDERSTATED THEM.</b> This guard
    /// used to declare, honestly but wrongly, that not counting a bill of materials, a batch, a price list or a
    /// job-work order was "named debt" whose consequence was a dangling BOM line. Measured, it is not a dangler:
    /// <c>batch_masters</c>, <c>bill_of_materials</c>, <c>bom_lines</c>, <c>price_lists</c>, <c>job_work_orders</c>
    /// and <c>job_work_order_lines</c> all declare <c>REFERENCES stock_items(id)</c>, so deleting a component named
    /// only by a BOM emptied the master from memory, threw <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> out of the key
    /// handler with an empty notice bar, and left the open company unsavable forever. They are counted.</para>
    ///
    /// <para><b>What is still NOT counted, and this half of the old note was right:</b>
    /// <c>ReorderDefinition.TargetId</c> carries <b>no</b> <c>REFERENCES</c> clause in the schema, so a reorder
    /// definition really does dangle softly rather than fail a Save. It is left uncounted deliberately, and it is
    /// the only member of the old list that behaves the way the old sentence described.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The stock item is used by an entry, or named by another
    /// master.</exception>
    public static void EnsureStockItemDeletable(Company company, StockItem item)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(item);

        var invoiceLines = company.Vouchers.Sum(v => v.InventoryLines.Count(l => l.StockItemId == item.Id));
        var inventoryLines = company.InventoryVouchers.Sum(
            v => v.Allocations.Count(a => a.StockItemId == item.Id)
               + v.DestinationAllocations.Count(a => a.StockItemId == item.Id)
               + v.OrderLines.Count(o => o.StockItemId == item.Id)
               + v.PhysicalLines.Count(p => p.StockItemId == item.Id));
        var openings = company.StockOpeningBalances.Count(b => b.StockItemId == item.Id);

        var total = invoiceLines + inventoryLines + openings;
        if (total > 0)
        {
            var parts = new List<string>();
            AddPart(parts, invoiceLines, "invoice line", "invoice lines");
            AddPart(parts, inventoryLines, "inventory-voucher line", "inventory-voucher lines");
            AddPart(parts, openings, "opening balance", "opening balances");

            var head = total == 1 ? "1 entry references it" : $"{total} entries reference it";
            throw new InvalidOperationException(
                $"Cannot delete stock item '{item.Name}': {head} ({string.Join(", ", parts)}). "
                + "A stock item that carries entries cannot be deleted.");
        }

        var referenceParts = new List<string>();
        // batch_masters.stock_item_id
        AddPart(referenceParts, company.BatchMasters.Count(b => b.StockItemId == item.Id),
                "batch", "batches");
        // bill_of_materials.stock_item_id
        AddPart(referenceParts, company.BillsOfMaterials.Count(b => b.StockItemId == item.Id),
                "bill of materials", "bills of materials");
        // bom_lines.component_stock_item_id
        AddPart(referenceParts, company.BillsOfMaterials.Sum(
                    b => b.Lines.Count(l => l.ComponentStockItemId == item.Id)),
                "bill-of-materials component line", "bill-of-materials component lines");
        // price_lists.stock_item_id
        AddPart(referenceParts, company.PriceLists.Count(p => p.StockItemId == item.Id),
                "price list", "price lists");
        // job_work_orders.fg_stock_item_id
        AddPart(referenceParts, company.InventoryVouchers.Count(
                    v => v.JobWorkOrder is { } o && o.FinishedGoodStockItemId == item.Id),
                "job-work order", "job-work orders");
        // job_work_order_lines.component_stock_item_id
        AddPart(referenceParts, company.InventoryVouchers.Sum(
                    v => v.JobWorkOrder is { } o ? o.Lines.Count(l => l.ComponentStockItemId == item.Id) : 0),
                "job-work component line", "job-work component lines");

        ThrowIfNamed(referenceParts, $"stock item '{item.Name}'");
    }

    // ==================================================================== helpers

    /// <summary>Appends "1 batch" / "3 batches" to <paramref name="parts"/> when the count is non-zero. One place,
    /// so a category cannot be added to the count and forgotten in the breakdown.</summary>
    private static void AddPart(List<string> parts, int n, string singular, string plural)
    {
        if (n > 0) parts.Add(Count(n, singular, plural));
    }

    /// <summary>
    /// The shared SECOND refusal for every master kind: the master is not carrying transactions, but other masters
    /// and settings still NAME it by a foreign key. Deliberately worded away from the attested transaction refusal
    /// so the two are never mistaken for each other, and it names the remedy that actually exists — go to the
    /// master that names it.
    /// </summary>
    private static void ThrowIfNamed(List<string> parts, string what)
    {
        if (parts.Count == 0) return;

        var total = parts.Count == 1 ? "1 other master or setting names it" : "Other masters and settings name it";
        throw new InvalidOperationException(
            $"Cannot delete {what}: {total} ({string.Join(", ", parts)}). "
            + "Each of those would be left pointing at a master that no longer exists, and the company could not "
            + "be saved again. Change them first.");
    }

    /// <summary>"Sales No. 3 dated 10-Jun-2024" — the operator-facing identity of a voucher, so a refusal names
    /// the same document the report row shows. The NUMBER is the half that tells the operator WHICH document, and
    /// the delete prompt is built from this same string, so both are asserted in the test suite.</summary>
    private static string DescribeVoucher(Company company, Voucher voucher)
    {
        var typeName = company.FindVoucherType(voucher.TypeId)?.Name ?? "Voucher";
        var number = company.FormatVoucherNumber(voucher);
        var numberPart = string.IsNullOrWhiteSpace(number) ? string.Empty : $" No. {number}";
        return $"{typeName}{numberPart} dated {voucher.Date:dd-MMM-yyyy}";
    }

    /// <summary>"1 e-invoice record" / "2 e-invoice records". Spelled both ways rather than concatenating an
    /// "(s)", because a refusal an operator reads under pressure should not say "1 documents".</summary>
    private static string Count(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";
}
