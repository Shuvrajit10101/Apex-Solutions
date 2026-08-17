using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The shared guards for the <b>Delete</b> verb (Phase 10.11 S4 / VL-2). Deliberately built on the
/// <see cref="MasterAlterationRules"/> shape — <b>every method THROWS <see cref="InvalidOperationException"/> and
/// none of them mutates the company</b> — rather than inventing a second convention for the same job. Pure,
/// framework- and DB-agnostic, and directly unit-tested.
///
/// <para><b>🔴 WHY THIS FILE IS THE DANGEROUS HALF OF S4.</b> <c>LedgerService.Delete</c> and
/// <c>InventoryPostingService.Delete</c> have existed since Phase 1 and, until this slice, <b>nothing in the
/// application called either of them</b>. S4 is what makes them reachable, so every consequence of removing a
/// posted voucher arrives with this slice and not with the engine that has silently carried it for ten phases.
/// Two of those consequences are guarded here.</para>
///
/// <para><b>🔴 CONSEQUENCE 1 — NUMBER REUSE (the numbering guard).</b> <c>LedgerService.NextNumber</c> is
/// <c>max + 1</c> computed by <b>scanning the vouchers</b>; there is no stored counter and no
/// <c>last_used_number</c> column anywhere in the schema (<c>InventoryPostingService</c> carries a byte-identical
/// copy). So deleting the <b>highest-numbered</b> voucher of a type makes the next post <b>REUSE its number</b> —
/// two different documents, at different times, carrying the same tax-invoice number, with the first no longer on
/// the books to prove which was which. The engine's own doc comment says Delete "may leave a gap in numbering",
/// which describes the MID-sequence case and misses this one.
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
/// <para><b>🔴 CONSEQUENCE 2 — ORPHAN POINTERS (the referential guard).</b> Records that live BESIDE a voucher
/// point at it by <see cref="Guid"/> and are not children of it: the §34 credit/debit-note link, the TDS and TCS
/// challan-voucher links, the e-invoice IRP artefact and the e-Way Bill artefact. Deleting the voucher leaves each
/// of those pointing at nothing. One report self-heals by construction — <c>Reports.ChallanReconciliation</c>'s
/// <c>ChallanHasLiveVoucher</c> looks the voucher up and drops the challan when it is missing or cancelled — and
/// nothing else was verified to. So the rule is: <b>refuse, and NAME THE COUNT of blocking documents</b>, which is
/// far more actionable than a bare refusal. See <see cref="EnsureVoucherDeletable"/> and
/// <see cref="DescribeVoucherReferences"/>.</para>
///
/// <para><b>The master side (corpus-attested, unlike the voucher side).</b> The Study Guide states outright that
/// <i>"You cannot delete any ledger, if any transaction(s) has been already made with that ledger"</i>
/// (STUDY-GUIDE PDF p.67). <see cref="EnsureLedgerDeletable"/> is that rule, refusing with the count of vouchers;
/// <see cref="EnsureGroupDeletable"/> and <see cref="EnsureStockItemDeletable"/> are the same shape for the two
/// other master kinds S4 routes Delete from.</para>
///
/// <para><b>🔴 R7 — FIDELITY, AND THE TWO CATEGORIES KEPT APART.</b>
/// <list type="bullet">
///   <item><b>ATTESTED, narrowed by us:</b> the ledger-with-transactions refusal above is a corpus rule. What is
///     ours is the <i>count</i> in the message and the extension of the same shape to groups and stock items.</item>
///   <item><b>UNVERIFIED-BY-DESIGN — ours, corpus silent:</b> the referential guard, the numbering guard, the
///     accepted residual, the decision to offer Cancel as the remedy, and every message string in this file. The
///     corpus is silent on what deleting a voucher does to a linked statutory document and silent on what happens
///     to its number. These are our decisions, recorded as ours.</item>
/// </list></para>
///
/// <para><b>Out of scope by ruling</b> (all recorded in plan.md §5): no audit trail of any kind — after this slice
/// an operator can delete a posted voucher and the books carry no record that it happened; no cascade that deletes
/// the blocking side records for the operator; no un-delete; no numbering floor.</para>
/// </summary>
public static class MasterDeletionRules
{
    // ==================================================================== voucher: the numbering guard (D-3)

    /// <summary>
    /// True when the voucher carries a <b>filed statutory document whose number is legally frozen</b>, and is
    /// therefore refused for deletion by <see cref="EnsureVoucherDeletable"/>.
    ///
    /// <para>This is deliberately the SAME two-status test the shipped numbering screen already applies
    /// (<c>VoucherNumberingConfigViewModel.IsFiledDocument</c>): for e-invoicing the frozen signal is any status
    /// that REACHED the IRP — <b>Generated</b> (an IRN was issued) OR <b>Cancelled</b> (the IRN was reported and
    /// the document number is permanently burned; a cancelled doc-no is never reusable). <c>Pending</c> and
    /// <c>Failed</c> never reached the IRP. An active (non-Cancelled) e-Way Bill likewise freezes the number.</para>
    ///
    /// <para><b>Why the two-status test here even though alteration uses a one-status test.</b> The standing
    /// ruling for ALTERATION refuses only <c>Generated</c>, which is defensible — a cancelled IRN's voucher
    /// content is no longer filed. For NUMBERING it is not: the document number stays burned either way. The
    /// difference is deliberate, not an inconsistency.</para>
    /// </summary>
    public static bool IsFiledStatutoryDocument(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (company.FindEInvoiceRecordForVoucher(voucherId)
            is { Status: EInvoiceStatus.Generated or EInvoiceStatus.Cancelled }) return true;
        // The finder already excludes a Cancelled e-Way Bill, so any hit here is a live one.
        if (company.FindEWayBillRecordForVoucher(voucherId) is not null) return true;
        return false;
    }

    // ==================================================================== voucher: the referential guard

    /// <summary>
    /// The records that point at <paramref name="voucherId"/> by <see cref="Guid"/> and would become <b>orphan
    /// pointers</b> if it were deleted, described one category per element ("1 credit/debit-note link",
    /// "2 TDS challan links", …). An empty list means the voucher is referentially free.
    ///
    /// <para><b>The five surfaces, and one addition of ours.</b> The design's delete-direction table names: the
    /// §34 link whose <c>OriginalInvoiceVoucherId</c> is this voucher, a TDS <c>ChallanVoucherLink</c>, a TCS
    /// challan link, an <c>EInvoiceRecord</c> and an <c>EWayBillRecord</c>. <b>Ours, added deliberately:</b> a §34
    /// link whose <c>CdnVoucherId</c> is this voucher — i.e. deleting the credit/debit note itself — because that
    /// row is orphaned by exactly the same mechanism and would leave a dangling GSTR-1 Table-9B pointer. It is
    /// labelled as an addition rather than folded in silently.</para>
    ///
    /// <para><b>"Live" §34 link.</b> A link is counted only while the note voucher it annotates still exists and
    /// is not cancelled — the same liveness test <c>Reports.ChallanReconciliation.ChallanHasLiveVoucher</c>
    /// already applies to a challan's booking voucher. A link left behind by an already-deleted or cancelled note
    /// blocks nothing.</para>
    ///
    /// <para><b>e-invoice / e-Way records are counted at ANY status</b>, Cancelled included: a cancelled IRP
    /// artefact is still a row holding this voucher's Guid, and its document number is still burned.</para>
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
            (company.CreditDebitNoteLinks.Count(
                 l => l.OriginalInvoiceVoucherId == voucherId && IsLiveCdnLink(company, l)),
             "credit/debit note issued against it", "credit/debit notes issued against it"),

            // OURS (see the summary): the note's OWN link, orphaned by the same mechanism as the one above.
            (company.CreditDebitNoteLinks.Count(l => l.CdnVoucherId == voucherId),
             "§34 credit/debit-note link on it", "§34 credit/debit-note links on it"),

            (company.ChallansLinkedToVoucher(voucherId).Count(),
             "TDS challan link", "TDS challan links"),

            (company.TcsChallansLinkedToVoucher(voucherId).Count(),
             "TCS challan link", "TCS challan links"),

            (company.EInvoiceRecords.Count(r => r.SourceVoucherId == voucherId),
             "e-invoice record", "e-invoice records"),

            (company.EWayBillRecords.Count(r => r.SourceVoucherId == voucherId),
             "e-Way Bill record", "e-Way Bill records"),
        };

    /// <summary>
    /// <b>THE VOUCHER DELETE GUARD.</b> Throws with a named, count-bearing message when
    /// <paramref name="voucher"/> must not be deleted; returns silently when it may be.
    ///
    /// <para><b>Order is load-bearing.</b> The numbering guard runs FIRST. A filed document always also carries an
    /// e-invoice or e-Way record and would therefore be caught by the referential guard anyway — but with a
    /// message that names a count and no remedy. The numbering refusal is the more specific diagnosis of the same
    /// voucher and it is the one that names what to do instead (<b>Cancel</b>), so it must win. Swap the order and
    /// the operator is told "1 document references this voucher" and left to guess.</para>
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
                + "Remove or re-link them first, or cancel the voucher instead (Alt+X).");
        }
    }

    // ==================================================================== master side

    /// <summary>
    /// <b>THE LEDGER DELETE GUARD</b> — the corpus rule, refusing with the count: <i>"You cannot delete any
    /// ledger, if any transaction(s) has been already made with that ledger"</i> (STUDY-GUIDE PDF p.67).
    ///
    /// <para>Two refusals precede it and both are ours, carried over from <see cref="MasterAlterationRules"/>'s
    /// reasoning rather than re-derived: a <b>predefined</b> ledger (Cash, Profit &amp; Loss A/c) cannot be
    /// deleted, and neither can one carrying a
    /// <see cref="MasterAlterationRules.WellKnownLedgerNames"/> name — ~14 engine sites resolve those by hardcoded
    /// string and fail <b>silently</b> when the lookup misses (the worst returns zero rounding rather than an
    /// error). If renaming one is refused because the engine would stop finding it, deleting one is strictly
    /// worse.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The ledger is predefined, reserved, or already carries
    /// transactions.</exception>
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

        var vouchers = company.Vouchers.Count(v => v.Lines.Any(l => l.LedgerId == ledger.Id));
        if (vouchers > 0)
        {
            var head = vouchers == 1
                ? "1 voucher has already been posted against it"
                : $"{vouchers} vouchers have already been posted against it";
            throw new InvalidOperationException(
                $"Cannot delete ledger '{ledger.Name}': {head}. "
                + "A ledger that carries transactions cannot be deleted.");
        }
    }

    /// <summary>
    /// <b>THE GROUP DELETE GUARD.</b> A predefined group is refused outright — the 28 reserved groups are primary
    /// heads and sub-heads of the Balance Sheet and the catalogue states they cannot be deleted. A custom group is
    /// refused while anything is still filed under it, with the count broken down into sub-groups and ledgers,
    /// because deleting it would leave every child pointing at a parent that no longer exists (the report
    /// classification walks UP through <c>ParentId</c>, and a missing ancestor is how a ledger silently lands on
    /// the wrong side of the Balance Sheet).
    /// </summary>
    /// <exception cref="InvalidOperationException">The group is predefined, or still has children.</exception>
    public static void EnsureGroupDeletable(Company company, Group group)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(group);

        if (group.IsPredefined)
            throw new InvalidOperationException(
                $"'{group.Name}' is a predefined group and cannot be deleted.");

        var subGroups = company.Groups.Count(g => g.ParentId == group.Id);
        var ledgers = company.Ledgers.Count(l => l.GroupId == group.Id);
        var total = subGroups + ledgers;
        if (total == 0) return;

        var parts = new List<string>();
        if (subGroups > 0) parts.Add(Count(subGroups, "sub-group", "sub-groups"));
        if (ledgers > 0) parts.Add(Count(ledgers, "ledger", "ledgers"));

        var head = total == 1 ? "1 master is filed under it" : $"{total} masters are filed under it";
        throw new InvalidOperationException(
            $"Cannot delete group '{group.Name}': {head} ({string.Join(", ", parts)}). "
            + "Move or delete them first.");
    }

    /// <summary>
    /// <b>THE STOCK-ITEM DELETE GUARD</b> — the master-side rule applied to the inventory master, refusing with
    /// the count. An item is in use when any item-invoice line on an accounting voucher, any inventory-voucher
    /// line (stock-journal allocation, order line or physical-stock count) or any opening balance names it: all
    /// three hold the item's Guid, and all three would be left pointing at a master that no longer exists.
    ///
    /// <para><b>What this guard does NOT cover, stated rather than implied.</b> The purely-descriptive masters
    /// that can also name a stock item — a bill of materials, a batch, a price list, a reorder definition — are
    /// not counted. They are out of S4's scope, and the consequence is real: deleting an unused item that a BOM
    /// still names leaves that BOM line dangling. Named debt, not an oversight.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The stock item is already used by an entry.</exception>
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
        if (total == 0) return;

        var parts = new List<string>();
        if (invoiceLines > 0) parts.Add(Count(invoiceLines, "invoice line", "invoice lines"));
        if (inventoryLines > 0) parts.Add(Count(inventoryLines, "inventory-voucher line", "inventory-voucher lines"));
        if (openings > 0) parts.Add(Count(openings, "opening balance", "opening balances"));

        var head = total == 1 ? "1 entry references it" : $"{total} entries reference it";
        throw new InvalidOperationException(
            $"Cannot delete stock item '{item.Name}': {head} ({string.Join(", ", parts)}). "
            + "A stock item that carries entries cannot be deleted.");
    }

    // ==================================================================== helpers

    /// <summary>A §34 link is LIVE while the note voucher it annotates exists and is not cancelled — the same
    /// test <c>ChallanReconciliation.ChallanHasLiveVoucher</c> applies to a challan's booking voucher.</summary>
    private static bool IsLiveCdnLink(Company company, GstCreditDebitNoteLink link) =>
        company.FindVoucher(link.CdnVoucherId) is { Cancelled: false };

    /// <summary>"Sales No. 3 dated 10-Jun-2024" — the operator-facing identity of a voucher, so a refusal names
    /// the same document the report row shows.</summary>
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
