namespace Apex.Ledger.Domain;

/// <summary>
/// The direction a stock movement pushes on-hand quantity (catalog §10; phase3-inventory-requirements
/// RQ-10..RQ-14). <see cref="Inward"/> increases on-hand (a receipt, a customer rejection-in, a
/// Stock-Journal destination); <see cref="Outward"/> decreases it (a delivery, a rejection-out to a
/// supplier, a Stock-Journal source). The ordinals are stable (persisted as an INTEGER column).
/// </summary>
public enum StockDirection
{
    /// <summary>Increases on-hand quantity at the target (item, godown[, batch]).</summary>
    Inward = 0,

    /// <summary>Decreases on-hand quantity at the target (item, godown[, batch]).</summary>
    Outward = 1,
}
