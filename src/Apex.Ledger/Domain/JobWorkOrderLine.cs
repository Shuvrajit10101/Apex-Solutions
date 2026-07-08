namespace Apex.Ledger.Domain;

/// <summary>
/// One tracked-component line on a <see cref="JobWorkOrder"/> (Phase 6 slice 8; RQ-47). It names the raw-material
/// (or scrap) item, its <see cref="Track"/> direction (the IN/OUT symmetry, RQ-50), the component godown, the
/// ordered <see cref="Quantity"/> and an optional per-unit <see cref="Rate"/>. Like every order artefact it
/// carries <b>no stock or accounting effect</b> — it is a commitment tracked for fulfilment against the linked
/// Material In/Out movements (RQ-47/RQ-48). When the line was auto-filled from a Slice-2 Bill of Materials the
/// resolved values are <b>snapshotted</b> here at order creation (design Open Question 3), so a later BOM edit
/// never rewrites a posted order; the BOM id lives on <see cref="JobWorkOrder.FillComponentsBomId"/> only for
/// provenance.
/// </summary>
public sealed class JobWorkOrderLine
{
    /// <summary>The raw-material / scrap <see cref="StockItem"/> tracked; required.</summary>
    public Guid ComponentStockItemId { get; }

    /// <summary>Pending to Receive vs Pending to Issue (RQ-47; the IN/OUT directionality, RQ-50).</summary>
    public JobWorkComponentTrack Track { get; }

    /// <summary>Optional due date for this component.</summary>
    public DateOnly? DueDate { get; }

    /// <summary>Optional component location (godown); <c>null</c> ⇒ resolved elsewhere.</summary>
    public Guid? GodownId { get; }

    /// <summary>Ordered component quantity (&gt; 0), in the item's base unit.</summary>
    public decimal Quantity { get; }

    /// <summary>Optional per-unit rate (paisa-exact); <c>null</c> when the line carries no rate.</summary>
    public Money? Rate { get; }

    public JobWorkOrderLine(
        Guid componentStockItemId,
        JobWorkComponentTrack track,
        decimal quantity,
        Guid? godownId = null,
        DateOnly? dueDate = null,
        Money? rate = null)
    {
        if (quantity <= 0m)
            throw new ArgumentException("A job-work order component quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Job-work order component quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate is { } r)
        {
            if (r.Amount < 0m)
                throw new ArgumentException("A job-work order component rate must be ≥ 0.", nameof(rate));
            if (!r.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Job-work order component rate {r.Amount} must be to the paisa (2 decimal places).");
        }

        ComponentStockItemId = componentStockItemId;
        Track = track;
        Quantity = quantity;
        GodownId = godownId;
        DueDate = dueDate;
        Rate = rate;
    }
}
