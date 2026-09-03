using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One tracked-component line in a Job Work Order Book (Phase 6 slice 8; RQ-51). Its
/// <see cref="PendingQuantity"/> = ordered − Σ fulfilling material movements linked to the order, floored at 0.
/// A <see cref="JobWorkComponentTrack.PendingToIssue"/> component is fulfilled by the linked <b>outward</b>
/// (Material Out dispatch) movements of that item; a <see cref="JobWorkComponentTrack.PendingToReceive"/>
/// component by the linked <b>inward</b> (Material In receipt) movements.
/// </summary>
public sealed record JobWorkOrderComponentRow(
    Guid ComponentStockItemId,
    string ComponentName,
    JobWorkComponentTrack Track,
    decimal OrderedQuantity,
    decimal FulfilledQuantity,
    decimal PendingQuantity);

/// <summary>
/// One Job Work Order in the In/Out Order Book (Phase 6 slice 8; RQ-51): its header (date, number, order no.,
/// party, finished good + quantity) plus the tracked component lines with their pending figures.
/// </summary>
public sealed record JobWorkOrderRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string OrderNo,
    JobWorkDirection Direction,
    Guid? PartyId,
    string? PartyName,
    Guid FinishedGoodStockItemId,
    string FinishedGoodName,
    decimal FinishedGoodQuantity,
    Money? FinishedGoodRate,
    IReadOnlyList<JobWorkOrderComponentRow> Components,
    string FormattedNumber = "");

/// <summary>
/// One movement line in a Material In / Material Out register (Phase 6 slice 8; RQ-51): a single allocation on
/// a Material voucher (both the source/outward and destination/inward lines are listed), with the Job Work
/// order number(s) it fulfils.
/// </summary>
public sealed record MaterialRegisterRow(
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
    IReadOnlyList<string> LinkedOrderNumbers,
    string? Narration,
    string FormattedNumber = "");

/// <summary>
/// The four Job Work registers (Phase 6 slice 8; RQ-51; Book1 pp.86, 89, 93, 96) — <b>Job Work In Order
/// Book</b>, <b>Job Work Out Order Book</b>, <b>Material In Register</b>, <b>Material Out Register</b>. Each is a
/// <b>pure</b>, deterministic projection over the company's inventory vouchers (no UI, no DB): cancelled
/// vouchers are excluded and a post-dated voucher only appears once its date ≤ <c>to</c>. The register column
/// set is our design (the corpus gives these as display/edit entry points, not a fixed column list). Print/export
/// ride the Phase-5 report IO (ER-11).
/// </summary>
public static class JobWorkReports
{
    /// <summary>The Job Work In Order Book — orders where we are the worker (RQ-47/RQ-51).</summary>
    public static IReadOnlyList<JobWorkOrderRow> BuildInOrderBook(Company company, DateOnly from, DateOnly to)
        => BuildOrderBook(company, from, to, JobWorkDirection.In);

    /// <summary>The Job Work Out Order Book — orders where we are the principal (RQ-47/RQ-51).</summary>
    public static IReadOnlyList<JobWorkOrderRow> BuildOutOrderBook(Company company, DateOnly from, DateOnly to)
        => BuildOrderBook(company, from, to, JobWorkDirection.Out);

    /// <summary>The Material In Register — receipt/consumption movements on Material In vouchers (RQ-51).</summary>
    public static IReadOnlyList<MaterialRegisterRow> BuildMaterialInRegister(Company company, DateOnly from, DateOnly to)
        => BuildMaterialRegister(company, from, to, VoucherBaseType.MaterialIn);

    /// <summary>The Material Out Register — dispatch/transfer movements on Material Out vouchers (RQ-51).</summary>
    public static IReadOnlyList<MaterialRegisterRow> BuildMaterialOutRegister(Company company, DateOnly from, DateOnly to)
        => BuildMaterialRegister(company, from, to, VoucherBaseType.MaterialOut);

    // ------------------------------------------------------------------ internal

    private static IReadOnlyList<JobWorkOrderRow> BuildOrderBook(
        Company company, DateOnly from, DateOnly to, JobWorkDirection direction)
    {
        var rows = new List<JobWorkOrderRow>();
        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (v.PostDated && v.Date > to) continue;
            if (v.JobWorkOrder is not { } jwo || jwo.Direction != direction) continue;

            var type = company.FindVoucherType(v.TypeId);
            var partyName = v.PartyId is { } pid ? company.FindLedger(pid)?.Name : null;
            var fg = company.FindStockItem(jwo.FinishedGoodStockItemId);

            var components = new List<JobWorkOrderComponentRow>();
            foreach (var line in jwo.Lines)
            {
                var item = company.FindStockItem(line.ComponentStockItemId);
                var fulfilled = FulfilledQuantity(company, v.Id, line);
                var pending = line.Quantity - fulfilled;
                if (pending < 0m) pending = 0m;
                components.Add(new JobWorkOrderComponentRow(
                    line.ComponentStockItemId, item?.Name ?? "(unknown)", line.Track,
                    line.Quantity, fulfilled, pending));
            }

            rows.Add(new JobWorkOrderRow(
                v.Date, type?.Name ?? "(unknown)", v.Number, jwo.OrderNo, jwo.Direction,
                v.PartyId, partyName, jwo.FinishedGoodStockItemId, fg?.Name ?? "(unknown)",
                jwo.FinishedGoodQuantity, jwo.FinishedGoodRate, components, company.FormatVoucherNumber(v)));
        }
        rows.Sort(CompareOrders);
        return rows;
    }

    /// <summary>Σ of the fulfilling material movements linked to <paramref name="orderId"/> for one component
    /// line: a Pending-to-Issue line is fulfilled by the <b>dispatch</b> (Material Out) outward lines; a
    /// Pending-to-Receive line by the <b>receipt</b> (Material In) inward lines. The fulfilling movement is pinned
    /// to its base <b>voucher type</b>, not merely to the line direction — otherwise a consuming Material In's
    /// internal component consumption (an outward line at the third-party godown) would be double-counted with the
    /// Material Out dispatch of the same component, over-stating the issued quantity (a consumption is not a second
    /// issue). Symmetrically, a Material Out transfer's inward-to-third-party line is not a receipt.</summary>
    private static decimal FulfilledQuantity(Company company, Guid orderId, JobWorkOrderLine line)
    {
        var (fulfilling, fulfillingBaseType) = line.Track == JobWorkComponentTrack.PendingToIssue
            ? (StockDirection.Outward, VoucherBaseType.MaterialOut)
            : (StockDirection.Inward, VoucherBaseType.MaterialIn);

        var total = 0m;
        foreach (var mv in company.InventoryVouchers)
        {
            if (mv.Cancelled) continue;
            if (!mv.OrderLinks.Contains(orderId)) continue;
            if (company.FindVoucherType(mv.TypeId)?.BaseType != fulfillingBaseType) continue;
            var lines = fulfilling == StockDirection.Outward ? mv.Allocations : mv.DestinationAllocations;
            foreach (var a in lines)
                if (a.StockItemId == line.ComponentStockItemId && a.Direction == fulfilling)
                    total += QuantityInBase(company, a);
        }
        return total;
    }

    private static IReadOnlyList<MaterialRegisterRow> BuildMaterialRegister(
        Company company, DateOnly from, DateOnly to, VoucherBaseType baseType)
    {
        var rows = new List<MaterialRegisterRow>();
        foreach (var v in company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (v.PostDated && v.Date > to) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != baseType) continue;

            var partyName = v.PartyId is { } pid ? company.FindLedger(pid)?.Name : null;
            var linkedNos = ResolveLinkedOrderNumbers(company, v.OrderLinks);

            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                var item = company.FindStockItem(a.StockItemId);
                var godown = company.FindGodown(a.GodownId);
                var qtyBase = QuantityInBase(company, a);
                var rateBase = RateInBase(company, a);
                var value = rateBase is { } r ? Money.ForexBase(r, qtyBase) : Money.Zero;
                // The Rate column is emitted PER BASE UNIT — the same unit Quantity is in — so Rate × Quantity
                // foots to Value (see InventoryRegisters for the same rule).
                rows.Add(new MaterialRegisterRow(
                    v.Date, type.Name, v.Number, a.StockItemId, item?.Name ?? "(unknown)",
                    a.GodownId, godown?.Name ?? "(unknown)", qtyBase, a.Direction,
                    rateBase, value, a.BatchLabel, v.PartyId, partyName, linkedNos, v.Narration,
                    company.FormatVoucherNumber(v)));
            }
        }
        rows.Sort((x, y) =>
        {
            var byDate = x.Date.CompareTo(y.Date);
            if (byDate != 0) return byDate;
            var byNum = x.Number.CompareTo(y.Number);
            return byNum != 0 ? byNum : string.Compare(x.ItemName, y.ItemName, StringComparison.OrdinalIgnoreCase);
        });
        return rows;
    }

    private static IReadOnlyList<string> ResolveLinkedOrderNumbers(Company company, IReadOnlyList<Guid> orderIds)
    {
        var nos = new List<string>();
        foreach (var id in orderIds)
            if (company.FindInventoryVoucher(id)?.JobWorkOrder is { } jwo)
                nos.Add(jwo.OrderNo);
        return nos;
    }

    private static int CompareOrders(JobWorkOrderRow a, JobWorkOrderRow b)
    {
        var byDate = a.Date.CompareTo(b.Date);
        if (byDate != 0) return byDate;
        var byNum = a.Number.CompareTo(b.Number);
        return byNum != 0 ? byNum : string.Compare(a.OrderNo, b.OrderNo, StringComparison.OrdinalIgnoreCase);
    }

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
