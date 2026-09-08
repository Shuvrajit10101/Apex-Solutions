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

    /// <summary>
    /// <b>National Pension Scheme (Tier – I)</b> — census row 7.18. The vendor's <i>Statutory Pay Type</i> for an
    /// NPS pay head, on <b>either</b> accounting side: an employee NPS deduction
    /// (<see cref="PayHeadType.EmployeesStatutoryDeductions"/>, under Current Liabilities) <b>or</b> the employer's
    /// NPS contribution (<see cref="PayHeadType.EmployersStatutoryContributions"/>, under Indirect Expenses). See
    /// <see cref="NationalPensionScheme"/> for the sourced facts, the side rule and the §80CCD mapping.
    /// <para>🔴 <b>Unlike <see cref="TaxDeductedAtSource"/>, this tag drives NO dedicated engine and computes
    /// nothing.</b> The vendor's own NPS pay head is an ordinary <i>As Computed Value</i> head — a user-entered
    /// Percentage slab on Basic Pay — so an NPS head stays on the generic <see cref="PayHeadCalculationType"/>
    /// path here too, exactly as it does in the vendor product. Nothing about the §192 estimate changes: §80CCD
    /// relief still comes from the employee's declared <c>TaxDeclaration.Section80CCD1B</c> /
    /// <c>Section80CCD2Employer</c> figure and is <b>not</b> re-derived from the posted pay head, so the two can
    /// never double-count.</para>
    /// <para><b>Why this enum and not a new column.</b> A statutory pay type is not, in itself, an income-tax
    /// classification — but the tag rides the existing <c>pay_heads.income_tax_component</c> column for the same
    /// reason <see cref="TaxDeductedAtSource"/> does (see <c>Schema.MigrateV35ToV36</c>'s note): it is a pure
    /// additive enum value on a column that already exists, so it needs <b>no schema version and no migration</b>,
    /// and an untagged pay head stays byte-identical (ER-13). Round-tripped by name in Io.</para>
    /// </summary>
    NationalPensionSchemeTierI = 11,

    /// <summary>
    /// <b>National Pension Scheme (Tier II)</b> — census row 7.18; the voluntary, withdrawable NPS account. An
    /// <b>employee-side deduction only</b>: the vendor states that <i>"Any contribution by the employer towards NPS
    /// will fall under the Tier I account of the scheme"</i>, so a Tier-II head typed as an employer contribution is
    /// rejected (<see cref="NationalPensionScheme.IsPayHeadTypeAllowed"/>).
    /// <para>🔴 <b>Tier-II attracts NO income-tax deduction at all</b> — <i>"contribution to this account does not
    /// attract any deduction under the Income Tax Act"</i> — so this value must never be read as a relief marker.
    /// It is a classification for the payslip and the Payroll Statutory Summary, nothing more. Same storage
    /// argument as <see cref="NationalPensionSchemeTierI"/>: ordinal 12 in the existing column, no schema change.</para>
    /// </summary>
    NationalPensionSchemeTierII = 12,
}
