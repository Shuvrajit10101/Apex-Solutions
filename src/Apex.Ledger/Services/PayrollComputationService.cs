using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>salary-computation engine</b> (Phase 8 slice 3; RQ-7; ER-1/ER-2/ER-3/ER-4) — a <b>pure, deterministic</b>
/// projection over the <see cref="Company"/> masters + posted attendance, framework-, DB-, clock- and RNG-free
/// (the mirror of <see cref="GstService"/>/<c>TdsService</c>). For one employee over one payroll period it:
/// <list type="number">
///   <item>resolves the employee's dated <see cref="SalaryStructure"/> in force on the period-end date (employee
///     scope first, else the employee's group scope — ER-4);</item>
///   <item>evaluates every pay head in the structure <b>in dependency order</b> (a computed head's basis may
///     reference other heads; the walk is memoised and <b>cycle-guarded</b> — ER-3);</item>
///   <item>applies each pay head's <see cref="PayHeadCalculationType"/> — Flat Rate, As-Computed-Value
///     (basis × slab bands), On-Attendance / Leave-with-Pay (pro-rated by attendance), On-Production
///     (rate × produced units), As-User-Defined-Value — and its rounding method (ER-2);</item>
///   <item>classifies each head into earning / employee-deduction / employer-contribution and derives gross
///     earnings, total deductions and net payable — all <see cref="Money"/>, paisa-exact.</item>
/// </list>
/// The result is a per-employee breakdown the <see cref="PayrollVoucherService"/> posts as a balanced integrated
/// voucher. <b>Conservation is by construction</b>: net is derived from the already-rounded line amounts
/// (gross − deductions), so Σ earnings = Σ deductions + net to the paisa.
/// </summary>
public sealed class PayrollComputationService
{
    private readonly Company _company;

    public PayrollComputationService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Computes the salary breakdown for <paramref name="employeeId"/> over <c>[periodFrom, periodTo]</c>. The
    /// salary structure in force on <paramref name="periodTo"/> is used (ER-4); attendance/production is summed
    /// over the period. <paramref name="userDefinedAmounts"/> supplies the value for each
    /// <see cref="PayHeadCalculationType.AsUserDefinedValue"/> head (by pay-head id); a required user-defined
    /// value that is missing is an error. Throws <see cref="InvalidOperationException"/> on any inconsistency
    /// (unknown employee, no structure in force, a formula cycle, a missing rate/basis), never returning a
    /// partial result.
    /// </summary>
    public PayrollComputationResult Compute(
        Guid employeeId,
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyDictionary<Guid, Money>? userDefinedAmounts = null)
    {
        if (periodTo < periodFrom)
            throw new InvalidOperationException("Payroll period end must be on or after its start.");

        var employee = _company.FindEmployee(employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        var structure = ResolveStructureInForce(employee, periodTo)
            ?? throw new InvalidOperationException(
                $"No salary structure is in force for employee '{employee.Name}' on {periodTo:yyyy-MM-dd}.");

        var linesByHead = new Dictionary<Guid, SalaryStructureLine>();
        foreach (var line in structure.Lines)
            linesByHead[line.PayHeadId] = line;

        var evaluator = new Evaluator(_company, employee, structure, linesByHead, periodFrom, periodTo, userDefinedAmounts);

        var computed = new List<PayrollComputedLine>(structure.Lines.Count);
        foreach (var line in structure.Lines)
        {
            var payHead = _company.FindPayHead(line.PayHeadId)
                ?? throw new InvalidOperationException($"Salary structure references a pay head ({line.PayHeadId}) that no longer exists.");
            var amount = evaluator.Evaluate(payHead.Id);
            computed.Add(new PayrollComputedLine(payHead, RoleOf(payHead.Type), amount, payHead.AffectsNetSalary));
        }

        return new PayrollComputationResult(employee.Id, structure.Id, periodFrom, periodTo, computed);
    }

    /// <summary>The employee's salary structure in force on <paramref name="date"/> — the employee-scoped version
    /// if any, else the employee's group-scoped version (ER-4). The structure is resolved <b>once</b>, as of the
    /// given date (the payroll period-end / voucher date), so a salary revision dated part-way through a period
    /// applies to the <b>whole</b> period — the month-end-dated convention (salary details are read as of the
    /// voucher date, not accrued day-by-day; F8). Deterministic, culture-invariant.</summary>
    public SalaryStructure? ResolveStructureInForce(Employee employee, DateOnly date)
    {
        var svc = new SalaryStructureService(_company);
        return svc.InForceOn(SalaryStructureScope.Employee, employee.Id, date)
            ?? svc.InForceOn(SalaryStructureScope.EmployeeGroup, employee.EmployeeGroupId, date);
    }

    /// <summary>The accounting posting role of a pay-head type (ER-1): earnings/reimbursements/bonus are
    /// <b>earnings</b> (Dr expense); employee/statutory deductions, advances and income-tax are <b>deductions</b>
    /// (Cr payable); employer contributions/charges and the gratuity provision are the <b>employer</b> pair (Dr
    /// expense / Cr employer payable). A <see cref="PayHeadType.NotApplicable"/> head neither earns nor deducts.</summary>
    public static PayHeadPostingRole RoleOf(PayHeadType type) => type switch
    {
        PayHeadType.Earnings => PayHeadPostingRole.Earning,
        PayHeadType.Reimbursements => PayHeadPostingRole.Earning,
        PayHeadType.Bonus => PayHeadPostingRole.Earning,
        PayHeadType.Deductions => PayHeadPostingRole.Deduction,
        PayHeadType.EmployeesStatutoryDeductions => PayHeadPostingRole.Deduction,
        PayHeadType.LoansAndAdvances => PayHeadPostingRole.Deduction,
        PayHeadType.EmployersStatutoryContributions => PayHeadPostingRole.EmployerContribution,
        PayHeadType.EmployersOtherCharges => PayHeadPostingRole.EmployerContribution,
        PayHeadType.Gratuity => PayHeadPostingRole.EmployerContribution,
        PayHeadType.NotApplicable => PayHeadPostingRole.None,
        _ => PayHeadPostingRole.None,
    };

    /// <summary>
    /// The memoised, cycle-guarded per-employee pay-head evaluator. Each pay head is evaluated at most once and
    /// its rounded amount cached; a re-entry during the recursion (an As-Computed-Value head whose basis closes a
    /// cycle) fails fast (ER-3), independently of the master-level cycle guard in <c>PayHeadService</c>.
    /// </summary>
    private sealed class Evaluator
    {
        private readonly Company _company;
        private readonly Employee _employee;
        private readonly SalaryStructure _structure;
        private readonly IReadOnlyDictionary<Guid, SalaryStructureLine> _linesByHead;
        private readonly DateOnly _from;
        private readonly DateOnly _to;
        private readonly IReadOnlyDictionary<Guid, Money>? _userDefined;
        private readonly Dictionary<Guid, Money> _cache = new();
        private readonly HashSet<Guid> _visiting = new();

        public Evaluator(
            Company company, Employee employee, SalaryStructure structure,
            IReadOnlyDictionary<Guid, SalaryStructureLine> linesByHead,
            DateOnly from, DateOnly to, IReadOnlyDictionary<Guid, Money>? userDefined)
        {
            _company = company;
            _employee = employee;
            _structure = structure;
            _linesByHead = linesByHead;
            _from = from;
            _to = to;
            _userDefined = userDefined;
        }

        public Money Evaluate(Guid payHeadId)
        {
            if (_cache.TryGetValue(payHeadId, out var cached)) return cached;
            if (!_visiting.Add(payHeadId))
                throw new InvalidOperationException(
                    $"Pay head {payHeadId} forms a computed-on cycle while computing salary (a head cannot, directly " +
                    "or transitively, be computed on itself).");

            var payHead = _company.FindPayHead(payHeadId)
                ?? throw new InvalidOperationException($"Salary structure references a pay head ({payHeadId}) that no longer exists.");

            var raw = EvaluateRaw(payHead);
            var amount = ApplyRounding(payHead, raw);

            _visiting.Remove(payHeadId);
            _cache[payHeadId] = amount;
            return amount;
        }

        private decimal EvaluateRaw(PayHead payHead)
        {
            switch (payHead.CalculationType)
            {
                case PayHeadCalculationType.FlatRate:
                    return RequireStructureAmount(payHead).Amount;

                case PayHeadCalculationType.AsComputedValue:
                {
                    var computation = payHead.Computation
                        ?? throw new InvalidOperationException($"As-Computed-Value pay head '{payHead.Name}' carries no computation formula.");
                    decimal basis = 0m;
                    foreach (var component in computation.BasisComponents)
                    {
                        var value = Evaluate(component.PayHeadId).Amount;
                        basis += component.IsSubtraction ? -value : value;
                    }
                    return EvaluateSlabs(basis, computation);
                }

                case PayHeadCalculationType.OnAttendance:
                {
                    var rate = RequireStructureAmount(payHead).Amount;
                    var attended = SumAttendance(payHead);
                    var basisDays = payHead.PerDayCalculationBasisDays
                        ?? (_to.DayNumber - _from.DayNumber + 1); // "as per calendar period" when no fixed basis
                    if (basisDays <= 0)
                        throw new InvalidOperationException($"On-Attendance pay head '{payHead.Name}' has a non-positive calculation basis.");
                    return rate * attended / basisDays;
                }

                case PayHeadCalculationType.OnProduction:
                {
                    var rate = RequireStructureAmount(payHead).Amount;
                    var produced = SumAttendance(payHead);
                    return rate * produced;
                }

                case PayHeadCalculationType.AsUserDefinedValue:
                    if (_userDefined is not null && _userDefined.TryGetValue(payHead.Id, out var v))
                        return v.Amount;
                    throw new InvalidOperationException(
                        $"As-User-Defined-Value pay head '{payHead.Name}' needs a value supplied on the payroll voucher.");

                default:
                    throw new InvalidOperationException($"Unsupported calculation type {payHead.CalculationType} on pay head '{payHead.Name}'.");
            }
        }

        private Money RequireStructureAmount(PayHead payHead)
        {
            if (!_linesByHead.TryGetValue(payHead.Id, out var line) || line.Amount is not { } amount)
                throw new InvalidOperationException(
                    $"Pay head '{payHead.Name}' ({payHead.CalculationType}) needs a per-employee amount on the salary structure.");
            return amount;
        }

        /// <summary>
        /// Σ attendance/production value for this pay head's linked attendance type over the payroll period, with
        /// each entry <b>clipped</b> to <c>[from, to]</c> (F3). An entry fully inside the period contributes its
        /// whole value; an entry that straddles a period boundary (e.g. a weekly record across a month-end)
        /// contributes only the share of its value that falls inside the period — pro-rated by the overlapping
        /// fraction of the entry's own day-span (the value is treated as uniformly spread across its dates), so the
        /// same record counts its correct share in each period it touches instead of being dropped from both. An
        /// entry with no overlap contributes nothing. Deterministic, culture-invariant.
        /// </summary>
        private decimal SumAttendance(PayHead payHead)
        {
            if (payHead.AttendanceTypeId is not { } attendanceTypeId)
                throw new InvalidOperationException(
                    $"{payHead.CalculationType} pay head '{payHead.Name}' must link an attendance/production type.");

            decimal sum = 0m;
            foreach (var e in _company.AttendanceEntries)
            {
                if (e.EmployeeId != _employee.Id) continue;
                if (e.AttendanceTypeId != attendanceTypeId) continue;

                // Overlap of the entry's own span with the payroll period, inclusive of both ends.
                var overlapFrom = e.FromDate > _from ? e.FromDate : _from;
                var overlapTo = e.ToDate < _to ? e.ToDate : _to;
                if (overlapTo < overlapFrom) continue; // no overlap with the period

                var overlapDays = overlapTo.DayNumber - overlapFrom.DayNumber + 1;
                var spanDays = e.ToDate.DayNumber - e.FromDate.DayNumber + 1;
                sum += overlapDays == spanDays ? e.Value : e.Value * overlapDays / spanDays;
            }
            return sum;
        }

        /// <summary>
        /// Evaluates the slab bands of a computed pay head against its <paramref name="basis"/> (the
        /// accounting-software "Amount Greater than / Amount Up to" + Slab Type Percentage / Value convention).
        /// <b>Percentage</b> slabs are marginal
        /// (income-tax style): each contributes its rate on the portion of the basis within its band, so a single
        /// unbanded "N%" slab is simply <c>rate × basis</c> and a "12% up to ₹15,000" slab is
        /// <c>12% × min(basis, 15000)</c>. <b>Value</b> slabs are select-one-band (PT style): the band whose
        /// <c>(from, to]</c> range contains the basis contributes its flat value. The two aggregate additively so
        /// a mixed set is well-defined.
        /// </summary>
        private static decimal EvaluateSlabs(decimal basis, PayHeadComputation computation)
        {
            decimal total = 0m;
            foreach (var slab in computation.Slabs)
            {
                var from = slab.FromAmount?.Amount ?? 0m;
                if (slab.SlabType == PayHeadComputationSlabType.Percentage)
                {
                    var upper = slab.ToAmount?.Amount ?? basis;
                    var portion = Math.Min(basis, upper) - from;
                    if (portion > 0m)
                        total += slab.RateBasisPoints / 10000m * portion;
                }
                else // FlatValue: band-selection on (from, to]
                {
                    var withinLower = slab.FromAmount is null || basis > from;
                    var withinUpper = slab.ToAmount is not { } t || basis <= t.Amount;
                    if (withinLower && withinUpper)
                        total += slab.Value.Amount;
                }
            }
            return total;
        }

        /// <summary>Applies a pay head's rounding method + limit to a raw computed value, returning a paisa-exact
        /// <see cref="Money"/>. <see cref="PayHeadRoundingMethod.NotApplicable"/> snaps to the paisa (2 dp) so the
        /// value can post through the integer-paisa store; Normal/Upward/Downward round to the nearest / next /
        /// previous multiple of the limit.</summary>
        private static Money ApplyRounding(PayHead payHead, decimal raw)
        {
            switch (payHead.RoundingMethod)
            {
                case PayHeadRoundingMethod.NotApplicable:
                    return new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero));
                case PayHeadRoundingMethod.Normal:
                {
                    var limit = payHead.RoundingLimit.Amount;
                    return new Money(Math.Round(raw / limit, 0, MidpointRounding.AwayFromZero) * limit);
                }
                case PayHeadRoundingMethod.Upward:
                {
                    var limit = payHead.RoundingLimit.Amount;
                    return new Money(Math.Ceiling(raw / limit) * limit);
                }
                case PayHeadRoundingMethod.Downward:
                {
                    var limit = payHead.RoundingLimit.Amount;
                    return new Money(Math.Floor(raw / limit) * limit);
                }
                default:
                    return new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero));
            }
        }
    }
}

/// <summary>The accounting posting role of a pay head in the salary computation (Phase 8 slice 3; ER-1).</summary>
public enum PayHeadPostingRole
{
    /// <summary>Neither an earning nor a deduction (a Not-Applicable head).</summary>
    None = 0,
    /// <summary>An earnings component — Dr expense, adds to gross + net.</summary>
    Earning = 1,
    /// <summary>An employee deduction — Cr payable, reduces net.</summary>
    Deduction = 2,
    /// <summary>An employer contribution / charge / gratuity provision — a separate balanced Dr/Cr pair.</summary>
    EmployerContribution = 3,
}

/// <summary>One evaluated pay head in a <see cref="PayrollComputationResult"/> (Phase 8 slice 3).</summary>
public sealed class PayrollComputedLine
{
    /// <summary>The evaluated pay head.</summary>
    public PayHead PayHead { get; }

    /// <summary>Its accounting posting role (earning / deduction / employer).</summary>
    public PayHeadPostingRole Role { get; }

    /// <summary>The computed amount (paisa-exact, rounded per the pay head's rounding method).</summary>
    public Money Amount { get; }

    /// <summary>The pay head's user-editable "Affect Net Salary?" flag (ER-1; F1). An <b>affecting</b> earning /
    /// deduction flows into <see cref="PayrollComputationResult.GrossEarnings"/> /
    /// <see cref="PayrollComputationResult.TotalDeductions"/> / net pay and is posted; a <b>non-affecting</b> one
    /// (a tracked-but-not-paid component) is still computed and recorded on this line — so a payslip / register can
    /// show it — but is excluded from those aggregates AND from the accounting posting. An employer contribution's
    /// posting is governed by its <see cref="Role"/>, not this flag.</summary>
    public bool AffectsNet { get; }

    public PayrollComputedLine(PayHead payHead, PayHeadPostingRole role, Money amount, bool affectsNet)
    {
        PayHead = payHead ?? throw new ArgumentNullException(nameof(payHead));
        Role = role;
        Amount = amount;
        AffectsNet = affectsNet;
    }
}

/// <summary>
/// The per-employee salary breakdown produced by <see cref="PayrollComputationService.Compute"/> (Phase 8 slice
/// 3). Carries every evaluated <see cref="Lines"/> plus the derived aggregates — gross earnings, total
/// deductions, net payable and employer contribution total — all paisa-exact and conserving
/// (<see cref="GrossEarnings"/> = <see cref="TotalDeductions"/> + <see cref="NetPayable"/>).
/// </summary>
public sealed class PayrollComputationResult
{
    /// <summary>The employee this breakdown is for.</summary>
    public Guid EmployeeId { get; }

    /// <summary>The salary structure version used (the one in force on the period end).</summary>
    public Guid SalaryStructureId { get; }

    /// <summary>The payroll period start.</summary>
    public DateOnly PeriodFrom { get; }

    /// <summary>The payroll period end (the structure-resolution date).</summary>
    public DateOnly PeriodTo { get; }

    /// <summary>The evaluated pay-head lines, in salary-structure order.</summary>
    public IReadOnlyList<PayrollComputedLine> Lines { get; }

    public PayrollComputationResult(
        Guid employeeId, Guid salaryStructureId, DateOnly periodFrom, DateOnly periodTo,
        IReadOnlyList<PayrollComputedLine> lines)
    {
        EmployeeId = employeeId;
        SalaryStructureId = salaryStructureId;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    /// <summary>Σ of the earnings lines that <b>affect net salary</b> (Dr expense) — the gross pay. A non-affecting
    /// earning (a tracked-but-not-paid component) is excluded (F1).</summary>
    public Money GrossEarnings => Sum(PayHeadPostingRole.Earning, affectingOnly: true);

    /// <summary>Σ of the employee-deduction lines that <b>affect net salary</b> (Cr payable). A non-affecting
    /// deduction is excluded (F1).</summary>
    public Money TotalDeductions => Sum(PayHeadPostingRole.Deduction, affectingOnly: true);

    /// <summary>Σ of the employer-contribution lines (a separate balanced pair; not in net pay). Governed by role,
    /// not the affect-net flag.</summary>
    public Money EmployerContributions => Sum(PayHeadPostingRole.EmployerContribution);

    /// <summary>Net payable = gross earnings − total deductions (the Cr Salary-Payable amount).</summary>
    public Money NetPayable => GrossEarnings - TotalDeductions;

    private Money Sum(PayHeadPostingRole role, bool affectingOnly = false)
    {
        var sum = 0m;
        foreach (var l in Lines)
            if (l.Role == role && (!affectingOnly || l.AffectsNet)) sum += l.Amount.Amount;
        return new Money(sum);
    }
}
