using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The <b>ITC-gate advisory view</b> (Phase 9 slice 6b; RQ-15/RQ-26; §16(2)(aa); §17(5)) — a pure projection (like
/// <see cref="Gstr3b"/>) that surfaces, per period, the ITC figures side-by-side plus the reversal <b>candidates</b>
/// for S7. It <b>posts nothing</b> (ER-14): it takes a <see cref="Company"/> + a 2B snapshot and returns this record — no
/// <c>LedgerService</c>, no <c>EntryLine</c>. It is the SOLE surface of the §17(5)-blocked + Table-4(D)-ineligible +
/// §16(2)(aa) reversal candidates; S7 is the sole poster (RQ-27).
/// <list type="bullet">
///   <item><b>ITC in books</b> (<see cref="BooksEligible"/>) — eligible Input GST on posted purchases, EXCLUDING RCM
///     lines, §17(5)-blocked purchases (surfaced on <see cref="BlockedItc"/>, the GSTR-3B Table 4(B)(1) advisory line)
///     and other-ineligible purchases (surfaced on <see cref="IneligibleItc"/>, the Table 4(D) advisory line). §17(5)
///     blocked-ness and Table-4(D) ineligibility are attributed <b>per contributing line</b> (an item's stock-item block
///     or an as-voucher line's purchase-ledger block), so a bill mixing eligible + blocked lines splits its ITC by each
///     line's taxable-value share rather than routing the whole voucher to one pool.</item>
///   <item><b>ITC in 2B</b> (<see cref="Portal2b"/>) — the portal <c>ItcAvailable</c> figure (§16(2)(aa): ITC only if
///     reflected in 2B). Eligible books ITC that matches a 2B line is <see cref="Claimable"/>; the rest is
///     <see cref="NotInPortal"/> — ineligible this period.</item>
///   <item><b>ITC claimed in 3B</b> (<see cref="Claimed3b"/>) — the existing <see cref="Gstr3b"/> ITC triple (display-only).</item>
/// </list>
/// </summary>
public sealed record ItcGateView(
    DateOnly From,
    DateOnly To,
    ItcTriple BooksEligible,
    ItcTriple BlockedItc,
    ItcTriple IneligibleItc,
    ItcTriple Claimable,
    ItcTriple NotInPortal,
    ItcTriple Portal2b,
    ItcTriple Claimed3b,
    IReadOnlyList<ItcReversalCandidate> ReversalCandidates)
{
    /// <summary>Σ books eligible ITC (§17(5)-blocked + Table-4(D)-ineligible + RCM excluded) across the GST heads.</summary>
    public Money BooksEligibleTotal => BooksEligible.Total;

    /// <summary>Σ §17(5)-blocked ITC (Table 4(B)(1)) across the heads — never claimable.</summary>
    public Money BlockedTotal => BlockedItc.Total;

    /// <summary>Σ Table-4(D) ineligible ITC (non-business / personal / time-barred) across the heads — never claimable.</summary>
    public Money IneligibleTotal => IneligibleItc.Total;

    /// <summary>Σ eligible ITC reflected in 2B (§16(2)(aa) claimable) across the heads.</summary>
    public Money ClaimableTotal => Claimable.Total;

    /// <summary>Σ eligible books ITC with NO 2B line this period (§16(2)(aa)-ineligible) across the heads.</summary>
    public Money NotInPortalTotal => NotInPortal.Total;

    /// <summary>
    /// Builds the advisory ITC-gate view for one 2B snapshot over <c>[from, to]</c>. Pure + deterministic (ER-14): runs
    /// the reconciler + GSTR-3B as read-only projections and surfaces the reversal candidates; posts nothing. The
    /// reconciliation tolerance is read from the company GST config (default exact ⇒ byte-identical when off, ER-13).
    /// </summary>
    public static ItcGateView Build(Company company, Gstr2bSnapshot snapshot, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(snapshot);

        var tolerance = company.Gst?.ReconTolerance ?? ReconTolerance.Exact;
        var report = Gstr2bReconciler.Reconcile(company, snapshot, from, to, tolerance);

        // A voucher is "in 2B" (claimable) iff the reconciler matched it (Matched or PartialMismatch); its eligible ITC is
        // §16(2)(aa)-ineligible this period iff the reconciler surfaced it as InBooksOnly (a booked purchase no 2B line
        // matched) OR it can never appear in 2B at all (no supplier GSTIN ⇒ excluded from the recon register). Blocked
        // (§17(5)) and Table-4(D)-ineligible ITC are ring-fenced out of the eligible pool per contributing line.
        var matchedVoucherIds = report.Matched.Select(m => m.MatchedVoucherId)
            .Concat(report.PartialMismatches.Select(m => m.MatchedVoucherId)).ToHashSet();
        var inBooksOnlyIds = report.InBooksOnly.Select(e => e.VoucherId).ToHashSet();

        decimal bkCgst = 0m, bkSgst = 0m, bkIgst = 0m;   // eligible (books)
        decimal blCgst = 0m, blSgst = 0m, blIgst = 0m;   // §17(5)-blocked (Table 4(B)(1))
        decimal ieCgst = 0m, ieSgst = 0m, ieIgst = 0m;   // Table-4(D) ineligible
        decimal clCgst = 0m, clSgst = 0m, clIgst = 0m;   // claimable (eligible ∩ in-2B)
        decimal npCgst = 0m, npSgst = 0m, npIgst = 0m;   // eligible not-in-portal (§16(2)(aa))
        var candidates = new List<ItcReversalCandidate>();

        foreach (var (voucher, _) in GstReportSupport.PostedGstVouchers(company, from, to, GstTaxDirection.Input))
        {
            // Per-head forward (non-RCM) input tax posted on this voucher — RCM ITC is its own 3B bucket (excluded here).
            decimal hCgst = 0m, hSgst = 0m, hIgst = 0m;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g || g.IsReverseCharge) continue;
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: hCgst += line.Amount.Amount; break;
                    case GstTaxHead.State: hSgst += line.Amount.Amount; break;
                    case GstTaxHead.Integrated: hIgst += line.Amount.Amount; break;
                    // Cess (ring-fenced) is not part of the CGST/SGST/IGST ITC triple.
                }
            }
            if (hCgst <= 0m && hSgst <= 0m && hIgst <= 0m) continue; // no forward ITC on this voucher (all RCM / none)

            // Attribute each head's tax across the three ITC pools PER CONTRIBUTING LINE, by taxable-value share (a bill
            // mixing a blocked line + an eligible line must NOT route the whole voucher to the blocked pool). Paisa-exact
            // largest-remainder split (the same paisa-conservation engine the additional-cost apportionment uses).
            var (eligBase, blockedBase, ineligBase) = ClassifyBaseValue(company, voucher);
            var (eC, blC, ieC) = SplitHeadTax(hCgst, eligBase, blockedBase, ineligBase);
            var (eS, blS, ieS) = SplitHeadTax(hSgst, eligBase, blockedBase, ineligBase);
            var (eI, blI, ieI) = SplitHeadTax(hIgst, eligBase, blockedBase, ineligBase);

            var inPortal = matchedVoucherIds.Contains(voucher.Id);
            // A booked purchase whose eligible ITC has no 2B line this period is §16(2)(aa)-ineligible: either the
            // reconciler flagged it InBooksOnly, OR it can NEVER appear in 2B because the supplier carries no GSTIN (so it
            // is excluded from the recon register — otherwise its eligible ITC would land in neither Claimable nor
            // NotInPortal, breaking BooksEligible = Claimable + NotInPortal).
            var notInPortal = inBooksOnlyIds.Contains(voucher.Id)
                || (!inPortal && !HasSupplierGstin(company, voucher));

            // §17(5)-blocked pool (Table 4(B)(1)).
            blCgst += blC.Amount; blSgst += blS.Amount; blIgst += blI.Amount;
            // Table-4(D) ineligible pool.
            ieCgst += ieC.Amount; ieSgst += ieS.Amount; ieIgst += ieI.Amount;
            // Eligible pool + its §16(2)(aa) claimable / not-in-portal split.
            bkCgst += eC.Amount; bkSgst += eS.Amount; bkIgst += eI.Amount;
            if (inPortal) { clCgst += eC.Amount; clSgst += eS.Amount; clIgst += eI.Amount; }
            else if (notInPortal) { npCgst += eC.Amount; npSgst += eS.Amount; npIgst += eI.Amount; }

            // Reversal candidates — no longer mutually exclusive: a mixed bill surfaces a blocked AND an ineligible AND
            // (for its eligible-not-in-portal share) a §16(2)(aa) candidate. S7 decides the actual reversal.
            var vBlocked = blC.Amount + blS.Amount + blI.Amount;
            var vInelig = ieC.Amount + ieS.Amount + ieI.Amount;
            var vElig = eC.Amount + eS.Amount + eI.Amount;
            if (vBlocked > 0m)
                candidates.Add(new ItcReversalCandidate(
                    ItcReversalReason.Section17_5Blocked, voucher.Id, null, new Money(vBlocked),
                    "§17(5) blocked credit — ITC must not be availed."));
            if (vInelig > 0m)
                candidates.Add(new ItcReversalCandidate(
                    ItcReversalReason.Ineligible, voucher.Id, null, new Money(vInelig),
                    "Ineligible ITC (Table 4(D)) — non-business / personal / time-barred."));
            if (notInPortal && vElig > 0m)
                candidates.Add(new ItcReversalCandidate(
                    ItcReversalReason.Section16_2aaNotInPortal, voucher.Id, null, new Money(vElig),
                    "§16(2)(aa) — booked ITC not reflected in GSTR-2B this period."));
        }

        // Portal 2B ITC-Available figure (§16(2)(aa) basis). Exclude the supplier-flagged RCM lines (they bypass 2B ITC).
        decimal p2bCgst = 0m, p2bSgst = 0m, p2bIgst = 0m;
        foreach (var l in snapshot.Lines)
        {
            if (!l.ItcAvailable || l.ReverseCharge) continue;
            p2bCgst += PaisaToRupees(l.CgstPaisa);
            p2bSgst += PaisaToRupees(l.SgstPaisa);
            p2bIgst += PaisaToRupees(l.IgstPaisa);
        }

        // Supplier credit/debit notes that were ACCEPTED (or deemed-accepted) are the ITC-reversal event for the recipient
        // (§3.2 hand-off to S7). ACCEPTING a supplier credit note reverses the recipient's ITC; REJECTING (or keeping it
        // Pending) means no reversal is due. On an Accept the recipient may have declared a partial (or no) reversal —
        // honour that; otherwise the whole forward tax of the note is the suggested reversal. Advisory only (ER-14).
        foreach (var l in snapshot.Lines)
        {
            if (!IsCreditDebitNote(l.DocType) || l.ReverseCharge) continue;
            if (ImsService.EffectiveStatus(company, l) != ImsStatus.Accepted) continue; // Rejected/Pending ⇒ no reversal
            var action = company.FindImsActionForLine(l.Id);
            decimal suggested;
            if (action is { NoReversalDeclared: true })
                suggested = 0m;                                                    // explicit no-reversal declaration
            else if (action is { DeclaredReversalPaisa: > 0 } a)
                suggested = PaisaToRupees(a.DeclaredReversalPaisa!.Value);         // Oct-2025 partial declared reversal
            else
                suggested = PaisaToRupees(l.IgstPaisa + l.CgstPaisa + l.SgstPaisa); // full forward tax (incl. deemed-accept)
            candidates.Add(new ItcReversalCandidate(
                ItcReversalReason.ImsAcceptedCreditNote, null, l.Id, new Money(suggested),
                "Accepted (or deemed-accepted) supplier credit/debit note — ITC reversal."));
        }

        var g3b = Gstr3b.Build(company, from, to);

        return new ItcGateView(from, to,
            new ItcTriple(new Money(bkCgst), new Money(bkSgst), new Money(bkIgst)),
            new ItcTriple(new Money(blCgst), new Money(blSgst), new Money(blIgst)),
            new ItcTriple(new Money(ieCgst), new Money(ieSgst), new Money(ieIgst)),
            new ItcTriple(new Money(clCgst), new Money(clSgst), new Money(clIgst)),
            new ItcTriple(new Money(npCgst), new Money(npSgst), new Money(npIgst)),
            new ItcTriple(new Money(p2bCgst), new Money(p2bSgst), new Money(p2bIgst)),
            new ItcTriple(g3b.ItcCgst, g3b.ItcSgst, g3b.ItcIgst),
            candidates);
    }

    /// <summary>
    /// The posted forward taxable value of an inward voucher, split by the §17(5)/Table-4(D) eligibility of each
    /// <b>contributing line</b> (RQ-26). An item-invoice voucher is classified from its item lines
    /// (<see cref="StockItemGstDetails.ItcEligibility"/> on each stock item); an as-voucher purchase from its
    /// purchase/expense legs (the ledger's <see cref="Domain.Ledger.SalesPurchaseGst"/> block). The party/cash-bank
    /// counter-leg, the tax legs and Round Off are excluded. An unclassified line defaults to <see cref="ItcEligibility.Eligible"/>.
    /// The three sums drive the per-head paisa-exact tax split.
    /// </summary>
    private static (decimal Eligible, decimal Blocked, decimal Ineligible) ClassifyBaseValue(Company company, Voucher voucher)
    {
        decimal elig = 0m, blocked = 0m, ineligible = 0m;

        void Add(ItcEligibility eligibility, decimal value)
        {
            switch (eligibility)
            {
                case ItcEligibility.BlockedSection17_5: blocked += value; break;
                case ItcEligibility.Ineligible: ineligible += value; break;
                default: elig += value; break;
            }
        }

        if (voucher.HasInventoryLines)
        {
            foreach (var il in voucher.InventoryLines)
            {
                var eligibility = company.FindStockItem(il.StockItemId)?.Gst?.ItcEligibility ?? ItcEligibility.Eligible;
                Add(eligibility, il.Value.Amount);
            }
            return (elig, blocked, ineligible);
        }

        // As-voucher purchase: the assessable base lines are the purchase/expense legs — an expense/purchase P&L ledger,
        // OR any ledger explicitly carrying a purchase GST block (covers a capital-goods asset ledger flagged §17(5)).
        // Exclude the tax legs, the party leg, the cash/bank counter-leg, Duties & Taxes and Round Off.
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not null) continue;                      // a tax leg
            if (line.LedgerId == voucher.PartyId) continue;          // the party (creditor) leg
            var ledger = company.FindLedger(line.LedgerId);
            if (ledger is null) continue;
            var isBase = ClassificationRules.IsProfitAndLossLedger(ledger, company) || ledger.SalesPurchaseGst is not null;
            if (!isBase) continue;
            if (ClassificationRules.IsDutiesAndTaxesLedger(ledger, company)) continue;
            if (ClassificationRules.IsCashOrBankLedger(ledger, company)) continue;
            if (string.Equals(ledger.Name, GstService.RoundOffLedgerName, StringComparison.OrdinalIgnoreCase)) continue;
            var eligibility = ledger.SalesPurchaseGst?.ItcEligibility ?? ItcEligibility.Eligible;
            Add(eligibility, line.Amount.Amount);
        }
        return (elig, blocked, ineligible);
    }

    /// <summary>Splits one GST head's posted tax across the (eligible, §17(5)-blocked, Table-4(D)-ineligible) pools by the
    /// contributing lines' taxable-value share, paisa-exact (largest-remainder). When nothing is blocked/ineligible the
    /// whole head is eligible (a fast path that also avoids apportioning over an all-zero base).</summary>
    private static (Money Eligible, Money Blocked, Money Ineligible) SplitHeadTax(
        decimal headTax, decimal eligBase, decimal blockedBase, decimal ineligBase)
    {
        if (headTax <= 0m) return (Money.Zero, Money.Zero, Money.Zero);
        if (blockedBase <= 0m && ineligBase <= 0m) return (new Money(headTax), Money.Zero, Money.Zero);
        var shares = AdditionalCostApportionment.Allocate(new[] { eligBase, blockedBase, ineligBase }, new Money(headTax));
        return (shares[0], shares[1], shares[2]);
    }

    /// <summary>True iff the purchase's supplier (party ledger) carries a GSTIN — a no-GSTIN purchase can never appear in
    /// 2B, so its eligible ITC is §16(2)(aa)-ineligible this period.</summary>
    private static bool HasSupplierGstin(Company company, Voucher voucher) =>
        voucher.PartyId is Guid pid && !string.IsNullOrWhiteSpace(company.FindLedger(pid)?.PartyGst?.Gstin);

    private static bool IsCreditDebitNote(Gstr2bDocType docType) => docType is
        Gstr2bDocType.CreditNote or Gstr2bDocType.DebitNote or
        Gstr2bDocType.CreditNoteAmendment or Gstr2bDocType.DebitNoteAmendment;

    private static decimal PaisaToRupees(long paisa) => paisa / 100m;
}

/// <summary>A CGST/SGST/IGST money triple for the ITC-gate (Phase 9 slice 6b). Compensation Cess is ring-fenced out of the
/// forward-tax triple (ER-2), consistent with the <see cref="Gstr3b"/> ITC figures.</summary>
public readonly record struct ItcTriple(Money Cgst, Money Sgst, Money Igst)
{
    /// <summary>Σ across the three GST heads.</summary>
    public Money Total => new(Cgst.Amount + Sgst.Amount + Igst.Amount);

    /// <summary>The all-zero triple.</summary>
    public static ItcTriple Zero => new(Money.Zero, Money.Zero, Money.Zero);
}

/// <summary>Why an ITC reversal candidate was surfaced for S7 (Phase 9 slice 6b; RQ-27; advisory grouping only).</summary>
public enum ItcReversalReason
{
    /// <summary>§17(5) blocked credit (Table 4(B)(1)) — the ITC must not be availed.</summary>
    Section17_5Blocked,

    /// <summary>Table-4(D) ineligible ITC (non-business / personal use, §16(4) time-barred, etc.).</summary>
    Ineligible,

    /// <summary>§16(2)(aa) — booked ITC not reflected in GSTR-2B this period (supplier has not filed / no GSTIN).</summary>
    Section16_2aaNotInPortal,

    /// <summary>An Accepted (or deemed-accepted) supplier credit/debit note — the recipient's ITC reversal event.</summary>
    ImsAcceptedCreditNote,
}

/// <summary>An ITC reversal <b>candidate</b> surfaced for the S7 poster (Phase 9 slice 6b; RQ-27) — carries the identity of
/// the affected purchase (<paramref name="VoucherId"/>) or 2B line (<paramref name="LineId"/>) + the <i>suggested</i>
/// reversal amount. The ITC-gate NEVER posts a reversal (ER-14); S7 is the sole poster.</summary>
/// <param name="Reason">Why the candidate was surfaced.</param>
/// <param name="VoucherId">The affected books purchase voucher, or <c>null</c> (for a 2B-line-keyed candidate).</param>
/// <param name="LineId">The affected 2B line, or <c>null</c> (for a voucher-keyed candidate).</param>
/// <param name="SuggestedReversal">The suggested reversal amount (forward CGST+SGST+IGST); S7 decides the actual figure.</param>
/// <param name="Description">A human-readable reason for the reversal.</param>
public readonly record struct ItcReversalCandidate(
    ItcReversalReason Reason, Guid? VoucherId, Guid? LineId, Money SuggestedReversal, string Description);
