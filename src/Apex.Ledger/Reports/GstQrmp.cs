using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The <b>M1 / M2 PMT-06 monthly-tax suggestion</b> for a QRMP filer (Phase 9 slice 8b; RQ-17; DP-19; A14-CONFIRMED).
/// A QRMP filer files GSTR-1 + GSTR-3B <b>quarterly</b> but <b>pays tax monthly</b> for the first two months of each
/// quarter (M1, M2) via a PMT-06 challan. This is a <b>pure, advisory projection</b> — S8b posts nothing; the actual
/// challan is booked by the S7 <see cref="Services.GstDepositService"/>. It surfaces the deposit computed <b>both</b>
/// fixed-sum ways plus the self-assessment method (the filer picks one):
/// <list type="bullet">
///   <item><b>Fixed-sum 35% of the preceding quarter's cash</b> — 35% of the tax paid <b>in cash</b> in the immediately
///     preceding <b>quarter</b> (the basis when the prior period was <b>quarterly</b>).</item>
///   <item><b>Fixed-sum 100% of the preceding quarter's last month's cash</b> — 100% of the cash tax of the <b>last
///     month</b> of the preceding quarter (the basis when the prior period was <b>monthly</b>). Both bases are shown; the
///     filer applies whichever matches its prior-period type — S8b never applies 35% unconditionally.</item>
///   <item><b>Self-assessment</b> — the forward output tax on the month's supplies less the available ITC (forward + the
///     matching RCM ITC), floored at zero, <b>plus</b> the reverse-charge output liability added on top as a cash-only
///     floor (§49(4): RCM output is never discharged by ITC). Cess is excluded consistently across all three methods
///     (GSTR-3B carries no forward output-cess field), so no method silently counts cess while another drops it.</item>
/// </list>
/// <see cref="AlreadyDeposited"/> reconciles the suggestion to the S7 challans actually deposited in the month (the M3
/// quarterly GSTR-3B nets the M1/M2 cash already paid). "Cash paid" counts only the <b>Tax</b> minor-head PMT-06 challans
/// (an interest / late-fee deposit is not tax, DP-34). Deterministic, paisa-exact.
/// </summary>
public sealed record GstPmt06Suggestion(
    DateOnly MonthFrom,
    DateOnly MonthTo,
    Money FixedSum35PercentPriorQuarter,
    Money FixedSum100PercentLastMonth,
    Money SelfAssessment,
    Money AlreadyDeposited);

/// <summary>
/// One <b>IFF (Invoice Furnishing Facility)</b> window for M1 or M2 of a QRMP quarter (Phase 9 slice 8b; RQ-17; DP-19).
/// IFF is the <b>optional B2B-invoices-only</b> upload a QRMP filer may furnish for the first two months of a quarter
/// (capped ₹50 lakh/month, filed by the 13th of the succeeding month) so the recipient sees the ITC early. It is a
/// <b>subset of the quarterly GSTR-1</b>: an invoice furnished via IFF is <b>not</b> re-entered in the quarterly return
/// (see <see cref="GstQrmpQuarter.QuarterlyResidualB2B"/>). B2C and the residual (M3) B2B always go in the quarterly
/// GSTR-1. The B2B rows are the exact <see cref="Gstr1.B2B"/> subset for the month (never recomputed).
/// </summary>
public sealed record GstIffWindow(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Gstr1B2BRow> B2B,
    Money TaxableValue,
    bool ExceedsCap)
{
    /// <summary>The IFF monthly cap — ₹50 lakh of invoice value (A14-CONFIRMED).</summary>
    public const decimal MonthlyCapRupees = 5_000_000m;

    /// <summary>The number of B2B invoices eligible to be furnished via IFF this month.</summary>
    public int InvoiceCount => B2B.Count;
}

/// <summary>
/// One QRMP <b>quarter</b> (Phase 9 slice 8b): the quarterly GSTR-1 / GSTR-3B return window, the M1/M2 IFF B2B windows,
/// the M3-only residual B2B that goes in the quarterly GSTR-1 (so the IFF-furnished M1/M2 B2B is never double-counted),
/// and the M1/M2 PMT-06 monthly-tax suggestions.
/// </summary>
public sealed record GstQrmpQuarter(
    int Index,
    DateOnly From,
    DateOnly To,
    GstIffWindow Month1Iff,
    GstIffWindow Month2Iff,
    IReadOnlyList<Gstr1B2BRow> QuarterlyResidualB2B,
    GstPmt06Suggestion Month1Pmt06,
    GstPmt06Suggestion Month2Pmt06);

/// <summary>
/// <b>QRMP</b> (Quarterly-Return-Monthly-Payment) cadence projection (Phase 9 slice 8b; RQ-17; DP-19). A pure, read-only
/// projection over the <b>existing</b> <see cref="GstConfig.Periodicity"/> election (persisted today as
/// <c>companies.gst_periodicity</c>) — <b>no schema</b>. For a Regular filer whose <see cref="GstReturnPeriodicity"/> is
/// <c>Quarterly</c> it lays out the FY's four quarters, and for each the M1/M2 IFF B2B windows + the M1/M2 PMT-06
/// suggestions (both fixed-sum bases + self-assessment). A <b>Monthly</b> filer (and a Composition / GST-off company)
/// yields a <b>not-applicable</b> (empty) projection, so ER-13 is automatic. Deterministic, paisa-exact; posts nothing.
/// </summary>
public sealed record GstQrmp(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    IReadOnlyList<GstQrmpQuarter> Quarters)
{
    /// <summary>Builds the QRMP/IFF cadence for a Quarterly-filing Regular company over the FY <c>[fyFrom, fyTo]</c>; a
    /// Monthly filer / Composition / GST-off company yields a not-applicable (empty) projection (ER-13).</summary>
    public static GstQrmp Build(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        ArgumentNullException.ThrowIfNull(company);

        // QRMP is the Quarterly-Return-Monthly-Payment scheme for a REGULAR filer who elected Quarterly periodicity
        // (AATO ≤ ₹5 cr). A Monthly filer pays + files monthly (no QRMP), and a Composition dealer files CMP-08 / GSTR-4
        // (not GSTR-1/3B) — both, and a GST-off company, yield a not-applicable (empty) projection ⇒ ER-13 is automatic.
        if (!company.GstEnabled
            || company.Gst?.RegistrationType != GstRegistrationType.Regular
            || company.Gst?.Periodicity != GstReturnPeriodicity.Quarterly)
            return new GstQrmp(fyFrom, fyTo, false, []);

        var quarters = new List<GstQrmpQuarter>(4);
        for (var i = 0; i < 4; i++)
        {
            var qFrom = fyFrom.AddMonths(3 * i);
            var qTo = fyFrom.AddMonths(3 * (i + 1)).AddDays(-1);
            var m1From = qFrom;
            var m1To = qFrom.AddMonths(1).AddDays(-1);
            var m2From = qFrom.AddMonths(1);
            var m2To = qFrom.AddMonths(2).AddDays(-1);
            var m3From = qFrom.AddMonths(2);
            var m3To = qTo;

            // The immediately preceding quarter (the fixed-sum 35% basis) and its LAST month (the 100% basis).
            var pqFrom = qFrom.AddMonths(-3);
            var pqTo = qFrom.AddDays(-1);
            var pqLastMonthFrom = qFrom.AddMonths(-1);

            var m1Iff = BuildIff(company, m1From, m1To);
            var m2Iff = BuildIff(company, m2From, m2To);
            // The quarterly GSTR-1 carries only the M3 (residual) B2B — the M1/M2 B2B was furnished via IFF (no
            // double-count). B2C and everything else still go in the quarterly GSTR-1 (out of scope of this residual list).
            var residual = Gstr1.Build(company, m3From, m3To).B2B;

            var m1Pmt = BuildPmt06(company, m1From, m1To, pqFrom, pqTo, pqLastMonthFrom);
            var m2Pmt = BuildPmt06(company, m2From, m2To, pqFrom, pqTo, pqLastMonthFrom);

            quarters.Add(new GstQrmpQuarter(i + 1, qFrom, qTo, m1Iff, m2Iff, residual, m1Pmt, m2Pmt));
        }

        return new GstQrmp(fyFrom, fyTo, true, quarters);
    }

    /// <summary>The IFF B2B subset for a month: the exact <see cref="Gstr1.B2B"/> rows (B2C excluded), their Σ taxable
    /// value, and the ₹50 lakh cap flag.</summary>
    private static GstIffWindow BuildIff(Company company, DateOnly from, DateOnly to)
    {
        var b2b = Gstr1.Build(company, from, to).B2B;
        var taxable = b2b.Sum(r => r.TaxableValue.Amount);
        return new GstIffWindow(from, to, b2b, new Money(taxable), taxable > GstIffWindow.MonthlyCapRupees);
    }

    /// <summary>The M1/M2 PMT-06 suggestion: the fixed-sum 35%-of-preceding-quarter-cash and 100%-of-last-month-cash
    /// bases, the self-assessment net cash liability for the month, and the cash actually deposited in the month.</summary>
    private static GstPmt06Suggestion BuildPmt06(
        Company company, DateOnly monthFrom, DateOnly monthTo, DateOnly priorQuarterFrom, DateOnly priorQuarterTo,
        DateOnly priorQuarterLastMonthFrom)
    {
        var priorQuarterCash = TaxCashDeposited(company, priorQuarterFrom, priorQuarterTo);
        var lastMonthCash = TaxCashDeposited(company, priorQuarterLastMonthFrom, priorQuarterTo);

        // Self-assessment = the month's suggested cash deposit. ITC (forward + the matching RCM ITC) may reduce ONLY the
        // FORWARD output tax, floored at zero (a credit surplus carries forward, it is not a negative payment). The
        // reverse-charge output liability is CASH-ONLY (§49(4)/§2(82); ER-3): it can NEVER be discharged by ITC — not even
        // the matching RCM ITC (which itself only carries forward / offsets OTHER forward output) — so it is added on TOP
        // as a cash-only floor, mirroring the authoritative poster GstSetOffService (which layers cashRcm over the credit
        // steps). Cess is excluded consistently across all three methods: GSTR-3B exposes no forward output-cess field
        // (the accepted S1/3B gap — not fixed by a schema change here), so this light advisory drops cess everywhere
        // rather than counting it in one method while dropping it in another (see TaxCashDeposited).
        var m = Gstr3b.Build(company, monthFrom, monthTo);
        var forwardNet = Math.Max(0m, m.TotalOutwardTax.Amount - (m.TotalItc.Amount + m.TotalRcmItc.Amount));
        var self = forwardNet + m.TotalRcmOutward.Amount;

        return new GstPmt06Suggestion(
            monthFrom, monthTo,
            new Money(Round(priorQuarterCash * 0.35m)),
            new Money(lastMonthCash),
            new Money(self),
            new Money(TaxCashDeposited(company, monthFrom, monthTo)));
    }

    /// <summary>Σ the <b>Tax</b> minor-head PMT-06 challan deposits made in <c>[from, to]</c> (the "cash paid" basis). An
    /// interest / late-fee / penalty deposit is NOT tax paid (DP-34), so it is excluded — mirroring GSTR-9 Table 9. The
    /// <b>Cess</b> major head is likewise excluded so the three PMT-06 methods treat Compensation Cess CONSISTENTLY: the
    /// self-assessment drops cess (GSTR-3B exposes no forward output-cess field — the accepted S1/3B gap, not fixed by a
    /// schema change here), so the fixed-sum + already-deposited cash bases must drop it too, else one method would
    /// silently count cess while another dropped it. RCM cess, like all RCM output, is cash-only and out of this advisory
    /// (ER-2/ER-3).</summary>
    private static decimal TaxCashDeposited(Company company, DateOnly from, DateOnly to)
    {
        var sum = 0m;
        foreach (var ch in company.GstChallans)
            if (ch.MinorHead == GstMinorHead.Tax && ch.MajorHead != GstTaxHead.Cess
                && ch.DepositDate >= from && ch.DepositDate <= to)
                sum += ch.Amount.Amount;
        return sum;
    }

    private static decimal Round(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);
}
