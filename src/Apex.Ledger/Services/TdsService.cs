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
    public Withholding ComputeWithholding(Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(nature);
        ArgumentNullException.ThrowIfNull(deductee);
        if (assessableValue.Amount < 0m)
            throw new ArgumentException("Assessable value must be ≥ 0.", nameof(assessableValue));
        if (!assessableValue.IsPaisaExact)
            throw new InvalidOperationException($"Assessable value {assessableValue} must be paisa-exact.");

        var panApplied = Pan.IsValid(deductee.PartyPan);
        var rateBp = panApplied ? nature.RateWithPanBp : nature.RateWithoutPanBp;

        var prior = ProjectPriorCumulative(deductee.Id, nature.Id, date);
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
    public Money ProjectPriorCumulative(Guid deducteeLedgerId, Guid natureId, DateOnly date)
    {
        var (fyStart, _) = FinancialYearOf(date);
        var sum = 0m;
        foreach (var v in _company.Vouchers)
        {
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
    public CarveOut BuildCarveOut(
        Money partyGrossObligation, Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date)
    {
        if (partyGrossObligation.Amount <= 0m)
            throw new ArgumentException("Party gross obligation must be > 0.", nameof(partyGrossObligation));
        if (!partyGrossObligation.IsPaisaExact)
            throw new InvalidOperationException($"Party gross obligation {partyGrossObligation} must be paisa-exact.");

        var w = ComputeWithholding(assessableValue, nature, deductee, date);
        var detail = new TdsLineTax(
            nature.Id, nature.SectionCode, assessableValue, w.RateBasisPoints, w.TdsAmount, deductee.Id, w.PanApplied);

        if (!w.Applies)
        {
            // Below threshold: no withholding — the party is credited the full gross; the detail (TDS 0) rides the
            // party line so the FY cumulative and the "TDS Not Deducted" projection still count this assessment.
            var partyFull = new EntryLine(deductee.Id, partyGrossObligation, DrCr.Credit, tds: detail);
            return new CarveOut(w, partyGrossObligation, partyFull, null, detail);
        }

        var payable = RequirePayableLedger();
        var net = partyGrossObligation - w.TdsAmount; // DERIVED — never gross × (1 − rate)
        if (net.Amount <= 0m)
            throw new InvalidOperationException(
                $"TDS {w.TdsAmount} ≥ party obligation {partyGrossObligation}; the net payable would be non-positive.");

        var partyLine = new EntryLine(deductee.Id, net, DrCr.Credit);
        var tdsPayableLine = new EntryLine(payable.Id, w.TdsAmount, DrCr.Credit, tds: detail);
        return new CarveOut(w, net, partyLine, tdsPayableLine, detail);
    }

    /// <summary>The auto-created "TDS Payable" liability ledger, or throws if TDS is not enabled.</summary>
    public Domain.Ledger RequirePayableLedger() =>
        _company.Ledgers.FirstOrDefault(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds)
        ?? throw new InvalidOperationException(
            "TDS Payable ledger not found — enable TDS first (TdsTcsService.EnableTds auto-creates it).");
}
