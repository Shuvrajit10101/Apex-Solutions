namespace Apex.Ledger.Domain;

/// <summary>
/// The transaction (instrument) type chosen in a bank allocation (catalog §8 "Bank Allocation":
/// transaction type — cheque/DD, NEFT, RTGS, …). It classifies HOW money moved through the bank and
/// feeds the Bank Reconciliation and statutory challans. Persisted as an enum ordinal, so members are
/// only ever appended (never reordered) once shipped.
/// </summary>
public enum BankTransactionType
{
    /// <summary>A cheque or demand draft — the classic instrument with a number and instrument date.</summary>
    ChequeOrDD = 0,

    /// <summary>National Electronic Funds Transfer.</summary>
    NEFT = 1,

    /// <summary>Real-Time Gross Settlement.</summary>
    RTGS = 2,

    /// <summary>A cash deposit/withdrawal at the bank.</summary>
    Cash = 3,

    /// <summary>Any other electronic/manual transfer (IMPS, UPI, inter-bank, adjustment, …).</summary>
    Other = 4,
}
