using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The stock-valuation engine (catalog §9 clone-note; phase3-inventory-requirements RQ-21..RQ-27,
/// ER-2/ER-3/ER-4; slice 3.3a). A <b>pure, deterministic, paisa-exact</b> projection that computes an
/// item's <b>closing value</b> from its movement history under the item's own
/// <see cref="StockValuationMethod"/>. It reuses <see cref="InventoryLedger"/>'s movement enumeration and
/// its exact as-of / post-dated / cancelled conventions, so on-hand <i>quantity</i> and on-hand
/// <i>value</i> always agree.
/// <para><b>Averaging approach.</b> Average Cost is a <b>perpetual moving average</b>: the running
/// weighted-average unit cost is recomputed after each inward lot, and the closing value is
/// <c>closing quantity × running average</c>, snapped to the paisa.</para>
/// <para><b>Cost layers.</b> FIFO/LIFO track cost layers (a queue/stack of (qty, unit-cost) lots): an
/// inward pushes a layer; an outward consumes the oldest (FIFO) or newest (LIFO) layers; the closing value
/// is Σ of the surviving layers, snapped to the paisa.</para>
/// <para><b>No-rate inward fallback (best-available-cost chain).</b> An inward line with <c>Rate == null</c>
/// (e.g. a Stock-Journal destination that carries no source cost, or an unrated first inward) is costed at
/// the first of these that yields a positive rate — never a crash, never a non-paisa value, never a silent
/// ₹0 for real units when any cost signal exists:
/// <list type="number">
///   <item>the current running average unit cost, if &gt; 0;</item>
///   <item>else the item's <see cref="StockItem.StandardCost"/>, if set;</item>
///   <item>else the item's most-recent <i>rated</i> inward/purchase rate anywhere in its movement history;</item>
///   <item>else 0 as a last resort (genuinely no cost signal — documented).</item>
/// </list>
/// The chain is deterministic and paisa-exact, and keeps a zero-value inward from silently deflating the
/// average or valuing real stock at nothing.</para>
/// <para><b>Standard cost.</b> <see cref="StockValuationMethod.StandardCost"/> values closing stock at the
/// item's <see cref="StockItem.StandardCost"/>; when that is unset it falls back to the last purchase cost.</para>
/// <para><b>Last Purchase / Last Sale graceful fallback.</b> <see cref="StockValuationMethod.LastPurchaseCost"/>
/// with no rated purchase falls back to running average → StandardCost → last rated inward → 0;
/// <see cref="StockValuationMethod.LastSaleCost"/> with no rated sale falls back to last purchase → running
/// average → StandardCost → 0. So real closing stock is never valued at ₹0 merely because the item was never
/// sold (or never had a rated purchase). Deterministic and paisa-exact.</para>
/// <para><b>Accounts↔inventory reconciliation precondition (RQ-25/RQ-26; BR-1).</b> Derived-closing-stock
/// reporting (<c>ClosingStockMode.InventoryDerived</c> in <see cref="Reports.ProfitAndLoss"/> /
/// <see cref="Reports.BalanceSheet"/>) assumes (a) every stock inward is paired with an accounting posting
/// (Purchases/Creditor, or a stock-journal cost transfer), and (b) opening inventory is booked to a
/// Stock-in-Hand ledger whose opening value matches the derived opening stock. When this invariant is
/// violated the statements still <i>balance</i>, but the imbalance can mask phantom profit. The invariant is
/// enforced by the item-invoice voucher (both arms posted atomically) and by fixtures like "Bright" booking
/// opening stock to Stock-in-Hand. This engine does NOT add runtime guards for it — it is a data-entry/posting
/// invariant, documented here for report readers.</para>
/// <para><b>Physical Stock (DP-3).</b> A counted quantity reconciles the layer stack to the counted total:
/// a downward count consumes layers by the item's method; an upward count adds a layer at the running
/// average, mirroring how <see cref="InventoryLedger"/> checkpoints the running quantity.</para>
/// No Avalonia/DB/clock/RNG dependency — unit-tested exactly like the accounting core.
/// </summary>
public sealed class StockValuationService
{
    private readonly Company _company;
    private readonly InventoryLedger _onHand;

    public StockValuationService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _onHand = new InventoryLedger(company);
    }

    /// <summary>
    /// The closing quantity + paisa-exact closing <b>value</b> for an item as of a date, under the item's
    /// <see cref="StockItem.ValuationMethod"/>. Returns <see cref="StockClosingValuation.Zero"/> when the
    /// item has no on-hand stock (value is exactly ₹0).
    /// </summary>
    public StockClosingValuation ClosingValue(Guid stockItemId, DateOnly asOf)
    {
        var item = _company.FindStockItem(stockItemId)
            ?? throw new InvalidOperationException($"Stock item {stockItemId} not found.");

        var events = MovementEvents(stockItemId, asOf);
        var closingQty = RoundQty(_onHand.OnHand(stockItemId, asOf));
        if (closingQty <= 0m)
            return StockClosingValuation.Zero;

        var cost = CostContext.For(item, events);

        var value = item.ValuationMethod switch
        {
            StockValuationMethod.AverageCost => AverageValue(events, closingQty, cost),
            StockValuationMethod.Fifo => LayerValue(events, closingQty, lifo: false, cost),
            StockValuationMethod.Lifo => LayerValue(events, closingQty, lifo: true, cost),
            StockValuationMethod.LastPurchaseCost => FlatValue(closingQty, LastPurchaseRate(events, cost)),
            StockValuationMethod.LastSaleCost => FlatValue(closingQty, LastSaleRate(events, cost)),
            StockValuationMethod.StandardCost => FlatValue(closingQty,
                item.StandardCost?.Amount ?? LastPurchaseRate(events, cost)),
            _ => AverageValue(events, closingQty, cost),
        };
        return new StockClosingValuation(closingQty, value);
    }

    /// <summary>
    /// The aggregate closing stock value across <b>every</b> stock item as of a date — Σ of each item's
    /// <see cref="ClosingValue"/> computed by its own method (RQ-25). This is the derived Stock-in-Hand
    /// figure when accounts &amp; inventory are integrated; paisa-exact (Σ of paisa-exact values).
    /// </summary>
    public Money TotalClosingStockValue(DateOnly asOf)
    {
        var total = Money.Zero;
        foreach (var item in _company.StockItems)
            total += ClosingValue(item.Id, asOf).Value;
        return total;
    }

    /// <summary>
    /// The paisa-exact stock value that issuing <paramref name="quantity"/> of an item as of a date would remove
    /// under its own valuation method: for FIFO/LIFO the actual cost of the layers the outward drains (the engine
    /// ignores an outward line's own rate and consumes layers), else quantity × the item's running/closing unit
    /// rate. A Manufacturing Journal values each non-batch component consumption through this so the finished good
    /// absorbs exactly what the component's stock loses — never phantom stock value with no counter-entry (PR-4).
    /// Deterministic and paisa-exact; returns ₹0 for a non-positive quantity.
    /// </summary>
    public Money IssueValue(Guid stockItemId, decimal quantity, DateOnly asOf)
    {
        var item = _company.FindStockItem(stockItemId)
            ?? throw new InvalidOperationException($"Stock item {stockItemId} not found.");
        if (quantity <= 0m) return Money.Zero;

        if (item.ValuationMethod is not (StockValuationMethod.Fifo or StockValuationMethod.Lifo))
        {
            // Average/flat methods: an outward at the running/closing unit rate reduces stock by qty × that rate.
            var closing = ClosingValue(stockItemId, asOf);
            var rate = closing.Quantity > 0m
                ? new Money(closing.Value.Amount / closing.Quantity).RoundToPaisa()
                : item.StandardCost ?? Money.Zero;
            return Money.ForexBase(rate, quantity);
        }

        var events = MovementEvents(stockItemId, asOf);
        var cost = CostContext.For(item, events);
        var layers = BuildLayers(events, lifo: item.ValuationMethod == StockValuationMethod.Lifo, cost);

        var remaining = quantity;
        var consumed = 0m;
        while (remaining > 0m && layers.Count > 0)
        {
            var idx = item.ValuationMethod == StockValuationMethod.Lifo ? layers.Count - 1 : 0;
            var layer = layers[idx];
            var take = Math.Min(layer.Quantity, remaining);
            consumed += take * layer.UnitCost;
            remaining -= take;
            if (take >= layer.Quantity) layers.RemoveAt(idx);
            else layers[idx] = new Layer(layer.Quantity - take, layer.UnitCost);
        }
        return new Money(consumed).RoundToPaisa();
    }

    // ------------------------------------------------------------------ movement enumeration

    /// <summary>
    /// The item's chronological inward/outward events as of <paramref name="asOf"/>: opening allocations
    /// first (earliest cost lots), then stock-affecting <see cref="InventoryVoucher"/> lines AND item-invoice
    /// (Purchase/Sales) stock lines merged in the same deterministic order <see cref="InventoryLedger"/> replays
    /// (by date; Physical-Stock last within a date; then number/id). Only stock-affecting, non-cancelled,
    /// in-date vouchers contribute (post-dated excluded until due). Quantities are normalised to the item's base
    /// unit (DP-6).
    /// </summary>
    private List<MovementEvent> MovementEvents(Guid stockItemId, DateOnly asOf)
    {
        // Opening balances are the earliest inward lots (each carries its own rate). They sort before every
        // dated voucher via a min-date key.
        var tagged = new List<(DateOnly Date, int PhysicalLast, int Number, Guid Id, MovementEvent Event)>();
        var minDate = DateOnly.MinValue;
        foreach (var b in _company.StockOpeningBalances)
            if (b.StockItemId == stockItemId && b.Quantity > 0m)
                tagged.Add((minDate, 0, int.MinValue, Guid.Empty, MovementEvent.Inward(b.Quantity, b.Rate.Amount)));

        foreach (var v in OrderedInventoryVouchers())
        {
            if (!Counts(v, asOf)) continue;
            var physicalLast = IsPhysicalStock(v) ? 1 : 0;

            // A Physical-Stock count reconciles the running quantity to the counted total (DP-3).
            if (IsPhysicalStock(v))
            {
                foreach (var pl in v.PhysicalLines)
                    if (pl.StockItemId == stockItemId)
                        tagged.Add((v.Date, physicalLast, v.Number, v.Id, MovementEvent.Count(pl.CountedQuantity)));
                continue;
            }

            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                if (a.StockItemId != stockItemId) continue;
                var qty = QuantityInBase(a);
                tagged.Add((v.Date, physicalLast, v.Number, v.Id, a.Direction == StockDirection.Inward
                    ? MovementEvent.Inward(qty, a.Rate?.Amount)
                    : MovementEvent.Outward(qty, a.Rate?.Amount)));
            }
        }

        // Item-invoice (Purchase/Sales) stock lines: merged as non-Physical movements at their voucher's
        // (date, number, id) so they interleave with pure-stock movements and sit before any same-date count.
        foreach (var m in ItemInvoiceStock.Movements(_company, asOf))
        {
            var a = m.Allocation;
            if (a.StockItemId != stockItemId) continue;
            tagged.Add((m.Date, 0, m.Number, m.VoucherId, a.Direction == StockDirection.Inward
                ? MovementEvent.Inward(a.Quantity, a.Rate?.Amount)
                : MovementEvent.Outward(a.Quantity, a.Rate?.Amount)));
        }

        return tagged
            .OrderBy(t => t.Date).ThenBy(t => t.PhysicalLast).ThenBy(t => t.Number).ThenBy(t => t.Id)
            .Select(t => t.Event)
            .ToList();
    }

    // ------------------------------------------------------------------ per-method valuation

    /// <summary>Perpetual moving-average closing value = closing qty × running weighted-average unit cost.</summary>
    private static Money AverageValue(IReadOnlyList<MovementEvent> events, decimal closingQty, CostContext cost)
    {
        var (avg, _) = RunAverage(events, cost);
        return Money.ForexBase(new Money(avg), closingQty);
    }

    /// <summary>
    /// FIFO/LIFO closing value = Σ of the surviving cost layers after consuming outwards by the chosen
    /// order. A no-rate inward is costed via the best-available-cost chain at that point (running average →
    /// standard → last rated inward → 0); a Physical-Stock count reconciles the layer stack to the counted total.
    /// </summary>
    private static Money LayerValue(IReadOnlyList<MovementEvent> events, decimal closingQty, bool lifo, CostContext cost)
    {
        var layers = BuildLayers(events, lifo, cost);

        // Value the surviving layers (their quantities already sum to the closing quantity).
        var value = 0m;
        foreach (var l in layers)
            value += l.Quantity * l.UnitCost;
        _ = closingQty; // layers already reconcile to closing qty via the same movement set
        return new Money(value).RoundToPaisa();
    }

    /// <summary>
    /// Replays the movement history into the surviving FIFO/LIFO cost layers as of the events' cut-off: an inward
    /// pushes a layer (no-rate lots costed via the best-available-cost chain), an outward consumes the oldest
    /// (FIFO) or newest (LIFO) layers, a Physical-Stock count reconciles the stack to the counted total. The
    /// resulting layers back both closing valuation (<see cref="LayerValue"/>) and issue costing
    /// (<see cref="IssueValue"/>), so quantity and value stay in lock-step.
    /// </summary>
    private static List<Layer> BuildLayers(IReadOnlyList<MovementEvent> events, bool lifo, CostContext cost)
    {
        var layers = new List<Layer>(); // oldest-first
        var runningQty = 0m;
        var runningCost = 0m;

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case MovementKind.Inward:
                {
                    var unit = e.Rate ?? cost.NoRateInwardCost(RunningAverage(runningQty, runningCost));
                    layers.Add(new Layer(e.Quantity, unit));
                    runningQty += e.Quantity;
                    runningCost += e.Quantity * unit;
                    break;
                }
                case MovementKind.Outward:
                {
                    Consume(layers, e.Quantity, lifo, ref runningQty, ref runningCost);
                    break;
                }
                case MovementKind.Count:
                {
                    var current = SumQty(layers);
                    if (e.Quantity < current)
                    {
                        Consume(layers, current - e.Quantity, lifo, ref runningQty, ref runningCost);
                    }
                    else if (e.Quantity > current)
                    {
                        var unit = RunningAverage(runningQty, runningCost);
                        var add = e.Quantity - current;
                        layers.Add(new Layer(add, unit));
                        runningQty += add;
                        runningCost += add * unit;
                    }
                    break;
                }
            }
        }

        return layers;
    }

    /// <summary>Flat closing value = closing qty × a single rate (Last Purchase / Last Sale / Standard).</summary>
    private static Money FlatValue(decimal closingQty, decimal unitRate)
        => Money.ForexBase(new Money(unitRate), closingQty);

    // ------------------------------------------------------------------ helpers

    /// <summary>The running weighted-average unit cost and total quantity after replaying every inward
    /// (net of outwards, which leave the average unchanged) — a no-rate inward carries the best-available cost
    /// (running average → standard → last rated inward → 0).</summary>
    private static (decimal Average, decimal Quantity) RunAverage(IReadOnlyList<MovementEvent> events, CostContext ctx)
    {
        var qty = 0m;
        var cost = 0m;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case MovementKind.Inward:
                {
                    var unit = e.Rate ?? ctx.NoRateInwardCost(RunningAverage(qty, cost));
                    qty += e.Quantity;
                    cost += e.Quantity * unit;
                    break;
                }
                case MovementKind.Outward:
                {
                    // Outward at the running average leaves the average unchanged.
                    var unit = RunningAverage(qty, cost);
                    qty -= e.Quantity;
                    cost -= e.Quantity * unit;
                    if (qty <= 0m) { qty = 0m; cost = 0m; }
                    break;
                }
                case MovementKind.Count:
                {
                    var unit = RunningAverage(qty, cost);
                    cost = e.Quantity * unit;
                    qty = e.Quantity;
                    break;
                }
            }
        }
        return (RunningAverage(qty, cost), qty);
    }

    private static decimal RunningAverage(decimal qty, decimal cost)
        => qty > 0m ? cost / qty : 0m;

    private static void Consume(List<Layer> layers, decimal quantity, bool lifo,
        ref decimal runningQty, ref decimal runningCost)
    {
        var remaining = quantity;
        while (remaining > 0m && layers.Count > 0)
        {
            var idx = lifo ? layers.Count - 1 : 0;
            var layer = layers[idx];
            var take = Math.Min(layer.Quantity, remaining);
            runningQty -= take;
            runningCost -= take * layer.UnitCost;
            remaining -= take;
            if (take >= layer.Quantity) layers.RemoveAt(idx);
            else layers[idx] = new Layer(layer.Quantity - take, layer.UnitCost);
        }
        if (runningQty <= 0m) { runningQty = 0m; runningCost = 0m; }
    }

    private static decimal SumQty(IEnumerable<Layer> layers)
    {
        var q = 0m;
        foreach (var l in layers) q += l.Quantity;
        return q;
    }

    /// <summary>
    /// The Last-Purchase-Cost rate with graceful fallback (never a silent ₹0 for real stock): the most-recent
    /// <i>rated</i> inward rate if the item was ever purchased at a rate; else running average → StandardCost →
    /// last rated inward (already none here) → 0. The running average itself already absorbs no-rate inwards
    /// via the best-available-cost chain, so an item whose only inward was unrated still values at that chain's
    /// cost, not 0.
    /// </summary>
    private static decimal LastPurchaseRate(IReadOnlyList<MovementEvent> events, CostContext cost)
    {
        if (RawLastRatedInwardRate(events) is { } rated)
            return rated;
        var (avg, _) = RunAverage(events, cost);
        if (avg > 0m) return avg;
        return cost.StandardCost ?? cost.LastRatedInwardRate ?? 0m;
    }

    /// <summary>
    /// The Last-Sale-Cost rate with graceful fallback (never a silent ₹0 for real stock): the most-recent
    /// <i>rated</i> outward (sale) rate if the item was ever sold at a rate; else Last Purchase → running
    /// average → StandardCost → 0. So closing stock of a never-sold item values at its purchase/average cost,
    /// not nothing.
    /// </summary>
    private static decimal LastSaleRate(IReadOnlyList<MovementEvent> events, CostContext cost)
    {
        decimal? lastSale = null;
        foreach (var e in events)
            if (e.Kind == MovementKind.Outward && e.Rate is { } r)
                lastSale = r;
        if (lastSale is { } s) return s;
        return LastPurchaseRate(events, cost); // Last Purchase → average → standard → 0
    }

    /// <summary>The most-recent inward that carried an explicit rate, or <c>null</c> when no inward was rated
    /// (every inward was a no-rate stock-journal/opening-less line). This is the raw "last rated purchase"
    /// signal that feeds both the no-rate-inward fallback chain and <see cref="LastPurchaseRate"/>.</summary>
    private static decimal? RawLastRatedInwardRate(IReadOnlyList<MovementEvent> events)
    {
        decimal? last = null;
        foreach (var e in events)
            if (e.Kind == MovementKind.Inward && e.Rate is { } r)
                last = r;
        return last;
    }

    private decimal QuantityInBase(InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = _company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }

    private static decimal RoundQty(decimal q) => Math.Round(q, Quantities.DecimalPlaces, MidpointRounding.AwayFromZero);

    // Mirror InventoryLedger's ordering + counting so quantity and value agree exactly.

    private IEnumerable<InventoryVoucher> OrderedInventoryVouchers()
        => _company.InventoryVouchers
            .OrderBy(v => v.Date)
            .ThenBy(v => IsPhysicalStock(v) ? 1 : 0)
            .ThenBy(v => v.Number)
            .ThenBy(v => v.Id);

    private bool IsPhysicalStock(InventoryVoucher v)
        => _company.FindVoucherType(v.TypeId)?.BaseType == VoucherBaseType.PhysicalStock;

    private bool Counts(InventoryVoucher v, DateOnly asOf)
    {
        if (v.Cancelled) return false;
        if (v.Date > asOf) return false;
        var type = _company.FindVoucherType(v.TypeId);
        return type is not null && type.AffectsStock;
    }

    // ------------------------------------------------------------------ internal value types

    /// <summary>
    /// The per-item cost fallbacks used when a movement carries no explicit rate: the item's
    /// <see cref="StockItem.StandardCost"/> (if set) and its most-recent <i>rated</i> inward rate anywhere in
    /// history (if any). <see cref="NoRateInwardCost"/> applies the best-available-cost chain
    /// (running-average → standard → last-rated-inward → 0) for a no-rate inward lot.
    /// </summary>
    private readonly record struct CostContext(decimal? StandardCost, decimal? LastRatedInwardRate)
    {
        public static CostContext For(StockItem item, IReadOnlyList<MovementEvent> events)
            => new(item.StandardCost is { } sc && sc.Amount > 0m ? sc.Amount : null,
                   RawLastRatedInwardRate(events));

        /// <summary>The unit cost for a no-rate inward: the running average if positive, else the item's
        /// standard cost, else the last rated inward rate, else 0 (documented last resort).</summary>
        public decimal NoRateInwardCost(decimal runningAverage)
        {
            if (runningAverage > 0m) return runningAverage;
            if (StandardCost is { } sc) return sc;
            if (LastRatedInwardRate is { } r) return r;
            return 0m;
        }
    }

    private readonly record struct Layer(decimal Quantity, decimal UnitCost);

    private enum MovementKind { Inward, Outward, Count }

    private readonly record struct MovementEvent(MovementKind Kind, decimal Quantity, decimal? Rate)
    {
        public static MovementEvent Inward(decimal qty, decimal? rate) => new(MovementKind.Inward, qty, rate);
        public static MovementEvent Outward(decimal qty, decimal? rate) => new(MovementKind.Outward, qty, rate);
        public static MovementEvent Count(decimal qty) => new(MovementKind.Count, qty, null);
    }
}

/// <summary>
/// An item's closing stock valuation as of a date: the closing quantity (item base unit) and its
/// paisa-exact closing <see cref="Value"/> under the item's valuation method.
/// </summary>
public readonly record struct StockClosingValuation(decimal Quantity, Money Value)
{
    /// <summary>Zero stock, zero value.</summary>
    public static readonly StockClosingValuation Zero = new(0m, Money.Zero);
}
