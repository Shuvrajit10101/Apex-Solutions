using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The stock/order-voucher posting service (catalog §10; phase3-inventory-requirements RQ-8..RQ-15,
/// ER-5/ER-10, DP-3/DP-5/DP-7). It validates an <see cref="InventoryVoucher"/> against its type, applies the
/// <b>no-negative-stock hard block</b> on every outward path (DP-7), enforces the <b>Stock-Journal balance</b>
/// rule (source total = destination total, in the base unit), then appends the voucher — assigning an
/// automatic number per type. It is the inventory analogue of <see cref="LedgerService"/> for the accounting
/// side, and it is the <b>only</b> path that mutates the company's stock/order voucher set, so no movement
/// can bypass the guard. Framework- and DB-agnostic — unit-tested exactly like the accounting core.
/// </summary>
public sealed class InventoryPostingService
{
    private readonly Company _company;
    private readonly InventoryLedger _ledger;

    public InventoryPostingService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _ledger = new InventoryLedger(company);
    }

    /// <summary>
    /// Validates then posts a stock/order voucher. Rejects (never persists) a voucher whose content does not
    /// match its type, whose Stock-Journal sides do not balance, or that would drive any (item, godown[,
    /// batch]) on-hand negative as of any date (DP-7). Returns the posted voucher.
    /// </summary>
    public InventoryVoucher Post(InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);

        var type = _company.FindVoucherType(voucher.TypeId)
            ?? throw new InvalidOperationException($"Unknown voucher type {voucher.TypeId}.");

        if (voucher.Date < _company.BooksBeginFrom)
            throw new InvalidOperationException(
                $"Voucher date {voucher.Date:yyyy-MM-dd} is before BooksBeginFrom {_company.BooksBeginFrom:yyyy-MM-dd}.");

        EnsureContentMatchesType(voucher, type);
        EnsureReferencesResolve(voucher);

        // Stock Journal: source and destination must balance in the base unit (a documented difference is
        // wastage/absorption — Phase 6; Phase 3 requires an exact balance). A Manufacturing Journal (base Stock
        // Journal + Use as Manufacturing Journal, RQ-11/RQ-13) is EXEMPT — a manufacture transforms inputs into a
        // different output, so source and destination need not balance by quantity. A plain Stock Journal still
        // must balance (ER-13).
        if (type.BaseType == VoucherBaseType.StockJournal && !type.IsManufacturingJournal)
            EnsureStockJournalBalances(voucher);

        if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0)
            voucher.Number = NextNumber(voucher.TypeId);

        // Apply provisionally, then verify no key ever goes negative across the whole timeline; roll back on
        // violation so a rejected voucher is never persisted (guards every outward path — DP-7/ER-5).
        _company.AddInventoryVoucherInternal(voucher);
        try
        {
            EnsureNoNegativeStockAnywhere();
        }
        catch
        {
            _company.RemoveInventoryVoucherInternal(voucher);
            throw;
        }
        return voucher;
    }

    /// <summary>
    /// Cancels a stock/order voucher (Alt+X): its effect drops to zero but its number is retained. Blocked if
    /// removing its effect would retro-drive a later movement negative (the same DP-7 guard).
    /// </summary>
    public void Cancel(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");
        var was = v.Cancelled;
        v.Cancelled = true;
        try { EnsureNoNegativeStockAnywhere(); }
        catch { v.Cancelled = was; throw; }
    }

    /// <summary>
    /// Deletes a stock/order voucher (Alt+D). Blocked when removing its (inward) effect would retro-drive a
    /// later movement's on-hand negative — mirroring the no-negative guard on the forward path (ER-5).
    /// </summary>
    public void Delete(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        _company.RemoveInventoryVoucherInternal(v);
        try
        {
            EnsureNoNegativeStockAnywhere();
        }
        catch
        {
            _company.AddInventoryVoucherInternal(v); // restore — the delete would corrupt on-hand
            throw;
        }
    }

    /// <summary>Next automatic number for an inventory voucher type = max existing + 1 (per type).</summary>
    public int NextNumber(Guid voucherTypeId)
    {
        var max = 0;
        foreach (var v in _company.InventoryVouchers)
            if (v.TypeId == voucherTypeId && v.Number > max)
                max = v.Number;
        return max + 1;
    }

    /// <summary>
    /// Runs the centralised no-negative-stock guard (DP-7/ER-5) across the whole company timeline — including
    /// item-invoice (Purchase/Sales) movements. <see cref="LedgerService"/> calls this <b>after</b> it has
    /// provisionally appended an item-invoice accounting voucher, so a Sales item-invoice that would over-draw
    /// on-hand is rejected and the whole voucher rolled back atomically (no accounting leg, no stock movement
    /// persisted). Throws a clean domain exception naming the item and godown on violation.
    /// </summary>
    public void EnsureNoNegativeStock() => EnsureNoNegativeStockAnywhere();

    // ------------------------------------------------------------------ validation

    private static void EnsureContentMatchesType(InventoryVoucher v, VoucherType type)
    {
        var bt = type.BaseType;

        if (!VoucherEffects.IsInventoryBaseType(bt))
            throw new InvalidOperationException(
                $"Voucher type '{type.Name}' is not a stock or order voucher; it cannot be posted through the inventory engine.");

        var hasOrders = v.OrderLines.Count > 0;
        var hasAllocs = v.Allocations.Count > 0;
        var hasDest = v.DestinationAllocations.Count > 0;
        var hasPhysical = v.PhysicalLines.Count > 0;

        switch (bt)
        {
            case VoucherBaseType.PurchaseOrder:
            case VoucherBaseType.SalesOrder:
                if (!hasOrders || hasAllocs || hasDest || hasPhysical)
                    throw new InvalidOperationException($"A {type.Name} must carry order lines only (no stock movements).");
                break;

            case VoucherBaseType.ReceiptNote:
            case VoucherBaseType.RejectionIn:
                RequireAllocationsOnly(type, v);
                RequireDirection(type, v.Allocations, StockDirection.Inward);
                break;

            case VoucherBaseType.DeliveryNote:
            case VoucherBaseType.RejectionOut:
                RequireAllocationsOnly(type, v);
                RequireDirection(type, v.Allocations, StockDirection.Outward);
                break;

            case VoucherBaseType.StockJournal:
                if (!hasAllocs || !hasDest || hasOrders || hasPhysical)
                    throw new InvalidOperationException(
                        $"A {type.Name} must carry source (outward) and destination (inward) lines.");
                RequireDirection(type, v.Allocations, StockDirection.Outward, "source");
                RequireDirection(type, v.DestinationAllocations, StockDirection.Inward, "destination");
                break;

            case VoucherBaseType.PhysicalStock:
                if (!hasPhysical || hasAllocs || hasDest || hasOrders)
                    throw new InvalidOperationException($"A {type.Name} must carry counted-quantity lines only.");
                break;
        }
    }

    private static void RequireAllocationsOnly(VoucherType type, InventoryVoucher v)
    {
        if (v.Allocations.Count == 0 || v.OrderLines.Count > 0 || v.DestinationAllocations.Count > 0 || v.PhysicalLines.Count > 0)
            throw new InvalidOperationException($"A {type.Name} must carry stock-movement lines only.");
    }

    private static void RequireDirection(VoucherType type, IReadOnlyList<InventoryAllocation> lines,
        StockDirection expected, string? side = null)
    {
        foreach (var a in lines)
            if (a.Direction != expected)
                throw new InvalidOperationException(
                    $"A {type.Name}{(side is null ? "" : $" {side}")} line must be {expected.ToString().ToLowerInvariant()}.");
    }

    private void EnsureReferencesResolve(InventoryVoucher v)
    {
        void CheckAlloc(InventoryAllocation a)
        {
            if (_company.FindStockItem(a.StockItemId) is null)
                throw new InvalidOperationException($"Inventory line references unknown stock item {a.StockItemId}.");
            if (_company.FindGodown(a.GodownId) is null)
                throw new InvalidOperationException($"Inventory line references unknown godown {a.GodownId}.");
            if (a.UnitId is { } uid && _company.FindUnit(uid) is null)
                throw new InvalidOperationException($"Inventory line references unknown unit {uid}.");
        }

        foreach (var a in v.Allocations) CheckAlloc(a);
        foreach (var a in v.DestinationAllocations) CheckAlloc(a);
        foreach (var o in v.OrderLines)
        {
            if (_company.FindStockItem(o.StockItemId) is null)
                throw new InvalidOperationException($"Order line references unknown stock item {o.StockItemId}.");
            if (_company.FindGodown(o.GodownId) is null)
                throw new InvalidOperationException($"Order line references unknown godown {o.GodownId}.");
        }
        foreach (var pl in v.PhysicalLines)
        {
            if (_company.FindStockItem(pl.StockItemId) is null)
                throw new InvalidOperationException($"Physical-stock line references unknown stock item {pl.StockItemId}.");
            if (_company.FindGodown(pl.GodownId) is null)
                throw new InvalidOperationException($"Physical-stock line references unknown godown {pl.GodownId}.");
        }
    }

    private void EnsureStockJournalBalances(InventoryVoucher v)
    {
        var source = v.Allocations.Sum(a => _ledger.QuantityInBase(a));
        var dest = v.DestinationAllocations.Sum(a => _ledger.QuantityInBase(a));
        if (source != dest)
            throw new InvalidOperationException(
                $"Stock Journal source total {source} and destination total {dest} do not balance (they must be equal in the base unit).");
    }

    /// <summary>
    /// The centralised no-negative-stock guard (ER-5/DP-7): across every (item, godown, batch) touched by any
    /// posted movement, on-hand must be ≥ 0 at every voucher date where the key is affected. A Physical-Stock
    /// count is applied LAST within its date and <b>sets</b> on-hand to the counted quantity (DP-3), so
    /// end-of-date sampling would mask a same-date outward line that over-drew pre-count stock. The guard
    /// therefore validates the running balance <b>before</b> that date's count checkpoint is applied
    /// (<see cref="InventoryLedger.PreCountOnHandForKey"/>) — on non-count dates this equals end-of-date
    /// on-hand. DP-3 reporting/carry-forward is unchanged (a count still sets on-hand). Throws a clean domain
    /// exception naming the item and godown.
    /// </summary>
    private void EnsureNoNegativeStockAnywhere()
    {
        // Every affected key.
        var keys = new HashSet<InventoryLedger.Key>();
        var dates = new SortedSet<DateOnly>();
        foreach (var v in _company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || !type.AffectsStock) continue;

            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                keys.Add(new InventoryLedger.Key(a.StockItemId, a.GodownId, Batch(a.BatchLabel)));
                dates.Add(v.Date);
            }
            foreach (var pl in v.PhysicalLines)
            {
                keys.Add(new InventoryLedger.Key(pl.StockItemId, pl.GodownId, Batch(pl.BatchLabel)));
                dates.Add(v.Date);
            }
        }

        // Item-invoice (Purchase/Sales) stock lines touch the same keys/dates and must satisfy the same guard,
        // so a Sales item-invoice that would over-draw on-hand is blocked (and rolled back) exactly like a
        // Delivery Note (DP-7). Cancelled/optional item-invoice vouchers never contribute (Counts filters them).
        foreach (var v in _company.Vouchers)
        {
            if (!v.HasInventoryLines) continue;
            if (v.Cancelled || v.Optional) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales)) continue;
            foreach (var line in v.InventoryLines)
            {
                keys.Add(new InventoryLedger.Key(line.StockItemId, line.GodownId, Batch(line.BatchLabel)));
                dates.Add(v.Date);
            }
        }

        foreach (var key in keys)
        {
            foreach (var date in dates)
            {
                // Validate the running balance BEFORE the same-date Physical-Stock checkpoint (DP-7): on a
                // count date this exposes an intra-day over-draw the checkpoint would otherwise hide; on a
                // non-count date it is identical to end-of-date on-hand.
                var onHand = _ledger.PreCountOnHandForKey(key, date);
                if (onHand < 0m)
                {
                    var item = _company.FindStockItem(key.ItemId)?.Name ?? key.ItemId.ToString();
                    var godown = _company.FindGodown(key.GodownId)?.Name ?? key.GodownId.ToString();
                    var batch = string.IsNullOrEmpty(key.Batch) ? "" : $" (batch '{key.Batch}')";
                    throw new InvalidOperationException(
                        $"Movement would drive '{item}' at '{godown}'{batch} negative (on-hand {onHand} as of {date:yyyy-MM-dd}). Negative stock is not allowed.");
                }
            }
        }
    }

    private static string Batch(string? batch) => string.IsNullOrWhiteSpace(batch) ? string.Empty : batch.Trim();
}
