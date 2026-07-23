using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One chronological movement row in a Stock Item Movement report (catalog §16; requirements RQ-28
/// drill-down): a single inward/outward event with its running balance quantity and running value.
/// </summary>
public sealed record StockItemMovementRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    decimal InwardQuantity,
    decimal OutwardQuantity,
    decimal RunningQuantity,
    Money? Rate,
    Money RunningValue,
    Guid? PartyId,
    string? Narration,
    string FormattedNumber = "");

/// <summary>
/// The Stock Item Movement report (catalog §16; requirements RQ-28) — the Stock-Summary drill target for a
/// single stock item over [from, to]: an ordered list of every inward/outward movement (Receipt/Delivery/
/// Rejection, Stock-Journal source &amp; destination, item-invoice Purchase/Sales) with the running balance
/// quantity after each, opening and closing quantities, and the closing value under the item's valuation
/// method. An in-period Physical-Stock count is a checkpoint, not a linear movement, so its variance
/// (counted − book-before) is surfaced as a synthetic "Physical Stock" adjustment row — inward for found
/// stock, outward for shrinkage — so the running balance steps to and ends at the counted on-hand (DP-3).
/// <para><b>Running value.</b> Each row's <see cref="StockItemMovementRow.RunningValue"/> is the item's
/// paisa-exact closing value <i>as of that row's date</i> (via <see cref="StockValuationService"/>), so the
/// running value column always ties to the valuation engine and the on-hand quantity. A <b>pure</b>
/// projection — no UI, no DB.</para>
/// </summary>
public sealed record StockItemMovement(
    Guid StockItemId,
    string ItemName,
    DateOnly From,
    DateOnly To,
    decimal OpeningQuantity,
    decimal ClosingQuantity,
    Money ClosingValue,
    IReadOnlyList<StockItemMovementRow> Rows)
{
    /// <summary>
    /// Builds the movement journal for one item. <paramref name="from"/> defaults to the company's
    /// <see cref="Company.BooksBeginFrom"/>. Opening quantity is the on-hand carried into the period
    /// (day before <paramref name="from"/>); the running balance begins there and each movement steps it.
    /// </summary>
    public static StockItemMovement Build(Company company, Guid stockItemId, DateOnly to, DateOnly? from = null)
    {
        var item = company.FindStockItem(stockItemId)
            ?? throw new InvalidOperationException($"Stock item {stockItemId} not found.");

        var periodFrom = from ?? company.BooksBeginFrom;
        var onHand = new InventoryLedger(company);
        var valuation = new StockValuationService(company);

        var opening = onHand.OnHand(stockItemId, periodFrom.AddDays(-1));
        var closingQty = onHand.OnHand(stockItemId, to);
        var closingValue = valuation.ClosingValue(stockItemId, to).Value;

        var movements = InventoryMovements.Between(company, from: periodFrom, to: to, onlyItemId: stockItemId);

        // In-period Physical-Stock counts adjust the closing book quantity (DP-3) but are checkpoints, not linear
        // movements, so they never appear in `movements`. Surface each in-period count's variance as a synthetic
        // "Physical Stock" adjustment row (found stock → inward, shrinkage → outward of |variance|) so the running
        // balance steps to — and ends at — the counted on-hand. Scope to counts dated within [from, to] (a count on
        // `from` is a period adjustment; opening already reflects counts on/before from−1). Zero-variance counts add
        // no row. Within a date, the count checkpoints AFTER that date's movements, mirroring the on-hand engine.
        var physicalTypeName = company.VoucherTypes
            .FirstOrDefault(t => t.BaseType == VoucherBaseType.PhysicalStock)?.Name ?? "Physical Stock";
        var countAdjustments = onHand.PhysicalStockAdjustments(to)
            .Where(a => a.StockItemId == stockItemId && a.Date >= periodFrom && a.AdjustmentQuantity != 0m)
            .Select(a => (a.Date, a.AdjustmentQuantity))
            .ToList();

        var rows = new List<StockItemMovementRow>();
        var running = opening;

        // Merge real movements (already sorted by date, then number/id) with count adjustments, ordering counts
        // last within their date so the running balance absorbs the day's movements before the checkpoint.
        var mi = 0;
        var ci = 0;
        while (mi < movements.Count || ci < countAdjustments.Count)
        {
            var takeMovement = ci >= countAdjustments.Count ||
                (mi < movements.Count && movements[mi].Date <= countAdjustments[ci].Date);
            if (takeMovement)
            {
                var m = movements[mi++];
                decimal inward = 0m, outward = 0m;
                if (m.Direction == StockDirection.Inward) { inward = m.Quantity; running += m.Quantity; }
                else { outward = m.Quantity; running -= m.Quantity; }

                var runningValue = valuation.ClosingValue(stockItemId, m.Date).Value;
                rows.Add(new StockItemMovementRow(
                    m.Date, m.VoucherTypeName, m.Number, inward, outward, running, m.Rate, runningValue,
                    m.PartyId, m.Narration, m.FormattedNumber));
            }
            else
            {
                var (date, adjustment) = countAdjustments[ci++];
                decimal inward = adjustment > 0m ? adjustment : 0m;
                decimal outward = adjustment < 0m ? -adjustment : 0m;
                running += adjustment;

                var runningValue = valuation.ClosingValue(stockItemId, date).Value;
                rows.Add(new StockItemMovementRow(
                    date, physicalTypeName, 0, inward, outward, running, Rate: null, runningValue,
                    PartyId: null, Narration: null));
            }
        }

        return new StockItemMovement(stockItemId, item.Name, periodFrom, to, opening, closingQty, closingValue, rows);
    }
}
