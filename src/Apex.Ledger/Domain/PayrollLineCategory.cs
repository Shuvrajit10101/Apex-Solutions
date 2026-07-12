namespace Apex.Ledger.Domain;

/// <summary>
/// The accounting role a <see cref="PayrollLineDetail"/> plays on a Payroll voucher (Phase 8 slice 3; catalog
/// §14; ER-1). Every entry line of a Payroll voucher self-describes which side of the integrated posting it is,
/// so the payslip / pay-sheet / register read the per-employee breakdown straight off the posted voucher without
/// recomputing (mirror of the GST/TDS self-describing line detail). Stored as the enum ordinal (0 = Earning).
/// <para>
/// The employee run balances by construction: Σ <see cref="Earning"/> (Dr expense) =
/// Σ <see cref="Deduction"/> (Cr payable) + <see cref="NetPayable"/> (Cr Salary Payable). Employer
/// contributions are a <b>separate</b> balanced pair — <see cref="EmployerContributionExpense"/> (Dr expense) =
/// <see cref="EmployerContributionPayable"/> (Cr employer payable) — that never enters net pay (ER-1).
/// </para>
/// </summary>
public enum PayrollLineCategory
{
    /// <summary>An earnings component (Basic, DA, HRA, allowances, reimbursements, bonus) — posted <b>Dr</b> to
    /// its expense ledger; adds to gross earnings and net pay.</summary>
    Earning = 0,

    /// <summary>An employee deduction (a general/statutory deduction, income-tax, an advance recovery) — posted
    /// <b>Cr</b> to its payable ledger; reduces net pay.</summary>
    Deduction = 1,

    /// <summary>The net Salary-Payable amount for the employee (gross − deductions) — posted <b>Cr</b> to the
    /// company Salary-Payable ledger. Its <see cref="PayrollLineDetail.PayHeadId"/> is <c>null</c> (it is not a
    /// pay head; it is the residual).</summary>
    NetPayable = 2,

    /// <summary>The employer's side of an employer statutory contribution / other charge / gratuity provision —
    /// the <b>Dr</b> to the employer expense ledger (the debit half of the balanced employer pair). Employer cost;
    /// not in net pay.</summary>
    EmployerContributionExpense = 3,

    /// <summary>The employer's side of an employer statutory contribution / other charge / gratuity provision —
    /// the <b>Cr</b> to the employer payable ledger (the credit half of the balanced employer pair). Employer
    /// cost; not in net pay.</summary>
    EmployerContributionPayable = 4,
}
