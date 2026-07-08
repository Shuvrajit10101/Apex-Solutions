namespace Apex.Ledger.Domain;

/// <summary>
/// A dated <b>Price List</b> version scoped to a <c>(Price Level, Stock Item)</c> pair (Phase 6 slice 5; RQ-27;
/// Tally-Book pp.33–34): an <see cref="ApplicableFrom"/> date plus an ordered set of quantity <see cref="Slabs"/>
/// (<c>From qty → To qty → Rate → Discount %</c>).
/// <para>
/// <b>Append-only history (RQ-27 core).</b> "Revising" a list for a (level, item) does NOT overwrite — it
/// <b>appends a new <see cref="PriceList"/> row with a later <see cref="ApplicableFrom"/></b> and its own slab
/// set; older versions are retained, never mutated. So a (level, item) may own several dated versions. At Sales
/// entry the resolver picks the version with the latest <see cref="ApplicableFrom"/> ≤ voucher date (RQ-29 —
/// the <c>RateInForce</c> pattern), then the slab whose band contains the line quantity (RQ-28).
/// </para>
/// Framework- and DB-agnostic; the header carries the <see cref="ResolveSlab"/> helper so the TOP-RISK #5
/// boundary rule is unit-tested directly.
/// </summary>
public sealed class PriceList
{
    private readonly List<PriceListSlab> _slabs;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The <see cref="PriceLevel"/> this list belongs to; required.</summary>
    public Guid PriceLevelId { get; }

    /// <summary>The <see cref="StockItem"/> this list prices; required (inventory items only, RQ-31).</summary>
    public Guid StockItemId { get; }

    /// <summary>The date from which this version is effective (RQ-27/RQ-29); the resolution key.</summary>
    public DateOnly ApplicableFrom { get; }

    /// <summary>The quantity slabs, in ascending order (contiguous, non-overlapping — service-validated).</summary>
    public IReadOnlyList<PriceListSlab> Slabs => _slabs;

    public PriceList(Guid id, Guid priceLevelId, Guid stockItemId, DateOnly applicableFrom,
        IReadOnlyList<PriceListSlab> slabs)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        if (slabs.Count == 0)
            throw new ArgumentException("A price list must carry at least one slab.", nameof(slabs));

        Id = id;
        PriceLevelId = priceLevelId;
        StockItemId = stockItemId;
        ApplicableFrom = applicableFrom;
        _slabs = new List<PriceListSlab>(slabs);
    }

    /// <summary>
    /// The slab whose half-open band contains <paramref name="qty"/> (RQ-28, From≥ / To&lt;), or <c>null</c> when
    /// no slab does (a quantity below the first slab, or at/above the last CLOSED slab with no open-ended top).
    /// Deterministic, culture-invariant — pure decimal comparison, no float, no clock.
    /// </summary>
    public PriceListSlab? ResolveSlab(decimal qty)
    {
        foreach (var slab in _slabs)
            if (slab.Contains(qty))
                return slab;
        return null;
    }
}
