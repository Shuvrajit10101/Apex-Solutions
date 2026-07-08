using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Stock Summary row (design §7; catalog §16; requirements RQ-28): a stock item's opening/inward/outward/
/// closing quantities over a period and its closing value under the item's valuation method.
/// </summary>
public sealed record StockSummaryRow(
    Guid StockItemId,
    string ItemName,
    string GroupName,
    decimal OpeningQuantity,
    decimal InwardQuantity,
    decimal OutwardQuantity,
    decimal ClosingQuantity,
    Money ClosingValue,
    StockValuationMethod Method);

/// <summary>
/// The Stock Summary (catalog §16; requirements RQ-28) — per stock item over a period [from, to]: opening
/// quantity (on-hand at the day before <c>from</c>, i.e. the carried-forward book balance), inward and
/// outward quantities summed from <b>every</b> stock-affecting movement in the period (Receipt/Delivery/
/// Rejection notes, Stock-Journal source &amp; destination, and item-invoice Purchase/Sales lines), closing
/// quantity (on-hand at <c>to</c>) and closing value (via <see cref="StockValuationService"/> using the
/// item's own method), plus a grand-total closing value. The identity
/// <c>opening + inward − outward = closing</c> holds per row. A Physical-Stock count dated inside the period
/// resets on-hand to the counted quantity (DP-3) but is a checkpoint, not a linear movement; its variance
/// (counted − book-before) is therefore folded into the period's inward (found stock) or outward (shrinkage)
/// totals by sign, so a period straddling a count foots exactly. A count on/before the day before <c>from</c>
/// is already absorbed into the carried-forward opening; a count on <c>from</c> is an in-period adjustment.
/// Rows are sorted by item name. A <b>pure</b> projection — no UI, no DB.
/// </summary>
public sealed record StockSummary(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StockSummaryRow> Rows,
    Money TotalClosingValue)
{
    /// <summary>
    /// Builds the Stock Summary for the whole company. <paramref name="from"/> defaults to the company's
    /// <see cref="Company.BooksBeginFrom"/>; <paramref name="to"/> is the as-of date. Opening quantity is the
    /// on-hand as of the day before <paramref name="from"/> (the period's carried-forward balance).
    /// </summary>
    public static StockSummary Build(Company company, DateOnly to, DateOnly? from = null)
    {
        var periodFrom = from ?? company.BooksBeginFrom;
        var onHand = new InventoryLedger(company);
        var valuation = new StockValuationService(company);

        // A movement's day-before boundary: opening on-hand is the balance carried into the period.
        var openingAsOf = periodFrom.AddDays(-1);
        var movements = InventoryMovements.Between(company, from: periodFrom, to: to);

        // In-period Physical-Stock counts adjust the closing book quantity (DP-3) but are checkpoints, not
        // linear movements, so they never appear in `movements`. Their variance (counted − book-before) must be
        // folded into inward/outward by sign, or the identity opening + inward − outward = closing breaks for a
        // period straddling a count. Scope to counts dated within [from, to]: a count on `from` is a period
        // adjustment (opening already reflects counts on/before from−1).
        var countAdjustments = onHand.PhysicalStockAdjustments(to)
            .Where(a => a.Date >= periodFrom && a.AdjustmentQuantity != 0m)
            .ToList();

        var rows = new List<StockSummaryRow>();
        var total = Money.Zero;

        foreach (var item in company.StockItems)
        {
            var opening = onHand.OnHand(item.Id, openingAsOf);
            var closing = onHand.OnHand(item.Id, to);

            var inward = 0m;
            var outward = 0m;
            foreach (var m in movements)
            {
                if (m.StockItemId != item.Id) continue;
                if (m.Direction == StockDirection.Inward) inward += m.Quantity;
                else outward += m.Quantity;
            }

            // Fold each in-period count's variance in by sign: found stock (variance > 0) is inward, shrinkage
            // (variance < 0) is outward of |variance|, so the identity foots exactly against the counted closing.
            foreach (var adj in countAdjustments)
            {
                if (adj.StockItemId != item.Id) continue;
                if (adj.AdjustmentQuantity > 0m) inward += adj.AdjustmentQuantity;
                else outward += -adj.AdjustmentQuantity;
            }

            var closingValue = valuation.ClosingValue(item.Id, to).Value;
            var group = company.FindStockGroup(item.StockGroupId);
            rows.Add(new StockSummaryRow(
                item.Id, item.Name, group?.Name ?? "(unknown)",
                opening, inward, outward, closing, closingValue, item.ValuationMethod));
            total += closingValue;
        }

        rows.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase));
        return new StockSummary(periodFrom, to, rows, total);
    }
}
