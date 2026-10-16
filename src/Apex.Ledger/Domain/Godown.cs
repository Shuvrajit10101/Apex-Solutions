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

    /// <summary>
    /// v67 (census row <b>3.7</b>): the vendor's <i>"Allow storage of materials"</i>.
    ///
    /// <para><b>R7 — ATTESTED, verbatim:</b> <i>"<b>Allow storage of materials</b> — By default, each godown
    /// can store materials. However, you can set a godown to not store any materials. To do so, setting this
    /// option to Yes to displays the Allow storage of materials option on the Godown Creation screen. Set that
    /// option to No."</i> (<c>help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/</c>,
    /// opened by content 2026-10-16; this is the canonical slug for row 3.7 per A14's 2026-10-13 decision.)</para>
    ///
    /// <para>🔴 <b>DEFAULT <c>true</c> IS THE VENDOR'S OWN DEFAULT</b> — <i>"By default, each godown can store
    /// materials"</i> — and it is the only behaviour this build had, so every pre-v67 godown is unchanged and
    /// the migration back-fills nothing (ER-13).</para>
    ///
    /// <para>🔴 <b>F12-GATED, AND THAT IS THE POINT OF THE ROW.</b> The vendor does not show this field on the
    /// Godown Creation screen until <see cref="Company.ConfigAllowStorageOfMaterials"/> is set, under the
    /// page's own rule <i>"What you see on the screen can vary based on your choices. Press F12 (Configure) to
    /// change the options."</i> <b>Surfacing it unconditionally would ITSELF be a divergence</b>, and no test
    /// would have caught the wrong build.</para>
    ///
    /// <para>⚠️ <b>STORAGE AND ELECTION in v67 — setting this to <c>false</c> does NOT yet refuse a stock
    /// movement into the godown.</b> Said plainly rather than left to be discovered.</para>
    /// </summary>
    public bool AllowStorageOfMaterials { get; set; } = true;

    /// <summary>
    /// v67 (census row <b>3.7</b>): the location's address — the vendor's F12 <i>"Use address for
    /// Locations"</i>. <c>null</c> = no address, which is every pre-v67 godown.
    ///
    /// <para><b>R7 — ATTESTED, verbatim:</b> <i>"<b>Use address for Locations</b> — You can add the Locations
    /// address by enabling this option. The Locations address can be printed in your invoices by configuring
    /// invoice printing."</i> (same page as <see cref="AllowStorageOfMaterials"/>.)</para>
    ///
    /// <para>🔴 <b>F12-GATED by <see cref="Company.ConfigUseAddressForLocations"/></b>, for the same reason
    /// and under the same page rule.</para>
    ///
    /// <para>⚠️ <b>ONE free-text address field, not the vendor's multi-line address block, and the invoice
    /// printing half is NOT built.</b> Both are divergences and both are ours.</para>
    /// </summary>
    public string? Address { get; set; }

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
