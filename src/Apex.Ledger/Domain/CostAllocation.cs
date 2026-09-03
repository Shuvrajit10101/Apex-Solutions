namespace Apex.Ledger.Domain;

/// <summary>
/// One cost allocation hung off an <see cref="EntryLine"/> whose ledger has cost centres applicable
/// (catalog §6; plan.md §5). It ties a slice of the line amount to a (<see cref="CategoryId"/>,
/// <see cref="CentreId"/>) pair. A single line may carry several allocations whose amounts <b>sum to the
/// line amount</b> — the "split across centres" behaviour — so this is a value object with no identity
/// of its own (it mirrors <see cref="BillAllocation"/>, added in slice 2.1).
/// </summary>
/// <remarks>
/// The <see cref="Amount"/> is a magnitude &gt; 0 (it inherits the parent line's Dr/Cr side). Reports
/// total these allocations per category, per centre (with a hierarchical roll-up), and per centre per
/// ledger.
/// </remarks>
public sealed class CostAllocation
{
    /// <summary>The cost category this allocation is classified under.</summary>
    public Guid CategoryId { get; }

    /// <summary>The cost centre (within <see cref="CategoryId"/>) the amount is allocated to.</summary>
    public Guid CentreId { get; }

    /// <summary>Allocated magnitude, always &gt; 0. Inherits the parent line's Dr/Cr side.</summary>
    public Money Amount { get; }

    public CostAllocation(Guid categoryId, Guid centreId, Money amount)
    {
        if (categoryId == Guid.Empty)
            throw new ArgumentException("A cost allocation must reference a category.", nameof(categoryId));
        if (centreId == Guid.Empty)
            throw new ArgumentException("A cost allocation must reference a cost centre.", nameof(centreId));
        if (amount.Amount <= 0m)
            throw new ArgumentException("A cost allocation amount must be > 0.", nameof(amount));
        // Mirrors BillAllocation: this persists through Paisa.FromMoney (InsertCostAllocations), which throws on a
        // sub-paisa figure, and the screen's own gate is a per-category exact-SUM check that two sub-paisa halves
        // satisfy exactly. Import and the SQLite read path build from INTEGER paisa and cannot trip it.
        // FitsPaisaStore, not IsPaisaExact — "storable" is magnitude AND exactness, and the exactness half alone
        // THROWS on a big enough figure instead of refusing it. See the note in BillAllocation.
        if (!PaisaConversion.FitsPaisaStore(amount.Amount))
            throw new InvalidOperationException(
                $"A cost allocation amount {amount.Amount} cannot be stored as integer paisa: it must be "
              + $"paisa-exact (2 decimal places) and no larger than {PaisaConversion.MaxStorableRupees}.");

        CategoryId = categoryId;
        CentreId = centreId;
        Amount = amount;
    }
}
