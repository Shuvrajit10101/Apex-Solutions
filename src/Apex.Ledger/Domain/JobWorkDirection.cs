namespace Apex.Ledger.Domain;

/// <summary>
/// The direction of a Job Work Order (Phase 6 slice 8; RQ-47). It expresses <b>which side of the job-work
/// relationship we are on</b>, NOT a hard-coded posting rule (the actual pending-to-receive/issue behaviour is
/// carried per component line by <see cref="JobWorkComponentTrack"/>, RQ-50):
/// <list type="bullet">
///   <item><see cref="In"/> — a <b>Job Work In Order</b>: we are the <i>job worker</i> (a principal sends us the
///     order + raw materials; Book1 p.83).</item>
///   <item><see cref="Out"/> — a <b>Job Work Out Order</b>: we are the <i>principal</i> (we delegate manufacture
///     to a worker; Book1 p.90).</item>
/// </list>
/// Persisted as the INTEGER ordinal on <c>job_work_orders.direction</c> (0 = In, 1 = Out).
/// </summary>
public enum JobWorkDirection
{
    /// <summary>Job Work In Order — we are the job worker (In).</summary>
    In = 0,

    /// <summary>Job Work Out Order — we are the principal (Out).</summary>
    Out = 1,
}
