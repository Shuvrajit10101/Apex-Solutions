namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Salary Structure</b> ("Salary Details") — a <b>dated</b> set of <see cref="PayHead"/> assignments for an
/// employee or an employee group (Phase 8 slice 2; RQ-5; Study Guide pp.211–212). It mirrors the dated
/// <see cref="PriceList"/> tier pattern: a structure has an <see cref="EffectiveFrom"/> date and an ordered list
/// of <see cref="SalaryStructureLine"/>s, and a <b>revision</b> is a NEW structure for the same
/// <c>(Scope, ScopeId)</c> with a later <see cref="EffectiveFrom"/> — older structures are retained, never
/// mutated. The structure "in force" on a voucher date is the one for that scope with the latest
/// <see cref="EffectiveFrom"/> ≤ the date (ER-4; resolved by <c>SalaryStructureService.InForceOn</c>).
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key. Line validity (a line's value matches its pay head's calculation type,
/// no duplicate pay head, dense ordering) and effective-date/uniqueness guards live in
/// <c>SalaryStructureService</c>. Not seeded on company creation (ER-13). Framework- and DB-agnostic — no clock.
/// </remarks>
public sealed class SalaryStructure
{
    private readonly List<SalaryStructureLine> _lines;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Whether this structure is defined at employee or employee-group level.</summary>
    public SalaryStructureScope Scope { get; }

    /// <summary>The <see cref="Employee"/> id or <see cref="EmployeeGroup"/> id the structure applies to
    /// (per <see cref="Scope"/>).</summary>
    public Guid ScopeId { get; }

    /// <summary>The date this structure version is effective from (ER-4); the revision-resolution key.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>How the structure was seeded (a UI choice, recorded for fidelity).</summary>
    public SalaryStructureStartType StartType { get; set; }

    /// <summary>The pay-head lines, in ascending <see cref="SalaryStructureLine.Order"/>.</summary>
    public IReadOnlyList<SalaryStructureLine> Lines => _lines;

    public SalaryStructure(
        Guid id,
        SalaryStructureScope scope,
        Guid scopeId,
        DateOnly effectiveFrom,
        SalaryStructureStartType startType,
        IEnumerable<SalaryStructureLine> lines)
    {
        if (scopeId == Guid.Empty)
            throw new ArgumentException("A salary structure must reference an employee or employee group.", nameof(scopeId));

        Id = id;
        Scope = scope;
        ScopeId = scopeId;
        EffectiveFrom = effectiveFrom;
        StartType = startType;
        _lines = new List<SalaryStructureLine>(lines ?? throw new ArgumentNullException(nameof(lines)));
    }
}

/// <summary>
/// One line of a <see cref="SalaryStructure"/> (Phase 8 slice 2) — a <see cref="PayHead"/> assigned to the
/// structure, with the per-employee value that its calculation type requires: a Flat-Rate head needs a flat
/// <see cref="Amount"/>; an On-Attendance / On-Production head needs a rate <see cref="Amount"/>; an
/// As-Computed-Value head needs no per-employee amount (the formula lives on the pay head); an As-User-Defined
/// head is left blank (entered at the voucher). The <see cref="Order"/> fixes display + resolution order.
/// </summary>
public sealed class SalaryStructureLine
{
    /// <summary>The pay head this line assigns.</summary>
    public Guid PayHeadId { get; }

    /// <summary>0-based position in the structure (ascending; dense — validated by the service).</summary>
    public int Order { get; }

    /// <summary>The per-employee value/rate; <c>null</c> for computed / user-defined heads (ER-2 — see the
    /// service's line-vs-calc-type guard).</summary>
    public Money? Amount { get; }

    public SalaryStructureLine(Guid payHeadId, int order, Money? amount = null)
    {
        if (payHeadId == Guid.Empty)
            throw new ArgumentException("A salary structure line must reference a pay head.", nameof(payHeadId));
        if (order < 0)
            throw new ArgumentException("A salary structure line order must be ≥ 0.", nameof(order));

        PayHeadId = payHeadId;
        Order = order;
        Amount = amount;
    }
}
