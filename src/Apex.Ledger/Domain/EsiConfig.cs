namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>company-level Employees'-State-Insurance configuration</b> (Phase 8 slice 5; catalog §14; F11 Payroll
/// Statutory) — the establishment's ESIC registration facts the ESI computation needs. Present (non-<c>null</c> on
/// <see cref="Company.EsiConfig"/>) once the establishment is enrolled for ESI; a company that never enrols carries
/// no config and serialises byte-identically to a pre-v34 company (ER-13). Pure data — framework-, DB-, clock- and
/// RNG-free. The dedicated <c>EsiContribution</c> engine reads the EE/ER rates from here.
/// </summary>
public sealed class EsiConfig
{
    /// <summary>The employee contribution rate in basis points: <b>75</b> (0.75%, the default). Configurable so a
    /// future rate revision is a data change, not a code change.</summary>
    public int EmployeeRateBasisPoints { get; set; } = DefaultEmployeeRateBasisPoints;

    /// <summary>The employer contribution rate in basis points: <b>325</b> (3.25%, the default).</summary>
    public int EmployerRateBasisPoints { get; set; } = DefaultEmployerRateBasisPoints;

    /// <summary>The ESIC <b>establishment / employer code</b> (the 17-digit registered establishment code printed on
    /// the monthly challan and contribution file — <b>not</b> the per-employee 10-digit IP/Insurance Number); optional
    /// (may be captured later). Structural validation lives at the service boundary.</summary>
    public string? EmployerCode { get; set; }

    /// <summary>The default employee ESI rate (0.75%) in basis points.</summary>
    public const int DefaultEmployeeRateBasisPoints = 75;

    /// <summary>The default employer ESI rate (3.25%) in basis points.</summary>
    public const int DefaultEmployerRateBasisPoints = 325;

    public EsiConfig() { }

    public EsiConfig(
        int employeeRateBasisPoints = DefaultEmployeeRateBasisPoints,
        int employerRateBasisPoints = DefaultEmployerRateBasisPoints,
        string? employerCode = null)
    {
        if (employeeRateBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(employeeRateBasisPoints), "ESI employee rate cannot be negative.");
        if (employerRateBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(employerRateBasisPoints), "ESI employer rate cannot be negative.");
        EmployeeRateBasisPoints = employeeRateBasisPoints;
        EmployerRateBasisPoints = employerRateBasisPoints;
        EmployerCode = string.IsNullOrWhiteSpace(employerCode) ? null : employerCode.Trim();
    }
}
