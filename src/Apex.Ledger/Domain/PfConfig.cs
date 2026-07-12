namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>company-level Provident-Fund configuration</b> (Phase 8 slice 4; catalog §14; F11 Payroll Statutory) —
/// the establishment's EPFO registration facts the PF computation needs. Present (non-<c>null</c> on
/// <see cref="Company.PfConfig"/>) once the establishment is enrolled for PF; a company that never enrols carries
/// no config and serialises byte-identically to a pre-v33 company (ER-13). Pure data — framework-, DB-, clock-
/// and RNG-free. The dedicated <c>PfContribution</c> engine reads the rate + cap flag from here.
/// </summary>
public sealed class PfConfig
{
    /// <summary>The employee/employer EPF rate in basis points: <b>1200</b> (12%, the default) or <b>1000</b> (10%,
    /// for a special establishment — &lt;20 employees / sick / jute-beedi-brick-coir-guar-gum). The 8.33% EPS split
    /// and the 0.5% EDLI / admin rates are fixed and do not vary with this toggle.</summary>
    public int EpfRateBasisPoints { get; set; } = PfConfig.DefaultEpfRateBasisPoints;

    /// <summary>The EPFO <b>establishment / PF code</b> (the registered establishment id printed on the ECR and the
    /// challan); optional (may be captured later).</summary>
    public string? EstablishmentCode { get; set; }

    /// <summary>Whether EPF wages are, by default, capped at the ₹15,000 statutory ceiling (the recommended
    /// default). A per-employee "contribute on higher wages" opt-in
    /// (<see cref="Employee.PfContributeOnHigherWages"/>) overrides it for that member.</summary>
    public bool CapWagesAtCeiling { get; set; } = true;

    /// <summary>The default EPF rate (12%) in basis points.</summary>
    public const int DefaultEpfRateBasisPoints = 1200;

    /// <summary>The reduced EPF rate (10%) in basis points for a special establishment.</summary>
    public const int ReducedEpfRateBasisPoints = 1000;

    public PfConfig() { }

    public PfConfig(int epfRateBasisPoints, string? establishmentCode = null, bool capWagesAtCeiling = true)
    {
        if (epfRateBasisPoints is not (DefaultEpfRateBasisPoints or ReducedEpfRateBasisPoints))
            throw new ArgumentException(
                $"EPF rate must be {DefaultEpfRateBasisPoints} (12%) or {ReducedEpfRateBasisPoints} (10%) basis points.",
                nameof(epfRateBasisPoints));
        EpfRateBasisPoints = epfRateBasisPoints;
        EstablishmentCode = string.IsNullOrWhiteSpace(establishmentCode) ? null : establishmentCode.Trim();
        CapWagesAtCeiling = capWagesAtCeiling;
    }
}
