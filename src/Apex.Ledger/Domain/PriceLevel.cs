namespace Apex.Ledger.Domain;

/// <summary>
/// A named <b>Price Level</b> (Wholesale, Retail…) — a bare per-company master (Phase 6 slice 5; RQ-26;
/// Tally-Book p.33). A level is nothing more than an <see cref="Id"/> + a <see cref="Name"/>: it groups the
/// dated <see cref="PriceList"/> versions that supply the auto-fill rate at Sales entry, and a party ledger may
/// carry one as its <see cref="Ledger.DefaultPriceLevelId"/>. Names are unique within a company
/// (case-insensitive); a rename does not change identity (the <see cref="Id"/> is the stable key). Immutable-ish
/// like <see cref="StockCategory"/>. Framework- and DB-agnostic.
/// </summary>
public sealed class PriceLevel
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The level name; unique within a company (case-insensitive), required.</summary>
    public string Name { get; set; }

    public PriceLevel(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A price-level name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
    }
}
