using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The expiry bucket a batch falls into for the age analysis (Phase 6 Cluster 1; requirements RQ-8): already
/// past its resolved expiry, or expiring within the near-expiry window. Ordinals are display order.
/// </summary>
public enum BatchExpiryBucket
{
    /// <summary>Resolved expiry is strictly before the as-of date (past-expiry — flagged distinctly).</summary>
    Expired = 0,

    /// <summary>Resolved expiry is on/after the as-of date and within the near-expiry window.</summary>
    ExpiringSoon = 1,
}

/// <summary>
/// One row of the batch Age Analysis (Phase 6 Cluster 1; requirements RQ-8): a batch that currently holds
/// stock and either is past its expiry or expires within the window — with its resolved expiry, the whole-day
/// gap to expiry (negative once past), the on-hand quantity + value at the batch's cost (DP-8), and its
/// bucket. Non-expiring / far-off batches are not listed.
/// </summary>
public sealed record BatchAgeRow(
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    string Batch,
    DateOnly? ManufacturingDate,
    DateOnly ExpiryDate,
    int DaysToExpiry,
    decimal Quantity,
    Money UnitCost,
    Money Value,
    BatchExpiryBucket Bucket)
{
    /// <summary>True iff this batch is already past its resolved expiry (past-expiry, flagged distinctly, RQ-8).</summary>
    public bool IsExpired => Bucket == BatchExpiryBucket.Expired;
}

/// <summary>
/// The Age Analysis of expiring batches (Phase 6 Cluster 1; requirements RQ-8; catalog §11) — every batch
/// with on-hand stock as of the report date whose resolved expiry is either <b>past</b> (past-expiry, flagged
/// distinctly) or falls <b>within <paramref name="withinDays"/></b> of the report date. A <b>pure</b>
/// projection over <see cref="BatchStockService"/> (so on-hand, cost and resolved expiry all tie to the same
/// engine, ER-4). Rows are sorted soonest-expiry-first (past-expiry, being most negative, sorts first). No
/// UI/DB dependency.
/// </summary>
public sealed record BatchAgeAnalysis(
    DateOnly AsOf,
    int WithinDays,
    IReadOnlyList<BatchAgeRow> Rows)
{
    /// <summary>The rows that are already past expiry (past-expiry, flagged distinctly).</summary>
    public IReadOnlyList<BatchAgeRow> ExpiredRows => Rows.Where(r => r.IsExpired).ToList();

    /// <summary>
    /// Builds the age analysis as of <paramref name="asOf"/> for batches expiring within
    /// <paramref name="withinDays"/> days (default 30) — plus every already-expired batch that still holds
    /// stock. Optionally scoped to a single item. Only buckets with a resolved expiry and positive on-hand are
    /// listed.
    /// </summary>
    public static BatchAgeAnalysis Build(
        Company company,
        DateOnly asOf,
        int withinDays = 30,
        Guid? onlyItemId = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (withinDays < 0)
            throw new ArgumentOutOfRangeException(nameof(withinDays), "The near-expiry window must be ≥ 0 days.");

        var batches = new BatchStockService(company);
        var rows = new List<BatchAgeRow>();

        var items = onlyItemId is { } only
            ? company.StockItems.Where(i => i.Id == only)
            : company.StockItems;

        foreach (var item in items)
        {
            foreach (var bucket in batches.BatchOnHands(item.Id, asOf))
            {
                if (bucket.Batch.Length == 0) continue;      // non-batch stock has no expiry
                if (bucket.Quantity <= 0m) continue;         // no stock left to worry about
                if (bucket.ExpiryDate is not { } expiry) continue;

                var days = expiry.DayNumber - asOf.DayNumber;
                BatchExpiryBucket? which =
                    days < 0 ? BatchExpiryBucket.Expired
                    : days <= withinDays ? BatchExpiryBucket.ExpiringSoon
                    : null;
                if (which is not { } bkt) continue;          // far-off — not listed

                var godown = company.FindGodown(bucket.GodownId);
                rows.Add(new BatchAgeRow(
                    item.Id, item.Name, bucket.GodownId, godown?.Name ?? "(unknown)",
                    bucket.Batch, bucket.ManufacturingDate, expiry, days,
                    bucket.Quantity, bucket.UnitCost, bucket.Value, bkt));
            }
        }

        rows.Sort((a, b) =>
        {
            var byDays = a.DaysToExpiry.CompareTo(b.DaysToExpiry); // most negative (most overdue) first
            if (byDays != 0) return byDays;
            var byItem = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
            return byItem != 0 ? byItem : string.Compare(a.Batch, b.Batch, StringComparison.OrdinalIgnoreCase);
        });

        return new BatchAgeAnalysis(asOf, withinDays, rows);
    }
}
