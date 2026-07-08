using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Godown Summary row (catalog §16; requirements RQ-29): the closing quantity and apportioned closing
/// value of one stock item located at one godown, as of a date.
/// </summary>
public sealed record GodownSummaryRow(
    Guid GodownId,
    string GodownName,
    Guid StockItemId,
    string ItemName,
    decimal ClosingQuantity,
    Money ClosingValue);

/// <summary>
/// The Godown Summary (catalog §16; requirements RQ-29) — per godown, the closing quantity and value of each
/// stock item located there as of <c>asOf</c>.
/// <para><b>Valuation approach (documented).</b> The item's total closing value is computed once, company-wide,
/// by <see cref="StockValuationService"/> under the item's own valuation method; that yields a single closing
/// <i>unit</i> value = total-closing-value ÷ total-closing-quantity. Each godown's value is then that unit
/// value × the godown's closing quantity (apportion-by-quantity). This is a defensible, method-agnostic
/// apportionment: it keeps Σ over godowns of the apportioned values reconciled with the item's company-wide
/// closing value (rounding the last godown to absorb the paisa remainder), without re-running per-godown FIFO/
/// LIFO layer tracking (which the engine does not maintain per location). Rows with zero closing quantity are
/// omitted. A <b>pure</b> projection — no UI, no DB.</para>
/// </summary>
public sealed record GodownSummary(
    DateOnly AsOf,
    IReadOnlyList<GodownSummaryRow> Rows,
    Money TotalClosingValue)
{
    /// <summary>Builds the Godown Summary for the whole company as of <paramref name="asOf"/>.</summary>
    public static GodownSummary Build(Company company, DateOnly asOf)
    {
        var onHand = new InventoryLedger(company);
        var valuation = new StockValuationService(company);

        var rows = new List<GodownSummaryRow>();
        var total = Money.Zero;

        foreach (var item in company.StockItems)
        {
            var itemClosing = valuation.ClosingValue(item.Id, asOf);
            var itemQty = itemClosing.Quantity;
            if (itemQty <= 0m) continue; // nothing on hand ⇒ nothing to place in any godown

            var unitValue = itemClosing.Value.Amount / itemQty; // closing unit value (method-agnostic)

            // The godowns holding this item, in a stable order (by godown name then id).
            var godownQtys = new List<(Godown Godown, decimal Qty)>();
            foreach (var godown in company.Godowns)
            {
                var qty = onHand.OnHand(item.Id, godown.Id, asOf);
                if (qty <= 0m) continue;
                godownQtys.Add((godown, qty));
            }
            godownQtys.Sort((a, b) =>
            {
                var byName = string.Compare(a.Godown.Name, b.Godown.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Godown.Id.CompareTo(b.Godown.Id);
            });

            // Apportion the item's paisa-exact closing value across its godowns; the last godown absorbs the
            // rounding remainder so Σ godown values == the item's company-wide closing value exactly.
            var allocated = Money.Zero;
            for (var i = 0; i < godownQtys.Count; i++)
            {
                var (godown, qty) = godownQtys[i];
                Money value;
                if (i == godownQtys.Count - 1)
                    value = itemClosing.Value - allocated; // remainder
                else
                    value = Money.ForexBase(new Money(unitValue), qty); // qty × unit value, paisa-snapped
                allocated += value;
                rows.Add(new GodownSummaryRow(godown.Id, godown.Name, item.Id, item.Name, qty, value));
                total += value;
            }
        }

        return new GodownSummary(asOf, rows, total);
    }
}
