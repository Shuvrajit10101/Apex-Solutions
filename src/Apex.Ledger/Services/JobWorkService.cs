using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The Job Work feature service (Phase 6 slice 8; RQ-45). It owns the <b>F11 "Enable Job Order Processing"</b>
/// toggle side-effects — the one piece of Job-Work behaviour that is more than a plain flag set: turning the
/// feature on must also <b>activate the four seeded-but-inactive predefined voucher types</b> (Job Work In/Out
/// Order, Material In/Out) and stamp their per-type flags ("Use for Job Work" on both Material types, "Allow
/// Consumption" on Material In). Framework- and DB-agnostic; the Desktop Features screen drives it and then
/// persists via the store, exactly like every other F11 toggle.
/// <para>Order entry and material movement themselves need no service — the caller posts an
/// <see cref="InventoryVoucher.JobWork"/> / <see cref="InventoryVoucher.MaterialMovement"/> voucher straight
/// through <see cref="InventoryPostingService"/>, which classifies and guards them (ER-5/ER-7).</para>
/// </summary>
public sealed class JobWorkService
{
    private readonly Company _company;

    public JobWorkService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The four predefined base kinds the feature governs (RQ-45).</summary>
    private static readonly VoucherBaseType[] JobWorkBaseTypes =
    {
        VoucherBaseType.JobWorkInOrder,
        VoucherBaseType.MaterialIn,
        VoucherBaseType.JobWorkOutOrder,
        VoucherBaseType.MaterialOut,
    };

    /// <summary>
    /// Turns <b>Enable Job Order Processing</b> on or off (RQ-45). When turned <b>on</b>: sets
    /// <see cref="Company.EnableJobOrderProcessing"/>, activates the four seeded Job-Work voucher types
    /// (<see cref="VoucherType.IsActive"/> = true), and stamps <see cref="VoucherType.UseForJobWork"/> on both
    /// Material types + <see cref="VoucherType.AllowConsumption"/> on Material In (auto-set, matching TallyHelp's
    /// semantics). When turned <b>off</b>: clears the company flag and re-hides the four types
    /// (<see cref="VoucherType.IsActive"/> = false); it never deletes any posted job-work data (harmless — the
    /// screens simply hide). Idempotent.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _company.EnableJobOrderProcessing = enabled;

        foreach (var type in _company.VoucherTypes)
        {
            if (Array.IndexOf(JobWorkBaseTypes, type.BaseType) < 0) continue;
            type.IsActive = enabled;

            if (type.BaseType is VoucherBaseType.MaterialIn or VoucherBaseType.MaterialOut)
                type.UseForJobWork = enabled;
            if (type.BaseType == VoucherBaseType.MaterialIn)
                type.AllowConsumption = enabled;
        }
    }

    // ================================================================ material-movement valuation (RQ-46/RQ-49; ER-4)
    //
    // The two-sided Material movements are TRANSFORMS/TRANSFERS whose destination stock value is DERIVED from the
    // live cost of the stock they consume — never from the order's planned rate or a hand-typed rate. Booking the
    // destination at a planned/entered rate that diverges from the live consumed cost injects phantom stock value
    // with no accounting counter-entry (it silently shifts Balance-Sheet Stock-in-Hand). These builders mirror
    // ManufacturingJournalService: the finished good / transfer inward is priced from StockValuationService's live
    // issue cost, paisa-conserved, so Stock Summary and Balance-Sheet Stock-in-Hand foot to the paisa (ER-4).

    /// <summary>
    /// Builds a <b>consuming Material In</b> (Allow Consumption) voucher whose finished-good production line(s) are
    /// valued from the <b>live cost of the consumed components</b> (RQ-49/ER-4) — exactly as a Manufacturing
    /// Journal values its finished good — rather than from the order's planned finished-good rate. Each consumption
    /// (source/outward) line is priced at its item's live issue rate; the finished good (destination/inward) total
    /// value is set to Σ that live issue cost, distributed across the destination line(s) and paisa-conserved so
    /// <c>Σ destination value = Σ consumed value</c> to the paisa (no phantom stock value, no silent ₹0 finished
    /// good). The result is an un-posted <see cref="InventoryVoucher.MaterialMovement"/>; the caller posts it
    /// through <see cref="InventoryPostingService"/> (which is balance-exempt for a consuming Material In, RQ-49).
    /// </summary>
    public InventoryVoucher BuildConsumingMaterialIn(
        Guid typeId,
        DateOnly date,
        IReadOnlyList<InventoryAllocation> consume,
        IReadOnlyList<InventoryAllocation> produce,
        IEnumerable<Guid>? orderLinks = null,
        string? narration = null,
        Guid? partyId = null)
    {
        ArgumentNullException.ThrowIfNull(consume);
        ArgumentNullException.ThrowIfNull(produce);

        var valuation = new StockValuationService(_company);
        var (perItemRate, totalConsumed) = ValuePerItemIssue(valuation, consume, date);

        // Source (consumption/outward) lines re-priced at each item's live issue rate. The valuation engine ignores
        // an outward line's own rate (average leaves the running average unchanged; FIFO/LIFO drains layers), so
        // this only sharpens the register display — it never changes what the components' stock actually loses.
        // The rate is re-expressed PER THE LINE'S OWN UNIT (see RatePerLineUnit) because the line keeps a.UnitId.
        var source = consume
            .Select(a => new InventoryAllocation(
                a.StockItemId, a.GodownId, a.Quantity, StockDirection.Outward,
                RatePerLineUnit(perItemRate[a.StockItemId], a.UnitId), a.BatchLabel, a.UnitId))
            .ToList();

        // Destination (finished-good/inward) lines valued so their Σ equals the live consumed cost (paisa-conserved).
        var destination = ValueDestinationToTotal(produce, totalConsumed);

        return InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), typeId, date, source, destination, orderLinks, number: 0, narration, partyId);
    }

    /// <summary>
    /// Builds a <b>Material Out</b> balanced transfer (RQ-46) that is <b>value-neutral</b>: the destination
    /// (third-party godown) inward is valued at the <b>source's live issue cost</b>, not the order's planned rate,
    /// so a pure location move changes no company total (stock genuinely stays on our books at the same value).
    /// Quantities are preserved on both sides (the transfer still balances in the base unit); each item's inward
    /// value is set to exactly the value the outward removes, paisa-conserved. The result is an un-posted
    /// <see cref="InventoryVoucher.MaterialMovement"/> the caller posts through <see cref="InventoryPostingService"/>.
    /// <para>
    /// <b>VALUE-NEUTRALITY INVARIANT (locked by test, defect D4).</b> For every item: the value the OUTWARD leg
    /// removes equals the value the INWARD leg re-adds — both as the engine books it (total Stock-in-Hand is
    /// unchanged by the transfer) and as the Material Out register REPORTS it (goods-out value reconciles with
    /// goods-in value, line by line). The reported half is the fragile one: it holds only while each leg's stored
    /// rate is expressed per that line's OWN unit, so that the register's base-unit normalisation applies the
    /// conversion factor exactly once. Storing a per-base rate on a compound-unit line broke the reported half
    /// silently — ₹2.00 out against ₹24.00 in — while the engine half stayed green, because valuation ignores an
    /// outward line's own rate. See <see cref="RatePerLineUnit"/>.
    /// </para>
    /// </summary>
    public InventoryVoucher BuildMaterialOutTransfer(
        Guid typeId,
        DateOnly date,
        IReadOnlyList<InventoryAllocation> source,
        IReadOnlyList<InventoryAllocation> destination,
        IEnumerable<Guid>? orderLinks = null,
        string? narration = null,
        Guid? partyId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var valuation = new StockValuationService(_company);
        var (perItemRate, _) = ValuePerItemIssue(valuation, source, date);

        // The exact value each item's outward removes (Σ per item) — the target the inward must re-add to stay
        // value-neutral. Method-consistent and independent of the lines' own rates.
        var perItemValue = source
            .GroupBy(a => a.StockItemId)
            .ToDictionary(g => g.Key, g => valuation.IssueValue(g.Key, g.Sum(BaseQty), date));

        var perItemDestQty = new Dictionary<Guid, decimal>();
        foreach (var a in destination)
        {
            perItemDestQty.TryGetValue(a.StockItemId, out var q);
            perItemDestQty[a.StockItemId] = q + BaseQty(a);
        }

        // Source outward re-priced at live issue rate (cosmetic — valuation ignores an outward rate), expressed
        // PER THE LINE'S OWN UNIT (see RatePerLineUnit) because the line keeps a.UnitId. Storing the raw per-BASE
        // rate here understated the value of goods sent to the job worker by exactly the conversion factor.
        var src = source
            .Select(a => new InventoryAllocation(
                a.StockItemId, a.GodownId, a.Quantity, StockDirection.Outward,
                RatePerLineUnit(perItemRate.TryGetValue(a.StockItemId, out var sr) ? sr : Money.Zero, a.UnitId),
                a.BatchLabel, a.UnitId))
            .ToList();

        // Destination inward valued so each item's Σ inward value = the value its outward removed (value-neutral).
        var remainingQty = new Dictionary<Guid, decimal>(perItemDestQty);
        var remainingVal = perItemValue.ToDictionary(kv => kv.Key, kv => kv.Value.Amount);
        var dst = new List<InventoryAllocation>();
        foreach (var a in destination)
        {
            var baseQty = BaseQty(a);
            var itemValue = remainingVal.TryGetValue(a.StockItemId, out var rv) ? rv : 0m;
            var itemQtyLeft = remainingQty.TryGetValue(a.StockItemId, out var rq) ? rq : baseQty;
            var rate = perItemRate.TryGetValue(a.StockItemId, out var pr) ? pr : Money.Zero;

            // Last line for this item takes the exact remaining value; earlier lines take rate × qty.
            var share = baseQty >= itemQtyLeft
                ? new Money(itemValue)
                : Money.ForexBase(rate, baseQty);
            remainingQty[a.StockItemId] = itemQtyLeft - baseQty;
            remainingVal[a.StockItemId] = itemValue - share.Amount;

            var unitRate = a.Quantity > 0m ? new Money(share.Amount / a.Quantity).RoundToPaisa() : Money.Zero;
            foreach (var alloc in ConservedInwardLines(
                         a.StockItemId, a.GodownId, a.Quantity, share, unitRate, a.BatchLabel, a.UnitId))
                dst.Add(alloc);
        }

        return InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), typeId, date, src, dst, orderLinks, number: 0, narration, partyId);
    }

    // ---------------------------------------------------------------- valuation helpers

    /// <summary>The live per-item issue rate (issue value ÷ consumed qty, paisa) for every distinct item in
    /// <paramref name="lines"/>, plus the Σ live issue value across them. The issue value is method-consistent
    /// (FIFO/LIFO drain layers; average/flat use the running rate) and independent of the lines' own rates.</summary>
    private (Dictionary<Guid, Money> PerItemRate, Money Total) ValuePerItemIssue(
        StockValuationService valuation, IReadOnlyList<InventoryAllocation> lines, DateOnly date)
    {
        var perItemQty = new Dictionary<Guid, decimal>();
        foreach (var a in lines)
        {
            perItemQty.TryGetValue(a.StockItemId, out var q);
            perItemQty[a.StockItemId] = q + BaseQty(a);
        }

        var perItemRate = new Dictionary<Guid, Money>();
        var total = Money.Zero;
        foreach (var (itemId, qty) in perItemQty)
        {
            var value = valuation.IssueValue(itemId, qty, date);
            total = new Money(total.Amount + value.Amount);
            perItemRate[itemId] = qty > 0m ? new Money(value.Amount / qty).RoundToPaisa() : Money.Zero;
        }
        return (perItemRate, total);
    }

    /// <summary>
    /// Values the destination (inward) lines so their booked stock value sums to exactly <paramref name="totalValue"/>
    /// (paisa-conserved). Each line takes a value share proportional to its quantity (the last line absorbs the
    /// rounding remainder), and each share is booked via <see cref="ConservedInwardLines"/> so no sub-paisa leaks.
    /// </summary>
    private List<InventoryAllocation> ValueDestinationToTotal(IReadOnlyList<InventoryAllocation> produce, Money totalValue)
    {
        var result = new List<InventoryAllocation>();
        if (produce.Count == 0) return result;

        var totalQty = 0m;
        foreach (var a in produce) totalQty += BaseQty(a);
        if (totalQty <= 0m) return produce.ToList();

        var assigned = 0m;
        for (var i = 0; i < produce.Count; i++)
        {
            var a = produce[i];
            var baseQty = BaseQty(a);
            Money share;
            if (i < produce.Count - 1)
            {
                share = new Money(totalValue.Amount * baseQty / totalQty).RoundToPaisa();
                assigned += share.Amount;
            }
            else
            {
                share = new Money(totalValue.Amount - assigned); // remainder-to-last-line (paisa-exact Σ)
            }

            var unitRate = a.Quantity > 0m ? new Money(share.Amount / a.Quantity).RoundToPaisa() : Money.Zero;
            foreach (var alloc in ConservedInwardLines(
                         a.StockItemId, a.GodownId, a.Quantity, share, unitRate, a.BatchLabel, a.UnitId))
                result.Add(alloc);
        }
        return result;
    }

    /// <summary>
    /// Inward line(s) for one item whose booked stock value equals <paramref name="targetValue"/> to the paisa —
    /// the DP-2 remainder-to-last-line technique (mirrors <c>ManufacturingJournalService.ConservedInwardLines</c>).
    /// A single line at <paramref name="unitRate"/> books <c>round(unitRate × qty)</c>, which drops the sub-paisa
    /// remainder whenever the target ÷ qty does not divide evenly; when a clean one-unit correction slice exists,
    /// this splits into a <c>qty − 1</c> bulk line + a one-unit correction line carrying the exact remainder so the
    /// two foot to <paramref name="targetValue"/> exactly (both quantities still sum to <c>qty</c>, so on-hand and
    /// any balance rule are unchanged). Falls back to the single rounded line when no exact split exists.
    /// </summary>
    private static IReadOnlyList<InventoryAllocation> ConservedInwardLines(
        Guid itemId, Guid godownId, decimal quantity, Money targetValue, Money unitRate,
        string? batchLabel, Guid? unitId)
    {
        var single = new[]
        {
            new InventoryAllocation(itemId, godownId, quantity, StockDirection.Inward, unitRate, batchLabel, unitId),
        };

        var bookedBySingleLine = Money.ForexBase(unitRate, quantity);
        var delta = targetValue.Amount - bookedBySingleLine.Amount;
        if (delta == 0m) return single;

        var bulkQty = quantity - 1m;
        var correctionRate = new Money(unitRate.Amount + delta);
        if (bulkQty <= 0m || !correctionRate.IsPaisaExact || correctionRate.Amount < 0m)
            return single; // no clean split (fractional/degenerate qty) — keep single-line behaviour

        var bulkValue = Money.ForexBase(unitRate, bulkQty);
        if ((bulkValue.Amount + correctionRate.Amount) != targetValue.Amount)
            return single;

        return new[]
        {
            new InventoryAllocation(itemId, godownId, bulkQty, StockDirection.Inward, unitRate, batchLabel, unitId),
            new InventoryAllocation(itemId, godownId, 1m, StockDirection.Inward, correctionRate, batchLabel, unitId),
        };
    }

    private decimal BaseQty(InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = _company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }

    /// <summary>
    /// Re-expresses a rate DERIVED IN BASE UNITS (the live issue cost from <see cref="ValuePerItemIssue"/>, which
    /// divides an issue value by the BASE quantity) as a rate per <paramref name="unitId"/> — the unit the line's
    /// own <c>Quantity</c> is stated in — so that quantity and rate are in the SAME unit.
    /// <para>
    /// <b>Why this exists (defect D4).</b> Every consumer of a stored line pairs <c>Quantity</c> with <c>Rate</c>,
    /// and the registers additionally normalise BOTH to base units before multiplying. Writing a per-base rate onto
    /// a line whose quantity is "2 Doz" therefore got the rate divided by the factor a SECOND time: goods worth
    /// ₹24.00 sent to a third-party job worker were reported at ₹2.00, a 12× understatement that also silently
    /// broke the value-neutrality invariant below (goods-out value must reconcile with goods-in value). See
    /// <see cref="Unit.RateFromBaseMeasure"/> for the full statement of the two-directional risk class.
    /// </para>
    /// </summary>
    private Money RatePerLineUnit(Money baseRate, Guid? unitId)
    {
        if (unitId is not { } id) return baseRate;
        var unit = _company.FindUnit(id);
        return unit is null ? baseRate : new Money(unit.RateFromBaseMeasure(baseRate.Amount));
    }
}
