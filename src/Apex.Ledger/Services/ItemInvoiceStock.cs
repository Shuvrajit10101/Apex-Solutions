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
    /// <param name="LandedUnitRate">The <b>landed</b> (effective) inward unit rate when this movement is an item
    /// line on a Purchase whose voucher type tracks additional costs AND additional cost actually loaded it
    /// (Phase 6 slice 3 RQ-16..RQ-19); <c>null</c> otherwise, so an untracked/zero-load movement takes the
    /// identical old valuation path (<c>LandedUnitRate ?? Allocation.Rate</c> — ER-13).</param>
    internal readonly record struct Movement(
        DateOnly Date, int Number, Guid VoucherId, InventoryAllocation Allocation, decimal? LandedUnitRate = null);

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

            // Additional Cost of Purchase (RQ-16..RQ-19): for a Purchase whose type tracks additional costs,
            // compute the per-item landed rates ONCE per voucher via the shared apportionment engine (ER-4). A
            // non-Purchase, an untracked type, or a purchase with no additional-cost ledger yields no load, so the
            // movement keeps its bare rate and the valuation is byte-identical (ER-13).
            IReadOnlyList<AdditionalCostApportionment.LandedLine>? landed = null;
            var type = company.FindVoucherType(v.TypeId);
            if (type is not null && type.TrackAdditionalCosts && type.BaseType == VoucherBaseType.Purchase)
                landed = AdditionalCostApportionment.ForPurchase(company, v);

            for (var i = 0; i < v.InventoryLines.Count; i++)
            {
                var line = v.InventoryLines[i];
                // The synthetic allocation moves stock by the ACTUAL quantity (line.Quantity) — on-hand is driven by
                // Actual, correctly and unchanged (Phase 6 slice 4 RQ-22/RQ-23).
                var alloc = new InventoryAllocation(
                    line.StockItemId, line.GodownId, line.Quantity, line.Direction,
                    rate: line.Rate, batchLabel: line.BatchLabel);
                // The effective inward unit cost for VALUATION (the LandedUnitRate override channel):
                //   • additional-cost load (slice 3, incl. composition with A/B): (billed value + apportioned
                //     share) ÷ Actual — LandedLine already computes this over the item's Actual quantity;
                //   • else Actual/Billed split or zero-valued (Billed ≠ Actual, incl. Billed 0): billed value ÷
                //     Actual, so free/short-billed goods drag the moving average down (RQ-24) and closing stock
                //     reconciles to the billed value to the paisa (ER-4);
                //   • else (feature off / Billed ≡ Actual, no load): null ⇒ keeps the bare rate ⇒ byte-identical (ER-13).
                decimal? landedRate = null;
                if (landed is { } ll && ll[i].HasLoad)
                    landedRate = ll[i].LandedUnitRate;
                else if (line.BilledQuantity != line.Quantity)
                    landedRate = line.StockValuationUnitRate;
                yield return new Movement(v.Date, v.Number, v.Id, alloc, landedRate);
            }
        }
    }
}
