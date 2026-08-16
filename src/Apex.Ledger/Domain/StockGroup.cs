namespace Apex.Ledger.Domain;

/// <summary>
/// A classification node for stock items (catalog §9; requirements RQ-1). Stock groups nest to any depth
/// under a parent (implicit "Primary" root when <see cref="ParentId"/> is <c>null</c>) and carry a
/// <see cref="AddQuantities"/> flag: when false, a group holding items of unlike units does not roll a
/// summed quantity into its parent (only value aggregates), matching the catalog's "Should quantities be
/// added?" option.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// This is the inventory analogue of <see cref="Group"/> on the accounting side; a <see cref="StockItem"/>
/// sits <c>Under</c> exactly one stock group.
/// </remarks>
public sealed class StockGroup
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Parent stock group; <c>null</c> ⇒ this sits directly under the implicit "Primary" root.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>
    /// "Should quantities be added?" (catalog §9). When false, a group of unlike-unit items does not roll a
    /// summed quantity up to its parent — only value aggregates. Defaults to true (the common case).
    /// </summary>
    public bool AddQuantities { get; set; }

    /// <summary>
    /// The stock group's "Set/Alter GST details" block — one of the five levels of the GST hierarchy (plan.md
    /// Phase 10.10 WF-1; register IV-1). <c>null</c> ⇒ this group declares no GST details and contributes nothing
    /// to a lookup, which is how every pre-v51 stock group reads. <b>Persisted-but-inert in slice S4</b>: no
    /// resolver reads it yet, so no existing figure moves.
    ///
    /// <para>⚠️ <b>This is NOT "level 2" — that label was removed by the owed review (lens 3 finding 3).</b> The 2
    /// was the ordinal of "Defining at Stock Group Level" in the corpus's list of five <i>methods</i>
    /// (<see cref="MasterGstDetails"/> quotes it verbatim), a list the corpus itself frames as "any one method of
    /// the following" — not a resolution order. Under the shipped default
    /// <see cref="GstDetailSource.LedgerFirst"/> the walk is Ledger → Group → Stock Item → <b>Stock Group</b> →
    /// Company, so the stock group is <b>fourth</b>; it is second only under
    /// <see cref="GstDetailSource.StockItemFirst"/>. A resolver author who reads a positional number here gets the
    /// wrong walk — take the order from <see cref="GstDetailSource"/> and from nowhere else.</para>
    /// </summary>
    public MasterGstDetails? Gst { get; set; }

    /// <summary>A top-level stock group has no parent (it sits under the implicit "Primary" root).</summary>
    public bool IsPrimary => ParentId is null;

    public StockGroup(
        Guid id,
        string name,
        Guid? parentId = null,
        string? alias = null,
        bool addQuantities = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Stock group name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        ParentId = parentId;
        Alias = alias;
        AddQuantities = addQuantities;
    }
}
