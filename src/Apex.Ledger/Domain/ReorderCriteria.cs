namespace Apex.Ledger.Domain;

/// <summary>
/// How an <b>Advanced</b> reorder figure reconciles the operator-typed fixed quantity <c>F</c> with the
/// consumption-derived quantity <c>C</c> (Phase 6 slice 6; requirements RQ-35; Tally-Book pp.159–160). The
/// ordinals are stable (persisted as the raw integer in <c>reorder_definitions.criteria</c>).
/// </summary>
public enum ReorderCriteria
{
    /// <summary>Effective figure = <c>max(F, C)</c> — the safety-stock stance (order more).</summary>
    Higher = 0,

    /// <summary>Effective figure = <c>min(F, C)</c>.</summary>
    Lower = 1,
}
