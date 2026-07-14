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
        var (outCgst, outSgst, outIgst, taxable, exempt) = ReadSide(company, from, to, GstTaxDirection.Output);
        var (itcCgst, itcSgst, itcIgst, _, _) = ReadSide(company, from, to, GstTaxDirection.Input);

        var rcm = ReadRcm(company, from, to);

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
        };
    }

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
            exempt += v.InventoryLinesValue.Amount;
        }
        return exempt;
    }
}
