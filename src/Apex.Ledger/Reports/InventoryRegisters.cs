using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One line in a stock-voucher register (catalog §10/§16; requirements RQ-31): a single item movement on a
/// Receipt/Delivery/Rejection note, with its item, godown, base-unit quantity, rate, value, batch, party and
/// narration. <see cref="Direction"/> distinguishes the two arms of a Rejection register.
/// </summary>
public sealed record InventoryRegisterRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    decimal Quantity,
    StockDirection Direction,
    Money? Rate,
    Money Value,
    string? BatchLabel,
    Guid? PartyId,
    string? PartyName,
    string? Narration,
    string FormattedNumber = "");

/// <summary>
/// One row in the Physical-Stock register (catalog §16; requirements RQ-31): the counted quantity vs the
/// book quantity before the count, and the implied variance (DP-3), for an (item, godown[, batch]) as of the
/// count date.
/// </summary>
public sealed record PhysicalStockRegisterRow(
    DateOnly Date,
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    string? BatchLabel,
    decimal BookQuantity,
    decimal CountedQuantity,
    decimal Variance);

/// <summary>
/// One row in the Order register (catalog §10/§16; requirements RQ-31, RQ-20): one Purchase/Sales-Order line
/// with its ordered quantity and — when derivable from tracked fulfilment — the fulfilled and outstanding
/// quantities. See <see cref="OrderRegister"/> for the fulfilment-derivation note.
/// </summary>
public sealed record OrderRegisterRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    decimal OrderedQuantity,
    decimal FulfilledQuantity,
    decimal OutstandingQuantity,
    Money? Rate,
    Guid? PartyId,
    string? PartyName,
    string FormattedNumber = "");

/// <summary>
/// The stock-voucher registers (catalog §10/§16; requirements RQ-31) — Day-Book-style flat chronological
/// lists over [from, to], sorted by date then number. Each is a <b>pure</b> projection (no UI, no DB) that
/// lists exactly the vouchers of its kind that count in the period (cancelled excluded; a post-dated voucher
/// only once its date ≤ <c>to</c>). One row per line.
/// </summary>
public static class InventoryRegisters
{
    /// <summary>The Receipt Note (GRN) register — inward movements on Receipt-Note vouchers.</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildReceiptNotes(Company company, DateOnly from, DateOnly to)
        => BuildAllocationRegister(company, from, to, VoucherBaseType.ReceiptNote);

    /// <summary>The Delivery Note register — outward movements on Delivery-Note vouchers.</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildDeliveryNotes(Company company, DateOnly from, DateOnly to)
        => BuildAllocationRegister(company, from, to, VoucherBaseType.DeliveryNote);

    /// <summary>The Rejection register — both Rejection In (inward) and Rejection Out (outward), each row's
    /// <see cref="InventoryRegisterRow.Direction"/> distinguishing the arm.</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildRejections(Company company, DateOnly from, DateOnly to)
        => BuildAllocationRegister(company, from, to, VoucherBaseType.RejectionIn, VoucherBaseType.RejectionOut);

    /// <summary>
    /// The Physical-Stock register — one row per counted line with counted vs book and the variance (DP-3),
    /// via <see cref="InventoryLedger.PhysicalStockAdjustments"/>, restricted to counts in [from, to].
    /// </summary>
    public static IReadOnlyList<PhysicalStockRegisterRow> BuildPhysicalStock(Company company, DateOnly from, DateOnly to)
    {
        var ledger = new InventoryLedger(company);
        var rows = new List<PhysicalStockRegisterRow>();
        foreach (var adj in ledger.PhysicalStockAdjustments(to))
        {
            if (adj.Date < from || adj.Date > to) continue;
            var item = company.FindStockItem(adj.StockItemId);
            var godown = company.FindGodown(adj.GodownId);
            rows.Add(new PhysicalStockRegisterRow(
                adj.Date, adj.StockItemId, item?.Name ?? "(unknown)",
                adj.GodownId, godown?.Name ?? "(unknown)", adj.BatchLabel,
                adj.BookQuantityBefore, adj.CountedQuantity, adj.AdjustmentQuantity));
        }
        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
        });
        return rows;
    }

    /// <summary>
    /// The Order register — Purchase &amp; Sales orders over [from, to], one row per order line, each carrying
    /// the quantity actually fulfilled and the quantity still outstanding.
    /// <para><b>Fulfilment (Phase 10.10 / WF-8).</b> Derived by <see cref="OrderFulfilment"/>, which retires an
    /// order against the Receipt/Delivery notes that fulfil it. Read that class before changing anything here:
    /// the attribution rule it applies is a <b>stated engineering choice</b> made because the explicit order
    /// link TallyPrime uses is unreachable in this product, and its limitations are enumerated there.</para>
    /// <para><b>The fulfilment map is built over the whole book up to <paramref name="to"/>, not over
    /// [from, to].</b> An order placed before the window can still be fulfilled by a movement inside it, and if
    /// such an order were absent from the cohort its movement would be misattributed to a NEWER in-window
    /// order — the window would silently change the figures it reports. Only the LISTING is windowed.</para>
    /// </summary>
    public static IReadOnlyList<OrderRegisterRow> BuildOrders(Company company, DateOnly from, DateOnly to)
    {
        var fulfilment = OrderFulfilment.Build(company, to);
        var rows = new List<OrderRegisterRow>();
        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (v.PostDated && v.Date > to) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (type.BaseType is not (VoucherBaseType.PurchaseOrder or VoucherBaseType.SalesOrder)) continue;

            var partyName = v.PartyId is { } pid ? company.FindLedger(pid)?.Name : null;
            for (var i = 0; i < v.OrderLines.Count; i++)
            {
                var line = v.OrderLines[i];
                var item = company.FindStockItem(line.StockItemId);
                var godown = company.FindGodown(line.GodownId);
                var done = fulfilment.TryGetValue((v.Id, i), out var f) ? f : 0m;
                // Outstanding is floored at zero so an over-delivered line reads 0 rather than a negative that
                // would net against a genuinely open line when a caller sums the column.
                // 🔴 Stated plainly so a reviewer does not have to work it out: with the CURRENT derivation this
                // floor can never fire — OrderFulfilment allocates at most each line's remaining quantity, so
                // `done <= line.Quantity` always, and the clamp that actually bites lives there (the surplus is
                // dropped for want of a line to carry it). It is kept because the floor belongs to the row, not
                // to one attribution rule: an OrderLinks-based sum — the mechanism this slice could not reach,
                // and the one JobWorkReports uses — has no per-line cap and CAN exceed the ordered quantity,
                // which is why JobWorkReports carries this identical guard on its own pending figure.
                var outstanding = line.Quantity - done;
                if (outstanding < 0m) outstanding = 0m;
                rows.Add(new OrderRegisterRow(
                    v.Date, type.Name, v.Number, line.StockItemId, item?.Name ?? "(unknown)",
                    line.GodownId, godown?.Name ?? "(unknown)",
                    line.Quantity, done, outstanding,
                    line.Rate, v.PartyId, partyName, company.FormatVoucherNumber(v)));
            }
        }
        SortRegister(rows, r => (r.Date, r.Number, r.ItemName));
        return rows;
    }

    // ------------------------------------------------------------------ internal

    private static IReadOnlyList<InventoryRegisterRow> BuildAllocationRegister(
        Company company, DateOnly from, DateOnly to, params VoucherBaseType[] baseTypes)
    {
        var wanted = new HashSet<VoucherBaseType>(baseTypes);
        var rows = new List<InventoryRegisterRow>();

        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (v.PostDated && v.Date > to) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !wanted.Contains(type.BaseType)) continue;

            var partyName = v.PartyId is { } pid ? company.FindLedger(pid)?.Name : null;
            foreach (var a in v.Allocations)
            {
                var item = company.FindStockItem(a.StockItemId);
                var godown = company.FindGodown(a.GodownId);
                var qtyBase = QuantityInBase(company, a);
                var rateBase = RateInBase(company, a);
                var value = rateBase is { } r ? Money.ForexBase(r, qtyBase) : Money.Zero;
                // The Rate column is emitted PER BASE UNIT — the same unit Quantity is in — so a reader can
                // multiply the two columns and land on Value. Emitting the raw per-displayed-unit rate beside
                // a base-unit quantity made the row disagree with itself by the conversion factor.
                rows.Add(new InventoryRegisterRow(
                    v.Date, type.Name, v.Number, a.StockItemId, item?.Name ?? "(unknown)",
                    a.GodownId, godown?.Name ?? "(unknown)", qtyBase, a.Direction,
                    rateBase, value, a.BatchLabel, v.PartyId, partyName, v.Narration,
                    company.FormatVoucherNumber(v)));
            }
        }

        SortRegister(rows, r => (r.Date, r.Number, r.ItemName));
        return rows;
    }

    private static void SortRegister<T>(List<T> rows, Func<T, (DateOnly, int, string)> key)
        => rows.Sort((a, b) =>
        {
            var (da, na, ia) = key(a);
            var (db, nb, ib) = key(b);
            var byDate = da.CompareTo(db);
            if (byDate != 0) return byDate;
            var byNum = na.CompareTo(nb);
            return byNum != 0 ? byNum : string.Compare(ia, ib, StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>The allocation's quantity re-expressed in the item's BASE unit, for this class's own movement
    /// registers — its only caller is <see cref="BuildAllocationRegister"/>.
    /// <para>🔴 <b>It was briefly widened to <c>internal</c> with a doc claiming
    /// <see cref="OrderFulfilment"/> normalised through it "so the two cannot drift". That was false</b> —
    /// <c>OrderFulfilment</c> contains no reference to this helper at all; it reads
    /// <see cref="InventoryMovements.Between"/>, which owns its own base-unit conversion for BOTH the
    /// pure-stock and the item-invoice path (<c>InventoryMovements.cs:161</c> and <c>:186</c>). The widening was
    /// therefore dead and the comment pointed a maintainer at the wrong code path — editing this method would
    /// have moved the Receipt/Delivery/Rejection register rows and left fulfilment untouched, the exact silent
    /// divergence it claimed to have removed. It is <c>private</c> again, and the real (stronger) guarantee is
    /// stated where it belongs: fulfilment single-sources every movement quantity through
    /// <see cref="InventoryMovements"/>, the same enumeration the on-hand and valuation engines read.</para></summary>
    private static decimal QuantityInBase(Company company, InventoryAllocation a)
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
