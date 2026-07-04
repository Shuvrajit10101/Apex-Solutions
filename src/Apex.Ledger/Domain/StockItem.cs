namespace Apex.Ledger.Domain;

/// <summary>
/// A Stock Item master — the thing that is bought, sold and held (catalog §9; requirements RQ-6). An item
/// sits <c>Under</c> exactly one <see cref="StockGroup"/>, optionally carries a <see cref="StockCategory"/>
/// (an orthogonal axis), is measured in a base <see cref="Unit"/>, and carries a per-item
/// <see cref="ValuationMethod"/> (defaulting to <see cref="StockValuationMethod.AverageCost"/>, DP-1). GST
/// fields (<see cref="HsnSacCode"/>, <see cref="IsTaxable"/>) are captured but inert until the GST slice.
/// Simple reorder fields (<see cref="ReorderLevel"/> + <see cref="MinimumOrderQuantity"/>) support the
/// Reorder-Status report; period/consumption-based reorder is deferred.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// Opening stock is expressed as one or more <see cref="StockOpeningBalance"/> allocations (per godown +
/// per batch label), each carrying quantity × rate → value; those live on the <see cref="Company"/>, not
/// inline here, mirroring how vouchers reference masters by id.
/// </remarks>
public sealed class StockItem
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>The <see cref="StockGroup"/> this item is <c>Under</c>; required.</summary>
    public Guid StockGroupId { get; set; }

    /// <summary>The optional <see cref="StockCategory"/> (independent axis); <c>null</c> ⇒ uncategorised.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>The base <see cref="Unit"/> of measure; required.</summary>
    public Guid BaseUnitId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>
    /// The costing/valuation method that drives this item's closing value (RQ-21). Defaults to
    /// <see cref="StockValuationMethod.AverageCost"/> for a new item (DP-1). The valuation computation is a
    /// later slice; this slice only persists the chosen method.
    /// </summary>
    public StockValuationMethod ValuationMethod { get; set; }

    /// <summary>GST HSN/SAC code placeholder (catalog §9) — captured but inert until the GST slice.</summary>
    public string? HsnSacCode { get; set; }

    /// <summary>Taxability placeholder (catalog §9) — captured but inert until the GST slice.</summary>
    public bool IsTaxable { get; set; }

    /// <summary>
    /// The optional per-item <b>standard cost</b> rate (RQ-21/RQ-22): the fixed per-unit rate the
    /// <see cref="StockValuationMethod.StandardCost"/> method values closing stock at, independent of actual
    /// movements. <c>null</c> ⇒ no standard rate set; a <c>StandardCost</c>-method item then falls back to its
    /// last purchase cost (documented in <c>StockValuationService</c>). Paisa-exact when set.
    /// </summary>
    public Money? StandardCost { get; set; }

    /// <summary>
    /// Simple reorder level: the on-hand quantity at/below which the item is flagged for reorder
    /// (RQ-33). <c>null</c> ⇒ no reorder level set.
    /// </summary>
    public decimal? ReorderLevel { get; set; }

    /// <summary>
    /// Minimum order quantity: the smallest quantity a reorder suggestion proposes (RQ-33). <c>null</c> ⇒
    /// none.
    /// </summary>
    public decimal? MinimumOrderQuantity { get; set; }

    public StockItem(
        Guid id,
        string name,
        Guid stockGroupId,
        Guid baseUnitId,
        Guid? categoryId = null,
        string? alias = null,
        StockValuationMethod valuationMethod = StockValuationMethod.AverageCost,
        string? hsnSacCode = null,
        bool isTaxable = false,
        decimal? reorderLevel = null,
        decimal? minimumOrderQuantity = null,
        Money? standardCost = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Stock item name is required.", nameof(name));
        if (standardCost is { } sc)
        {
            if (sc.Amount < 0m)
                throw new ArgumentException("Standard cost must be ≥ 0 when set.", nameof(standardCost));
            if (!sc.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Standard cost {sc.Amount} must be to the paisa (2 decimal places).");
        }
        if (reorderLevel is < 0m)
            throw new ArgumentException("Reorder level must be ≥ 0 when set.", nameof(reorderLevel));
        if (reorderLevel is { } rl && !Quantities.IsWithinPrecision(rl))
            throw new InvalidOperationException(
                $"Reorder level {rl} must be to {Quantities.DecimalPlaces} decimal places.");
        if (minimumOrderQuantity is < 0m)
            throw new ArgumentException("Minimum order quantity must be ≥ 0 when set.", nameof(minimumOrderQuantity));
        if (minimumOrderQuantity is { } mq && !Quantities.IsWithinPrecision(mq))
            throw new InvalidOperationException(
                $"Minimum order quantity {mq} must be to {Quantities.DecimalPlaces} decimal places.");

        Id = id;
        Name = name.Trim();
        StockGroupId = stockGroupId;
        BaseUnitId = baseUnitId;
        CategoryId = categoryId;
        Alias = alias;
        ValuationMethod = valuationMethod;
        HsnSacCode = string.IsNullOrWhiteSpace(hsnSacCode) ? null : hsnSacCode.Trim();
        IsTaxable = isTaxable;
        ReorderLevel = reorderLevel;
        MinimumOrderQuantity = minimumOrderQuantity;
        StandardCost = standardCost;
    }
}
