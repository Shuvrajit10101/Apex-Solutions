namespace Apex.Ledger.Domain;

/// <summary>
/// The kind of a single POS payment tender (catalog §11; Phase 6 slice 7 RQ-40). A POS bill is settled by one
/// tender (single-mode) or a split across up to four (multi-mode): a <b>Gift Voucher</b> (posts to a Sundry
/// Debtors ledger), a <b>Credit/Debit Card</b> and a <b>Cheque/DD</b> (both post to a Bank ledger), and
/// <b>Cash</b> (posts to a Cash-in-Hand ledger). The ordinal is stored as an INTEGER in
/// <c>pos_tender_allocations.tender_type</c>; the declaration order (Gift, Card, Cheque, Cash) IS the stable
/// tender order the multi-tender panel uses (Cash always last, auto-filling the residual).
/// </summary>
public enum PosTenderType
{
    /// <summary>A gift voucher redeemed at the till — a receivable settlement, posts Dr to a Sundry-Debtors ledger.</summary>
    GiftVoucher = 0,

    /// <summary>A credit/debit card swipe — posts Dr to a Bank ledger; carries the card number.</summary>
    Card = 1,

    /// <summary>A cheque / demand draft — posts Dr to a Bank ledger; carries the bank name and cheque number.</summary>
    Cheque = 2,

    /// <summary>Cash — posts Dr to a Cash-in-Hand ledger for the <b>residual</b> payable (never the tendered amount);
    /// carries the cash tendered and the informational change.</summary>
    Cash = 3,
}
