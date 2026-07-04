namespace Apex.Ledger.Domain;

/// <summary>
/// A Godown / Location — a physical (or logical) place stock is held (catalog §9; requirements RQ-5).
/// Godowns nest under a parent to any depth. Every company seeds a default <b>"Main Location"</b> with
/// <see cref="IsMainLocation"/> set. A godown may be flagged <see cref="ThirdParty"/> ("our stock with a
/// third party" — the job-work flag): the flag is captured now, but its job-work workflow is a later phase.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// Stock is allocated per godown, so a <see cref="StockOpeningBalance"/> (and later stock movements)
/// names the godown it sits in.
/// </remarks>
public sealed class Godown
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Parent godown; <c>null</c> ⇒ this is a top-level location.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>
    /// "Our stock with a third party" (catalog §9) — the job-work flag. Captured in this phase; the
    /// job-work workflow that hangs off it is deferred to a later phase.
    /// </summary>
    public bool ThirdParty { get; set; }

    /// <summary>True for the single seeded default godown ("Main Location"); it cannot be deleted.</summary>
    public bool IsMainLocation { get; }

    /// <summary>A top-level location has no parent.</summary>
    public bool IsPrimary => ParentId is null;

    public Godown(
        Guid id,
        string name,
        Guid? parentId = null,
        string? alias = null,
        bool thirdParty = false,
        bool isMainLocation = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Godown name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        ParentId = parentId;
        Alias = alias;
        ThirdParty = thirdParty;
        IsMainLocation = isMainLocation;
    }
}
