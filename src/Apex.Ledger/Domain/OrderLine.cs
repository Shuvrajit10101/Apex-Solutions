namespace Apex.Ledger.Domain;

/// <summary>
/// One ordered-item line on a Purchase-Order or Sales-Order <see cref="InventoryVoucher"/> (catalog §10;
/// phase3-inventory-requirements RQ-8/RQ-9). It records the item, godown, ordered quantity and an optional
/// rate — but carries <b>no stock or accounting effect</b>: an order is an outstanding commitment only,
/// tracked for fulfilment. The stock actually moves later, on the linked Receipt/Delivery Note (or the
/// direct invoice).
/// </summary>
public sealed class OrderLine
{
    /// <summary>The <see cref="StockItem"/> ordered; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The <see cref="Godown"/> the order is expected in/out of; required.</summary>
    public Guid GodownId { get; }

    /// <summary>Ordered quantity (&gt; 0), in the item's base unit.</summary>
    public decimal Quantity { get; }

    /// <summary>Optional per-unit rate (paisa-exact); <c>null</c> when the order carries no rate.</summary>
    public Money? Rate { get; }

    public OrderLine(Guid stockItemId, Guid godownId, decimal quantity, Money? rate)
    {
        if (quantity <= 0m)
            throw new ArgumentException("An order line quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Order line quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate is { } r)
        {
            if (r.Amount < 0m)
                throw new ArgumentException("An order line rate must be ≥ 0.", nameof(rate));
            if (!r.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Order line rate {r.Amount} must be to the paisa (2 decimal places).");
        }

        StockItemId = stockItemId;
        GodownId = godownId;
        Quantity = quantity;
        Rate = rate;
    }
}
