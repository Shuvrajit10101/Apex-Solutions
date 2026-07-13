namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Professional-Tax statutory role</b> of a <see cref="PayHead"/> (Phase 8 slice 6; catalog §14; Article 276).
/// A pay head tagged with the PT component is <b>not</b> evaluated by the generic calculation-type slabs — the
/// payroll engine computes it with the dedicated PT logic (<c>ProfessionalTax</c>), because the state slab tables
/// (flat-amount-by-band, a February over-charge, a gender-scoped exemption and a constitutional ₹2,500/year hard
/// cap on the cumulative FY deduction) cannot be expressed as ordinary slabs.
/// <para>
/// The default <see cref="None"/> leaves a pay head on the ordinary computation path, so every pre-v35 pay head is
/// byte-identical (ER-13). Stored as the enum ordinal (0 = None).
/// </para>
/// </summary>
public enum PtStatutoryComponent
{
    /// <summary>Not a PT statutory head — evaluated by its ordinary <see cref="PayHeadCalculationType"/>.</summary>
    None = 0,

    /// <summary>Professional Tax (a state slab deduction on the monthly PT-wages) — an
    /// <see cref="PayHeadType.EmployeesStatutoryDeductions"/> head that reduces net pay and is credited to the state
    /// ("Professional Tax Payable"). There is <b>no employer contribution</b> for PT.</summary>
    ProfessionalTax = 1,
}
