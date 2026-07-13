namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>gender scope</b> of a <see cref="PtSlab"/> table (Phase 8 slice 6). Professional Tax is a state subject
/// and only <b>Maharashtra</b> differentiates by gender (women enjoy a higher exemption threshold), so a state's
/// slab table is normally <see cref="Any"/> (gender-agnostic — Karnataka, West Bengal, …) and only Maharashtra
/// seeds a <see cref="Male"/> table and a <see cref="Female"/> table. The active table for an employee is resolved
/// from <see cref="Employee.Gender"/> against the configured state (see <c>PtConfig.ResolveSlab</c>). Stored as the
/// enum ordinal (0 = Any).
/// </summary>
public enum PtGenderScope
{
    /// <summary>Gender-agnostic — the table applies to every employee in the state (the norm).</summary>
    Any = 0,

    /// <summary>Applies to male employees (Maharashtra's general slab).</summary>
    Male = 1,

    /// <summary>Applies to female employees (Maharashtra's higher-exemption slab).</summary>
    Female = 2,
}
