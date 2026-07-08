namespace Apex.Ledger.Domain;

/// <summary>
/// A Stock Category (catalog §9; requirements RQ-2): an <b>independent</b> classification axis for stock
/// items that is <i>not</i> nested under <see cref="StockGroup"/>s. A <see cref="StockItem"/> may carry a
/// category orthogonally to its group (e.g. group by product line, category by brand). Categories may
/// themselves nest under a parent category to any depth.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// Category is optional on an item, so the axis stays out of the way for companies that do not use it.
/// </remarks>
public sealed class StockCategory
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Parent category; <c>null</c> ⇒ this is a top-level (primary) category.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>A primary category has no parent.</summary>
    public bool IsPrimary => ParentId is null;

    public StockCategory(
        Guid id,
        string name,
        Guid? parentId = null,
        string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Stock category name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        ParentId = parentId;
        Alias = alias;
    }
}
