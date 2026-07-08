namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Reorder Level</b> master record (Phase 6 slice 6; requirements RQ-32..RQ-35; Tally-Book pp.158–162). It
/// governs, for a target <see cref="StockItem"/>, <see cref="StockGroup"/> or <see cref="StockCategory"/> (see
/// <see cref="Scope"/>), two figures the Reorder-Status report needs:
/// <list type="bullet">
///   <item>the <b>reorder level</b> — the stock threshold below which the item must be re-ordered; and</item>
///   <item>the <b>minimum order quantity</b> — the smallest quantity a single order may place.</item>
/// </list>
/// Each figure is independently <b>Simple</b> (a fixed typed quantity — <see cref="ReorderAdvanced"/> /
/// <see cref="MinQtyAdvanced"/> = <c>false</c>) or <b>Advanced</b> (the fixed figure is reconciled by
/// <see cref="Criteria"/> — Higher/Lower — against the item's <i>consumption over a rolling period</i>, RQ-34).
/// The two Simple/Advanced flags are independent (the screen's Alt+S vs Alt+V toggles), but a single shared
/// <see cref="PeriodCount"/>/<see cref="PeriodUnit"/> triple + <see cref="Criteria"/> governs both Advanced
/// figures (DD-1). A <b>pure</b> value object — no engine, DB or clock; the resolution/consumption logic lives in
/// <see cref="Reports.ReorderStatus"/> / <see cref="Services.InventoryLedger"/>. Quantities are exact (micros).
/// </summary>
public sealed class ReorderDefinition
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Whether this definition targets an item, a group or a category (RQ-32).</summary>
    public ReorderScope Scope { get; }

    /// <summary>The id of the <see cref="StockItem"/> / <see cref="StockGroup"/> / <see cref="StockCategory"/>
    /// this definition is attached to (per <see cref="Scope"/>); required.</summary>
    public Guid TargetId { get; }

    /// <summary>Alt+S: <c>false</c> = Simple (fixed <see cref="ReorderQuantity"/>); <c>true</c> = Advanced
    /// (reconcile the fixed figure against consumption via <see cref="Criteria"/>).</summary>
    public bool ReorderAdvanced { get; }

    /// <summary>The fixed (Simple) reorder level, or the Advanced figure's typed baseline; <c>null</c> = unset.</summary>
    public decimal? ReorderQuantity { get; }

    /// <summary>Alt+V: <c>false</c> = Simple (fixed <see cref="MinOrderQuantity"/>); <c>true</c> = Advanced.</summary>
    public bool MinQtyAdvanced { get; }

    /// <summary>The fixed (Simple) minimum order quantity, or the Advanced figure's typed baseline; <c>null</c> = unset.</summary>
    public decimal? MinOrderQuantity { get; }

    /// <summary>The rolling-window length (&gt; 0) when either figure is Advanced (shared by both, DD-1); else <c>null</c>.</summary>
    public int? PeriodCount { get; }

    /// <summary>The unit the <see cref="PeriodCount"/> is measured in (Days/Weeks/Months/Years) when Advanced; else <c>null</c>.</summary>
    public ExpiryPeriodUnit? PeriodUnit { get; }

    /// <summary>Higher/Lower reconciliation of fixed vs consumption when Advanced (RQ-35); else <c>null</c>.</summary>
    public ReorderCriteria? Criteria { get; }

    public ReorderDefinition(
        Guid id,
        ReorderScope scope,
        Guid targetId,
        bool reorderAdvanced = false,
        decimal? reorderQuantity = null,
        bool minQtyAdvanced = false,
        decimal? minOrderQuantity = null,
        int? periodCount = null,
        ExpiryPeriodUnit? periodUnit = null,
        ReorderCriteria? criteria = null)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A reorder-definition target is required.", nameof(targetId));
        ValidateQuantity(reorderQuantity, nameof(reorderQuantity));
        ValidateQuantity(minOrderQuantity, nameof(minOrderQuantity));

        // A single shared period/criteria triple governs BOTH Advanced figures (DD-1). It is required whenever
        // either figure is Advanced, and must be a positive window with a resolvable Higher/Lower criterion.
        if (reorderAdvanced || minQtyAdvanced)
        {
            if (periodCount is not { } pc || pc <= 0)
                throw new InvalidOperationException(
                    "An Advanced reorder figure requires a consumption period count > 0.");
            if (periodUnit is null)
                throw new InvalidOperationException("An Advanced reorder figure requires a period unit.");
            if (criteria is null)
                throw new InvalidOperationException(
                    "An Advanced reorder figure requires a Higher/Lower criterion.");
        }

        Id = id;
        Scope = scope;
        TargetId = targetId;
        ReorderAdvanced = reorderAdvanced;
        ReorderQuantity = reorderQuantity;
        MinQtyAdvanced = minQtyAdvanced;
        MinOrderQuantity = minOrderQuantity;
        PeriodCount = periodCount;
        PeriodUnit = periodUnit;
        Criteria = criteria;
    }

    /// <summary>
    /// The start of the consumption rolling window for a report date (RQ-34; DP-5): <paramref name="asOf"/> minus
    /// the <see cref="PeriodCount"/>/<see cref="PeriodUnit"/> period, using exact calendar arithmetic
    /// (Months/Years leap-safe). Deterministic and culture-invariant. The window is half-open
    /// <c>(WindowStart, asOf]</c> — a movement exactly on <c>WindowStart</c> is excluded, one on <c>asOf</c> is
    /// included. Throws when this definition has no period (i.e. neither figure is Advanced).
    /// </summary>
    public DateOnly WindowStart(DateOnly asOf)
    {
        if (PeriodCount is not { } count || PeriodUnit is not { } unit)
            throw new InvalidOperationException("This reorder definition has no consumption period.");
        return unit switch
        {
            ExpiryPeriodUnit.Days => asOf.AddDays(-count),
            ExpiryPeriodUnit.Weeks => asOf.AddDays(-count * 7),
            ExpiryPeriodUnit.Months => asOf.AddMonths(-count),
            ExpiryPeriodUnit.Years => asOf.AddYears(-count),
            _ => asOf.AddDays(-count),
        };
    }

    private static void ValidateQuantity(decimal? qty, string paramName)
    {
        if (qty is not { } q) return;
        if (q < 0m)
            throw new ArgumentException($"A reorder quantity must be ≥ 0 when set.", paramName);
        if (!Quantities.IsWithinPrecision(q))
            throw new InvalidOperationException(
                $"Reorder quantity {q} must be to {Quantities.DecimalPlaces} decimal places.");
    }
}
