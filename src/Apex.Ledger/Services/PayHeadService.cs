using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Pay Head</b> master service (Phase 8 slice 2; RQ-4). Mirrors <see cref="PayrollService"/> (masters +
/// guards) and <see cref="InventoryService"/>: pure, deterministic mutation over the <see cref="Company"/>
/// aggregate — framework-, DB-, clock- and RNG-free. It creates/alters/deletes pay heads across all pay-head
/// types and all five calculation types, and enforces the slice's integrity rules:
/// <list type="bullet">
///   <item>name unique within a company (case-insensitive);</item>
///   <item>the accounting group (<see cref="PayHead.UnderGroupId"/>) and attendance-type references exist, and
///     an On-Attendance / On-Production head links a kind-appropriate attendance type;</item>
///   <item>an <see cref="PayHeadCalculationType.AsComputedValue"/> head carries a computation whose basis
///     references resolve, are not self-referential, and — the key adversarial rule — form <b>no cycle</b>
///     (A computed on B computed on A is rejected);</item>
///   <item>a non-computed head carries no computation;</item>
///   <item>a pay head is delete-blocked while another head computes on it or a salary structure references it.</item>
/// </list>
/// <b>No salary computation ships in this slice</b> (that is the slice-3 payroll-voucher engine) and <b>no ledger
/// is auto-created</b> (deferred to slice 3) — the pay head only captures its accounting classification. The
/// service throws <see cref="InvalidOperationException"/> on any violation, never mutating the company.
/// </summary>
public sealed class PayHeadService
{
    private readonly Company _company;

    public PayHeadService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a pay head of the given type + calculation type. Optional accounting-group linkage
    /// (<paramref name="underGroupId"/>), income-tax tag, gratuity flag, rounding, calculation period, attendance
    /// linkage and — for <see cref="PayHeadCalculationType.AsComputedValue"/> — the computation formula are
    /// validated here. <paramref name="affectsNetSalary"/> defaults from the type when omitted.
    /// </summary>
    public PayHead CreatePayHead(
        string name,
        PayHeadType type,
        PayHeadCalculationType calculationType,
        Guid? underGroupId = null,
        bool? affectsNetSalary = null,
        IncomeTaxComponent incomeTaxComponent = IncomeTaxComponent.NotApplicable,
        bool useForGratuity = false,
        PayHeadRoundingMethod roundingMethod = PayHeadRoundingMethod.NotApplicable,
        Money? roundingLimit = null,
        PayHeadCalculationPeriod calculationPeriod = PayHeadCalculationPeriod.Month,
        Guid? attendanceTypeId = null,
        int? perDayCalculationBasisDays = null,
        PayHeadComputation? computation = null,
        string? displayName = null,
        PfStatutoryComponent pfComponent = PfStatutoryComponent.None,
        bool partOfPfWages = false,
        EsiStatutoryComponent esiComponent = EsiStatutoryComponent.None,
        bool partOfEsiWages = false,
        bool isOvertime = false)
    {
        var trimmed = RequireName(name);
        if (_company.FindPayHeadByName(trimmed) is not null)
            throw new InvalidOperationException($"A pay head named '{trimmed}' already exists.");

        var id = Guid.NewGuid();
        var payHead = new PayHead(id, trimmed, type, calculationType)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            UnderGroupId = underGroupId,
            IncomeTaxComponent = incomeTaxComponent,
            UseForGratuity = useForGratuity,
            RoundingMethod = roundingMethod,
            RoundingLimit = roundingLimit ?? Money.Zero,
            CalculationPeriod = calculationPeriod,
            AttendanceTypeId = attendanceTypeId,
            PerDayCalculationBasisDays = perDayCalculationBasisDays,
            Computation = computation,
            PfComponent = pfComponent,
            PartOfPfWages = partOfPfWages,
            EsiComponent = esiComponent,
            PartOfEsiWages = partOfEsiWages,
            IsOvertime = isOvertime,
        };
        if (affectsNetSalary is { } a) payHead.AffectsNetSalary = a;

        ValidatePayHead(payHead);
        _company.AddPayHead(payHead);
        return payHead;
    }

    /// <summary>Renames a pay head in place (stable id), rejecting a clash with another head.</summary>
    public void RenamePayHead(Guid payHeadId, string newName)
    {
        var payHead = _company.FindPayHead(payHeadId)
            ?? throw new InvalidOperationException($"Pay head {payHeadId} not found.");
        var trimmed = RequireName(newName);
        var clash = _company.FindPayHeadByName(trimmed);
        if (clash is not null && clash.Id != payHeadId)
            throw new InvalidOperationException($"A pay head named '{trimmed}' already exists.");
        payHead.Name = trimmed;
    }

    /// <summary>Sets (or clears) a pay head's computation formula, re-validating calc-type consistency, basis
    /// references and the no-cycle rule. Passing <c>null</c> clears the formula (valid only for a non-computed
    /// head).</summary>
    public void SetComputation(Guid payHeadId, PayHeadComputation? computation)
    {
        var payHead = _company.FindPayHead(payHeadId)
            ?? throw new InvalidOperationException($"Pay head {payHeadId} not found.");
        var previous = payHead.Computation;
        payHead.Computation = computation;
        try { ValidatePayHead(payHead); }
        catch { payHead.Computation = previous; throw; }
    }

    /// <summary>Deletes a pay head, blocked while another head computes on it or a salary structure references it.</summary>
    public void DeletePayHead(Guid payHeadId)
    {
        var payHead = _company.FindPayHead(payHeadId)
            ?? throw new InvalidOperationException($"Pay head {payHeadId} not found.");

        foreach (var other in _company.PayHeads)
            if (other.Id != payHeadId && other.Computation is { } comp)
                foreach (var c in comp.BasisComponents)
                    if (c.PayHeadId == payHeadId)
                        throw new InvalidOperationException(
                            $"Pay head '{payHead.Name}' is used in the computation of '{other.Name}' and cannot be deleted.");

        foreach (var structure in _company.SalaryStructures)
            foreach (var line in structure.Lines)
                if (line.PayHeadId == payHeadId)
                    throw new InvalidOperationException(
                        $"Pay head '{payHead.Name}' is used in a salary structure and cannot be deleted.");

        _company.RemovePayHead(payHead);
    }

    // ------------------------------------------------------------------ validation

    private void ValidatePayHead(PayHead payHead)
    {
        if (payHead.UnderGroupId is { } gid && _company.FindGroup(gid) is null)
            throw new InvalidOperationException($"Pay head '{payHead.Name}' posts under a group ({gid}) that does not exist.");

        if (payHead.LedgerId is { } lid && _company.FindLedger(lid) is null)
            throw new InvalidOperationException($"Pay head '{payHead.Name}' links a ledger ({lid}) that does not exist.");

        if (payHead.RoundingMethod == PayHeadRoundingMethod.NotApplicable)
        {
            if (payHead.RoundingLimit != Money.Zero)
                throw new InvalidOperationException($"Pay head '{payHead.Name}' has a rounding limit but no rounding method.");
        }
        else if (payHead.RoundingLimit <= Money.Zero)
        {
            throw new InvalidOperationException($"Pay head '{payHead.Name}' needs a positive rounding limit for its rounding method.");
        }
        else if (!payHead.RoundingLimit.IsPaisaExact)
        {
            // The rounding limit is the multiple the amount snaps to (k × limit). A sub-paisa limit (e.g. ₹0.005)
            // yields a sub-paisa result the balanced-voucher validator accepts but the integer-paisa store rejects,
            // so reject it up front at the master-save boundary (F6).
            throw new InvalidOperationException(
                $"Pay head '{payHead.Name}' rounding limit {payHead.RoundingLimit} must be a whole number of paisa.");
        }

        ValidateAttendanceLinkage(payHead);
        ValidateComputation(payHead);
    }

    private void ValidateAttendanceLinkage(PayHead payHead)
    {
        switch (payHead.CalculationType)
        {
            case PayHeadCalculationType.OnAttendance:
            {
                if (payHead.AttendanceTypeId is not { } aid)
                    throw new InvalidOperationException($"On-Attendance pay head '{payHead.Name}' must link an attendance type.");
                var at = _company.FindAttendanceType(aid)
                    ?? throw new InvalidOperationException($"Pay head '{payHead.Name}' links an attendance type ({aid}) that does not exist.");
                if (at.Kind == AttendanceTypeKind.Production)
                    throw new InvalidOperationException($"On-Attendance pay head '{payHead.Name}' must link an attendance/leave type, not a production type.");
                break;
            }
            case PayHeadCalculationType.OnProduction:
            {
                if (payHead.AttendanceTypeId is not { } pid)
                    throw new InvalidOperationException($"On-Production pay head '{payHead.Name}' must link a production type.");
                var pt = _company.FindAttendanceType(pid)
                    ?? throw new InvalidOperationException($"Pay head '{payHead.Name}' links a production type ({pid}) that does not exist.");
                if (pt.Kind != AttendanceTypeKind.Production)
                    throw new InvalidOperationException($"On-Production pay head '{payHead.Name}' must link a Production-kind attendance type.");
                break;
            }
            default:
                if (payHead.AttendanceTypeId is not null)
                    throw new InvalidOperationException($"Pay head '{payHead.Name}' is not attendance/production based and must not link an attendance type.");
                break;
        }
    }

    private void ValidateComputation(PayHead payHead)
    {
        if (payHead.CalculationType != PayHeadCalculationType.AsComputedValue)
        {
            if (payHead.Computation is not null)
                throw new InvalidOperationException($"Pay head '{payHead.Name}' is not As-Computed-Value and must not carry a computation formula.");
            return;
        }

        if (payHead.Computation is not { } computation)
            throw new InvalidOperationException($"As-Computed-Value pay head '{payHead.Name}' must carry a computation formula.");
        if (computation.BasisComponents.Count == 0)
            throw new InvalidOperationException($"As-Computed-Value pay head '{payHead.Name}' must compute on at least one pay head.");
        if (computation.Slabs.Count == 0)
            throw new InvalidOperationException($"As-Computed-Value pay head '{payHead.Name}' must carry at least one slab (a percentage or a value) to turn its basis into an amount.");

        var seen = new HashSet<Guid>();
        foreach (var component in computation.BasisComponents)
        {
            if (component.PayHeadId == payHead.Id)
                throw new InvalidOperationException($"Pay head '{payHead.Name}' cannot compute on itself.");
            if (_company.FindPayHead(component.PayHeadId) is null)
                throw new InvalidOperationException($"Pay head '{payHead.Name}' computes on a pay head ({component.PayHeadId}) that does not exist.");
            if (!seen.Add(component.PayHeadId))
                throw new InvalidOperationException($"Pay head '{payHead.Name}' references the same computation component more than once.");
        }

        EnsureNoCycle(payHead.Id, computation);
    }

    /// <summary>
    /// Rejects a computed-on cycle. Starting from the proposed basis components of <paramref name="payHeadId"/>,
    /// it walks the computed-on graph (each visited head's stored computation supplying its out-edges); if it ever
    /// reaches <paramref name="payHeadId"/> the formula closes a cycle (e.g. A computed on B computed on A). The
    /// walk uses the <b>proposed</b> computation for the head under validation and the stored computation for
    /// every other head, so an Alter that would introduce a cycle is caught before it is applied (ER-3).
    /// </summary>
    private void EnsureNoCycle(Guid payHeadId, PayHeadComputation computation)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        foreach (var component in computation.BasisComponents)
            stack.Push(component.PayHeadId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == payHeadId)
                throw new InvalidOperationException(
                    $"Pay head {payHeadId} would form a computed-on cycle (a pay head cannot, directly or transitively, be computed on itself).");
            if (!visited.Add(current)) continue;
            if (_company.FindPayHead(current)?.Computation is { } comp)
                foreach (var component in comp.BasisComponents)
                    stack.Push(component.PayHeadId);
        }
    }

    private static string RequireName(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A pay head name is required.");
        return trimmed;
    }
}
