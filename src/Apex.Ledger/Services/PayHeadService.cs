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
        bool isOvertime = false,
        PtStatutoryComponent ptComponent = PtStatutoryComponent.None)
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
            PtComponent = ptComponent,
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

    /// <summary>
    /// <b>Alters a pay head in place</b>, keeping its identity (census rows 7.6 and 7.16; defect T2-38).
    ///
    /// <para>🔴 <b>WHY THIS EXISTS: IT CLOSES A PERMANENT WRONG-MONEY PATH.</b> Before it, this service offered
    /// <see cref="CreatePayHead"/>, <see cref="RenamePayHead"/>, <see cref="SetComputation"/> and
    /// <see cref="DeletePayHead"/> and nothing else — so a pay head whose <b>rate</b>, <b>Under</b> group,
    /// pay-head <b>type</b>, statutory component, rounding or attendance link was mistyped could never be
    /// corrected. Renaming it does not fix the rate, and the only other recovery — delete and re-create — is
    /// refused the moment any salary structure references the head or another head computes on it, which is the
    /// normal state of a configured book. A wrong deduction rate therefore stayed wrong on <i>every payslip
    /// thereafter</i>. That is wrong money, not a convenience gap.</para>
    ///
    /// <para><b>The alteration is FORWARD-ONLY, and that is a property of the posting model rather than a promise
    /// made here.</b> A posted payroll voucher carries its own immutable <see cref="PayrollLineDetail"/> per line
    /// — the employee, the pay head, the accounting <c>Category</c> and the computed <c>Amount</c> — and the
    /// payslip, pay sheet and registers read that posted detail instead of recomputing from the master. So
    /// altering a head (even flipping its <see cref="PayHead.Type"/> from an earning to a deduction) cannot reach
    /// backwards and restate a period already paid; it governs the next computation. Pinned by
    /// <c>PayHeadAlterTests.Altering_a_pay_head_does_not_restate_an_already_posted_payroll_voucher</c>.</para>
    ///
    /// <para><b>All-or-nothing.</b> Every field is written to the live head and then
    /// <see cref="ValidatePayHead"/> runs; on ANY refusal the head is restored field-by-field from the snapshot
    /// taken first, so a rejected alteration leaves the aggregate byte-identical. Validating a detached copy
    /// instead is not an option: the cycle guard and the self-reference rule have to see the head <i>as the
    /// company holds it</i> (see <see cref="EnsureNoCycle"/>, which reads stored computations for every other
    /// head and the proposed one for this head — which is exactly what an in-place write gives it).</para>
    ///
    /// <para>Fidelity (R7 / ruling 14): the vendor reaches master alteration with <b>Alt+G (Go To) → Alter
    /// Master</b>, selects the master, and saves with <b>Ctrl+A</b>
    /// (<c>help.tallysolutions.com/ledgers-in-tallyprime/</c>;
    /// <c>help.tallysolutions.com/tally-prime/payroll-masters/tax-configuration-tally/</c> uses the same route to
    /// reach Income Tax Pay Heads Details). This application's in-product equivalent — arrow to the row on the
    /// master's existing-list, Ctrl+Enter to open it for alteration, Ctrl+A to accept — is the shape already
    /// shipped by the five payroll masters that had alteration before this one, and the pay head now joins it.</para>
    /// </summary>
    /// <para>🔴 <b>THE SHAPE OF THE ARGUMENT IS A SAFETY PROPERTY, NOT A STYLE CHOICE. READ THIS BEFORE CHANGING
    /// IT BACK.</b> This method used to take twenty-one loose parameters of which <b>sixteen carried defaults</b>,
    /// so <c>AlterPayHead(id, name, type, calcType)</c> compiled — and silently RESET the income-tax component,
    /// the gratuity flag, the rounding method and limit, the calculation period (to Month), the attendance link,
    /// the per-day basis, the computation formula, the display name and all four statutory tags. On an ALTER,
    /// omission meant <i>destroy</i>. The field most likely to be wiped that way is the computation — i.e. the
    /// RATE — which is the very thing this method exists to let an operator correct, and the shipped screen was
    /// safe only because <c>PayHeadMasterViewModel</c> hand-copied six fields and relied on the rest arriving
    /// pre-filled: a comment-enforced invariant, not a compiler-enforced one. The fixture in
    /// <c>PayHeadAlterTests</c> itself used the four-argument form three times.
    /// <br/>It now takes one <see cref="PayHeadEdit"/> whose every member is <c>required</c>, so no field can be
    /// defaulted into existence, and whose intended construction is
    /// <c>PayHeadEdit.From(head) with { … }</c> — <b>omission therefore means KEEP</b>, which is what an
    /// alteration means everywhere else in this product. A caller who genuinely wants to clear a field says so.</para>
    /// <returns>The same <see cref="PayHead"/> instance, altered.</returns>
    public PayHead AlterPayHead(Guid payHeadId, PayHeadEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var payHead = _company.FindPayHead(payHeadId)
            ?? throw new InvalidOperationException($"Pay head {payHeadId} not found.");

        var trimmed = RequireName(edit.Name);
        // Uniqueness EXCLUDES the head being altered — otherwise saving a head without renaming it would be
        // refused by its own name. Checked before anything is written, so a clash cannot leave a partial state.
        var clash = _company.FindPayHeadByName(trimmed);
        if (clash is not null && clash.Id != payHeadId)
            throw new InvalidOperationException($"A pay head named '{trimmed}' already exists.");

        var snapshot = PayHeadState.Capture(payHead);
        payHead.Name = trimmed;
        payHead.DisplayName = string.IsNullOrWhiteSpace(edit.DisplayName) ? null : edit.DisplayName.Trim();
        payHead.Type = edit.Type;
        payHead.CalculationType = edit.CalculationType;
        payHead.UnderGroupId = edit.UnderGroupId;
        payHead.AffectsNetSalary = edit.AffectsNetSalary ?? PayHead.DefaultAffectsNetSalary(edit.Type);
        payHead.IncomeTaxComponent = edit.IncomeTaxComponent;
        payHead.UseForGratuity = edit.UseForGratuity;
        payHead.RoundingMethod = edit.RoundingMethod;
        payHead.RoundingLimit = edit.RoundingLimit ?? Money.Zero;
        payHead.CalculationPeriod = edit.CalculationPeriod;
        payHead.AttendanceTypeId = edit.AttendanceTypeId;
        payHead.PerDayCalculationBasisDays = edit.PerDayCalculationBasisDays;
        payHead.Computation = edit.Computation;
        payHead.PfComponent = edit.PfComponent;
        payHead.PartOfPfWages = edit.PartOfPfWages;
        payHead.EsiComponent = edit.EsiComponent;
        payHead.PartOfEsiWages = edit.PartOfEsiWages;
        payHead.IsOvertime = edit.IsOvertime;
        payHead.PtComponent = edit.PtComponent;

        try { ValidatePayHead(payHead); }
        catch { snapshot.RestoreTo(payHead); throw; }

        return payHead;
    }

    /// <summary>
    /// How many <b>salary structures</b> currently name <paramref name="payHeadId"/> on a line (census 7.6;
    /// review finding F11).
    ///
    /// <para>🔴 <b>WHY THIS IS A COUNT AND NOT A REFUSAL.</b> <see cref="DeletePayHead"/> refuses outright while a
    /// structure references the head, and copying that rule here would close the one wrong-money path this whole
    /// slice exists to open: a mistyped rate on a REFERENCED head is the normal case, and refusing to correct it
    /// is what made the wrong figure permanent. So an alteration on a referenced head is allowed — and the
    /// operator is told how far it reaches, because the reach is not visible on the screen they are standing on.
    /// The alteration is forward-only (a posted payroll voucher carries its own immutable
    /// <see cref="PayrollLineDetail"/>), so what this number describes is exactly the NEXT payroll run, which is
    /// what the notice says.</para>
    /// </summary>
    public int SalaryStructuresUsing(Guid payHeadId)
    {
        var count = 0;
        foreach (var structure in _company.SalaryStructures)
            foreach (var line in structure.Lines)
                if (line.PayHeadId == payHeadId) { count++; break; }
        return count;
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
        ValidateStatutoryComponentRole(payHead);
        ValidateNpsComponentSide(payHead);
    }

    /// <summary>
    /// Rejects a <b>statutory-component</b> head whose pay-head type posts on the wrong accounting side (F3): a
    /// Professional-Tax or Employee PF/ESI head must be an <b>employee deduction</b>, and an Employer
    /// PF/Pension/EDLI/admin or Employer-ESI head an <b>employer contribution / charge</b>. A mis-typed statutory head
    /// (e.g. a Professional-Tax component tagged onto an Employer's-Statutory-Contributions head) would otherwise post
    /// a phantom, self-balancing pair on the wrong side. Enforced uniformly for all three statutory systems (PF, ESI,
    /// PT) against <see cref="PayrollComputationService.RoleOf"/>, and mirrored by the Io import pre-flight (the
    /// direct-construction import path bypasses this service).
    /// </summary>
    private static void ValidateStatutoryComponentRole(PayHead payHead)
    {
        if (PayrollComputationService.RequiredStatutoryRole(
                payHead.PtComponent, payHead.PfComponent, payHead.EsiComponent, payHead.IncomeTaxComponent) is not { } required)
            return;
        var actual = PayrollComputationService.RoleOf(payHead.Type);
        if (actual != required)
            throw new InvalidOperationException(
                $"Pay head '{payHead.Name}' carries a statutory component that must post as {required}, but its " +
                $"pay-head type '{payHead.Type}' posts as {actual}.");
    }

    /// <summary>
    /// Rejects an <b>NPS</b> statutory pay type on a pay-head type the scheme has no side for (census row 7.18).
    /// NPS is the one statutory tag in this book that is legitimately <b>two-sided</b> — Tier-I is valid both as an
    /// employee deduction and as the employer's contribution — so it cannot go through
    /// <see cref="ValidateStatutoryComponentRole"/>, which enforces a <i>single</i> required posting role. The rule
    /// itself lives in <see cref="NationalPensionScheme.IsPayHeadTypeAllowed"/> with its vendor citation; this is
    /// only the master-creation gate. Mirrored by the Io import pre-flight, which bypasses this service.
    /// </summary>
    private static void ValidateNpsComponentSide(PayHead payHead)
    {
        if (!NationalPensionScheme.IsNpsComponent(payHead.IncomeTaxComponent)) return;
        if (NationalPensionScheme.IsPayHeadTypeAllowed(payHead.IncomeTaxComponent, payHead.Type)) return;
        throw new InvalidOperationException(
            NationalPensionScheme.WrongSideMessage(payHead.Name, payHead.IncomeTaxComponent, payHead.Type));
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

/// <summary>
/// The complete set of fields <see cref="PayHeadService.AlterPayHead"/> writes — the argument to an alteration.
///
/// <para>🔴 <b>EVERY MEMBER IS <c>required</c>, AND THAT IS THE WHOLE REASON THE TYPE EXISTS.</b> The method it
/// replaced took twenty-one loose parameters, sixteen of them defaulted, so a four-argument call compiled and
/// silently reset the computation formula (the RATE), the statutory tags, the rounding, the calculation period
/// and the attendance link. <c>required</c> means no field can be defaulted into existence: a caller either
/// states it or starts from <see cref="From"/>.</para>
///
/// <para><b>The intended call is <c>PayHeadEdit.From(head) with { … }</c>.</b> That inverts the old default:
/// a field you do not mention KEEPS its current value instead of being cleared, which is what "alter" means
/// everywhere else in this product. Clearing a field is then something a caller has to write down
/// (<c>with { Computation = null }</c>), which is the correct place for that decision to be visible.</para>
///
/// <para><see cref="AffectsNetSalary"/> stays nullable because <c>null</c> has a MEANING the other fields do not
/// have: derive it from the pay-head <see cref="Type"/> (<c>PayHead.DefaultAffectsNetSalary</c>). <see cref="From"/>
/// captures the head's current value rather than null, so a round-trip through it is lossless.</para>
/// </summary>
public sealed record PayHeadEdit
{
    public required string Name { get; init; }
    public required string? DisplayName { get; init; }
    public required PayHeadType Type { get; init; }
    public required PayHeadCalculationType CalculationType { get; init; }

    /// <summary><c>null</c> means "derive from <see cref="Type"/>"; a value overrides it.</summary>
    public required bool? AffectsNetSalary { get; init; }

    public required Guid? UnderGroupId { get; init; }
    public required IncomeTaxComponent IncomeTaxComponent { get; init; }
    public required bool UseForGratuity { get; init; }
    public required PayHeadRoundingMethod RoundingMethod { get; init; }

    /// <summary><c>null</c> is read as <see cref="Money.Zero"/>, matching the create path.</summary>
    public required Money? RoundingLimit { get; init; }

    public required PayHeadCalculationPeriod CalculationPeriod { get; init; }
    public required Guid? AttendanceTypeId { get; init; }
    public required int? PerDayCalculationBasisDays { get; init; }
    public required PayHeadComputation? Computation { get; init; }
    public required PfStatutoryComponent PfComponent { get; init; }
    public required bool PartOfPfWages { get; init; }
    public required EsiStatutoryComponent EsiComponent { get; init; }
    public required bool PartOfEsiWages { get; init; }
    public required bool IsOvertime { get; init; }
    public required PtStatutoryComponent PtComponent { get; init; }

    /// <summary>
    /// The head exactly as it stands — the base every alteration should be built from, so an unmentioned field is
    /// KEPT rather than reset. <c>LedgerId</c> and <c>EmployerExpenseLedgerId</c> are deliberately absent from
    /// this type: <see cref="PayHeadService.AlterPayHead"/> does not write them, and carrying a field an
    /// alteration ignores would be a knob that silently does nothing.
    /// </summary>
    public static PayHeadEdit From(PayHead p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new PayHeadEdit
        {
            Name = p.Name,
            DisplayName = p.DisplayName,
            Type = p.Type,
            CalculationType = p.CalculationType,
            AffectsNetSalary = p.AffectsNetSalary,
            UnderGroupId = p.UnderGroupId,
            IncomeTaxComponent = p.IncomeTaxComponent,
            UseForGratuity = p.UseForGratuity,
            RoundingMethod = p.RoundingMethod,
            RoundingLimit = p.RoundingLimit,
            CalculationPeriod = p.CalculationPeriod,
            AttendanceTypeId = p.AttendanceTypeId,
            PerDayCalculationBasisDays = p.PerDayCalculationBasisDays,
            Computation = p.Computation,
            PfComponent = p.PfComponent,
            PartOfPfWages = p.PartOfPfWages,
            EsiComponent = p.EsiComponent,
            PartOfEsiWages = p.PartOfEsiWages,
            IsOvertime = p.IsOvertime,
            PtComponent = p.PtComponent,
        };
    }
}

/// <summary>
/// Every mutable field of a <see cref="PayHead"/>, captured before an in-place alteration so a refused one can be
/// undone exactly (census 7.6 / defect T2-38).
///
/// <para>🔴 <b>It captures the fields BY NAME, one per line, and that is the point.</b> The cheaper alternatives —
/// restoring only the handful of fields a guard is known to read, or trusting the caller to re-apply the old values
/// — are how a rejected alteration leaves the aggregate holding half of it, which then persists on the NEXT
/// unrelated save. Adding a settable property to <see cref="PayHead"/> without adding it here is the failure mode;
/// <c>PayHeadAlterTests.Every_settable_pay_head_field_is_carried_by_the_rollback_snapshot</c> walks the type by
/// reflection so that omission goes red instead of silent.</para>
///
/// <para><b>Public because the rollback is needed on BOTH failure boundaries.</b>
/// <see cref="PayHeadService.AlterPayHead"/> restores it when a domain guard refuses. But the caller has a second
/// one the engine cannot see: the alteration is written to the shared in-memory aggregate BEFORE the company is
/// saved, so a store failure (a second instance holding the write lock, a read-only or full disk) would otherwise
/// leave the aggregate altered and the <c>.db</c> not — and every LATER save would then quietly persist an
/// alteration the operator was told had failed. That is the same defect W0-13 B7 found on the create path.</para>
/// </summary>
public readonly record struct PayHeadState(
    string Name,
    string? DisplayName,
    PayHeadType Type,
    PayHeadCalculationType CalculationType,
    bool AffectsNetSalary,
    Guid? UnderGroupId,
    Guid? LedgerId,
    Guid? EmployerExpenseLedgerId,
    PfStatutoryComponent PfComponent,
    bool PartOfPfWages,
    EsiStatutoryComponent EsiComponent,
    bool PartOfEsiWages,
    bool IsOvertime,
    PtStatutoryComponent PtComponent,
    IncomeTaxComponent IncomeTaxComponent,
    bool UseForGratuity,
    PayHeadRoundingMethod RoundingMethod,
    Money RoundingLimit,
    PayHeadCalculationPeriod CalculationPeriod,
    Guid? AttendanceTypeId,
    int? PerDayCalculationBasisDays,
    PayHeadComputation? Computation)
{
    /// <summary>Captures every settable field of <paramref name="p"/>.</summary>
    public static PayHeadState Capture(PayHead p) => new(
        p.Name, p.DisplayName, p.Type, p.CalculationType, p.AffectsNetSalary, p.UnderGroupId, p.LedgerId,
        p.EmployerExpenseLedgerId, p.PfComponent, p.PartOfPfWages, p.EsiComponent, p.PartOfEsiWages,
        p.IsOvertime, p.PtComponent, p.IncomeTaxComponent, p.UseForGratuity, p.RoundingMethod,
        p.RoundingLimit, p.CalculationPeriod, p.AttendanceTypeId, p.PerDayCalculationBasisDays, p.Computation);

    /// <summary>Writes every captured field back onto <paramref name="p"/>. Idempotent, so it is safe for a caller
    /// to restore a state the engine has already restored.</summary>
    public void RestoreTo(PayHead p)
    {
        ArgumentNullException.ThrowIfNull(p);
        p.Name = Name;
        p.DisplayName = DisplayName;
        p.Type = Type;
        p.CalculationType = CalculationType;
        p.AffectsNetSalary = AffectsNetSalary;
        p.UnderGroupId = UnderGroupId;
        p.LedgerId = LedgerId;
        p.EmployerExpenseLedgerId = EmployerExpenseLedgerId;
        p.PfComponent = PfComponent;
        p.PartOfPfWages = PartOfPfWages;
        p.EsiComponent = EsiComponent;
        p.PartOfEsiWages = PartOfEsiWages;
        p.IsOvertime = IsOvertime;
        p.PtComponent = PtComponent;
        p.IncomeTaxComponent = IncomeTaxComponent;
        p.UseForGratuity = UseForGratuity;
        p.RoundingMethod = RoundingMethod;
        p.RoundingLimit = RoundingLimit;
        p.CalculationPeriod = CalculationPeriod;
        p.AttendanceTypeId = AttendanceTypeId;
        p.PerDayCalculationBasisDays = PerDayCalculationBasisDays;
        p.Computation = Computation;
    }
}
