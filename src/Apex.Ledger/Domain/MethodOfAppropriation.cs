namespace Apex.Ledger.Domain;

/// <summary>
/// The "<b>Method of Appropriation in Purchase invoice</b>" on an <b>additional-cost ledger</b> (Book pp.133–141;
/// catalog §11; Phase 6 slice 3 RQ-16..RQ-20). An additional-cost ledger (Freight, Packing, Loading …) is an
/// ordinary <b>Direct Expenses</b> ledger whose "Inventory values are affected" is <b>No</b>, but which — when
/// used on a Purchase whose voucher type has <c>Track Additional Costs = Yes</c> — has its amount
/// <b>apportioned across the item lines</b> to raise each item's <i>landed</i> (effective) stock rate. A
/// <b>non-null</b> method is what MARKS a ledger as an additional-cost ledger; a plain Direct-Expenses ledger
/// with no method stays purely P&amp;L and never touches a stock rate (RQ-19).
/// </summary>
public enum MethodOfAppropriation
{
    /// <summary><b>Appropriate by Quantity</b> — each item line's share is proportional to its base-unit
    /// quantity, so the load is a flat ₹/unit spread evenly across every unit.</summary>
    ByQuantity = 0,

    /// <summary><b>Appropriate by Value</b> — each item line's share is proportional to its purchase value
    /// (qty × rate), so dearer lines absorb proportionally more of the additional cost.</summary>
    ByValue = 1,
}
