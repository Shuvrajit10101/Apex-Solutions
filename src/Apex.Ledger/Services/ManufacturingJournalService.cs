using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Manufacturing Journal</b> posting service (Phase 6 Cluster 2; requirements RQ-11..RQ-15, DP-3, PR-4).
/// It explodes a <see cref="BillOfMaterials"/> for a manufacture of <c>N</c> finished units, values the finished
/// good = Σ component cost + Σ additional cost − Σ by-product/co-product/scrap carve-out value (RQ-13/DP-3), and
/// posts a single <b>Stock Journal</b> (base type with <see cref="VoucherType.UseAsManufacturingJournal"/> on,
/// RQ-11) whose <b>source</b> lines consume the scaled components (outward, RQ-12) and whose <b>destination</b>
/// lines produce the finished good + the carved-out by-products/scrap (inward). It routes through the same
/// <see cref="InventoryPostingService"/> + guards the keyboard UI uses (ER-7): the no-negative-stock guard
/// blocks a manufacture short of components, exactly like a Delivery Note.
/// <para><b>Additional cost is a STOCK cost, not a P&amp;L expense (RQ-13).</b> A Manufacturing Journal is a
/// pure inventory voucher (<see cref="VoucherType.AffectsAccounts"/> is <c>false</c>), so it books no
/// double-entry. Additional-cost lines (labour/overhead/freight) therefore add to the finished good's stock
/// value only — they never hit the Profit &amp; Loss at manufacture. The by-product/co-product/scrap value is
/// <b>carved out</b> of the main finished good's cost (DP-3) and added to those items' stock at the carve-out
/// value.</para>
/// <para><b>Component cost is batch-aware where the component is batch-tracked (RQ-13/DP-8, ER-5).</b> Each
/// consumed component is valued at its live per-unit cost from the existing valuation engine — via
/// <see cref="BatchStockService"/> when the item is maintained in batches (per-lot inward rate, FEFO/FIFO
/// selection), else via <see cref="StockValuationService"/>'s running/closing unit rate under the item's own
/// method. No parallel costing engine is introduced.</para>
/// <para><b>Exact, deterministic, paisa-reconciling (ER-2/ER-10).</b> Every figure is exact decimal rupees; the
/// finished good's per-unit rate is <c>finished-good value ÷ N</c> snapped to the paisa (the same per-unit
/// rounding every rate-based line in the system uses), so Stock Summary and Balance-Sheet Stock-in-Hand foot to
/// the paisa (PR-4). No Avalonia/DB/clock/RNG dependency — unit-tested exactly like the accounting core.</para>
/// </summary>
public sealed class ManufacturingJournalService
{
    private readonly Company _company;
    private readonly InventoryPostingService _posting;
    private readonly StockValuationService _valuation;
    private readonly BatchStockService _batches;

    public ManufacturingJournalService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _posting = new InventoryPostingService(company);
        _valuation = new StockValuationService(company);
        _batches = new BatchStockService(company);
    }

    /// <summary>
    /// Creates a user-defined <b>Manufacturing Journal</b> voucher type (RQ-11): a <see cref="VoucherType"/>
    /// whose base is <see cref="VoucherBaseType.StockJournal"/> with <see cref="VoucherType.UseAsManufacturingJournal"/>
    /// on. It is NOT one of the 24 predefined seeds — the user creates it. Any other base type is rejected (a
    /// manufacturing journal is always a Stock Journal, RQ-11). The type is registered on the company and
    /// returned.
    /// </summary>
    public VoucherType CreateManufacturingJournalType(
        string name,
        VoucherBaseType baseType = VoucherBaseType.StockJournal,
        string? shortcut = "Alt+F7")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("A voucher type name is required.");
        if (baseType != VoucherBaseType.StockJournal)
            throw new InvalidOperationException(
                "A Manufacturing Journal must derive from the Stock Journal base type (RQ-11).");
        if (_company.VoucherTypes.Any(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A voucher type named '{name.Trim()}' already exists.");

        var type = new VoucherType(
            Guid.NewGuid(),
            name.Trim(),
            VoucherBaseType.StockJournal,
            NumberingMethod.Automatic,
            shortcut,
            abbreviation: "Mfg",
            isActive: true,
            isPredefined: false,
            useAsManufacturingJournal: true);
        _company.AddVoucherType(type);
        return type;
    }

    /// <summary>
    /// Manufactures <paramref name="quantity"/> finished units from <paramref name="bomId"/> through
    /// <paramref name="manufacturingJournalTypeId"/> (a Manufacturing-Journal type, RQ-11), posting one Stock
    /// Journal that consumes the scaled components (RQ-12) and produces the finished good + carve-outs, valued
    /// per RQ-13/DP-3. Components are consumed from <paramref name="consumptionGodownId"/> (a BOM line may pin
    /// its own godown, which wins); the finished good + carve-outs are produced into
    /// <paramref name="productionGodownId"/> (RQ-14). Additional-cost lines add to the finished-good stock value
    /// (RQ-13). Returns the <see cref="ManufacturingResult"/> (the posted voucher + the reconciled figures).
    /// Throws (and persists nothing) on an unknown/mismatched type or BOM, a non-positive quantity, or a
    /// no-negative-stock violation (ER-7).
    /// </summary>
    public ManufacturingResult Manufacture(
        Guid manufacturingJournalTypeId,
        Guid bomId,
        decimal quantity,
        DateOnly date,
        Guid consumptionGodownId,
        Guid productionGodownId,
        IReadOnlyList<ManufacturingAdditionalCost>? additionalCosts = null)
    {
        var plan = PlanManufacture(
            manufacturingJournalTypeId, bomId, quantity, date,
            consumptionGodownId, productionGodownId, additionalCosts);

        var posted = _posting.Post(plan.Voucher);

        return new ManufacturingResult(
            posted, bomId, quantity,
            plan.Result.ComponentCostTotal, plan.Result.AdditionalCostTotal, plan.Result.CarveOutTotal,
            plan.Result.FinishedGoodValue, plan.Result.FinishedGoodUnitRate);
    }

    /// <summary>
    /// Computes the manufacture <b>without posting</b> (Phase 6 Cluster 2; RQ-12/RQ-13, ER-4): validates the
    /// inputs, explodes the BOM by the output quantity (auto-scaled consumption, RQ-12), values the finished
    /// good = Σ component cost + Σ additional cost − Σ carve-outs (RQ-13/DP-3), and returns the same
    /// <see cref="ManufacturingResult"/> figures a real <see cref="Manufacture"/> would post (the voucher on the
    /// result carries the yet-to-be-posted lines, so its <c>Number</c> is 0). This is the single source of the
    /// on-screen breakdown, so the screen never recomputes valuation (ER-4). Throws (persisting nothing) on the
    /// same invalid-input conditions as <see cref="Manufacture"/>.
    /// </summary>
    public ManufacturingResult PreviewManufacture(
        Guid manufacturingJournalTypeId,
        Guid bomId,
        decimal quantity,
        DateOnly date,
        Guid consumptionGodownId,
        Guid productionGodownId,
        IReadOnlyList<ManufacturingAdditionalCost>? additionalCosts = null)
    {
        var plan = PlanManufacture(
            manufacturingJournalTypeId, bomId, quantity, date,
            consumptionGodownId, productionGodownId, additionalCosts);
        return plan.Result;
    }

    /// <summary>
    /// The shared plan-then-post core: builds the Stock-Journal voucher (source consumption + destination
    /// production/carve-outs) and the reconciled figures for a manufacture, WITHOUT touching the ledger. Both
    /// <see cref="Manufacture"/> (which posts <see cref="ManufacturePlan.Voucher"/>) and
    /// <see cref="PreviewManufacture"/> (which reads <see cref="ManufacturePlan.Result"/>) route through here, so
    /// the on-screen preview and the posted voucher are computed by the SAME code (ER-4).
    /// </summary>
    private ManufacturePlan PlanManufacture(
        Guid manufacturingJournalTypeId,
        Guid bomId,
        decimal quantity,
        DateOnly date,
        Guid consumptionGodownId,
        Guid productionGodownId,
        IReadOnlyList<ManufacturingAdditionalCost>? additionalCosts)
    {
        if (quantity <= 0m)
            throw new ArgumentException("Manufacture quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Manufacture quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");

        var type = _company.FindVoucherType(manufacturingJournalTypeId)
            ?? throw new InvalidOperationException($"Unknown voucher type {manufacturingJournalTypeId}.");
        if (!type.IsManufacturingJournal)
            throw new InvalidOperationException(
                $"Voucher type '{type.Name}' is not a Manufacturing Journal (base Stock Journal + Use as " +
                "Manufacturing Journal = Yes required, RQ-11).");

        var bom = _company.FindBillOfMaterials(bomId)
            ?? throw new InvalidOperationException($"BOM {bomId} not found.");
        if (_company.FindStockItem(bom.StockItemId) is null)
            throw new InvalidOperationException($"BOM finished-good item {bom.StockItemId} not found.");
        if (_company.FindGodown(consumptionGodownId) is null)
            throw new InvalidOperationException($"Consumption godown {consumptionGodownId} not found.");
        if (_company.FindGodown(productionGodownId) is null)
            throw new InvalidOperationException($"Production godown {productionGodownId} not found.");

        // Blocks per finished unit: producing N units from a BOM stated per `UnitOfManufacture` block runs
        // (N ÷ unit-of-manufacture) blocks; a component's consumed qty = QuantityPerBlock × blocks (RQ-12).
        // A unit-of-manufacture > 1 therefore divides correctly — the classic off-by-scale guard.
        var blocks = quantity / bom.UnitOfManufacture;

        // ---- source: consume the scaled components; sum their live cost (batch-aware where tracked) ----
        var source = new List<InventoryAllocation>();
        var componentCost = Money.Zero;
        foreach (var line in bom.ComponentLines)
        {
            var consumedQty = RoundQty(line.QuantityPerBlock * blocks);
            if (consumedQty <= 0m) continue;
            var godown = line.GodownId ?? consumptionGodownId;

            var lineValue = AppendComponentConsumption(source, line.ComponentStockItemId, godown, consumedQty, date);
            componentCost += lineValue;
        }

        // ---- additional cost (labour/overhead/freight) — adds to FG stock value, never P&L (RQ-13) ----
        var additionalCostTotal = Money.Zero;
        if (additionalCosts is not null)
            foreach (var ac in additionalCosts)
            {
                if (ac.Amount.Amount < 0m)
                    throw new InvalidOperationException(
                        $"Additional cost '{ac.Name}' must be ≥ 0.");
                if (!ac.Amount.IsPaisaExact)
                    throw new InvalidOperationException(
                        $"Additional cost '{ac.Name}' {ac.Amount} must be to the paisa (2 decimal places).");
                additionalCostTotal += ac.Amount;
            }

        // Pre-carve cost is what the finished good would cost before by-products/scrap are carved out (DP-3).
        var preCarveCost = componentCost + additionalCostTotal;

        // ---- destination: carve out by-products/co-products/scrap, then produce the finished good ----
        var destination = new List<InventoryAllocation>();
        var carveOutTotal = Money.Zero;
        foreach (var line in bom.CarveOutLines)
        {
            var producedQty = RoundQty(line.QuantityPerBlock * blocks);
            if (producedQty <= 0m) continue;
            var godown = line.GodownId ?? productionGodownId;

            var (unitRate, lineValue) = CarveOutValue(line, producedQty, preCarveCost);
            carveOutTotal += lineValue;
            destination.Add(new InventoryAllocation(
                line.ComponentStockItemId, godown, producedQty, StockDirection.Inward, unitRate));
        }

        // Finished-good value = Σ components + Σ additional − Σ carve-outs (RQ-13). Per-unit rate is the value
        // spread over the produced units, snapped to the paisa (the system-wide per-unit rounding, ER-2).
        var finishedGoodValue = preCarveCost - carveOutTotal;
        if (finishedGoodValue.Amount < 0m)
            throw new InvalidOperationException(
                $"Finished-good value {finishedGoodValue} is negative — the carve-outs exceed component + " +
                "additional cost. Check the by-product/co-product/scrap rates (DP-3).");
        var fgUnitRate = new Money(finishedGoodValue.Amount / quantity).RoundToPaisa();

        // Book the FG inward so the STOCK VALUE it carries equals finishedGoodValue TO THE PAISA (PR-4/ER-2). A
        // single line at fgUnitRate books round(fgUnitRate × N), which loses the ≤ half-paisa-per-unit remainder
        // whenever value ÷ N does not divide evenly (A10 finding #2). We therefore split off a one-unit
        // correction line carrying the exact remainder so Σ line values = finishedGoodValue — the DP-2
        // remainder-to-last-line technique. The lines still sum to exactly N units, so on-hand is unchanged.
        foreach (var fg in FinishedGoodLines(bom.StockItemId, productionGodownId, quantity, finishedGoodValue, fgUnitRate)
                     .Reverse())
            destination.Insert(0, fg);

        var voucher = InventoryVoucher.StockJournal(
            Guid.NewGuid(), type.Id, date, source, destination,
            narration: $"Manufacture {quantity} × '{_company.FindStockItem(bom.StockItemId)!.Name}' (BOM '{bom.Name}').");

        var result = new ManufacturingResult(
            voucher, bom.Id, quantity,
            componentCost, additionalCostTotal, carveOutTotal, finishedGoodValue, fgUnitRate);
        return new ManufacturePlan(voucher, result);
    }

    /// <summary>An un-posted manufacture: the Stock-Journal voucher ready to post and the reconciled figures.</summary>
    private readonly record struct ManufacturePlan(InventoryVoucher Voucher, ManufacturingResult Result);

    // ------------------------------------------------------------------ costing helpers

    /// <summary>
    /// Appends the outward consumption line(s) for a component to <paramref name="source"/> and returns their
    /// total consumed value (RQ-13/DP-8, ER-5/ER-7). <b>Batch-aware</b> when the item is maintained in batches:
    /// it emits <b>one outward line per FEFO/FIFO batch pick</b> (each carrying that lot's batch label and its
    /// authoritative per-unit rate, DP-8) so the correct batches are actually drawn down and valued at their own
    /// inward rate — plus a single label-less residual line only when the batches do not cover the whole
    /// quantity (which the no-negative-stock guard then rejects at post time, ER-7). For a non-batch item it
    /// emits one label-less line at the item's running/closing unit rate under its own valuation method
    /// (<see cref="StockValuationService"/>). Never introduces a parallel costing engine.
    /// <para>Emitting per-lot lines (rather than one aggregate label-less line) is the fix for A10 finding #1:
    /// the old aggregate line drove the EMPTY-batch bucket negative and the no-negative guard blocked every
    /// manufacture that consumed a batch-tracked component.</para>
    /// </summary>
    private Money AppendComponentConsumption(
        List<InventoryAllocation> source, Guid componentId, Guid godownId, decimal consumedQty, DateOnly asOf)
    {
        var item = _company.FindStockItem(componentId)
            ?? throw new InvalidOperationException($"BOM component item {componentId} not found.");

        if (!item.MaintainInBatches)
        {
            var unitRate = ItemUnitRate(componentId, asOf);
            source.Add(new InventoryAllocation(
                componentId, godownId, consumedQty, StockDirection.Outward, unitRate));
            return Money.ForexBase(unitRate, consumedQty);
        }

        // Batch-tracked: draw the default (FEFO/FIFO) issue plan and post one outward per lot, each labelled with
        // its own batch and priced at that lot's authoritative inward rate (DP-8). This actually reduces the
        // right batch's on-hand and keeps batch-wise valuation/age/expiry correct (RQ-13/DP-8, ER-5).
        var value = Money.Zero;
        var plannedQty = 0m;
        foreach (var issue in _batches.DefaultIssueSelection(componentId, godownId, consumedQty, asOf))
        {
            if (issue.Quantity <= 0m) continue;
            source.Add(new InventoryAllocation(
                componentId, godownId, issue.Quantity, StockDirection.Outward,
                issue.UnitCost.RoundToPaisa(), batchLabel: issue.Batch));
            value += issue.Value;
            plannedQty += issue.Quantity;
        }

        // Residual the eligible batches do not cover: emit ONE label-less line at the item's running unit rate so
        // no unit is unpriced. This over-draws the empty-batch bucket, so the no-negative guard rejects the whole
        // manufacture at post time exactly like a short Delivery Note (ER-7) — never a silent partial consume.
        var residualQty = RoundQty(consumedQty - plannedQty);
        if (residualQty > 0m)
        {
            var residualRate = ItemUnitRate(componentId, asOf);
            source.Add(new InventoryAllocation(
                componentId, godownId, residualQty, StockDirection.Outward, residualRate));
            value += Money.ForexBase(residualRate, residualQty);
        }

        return value;
    }

    /// <summary>
    /// The finished-good inward line(s) whose booked stock value equals <paramref name="finishedGoodValue"/>
    /// <b>to the paisa</b> (PR-4/ER-2, A10 finding #2). A single line at <paramref name="fgUnitRate"/> books
    /// <c>round(fgUnitRate × N)</c>, which drops the sub-paisa remainder whenever value ÷ N does not divide
    /// evenly. When that remainder is non-zero (and a clean one-unit correction slice exists — the integer-unit
    /// case that covers every real manufacture), this splits the inward into a bulk line of <c>N − 1</c> units
    /// at <paramref name="fgUnitRate"/> plus a one-unit correction line carrying the exact remainder, so
    /// <c>Σ line values = finishedGoodValue</c> exactly (DP-2 remainder-to-last-line). Both quantities sum to
    /// exactly <c>N</c>, so on-hand is unchanged. If no exact paisa split exists (e.g. a fractional produced
    /// quantity), it falls back to the single rounded line — never worse than before, and no such case arises in
    /// scope.
    /// </summary>
    private static IReadOnlyList<InventoryAllocation> FinishedGoodLines(
        Guid itemId, Guid godownId, decimal quantity, Money finishedGoodValue, Money fgUnitRate)
    {
        var single = new[]
        {
            new InventoryAllocation(itemId, godownId, quantity, StockDirection.Inward, fgUnitRate),
        };

        // Remainder lost by a single rounded per-unit line. Both terms are paisa-exact, so δ is a whole number of
        // paisa; nothing to correct when it is zero.
        var bookedBySingleLine = Money.ForexBase(fgUnitRate, quantity);
        var delta = finishedGoodValue.Amount - bookedBySingleLine.Amount;
        if (delta == 0m)
            return single;

        // A one-unit correction slice requires ≥ 1 whole spare unit (bulk = N − 1 > 0). The correction unit
        // carries fgUnitRate + δ so the two lines foot to finishedGoodValue exactly; δ is a multiple of a paisa
        // and fgUnitRate is paisa-exact, so the correction rate is paisa-exact too.
        var bulkQty = quantity - 1m;
        var correctionRate = new Money(fgUnitRate.Amount + delta);
        if (bulkQty <= 0m || !correctionRate.IsPaisaExact || correctionRate.Amount < 0m)
            return single; // no clean split (fractional/degenerate qty) — keep today's single-line behaviour

        var bulkValue = Money.ForexBase(fgUnitRate, bulkQty);
        // Sanity: the two lines must reconcile to the paisa; if not, keep the single line (never make it worse).
        if ((bulkValue.Amount + correctionRate.Amount) != finishedGoodValue.Amount)
            return single;

        return new[]
        {
            new InventoryAllocation(itemId, godownId, bulkQty, StockDirection.Inward, fgUnitRate),
            new InventoryAllocation(itemId, godownId, 1m, StockDirection.Inward, correctionRate),
        };
    }

    /// <summary>
    /// The item's running/closing unit rate as of a date = closing value ÷ closing quantity under its own
    /// valuation method (paisa-exact). Falls back to the item's standard cost, else ₹0, when it has no on-hand
    /// value to derive a rate from — deterministic, never a crash.
    /// </summary>
    private Money ItemUnitRate(Guid itemId, DateOnly asOf)
    {
        var closing = _valuation.ClosingValue(itemId, asOf);
        if (closing.Quantity > 0m)
            return new Money(closing.Value.Amount / closing.Quantity).RoundToPaisa();
        return _company.FindStockItem(itemId)?.StandardCost ?? Money.Zero;
    }

    /// <summary>
    /// The per-unit rate and total carved-out value of a by-product/co-product/scrap line (DP-3): the line's
    /// explicit <see cref="BomLine.Rate"/> × produced-qty when a rate is set; else
    /// <see cref="BomLine.PercentOfFinishedGoodCost"/> of the finished good's <paramref name="preCarveCost"/>
    /// when a percent is set; else the component item's standard cost × produced-qty (the "default the item
    /// standard cost where blank" rule); else ₹0. Paisa-exact and deterministic.
    /// </summary>
    private (Money UnitRate, Money Value) CarveOutValue(BomLine line, decimal producedQty, Money preCarveCost)
    {
        if (line.Rate is { } rate)
        {
            var value = Money.ForexBase(rate, producedQty);
            return (rate, value);
        }

        if (line.PercentOfFinishedGoodCost is { } pct)
        {
            var value = new Money(preCarveCost.Amount * pct / 100m).RoundToPaisa();
            var unit = producedQty > 0m ? new Money(value.Amount / producedQty).RoundToPaisa() : Money.Zero;
            return (unit, value);
        }

        // Default: the carve-out item's own standard cost (DP-3). Blank standard cost ⇒ ₹0.
        var std = _company.FindStockItem(line.ComponentStockItemId)?.StandardCost ?? Money.Zero;
        return (std, Money.ForexBase(std, producedQty));
    }

    private static decimal RoundQty(decimal q) => Math.Round(q, Quantities.DecimalPlaces, MidpointRounding.AwayFromZero);
}

/// <summary>
/// One additional-cost line on a Manufacturing Journal (RQ-13): a labour/overhead/freight charge that ADDS to
/// the finished good's stock value (it is never a separate P&amp;L expense at manufacture — a manufacturing
/// journal books no accounting entry). Money is exact decimal rupees, paisa-exact at the boundary (ER-2).
/// </summary>
public sealed record ManufacturingAdditionalCost(string Name, Money Amount);

/// <summary>
/// The outcome of a <see cref="ManufacturingJournalService.Manufacture"/> call: the posted Stock-Journal
/// <see cref="Voucher"/> plus the reconciled figures (RQ-13/PR-4). <see cref="FinishedGoodValue"/> =
/// <see cref="ComponentCostTotal"/> + <see cref="AdditionalCostTotal"/> − <see cref="CarveOutTotal"/>, and
/// <see cref="FinishedGoodUnitRate"/> = that value ÷ produced quantity, snapped to the paisa. Every figure is
/// paisa-exact so Stock Summary and Balance-Sheet Stock-in-Hand foot to the paisa.
/// </summary>
public sealed record ManufacturingResult(
    InventoryVoucher Voucher,
    Guid BomId,
    decimal Quantity,
    Money ComponentCostTotal,
    Money AdditionalCostTotal,
    Money CarveOutTotal,
    Money FinishedGoodValue,
    Money FinishedGoodUnitRate);
