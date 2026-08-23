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
/// <see cref="ComputeWithholding"/> resolves the rate (PAN ⇒ with-PAN rate — which on <b>§194C</b> branches on the
/// deductee's legal status, 1% to an individual or HUF and 2% to anyone else per §194C(1); no PAN ⇒ the nature's
/// §206AA no-PAN rate, which the seed sets to 20% generally and 5% for §194Q), applies the section threshold (single-transaction
/// and cumulative-FY, the latter a <b>pure projection</b> over prior posted vouchers per party×nature — like
/// <c>Gstr1</c> YTD accumulation, deterministic with no clock/order side-effects), and applies income-tax
/// <b>nearest-rupee, round-half-up</b> rounding (per A14). TDS is assessed on the <b>GST-exclusive</b> base
/// (Circular 23/2017): the caller passes the assessable value separately from the party's gross obligation.
/// 🔴 <b>T0-1:</b> §194Q charges only the value <b>exceeding</b> its ₹50-lakh cumulative threshold (§194Q(1)); every
/// other section is a qualifying gate and charges the full value once crossed. See <see cref="ChargeableBase"/>,
/// and <see cref="TcsService"/> for the mirror §206C(1H) carve — register IV-2.
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
    /// <param name="TdsAmount">The TDS withheld (nearest rupee, round-half-up); <see cref="Money.Zero"/> when below
    /// threshold. 🔴 T0-1: for §194Q this is the rate applied to the value <b>exceeding</b> the ₹50-lakh cumulative
    /// threshold, NOT to <c>AssessableValue</c> — see <c>ChargeableBase</c>.</param>
    /// <param name="PanApplied">True iff the deductee PAN was present+valid so the with-PAN rate applied.</param>
    /// <param name="PriorCumulativeInFy">Σ prior-posted assessable for this party×nature in the FY (the projection).</param>
    public readonly record struct Withholding(
        bool Applies, Money AssessableValue, int RateBasisPoints, Money TdsAmount, bool PanApplied,
        Money PriorCumulativeInFy);

    /// <summary>
    /// Assesses a payment of <paramref name="assessableValue"/> (the GST-exclusive base) to
    /// <paramref name="deductee"/> under <paramref name="nature"/> dated <paramref name="date"/>: resolves the rate
    /// (PAN ⇒ <see cref="ResolveWithPanRate"/> — <see cref="NatureOfPayment.RateWithPanBp"/>, or on §194C the
    /// <see cref="NatureOfPayment.RateWithPanOtherThanIndividualBp"/> arm when the deductee is not an individual or a
    /// HUF; no PAN ⇒ <see cref="NatureOfPayment.RateWithoutPanBp"/> —
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
    /// <param name="postedRateBasisPoints">
    /// 🔴 <b>The rate the voucher being ALTERED was POSTED with</b>, read off its own stamped
    /// <see cref="TdsLineTax.RateBasisPoints"/> — the grandfathering carrier. <c>null</c> for a fresh posting, and
    /// every pre-bifurcation caller is byte-identical. It is an <b>explicit fact about the voucher</b>, never a
    /// date comparison: see <see cref="GrandfatheredRate"/> for exactly the one disagreement it absorbs and the
    /// four it refuses to.
    /// </param>
    /// <param name="postedAssessableValue">
    /// 🔴 <b>The assessable base the voucher being ALTERED was POSTED on</b>, read off its own stamped
    /// <see cref="TdsLineTax.AssessableValue"/>. With <paramref name="postedTdsAmount"/> it forms the <b>§194-I
    /// grandfathering carrier</b> — a pair of facts about the voucher, never a date comparison. <c>null</c> for a
    /// fresh posting, and every caller that omits it is byte-identical. See
    /// <see cref="GrandfatheredLiability"/> for the rule and for why the base has to travel with the outcome.
    /// </param>
    /// <param name="postedTdsAmount">
    /// 🔴 <b>The TDS the voucher being ALTERED actually WITHHELD when it was posted</b>, read off its own stamped
    /// <see cref="TdsLineTax.TdsAmount"/> (0 on a below-threshold assessment, whose detail rides the party leg).
    /// The §194-I grandfathering carrier's second half. <c>null</c> for a fresh posting.
    /// </param>
    public Withholding ComputeWithholding(
        Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date,
        Guid? asPostedBefore = null, int? postedRateBasisPoints = null,
        Money? postedAssessableValue = null, Money? postedTdsAmount = null)
    {
        ArgumentNullException.ThrowIfNull(nature);
        ArgumentNullException.ThrowIfNull(deductee);
        if (assessableValue.Amount < 0m)
            throw new ArgumentException("Assessable value must be ≥ 0.", nameof(assessableValue));
        if (!assessableValue.IsPaisaExact)
            throw new InvalidOperationException($"Assessable value {assessableValue} must be paisa-exact.");

        var panApplied = Pan.IsValid(deductee.PartyPan);
        var rateBp = GrandfatheredRate(
            nature,
            panApplied ? ResolveWithPanRate(nature, deductee) : nature.RateWithoutPanBp,
            panApplied, postedRateBasisPoints);

        // The FY aggregate is projected for every section: it is what Withholding reports, and it is the base
        // §194Q's excess-only carve is measured against. The THRESHOLD test, however, uses the section's own
        // window — the calendar month on §194-I, the financial year everywhere else.
        var prior = ProjectPriorCumulative(deductee.Id, nature.Id, date, asPostedBefore);
        var priorInWindow = nature.ThresholdWindowIsPerMonth
            ? ProjectPriorInMonth(deductee.Id, nature.Id, date, asPostedBefore)
            : prior;
        var applies = GrandfatheredLiability(nature, assessableValue, postedAssessableValue, postedTdsAmount)
                      ?? ThresholdCrossed(nature, assessableValue, priorInWindow);
        if (!applies)
            return new Withholding(false, assessableValue, rateBp, Money.Zero, panApplied, prior);

        // 🔴 T0-1. The base the TDS is actually CHARGED on. §194Q charges only the value EXCEEDING its ₹50-lakh
        // cumulative threshold; every other section is a qualifying gate that charges the FULL value once crossed.
        // AssessableValue (returned, and stamped on the line for the FY projection) stays the FULL value, so later
        // cumulative arithmetic is unaffected — only the charged base is carved.
        var chargeableBase = ChargeableBase(nature, assessableValue, prior);
        var tds = NearestRupee(chargeableBase.Amount * rateBp / 10_000m);
        return new Withholding(true, assessableValue, rateBp, tds, panApplied, prior);
    }

    /// <summary>
    /// 🔴 <b>The with-PAN rate, which on §194C turns on the DEDUCTEE'S LEGAL STATUS.</b>
    /// <para><b>Statute.</b> Income-tax Act 1961 <b>§194C(1)</b>: "(i) <b>one per cent</b> where the payment is
    /// being made or credit is being given to an <b>individual or a Hindu undivided family</b>; (ii) <b>two per
    /// cent</b> where the payment is being made or credit is being given to a <b>person other than an individual or
    /// a Hindu undivided family</b>" (<c>https://www.incometaxindia.gov.in/w/section-194c</c>; the Department's rate
    /// chart for AY 2026-27 states the same split). Every other seeded section has one with-PAN rate whatever the
    /// deductee is, so <see cref="NatureOfPayment.RateWithPanOtherThanIndividualBp"/> is <c>null</c> for them and
    /// this method returns <see cref="NatureOfPayment.RateWithPanBp"/> unchanged — byte-identical to the pre-branch
    /// engine for §194A, §194H, §194I(a), §194I(b), §194J(a), §194J(b) and §194Q.</para>
    ///
    /// <para>🔴 <b>WHAT THIS FIXED, WITH THE LITERAL FIGURES.</b> The rate used to be
    /// <c>panApplied ? nature.RateWithPanBp : nature.RateWithoutPanBp</c> and read
    /// <see cref="Domain.Ledger.DeducteeType"/> nowhere, so Individual, HUF, Firm and Company all resolved the
    /// seeded 100 bp. Measured on a PAN-holding <b>company</b> contractor and a ₹50,000 bill (liable through
    /// §194C's ₹30,000 single-transaction limb) the engine withheld <b>₹500.00</b> where §194C(1)(ii) requires
    /// <b>₹1,000.00</b> — an under-deduction the deductor answers for under §201. Two tests in the suite asserted
    /// the wrong figure against a company deductee and are corrected alongside this change.</para>
    ///
    /// <para><b>An unrecorded legal status is refused by name.</b> §194C(1)(i) grants the 1% arm only where the
    /// payee <i>is</i> an individual or a HUF; a party with no <see cref="Domain.Ledger.DeducteeType"/> does not
    /// evidence that, and guessing either way moves money. The entry screen cannot produce the shape — a party is
    /// only recognised as a deductee when it carries a deductee type (<c>VoucherEntryViewModel.IsDeducteeLedger</c>)
    /// — so this guards the engine API and the import path, not the operator.</para>
    /// </summary>
    private static int ResolveWithPanRate(NatureOfPayment nature, Domain.Ledger deductee)
    {
        if (nature.RateWithPanOtherThanIndividualBp is not { } otherThanIndividualBp) return nature.RateWithPanBp;
        if (deductee.DeducteeType is not { } status)
            throw new InvalidOperationException(
                $"'{deductee.Name}' is a §{nature.SectionCode} deductee with no deductee type recorded, and "
                + $"§{nature.SectionCode} withholds at different rates depending on whether the payee is an "
                + "individual or a Hindu undivided family. Set the party's Deductee Type on the ledger master "
                + "before withholding from it.");
        return status is DeducteeType.Individual or DeducteeType.HinduUndividedFamily
            ? nature.RateWithPanBp
            : otherThanIndividualBp;
    }

    /// <summary>
    /// 🔴 <b>GRANDFATHERING — a voucher posted before the §194C deductee-type branch existed keeps the rate it was
    /// posted with. That is a fact about the VOUCHER, carried explicitly; nothing here reads a clock.</b>
    ///
    /// <para><b>Why it is needed.</b> Before the branch, EVERY §194C voucher resolved
    /// <see cref="NatureOfPayment.RateWithPanBp"/> — 100 bp — including those whose deductee is a company, a firm
    /// or an AOP. The alteration path pins <c>RateBasisPoints</c> off the posted voucher and refuses a
    /// disagreement (<c>VoucherEntryViewModel.ApplyReCarve</c>), so switching the branch on would have made every
    /// one of those vouchers <b>unalterable</b> — turning a rate defect into a data-migration problem for anyone
    /// with §194C history. <paramref name="postedBp"/> comes off the voucher's own stamped
    /// <see cref="TdsLineTax.RateBasisPoints"/>, so the rule is explicit and pinned rather than implicit in a date
    /// test.</para>
    ///
    /// <para>🔴 <b>EXACTLY ONE DISAGREEMENT IS ABSORBED, AND IT IS DIRECTIONAL:</b> posted on the section's own
    /// <see cref="NatureOfPayment.RateWithPanBp"/> arm, now resolving its
    /// <see cref="NatureOfPayment.RateWithPanOtherThanIndividualBp"/> arm. That is the one shape a pre-bifurcation
    /// voucher can have, because before the branch that arm was the only reachable answer. Everything else falls
    /// through to <paramref name="resolvedBp"/> and is therefore still refused upstream:</para>
    /// <list type="bullet">
    ///   <item><b>No PAN.</b> The §206AA rate is never grandfathered — a PAN added or removed after posting must
    ///     still be refused, and that refusal is pinned by
    ///     <c>VoucherAlterReDeriveTests.A_deductee_PAN_added_after_posting_is_refused_rather_than_re_carved_at_the_new_rate</c>.</item>
    ///   <item><b>A section with no deductee-type branch.</b> A moved §194J / §194I / §194A rate master is still a
    ///     disagreement.</item>
    ///   <item><b>The other direction.</b> Posted at 200 bp and now resolving 100 bp means the party was RE-TYPED
    ///     down to an individual or HUF after posting — drift, not history, and still refused.</item>
    ///   <item><b>A posted rate outside this section's own two arms</b> — a hand-edited or imported figure — is not
    ///     honoured, so an arbitrary stamped rate cannot be resurrected by handing it back.</item>
    /// </list>
    ///
    /// <para><b>What it cannot distinguish, stated plainly.</b> A genuinely pre-bifurcation company voucher
    /// (posted 100, resolves 200) and a post-bifurcation <i>individual</i> voucher whose party was later re-typed
    /// UP to a company (also posted 100, resolves 200) are the same two numbers, and no persisted field tells them
    /// apart without a schema change. The ambiguity is resolved towards grandfathering because that is the ruling's
    /// purpose, and the safety property holds either way: <b>a deduction that has already been posted and reported
    /// is never restated.</b></para>
    /// </summary>
    internal static int GrandfatheredRate(NatureOfPayment nature, int resolvedBp, bool panApplied, int? postedBp)
    {
        if (postedBp is not { } posted || posted == resolvedBp) return resolvedBp;
        if (!panApplied) return resolvedBp;
        if (nature.RateWithPanOtherThanIndividualBp is not { } otherThanIndividualBp) return resolvedBp;
        return posted == nature.RateWithPanBp && resolvedBp == otherThanIndividualBp ? posted : resolvedBp;
    }

    /// <summary>
    /// 🔴 <b>GRANDFATHERING FOR §194-I — the user's ruling, and it pins the posted OUTCOME, not a rate. Nothing
    /// here reads a clock either.</b> Returns <c>true</c>/<c>false</c> to FORCE the liability decision the voucher
    /// was posted with, or <c>null</c> to let <see cref="ThresholdCrossed"/> decide as it does for a fresh entry.
    ///
    /// <para>🔴 <b>WHY THIS IS NOT <see cref="GrandfatheredRate"/>, STATED PLAINLY BECAUSE IT IS THE WHOLE
    /// DIFFICULTY.</b> §194C's grandfathering absorbs a <b>rate</b> disagreement: the voucher withheld, it still
    /// withholds, only the percentage moved. §194-I's window did not move a percentage — it moved <b>whether the
    /// threshold was crossed at all</b>. A ₹60,000 rent bill posted under the superseded ₹6,00,000-a-year rule
    /// withheld <b>₹0.00</b>; under the statutory ₹50,000-a-month rule the same bill owes <b>₹6,000.00</b>. And it
    /// runs the other way too: twelve ₹40,000 months crossed ₹6,00,000 in the eleventh and withheld ₹4,000 there,
    /// where no single month exceeds ₹50,000 and the statute withholds nothing. So the fact that has to be pinned
    /// is the posted <b>outcome</b> — did this voucher withhold — and a rate alone cannot carry it.</para>
    ///
    /// <para>🔴 <b>AND WHY IT MUST BE PINNED AT ALL: WITHOUT IT EVERY SUCH VOUCHER BECOMES UNALTERABLE.</b>
    /// <c>VoucherEntryViewModel.ApplyReCarve</c> refuses an alteration whose re-carve produces a different TDS
    /// while the party's gross has not moved — deliberately, because a re-computed figure would restate a
    /// deduction that has already been deposited and reported in a return. Flipping the window without this pin
    /// would trip that refusal on every §194-I voucher in every existing book: a narration fix, a cost-centre
    /// correction, a date typo, all refused. The ruling exists so those vouchers <b>keep their posted figure and
    /// stay editable</b>, and the pin is what delivers both at once — the re-carve reproduces the posted amount, so
    /// the refusal never fires.</para>
    ///
    /// <para>🔴 <b>THE RULE, AND ITS ONE GATE: THE BASE MUST BE UNCHANGED.</b> The pin applies only while
    /// <paramref name="postedAssessableValue"/> still equals <paramref name="assessableValue"/> — i.e. while the
    /// alteration has not touched the sum the tax is assessed on. An operator who genuinely amends the rent from
    /// ₹60,000 to ₹40,000 is restating the transaction, and the correct answer for the amended figure is the
    /// <b>statutory</b> one, not the superseded one; without this gate the amended bill would keep withholding
    /// because a different bill once did. This is the same line <c>ApplyReCarve</c> already draws with its own
    /// "the gross has not moved" test, so the two agree: base unchanged ⇒ the posted outcome stands; base moved ⇒
    /// an ordinary re-carve, and if some master moved underneath it the existing refusal still catches it.</para>
    ///
    /// <para><b>Scope, and what stays byte-identical.</b> Only a section whose window is per-month
    /// (<see cref="NatureOfPayment.ThresholdWindowIsPerMonth"/> — §194-I alone) is grandfathered, because the
    /// window redefinition is the only drift being absorbed. §194A / §194C / §194H / §194J / §194Q reach the
    /// ordinary threshold test with these arguments supplied or not, so their behaviour is unchanged, and so is
    /// every fresh posting on §194-I (a fresh entry has no posted outcome to hand in).</para>
    ///
    /// <para><b>What it cannot distinguish, stated plainly.</b> "Withheld" is read as
    /// <c>postedTdsAmount &gt; 0</c>. A voucher that was liable but whose tax rounded to zero — reachable only on
    /// an assessable under ₹25, i.e. a residual bill inside a month already over ₹50,000 — reads as
    /// "did not withhold". The two answers are the same figure (₹0.00) in that case, so no money moves either
    /// way; recording it because a stored applies-flag would remove the ambiguity and that needs a schema
    /// column.</para>
    /// </summary>
    internal static bool? GrandfatheredLiability(
        NatureOfPayment nature, Money assessableValue, Money? postedAssessableValue, Money? postedTdsAmount)
    {
        if (!nature.ThresholdWindowIsPerMonth) return null;
        if (postedAssessableValue is not { } postedBase || postedTdsAmount is not { } postedTds) return null;
        if (postedBase != assessableValue) return null;
        return postedTds.Amount > 0m;
    }

    /// <summary>
    /// 🔴 <b>T0-1 — the portion of <paramref name="current"/> the TDS is actually charged on.</b> For a section
    /// flagged <see cref="NatureOfPayment.ChargesOnlyExcessOverCumulativeThreshold"/> (§194Q — Income-tax Act 1961
    /// §194Q(1), "0.1 per cent. of such sum exceeding fifty lakh rupees") only the value above the cumulative-FY
    /// threshold is charged: the excess is <c>(prior + current) − cumulativeThreshold</c>, clamped to
    /// <c>[0, current]</c>. Every other section charges the full value once its gate is crossed. Callers reach here
    /// only after <see cref="ThresholdCrossed"/> returned true.
    ///
    /// <para>🔴 <b>LIMB-AWARE, and the naive version returns ₹0 on a liable bill.</b> A section can be liable
    /// through EITHER of two limbs — §194C has a ₹30,000 single-transaction limb AND a ₹1,00,000 cumulative one. A
    /// ₹50,000 §194C bill is liable through the SINGLE limb while the cumulative is nowhere near crossed; carving
    /// against the cumulative limb gives <c>(0 + 50,000) − 1,00,000 = −50,000</c>, clamps to 0, and withholds
    /// NOTHING on a bill that owes ₹500. So the carve is refused whenever the single-transaction limb is the one
    /// that fired. §194Q has no single-transaction limb, so for it this guard never engages — it exists to stop the
    /// carve leaking onto a two-limb section, which is exactly what copying <c>TcsService.ChargeableBase</c>
    /// verbatim would have done (that engine is single-limb by construction).</para>
    /// </summary>
    private static Money ChargeableBase(NatureOfPayment nature, Money current, Money prior)
    {
        if (!nature.ChargesOnlyExcessOverCumulativeThreshold) return current;
        if (nature.CumulativeThreshold is not { } cumulative) return current;
        // Liable through the single-transaction limb ⇒ the whole value is charged, never the cumulative excess.
        if (nature.SingleTransactionThreshold is { } single && current > single) return current;

        var excess = (prior.Amount + current.Amount) - cumulative.Amount;
        if (excess < 0m) excess = 0m;
        if (excess > current.Amount) excess = current.Amount;
        return new Money(excess);
    }

    /// <summary>Rounds a raw amount to the nearest whole rupee, <b>round-half-up</b> (away-from-zero) — the
    /// income-tax TDS/TCS rounding rule (A14). A positive raw amount's away-from-zero is exactly half-up.</summary>
    public static Money NearestRupee(decimal raw) => Money.FromRupees(Math.Round(raw, 0, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Whether the section threshold is crossed so TDS must be withheld: a nature with <b>no</b> threshold always
    /// applies; otherwise TDS applies iff the current transaction <b>exceeds</b> the single-transaction threshold
    /// (§194C ₹30,000) OR the aggregate over the section's own <b>threshold window</b>
    /// (<paramref name="priorInWindow"/> + current) <b>exceeds</b> that window's limb. "Exceeds" is strict (at
    /// exactly the threshold ⇒ no TDS, per the bare Act wording).
    ///
    /// <para>🔴 <b>THE WINDOW IS THE NATURE'S, NOT THIS METHOD'S — and reading
    /// <see cref="NatureOfPayment.CumulativeThreshold"/> here again is the one edit that silently reopens the
    /// §194-I under-deduction.</b> Both the limb (<see cref="NatureOfPayment.AggregateThreshold"/>) and the
    /// aggregate handed in must come from the same window: the financial year for §194A/§194C/§194H/§194J/§194Q,
    /// the <b>calendar month</b> for §194-I, whose first proviso tests the rent "for a month or part of a month"
    /// against ₹50,000 and which has no annual limb at all. <see cref="ComputeWithholding"/> is the only caller
    /// and pairs them; the "no threshold at all ⇒ always applies" early return tests
    /// <see cref="NatureOfPayment.AggregateThreshold"/> for the same reason — against a §194-I nature whose
    /// superseded <see cref="NatureOfPayment.CumulativeThreshold"/> is now unset, testing that field instead would
    /// read "no threshold" and withhold on <b>every</b> rent bill, ₹100 included.</para>
    /// </summary>
    private static bool ThresholdCrossed(NatureOfPayment nature, Money current, Money priorInWindow)
    {
        if (nature.SingleTransactionThreshold is null && nature.AggregateThreshold is null) return true;
        var single = nature.SingleTransactionThreshold is { } st && current > st;
        var aggregate = nature.AggregateThreshold is { } at && (priorInWindow + current) > at;
        return single || aggregate;
    }

    // ---- threshold-window projection (pure, like Gstr1 YTD): per-FY, or per-MONTH for §194-I ----

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
        Guid deducteeLedgerId, Guid natureId, DateOnly date, Guid? asPostedBefore = null) =>
        ProjectPriorBetween(deducteeLedgerId, natureId, FinancialYearOf(date).Start, date, asPostedBefore);

    /// <summary>
    /// 🔴 <b>§194-I's window: Σ of the assessable value already posted for
    /// (<paramref name="deducteeLedgerId"/>, <paramref name="natureId"/>) in the CALENDAR MONTH of
    /// <paramref name="date"/>, up to and including that date.</b> Income-tax Act 1961 §194-I, first proviso
    /// (FY 2025-26): the comparable set is the rent "credited or paid <b>for a month or part of a month</b>" to
    /// that payee. The month is derived from the <b>voucher date</b> — the model carries no rent-period field, and
    /// the date on which the rent is credited or paid is exactly the trigger the proviso names.
    ///
    /// <para><b>A part-month is not pro-rated.</b> The window is the whole calendar month containing the date and
    /// the limb stays the whole ₹50,000, so a tenancy running half of April and half of May is two windows with a
    /// full allowance each, not one allowance split between them.</para>
    ///
    /// <para><b>The financial year never enters, and it never has to.</b> An Indian FY runs 1 April – 31 March, so
    /// a calendar month is always wholly inside one FY and this window can neither straddle a year boundary nor
    /// leak across one: 31-Mar and 1-Apr are a different month AND a different year, and either test alone
    /// separates them.</para>
    ///
    /// <para>🔴 <b><paramref name="asPostedBefore"/> means here exactly what it means on
    /// <see cref="ProjectPriorCumulative"/></b> — the same list-index resolution, in the same shared loop, because
    /// a monthly window that took the projection over the WHOLE book would reintroduce the defect S5c closed one
    /// section over: a §194-I voucher altered after a later sibling was posted would count that sibling as
    /// "prior", and a narration edit would acquire a withholding the posting never made.</para>
    /// </summary>
    public Money ProjectPriorInMonth(
        Guid deducteeLedgerId, Guid natureId, DateOnly date, Guid? asPostedBefore = null) =>
        ProjectPriorBetween(deducteeLedgerId, natureId, new DateOnly(date.Year, date.Month, 1), date, asPostedBefore);

    /// <summary>
    /// The prior aggregate over <b>the section's own threshold window</b> — the calendar month for a per-month
    /// nature (§194-I), the financial year for every other. The one entry point a caller that does not already
    /// know which window applies should use, so a report and the engine can never disagree about the window.
    /// </summary>
    public Money ProjectPriorInThresholdWindow(
        NatureOfPayment nature, Guid deducteeLedgerId, DateOnly date, Guid? asPostedBefore = null)
    {
        ArgumentNullException.ThrowIfNull(nature);
        return nature.ThresholdWindowIsPerMonth
            ? ProjectPriorInMonth(deducteeLedgerId, nature.Id, date, asPostedBefore)
            : ProjectPriorCumulative(deducteeLedgerId, nature.Id, date, asPostedBefore);
    }

    /// <summary>The shared projection loop — identical for both windows but for <paramref name="from"/>.</summary>
    private Money ProjectPriorBetween(
        Guid deducteeLedgerId, Guid natureId, DateOnly from, DateOnly to, Guid? asPostedBefore)
    {
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
            if (v.Date < from || v.Date > to) continue;
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
    /// <param name="postedRateBasisPoints">
    /// 🔴 <b>The rate the voucher being ALTERED was POSTED with</b> — the §194C grandfathering carrier, passed
    /// straight through to <see cref="ComputeWithholding"/>. <c>null</c> for a fresh posting. See
    /// <see cref="GrandfatheredRate"/>.
    /// </param>
    /// <param name="postedAssessableValue">
    /// 🔴 <b>The assessable base the voucher being ALTERED was POSTED on</b> — with
    /// <paramref name="postedTdsAmount"/>, the §194-I grandfathering carrier, passed straight through to
    /// <see cref="ComputeWithholding"/>. <c>null</c> for a fresh posting. See
    /// <see cref="GrandfatheredLiability"/>.
    /// </param>
    /// <param name="postedTdsAmount">
    /// 🔴 <b>The TDS that voucher actually withheld at posting</b> (0 below threshold) — the other half of the
    /// §194-I grandfathering carrier. <c>null</c> for a fresh posting.
    /// </param>
    public CarveOut BuildCarveOut(
        Money partyGrossObligation, Money assessableValue, NatureOfPayment nature, Domain.Ledger deductee, DateOnly date,
        Guid? asPostedBefore = null,
        EntryLine? keyedPartyLine = null,
        int? postedRateBasisPoints = null,
        Money? postedAssessableValue = null,
        Money? postedTdsAmount = null)
    {
        if (partyGrossObligation.Amount <= 0m)
            throw new ArgumentException("Party gross obligation must be > 0.", nameof(partyGrossObligation));
        if (!partyGrossObligation.IsPaisaExact)
            throw new InvalidOperationException($"Party gross obligation {partyGrossObligation} must be paisa-exact.");

        var w = ComputeWithholding(
            assessableValue, nature, deductee, date, asPostedBefore, postedRateBasisPoints,
            postedAssessableValue, postedTdsAmount);
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
