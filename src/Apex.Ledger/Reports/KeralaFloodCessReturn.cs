using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>rate slab</b> of the Kerala Flood Cess return (census row 6.26): the GST rate the outward turnover was
/// taxed at, the schedule limb that rate falls in, the cess rate that limb bears, the leviable turnover accumulated
/// at that rate and the cess on it.
/// </summary>
/// <param name="GstRateBasisPoints">The <b>integrated</b> GST rate (1200 = 12%), the axis the Department's own return
/// uses — [KFC-FAQ] Q8, "turnover of outward supply leviable under Kerala Flood Cess <b>based on GST tax rates</b>".</param>
/// <param name="Limb">Which schedule limb the rate falls in (1% or 0.25%). Never
/// <see cref="KfcRateLimb.NotLeviable"/> — a non-leviable slab produces no row at all.</param>
/// <param name="CessRateBasisPoints">The cess rate for the limb: 100 (1%) or 25 (0.25%).</param>
/// <param name="Turnover">The leviable turnover at this GST rate, <b>GST-exclusive</b> ([KFC-FAQ] Q10).</param>
/// <param name="Cess">The Kerala Flood Cess on <paramref name="Turnover"/>, rounded <b>once</b> for the slab.</param>
public sealed record KfcSlabRow(
    int GstRateBasisPoints,
    KfcRateLimb Limb,
    int CessRateBasisPoints,
    Money Turnover,
    Money Cess);

/// <summary>
/// A complete <b>Kerala Flood Cess return</b> projection for a period (census row 6.26) — the turnover of outward
/// supply leviable to the cess, grouped by GST rate, and the cess payable on it. Pure, deterministic and
/// clock-free; built by <see cref="KeralaFloodCessReturn.Build"/> from posted vouchers only.
///
/// <para><b>The shape is the Department's own.</b> [KFC-FAQ] Q8 describes the return in one sentence — "select the
/// return period and enter the details of <b>turnover of outward supply leviable under Kerala Flood Cess based on
/// GST tax rates</b>" — and Q7 fixes the period and due date to GSTR-3B's (the 20th of the succeeding month). This
/// projection produces exactly those figures, so a taxpayer filing for a period inside the levy could read the
/// turnover and cess straight off it.</para>
///
/// <para>🔴 <b>NO FORM NUMBER IS CLAIMED, AND THAT IS DELIBERATE.</b> The return is widely referred to as
/// "Form KFC-A", but the only sources that name it are commercial secondaries; the Kerala GST Department's own FAQ
/// describes the return's CONTENT (Q8) and its DUE DATE (Q7) without naming a form, and the Kerala Flood Cess Rules
/// PDF on <c>keralataxes.gov.in</c> is a scan with no text layer, so its rule and form numbering could not be read.
/// Rather than print a form number this build cannot stand behind, the screen and this type are titled from what the
/// primary source does attest. If the Rules are later read, naming the form is a one-line caption change.</para>
/// </summary>
/// <param name="From">The requested period start.</param>
/// <param name="To">The requested period end.</param>
/// <param name="LevyFrom">The start of the part of the period the levy actually covered, or <c>null</c> when the
/// period lies wholly outside <c>[01-08-2019, 31-07-2021]</c>.</param>
/// <param name="LevyTo">The end of that covered part, or <c>null</c>.</param>
/// <param name="Slabs">The leviable slabs, ordered by GST rate. Empty when nothing is leviable.</param>
/// <param name="TotalTurnover">Σ <see cref="KfcSlabRow.Turnover"/> — the leviable turnover.</param>
/// <param name="TotalCess">Σ <see cref="KfcSlabRow.Cess"/> — the cess payable for the period.</param>
/// <param name="ExemptedBusinessTurnover">🔴 The GST-exclusive outward turnover that was <b>excluded</b> because it
/// went to a Kerala-registered buyer in furtherance of business ([KFC-FAQ] Q12/Q21). Reported rather than silently
/// dropped, because the exclusion is the single largest judgement this projection makes and a filer must be able to
/// see how much turnover it removed.</param>
/// <param name="TurnoverOutsideTheSchedules">🔴 The GST-exclusive intra-Kerala turnover at rates the levy never
/// reached — the 5% and 0.25% slabs, which are in <b>neither</b> limb of [KFC-FAQ] Q5. Shown separately so a reader
/// can see that it was considered and correctly bore nothing, rather than wondering whether it was lost.</param>
public sealed record KeralaFloodCessReturn(
    DateOnly From,
    DateOnly To,
    DateOnly? LevyFrom,
    DateOnly? LevyTo,
    IReadOnlyList<KfcSlabRow> Slabs,
    Money TotalTurnover,
    Money TotalCess,
    Money ExemptedBusinessTurnover,
    Money TurnoverOutsideTheSchedules)
{
    /// <summary>True iff the requested period overlaps the levy window at all. When false every figure is zero and
    /// there is nothing to file — the levy either had not commenced or had already lapsed.</summary>
    public bool PeriodIntersectsTheLevy => LevyFrom is not null && LevyTo is not null;

    /// <summary>True iff the return has nothing on it (no leviable turnover in the period).</summary>
    public bool IsEmpty => Slabs.Count == 0;
}

/// <summary>
/// Builds the <see cref="KeralaFloodCessReturn"/> — a <b>pure, deterministic</b> projection over posted outward
/// vouchers. Every statutory rule it applies is cited in <see cref="KeralaFloodCess"/>, which is where the rates,
/// the window and the leviability predicate live; this type only decides which posted vouchers to feed it and how to
/// aggregate the answers.
/// </summary>
public static class KeralaFloodCessReturnBuilder
{
    /// <summary>
    /// Builds the Kerala Flood Cess return for <c>[from, to]</c>.
    ///
    /// <para><b>What is swept.</b> Posted <b>outward</b> vouchers (Sales and Credit Note — the same set GSTR-1
    /// reads), already filtered for cancelled / optional / provisional / post-dated by
    /// <see cref="GstReportSupport.PostedDirectionalVouchers"/>, and further clipped to the part of the period the
    /// levy actually covered, so a period straddling 31-07-2021 reports only the days before it.</para>
    ///
    /// <para>🔴 <b>A Credit Note SUBTRACTS.</b> The cess is on "turnover of outward supply" ([KFC-FAQ] Q8) and a
    /// credit note reduces that turnover, so a Credit-Note-base voucher contributes its rate groups negatively. This
    /// is arithmetic on the words of Q8, <b>not</b> a sourced statutory rule about credit notes — the FAQ is silent
    /// on them — and it is recorded as such. The alternative readings are both worse: adding a credit note would
    /// inflate turnover by twice the return, and dropping it would leave a return that never reflects a sales
    /// return at all.</para>
    ///
    /// <para><b>What is excluded, and why each.</b>
    /// <list type="bullet">
    ///   <item>Everything, when the supplier is not registered in Kerala or has opted for composition
    ///     ([KFC-FAQ] Q11/Q14/Q21) — the return is then empty by construction.</item>
    ///   <item>An <b>inter-State</b> supply (Q11), read off the tax the voucher POSTED
    ///     (<see cref="GstReportSupport.PostedForwardRouting"/>) rather than off the party's live, editable State
    ///     master — the same rule the rest of this product's return projections use, so a party re-stated after the
    ///     document was issued cannot retrospectively move a filed figure.</item>
    ///   <item>A supply to a buyer holding a <b>Kerala</b> GST registration (Q12/Q21) — reported in
    ///     <see cref="KeralaFloodCessReturn.ExemptedBusinessTurnover"/>, not silently dropped.</item>
    ///   <item>An exempt / nil / zero-rated supply (Q13) — such a voucher posts no forward tax leg, so it yields no
    ///     rate group and contributes nothing without needing a rule of its own.</item>
    /// </list></para>
    ///
    /// <para>🔴 <b>THE "IN FURTHERANCE OF BUSINESS" READING, STATED HERE AND ON THE SCREEN.</b> [KFC-FAQ] Q12 exempts
    /// a supply to a Kerala-registered taxable person only where it is made in furtherance of business, and Q18/Q19
    /// levy the cess where it is not (the Department's own example is a motor vehicle bought for the buyer's own
    /// use). <b>No master or voucher in this application records that fact</b>, so this projection takes the ordinary
    /// reading — a registered Kerala buyer is buying for its business — which is the rule in Q12 rather than its
    /// exception. Persisting the exception needs a column on the voucher, i.e. a schema migration, which this work
    /// does not have; inventing a silent default in either direction would move money, so the reading is made
    /// explicit, its effect is reported as a separate figure, and the screen says so in words.</para>
    /// </summary>
    public static KeralaFloodCessReturn Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var empty = new KeralaFloodCessReturn(
            from, to, null, null, [], Money.Zero, Money.Zero, Money.Zero, Money.Zero);

        // The part of the requested period the levy actually covered. No overlap ⇒ nothing to file.
        var levyFrom = from > KeralaFloodCess.Commencement ? from : KeralaFloodCess.Commencement;
        var levyTo = to < KeralaFloodCess.Cessation ? to : KeralaFloodCess.Cessation;
        if (levyFrom > levyTo) return empty;

        // Supplier-side gates ([KFC-FAQ] Q11/Q14/Q21). A non-Kerala or composition supplier files nothing, and the
        // return is empty rather than zero-filled — there is no such return for them to file.
        if (!IsKeralaRegisteredSupplier(company)) return empty with { LevyFrom = levyFrom, LevyTo = levyTo };
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return empty with { LevyFrom = levyFrom, LevyTo = levyTo };

        // Accumulate BEFORE rounding, so each slab is rounded once (the boundary GstService's cess already uses).
        var turnoverByRate = new Dictionary<int, decimal>();
        var exemptedBusiness = 0m;
        var outsideSchedules = 0m;

        foreach (var (voucher, type) in
                 GstReportSupport.PostedDirectionalVouchers(company, levyFrom, levyTo, GstTaxDirection.Output))
        {
            // A Credit Note reduces the outward turnover it relates to; see the remarks above.
            var sign = type.BaseType == VoucherBaseType.CreditNote ? -1m : 1m;

            // Q11 — intra-State only, read off the tax the voucher POSTED. `null` means the voucher posted no
            // forward tax at all (exempt / nil / zero-rated), which Q13 excludes anyway.
            if (GstReportSupport.PostedForwardRouting(voucher) is not false) continue;

            var groups = GstReportSupport.ReadPostedRateGroups(voucher);
            if (groups.Count == 0) continue;

            // Q12/Q21 — a buyer holding a Kerala GST registration, buying in furtherance of business.
            var party = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
            if (IsKeralaRegisteredBuyer(party))
            {
                foreach (var g in groups) exemptedBusiness += sign * g.Taxable;
                continue;
            }

            foreach (var g in groups)
            {
                if (KeralaFloodCess.LimbFor(g.Rate) == KfcRateLimb.NotLeviable)
                {
                    outsideSchedules += sign * g.Taxable;
                    continue;
                }
                turnoverByRate[g.Rate] = (turnoverByRate.TryGetValue(g.Rate, out var cur) ? cur : 0m)
                                         + sign * g.Taxable;
            }
        }

        var slabs = new List<KfcSlabRow>();
        var totalTurnover = 0m;
        var totalCess = 0m;
        foreach (var rate in turnoverByRate.Keys.OrderBy(r => r))
        {
            var turnover = turnoverByRate[rate];
            if (turnover == 0m) continue;    // a sale fully reversed by a credit note is not a slab of the return

            var limb = KeralaFloodCess.LimbFor(rate);
            // The cess is computed on the SLAB total and rounded once. `levyFrom` is inside the window by
            // construction (the guard above), so the rate is the schedule's — the window is still consulted, never
            // bypassed.
            var cess = new Money(KeralaFloodCess.CessBeforeRounding(new Money(turnover), levyFrom, rate))
                .RoundToPaisa();
            slabs.Add(new KfcSlabRow(rate, limb, KeralaFloodCess.RateBasisPointsOf(limb), new Money(turnover), cess));
            totalTurnover += turnover;
            totalCess += cess.Amount;
        }

        return new KeralaFloodCessReturn(
            from, to, levyFrom, levyTo, slabs,
            new Money(totalTurnover), new Money(totalCess),
            new Money(exemptedBusiness), new Money(outsideSchedules));
    }

    /// <summary>True iff the company holds a Kerala GST registration — its GST home State is Kerala (code 32).</summary>
    public static bool IsKeralaRegisteredSupplier(Company company) =>
        company.Gst is { Enabled: true } gst
        && string.Equals(gst.HomeStateCode, KeralaFloodCess.KeralaStateCode, StringComparison.Ordinal);

    /// <summary>
    /// True iff <paramref name="party"/> is a taxable person holding a GST registration <b>in Kerala</b> —
    /// [KFC-FAQ] Q21, "Exemption is eligible only for registered taxable person having GST registration in Kerala
    /// GST."
    ///
    /// <para>🔴 <b>The Kerala-ness is read off the GSTIN's own first two digits, not off the party's State field.</b>
    /// Those two are different facts: <see cref="PartyGstDetails.StateCode"/> is the place-of-supply driver (where
    /// goods go), while the exemption in Q21 turns on <b>where the registration is</b>, which is exactly what the
    /// GSTIN's leading State code records. They normally agree; where they do not, Q21 names the registration. The
    /// State field is used only as a fall-back for a party carrying a registration type but no GSTIN string.</para>
    /// </summary>
    public static bool IsKeralaRegisteredBuyer(Domain.Ledger? party)
    {
        if (party?.PartyGst is not { } pg || pg.IsB2C) return false;   // unregistered / consumer ⇒ Q19, leviable
        var gstin = pg.Gstin;
        var registrationState = gstin is { Length: >= 2 } ? gstin[..2] : pg.StateCode;
        return string.Equals(registrationState, KeralaFloodCess.KeralaStateCode, StringComparison.Ordinal);
    }
}
