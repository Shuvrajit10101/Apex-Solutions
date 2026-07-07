using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The resolved auto-fill price for one Sales item line (Phase 6 slice 5; RQ-28/RQ-30): the slab
/// <see cref="Rate"/>, its <see cref="DiscountPercent"/>, and the net <see cref="EffectiveUnitRate"/>
/// (<c>Rate × (1 − Discount/100)</c>, paisa-rounded). Consumed ONLY by the ViewModel auto-fill and the Price
/// List report — never by posting/valuation.
/// </summary>
public readonly record struct ResolvedPrice(Money Rate, decimal DiscountPercent, Money EffectiveUnitRate);

/// <summary>
/// The <b>Price resolver</b> (Phase 6 slice 5; RQ-28/RQ-29; Tally-Book p.34) — a pure, deterministic,
/// framework-agnostic lookup that supplies the Sales-line auto-fill default. Given a company, a
/// <see cref="PriceLevel"/>, a stock item, a line quantity and the voucher date it:
/// <list type="number">
///   <item>selects, among the (level, item) price-list versions, the one with the <b>latest
///     <see cref="PriceList.ApplicableFrom"/> ≤ voucher date</b> (RQ-29 — the <c>Company.RateInForce</c>
///     pattern);</item>
///   <item>on that version resolves the slab whose half-open band contains the quantity (RQ-28, From≥ / To&lt;);</item>
///   <item>returns the slab's <see cref="ResolvedPrice"/>, or <c>null</c> when there is no applicable version
///     or no matching slab (the auto-fill then leaves the line blank).</item>
/// </list>
/// <b>Zero engine coupling to posting:</b> this resolver is consumed only by the auto-fill ViewModel and the
/// report — it never enters <c>InventoryPostingService</c>, <c>VoucherValidator</c>,
/// <c>StockValuationService</c> or <c>ItemInvoiceStock</c>, so posting/valuation invariants are untouched.
/// Culture-invariant integer-scale (micros/paisa/millis) comparison — no float, no clock (ER-10).
/// </summary>
public static class PriceResolver
{
    /// <summary>
    /// Resolves the auto-fill price for <paramref name="levelId"/> / <paramref name="itemId"/> at
    /// <paramref name="qty"/> on <paramref name="voucherDate"/>, or <c>null</c> when none applies (RQ-28/RQ-29).
    /// </summary>
    public static ResolvedPrice? Resolve(Company company, Guid levelId, Guid itemId, decimal qty, DateOnly voucherDate)
    {
        ArgumentNullException.ThrowIfNull(company);

        // RQ-29: the latest-dated version on/before the voucher date wins (RateInForce pattern; ties impossible
        // per the RQ-27 strict-increasing guard).
        PriceList? best = null;
        foreach (var pl in company.PriceListsFor(levelId, itemId))
        {
            if (pl.ApplicableFrom > voucherDate) continue;
            if (best is null || pl.ApplicableFrom > best.ApplicableFrom) best = pl;
        }
        if (best is null) return null;

        // RQ-28: the slab whose half-open band contains the quantity (From≥ / To<).
        if (best.ResolveSlab(qty) is not { } slab) return null;

        return new ResolvedPrice(slab.Rate, slab.DiscountPercent, slab.EffectiveUnitRate);
    }
}
