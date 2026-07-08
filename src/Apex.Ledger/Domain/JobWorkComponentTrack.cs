namespace Apex.Ledger.Domain;

/// <summary>
/// How a Job Work Order component line is tracked (Phase 6 slice 8; RQ-47). This — NOT the order's
/// <see cref="JobWorkDirection"/> — is the load-bearing expression of the IN-vs-OUT symmetry (RQ-50): on a
/// principal's <b>Out</b> order the raw-material components are <see cref="PendingToIssue"/> (we still have to
/// dispatch them) while scrap is <see cref="PendingToReceive"/>; on a worker's <b>In</b> order the roles mirror.
/// Persisted as the INTEGER ordinal on <c>job_work_order_lines.track</c> (0 = Pending to Receive, 1 = Pending to
/// Issue).
/// </summary>
public enum JobWorkComponentTrack
{
    /// <summary>The component is still to be <b>received</b> against this order.</summary>
    PendingToReceive = 0,

    /// <summary>The component is still to be <b>issued</b> (dispatched) against this order.</summary>
    PendingToIssue = 1,
}
