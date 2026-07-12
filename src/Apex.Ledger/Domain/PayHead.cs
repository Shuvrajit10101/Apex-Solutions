namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Pay Head</b> (Phase 8 slice 2; catalog §14; Study Guide pp.198–210) — the heart of the salary structure.
/// In Tally a pay head is a ledger with payroll attributes: a <see cref="Type"/> (the accounting/statutory
/// nature), a <see cref="CalculationType"/> (one of the five methods), an accounting-group linkage
/// (<see cref="UnderGroupId"/>), a rounding rule, an income-tax component tag, a use-for-gratuity flag and — for
/// <see cref="PayHeadCalculationType.AsComputedValue"/> — a <see cref="Computation"/> formula (a basis of other
/// pay heads + percentage and/or slab bands).
/// <para>
/// <b>This slice is a pure model — it performs NO salary computation</b> (that is the slice-3 payroll-voucher
/// engine). But the model is complete enough for that engine to evaluate. <b>Ledger auto-create is deferred to
/// slice 3</b>: this slice only captures the accounting classification (<see cref="UnderGroupId"/> — the group
/// the pay head will post <c>Under</c>) and, optionally, a forward <see cref="LedgerId"/> that slice 3 populates
/// once the ledger exists; no ledger is created here.
/// </para>
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is unique within a company (case-insensitive),
/// so an Alter renames in place. Computation-basis integrity (no dangling / self / cyclic computed-on reference)
/// is enforced by <c>PayHeadService</c> against the company's other pay heads. Not seeded on company creation
/// (ER-13). Framework- and DB-agnostic.
/// </remarks>
public sealed class PayHead
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company (case-insensitive); a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Optional display name (the name to appear on the payslip); <c>null</c> ⇒ use <see cref="Name"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The accounting / statutory nature of the head.</summary>
    public PayHeadType Type { get; set; }

    /// <summary>How the head's amount is derived (one of the five Tally methods).</summary>
    public PayHeadCalculationType CalculationType { get; set; }

    /// <summary>Whether this head affects net salary (earnings/deductions do; employer contributions/charges and
    /// the gratuity provision do not). Defaults follow <see cref="DefaultAffectsNetSalary"/> when created, but the
    /// stored value is explicit.</summary>
    public bool AffectsNetSalary { get; set; }

    /// <summary>The accounting <see cref="Group"/> this head posts <c>Under</c> (Indirect Expenses / Current
    /// Liabilities / Duties &amp; Taxes) — the accounting classification. <c>null</c> ⇒ not yet classified. The
    /// reference is captured now; the actual ledger is auto-created in slice 3.</summary>
    public Guid? UnderGroupId { get; set; }

    /// <summary>The <see cref="Ledger"/> this head is linked to, auto-created + populated by slice 3's payroll
    /// posting. For an earning head this is the head's own <b>expense</b> ledger (posted Dr); for an employee
    /// deduction head this is its <b>payable</b> ledger (posted Cr); for an <b>employer</b> contribution / charge /
    /// gratuity head this is the employer <b>payable</b> ledger (posted Cr), paired with
    /// <see cref="EmployerExpenseLedgerId"/>. <c>null</c> until first posted.</summary>
    public Guid? LedgerId { get; set; }

    /// <summary>The employer <b>expense</b> ledger for an Employer's-Statutory-Contribution / Other-Charges /
    /// Gratuity head (Phase 8 slice 3) — the debit side of the balanced employer pair, while
    /// <see cref="LedgerId"/> holds the employer-payable (credit) side. Auto-created + populated by the payroll
    /// posting; <c>null</c> for every non-employer head (and until first posted). Additive, defaults null so an
    /// existing pay head is byte-identical (ER-13).</summary>
    public Guid? EmployerExpenseLedgerId { get; set; }

    /// <summary>The <b>Provident-Fund statutory role</b> of this head (Phase 8 slice 4); default
    /// <see cref="PfStatutoryComponent.None"/>. A non-<c>None</c> head is computed by the dedicated EPF/EPS/EDLI
    /// engine (<c>PfContribution</c>) rather than its ordinary <see cref="CalculationType"/> slabs, because the
    /// statutory split (EPS cap, employer-EPF residual, establishment admin floor) cannot be expressed as ordinary
    /// slabs. Additive, defaults <see cref="PfStatutoryComponent.None"/> so an existing pay head is byte-identical
    /// (ER-13).</summary>
    public PfStatutoryComponent PfComponent { get; set; } = PfStatutoryComponent.None;

    /// <summary>Whether this earning head counts toward <b>PF (EPF/EPS/EDLI) wages</b> — the Basic + DA (+ retaining
    /// allowance) basis PF is computed on, with HRA and other allowances excluded (Phase 8 slice 4). Default
    /// <c>false</c>; the PF-wage earnings (typically Basic and DA) are flagged <c>true</c> when the salary
    /// structure is set up. Additive, defaults <c>false</c> so an existing pay head is byte-identical (ER-13).</summary>
    public bool PartOfPfWages { get; set; }

    /// <summary>The income-tax component tag (§192 treatment); default <see cref="IncomeTaxComponent.NotApplicable"/>.</summary>
    public IncomeTaxComponent IncomeTaxComponent { get; set; } = IncomeTaxComponent.NotApplicable;

    /// <summary>"Use for calculation of gratuity?" — whether this head enters the gratuity base (Basic + DA).</summary>
    public bool UseForGratuity { get; set; }

    /// <summary>The rounding method applied to the computed amount; default <see cref="PayHeadRoundingMethod.NotApplicable"/>.</summary>
    public PayHeadRoundingMethod RoundingMethod { get; set; } = PayHeadRoundingMethod.NotApplicable;

    /// <summary>The rounding limit / multiple (e.g. ₹1); <see cref="Money.Zero"/> when no rounding.</summary>
    public Money RoundingLimit { get; set; } = Money.Zero;

    /// <summary>The period the amount is stated over; default <see cref="PayHeadCalculationPeriod.Month"/>.</summary>
    public PayHeadCalculationPeriod CalculationPeriod { get; set; } = PayHeadCalculationPeriod.Month;

    /// <summary>For <see cref="PayHeadCalculationType.OnAttendance"/> / <see cref="PayHeadCalculationType.OnProduction"/>:
    /// the <see cref="AttendanceType"/> the head is calculated on; <c>null</c> otherwise.</summary>
    public Guid? AttendanceTypeId { get; set; }

    /// <summary>For an On-Attendance head, the per-day calculation basis — the denominator days per month (e.g. 26);
    /// <c>null</c> ⇒ "as per calendar period".</summary>
    public int? PerDayCalculationBasisDays { get; set; }

    /// <summary>For an <see cref="PayHeadCalculationType.AsComputedValue"/> head, the computation formula (basis
    /// components + slab bands); <c>null</c> for the other calculation types.</summary>
    public PayHeadComputation? Computation { get; set; }

    public PayHead(Guid id, string name, PayHeadType type, PayHeadCalculationType calculationType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pay head name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        Type = type;
        CalculationType = calculationType;
        AffectsNetSalary = DefaultAffectsNetSalary(type);
    }

    /// <summary>The default "affect net salary" side for a pay-head <paramref name="type"/> — earnings, general
    /// and statutory deductions, advances, reimbursements and bonus affect net pay; employer
    /// contributions/charges and the gratuity provision are employer cost, booked separately (ER-1).</summary>
    public static bool DefaultAffectsNetSalary(PayHeadType type) => type switch
    {
        PayHeadType.Earnings => true,
        PayHeadType.Deductions => true,
        PayHeadType.EmployeesStatutoryDeductions => true,
        PayHeadType.LoansAndAdvances => true,
        PayHeadType.Reimbursements => true,
        PayHeadType.Bonus => true,
        PayHeadType.EmployersStatutoryContributions => false,
        PayHeadType.EmployersOtherCharges => false,
        PayHeadType.Gratuity => false,
        PayHeadType.NotApplicable => false,
        _ => false,
    };
}

/// <summary>
/// The computation formula of an <see cref="PayHeadCalculationType.AsComputedValue"/> <see cref="PayHead"/>
/// (Phase 8 slice 2): a <b>basis</b> — the set of other pay heads the value is computed on (each added or
/// subtracted, e.g. "Basic + DA") — plus the <b>slabs</b> that turn that basis into an amount (a single
/// percentage slab for "12% of basis", or banded slabs for a slabbed statutory/PT figure). Pure data; the
/// slice-3 engine evaluates it. Immutable once constructed.
/// </summary>
public sealed class PayHeadComputation
{
    private readonly List<PayHeadComputationComponent> _basisComponents;
    private readonly List<PayHeadComputationSlab> _slabs;

    /// <summary>The pay heads the value is computed on (order-preserved), each added or subtracted.</summary>
    public IReadOnlyList<PayHeadComputationComponent> BasisComponents => _basisComponents;

    /// <summary>The slab bands mapping the computed basis to an amount (order-preserved).</summary>
    public IReadOnlyList<PayHeadComputationSlab> Slabs => _slabs;

    public PayHeadComputation(
        IEnumerable<PayHeadComputationComponent> basisComponents,
        IEnumerable<PayHeadComputationSlab> slabs)
    {
        _basisComponents = new List<PayHeadComputationComponent>(basisComponents ?? throw new ArgumentNullException(nameof(basisComponents)));
        _slabs = new List<PayHeadComputationSlab>(slabs ?? throw new ArgumentNullException(nameof(slabs)));
        if (_basisComponents.Count == 0 && _slabs.Count == 0)
            throw new ArgumentException("A computation must carry at least one basis component or slab.");
    }
}

/// <summary>
/// One term of an <see cref="PayHeadComputation"/> basis (Phase 8 slice 2) — a reference to another
/// <see cref="PayHead"/> that is <b>added</b> (default) or <b>subtracted</b> from the computed base, e.g.
/// "HRA computed on (Basic + DA)". Referential integrity (exists / not self / not cyclic) is enforced by
/// <c>PayHeadService</c>.
/// </summary>
public sealed class PayHeadComputationComponent
{
    /// <summary>The pay head this term references.</summary>
    public Guid PayHeadId { get; }

    /// <summary><c>false</c> ⇒ added to the basis (default); <c>true</c> ⇒ subtracted.</summary>
    public bool IsSubtraction { get; }

    public PayHeadComputationComponent(Guid payHeadId, bool isSubtraction = false)
    {
        if (payHeadId == Guid.Empty)
            throw new ArgumentException("A computation component must reference a pay head.", nameof(payHeadId));
        PayHeadId = payHeadId;
        IsSubtraction = isSubtraction;
    }
}

/// <summary>
/// One slab band of an <see cref="PayHeadComputation"/> (Phase 8 slice 2; Tally "Amount Greater than / Amount
/// Up to" + Slab Type Percentage/Value). <see cref="FromAmount"/> is the band's lower bound ("greater than",
/// <c>null</c> ⇒ from zero); <see cref="ToAmount"/> is the upper bound ("up to", <c>null</c> ⇒ open-ended top).
/// A <see cref="PayHeadComputationSlabType.Percentage"/> slab carries a <see cref="RateBasisPoints"/> (1200 =
/// 12%); a <see cref="PayHeadComputationSlabType.FlatValue"/> slab carries a flat <see cref="Value"/>.
/// </summary>
public sealed class PayHeadComputationSlab
{
    /// <summary>Lower bound of the band on the computed basis ("greater than"); <c>null</c> ⇒ from zero.</summary>
    public Money? FromAmount { get; }

    /// <summary>Upper bound of the band ("up to"); <c>null</c> ⇒ open-ended top slab.</summary>
    public Money? ToAmount { get; }

    /// <summary>Percentage vs flat-value.</summary>
    public PayHeadComputationSlabType SlabType { get; }

    /// <summary>The rate in basis points for a <see cref="PayHeadComputationSlabType.Percentage"/> slab
    /// (100 bp = 1%); 0 for a value slab.</summary>
    public int RateBasisPoints { get; }

    /// <summary>The flat amount for a <see cref="PayHeadComputationSlabType.FlatValue"/> slab;
    /// <see cref="Money.Zero"/> for a percentage slab.</summary>
    public Money Value { get; }

    public PayHeadComputationSlab(
        PayHeadComputationSlabType slabType,
        int rateBasisPoints = 0,
        Money value = default,
        Money? fromAmount = null,
        Money? toAmount = null)
    {
        if (rateBasisPoints < 0)
            throw new ArgumentException("Slab rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (fromAmount is { } f && toAmount is { } t && t <= f)
            throw new ArgumentException("Slab 'up to' amount must be greater than the 'greater than' amount.", nameof(toAmount));

        SlabType = slabType;
        RateBasisPoints = rateBasisPoints;
        Value = value;
        FromAmount = fromAmount;
        ToAmount = toAmount;
    }

    /// <summary>The rate as a percentage (e.g. 12.00 for 1200 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;

    /// <summary>Convenience factory for a single "N% of the whole basis" slab (no bands).</summary>
    public static PayHeadComputationSlab Percentage(int rateBasisPoints) =>
        new(PayHeadComputationSlabType.Percentage, rateBasisPoints);

    /// <summary>Convenience factory for a flat-value slab.</summary>
    public static PayHeadComputationSlab FlatValue(Money value, Money? fromAmount = null, Money? toAmount = null) =>
        new(PayHeadComputationSlabType.FlatValue, value: value, fromAmount: fromAmount, toAmount: toAmount);
}
