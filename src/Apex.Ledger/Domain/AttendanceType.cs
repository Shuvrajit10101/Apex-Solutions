namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>Attendance / Production Type</b> (Phase 8 slice 1; Study Guide pp.196–197) — a named attendance,
/// leave or production calendar type (mirrors <see cref="Unit"/> / <see cref="StockCategory"/>). It nests under
/// an optional <see cref="ParentId"/> (another attendance type), carries a <see cref="Kind"/>
/// (Attendance/Leave-with-Pay · Leave-without-Pay · Production · User-defined) and an optional
/// <see cref="PayrollUnitId"/> — the period unit (Days/Hrs) for an attendance type, or the production unit for a
/// Production type. <b>No computation ships in this slice</b>; the type is a master the later attendance engine
/// reads.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place. Parent
/// cycles are rejected by <c>PayrollService</c>. Not seeded on company creation (ER-13). Framework- and
/// DB-agnostic.
/// </remarks>
public sealed class AttendanceType
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company (case-insensitive); a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Parent attendance type; <c>null</c> ⇒ a top-level type.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>The calendar/production nature of this type.</summary>
    public AttendanceTypeKind Kind { get; set; }

    /// <summary>The period unit (Days/Hrs) or the production unit — a <see cref="PayrollUnit"/> id, or
    /// <c>null</c> when unset.</summary>
    public Guid? PayrollUnitId { get; set; }

    public AttendanceType(
        Guid id,
        string name,
        AttendanceTypeKind kind,
        Guid? parentId = null,
        Guid? payrollUnitId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attendance type name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        Kind = kind;
        ParentId = parentId;
        PayrollUnitId = payrollUnitId;
    }
}
