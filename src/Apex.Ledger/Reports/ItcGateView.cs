using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>Per-head ITC totals (Phase 9 slice 6b), integer paisa. Cess is ring-fenced (ER-2) but carried alongside so the
/// gate view foots to the posted lines.</summary>
/// <param name="CgstPaisa">CGST input tax, paisa.</param>
/// <param name="SgstPaisa">SGST/UTGST input tax, paisa.</param>
/// <param name="IgstPaisa">IGST input tax, paisa.</param>
/// <param name="CessPaisa">Compensation Cess input tax, paisa.</param>
public readonly record struct ItcHeadTotals(long CgstPaisa, long SgstPaisa, long IgstPaisa, long CessPaisa)
{
    /// <summary>All-zero totals.</summary>
    public static readonly ItcHeadTotals Zero = new(0, 0, 0, 0);

    /// <summary>Σ across heads (incl. cess), paisa.</summary>
    public long TotalPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;

    /// <summary>Head-wise sum of two totals.</summary>
    public ItcHeadTotals Add(ItcHeadTotals o) =>
        new(CgstPaisa + o.CgstPaisa, SgstPaisa + o.SgstPaisa, IgstPaisa + o.IgstPaisa, CessPaisa + o.CessPaisa);
}

/// <summary>The reason a purchase's ITC is a reversal / non-claim candidate this period (Phase 9 slice 6b; RQ-15).</summary>
public enum ItcReversalReason
{
    /// <summary>§16(2)(aa): the invoice is not in the taxpayer's GSTR-2B (the supplier has not furnished it) ⇒ ITC is not
    /// available this period. Re-availed automatically once the invoice appears in a later 2B.</summary>
    Section16_2aaNotInPortal = 0,

    /// <summary>§17(5): the credit is blocked (motor vehicles / food-beverages / works-contract / …) ⇒ GSTR-3B Table
    /// 4(B)(1).</summary>
    Section17_5Blocked = 1,
}

/// <summary>One surfaced reversal / non-claim candidate for the S7 poster (Phase 9 slice 6b; RQ-15; RQ-27). ADVISORY only —
/// carries the identity of the affected purchase + the suggested amount; this view never posts the reversal (ER-14).</summary>
/// <param name="VoucherId">The affected posted purchase voucher (a read-only pointer, ER-14).</param>
/// <param name="Reason">Why the ITC is a reversal / non-claim candidate.</param>
/// <param name="BlockedCategory">The §17(5) sub-clause (only for <see cref="ItcReversalReason.Section17_5Blocked"/>).</param>
/// <param name="Amount">The suggested reversal amount, per head, paisa.</param>
public readonly record struct ItcReversalCandidate(
    Guid VoucherId, ItcReversalReason Reason, BlockedCreditCategory BlockedCategory, ItcHeadTotals Amount);

/// <summary>
/// The <b>ITC-gate advisory view</b> (Phase 9 slice 6b; RQ-15; DP-15; DP-31) — a pure projection (like <see cref="Gstr3b"/>)
/// that surfaces, per period, the taxpayer's booked input tax split three ways and reconciled against the portal 2B and the
/// GSTR-3B claim. It <b>posts nothing</b> (ER-14): there is no <c>LedgerService</c>, no journal, no <c>EntryLine</c> — it
/// only classifies and surfaces <see cref="ReversalCandidates"/> for the S7 set-off/stat-adjustment engine (the sole poster,
/// RQ-27).
/// <para>
/// The books-side input tax on posted forward-charge purchases (RCM + composition excluded, mirroring
/// <see cref="Gstr2bReconciler"/>) is partitioned by §17(5) then §16(2)(aa):
/// <list type="bullet">
///   <item><see cref="EligibleItc"/> — §17(5)-eligible AND the invoice is in the 2B (matched) ⇒ claimable (Table 4(A) net).</item>
///   <item><see cref="Section16_2aaIneligible"/> — §17(5)-eligible but the invoice is NOT in the 2B ⇒ ineligible this period
///     (§16(2)(aa)); a reversal candidate for S7.</item>
///   <item><see cref="Section17_5Blocked"/> — blocked under §17(5) ⇒ GSTR-3B Table 4(B)(1) reversal; a candidate for S7.</item>
///   <item><see cref="IneligibleOther"/> — otherwise ineligible ⇒ GSTR-3B Table 4(D) (disclosed, not reversed).</item>
/// </list>
/// It also carries <see cref="Portal2bItc"/> (the §16(2)(aa) ceiling — the portal's ITC-available total) and
/// <see cref="ClaimedItc"/> (the existing <see cref="Gstr3b"/> ITC, display-only) so the three columns sit side by side.
/// </para>
/// </summary>
public sealed record ItcGateView(
    DateOnly From,
    DateOnly To,
    ItcHeadTotals BooksItc,
    ItcHeadTotals EligibleItc,
    ItcHeadTotals Section16_2aaIneligible,
    ItcHeadTotals Section17_5Blocked,
    ItcHeadTotals IneligibleOther,
    ItcHeadTotals Portal2bItc,
    ItcHeadTotals ClaimedItc,
    IReadOnlyList<ItcReversalCandidate> ReversalCandidates)
{
    /// <summary>
    /// Builds the ITC-gate view over <c>[from, to]</c>. The <paramref name="snapshot"/> is the matched GSTR-2B (the
    /// §16(2)(aa) basis, DP-13); when <c>null</c>, no invoice is "in the 2B", so every §17(5)-eligible purchase falls to
    /// <see cref="Section16_2aaIneligible"/> (nothing claimable this period). Pure + advisory (ER-14) — posts nothing.
    /// </summary>
    public static ItcGateView Build(
        Company company, Gstr2bSnapshot? snapshot, DateOnly from, DateOnly to, ReconTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(company);

        var g3b = Gstr3b.Build(company, from, to);
        var claimed = new ItcHeadTotals(
            ToPaisa(g3b.ItcCgst.Amount), ToPaisa(g3b.ItcSgst.Amount), ToPaisa(g3b.ItcIgst.Amount), 0);

        // The portal 2B ITC-available ceiling (§16(2)(aa)): the forward-charge lines the portal reports ITC-available on.
        var portal = ItcHeadTotals.Zero;
        if (snapshot is not null)
            foreach (var l in snapshot.Lines)
                if (l.ItcAvailable && !l.ReverseCharge)
                    portal = portal.Add(new ItcHeadTotals(l.CgstPaisa, l.SgstPaisa, l.IgstPaisa, l.CessPaisa));

        // A composition dealer takes no ITC — an all-zero books view (mirrors Gstr2bReconciler / Gstr3b). Portal/claimed
        // stay as read (both zero for a composition company).
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new ItcGateView(from, to, ItcHeadTotals.Zero, ItcHeadTotals.Zero, ItcHeadTotals.Zero,
                ItcHeadTotals.Zero, ItcHeadTotals.Zero, portal, claimed, Array.Empty<ItcReversalCandidate>());

        // Which booked purchases are "in the 2B" (matched or partially matched) — the §16(2)(aa) gate. Reuse the
        // reconciler so the gate never drifts from the reconciliation buckets.
        var inPortal = new HashSet<Guid>();
        if (snapshot is not null)
        {
            var recon = Gstr2bReconciler.Reconcile(company, snapshot, from, to, tolerance);
            foreach (var m in recon.Matched) inPortal.Add(m.MatchedVoucherId);
            foreach (var m in recon.PartialMismatches) inPortal.Add(m.MatchedVoucherId);
        }

        var books = ItcHeadTotals.Zero;
        var eligible = ItcHeadTotals.Zero;
        var notInPortal = ItcHeadTotals.Zero;
        var blocked = ItcHeadTotals.Zero;
        var ineligibleOther = ItcHeadTotals.Zero;
        var candidates = new List<ItcReversalCandidate>();

        foreach (var (voucher, _) in GstReportSupport.PostedGstVouchers(company, from, to, GstTaxDirection.Input))
        {
            // Only regular forward-charge purchases carry claimable ITC on their tax lines; a purely reverse-charge
            // inward supply bypasses 2B/IMS (its ITC is bucketed by Gstr3b's RCM path), so it is excluded here.
            if (!GstReportSupport.HasForwardTaxLines(voucher)) continue;

            var head = ForwardHeadTotals(voucher);
            books = books.Add(head);

            var (eligibility, category) = ItcEligibilityOf(company, voucher);
            switch (eligibility)
            {
                case ItcEligibility.BlockedSection17_5:
                    blocked = blocked.Add(head);
                    candidates.Add(new ItcReversalCandidate(
                        voucher.Id, ItcReversalReason.Section17_5Blocked, category, head));
                    break;
                case ItcEligibility.Ineligible:
                    ineligibleOther = ineligibleOther.Add(head);
                    break;
                default: // Eligible — §16(2)(aa) then decides claimable vs ineligible-this-period.
                    if (inPortal.Contains(voucher.Id))
                        eligible = eligible.Add(head);
                    else
                    {
                        notInPortal = notInPortal.Add(head);
                        candidates.Add(new ItcReversalCandidate(
                            voucher.Id, ItcReversalReason.Section16_2aaNotInPortal, BlockedCreditCategory.None, head));
                    }
                    break;
            }
        }

        candidates.Sort((a, b) => a.VoucherId.CompareTo(b.VoucherId)); // deterministic surface for S7
        return new ItcGateView(from, to, books, eligible, notInPortal, blocked, ineligibleOther, portal, claimed, candidates);
    }

    /// <summary>The forward (non-RCM) per-head input tax posted on a voucher, paisa. Mirrors the head exclusions of
    /// <see cref="GstReportSupport.PostedForwardTaxTotal"/> (RCM lines are their own buckets), plus the ring-fenced cess.</summary>
    private static ItcHeadTotals ForwardHeadTotals(Voucher voucher)
    {
        long cgst = 0, sgst = 0, igst = 0, cess = 0;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            var amt = ToPaisa(line.Amount.Amount);
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: cgst += amt; break;
                case GstTaxHead.State: sgst += amt; break;
                case GstTaxHead.Integrated: igst += amt; break;
                case GstTaxHead.Cess: cess += amt; break;
            }
        }
        return new ItcHeadTotals(cgst, sgst, igst, cess);
    }

    /// <summary>
    /// The §17(5) ITC-eligibility determination for a purchase — driven by the item/expense master (RQ-26; DP-31). An
    /// item-invoice purchase reads its stock items' GST blocks; an as-voucher purchase reads its debit expense/purchase
    /// ledgers' GST blocks (the tax + party legs carry no <c>SalesPurchaseGst</c> and are skipped). The <b>first</b>
    /// non-eligible determination wins (a purchase with any blocked/ineligible line is conservatively flagged); a purchase
    /// with no §17(5) tagging is <see cref="ItcEligibility.Eligible"/> (byte-identical when off, ER-13).
    /// </summary>
    private static (ItcEligibility Eligibility, BlockedCreditCategory Category) ItcEligibilityOf(
        Company company, Voucher voucher)
    {
        foreach (var il in voucher.InventoryLines)
            if (company.FindStockItem(il.StockItemId)?.Gst is { } g && g.ItcEligibility != ItcEligibility.Eligible)
                return (g.ItcEligibility, g.BlockedCreditCategory);

        foreach (var line in voucher.Lines)
        {
            if (line.Side != DrCr.Debit) continue;
            if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { } spg
                && spg.ItcEligibility != ItcEligibility.Eligible)
                return (spg.ItcEligibility, spg.BlockedCreditCategory);
        }
        return (ItcEligibility.Eligible, BlockedCreditCategory.None);
    }

    private static long ToPaisa(decimal rupees) => (long)Math.Round(rupees * 100m, MidpointRounding.AwayFromZero);
}
