namespace Apex.Ledger.Domain;

/// <summary>
/// One POS payment tender on a POS Sales voucher (catalog §11; Phase 6 slice 7 RQ-39/RQ-40; DP-6). A POS bill's
/// single customer debit is replaced by a split of tender debits — this record is the per-tender metadata the
/// balanced accounting <see cref="EntryLine"/> cannot carry (tender kind, cash tendered/change, card/bank/cheque
/// references). It is <b>voucher-level</b> metadata paired 1:1 with the tender debit lines; the credit side
/// (Cr Sales + Cr Output CGST/SGST/IGST) is byte-identical to a normal sale, so GST reuses the Phase-4 engine
/// unchanged.
/// </summary>
/// <param name="Type">The tender kind (Gift / Card / Cheque / Cash).</param>
/// <param name="LedgerId">The ledger this tender debits — validated to the required group (Gift → Sundry Debtors,
/// Card/Cheque → Bank, Cash → Cash-in-Hand) by <see cref="Services.PosTenderService.EnsureGrouping"/>.</param>
/// <param name="Amount">The <b>posted</b> payable share for this tender, paisa-exact. For Cash this is the
/// <b>residual</b> (bill total − the non-cash tenders), NOT the tendered amount — the books never see the change.
/// Σ over all tenders equals the bill total.</param>
/// <param name="Tendered">Cash only: the cash handed over (≥ <see cref="Amount"/>); <c>null</c> for non-cash tenders.</param>
/// <param name="Change">Cash only: the informational change = <see cref="Tendered"/> − <see cref="Amount"/> (≥ 0);
/// it produces NO ledger line. <c>null</c> for non-cash tenders.</param>
/// <param name="CardNo">Card only: the card number reference; <c>null</c> otherwise.</param>
/// <param name="BankName">Cheque/DD only: the drawee bank name; <c>null</c> otherwise.</param>
/// <param name="ChequeNo">Cheque/DD only: the cheque/DD number; <c>null</c> otherwise.</param>
public sealed record PosTender(
    PosTenderType Type,
    Guid LedgerId,
    Money Amount,
    Money? Tendered = null,
    Money? Change = null,
    string? CardNo = null,
    string? BankName = null,
    string? ChequeNo = null);
