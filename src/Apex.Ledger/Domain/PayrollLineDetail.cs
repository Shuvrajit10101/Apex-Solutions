namespace Apex.Ledger.Domain;

/// <summary>
/// The per-employee computed salary detail hung off a Payroll-voucher <see cref="EntryLine"/> (Phase 8 slice 3;
/// catalog §14). It self-describes the line — which <see cref="EmployeeId"/> the amount is for, which
/// <see cref="PayHeadId"/> produced it (<c>null</c> for the net Salary-Payable line), its accounting
/// <see cref="Category"/> and the computed <see cref="Amount"/> — so the payslip / pay-sheet / register read the
/// breakdown straight off the posted voucher without recomputing (the mirror of <see cref="GstLineTax"/> /
/// <see cref="TdsLineTax"/>). Immutable.
/// </summary>
/// <remarks>
/// <b>Invariant</b>: the detail's <see cref="Amount"/> always equals the entry line's own amount — they are
/// posted together and the payslip reads the detail as the ledger posting. <see cref="EntryLine"/> enforces this
/// at its single construction choke point (posting, import, tests all flow through it), so a hand-edited import
/// can never persist a <c>payroll_lines</c> amount that diverges from the ledger it foots to.
/// </remarks>
public sealed class PayrollLineDetail
{
    /// <summary>The employee this computed line is for.</summary>
    public Guid EmployeeId { get; }

    /// <summary>The pay head that produced this line; <c>null</c> for the net Salary-Payable line (the residual,
    /// which is not a pay head).</summary>
    public Guid? PayHeadId { get; }

    /// <summary>Which side of the integrated posting this line is (earning / deduction / net / employer pair).</summary>
    public PayrollLineCategory Category { get; }

    /// <summary>The computed amount (equal to the entry line's amount, paisa-exact).</summary>
    public Money Amount { get; }

    public PayrollLineDetail(Guid employeeId, Guid? payHeadId, PayrollLineCategory category, Money amount)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("A payroll line detail must reference an employee.", nameof(employeeId));
        if (category == PayrollLineCategory.NetPayable)
        {
            if (payHeadId is not null)
                throw new ArgumentException("The net Salary-Payable line is not a pay head; its pay head must be null.", nameof(payHeadId));
        }
        else if (payHeadId is null)
        {
            throw new ArgumentException($"A {category} payroll line must reference a pay head.", nameof(payHeadId));
        }
        if (amount.Amount <= 0m)
            throw new ArgumentException("A payroll line amount must be > 0.", nameof(amount));

        EmployeeId = employeeId;
        PayHeadId = payHeadId;
        Category = category;
        Amount = amount;
    }
}
