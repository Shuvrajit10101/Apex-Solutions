namespace Apex.Ledger;

/// <summary>
/// The side of a double-entry posting — the To/By (Cr/Dr) model.
/// Every voucher line is either a debit or a credit; a balanced voucher
/// has equal total debits and credits (NFR-3).
/// </summary>
public enum DrCr
{
    /// <summary>Debit side ("By" in the entry vocabulary).</summary>
    Debit = 0,

    /// <summary>Credit side ("To" in the entry vocabulary).</summary>
    Credit = 1,
}
