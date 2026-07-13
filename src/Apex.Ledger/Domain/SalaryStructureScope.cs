namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>scope</b> a <see cref="SalaryStructure"/> ("Salary Details") is defined at (Phase 8 slice 2; Study
/// Guide pp.211–212) — an <see cref="EmployeeGroup"/> (a template shared by everyone under the group) or a
/// single <see cref="Employee"/> (an individual structure that may be seeded/copied from the group). Stored as
/// the enum ordinal (0 = EmployeeGroup).
/// </summary>
public enum SalaryStructureScope
{
    /// <summary>Defined at Employee-Group level (a template for the group).</summary>
    EmployeeGroup = 0,

    /// <summary>Defined at Employee level (an individual structure).</summary>
    Employee = 1,
}
