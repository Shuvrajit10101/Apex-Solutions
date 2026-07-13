using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Payroll voucher</b> service (Phase 8 slice 3; RQ-7; ER-1/ER-2) — orchestrates a pay run: it
/// <b>auto-creates the payroll accounting ledgers</b> (resolving the S1/S2 carry-forward), runs the pure
/// <see cref="PayrollComputationService"/> for each employee, and <b>posts one balanced integrated accounting
/// voucher</b> (base type <see cref="VoucherBaseType.Payroll"/>) through <see cref="LedgerService.Post"/>:
/// <list type="bullet">
///   <item>Dr each earnings pay head's <b>expense</b> ledger;</item>
///   <item>Cr each employee-deduction pay head's <b>payable</b> ledger;</item>
///   <item>Cr the employee's net into the company <b>Salary Payable</b> ledger;</item>
///   <item>for each employer statutory-contribution / charge head, Dr the employer <b>expense</b> and Cr the
///     employer <b>payable</b> (a separate balanced pair, ER-1).</item>
/// </list>
/// Every line carries a self-describing <see cref="PayrollLineDetail"/> so the payslip / register read the
/// breakdown straight off the posting. The voucher <b>balances to the paisa by construction</b> — net is derived
/// from the already-rounded line amounts — and <see cref="LedgerService.Post"/> rejects (and never persists) any
/// imbalance. Auto-ledger creation is <b>idempotent</b> and <b>non-destructive</b>: a ledger already present by
/// name (e.g. user pre-created) is reused as-is, never relocated.
/// </summary>
public sealed class PayrollVoucherService
{
    private readonly Company _company;

    public PayrollVoucherService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The auto-created company Salary-Payable ledger name (the net-pay liability).</summary>
    public const string SalaryPayableLedgerName = "Salary Payable";

    /// <summary>The auto-created establishment EPF-admin-charge <b>expense</b> ledger name (A/c 2, Dr; Phase 8 slice 4).</summary>
    public const string PfAdminExpenseLedgerName = "PF Admin Charges";

    /// <summary>The auto-created establishment EPF-admin-charge <b>payable</b> ledger name (A/c 2, Cr; Phase 8 slice 4).</summary>
    public const string PfAdminPayableLedgerName = "PF Admin Charges Payable";

    /// <summary>The auto-created <b>Gratuity Expense</b> ledger name (Indirect Expenses, Dr; Phase 8 slice 9) — the
    /// debit side of the period-end gratuity-provision pair.</summary>
    public const string GratuityExpenseLedgerName = "Gratuity Expense";

    /// <summary>The auto-created <b>Gratuity Provision</b> ledger name (Current Liabilities, Cr; Phase 8 slice 9) — the
    /// accumulating provision liability the delta posts against.</summary>
    public const string GratuityProvisionLedgerName = "Gratuity Provision";

    private const string IndirectExpensesGroupName = "Indirect Expenses";
    private const string CurrentLiabilitiesGroupName = "Current Liabilities";

    /// <summary>
    /// Runs and posts a Payroll voucher for <paramref name="employeeIds"/> over <c>[periodFrom, periodTo]</c>.
    /// The voucher date defaults to <paramref name="periodTo"/> (the pay date, and the salary-structure
    /// resolution date). <paramref name="userDefinedAmountsByEmployee"/> supplies per-employee values for any
    /// As-User-Defined-Value pay heads. Auto-creates the required ledgers first, then posts the balanced voucher.
    /// Throws <see cref="InvalidOperationException"/> if Payroll is not enabled, the Payroll voucher type is
    /// missing, the run produces no postable lines, or any employee's net pay is negative.
    /// </summary>
    public Voucher Post(
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyList<Guid> employeeIds,
        DateOnly? voucherDate = null,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null,
        string? narration = null)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        if (!_company.PayrollEnabled)
            throw new InvalidOperationException("Payroll is not enabled on this company (F11 Maintain Payroll).");
        if (employeeIds.Count == 0)
            throw new InvalidOperationException("A payroll run must include at least one employee.");

        var payrollType = _company.VoucherTypes.FirstOrDefault(t => t.BaseType == VoucherBaseType.Payroll)
            ?? throw new InvalidOperationException("No Payroll voucher type is defined.");

        var computation = new PayrollComputationService(_company);
        var date = voucherDate ?? periodTo;

        // 1) PURE PRE-PASS (no mutation): compute every employee and validate the run is postable — a Compute
        //    failure, a negative net, or a negative postable line is surfaced BEFORE any ledger is created or any
        //    pay-head link is set, so a rejected run leaves the company byte-for-byte unchanged (F2/F5).
        var results = new List<PayrollComputationResult>(employeeIds.Count);
        foreach (var employeeId in employeeIds)
        {
            IReadOnlyDictionary<Guid, Money>? userDefined = null;
            userDefinedAmountsByEmployee?.TryGetValue(employeeId, out userDefined);

            var result = computation.Compute(employeeId, periodFrom, periodTo, userDefined);
            ValidatePostable(result);
            results.Add(result);
        }

        // 2) MUTATING: auto-create ledgers, set the pay-head links and assemble + post the voucher — all inside a
        //    rollback scope, so ANY later failure (e.g. a voucher date before BooksBeginFrom rejected by the
        //    validator) removes every ledger we added and restores every pay-head link we touched (atomic; F2).
        var scope = new LedgerCreationScope(_company);
        try
        {
            var lines = new List<EntryLine>();
            foreach (var result in results)
                AppendEmployeeLines(lines, result, scope);

            // Establishment EPF administration charge (A/c 2) — computed ONCE over the run's contributory members
            // and posted as a single balanced pair, NOT per member (Phase 8 slice 4).
            AppendPfAdminLine(lines, results, scope);

            if (lines.Count < 2)
                throw new InvalidOperationException(
                    "The payroll run produced no postable lines (all computed amounts were zero).");

            var voucher = new Voucher(Guid.NewGuid(), payrollType.Id, date, lines, narration: narration);
            return new LedgerService(_company).Post(voucher);
        }
        catch
        {
            scope.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Posts the <b>period-end gratuity provision</b> voucher (Phase 8 slice 9; RQ-14) as-on <paramref name="asOn"/>
    /// for <paramref name="employeeIds"/>: it computes the total accrued gratuity liability (the pure
    /// <see cref="GratuityProvision"/> accrual over the active members), derives the <b>prior provision balance</b>
    /// from the posted books (the accumulated Cr − Dr on the <see cref="GratuityProvisionLedgerName"/> ledger before
    /// the voucher date), and posts <b>only the delta</b> as a balanced Journal:
    /// <list type="bullet">
    ///   <item>an <b>increase</b> (accrued &gt; prior) ⇒ <c>Dr Gratuity Expense / Cr Gratuity Provision</c> for the rise;</item>
    ///   <item>a <b>decrease</b> (accrued &lt; prior) ⇒ the reverse <c>Dr Gratuity Provision / Cr Gratuity Expense</c>
    ///     write-back.</item>
    /// </list>
    /// The Dr and Cr legs are equal by construction, so the voucher balances to the paisa. Ledger creation reuses the
    /// idempotent, non-destructive auto-ledger path inside a rollback scope, so a rejected posting leaves the company
    /// byte-for-byte unchanged (F2). Throws when gratuity is not enrolled, Payroll is off, no Journal voucher type
    /// exists, or the provision is <b>unchanged</b> (delta = 0 ⇒ nothing to post).
    /// </summary>
    public Voucher PostGratuityProvision(
        DateOnly asOn,
        IReadOnlyList<Guid> employeeIds,
        DateOnly? voucherDate = null,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, Money>>? userDefinedAmountsByEmployee = null,
        string? narration = null)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        if (!_company.PayrollEnabled)
            throw new InvalidOperationException("Payroll is not enabled on this company (F11 Maintain Payroll).");
        if (_company.GratuityConfig is null)
            throw new InvalidOperationException("Gratuity is not enrolled on this company (F11 Payroll Statutory).");

        var journalType = _company.VoucherTypes.FirstOrDefault(t => t.BaseType == VoucherBaseType.Journal)
            ?? throw new InvalidOperationException("No Journal voucher type is defined.");

        var date = voucherDate ?? asOn;
        var accrued = GratuityProvision.TotalLiability(_company, employeeIds, asOn, userDefinedAmountsByEmployee);
        // The prior must be INCLUSIVE of any provision already dated on the same period-end (PriorGratuityProvisionBalance
        // is strictly-before, so pass date + 1 day). A same-date true-up after the accrual changes then posts only the
        // increment over the existing same-date voucher — never re-posting the whole accrued (which would double-count
        // the liability) — matching the register VM's inclusive-prior guard so the ledger reconciles to the register.
        var prior = PriorGratuityProvisionBalance(date.AddDays(1));
        var delta = accrued - prior;
        if (delta.Amount == 0m)
            throw new InvalidOperationException(
                "The gratuity provision is unchanged from the prior balance; there is no delta to post.");

        var scope = new LedgerCreationScope(_company);
        try
        {
            var (expense, provision) = EnsureGratuityLedgers(scope);
            var magnitude = new Money(Math.Abs(delta.Amount));
            var lines = delta.Amount > 0m
                ? new List<EntryLine>
                {
                    new(expense, magnitude, DrCr.Debit),    // provision rises: charge the expense …
                    new(provision, magnitude, DrCr.Credit), // … and build the liability
                }
                : new List<EntryLine>
                {
                    new(provision, magnitude, DrCr.Debit),  // provision falls: release the liability …
                    new(expense, magnitude, DrCr.Credit),   // … and credit back the expense
                };

            var voucher = new Voucher(Guid.NewGuid(), journalType.Id, date, lines, narration: narration);
            return new LedgerService(_company).Post(voucher);
        }
        catch
        {
            scope.Rollback();
            throw;
        }
    }

    /// <summary>The accumulated <b>Gratuity Provision</b> liability from the posted books strictly before
    /// <paramref name="before"/> (Phase 8 slice 9): the Σ of Cr − Dr on the provision ledger over non-cancelled
    /// vouchers dated earlier. Derived (pure) from the ledger — no persisted running total; a cancelled provision
    /// voucher never accrued, so it is excluded (F2). Zero before the first provision (ledger not yet created).</summary>
    public Money PriorGratuityProvisionBalance(DateOnly before)
    {
        var ledger = _company.FindLedgerByName(GratuityProvisionLedgerName);
        if (ledger is null) return Money.Zero;

        var sum = 0m;
        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled) continue;      // a cancelled provision never accrued (F2)
            if (v.Date >= before) continue; // strictly before this provision date
            foreach (var line in v.Lines)
            {
                if (line.LedgerId != ledger.Id) continue;
                sum += line.Side == DrCr.Credit ? line.Amount.Amount : -line.Amount.Amount;
            }
        }
        return new Money(sum);
    }

    /// <summary>Ensures the Gratuity Expense (Dr, Indirect Expenses) + Gratuity Provision (Cr, Current Liabilities)
    /// ledger pair, idempotent + non-destructive (a ledger present by name is reused as-is), returning
    /// <c>(expenseId, provisionId)</c>. A newly-created ledger is recorded in <paramref name="scope"/> for rollback (F2).</summary>
    private (Guid Expense, Guid Provision) EnsureGratuityLedgers(LedgerCreationScope? scope)
    {
        var expense = EnsureLedger(GratuityExpenseLedgerName, () => GroupIdByName(IndirectExpensesGroupName), openingIsDebit: true, scope);
        var provision = EnsureLedger(GratuityProvisionLedgerName, () => GroupIdByName(CurrentLiabilitiesGroupName), openingIsDebit: false, scope);
        return (expense, provision);
    }

    /// <summary>Validates that a computed employee run is postable BEFORE any mutation (F2/F5): a postable line
    /// (an affecting earning / deduction, or either leg of an employer pair) must be ≥ 0 — a negative computed
    /// amount (e.g. a negative flat-value slab or an underflowing subtraction) is rejected with a clean domain
    /// error rather than silently dropped from the posting (which would unbalance the voucher) — and the net pay
    /// must not be negative.</summary>
    private static void ValidatePostable(PayrollComputationResult result)
    {
        foreach (var line in result.Lines)
        {
            var postableRole = line.Role is PayHeadPostingRole.Earning
                or PayHeadPostingRole.Deduction or PayHeadPostingRole.EmployerContribution;
            if (postableRole && line.Amount.Amount < 0m)
                throw new InvalidOperationException(
                    $"Pay head '{line.PayHead.Name}' computed a negative amount ({line.Amount}); an earning, " +
                    "deduction or employer-contribution line must be ≥ 0.");
        }

        if (result.NetPayable.Amount < 0m)
            throw new InvalidOperationException(
                $"Employee {result.EmployeeId} has a negative net pay ({result.NetPayable}); deductions exceed earnings.");
    }

    /// <summary>Appends one employee's balanced set of entry lines to <paramref name="lines"/>: affecting earnings
    /// Dr, affecting deductions Cr, net Cr, and each employer contribution as a Dr/Cr pair. An earning / deduction
    /// whose "Affect Net Salary?" flag is off is computed but <b>not posted</b> (F1); zero-amount lines are skipped;
    /// the net is derived from the affecting rounded amounts so the set balances to the paisa. Every ledger created
    /// and pay-head link set here is recorded in <paramref name="scope"/> for rollback (F2).</summary>
    private void AppendEmployeeLines(List<EntryLine> lines, PayrollComputationResult result, LedgerCreationScope scope)
    {
        var salaryPayableLedgerId = EnsureSalaryPayableLedger(scope);

        foreach (var computed in result.Lines)
        {
            var amount = computed.Amount;
            if (amount.Amount == 0m) continue; // nothing to post for a zero head (negatives were rejected pre-flight)

            var payHead = computed.PayHead;
            switch (computed.Role)
            {
                case PayHeadPostingRole.Earning:
                {
                    if (!computed.AffectsNet) continue; // tracked-but-not-paid earning: computed, not posted (F1)
                    var expense = EnsurePrimaryLedger(payHead, PayHeadPostingRole.Earning, scope);
                    lines.Add(new EntryLine(expense, amount, DrCr.Debit,
                        payroll: new PayrollLineDetail(result.EmployeeId, payHead.Id, PayrollLineCategory.Earning, amount)));
                    break;
                }
                case PayHeadPostingRole.Deduction:
                {
                    if (!computed.AffectsNet) continue; // non-affecting deduction: computed, not posted (F1)
                    var payable = EnsurePrimaryLedger(payHead, PayHeadPostingRole.Deduction, scope);
                    lines.Add(new EntryLine(payable, amount, DrCr.Credit,
                        payroll: new PayrollLineDetail(result.EmployeeId, payHead.Id, PayrollLineCategory.Deduction, amount)));
                    break;
                }
                case PayHeadPostingRole.EmployerContribution:
                {
                    var (expense, payable) = EnsureEmployerLedgers(payHead, scope);
                    lines.Add(new EntryLine(expense, amount, DrCr.Debit,
                        payroll: new PayrollLineDetail(result.EmployeeId, payHead.Id, PayrollLineCategory.EmployerContributionExpense, amount)));
                    lines.Add(new EntryLine(payable, amount, DrCr.Credit,
                        payroll: new PayrollLineDetail(result.EmployeeId, payHead.Id, PayrollLineCategory.EmployerContributionPayable, amount)));
                    break;
                }
                default:
                    continue; // Not-Applicable heads post nothing
            }
        }

        // Net pay (gross − deductions) is guaranteed ≥ 0 by ValidatePostable, run before any mutation.
        var net = result.NetPayable;
        if (net.Amount > 0m)
            lines.Add(new EntryLine(salaryPayableLedgerId, net, DrCr.Credit,
                payroll: new PayrollLineDetail(result.EmployeeId, null, PayrollLineCategory.NetPayable, net)));
    }

    /// <summary>
    /// Appends the establishment <b>EPF administration charge</b> (A/c 2; Phase 8 slice 4) as a single balanced
    /// employer pair (Dr <see cref="PfAdminExpenseLedgerName"/> / Cr <see cref="PfAdminPayableLedgerName"/>). The
    /// charge is <c>max(round(0.5% × Σ EPF wages), 500)</c> over the run's contributory members (₹75 when none),
    /// applied <b>once per challan</b> at the establishment aggregate — a per-member floor would over-charge. It is
    /// posted only for a PF-registered establishment (<see cref="Company.PfConfig"/> present), so a non-PF run is
    /// byte-identical (ER-13). The pair self-balances, keeping the voucher balanced to the paisa. No per-employee
    /// <see cref="PayrollLineDetail"/> — the charge is establishment-level, not attributable to one member.
    /// </summary>
    private void AppendPfAdminLine(List<EntryLine> lines, List<PayrollComputationResult> results, LedgerCreationScope scope)
    {
        if (_company.PfConfig is not { } pfConfig) return; // establishment not enrolled for PF

        var contributoryEpfWages = new List<Money>();
        foreach (var result in results)
        {
            var employee = _company.FindEmployee(result.EmployeeId);
            if (employee is null || !employee.PfApplicable) continue;
            var pfWages = result.PfWages.Amount;
            if (pfWages <= 0m) continue; // no PF wages ⇒ not contributory this period
            // The A/c 2 basis uses the same EPF-wage cap the per-member EPF used: uncapped when the member opts in
            // OR the establishment's default is not-cap (PfConfig.CapWagesAtCeiling), else capped at ₹15,000.
            var epfWages = PfContribution.ContributesOnHigherWages(
                    employee.PfContributeOnHigherWages, pfConfig.CapWagesAtCeiling)
                ? pfWages
                : Math.Min(pfWages, PfContribution.WageCeiling);
            contributoryEpfWages.Add(new Money(epfWages));
        }

        var admin = PfContribution.ComputeAdminCharge(contributoryEpfWages);
        if (admin.Amount <= 0m) return;

        var (expense, payable) = EnsurePfAdminLedgers(scope);
        lines.Add(new EntryLine(expense, admin, DrCr.Debit));
        lines.Add(new EntryLine(payable, admin, DrCr.Credit));
    }

    /// <summary>Ensures the establishment EPF-admin expense (Dr, Indirect Expenses) + payable (Cr, Current
    /// Liabilities) ledger pair, idempotent + non-destructive (a ledger present by name is reused as-is), and
    /// returns <c>(expenseId, payableId)</c>. A newly-created ledger is recorded in <paramref name="scope"/> for
    /// rollback (F2).</summary>
    private (Guid Expense, Guid Payable) EnsurePfAdminLedgers(LedgerCreationScope? scope)
    {
        var expense = EnsureLedger(PfAdminExpenseLedgerName, () => GroupIdByName(IndirectExpensesGroupName), openingIsDebit: true, scope);
        var payable = EnsureLedger(PfAdminPayableLedgerName, () => GroupIdByName(CurrentLiabilitiesGroupName), openingIsDebit: false, scope);
        return (expense, payable);
    }

    // ------------------------------------------------------------------ auto-ledger creation (idempotent, non-destructive)

    /// <summary>Ensures the company Salary-Payable ledger exists (under Current Liabilities, credit side) and
    /// returns its id. Idempotent; a ledger already present by name is reused as-is.</summary>
    public Guid EnsureSalaryPayableLedger() => EnsureSalaryPayableLedger(null);

    private Guid EnsureSalaryPayableLedger(LedgerCreationScope? scope) =>
        EnsureLedger(SalaryPayableLedgerName, () => GroupIdByName(CurrentLiabilitiesGroupName), openingIsDebit: false, scope);

    /// <summary>
    /// Ensures (idempotently) the accounting ledgers a pay head needs and populates its
    /// <see cref="PayHead.LedgerId"/> / <see cref="PayHead.EmployerExpenseLedgerId"/>: an earning head gets an
    /// expense ledger; an employee-deduction head a payable ledger; an employer head an expense + a payable pair.
    /// Each ledger is classified under the pay head's <see cref="PayHead.UnderGroupId"/> (or a role default) and,
    /// if a ledger by the target name already exists, is <b>reused as-is</b> (never relocated). A Not-Applicable
    /// head needs no ledger.
    /// </summary>
    public void EnsureLedgersFor(PayHead payHead)
    {
        ArgumentNullException.ThrowIfNull(payHead);
        var role = PayrollComputationService.RoleOf(payHead.Type);
        switch (role)
        {
            case PayHeadPostingRole.Earning:
            case PayHeadPostingRole.Deduction:
                EnsurePrimaryLedger(payHead, role, null);
                break;
            case PayHeadPostingRole.EmployerContribution:
                EnsureEmployerLedgers(payHead, null);
                break;
        }
    }

    /// <summary>Ensures the single primary ledger of an earning (expense, Dr) / deduction (payable, Cr) head,
    /// caching it in <see cref="PayHead.LedgerId"/>, and returns its id. When <paramref name="scope"/> is present
    /// the created ledger + the pay-head-link mutation are recorded for rollback (F2).</summary>
    private Guid EnsurePrimaryLedger(PayHead payHead, PayHeadPostingRole role, LedgerCreationScope? scope)
    {
        if (payHead.LedgerId is { } existing && _company.FindLedger(existing) is not null)
            return existing;

        var isEarning = role == PayHeadPostingRole.Earning;
        var name = DisplayNameOf(payHead);
        var fallbackGroup = isEarning ? IndirectExpensesGroupName : CurrentLiabilitiesGroupName;
        var id = EnsureLedger(name, () => ResolveGroupId(payHead.UnderGroupId, fallbackGroup), openingIsDebit: isEarning, scope);
        scope?.SnapshotPayHead(payHead);
        payHead.LedgerId = id;
        return id;
    }

    /// <summary>Ensures the employer expense (Dr) + payable (Cr) ledger pair for an employer-contribution head,
    /// caching them in <see cref="PayHead.EmployerExpenseLedgerId"/> / <see cref="PayHead.LedgerId"/>, and
    /// returns <c>(expenseId, payableId)</c>. The expense takes the pay head's own name under Indirect Expenses;
    /// the payable takes "<i>name</i> Payable" under the pay head's classification (or Current Liabilities).</summary>
    private (Guid Expense, Guid Payable) EnsureEmployerLedgers(PayHead payHead, LedgerCreationScope? scope)
    {
        var displayName = DisplayNameOf(payHead);

        Guid expense;
        if (payHead.EmployerExpenseLedgerId is { } e && _company.FindLedger(e) is not null)
            expense = e;
        else
        {
            expense = EnsureLedger(displayName, () => GroupIdByName(IndirectExpensesGroupName), openingIsDebit: true, scope);
            scope?.SnapshotPayHead(payHead);
            payHead.EmployerExpenseLedgerId = expense;
        }

        Guid payable;
        if (payHead.LedgerId is { } p && _company.FindLedger(p) is not null)
            payable = p;
        else
        {
            payable = EnsureLedger($"{displayName} Payable",
                () => ResolveGroupId(payHead.UnderGroupId, CurrentLiabilitiesGroupName), openingIsDebit: false, scope);
            scope?.SnapshotPayHead(payHead);
            payHead.LedgerId = payable;
        }

        return (expense, payable);
    }

    /// <summary>Reuses a ledger by name (non-destructive — never relocates it), or creates a fresh one under the
    /// resolved group with the given opening side. A newly-created ledger is recorded in <paramref name="scope"/>
    /// (when present) for rollback (F2). Returns the ledger id.</summary>
    private Guid EnsureLedger(string name, Func<Guid> groupIdFactory, bool openingIsDebit, LedgerCreationScope? scope)
    {
        var existing = _company.FindLedgerByName(name);
        if (existing is not null) return existing.Id;

        var ledger = new Domain.Ledger(Guid.NewGuid(), name, groupIdFactory(), Money.Zero, openingIsDebit);
        _company.AddLedger(ledger);
        scope?.TrackLedger(ledger);
        return ledger.Id;
    }

    private static string DisplayNameOf(PayHead payHead) =>
        string.IsNullOrWhiteSpace(payHead.DisplayName) ? payHead.Name : payHead.DisplayName!;

    private Guid ResolveGroupId(Guid? underGroupId, string fallbackGroupName)
    {
        if (underGroupId is { } g && _company.FindGroup(g) is not null) return g;
        return GroupIdByName(fallbackGroupName);
    }

    private Guid GroupIdByName(string name) =>
        _company.FindGroupByName(name)?.Id
        ?? throw new InvalidOperationException($"Seed missing '{name}' group; cannot auto-create payroll ledgers.");

    /// <summary>
    /// A rollback scope for one <see cref="Post"/> run (F2): it records every ledger the run auto-creates and a
    /// one-time snapshot of every pay head whose <see cref="PayHead.LedgerId"/> / <see cref="PayHead.EmployerExpenseLedgerId"/>
    /// link the run sets. If assembly or posting later fails, <see cref="Rollback"/> restores those links and
    /// removes the added ledgers, so a rejected pay run leaves the company byte-for-byte unchanged (never a
    /// user-pre-created ledger, which the run reuses by name but never adds, and so never removes).
    /// </summary>
    private sealed class LedgerCreationScope
    {
        private readonly Company _company;
        private readonly List<Domain.Ledger> _addedLedgers = new();
        private readonly List<(PayHead Head, Guid? Ledger, Guid? EmployerExpense)> _payHeadSnapshots = new();
        private readonly HashSet<Guid> _snapshotted = new();

        public LedgerCreationScope(Company company) => _company = company;

        /// <summary>Records a ledger this run created, so a rollback can remove it.</summary>
        public void TrackLedger(Domain.Ledger ledger) => _addedLedgers.Add(ledger);

        /// <summary>Captures a pay head's link values ONCE (before the first mutation), so a rollback can restore them.</summary>
        public void SnapshotPayHead(PayHead head)
        {
            if (_snapshotted.Add(head.Id))
                _payHeadSnapshots.Add((head, head.LedgerId, head.EmployerExpenseLedgerId));
        }

        /// <summary>Undoes the run's mutations: restores every snapshotted pay-head link, then removes every added ledger.</summary>
        public void Rollback()
        {
            foreach (var (head, ledger, employerExpense) in _payHeadSnapshots)
            {
                head.LedgerId = ledger;
                head.EmployerExpenseLedgerId = employerExpense;
            }
            for (var i = _addedLedgers.Count - 1; i >= 0; i--)
                _company.RemoveLedger(_addedLedgers[i]);
        }
    }
}
