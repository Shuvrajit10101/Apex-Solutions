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
    public static ItcGateView Build(
        Company company, Gstr2bSnapshot snapshot, DateOnly from, DateOnly to, Guid? registrationId = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(snapshot);

        var tolerance = company.Gst?.ReconTolerance ?? ReconTolerance.Exact;
        // 🔴 THE REGISTRATION SCOPE MUST TRAVEL. This call dropped `registrationId`, which did two things. For a
        // SINGLE-registration book it silently reconciled the whole book — harmless, because that is the same set.
        // For a MULTI-registration book it made the gate UNREACHABLE: the unscoped sweep hits
        // GstReportSupport.EnsureRegistrationScoped and throws "a GST return must name the registration it is filed
        // for", so a company with a branch — or any ISD company, which has at least two registrations by
        // construction — could not open the ITC gate at all. Found by the purchase-return measurement, which needed
        // a two-registration book to measure anything (census 6.23's scoping rule, same family as the GSTR-1/3B
        // registration filters).
        var report = Gstr2bReconciler.Reconcile(company, snapshot, from, to, tolerance, registrationId);

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
        // 🔴 T1-72. The returns are STAGED here and applied after the sweep — see the long note at the staging site.
        var returns = new List<ReturnedCredit>();

        foreach (var (voucher, type) in GstReportSupport.PostedGstVouchers(company, from, to, GstTaxDirection.Input, registrationId))
        {
            // 🔴 T1-64, MEASURED. A PURCHASE-RETURN DEBIT NOTE REDUCES THE CLAIM; this loop added it. Measured on a
            // ₹50,000 input service at 18% (₹9,000 ITC) with ₹20,000 returned (₹3,600): the gate reported
            // BooksEligible CGST 6,300.00 + SGST 6,300.00 = ₹12,600.00 where ₹5,400.00 was available — 133%
            // overstated, on the screen an operator reads to decide what to claim in GSTR-3B §4.
            //
            // 🔴 THE SIGN IS APPLIED TO THE ACCUMULATORS, NOT TO THE HEAD TOTALS, AND THAT IS DELIBERATE. The head
            // figures below feed SplitHeadTax, a largest-remainder paisa split that assumes a NON-NEGATIVE amount;
            // handing it a negative would have been a new defect in the apportionment engine rather than a fix to
            // this one. Keeping hCgst..hCess positive also keeps the `<= 0m` no-forward-ITC guard meaning what it
            // says — negating the heads instead would have made all three negative, tripped that guard, and SKIPPED
            // the return entirely, leaving the overstatement exactly where it was while looking fixed.
            //
            // A §34 note on an outward supply returns 0 and contributes nothing here (it is not ITC at all).
            var sign = GstReportSupport.SignOf(company, voucher, type.BaseType);
            if (sign == 0) continue;

            // Per-head forward (non-RCM) input tax posted on this voucher — RCM ITC is its own 3B bucket (excluded here).
            // Cess is ring-fenced OUT of the CGST/SGST/IGST triple (ER-2) but IS accumulated here so a blocked / ineligible
            // item's cess ITC can be reversed as its own ring-fenced cess leg (S7b), never bled into a GST head.
            decimal hCgst = 0m, hSgst = 0m, hIgst = 0m, hCess = 0m;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g || g.IsReverseCharge) continue;
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: hCgst += line.Amount.Amount; break;
                    case GstTaxHead.State: hSgst += line.Amount.Amount; break;
                    case GstTaxHead.Integrated: hIgst += line.Amount.Amount; break;
                    case GstTaxHead.Cess: hCess += line.Amount.Amount; break;
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
            // The cess split mirrors the GST-head split (same per-line eligibility bases) — kept OUT of the triples, used
            // only to give each candidate its exact ring-fenced cess so the reversal never carves cess out of a GST pool.
            var (eCess, blCess, ieCess) = SplitHeadTax(hCess, eligBase, blockedBase, ineligBase);

            var inPortal = matchedVoucherIds.Contains(voucher.Id);
            // A booked purchase whose eligible ITC has no 2B line this period is §16(2)(aa)-ineligible: either the
            // reconciler flagged it InBooksOnly, OR it can NEVER appear in 2B because the supplier carries no GSTIN (so it
            // is excluded from the recon register — otherwise its eligible ITC would land in neither Claimable nor
            // NotInPortal, breaking BooksEligible = Claimable + NotInPortal).
            var notInPortal = inBooksOnlyIds.Contains(voucher.Id)
                || (!inPortal && !HasSupplierGstin(company, voucher));

            // Every TOTAL pool carries the sign: a purchase return nets the claim DOWN in whichever pool its own
            // lines classify into, so a returned blocked item reduces the blocked pool and a returned eligible item
            // reduces the eligible pool.
            // §17(5)-blocked pool (Table 4(B)(1)).
            blCgst += sign * blC.Amount; blSgst += sign * blS.Amount; blIgst += sign * blI.Amount;
            // Table-4(D) ineligible pool.
            ieCgst += sign * ieC.Amount; ieSgst += sign * ieS.Amount; ieIgst += sign * ieI.Amount;
            // Eligible pool (its §16(2)(aa) claimable / not-in-portal split is below).
            bkCgst += sign * eC.Amount; bkSgst += sign * eS.Amount; bkIgst += sign * eI.Amount;

            // 🔴🔴 T1-72, MEASURED. A RETURN IS NOT A REVERSAL CANDIDATE — AND IT IS NOT IN EITHER §16(2)(aa)
            // BUCKET EITHER. Both facts have the SAME cause, and getting the first right without the second is what
            // broke the identity this comment used to claim it preserved.
            //
            // `inPortal` / `notInPortal` above are keyed on the reconciler's matched / in-books-only sets, and the
            // same slice CORRECTLY excluded a return from the books register (Gstr2bReconciler.BuildBooksRegister:
            // our own purchase-return debit note is not a supplier invoice and has no 2B line to pair with). So a
            // return is in NEITHER set — and because it carries the supplier's GSTIN, the `!HasSupplierGstin`
            // fallback does not catch it either. The eligible TOTAL netted; neither sub-pool did.
            //
            // MEASURED on a ₹50,000 input service at 18% (₹9,000 ITC) with ₹20,000 returned (₹3,600) and an empty
            // 2B: BooksEligible 5,400.00 but NotInPortal 9,000.00 — broken by exactly the return, and the gate
            // screen renders "of which not in 2B 9,000.00" directly beneath "ITC in books — eligible 5,400.00"
            // (ItcGateReportViewModel), a part larger than its whole. The candidate list was worse: it is built from
            // the ORIGINAL invoice's un-netted share, so it advised reversing the whole ₹9,000.00 out of a pool
            // holding ₹5,400.00. With the same service flagged §17(5)-blocked that candidate carries a RULE, so
            // GstReversalService.PostFromCandidate POSTS it head-for-head — a ₹3,600.00 over-reversal in the BOOKS,
            // removing credit the return had already removed. (A §16(2)(aa) candidate posts nothing — it is a
            // deferral and PostFromCandidate returns null for it — so the posted money travels by the blocked /
            // ineligible route, not that one.)
            //
            // Both halves are deferred to a post-sweep pass because the routing question cannot be answered inside
            // the loop: the proportional fallback below needs the FINAL pool sizes, and the candidate a return nets
            // against may not have been built yet.
            if (sign < 0)
            {
                returns.Add(new ReturnedCredit(
                    GstReportSupport.CdnLinkFor(company, voucher)?.OriginalInvoiceVoucherId,
                    eC.Amount, eS.Amount, eI.Amount,
                    ToPaisa(eC), ToPaisa(eS), ToPaisa(eI), ToPaisa(eCess),
                    ToPaisa(blC), ToPaisa(blS), ToPaisa(blI), ToPaisa(blCess),
                    ToPaisa(ieC), ToPaisa(ieS), ToPaisa(ieI), ToPaisa(ieCess)));
                continue;
            }

            // The §16(2)(aa) claimable / not-in-portal split of the eligible pool (forward documents only; `sign` is
            // provably 1 here — 0 and negative both `continue` above).
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
                    "§17(5) blocked credit — ITC must not be availed.",
                    ToPaisa(blC), ToPaisa(blS), ToPaisa(blI), ToPaisa(blCess)));
            if (vInelig > 0m)
                candidates.Add(new ItcReversalCandidate(
                    ItcReversalReason.Ineligible, voucher.Id, null, new Money(vInelig),
                    "Ineligible ITC (Table 4(D)) — non-business / personal / time-barred.",
                    ToPaisa(ieC), ToPaisa(ieS), ToPaisa(ieI), ToPaisa(ieCess)));
            if (notInPortal && vElig > 0m)
                candidates.Add(new ItcReversalCandidate(
                    ItcReversalReason.Section16_2aaNotInPortal, voucher.Id, null, new Money(vElig),
                    "§16(2)(aa) — booked ITC not reflected in GSTR-2B this period.",
                    ToPaisa(eC), ToPaisa(eS), ToPaisa(eI), ToPaisa(eCess)));
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
        //  🔴 T1-72 — APPLY THE STAGED RETURNS to the §16(2)(aa) split and to the candidate list.
        //
        //  The invariants this pass exists to hold, both of which a return broke:
        //    1. BooksEligible = Claimable + NotInPortal — the gate screen renders the first as a total and the other
        //       two as its sub-rows, so a break makes a sub-row exceed its own total on the face of the report.
        //    2. Σ candidates(reason) = the pool that reason is drawn from. S7 POSTS from this list, so a candidate
        //       larger than its pool is an instruction to remove money that is no longer there. This identity holds
        //       on main before any return (both sides are the same sum over the same vouchers) and is restored here.
        //
        //  ATTRIBUTION, and the one honest limit of it. A return reduces the credit on its ORIGINAL invoice, so the
        //  pool to net is the pool that invoice fell into — exactly resolvable when the §34 note names the original
        //  (GstCreditDebitNoteLink.OriginalInvoiceVoucherId, what the entry screen records when the operator picks
        //  the invoice). It is NOT resolvable for the "Consolidated…" note shape the same screen offers, which
        //  leaves that id null by design, nor for a return whose original was bought in an earlier period and so is
        //  not in this sweep at all. For those the reduction is split across the two pools PRO RATA to their own
        //  sizes, paisa-exact on the shared largest-remainder engine: a neutral allocation that asserts nothing
        //  about which invoice was returned, reproduces the unambiguous answer exactly whenever one pool is empty
        //  (all of a return against a fully-2B-matched book lands on Claimable; all of one against a book with no
        //  2B line lands on NotInPortal), and can never invent a pool out of nothing. 🔴 DIVERGENCE, LABELLED AS
        //  OURS (R7): neither CBIC Rule 39 nor the GSTN documentation says how an unattributed return should be
        //  apportioned between a claimable and a not-yet-in-2B pool, because the portal has no such advisory view —
        //  the pro rata is our choice, not a cited rule, and the exact attribution above is preferred wherever the
        //  link makes it available.
        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
        if (returns.Count > 0)
        {
            // (a) The blocked and Table-4(D) pools have no portal question at all — their candidates net directly.
            foreach (var r in returns)
            {
                NetCandidates(candidates, ItcReversalReason.Section17_5Blocked, r.OriginalVoucherId,
                    r.BlockedCgstPaisa, r.BlockedSgstPaisa, r.BlockedIgstPaisa, r.BlockedCessPaisa);
                NetCandidates(candidates, ItcReversalReason.Ineligible, r.OriginalVoucherId,
                    r.IneligCgstPaisa, r.IneligSgstPaisa, r.IneligIgstPaisa, r.IneligCessPaisa);
            }

            // (b) The eligible share: route each head between Claimable and NotInPortal, then net the §16(2)(aa)
            //     candidates by the part that came out of the not-in-portal pool (the part that came out of
            //     Claimable was never a candidate — a matched invoice raises no §16(2)(aa) advice).
            //     Exactly-attributed returns are applied FIRST so the pro-rata ones see the pools as they then
            //     stand, which keeps a mixed book's answer independent of voucher order within each group.
            foreach (var r in returns.Where(x => x.OriginalVoucherId is not null))
            {
                var oid = r.OriginalVoucherId!.Value;
                bool? toClaimable =
                    matchedVoucherIds.Contains(oid) ? true
                    : inBooksOnlyIds.Contains(oid) ? false
                    : company.FindVoucher(oid) is { } orig && !HasSupplierGstin(company, orig) ? false
                    : null;   // the original is outside this sweep — fall through to the pro rata below
                if (toClaimable is null) continue;

                if (toClaimable.Value)
                {
                    clCgst -= r.EligCgst; clSgst -= r.EligSgst; clIgst -= r.EligIgst;
                }
                else
                {
                    npCgst -= r.EligCgst; npSgst -= r.EligSgst; npIgst -= r.EligIgst;
                    NetCandidates(candidates, ItcReversalReason.Section16_2aaNotInPortal, oid,
                        r.EligCgstPaisa, r.EligSgstPaisa, r.EligIgstPaisa, r.EligCessPaisa);
                }
                r.Applied = true;
            }

            // The pro-rata remainder: summed per head first, so the allocation is computed ONCE against one set of
            // pool sizes and is therefore order-independent across the unattributed returns.
            decimal rtCgst = 0m, rtSgst = 0m, rtIgst = 0m;
            foreach (var r in returns.Where(x => !x.Applied))
            {
                rtCgst += r.EligCgst; rtSgst += r.EligSgst; rtIgst += r.EligIgst;
            }
            if (rtCgst > 0m || rtSgst > 0m || rtIgst > 0m)
            {
                var (clC2, npC2) = RouteReturnedHead(rtCgst, clCgst, npCgst);
                var (clS2, npS2) = RouteReturnedHead(rtSgst, clSgst, npSgst);
                var (clI2, npI2) = RouteReturnedHead(rtIgst, clIgst, npIgst);
                clCgst -= clC2; clSgst -= clS2; clIgst -= clI2;
                npCgst -= npC2; npSgst -= npS2; npIgst -= npI2;

                // Net the §16(2)(aa) candidates by the not-in-portal part. The cess reduction rides with the GST
                // heads in the same proportion the routing produced, so a ring-fenced cess candidate (ER-2) cannot
                // outlive the GST credit it was blocked alongside.
                long rtCessPaisa = 0L;
                foreach (var r in returns.Where(x => !x.Applied)) rtCessPaisa += r.EligCessPaisa;
                var returnedGst = rtCgst + rtSgst + rtIgst;
                var npGst = npC2 + npS2 + npI2;
                var npCessPaisa = returnedGst > 0m
                    ? (long)Math.Round(rtCessPaisa * npGst / returnedGst, MidpointRounding.AwayFromZero)
                    : 0L;
                NetCandidates(candidates, ItcReversalReason.Section16_2aaNotInPortal, preferVoucherId: null,
                    ToPaisa(new Money(npC2)), ToPaisa(new Money(npS2)), ToPaisa(new Money(npI2)), npCessPaisa);
            }

            // A candidate netted to nothing is no longer advice — drop it. Only VOUCHER-keyed candidates are
            // considered: a 2B-line-keyed credit-note candidate is legitimately zero when the recipient declared
            // no reversal, and the screen is meant to show that one (PostFromCandidate posts nothing for it).
            candidates.RemoveAll(c => c.VoucherId is not null
                && c.CgstPaisa <= 0 && c.SgstPaisa <= 0 && c.IgstPaisa <= 0 && c.CessPaisa <= 0);
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

            // Reverse the forward tax of the note per head. A partial declared reversal (Oct-2025 IMS) is apportioned
            // across the GST heads by their per-head tax weight (paisa-exact); its cess is reversed in the SAME
            // proportion (ring-fenced, ER-2). Full / deemed-accept reverses the whole forward tax head-for-head. This
            // per-head breakdown is what S7b posts head-for-head (never a re-split of the GST-only total across weights
            // that include cess — the S7b FIX-1 cess-bleed bug).
            long fullGst = l.CgstPaisa + l.SgstPaisa + l.IgstPaisa;
            long cgstRev, sgstRev, igstRev, cessRev;
            if (action is { NoReversalDeclared: true })
            {
                cgstRev = sgstRev = igstRev = cessRev = 0;                          // explicit no-reversal declaration
            }
            else if (action is { DeclaredReversalPaisa: > 0 } a && a.DeclaredReversalPaisa!.Value < fullGst)
            {
                long declared = a.DeclaredReversalPaisa!.Value;                     // partial declared reversal
                var sh = AdditionalCostApportionment.Allocate(
                    new decimal[] { l.CgstPaisa, l.SgstPaisa, l.IgstPaisa }, new Money(declared / 100m));
                cgstRev = ToPaisa(sh[0]); sgstRev = ToPaisa(sh[1]); igstRev = ToPaisa(sh[2]);
                cessRev = fullGst > 0
                    ? (long)Math.Round((decimal)l.CessPaisa * declared / fullGst, MidpointRounding.AwayFromZero) : 0;
            }
            else
            {
                cgstRev = l.CgstPaisa; sgstRev = l.SgstPaisa; igstRev = l.IgstPaisa; cessRev = l.CessPaisa; // full forward tax
            }
            candidates.Add(new ItcReversalCandidate(
                ItcReversalReason.ImsAcceptedCreditNote, null, l.Id, new Money(PaisaToRupees(cgstRev + sgstRev + igstRev)),
                "Accepted (or deemed-accepted) supplier credit/debit note — ITC reversal.",
                cgstRev, sgstRev, igstRev, cessRev));
        }

        // 🔴 SECOND DROPPED SCOPE, same defect as the Reconcile call above: the "Claimed in 3B" column is the figure
        // the gate compares its own books sweep against, so reading an UNSCOPED 3B would have compared one
        // registration's books with every registration's claim. On a multi-registration book it threw before it could
        // — which is how both drops stayed invisible.
        var g3b = Gstr3b.Build(company, from, to, registrationId);

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
    /// <para>🔴 <b>Made public for census row 6.24 (ISD), and deliberately SHARED rather than re-implemented.</b>
    /// Rule 39(1)(g) requires an Input Service Distributor to "<i>separately distribute the amount of ineligible
    /// input tax credit (ineligible under the provisions of sub-section (5) of section 17 or otherwise) and the
    /// amount of eligible input tax credit</i>", which is the same §17(5)/Table-4(D) question this method already
    /// answers for the ITC gate. A second copy in <see cref="Gstr6"/> would be free to drift, and the two would
    /// then disagree about the same voucher on two filed documents. Nothing about the behaviour changed here.</para>
    /// </summary>
    public static (decimal Eligible, decimal Blocked, decimal Ineligible) ClassifyBaseValue(Company company, Voucher voucher)
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

    /// <summary>
    /// 🔴 One staged purchase-return (T1-72): the per-pool credit a return takes back OUT, carried from the sweep to
    /// the post-sweep pass that routes it. Rupee fields drive the <see cref="Claimable"/> / <see cref="NotInPortal"/>
    /// pools; the paisa fields net the matching reversal candidates head-for-head, which is how S7 posts.
    /// <paramref name="OriginalVoucherId"/> is the §34-linked original when the note names one, and <c>null</c> for
    /// the supported "Consolidated…" shape — the two cases the routing treats differently.
    /// </summary>
    private sealed record ReturnedCredit(
        Guid? OriginalVoucherId,
        decimal EligCgst, decimal EligSgst, decimal EligIgst,
        long EligCgstPaisa, long EligSgstPaisa, long EligIgstPaisa, long EligCessPaisa,
        long BlockedCgstPaisa, long BlockedSgstPaisa, long BlockedIgstPaisa, long BlockedCessPaisa,
        long IneligCgstPaisa, long IneligSgstPaisa, long IneligIgstPaisa, long IneligCessPaisa)
    {
        /// <summary>Set once the eligible share has been attributed exactly, so the pro-rata pass skips it.</summary>
        public bool Applied { get; set; }
    }

    /// <summary>
    /// Nets a return's per-head paisa reduction out of the candidates carrying one <paramref name="reason"/>, in
    /// place. The candidate naming <paramref name="preferVoucherId"/> — the invoice the return actually adjusts — is
    /// reduced FIRST, so an exactly-attributed return lands on its own document; whatever a candidate cannot absorb
    /// spills to the next candidate of the same reason in the sweep's deterministic order. Each head clamps at zero:
    /// a candidate is advice to reverse credit, and there is no such thing as advice to reverse a negative amount
    /// (the over-return case shows on the POOL, which may go negative, and is reported there).
    /// </summary>
    private static void NetCandidates(
        List<ItcReversalCandidate> candidates, ItcReversalReason reason, Guid? preferVoucherId,
        long cgst, long sgst, long igst, long cess)
    {
        if (cgst <= 0 && sgst <= 0 && igst <= 0 && cess <= 0) return;

        var order = Enumerable.Range(0, candidates.Count)
            .Where(i => candidates[i].Reason == reason && candidates[i].VoucherId is not null)
            .OrderBy(i => preferVoucherId is { } p && candidates[i].VoucherId == p ? 0 : 1)
            .ToList();

        foreach (var i in order)
        {
            if (cgst <= 0 && sgst <= 0 && igst <= 0 && cess <= 0) return;
            var c = candidates[i];
            var takeC = Math.Min(Math.Max(cgst, 0), c.CgstPaisa);
            var takeS = Math.Min(Math.Max(sgst, 0), c.SgstPaisa);
            var takeI = Math.Min(Math.Max(igst, 0), c.IgstPaisa);
            var takeCess = Math.Min(Math.Max(cess, 0), c.CessPaisa);
            if (takeC == 0 && takeS == 0 && takeI == 0 && takeCess == 0) continue;

            cgst -= takeC; sgst -= takeS; igst -= takeI; cess -= takeCess;
            var newC = c.CgstPaisa - takeC;
            var newS = c.SgstPaisa - takeS;
            var newI = c.IgstPaisa - takeI;
            candidates[i] = c with
            {
                CgstPaisa = newC,
                SgstPaisa = newS,
                IgstPaisa = newI,
                CessPaisa = c.CessPaisa - takeCess,
                // SuggestedReversal is DEFINED as Cgst+Sgst+Igst (÷100, cess ring-fenced out, ER-2) — recomputed
                // rather than adjusted, so the record cannot drift out of agreement with its own per-head profile,
                // which is what PostFromCandidate posts from.
                SuggestedReversal = new Money((newC + newS + newI) / 100m),
            };
        }
    }

    /// <summary>
    /// Splits one head's RETURNED eligible tax between the claimable and not-in-portal pools, paisa-exact and
    /// pro rata to the pools as they stand (the shared largest-remainder engine, so the two parts sum EXACTLY to the
    /// returned amount and the identity cannot leak a paisa). When neither pool can absorb it — both non-positive,
    /// i.e. the period's returns exceed its credit — the whole reduction stays on the not-in-portal side: that is
    /// where a credit with no 2B line belongs, and it is the side that may legitimately read negative, which this
    /// build reports rather than floors (the disclosed negative-received decision).
    /// </summary>
    private static (decimal FromClaimable, decimal FromNotInPortal) RouteReturnedHead(
        decimal returned, decimal claimablePool, decimal notInPortalPool)
    {
        if (returned <= 0m) return (0m, 0m);
        if (claimablePool <= 0m && notInPortalPool <= 0m) return (0m, returned);
        var shares = AdditionalCostApportionment.Allocate(
            new[] { claimablePool, notInPortalPool }, new Money(returned));
        return (shares[0].Amount, shares[1].Amount);
    }

    /// <summary>True iff the purchase's supplier (party ledger) carries a GSTIN — a no-GSTIN purchase can never appear in
    /// 2B, so its eligible ITC is §16(2)(aa)-ineligible this period.</summary>
    private static bool HasSupplierGstin(Company company, Voucher voucher) =>
        voucher.PartyId is Guid pid && !string.IsNullOrWhiteSpace(company.FindLedger(pid)?.PartyGst?.Gstin);

    private static bool IsCreditDebitNote(Gstr2bDocType docType) => docType is
        Gstr2bDocType.CreditNote or Gstr2bDocType.DebitNote or
        Gstr2bDocType.CreditNoteAmendment or Gstr2bDocType.DebitNoteAmendment;

    private static decimal PaisaToRupees(long paisa) => paisa / 100m;

    /// <summary>A paisa-exact rupee Money → integer paisa (the split shares are already paisa-exact, so this never rounds
    /// off a real fraction — it only crosses the rupees↔paisa boundary for the candidate's per-head fields).
    /// Delegates to <see cref="PaisaConversion.ToPaisaRounded(Money)"/> — the ONE rupees→paisa rule (drift lock D3),
    /// ROUNDED semantics: a derived report quantises a sub-paisa intermediate, it does not abort.</summary>
    private static long ToPaisa(Money money) => PaisaConversion.ToPaisaRounded(money);
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
/// reversal amount AND its exact <b>per-head</b> breakdown. The ITC-gate NEVER posts a reversal (ER-14); S7 is the sole
/// poster — and it reverses <b>head-for-head</b> from the per-head fields (never a weight-based re-split of the total,
/// which bled a cess-excluded pool into the cess head — Phase 9 S7b FIX 1).</summary>
/// <param name="Reason">Why the candidate was surfaced.</param>
/// <param name="VoucherId">The affected books purchase voucher, or <c>null</c> (for a 2B-line-keyed candidate).</param>
/// <param name="LineId">The affected 2B line, or <c>null</c> (for a voucher-keyed candidate).</param>
/// <param name="SuggestedReversal">The suggested reversal amount (forward CGST+SGST+IGST; cess ring-fenced out, ER-2);
/// S7 decides the actual figure. Equals <see cref="CgstPaisa"/>+<see cref="SgstPaisa"/>+<see cref="IgstPaisa"/> (÷100).</param>
/// <param name="Description">A human-readable reason for the reversal.</param>
/// <param name="CgstPaisa">The exact CGST reversal for this candidate, in paisa (the blocked / ineligible / accepted-CN share).</param>
/// <param name="SgstPaisa">The exact SGST/UTGST reversal for this candidate, in paisa.</param>
/// <param name="IgstPaisa">The exact IGST reversal for this candidate, in paisa.</param>
/// <param name="CessPaisa">The exact <b>ring-fenced</b> Compensation-Cess reversal for this candidate, in paisa (a blocked /
/// ineligible item's cess ITC is blocked too; for an accepted credit note, the cess proportional to the accepted reversal).</param>
public readonly record struct ItcReversalCandidate(
    ItcReversalReason Reason, Guid? VoucherId, Guid? LineId, Money SuggestedReversal, string Description,
    long CgstPaisa = 0, long SgstPaisa = 0, long IgstPaisa = 0, long CessPaisa = 0);
