namespace Apex.Ledger.Domain;

/// <summary>
/// The wage basis a <b>gratuity provision</b> is accrued against (Phase 8 slice 9; Payment of Gratuity Act 1972 §4).
/// Statutory gratuity is computed on the <b>last-drawn Basic + Dearness Allowance</b> only (HRA / overtime / other
/// allowances excluded), so the single defined basis is <see cref="BasicAndDearnessAllowance"/>. Modelled as an enum
/// (mirroring <c>PtWageBasis</c>) so a future basis is a data choice, not a code change. Stored as the ordinal
/// (0 = Basic + DA).
/// </summary>
public enum GratuityWageBasis
{
    /// <summary>Last-drawn Basic + Dearness Allowance — the earning heads flagged
    /// <see cref="PayHead.UseForGratuity"/> (the Act's "wages" for the 15/26 formula).</summary>
    BasicAndDearnessAllowance = 0,
}
