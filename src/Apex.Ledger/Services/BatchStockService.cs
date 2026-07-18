using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The batch-aware stock engine (Phase 6 Cluster 1; requirements RQ-5, RQ-6, RQ-7, DP-1, DP-8). A <b>pure</b>
/// projection layered on top of the existing <see cref="InventoryLedger"/> (on-hand) and
/// <see cref="StockValuationService"/> conventions: it splits an item's on-hand into per-(godown, batch)
/// balances, decides the natural <b>outward selection order</b> — FEFO when the item's UseExpiryDates switch
/// is on, else FIFO-by-inward (DP-1) — with the user always able to pin a specific batch, values each batch at its own
/// authoritative inward rate (DP-8), and exposes a non-blocking expired/near-expiry <b>warning</b> signal
/// (RQ-7, warn-not-block).
/// <para><b>Non-batch items unchanged (ER-13).</b> An item with no batch masters and no batch labels on its
/// movements has exactly one on-hand bucket (batch ""), so its on-hand and valuation flow through the existing
/// engines byte-identically — this service adds a layer, it does not alter the base engines.</para>
/// <para><b>Per-batch valuation (RQ-6/DP-8).</b> A batch's unit cost is, in order: the batch master's own
/// inward rate (authoritative); else the batch's most-recent rated inward movement; else the item's running
/// closing rate (closing value ÷ closing qty) as a graceful fallback so a batch is never valued at ₹0 while
/// the item as a whole has cost.</para>
/// <para><b>Batch-wise value is a SEPARATE projection from Stock Summary for non-per-lot methods (DP-8).</b>
/// Because each batch is valued at its <i>own</i> inward rate, the Σ of an item's per-batch closing values
/// equals the item-level Stock Summary / <see cref="StockValuationService"/> closing value <b>only when the
/// item's valuation method already prices remaining stock at per-lot inward cost</b> — i.e. every remaining
/// lot's cost coincides with what the item method would assign (e.g. a single cost layer, or FIFO/LIFO where
/// the surviving lots are exactly the method's kept lots). For <see cref="StockValuationMethod.AverageCost"/>
/// (and, in general, FIFO/LIFO when the batches consumed are not the method's chosen lots) the two figures
/// legitimately differ: DP-8 makes per-batch inward cost authoritative for a batch's own issues, while the
/// item method aggregates across lots for item-level reports. The Batch-wise report therefore shows a
/// per-lot-cost figure that is a distinct, intended projection from Stock Summary for such items — this is the
/// faithful behaviour, not a footing error (ER-4 governs one source per figure; Batch-wise and Stock Summary
/// are two different figures with two different definitions).</para>
/// No Avalonia/DB/clock/RNG dependency — unit-tested exactly like the accounting core.
/// </summary>
public sealed class BatchStockService
{
    private readonly Company _company;
    private readonly InventoryLedger _onHand;
    private readonly StockValuationService _valuation;

    public BatchStockService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _onHand = new InventoryLedger(company);
        _valuation = new StockValuationService(company);
    }

    // ------------------------------------------------------------------ batch-aware on-hand (RQ-5)

    /// <summary>
    /// The per-batch on-hand for an item as of a date (RQ-5): one <see cref="BatchOnHand"/> per
    /// (godown, batch) bucket that carries any opening or movement, with the batch's resolved mfg/expiry (from
    /// the batch master when one exists) and its authoritative unit cost (DP-8). Buckets with the empty batch
    /// label are the item's non-batch stock. Zero-on-hand buckets are included when they had movement, so a
    /// fully-issued batch still surfaces in the batch-wise report; callers filter as needed.
    /// </summary>
    public IReadOnlyList<BatchOnHand> BatchOnHands(Guid stockItemId, DateOnly asOf)
    {
        var result = new List<BatchOnHand>();
        foreach (var (godownId, label) in BucketsFor(stockItemId))
        {
            var qty = _onHand.OnHand(stockItemId, godownId, NullIfEmpty(label), asOf);
            var master = string.IsNullOrEmpty(label) ? null : _company.FindBatchByNumber(stockItemId, label);
            result.Add(new BatchOnHand(
                stockItemId, godownId, label,
                master?.Id,
                master?.ManufacturingDate,
                master?.ResolvedExpiryDate,
                qty,
                BatchUnitCost(stockItemId, godownId, label, master, asOf)));
        }

        // Deterministic order: godown, then FEFO/FIFO within godown so the natural issue order is visible.
        result.Sort(CompareForIssue);
        return result;
    }

    // ------------------------------------------------------------------ default issue selection (DP-1)

    /// <summary>
    /// The batches to consume for an outward of <paramref name="requiredQuantity"/> from an (item, godown),
    /// in the natural default order (DP-1): <b>FEFO</b> (soonest resolved-expiry first) when the item's
    /// <see cref="StockItem.UseExpiryDates"/> switch is on, else <b>FIFO-by-inward</b> (earliest inward date
    /// first). The decision follows the item switch, NOT whether individual batch masters happen to carry an
    /// expiry date (a FIFO-mode item's batches may still carry expiry dates, RQ-2), so such an item issues
    /// earliest-in first. Only buckets with a positive on-hand as of <paramref name="asOf"/> are eligible; each pick takes
    /// min(remaining, bucket on-hand). This is the selection the UI pre-fills; the user MAY override by pinning
    /// a specific batch (see <see cref="PinnedIssue"/>). It does <b>not</b> block on insufficient stock — it
    /// returns as much as the eligible batches cover (the no-negative guard enforces sufficiency at post time).
    /// </summary>
    public IReadOnlyList<BatchIssue> DefaultIssueSelection(
        Guid stockItemId, Guid godownId, decimal requiredQuantity, DateOnly asOf)
    {
        if (requiredQuantity <= 0m) return Array.Empty<BatchIssue>();

        var candidates = BatchOnHands(stockItemId, asOf)
            .Where(b => b.GodownId == godownId && b.Quantity > 0m)
            .ToList();
        candidates.Sort(CompareForIssue);

        return DrawDown(candidates, requiredQuantity);
    }

    /// <summary>
    /// The issue plan when the user <b>pins</b> a specific batch (RQ-5 "user MAY select a specific batch"): draw
    /// from that batch first (up to its on-hand), then fall back to the default FEFO/FIFO order for any residual.
    /// If the pinned batch has no stock the plan is simply the default selection.
    /// </summary>
    public IReadOnlyList<BatchIssue> PinnedIssue(
        Guid stockItemId, Guid godownId, string pinnedBatchLabel, decimal requiredQuantity, DateOnly asOf)
    {
        if (requiredQuantity <= 0m) return Array.Empty<BatchIssue>();
        var pinned = Normalise(pinnedBatchLabel);

        var buckets = BatchOnHands(stockItemId, asOf)
            .Where(b => b.GodownId == godownId && b.Quantity > 0m)
            .ToList();

        var ordered = new List<BatchOnHand>();
        var pinnedBucket = buckets.FirstOrDefault(b => b.Batch == pinned);
        if (pinnedBucket is not null) ordered.Add(pinnedBucket);
        var rest = buckets.Where(b => b.Batch != pinned).ToList();
        rest.Sort(CompareForIssue);
        ordered.AddRange(rest);

        return DrawDown(ordered, requiredQuantity);
    }

    // ------------------------------------------------------------------ per-batch valuation (RQ-6/DP-8)

    /// <summary>
    /// The paisa-exact value of an issue of <paramref name="quantity"/> from a specific batch (RQ-6/DP-8):
    /// <paramref name="quantity"/> × the batch's authoritative unit cost. Used to value an outward at the
    /// batch's own inward rate rather than the item average.
    /// </summary>
    public Money ValueOfIssue(Guid stockItemId, Guid godownId, string? batchLabel, decimal quantity, DateOnly asOf)
    {
        var label = Normalise(batchLabel);
        var master = string.IsNullOrEmpty(label) ? null : _company.FindBatchByNumber(stockItemId, label);
        var unit = BatchUnitCost(stockItemId, godownId, label, master, asOf);
        return Money.ForexBase(unit, quantity);
    }

    // ------------------------------------------------------------------ warn-not-block (RQ-7)

    /// <summary>
    /// The non-blocking warning for selecting a batch on an outward (RQ-7, warn-not-block): a
    /// <see cref="BatchExpiryWarning"/> when the batch is expired or expires within
    /// <paramref name="nearExpiryDays"/> of <paramref name="asOf"/>, else <c>null</c>. This is advisory only —
    /// the engine never hard-blocks the issue on it (the caller shows it and proceeds).
    /// </summary>
    public BatchExpiryWarning? ExpiryWarningFor(
        Guid stockItemId, string batchLabel, DateOnly asOf, int nearExpiryDays = 30)
    {
        var label = Normalise(batchLabel);
        if (label.Length == 0) return null;
        var master = _company.FindBatchByNumber(stockItemId, label);
        if (master?.ResolvedExpiryDate is not { } expiry) return null;

        var daysToExpiry = expiry.DayNumber - asOf.DayNumber;
        if (daysToExpiry < 0)
            return new BatchExpiryWarning(label, expiry, daysToExpiry, BatchExpiryKind.Expired);
        if (daysToExpiry <= nearExpiryDays)
            return new BatchExpiryWarning(label, expiry, daysToExpiry, BatchExpiryKind.NearExpiry);
        return null;
    }

    // ------------------------------------------------------------------ internals

    /// <summary>The (godown, normalised-batch) buckets that carry any opening/movement for the item.</summary>
    private IEnumerable<(Guid GodownId, string Batch)> BucketsFor(Guid stockItemId)
    {
        var buckets = new HashSet<(Guid, string)>();

        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == stockItemId)
                buckets.Add((b.GodownId, Normalise(b.BatchLabel)));

        foreach (var v in _company.InventoryVouchers)
        {
            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
                if (a.StockItemId == stockItemId)
                    buckets.Add((a.GodownId, Normalise(a.BatchLabel)));
            foreach (var pl in v.PhysicalLines)
                if (pl.StockItemId == stockItemId)
                    buckets.Add((pl.GodownId, Normalise(pl.BatchLabel)));
        }

        foreach (var v in _company.Vouchers)
            foreach (var line in v.InventoryLines)
                if (line.StockItemId == stockItemId)
                    buckets.Add((line.GodownId, Normalise(line.BatchLabel)));

        // A batch master with a stated inward godown is a bucket even before its first movement (so the batch
        // is visible), keyed by its own number.
        foreach (var m in _company.BatchesFor(stockItemId))
            if (m.GodownId is { } gid)
                buckets.Add((gid, Normalise(m.BatchNumber)));

        return buckets;
    }

    /// <summary>
    /// The authoritative per-batch unit cost (DP-8): the batch master's inward rate if set; else the batch's
    /// most-recent rated inward movement (opening / receipt / purchase); else the item's running closing rate
    /// (closing value ÷ closing qty) so a batch is never valued at ₹0 while the item carries cost. Deterministic.
    /// </summary>
    private Money BatchUnitCost(Guid stockItemId, Guid godownId, string label, BatchMaster? master, DateOnly asOf)
    {
        if (master?.InwardRate is { } rate)
            return rate;

        // Most-recent rated inward for THIS (item, godown, batch) as of the date.
        if (LastRatedInwardRate(stockItemId, godownId, label, asOf) is { } inwardRate)
            return inwardRate;

        // Graceful fallback: the item's running closing unit rate (value ÷ qty), never a silent ₹0 when the
        // item as a whole has cost. Non-batch items with a single bucket land here and match the item engine.
        var closing = _valuation.ClosingValue(stockItemId, asOf);
        if (closing.Quantity > 0m)
            return new Money(closing.Value.Amount / closing.Quantity).RoundToPaisa();
        return Money.Zero;
    }

    /// <summary>The most-recent rated inward rate for an (item, godown, batch) as of a date, or <c>null</c>.</summary>
    private Money? LastRatedInwardRate(Guid stockItemId, Guid godownId, string label, DateOnly asOf)
    {
        Money? last = null;
        DateOnly bestDate = DateOnly.MinValue;
        var seenOpening = false;

        // Opening allocations are the earliest lots.
        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == stockItemId && b.GodownId == godownId && Normalise(b.BatchLabel) == label && b.Quantity > 0m)
            {
                last = b.Rate;
                seenOpening = true;
            }

        foreach (var v in _company.InventoryVouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || !type.AffectsStock) continue;
            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                if (a.StockItemId != stockItemId || a.GodownId != godownId || Normalise(a.BatchLabel) != label) continue;
                if (a.Direction != StockDirection.Inward || a.Rate is not { } r) continue;
                // The rate is PER THE LINE'S OWN UNIT; BatchOnHand.Quantity is base-normalised, so the unit
                // cost must be re-expressed per base unit or the batch value inflates by the conversion
                // factor ("2 Doz @ ₹10" would value 24 Nos at ₹240 instead of ₹20).
                if (!seenOpening || v.Date >= bestDate) { last = RateInBase(a, r); bestDate = v.Date; }
            }
        }

        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled || v.Optional || v.Date > asOf) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales)) continue;
            foreach (var line in v.InventoryLines)
            {
                if (line.StockItemId != stockItemId || line.GodownId != godownId || Normalise(line.BatchLabel) != label) continue;
                if (line.Direction != StockDirection.Inward) continue;
                if (!seenOpening || v.Date >= bestDate) { last = line.Rate; bestDate = v.Date; }
            }
        }

        return last;
    }

    /// <summary>
    /// An allocation's rate re-expressed per the item's BASE unit (WI-10 slice C; see
    /// <see cref="Unit.RateInBaseMeasure"/>). A line states its rate per the unit the LINE is in, so pairing it
    /// with a base-normalised quantity requires dividing by exactly the factor the quantity was multiplied by.
    /// Opening balances and item-invoice lines carry no line unit, so they are already per-base.
    /// </summary>
    private Money RateInBase(InventoryAllocation a, Money rate)
    {
        if (a.UnitId is not { } unitId) return rate;
        var unit = _company.FindUnit(unitId);
        return unit is null ? rate : new Money(unit.RateInBaseMeasure(rate.Amount));
    }

    /// <summary>Draws <paramref name="required"/> across the ordered buckets, min(remaining, bucket) each.</summary>
    private List<BatchIssue> DrawDown(IReadOnlyList<BatchOnHand> ordered, decimal required)
    {
        var plan = new List<BatchIssue>();
        var remaining = required;
        foreach (var b in ordered)
        {
            if (remaining <= 0m) break;
            var take = Math.Min(b.Quantity, remaining);
            if (take <= 0m) continue;
            plan.Add(new BatchIssue(b.GodownId, b.Batch, take, b.UnitCost, b.ExpiryDate));
            remaining -= take;
        }
        return plan;
    }

    /// <summary>
    /// The default ordering for issue/display (DP-1/RQ-5): <b>FEFO</b> (earliest resolved-expiry first; dated
    /// before undated) <b>only when the item uses expiry dates</b> (<see cref="StockItem.UseExpiryDates"/>);
    /// otherwise, and for batches with the same or no expiry, <b>FIFO-by-inward date</b>, then batch label for a
    /// stable, deterministic order. Gating FEFO on the item switch (not merely on whether a batch master happens
    /// to carry an expiry date) means a FIFO-mode item whose batches carry expiry dates still issues earliest-in
    /// first (A10 finding; DP-1 "FEFO WHEN THE ITEM USES EXPIRY, else FIFO-by-inward"). All buckets in one sort
    /// belong to a single item, so the switch is resolved once from the item id.
    /// </summary>
    private int CompareForIssue(BatchOnHand a, BatchOnHand b)
    {
        // FEFO applies ONLY when the item uses expiry dates (DP-1/RQ-5). Batch masters may carry an expiry date
        // even for a FIFO-mode item (RQ-2 switches are independent), so we gate on the item switch, not on the
        // presence of a date.
        if (ItemUsesExpiry(a.StockItemId))
        {
            // FEFO: a dated batch (soonest expiry) issues before a later/undated one.
            var ae = a.ExpiryDate;
            var be = b.ExpiryDate;
            if (ae is { } aed && be is { } bed)
            {
                var byExpiry = aed.CompareTo(bed);
                if (byExpiry != 0) return byExpiry;
            }
            else if (ae is not null) return -1; // a has expiry, b does not → a first
            else if (be is not null) return 1;
        }

        // FIFO (the default for non-expiry items, and the tie-break for FEFO): earliest inward date first.
        var byInward = FirstInwardDate(a).CompareTo(FirstInwardDate(b));
        if (byInward != 0) return byInward;

        return string.Compare(a.Batch, b.Batch, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the item tracks expiry dates (drives FEFO vs FIFO, DP-1/RQ-5).</summary>
    private bool ItemUsesExpiry(Guid stockItemId) =>
        _company.FindStockItem(stockItemId)?.UseExpiryDates ?? false;

    /// <summary>The earliest inward date for a bucket (opening = min-date), used as the FIFO key.</summary>
    private DateOnly FirstInwardDate(BatchOnHand bucket)
    {
        DateOnly? earliest = null;

        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == bucket.StockItemId && b.GodownId == bucket.GodownId &&
                Normalise(b.BatchLabel) == bucket.Batch && b.Quantity > 0m)
                return DateOnly.MinValue; // opening is the earliest lot

        foreach (var v in _company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
                if (a.StockItemId == bucket.StockItemId && a.GodownId == bucket.GodownId &&
                    Normalise(a.BatchLabel) == bucket.Batch && a.Direction == StockDirection.Inward)
                    earliest = earliest is { } e && e <= v.Date ? e : v.Date;
        }

        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled || v.Optional) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales)) continue;
            foreach (var line in v.InventoryLines)
                if (line.StockItemId == bucket.StockItemId && line.GodownId == bucket.GodownId &&
                    Normalise(line.BatchLabel) == bucket.Batch && line.Direction == StockDirection.Inward)
                    earliest = earliest is { } e && e <= v.Date ? e : v.Date;
        }

        return earliest ?? DateOnly.MaxValue;
    }

    private static string Normalise(string? batch) => string.IsNullOrWhiteSpace(batch) ? string.Empty : batch.Trim();
    private static string? NullIfEmpty(string label) => label.Length == 0 ? null : label;
}

/// <summary>
/// The on-hand of one (item, godown, batch) bucket as of a date (RQ-5): its resolved mfg/expiry (from the
/// batch master when one exists), quantity in the item base unit, and the batch's authoritative unit cost
/// (DP-8). The empty <see cref="Batch"/> is the item's non-batch stock.
/// </summary>
public sealed record BatchOnHand(
    Guid StockItemId,
    Guid GodownId,
    string Batch,
    Guid? BatchMasterId,
    DateOnly? ManufacturingDate,
    DateOnly? ExpiryDate,
    decimal Quantity,
    Money UnitCost)
{
    /// <summary>The paisa-exact value of this bucket = <see cref="Quantity"/> × <see cref="UnitCost"/> (DP-8).</summary>
    public Money Value => Money.ForexBase(UnitCost, Quantity);
}

/// <summary>
/// One line of a batch issue plan (DP-1): the quantity to draw from a (godown, batch), its per-unit cost
/// (DP-8) and the batch's resolved expiry (so a warn-not-block signal can be raised, RQ-7).
/// </summary>
public sealed record BatchIssue(
    Guid GodownId,
    string Batch,
    decimal Quantity,
    Money UnitCost,
    DateOnly? ExpiryDate)
{
    /// <summary>The paisa-exact value of this issue line = <see cref="Quantity"/> × <see cref="UnitCost"/>.</summary>
    public Money Value => Money.ForexBase(UnitCost, Quantity);
}

/// <summary>Whether a batch selection is flagged as expired or merely near expiry (RQ-7).</summary>
public enum BatchExpiryKind
{
    /// <summary>The batch's resolved expiry is strictly before the selection date.</summary>
    Expired = 0,

    /// <summary>The batch expires within the near-expiry window (but has not yet expired).</summary>
    NearExpiry = 1,
}

/// <summary>
/// A non-blocking expiry warning for a batch selection (RQ-7, warn-not-block): the batch, its resolved expiry,
/// the whole-day gap to expiry (negative once past), and whether it is expired or near-expiry. Advisory only —
/// the engine never blocks the issue on it.
/// </summary>
public sealed record BatchExpiryWarning(
    string Batch,
    DateOnly ExpiryDate,
    int DaysToExpiry,
    BatchExpiryKind Kind);
