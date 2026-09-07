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

    /// <summary>
    /// The godown master's <b>"Set job/project for job costing"</b> — the <see cref="CostCentre"/> this location
    /// <b>is</b>, as a job or project (census 9.6; schema v58). <c>null</c> ⇒ an ordinary storage location, which
    /// is every godown created before the feature existed.
    /// <para>
    /// 🔴 <b>This is a LINK to the existing cost-centre machinery, not a new dimension.</b> The vendor documents
    /// job costing as requiring both Cost Centres and godowns — the godown tracks the site's material movement,
    /// the cost centre it names carries the site's money — and the Job Work Analysis report reads the ordinary
    /// <see cref="CostAllocation"/> rows posted against that centre. Nothing here duplicates a cost centre; a
    /// godown merely points at one.
    /// </para>
    /// <para>Surfaced on the master only when <see cref="Company.EnableJobCosting"/> is on. R7:
    /// <c>help.tallysolutions.com/job-costing-tally/</c> — "you can select a cost centre under Set job/project
    /// for job costing".</para>
    /// </summary>
    public Guid? JobCostCentreId { get; set; }

    /// <summary>True when this godown has been designated a job/project for job costing (census 9.6).</summary>
    public bool IsJobProject => JobCostCentreId is not null;

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
