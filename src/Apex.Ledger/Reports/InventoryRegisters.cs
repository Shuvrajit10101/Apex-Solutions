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
    /// The Order register — Purchase &amp; Sales orders over [from, to], one row per order line.
    /// <para><b>Fulfilment derivation (judgment call).</b> Phase-3 orders carry no persisted tracking link to
    /// their Receipt/Delivery notes, so this register cannot attribute a specific note to a specific order. It
    /// therefore reports <see cref="OrderRegisterRow.FulfilledQuantity"/> = 0 and
    /// <see cref="OrderRegisterRow.OutstandingQuantity"/> = the full ordered quantity — i.e. the orders as
    /// placed. (Tracking-number-based fulfilment attribution is deferred with the order-processing chain; see
    /// requirements RQ-18/RQ-20.) The columns exist so the UI can populate them once tracking lands.</para>
    /// </summary>
    public static IReadOnlyList<OrderRegisterRow> BuildOrders(Company company, DateOnly from, DateOnly to)
    {
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
            foreach (var line in v.OrderLines)
            {
                var item = company.FindStockItem(line.StockItemId);
                var godown = company.FindGodown(line.GodownId);
                rows.Add(new OrderRegisterRow(
                    v.Date, type.Name, v.Number, line.StockItemId, item?.Name ?? "(unknown)",
                    line.GodownId, godown?.Name ?? "(unknown)",
                    line.Quantity, FulfilledQuantity: 0m, OutstandingQuantity: line.Quantity,
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
