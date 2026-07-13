namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Employees'-State-Insurance statutory role</b> of a <see cref="PayHead"/> (Phase 8 slice 5; catalog §14;
/// ESIC — ESI Central Rules 1950, Rule 51). A pay head tagged with an ESI component is <b>not</b> evaluated by the
/// generic calculation-type slabs — the payroll engine computes it with the dedicated ESI logic
/// (<c>EsiContribution</c>), because the statutory figures cannot be expressed as ordinary slabs: each side is
/// rounded <b>up</b> (ceiling) to the next whole rupee <i>independently</i> (never 4%-then-split), the employee
/// share is <b>waived</b> when the average daily wage is ≤ ₹176 (the employer still pays), and coverage is decided
/// once at the start of the contribution period and frozen for the whole period.
/// <para>
/// The default <see cref="None"/> leaves a pay head on the ordinary computation path, so every pre-v34 pay head is
/// byte-identical (ER-13). Stored as the enum ordinal (0 = None).
/// </para>
/// </summary>
public enum EsiStatutoryComponent
{
    /// <summary>Not an ESI statutory head — evaluated by its ordinary <see cref="PayHeadCalculationType"/>.</summary>
    None = 0,

    /// <summary>Employee ESI (0.75% of ESI contribution wages, rounded up) — an
    /// <see cref="PayHeadType.EmployeesStatutoryDeductions"/> head that reduces net pay. Waived (0) when the
    /// member's average daily wage is ≤ ₹176.</summary>
    EmployeeStateInsurance = 1,

    /// <summary>Employer ESI (3.25% of ESI contribution wages, rounded up <b>independently</b> of the employee
    /// share) — an <see cref="PayHeadType.EmployersStatutoryContributions"/> head (employer cost, not affecting
    /// net). Always payable while the member is covered, even when the employee share is waived.</summary>
    EmployerStateInsurance = 2,
}
