using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The shared, <b>pure</b> movement-enumeration used by the inventory report projections (catalog §16;
/// phase3-inventory-requirements RQ-28..RQ-33). It flattens every stock-affecting movement — pure-stock
/// <see cref="InventoryVoucher"/> allocations (Receipt/Delivery/Rejection, Stock-Journal source &amp;
/// destination) <b>and</b> item-invoice (Purchase/Sales) <see cref="VoucherInventoryLine"/>s — into one
/// chronological, deterministic sequence, mirroring the exact ordering, as-of, post-dated and cancelled
/// conventions the on-hand engine (<see cref="InventoryLedger"/>) and the valuation engine
/// (<see cref="StockValuationService"/>) use, so quantities always reconcile. Orders (PO/SO) never move
/// stock and are never emitted here; Physical-Stock counts are surfaced separately via
/// <see cref="InventoryLedger.PhysicalStockAdjustments"/>. Quantities are normalised to the item's base unit
/// (DP-6). No UI/DB/clock dependency.
/// </summary>
internal static class InventoryMovements
{
    /// <summary>The origin of a flattened stock movement (which register/source it came from).</summary>
    internal enum Source
    {
        ReceiptNote,
        DeliveryNote,
        RejectionIn,
        RejectionOut,
        StockJournalSource,
        StockJournalDestination,
        ItemInvoicePurchase,
        ItemInvoiceSales,
        Other,
    }

    /// <summary>
    /// One flattened stock movement: its date/number, the voucher type name, the moved item/godown/batch,
    /// the base-unit quantity (always &gt; 0), the movement direction, an optional per-unit rate, the party
    /// (if any) and narration, plus its <see cref="Source"/>. Sorting key is (date, number, id).
    /// </summary>
    internal sealed record Movement(
        DateOnly Date,
        int Number,
        Guid VoucherId,
        Guid VoucherTypeId,
        string VoucherTypeName,
        VoucherBaseType BaseType,
        Source Origin,
        Guid StockItemId,
        Guid GodownId,
        string? BatchLabel,
        decimal Quantity,
        StockDirection Direction,
        Money? Rate,
        Guid? PartyId,
        string? Narration);

    /// <summary>
    /// Every stock-affecting movement in the company dated within [<paramref name="from"/>, <paramref name="to"/>]
    /// (inclusive), honouring the as-of / post-dated / cancelled conventions. When <paramref name="from"/> is
    /// <c>null</c> there is no lower bound (used for opening-quantity replays that need everything up to a date).
    /// Ordered by (date, number, id).
    /// </summary>
    internal static IReadOnlyList<Movement> Between(Company company, DateOnly? from, DateOnly to,
        Guid? onlyItemId = null)
    {
        var result = new List<Movement>();

        foreach (var v in company.InventoryVouchers)
        {
            if (!CountsPureStock(company, v, to)) continue;
            if (from is { } lo && v.Date < lo) continue;

            var type = company.FindVoucherType(v.TypeId);
            var typeName = type?.Name ?? "(unknown)";
            var baseType = type?.BaseType ?? VoucherBaseType.Journal;

            foreach (var a in v.Allocations)
            {
                if (onlyItemId is { } id && a.StockItemId != id) continue;
                result.Add(new Movement(v.Date, v.Number, v.Id, v.TypeId, typeName, baseType,
                    OriginOfAllocation(baseType, isDestination: false),
                    a.StockItemId, a.GodownId, a.BatchLabel, QuantityInBase(company, a), a.Direction,
                    a.Rate, v.PartyId, v.Narration));
            }

            foreach (var a in v.DestinationAllocations)
            {
                if (onlyItemId is { } id && a.StockItemId != id) continue;
                result.Add(new Movement(v.Date, v.Number, v.Id, v.TypeId, typeName, baseType,
                    OriginOfAllocation(baseType, isDestination: true),
                    a.StockItemId, a.GodownId, a.BatchLabel, QuantityInBase(company, a), a.Direction,
                    a.Rate, v.PartyId, v.Narration));
            }
        }

        // Item-invoice (Purchase/Sales) stock lines lifted from balanced accounting vouchers.
        foreach (var v in company.Vouchers)
        {
            if (!ItemInvoiceCounts(company, v, to)) continue;
            if (from is { } lo && v.Date < lo) continue;

            var type = company.FindVoucherType(v.TypeId);
            var typeName = type?.Name ?? "(unknown)";
            var baseType = type?.BaseType ?? VoucherBaseType.Journal;
            var origin = baseType == VoucherBaseType.Sales ? Source.ItemInvoiceSales : Source.ItemInvoicePurchase;

            foreach (var line in v.InventoryLines)
            {
                if (onlyItemId is { } id && line.StockItemId != id) continue;
                result.Add(new Movement(v.Date, v.Number, v.Id, v.TypeId, typeName, baseType, origin,
                    line.StockItemId, line.GodownId, line.BatchLabel, line.Quantity, line.Direction,
                    line.Rate, v.PartyId, v.Narration));
            }
        }

        result.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byNum = a.Number.CompareTo(b.Number);
            return byNum != 0 ? byNum : a.VoucherId.CompareTo(b.VoucherId);
        });
        return result;
    }

    private static Source OriginOfAllocation(VoucherBaseType baseType, bool isDestination) => baseType switch
    {
        VoucherBaseType.ReceiptNote => Source.ReceiptNote,
        VoucherBaseType.DeliveryNote => Source.DeliveryNote,
        VoucherBaseType.RejectionIn => Source.RejectionIn,
        VoucherBaseType.RejectionOut => Source.RejectionOut,
        VoucherBaseType.StockJournal => isDestination ? Source.StockJournalDestination : Source.StockJournalSource,
        _ => Source.Other,
    };

    /// <summary>Mirrors <see cref="InventoryLedger"/>'s counting rule for pure-stock vouchers.</summary>
    private static bool CountsPureStock(Company company, InventoryVoucher v, DateOnly asOf)
    {
        if (v.Cancelled) return false;
        if (v.Date > asOf) return false;
        var type = company.FindVoucherType(v.TypeId);
        // Physical-Stock counts are checkpoints, surfaced separately — they are not linear movements.
        return type is not null && type.AffectsStock && type.BaseType != VoucherBaseType.PhysicalStock;
    }

    /// <summary>Mirrors <see cref="ItemInvoiceStock"/>'s counting rule for item-invoice vouchers.</summary>
    private static bool ItemInvoiceCounts(Company company, Voucher v, DateOnly asOf)
    {
        if (!v.HasInventoryLines) return false;
        if (v.Cancelled || v.Optional) return false;
        if (v.Date > asOf) return false;
        var type = company.FindVoucherType(v.TypeId);
        return type is not null && type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales;
    }

    private static decimal QuantityInBase(Company company, InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }
}
