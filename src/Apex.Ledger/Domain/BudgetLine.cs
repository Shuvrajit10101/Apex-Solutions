namespace Apex.Ledger.Domain;

/// <summary>
/// One row of a <see cref="Budget"/> (catalog §7): a budgeted <see cref="Amount"/> for a single target —
/// a <b>Group</b> (rolls up its ledgers), a <b>Ledger</b>, or (v67; census 2.9) a <b>Cost Centre</b> —
/// measured either <see cref="BudgetType.OnClosingBalance"/> or <see cref="BudgetType.OnNettTransactions"/>.
/// Exactly one of <see cref="GroupId"/> / <see cref="LedgerId"/> / <see cref="CostCentreId"/> is set; the
/// other two are <c>null</c>.
/// </summary>
/// <remarks>
/// A line carries no side — <see cref="Amount"/> is the budgeted magnitude. The variance projection
/// compares it against the actual magnitude the target reaches over the budget's period.
/// </remarks>
public sealed class BudgetLine
{
    /// <summary>The budgeted group, when this line targets a group; else <c>null</c>.</summary>
    public Guid? GroupId { get; }

    /// <summary>The budgeted ledger, when this line targets a ledger; else <c>null</c>.</summary>
    public Guid? LedgerId { get; }

    /// <summary>
    /// v67 (census row <b>2.9</b>): the budgeted <b>cost centre</b>, when this line targets one; else
    /// <c>null</c>. The THIRD target the vendor documents and this build had no storage for — which is exactly
    /// the gap row 2.9's cell names (<i>"no cost-centre target (the target option carries no cost-centre
    /// id)"</i>).
    ///
    /// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/budgets-tally/</c> ("Create, Alter, and Delete
    /// Budgets in TallyPrime"), opened by content 2026-10-16: the budget screen offers <i>"Set Groups to
    /// Yes"</i>, <i>"Set Ledgers to Yes"</i> and <i>"Set Cost Centres to Yes"</i>, and <i>"In the Cost Centre
    /// Budget screen, select the required Cost Centre, Expenses, Income and Closing Balance."</i></para>
    ///
    /// <para>⚠️ <b>THE VENDOR'S COST-CENTRE BUDGET SCREEN CARRIES THREE FIGURES (Expenses, Income, Closing
    /// Balance) AND THIS LINE CARRIES ONE.</b> A cost-centre budget is therefore expressed here as up to three
    /// LINES against the same centre, each with its own <see cref="Type"/> and <see cref="Amount"/>, rather
    /// than one line with three columns. That is a shape divergence, it is ours, and it is written down rather
    /// than left for a reader to infer from the absence of two properties.</para>
    /// </summary>
    public Guid? CostCentreId { get; }

    /// <summary>Whether the figure is measured on closing balance or on nett transactions.</summary>
    public BudgetType Type { get; }

    /// <summary>The budgeted magnitude (≥ 0) for this target over the budget's period.</summary>
    public Money Amount { get; }

    /// <summary>True iff this line targets a group (rolls up its ledgers).</summary>
    public bool IsGroupTarget => GroupId is not null;

    /// <summary>True iff this line targets a single ledger.</summary>
    public bool IsLedgerTarget => LedgerId is not null;

    /// <summary>True iff this line targets a single cost centre (v67; census 2.9).</summary>
    public bool IsCostCentreTarget => CostCentreId is not null;

    private BudgetLine(Guid? groupId, Guid? ledgerId, Guid? costCentreId, BudgetType type, Money amount)
    {
        // v67 (census 2.9): THREE mutually exclusive targets now, not two. The old guard was
        // `(groupId is null) == (ledgerId is null)`, which is the two-target form of exactly this count.
        var targets = (groupId is null ? 0 : 1) + (ledgerId is null ? 0 : 1) + (costCentreId is null ? 0 : 1);
        if (targets != 1)
            throw new ArgumentException(
                "A budget line must target exactly one of a group, a ledger or a cost centre.");
        if (amount.Amount < 0m)
            throw new ArgumentException("Budget amount magnitude must be ≥ 0.", nameof(amount));
        // Mirrors BillAllocation / CostAllocation / AdditionalCostLine: the line persists through Paisa.FromMoney
        // (SqliteCompanyStore.InsertBudgets), which THROWS on a sub-paisa figure — and the Budget master had no
        // catch block anywhere in the file, so that throw escaped as an UNHANDLED exception out of a Ctrl+A
        // keystroke, leaving the budget on the aggregate so every LATER save threw too. Refuse it at the one
        // choke point both factories flow through. Import (ImportPlan, via MoneyCodec.FromPaisa) and the SQLite
        // read path (via Paisa.ToMoney) build from INTEGER paisa, so neither can ever trip this.
        // FitsPaisaStore, not IsPaisaExact — "storable" is magnitude AND exactness, and the exactness half alone
        // THROWS on a big enough figure instead of refusing it. See the note in BillAllocation.
        if (!PaisaConversion.FitsPaisaStore(amount.Amount))
            throw new InvalidOperationException(
                $"A budget amount {amount.Amount} cannot be stored as integer paisa: it must be paisa-exact "
              + $"(2 decimal places) and no larger than {PaisaConversion.MaxStorableRupees}.");

        GroupId = groupId;
        LedgerId = ledgerId;
        CostCentreId = costCentreId;
        Type = type;
        Amount = amount;
    }

    /// <summary>A budget line that targets a group (rolls up the group's ledgers).</summary>
    public static BudgetLine ForGroup(Guid groupId, BudgetType type, Money amount) =>
        new(groupId, null, null, type, amount);

    /// <summary>A budget line that targets a single ledger.</summary>
    public static BudgetLine ForLedger(Guid ledgerId, BudgetType type, Money amount) =>
        new(null, ledgerId, null, type, amount);

    /// <summary>
    /// v67 (census 2.9): a budget line that targets a single <b>cost centre</b> — the vendor's
    /// <i>"Set Cost Centres to Yes"</i> budget. See <see cref="CostCentreId"/> for the citation and for the
    /// one shape divergence this carries.
    /// </summary>
    public static BudgetLine ForCostCentre(Guid costCentreId, BudgetType type, Money amount) =>
        new(null, null, costCentreId, type, amount);
}
