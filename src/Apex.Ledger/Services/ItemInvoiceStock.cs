using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The bridge that folds <b>Item-Invoice</b> accounting-voucher stock lines into the inventory engines
/// (catalog §10; phase3-inventory-requirements RQ-16/RQ-17; slice 3.3b). A Purchase/Sales voucher run in
/// item-invoice mode carries <see cref="Voucher.InventoryLines"/> whose stock movements must be counted on-hand
/// and valued <b>alongside</b> the pure-stock <see cref="InventoryVoucher"/> movements — so that a stock
/// inward/outward recorded on an item invoice always drives closing stock, and (because the accounting leg is
/// posted in the same voucher, atomically) can never invent phantom profit. This helper exposes the item-invoice
/// lines as synthetic <see cref="InventoryAllocation"/>s under a stable ordering key, honouring the SAME as-of /
/// post-dated / cancelled / optional conventions the accounting side uses
/// (<see cref="Reports.LedgerBalances.CountsAsOf"/>), so <see cref="InventoryLedger"/> and
/// <see cref="StockValuationService"/> stay in exact agreement.
/// </summary>
internal static class ItemInvoiceStock
{
    /// <summary>
    /// One item-invoice stock movement lifted from an accounting <see cref="Voucher"/>: the ordering key
    /// (date/number/id) mirroring the inventory replay ordering, plus the synthetic allocation. The line's
    /// direction was stamped from the voucher nature at posting time (Purchase ⇒ Inward, Sales ⇒ Outward).
    /// </summary>
    internal readonly record struct Movement(DateOnly Date, int Number, Guid VoucherId, InventoryAllocation Allocation);

    /// <summary>
    /// Whether an item-invoice accounting voucher contributes stock at <paramref name="asOf"/> — the same rule
    /// the real books use (<see cref="Reports.LedgerBalances.CountsAsOf"/>): cancelled/optional never count, a
    /// not-yet-due post-dated voucher does not count, a voucher dated after <paramref name="asOf"/> is excluded.
    /// The presence of item lines on a Purchase/Sales voucher <b>is</b> item-invoice mode, so — unlike a
    /// pure-stock voucher — the movement counts on the item lines' presence, not the type's <c>AffectsStock</c>
    /// flag. Only Purchase/Sales base kinds are valid carriers (the validator enforces this at post time).
    /// </summary>
    internal static bool Counts(Company company, Voucher v, DateOnly asOf)
    {
        if (!v.HasInventoryLines) return false;
        if (v.Cancelled || v.Optional) return false;
        if (v.Date > asOf) return false;
        if (v.PostDated && v.Date > asOf) return false; // redundant with the date bound, kept for clarity
        var type = company.FindVoucherType(v.TypeId);
        return type is not null && type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales;
    }

    /// <summary>
    /// Every item-invoice stock movement in the company that counts as of <paramref name="asOf"/>, projected to
    /// synthetic <see cref="InventoryAllocation"/>s. Optionally skips one voucher (used to model a would-be
    /// delete/cancel in the no-negative guard). The item-line quantity is already in the item's base unit.
    /// </summary>
    internal static IEnumerable<Movement> Movements(Company company, DateOnly asOf, Guid? excludeVoucherId = null)
    {
        foreach (var v in company.Vouchers)
        {
            if (excludeVoucherId is { } ex && v.Id == ex) continue;
            if (!Counts(company, v, asOf)) continue;
            foreach (var line in v.InventoryLines)
            {
                var alloc = new InventoryAllocation(
                    line.StockItemId, line.GodownId, line.Quantity, line.Direction,
                    rate: line.Rate, batchLabel: line.BatchLabel);
                yield return new Movement(v.Date, v.Number, v.Id, alloc);
            }
        }
    }
}
