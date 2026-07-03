namespace Apex.Ledger.Domain;

/// <summary>
/// A cost centre — the leaf/branch axis a voucher line's amount is allocated to (catalog §6; plan.md §5).
/// Each centre belongs to exactly one <see cref="CostCategory"/> and nests under a <see cref="ParentId"/>
/// (Primary ⇒ no parent, else another centre in the <b>same</b> category), forming a hierarchy that
/// reports roll up (catalog §6: "Under (Primary or parent centre) — hierarchical").
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// A centre carries no balance of its own — its total is the Σ of the <see cref="CostAllocation"/>s that
/// name it (plus its descendants, in a hierarchical roll-up).
/// </remarks>
public sealed class CostCentre
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>The <see cref="CostCategory"/> this centre belongs to; required.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Parent cost centre; <c>null</c> ⇒ this is a primary (top-level) centre under the category.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>A primary centre has no parent (it sits directly under its category).</summary>
    public bool IsPrimary => ParentId is null;

    public CostCentre(
        Guid id,
        string name,
        Guid categoryId,
        Guid? parentId = null,
        string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Cost centre name is required.", nameof(name));

        Id = id;
        Name = name;
        CategoryId = categoryId;
        ParentId = parentId;
        Alias = alias;
    }
}
