namespace Apex.Ledger.Domain;

/// <summary>
/// The scope a <see cref="ReorderDefinition"/> is attached to (Phase 6 slice 6; requirements RQ-32). Reorder
/// levels may be defined per <b>Stock Item</b>, per <b>Stock Group</b> or per <b>Stock Category</b> (Tally-Book
/// pp.158–162, Note 2); an item resolves the most-specific definition that applies to it (RQ-36). The ordinals
/// are stable (persisted as the raw integer in <c>reorder_definitions.scope</c>).
/// </summary>
public enum ReorderScope
{
    /// <summary>The definition targets a single <see cref="StockItem"/>.</summary>
    Item = 0,

    /// <summary>The definition targets a <see cref="StockGroup"/> (applies to every item in that group / a child group).</summary>
    Group = 1,

    /// <summary>The definition targets a <see cref="StockCategory"/> (applies to every item in that category / a child).</summary>
    Category = 2,
}
