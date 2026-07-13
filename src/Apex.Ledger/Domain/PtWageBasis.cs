namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>wage basis</b> a Professional-Tax slab is selected against (Phase 8 slice 6). PT is levied on the
/// employee's monthly PT-wages; the recommended default (DP-3) is <see cref="GrossEarnings"/> — the month's gross
/// earnings. Modelled as an enum so the basis is a configurable data choice on <see cref="PtConfig"/>, not a code
/// change, should a state key its slab off a different figure. Stored as the enum ordinal (0 = GrossEarnings).
/// </summary>
public enum PtWageBasis
{
    /// <summary>The month's <b>gross earnings</b> (the sum of the affecting earning heads) — the default PT basis.</summary>
    GrossEarnings = 0,
}
