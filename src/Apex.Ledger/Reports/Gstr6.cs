using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One distribution row of GSTR-6 — what a single recipient registration receives, on one side of the
/// Rule 39(1)(g) eligible / ineligible split, after the Rule 39(1)(j) head conversion.</summary>
public sealed record Gstr6DistributionRow(
    Guid RegistrationId,
    string Name,
    string StateCode,
    string? Gstin,
    bool IsEligible,
    Money Cgst,
    Money Sgst,
    Money Igst,
    Money Cess)
{
    /// <summary>Σ the four heads for this row.</summary>
    public Money Total => new(Cgst.Amount + Sgst.Amount + Igst.Amount + Cess.Amount);
}

/// <summary>
/// <b>FORM GSTR-6</b> — the Input Service Distributor's monthly return (census row 6.24). A pure, read-only,
/// recomputed projection over the posted vouchers attributed to one ISD registration, in exactly the way CMP-08,
/// GSTR-4, GSTR-9 and GSTR-3B are projections in this app: no clock, no randomness, no I/O, no posting.
///
/// <para><b>WHAT IT IS, AND WHY IT IS MONTHLY.</b> Rule 39(1)(a) of the CGST Rules: "<i>the input tax credit
/// available for distribution in a month shall be distributed in the same month and the details thereof shall be
/// furnished in FORM GSTR-6</i>". §39(4) of the CGST Act sets the due date: "<i>Every taxable person registered as
/// an Input Service Distributor shall, for every calendar month or part thereof, furnish … a return,
/// electronically, <b>within thirteen days after the end of such month</b></i>". Both from
/// <c>cbic-gst.gov.in</c>. <see cref="DueDate"/> is the 13th of the following month, derived, never hard-coded per
/// year.</para>
///
/// <para><b>WHERE EACH FIGURE COMES FROM.</b> The credit <i>received</i> for distribution is the posted INPUT tax
/// on inward vouchers recorded under the ISD registration in the month — read off the posted
/// <see cref="GstLineTax"/> lines, never recomputed (ER-9). Each such voucher becomes one
/// <see cref="IsdCreditPool"/>, split into its eligible and ineligible parts by the SAME §17(5)/Table-4(D)
/// classifier the ITC gate uses (<see cref="ItcGateView.ClassifyBaseValue"/>), satisfying Rule 39(1)(g). The
/// distribution itself is <see cref="IsdDistribution"/> — the Rule 39(1)(d)/(e)/(f)/(i)/(j) engine — so the
/// statutory arithmetic lives in one tested place and this report only feeds it.</para>
///
/// <para>🔴 <b>THE CLAUSE LETTERS AND THE SOURCE.</b> Rule 39 was substituted by Notification 12/2024-CT
/// (10.07.2024) with effect from 01.04.2025 (Notification 09/2025-CT, 11.02.2025), and CGST Act §20 was itself
/// substituted w.e.f. the same date by s. 12 of the Finance (No. 8) Act, 2024 — after which <b>§20(2)(a)–(e) and
/// the §20 Explanation no longer exist</b> and the conditions and definitions live wholly in Rule 39.
/// <see cref="IsdDistribution"/> carries the full in-force note and the CBIC source URLs; this file cites the
/// substituted lettering throughout.</para>
///
/// <para><b>THE RELEVANT PERIOD IS DERIVED, NOT ASSUMED.</b> The <b>Explanation to Rule 39</b>, clause (a): the
/// relevant period is "<i>if the recipients of credit have turnover in their States or Union territories in the
/// financial year preceding the year during which credit is to be distributed, the said financial year</i>";
/// otherwise "<i>the last quarter for which details of such turnover of all the recipients are available, previous
/// to the month during which credit is to be distributed</i>". <see cref="Build"/> tries the preceding financial
/// year first and falls back by walking quarters backwards, and records which branch it took in
/// <see cref="RelevantPeriodBasis"/> so the operator can see the basis of a filed figure rather than infer it.</para>
///
/// <para>🔴 <b>WHAT IS NOT BUILT, STATED HERE RATHER THAN LEFT TO BE DISCOVERED.</b></para>
/// <list type="bullet">
///   <item><b>Per-invoice direct attribution.</b> Rule 39(1)(c) distributes credit attributable to ONE recipient
///   only to that recipient, and <see cref="IsdDistribution"/> implements it — but nothing in this schema records,
///   for a given inward invoice, which units it was for. Every pool this report builds therefore carries a
///   <c>null</c> attribution, i.e. the Rule 39(1)(e) all-recipients case. An ISD whose invoices are genuinely
///   unit-specific cannot express that yet, and <see cref="Diagnostics"/> says so on every build that has
///   recipients. Storing it needs a column, which this slice had no schema allocation for.</item>
///   <item><b>The ISD invoice and credit note.</b> Rule 39(1)(k)/(l) require a document per distribution with its
///   own number; Rule 39(1)(m)/(n) govern later debit/credit notes against the distributor. None is issued here —
///   they need a persisted document identity, which again is storage this slice did not have.</item>
///   <item><b>Posting.</b> Nothing here moves credit between registrations. GSTR-6 is a statement of what the
///   distribution WOULD be on the posted data, in the same sense that this app's GSTR-3B shows indicative net tax
///   without posting the set-off.</item>
/// </list>
/// </summary>
public sealed record Gstr6(
    DateOnly From,
    DateOnly To,
    DateOnly DueDate,
    Guid IsdRegistrationId,
    string IsdName,
    string? IsdGstin,
    string IsdStateCode,
    Money ReceivedCgst,
    Money ReceivedSgst,
    Money ReceivedIgst,
    Money ReceivedCess,
    Money EligibleCredit,
    Money IneligibleCredit,
    DateOnly RelevantPeriodFrom,
    DateOnly RelevantPeriodTo,
    string RelevantPeriodBasis,
    IReadOnlyList<Gstr6DistributionRow> Distribution,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Table 4 — Σ the input tax credit received for distribution in the month, across all four heads.</summary>
    public Money TotalReceived =>
        new(ReceivedCgst.Amount + ReceivedSgst.Amount + ReceivedIgst.Amount + ReceivedCess.Amount);

    /// <summary>Σ what the distribution rows actually carry, across all four heads.</summary>
    public Money TotalDistributed => new(Distribution.Sum(r => r.Total.Amount));

    /// <summary>
    /// 🔴 <b>The one figure a filer must be able to check at a glance.</b> Rule 39(1)(b): "<i>the amount
    /// of the credit distributed shall not exceed the amount of credit available for distribution</i>". This is
    /// <see cref="TotalReceived"/> − <see cref="TotalDistributed"/>: zero when every rupee received was
    /// distributed, positive when something was withheld (and then <see cref="Diagnostics"/> names why), and never
    /// negative — a negative would mean more credit left than arrived, which is the failure Rule 39(1)(b) forbids.
    /// </summary>
    public Money UndistributedCredit => new(TotalReceived.Amount - TotalDistributed.Amount);

    /// <summary>True when every rupee of credit received in the month was distributed and nothing was withheld.</summary>
    public bool IsFullyDistributed => UndistributedCredit.Amount == 0m && Diagnostics.Count == 0;

    /// <summary>
    /// Builds GSTR-6 for the calendar month <c>[from, to]</c> and the ISD registration named by
    /// <paramref name="isdRegistrationId"/>.
    ///
    /// <para>Returns an <b>empty</b> return — every figure zero, no rows — when GST is off, when the id names no
    /// registration this company holds, or when the registration it names is not an
    /// <see cref="GstRegistrationType.InputServiceDistributor"/>. That last guard is deliberate: an ordinary
    /// Regular registration files GSTR-1 and GSTR-3B and has no business producing a distribution statement, and
    /// silently building one for it would invite a filed document that has no legal basis.</para>
    /// </summary>
    public static Gstr6 Build(Company company, DateOnly from, DateOnly to, Guid isdRegistrationId)
    {
        ArgumentNullException.ThrowIfNull(company);

        var isd = company.Gst?.FindRegistration(isdRegistrationId);
        if (company.Gst is not { Enabled: true } gst
            || isd is null
            || isd.RegistrationType != GstRegistrationType.InputServiceDistributor)
        {
            return Empty(company, from, to, isdRegistrationId, isd);
        }

        // ---- Table 3/4: the credit received for distribution in the month, per inward voucher. ----
        var pools = new List<IsdCreditPool>();
        decimal recCgst = 0m, recSgst = 0m, recIgst = 0m, recCess = 0m;
        decimal eligibleTotal = 0m, ineligibleTotal = 0m;

        foreach (var (voucher, _) in GstReportSupport.PostedDirectionalVouchers(
                     company, from, to, GstTaxDirection.Input, isdRegistrationId))
        {
            long cgst = 0, sgst = 0, igst = 0, cess = 0;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g) continue;
                // Reverse-charge lines are excluded on the CBIC flier's own statement: "An ISD cannot accept any
                // invoices on which tax is to be discharged under reverse charge mechanism … The ISD itself cannot
                // discharge any tax liability (as person liable to pay tax)"
                // (cbic-gst.gov.in/pdf/e-version-gst-fliers/InputServiceDistributorinGST.pdf). An RCM line under an
                // ISD registration is a data error, not credit for distribution, so it is left out rather than
                // distributed. ⚠️ Section 2(61)/20(1) were amended with effect from 01-04-2025 to bring inter-State
                // RCM supplies INTO the ISD mechanism; that amended flow is not built here and is reported, not guessed.
                if (g.IsReverseCharge) continue;
                if (g.Adjustment is not null) continue; // a stat-adjustment tag is not received credit
                var paisa = PaisaConversion.ToPaisaRounded(line.Amount);
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += paisa; break;
                    case GstTaxHead.State: sgst += paisa; break;
                    case GstTaxHead.Integrated: igst += paisa; break;
                    case GstTaxHead.Cess: cess += paisa; break;
                }
            }

            if (cgst == 0 && sgst == 0 && igst == 0 && cess == 0) continue;

            recCgst += cgst / 100m; recSgst += sgst / 100m; recIgst += igst / 100m; recCess += cess / 100m;

            // Rule 39(1)(g): split the voucher's credit into its eligible and ineligible halves and distribute the
            // two as separate pools, so they can never be merged into one statement row. The classifier is the ITC
            // gate's — §17(5)-blocked and Table-4(D) ineligible both count as "ineligible" for Rule 39(1)(g), which
            // says "ineligible under the provisions of sub-section (5) of section 17 OR OTHERWISE".
            var (eligBase, blockedBase, ineligBase) = ItcGateView.ClassifyBaseValue(company, voucher);
            var ineligibleBase = blockedBase + ineligBase;
            var totalBase = eligBase + ineligibleBase;

            var label = $"{voucher.Number} dated {voucher.Date:dd-MMM-yyyy}";

            if (ineligibleBase <= 0m || totalBase <= 0m)
            {
                pools.Add(new IsdCreditPool(label, cgst, sgst, igst, cess, IsEligible: true));
                eligibleTotal += (cgst + sgst + igst + cess) / 100m;
            }
            else
            {
                // Split each head by the eligible/ineligible share of the base value, the ineligible side taking the
                // remainder so the two halves foot to the voucher's posted tax exactly.
                var eC = ProRata.Paisa(cgst, ToBase(eligBase), ToBase(totalBase));
                var eS = ProRata.Paisa(sgst, ToBase(eligBase), ToBase(totalBase));
                var eI = ProRata.Paisa(igst, ToBase(eligBase), ToBase(totalBase));
                var eX = ProRata.Paisa(cess, ToBase(eligBase), ToBase(totalBase));

                if (eC + eS + eI + eX > 0)
                    pools.Add(new IsdCreditPool(label + " (eligible)", eC, eS, eI, eX, IsEligible: true));
                var nC = cgst - eC; var nS = sgst - eS; var nI = igst - eI; var nX = cess - eX;
                if (nC + nS + nI + nX > 0)
                    pools.Add(new IsdCreditPool(label + " (ineligible)", nC, nS, nI, nX, IsEligible: false));

                eligibleTotal += (eC + eS + eI + eX) / 100m;
                ineligibleTotal += (nC + nS + nI + nX) / 100m;
            }
        }

        // ---- The recipients of credit and their turnover in the relevant period. ----
        var recipientRegs = gst.IsdRecipientRegistrations(isdRegistrationId);
        var (rpFrom, rpTo, rpBasis) = RelevantPeriod(company, recipientRegs, from);

        var recipients = recipientRegs
            .Select(r => new IsdRecipient(
                r.Id, r.Name, r.StateCode, r.Gstin,
                PaisaConversion.ToPaisaRounded(TurnoverOf(company, r.Id, rpFrom, rpTo))))
            .ToList();

        var diagnostics = new List<string>();

        if (recipients.Count == 0)
        {
            diagnostics.Add(
                "This company holds no recipient of credit for the distributor — clause (b) of the Explanation to "
                + "Rule 39 defines one as a supplier having the same PAN as the Input Service Distributor, which "
                + "here means another GST "
                + "registration on this company. Create the branch registrations before filing GSTR-6.");
        }
        else
        {
            // Named on every build that has recipients, because a reader of a distribution statement cannot tell
            // "all credit was genuinely common" from "the product could not express anything else".
            diagnostics.Add(
                "Every invoice in this month was distributed as COMMON credit (Rule 39(1)(e)). Per-invoice direct "
                + "attribution under Rule 39(1)(c) is not recorded by this build, so an invoice that was genuinely for a "
                + "single unit is still being spread pro rata. Check the statement against the invoices before filing.");
        }

        var result = IsdDistribution.Distribute(isd.StateCode, recipients, pools);
        diagnostics.AddRange(result.Diagnostics);

        var rows = result.Lines
            .Select(l => new Gstr6DistributionRow(
                l.RecipientId, l.RecipientName, l.StateCode, l.Gstin, l.IsEligible,
                new Money(l.CgstPaisa / 100m), new Money(l.SgstPaisa / 100m),
                new Money(l.IgstPaisa / 100m), new Money(l.CessPaisa / 100m)))
            .ToList();

        return new Gstr6(
            from, to, DueDateFor(to),
            isd.Id, isd.Name, isd.Gstin, isd.StateCode,
            new Money(recCgst), new Money(recSgst), new Money(recIgst), new Money(recCess),
            new Money(eligibleTotal), new Money(ineligibleTotal),
            rpFrom, rpTo, rpBasis,
            rows, diagnostics);
    }

    /// <summary>
    /// §39(4) of the CGST Act — "<i>within thirteen days after the end of such month</i>". Derived from the return
    /// period's last day so it is right for every month and every year, with no table to go stale.
    /// </summary>
    public static DateOnly DueDateFor(DateOnly periodEnd)
    {
        var firstOfNext = new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(1);
        return firstOfNext.AddDays(12); // the 13th of the following month
    }

    /// <summary>
    /// Clause (a) of the Explanation to Rule 39: the relevant period whose turnover drives <c>t1</c> and <c>T</c>. The preceding
    /// financial year when every recipient has turnover in it; otherwise the last quarter, walking backwards from
    /// the month of distribution, in which details are available for ALL recipients. Falls back to the preceding
    /// financial year (with a turnover of zero, which <see cref="IsdDistribution"/> then refuses loudly) when no
    /// such quarter exists — never to a silently invented split.
    /// </summary>
    private static (DateOnly From, DateOnly To, string Basis) RelevantPeriod(
        Company company, IReadOnlyList<GstRegistration> recipients, DateOnly distributionMonth)
    {
        // The financial year preceding the year during which the credit is to be distributed. The company's own FY
        // start month is used (India's 1-Apr by default, but the book decides), so this follows the book's year.
        var fyStartMonth = company.FinancialYearStart.Month;
        var fyStartDay = company.FinancialYearStart.Day;

        var currentFyStartYear = distributionMonth.Month > fyStartMonth
                                 || (distributionMonth.Month == fyStartMonth && distributionMonth.Day >= fyStartDay)
            ? distributionMonth.Year
            : distributionMonth.Year - 1;

        var prevFrom = new DateOnly(currentFyStartYear - 1, fyStartMonth, fyStartDay);
        var prevTo = new DateOnly(currentFyStartYear, fyStartMonth, fyStartDay).AddDays(-1);

        if (recipients.Count > 0 && recipients.All(r => TurnoverOf(company, r.Id, prevFrom, prevTo).Amount > 0m))
            return (prevFrom, prevTo, "Preceding financial year — Rule 39 Explanation (a), first limb.");

        // Second limb: the last quarter, previous to the month of distribution, for which turnover details of ALL the
        // recipients are available. Walk backwards a bounded number of quarters (three years) so the search always
        // terminates; beyond that there is nothing a filer should be relying on anyway.
        var quarterEnd = FirstOfQuarter(distributionMonth).AddDays(-1);
        for (var i = 0; i < 12; i++)
        {
            var qFrom = FirstOfQuarter(quarterEnd);
            if (recipients.Count > 0 && recipients.All(r => TurnoverOf(company, r.Id, qFrom, quarterEnd).Amount > 0m))
                return (qFrom, quarterEnd, "Last quarter with turnover for every recipient — Rule 39 Explanation (a), second limb.");
            quarterEnd = qFrom.AddDays(-1);
        }

        return (prevFrom, prevTo,
            "Preceding financial year — Rule 39 Explanation (a), first limb; no earlier quarter has turnover for every recipient.");
    }

    /// <summary>The first day of the calendar quarter containing <paramref name="date"/>.</summary>
    private static DateOnly FirstOfQuarter(DateOnly date)
        => new(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);

    /// <summary>
    /// The turnover of one registration in <c>[from, to]</c> — "<i>the turnover in a State or turnover in a Union
    /// territory of such recipient</i>" (Rule 39(1)(d)/(e)). Read off the posted outward supply value, net of sale
    /// returns by base type, exactly as <c>CompositionTaxService</c> reads a composition dealer's turnover, and
    /// floored at zero (a turnover is never negative).
    /// </summary>
    private static Money TurnoverOf(Company company, Guid registrationId, DateOnly from, DateOnly to)
    {
        var total = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(
                     company, from, to, GstTaxDirection.Output, registrationId))
        {
            var sign = type.BaseType == VoucherBaseType.CreditNote ? -1m : 1m;
            total += sign * GstReportSupport.OutwardSupplyValue(company, voucher, type.BaseType).Total.Amount;
        }
        return new Money(Math.Max(0m, total));
    }

    /// <summary>Rupee base values as whole paisa, for the integer <see cref="ProRata.Paisa"/> split.</summary>
    private static long ToBase(decimal rupees) => PaisaConversion.ToPaisaRounded(rupees);

    private static Gstr6 Empty(Company company, DateOnly from, DateOnly to, Guid id, GstRegistration? isd)
    {
        var fyStart = company.FinancialYearStart;
        return new Gstr6(
            from, to, DueDateFor(to), id,
            isd?.Name ?? string.Empty, isd?.Gstin, isd?.StateCode ?? string.Empty,
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            fyStart.AddYears(-1), fyStart.AddDays(-1),
            "Not an Input Service Distributor registration — GSTR-6 does not apply.",
            Array.Empty<Gstr6DistributionRow>(),
            new[]
            {
                "GSTR-6 is the return of an Input Service Distributor (§39(4)). This registration is not an ISD, "
                + "so nothing is distributed. Set the registration type to Input Service Distributor to file one.",
            });
    }
}
