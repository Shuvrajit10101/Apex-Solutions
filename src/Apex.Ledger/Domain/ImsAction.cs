namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>offline mirror</b> of a portal <b>IMS</b> (Invoice Management System) decision on one imported 2B line (Phase 9
/// slice 6; RQ-14; DP-14). Because the <see cref="Gstr2bSnapshot"/> is immutable (ER-6) but IMS is the recipient's evolving
/// decision, the action is a <b>separate mutable record keyed to the immutable line</b> (<see cref="LineId"/>) — it NEVER
/// mutates the line. The real Accept/Reject happens on the GST portal; this is only the app's local record of it.
/// <para>
/// <b>Deemed-accept</b> (<see cref="ImsStatus.NoAction"/> ⇒ Accepted at 2B generation) is a <b>derived</b> view
/// (<see cref="EffectiveStatus"/>), never a stored flip — a fresh import needs zero IMS rows and still reads "all deemed
/// accepted" (ER-13: empty when unused). The Oct-2025 credit-note change (declare a <b>partial / no ITC reversal</b> on
/// Accept of a credit note) is <b>stored</b> here (<see cref="DeclaredReversalPaisa"/> / <see cref="NoReversalDeclared"/>);
/// it is <b>never posted</b> — the reversal is S7 (ER-14, advisory-only).
/// </para>
/// </summary>
/// <remarks>Framework-, DB- and clock-free (<see cref="ActedOn"/> is caller-supplied, never a clock read here). A
/// mutable value-object-with-identity, mirroring the <see cref="Gstr2bReconResult"/> shape.</remarks>
public sealed class ImsAction
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The immutable 2B line this decision applies to (FK <see cref="Gstr2bLine.Id"/>).</summary>
    public Guid LineId { get; }

    /// <summary>The mirrored IMS status (NoAction ⇒ deemed-accept; Accepted / Rejected / Pending).</summary>
    public ImsStatus Status { get; private set; }

    /// <summary>Free-text remark (mandatory when a partial reversal is declared on an accepted credit note).</summary>
    public string? Remarks { get; private set; }

    /// <summary>The Oct-2025 <b>partial</b> ITC reversal declared on Accept of a credit note, in paisa; <c>null</c> when
    /// none. Stored only — never posted (ER-14).</summary>
    public long? DeclaredReversalPaisa { get; private set; }

    /// <summary>The Oct-2025 <b>no-reversal</b> declaration on Accept of a credit note (the recipient declares the CN needs
    /// no ITC reversal). Stored only — never posted (ER-14).</summary>
    public bool NoReversalDeclared { get; private set; }

    /// <summary>When the action was taken (caller-supplied), or <c>null</c>.</summary>
    public DateOnly? ActedOn { get; private set; }

    /// <summary>The <b>derived</b> effective IMS status: <see cref="ImsStatus.NoAction"/> reads as
    /// <see cref="ImsStatus.Accepted"/> (deemed-accepted at 2B generation), any explicit status reads verbatim. Never
    /// stored — mirrors how <see cref="EWayStatus.Expired"/> is derived, not persisted.</summary>
    public ImsStatus EffectiveStatus => Status == ImsStatus.NoAction ? ImsStatus.Accepted : Status;

    /// <summary>Creates a <b>fresh</b> action record for a 2B line — status <see cref="ImsStatus.NoAction"/> (deemed
    /// accept), no declaration. The status is advanced only through <see cref="ImsService"/> (which validates the line is
    /// IMS-actionable).</summary>
    public ImsAction(Guid id, Guid lineId)
    {
        Id = id;
        LineId = lineId;
        Status = ImsStatus.NoAction;
    }

    /// <summary>Rehydrates a persisted/imported IMS action verbatim from the trusted store/import (Phase 9 slice 6). The
    /// invariant checks run so a malformed record (a declared reversal on a non-Accepted status, a partial reversal with no
    /// remark) fails fast in pre-flight ⇒ all-or-nothing (RQ-23).</summary>
    public static ImsAction Rehydrate(
        Guid id, Guid lineId, ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared,
        DateOnly? actedOn)
    {
        ValidateInvariant(status, declaredReversalPaisa, noReversalDeclared, remarks);
        return new ImsAction(id, lineId)
        {
            Status = status,
            Remarks = remarks,
            DeclaredReversalPaisa = declaredReversalPaisa,
            NoReversalDeclared = noReversalDeclared,
            ActedOn = actedOn,
        };
    }

    /// <summary>Advances the action to a new status + optional Oct-2025 credit-note reversal declaration (used by
    /// <see cref="ImsService"/>; the portal-actionability + credit-note gate are enforced there).</summary>
    internal void Apply(ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared,
        DateOnly? actedOn)
    {
        ValidateInvariant(status, declaredReversalPaisa, noReversalDeclared, remarks);
        Status = status;
        Remarks = remarks;
        DeclaredReversalPaisa = declaredReversalPaisa;
        NoReversalDeclared = noReversalDeclared;
        ActedOn = actedOn;
    }

    private static void ValidateInvariant(
        ImsStatus status, long? declaredReversalPaisa, bool noReversalDeclared, string? remarks)
    {
        // A declared ITC reversal (a partial paisa figure OR a no-reversal declaration) is the Oct-2025 credit-note
        // concept — meaningful ONLY on an Accepted record. A non-Accepted status must carry no declaration.
        if (status != ImsStatus.Accepted && (declaredReversalPaisa is not null || noReversalDeclared))
            throw new ArgumentException("A declared ITC reversal can only be recorded on an Accepted IMS record.");
        if (declaredReversalPaisa is < 0)
            throw new ArgumentException("A declared ITC reversal must be ≥ 0 paisa.");
        // A record cannot declare both a specific partial reversal AND a no-reversal.
        if (declaredReversalPaisa is not null && noReversalDeclared)
            throw new ArgumentException("A record cannot declare both a partial reversal and no reversal.");
        // A partial declared reversal requires an explaining remark (mandatory when partial, §3.2).
        if (declaredReversalPaisa is not null && string.IsNullOrWhiteSpace(remarks))
            throw new ArgumentException("A partial declared ITC reversal requires a remark.");
    }
}
