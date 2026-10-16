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

    /// <summary>
    /// 🔴 <b>The company registration this statement was imported FOR</b> (schema <b>v66</b>; census 6.23;
    /// gap-register <b>T2-121(ii)</b> and <b>T2-125</b>). <c>null</c> or
    /// <see cref="GstRegistration.PrimaryId"/> ⇒ the company's own first registration — which is every snapshot
    /// in every pre-v66 book, since before v66 the import path hard-coded the primary's GSTIN and refused any
    /// other, so the back-fill is a no-op and is exact by construction rather than by a statement that ran once.
    ///
    /// <para><b>R7 — THE QUESTION T2-121(ii) ASKED IS ANSWERED <i>YES</i> BY THE VENDOR, BY CONTENT.</b>
    /// <c>help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/</c>, opened 2026-10-13,
    /// documents the <i>Import GST Returns</i> screen's own <b>GST Registration</b> field verbatim:
    /// <i>"GST Registration: It will be selected by default. In case of multiple GST registrations, select the
    /// required one."</i> The same page scopes the report the same way: <i>"If you have multiple GST
    /// registrations, the report shows consolidated data for all registrations by default. Press F3
    /// (Company/Tax Registration) &gt; select the required company or registration."</i> So a branch's or an
    /// ISD's own 2B <b>may</b> be imported, and refusing it would have been a divergence rather than caution.</para>
    ///
    /// <para>🔴 <b>WHY THE ANCHOR IS THE ID AND NOT THE GSTIN, when the GSTIN is right there on
    /// <see cref="RecipientGstin"/>.</b> Because matching by GSTIN is exactly the hole T2-121(iii) records: when
    /// a snapshot's recipient GSTIN matches no registration the company holds — reachable by editing the primary
    /// GSTIN after an import, or by restoring canonical XML that staged an arbitrary one — every GSTIN-matching
    /// lookup returns <c>null</c>, both layers fall through to the caller's own pick, and the duplicate
    /// (₹12,000 filed across two returns against a ₹6,000 note) reproduces unchanged. A stored id cannot fall
    /// through: it says which registration's return this statement belongs to, and keeps saying it after the
    /// GSTIN is edited.</para>
    /// </summary>
    public Guid? GstRegistrationId { get; }

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
        long summarySgstPaisa, long summaryCessPaisa, IEnumerable<Gstr2bLine> lines,
        Guid? gstRegistrationId = null)
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
        GstRegistrationId = gstRegistrationId;
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
        long summarySgstPaisa, long summaryCessPaisa, IEnumerable<Gstr2bLine> lines,
        Guid? gstRegistrationId = null) =>
        new(id, statementType, returnPeriod, recipientGstin, generatedOn, sourceFileHash, importedAt,
            summaryIgstPaisa, summaryCgstPaisa, summarySgstPaisa, summaryCessPaisa, lines, gstRegistrationId);

    /// <summary>Finds a child line by its id, or <c>null</c>.</summary>
    public Gstr2bLine? FindLine(Guid lineId) => _lines.FirstOrDefault(l => l.Id == lineId);

    /// <summary>
    /// The registration this statement belongs to, normalised the same way
    /// <c>GstReportSupport.RegistrationOf(Voucher)</c> normalises a voucher's: a <c>null</c>
    /// <see cref="GstRegistrationId"/> reads as <see cref="GstRegistration.PrimaryId"/>. Published here so every
    /// consumer asks the snapshot rather than re-deriving the null rule, and so no consumer can reach for
    /// <see cref="RecipientGstin"/> and inherit T2-121(iii)'s fall-through.
    /// </summary>
    public Guid OwningRegistrationId => GstRegistrationId ?? GstRegistration.PrimaryId;
}
