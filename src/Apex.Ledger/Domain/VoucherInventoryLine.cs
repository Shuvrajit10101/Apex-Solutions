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

    /// <summary>
    /// The <b>Actual</b> movement quantity (&gt; 0), in the item's base unit (6-dp). This is the quantity that
    /// moves <b>stock</b> (on-hand). When the company's "Use separate Actual &amp; Billed quantity columns" feature
    /// (F11; <see cref="Company.UseSeparateActualBilledQuantity"/>) is off, <see cref="BilledQuantity"/> ≡
    /// <see cref="Quantity"/> and this is simply "the quantity" (Phase 6 slice 4 RQ-22/RQ-23).
    /// </summary>
    public decimal Quantity { get; }

    /// <summary>
    /// The <b>Billed</b> quantity (≥ 0), in the item's base unit (6-dp) — the quantity the <b>accounts</b> (and
    /// GST) are updated with (Book pp.145–147; Phase 6 slice 4 RQ-22..RQ-25). Defaults to <see cref="Quantity"/>
    /// (Actual) so a feature-off line is byte-identical (ER-13). It may be <b>less</b> than Actual (the common
    /// free / quantity-discount case — e.g. receive 60, billed 50), <b>zero</b> (a zero-valued free-goods line —
    /// RQ-21), or <b>greater</b> than Actual (a rare quality shortfall billed in full — RQ-25); there is no
    /// ordering constraint between the two. <see cref="Value"/> and the pairing invariant derive from this, NOT
    /// from <see cref="Quantity"/> — there is deliberately no <c>value = qty × rate</c> shortcut.
    /// </summary>
    public decimal BilledQuantity { get; }

    /// <summary>Per-unit rate (paisa-exact, ≥ 0). A <b>positive</b> rate is the norm; a <b>zero</b> rate is a
    /// legitimate <b>zero-valued</b> free-goods line (RQ-21) — whether it is <i>permitted</i> is decided by
    /// <c>VoucherValidator</c> against the voucher type's <see cref="VoucherType.AllowZeroValuedTransactions"/>
    /// flag, so the domain object no longer unconditionally forbids it (a negative rate is always rejected).</summary>
    public Money Rate { get; }

    /// <summary>Inward (Purchase) or Outward (Sales) — implied by the voucher nature and stamped by
    /// <see cref="Voucher"/> when the line is attached.</summary>
    public StockDirection Direction { get; }

    /// <summary>Optional batch/lot label (DP-10); <c>null</c> for a non-batch line.</summary>
    public string? BatchLabel { get; }

    /// <summary>
    /// The unit <see cref="Quantity"/>, <see cref="BilledQuantity"/> and <see cref="Rate"/> are ALL stated in
    /// (WI-10 Gap 2; schema v46). <c>null</c> ⇒ the item's own base unit — which is what every line written
    /// before this feature carries, so an unchanged line stays byte-identical (ER-13).
    ///
    /// <para><b>The money risk class — read before touching any consumer.</b> A site is correct only when the
    /// quantity and the rate it multiplies are expressed in the SAME unit. On this line they always are (both
    /// are per <see cref="UnitId"/>), which is exactly why <see cref="Value"/> needs no conversion: "2 Doz @
    /// ₹10" is ₹20. A consumer that normalises the quantity to the item's base unit (24 Nos) MUST also divide
    /// the rate by the same factor (<see cref="Unit.RateInBaseMeasure"/>) — pairing a base quantity with a
    /// per-displayed rate overstates by the factor (₹240), and converting a rate twice, or pairing a displayed
    /// quantity with a per-base rate, understates by it (₹1.67). Both directions are wrong; curing one by
    /// blanket-converting creates the other.</para>
    ///
    /// <para>The line deliberately holds the unit <b>id</b>, not the <see cref="Unit"/>: resolving it needs
    /// <c>Company.FindUnit</c>, which the domain object has no access to. Conversion is a CONSUMER-side
    /// responsibility here, exactly as it already is for <see cref="InventoryAllocation.UnitId"/>.</para>
    /// </summary>
    public Guid? UnitId { get; }

    public VoucherInventoryLine(
        Guid stockItemId,
        Guid godownId,
        decimal quantity,
        Money rate,
        StockDirection direction = StockDirection.Inward,
        string? batchLabel = null,
        decimal? billedQuantity = null,
        Guid? unitId = null)
    {
        if (quantity <= 0m)
            throw new ArgumentException("An item-invoice line quantity must be > 0.", nameof(quantity));
        if (!Quantities.IsWithinPrecision(quantity))
            throw new InvalidOperationException(
                $"Item-invoice line quantity {quantity} must be to {Quantities.DecimalPlaces} decimal places.");
        // Billed defaults to Actual (feature off ⇒ byte-identical, ER-13). It must be ≥ 0 (a zero-valued line
        // bills nothing) and 6-dp exact; there is NO upper bound relative to Actual (RQ-25 allows Billed > Actual).
        var billed = billedQuantity ?? quantity;
        if (billed < 0m)
            throw new ArgumentException("An item-invoice line billed quantity must be ≥ 0.", nameof(billedQuantity));
        if (!Quantities.IsWithinPrecision(billed))
            throw new InvalidOperationException(
                $"Item-invoice line billed quantity {billed} must be to {Quantities.DecimalPlaces} decimal places.");
        // A zero rate is a legitimate zero-valued free-goods line (RQ-21) — the voucher-type flag decides whether
        // it is permitted (VoucherValidator), so the domain rejects only a negative rate. Rate must stay paisa-exact.
        if (rate.Amount < 0m)
            throw new ArgumentException("Item-invoice line rate must be ≥ 0.", nameof(rate));
        if (!rate.IsPaisaExact)
            throw new InvalidOperationException(
                $"Item-invoice line rate {rate.Amount} must be to the paisa (2 decimal places).");

        StockItemId = stockItemId;
        GodownId = godownId;
        Quantity = quantity;
        BilledQuantity = billed;
        Rate = rate;
        Direction = direction;
        BatchLabel = string.IsNullOrWhiteSpace(batchLabel) ? null : batchLabel.Trim();
        UnitId = unitId;
    }

    /// <summary>The paisa-exact extended value of this line = <see cref="BilledQuantity"/> × <see cref="Rate"/> —
    /// the amount the accounting stock/purchase/sales leg (and GST) is backed by. <b>Not</b> Actual × Rate: when
    /// Billed ≠ Actual the two diverge, and a zero-valued line (Billed 0) contributes ₹0 (RQ-23).
    ///
    /// <para><b><see cref="UnitId"/> deliberately does NOT enter here.</b> Quantity and Rate are both stated in
    /// the line unit, so the product is already the correct money: "2 Doz @ ₹10" = ₹20. Applying
    /// <see cref="Unit.RateInBaseMeasure"/> to the rate while the quantity stays at 2 would yield ₹1.67 — the
    /// 12× understatement — and would feed straight into the GST taxable value and the pairing invariant
    /// (which reconciles this against the posted Sales/Purchase leg). Consumers that want a base-unit view
    /// convert BOTH sides; see <see cref="UnitId"/>.</para></summary>
    public Money Value => Money.ForexBase(Rate, BilledQuantity);

    /// <summary>The effective <b>stock valuation</b> unit cost of this inward lot = <see cref="Value"/> ÷
    /// <see cref="Quantity"/> (billed value spread over the Actual units moved). A zero-valued line yields ₹0 (so
    /// free goods drag the moving average down, RQ-24); a short-billed line yields a below-rate unit. The
    /// valuation bridge feeds this as the movement's inward rate so closing stock reconciles to the billed value
    /// to the paisa (ER-4). Exact decimal (the valuation snaps to the paisa only when it aggregates).
    ///
    /// <para><b>This rate is PER <see cref="UnitId"/>, not per base unit</b> — both operands are in the line
    /// unit. A consumer that hands it to the valuation engine beside a base-normalised quantity must first
    /// re-express it via <see cref="Unit.RateInBaseMeasure"/>; <c>ItemInvoiceStock</c> does exactly that, and
    /// publishes its <c>Movement.LandedUnitRate</c> already per base unit.</para></summary>
    public decimal StockValuationUnitRate => Quantity != 0m ? Value.Amount / Quantity : 0m;

    /// <summary>Returns a copy of this line with its <see cref="Direction"/> set to <paramref name="direction"/>
    /// (used by <see cref="Voucher"/> to stamp the voucher-nature-implied direction on attach). Carries
    /// <see cref="BilledQuantity"/> through so the Actual/Billed split survives the stamping (which runs before
    /// validation and valuation).</summary>
    public VoucherInventoryLine WithDirection(StockDirection direction) =>
        new(StockItemId, GodownId, Quantity, Rate, direction, BatchLabel, BilledQuantity, UnitId);
}
