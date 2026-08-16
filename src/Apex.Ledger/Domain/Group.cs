namespace Apex.Ledger.Domain;

/// <summary>
/// A classification node in the chart of accounts: a nature plus a parent
/// (catalog §3; plan.md §4.1). The 28 predefined groups form the backbone;
/// custom groups nest under any of them. The <see cref="Id"/> is the stable
/// key — the <see cref="Name"/> is not, so an Alter renames in place.
/// </summary>
public sealed class Group
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Asset / Liability / Income / Expense — equal to the primary ancestor's nature.</summary>
    public GroupNature Nature { get; set; }

    /// <summary>Parent group; <c>null</c> ⇒ this is one of the 15 primary heads.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>True for the 28 seeded groups — they cannot be deleted (§6).</summary>
    public bool IsPredefined { get; }

    /// <summary>
    /// The accounting group's GST details block — the "Group" level of the five-level GST hierarchy, sitting
    /// directly above the Sales/Purchase ledger (plan.md Phase 10.10 WF-1; register IV-1). <c>null</c> ⇒ this group
    /// declares no GST details and contributes nothing to a lookup, which is how every pre-v51 group reads.
    /// <b>Persisted-but-inert in slice S1</b>: no resolver reads it yet, so no existing figure moves.
    /// </summary>
    public MasterGstDetails? Gst { get; set; }

    /// <summary>A primary group has no parent.</summary>
    public bool IsPrimary => ParentId is null;

    public Group(
        Guid id,
        string name,
        GroupNature nature,
        Guid? parentId = null,
        string? alias = null,
        bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));

        Id = id;
        Name = name;
        Nature = nature;
        ParentId = parentId;
        Alias = alias;
        IsPredefined = isPredefined;
    }
}
