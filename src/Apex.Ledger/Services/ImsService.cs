using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The thin mutator over a <see cref="Company"/>'s offline <b>IMS</b> (Invoice Management System) mirror (Phase 9 slice
/// 6b; RQ-14; DP-14). It creates/updates/clears the <see cref="ImsAction"/> for a 2B line and derives the
/// <b>deemed-accept</b> effective status. It has <b>no posting surface</b> (ER-14): it never takes a
/// <c>LedgerService</c>, never posts a voucher or a reversal, and emits no <c>EntryLine</c> — the <b>real</b>
/// Accept/Reject happens on the GST portal; this app is an offline mirror (DP-14/DP-25). The Oct-2025 credit-note
/// reversal declaration is <b>stored</b> here but <b>posted</b> only by S7.
/// </summary>
public static class ImsService
{
    /// <summary>
    /// Records (or re-decides in place) the IMS decision for the 2B line <paramref name="lineId"/>. Creates a fresh
    /// <see cref="ImsAction"/> the first time and updates it thereafter (never a second row per line). <b>Bypass rule
    /// (§3.3):</b> a <b>supplier-flagged reverse-charge</b> line is <b>not IMS-actionable</b> (inward RCM is handled by
    /// the S2a self-invoice path), so <c>SetAction</c> on it is rejected. The <see cref="ImsAction"/> invariant (a
    /// reversal declaration only on an Accept; remarks mandatory when partial; partial + no-reversal mutually exclusive)
    /// is validated fail-fast — a rejected update leaves the existing decision untouched.
    /// </summary>
    /// <returns>The created/updated action.</returns>
    /// <exception cref="ArgumentException">The line id is unknown, or the reversal-declaration invariant is violated.</exception>
    /// <exception cref="InvalidOperationException">The line is a supplier-flagged reverse-charge line (non-actionable).</exception>
    public static ImsAction SetAction(
        Company company, Guid lineId, ImsStatus status, string? remarks = null, long? declaredReversalPaisa = null,
        bool noReversalDeclared = false, DateOnly? actedOn = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var line = company.FindGstr2bLine(lineId)
            ?? throw new ArgumentException($"No imported GSTR-2B line with id {lineId}.", nameof(lineId));
        if (line.ReverseCharge)
            throw new InvalidOperationException(
                "A supplier-flagged reverse-charge 2B line bypasses IMS (§3.3) and is not IMS-actionable.");

        var existing = company.FindImsActionForLine(lineId);
        if (existing is null)
        {
            var action = new ImsAction(
                Guid.NewGuid(), lineId, status, remarks, declaredReversalPaisa, noReversalDeclared, actedOn);
            company.AddImsAction(action);
            return action;
        }

        existing.Set(status, remarks, declaredReversalPaisa, noReversalDeclared, actedOn);
        return existing;
    }

    /// <summary>Clears the IMS decision for a line (a re-import/undo) ⇒ the line reverts to <b>deemed-accept</b>
    /// (<see cref="EffectiveStatus"/> returns <see cref="ImsStatus.Accepted"/>). A no-op when the line has no action.</summary>
    public static void ClearAction(Company company, Guid lineId)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindImsActionForLine(lineId) is { } existing)
            company.RemoveImsAction(existing);
    }

    /// <summary>
    /// The <b>effective</b> IMS status of a 2B line (Phase 9 slice 6b; §3.2): <see cref="ImsStatus.Accepted"/> when the
    /// line has <b>no</b> action or its stored status is <see cref="ImsStatus.NoAction"/> (deemed-accepted at 2B
    /// generation), else the stored status. A <b>derived</b> view (mirrors how <c>EWayStatus.Expired</c> is derived), so
    /// a fresh import needs zero IMS rows and still reads "all deemed accepted".
    /// </summary>
    public static ImsStatus EffectiveStatus(Company company, Gstr2bLine line)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(line);
        var action = company.FindImsActionForLine(line.Id);
        return action is null || action.Status == ImsStatus.NoAction ? ImsStatus.Accepted : action.Status;
    }
}
