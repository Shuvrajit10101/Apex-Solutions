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
/// excluded. A non-GST company yields empty zero-valued sides.
///
/// <para>🔴 <b>IT IS A USER-REACHABLE REPORT, and the sentence struck here claimed otherwise.</b> The summary used
/// to end "<i>No UI, no DB</i>". That is FALSE and was the reason T2-113 (i) called this the sixth unsigned site
/// rather than a dormant one: <c>ReportsViewModel.BuildTaxAnalysis</c> renders it (via
/// <c>Report.BuildTaxAnalysis</c>) under <c>ReportKind.TaxAnalysis</c>, so every figure here is on screen. The
/// claim is struck rather than deleted because an unreachable projection and a rendered one are graded differently,
/// and this one was misfiled.</para>
///
/// <para>⚠️ <b>REPORTED, NOT FIXED HERE (census 6.23).</b> That single caller passes <b>no</b>
/// <paramref name="registrationId"/>, so on a multi-registration company this report throws
/// <c>GstReportSupport.EnsureRegistrationScoped</c>'s refusal instead of rendering — the screen has no F3
/// registration picker wired to it, unlike the GST filing screen. The parameter here is honoured; supplying it is
/// a Desktop change outside this slice and is filed rather than half-done.</para>
/// </summary>
public sealed record TaxAnalysis(DateOnly From, DateOnly To, TaxAnalysisSide Outward, TaxAnalysisSide Inward)
{
    /// <summary>Builds the Tax Analysis for the whole company over <c>[from, to]</c>.</summary>
    public static TaxAnalysis Build(Company company, DateOnly from, DateOnly to, Guid? registrationId = null)
    {
        var outward = BuildSide(company, from, to, GstTaxDirection.Output, registrationId);
        var inward = BuildSide(company, from, to, GstTaxDirection.Input, registrationId);
        return new TaxAnalysis(from, to, outward, inward);
    }

    private static TaxAnalysisSide BuildSide(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction, Guid? registrationId = null)
    {
        // Accumulate per (head, head-rate): taxable value + tax, read from the posted tax lines.
        var acc = new Dictionary<(GstTaxHead, int), (decimal Taxable, decimal Tax)>();
        var cgst = 0m; var sgst = 0m; var igst = 0m;

        foreach (var (voucher, type) in GstReportSupport.PostedGstVouchers(company, from, to, direction, registrationId))
        {
            // A purchase return REDUCES the inward rate row; unsigned, this analysis overstated the inward side by
            // the whole tax of every purchase return and could not be reconciled against GSTR-3B Table 4.
            //
            // 🔴 T2-122 — BOTH ARMS ARE NOW SIGNED, AND THE PRECONDITION THE OUTWARD HALF WAITED TWO WAVES FOR IS
            // MET. The comment this replaces said: "INWARD ONLY … the outward mirror is real and measured, but it
            // must move together with GSTR-1 so this analysis keeps agreeing with the return it is reconciled
            // against." GSTR-1 HAS MOVED — an unlinked §34 note now takes its own Table 9B row and its tax is signed
            // into the GSTR-1 header — so signing here makes the two agree instead of parting them. That agreement
            // is asserted ACROSS the two projections (not assumed from a shared path, because there is none:
            // Gstr1.Build signs with VoucherEffects.IsReturnNote plus a BuildTable9B fold, this signs with SignOf
            // over one sweep) and against the Output tax LEDGER's own net movement, in
            // GstOutwardRegistrationScopeAndSignTests.
            //
            // 🔴 WHAT WAS WRONG ON SCREEN, measured: a ₹50,000 sale at 18% with ₹20,000 returned reported outward
            // CGST 6,300.00 + SGST 6,300.00 against a true 2,700.00 + 2,700.00 — the return's tax ADDED rather than
            // subtracted — and the 900-basis-point rate row showed taxable 70,000 against a true 30,000. CGST Act
            // §34(2) (taxinformation.cbic.gov.in, opened by content) is the authority: the issuer "shall declare the
            // details of such credit note in the return … and the tax liability shall be ADJUSTED".
            //
            // 🔴 IT ROUTES THROUGH THE SHARED SignOf ON BOTH ARMS RATHER THAN HAND-ROLLING AN OUTWARD RULE, and that
            // is load-bearing here in a way it is not in Gstr3b.ReadSide. There, the CdnLinkFor guard stands FIRST,
            // so SignOf's linked-note branch is unreachable and a bare IsReturnNote gives the same answer. This
            // sweep has NO credit/debit-note exclusion at all, so the linked branch is LIVE: a credit note recording
            // the SUPPLIER's credit note on a PURCHASE carries the CreditNote base type, which DirectionOf maps
            // OUTWARD, yet it is an inward document. IsReturnNote would return -1 and reduce the outward side by a
            // figure that was never outward (measured on the fixture: 2,700 would read 1,800); SignOf returns 0 —
            // "not on this side at all" — because the linked ORIGINAL is a Purchase. That is the second wrong-side
            // defect T2-113 records, and this is the one outward sweep where it is reachable.
            var sign = GstReportSupport.SignOf(company, voucher, type.BaseType);
            if (sign == 0) continue;

            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g) continue;
                var key = (g.TaxHead, g.RateBasisPoints);
                var (t, x) = acc.TryGetValue(key, out var cur) ? cur : (0m, 0m);
                acc[key] = (t + sign * g.TaxableValue.Amount, x + sign * line.Amount.Amount);

                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += sign * line.Amount.Amount; break;
                    case GstTaxHead.State: sgst += sign * line.Amount.Amount; break;
                    case GstTaxHead.Integrated: igst += sign * line.Amount.Amount; break;
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
