namespace Apex.Ledger.Domain;

/// <summary>
/// The income-tax regime a payroll <see cref="Employee"/> has elected for §192 salary-TDS estimation (Phase 8
/// slice 1; captured now, consumed by the later §192 slice — DP-2). <see cref="New"/> is the statutory default
/// u/s 115BAC; <see cref="Old"/> is the opt-in regime carrying Chapter VI-A deductions. Stored as the enum
/// ordinal (0 = New), so an employee defaults to the new regime.
/// </summary>
public enum TaxRegime
{
    /// <summary>New regime — the statutory default u/s 115BAC.</summary>
    New = 0,

    /// <summary>Old regime — opt-in, with Chapter VI-A deductions.</summary>
    Old = 1,
}
