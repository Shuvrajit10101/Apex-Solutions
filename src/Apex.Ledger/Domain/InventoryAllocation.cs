namespace Apex.Ledger.Domain;

/// <summary>
/// One stock movement line attached to an <see cref="InventoryVoucher"/> (catalog §10;
/// phase3-inventory-requirements RQ-10..RQ-14). It names the <see cref="StockItemId"/> moved, the
/// <see cref="GodownId"/> it moves in/out of, the <see cref="Quantity"/> (in <see cref="UnitId"/> — the
/// item's base unit when <c>null</c>), the <see cref="Direction"/> (inward/outward), an optional per-unit
/// <see cref="Rate"/> (paisa-exact when present) and an optional <see cref="BatchLabel"/> (DP-10).
/// </summary>
/// <remarks>
/// <para>Quantity is validated to 6-dp precision up front (<see cref="Quantities.IsWithinPrecision"/>), so a
/// finer value is rejected before persistence rather than at Save time; it must be strictly &gt; 0 (a
/// zero-quantity movement is meaningless).</para>
/// <para><see cref="UnitId"/> lets a line be expressed in a compound unit (e.g. transfer "1 Dozen"); the
/// stock-movement engine normalises it to the item's base unit via the unit's exact integer factor (DP-6),
/// so on-hand is always accumulated in one canonical unit. <c>null</c> means "already in the item's base
/// unit".</para>
/// </remarks>
public sealed class InventoryAllocation
{
    /// <summary>The <see cref="StockItem"/> moved; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The <see cref="Godown"/> the quantity moves in/out of; required.</summary>
    public Guid GodownId { get; }

    /// <summary>Movement quantity (&gt; 0), in <see cref="UnitId"/> (or the item's base unit when null).</summary>
    public decimal Quantity { get; }

    /// <summary>Inward (increase) or outward (decrease).</summary>
    public StockDirection Direction { get; }

    /// <summary>Optional per-unit rate (paisa-exact); <c>null</c> when the line carries no rate.</summary>
    public Money? Rate { get; }

    /// <summary>Optional batch/lot label (DP-10); <c>null</c> for a non-batch line.</summary>
    public string? BatchLabel { get; }

    /// <summary>
    /// The unit the <see cref="Quantity"/> is expressed in (a simple or compound unit). <c>null</c> ⇒ the
    /// item's base unit. The engine converts to the base unit before accumulating on-hand.
    /// </summary>
    public Guid? UnitId { get; }

    public InventoryAllocation(
        Guid stockItemId,
        Guid godownId,
        decimal quantity,
        StockDirection direction,
        Money? rate = null,
        string? batchLabel = null,
        Guid? unitId = null)
    {
        if (quantity <= 0m)
            throw new ArgumentException("An inventory allocation quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Inventory allocation quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate is { } r)
        {
            if (r.Amount < 0m)
                throw new ArgumentException("An inventory allocation rate must be ≥ 0.", nameof(rate));
            if (!r.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Inventory allocation rate {r.Amount} must be to the paisa (2 decimal places).");
        }

        StockItemId = stockItemId;
        GodownId = godownId;
        Quantity = quantity;
        Direction = direction;
        Rate = rate;
        BatchLabel = string.IsNullOrWhiteSpace(batchLabel) ? null : batchLabel.Trim();
        UnitId = unitId;
    }
}
