namespace Apex.Ledger.Domain;

/// <summary>
/// Which employees a <b>gratuity provision run</b> accrues for (Phase 8 slice 9; DP-4). The recommended default
/// (<see cref="AllActiveEmployees"/>) accrues the liability for <b>every active employee</b> — the provision builds
/// even before an employee vests at five years (a "Vested (≥5 yrs)" flag distinguishes the two on the register);
/// <see cref="VestedOnly"/> accrues only for members who have completed the five-year vesting service. Stored as the
/// ordinal (0 = all active).
/// </summary>
public enum GratuityProvisionPopulation
{
    /// <summary>Accrue for every active employee (liability builds pre-vesting) — the recommended default.</summary>
    AllActiveEmployees = 0,

    /// <summary>Accrue only for employees who have completed five years' continuous service (already vested).</summary>
    VestedOnly = 1,
}
