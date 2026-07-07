using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One slab row of the Price List report (Phase 6 slice 5; RQ-31): a quantity band with its rate and
/// discount, under a given level → item → applicable-from version.</summary>
public sealed record PriceListReportSlabRow(
    decimal FromQty,
    decimal? ToQty,
    Money Rate,
    decimal DiscountPercent,
    Money EffectiveUnitRate);

/// <summary>One dated version block of the Price List report: an <see cref="ApplicableFrom"/> and its slabs.</summary>
public sealed record PriceListReportVersion(
    DateOnly ApplicableFrom,
    IReadOnlyList<PriceListReportSlabRow> Slabs);

/// <summary>One (level, item) group of the Price List report: the dated versions for that inventory item under
/// that price level, newest applicable-from first.</summary>
public sealed record PriceListReportItem(
    Guid PriceLevelId,
    string PriceLevelName,
    Guid StockItemId,
    string StockItemName,
    IReadOnlyList<PriceListReportVersion> Versions);

/// <summary>
/// The <b>Price List report</b> (Phase 6 slice 5; RQ-31; Tally-Book p.35) — a pure projection listing, per price
/// <see cref="PriceLevel"/>, per <b>inventory item</b>, per <see cref="PriceList.ApplicableFrom"/> version, the
/// quantity slabs (<c>From / To / Rate / Discount %</c>, plus the net effective rate). <b>Inventory items only</b>
/// — a ledger never appears. Rows are ordered by level name, then item name, then applicable-from (newest first),
/// deterministically. No UI, no DB — the projection returns a table the generic print/CSV/XLSX writers consume.
/// </summary>
public sealed record PriceListReport(IReadOnlyList<PriceListReportItem> Items)
{
    /// <summary>Builds the Price List report for the whole company (RQ-31).</summary>
    public static PriceListReport Build(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        var items = new List<PriceListReportItem>();

        foreach (var level in company.PriceLevels)
        {
            // Group this level's lists by inventory item; a list whose item no longer exists is skipped.
            var byItem = company.PriceLists
                .Where(pl => pl.PriceLevelId == level.Id && company.FindStockItem(pl.StockItemId) is not null)
                .GroupBy(pl => pl.StockItemId);

            foreach (var group in byItem)
            {
                var item = company.FindStockItem(group.Key)!;
                var versions = group
                    .OrderByDescending(pl => pl.ApplicableFrom)
                    .Select(pl => new PriceListReportVersion(
                        pl.ApplicableFrom,
                        pl.Slabs.Select(s => new PriceListReportSlabRow(
                            s.FromQty, s.ToQty, s.Rate, s.DiscountPercent, s.EffectiveUnitRate)).ToList()))
                    .ToList();

                items.Add(new PriceListReportItem(level.Id, level.Name, item.Id, item.Name, versions));
            }
        }

        items.Sort((a, b) =>
        {
            var byLevel = string.Compare(a.PriceLevelName, b.PriceLevelName, StringComparison.OrdinalIgnoreCase);
            return byLevel != 0
                ? byLevel
                : string.Compare(a.StockItemName, b.StockItemName, StringComparison.OrdinalIgnoreCase);
        });

        return new PriceListReport(items);
    }
}
