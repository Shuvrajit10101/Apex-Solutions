using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The stock-on-hand engine (catalog §10/§16; phase3-inventory-requirements RQ-19/RQ-20, ER-4/ER-5,
/// DP-3/DP-6). A <b>pure</b> projection over the company's opening balances and posted stock/order vouchers:
/// on-hand for an (item, godown[, batch]) as of a date =
/// <c>opening + Σ inward − Σ outward</c> from stock-affecting vouchers dated ≤ <c>asOf</c>, with a
/// <b>Physical-Stock</b> count acting as a checkpoint that resets the running balance to the counted quantity
/// as of its date (DP-3). It honours the same as-of / post-dated convention the accounting side uses
/// (<see cref="Reports.LedgerBalances.CountsAsOf"/>): a cancelled voucher never counts, and a post-dated
/// voucher's stock only counts once its date ≤ <c>asOf</c>. Compound-unit line quantities are normalised to
/// the item's base unit via the unit's exact integer factor (DP-6). No Avalonia/DB/clock dependency — it is
/// unit-tested exactly like the accounting core.
/// </summary>
public sealed class InventoryLedger
{
    private readonly Company _company;

    public InventoryLedger(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The on-hand quantity (item's base unit) for a specific batch at a godown, as of a date.</summary>
    public decimal OnHand(Guid stockItemId, Guid godownId, string? batchLabel, DateOnly asOf)
        => OnHandForKey(new Key(stockItemId, godownId, Normalise(batchLabel)), asOf, excludeVoucherId: null);

    /// <summary>The on-hand quantity (item's base unit) at a godown, summed across all batches, as of a date.</summary>
    public decimal OnHand(Guid stockItemId, Guid godownId, DateOnly asOf)
    {
        var total = 0m;
        foreach (var key in KeysFor(stockItemId, godownId))
            total += OnHandForKey(key, asOf, excludeVoucherId: null);
        return total;
    }

    /// <summary>The total on-hand quantity for an item across every godown and batch, as of a date.</summary>
    public decimal OnHand(Guid stockItemId, DateOnly asOf)
    {
        var total = 0m;
        foreach (var key in KeysFor(stockItemId, godownId: null))
            total += OnHandForKey(key, asOf, excludeVoucherId: null);
        return total;
    }

    /// <summary>
    /// The list of implied Physical-Stock adjustments recorded on or before <paramref name="asOf"/> (DP-3):
    /// for each counted line, the (item, godown, batch) and the adjustment = counted − book-before-count.
    /// The Physical-Stock register surfaces this difference.
    /// </summary>
    public IReadOnlyList<PhysicalStockAdjustment> PhysicalStockAdjustments(DateOnly asOf)
    {
        var result = new List<PhysicalStockAdjustment>();
        foreach (var v in OrderedInventoryVouchers())
        {
            if (v.PhysicalLines.Count == 0) continue;
            if (!Counts(v, asOf)) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != VoucherBaseType.PhysicalStock) continue;
            foreach (var pl in v.PhysicalLines)
            {
                var key = new Key(pl.StockItemId, pl.GodownId, Normalise(pl.BatchLabel));
                // Book quantity just BEFORE this count = on-hand as of the day before this voucher's effect,
                // computed by replaying every earlier movement (including earlier counts) for the key.
                var before = OnHandForKey(key, v.Date, excludeFromInclusive: v);
                result.Add(new PhysicalStockAdjustment(pl.StockItemId, pl.GodownId, pl.BatchLabel,
                    v.Date, before, pl.CountedQuantity, pl.CountedQuantity - before));
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ internal on-hand computation

    /// <summary>
    /// Replays every stock-affecting movement for a single key in chronological order up to
    /// <paramref name="asOf"/>, returning the running on-hand. A Physical-Stock count sets the running
    /// balance to its counted quantity (DP-3). When <paramref name="excludeVoucherId"/> is set, that voucher
    /// is skipped (used to test a would-be delete). When <paramref name="excludeFromInclusive"/> is set, that
    /// voucher and everything from it onward (in the ordered sequence) is skipped (used to read the
    /// book-before-count for a Physical-Stock line).
    /// </summary>
    internal decimal OnHandForKey(Key key, DateOnly asOf, Guid? excludeVoucherId = null,
        InventoryVoucher? excludeFromInclusive = null, bool skipCountCheckpointOnAsOf = false)
    {
        var running = OpeningFor(key);
        var reached = excludeFromInclusive is null;
        foreach (var v in OrderedInventoryVouchers())
        {
            if (!reached)
            {
                if (ReferenceEquals(v, excludeFromInclusive)) break; // stop just before this voucher
            }
            if (excludeVoucherId is { } ex && v.Id == ex) continue;
            if (!Counts(v, asOf)) continue;
            // For the no-negative GUARD only: on the as-of date itself, apply that date's movements but NOT its
            // Physical-Stock count checkpoint, so an outward line that over-drew pre-count stock is not masked
            // by the count SETTING on-hand back to the counted quantity (DP-3 vs DP-7).
            var suppressCheckpoint = skipCountCheckpointOnAsOf && v.Date == asOf;
            ApplyToKey(v, key, ref running, suppressCheckpoint);
        }
        return running;
    }

    /// <summary>
    /// The running on-hand for a key up to <paramref name="asOf"/> computed WITHOUT applying that date's
    /// Physical-Stock count checkpoint — i.e. opening + every earlier movement (including earlier counts) +
    /// that date's own non-count movements. This is the value the no-negative guard (DP-7) must validate on a
    /// count date, since <see cref="ApplyToKey"/> would otherwise reset the running balance to the counted
    /// quantity and hide an intra-day over-draw. Reporting/carry-forward still uses <see cref="OnHandForKey"/>
    /// (checkpoint applied), so DP-3 semantics are unchanged.
    /// </summary>
    internal decimal PreCountOnHandForKey(Key key, DateOnly asOf)
        => OnHandForKey(key, asOf, skipCountCheckpointOnAsOf: true);

    private void ApplyToKey(InventoryVoucher v, Key key, ref decimal running, bool suppressCountCheckpoint = false)
    {
        // Physical Stock: a matching count resets the running balance to the counted quantity (DP-3). The
        // no-negative guard suppresses this reset on the sampled date so an intra-day over-draw is not masked.
        if (!suppressCountCheckpoint)
            foreach (var pl in v.PhysicalLines)
                if (pl.StockItemId == key.ItemId && pl.GodownId == key.GodownId && Normalise(pl.BatchLabel) == key.Batch)
                    running = pl.CountedQuantity;

        // Movement allocations (GRN/Delivery/Rejection/Stock-Journal source) and Stock-Journal destination.
        foreach (var a in v.Allocations)
            running += SignedFor(a, key);
        foreach (var a in v.DestinationAllocations)
            running += SignedFor(a, key);
    }

    private decimal SignedFor(InventoryAllocation a, Key key)
    {
        if (a.StockItemId != key.ItemId || a.GodownId != key.GodownId || Normalise(a.BatchLabel) != key.Batch)
            return 0m;
        var baseQty = QuantityInBase(a);
        return a.Direction == StockDirection.Inward ? baseQty : -baseQty;
    }

    /// <summary>The allocation's quantity normalised to the item's base unit (DP-6).</summary>
    internal decimal QuantityInBase(InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = _company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }

    private decimal OpeningFor(Key key)
    {
        var sum = 0m;
        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == key.ItemId && b.GodownId == key.GodownId && Normalise(b.BatchLabel) == key.Batch)
                sum += b.Quantity;
        return sum;
    }

    /// <summary>Every (item, godown, batch) key that has any opening or movement for the given filter.</summary>
    private IEnumerable<Key> KeysFor(Guid stockItemId, Guid? godownId)
    {
        var keys = new HashSet<Key>();
        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == stockItemId && (godownId is null || b.GodownId == godownId.Value))
                keys.Add(new Key(stockItemId, b.GodownId, Normalise(b.BatchLabel)));

        foreach (var v in _company.InventoryVouchers)
        {
            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
                if (a.StockItemId == stockItemId && (godownId is null || a.GodownId == godownId.Value))
                    keys.Add(new Key(stockItemId, a.GodownId, Normalise(a.BatchLabel)));
            foreach (var pl in v.PhysicalLines)
                if (pl.StockItemId == stockItemId && (godownId is null || pl.GodownId == godownId.Value))
                    keys.Add(new Key(stockItemId, pl.GodownId, Normalise(pl.BatchLabel)));
        }
        return keys;
    }

    /// <summary>
    /// Inventory vouchers ordered for a deterministic on-hand replay: by date, then — within a date — a
    /// Physical-Stock count is applied <b>last</b> (it is the end-of-day book truth, DP-3, so it checkpoints
    /// over that day's movements), then by number and id for stability.
    /// </summary>
    private IEnumerable<InventoryVoucher> OrderedInventoryVouchers()
        => _company.InventoryVouchers
            .OrderBy(v => v.Date)
            .ThenBy(v => IsPhysicalStock(v) ? 1 : 0)
            .ThenBy(v => v.Number)
            .ThenBy(v => v.Id);

    private bool IsPhysicalStock(InventoryVoucher v)
        => _company.FindVoucherType(v.TypeId)?.BaseType == VoucherBaseType.PhysicalStock;

    /// <summary>
    /// Whether a stock/order voucher contributes to on-hand at <paramref name="asOf"/> — mirroring the
    /// accounting side's <see cref="Reports.LedgerBalances.CountsAsOf"/>: cancelled never counts, a
    /// post-dated voucher only counts once its date ≤ <paramref name="asOf"/>, and only stock-affecting types
    /// (not orders) count. A voucher dated after <paramref name="asOf"/> is excluded.
    /// </summary>
    private bool Counts(InventoryVoucher v, DateOnly asOf)
    {
        if (v.Cancelled) return false;
        if (v.Date > asOf) return false;
        if (v.PostDated && v.Date > asOf) return false; // redundant with the date bound, kept for clarity
        var type = _company.FindVoucherType(v.TypeId);
        return type is not null && type.AffectsStock;
    }

    private static string Normalise(string? batch) => string.IsNullOrWhiteSpace(batch) ? string.Empty : batch.Trim();

    /// <summary>The (item, godown, batch) on-hand key. Batch "" denotes the no-batch bucket.</summary>
    internal readonly record struct Key(Guid ItemId, Guid GodownId, string Batch);
}

/// <summary>
/// One implied Physical-Stock adjustment (DP-3): the book quantity just before the count, the counted
/// quantity, and their difference for an (item, godown[, batch]) as of the count date. Surfaced by the
/// Physical-Stock register.
/// </summary>
public sealed record PhysicalStockAdjustment(
    Guid StockItemId,
    Guid GodownId,
    string? BatchLabel,
    DateOnly Date,
    decimal BookQuantityBefore,
    decimal CountedQuantity,
    decimal AdjustmentQuantity);
