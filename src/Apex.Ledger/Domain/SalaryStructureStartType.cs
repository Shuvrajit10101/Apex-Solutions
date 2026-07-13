namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Start Type</b> chosen when defining a <see cref="SalaryStructure"/> (Phase 8 slice 2; Study Guide
/// pp.211–212) — Tally's "Copy From Parent Value / Copy From Employee / Start Afresh". It is a UI seeding choice
/// recorded on the structure (the engine treats the resulting lines identically). Stored as the enum ordinal
/// (0 = StartAfresh).
/// </summary>
public enum SalaryStructureStartType
{
    /// <summary>Start Afresh — begin with an empty structure.</summary>
    StartAfresh = 0,

    /// <summary>Copy From Parent Value — seed the lines from the employee group's structure.</summary>
    CopyFromParent = 1,

    /// <summary>Copy From Employee — seed the lines from another employee's structure.</summary>
    CopyFromEmployee = 2,
}
