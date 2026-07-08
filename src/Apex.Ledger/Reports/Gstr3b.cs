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

        return new Gstr3b(from, to,
            new Money(taxable), new Money(exempt),
            new Money(outCgst), new Money(outSgst), new Money(outIgst),
            new Money(itcCgst), new Money(itcSgst), new Money(itcIgst));
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
            exempt += v.InventoryLinesValue.Amount;
        }
        return exempt;
    }
}
