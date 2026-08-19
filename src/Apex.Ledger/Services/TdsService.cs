using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The TDS <b>compute + auto-deduct</b> engine (catalog §13; Phase 7 slice 2). Framework-, DB-, clock- and
/// RNG-free: a pure, deterministic mutation-free computation over the <see cref="Company"/> aggregate, exactly like
/// <see cref="GstService"/> — but <b>withholding, not additive-on-top</b>. Where GST adds tax to the party leg, TDS
/// <b>carves it out</b>: on a Journal / Payment / Purchase / expense voucher where an expense ledger is <i>Is TDS
/// Applicable</i> and the party is a deductee, the deductor books
/// <list type="bullet">
///   <item><c>Dr Expense/Purchase = GROSS</c>,</item>
///   <item><c>Cr Party = NET</c> (= GROSS − TDS, <b>derived</b> — never gross×(1−rate)),</item>
///   <item><c>Cr "TDS Payable"</c> (a Duties &amp; Taxes liability) <c>= TDS</c>.</item>
/// </list>
/// so <c>GROSS Dr == NET Cr + TDS Cr</c> to the paisa <b>by construction</b> (the balance invariant is the guard —
/// a leaky independently-computed net trips <see cref="VoucherValidator"/>). The TDS Payable ledger sits under
/// Duties &amp; Taxes, so <c>ClassificationRules.IsDutiesAndTaxesLedger</c> excludes it from the item-invoice
/// pairing sum, exactly like the GST tax ledgers — the carve-out foots without changing that invariant.
/// <para>
/// <see cref="ComputeWithholding"/> resolves the rate (PAN ⇒ with-PAN rate; no PAN ⇒ the nature's §206AA no-PAN
/// rate, which the seed sets to 20% generally and 5% for §194Q), applies the section threshold (single-transaction
/// and cumulative-FY, the latter a <b>pure projection</b> over prior posted vouchers per party×nature — like
/// <c>Gstr1</c> YTD accumulation, deterministic with no clock/order side-effects), and applies income-tax
/// <b>nearest-rupee, round-half-up</b> rounding (per A14). TDS is assessed on the <b>GST-exclusive</b> base
/// (Circular 23/2017): the caller passes the assessable value separately from the party's gross obligation.
/// </para>
/// </summary>
public sealed class TdsService
{
    private readonly Company _company;

    public TdsService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ---- rate resolution + threshold + rounding (pure) ----

    /// <summary>The outcome of assessing a payment for TDS (pure; no posting).</summary>
    /// <param name="Applies">True iff the section threshold is crossed so TDS must be withheld.</param>
    /// <param name="AssessableValue">The GST-exclusive base the TDS is (or would be) computed on.</param>
    /// <param name="RateBasisPoints">The resolved rate in basis points (with-PAN, or the no-PAN §206AA/§194Q rate).</param>
    /// <param name="TdsAmount">The TDS withheld (nearest rupee, round-half-up); <see cref="Money.Zero"/> when below threshold.</param>
    /// <param name="PanApplied">True iff the deductee PAN was present+valid so the with-PAN rate applied.</param>
    /// <param name="PriorCumulativeInFy">Σ prior-posted assessable for this party×nature in the FY (the projection).</param>
    public readonly record struct Withholding(
        bool Applies, Money AssessableValue, int RateBasisPoints, Money TdsAmount, bool PanApplied,
        Money PriorCumulativeInFy);

    /// <summary>
    /// Assesses a payment of <paramref name="assessableValue"/> (the GST-exclusive base) to
    /// <paramref name="deductee"/> under <paramref name="nature"/> dated <paramref name="date"/>: resolves the rate
    /// (PAN ⇒ <see cref="NatureOfPayment.RateWithPanBp"/>; no PAN ⇒ <see cref="NatureOfPayment.RateWithoutPanBp"/> —
    /// the §206AA 20% general / §194Q 5% special the seed encodes), tests the section threshold (single-transaction
    /// OR cumulative-FY, the cumulative a pure projection over prior posted vouchers), and — when crossed — computes
    /// the TDS as <c>round_half_up(assessable × rate / 10000)</c> to the <b>nearest rupee</b>. Pure and total; posts
    /// nothing.
    /// </summary>
    /// <param name="asPostedBefore">
    /// 🔴 <b>Project the book as it stood immediately BEFORE this voucher was posted</b> — Phase 10.11 S5c.
    /// <c>null</c> for a fresh posting (the voucher is not in the book yet, so the whole book is prior, and every
    /// pre-S5c caller is byte-identical). On an ALTERATION the voucher IS already in <c>Company.Vouchers</c>
    /// carrying its own <see cref="TdsLineTax"/>, and so is everything posted after it.
    /// See <see cref="ProjectPriorCumulative"/> for why excluding the voucher's own id is not enough.
    /// </param>
    public Withholding ComputeWithholding(
        Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date,
        Guid? asPostedBefore = null)
    {
        ArgumentNullException.ThrowIfNull(nature);
        ArgumentNullException.ThrowIfNull(deductee);
        if (assessableValue.Amount < 0m)
            throw new ArgumentException("Assessable value must be ≥ 0.", nameof(assessableValue));
        if (!assessableValue.IsPaisaExact)
            throw new InvalidOperationException($"Assessable value {assessableValue} must be paisa-exact.");

        var panApplied = Pan.IsValid(deductee.PartyPan);
        var rateBp = panApplied ? nature.RateWithPanBp : nature.RateWithoutPanBp;

        var prior = ProjectPriorCumulative(deductee.Id, nature.Id, date, asPostedBefore);
        if (!ThresholdCrossed(nature, assessableValue, prior))
            return new Withholding(false, assessableValue, rateBp, Money.Zero, panApplied, prior);

        var tds = NearestRupee(assessableValue.Amount * rateBp / 10_000m);
        return new Withholding(true, assessableValue, rateBp, tds, panApplied, prior);
    }

    /// <summary>Rounds a raw amount to the nearest whole rupee, <b>round-half-up</b> (away-from-zero) — the
    /// income-tax TDS/TCS rounding rule (A14). A positive raw amount's away-from-zero is exactly half-up.</summary>
    public static Money NearestRupee(decimal raw) => Money.FromRupees(Math.Round(raw, 0, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Whether the section threshold is crossed so TDS must be withheld: a nature with <b>no</b> threshold always
    /// applies; otherwise TDS applies iff the current transaction <b>exceeds</b> the single-transaction threshold
    /// (§194C ₹30,000) OR the FY aggregate (<paramref name="prior"/> + current) <b>exceeds</b> the cumulative
    /// threshold (§194J ₹50,000). "Exceeds" is strict (at exactly the threshold ⇒ no TDS, per the bare Act wording).
    /// </summary>
    private static bool ThresholdCrossed(NatureOfPayment nature, Money current, Money prior)
    {
        if (nature.SingleTransactionThreshold is null && nature.CumulativeThreshold is null) return true;
        var single = nature.SingleTransactionThreshold is { } st && current > st;
        var cumulative = nature.CumulativeThreshold is { } ct && (prior + current) > ct;
        return single || cumulative;
    }

    // ---- cumulative-FY threshold projection (pure, like Gstr1 YTD) ----

    /// <summary>
    /// Σ of the assessable value already posted for (<paramref name="deducteeLedgerId"/>,
    /// <paramref name="natureId"/>) in the financial year of <paramref name="date"/>, up to and including that date
    /// — a <b>pure projection</b> over the company's non-cancelled posted vouchers, reading each line's
    /// <see cref="TdsLineTax.AssessableValue"/> (present on every TDS-assessed transaction, deducted or below
    /// threshold). Deterministic and order-independent for a fixed voucher set; the not-yet-posted current
    /// transaction is naturally excluded. Mirrors how <c>Gstr1</c> accumulates posted <see cref="GstLineTax"/>.
    /// </summary>
    /// <param name="asPostedBefore">
    /// 🔴 <b>The voucher whose POSTING MOMENT the projection is taken at</b> — Phase 10.11 S5c. The
    /// projection is then over the vouchers that were in the book when that voucher was posted, i.e. everything
    /// BEFORE it in <c>Company.Vouchers</c> list order (which is posting order, and which
    /// <c>LedgerService.Replace</c> deliberately preserves). <c>null</c> — or an id that is not in the book, which
    /// is the same thing — projects over the whole book, exactly as a fresh posting does.
    ///
    /// <para>🔴 <b>Why excluding the voucher's OWN id is not enough, and shipped as a defect that moved real
    /// money.</b> An earlier form of this argument removed only the named voucher. But the loop selects by DATE, so
    /// a sibling posted LATER and dated on or before the voucher still counted as "prior" although it was not in
    /// the book at posting. Measured on §194J(b) (₹50,000 cumulative, no single-transaction threshold): two
    /// same-dated ₹30,000.30 journals, then a NARRATION-ONLY alteration of the FIRST moved it from 2 lines /
    /// party ₹30,000.30 to 3 lines / party ₹27,000.30 / TDS Payable ₹3,000.00 — a statutory liability created by
    /// editing a narration. The reachable window was "posted later, dated on or before", i.e. every same-day batch
    /// and every back-dated correction. Taking the window at the POSTING MOMENT reproduces the posting-time set
    /// exactly, with no schema change.</para>
    /// </param>
    public Money ProjectPriorCumulative(
        Guid deducteeLedgerId, Guid natureId, DateOnly date, Guid? asPostedBefore = null)
    {
        var (fyStart, _) = FinancialYearOf(date);
        var vouchers = _company.Vouchers;

        // The posting moment: everything before this voucher in list order. Not found (or null) ⇒ the whole book,
        // which is what a voucher not yet in the book sees.
        var limit = vouchers.Count;
        if (asPostedBefore is { } marker)
            for (var i = 0; i < vouchers.Count; i++)
                if (vouchers[i].Id == marker) { limit = i; break; }

        var sum = 0m;
        for (var i = 0; i < limit; i++)
        {
            var v = vouchers[i];
            if (v.Cancelled) continue;
            if (v.Date < fyStart || v.Date > date) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t) continue;
                if (t.DeducteeLedgerId != deducteeLedgerId || t.NatureId != natureId) continue;
                sum += t.AssessableValue.Amount;
            }
        }
        return new Money(sum);
    }

    /// <summary>The Indian financial year (1 April – 31 March) containing <paramref name="date"/>.</summary>
    public static (DateOnly Start, DateOnly End) FinancialYearOf(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        var start = new DateOnly(startYear, 4, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }

    // ---- withholding carve-out (assemble the party-net + TDS-payable legs) ----

    /// <summary>
    /// The carve-out legs for a withholding voucher (Phase 7 slice 2). The <see cref="PartyLine"/> credits the
    /// deductee the <see cref="NetPartyAmount"/> (= <c>partyGrossObligation − TDS</c>, <b>derived</b>); when TDS
    /// applies the <see cref="TdsPayableLine"/> credits "TDS Payable" the withheld amount and carries the
    /// <see cref="TdsLineTax"/> detail. The caller books the expense/purchase debit at the gross and appends these.
    /// </summary>
    /// <param name="Withholding">The computed rate/threshold outcome.</param>
    /// <param name="NetPartyAmount">The party's net credit = gross obligation − TDS (= gross when TDS does not apply).</param>
    /// <param name="PartyLine">The party credit line (net, or full gross when below threshold — carrying the detail then).</param>
    /// <param name="TdsPayableLine">The TDS Payable credit line carrying the detail; <c>null</c> when below threshold.</param>
    /// <param name="Detail">The withholding detail (also present below threshold, with <c>TdsAmount</c> 0).</param>
    public sealed record CarveOut(
        Withholding Withholding, Money NetPartyAmount, EntryLine PartyLine, EntryLine? TdsPayableLine, TdsLineTax Detail)
    {
        /// <summary>True iff TDS was withheld (the threshold was crossed).</summary>
        public bool Applies => Withholding.Applies;

        /// <summary>The TDS withheld (0 when below threshold).</summary>
        public Money TdsAmount => Withholding.TdsAmount;
    }

    /// <summary>
    /// Builds the party-net and TDS-payable legs for a withholding voucher: computes the TDS on
    /// <paramref name="assessableValue"/> (GST-exclusive) under <paramref name="nature"/> for
    /// <paramref name="deductee"/>, then <b>derives</b> the party's net credit as
    /// <c>partyGrossObligation − TDS</c> (never an independent gross×(1−rate), so net + TDS == gross to the paisa).
    /// <paramref name="partyGrossObligation"/> is the party's full credit (assessable + any separately-shown GST);
    /// the TDS is still computed only on the GST-exclusive assessable. When the threshold is not crossed the party
    /// is credited the full gross and no TDS Payable line is produced (the detail — with TDS 0 — rides the party
    /// line so the cumulative projection and the Not-Deducted report still see the transaction). Requires TDS to be
    /// enabled (the auto-created "TDS Payable" ledger).
    /// </summary>
    /// <param name="asPostedBefore">The voucher being ALTERED: the cumulative-FY projection is then taken at that
    /// voucher's POSTING MOMENT, so a re-carve makes the same threshold test the posting made (Phase 10.11 S5c).
    /// <c>null</c> for a fresh posting. See <see cref="ProjectPriorCumulative"/>.</param>
    /// <param name="keyedPartyLine">
    /// 🔴 <b>The deductee's line AS THE OPERATOR KEYED IT, so its bill-wise / cost-centre / bank / forex children
    /// are not destroyed by the carve.</b> The derived party leg used to be built from <c>(ledgerId, amount, side)</c>
    /// alone and the caller then SPLICED it over the keyed row, so every child the operator had keyed vanished at
    /// posting with no message. Measured: a ₹1,20,000.30 professional-fees journal against a bill-by-bill Sundry
    /// Creditor with one New Ref posted with <c>billAllocations=0</c>, and <c>Outstandings.OpenBillsFor</c> then
    /// returned NO rows for a creditor owed ₹1,08,000.30. The same loss happened on the BELOW-THRESHOLD branch,
    /// where nothing is carved at all. <c>null</c> ⇒ the pre-S5c childless legs, byte-identical for every
    /// engine-level caller.
    /// </param>
    public CarveOut BuildCarveOut(
        Money partyGrossObligation, Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date,
        Guid? asPostedBefore = null,
        EntryLine? keyedPartyLine = null)
    {
        if (partyGrossObligation.Amount <= 0m)
            throw new ArgumentException("Party gross obligation must be > 0.", nameof(partyGrossObligation));
        if (!partyGrossObligation.IsPaisaExact)
            throw new InvalidOperationException($"Party gross obligation {partyGrossObligation} must be paisa-exact.");

        var w = ComputeWithholding(assessableValue, nature, deductee, date, asPostedBefore);
        var detail = new TdsLineTax(
            nature.Id, nature.SectionCode, assessableValue, w.RateBasisPoints, w.TdsAmount, deductee.Id, w.PanApplied);

        if (!w.Applies)
        {
            // Below threshold: no withholding — the party is credited the full gross; the detail (TDS 0) rides the
            // party line so the FY cumulative and the "TDS Not Deducted" projection still count this assessment.
            // The amount is UNCHANGED on this branch, so every keyed child rides across verbatim — there is
            // nothing to re-derive and nothing that can stop footing.
            var partyFull = new EntryLine(
                deductee.Id, partyGrossObligation, DrCr.Credit,
                NonEmpty(keyedPartyLine?.BillAllocations),
                NonEmpty(keyedPartyLine?.CostAllocations),
                keyedPartyLine?.BankAllocation,
                keyedPartyLine?.Forex,
                tds: detail);
            return new CarveOut(w, partyGrossObligation, partyFull, null, detail);
        }

        var payable = RequirePayableLedger();
        var net = partyGrossObligation - w.TdsAmount; // DERIVED — never gross × (1 − rate)
        if (net.Amount <= 0m)
            throw new InvalidOperationException(
                $"TDS {w.TdsAmount} ≥ party obligation {partyGrossObligation}; the net payable would be non-positive.");

        var partyLine = new EntryLine(
            deductee.Id, net, DrCr.Credit,
            CarveBills(keyedPartyLine, w.TdsAmount, deductee),
            CarveCosts(keyedPartyLine, w.TdsAmount, deductee),
            keyedPartyLine?.BankAllocation,
            RefuseForexOnACarve(keyedPartyLine, deductee));
        var tdsPayableLine = new EntryLine(payable.Id, w.TdsAmount, DrCr.Credit, tds: detail);
        return new CarveOut(w, net, partyLine, tdsPayableLine, detail);
    }

    private static IReadOnlyList<T>? NonEmpty<T>(IReadOnlyList<T>? items) => items is { Count: > 0 } ? items : null;

    /// <summary>
    /// The keyed bill-wise split, carried onto the DERIVED net party leg. The split was keyed against the GROSS
    /// (that is what the entry grid's own bill-split check demands) while the party is credited the NET, so the
    /// deduction has to come out of a reference — and with more than one reference on the line there is no way to
    /// decide WHICH, so that shape is refused by name rather than silently flattened.
    /// <c>VoucherValidator.EnsureBillAllocationsValid</c> is the hard invariant behind this: allocations must sum to
    /// the line amount.
    /// </summary>
    private static IReadOnlyList<BillAllocation>? CarveBills(EntryLine? keyed, Money tds, Domain.Ledger deductee)
    {
        if (keyed is null || keyed.BillAllocations.Count == 0) return null;
        if (keyed.BillAllocations.Count > 1)
            throw new InvalidOperationException(
                $"'{deductee.Name}' is credited against {keyed.BillAllocations.Count} bill references while {tds} of "
                + "TDS is withheld from that credit. The withholding reduces the party's balance to the net, and "
                + "there is no way to decide which reference the deduction comes out of — key the withheld "
                + "transaction against ONE bill reference.");

        var b = keyed.BillAllocations[0];
        var reduced = b.Amount - tds;
        if (reduced.Amount <= 0m)
            throw new InvalidOperationException(
                $"the bill reference '{b.Name}' for '{deductee.Name}' is {b.Amount} while {tds} of TDS is withheld "
                + "from the party's credit, so the bill would be left at or below zero.");
        return new[] { new BillAllocation(b.RefType, b.Name, reduced, b.DueDate, b.CreditPeriodDays) };
    }

    /// <summary>The keyed cost-centre split, carried onto the DERIVED net party leg — the same single-allocation
    /// rule and the same reason as <see cref="CarveBills"/> (cost allocations must total the line amount per
    /// category).</summary>
    private static IReadOnlyList<CostAllocation>? CarveCosts(EntryLine? keyed, Money tds, Domain.Ledger deductee)
    {
        if (keyed is null || keyed.CostAllocations.Count == 0) return null;
        if (keyed.CostAllocations.Count > 1)
            throw new InvalidOperationException(
                $"'{deductee.Name}' is credited across {keyed.CostAllocations.Count} cost-centre allocations while "
                + $"{tds} of TDS is withheld from that credit. The withholding reduces the party's balance to the "
                + "net and there is no way to decide which allocation absorbs it — allocate the withheld "
                + "transaction to ONE cost centre.");

        var a = keyed.CostAllocations[0];
        var reduced = a.Amount - tds;
        if (reduced.Amount <= 0m)
            throw new InvalidOperationException(
                $"the cost allocation on '{deductee.Name}' is {a.Amount} while {tds} of TDS is withheld from the "
                + "party's credit, so the allocation would be left at or below zero.");
        return new[] { new CostAllocation(a.CategoryId, a.CentreId, reduced) };
    }

    /// <summary>
    /// A forex party leg carrying a withholding is refused by name. <see cref="ForexInfo"/>'s contract is
    /// <c>ForexAmount × Rate == the line's base amount</c> (enforced by <c>VoucherValidator.EnsureForexValid</c>)
    /// and the withholding is computed and rounded in the BASE currency, so there is no foreign-currency figure the
    /// net leg can honestly state. It used to be dropped silently, which left the posted line disagreeing with what
    /// the operator keyed and no message anywhere.
    /// </summary>
    private static ForexInfo? RefuseForexOnACarve(EntryLine? keyed, Domain.Ledger deductee)
    {
        if (keyed?.Forex is not { } f) return null;
        throw new InvalidOperationException(
            $"'{deductee.Name}' is credited in a foreign currency ({f.ForexAmount} at {f.Rate}) and TDS is withheld "
            + "from that credit. The withholding is computed and rounded to the nearest RUPEE, so the net leg has no "
            + "exact foreign-currency amount to state — book the withholding on a base-currency line.");
    }

    /// <summary>The auto-created "TDS Payable" liability ledger, or throws if TDS is not enabled.</summary>
    public Domain.Ledger RequirePayableLedger() =>
        _company.Ledgers.FirstOrDefault(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds)
        ?? throw new InvalidOperationException(
            "TDS Payable ledger not found — enable TDS first (TdsTcsService.EnableTds auto-creates it).");
}
