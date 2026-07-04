namespace Apex.Ledger.Domain;

/// <summary>
/// The interest style (catalog §7): simple interest on the principal, or compound interest where each
/// sub-period's interest is added to the principal before the next sub-period accrues.
/// </summary>
public enum InterestStyle
{
    /// <summary>Simple interest: principal × rate% × days / basis, computed once over the window.</summary>
    Simple = 0,

    /// <summary>
    /// Compound interest: the window is split into calendar-month sub-periods; each month's interest is
    /// capitalised into the principal before the next month accrues.
    /// </summary>
    Compound = 1,
}
