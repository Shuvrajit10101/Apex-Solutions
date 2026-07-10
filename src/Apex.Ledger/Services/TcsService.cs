using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The TCS <b>compute + auto-collect</b> engine (catalog §13; Phase 7 slice 5). Framework-, DB-, clock- and RNG-free:
/// a pure, deterministic mutation-free computation over the <see cref="Company"/> aggregate, exactly like
/// <see cref="GstService"/> — and, unlike the withholding <see cref="TdsService"/>, TCS is <b>additive</b>: it is
/// collected <i>on top</i> of the sale, the mirror of GST. On a Sales voucher where a stock item (or the sales
/// ledger) is TCS-applicable under a §206C <see cref="NatureOfGoods"/> AND the party is a collectee, the collector
/// books
/// <list type="bullet">
///   <item><c>Dr Party = value + GST + TCS</c>,</item>
///   <item><c>Cr Sales = value</c> (unchanged),</item>
///   <item><c>Cr Output GST</c> (unchanged Phase-4 engine),</item>
///   <item><c>Cr "TCS Payable"</c> (a Duties &amp; Taxes liability) <c>= TCS</c>.</item>
/// </list>
/// The TCS Payable ledger sits under Duties &amp; Taxes, so <c>ClassificationRules.IsDutiesAndTaxesLedger</c> excludes
/// it from the item-invoice pairing sum, exactly like the GST tax ledgers — the additive collection foots without
/// changing the Sales pairing invariant (Sales credit == Σ item value). There is <b>no double-count</b> between
/// GSTR-1 and 27EQ: TCS is its own payable, never a sales/GST amount.
/// <para>
/// <b>Goods-driven detection (the S2 lesson applied to TCS).</b> The Nature of Goods comes from the STOCK ITEM's
/// <see cref="StockItem.TcsNatureOfGoodsId"/> (or the sales ledger's <see cref="Domain.Ledger.TcsNatureOfGoodsId"/>),
/// <b>not</b> the party. The <b>party</b> drives only PAN/rate (PAN ⇒ <see cref="NatureOfGoods.RateWithPanBp"/>;
/// no-PAN ⇒ the §206CC <see cref="NatureOfGoods.RateWithoutPanBp"/> — higher of 2×/5%, EXCEPT the 206C(1H) no-PAN cap
/// of 1% the seed already encodes) + the collectee gate. The base follows the nature's
/// <see cref="NatureOfGoods.BaseIncludesGst"/> flag (GST-inclusive for every §206C row, Circular 17/2020). Rounding
/// is income-tax <b>nearest-rupee, round-half-up</b> (same as TDS). The §206C(1H) legacy nature is non-selectable for
/// dates ≥ 01-Apr-2025 (FA2025 year-gate, <see cref="NatureOfGoods.IsSelectableOn"/>).
/// </para>
/// </summary>
public sealed class TcsService
{
    private readonly Company _company;

    public TcsService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ---- goods-driven Nature-of-Goods resolution (item first, then sales ledger) ----

    /// <summary>
    /// Resolves the §206C <see cref="NatureOfGoods"/> for a sale line the <b>goods-driven</b> way (the S2 lesson):
    /// the STOCK ITEM's <see cref="StockItem.TcsNatureOfGoodsId"/> wins; failing that the sales ledger's
    /// <see cref="Domain.Ledger.TcsNatureOfGoodsId"/> (only when it is marked <see cref="Domain.Ledger.TcsApplicable"/>).
    /// Returns <c>null</c> when neither carries a nature — a non-TCS line (no collection, ER-13). The party is
    /// <b>never</b> consulted here; it drives only PAN/rate and the collectee gate.
    /// </summary>
    public NatureOfGoods? ResolveNature(StockItem? item, Domain.Ledger? salesLedger)
    {
        if (item?.TcsNatureOfGoodsId is { } itemNatureId && _company.FindNatureOfGoods(itemNatureId) is { } fromItem)
            return fromItem;
        if (salesLedger is { TcsApplicable: true, TcsNatureOfGoodsId: { } ledgerNatureId } &&
            _company.FindNatureOfGoods(ledgerNatureId) is { } fromLedger)
            return fromLedger;
        return null;
    }

    // ---- rate resolution + threshold + rounding (pure) ----

    /// <summary>The outcome of assessing a sale for TCS (pure; no posting).</summary>
    /// <param name="Applies">True iff TCS must be collected (any threshold crossed).</param>
    /// <param name="AssessableValue">The base the TCS is (or would be) computed on, per the nature's base-incl-GST flag.</param>
    /// <param name="RateBasisPoints">The resolved rate in basis points (with-PAN, or the §206CC no-PAN rate).</param>
    /// <param name="TcsAmount">The TCS collected (nearest rupee, round-half-up); <see cref="Money.Zero"/> when below threshold.</param>
    /// <param name="PanApplied">True iff the collectee PAN was present+valid so the with-PAN rate applied.</param>
    /// <param name="PriorCumulativeInFy">Σ prior-posted assessable for this collectee×nature in the FY (§206C(1H) projection).</param>
    public readonly record struct Collection(
        bool Applies, Money AssessableValue, int RateBasisPoints, Money TcsAmount, bool PanApplied,
        Money PriorCumulativeInFy);

    /// <summary>
    /// Assesses a sale of <paramref name="value"/> (GST-exclusive line value) + <paramref name="gstAmount"/> to
    /// <paramref name="collectee"/> under <paramref name="nature"/> dated <paramref name="date"/>. The <b>assessable
    /// base</b> is resolved from the nature's <see cref="NatureOfGoods.BaseIncludesGst"/> flag: GST-inclusive
    /// (<c>value + gst</c>) for every §206C row (Circular 17/2020), else <c>value</c>. The rate is PAN-driven (PAN ⇒
    /// <see cref="NatureOfGoods.RateWithPanBp"/>; no PAN ⇒ the §206CC <see cref="NatureOfGoods.RateWithoutPanBp"/>).
    /// The threshold gate: a nature with no threshold collects on the full base (scrap); the legacy §206C(1H) nature
    /// applies its ₹50-lakh threshold as a <b>cumulative-FY receipts projection</b> (over prior posted vouchers, like
    /// <c>Gstr1</c> YTD); any other nature with a threshold (e.g. §206C(1F) motor vehicle ₹10-lakh) applies it
    /// <b>per single transaction</b>. When crossed, TCS = <c>round_half_up(chargeable × rate / 10000)</c> to the
    /// nearest rupee, where the <b>chargeable</b> base is the full assessable for every nature EXCEPT the legacy
    /// §206C(1H) one, which charges only receipts <b>exceeding</b> its ₹50-lakh threshold (see
    /// <see cref="ChargeableBase"/>). Pure and total; posts nothing.
    /// </summary>
    public Collection Compute(Money value, Money gstAmount, NatureOfGoods nature, Domain.Ledger collectee, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(nature);
        ArgumentNullException.ThrowIfNull(collectee);
        if (value.Amount < 0m)
            throw new ArgumentException("Sale value must be ≥ 0.", nameof(value));
        if (gstAmount.Amount < 0m)
            throw new ArgumentException("GST amount must be ≥ 0.", nameof(gstAmount));
        if (!value.IsPaisaExact) throw new InvalidOperationException($"Sale value {value} must be paisa-exact.");
        if (!gstAmount.IsPaisaExact) throw new InvalidOperationException($"GST amount {gstAmount} must be paisa-exact.");

        var assessable = nature.BaseIncludesGst ? value + gstAmount : value;

        var panApplied = Pan.IsValid(collectee.PartyPan);
        var rateBp = panApplied ? nature.RateWithPanBp : nature.RateWithoutPanBp;

        var prior = ProjectPriorCumulative(collectee.Id, nature.Id, date);
        if (!ThresholdCrossed(nature, assessable, prior))
            return new Collection(false, assessable, rateBp, Money.Zero, panApplied, prior);

        // The base the TCS is actually CHARGED on. §206C(1H) (the legacy/cumulative nature, mirror of §194Q) collects
        // only on receipts EXCEEDING the ₹50-lakh FY threshold — "a sum equal to 0.1% of the sale consideration
        // exceeding fifty lakh rupees" — so on a straddling transaction only the part of this sale above the threshold
        // is charged. Every other nature (scrap §206C(1F) etc.) charges the FULL base once its gate is crossed. Note
        // AssessableValue (returned + recorded for the FY projection) stays the FULL receipts — only the charged base
        // is carved, so subsequent-year cumulative arithmetic remains exact.
        var chargeableBase = ChargeableBase(nature, assessable, prior);
        var tcs = TdsService.NearestRupee(chargeableBase.Amount * rateBp / 10_000m);
        return new Collection(true, assessable, rateBp, tcs, panApplied, prior);
    }

    /// <summary>
    /// Whether the §206C threshold is crossed so TCS must be collected: a nature with <b>no</b> threshold always
    /// collects (scrap etc.); the legacy §206C(1H) nature collects once the FY aggregate
    /// (<paramref name="prior"/> + current) <b>exceeds</b> its ₹50-lakh receipts threshold; any other nature with a
    /// threshold (§206C(1F) motor vehicle) collects once the <b>single</b> transaction base exceeds it. "Exceeds" is
    /// strict (at exactly the threshold ⇒ no TCS, per the bare-section wording).
    /// </summary>
    private static bool ThresholdCrossed(NatureOfGoods nature, Money current, Money prior)
    {
        if (nature.Threshold is not { } threshold) return true;
        return nature.IsLegacy
            ? (prior + current) > threshold   // §206C(1H): cumulative-FY receipts
            : current > threshold;            // §206C(1F) etc.: single transaction
    }

    /// <summary>
    /// The portion of <paramref name="current"/> the TCS is actually charged on. For the legacy §206C(1H)
    /// cumulative nature only the receipts <b>exceeding</b> the ₹50-lakh FY threshold are charged (bare-section
    /// "sale consideration exceeding fifty lakh rupees", mirror of §194Q): the excess is
    /// <c>(prior + current) − threshold</c>, clamped to <c>[0, current]</c>. Every other nature (scrap, the §206C(1F)
    /// single-transaction gate, …) charges the full base. Callers reach here only after the gate is crossed.
    /// </summary>
    private static Money ChargeableBase(NatureOfGoods nature, Money current, Money prior)
    {
        if (nature is not { IsLegacy: true, Threshold: { } threshold }) return current;
        var excess = (prior.Amount + current.Amount) - threshold.Amount;
        if (excess < 0m) excess = 0m;
        if (excess > current.Amount) excess = current.Amount;
        return new Money(excess);
    }

    // ---- cumulative-FY receipts projection (pure, like Gstr1 YTD) — §206C(1H) ----

    /// <summary>
    /// Σ of the assessable value already posted for (<paramref name="collecteeLedgerId"/>,
    /// <paramref name="natureId"/>) in the financial year of <paramref name="date"/>, up to and including that date —
    /// a <b>pure projection</b> over the company's non-cancelled posted vouchers, reading each line's
    /// <see cref="TcsLineTax.AssessableValue"/> (present on every TCS-assessed sale, collected or below threshold).
    /// Deterministic and order-independent for a fixed voucher set; the not-yet-posted current transaction is
    /// naturally excluded. Mirrors <see cref="TdsService.ProjectPriorCumulative"/>.
    /// </summary>
    public Money ProjectPriorCumulative(Guid collecteeLedgerId, Guid natureId, DateOnly date)
    {
        var (fyStart, _) = TdsService.FinancialYearOf(date);
        var sum = 0m;
        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < fyStart || v.Date > date) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t) continue;
                if (t.CollecteeLedgerId != collecteeLedgerId || t.NatureId != natureId) continue;
                sum += t.AssessableValue.Amount;
            }
        }
        return new Money(sum);
    }

    // ---- additive collection (assemble the TCS-Payable credit leg) ----

    /// <summary>
    /// The additive collection legs for a sale (Phase 7 slice 5). When TCS is collected the
    /// <see cref="TcsPayableLine"/> credits "TCS Payable" the collected amount and carries the
    /// <see cref="TcsLineTax"/> detail; the caller adds it to the sale so the party debit rises by the same amount
    /// (Dr Party = value + GST + TCS). When below threshold no TCS Payable line is produced and the detail (with
    /// <c>TcsAmount</c> 0) is exposed so the caller can ride it on the party leg (keeping the FY receipts projection
    /// exact).
    /// </summary>
    /// <param name="Collection">The computed rate/threshold outcome.</param>
    /// <param name="TcsPayableLine">The TCS Payable credit line carrying the detail; <c>null</c> when below threshold.</param>
    /// <param name="Detail">The collection detail (also present below threshold, with <c>TcsAmount</c> 0).</param>
    public sealed record CollectionPost(Collection Collection, EntryLine? TcsPayableLine, TcsLineTax Detail)
    {
        /// <summary>True iff TCS was collected (a threshold was crossed).</summary>
        public bool Applies => Collection.Applies;

        /// <summary>The TCS collected (0 when below threshold).</summary>
        public Money TcsAmount => Collection.TcsAmount;
    }

    /// <summary>
    /// Builds the additive TCS-Payable credit leg for a sale: computes the TCS on <paramref name="value"/> +
    /// <paramref name="gstAmount"/> under <paramref name="nature"/> for <paramref name="collectee"/>, then — when the
    /// threshold is crossed — produces a "TCS Payable" credit line for the collected amount carrying the
    /// <see cref="TcsLineTax"/> detail. Below threshold no credit line is produced (the detail, with TCS 0, is still
    /// returned so the caller can attach it to the party leg for the Not-Collected projection). Requires TCS to be
    /// enabled (the auto-created "TCS Payable" ledger).
    /// </summary>
    public CollectionPost BuildCollection(
        Money value, Money gstAmount, NatureOfGoods nature, Domain.Ledger collectee, DateOnly date)
    {
        var col = Compute(value, gstAmount, nature, collectee, date);
        var detail = new TcsLineTax(
            nature.Id, nature.CollectionCode, col.AssessableValue, col.RateBasisPoints, col.TcsAmount,
            collectee.Id, col.PanApplied);

        if (!col.Applies)
            return new CollectionPost(col, null, detail);

        var payable = RequirePayableLedger();
        var tcsPayableLine = new EntryLine(payable.Id, col.TcsAmount, DrCr.Credit, tcs: detail);
        return new CollectionPost(col, tcsPayableLine, detail);
    }

    /// <summary>The auto-created "TCS Payable" liability ledger, or throws if TCS is not enabled.</summary>
    public Domain.Ledger RequirePayableLedger() =>
        _company.Ledgers.FirstOrDefault(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tcs)
        ?? throw new InvalidOperationException(
            "TCS Payable ledger not found — enable TCS first (TdsTcsService.EnableTcs auto-creates it).");
}
