using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The batch-master service (Phase 6 Cluster 1; requirements RQ-1, RQ-4). Creates and deletes
/// <see cref="BatchMaster"/> records, enforcing the same discipline the other inventory masters ship with:
/// <list type="bullet">
///   <item>the stock item must exist;</item>
///   <item>a batch number is <b>unique within its item</b> (RQ-1) — case-insensitive — but MAY be reused
///     across different items (mirrors the schema's UNIQUE (stock_item_id, batch_no) index, not a global one);</item>
///   <item>an inward-layer godown, when named, must exist;</item>
///   <item>expiry may be given as an absolute date or a resolvable <see cref="ExpiryPeriod"/> (RQ-4) — the
///     master resolves the concrete date via <see cref="BatchMaster.ResolvedExpiryDate"/>.</item>
/// </list>
/// The service throws <see cref="InvalidOperationException"/> on any violation (never mutating the company),
/// exactly like <see cref="InventoryService"/>. Framework- and DB-agnostic — unit-tested like the accounting core.
/// </summary>
public sealed class BatchService
{
    private readonly Company _company;

    public BatchService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a batch for an item. The item must exist; the batch number must be non-blank and not already
    /// used <b>by that item</b> (RQ-1); the inward godown, if given, must exist. Expiry may be an absolute date
    /// and/or an <see cref="ExpiryPeriod"/> resolved from the mfg date (RQ-4).
    /// </summary>
    public BatchMaster CreateBatch(
        Guid stockItemId,
        string batchNumber,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null,
        ExpiryPeriod? expiryPeriod = null,
        Guid? godownId = null,
        decimal? inwardQuantity = null,
        Money? inwardRate = null)
    {
        var trimmed = (batchNumber ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A batch number is required.");
        if (_company.FindStockItem(stockItemId) is not { } item)
            throw new InvalidOperationException($"Stock item {stockItemId} not found.");
        if (_company.FindBatchByNumber(stockItemId, trimmed) is not null)
            throw new InvalidOperationException(
                $"A batch '{trimmed}' already exists for item '{item.Name}' (batch numbers are unique per item).");
        if (godownId is { } gid && _company.FindGodown(gid) is null)
            throw new InvalidOperationException($"Godown {gid} not found.");

        var batch = new BatchMaster(Guid.NewGuid(), stockItemId, trimmed, manufacturingDate, expiryDate,
            expiryPeriod, godownId, inwardQuantity, inwardRate);
        _company.AddBatchMaster(batch);
        return batch;
    }

    /// <summary>
    /// Deletes a batch, blocked while any opening balance, inventory-voucher allocation, item-invoice line or
    /// physical-count line references its number for the same item (so no movement is orphaned).
    /// </summary>
    public void DeleteBatch(Guid batchId)
    {
        var batch = _company.FindBatchMaster(batchId)
            ?? throw new InvalidOperationException($"Batch {batchId} not found.");

        if (IsBatchReferenced(batch))
            throw new InvalidOperationException(
                $"Batch '{batch.BatchNumber}' has stock movements and cannot be deleted.");

        _company.RemoveBatchMaster(batch);
    }

    private bool IsBatchReferenced(BatchMaster batch)
    {
        static string Norm(string? s) => (s ?? string.Empty).Trim();
        var label = Norm(batch.BatchNumber);

        bool Matches(Guid itemId, string? batchLabel)
            => itemId == batch.StockItemId &&
               string.Equals(Norm(batchLabel), label, StringComparison.OrdinalIgnoreCase);

        foreach (var b in _company.StockOpeningBalances)
            if (Matches(b.StockItemId, b.BatchLabel)) return true;

        foreach (var v in _company.InventoryVouchers)
        {
            foreach (var a in v.Allocations)
                if (Matches(a.StockItemId, a.BatchLabel)) return true;
            foreach (var a in v.DestinationAllocations)
                if (Matches(a.StockItemId, a.BatchLabel)) return true;
            foreach (var pl in v.PhysicalLines)
                if (Matches(pl.StockItemId, pl.BatchLabel)) return true;
        }

        foreach (var v in _company.Vouchers)
            foreach (var line in v.InventoryLines)
                if (Matches(line.StockItemId, line.BatchLabel)) return true;

        return false;
    }
}
