using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Shared read-only primitives for the GST report projections (phase4-gst-requirements RQ-20..RQ-24; ER-7).
/// Every GST report reads the <b>posted</b> tax straight off the tax <see cref="EntryLine"/>s'
/// <see cref="GstLineTax"/> metadata — the head, the applied rate, the taxable value the tax was computed on,
/// and the line's own <see cref="EntryLine.Amount"/> (the tax) — so the returns never recompute tax; they
/// reconcile to the tax-ledger postings by construction. A voucher's <b>direction</b> (outward vs inward) is
/// derived from its type's base type (DP-11): Sales/Credit-Note ⇒ outward (Output tax), Purchase/Debit-Note ⇒
/// inward (Input tax). Cancelled and post-dated-after-<c>to</c> vouchers are excluded via
/// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> — the same filter the balances
/// use — so a report over the tax lines foots to the ledger postings.
/// </summary>
public static class GstReportSupport
{
    /// <summary>
    /// The GST direction implied by a voucher base type (DP-11), or <c>null</c> for a base type that never
    /// carries GST (contra/payment/receipt/journal/order/inventory/payroll). Sales &amp; Credit-Note are
    /// <b>outward</b> (an outward supply, Output tax → GSTR-1 / GSTR-3B §3.1); Purchase &amp; Debit-Note are
    /// <b>inward</b> (Input tax / ITC → GSTR-3B §4).
    /// </summary>
    public static GstTaxDirection? DirectionOf(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.Sales or VoucherBaseType.CreditNote => GstTaxDirection.Output,
        VoucherBaseType.Purchase or VoucherBaseType.DebitNote => GstTaxDirection.Input,
        _ => null,
    };

    /// <summary>
    /// Enumerates the posted vouchers that carry GST in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> (outward or inward), already filtered for cancelled / optional / provisional
    /// / post-dated-after-<paramref name="to"/> (via <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly,
    /// VoucherBaseType?)"/>) and the lower date bound. Each yielded voucher has at least one tax
    /// (<see cref="GstLineTax"/>) line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedGstVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        foreach (var pair in PostedDirectionalVouchers(company, from, to, direction))
            if (pair.Voucher.Lines.Any(l => l.HasGst))
                yield return pair;
    }

    /// <summary>
    /// Enumerates <b>all</b> posted vouchers in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> — including exempt/nil supplies that carry <b>no</b> tax line — already
    /// filtered for cancelled / optional / provisional / post-dated-after-<paramref name="to"/> and the lower
    /// date bound. GSTR-1 uses this so exempt outward supplies still appear in the HSN summary and exempt
    /// bucket; the taxable ones are the subset with a tax line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedDirectionalVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        if (!company.GstEnabled) yield break;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (DirectionOf(type.BaseType) != direction) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue; // cancelled/post-dated/date filter
            yield return (v, type);
        }
    }

    /// <summary>
    /// The place-of-supply state code for a voucher (DP-7): the party ledger's recorded GST state, falling back
    /// to the company home state for a walk-in with no recorded state. Used to label GSTR-1 rows.
    /// </summary>
    public static string? PlaceOfSupply(Company company, Voucher voucher)
    {
        if (voucher.PartyId is Guid pid && company.FindLedger(pid)?.PartyGst?.StateCode is { } code)
            return code;
        return company.Gst?.HomeStateCode;
    }

    /// <summary>
    /// The integrated-rate basis points a tax line represents, for rate-wise grouping. A CGST/SGST line carries
    /// the <b>half</b> rate on its <see cref="GstLineTax.RateBasisPoints"/> (900 for an 18% intra supply), so we
    /// double it to recover the integrated slab (1800); an IGST line already carries the full rate. A zero-rate
    /// line (unusual) stays 0.
    /// </summary>
    public static int IntegratedRateOf(GstLineTax gst) =>
        gst.TaxHead == GstTaxHead.Integrated ? gst.RateBasisPoints : gst.RateBasisPoints * 2;

    /// <summary>
    /// The taxable value attributable to a voucher's supply: the sum, <b>over each distinct integrated rate
    /// group</b>, of the max taxable value across that group's tax lines. A voucher now posts one tax line per
    /// (head, rate) group, so within one rate group the CGST and SGST lines each record the <b>same</b> group
    /// taxable subtotal (taking the max dedups the two intra heads); an IGST group has a single line. Summing the
    /// per-rate maxes yields the whole-invoice taxable value for a multi-rate invoice (e.g. 1000@18% + 500@5% ⇒
    /// 1500) while still not double-counting the CGST+SGST legs of any one rate group. A single-rate invoice
    /// reduces to the previous "max taxable across tax lines". A voucher with no tax line contributes zero.
    /// <b>Compensation-Cess lines are excluded</b> (Phase 9 slice 1): a cess line records the SAME taxable value on
    /// its own (doubled) cess-rate key, so counting it would double the CGST/SGST taxable value and inject a phantom
    /// rate group into GSTR-1/3B. Cess is a ring-fenced own-column charge, never a CGST/SGST/IGST rate group (ER-2).
    /// </summary>
    public static Money InvoiceTaxableValue(Voucher voucher)
    {
        var maxByRate = new Dictionary<int, decimal>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue; // ring-fenced cess is not a CGST/SGST/IGST rate group
            var rate = IntegratedRateOf(g);
            var cur = maxByRate.TryGetValue(rate, out var m) ? m : 0m;
            if (g.TaxableValue.Amount > cur) maxByRate[rate] = g.TaxableValue.Amount;
        }
        return new Money(maxByRate.Values.Sum());
    }
}
