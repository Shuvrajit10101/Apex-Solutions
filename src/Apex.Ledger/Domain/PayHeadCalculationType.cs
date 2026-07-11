namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Calculation Type</b> of a <see cref="PayHead"/> (Phase 8 slice 2; catalog §14; Study Guide pp.198–210)
/// — the five Tally methods by which a pay head's amount is derived. <b>No computation ships in this slice</b>
/// (that is the Phase-8 slice-3 payroll-voucher engine); this enum classifies how the later engine will resolve
/// the head, and gates which per-employee value a <see cref="SalaryStructureLine"/> must carry. Stored as the
/// enum ordinal (0 = OnAttendance).
/// </summary>
public enum PayHeadCalculationType
{
    /// <summary>On Attendance / Leave with Pay — amount = rate × attended units (needs an attendance-type link and
    /// a per-employee rate).</summary>
    OnAttendance = 0,

    /// <summary>Flat Rate — a fixed monthly amount (the per-employee flat amount lives on the structure line).</summary>
    FlatRate = 1,

    /// <summary>As Computed Value — computed on a basis of other pay heads via a percentage and/or slab bands (the
    /// formula lives on the pay head, e.g. "PF = 12% of Basic + DA").</summary>
    AsComputedValue = 2,

    /// <summary>On Production — amount = rate × produced units (needs a Production attendance-type link and a
    /// per-employee rate).</summary>
    OnProduction = 3,

    /// <summary>As User-Defined Value — entered ad-hoc at the payroll voucher (no fixed structure amount).</summary>
    AsUserDefinedValue = 4,
}
