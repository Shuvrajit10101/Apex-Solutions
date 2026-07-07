using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The Bill-of-Materials master service (Phase 6 Cluster 2; requirements RQ-9/RQ-10). Creates and deletes
/// <see cref="BillOfMaterials"/> records for a finished-good stock item, enforcing the same discipline the
/// other inventory masters ship with:
/// <list type="bullet">
///   <item>the finished-good item must exist;</item>
///   <item>a BOM name is <b>unique within its finished good</b> (RQ-9) — case-insensitive — but MAY be reused
///     across different items (mirrors the schema's UNIQUE (stock_item_id, name COLLATE NOCASE) index);</item>
///   <item>every component/output item referenced by a line must exist, and a named godown must exist;</item>
///   <item>a component may not be the finished good itself (a BOM cannot consume/produce its own output);</item>
///   <item>creating a BOM turns the item's <see cref="StockItem.SetComponents"/> flag on (RQ-10).</item>
/// </list>
/// The service throws <see cref="InvalidOperationException"/> on any violation (never mutating the company),
/// exactly like <see cref="BatchService"/>/<see cref="InventoryService"/>. Framework- and DB-agnostic —
/// unit-tested like the accounting core. It does NOT post anything; posting a manufacture is
/// <see cref="ManufacturingJournalService"/>.
/// </summary>
public sealed class BomService
{
    private readonly Company _company;

    public BomService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a BOM for a finished good. The item must exist; the name must be non-blank and not already used
    /// <b>by that item</b> (RQ-9); every line's component item must exist (and differ from the finished good);
    /// a named godown must exist; there must be at least one Component line (enforced by
    /// <see cref="BillOfMaterials"/>). Turns the item's <see cref="StockItem.SetComponents"/> flag on (RQ-10).
    /// </summary>
    public BillOfMaterials CreateBom(
        Guid stockItemId,
        string name,
        decimal unitOfManufacture,
        IEnumerable<BomLine> lines)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A BOM name is required.");
        if (_company.FindStockItem(stockItemId) is not { } item)
            throw new InvalidOperationException($"Stock item {stockItemId} not found.");
        if (_company.FindBomByName(stockItemId, trimmed) is not null)
            throw new InvalidOperationException(
                $"A BOM '{trimmed}' already exists for item '{item.Name}' (BOM names are unique per item).");

        var lineList = lines?.ToList() ?? throw new ArgumentNullException(nameof(lines));
        foreach (var line in lineList)
        {
            if (_company.FindStockItem(line.ComponentStockItemId) is null)
                throw new InvalidOperationException(
                    $"BOM line references unknown stock item {line.ComponentStockItemId}.");
            if (line.ComponentStockItemId == stockItemId)
                throw new InvalidOperationException(
                    $"A BOM for '{item.Name}' cannot reference the finished good itself as a component/output.");
            if (line.GodownId is { } gid && _company.FindGodown(gid) is null)
                throw new InvalidOperationException($"BOM line references unknown godown {gid}.");
        }

        // Constructs (validates ≥ 1 line, ≥ 1 Component, positive unit-of-manufacture) — throws on bad input.
        var bom = new BillOfMaterials(Guid.NewGuid(), stockItemId, trimmed, unitOfManufacture, lineList);
        _company.AddBillOfMaterials(bom);
        item.SetComponents = true; // RQ-10: an item with a BOM is a manufactured item.
        return bom;
    }

    /// <summary>
    /// Deletes a BOM. If it was the finished good's <b>last</b> BOM, the item's
    /// <see cref="StockItem.SetComponents"/> flag is turned back off (it is no longer a manufactured item).
    /// </summary>
    public void DeleteBom(Guid bomId)
    {
        var bom = _company.FindBillOfMaterials(bomId)
            ?? throw new InvalidOperationException($"BOM {bomId} not found.");

        _company.RemoveBillOfMaterials(bom);

        if (_company.FindStockItem(bom.StockItemId) is { } item && !_company.BomsFor(bom.StockItemId).Any())
            item.SetComponents = false;
    }
}
