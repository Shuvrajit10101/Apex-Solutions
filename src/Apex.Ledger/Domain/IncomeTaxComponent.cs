namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Income-Tax component</b> tag a <see cref="PayHead"/> carries (Phase 8 slice 2; catalog §14; Study Guide
/// pp.198–210) — how the head is treated in the §192 salary-TDS computation (the Phase-8 slice-7 regime engine
/// reads this back). A representative Tally set; <b>no tax is computed in this slice</b>. Stored as the enum
/// ordinal (0 = NotApplicable).
/// </summary>
public enum IncomeTaxComponent
{
    /// <summary>Not tagged for income-tax (default).</summary>
    NotApplicable = 0,

    /// <summary>Basic Salary.</summary>
    BasicSalary = 1,

    /// <summary>Dearness Allowance.</summary>
    DearnessAllowance = 2,

    /// <summary>House Rent Allowance (drives the old-regime HRA exemption).</summary>
    HouseRentAllowance = 3,

    /// <summary>Conveyance / Transport Allowance.</summary>
    ConveyanceAllowance = 4,

    /// <summary>Special / other taxable allowance.</summary>
    SpecialAllowance = 5,

    /// <summary>Medical reimbursement.</summary>
    MedicalReimbursement = 6,

    /// <summary>Bonus.</summary>
    Bonus = 7,

    /// <summary>Gratuity.</summary>
    Gratuity = 8,

    /// <summary>Fully exempt from income-tax.</summary>
    FullyExempt = 9,

    /// <summary>
    /// <b>§192 Tax Deducted at Source on salary</b> (Phase 8 slice 7) — the marker that a
    /// <see cref="PayHeadType.EmployeesStatutoryDeductions"/> pay head <b>is</b> the salary-TDS withholding head:
    /// its monthly amount is computed by the dedicated §192 regime engine (<c>SalaryIncomeTax</c>) — annual tax on
    /// the estimated salary spread average-rate over the FY months — rather than the head's ordinary calculation
    /// slabs. Additive; every earning-classification value above is unchanged, so a pre-slice-7 pay head is
    /// byte-identical (ER-13). Stored as this enum ordinal (10) in the existing <c>income_tax_component</c> column
    /// (no schema change) and round-tripped by name in Io.
    /// </summary>
    TaxDeductedAtSource = 10,
}
