using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The advisory result of reconciling one imported <see cref="Gstr2bSnapshot"/> against the booked purchase register
/// (Phase 9 slice 6; RQ-13; DP-13/DP-15). A <b>pure record</b> (like <see cref="Gstr3b"/>) — it <b>posts nothing</b>
/// (ER-14). Each 2B line falls into exactly one of four buckets; the report also exposes the reversal <b>candidates</b>
/// (books ITC with no matching 2B line ⇒ §16(2)(aa) ineligible this period) for the S7 poster to consume — S6 itself
/// writes no reversal.
/// </summary>
public sealed record Gstr2bReconciliationReport(
    Gstr2bSnapshot Snapshot,
    IReadOnlyList<ReconMatch> Matched,
    IReadOnlyList<ReconMatch> PartialMismatches,
    IReadOnlyList<Gstr2bLine> InPortalOnly,
    IReadOnlyList<ReconBooksEntry> InBooksOnly)
{
    /// <summary>Count of cleanly matched 2B lines.</summary>
    public int MatchedCount => Matched.Count;
    /// <summary>Count of key-matched-but-value-differing 2B lines.</summary>
    public int PartialMismatchCount => PartialMismatches.Count;
    /// <summary>Count of 2B lines with no booked purchase.</summary>
    public int InPortalOnlyCount => InPortalOnly.Count;
    /// <summary>Count of booked purchases with no 2B line.</summary>
    public int InBooksOnlyCount => InBooksOnly.Count;

    /// <summary>
    /// The reversal <b>candidates</b> surfaced for S7 (RQ-27): booked purchases with no matching 2B line — the supplier
    /// has not filed, so the ITC is ineligible this period (§16(2)(aa)). This report never posts the reversal (ER-14); it
    /// only surfaces the candidate identity + amount.
    /// </summary>
    public IReadOnlyList<ReconBooksEntry> ReversalCandidates => InBooksOnly;

    /// <summary>
    /// Materialises the three <b>portal-side</b> buckets (Matched / PartialMismatch / InPortalOnly) into persistable
    /// <see cref="Gstr2bReconResult"/> rows keyed to their 2B lines — the audit snapshot a "Reconcile" action writes.
    /// <see cref="ReconBucket.InBooksOnly"/> is deliberately NOT materialised (it has no 2B line to key on, §6.4). Pure —
    /// posts nothing (ER-14).
    /// </summary>
    public IReadOnlyList<Gstr2bReconResult> ToPersistedResults(Func<Guid> idFactory, DateTimeOffset reconciledAt)
    {
        var results = new List<Gstr2bReconResult>(Matched.Count + PartialMismatches.Count + InPortalOnly.Count);
        foreach (var m in Matched)
            results.Add(new Gstr2bReconResult(idFactory(), m.Line.Id, ReconBucket.Matched, m.MatchedVoucherId,
                m.TaxableVariancePaisa, m.TaxVariancePaisa, matchPinned: false, reconciledAt));
        foreach (var m in PartialMismatches)
            results.Add(new Gstr2bReconResult(idFactory(), m.Line.Id, ReconBucket.PartialMismatch, m.MatchedVoucherId,
                m.TaxableVariancePaisa, m.TaxVariancePaisa, matchPinned: false, reconciledAt));
        foreach (var line in InPortalOnly)
            results.Add(new Gstr2bReconResult(idFactory(), line.Id, ReconBucket.InPortalOnly, matchedVoucherId: null,
                taxableVariancePaisa: 0, taxVariancePaisa: 0, matchPinned: false, reconciledAt));
        return results;
    }
}

/// <summary>A 2B line matched (or partially matched) to a booked purchase (Phase 9 slice 6). The variances are signed
/// portal − books (paisa); both are 0 for a clean <see cref="ReconBucket.Matched"/> within a zero tolerance.</summary>
/// <param name="Line">The imported 2B line.</param>
/// <param name="MatchedVoucherId">The matched books purchase voucher (a read-only pointer, ER-14).</param>
/// <param name="TaxableVariancePaisa">Taxable-value variance (portal − books), in paisa.</param>
/// <param name="TaxVariancePaisa">Total-tax variance (portal − books, cess folded in), in paisa.</param>
public readonly record struct ReconMatch(
    Gstr2bLine Line, Guid MatchedVoucherId, long TaxableVariancePaisa, long TaxVariancePaisa);

/// <summary>One entry in the booked purchase register — a projection of a posted inward voucher used by the reconciler
/// and surfaced in the <see cref="ReconBucket.InBooksOnly"/> bucket (Phase 9 slice 6). Money is integer paisa.</summary>
/// <param name="VoucherId">The posted purchase/debit-note voucher.</param>
/// <param name="SupplierGstin">The supplier GSTIN (upper-cased/trimmed match key).</param>
/// <param name="SupplierDocNumber">The supplier's document number from the bill-wise ref, or <c>null</c> when absent.</param>
/// <param name="Date">The voucher date (the books-side doc date).</param>
/// <param name="TaxableValuePaisa">The invoice taxable value, in paisa (excludes cess + RCM).</param>
/// <param name="TotalTaxPaisa">Forward GST + Compensation Cess, in paisa (excludes RCM).</param>
public readonly record struct ReconBooksEntry(
    Guid VoucherId, string SupplierGstin, string? SupplierDocNumber, DateOnly Date,
    long TaxableValuePaisa, long TotalTaxPaisa);
