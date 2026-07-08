using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The POS tender engine (catalog §11; Phase 6 slice 7 RQ-39..RQ-42; TOP RISK #6; DP-6). Pure, deterministic,
/// paisa-exact — no clock, no RNG, no DB. A POS bill's single customer debit is replaced by a split of tender
/// debits (Gift Voucher / Card / Cheque / Cash); this service computes the residual cash share, the informational
/// change, and the tender debit lines, and enforces the two POS-specific invariants:
/// <list type="bullet">
///   <item><b>Reconciliation</b> — Σ tender.Amount == bill total, paisa-exact (<see cref="EnsureBalanced"/>).</item>
///   <item><b>Grouping</b> — each tender ledger sits under its required group: Gift → Sundry Debtors,
///     Card/Cheque → Bank, Cash → Cash-in-Hand (<see cref="EnsureGrouping"/>). <b>Load-bearing (RQ-41).</b></item>
/// </list>
/// The cash tender posts the <b>residual payable</b>, NOT the tendered amount — the books never see the change
/// (subtlety b/d/e). Everything else (Cr Sales + Cr Output CGST/SGST/IGST, the item-invoice stock movement) is
/// byte-identical to a normal sale, so GST reuses the Phase-4 engine unchanged.
/// </summary>
public static class PosTenderService
{
    /// <summary>
    /// The <b>cash residual</b> (RQ-40): bill total − (gift + card + cheque). This is the amount the Cash tender
    /// posts (never the tendered cash). Throws <see cref="InvalidVoucherException"/> when the non-cash tenders
    /// over-pay the bill (residual &lt; 0) — a friendly fail-fast so an over-tender can never smuggle a negative
    /// cash line onto the books.
    /// </summary>
    public static Money CashResidual(Money billTotal, Money gift, Money card, Money cheque)
    {
        var residual = billTotal - (gift + card + cheque);
        if (residual.Amount < 0m)
            throw new InvalidVoucherException(
                $"POS tenders over-pay the bill: the non-cash tenders (Gift {gift} + Card {card} + Cheque {cheque}) " +
                $"exceed the bill total {billTotal}. Reduce a tender so the cash residual is not negative.");
        return residual;
    }

    /// <summary>
    /// The informational <b>change</b> (RQ-39/RQ-40): cash tendered − cash payable(residual), which must be ≥ 0.
    /// Change is never posted (it produces no ledger line); the cash ledger posts the payable, not the tendered.
    /// Throws when the tendered cash is short of the payable (change &lt; 0).
    /// </summary>
    public static Money Change(Money cashTendered, Money cashPayable)
    {
        var change = cashTendered - cashPayable;
        if (change.Amount < 0m)
            throw new InvalidVoucherException(
                $"Cash tendered {cashTendered} is less than the cash payable {cashPayable}; the change would be " +
                "negative. The customer must tender at least the residual payable.");
        return change;
    }

    /// <summary>
    /// The tender-reconciliation invariant (RQ-40; TOP RISK #6): Σ tender.Amount == <paramref name="billTotal"/>,
    /// paisa-exact. Throws <see cref="InvalidVoucherException"/> on any mismatch so an unbalanced tender split can
    /// never persist.
    /// </summary>
    public static void EnsureBalanced(Money billTotal, IReadOnlyList<PosTender> tenders)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        var sum = Money.Zero;
        foreach (var t in tenders) sum += t.Amount;
        if (sum != billTotal)
            throw new InvalidVoucherException(
                $"POS tenders total {sum} but the bill total is {billTotal}; the tenders must reconcile to the " +
                "bill to the paisa.");
    }

    /// <summary>
    /// The tender-ledger grouping invariant (RQ-41; Study Guide p.234 "Note"; TOP RISK #6) — <b>load-bearing</b>.
    /// Each tender must debit a ledger under its required group:
    /// <list type="bullet">
    ///   <item><b>Gift Voucher</b> → a ledger under <b>Sundry Debtors</b> (a receivable settlement).</item>
    ///   <item><b>Card</b> / <b>Cheque</b> → a <b>Bank</b> ledger (Bank Accounts / Bank OD/OCC).</item>
    ///   <item><b>Cash</b> → a <b>Cash-in-Hand</b> ledger.</item>
    /// </list>
    /// A misgrouped tender (e.g. a Gift on a Sales ledger, a Card on a Cash ledger) throws
    /// <see cref="InvalidVoucherException"/>. An unknown ledger reference throws too.
    /// </summary>
    public static void EnsureGrouping(Company company, IReadOnlyList<PosTender> tenders)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(tenders);
        foreach (var t in tenders)
        {
            var ledger = company.FindLedger(t.LedgerId)
                ?? throw new InvalidVoucherException($"POS tender references unknown ledger {t.LedgerId}.");
            switch (t.Type)
            {
                case PosTenderType.GiftVoucher:
                    if (!ClassificationRules.GroupIsUnder(ledger.GroupId, "Sundry Debtors", company))
                        throw new InvalidVoucherException(
                            $"POS Gift-Voucher tender ledger '{ledger.Name}' must sit under 'Sundry Debtors'.");
                    break;
                case PosTenderType.Card:
                case PosTenderType.Cheque:
                    if (!ClassificationRules.IsBankLedger(ledger, company))
                        throw new InvalidVoucherException(
                            $"POS {t.Type} tender ledger '{ledger.Name}' must be a Bank ledger " +
                            "(Bank Accounts / Bank OD/OCC).");
                    break;
                case PosTenderType.Cash:
                    if (!ClassificationRules.IsCashLedger(ledger, company))
                        throw new InvalidVoucherException(
                            $"POS Cash tender ledger '{ledger.Name}' must sit under 'Cash-in-Hand'.");
                    break;
                default:
                    throw new InvalidVoucherException($"Unknown POS tender type {t.Type}.");
            }
        }
    }

    /// <summary>
    /// Builds the tender <b>debit</b> entry lines (RQ-40): one <see cref="DrCr.Debit"/> to each tender's ledger for
    /// its <see cref="PosTender.Amount"/> (the Cash Dr is the residual payable, <b>not</b> the tendered cash — the
    /// change is never posted). These replace the single customer debit on a POS Sales voucher; the caller
    /// concatenates them with the Cr Sales + Cr Output GST credit legs to assemble the balanced voucher.
    /// </summary>
    public static IReadOnlyList<EntryLine> BuildTenderDebitLines(IReadOnlyList<PosTender> tenders)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        var lines = new List<EntryLine>(tenders.Count);
        foreach (var t in tenders)
            lines.Add(new EntryLine(t.LedgerId, t.Amount, DrCr.Debit));
        return lines;
    }
}
