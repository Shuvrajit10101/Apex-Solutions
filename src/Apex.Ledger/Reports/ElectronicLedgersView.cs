using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The GST <b>electronic ledgers</b> statement (Phase 9 slice 7; RQ-20; ER-9/ER-2/ER-13) — a pure, read-only
/// <b>projection</b> over the posted GST vouchers + the <c>gst_challans</c> deposits, mirroring the GST portal's three
/// ledgers. There is <b>no stored balance table</b>: the four <c>Input {head}</c> Duties-&amp;-Taxes ledgers <b>are</b>
/// the electronic Credit ledger's four pools, the four <c>Output {head}</c> ledgers (+ the RCM Output ledgers) <b>are</b>
/// the Liability ledger, and the <c>Electronic Cash Ledger</c> balance (split by challan into its (major, minor) cells)
/// <b>is</b> the Cash ledger — so a company that never sets off / pays / reverses reads all-zero and is byte-identical
/// to a v43 company (ER-13). Nothing is posted here.
/// <list type="bullet">
///   <item><b>Credit ledger</b> — per head: closing available ITC (the <c>Input {head}</c> debit balance), decomposed
///     into window additions (forward + RCM ITC), set-off utilisation, and reversals. Cess ring-fenced (ER-2).</item>
///   <item><b>Cash ledger</b> — per (major, minor) cell: deposits (challans) − utilisation (cash discharges).</item>
///   <item><b>Liability ledger</b> — per head: outstanding output tax (the <c>Output {head}</c> credit balance) + the
///     cash-only RCM output liability; should foot to zero for a fully discharged period.</item>
/// </list>
/// </summary>
public sealed record ElectronicLedgersView(
    DateOnly From,
    DateOnly To,
    // ---- Credit ledger (available ITC pools; closing debit balances) ----
    Money CreditCgst, Money CreditSgst, Money CreditIgst, Money CreditCess,
    // ---- Credit-ledger window movements (audit decomposition) ----
    Money CreditAdditionsCgst, Money CreditAdditionsSgst, Money CreditAdditionsIgst, Money CreditAdditionsCess,
    Money CreditUtilisedCgst, Money CreditUtilisedSgst, Money CreditUtilisedIgst, Money CreditUtilisedCess,
    Money CreditReversedCgst, Money CreditReversedSgst, Money CreditReversedIgst, Money CreditReversedCess,
    // ---- Liability ledger (outstanding output tax; closing credit balances) ----
    Money LiabilityCgst, Money LiabilitySgst, Money LiabilityIgst, Money LiabilityCess,
    Money RcmLiabilityCgst, Money RcmLiabilitySgst, Money RcmLiabilityIgst, Money RcmLiabilityCess,
    // ---- Cash ledger ----
    Money CashBalance,
    IReadOnlyDictionary<(GstTaxHead Major, GstMinorHead Minor), Money> CashCells)
{
    /// <summary>Σ available credit across the four pools.</summary>
    public Money TotalCredit => new(CreditCgst.Amount + CreditSgst.Amount + CreditIgst.Amount + CreditCess.Amount);

    /// <summary>Σ outstanding output liability across heads (excludes RCM &amp; cash-only heads; ER-2 cess separate).</summary>
    public Money TotalLiability =>
        new(LiabilityCgst.Amount + LiabilitySgst.Amount + LiabilityIgst.Amount + LiabilityCess.Amount);

    /// <summary>Σ outstanding RCM output liability across the GST heads (cash-only, ER-3).</summary>
    public Money TotalRcmLiability =>
        new(RcmLiabilityCgst.Amount + RcmLiabilitySgst.Amount + RcmLiabilityIgst.Amount + RcmLiabilityCess.Amount);

    /// <summary>Builds the electronic-ledgers statement for the whole company over <c>[from, to]</c>.</summary>
    public static ElectronicLedgersView Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);
        var gst = new GstService(company);

        // Credit pools = the Input {head} debit balances (available ITC). Zero when GST off / never accrued (ER-13).
        Money InputClosing(GstTaxHead head)
        {
            var l = gst.FindTaxLedger(head, GstTaxDirection.Input);
            return l is null ? Money.Zero : new Money(Math.Max(0m, LedgerBalances.SignedClosing(company, l, to)));
        }

        // Liability = the Output {head} credit balance (outstanding). A credit balance is negative signed ⇒ magnitude.
        Money OutputClosing(GstTaxHead head)
        {
            var l = gst.FindTaxLedger(head, GstTaxDirection.Output);
            if (l is null) return Money.Zero;
            var signed = LedgerBalances.SignedClosing(company, l, to);
            return new Money(signed < 0m ? -signed : 0m);
        }

        Money RcmOutputClosing(GstTaxHead head)
        {
            var l = gst.FindRcmOutputLedger(head);
            if (l is null) return Money.Zero;
            var signed = LedgerBalances.SignedClosing(company, l, to);
            return new Money(signed < 0m ? -signed : 0m);
        }

        // Window movements on the Input {head} pool, decomposed by side + adjustment tag (audit).
        (decimal Add, decimal Used, decimal Rev) InputMovement(GstTaxHead head)
        {
            var l = gst.FindTaxLedger(head, GstTaxDirection.Input);
            if (l is null) return (0m, 0m, 0m);
            decimal add = 0m, used = 0m, rev = 0m;
            foreach (var v in company.Vouchers)
            {
                if (v.Date < from) continue;
                var type = company.FindVoucherType(v.TypeId);
                if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;
                foreach (var line in v.Lines)
                {
                    if (line.LedgerId != l.Id) continue;
                    if (line.Side == DrCr.Debit) add += line.Amount.Amount;                 // forward + RCM ITC accrual
                    else if (line.Gst?.Adjustment == GstAdjustmentKind.SetOff) used += line.Amount.Amount;
                    else if (line.Gst?.Adjustment is { } adj && IsReversal(adj)) rev += line.Amount.Amount;
                }
            }
            return (add, used, rev);
        }

        var mvC = InputMovement(GstTaxHead.Central);
        var mvS = InputMovement(GstTaxHead.State);
        var mvI = InputMovement(GstTaxHead.Integrated);
        var mvCess = InputMovement(GstTaxHead.Cess);

        // Cash ledger: per (major, minor) cell = challan deposits − posted cash draws (reuses the deposit engine's
        // paisa-exact projection). Only cells with any deposit are surfaced.
        var deposit = new GstDepositService(company);
        var cells = new Dictionary<(GstTaxHead, GstMinorHead), Money>();
        foreach (var ch in company.GstChallans)
        {
            var key = (ch.MajorHead, ch.MinorHead);
            if (!cells.ContainsKey(key)) cells[key] = deposit.AvailableCash(ch.MajorHead, ch.MinorHead);
        }

        var cashLedger = company.FindLedgerByName(GstService.ElectronicCashLedgerName);
        var cashBalance = cashLedger is null
            ? Money.Zero
            : new Money(LedgerBalances.SignedClosing(company, cashLedger, to));

        return new ElectronicLedgersView(
            from, to,
            InputClosing(GstTaxHead.Central), InputClosing(GstTaxHead.State),
            InputClosing(GstTaxHead.Integrated), InputClosing(GstTaxHead.Cess),
            new Money(mvC.Add), new Money(mvS.Add), new Money(mvI.Add), new Money(mvCess.Add),
            new Money(mvC.Used), new Money(mvS.Used), new Money(mvI.Used), new Money(mvCess.Used),
            new Money(mvC.Rev), new Money(mvS.Rev), new Money(mvI.Rev), new Money(mvCess.Rev),
            OutputClosing(GstTaxHead.Central), OutputClosing(GstTaxHead.State),
            OutputClosing(GstTaxHead.Integrated), OutputClosing(GstTaxHead.Cess),
            RcmOutputClosing(GstTaxHead.Central), RcmOutputClosing(GstTaxHead.State),
            RcmOutputClosing(GstTaxHead.Integrated), RcmOutputClosing(GstTaxHead.Cess),
            cashBalance, cells);
    }

    private static bool IsReversal(GstAdjustmentKind adj) => adj is
        GstAdjustmentKind.ReversalRule37 or GstAdjustmentKind.ReversalRule37A or GstAdjustmentKind.ReversalRule42
        or GstAdjustmentKind.ReversalRule43 or GstAdjustmentKind.ReversalSection17_5
        or GstAdjustmentKind.ReversalIneligible or GstAdjustmentKind.ReversalCreditNote;
}
