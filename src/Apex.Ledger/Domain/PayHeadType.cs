namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Pay Head Type</b> (Phase 8 slice 2; catalog §14; Study Guide pp.198–210) — the accounting/statutory
/// nature of a <see cref="PayHead"/>. It drives the default "affect net salary" side (earnings and deductions
/// affect net pay; employer contributions/charges and the gratuity provision are employer cost booked separately)
/// and which accounting group the pay head posts <b>Under</b>. These are the Tally pay-head types a faithful clone
/// preserves. Stored as the enum ordinal (0 = Earnings).
/// </summary>
public enum PayHeadType
{
    /// <summary>Earnings for employees (Basic, DA, HRA, allowances) — an income/expense head that adds to net pay.</summary>
    Earnings = 0,

    /// <summary>Deductions from employees (a general deduction, e.g. a recovery) — reduces net pay.</summary>
    Deductions = 1,

    /// <summary>Employees' Statutory Deductions (Employee PF, ESI, Professional Tax, Income-Tax) — reduces net pay.</summary>
    EmployeesStatutoryDeductions = 2,

    /// <summary>Employer's Statutory Contributions (Employer EPF/EPS, Employer ESI) — employer cost, not in net pay.</summary>
    EmployersStatutoryContributions = 3,

    /// <summary>Employer's Other Charges (EDLI, PF admin charges) — employer cost, not in net pay.</summary>
    EmployersOtherCharges = 4,

    /// <summary>Gratuity — the statutory gratuity provision, employer cost, not in net pay.</summary>
    Gratuity = 5,

    /// <summary>Loans &amp; Advances to employees — a recoverable advance deducted from pay.</summary>
    LoansAndAdvances = 6,

    /// <summary>Reimbursements to employees (medical, conveyance) — added to pay.</summary>
    Reimbursements = 7,

    /// <summary>Bonus — statutory / performance bonus, added to pay.</summary>
    Bonus = 8,

    /// <summary>Not Applicable — a payable/round-off head that neither earns nor deducts by default.</summary>
    NotApplicable = 9,
}
