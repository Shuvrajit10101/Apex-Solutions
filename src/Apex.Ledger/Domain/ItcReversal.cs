namespace Apex.Ledger.Domain;

/// <summary>
/// The audit record of a posted <b>ITC reversal</b> (Rule 37 / 37A / 42 / 43 / §17(5) / Ineligible) or a re-availment
/// reclaim (Phase 9 slice 7; RQ-27; DP-30/DP-31). The reversal <b>engine</b> — the sole poster consuming the S6
/// candidates — arrives in <b>S7b</b>; this record + the <c>itc_reversals</c> table land in <b>S7a</b> (empty), so the
/// whole v44 schema settles at one version bump.
/// <para>
/// Each row is keyed for idempotency by <c>(company, rule, period, source_voucher_id, source_line_id)</c> — a UNIQUE
/// index makes a double-post a DB error, not silent drift (§5.3). A reclaim row sets <see cref="ReclaimOfId"/> to its
/// reversal row (Rule 37/37A re-availment), and the engine caps a reclaim at the tracked reversal balance (ECRS,
/// §11.7). <see cref="Table4bBucket"/> routes the posted line to GSTR-3B 4(B)(1) / 4(B)(2) / 4(D)(1). A pure value
/// object with identity.
/// </para>
/// </summary>
public sealed class ItcReversal
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The statutory rule this reversal was made under.</summary>
    public ItcReversalRule Rule { get; }

    /// <summary>The period ("yyyy-MM", or an FY for the Rule 42/43 annual true-up).</summary>
    public string Period { get; }

    /// <summary>The CGST amount reversed, in paisa (≥ 0).</summary>
    public long CgstPaisa { get; }

    /// <summary>The SGST/UTGST amount reversed, in paisa (≥ 0).</summary>
    public long SgstPaisa { get; }

    /// <summary>The IGST amount reversed, in paisa (≥ 0).</summary>
    public long IgstPaisa { get; }

    /// <summary>The Compensation-Cess amount reversed, in paisa (≥ 0; ring-fenced, ER-2).</summary>
    public long CessPaisa { get; }

    /// <summary>The Rule 42/43 D1 apportionment basis (audit), in paisa, or <c>null</c>.</summary>
    public long? D1BasisPaisa { get; }

    /// <summary>The Rule 42/43 D2 apportionment basis (audit), in paisa, or <c>null</c>.</summary>
    public long? D2BasisPaisa { get; }

    /// <summary>The purchase voucher whose ITC is reversed (Rule 37/42/43/§17(5)), or <c>null</c>.</summary>
    public Guid? SourceVoucherId { get; }

    /// <summary>The imported 2B line the reversal keys on (IMS-accepted CN / Rule 37A candidate), or <c>null</c>.</summary>
    public Guid? SourceLineId { get; }

    /// <summary>The posted stat-adjustment Journal that booked this reversal.</summary>
    public Guid ReversalVoucherId { get; }

    /// <summary>On a reclaim row, the reversal row being re-availed (Rule 37/37A), or <c>null</c>.</summary>
    public Guid? ReclaimOfId { get; }

    /// <summary>The DRC-03 landing instrument for an off-return reversal, or <c>null</c>.</summary>
    public Guid? Drc03Id { get; }

    /// <summary>The GSTR-3B Table-4 bucket this reversal / reclaim routes to.</summary>
    public Table4bBucket Table4bBucket { get; }

    /// <summary>When the record was created (caller-supplied, never a clock read).</summary>
    public DateTimeOffset CreatedAt { get; }

    public ItcReversal(
        Guid id, ItcReversalRule rule, string period, long cgstPaisa, long sgstPaisa, long igstPaisa, long cessPaisa,
        long? d1BasisPaisa, long? d2BasisPaisa, Guid? sourceVoucherId, Guid? sourceLineId, Guid reversalVoucherId,
        Guid? reclaimOfId, Guid? drc03Id, Table4bBucket table4bBucket, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("ITC reversal period is required.", nameof(period));
        if (cgstPaisa < 0 || sgstPaisa < 0 || igstPaisa < 0 || cessPaisa < 0)
            throw new ArgumentException("ITC reversal amounts must be ≥ 0 paisa.");
        if (reversalVoucherId == Guid.Empty)
            throw new ArgumentException("ITC reversal must reference its posted reversal voucher.", nameof(reversalVoucherId));

        Id = id;
        Rule = rule;
        Period = period.Trim();
        CgstPaisa = cgstPaisa;
        SgstPaisa = sgstPaisa;
        IgstPaisa = igstPaisa;
        CessPaisa = cessPaisa;
        D1BasisPaisa = d1BasisPaisa;
        D2BasisPaisa = d2BasisPaisa;
        SourceVoucherId = sourceVoucherId;
        SourceLineId = sourceLineId;
        ReversalVoucherId = reversalVoucherId;
        ReclaimOfId = reclaimOfId;
        Drc03Id = drc03Id;
        Table4bBucket = table4bBucket;
        CreatedAt = createdAt;
    }

    /// <summary>Rehydrates a persisted / imported ITC reversal verbatim (the fail-fast ctor re-checks the invariants).</summary>
    public static ItcReversal Rehydrate(
        Guid id, ItcReversalRule rule, string period, long cgstPaisa, long sgstPaisa, long igstPaisa, long cessPaisa,
        long? d1BasisPaisa, long? d2BasisPaisa, Guid? sourceVoucherId, Guid? sourceLineId, Guid reversalVoucherId,
        Guid? reclaimOfId, Guid? drc03Id, Table4bBucket table4bBucket, DateTimeOffset createdAt) =>
        new(id, rule, period, cgstPaisa, sgstPaisa, igstPaisa, cessPaisa, d1BasisPaisa, d2BasisPaisa, sourceVoucherId,
            sourceLineId, reversalVoucherId, reclaimOfId, drc03Id, table4bBucket, createdAt);
}
