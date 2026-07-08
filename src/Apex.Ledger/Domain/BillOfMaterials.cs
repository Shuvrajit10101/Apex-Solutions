namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Bill of Materials</b> — a named recipe for manufacturing a finished good (Phase 6 Cluster 2;
/// requirements RQ-9/RQ-10, DP-3; schema v17 <c>bill_of_materials</c> + <c>bom_lines</c>). It belongs to one
/// finished-good <see cref="StockItemId"/>, carries a <see cref="UnitOfManufacture"/> (the block size the line
/// quantities are stated against — e.g. 1 or 10), and a set of <see cref="Lines"/> that are either consumed
/// <see cref="BomLineType.Component"/>s or carved-out By-Product/Co-Product/Scrap outputs (DP-3).
/// </summary>
/// <remarks>
/// <para><b>Multiple BOMs per item (RQ-9).</b> A finished good may have several named BOMs (e.g. "Standard",
/// "Economy"), selectable at manufacture. The <see cref="Name"/> is unique <i>within the item</i>
/// (case-insensitive) — the schema enforces this with a UNIQUE (stock_item_id, name COLLATE NOCASE) index;
/// <see cref="Services.BomService"/> enforces it at create time. The <see cref="Id"/> is the stable key.</para>
/// <para><b>Unit of manufacture / scaling (RQ-12).</b> Each line's <see cref="BomLine.QuantityPerBlock"/> is
/// stated per <see cref="UnitOfManufacture"/> block; the manufacturing engine consumes/produces
/// <c>QuantityPerBlock ÷ UnitOfManufacture × N</c> for a manufacture of <c>N</c> finished units. A
/// unit-of-manufacture &gt; 1 therefore divides correctly (the classic off-by-scale error is guarded).</para>
/// </remarks>
public sealed class BillOfMaterials
{
    private readonly List<BomLine> _lines;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The finished-good <see cref="StockItem"/> this BOM manufactures; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>BOM name; unique within the item (case-insensitive). A rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>
    /// The block size the line quantities are stated against (RQ-9/RQ-12), strictly &gt; 0 and 6-dp exact —
    /// e.g. a BOM whose lines are "per 10 units" has <see cref="UnitOfManufacture"/> = 10.
    /// </summary>
    public decimal UnitOfManufacture { get; }

    /// <summary>The component/output lines (RQ-9). At least one <see cref="BomLineType.Component"/> is required.</summary>
    public IReadOnlyList<BomLine> Lines => _lines;

    /// <summary>The consumed-component lines (their cost adds to the finished-good value, RQ-13).</summary>
    public IEnumerable<BomLine> ComponentLines => _lines.Where(l => l.IsComponent);

    /// <summary>The carved-out By-Product/Co-Product/Scrap lines (their value is carved out of FG cost, DP-3).</summary>
    public IEnumerable<BomLine> CarveOutLines => _lines.Where(l => l.IsCarveOut);

    public BillOfMaterials(
        Guid id,
        Guid stockItemId,
        string name,
        decimal unitOfManufacture,
        IEnumerable<BomLine> lines)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A BOM name is required.", nameof(name));
        if (unitOfManufacture <= 0m)
            throw new ArgumentException("The unit of manufacture must be > 0.", nameof(unitOfManufacture));
        if (!Quantities.IsWithinPrecision(unitOfManufacture))
            throw new InvalidOperationException(
                $"The unit of manufacture {unitOfManufacture} must be to {Quantities.DecimalPlaces} decimal places.");

        _lines = lines?.ToList() ?? throw new ArgumentNullException(nameof(lines));
        if (_lines.Count == 0)
            throw new InvalidOperationException("A BOM must have at least one line.");
        if (!_lines.Any(l => l.IsComponent))
            throw new InvalidOperationException("A BOM must have at least one Component line.");

        Id = id;
        StockItemId = stockItemId;
        Name = name.Trim();
        UnitOfManufacture = unitOfManufacture;
    }
}
