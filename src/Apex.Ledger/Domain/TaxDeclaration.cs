namespace Apex.Ledger.Domain;

/// <summary>
/// A payroll <see cref="Employee"/>'s <b>income-tax declaration</b> (Phase 8 slice 7; Form 12BB) — the
/// investment / exemption / prior-income figures the employee declares so the employer can estimate the §192
/// salary-TDS. Mostly relevant to the <b>old regime</b> (the new regime allows only the standard deduction plus
/// employer-NPS 80CCD(2)); the new regime ignores the Chapter VI-A / HRA / 24(b) fields.
/// <para>
/// Pure data — framework-, DB-, clock- and RNG-free. Additive: an employee without a declaration is byte-identical
/// to a pre-v36 employee (ER-13); the §192 engine treats a missing declaration as all-zero.
/// </para>
/// </summary>
public sealed class TaxDeclaration
{
    /// <summary>§80C statutory ceiling — ₹1,50,000.</summary>
    public const decimal Section80CCap = 1_50_000m;

    /// <summary>§80CCD(1B) additional-NPS ceiling — ₹50,000.</summary>
    public const decimal Section80CCD1BCap = 50_000m;

    /// <summary>§24(b) self-occupied house-property interest set-off ceiling — ₹2,00,000.</summary>
    public const decimal HomeLoanInterestCap = 2_00_000m;

    /// <summary>The employee this declaration is for (the stable <see cref="Employee.Id"/>).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>§80C investments (LIC / PPF / ELSS / principal …), capped at ₹1,50,000 (old regime).</summary>
    public Money Section80C { get; set; } = Money.Zero;

    /// <summary>§80D medical-insurance premium (old regime).</summary>
    public Money Section80D { get; set; } = Money.Zero;

    /// <summary>§80CCD(1B) additional NPS, capped at ₹50,000 (old regime).</summary>
    public Money Section80CCD1B { get; set; } = Money.Zero;

    /// <summary>§80CCD(2) employer NPS contribution — allowed in <b>both</b> regimes.</summary>
    public Money Section80CCD2Employer { get; set; } = Money.Zero;

    /// <summary>§10(13A) House Rent Allowance exemption (old regime).</summary>
    public Money HouseRentAllowanceExempt { get; set; } = Money.Zero;

    /// <summary>§24(b) home-loan interest (self-occupied loss), capped at ₹2,00,000 (old regime).</summary>
    public Money HomeLoanInterest24b { get; set; } = Money.Zero;

    /// <summary>Other income the employee has declared to the employer (added to the estimate).</summary>
    public Money OtherIncome { get; set; } = Money.Zero;

    /// <summary>Salary earned with a previous employer this FY (Form 12B), added to the estimate.</summary>
    public Money PreviousEmployerSalary { get; set; } = Money.Zero;

    /// <summary>TDS already deducted by the previous employer this FY, credited against the estimated annual tax.</summary>
    public Money PreviousEmployerTds { get; set; } = Money.Zero;

    /// <summary>
    /// The deductions/exemptions allowed under the <paramref name="regime"/> (subtracted from estimated income to
    /// reach taxable income). New regime: only §80CCD(2) employer-NPS. Old regime: §80C (capped ₹1.5L) + §80D +
    /// §80CCD(1B) (capped ₹50k) + §80CCD(2) + HRA exemption + §24(b) home-loan interest (capped ₹2L). Each statutory
    /// ceiling is applied here so the caller passes a single allowed-deductions figure to the engine.
    /// </summary>
    public Money AllowedDeductions(TaxRegime regime)
    {
        if (regime == TaxRegime.New)
            return Section80CCD2Employer;

        var c80 = Math.Min(Section80C.Amount, Section80CCap);
        var ccd1b = Math.Min(Section80CCD1B.Amount, Section80CCD1BCap);
        var loan = Math.Min(HomeLoanInterest24b.Amount, HomeLoanInterestCap);
        return new Money(
            c80 + Section80D.Amount + ccd1b + Section80CCD2Employer.Amount + HouseRentAllowanceExempt.Amount + loan);
    }

    /// <summary>Income added to the salary estimate regardless of regime — declared other income + previous-employer
    /// salary this FY.</summary>
    public Money AdditionalIncome => OtherIncome + PreviousEmployerSalary;
}
