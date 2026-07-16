namespace Apex.Ledger.Domain;

/// <summary>
/// The persisted <b>reconciliation result</b> for one imported 2B line (Phase 9 slice 6; RQ-13; DP-13) — an audit
/// snapshot of what <c>Gstr2bReconciler</c> decided, keyed to the immutable <see cref="Gstr2bLine"/> by
/// <see cref="LineId"/>. Only the three <b>portal-side</b> buckets are persisted (Matched / PartialMismatch /
/// InPortalOnly); <see cref="ReconBucket.InBooksOnly"/> has no 2B line to key on and is a report-time derivation only.
/// <para>
/// <b>ADVISORY only (ER-14):</b> <see cref="MatchedVoucherId"/> is a <b>read-only pointer</b> to an existing purchase
/// voucher — never a mutation of it. This record posts nothing; the S7 set-off engine is the sole poster.
/// </para>
/// </summary>
/// <remarks>A mutable value-object-with-identity: a manual re-pin (<see cref="MatchPinned"/>) or a re-run overwrites it.
/// The invariant that a matched bucket carries a voucher (and an in-portal-only bucket does not) is enforced fail-fast so
/// a malformed import rejects the whole batch (RQ-23).</remarks>
public sealed class Gstr2bReconResult
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The immutable 2B line this result reconciles (FK <see cref="Gstr2bLine.Id"/>).</summary>
    public Guid LineId { get; }

    /// <summary>The reconciliation bucket (one of the three portal-side buckets).</summary>
    public ReconBucket Bucket { get; private set; }

    /// <summary>The matched books purchase voucher (a read-only pointer), or <c>null</c> for an in-portal-only line.</summary>
    public Guid? MatchedVoucherId { get; private set; }

    /// <summary>Taxable-value variance (portal − books), in paisa; 0 for an in-portal-only line.</summary>
    public long TaxableVariancePaisa { get; private set; }

    /// <summary>Total-tax variance (portal − books), in paisa; 0 for an in-portal-only line.</summary>
    public long TaxVariancePaisa { get; private set; }

    /// <summary>True iff the user manually pinned this match (overrides the auto-matcher on a re-run).</summary>
    public bool MatchPinned { get; private set; }

    /// <summary>When the reconciliation ran (caller-supplied), or <c>null</c>.</summary>
    public DateTimeOffset? ReconciledAt { get; private set; }

    public Gstr2bReconResult(
        Guid id, Guid lineId, ReconBucket bucket, Guid? matchedVoucherId, long taxableVariancePaisa,
        long taxVariancePaisa, bool matchPinned = false, DateTimeOffset? reconciledAt = null)
    {
        ValidateInvariant(bucket, matchedVoucherId);
        Id = id;
        LineId = lineId;
        Bucket = bucket;
        MatchedVoucherId = matchedVoucherId;
        TaxableVariancePaisa = taxableVariancePaisa;
        TaxVariancePaisa = taxVariancePaisa;
        MatchPinned = matchPinned;
        ReconciledAt = reconciledAt;
    }

    /// <summary>Rehydrates a persisted/imported recon result verbatim; the invariant check runs in the ctor so a
    /// malformed record (a Matched bucket with no voucher, or an InPortalOnly bucket with a voucher) fails fast in
    /// pre-flight ⇒ all-or-nothing (RQ-23).</summary>
    public static Gstr2bReconResult Rehydrate(
        Guid id, Guid lineId, ReconBucket bucket, Guid? matchedVoucherId, long taxableVariancePaisa,
        long taxVariancePaisa, bool matchPinned, DateTimeOffset? reconciledAt) =>
        new(id, lineId, bucket, matchedVoucherId, taxableVariancePaisa, taxVariancePaisa, matchPinned, reconciledAt);

    private static void ValidateInvariant(ReconBucket bucket, Guid? matchedVoucherId)
    {
        switch (bucket)
        {
            case ReconBucket.Matched or ReconBucket.PartialMismatch when matchedVoucherId is null:
                throw new ArgumentException(
                    $"A {bucket} reconciliation result requires a matched voucher id.", nameof(matchedVoucherId));
            case ReconBucket.InPortalOnly when matchedVoucherId is not null:
                throw new ArgumentException(
                    "An InPortalOnly reconciliation result must not carry a matched voucher id.", nameof(matchedVoucherId));
            case ReconBucket.InBooksOnly:
                throw new ArgumentException(
                    "InBooksOnly is a report-time derivation and is never persisted (no 2B line to key on).", nameof(bucket));
        }
    }
}
