namespace Apex.Ledger.Domain;

/// <summary>
/// One allocation of a <see cref="StockItem"/>'s <b>opening stock</b>, held at a specific
/// <see cref="Godown"/> and (optionally) a batch/lot label (catalog §9; requirements RQ-6/DP-10). Opening
/// stock is expressed as <see cref="Quantity"/> × <see cref="Rate"/> → <see cref="Value"/>; an item's
/// total opening value is the Σ of its allocations, to the paisa (NFR-3).
/// </summary>
/// <remarks>
/// <para><see cref="Rate"/> is the per-unit rate and <see cref="Value"/> is the paisa-exact
/// <c>Quantity × Rate</c> (snapped to the paisa). Both are <see cref="Money"/> so the persistence layer
/// stores them as INTEGER paisa.</para>
/// <para>The optional <see cref="BatchLabel"/> lets multi-batch opening stock reconcile without pulling in
/// batch-expiry behaviour. <see cref="ManufacturingDate"/> / <see cref="ExpiryDate"/> columns exist for
/// forward-compatibility only — no behaviour hangs off the dates in this phase (they are Phase-6 territory).</para>
/// </remarks>
public sealed class StockOpeningBalance
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The <see cref="StockItem"/> this opening allocation belongs to; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The <see cref="Godown"/> the opening quantity sits in; required.</summary>
    public Guid GodownId { get; set; }

    /// <summary>Optional batch/lot label (DP-10); <c>null</c> for a non-batch item.</summary>
    public string? BatchLabel { get; set; }

    /// <summary>Opening quantity (≥ 0), in the item's base unit.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Per-unit rate; the opening value is <see cref="Quantity"/> × this, snapped to the paisa.</summary>
    public Money Rate { get; set; }

    /// <summary>Forward-compat only (Phase 6): manufacturing date; no behaviour hangs off it yet.</summary>
    public DateOnly? ManufacturingDate { get; set; }

    /// <summary>Forward-compat only (Phase 6): expiry date; no behaviour hangs off it yet.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>The paisa-exact opening value = <see cref="Quantity"/> × <see cref="Rate"/>.</summary>
    public Money Value => Money.ForexBase(Rate, Quantity);

    public StockOpeningBalance(
        Guid id,
        Guid stockItemId,
        Guid godownId,
        decimal quantity,
        Money rate,
        string? batchLabel = null,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null)
    {
        if (quantity < 0m)
            throw new ArgumentException("Opening quantity must be ≥ 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Opening stock quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate.Amount < 0m)
            throw new ArgumentException("Opening rate must be ≥ 0.", nameof(rate));
        if (!rate.IsPaisaExact)
            throw new InvalidOperationException(
                $"Opening stock rate {rate.Amount} must be to the paisa (2 decimal places).");

        Id = id;
        StockItemId = stockItemId;
        GodownId = godownId;
        Quantity = quantity;
        Rate = rate;
        BatchLabel = string.IsNullOrWhiteSpace(batchLabel) ? null : batchLabel.Trim();
        ManufacturingDate = manufacturingDate;
        ExpiryDate = expiryDate;
    }
}
