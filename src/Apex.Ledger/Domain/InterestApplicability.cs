namespace Apex.Ledger.Domain;

/// <summary>
/// The "Applicability" of interest (catalog §7): whether interest runs for the whole period or only after
/// a bill's due date. Mirrors the Advance-parameter "Applicability = Always / Past Due Date".
/// </summary>
public enum InterestApplicability
{
    /// <summary>Interest accrues across the whole period from the calculate-from date.</summary>
    Always = 0,

    /// <summary>
    /// Interest accrues only <b>after</b> the bill due date (catalog §5 bill-wise due dates). Days before the
    /// due date carry no interest; the accrual window starts the day the bill falls due.
    /// </summary>
    PostDue = 1,
}
