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
/// electronically, <b>within thirteen days after the end of such month</b></i>". Rule 39 from
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>
/// and §39(4) from <c>…/2017_CGST_act/active/chapter9/section39_v1.00.html</c>, both fetched and read by content.
/// <see cref="DueDate"/> is the 13th of the following month, derived, never hard-coded per year.</para>
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
/// <para><b>THE RELEVANT PERIOD IS DERIVED, NOT ASSUMED.</b> The <b>Explanation to Rule 39</b>, clause (i): the
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
        // Reverse-charge credit received under the ISD registration: counted as RECEIVED (§20(1) puts it there
        // expressly) but held back from the distribution, and named in a diagnostic. See the long note below.
        decimal rcmReceived = 0m;
        var rcmVouchers = new List<string>();

        // 🔴 PURCHASE RETURNS. A purchase-return Debit Note REDUCES the credit available for distribution — Rule
        // 39(1)(b)'s cap moves DOWN — and before this it was ADDED. Measured: a ₹50,000 input service at 18%
        // (₹9,000) with ₹20,000 returned (₹3,600) reported received ₹12,600.00 and DISTRIBUTED ₹12,600.00 across
        // real GSTINs against ₹5,400.00 available, with UndistributedCredit showing 0.00 because BOTH sides were
        // inflated by the same ₹3,600 — so the Rule 39(1)(b) footing row, which exists to catch exactly this, read
        // perfectly. 133% overstated on a filed return.
        //
        // The return is accumulated here rather than pushed through as a negative pool because IsdDistribution
        // REFUSES a negative head, and rightly: reducing credit already distributed is an ISD credit note under
        // Rule 39(1)(l)/(n), a separate document this build does not issue. So the reduction is netted against the
        // pools BEFORE distribution, per eligibility, and anything it cannot absorb is reported rather than hidden.
        // 🔴 PER HEAD, NOT AS A LUMP. A first cut accumulated one total and consumed it from the pool head by head,
        // which got TotalReceived right and the HEADS wrong: an intra-State return of CGST 1,800 + SGST 1,800 ate
        // ₹3,600 out of CGST alone, leaving CGST 900.00 / SGST 4,500.00 where 2,700.00 / 2,700.00 is correct. The
        // per-head figure is what the return actually reports and what the recipient claims, so a right total with a
        // wrong head mix is still wrong money on a filed document — and CGST and SGST must stay equal on an
        // intra-State credit.
        long retEligC = 0, retEligS = 0, retEligI = 0, retEligX = 0;
        long retIneligC = 0, retIneligS = 0, retIneligI = 0, retIneligX = 0;
        var returnVouchers = new List<string>();

        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(
                     company, from, to, GstTaxDirection.Input, isdRegistrationId))
        {
            // +1 a purchase, -1 a purchase return, 0 a §34 note on an OUTWARD supply (not inward credit at all).
            var sign = GstReportSupport.SignOf(company, voucher, type.BaseType);
            if (sign == 0) continue;

            long cgst = 0, sgst = 0, igst = 0, cess = 0;
            long rcm = 0, rcmC = 0, rcmS = 0, rcmI = 0, rcmX = 0;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { } g) continue;
                if (g.Adjustment is not null) continue; // a stat-adjustment tag is not received credit

                // 🔴 REVERSE CHARGE. READ THIS BEFORE CHANGING THE BRANCH BELOW — IT WAS A SILENT `continue` AND
                // THAT WAS WRONG MONEY ON A FILED RETURN.
                //
                // The original code did `if (g.IsReverseCharge) continue;` BEFORE the accumulation, on the strength
                // of the old CBIC flier's "an ISD cannot accept any invoices on which tax is to be discharged under
                // reverse charge mechanism". Two things were wrong with that. First, the flier URL does not resolve
                // (404), so the claim had no retrievable source. Second and much worse, the flier stated the
                // PRE-AMENDMENT position, and the substituted §20 — in force from 01.04.2025, which is on or before
                // every date this app distributes for — says the opposite in its own words:
                //
                //   §20(1): "Any office of the supplier … which receives tax invoices towards the receipt of input
                //   services, INCLUDING INVOICES IN RESPECT OF SERVICES LIABLE TO TAX UNDER SUB-SECTION (3) OR
                //   SUB-SECTION (4) OF SECTION 9 of this Act or under sub-section (3) or sub-section (4) of section 5
                //   of the Integrated Goods and Services Tax Act, 2017 … shall be required to be registered as Input
                //   Service Distributor … and shall distribute the input tax credit in respect of such invoices."
                //
                //   §20(2): "The Input Service Distributor shall distribute the credit … INCLUDING THE CREDIT … IN
                //   RESPECT OF SERVICES SUBJECT TO LEVY OF TAX UNDER SUB-SECTION (3) OR SUB-SECTION (4) OF SECTION 9
                //   … paid by a distinct person registered in the same State as the said Input Service Distributor …"
                //
                // (taxinformation.cbic.gov.in/content/html/tax_repository/gst/acts/2017_CGST_act/active/chapter5/
                // section20_v1.00.html — fetched and read by content for this slice. §9(3)/(4) IS reverse charge.)
                //
                // So RCM credit is squarely INSIDE the ISD mechanism now, and dropping it silently understated both
                // the credit received and the credit distributed by the same amount — which meant UndistributedCredit
                // came out at ZERO and the return LOOKED perfectly footed while being short by the whole RCM figure.
                // That is the exact failure mode this report's own Rule 39(1)(b) footing check exists to catch, and
                // the drop was positioned to slip past it.
                //
                // What is done instead, and why not simply distribute it: §20(2) attaches a condition this build
                // cannot check — the tax must have been "paid by a distinct person registered in the same State as
                // the said Input Service Distributor", i.e. the ISD does not discharge it itself and there is a
                // cross-charge behind it. This app has no cross-charge model and cannot tell that shape from an RCM
                // purchase mis-posted onto the ISD registration. Distributing regardless would invent the condition;
                // dropping silently hides money. So the credit is COUNTED AS RECEIVED (§20(1) puts it there
                // expressly), WITHHELD from the distribution, and NAMED in a diagnostic — leaving UndistributedCredit
                // non-zero and the reason on the face of the return. That is the same visible-and-fixable convention
                // IsdDistribution already applies when the Rule 39(1)(f) denominator T is zero.
                if (g.IsReverseCharge)
                {
                    // 🔴 ONE RCM VOUCHER CARRIES TWO TAGGED LINES AND ONLY ONE OF THEM IS CREDIT. The §49(4) output
                    // liability the recipient bears is posted to a ledger whose own GstClassification is
                    // IsReverseCharge; the ITC side is not. Counting both double-counts the credit — the first cut of
                    // this fix did exactly that and reported ₹10,800 received where ₹9,009 was right, which the
                    // emitted-file footing test caught. The discriminator below is the one this codebase already
                    // uses for the same distinction in Gstr4 and GstReportSupport, so the three agree by
                    // construction rather than by coincidence.
                    if (company.FindLedger(line.LedgerId)?.GstClassification is { IsReverseCharge: true })
                        continue;

                    var rcmPaisa = PaisaConversion.ToPaisaRounded(line.Amount);
                    rcm += rcmPaisa;
                    // Kept per head so TotalReceived stays a truthful per-head figure rather than a lump.
                    switch (g.TaxHead)
                    {
                        case GstTaxHead.Central: rcmC += rcmPaisa; break;
                        case GstTaxHead.State: rcmS += rcmPaisa; break;
                        case GstTaxHead.Integrated: rcmI += rcmPaisa; break;
                        case GstTaxHead.Cess: rcmX += rcmPaisa; break;
                    }
                    continue;
                }
                var paisa = PaisaConversion.ToPaisaRounded(line.Amount);
                switch (g.TaxHead)
                {
                    case GstTaxHead.Central: cgst += paisa; break;
                    case GstTaxHead.State: sgst += paisa; break;
                    case GstTaxHead.Integrated: igst += paisa; break;
                    case GstTaxHead.Cess: cess += paisa; break;
                }
            }

            // Reverse-charge credit on this voucher: into RECEIVED (§20(1)), never into a pool, and remembered so
            // the diagnostic can name the vouchers rather than just an amount.
            if (rcm > 0)
            {
                // Signed: a return of a reverse-charge input service reduces both the received figure and the
                // withheld RCM figure, so the diagnostic's amount stays truthful.
                recCgst += sign * rcmC / 100m; recSgst += sign * rcmS / 100m;
                recIgst += sign * rcmI / 100m; recCess += sign * rcmX / 100m;
                rcmReceived += sign * rcm / 100m;
                rcmVouchers.Add($"{voucher.Number} dated {voucher.Date:dd-MMM-yyyy}");
            }

            if (cgst == 0 && sgst == 0 && igst == 0 && cess == 0) continue;

            recCgst += sign * cgst / 100m; recSgst += sign * sgst / 100m;
            recIgst += sign * igst / 100m; recCess += sign * cess / 100m;

            // Rule 39(1)(g): split the voucher's credit into its eligible and ineligible halves and distribute the
            // two as separate pools, so they can never be merged into one statement row. The classifier is the ITC
            // gate's — §17(5)-blocked and Table-4(D) ineligible both count as "ineligible" for Rule 39(1)(g), which
            // says "ineligible under the provisions of sub-section (5) of section 17 OR OTHERWISE".
            var (eligBase, blockedBase, ineligBase) = ItcGateView.ClassifyBaseValue(company, voucher);
            var ineligibleBase = blockedBase + ineligBase;
            var totalBase = eligBase + ineligibleBase;

            var label = $"{voucher.Number} dated {voucher.Date:dd-MMM-yyyy}";

            // A RETURN creates no pool. It is classified by its OWN lines — a returned blocked item reduces the
            // ineligible side, a returned eligible item the eligible side — and netted against the pools after the
            // loop, so the eligible/ineligible split of Rule 39(1)(g) survives the netting.
            if (sign < 0)
            {
                if (ineligibleBase <= 0m || totalBase <= 0m)
                {
                    retEligC += cgst; retEligS += sgst; retEligI += igst; retEligX += cess;
                    eligibleTotal -= (cgst + sgst + igst + cess) / 100m;
                }
                else
                {
                    // Same per-head eligible/ineligible split the purchase path uses, so a returned mixed bill nets
                    // off the two Rule 39(1)(g) sides in the proportion it was received in.
                    var eC = ProRata.Paisa(cgst, ToBase(eligBase), ToBase(totalBase));
                    var eS = ProRata.Paisa(sgst, ToBase(eligBase), ToBase(totalBase));
                    var eI = ProRata.Paisa(igst, ToBase(eligBase), ToBase(totalBase));
                    var eX = ProRata.Paisa(cess, ToBase(eligBase), ToBase(totalBase));

                    retEligC += eC; retEligS += eS; retEligI += eI; retEligX += eX;
                    retIneligC += cgst - eC; retIneligS += sgst - eS;
                    retIneligI += igst - eI; retIneligX += cess - eX;

                    eligibleTotal -= (eC + eS + eI + eX) / 100m;
                    ineligibleTotal -= ((cgst - eC) + (sgst - eS) + (igst - eI) + (cess - eX)) / 100m;
                }
                returnVouchers.Add(label);
                continue;
            }

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

        // ---- Net the month's purchase returns against the pools, per eligibility (Rule 39(1)(b)). ----
        var unabsorbed =
            NetReturnsAgainstPools(pools, retEligC, retEligS, retEligI, retEligX, IsEligible: true)
          + NetReturnsAgainstPools(pools, retIneligC, retIneligS, retIneligI, retIneligX, IsEligible: false);

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
                "This company holds no recipient of credit for the distributor — clause (ii) of the Explanation to "
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

        if (rcmReceived > 0m)
        {
            // Loud on purpose: this figure is IN TotalReceived and NOT in TotalDistributed, so it is exactly the
            // gap UndistributedCredit now shows, and the filer is told which vouchers make it up.
            diagnostics.Add(
                $"₹{IndianMoneyFormat.Amount(rcmReceived)} of reverse-charge credit was received under this "
                + "registration and has NOT been distributed. Section 20(1) brings invoices for services liable to "
                + "tax under section 9(3)/9(4) into the ISD mechanism with effect from 01-04-2025, so this credit is "
                + "shown as received; but section 20(2) only allows it to be distributed where the tax was \"paid by "
                + "a distinct person registered in the same State as the said Input Service Distributor\", and this "
                + "build records no cross-charge, so it cannot confirm that condition. Confirm the treatment before "
                + $"filing. Voucher(s): {string.Join("; ", rcmVouchers)}.");
        }

        var returnedTotal = retEligC + retEligS + retEligI + retEligX
                          + retIneligC + retIneligS + retIneligI + retIneligX;
        if (returnedTotal > 0)
        {
            diagnostics.Add(
                $"₹{IndianMoneyFormat.Amount(returnedTotal / 100m)} of input tax credit was RETURNED in this "
                + "period on a purchase-return debit note and has been netted off the credit available for "
                + "distribution: Rule 39(1)(b) caps the distribution at \"the amount of credit available for "
                + "distribution\", and a return moves that amount down. The eligible and ineligible halves are "
                + $"netted separately so the Rule 39(1)(g) split survives. Voucher(s): {string.Join("; ", returnVouchers)}.");
        }

        if (unabsorbed > 0)
        {
            // Reported, never floored: a return bigger than the month's credit is a real condition the filer must
            // resolve, and silently clamping it to zero would hand them a plausible-looking return instead.
            diagnostics.Add(
                $"₹{IndianMoneyFormat.Amount(unabsorbed / 100m)} of the period's purchase returns could NOT be netted "
                + "against the credit received, because the returns exceed it. Nothing was distributed for that "
                + "excess and it is left visible in the Rule 39(1)(b) row as a negative figure. Either the original "
                + "invoice falls in an earlier period — in which case the reduction belongs to an ISD credit note "
                + "under Rule 39(1)(l)/(n), which this build does not issue — or the return is mis-dated. Resolve it "
                + "before filing.");
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
    /// 🔴 <b>Clause (i) of the Explanation to Rule 39 — NOT clause (a).</b> The Explanation is lettered
    /// (i)/(ii)/(iii), and "relevant period" is (i), whose two limbs are (i)(a) and (i)(b). Retrieved verbatim from
    /// the CBIC rule-39 page: "<i>Explanation. – For the purpose of this rule, – (i) the term "relevant period" shall
    /// be— (a) … or (b) …; (ii) the expression "recipient of credit" means the supplier … having the same Permanent
    /// Account Number as that of the Input Service Distributor; (iii) the term "turnover" …</i>". The pre-substitution
    /// (a)/(b) lettering was used at ten sites here, five of which reached the SCREEN and the EMITTED FILE via
    /// <c>RelevantPeriodBasis</c> / <c>relevant_period_basis</c> — a wrong clause citation on a filed return is the
    /// same class of defect as a wrong figure.
    /// <para>The relevant period whose turnover drives <c>t1</c> and <c>T</c>. The preceding
    /// financial year when every recipient has turnover in it; otherwise the last quarter, walking backwards from
    /// the month of distribution, in which details are available for ALL recipients. Falls back to the preceding
    /// financial year (with a turnover of zero, which <see cref="IsdDistribution"/> then refuses loudly) when no
    /// such quarter exists — never to a silently invented split.</para>
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
            return (prevFrom, prevTo, "Preceding financial year — Rule 39 Explanation (i)(a).");

        // Second limb: the last quarter, previous to the month of distribution, for which turnover details of ALL the
        // recipients are available. Walk backwards a bounded number of quarters (three years) so the search always
        // terminates; beyond that there is nothing a filer should be relying on anyway.
        var quarterEnd = FirstOfQuarter(distributionMonth).AddDays(-1);
        for (var i = 0; i < 12; i++)
        {
            var qFrom = FirstOfQuarter(quarterEnd);
            if (recipients.Count > 0 && recipients.All(r => TurnoverOf(company, r.Id, qFrom, quarterEnd).Amount > 0m))
                return (qFrom, quarterEnd, "Last quarter with turnover for every recipient — Rule 39 Explanation (i)(b).");
            quarterEnd = qFrom.AddDays(-1);
        }

        return (prevFrom, prevTo,
            "Preceding financial year — Rule 39 Explanation (i)(a); no earlier quarter has turnover for every recipient.");
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
            // Routed through the one helper that answers this for both sides. On an Output sweep this is the same
            // answer the hand-rolled `BaseType == CreditNote ? -1 : 1` gave — which is exactly why the identical
            // expression copied onto an INPUT sweep looked right and was a silent no-op. See GstReportSupport.SignOf.
            var sign = GstReportSupport.SignOf(company, voucher, type.BaseType);
            total += sign * GstReportSupport.OutwardSupplyValue(company, voucher, type.BaseType).Total.Amount;
        }
        return new Money(Math.Max(0m, total));
    }

    /// <summary>
    /// Nets <paramref name="returnPaisa"/> of purchase-return credit off the pools of the matching
    /// <paramref name="IsEligible"/> side, and returns whatever could NOT be absorbed (0 when it all was).
    ///
    /// <para><b>Why the pools and not the distribution.</b> Rule 39(1)(b) caps the distribution at "<i>the amount of
    /// credit available for distribution</i>"; a purchase return moves that amount down, so the reduction belongs
    /// before the split, not after it. Pushing it through as a negative pool is not an option —
    /// <see cref="IsdDistribution.Distribute"/> refuses a negative head, and the refusal is correct: reducing credit
    /// already distributed is an ISD credit note under Rule 39(1)(l)/(n), a separate document this build does not
    /// issue.</para>
    ///
    /// <para>🔴 <b>EACH HEAD IS NETTED AGAINST ITS OWN HEAD.</b> CGST comes off CGST, SGST off SGST, IGST off IGST,
    /// cess off cess — never off whichever head happens to have a balance. A first cut consumed one lump total head
    /// by head and produced the right <c>TotalReceived</c> with a WRONG head mix (an intra-State return of
    /// CGST 1,800 + SGST 1,800 left CGST 900.00 / SGST 4,500.00 instead of 2,700.00 / 2,700.00). The per-head figure
    /// is what the recipient claims, and CGST and SGST must stay equal on an intra-State credit, so a right total
    /// over a wrong mix is still wrong money. Cess stays ring-fenced (ER-2) by the same rule.</para>
    ///
    /// <para><b>The absorption ORDER is OURS (ruling 9) and it is money-neutral.</b> No source prescribes which
    /// invoice's credit a return consumes when the return names no invoice. The order is therefore ours: largest
    /// pool first, then by description, so it is deterministic. It cannot move any recipient's figure, because every
    /// pool of a side is split by the SAME Rule 39(1)(f) turnover ratio over the same attributable recipients — only
    /// the sub-paisa remainder of the largest-remainder split can differ. It is NOT invented law: the total netted
    /// is exact, and only the bookkeeping of which pool shrank is a convention.</para>
    ///
    /// <para><b>Clamped at zero, and the remainder is REPORTED.</b> A return larger than the credit received cannot
    /// be absorbed, and silently flooring it would turn an impossible month into a plausible-looking return. The
    /// excess is returned here so <c>Build</c> can name it in a diagnostic and leave it visible in the
    /// Rule 39(1)(b) row.</para>
    /// </summary>
    private static long NetReturnsAgainstPools(
        List<IsdCreditPool> pools, long cgst, long sgst, long igst, long cess, bool IsEligible)
    {
        if (cgst <= 0 && sgst <= 0 && igst <= 0 && cess <= 0) return 0;

        long remC = Math.Max(0, cgst), remS = Math.Max(0, sgst);
        long remI = Math.Max(0, igst), remX = Math.Max(0, cess);

        var ordered = pools
            .Select((p, i) => (Pool: p, Index: i))
            .Where(x => x.Pool.IsEligible == IsEligible && x.Pool.TotalPaisa > 0)
            .OrderByDescending(x => x.Pool.TotalPaisa)
            .ThenBy(x => x.Pool.Description, StringComparer.Ordinal)
            .ToList();

        foreach (var (_, index) in ordered)
        {
            if (remC <= 0 && remS <= 0 && remI <= 0 && remX <= 0) break;

            var pool = pools[index];
            // Head against its own head, never below zero (the guard IsdDistribution enforces on the way in).
            var takeC = Math.Min(pool.CgstPaisa, remC); remC -= takeC;
            var takeS = Math.Min(pool.SgstPaisa, remS); remS -= takeS;
            var takeI = Math.Min(pool.IgstPaisa, remI); remI -= takeI;
            var takeX = Math.Min(pool.CessPaisa, remX); remX -= takeX;

            pools[index] = pool with
            {
                CgstPaisa = pool.CgstPaisa - takeC,
                SgstPaisa = pool.SgstPaisa - takeS,
                IgstPaisa = pool.IgstPaisa - takeI,
                CessPaisa = pool.CessPaisa - takeX,
            };
        }

        return remC + remS + remI + remX;
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
