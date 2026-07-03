namespace Apex.Ledger.Domain;

/// <summary>
/// One row of a <see cref="Budget"/> (catalog §7): a budgeted <see cref="Amount"/> for a single target —
/// either a <b>Group</b> (rolls up its ledgers) or a <b>Ledger</b> — measured either
/// <see cref="BudgetType.OnClosingBalance"/> or <see cref="BudgetType.OnNettTransactions"/>. Exactly one
/// of <see cref="GroupId"/> / <see cref="LedgerId"/> is set; the other is <c>null</c>.
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

    /// <summary>Whether the figure is measured on closing balance or on nett transactions.</summary>
    public BudgetType Type { get; }

    /// <summary>The budgeted magnitude (≥ 0) for this target over the budget's period.</summary>
    public Money Amount { get; }

    /// <summary>True iff this line targets a group (rolls up its ledgers).</summary>
    public bool IsGroupTarget => GroupId is not null;

    /// <summary>True iff this line targets a single ledger.</summary>
    public bool IsLedgerTarget => LedgerId is not null;

    private BudgetLine(Guid? groupId, Guid? ledgerId, BudgetType type, Money amount)
    {
        if ((groupId is null) == (ledgerId is null))
            throw new ArgumentException("A budget line must target exactly one of a group or a ledger.");
        if (amount.Amount < 0m)
            throw new ArgumentException("Budget amount magnitude must be ≥ 0.", nameof(amount));

        GroupId = groupId;
        LedgerId = ledgerId;
        Type = type;
        Amount = amount;
    }

    /// <summary>A budget line that targets a group (rolls up the group's ledgers).</summary>
    public static BudgetLine ForGroup(Guid groupId, BudgetType type, Money amount) =>
        new(groupId, null, type, amount);

    /// <summary>A budget line that targets a single ledger.</summary>
    public static BudgetLine ForLedger(Guid ledgerId, BudgetType type, Money amount) =>
        new(null, ledgerId, type, amount);
}
