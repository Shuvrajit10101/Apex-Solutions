using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Salary Structure</b> ("Salary Details") service (Phase 8 slice 2; RQ-5). Pure, deterministic mutation
/// over the <see cref="Company"/> aggregate — framework-, DB-, clock- and RNG-free. It defines dated per-employee
/// / per-group pay-head structures and enforces the slice's rules:
/// <list type="bullet">
///   <item>the scope target exists (an <see cref="Employee"/> for an employee-scoped structure, an
///     <see cref="EmployeeGroup"/> for a group-scoped one);</item>
///   <item><b>dated revision</b>: at most one structure per <c>(Scope, ScopeId)</c> per effective-from date; a
///     later effective-from supersedes from its date (older versions are retained);</item>
///   <item>each line references an existing pay head at most once, in dense ascending order, and its value
///     <b>matches the pay head's calculation type</b> (Flat-Rate / On-Attendance / On-Production need an amount;
///     As-Computed-Value / As-User-Defined must not carry one).</item>
/// </list>
/// The structure "in force" on a date is resolved by <see cref="InForceOn"/> (ER-4). The service throws
/// <see cref="InvalidOperationException"/> on any violation, never mutating the company.
/// </summary>
public sealed class SalaryStructureService
{
    private readonly Company _company;

    public SalaryStructureService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>Defines a salary structure for an <see cref="Employee"/> effective from a date, seeded per
    /// <paramref name="startType"/> (a UI seeding choice; the lines are supplied explicitly).</summary>
    public SalaryStructure DefineForEmployee(
        Guid employeeId,
        DateOnly effectiveFrom,
        IEnumerable<SalaryStructureLine> lines,
        SalaryStructureStartType startType = SalaryStructureStartType.StartAfresh)
    {
        if (_company.FindEmployee(employeeId) is null)
            throw new InvalidOperationException($"Employee {employeeId} not found.");
        return Define(SalaryStructureScope.Employee, employeeId, effectiveFrom, lines, startType);
    }

    /// <summary>Defines a salary structure for an <see cref="EmployeeGroup"/> effective from a date.</summary>
    public SalaryStructure DefineForGroup(
        Guid employeeGroupId,
        DateOnly effectiveFrom,
        IEnumerable<SalaryStructureLine> lines,
        SalaryStructureStartType startType = SalaryStructureStartType.StartAfresh)
    {
        if (_company.FindEmployeeGroup(employeeGroupId) is null)
            throw new InvalidOperationException($"Employee group {employeeGroupId} not found.");
        return Define(SalaryStructureScope.EmployeeGroup, employeeGroupId, effectiveFrom, lines, startType);
    }

    private SalaryStructure Define(
        SalaryStructureScope scope,
        Guid scopeId,
        DateOnly effectiveFrom,
        IEnumerable<SalaryStructureLine> lines,
        SalaryStructureStartType startType)
    {
        var lineList = (lines ?? throw new ArgumentNullException(nameof(lines))).ToList();
        ValidateLines(lineList);

        if (_company.SalaryStructures.Any(s =>
                s.Scope == scope && s.ScopeId == scopeId && s.EffectiveFrom == effectiveFrom))
            throw new InvalidOperationException(
                $"A salary structure effective from {effectiveFrom:yyyy-MM-dd} already exists for this {(scope == SalaryStructureScope.Employee ? "employee" : "employee group")}.");

        var structure = new SalaryStructure(Guid.NewGuid(), scope, scopeId, effectiveFrom, startType, lineList);
        _company.AddSalaryStructure(structure);
        return structure;
    }

    /// <summary>
    /// The salary structure "in force" for a scope on <paramref name="date"/> — the version for that
    /// <c>(scope, scopeId)</c> with the latest <see cref="SalaryStructure.EffectiveFrom"/> ≤ the date, or
    /// <c>null</c> when none applies yet (mirrors the price-list <c>RateInForce</c> rule; ER-4). Deterministic,
    /// culture-invariant.
    /// </summary>
    public SalaryStructure? InForceOn(SalaryStructureScope scope, Guid scopeId, DateOnly date) =>
        _company.SalaryStructures
            .Where(s => s.Scope == scope && s.ScopeId == scopeId && s.EffectiveFrom <= date)
            .OrderByDescending(s => s.EffectiveFrom)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault();

    /// <summary>Deletes a salary structure version.</summary>
    public void DeleteSalaryStructure(Guid structureId)
    {
        var structure = _company.FindSalaryStructure(structureId)
            ?? throw new InvalidOperationException($"Salary structure {structureId} not found.");
        _company.RemoveSalaryStructure(structure);
    }

    // ------------------------------------------------------------------ validation

    private void ValidateLines(IReadOnlyList<SalaryStructureLine> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("A salary structure must carry at least one pay-head line.");

        var seenPayHeads = new HashSet<Guid>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Order != i)
                throw new InvalidOperationException(
                    $"Salary structure lines must be densely ordered from 0 (line {i} has order {line.Order}).");

            var payHead = _company.FindPayHead(line.PayHeadId)
                ?? throw new InvalidOperationException($"Salary structure line references a pay head ({line.PayHeadId}) that does not exist.");

            if (!seenPayHeads.Add(line.PayHeadId))
                throw new InvalidOperationException($"Pay head '{payHead.Name}' appears more than once in the salary structure.");

            ValidateLineAgainstCalcType(payHead, line);
        }
    }

    private static void ValidateLineAgainstCalcType(PayHead payHead, SalaryStructureLine line)
    {
        switch (payHead.CalculationType)
        {
            case PayHeadCalculationType.FlatRate:
            case PayHeadCalculationType.OnAttendance:
            case PayHeadCalculationType.OnProduction:
                if (line.Amount is null)
                    throw new InvalidOperationException(
                        $"Pay head '{payHead.Name}' ({payHead.CalculationType}) needs a per-employee amount on its salary structure line.");
                if (line.Amount.Value < Money.Zero)
                    throw new InvalidOperationException($"Pay head '{payHead.Name}' has a negative amount on its salary structure line.");
                break;

            case PayHeadCalculationType.AsComputedValue:
            case PayHeadCalculationType.AsUserDefinedValue:
                if (line.Amount is not null)
                    throw new InvalidOperationException(
                        $"Pay head '{payHead.Name}' ({payHead.CalculationType}) is computed/entered at the voucher and must not carry a structure-line amount.");
                break;
        }
    }
}
