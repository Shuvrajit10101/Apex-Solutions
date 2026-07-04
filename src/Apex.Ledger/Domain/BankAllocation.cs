namespace Apex.Ledger.Domain;

/// <summary>
/// The bank-allocation detail hung off an <see cref="EntryLine"/> whose ledger is a bank account
/// (Bank Accounts / Bank OD A/c), catalog §8. It captures HOW the money moved through the bank —
/// the <see cref="TransactionType"/> (cheque/DD, NEFT, RTGS, cash, other), the
/// <see cref="InstrumentNumber"/> and <see cref="InstrumentDate"/> — and, once the transaction has
/// cleared, the <see cref="BankDate"/> (the date the bank statement shows it). A line carries at most
/// <b>one</b> bank allocation (unlike bill/cost allocations, a bank line is not "split"), so it is
/// added to <see cref="EntryLine"/> as a single optional trailing parameter, leaving every existing
/// caller unaffected.
/// </summary>
/// <remarks>
/// <para><see cref="BankDate"/> is the ONLY mutable field: it is <c>null</c> when the transaction is
/// unreconciled and is set by the Bank Reconciliation / statement-import flow when the transaction
/// clears (the "enter the Bank Date per line" BRS step). Everything else is captured at voucher entry
/// and never changes.</para>
/// <para>The allocation carries no amount of its own — the whole line amount is the bank movement.</para>
/// </remarks>
public sealed class BankAllocation
{
    /// <summary>Cheque/DD, NEFT, RTGS, Cash or Other.</summary>
    public BankTransactionType TransactionType { get; }

    /// <summary>
    /// The instrument number (cheque no., DD no., UTR/reference for a transfer). May be empty for a
    /// cash deposit or an untracked transfer.
    /// </summary>
    public string InstrumentNumber { get; }

    /// <summary>The instrument date (the date on the cheque / of the transfer), or <c>null</c> if unknown.</summary>
    public DateOnly? InstrumentDate { get; }

    /// <summary>
    /// The <b>Bank Date</b> — the date the bank statement shows the transaction as cleared. <c>null</c>
    /// while unreconciled. Set by <see cref="Reports.BankReconciliation"/> (or statement import) when the
    /// transaction is matched/reconciled; this is the one field that changes after entry.
    /// </summary>
    public DateOnly? BankDate { get; set; }

    public BankAllocation(
        BankTransactionType transactionType,
        string? instrumentNumber = null,
        DateOnly? instrumentDate = null,
        DateOnly? bankDate = null)
    {
        TransactionType = transactionType;
        InstrumentNumber = instrumentNumber?.Trim() ?? string.Empty;
        InstrumentDate = instrumentDate;
        BankDate = bankDate;
    }

    /// <summary>True once a <see cref="BankDate"/> has been set (the transaction has cleared the bank).</summary>
    public bool IsReconciled => BankDate is not null;
}
