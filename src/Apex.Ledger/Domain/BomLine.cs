namespace Apex.Ledger.Domain;

/// <summary>
/// One component/output line of a <see cref="BillOfMaterials"/> (Phase 6 Cluster 2; requirements RQ-9, DP-3).
/// It names the <see cref="ComponentStockItemId"/> and its <see cref="LineType"/>
/// (<see cref="BomLineType.Component"/> consumed, or a By-Product/Co-Product/Scrap output carved out of the
/// finished-good cost), an optional consumption/production <see cref="GodownId"/> (<c>null</c> ⇒ resolve at
/// manufacture), a <see cref="QuantityPerBlock"/> expressed <b>per unit-of-manufacture block</b> (RQ-12), and
/// — for a carve-out line — an optional value basis (a per-unit <see cref="Rate"/> OR a
/// <see cref="PercentOfFinishedGoodCost"/>, DP-3).
/// </summary>
/// <remarks>
/// <para><b>Quantity is per BLOCK, not per finished unit (RQ-12).</b> <see cref="QuantityPerBlock"/> is the
/// quantity for one <see cref="BillOfMaterials.UnitOfManufacture"/> block. Producing <c>N</c> finished units
/// consumes/produces <c>QuantityPerBlock ÷ UnitOfManufacture × N</c> — the manufacturing engine does the
/// scaling, so a unit-of-manufacture &gt; 1 divides correctly (the classic off-by-scale guard, subtlety e).</para>
/// <para><b>Carve-out value basis (DP-3).</b> For a By-Product/Co-Product/Scrap line the value carved out of
/// the finished-good cost is: <see cref="Rate"/> × produced-qty when a rate is set; else
/// <see cref="PercentOfFinishedGoodCost"/> of the finished good's pre-carve cost when a percent is set; else
/// the component item's standard cost × produced-qty (the "default the item standard cost where blank" rule,
/// RQ-13/DP-3). A <see cref="BomLineType.Component"/> line ignores these value fields — its cost comes from the
/// live valuation engine (batch-aware) at manufacture time.</para>
/// <para>Money is exact decimal rupees in-memory (integer paisa at the boundary); quantity is exact 6-dp.</para>
/// </remarks>
public sealed class BomLine
{
    /// <summary>Whether this line is a consumed component or a carved-out By-Product/Co-Product/Scrap (RQ-9).</summary>
    public BomLineType LineType { get; }

    /// <summary>The component/output <see cref="StockItem"/> for this line; required.</summary>
    public Guid ComponentStockItemId { get; }

    /// <summary>
    /// The consumption (Component) or production (carve-out) <see cref="Godown"/>; <c>null</c> ⇒ resolve at
    /// manufacture (defaults to the manufacturing journal's consumption/production godown, RQ-14).
    /// </summary>
    public Guid? GodownId { get; }

    /// <summary>
    /// The quantity for one <see cref="BillOfMaterials.UnitOfManufacture"/> block (RQ-9/RQ-12), in the
    /// component item's base unit, strictly &gt; 0 and 6-dp exact.
    /// </summary>
    public decimal QuantityPerBlock { get; }

    /// <summary>
    /// The optional per-unit carve-out rate for a By-Product/Co-Product/Scrap line (DP-3, paisa-exact, ≥ 0);
    /// <c>null</c> ⇒ no explicit rate. Ignored for a <see cref="BomLineType.Component"/> line.
    /// </summary>
    public Money? Rate { get; }

    /// <summary>
    /// The optional carve-out value as a <b>percentage of the finished good's pre-carve cost</b> for a
    /// By-Product/Co-Product/Scrap line (DP-3, ≥ 0, e.g. 5m ⇒ 5%); <c>null</c> ⇒ not a %-basis line. Ignored for
    /// a <see cref="BomLineType.Component"/> line and superseded by <see cref="Rate"/> when both are set.
    /// </summary>
    public decimal? PercentOfFinishedGoodCost { get; }

    /// <summary>True iff this line is a consumed component (its cost adds to the finished-good value).</summary>
    public bool IsComponent => LineType == BomLineType.Component;

    /// <summary>True iff this line is a carved-out output (By-Product/Co-Product/Scrap) — its value reduces the
    /// finished good's residual cost (DP-3).</summary>
    public bool IsCarveOut => LineType is BomLineType.ByProduct or BomLineType.CoProduct or BomLineType.Scrap;

    public BomLine(
        BomLineType lineType,
        Guid componentStockItemId,
        decimal quantityPerBlock,
        Guid? godownId = null,
        Money? rate = null,
        decimal? percentOfFinishedGoodCost = null)
    {
        if (quantityPerBlock <= 0m)
            throw new ArgumentException("A BOM line quantity (per block) must be > 0.", nameof(quantityPerBlock));
        if (!Quantities.IsWithinPrecision(quantityPerBlock))
            throw new InvalidOperationException(
                $"BOM line quantity {quantityPerBlock} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate is { } r)
        {
            if (r.Amount < 0m)
                throw new ArgumentException("A BOM carve-out rate must be ≥ 0 when set.", nameof(rate));
            if (!r.IsPaisaExact)
                throw new InvalidOperationException(
                    $"BOM carve-out rate {r.Amount} must be to the paisa (2 decimal places).");
        }
        if (percentOfFinishedGoodCost is < 0m)
            throw new ArgumentException("A BOM carve-out percentage must be ≥ 0 when set.",
                nameof(percentOfFinishedGoodCost));
        // A value basis only makes sense for a carve-out line; a Component line never carries one.
        if (lineType == BomLineType.Component && (rate is not null || percentOfFinishedGoodCost is not null))
            throw new InvalidOperationException(
                "A Component BOM line cannot carry a carve-out rate or percentage (its cost comes from valuation).");

        LineType = lineType;
        ComponentStockItemId = componentStockItemId;
        QuantityPerBlock = quantityPerBlock;
        GodownId = godownId;
        Rate = rate;
        PercentOfFinishedGoodCost = percentOfFinishedGoodCost;
    }
}
