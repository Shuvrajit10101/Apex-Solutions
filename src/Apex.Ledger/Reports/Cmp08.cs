using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Form CMP-08</b> — the composition dealer's quarterly self-assessed statement of tax (Phase 9 slice 3; RQ-16;
/// §10 + Rule 62). A pure, read-only projection over <see cref="CompositionTaxService.ComputeForPeriod"/> for the
/// quarter, laid out to the CMP-08 form:
/// <list type="bullet">
///   <item><b>Table 3(i)</b> — outward supplies (incl. exempt) ⇒ the tax on turnover (CGST+SGST).</item>
///   <item><b>Table 3(ii)</b> — inward supplies attracting reverse charge ⇒ the RCM tax the dealer pays in cash.</item>
///   <item><b>Table 3(iii)</b> — tax payable = (i)+(ii) by head (CGST/SGST/IGST, + ring-fenced Cess, ER-2).</item>
///   <item><b>Table 3(iv)</b> — interest (§50): modelled but left <see cref="Money.Zero"/> in S3 (DP-34 flag-only —
///     the late-payment interest posting is out of scope, a carry-forward).</item>
/// </list>
/// A non-composition company yields a <b>not-applicable</b> (empty) CMP-08. No UI, no DB.
/// </summary>
public sealed record Cmp08(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    CompositionSubType? SubType,
    int RateBasisPoints,
    Money TurnoverBase,
    Money OutwardCgst,
    Money OutwardSgst,
    Money InwardRcmCgst,
    Money InwardRcmSgst,
    Money InwardRcmIgst,
    Money InwardRcmCess,
    Money Interest)
{
    /// <summary>Table 3(i) — the composition tax on turnover (CGST + SGST).</summary>
    public Money OutwardTurnoverTax => new(OutwardCgst.Amount + OutwardSgst.Amount);

    /// <summary>Table 3(ii) — the inward reverse-charge tax the dealer pays in cash (excludes cess, ER-2).</summary>
    public Money InwardRcmTax => new(InwardRcmCgst.Amount + InwardRcmSgst.Amount + InwardRcmIgst.Amount);

    /// <summary>Table 3(iii) CGST payable = outward turnover CGST + inward-RCM CGST.</summary>
    public Money PayableCgst => new(OutwardCgst.Amount + InwardRcmCgst.Amount);

    /// <summary>Table 3(iii) SGST payable = outward turnover SGST + inward-RCM SGST.</summary>
    public Money PayableSgst => new(OutwardSgst.Amount + InwardRcmSgst.Amount);

    /// <summary>Table 3(iii) IGST payable = inward-RCM IGST (a composition dealer collects no outward IGST).</summary>
    public Money PayableIgst => InwardRcmIgst;

    /// <summary>Table 3(iii) Compensation Cess payable = inward-RCM cess (ring-fenced, ER-2).</summary>
    public Money PayableCess => InwardRcmCess;

    /// <summary>Σ tax payable across the GST heads (excludes cess, ER-2).</summary>
    public Money TotalTaxPayable => new(PayableCgst.Amount + PayableSgst.Amount + PayableIgst.Amount);

    /// <summary>Builds CMP-08 for a composition company over the quarter <c>[from, to]</c>; a non-composition company
    /// yields a not-applicable statement.</summary>
    public static Cmp08 Build(Company company, DateOnly from, DateOnly to)
    {
        if (company.Gst?.RegistrationType != GstRegistrationType.Composition)
            return NotApplicable(from, to);

        var t = new CompositionTaxService(company).ComputeForPeriod(from, to);
        return new Cmp08(from, to, true, t.SubType, t.RateBasisPoints, t.TurnoverBase,
            t.Cgst, t.Sgst, t.RcmInwardCgst, t.RcmInwardSgst, t.RcmInwardIgst, t.RcmInwardCess, Money.Zero);
    }

    /// <summary>A not-applicable (empty) CMP-08 for a non-composition company (all zero, no sub-type).</summary>
    public static Cmp08 NotApplicable(DateOnly from, DateOnly to) => new(
        from, to, false, null, 0, Money.Zero, Money.Zero, Money.Zero,
        Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);
}
