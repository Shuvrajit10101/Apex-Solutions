namespace Apex.Ledger.Domain;

/// <summary>
/// The kind of a <see cref="BomLine"/> in a <see cref="BillOfMaterials"/> (Phase 6 Cluster 2; requirements
/// RQ-9, DP-3). The ordinals are load-bearing — they are persisted verbatim into <c>bom_lines.line_type</c>
/// (schema v17), so their numeric values MUST match the schema contract A3 stamped
/// (<b>Component=0, ByProduct=1, CoProduct=2, Scrap=3</b>).
/// </summary>
/// <remarks>
/// A <see cref="Component"/> is <i>consumed</i> to make the finished good (its cost adds to the finished
/// good's value). A <see cref="ByProduct"/>, <see cref="CoProduct"/> or <see cref="Scrap"/> is <i>produced</i>
/// alongside the finished good and its value is <b>carved out</b> of the finished good's cost (DP-3), valued at
/// the operator-entered rate/% (default the item's standard cost). Splitting the three carve-out kinds keeps
/// the corpus's terminology; the manufacturing engine treats them identically (all are outputs whose value
/// reduces the main finished good's residual cost).
/// </remarks>
public enum BomLineType
{
    /// <summary>A raw-material/sub-assembly line that is <b>consumed</b> to make the finished good.</summary>
    Component = 0,

    /// <summary>A secondary output produced incidentally; its value is carved out of the finished-good cost (DP-3).</summary>
    ByProduct = 1,

    /// <summary>A joint primary output with real value; its value is carved out of the finished-good cost (DP-3).</summary>
    CoProduct = 2,

    /// <summary>A waste/residue output; its value (often low or zero) is carved out of the finished-good cost (DP-3).</summary>
    Scrap = 3,
}
