using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// A <b>recipient of credit</b> in an ISD distribution — one of the distributor's sibling registrations.
/// <para>The <b>Explanation to Rule 39</b> of the CGST Rules, clause (b), defines it: "<i>the expression 'recipient
/// of credit' means the supplier of goods or services or both having the same Permanent Account Number as that of
/// the Input Service Distributor</i>" (<see cref="IsdDistribution"/> carries the source and the in-force note).</para>
/// </summary>
/// <param name="RegistrationId">The recipient registration's id (<c>GstRegistration.Id</c>).</param>
/// <param name="Name">The registration name, for the statement and the ISD invoice.</param>
/// <param name="StateCode">The recipient's 2-digit GST State/UT code — this alone decides the head conversion
/// under Rule 39(1)(j), by comparison with the distributor's State.</param>
/// <param name="Gstin">The recipient's GSTIN, printed on the ISD invoice; may be <c>null</c> only for a recipient
/// that is genuinely unregistered, which Rule 39(1)(f) expressly contemplates — it provides for recipients
/// "<i>who are engaged in making exempt supply, or are otherwise not registered for any reason</i>".</param>
/// <param name="TurnoverPaisa"><b>t1</b> — "<i>the turnover of person R1 during the relevant period</i>"
/// (Rule 39(1)(f)). Integer paisa, ≥ 0.</param>
/// <param name="IsOperationalInCurrentYear">Rule 39(1)(d)/(e) restrict the denominator to recipients "<i>which are
/// operational in the current year</i>"; a recipient that is not is excluded from both t1 and T.</param>
public sealed record IsdRecipient(
    Guid RegistrationId,
    string Name,
    string StateCode,
    string? Gstin,
    long TurnoverPaisa,
    bool IsOperationalInCurrentYear = true);

/// <summary>
/// One pool of credit an Input Service Distributor has to distribute in the month — the per-head input tax on a
/// common input-service invoice (or on a group of them that share an attribution).
///
/// <para><b>Why "pool" and not "invoice".</b> Rule 39(1)(f) runs its formula over "<i>the aggregate of the turnover
/// during the relevant period of all recipients to whom the input service is attributable</i>" — so the unit the
/// formula divides is a body of credit
/// that shares one attribution — not necessarily one document. Two invoices attributable to the same set of units
/// distribute identically whether pooled or run separately, up to rounding; a pool per attribution is the smaller
/// and more faithful unit.</para>
/// </summary>
/// <param name="Description">A label for the statement row (e.g. the supplier / service).</param>
/// <param name="CgstPaisa">Central tax available for distribution, integer paisa ≥ 0.</param>
/// <param name="SgstPaisa">State/UT tax available for distribution, integer paisa ≥ 0.</param>
/// <param name="IgstPaisa">Integrated tax available for distribution, integer paisa ≥ 0.</param>
/// <param name="CessPaisa">Compensation cess available for distribution, integer paisa ≥ 0. See
/// <see cref="IsdDistribution"/> for why cess is carried through un-converted and labelled as ours.</param>
/// <param name="IsEligible">Rule 39(1)(g): the ISD "<i>shall, in accordance with the provisions of clause (d) and
/// (e), separately distribute the amount of ineligible input tax credit (ineligible under the provisions of
/// sub-section (5) of section 17 or otherwise) and the amount of eligible input tax credit</i>". The two never
/// merge into one statement row.</param>
/// <param name="AttributableTo">Rule 39(1)(c): "<i>the credit of tax paid on input services attributable to a
/// recipient of credit shall be distributed only to that recipient</i>". A non-empty set restricts the distribution
/// to those recipients; <c>null</c> or an empty set means the credit is attributable to all of them
/// (Rule 39(1)(e)).</param>
public sealed record IsdCreditPool(
    string Description,
    long CgstPaisa,
    long SgstPaisa,
    long IgstPaisa,
    long CessPaisa,
    bool IsEligible = true,
    IReadOnlyList<Guid>? AttributableTo = null)
{
    /// <summary>Σ the four heads, in paisa — the "<b>C</b>" of Rule 39(1)(f) taken across heads.</summary>
    public long TotalPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;
}

/// <summary>One distributed line — what a single recipient receives out of one pool, AFTER the Rule 39(1)(i)/(j)
/// head conversion.</summary>
public sealed record IsdDistributionLine(
    Guid RecipientId,
    string RecipientName,
    string StateCode,
    string? Gstin,
    bool IsEligible,
    long CgstPaisa,
    long SgstPaisa,
    long IgstPaisa,
    long CessPaisa)
{
    /// <summary>Σ the four heads for this line, in paisa.</summary>
    public long TotalPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;
}

/// <summary>The outcome of a distribution: the per-recipient lines plus any pool that could not be distributed,
/// each named rather than silently dropped.</summary>
public sealed record IsdDistributionResult(
    IReadOnlyList<IsdDistributionLine> Lines,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Σ across every line, in paisa — what actually left the distributor.</summary>
    public long DistributedPaisa => Lines.Sum(l => l.TotalPaisa);

    /// <summary>True when nothing was withheld for want of a denominator or a recipient.</summary>
    public bool IsComplete => Diagnostics.Count == 0;
}

/// <summary>
/// <b>The Input Service Distributor credit-distribution engine</b> (census row 6.24) — <b>Rule 39 of the CGST
/// Rules</b>, applied to integer paisa. Pure and total: no clock, no randomness, no I/O, no <c>Company</c>.
/// Everything it needs is in its arguments, which is what lets the CBIC worked example be run against it verbatim
/// as a test.
///
/// <para>🔴 <b>WHICH TEXT IS IN FORCE, AND WHY THIS FILE CITES RULE 39 AND NOT §20. READ BEFORE "CORRECTING" A
/// CLAUSE LETTER BACK.</b> Both the section and the rule were replaced with effect from <b>1 April 2025</b>:</para>
/// <list type="bullet">
///   <item><b>CGST Act §20</b> was <b>substituted</b> by s. 12 of the <b>Finance (No. 8) Act, 2024</b>, w.e.f.
///   01.04.2025. The substituted section has <b>three sub-sections and no Explanation</b>. <b>§20(2)(a)–(e) and the
///   §20 Explanation no longer exist</b> — the pro-rata clauses, the "operational in the current year" restriction
///   and the definitions of <i>relevant period</i> and <i>recipient of credit</i> that used to sit there now live
///   wholly in Rule 39. §20(2) delegates: the ISD distributes "<i>in such manner, within such time and subject to
///   such restrictions and conditions as may be prescribed</i>".</item>
///   <item><b>Rule 39</b> was substituted by <b>Notification 12/2024-CT dated 10.07.2024</b>, brought into force on
///   <b>01.04.2025</b> by <b>Notification 09/2025-CT dated 11.02.2025</b>. <b>The clause letters moved</b>: the
///   pro-rata formula is now <b>(f)</b> (it was (d)); integrated tax is <b>(i)</b> (it was (e)); the central/State
///   head conversion is <b>(j)</b> (it was (f)); the eligible/ineligible split is <b>(g)</b> (it was (b)); the ISD
///   invoice and credit note are <b>(k)</b> and <b>(l)</b> (they were (g) and (h)).</item>
/// </list>
/// <para>Every date this app distributes for is on or after 01.04.2025, so the substituted text is the operative
/// one and the pre-substitution clause letters would not resolve by content. Source, checked by content:
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>
/// and <c>…/gst/acts/2017_CGST_act/active/chapter5/section20_v1.00.html</c>.</para>
///
/// <para><b>THE RULES IT IMPLEMENTS, EACH QUOTED FROM cbic.gov.in's own rule repository.</b></para>
/// <list type="number">
/// <item><b>Rule 39(1)(b) — the footing.</b> "<i>the amount of the credit distributed shall not exceed the amount of
/// credit available for distribution</i>". This engine is stricter than "not exceed": when a pool distributes at
/// all, the distributed total equals the available total to the paisa. The last recipient in input order takes the
/// arithmetic remainder of each head, the same convention <see cref="ProRata"/> documents for every other posted
/// group total in this app. A distribution that footed to anything else would be wrong money on a filed return.</item>
/// <item><b>Rule 39(1)(c) — direct attribution.</b> "<i>the credit of tax paid on input services attributable to a
/// recipient of credit shall be distributed only to that recipient</i>" — <see cref="IsdCreditPool.AttributableTo"/>.</item>
/// <item><b>Rule 39(1)(d)/(e) + (f) — the pro-rata formula.</b> (d) governs credit attributable to more than one
/// recipient and (e) credit attributable to all of them; both distribute "<i>pro rata on the basis of the turnover
/// in a State or turnover in a Union territory of such recipient, during the relevant period, to the aggregate of
/// the turnover of all … recipients … <b>and which are operational in the current year</b>, during the said
/// relevant period</i>". (f) states the arithmetic: "<i>C1 = (t1 ÷ T) × C</i>", where C is "<i>the amount of credit
/// to be distributed</i>", t1 "<i>the turnover of person R1 during the relevant period</i>" and T "<i>the aggregate
/// of the turnover during the relevant period of all recipients to whom the input service is attributable</i>".</item>
/// <item><b>Rule 39(1)(i) — integrated tax.</b> "<i>the input tax credit on account of integrated tax shall be
/// distributed as input tax credit of integrated tax to every recipient</i>".</item>
/// <item><b>Rule 39(1)(j) — the head conversion.</b> Central and State/UT tax go out "<i>(i) in respect of a
/// recipient located in the same State or Union territory in which the Input Service Distributor is located, be
/// distributed as input tax credit of central tax and State tax or Union territory tax respectively</i>"; and
/// "<i>(ii) in respect of a recipient located in a State or Union territory other than that of the Input Service
/// Distributor, be distributed as integrated tax and the amount to be so distributed shall be equal to the
/// aggregate of the amount of input tax credit of central tax and State tax or Union territory tax that qualifies
/// for distribution to such recipient as referred to in clause (d) and (e)</i>". Note what this does and does not
/// preserve: the head MIX changes, the TOTAL does not.</item>
/// <item><b>Rule 39(1)(g) — eligible and ineligible separately.</b> Carried on the pool and never merged.</item>
/// </list>
///
/// <para>🔴 <b>COMPENSATION CESS IS OUR LABELLED DIVERGENCE, NOT A CLONED RULE.</b> Rule 39(1)(h)/(i)/(j) enumerate
/// central tax, State tax, Union territory tax and integrated tax, and say nothing about compensation cess. No
/// retrievable official source states how — or whether — an ISD converts a cess head on distribution. Two wrong
/// answers were available: silently dropping the cess (which loses money off a filed return) or inventing an
/// IGST-style conversion for it (which invents law). This engine does neither: cess is distributed pro-rata by the
/// same t1÷T ratio and carried through as <b>cess</b>, un-converted, matching this codebase's standing ER-2 rule
/// that cess is ring-fenced from the three tax heads. It is recorded here as ours so a reader can find it.</para>
///
/// <para>🔴 <b>WHAT THIS ENGINE DELIBERATELY DOES NOT DO.</b> It does not post. Nothing here writes a voucher,
/// touches an electronic credit ledger or issues a document. Rule 39(1)(k) requires an ISD invoice "<i>as provided
/// in sub-rule (1) of rule 54</i>" and Rule 39(1)(l) an ISD credit note, and Rule 39(1)(m)/(n) govern later
/// debit/credit notes against the distributor — all of those
/// need a persisted document identity, and none of them is built here. The distribution is a recomputed projection,
/// exactly as CMP-08, GSTR-4 and GSTR-9 are in this app.</para>
/// </summary>
public static class IsdDistribution
{
    /// <summary>
    /// Distributes <paramref name="pools"/> from an ISD located in <paramref name="isdStateCode"/> across
    /// <paramref name="recipients"/>, per Rule 39(1)(d)/(e)/(f)/(i)/(j). Returns one line per (recipient, eligibility)
    /// that received anything, in recipient order, plus a diagnostic for every pool that could not be distributed.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The ISD State code is not a valid Indian State/UT code, a recipient has
    /// a negative turnover, or a pool carries a negative head. A negative head is refused rather than netted: a
    /// reduction of already-distributed credit is an ISD credit note under Rule 39(1)(l)/(n), which is a different
    /// document with its own apportionment rule and is not built here.</exception>
    public static IsdDistributionResult Distribute(
        string isdStateCode,
        IReadOnlyList<IsdRecipient> recipients,
        IReadOnlyList<IsdCreditPool> pools)
    {
        ArgumentNullException.ThrowIfNull(isdStateCode);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(pools);

        if (!IndianState.IsValidCode(isdStateCode))
            throw new ArgumentException(
                $"The Input Service Distributor's State code '{isdStateCode}' is not a valid Indian State/UT code.",
                nameof(isdStateCode));

        foreach (var r in recipients)
        {
            if (r.TurnoverPaisa < 0)
                throw new ArgumentException(
                    $"Recipient '{r.Name}' has a negative turnover; t1 in Rule 39(1)(f) cannot be negative.",
                    nameof(recipients));
        }

        foreach (var p in pools)
        {
            if (p.CgstPaisa < 0 || p.SgstPaisa < 0 || p.IgstPaisa < 0 || p.CessPaisa < 0)
                throw new ArgumentException(
                    $"Credit pool '{p.Description}' carries a negative head. Reducing credit already distributed is "
                    + "an ISD credit note (Rule 39(1)(l)/(n)), which is a separate document and is not distributed here.",
                    nameof(pools));
        }

        // Accumulate per (recipient, eligibility) so two pools with the same attribution fold into one statement row
        // while the Rule 39(1)(g) eligible/ineligible split is preserved.
        var acc = new Dictionary<(Guid Recipient, bool Eligible), Head>();
        var diagnostics = new List<string>();

        foreach (var pool in pools)
        {
            if (pool.TotalPaisa == 0) continue; // nothing to distribute; not a diagnostic

            var targets = TargetsFor(pool, recipients);

            if (targets.Count == 0)
            {
                diagnostics.Add(
                    $"'{pool.Description}': no operational recipient of credit is attributable, so "
                    + $"{Rupees(pool.TotalPaisa)} was NOT distributed. Rule 39(1)(d)/(e) restrict the distribution to "
                    + "recipients which are operational in the current year.");
                continue;
            }

            var t = targets.Sum(r => r.TurnoverPaisa);
            if (t == 0)
            {
                // Rule 39(1)(f)'s formula divides by T. With T = 0 it has no value, and no retrievable official
                // source supplies a fallback (an equal split would be invented law). Withhold and say so, loudly:
                // an undistributed pool is visible and fixable, a silently invented split is wrong money on GSTR-6.
                diagnostics.Add(
                    $"'{pool.Description}': the aggregate turnover T of the attributable recipients is zero for the "
                    + $"relevant period, so Rule 39(1)(f)'s C1 = (t1 ÷ T) × C has no value and "
                    + $"{Rupees(pool.TotalPaisa)} was NOT distributed. Record the recipients' turnover for the "
                    + "relevant period (Explanation (a) to Rule 39) and rebuild.");
                continue;
            }

            // Split each head pro rata, last target absorbing the remainder so the pool foots exactly (R39(1)(b)).
            var cgst = Split(pool.CgstPaisa, targets, t);
            var sgst = Split(pool.SgstPaisa, targets, t);
            var igst = Split(pool.IgstPaisa, targets, t);
            var cess = Split(pool.CessPaisa, targets, t);

            for (var i = 0; i < targets.Count; i++)
            {
                var recipient = targets[i];
                var converted = ConvertHeads(isdStateCode, recipient.StateCode, cgst[i], sgst[i], igst[i], cess[i]);

                var key = (recipient.RegistrationId, pool.IsEligible);
                acc[key] = acc.TryGetValue(key, out var running) ? running + converted : converted;
            }
        }

        // Emit in recipient order, eligible before ineligible, so the statement is stable across runs.
        var lines = new List<IsdDistributionLine>();
        foreach (var recipient in recipients)
        {
            foreach (var eligible in new[] { true, false })
            {
                if (!acc.TryGetValue((recipient.RegistrationId, eligible), out var h) || h.IsZero) continue;
                lines.Add(new IsdDistributionLine(
                    recipient.RegistrationId, recipient.Name, recipient.StateCode, recipient.Gstin,
                    eligible, h.Cgst, h.Sgst, h.Igst, h.Cess));
            }
        }

        return new IsdDistributionResult(lines, diagnostics);
    }

    /// <summary>
    /// The recipients one pool distributes to: its attribution set when it has one (Rule 39(1)(c)), otherwise every
    /// recipient (Rule 39(1)(e)); in both cases restricted to those "<i>operational in the current year</i>"
    /// (Rule 39(1)(d)/(e)). Input order is preserved, because the last target absorbs the rounding remainder.
    /// </summary>
    private static List<IsdRecipient> TargetsFor(IsdCreditPool pool, IReadOnlyList<IsdRecipient> recipients)
    {
        var attributable = pool.AttributableTo;
        return recipients
            .Where(r => r.IsOperationalInCurrentYear)
            .Where(r => attributable is null || attributable.Count == 0 || attributable.Contains(r.RegistrationId))
            .ToList();
    }

    /// <summary>
    /// Rule 39(1)(f): <c>C1 = (t1 ÷ T) × C</c> for every target, with the LAST target taking
    /// <c>C − Σ(the others)</c> so the split foots to <paramref name="credit"/> exactly (Rule 39(1)(b)). This mirrors
    /// the convention <see cref="ProRata"/> documents for every other apportioned group total in this app.
    /// </summary>
    private static long[] Split(long credit, List<IsdRecipient> targets, long t)
    {
        var shares = new long[targets.Count];
        if (credit == 0) return shares;

        long assigned = 0;
        for (var i = 0; i < targets.Count - 1; i++)
        {
            shares[i] = ProRata.Paisa(credit, targets[i].TurnoverPaisa, t);
            assigned += shares[i];
        }
        shares[^1] = credit - assigned;
        return shares;
    }

    /// <summary>
    /// Rule 39(1)(i)/(j). Integrated tax stays integrated tax for every recipient. Central + State/UT tax stay as
    /// they are for a recipient in the distributor's own State/UT, and become integrated tax — "<i>equal to the
    /// aggregate</i>" of the two — for a recipient anywhere else. Cess passes through un-converted (our labelled
    /// divergence; see the type remarks). The total is unchanged by this step in every branch.
    /// </summary>
    private static Head ConvertHeads(string isdStateCode, string recipientStateCode,
        long cgst, long sgst, long igst, long cess)
        => string.Equals(isdStateCode, recipientStateCode, StringComparison.Ordinal)
            ? new Head(cgst, sgst, igst, cess)
            : new Head(0, 0, igst + cgst + sgst, cess);

    /// <summary>
    /// The rupee figure inside a diagnostic, grouped through the <b>one</b> home for rupee formatting.
    ///
    /// <para>🔴 <b>NOT an interpolated <c>0.00</c>, and the difference is not cosmetic.</b> An interpolated format
    /// binds to <c>CurrentCulture</c>, so the withheld-credit figure in these diagnostics would render with a comma
    /// decimal separator on a de-DE host and with Western thousands grouping everywhere — and every withheld ISD
    /// pool is a lakh-scale figure, which is exactly the shape this app has already shipped wrong once. Going
    /// through <see cref="IndianMoneyFormat.Amount(decimal)"/> makes it 1,00,000.00 on ubuntu, macOS and Windows
    /// alike, and makes it obey the Millions grouping switch instead of being deaf to it.</para>
    /// </summary>
    private static string Rupees(long paisa) => $"₹{IndianMoneyFormat.Amount(paisa / 100m)}";

    /// <summary>A per-head paisa tuple, summable.</summary>
    private readonly record struct Head(long Cgst, long Sgst, long Igst, long Cess)
    {
        public bool IsZero => Cgst == 0 && Sgst == 0 && Igst == 0 && Cess == 0;

        public static Head operator +(Head a, Head b)
            => new(a.Cgst + b.Cgst, a.Sgst + b.Sgst, a.Igst + b.Igst, a.Cess + b.Cess);
    }
}
