using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The thin mutator for the offline <b>IMS</b> (Invoice Management System) mirror (Phase 9 slice 6b; RQ-14; DP-14). Sets /
/// clears / queries a per-2B-line <see cref="ImsAction"/> on the company. It has <b>no posting surface</b> — no
/// <c>LedgerService</c>, no journal, emits no <c>EntryLine</c> — the ADVISORY-only guarantee is the structural absence of
/// any posting path (ER-14). The real Accept/Reject happens on the GST portal; this only records the taxpayer's decision.
/// <para>
/// <b>Deemed-accept</b> is derived, not stored: a line with no action reads <see cref="ImsStatus.Accepted"/>
/// (<see cref="EffectiveStatusOf"/>). <b>Non-actionable</b> records (supplier-flagged RCM, or a §16(4)/POS ITC-unavailable
/// line) bypass IMS — <see cref="SetAction"/> on one is rejected (they belong in the read-only non-actionable section).
/// </para>
/// </summary>
public sealed class ImsService
{
    private readonly Company _company;

    public ImsService(Company company) => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>True iff a 2B line is IMS-actionable: a forward-charge line whose ITC the portal reports as available. A
    /// supplier-flagged reverse-charge line (handled by the S2a self-invoice path) or a line the portal marks ITC-
    /// unavailable (§16(4) time-bar / POS-ineligible) <b>bypasses IMS</b> and is shown read-only, not offered Accept/Reject.</summary>
    public static bool IsActionable(Gstr2bLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !line.ReverseCharge && line.ItcAvailable;
    }

    /// <summary>The <b>effective</b> IMS status of a 2B line: the stored action's <see cref="ImsAction.EffectiveStatus"/>,
    /// or <see cref="ImsStatus.Accepted"/> (deemed-accepted at 2B generation) when no action is recorded (RQ-14).</summary>
    public ImsStatus EffectiveStatusOf(Guid lineId) =>
        _company.FindImsActionByLine(lineId)?.EffectiveStatus ?? ImsStatus.Accepted;

    /// <summary>The stored IMS action for a 2B line, or <c>null</c> when none has been recorded (⇒ deemed-accept).</summary>
    public ImsAction? ActionFor(Guid lineId) => _company.FindImsActionByLine(lineId);

    /// <summary>
    /// Records (or advances) the IMS decision for a 2B line, creating the action on first use. Rejects a non-actionable
    /// line (RCM / ITC-unavailable — it bypasses IMS). A declared ITC reversal (the Oct-2025 change) is accepted only on
    /// an <see cref="ImsStatus.Accepted"/> <b>credit-note</b> line, and a partial reversal requires a remark (enforced by
    /// <see cref="ImsAction"/>). Never posts (ER-14).
    /// </summary>
    public ImsAction SetAction(Guid lineId, ImsStatus status, DateOnly? actedOn = null, string? remarks = null,
        long? declaredReversalPaisa = null, bool noReversalDeclared = false)
    {
        var line = _company.FindGstr2bLine(lineId)
            ?? throw new InvalidOperationException($"No imported 2B line {lineId} to record an IMS action for.");
        if (!IsActionable(line))
            throw new InvalidOperationException(
                "This 2B record is not IMS-actionable — a reverse-charge / ITC-unavailable record bypasses IMS " +
                "(the Accept/Reject action is not offered; it is shown read-only).");
        if ((declaredReversalPaisa is not null || noReversalDeclared)
            && line.DocType is not (Gstr2bDocType.CreditNote or Gstr2bDocType.CreditNoteAmendment))
            throw new InvalidOperationException(
                "A declared ITC reversal is an Oct-2025 credit-note concept — it can only be recorded on a credit-note record.");

        var action = _company.FindImsActionByLine(lineId);
        if (action is null)
        {
            action = new ImsAction(Guid.NewGuid(), lineId);
            _company.AddImsAction(action);
        }
        action.Apply(status, remarks, declaredReversalPaisa, noReversalDeclared, actedOn);
        return action;
    }

    /// <summary>Clears any stored IMS action for a line, reverting it to <see cref="ImsStatus.NoAction"/> (⇒ deemed-accept).
    /// Removing the row keeps an unused company byte-identical (ER-13). Idempotent when no action is recorded.</summary>
    public void ClearAction(Guid lineId)
    {
        if (_company.FindImsActionByLine(lineId) is { } action)
            _company.RemoveImsAction(action);
    }
}
