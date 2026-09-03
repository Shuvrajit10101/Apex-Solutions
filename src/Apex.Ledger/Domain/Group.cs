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
    /// directly below the Sales/Purchase ledger in the shipped <see cref="GstDetailSource.LedgerFirst"/> walk
    /// (plan.md Phase 10.10 WF-1; register IV-1). <c>null</c> ⇒ this group declares no GST details and contributes
    /// nothing to a lookup, which is how every pre-v51 group reads. <b>Persisted-but-inert in slice S4</b>: no
    /// resolver reads it yet, so no existing figure moves.
    ///
    /// <para>🔴 <b>R7 — THIS LEVEL HAS ZERO CORPUS SUPPORT (owed-review lens 3 finding 1).</b> It comes only from
    /// TallyHelp's published hierarchy strings (<c>[web]</c>, <c>docs/invented-vs-cloned.md</c> IV-1). The corpus's
    /// own list of five GST methods does <b>not</b> contain an accounting Group, and the corpus's accounting-Group
    /// creation screen carries <b>no GST field</b> — see <see cref="MasterGstDetails"/> for both extracts with their
    /// PDF and line numbers. Treat it as web-sourced and A14-unverified, not as clone fidelity.</para>
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
