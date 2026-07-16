namespace Apex.Ledger.Domain;

/// <summary>
/// The offline mirror of the recipient's <b>IMS</b> (Invoice Management System) decision on one imported 2B line (Phase 9
/// slice 6b; RQ-14; DP-14) — a <b>mutable value-object-with-identity</b> keyed to the immutable <see cref="Gstr2bLine"/>
/// by <see cref="LineId"/> (never a field on the line, so the imported statement stays immutable, ER-6). A line with
/// <b>no</b> <c>ImsAction</c> (or one whose <see cref="Status"/> is <see cref="ImsStatus.NoAction"/>) is
/// <b>deemed-accepted</b> at 2B generation — a derived view (<c>ImsService.EffectiveStatus</c>), never a stored flip, so a
/// fresh import needs zero IMS rows and still reads "all deemed accepted" (ER-13).
/// <para>
/// <b>Oct-2025 credit-note change (§3.2):</b> on an <b>Accept</b> of a CDN line the recipient may declare a partial
/// (<see cref="DeclaredReversalPaisa"/>) or a no-reversal (<see cref="NoReversalDeclared"/>) ITC reversal. The mirror
/// <b>stores</b> the declaration; it <b>does not post</b> the reversal — that is S7 (ER-14). This record has no posting
/// surface: it takes no <c>LedgerService</c> and emits no <c>EntryLine</c>.
/// </para>
/// </summary>
/// <remarks>The invariant (a reversal declaration only on an Accept; remarks mandatory when partial; partial and
/// no-reversal mutually exclusive) is enforced fail-fast in the ctor / mutator so a malformed import rejects the whole
/// batch (RQ-23).</remarks>
public sealed class ImsAction
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The immutable 2B line this decision applies to (FK <see cref="Gstr2bLine.Id"/>).</summary>
    public Guid LineId { get; }

    /// <summary>The mirrored IMS decision (NoAction ⇒ deemed-accept).</summary>
    public ImsStatus Status { get; private set; }

    /// <summary>Free-text remarks (mandatory when a partial reversal is declared, §3.2), or <c>null</c>.</summary>
    public string? Remarks { get; private set; }

    /// <summary>The Oct-2025 partial declared ITC reversal on an Accepted CDN line, in paisa; <c>null</c> when none.</summary>
    public long? DeclaredReversalPaisa { get; private set; }

    /// <summary>The Oct-2025 "no ITC reversal required" declaration on an Accepted CDN line (mutually exclusive with a
    /// partial <see cref="DeclaredReversalPaisa"/>).</summary>
    public bool NoReversalDeclared { get; private set; }

    /// <summary>When the recipient acted (caller-supplied), or <c>null</c>.</summary>
    public DateOnly? ActedOn { get; private set; }

    public ImsAction(
        Guid id, Guid lineId, ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared,
        DateOnly? actedOn)
    {
        ValidateInvariant(status, remarks, declaredReversalPaisa, noReversalDeclared);
        Id = id;
        LineId = lineId;
        Status = status;
        Remarks = remarks;
        DeclaredReversalPaisa = declaredReversalPaisa;
        NoReversalDeclared = noReversalDeclared;
        ActedOn = actedOn;
    }

    /// <summary>Rehydrates a persisted/imported IMS action verbatim; the invariant check runs in the ctor so a malformed
    /// record (a reversal declaration on a non-Accepted line, a partial with no remarks, or both a partial + no-reversal)
    /// fails fast in pre-flight ⇒ all-or-nothing (RQ-23).</summary>
    public static ImsAction Rehydrate(
        Guid id, Guid lineId, ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared,
        DateOnly? actedOn) =>
        new(id, lineId, status, remarks, declaredReversalPaisa, noReversalDeclared, actedOn);

    /// <summary>Re-decides this action in place (the <c>ImsService</c> mutator). Fail-fast: the invariant is validated
    /// <b>before</b> any field is mutated, so a rejected update leaves the existing decision untouched.</summary>
    internal void Set(
        ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared, DateOnly? actedOn)
    {
        ValidateInvariant(status, remarks, declaredReversalPaisa, noReversalDeclared);
        Status = status;
        Remarks = remarks;
        DeclaredReversalPaisa = declaredReversalPaisa;
        NoReversalDeclared = noReversalDeclared;
        ActedOn = actedOn;
    }

    private static void ValidateInvariant(
        ImsStatus status, string? remarks, long? declaredReversalPaisa, bool noReversalDeclared)
    {
        if (declaredReversalPaisa is < 0)
            throw new ArgumentException(
                "A declared ITC reversal must be ≥ 0 paisa.", nameof(declaredReversalPaisa));

        var hasPartial = declaredReversalPaisa is > 0;

        // A reversal declaration (a partial amount OR a no-reversal flag) only accompanies an Accept of a CDN line (§3.2).
        if ((hasPartial || noReversalDeclared) && status != ImsStatus.Accepted)
            throw new ArgumentException(
                "A declared ITC reversal (partial or no-reversal) is only valid on an Accepted IMS action.",
                nameof(status));

        // A partial declared reversal and a "no reversal required" declaration are mutually exclusive.
        if (hasPartial && noReversalDeclared)
            throw new ArgumentException(
                "A partial declared reversal and a no-reversal declaration are mutually exclusive.",
                nameof(noReversalDeclared));

        // Remarks are mandatory when a partial reversal is declared (§3.2).
        if (hasPartial && string.IsNullOrWhiteSpace(remarks))
            throw new ArgumentException(
                "A partial declared ITC reversal requires remarks.", nameof(remarks));
    }
}
