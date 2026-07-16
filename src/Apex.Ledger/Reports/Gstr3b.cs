using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>GSTR-3B</b> (summary return) — a pure, read-only projection over the posted GST vouchers in
/// <c>[from, to]</c> (phase4-gst-requirements RQ-22; catalog §12; DP-9; ER-7). Every figure is read off the
/// posted tax <see cref="EntryLine"/>s' <see cref="GstLineTax"/> — never recomputed — so:
/// <list type="bullet">
///   <item><b>§3.1 outward</b> tax by head (<see cref="OutwardCgst"/>/<see cref="OutwardSgst"/>/
///     <see cref="OutwardIgst"/>) reconciles to the Output tax-ledger postings for the period;</item>
///   <item><b>§4 eligible ITC</b> by head (<see cref="ItcCgst"/>/<see cref="ItcSgst"/>/<see cref="ItcIgst"/>)
///     reconciles to the Input tax-ledger postings;</item>
///   <item><b>net tax payable</b> by head (<see cref="NetCgst"/>/<see cref="NetSgst"/>/<see cref="NetIgst"/>)
///     = outward − ITC. This is a <b>display-only computation</b> (DP-9): a negative head is a carried-forward
///     credit, not a payable. The <b>posting</b> of the Rule-88A set-off / cash-ledger payment is Phase 9 —
///     GSTR-3B here only shows the indicative arithmetic a core-GST user needs to read a 3B.</item>
/// </list>
/// Cancelled and post-dated-after-<c>to</c> vouchers are excluded; exempt/nil/non-GST outward value is shown
/// separately. A non-GST company yields an all-zero return. No UI, no DB.
/// </summary>
public sealed record Gstr3b(
    DateOnly From,
    DateOnly To,
    Money TaxableOutwardValue,
    Money ExemptNilNonGstOutward,
    Money OutwardCgst,
    Money OutwardSgst,
    Money OutwardIgst,
    Money ItcCgst,
    Money ItcSgst,
    Money ItcIgst)
{
    // ---- Phase 9 slice 2: reverse-charge (RCM) buckets (RQ-7). Default Money.Zero so a company with no RCM line is
    // byte-identical to a pre-S2 3B (ER-13). Set by Build; positional constructions (tests) keep them zero. ----

    /// <summary>Table 3.1(d) — inward supplies liable to reverse charge, CGST (the RCM output liability).</summary>
    public Money RcmOutwardCgst { get; init; }
    /// <summary>Table 3.1(d) — inward supplies liable to reverse charge, SGST/UTGST.</summary>
    public Money RcmOutwardSgst { get; init; }
    /// <summary>Table 3.1(d) — inward supplies liable to reverse charge, IGST.</summary>
    public Money RcmOutwardIgst { get; init; }
    /// <summary>Table 3.1(d) — inward supplies liable to reverse charge, Compensation Cess (ring-fenced, ER-2).</summary>
    public Money RcmOutwardCess { get; init; }

    /// <summary>Table 4(A)(2) — ITC on import of services (IGST only).</summary>
    public Money RcmItcImportIgst { get; init; }

    /// <summary>Table 4(A)(3) — ITC on other inward supplies liable to reverse charge, CGST.</summary>
    public Money RcmItcOtherCgst { get; init; }
    /// <summary>Table 4(A)(3) — ITC on other reverse-charge inward supplies, SGST/UTGST.</summary>
    public Money RcmItcOtherSgst { get; init; }
    /// <summary>Table 4(A)(3) — ITC on other reverse-charge inward supplies, IGST.</summary>
    public Money RcmItcOtherIgst { get; init; }
    /// <summary>Table 4(A)(3) — ITC on other reverse-charge inward supplies, Compensation Cess (ring-fenced, ER-2).</summary>
    public Money RcmItcOtherCess { get; init; }

    // ---- Phase 9 slice 7b: ITC-reversal projection (RQ-27; A14-CONFIRMED §11.5). Additive init-only fields defaulting
    // to Money.Zero so a company that never reverses is byte-identical to a pre-S7b 3B (ER-13). Computed by Build ONLY
    // from the posted stat-adjustment vouchers' reversal/reclaim-tagged lines (by GstAdjustmentKind) — the set-off /
    // reversal vouchers are Journal base ⇒ excluded from PostedGstVouchers, so these additions cannot move the 3.1
    // outward or 4(A) ITC sums above (§0 fact 2). Table 4(B)(1) = non-reclaimable (Rules 38/42/43 + §17(5) + credit
    // notes); 4(B)(2) = reclaimable (Rule 37/37A); 4(D)(1) = a reclaim of an earlier reversal. ----

    /// <summary>Table 4(B)(1) — non-reclaimable ITC reversal (Rules 38/42/43, §17(5), credit notes), CGST.</summary>
    public Money ItcReversed4B1Cgst { get; init; }
    /// <summary>Table 4(B)(1) — non-reclaimable ITC reversal, SGST/UTGST.</summary>
    public Money ItcReversed4B1Sgst { get; init; }
    /// <summary>Table 4(B)(1) — non-reclaimable ITC reversal, IGST.</summary>
    public Money ItcReversed4B1Igst { get; init; }
    /// <summary>Table 4(B)(1) — non-reclaimable ITC reversal, Compensation Cess (ring-fenced, ER-2).</summary>
    public Money ItcReversed4B1Cess { get; init; }

    /// <summary>Table 4(B)(2) — reclaimable ITC reversal (Rule 37 / 37A), CGST.</summary>
    public Money ItcReversed4B2Cgst { get; init; }
    /// <summary>Table 4(B)(2) — reclaimable ITC reversal (Rule 37 / 37A), SGST/UTGST.</summary>
    public Money ItcReversed4B2Sgst { get; init; }
    /// <summary>Table 4(B)(2) — reclaimable ITC reversal (Rule 37 / 37A), IGST.</summary>
    public Money ItcReversed4B2Igst { get; init; }
    /// <summary>Table 4(B)(2) — reclaimable ITC reversal (Rule 37 / 37A), Compensation Cess (ring-fenced, ER-2).</summary>
    public Money ItcReversed4B2Cess { get; init; }

    /// <summary>Table 4(D)(1) — reclaim of an earlier reversal (pulled back into net ITC via 4(A)(5)), CGST.</summary>
    public Money ItcReclaimed4D1Cgst { get; init; }
    /// <summary>Table 4(D)(1) — reclaim of an earlier reversal, SGST/UTGST.</summary>
    public Money ItcReclaimed4D1Sgst { get; init; }
    /// <summary>Table 4(D)(1) — reclaim of an earlier reversal, IGST.</summary>
    public Money ItcReclaimed4D1Igst { get; init; }
    /// <summary>Table 4(D)(1) — reclaim of an earlier reversal, Compensation Cess (ring-fenced, ER-2).</summary>
    public Money ItcReclaimed4D1Cess { get; init; }

    /// <summary>Σ Table 4(B) ITC reversed across the GST heads (4(B)(1) + 4(B)(2); excludes cess, ER-2).</summary>
    public Money TotalItcReversed => new(
        ItcReversed4B1Cgst.Amount + ItcReversed4B1Sgst.Amount + ItcReversed4B1Igst.Amount +
        ItcReversed4B2Cgst.Amount + ItcReversed4B2Sgst.Amount + ItcReversed4B2Igst.Amount);

    /// <summary>Σ Table 4(D)(1) ITC reclaimed across the GST heads (excludes cess, ER-2).</summary>
    public Money TotalItcReclaimed =>
        new(ItcReclaimed4D1Cgst.Amount + ItcReclaimed4D1Sgst.Amount + ItcReclaimed4D1Igst.Amount);

    /// <summary>Net CGST payable = outward − ITC (display-only, DP-9; negative ⇒ carried-forward credit).</summary>
    public Money NetCgst => new(OutwardCgst.Amount - ItcCgst.Amount);

    /// <summary>Net SGST/UTGST payable = outward − ITC (display-only, DP-9).</summary>
    public Money NetSgst => new(OutwardSgst.Amount - ItcSgst.Amount);

    /// <summary>Net IGST payable = outward − ITC (display-only, DP-9).</summary>
    public Money NetIgst => new(OutwardIgst.Amount - ItcIgst.Amount);

    /// <summary>Σ outward tax across heads.</summary>
    public Money TotalOutwardTax => new(OutwardCgst.Amount + OutwardSgst.Amount + OutwardIgst.Amount);

    /// <summary>Σ eligible ITC across heads.</summary>
    public Money TotalItc => new(ItcCgst.Amount + ItcSgst.Amount + ItcIgst.Amount);

    /// <summary>Σ Table 3.1(d) reverse-charge outward liability across the GST heads (excludes cess, ER-2).</summary>
    public Money TotalRcmOutward => new(RcmOutwardCgst.Amount + RcmOutwardSgst.Amount + RcmOutwardIgst.Amount);

    /// <summary>Σ Table 4(A)(2)+4(A)(3) reverse-charge ITC across the GST heads (excludes cess, ER-2).</summary>
    public Money TotalRcmItc => new(
        RcmItcImportIgst.Amount + RcmItcOtherCgst.Amount + RcmItcOtherSgst.Amount + RcmItcOtherIgst.Amount);

    /// <summary>
    /// Σ net tax payable across heads (display-only). Negative-head credits are netted in, mirroring the
    /// indicative arithmetic; this is NOT a Rule-88A set-off (Phase 9).
    /// </summary>
    public Money TotalNetPayable => new(NetCgst.Amount + NetSgst.Amount + NetIgst.Amount);

    /// <summary>Builds GSTR-3B for the whole company over <c>[from, to]</c>.</summary>
    public static Gstr3b Build(Company company, DateOnly from, DateOnly to)
    {
        // Phase 9 slice 3 (RQ-16): a Composition dealer files CMP-08 / GSTR-4, NOT GSTR-3B — early-return an empty
        // summary. The dealer's inward RCM is reported in CMP-08 Table 3(ii) (not 3.1(d) here), so no data is lost. A
        // Regular company never enters this branch ⇒ byte-identical (ER-13).
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new Gstr3b(from, to, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
                Money.Zero, Money.Zero, Money.Zero);

        var (outCgst, outSgst, outIgst, taxable, exempt) = ReadSide(company, from, to, GstTaxDirection.Output);
        var (itcCgst, itcSgst, itcIgst, _, _) = ReadSide(company, from, to, GstTaxDirection.Input);

        var rcm = ReadRcm(company, from, to);

        // Phase 9 slice 2b: fold the §34 CDN net (signed by note type) into 3.1(a) outward — a credit note reduces the
        // outward tax + taxable value, a debit note increases them. CDN-linked vouchers are excluded from ReadSide (both
        // directions) so they are counted here once, signed (risk #4). No CDN ⇒ zero delta (byte-identical, ER-13).
        var cdn = ReadCdn(company, from, to);
        outCgst += cdn.Cgst; outSgst += cdn.Sgst; outIgst += cdn.Igst; taxable += cdn.Taxable;

        // Phase 9 slice 7b: Table 4(B)/4(D) ITC-reversal projection — Σ the posted stat-adjustment reversal/reclaim
        // lines by their GstAdjustmentKind tag (routed to 4(B)(1) / 4(B)(2) / 4(D)(1)). Zero when no reversal was
        // posted ⇒ byte-identical (ER-13). These vouchers are Journal base ⇒ already out of the 3.1/4(A) sums.
        var rev = ReadReversals(company, from, to);

        return new Gstr3b(from, to,
            new Money(taxable), new Money(exempt),
            new Money(outCgst), new Money(outSgst), new Money(outIgst),
            new Money(itcCgst), new Money(itcSgst), new Money(itcIgst))
        {
            RcmOutwardCgst = new Money(rcm.OutCgst), RcmOutwardSgst = new Money(rcm.OutSgst),
            RcmOutwardIgst = new Money(rcm.OutIgst), RcmOutwardCess = new Money(rcm.OutCess),
            RcmItcImportIgst = new Money(rcm.ImportIgst),
            RcmItcOtherCgst = new Money(rcm.OtherCgst), RcmItcOtherSgst = new Money(rcm.OtherSgst),
            RcmItcOtherIgst = new Money(rcm.OtherIgst), RcmItcOtherCess = new Money(rcm.OtherCess),
            ItcReversed4B1Cgst = new Money(rev.B1Cgst), ItcReversed4B1Sgst = new Money(rev.B1Sgst),
            ItcReversed4B1Igst = new Money(rev.B1Igst), ItcReversed4B1Cess = new Money(rev.B1Cess),
            ItcReversed4B2Cgst = new Money(rev.B2Cgst), ItcReversed4B2Sgst = new Money(rev.B2Sgst),
            ItcReversed4B2Igst = new Money(rev.B2Igst), ItcReversed4B2Cess = new Money(rev.B2Cess),
            ItcReclaimed4D1Cgst = new Money(rev.D1Cgst), ItcReclaimed4D1Sgst = new Money(rev.D1Sgst),
            ItcReclaimed4D1Igst = new Money(rev.D1Igst), ItcReclaimed4D1Cess = new Money(rev.D1Cess),
        };
    }

    /// <summary>
    /// Reads the Table 4(B)/4(D) ITC-reversal buckets (Phase 9 slice 7b; RQ-27): Σ the posted stat-adjustment lines
    /// whose <see cref="GstLineTax.Adjustment"/> is a reversal (Rule 37/37A/42/43/§17(5)/Ineligible/CreditNote) or a
    /// reclaim, routed to 4(B)(1) (non-reclaimable) / 4(B)(2) (reclaimable) / 4(D)(1) (reclaim) by the tag. A pure
    /// projection over the posted adjustment vouchers, never recomputed (ER-9). No reversal posted ⇒ all zero (ER-13).
    /// </summary>
    private static (decimal B1Cgst, decimal B1Sgst, decimal B1Igst, decimal B1Cess,
        decimal B2Cgst, decimal B2Sgst, decimal B2Igst, decimal B2Cess,
        decimal D1Cgst, decimal D1Sgst, decimal D1Igst, decimal D1Cess) ReadReversals(
        Company company, DateOnly from, DateOnly to)
    {
        decimal b1C = 0m, b1S = 0m, b1I = 0m, b1Cess = 0m;
        decimal b2C = 0m, b2S = 0m, b2I = 0m, b2Cess = 0m;
        decimal d1C = 0m, d1S = 0m, d1I = 0m, d1Cess = 0m;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            foreach (var line in v.Lines)
            {
                if (line.Gst is not { Adjustment: { } adj } g) continue;
                var bucket = Table4bBucketOf(adj);
                if (bucket is not { } b) continue; // SetOff / CashPayment are Table 6.1, not 4(B)/4(D)
                var amt = line.Amount.Amount;
                switch (b, g.TaxHead)
                {
                    case (Table4bBucket.Table4B1, GstTaxHead.Central): b1C += amt; break;
                    case (Table4bBucket.Table4B1, GstTaxHead.State): b1S += amt; break;
                    case (Table4bBucket.Table4B1, GstTaxHead.Integrated): b1I += amt; break;
                    case (Table4bBucket.Table4B1, GstTaxHead.Cess): b1Cess += amt; break;
                    case (Table4bBucket.Table4B2, GstTaxHead.Central): b2C += amt; break;
                    case (Table4bBucket.Table4B2, GstTaxHead.State): b2S += amt; break;
                    case (Table4bBucket.Table4B2, GstTaxHead.Integrated): b2I += amt; break;
                    case (Table4bBucket.Table4B2, GstTaxHead.Cess): b2Cess += amt; break;
                    case (Table4bBucket.Table4D1, GstTaxHead.Central): d1C += amt; break;
                    case (Table4bBucket.Table4D1, GstTaxHead.State): d1S += amt; break;
                    case (Table4bBucket.Table4D1, GstTaxHead.Integrated): d1I += amt; break;
                    case (Table4bBucket.Table4D1, GstTaxHead.Cess): d1Cess += amt; break;
                }
            }
        }

        return (b1C, b1S, b1I, b1Cess, b2C, b2S, b2I, b2Cess, d1C, d1S, d1I, d1Cess);
    }

    /// <summary>Maps a posted line's <see cref="GstAdjustmentKind"/> to its GSTR-3B Table-4 reversal bucket, or
    /// <c>null</c> for a set-off / cash-payment tag (Table 6.1, not a 4(B)/4(D) reversal). A14-CONFIRMED §11.5:
    /// Rule 37/37A ⇒ 4(B)(2); Rule 42/43/§17(5)/Ineligible/CreditNote ⇒ 4(B)(1); a reclaim ⇒ 4(D)(1).</summary>
    private static Table4bBucket? Table4bBucketOf(GstAdjustmentKind adj) => adj switch
    {
        GstAdjustmentKind.ReversalRule37 or GstAdjustmentKind.ReversalRule37A => Table4bBucket.Table4B2,
        GstAdjustmentKind.ReversalRule42 or GstAdjustmentKind.ReversalRule43
            or GstAdjustmentKind.ReversalSection17_5 or GstAdjustmentKind.ReversalIneligible
            or GstAdjustmentKind.ReversalCreditNote => Table4bBucket.Table4B1,
        GstAdjustmentKind.Reclaim => Table4bBucket.Table4D1,
        _ => null, // SetOff / CashPayment
    };

    /// <summary>
    /// Reads the reverse-charge buckets (Phase 9 slice 2; RQ-7): 3.1(d) = Σ RCM output-liability lines by head; 4A(2) =
    /// Σ import-of-services RCM ITC (IGST); 4A(3) = Σ other RCM ITC by head. A pure projection over
    /// <see cref="GstReportSupport.RcmLines"/> (reads the posted RCM-tagged lines, never recomputed). These lines are
    /// <b>excluded</b> from the ordinary outward/ITC buckets in <see cref="ReadSide"/> (no double-count, risk #3).
    /// </summary>
    private static (decimal OutCgst, decimal OutSgst, decimal OutIgst, decimal OutCess,
        decimal ImportIgst, decimal OtherCgst, decimal OtherSgst, decimal OtherIgst, decimal OtherCess) ReadRcm(
        Company company, DateOnly from, DateOnly to)
    {
        decimal outCgst = 0m, outSgst = 0m, outIgst = 0m, outCess = 0m;
        decimal importIgst = 0m, otherCgst = 0m, otherSgst = 0m, otherIgst = 0m, otherCess = 0m;

        foreach (var l in GstReportSupport.RcmLines(company, from, to))
        {
            var amt = l.Amount.Amount;
            if (l.IsOutputLiability)
            {
                switch (l.Gst.TaxHead)
                {
                    case GstTaxHead.Central: outCgst += amt; break;
                    case GstTaxHead.State: outSgst += amt; break;
                    case GstTaxHead.Integrated: outIgst += amt; break;
                    case GstTaxHead.Cess: outCess += amt; break;
                }
            }
            else if (l.Scheme == RcmItcScheme.ImportOfServices)
            {
                // Import of services is always IGST → 4A(2).
                if (l.Gst.TaxHead == GstTaxHead.Integrated) importIgst += amt;
                else otherCess += l.Gst.TaxHead == GstTaxHead.Cess ? amt : 0m;
            }
            else // OtherRcm → 4A(3)
            {
                switch (l.Gst.TaxHead)
                {
                    case GstTaxHead.Central: otherCgst += amt; break;
                    case GstTaxHead.State: otherSgst += amt; break;
                    case GstTaxHead.Integrated: otherIgst += amt; break;
                    case GstTaxHead.Cess: otherCess += amt; break;
                }
            }
        }

        return (outCgst, outSgst, outIgst, outCess, importIgst, otherCgst, otherSgst, otherIgst, otherCess);
    }

    /// <summary>
    /// The §34 CDN net (Phase 9 slice 2b; RQ-24) folded into 3.1(a) outward: Σ over the credit/debit notes posted in the
    /// window of their <b>signed</b> Output tax + taxable value (a credit note negative — reduces output; a debit note
    /// positive — increases it). Read off the note's posted Output-tax lines (never recomputed), joined to the
    /// <see cref="GstCreditDebitNoteLink"/> record, decoupled from the base-type→direction map so a §34 debit note (whose
    /// base type maps to Input) still nets the <b>output</b> tax up. A company with no §34 note yields a zero delta (ER-13).
    /// </summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst, decimal Taxable) ReadCdn(
        Company company, DateOnly from, DateOnly to)
    {
        if (company.CreditDebitNoteLinks.Count == 0) return (0m, 0m, 0m, 0m);

        decimal cgst = 0m, sgst = 0m, igst = 0m, taxable = 0m;
        foreach (var link in company.CreditDebitNoteLinks)
        {
            var v = company.FindVoucher(link.CdnVoucherId);
            if (v is null || v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            var sign = link.CdnType == CdnType.Credit ? -1m : 1m;
            foreach (var line in v.Lines)
            {
                if (line.Gst is not { } g || g.IsReverseCharge) continue;
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += sign * line.Amount.Amount; break;
                    case GstTaxHead.State: sgst += sign * line.Amount.Amount; break;
                    case GstTaxHead.Integrated: igst += sign * line.Amount.Amount; break;
                }
            }
            taxable += sign * GstReportSupport.InvoiceTaxableValue(v).Amount;
        }
        return (cgst, sgst, igst, taxable);
    }

    /// <summary>
    /// Reads one side's per-head tax + (for outward) taxable and exempt outward value, off the posted tax
    /// lines. Taxable value is summed once per voucher (the whole-invoice taxable value, not per head, to avoid
    /// double-counting the CGST+SGST legs); an all-exempt/nil outward supply adds its stock value to exempt.
    /// </summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst, decimal Taxable, decimal Exempt) ReadSide(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        var cgst = 0m; var sgst = 0m; var igst = 0m; var taxable = 0m; var exempt = 0m;

        foreach (var (voucher, _) in GstReportSupport.PostedGstVouchers(company, from, to, direction))
        {
            // Phase 9 slice 2b: a formalised §34 credit/debit note is projected — signed — into 3.1(a) by ReadCdn; exclude
            // it from BOTH the ordinary outward and the "all other ITC" sweeps so it is never double-counted (risk #4). A
            // §34 debit note's base type maps to Input, so this exclusion also keeps it out of ITC. No CDN ⇒ no exclusion.
            if (GstReportSupport.CdnLinkFor(company, voucher) is not null) continue;

            var hasTax = false;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g) continue;
                // Phase 9 slice 2: reverse-charge lines are their own 3.1(d)/4A(2)/4A(3) buckets (ReadRcm) — EXCLUDE them
                // from the ordinary outward / "all other ITC" (4A(5)) accumulation so they are never double-counted (risk #3).
                if (g.IsReverseCharge) continue;
                hasTax = true;
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += line.Amount.Amount; break;
                    case GstTaxHead.State: sgst += line.Amount.Amount; break;
                    case GstTaxHead.Integrated: igst += line.Amount.Amount; break;
                }
            }
            if (hasTax)
                taxable += GstReportSupport.InvoiceTaxableValue(voucher).Amount;
        }

        // Exempt/nil/non-GST outward value: outward vouchers with a stock/sales leg but NO tax line. These are
        // filtered out of PostedGstVouchers (which requires a tax line), so scan outward vouchers separately.
        if (direction == GstTaxDirection.Output)
            exempt = ExemptOutwardValue(company, from, to);

        return (cgst, sgst, igst, taxable, exempt);
    }

    /// <summary>
    /// The exempt/nil/non-GST outward value: the stock value of outward (Sales/Credit-Note) vouchers that carry
    /// item lines but no tax line (an exempt supply posts zero tax). Reads posted stock-line values only.
    /// </summary>
    private static decimal ExemptOutwardValue(Company company, DateOnly from, DateOnly to)
    {
        if (!company.GstEnabled) return 0m;
        var exempt = 0m;
        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || GstReportSupport.DirectionOf(type.BaseType) != GstTaxDirection.Output) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;
            if (v.Lines.Any(l => l.HasGst)) continue;   // taxable vouchers already counted
            // An outward reverse-charge supply carries zero tax too, but it belongs only in 3.1(d)-value / GSTR-1 4B —
            // NOT the exempt/nil/non-GST bucket (else it is double-represented). Exclude it (Phase 9 slice 2; RQ-7).
            if (GstReportSupport.IsOutwardReverseChargeSupply(company, v)) continue;
            // A §34 CDN-linked voucher is projected — signed — by its own table (3.1(a) via ReadCdn / GSTR-1 Table 9B),
            // so a zero-tax (exempt) §34 note must NOT also land in the exempt/nil/non-GST bucket, else GSTR-3B over-states
            // exempt outward and diverges from the GSTR-1 main sweep (which already skips CDN-linked vouchers). Finding #6.
            if (GstReportSupport.CdnLinkFor(company, v) is not null) continue;
            exempt += v.InventoryLinesValue.Amount;
        }
        return exempt;
    }
}
