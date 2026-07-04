namespace Apex.Ledger.Domain;

/// <summary>
/// One item line on an <b>Item-Invoice</b> accounting <see cref="Voucher"/> (catalog §10;
/// phase3-inventory-requirements RQ-16/RQ-17; slice 3.3b). It is the accounts↔inventory bridge: a Purchase
/// or Sales voucher run in Item-Invoice mode carries these lines so the <b>same voucher</b> both posts the
/// double-entry accounting effect (Dr/Cr <see cref="EntryLine"/>s) AND moves stock. The line names the
/// <see cref="StockItemId"/> moved, the <see cref="GodownId"/> it moves in/out of, the
/// <see cref="Quantity"/> (in the item's base unit — 6-dp), a per-unit <see cref="Rate"/> (paisa-exact) and
/// an optional <see cref="BatchLabel"/> (DP-10). Its <see cref="Direction"/> is <b>implied by the voucher
/// nature</b> — Purchase ⇒ <see cref="StockDirection.Inward"/>, Sales ⇒ <see cref="StockDirection.Outward"/> —
/// and is stamped by <see cref="Voucher"/> when the item-invoice lines are attached, so a caller never sets it
/// inconsistently.
/// </summary>
/// <remarks>
/// <para><b>Value.</b> <see cref="Value"/> = <see cref="Quantity"/> × <see cref="Rate"/>, snapped to the
/// paisa — the amount this item line contributes to the voucher's stock/purchase/sales accounting leg. The
/// <b>pairing invariant</b> (enforced in <c>VoucherValidator</c>) requires Σ of the item-line values to
/// reconcile with the voucher's stock accounting amount, so no item-invoice can create stock that is not
/// backed by an accounting posting.</para>
/// <para><b>Non-breaking.</b> These lines are OPTIONAL on a <see cref="Voucher"/>; a voucher with none behaves
/// exactly as before (all existing accounting tests are unaffected).</para>
/// </remarks>
public sealed class VoucherInventoryLine
{
    /// <summary>The <see cref="StockItem"/> moved; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The <see cref="Godown"/> the quantity moves in/out of; required.</summary>
    public Guid GodownId { get; }

    /// <summary>Movement quantity (&gt; 0), in the item's base unit (6-dp).</summary>
    public decimal Quantity { get; }

    /// <summary>Per-unit rate (paisa-exact, &gt; 0); required — an item-invoice line always carries a positive
    /// rate, so it can never move stock without a backing accounting value (a zero rate would inject unbacked
    /// stock / phantom profit that slips through the pairing invariant, so it is rejected).</summary>
    public Money Rate { get; }

    /// <summary>Inward (Purchase) or Outward (Sales) — implied by the voucher nature and stamped by
    /// <see cref="Voucher"/> when the line is attached.</summary>
    public StockDirection Direction { get; }

    /// <summary>Optional batch/lot label (DP-10); <c>null</c> for a non-batch line.</summary>
    public string? BatchLabel { get; }

    public VoucherInventoryLine(
        Guid stockItemId,
        Guid godownId,
        decimal quantity,
        Money rate,
        StockDirection direction = StockDirection.Inward,
        string? batchLabel = null)
    {
        if (quantity <= 0m)
            throw new ArgumentException("An item-invoice line quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Item-invoice line quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (rate.Amount <= 0m)
            throw new ArgumentException(
                "Item-invoice line rate must be greater than zero (a zero-rate line would move stock with no " +
                "accounting backing).", nameof(rate));
        if (!rate.IsPaisaExact)
            throw new InvalidOperationException(
                $"Item-invoice line rate {rate.Amount} must be to the paisa (2 decimal places).");

        StockItemId = stockItemId;
        GodownId = godownId;
        Quantity = quantity;
        Rate = rate;
        Direction = direction;
        BatchLabel = string.IsNullOrWhiteSpace(batchLabel) ? null : batchLabel.Trim();
    }

    /// <summary>The paisa-exact extended value of this line = <see cref="Quantity"/> × <see cref="Rate"/>.</summary>
    public Money Value => Money.ForexBase(Rate, Quantity);

    /// <summary>Returns a copy of this line with its <see cref="Direction"/> set to <paramref name="direction"/>
    /// (used by <see cref="Voucher"/> to stamp the voucher-nature-implied direction on attach).</summary>
    public VoucherInventoryLine WithDirection(StockDirection direction) =>
        new(StockItemId, GodownId, Quantity, Rate, direction, BatchLabel);
}
