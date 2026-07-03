namespace Apex.Ledger.Domain;

/// <summary>
/// How a <see cref="BudgetLine"/>'s figure is measured against actuals (catalog §7):
/// <b>On Closing Balance</b> compares the target's closing balance at the budget's period-end;
/// <b>On Nett Transactions</b> compares the target's net movement (Σ postings) within the period.
/// </summary>
public enum BudgetType
{
    /// <summary>Compare the target's closing balance as-of the period end (opening + movements ≤ PeriodTo).</summary>
    OnClosingBalance = 0,

    /// <summary>Compare the target's net transaction total within [PeriodFrom, PeriodTo] (movements only, no opening).</summary>
    OnNettTransactions = 1,
}
