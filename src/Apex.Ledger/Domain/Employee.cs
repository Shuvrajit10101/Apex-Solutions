namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>Employee</b> master (Phase 8 slice 1; Study Guide pp.193–195) — a distinct payroll master (not an
/// ordinary <see cref="Ledger"/>) that sits under a required <see cref="EmployeeGroup"/> and an optional
/// <see cref="EmployeeCategory"/>. It carries the identity + statutory-identifier + bank fields the catalog captures.
/// <b>This slice is masters only</b> — the statutory identifiers (PAN / UAN / ESI / PF a/c) and the
/// <see cref="ApplicableTaxRegime"/> are captured and validated at the master-save boundary but drive no
/// computation yet (PF/ESI/§192 are later slices).
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key (rename-in-place, ER-6); the <see cref="Name"/> is unique within a
/// company. Structural validation of PAN/UAN/ESI lives in <c>PayrollService</c> (the master-save boundary), so a
/// rehydrated/imported employee is trusted. Not seeded on company creation (ER-13). Framework- and DB-agnostic.
/// </remarks>
public sealed class Employee
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company (case-insensitive); a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>The <see cref="EmployeeGroup"/> this employee belongs to; required.</summary>
    public Guid EmployeeGroupId { get; set; }

    /// <summary>The optional <see cref="EmployeeCategory"/> classification; <c>null</c> ⇒ uncategorised.</summary>
    public Guid? EmployeeCategoryId { get; set; }

    /// <summary>Employee number / code (payroll id); optional.</summary>
    public string? EmployeeNumber { get; set; }

    /// <summary>Date of joining; optional.</summary>
    public DateOnly? DateOfJoining { get; set; }

    /// <summary>Date of leaving / resignation; optional.</summary>
    public DateOnly? DateOfLeaving { get; set; }

    public string? Designation { get; set; }
    public string? Function { get; set; }
    public string? Location { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>PAN (validated per <see cref="Pan"/> at the service boundary when set); optional.</summary>
    public string? Pan { get; set; }

    public string? Aadhaar { get; set; }

    /// <summary>Universal Account Number (12 digits; validated at the service boundary when set); optional.</summary>
    public string? Uan { get; set; }

    public string? PfAccountNumber { get; set; }

    /// <summary>Whether Provident Fund applies to this employee (Phase 8 slice 4). When <c>false</c> the payroll
    /// engine computes no PF for the member even if PF pay heads sit in the structure. Additive, defaults
    /// <c>false</c> so an existing employee is byte-identical (ER-13); set <c>true</c> when the employee is enrolled
    /// for PF.</summary>
    public bool PfApplicable { get; set; }

    /// <summary>Whether the employee has opted to <b>contribute on wages above the ₹15,000 ceiling</b> (Phase 8
    /// slice 4). When <c>true</c>, EPF is computed on the full (uncapped) PF wages; EPS and EDLI stay capped at
    /// ₹15,000 regardless. Additive, defaults <c>false</c> (the recommended default = cap at the ceiling).</summary>
    public bool PfContributeOnHigherWages { get; set; }

    /// <summary>The date the employee joined / became a PF member (Phase 8 slice 4); optional. The UAN
    /// (<see cref="Uan"/>) already carries the member's universal account number from slice 1.</summary>
    public DateOnly? PfJoinDate { get; set; }

    /// <summary>ESIC Insurance Number (17 digits; validated at the service boundary when set); optional.</summary>
    public string? EsiNumber { get; set; }

    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankIfsc { get; set; }

    /// <summary>The employee's elected income-tax regime for §192 (default <see cref="TaxRegime.New"/>, DP-2).</summary>
    public TaxRegime ApplicableTaxRegime { get; set; } = TaxRegime.New;

    public Employee(Guid id, string name, Guid employeeGroupId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Employee name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        EmployeeGroupId = employeeGroupId;
    }
}
