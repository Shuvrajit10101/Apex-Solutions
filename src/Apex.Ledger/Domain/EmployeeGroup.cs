namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>Employee Group</b> (Phase 8 slice 1; Study Guide pp.193–195) — the optional department / division /
/// function classification tree for employees (mirrors <see cref="Group"/> / <see cref="CostCentre"/>). Each
/// group nests under an optional <see cref="ParentId"/> (another employee group in the same company), forming a
/// hierarchy that payroll reports roll up. The <see cref="DefineSalaryDetails"/> flag records the
/// "Define salary details?" choice (a salary structure attached at group level — built in the later structure
/// slice; captured here).
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place. Parent
/// cycles are rejected by <c>PayrollService</c> (reusing <see cref="HierarchyOrdering"/> on persist). Not seeded
/// on company creation (ER-13). Framework- and DB-agnostic.
/// </remarks>
public sealed class EmployeeGroup
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company (case-insensitive); a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Parent employee group; <c>null</c> ⇒ this is a top-level (primary) group.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>"Define salary details?" — whether a salary structure is defined at this group level.</summary>
    public bool DefineSalaryDetails { get; set; }

    /// <summary>A primary group has no parent (it sits at the top of the tree).</summary>
    public bool IsPrimary => ParentId is null;

    public EmployeeGroup(
        Guid id,
        string name,
        Guid? parentId = null,
        string? alias = null,
        bool defineSalaryDetails = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Employee group name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        ParentId = parentId;
        Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        DefineSalaryDetails = defineSalaryDetails;
    }
}
