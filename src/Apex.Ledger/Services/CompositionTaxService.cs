using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The composition <b>tax-on-turnover</b> engine (Phase 9 slice 3; RQ-10/RQ-16; ER-9/ER-10). Framework-, DB-, clock-
/// and RNG-free: a pure, deterministic, paisa-exact projection over the posted outward-sale vouchers, modelled on
/// <see cref="Reports.Gstr1"/> (a read over posted vouchers) + the <see cref="GstService"/> compute-total-then-split.
/// <para>
/// A composition dealer posts <b>no tax lines</b> (it issues a Bill of Supply), so the turnover is read from the posted
/// stock/sales <b>value</b> (<see cref="GstReportSupport.OutwardSupplyValue"/>) — never recomputed from tax lines. The
/// turnover base depends on the sub-type (<see cref="CompositionThreshold.TaxesTotalTurnover"/>): Manufacturer/
/// Restaurant tax the TOTAL turnover in state (incl. exempt), Trader/§10(2A) service provider tax TAXABLE supplies
/// only. The flat composition tax = round(base × rate) split CGST+SGST once (paisa-exact: CGST + SGST == total). The
/// dealer <b>also</b> pays inward reverse-charge tax in cash (read off the posted RCM output-liability lines), which
/// CMP-08 Table 3(ii) requires; Cess is kept ring-fenced (ER-2).
/// </para>
/// </summary>
public sealed class CompositionTaxService
{
    private readonly Company _company;

    public CompositionTaxService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Computes the composition tax-on-turnover + inward-RCM liability over <c>[from, to]</c> — one quarter for CMP-08
    /// or a full FY for GSTR-4. Outward turnover is <b>net of sales returns</b>: a <see cref="VoucherBaseType.Sales"/>
    /// Bill of Supply adds, a <see cref="VoucherBaseType.CreditNote"/> sale-return nets down (sign by base type;
    /// cancelled / post-dated-after-<paramref name="to"/> are excluded via <see cref="LedgerBalances.CountsAsOf"/>).
    /// Throws when the company is not a composition dealer (the caller — the returns — guards before invoking).
    /// </summary>
    public CompositionTax ComputeForPeriod(DateOnly from, DateOnly to)
    {
        var gst = _company.Gst
            ?? throw new InvalidOperationException("GST is not enabled — composition tax is not applicable.");
        if (gst.RegistrationType != GstRegistrationType.Composition)
            throw new InvalidOperationException("Composition tax is only applicable to a Composition dealer.");
        var subType = gst.CompositionSubType
            ?? throw new InvalidOperationException("The Composition sub-type is not set — cannot resolve the tax-on-turnover rate/base.");

        var total = 0m; var taxable = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(_company, from, to, GstTaxDirection.Output))
        {
            // A composition dealer's turnover is NET of sales returns: a Bill-of-Supply sale-return Credit Note reduces
            // the turnover base (sign by base type — Sales +, Credit Note − — mirroring Gstr1.ComputeRcm4BOutwardValue).
            // Only Sales / Credit-Note reach here (DirectionOf(Output) gates every other base type out).
            var sign = type.BaseType == VoucherBaseType.CreditNote ? -1m : 1m;
            var (t, tx) = GstReportSupport.OutwardSupplyValue(_company, voucher, type.BaseType);
            total += sign * t.Amount;
            taxable += sign * tx.Amount;
        }

        // Floor the base at zero: in a pathological window where returns exceed sales the netted turnover could go
        // negative; a composition tax-on-turnover base is never negative (no negative liability).
        var totalTurnover = new Money(Math.Max(0m, total));
        var taxableTurnover = new Money(Math.Max(0m, taxable));
        var baseValue = CompositionThreshold.TaxesTotalTurnover(subType) ? totalTurnover : taxableTurnover;

        // Compute-total-then-split (paisa-exact): CGST = round(total/2), SGST = total − CGST ⇒ CGST + SGST == total. A
        // composition dealer's outward supply is always intra-state (§10 bars inter-state outward), so C+S only.
        var rateBp = CompositionThreshold.RateBasisPoints(subType);
        var compTotal = GstService.TaxAmount(baseValue, rateBp);
        var cgst = new Money(compTotal.Amount / 2m).RoundToPaisa();
        var sgst = new Money(compTotal.Amount - cgst.Amount);

        // Inward RCM the dealer also pays in CASH (→ CMP-08 Table 3(ii)) — the posted RCM output-liability lines. Cess
        // ring-fenced (ER-2). Read, never recomputed.
        var rc = 0m; var rs = 0m; var ri = 0m; var rcess = 0m;
        foreach (var l in GstReportSupport.RcmLines(_company, from, to))
        {
            if (!l.IsOutputLiability) continue;
            switch (l.Gst.TaxHead)
            {
                case GstTaxHead.Central: rc += l.Amount.Amount; break;
                case GstTaxHead.State: rs += l.Amount.Amount; break;
                case GstTaxHead.Integrated: ri += l.Amount.Amount; break;
                case GstTaxHead.Cess: rcess += l.Amount.Amount; break;
            }
        }

        return new CompositionTax(
            from, to, subType, totalTurnover, taxableTurnover, baseValue, rateBp, cgst, sgst,
            new Money(rc), new Money(rs), new Money(ri), new Money(rcess));
    }
}

/// <summary>
/// The composition tax-on-turnover result for a period (Phase 9 slice 3; RQ-16). The composition tax is CGST+SGST on
/// the turnover base; the RCM-inward figures are the dealer's cash reverse-charge liability the same period. Cess is a
/// separate field, never folded into the GST payable total (ring-fence, ER-2).
/// </summary>
public sealed record CompositionTax(
    DateOnly From,
    DateOnly To,
    CompositionSubType SubType,
    Money TotalTurnover,
    Money TaxableTurnover,
    Money TurnoverBase,
    int RateBasisPoints,
    Money Cgst,
    Money Sgst,
    Money RcmInwardCgst,
    Money RcmInwardSgst,
    Money RcmInwardIgst,
    Money RcmInwardCess)
{
    /// <summary>The flat composition tax on the turnover base (CGST + SGST).</summary>
    public Money CompositionTaxAmount => new(Cgst.Amount + Sgst.Amount);

    /// <summary>Σ the inward reverse-charge GST heads the dealer pays in cash (excludes cess, ER-2).</summary>
    public Money RcmInwardTax => new(RcmInwardCgst.Amount + RcmInwardSgst.Amount + RcmInwardIgst.Amount);

    /// <summary>Total GST payable = composition tax-on-turnover + inward-RCM tax (cess ring-fenced separately, ER-2).</summary>
    public Money TotalPayable => new(CompositionTaxAmount.Amount + RcmInwardTax.Amount);
}
