namespace Apex.Ledger.Domain;

/// <summary>
/// The "On" filter for interest calculation (catalog §7): which side of the outstanding balance the
/// interest accrues on. Mirrors "Calculate interest on … All balances / Debit balances only /
/// Credit balances only".
/// </summary>
public enum InterestOnBalance
{
    /// <summary>Accrue on the balance whichever side it sits (debit or credit).</summary>
    All = 0,

    /// <summary>Accrue only while the ledger closing balance is a <b>debit</b> (e.g. a receivable).</summary>
    DebitOnly = 1,

    /// <summary>Accrue only while the ledger closing balance is a <b>credit</b> (e.g. a payable/loan).</summary>
    CreditOnly = 2,
}
