namespace Apex.Ledger.Domain;

/// <summary>
/// A budget master (catalog §7; plan.md §5): a named set of budgeted figures for a period.
/// <c>Create → Budget</c> captures Name, <see cref="UnderId"/> (optional Primary group — budgets may
/// nest under a parent/Primary; modelled here as an optional Primary group reference),
/// <see cref="PeriodFrom"/>/<see cref="PeriodTo"/>, and a set of <see cref="BudgetLine"/>s set
/// <b>On Groups</b> and <b>On Ledgers</b>. Actuals are compared against it by the variance projection.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// A budget carries no accounting balance — it is a target the reports measure actuals against.
/// </remarks>
public sealed class Budget
{
    private readonly List<BudgetLine> _lines = new();

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Optional parent (a Primary group), mirroring the catalog's "Under (Primary)". <c>null</c> ⇒ top-level.</summary>
    public Guid? UnderId { get; set; }

    /// <summary>The first date the budget covers (inclusive).</summary>
    public DateOnly PeriodFrom { get; set; }

    /// <summary>The last date the budget covers (inclusive); ≥ <see cref="PeriodFrom"/>.</summary>
    public DateOnly PeriodTo { get; set; }

    /// <summary>The budgeted lines — each targets a group or a ledger (catalog §7).</summary>
    public IReadOnlyList<BudgetLine> Lines => _lines;

    public Budget(
        Guid id,
        string name,
        DateOnly periodFrom,
        DateOnly periodTo,
        Guid? underId = null,
        IEnumerable<BudgetLine>? lines = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Budget name is required.", nameof(name));
        if (periodTo < periodFrom)
            throw new ArgumentException("PeriodTo must be ≥ PeriodFrom.", nameof(periodTo));

        Id = id;
        Name = name;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        UnderId = underId;
        if (lines is not null)
            _lines.AddRange(lines);
    }

    /// <summary>Adds a budget line (targeting a group or a ledger).</summary>
    public void AddLine(BudgetLine line) => _lines.Add(line);
}
