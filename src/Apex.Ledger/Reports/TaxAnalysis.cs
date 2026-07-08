using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Tax Analysis rate/head row (phase4-gst-requirements RQ-20): the taxable value and tax accumulated for a
/// single (<see cref="Head"/>, <see cref="RateBasisPoints"/>) pair over the period, on one side (outward or
/// inward). <see cref="RateBasisPoints"/> is the <b>head's own</b> applied rate (900 for a CGST/SGST leg of an
/// 18% intra supply; 1800 for an IGST leg), matching the <see cref="GstLineTax.RateBasisPoints"/> posted.
/// </summary>
public sealed record TaxAnalysisRateRow(GstTaxHead Head, int RateBasisPoints, Money TaxableValue, Money Tax);

/// <summary>
/// One side (outward or inward) of the Tax Analysis: the per-head totals (Σ CGST/SGST/IGST tax) and the
/// rate-wise breakdown rows, read from the posted tax lines. <see cref="TotalTax"/> = CGST + SGST + IGST.
/// </summary>
public sealed record TaxAnalysisSide(
    IReadOnlyList<TaxAnalysisRateRow> RateRows,
    Money TotalCgst,
    Money TotalSgst,
    Money TotalIgst)
{
    /// <summary>Σ all tax on this side (CGST + SGST + IGST).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);
}

/// <summary>
/// The <b>Tax Analysis</b> period summary (phase4-gst-requirements RQ-20; catalog §12) — a pure, read-only
/// projection over the posted GST vouchers in <c>[from, to]</c>. It reads the tax straight from each tax
/// <see cref="EntryLine"/>'s <see cref="GstLineTax"/> (head, applied rate, taxable value) and the line's
/// <see cref="EntryLine.Amount"/> (the tax itself) — it never recomputes tax — so its per-head totals
/// reconcile to the Output/Input tax-ledger postings for the period, to the paisa (ER-7). It separates
/// <see cref="Outward"/> (Sales/Credit-Note ⇒ Output tax) from <see cref="Inward"/> (Purchase/Debit-Note ⇒
/// Input tax), each summarised by GST rate and by head. Cancelled and post-dated-after-<c>to</c> vouchers are
/// excluded. A non-GST company yields empty zero-valued sides. No UI, no DB.
/// </summary>
public sealed record TaxAnalysis(DateOnly From, DateOnly To, TaxAnalysisSide Outward, TaxAnalysisSide Inward)
{
    /// <summary>Builds the Tax Analysis for the whole company over <c>[from, to]</c>.</summary>
    public static TaxAnalysis Build(Company company, DateOnly from, DateOnly to)
    {
        var outward = BuildSide(company, from, to, GstTaxDirection.Output);
        var inward = BuildSide(company, from, to, GstTaxDirection.Input);
        return new TaxAnalysis(from, to, outward, inward);
    }

    private static TaxAnalysisSide BuildSide(Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        // Accumulate per (head, head-rate): taxable value + tax, read from the posted tax lines.
        var acc = new Dictionary<(GstTaxHead, int), (decimal Taxable, decimal Tax)>();
        var cgst = 0m; var sgst = 0m; var igst = 0m;

        foreach (var (voucher, _) in GstReportSupport.PostedGstVouchers(company, from, to, direction))
        {
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g) continue;
                var key = (g.TaxHead, g.RateBasisPoints);
                var (t, x) = acc.TryGetValue(key, out var cur) ? cur : (0m, 0m);
                acc[key] = (t + g.TaxableValue.Amount, x + line.Amount.Amount);

                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += line.Amount.Amount; break;
                    case GstTaxHead.State: sgst += line.Amount.Amount; break;
                    case GstTaxHead.Integrated: igst += line.Amount.Amount; break;
                }
            }
        }

        var rows = acc
            .Select(kv => new TaxAnalysisRateRow(kv.Key.Item1, kv.Key.Item2,
                new Money(kv.Value.Taxable), new Money(kv.Value.Tax)))
            .OrderBy(r => r.RateBasisPoints).ThenBy(r => r.Head)
            .ToList();

        return new TaxAnalysisSide(rows, new Money(cgst), new Money(sgst), new Money(igst));
    }
}
