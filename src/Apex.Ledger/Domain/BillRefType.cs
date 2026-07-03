namespace Apex.Ledger.Domain;

/// <summary>
/// The four bill-reference types a bill-wise allocation can carry (catalog §5;
/// plan.md §5, C-3). Chosen on the "Type of Ref" prompt during voucher entry.
/// </summary>
public enum BillRefType
{
    /// <summary>Opens a brand-new outstanding bill for the party.</summary>
    NewRef = 0,

    /// <summary>Settles (knocks off) a pending bill selected from the party's open list.</summary>
    AgstRef = 1,

    /// <summary>An advance paid/received; opens an advance (no due date).</summary>
    Advance = 2,

    /// <summary>Unallocated/suspense — money on account, not tied to a specific bill.</summary>
    OnAccount = 3,
}
