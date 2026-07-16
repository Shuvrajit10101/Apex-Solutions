namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>immutable dated GSTR-2B/2A statement</b> imported from the portal (Phase 9 slice 6; RQ-12; ER-6) — owns its
/// <see cref="Gstr2bLine"/> children. This is the taxpayer's own portal download (external data, NOT an app posting), so
/// it is <b>never mutated</b>: a re-import for the same period creates a fresh snapshot (versioned by
/// <see cref="ImportedAt"/> + <see cref="SourceFileHash"/>), the old one untouched. The reconciliation and the S6b
/// ITC-gate read the <see cref="GstStatementType.Gstr2b"/> snapshot; a 2A snapshot is supplementary.
/// <para>
/// <b>ADVISORY only (ER-14):</b> a snapshot has <b>no posting surface</b> — there is deliberately no method that mints a
/// voucher, a <c>GstLineTax</c>, or a reversal. Reconciliation is a pure read (<c>Gstr2bReconciler</c>).
/// </para>
/// </summary>
/// <remarks>Framework-, DB- and clock-free (<see cref="ImportedAt"/> is supplied by the caller, never read from a clock,
/// so a materialisation is deterministic).</remarks>
public sealed class Gstr2bSnapshot
{
    private readonly List<Gstr2bLine> _lines;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Which portal statement this is (2B = the ITC gate, 2A = supplementary).</summary>
    public GstStatementType StatementType { get; }

    /// <summary>The return period, "yyyy-MM" (e.g. "2025-10").</summary>
    public string ReturnPeriod { get; }

    /// <summary>The recipient (this taxpayer's) GSTIN the statement was generated for.</summary>
    public string RecipientGstin { get; }

    /// <summary>The portal generation date (2B <c>gendt</c>), or <c>null</c>.</summary>
    public DateOnly? GeneratedOn { get; }

    /// <summary>The SHA-256 hex of the imported JSON bytes — dedupe + an immutability audit anchor.</summary>
    public string SourceFileHash { get; }

    /// <summary>When this statement was imported into the app (caller-supplied; never a clock read here).</summary>
    public DateTimeOffset ImportedAt { get; }

    /// <summary>The portal summary ITC totals (paisa) — for a reconcile-to-source integrity check.</summary>
    public long SummaryIgstPaisa { get; }
    /// <summary>The portal summary ITC totals (paisa).</summary>
    public long SummaryCgstPaisa { get; }
    /// <summary>The portal summary ITC totals (paisa).</summary>
    public long SummarySgstPaisa { get; }
    /// <summary>The portal summary ITC totals (paisa).</summary>
    public long SummaryCessPaisa { get; }

    /// <summary>The imported inward-supply records (immutable; a re-import creates a new snapshot, ER-6).</summary>
    public IReadOnlyList<Gstr2bLine> Lines => _lines;

    public Gstr2bSnapshot(
        Guid id, GstStatementType statementType, string returnPeriod, string recipientGstin, DateOnly? generatedOn,
        string sourceFileHash, DateTimeOffset importedAt, long summaryIgstPaisa, long summaryCgstPaisa,
        long summarySgstPaisa, long summaryCessPaisa, IEnumerable<Gstr2bLine> lines)
    {
        if (string.IsNullOrWhiteSpace(returnPeriod))
            throw new ArgumentException("A GSTR-2B snapshot requires a return period.", nameof(returnPeriod));
        if (string.IsNullOrWhiteSpace(recipientGstin))
            throw new ArgumentException("A GSTR-2B snapshot requires a recipient GSTIN.", nameof(recipientGstin));
        if (string.IsNullOrWhiteSpace(sourceFileHash))
            throw new ArgumentException("A GSTR-2B snapshot requires a source-file hash.", nameof(sourceFileHash));

        Id = id;
        StatementType = statementType;
        ReturnPeriod = returnPeriod;
        RecipientGstin = recipientGstin;
        GeneratedOn = generatedOn;
        SourceFileHash = sourceFileHash;
        ImportedAt = importedAt;
        SummaryIgstPaisa = summaryIgstPaisa;
        SummaryCgstPaisa = summaryCgstPaisa;
        SummarySgstPaisa = summarySgstPaisa;
        SummaryCessPaisa = summaryCessPaisa;
        _lines = lines?.ToList() ?? new List<Gstr2bLine>();
    }

    /// <summary>Rehydrates a persisted/imported snapshot verbatim from the trusted store/import (Phase 9 slice 6). The
    /// invariant checks (non-empty period / recipient / hash) run in the ctor, so a malformed record fails fast in
    /// pre-flight ⇒ all-or-nothing (RQ-23).</summary>
    public static Gstr2bSnapshot Rehydrate(
        Guid id, GstStatementType statementType, string returnPeriod, string recipientGstin, DateOnly? generatedOn,
        string sourceFileHash, DateTimeOffset importedAt, long summaryIgstPaisa, long summaryCgstPaisa,
        long summarySgstPaisa, long summaryCessPaisa, IEnumerable<Gstr2bLine> lines) =>
        new(id, statementType, returnPeriod, recipientGstin, generatedOn, sourceFileHash, importedAt,
            summaryIgstPaisa, summaryCgstPaisa, summarySgstPaisa, summaryCessPaisa, lines);

    /// <summary>Finds a child line by its id, or <c>null</c>.</summary>
    public Gstr2bLine? FindLine(Guid lineId) => _lines.FirstOrDefault(l => l.Id == lineId);
}
