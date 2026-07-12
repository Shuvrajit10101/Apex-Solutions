namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Provident-Fund statutory role</b> of a <see cref="PayHead"/> (Phase 8 slice 4; catalog §14; EPFO
/// ContributionRate). A pay head tagged with a PF component is <b>not</b> evaluated by the generic
/// calculation-type slabs — the payroll engine computes it with the dedicated EPF/EPS/EDLI split logic
/// (<c>PfContribution</c>), because the statutory figures cannot be expressed as ordinary slabs: EPS is capped
/// at the ₹15,000 wage ceiling and ₹1,250, the employer EPF is the <i>residual</i> (employee share − EPS, never a
/// re-computed 3.67%), and the establishment admin charge has a floor applied once per challan, not per member.
/// <para>
/// The default <see cref="None"/> leaves a pay head on the ordinary computation path, so every pre-v33 pay head is
/// byte-identical (ER-13). Stored as the enum ordinal (0 = None).
/// </para>
/// </summary>
public enum PfStatutoryComponent
{
    /// <summary>Not a PF statutory head — evaluated by its ordinary <see cref="PayHeadCalculationType"/>.</summary>
    None = 0,

    /// <summary>Employee EPF (EPFO A/c 1, employee share) — 12% (or 10% for a special establishment) of EPF wages,
    /// an <see cref="PayHeadType.EmployeesStatutoryDeductions"/> head that reduces net pay.</summary>
    EmployeeProvidentFund = 1,

    /// <summary>Employer EPF (EPFO A/c 1, employer share) — the <b>residual</b> employee-share − EPS (never a
    /// re-computed 3.67%), an <see cref="PayHeadType.EmployersStatutoryContributions"/> head (employer cost).</summary>
    EmployerProvidentFund = 2,

    /// <summary>Employer Pension / EPS (EPFO A/c 10) — 8.33% of min(EPS wages, ₹15,000), capped at ₹1,250, an
    /// <see cref="PayHeadType.EmployersStatutoryContributions"/> head (employer cost).</summary>
    EmployerPension = 3,

    /// <summary>EDLI (EPFO A/c 21) — 0.5% of min(EDLI wages, ₹15,000), capped at ₹75, an
    /// <see cref="PayHeadType.EmployersOtherCharges"/> head (employer cost).</summary>
    EmployeesDepositLinkedInsurance = 4,

    /// <summary>EPF administration charges (EPFO A/c 2) — 0.5% of the establishment's total EPF wages, floored at
    /// ₹500 per month (₹75 when there is no contributory member). It is an <b>establishment-level</b> charge
    /// applied <b>once per payroll period / challan</b>, never per member, so a head tagged with it is <b>not</b>
    /// evaluated on the per-member salary line — the payroll voucher posts the aggregate as one balanced pair.</summary>
    ProvidentFundAdminCharges = 5,
}
