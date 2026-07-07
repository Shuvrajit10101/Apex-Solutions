namespace Apex.Ledger.Domain;

/// <summary>
/// One quantity <b>slab</b> of a <see cref="PriceList"/> (Phase 6 slice 5; RQ-27/RQ-28; Tally-Book p.34): a
/// half-open quantity band <c>[FromQty, ToQty)</c> carrying a per-unit <see cref="Rate"/> and an optional
/// <see cref="DiscountPercent"/>. <b>From is inclusive (≥), To is exclusive (&lt; next-slab From)</b> — the
/// TOP-RISK #5 rule — so a quantity that lands exactly on a boundary resolves to the HIGHER slab
/// (qty 2 with <c>0–2</c> and <c>2–4</c> → the <c>2–4</c> slab). <see cref="ToQty"/> is <c>null</c> for an
/// open-ended top slab (any quantity at or above <see cref="FromQty"/>). Quantities are exact decimals
/// (persisted as INTEGER micros); the rate is paisa-exact <see cref="Money"/>; the discount is a percent
/// (persisted as INTEGER millis = percent × 1,000). A <c>readonly record struct</c> — value-equal and immutable.
/// </summary>
public readonly record struct PriceListSlab
{
    /// <summary>Inclusive lower quantity bound (From ≥); must be ≥ 0.</summary>
    public decimal FromQty { get; }

    /// <summary>Exclusive upper quantity bound (To &lt;); <c>null</c> = open-ended top slab.</summary>
    public decimal? ToQty { get; }

    /// <summary>Per-unit rate, paisa-exact; must be ≥ 0.</summary>
    public Money Rate { get; }

    /// <summary>Discount percent applied to <see cref="Rate"/>; <c>0 ≤ Discount &lt; 100</c>. 0 = none.</summary>
    public decimal DiscountPercent { get; }

    public PriceListSlab(decimal fromQty, decimal? toQty, Money rate, decimal discountPercent = 0m)
    {
        FromQty = fromQty;
        ToQty = toQty;
        Rate = rate;
        DiscountPercent = discountPercent;
    }

    /// <summary>
    /// True when <paramref name="qty"/> falls in this slab under the From≥ / To&lt; rule (RQ-28): at or above
    /// <see cref="FromQty"/> and strictly below <see cref="ToQty"/> (or the slab is open-ended). A boundary
    /// quantity equal to <see cref="ToQty"/> is NOT contained here — it belongs to the next (higher) slab.
    /// </summary>
    public bool Contains(decimal qty) => qty >= FromQty && (ToQty is null || qty < ToQty.Value);

    /// <summary>
    /// The net per-unit rate after discount (RQ-30; DP-A): <c>Rate × (1 − DiscountPercent/100)</c>, rounded to
    /// the paisa deterministically (culture-invariant decimal math, ER-10). Feeds the invoice auto-fill so the
    /// existing <c>value = qty × rate</c> invariant is preserved with the discounted rate.
    /// </summary>
    public Money EffectiveUnitRate =>
        new Money(Rate.Amount * (1m - DiscountPercent / 100m)).RoundToPaisa();
}
