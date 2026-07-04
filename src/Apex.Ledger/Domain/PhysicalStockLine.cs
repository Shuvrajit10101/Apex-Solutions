namespace Apex.Ledger.Domain;

/// <summary>
/// One counted-quantity line on a Physical-Stock <see cref="InventoryVoucher"/> (catalog §10;
/// phase3-inventory-requirements RQ-15, DP-3). It records the physically counted quantity for an
/// (item, godown[, batch]) as of the voucher's date. Per DP-3 the engine treats the counted quantity as the
/// <b>new book quantity</b> as of that date — an implicit adjustment to the difference — so the counted
/// quantity must be ≥ 0.
/// </summary>
public sealed class PhysicalStockLine
{
    /// <summary>The <see cref="StockItem"/> counted; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The <see cref="Godown"/> the count is taken in; required.</summary>
    public Guid GodownId { get; }

    /// <summary>The physically counted quantity (≥ 0), in the item's base unit — the new book quantity (DP-3).</summary>
    public decimal CountedQuantity { get; }

    /// <summary>Optional batch/lot label (DP-10); <c>null</c> for a non-batch count.</summary>
    public string? BatchLabel { get; }

    public PhysicalStockLine(Guid stockItemId, Guid godownId, decimal countedQuantity, string? batchLabel)
    {
        if (countedQuantity < 0m)
            throw new ArgumentException("A physical-stock counted quantity must be ≥ 0.", nameof(countedQuantity));
        if (!Quantities.IsWithinPrecision(countedQuantity))
            throw new InvalidOperationException(
                $"Physical-stock counted quantity {countedQuantity} must be to {Quantities.DecimalPlaces} decimal places.");

        StockItemId = stockItemId;
        GodownId = godownId;
        CountedQuantity = countedQuantity;
        BatchLabel = string.IsNullOrWhiteSpace(batchLabel) ? null : batchLabel.Trim();
    }
}
