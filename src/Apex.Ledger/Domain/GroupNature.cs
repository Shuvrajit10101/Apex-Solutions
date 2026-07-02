namespace Apex.Ledger.Domain;

/// <summary>
/// The four accounting natures a <see cref="Group"/> can carry. Drives the sign
/// convention and where a balance lands in the statements (Balance Sheet vs P&amp;L).
/// The engine's canonical spelling is singular; the fixtures spell them
/// "Assets"/"Liabilities"/"Income"/"Expenses" and the loader maps those in.
/// </summary>
public enum GroupNature
{
    /// <summary>Debit-natured; shown on the Balance Sheet asset side.</summary>
    Asset,

    /// <summary>Credit-natured; shown on the Balance Sheet liabilities/capital side.</summary>
    Liability,

    /// <summary>Credit-natured revenue; flows into the Profit &amp; Loss income side.</summary>
    Income,

    /// <summary>Debit-natured cost; flows into the Profit &amp; Loss expense side.</summary>
    Expense,
}
