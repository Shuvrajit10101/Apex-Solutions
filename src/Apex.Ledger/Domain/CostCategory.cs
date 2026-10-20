namespace Apex.Ledger.Domain;

/// <summary>
/// A cost category — the top axis of cost allocation (catalog §6; plan.md §5). A category groups a set
/// of <see cref="CostCentre"/>s and declares whether it may allocate revenue and/or non-revenue items.
/// Every company seeds a default <b>"Primary Cost Category"</b>. At least one of
/// <see cref="AllocateRevenueItems"/> / <see cref="AllocateNonRevenueItems"/> must be true (catalog §6:
/// "≥1 must be Yes").
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.
/// Categories carry no accounting balance of their own — they merely partition the cost centres that a
/// voucher line allocates to. Multiple categories let the same amount be classified along more than one
/// axis (e.g. Department and Salesperson); the engine models each category's centres independently.
/// </remarks>
public sealed class CostCategory
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>"Allocate Revenue Items" (catalog §6): may allocate P&amp;L (income/expense) lines.</summary>
    public bool AllocateRevenueItems { get; set; }

    /// <summary>"Allocate Non-Revenue Items" (catalog §6): may allocate balance-sheet lines.</summary>
    public bool AllocateNonRevenueItems { get; set; }

    /// <summary>True for the seeded "Primary Cost Category" — it cannot be deleted (catalog §6).</summary>
    public bool IsPredefined { get; }

    /// <summary>
    /// v67 (census row <b>2.7</b>): the vendor-attested <b>alias</b> on the Cost Category screen —
    /// <i>"Name &amp; alias: As in other masters, you can specify multiple aliases."</i>
    /// (<c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>, opened by content 2026-10-16; the
    /// clause appears in the Cost <b>Category</b> section as well as the Cost Centre section).
    ///
    /// <para>🔴 <b>THIS IS THE FIELD ROW 2.7 WAS DEMOTED FOR on 2026-10-04.</b> <see cref="CostCentre.Alias"/>
    /// has existed since v3 and the category had no counterpart at all — not a property, not a column — and
    /// that asymmetry is what made the gap visible. <c>null</c> = no alias, which is every pre-v67 category, so
    /// the migration back-fills nothing (ER-13).</para>
    ///
    /// <para>⚠️ <b>ONE alias, not "multiple aliases" — a documented divergence, and it is OURS.</b> The vendor
    /// sentence is plural. This product stores a single alias on every master (<see cref="CostCentre.Alias"/>,
    /// <see cref="Godown.Alias"/>, <see cref="Ledger"/>), so the category matches its siblings here rather than
    /// the vendor. Widening every master to an alias collection is a separate change and is not smuggled in
    /// under this row.</para>
    /// </summary>
    public string? Alias { get; set; }

    public CostCategory(
        Guid id,
        string name,
        bool allocateRevenueItems = true,
        bool allocateNonRevenueItems = false,
        bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Cost category name is required.", nameof(name));
        if (!allocateRevenueItems && !allocateNonRevenueItems)
            throw new ArgumentException(
                "A cost category must allocate revenue and/or non-revenue items (at least one must be Yes).",
                nameof(allocateRevenueItems));

        Id = id;
        Name = name;
        AllocateRevenueItems = allocateRevenueItems;
        AllocateNonRevenueItems = allocateNonRevenueItems;
        IsPredefined = isPredefined;
    }
}
