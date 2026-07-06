using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of the Batch-wise report (Phase 6 Cluster 1; requirements RQ-8): a single (item, godown, batch)
/// lot over [from, to] with its opening/inward/outward/closing quantities, its closing value at the batch's
/// authoritative unit cost (DP-8), and the batch's resolved mfg &amp; expiry dates. The empty
/// <see cref="Batch"/> is the item's non-batch stock.
/// </summary>
public sealed record BatchwiseRow(
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    string Batch,
    DateOnly? ManufacturingDate,
    DateOnly? ExpiryDate,
    decimal OpeningQuantity,
    decimal InwardQuantity,
    decimal OutwardQuantity,
    decimal ClosingQuantity,
    Money UnitCost,
    Money ClosingValue);

/// <summary>
/// The Batch-wise report (Phase 6 Cluster 1; requirements RQ-8; catalog §11) — per item, per batch:
/// inwards/outwards/closing with mfg &amp; expiry (Reports → Inventory Books → Batch). A <b>pure</b> projection
/// that reuses the shared <see cref="InventoryMovements"/> enumeration for period inward/outward (mirroring the
/// on-hand and valuation engines so quantities reconcile) and <see cref="BatchStockService"/> for the closing
/// on-hand, per-batch cost (DP-8) and resolved mfg/expiry. The identity
/// <c>opening + inward − outward = closing</c> holds per row (a Physical-Stock count folds its variance into
/// inward/outward by sign, exactly as the Stock Summary does). No UI/DB dependency.
/// </summary>
public sealed record BatchwiseReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<BatchwiseRow> Rows)
{
    /// <summary>
    /// Builds the batch-wise report over [<paramref name="from"/>, <paramref name="to"/>] for the whole company
    /// (or, when <paramref name="onlyItemId"/> is set, a single item). <paramref name="from"/> defaults to the
    /// company's <see cref="Company.BooksBeginFrom"/>. Only <b>batch-tracked</b> buckets (non-empty batch
    /// label) are reported by default; pass <paramref name="includeNonBatch"/> to include the item's
    /// non-batch stock too. Rows are sorted by item, then in the batch-stock engine's natural issue order —
    /// FEFO when the item uses expiry dates, else FIFO-by-inward (DP-1/RQ-5) — inherited directly from
    /// <see cref="BatchStockService.BatchOnHands"/> so the report row order mirrors the issue order exactly.
    /// </summary>
    public static BatchwiseReport Build(
        Company company,
        DateOnly to,
        DateOnly? from = null,
        Guid? onlyItemId = null,
        bool includeNonBatch = false)
    {
        ArgumentNullException.ThrowIfNull(company);
        var periodFrom = from ?? company.BooksBeginFrom;
        var openingAsOf = periodFrom.AddDays(-1);
        var onHand = new InventoryLedger(company);
        var batches = new BatchStockService(company);

        // Period inward/outward per (item, godown, batch) from the shared movement enumeration.
        var movements = InventoryMovements.Between(company, from: periodFrom, to: to, onlyItemId: onlyItemId);
        var periodDelta = new Dictionary<(Guid Item, Guid Godown, string Batch), (decimal In, decimal Out)>();
        foreach (var m in movements)
        {
            var key = (m.StockItemId, m.GodownId, Norm(m.BatchLabel));
            periodDelta.TryGetValue(key, out var cur);
            if (m.Direction == StockDirection.Inward) cur.In += m.Quantity;
            else cur.Out += m.Quantity;
            periodDelta[key] = cur;
        }

        // In-period Physical-Stock count variances fold into inward (found) / outward (shrinkage) by sign, so
        // opening + inward − outward = closing still foots when a period straddles a count (mirrors StockSummary).
        foreach (var adj in onHand.PhysicalStockAdjustments(to))
        {
            if (adj.Date < periodFrom || adj.AdjustmentQuantity == 0m) continue;
            if (onlyItemId is { } id && adj.StockItemId != id) continue;
            var key = (adj.StockItemId, adj.GodownId, Norm(adj.BatchLabel));
            periodDelta.TryGetValue(key, out var cur);
            if (adj.AdjustmentQuantity > 0m) cur.In += adj.AdjustmentQuantity;
            else cur.Out += -adj.AdjustmentQuantity;
            periodDelta[key] = cur;
        }

        // Rows carry the batch-stock engine's per-item issue sequence so the report row order mirrors the
        // engine's FEFO/FIFO natural order EXACTLY (ER-4 one projection) — FEFO only when the item uses expiry
        // (DP-1/RQ-5), else FIFO-by-inward. This inherits the switch-aware ordering from BatchOnHands rather than
        // re-deriving a weaker (label-based) order that could disagree with the issue engine.
        var ordered = new List<(BatchwiseRow Row, int Seq)>();
        var items = onlyItemId is { } only
            ? company.StockItems.Where(i => i.Id == only)
            : company.StockItems;

        foreach (var item in items)
        {
            var seq = 0;
            foreach (var bucket in batches.BatchOnHands(item.Id, to))
            {
                var thisSeq = seq++;
                if (!includeNonBatch && bucket.Batch.Length == 0) continue;

                var opening = onHand.OnHand(item.Id, bucket.GodownId, NullIfEmpty(bucket.Batch), openingAsOf);
                periodDelta.TryGetValue((item.Id, bucket.GodownId, bucket.Batch), out var delta);
                var closing = bucket.Quantity;

                // A batch that has no opening, no period movement and no on-hand contributes nothing — skip it
                // (e.g. a batch master with a stated godown but never used) unless it currently holds stock.
                if (opening == 0m && delta.In == 0m && delta.Out == 0m && closing == 0m)
                    continue;

                var godown = company.FindGodown(bucket.GodownId);
                ordered.Add((new BatchwiseRow(
                    item.Id, item.Name, bucket.GodownId, godown?.Name ?? "(unknown)",
                    bucket.Batch, bucket.ManufacturingDate, bucket.ExpiryDate,
                    opening, delta.In, delta.Out, closing,
                    bucket.UnitCost, bucket.Value), thisSeq));
            }
        }

        // Group by item name (stable), preserving each item's engine issue sequence within the group.
        ordered.Sort((a, b) =>
        {
            var byItem = string.Compare(a.Row.ItemName, b.Row.ItemName, StringComparison.OrdinalIgnoreCase);
            if (byItem != 0) return byItem;
            return a.Seq.CompareTo(b.Seq);
        });
        var rows = ordered.Select(x => x.Row).ToList();

        return new BatchwiseReport(periodFrom, to, rows);
    }

    private static string Norm(string? batch) => string.IsNullOrWhiteSpace(batch) ? string.Empty : batch.Trim();
    private static string? NullIfEmpty(string label) => label.Length == 0 ? null : label;
}
