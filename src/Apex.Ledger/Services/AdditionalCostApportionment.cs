using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The single <b>Additional Cost of Purchase</b> apportionment engine (Book pp.133–141; catalog §11; Phase 6
/// slice 3 RQ-16..RQ-20; PR-5). A <b>pure, deterministic, paisa-exact</b> service that spreads an additional-cost
/// pool (Freight, Packing, …) across the item lines of a Purchase item-invoice (<see cref="ForPurchase"/>) or the
/// destination allocations of a Stock-Journal transfer (<see cref="ForTransfer"/>), raising each line's
/// <b>landed</b> (effective) stock rate. It is used by BOTH paths (ER-4: the same engine feeds the Desktop
/// screen and the valuation, so the displayed landed rate == the posted/reported rate).
///
/// <para><b>Apportionment (DP-2).</b> A ledger's method decides the weight:
/// <list type="bullet">
///   <item><b>By Quantity</b> — weight = the line's base-unit quantity (a flat ₹/unit spread evenly).</item>
///   <item><b>By Value</b> — weight = the line's purchase value (qty × rate); dearer lines absorb more.</item>
/// </list>
/// Shares are computed by <see cref="Allocate"/> with a deterministic <b>largest-remainder</b> rule so the
/// per-line loads sum <b>exactly</b> to the pool with no lost or invented paisa.</para>
///
/// <para><b>RQ-19 (the fidelity trap).</b> A plain Direct-Expenses ledger (no
/// <see cref="Ledger.MethodOfAppropriation"/>) is NOT swept into either pool, so it stays purely P&amp;L and never
/// touches a stock rate — even on a Purchase whose voucher type has
/// <see cref="VoucherType.TrackAdditionalCosts"/>. The difference is the ledger's method + the tracking flag, not
/// the ledger itself.</para>
/// </summary>
public static class AdditionalCostApportionment
{
    /// <summary>
    /// Splits <paramref name="pool"/> across <paramref name="weights"/> by the deterministic largest-remainder
    /// method (DP-2), paisa-exact: floor each proportional share to the paisa, then hand out the leftover paisa
    /// one at a time to the largest fractional remainder (ties broken by <b>ascending</b> index). The returned
    /// shares always sum <b>exactly</b> to <paramref name="pool"/>. A non-positive pool, or a zero total weight,
    /// yields all-zero shares; a zero-weight line gets a zero share.
    /// </summary>
    public static IReadOnlyList<Money> Allocate(IReadOnlyList<decimal> weights, Money pool)
    {
        if (weights is null) throw new ArgumentNullException(nameof(weights));

        var n = weights.Count;
        var zeros = new Money[n];
        for (var i = 0; i < n; i++) zeros[i] = Money.Zero;
        if (n == 0) return zeros;

        // Pool is a paisa-exact Money; work in integer paisa so the split can never leak a sub-paisa tail.
        var poolPaisa = (long)decimal.Round(pool.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (poolPaisa <= 0L) return zeros;

        var totalWeight = 0m;
        foreach (var w in weights) if (w > 0m) totalWeight += w;
        if (totalWeight <= 0m) return zeros;

        var floors = new long[n];
        var remainders = new decimal[n];
        var assigned = 0L;
        for (var i = 0; i < n; i++)
        {
            var w = weights[i] > 0m ? weights[i] : 0m;
            var exact = poolPaisa * w / totalWeight;          // exact proportional share, in paisa
            var floor = decimal.Floor(exact);
            floors[i] = (long)floor;
            remainders[i] = exact - floor;
            assigned += floors[i];
        }

        var leftover = poolPaisa - assigned;
        // Order candidate indices by fractional remainder DESC, then ascending index (deterministic tie-break).
        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            var cmp = remainders[b].CompareTo(remainders[a]);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        for (var k = 0; k < leftover; k++)
            floors[order[k % n]] += 1;

        var result = new Money[n];
        for (var i = 0; i < n; i++) result[i] = new Money(floors[i] / 100m);
        return result;
    }

    /// <summary>
    /// One item line's landed valuation: its own purchase value plus the by-quantity and by-value shares of the
    /// additional-cost pools. <see cref="LandedValue"/> = purchase + both shares (paisa-exact); the
    /// <see cref="LandedUnitRate"/> is the <b>exact</b> decimal <c>LandedValue / Quantity</c> the valuation reads
    /// as the movement's inward rate.
    /// </summary>
    public readonly record struct LandedLine(int Index, decimal Quantity, Money PurchaseValue, Money QtyShare, Money ValueShare)
    {
        /// <summary>The paisa-exact landed value = purchase value + by-quantity share + by-value share.</summary>
        public Money LandedValue => PurchaseValue + QtyShare + ValueShare;

        /// <summary>The exact landed unit rate = <see cref="LandedValue"/> ÷ <see cref="Quantity"/> (the valuation
        /// snaps to the paisa only when it aggregates, so this stays an exact decimal).</summary>
        public decimal LandedUnitRate => Quantity != 0m ? LandedValue.Amount / Quantity : 0m;

        /// <summary>True iff any additional cost actually loaded this line (so an untracked/zero-load line can
        /// take the identical old valuation path — ER-13).</summary>
        public bool HasLoad => QtyShare.Amount != 0m || ValueShare.Amount != 0m;
    }

    /// <summary>
    /// Apportions a Purchase item-invoice's additional-cost entry lines across its item lines (RQ-16..RQ-19). The
    /// additional-cost lines are the voucher's <b>debit</b> entry lines whose ledger has a non-null
    /// <see cref="Ledger.MethodOfAppropriation"/> — but only when the voucher type is a Purchase with
    /// <see cref="VoucherType.TrackAdditionalCosts"/> on. Each such line joins its method's pool (by-quantity or
    /// by-value); the pools are then allocated over the item lines (weights = base-unit quantity / line value). A
    /// plain freight ledger with no method stays out of both pools (pure P&amp;L — RQ-19). Returns one
    /// <see cref="LandedLine"/> per item line, in item-line order; when nothing is tracked every share is zero.
    /// </summary>
    public static IReadOnlyList<LandedLine> ForPurchase(Company company, Voucher voucher)
    {
        if (company is null) throw new ArgumentNullException(nameof(company));
        if (voucher is null) throw new ArgumentNullException(nameof(voucher));

        var items = voucher.InventoryLines;

        var qtyPool = Money.Zero;
        var valuePool = Money.Zero;

        var type = company.FindVoucherType(voucher.TypeId);
        var tracked = type is not null && type.TrackAdditionalCosts && type.BaseType == VoucherBaseType.Purchase;
        if (tracked)
        {
            foreach (var line in voucher.Lines)
            {
                if (line.Side != DrCr.Debit) continue; // an additional cost posts a Dr to its Direct-Expenses ledger
                var ledger = company.FindLedger(line.LedgerId);
                if (ledger?.MethodOfAppropriation is not { } method) continue; // plain P&L ledger (RQ-19)
                if (method == MethodOfAppropriation.ByQuantity) qtyPool += line.Amount;
                else valuePool += line.Amount;
            }
        }

        var qtyWeights = new decimal[items.Count];
        var valueWeights = new decimal[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            qtyWeights[i] = items[i].Quantity;
            valueWeights[i] = items[i].Value.Amount;
        }

        var qtyShares = Allocate(qtyWeights, qtyPool);
        var valueShares = Allocate(valueWeights, valuePool);

        var result = new List<LandedLine>(items.Count);
        for (var i = 0; i < items.Count; i++)
            result.Add(new LandedLine(i, items[i].Quantity, items[i].Value, qtyShares[i], valueShares[i]));
        return result;
    }

    /// <summary>
    /// Apportions a Stock-Journal transfer's <see cref="InventoryVoucher.AdditionalCostLines"/> across its
    /// <see cref="InventoryVoucher.DestinationAllocations"/> (RQ-20), loading the destination stock's landed inward
    /// rate by each ledger's method. Weights are the destination allocation's <b>base-unit</b> quantity (by-qty)
    /// and its base-value = base-qty × rate (by-value; a rateless destination line contributes zero value weight).
    /// <b>Money-conservation guard:</b> if an Appropriate-by-Value pool is positive but <b>every</b> destination is
    /// rateless (so the by-value basis is all-zero), the by-value pool falls back to a by-quantity spread rather
    /// than vanishing — Σ(per-line loads) always equals the pool exactly (PR-5). Returns one
    /// <see cref="LandedLine"/> per destination allocation, in order; the source lines are untouched.
    /// </summary>
    public static IReadOnlyList<LandedLine> ForTransfer(Company company, InventoryVoucher voucher)
    {
        if (company is null) throw new ArgumentNullException(nameof(company));
        if (voucher is null) throw new ArgumentNullException(nameof(voucher));

        var dest = voucher.DestinationAllocations;

        var qtyPool = Money.Zero;
        var valuePool = Money.Zero;
        foreach (var acl in voucher.AdditionalCostLines)
        {
            var ledger = company.FindLedger(acl.LedgerId);
            if (ledger?.MethodOfAppropriation is not { } method) continue;
            if (method == MethodOfAppropriation.ByQuantity) qtyPool += acl.Amount;
            else valuePool += acl.Amount;
        }

        var qtyWeights = new decimal[dest.Count];
        var valueWeights = new decimal[dest.Count];
        var baseQty = new decimal[dest.Count];
        var baseValue = new Money[dest.Count];
        for (var i = 0; i < dest.Count; i++)
        {
            var a = dest[i];
            var q = BaseQuantity(company, a);
            baseQty[i] = q;
            var value = RateInBase(company, a) is { } r ? Money.ForexBase(r, q) : Money.Zero;
            baseValue[i] = value;
            qtyWeights[i] = q;
            valueWeights[i] = value.Amount;
        }

        var qtyShares = Allocate(qtyWeights, qtyPool);

        // Money-conservation guard: a by-value pool needs a positive value basis to spread over. When every
        // destination is rateless the value basis is all-zero and Allocate would return all-zeros, silently
        // destroying the pool (it posts to neither stock nor P&L on a Stock Journal). Fall back to a by-quantity
        // spread so a positive pool always lands somewhere and Σ(shares) == pool holds (PR-5). RQ-19 is
        // unaffected — a method-less ledger never enters valuePool at all.
        var valueBasis = valueWeights;
        var hasValueBasis = false;
        foreach (var w in valueWeights) if (w > 0m) { hasValueBasis = true; break; }
        if (!hasValueBasis) valueBasis = qtyWeights;
        var valueShares = Allocate(valueBasis, valuePool);

        var result = new List<LandedLine>(dest.Count);
        for (var i = 0; i < dest.Count; i++)
            result.Add(new LandedLine(i, baseQty[i], baseValue[i], qtyShares[i], valueShares[i]));
        return result;
    }

    /// <summary>The allocation quantity normalised to the item's base unit (mirrors the valuation engine's
    /// unit conversion, DP-6), so a landed rate produced here matches the rate the valuation reads.</summary>
    private static decimal BaseQuantity(Company company, InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }

    /// <summary>The allocation's rate re-expressed per the item's BASE unit — the rate on a line is per the
    /// unit the LINE is stated in (WI-10 slice C), so it is divided by exactly the factor the quantity was
    /// multiplied by, keeping value = qty x rate invariant under the conversion.</summary>
    private static Money? RateInBase(Company company, InventoryAllocation a)
    {
        if (a.Rate is not { } r) return null;
        if (a.UnitId is not { } unitId) return r;
        var unit = company.FindUnit(unitId);
        return unit is null ? r : new Money(unit.RateInBaseMeasure(r.Amount));
    }

}
