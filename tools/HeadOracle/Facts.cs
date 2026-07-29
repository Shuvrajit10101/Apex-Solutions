namespace HeadOracle;

// ---------------------------------------------------------------------------------------------------
// FACTS — the engine-INDEPENDENT ground truth behind the absolute checks 6, 7 and 8.
//
// NOTHING in this file calls the engine. Every number here is derived from the Scenario SPEC alone:
// the rate band, the total inward spend, the total outward quantity, the standard cost.
//
// That independence is the whole point. Checks 6, 7 and 8 must be able to CONVICT the engine; an
// oracle that asked the engine what the right answer was could never do that.
//
// Everything is computed AS OF A DATE, because the engine's closing value at 2024-04-07 must not be
// judged against purchases that happen on 2024-04-15.
//
// UNIT NORMALISATION. A line stated in a compound unit ("5 Doz" of a Nos-measured item) carries a rate
// PER THE LINE'S UNIT. So base quantity = qty x factor and base rate = rate / factor, and the LINE
// TOTAL qty x rate is invariant. Spend is therefore accumulated as qty x rate (exact, unit-free);
// only quantities and the rate BAND are converted to base units.
//
// AUDIT FIX H1 — the v1 harness stood checks 3 and 4 (now 7 and 8) DOWN entirely on physical-count and
// unrated-inward scenarios, i.e. exactly the G6/G7 families that are the debt-semantics edge cases.
// It did so because a count-up or an unrated inward creates units that no rated purchase paid for, so
// "you cannot hold more asset than you bought" has no premise. The fix is not to skip the check but to
// IMPUTE a ceiling cost for those units: the highest rate signal the item has (its dearest inward rate,
// or its standard cost, whichever is larger). Every such unit is then allowed to be worth at most that,
// which is still a hard upper bound, and the checks run on every family. The report states, per
// subject, whether imputation was involved so a reader can tell a tight bound from a loose one.
// ---------------------------------------------------------------------------------------------------

/// <summary>
/// Spec-derived ground truth for one (scenario, item) as of one date. Money-shaped facts are in paisa;
/// quantities in micro-units (1e6). Rate-band edges are FLOORED (min) / CEILINGED (max) to the paisa so
/// the band can never be narrower than the true band — a rounding artefact must never convict the engine.
/// </summary>
public sealed record AsOfFacts(
    bool HasRatedInward,
    bool HasUnratedInward,
    bool HasCount,
    bool HasRatedOutward,
    bool HasStandardCost,
    long StandardCostPaisa,
    long MinInwardRateBandPaisa,
    long MaxInwardRateBandPaisa,
    long MinOutwardRateBandPaisa,
    long MaxOutwardRateBandPaisa,
    long RatedSpendPaisaCeil,
    long SpendCeilingPaisa,
    long ImputedUnitsMicro,
    long TotalInwardMicro,
    long TotalOutwardMicro)
{
    /// <summary>True when the spend ceiling had to impute a cost for units nobody bought.</summary>
    public bool UsesImputation => ImputedUnitsMicro > 0L;

    /// <summary>The union of two fact sets — how the company-wide aggregate row is built.</summary>
    public static AsOfFacts Union(AsOfFacts a, AsOfFacts b) => new(
        HasRatedInward: a.HasRatedInward || b.HasRatedInward,
        HasUnratedInward: a.HasUnratedInward || b.HasUnratedInward,
        HasCount: a.HasCount || b.HasCount,
        HasRatedOutward: a.HasRatedOutward || b.HasRatedOutward,
        HasStandardCost: a.HasStandardCost || b.HasStandardCost,
        StandardCostPaisa: Math.Max(a.StandardCostPaisa, b.StandardCostPaisa),
        MinInwardRateBandPaisa: Merge(a.HasRatedInward, a.MinInwardRateBandPaisa, b.HasRatedInward, b.MinInwardRateBandPaisa, min: true),
        MaxInwardRateBandPaisa: Merge(a.HasRatedInward, a.MaxInwardRateBandPaisa, b.HasRatedInward, b.MaxInwardRateBandPaisa, min: false),
        MinOutwardRateBandPaisa: Merge(a.HasRatedOutward, a.MinOutwardRateBandPaisa, b.HasRatedOutward, b.MinOutwardRateBandPaisa, min: true),
        MaxOutwardRateBandPaisa: Merge(a.HasRatedOutward, a.MaxOutwardRateBandPaisa, b.HasRatedOutward, b.MaxOutwardRateBandPaisa, min: false),
        RatedSpendPaisaCeil: a.RatedSpendPaisaCeil + b.RatedSpendPaisaCeil,
        SpendCeilingPaisa: a.SpendCeilingPaisa + b.SpendCeilingPaisa,
        ImputedUnitsMicro: a.ImputedUnitsMicro + b.ImputedUnitsMicro,
        TotalInwardMicro: a.TotalInwardMicro + b.TotalInwardMicro,
        TotalOutwardMicro: a.TotalOutwardMicro + b.TotalOutwardMicro);

    private static long Merge(bool aHas, long a, bool bHas, long b, bool min)
        => aHas && bHas ? (min ? Math.Min(a, b) : Math.Max(a, b))
         : aHas ? a
         : bHas ? b
         : 0L;
}

public static class Facts
{
    /// <summary>
    /// Derives the facts for one item of <paramref name="s"/> as of <paramref name="asOf"/> from the spec
    /// only. Opening stock is an inward lot dated before every voucher, so it always participates.
    /// </summary>
    public static AsOfFacts For(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var hasRatedInward = false;
        var hasUnratedInward = false;
        var hasCount = false;
        var hasRatedOutward = false;

        decimal? minIn = null, maxIn = null, minOut = null, maxOut = null;
        var ratedSpend = 0m;     // rupees, exact: sum of (line qty x line rate)
        var inwardBase = 0m;     // base units
        var outwardBase = 0m;    // base units

        void NoteInward(decimal baseRate)
        {
            hasRatedInward = true;
            if (minIn is null || baseRate < minIn) minIn = baseRate;
            if (maxIn is null || baseRate > maxIn) maxIn = baseRate;
        }

        void NoteOutward(decimal baseRate)
        {
            hasRatedOutward = true;
            if (minOut is null || baseRate < minOut) minOut = baseRate;
            if (maxOut is null || baseRate > maxOut) maxOut = baseRate;
        }

        // --- opening stock: an inward lot that sorts before every dated voucher, at every as-of date.
        if (item.OpeningQty > 0m)
        {
            NoteInward(item.OpeningRate);
            ratedSpend += item.OpeningQty * item.OpeningRate;
            inwardBase += item.OpeningQty;
        }

        foreach (var m in s.MovementsOf(item))
        {
            if (m.Cancelled) continue;   // a cancelled voucher never counts — no spend, no rate, no quantity
            if (m.Date > asOf) continue;

            var factor = m.UseCompoundUnit
                ? (decimal)Corpus.CompoundNumerator / Corpus.CompoundDenominator
                : 1m;
            var baseQty = m.Qty * factor;

            switch (m.Kind)
            {
                case MoveKind.Inward:
                    if (m.Rate is { } inRate)
                    {
                        NoteInward(inRate / factor);
                        ratedSpend += m.Qty * inRate;   // line total is unit-invariant
                    }
                    else
                    {
                        hasUnratedInward = true;
                    }
                    inwardBase += baseQty;
                    break;

                case MoveKind.Outward:
                    if (m.Rate is { } outRate) NoteOutward(outRate / factor);
                    outwardBase += baseQty;
                    break;

                case MoveKind.Count:
                    hasCount = true;
                    break;
            }
        }

        var std = Corpus.StandardCostOf(item);
        var stdSet = std is not null;
        var stdRupees = std ?? 0m;
        var stdPaisa = (long)decimal.Round(stdRupees * 100m, 0, MidpointRounding.AwayFromZero);

        // --- imputed spend for units no rated purchase paid for (audit H1).
        // The dearest rate signal the item has is the ceiling any such unit could possibly be worth:
        // the best-available-cost chain can only ever reach a rate the item has actually seen.
        var imputed = Reference.Imputed(s, item, asOf);
        var maxRateSignal = Math.Max(maxIn ?? 0m, stdRupees);
        var spendCeiling = ratedSpend + imputed.Total * maxRateSignal;

        return new AsOfFacts(
            HasRatedInward: hasRatedInward,
            HasUnratedInward: hasUnratedInward,
            HasCount: hasCount,
            HasRatedOutward: hasRatedOutward,
            HasStandardCost: stdSet,
            StandardCostPaisa: stdSet ? stdPaisa : 0L,
            MinInwardRateBandPaisa: minIn is { } lo ? (long)decimal.Floor(lo * 100m) : 0L,
            MaxInwardRateBandPaisa: maxIn is { } hi ? (long)decimal.Ceiling(hi * 100m) : 0L,
            MinOutwardRateBandPaisa: minOut is { } olo ? (long)decimal.Floor(olo * 100m) : 0L,
            MaxOutwardRateBandPaisa: maxOut is { } ohi ? (long)decimal.Ceiling(ohi * 100m) : 0L,
            // Ceiling in paisa: the containment checks must never trip on a sub-paisa rounding artefact.
            RatedSpendPaisaCeil: (long)decimal.Ceiling(ratedSpend * 100m),
            SpendCeilingPaisa: (long)decimal.Ceiling(spendCeiling * 100m),
            ImputedUnitsMicro: (long)decimal.Round(imputed.Total * 1_000_000m, 0, MidpointRounding.AwayFromZero),
            TotalInwardMicro: (long)decimal.Round(inwardBase * 1_000_000m, 0, MidpointRounding.AwayFromZero),
            TotalOutwardMicro: (long)decimal.Round(outwardBase * 1_000_000m, 0, MidpointRounding.AwayFromZero));
    }


    // =============================================================================================
    // THE FLATTENED ITEM-LEVEL REPLAY, AS A PURE QUANTITY WALK.
    // =============================================================================================
    // Three facts below (PostDryLots, InventedByRule, DebtShape) rest on ONE equivalence: "the
    // reference's outstanding debt is positive at an event precisely when the ITEM-LEVEL net quantity
    // before that event is negative". This walk re-derives that net from the SPEC and from QUANTITIES
    // ONLY — no rate, no cost, no layer — which is what makes these facts able to audit the reference
    // without sharing its arithmetic: they are a quantity walk, and it is a cost walk.
    //
    // UNGATED, matching the reference. An earlier round confined the debt rule to single-key items and
    // this walk carried a copy of that predicate; the scoping was abandoned (a predicate-gated scope
    // puts a valuation cliff at its own boundary — see Reference.cs's header), so the gate is gone from
    // both. A shortfall in the flattened stream is ALWAYS owed.
    //
    // The flattened replay's net therefore obeys, exactly:
    //     net == SUM(surviving layer qty) - outstanding debt
    // at every point, because a debt can only exist once the layers are drained (an inward repays
    // before it adds a layer, a count writes the debt off), so layers = max(net, 0) and
    // debt = max(-net, 0) throughout.

    /// <summary>One event of the flattened item-level stream, with the (godown, batch) key the QUANTITY
    /// register uses and whether its rate is stated.</summary>
    private readonly record struct FlatEv(
        string Token, MoveKind Kind, decimal Qty, bool Rated, (int Godown, string Batch) Key);

    /// <summary>The item's movements as of a date, flattened into ONE stream in the replay's order.</summary>
    private static List<FlatEv> FlatStream(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var stream = new List<(DateOnly Date, int CountLast, int Number, int Seq, FlatEv E)>();

        if (item.OpeningQty > 0m)
            stream.Add((DateOnly.MinValue, 0, int.MinValue, int.MinValue,
                new FlatEv(LotToken.Opening, MoveKind.Inward, item.OpeningQty, true,
                           (item.OpeningGodown, ""))));

        foreach (var m in s.MovementsOf(item))
        {
            if (m.Cancelled) continue;
            if (m.Date > asOf) continue;
            var baseQty = m.Kind == MoveKind.Count
                ? m.Qty                                     // a counted quantity is always in base units
                : m.UseCompoundUnit
                    ? m.Qty * Corpus.CompoundNumerator / Corpus.CompoundDenominator
                    : m.Qty;
            stream.Add((m.Date, m.Kind == MoveKind.Count ? 1 : 0, m.Number, m.Seq,
                new FlatEv(LotToken.For(m), m.Kind, baseQty, m.Rate is not null, (m.Godown, m.Batch))));
        }

        return stream
            .OrderBy(t => t.Date).ThenBy(t => t.CountLast).ThenBy(t => t.Number).ThenBy(t => t.Seq)
            .Select(t => t.E)
            .ToList();
    }

    /// <summary>
    /// The flattened item-level replay's NET quantity BEFORE each event, plus the net it finishes at.
    /// An outward can only draw what the stack holds (<c>max(net, 0)</c>); the shortfall goes to DEBT —
    /// i.e. the net goes negative — unconditionally. A count is an absolute statement.
    /// </summary>
    private static (decimal[] Before, decimal Final) FlatNet(List<FlatEv> ev)
    {
        var before = new decimal[ev.Count];
        var net = 0m;
        for (var i = 0; i < ev.Count; i++)
        {
            before[i] = net;
            switch (ev[i].Kind)
            {
                case MoveKind.Count:
                    net = ev[i].Qty;
                    break;
                case MoveKind.Inward:
                    net += ev[i].Qty;
                    break;
                default:
                {
                    var take = Math.Min(ev[i].Qty, Math.Max(net, 0m));
                    net -= take;
                    var shortfall = ev[i].Qty - take;
                    if (shortfall > 0m) net -= shortfall;
                    break;
                }
            }
        }
        return (before, net);
    }

    /// <summary>
    /// SPEC-DERIVED: the quantity the ITEM-LEVEL cost-layer replay reaches — <c>layers - debt</c> — in
    /// micro-units, signed.
    /// <para>WHY THIS EXISTS, AND WHAT IT MAKES VISIBLE. The reference's self-consistency invariant used to
    /// compare the layer stack against the REPORTED closing quantity, which the quantity oracle replays PER
    /// (item, godown, batch). Those two agree on every single-key book and on every multi-key book without a
    /// physical count — but a per-key COUNT applied to a flattened stack is exactly where they part company,
    /// and that DESYNC is a real, pre-existing property of the engine, not a harness defect. Comparing the
    /// layers against THIS number keeps the invariant sharp (it still convicts a replay that values a
    /// different number of units than it holds) while the desync itself is reported as a measured delta
    /// rather than swallowed as a harness failure.</para>
    /// </summary>
    public static long FlatNetMicro(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var ev = FlatStream(s, item, asOf);
        var (_, final) = FlatNet(ev);
        return (long)decimal.Round(final * 1_000_000m, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// THE SPEC'S LOT TABLE for one (item, as-of): every inward lot that exists, named by
    /// <see cref="LotToken"/>, with its BASE quantity and its BASE rate ("-" when the lot is unrated).
    /// Format: <c>token:qty@rate;token:qty@-</c>, ordered by the same (date, count-last, number, seq) key
    /// the valuation replay uses.
    /// <para>AUDIT #3 FINDING [1] (HIGH). The REFERENCE VALUE INVARIANT asserted only that a surviving
    /// layer's rate was a MEMBER of the admissible set. That is necessary and NOT sufficient: the single
    /// most likely genuine mistake in the debt branch — re-rating the repayment surplus at the rate of the
    /// stock that ran out — uses a rate that IS in the set, so the invariant acquitted it while the poisoned
    /// reference demanded Rs 2,503.25 where the brief says Rs 197.75. This table is what makes the question
    /// answerable: not "is the rate in the set?" but "is it the rate of THE LOT these units came from, and
    /// did that lot even have this many units?".</para>
    /// <para>DERIVED HERE, NOT IN <see cref="Reference"/>, and by this file's OWN walk of the spec: the
    /// arithmetic under audit lives in Reference.BuildStack, and a table produced by that arithmetic could
    /// not audit it. The only thing shared is the ORDERING key and the compound-unit conversion, both of
    /// which are pinned independently by CHECK 5 (the quantity oracle) and by CHECK 4 (calibration).</para>
    /// </summary>
    public static string InwardLots(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var lots = new List<(DateOnly Date, int CountLast, int Number, int Seq, string Text)>();

        if (item.OpeningQty > 0m)
            lots.Add((DateOnly.MinValue, 0, int.MinValue, int.MinValue,
                LotToken.Opening + ":" + Plain(item.OpeningQty) + "@" + Plain(item.OpeningRate)));

        foreach (var m in s.MovementsOf(item))
        {
            if (m.Cancelled) continue;
            if (m.Date > asOf) continue;
            if (m.Kind != MoveKind.Inward) continue;

            // A compound line states qty in the LINE's unit and rate PER THAT UNIT, so base qty scales up
            // and base rate scales down. Same operand order as Reference.BaseQty/BaseRate, so the decimal
            // arithmetic is bit-identical — a lot table that rounded differently would convict the reference
            // for a rounding artefact.
            var baseQty = m.UseCompoundUnit
                ? m.Qty * Corpus.CompoundNumerator / Corpus.CompoundDenominator
                : m.Qty;
            var rate = m.Rate is not { } r ? "-"
                : Plain(m.UseCompoundUnit ? r * Corpus.CompoundDenominator / Corpus.CompoundNumerator : r);

            lots.Add((m.Date, 0, m.Number, m.Seq, LotToken.For(m) + ":" + Plain(baseQty) + "@" + rate));
        }

        return string.Join(';', lots
            .OrderBy(t => t.Date).ThenBy(t => t.CountLast).ThenBy(t => t.Number).ThenBy(t => t.Seq)
            .Select(t => t.Text));
    }

    /// <summary>
    /// THE ORDERING FACT — the tokens a surviving cost layer is ALLOWED to name, given nothing but the
    /// spec's QUANTITIES. Returns <c>"*"</c> when the book never runs dry (unconstrained), otherwise the
    /// ';'-joined tokens of every event STRICTLY AFTER the last point at which the company-wide net
    /// quantity was &lt;= 0.
    /// <para>WHY. Audit #3 asked for an ordering property and audit #4 recorded that it was not built.
    /// The gap it closes is real and was demonstrated: a poisoned reference that RESURRECTS the drained
    /// lot's units after a repayment binds every layer TRUTHFULLY to a real lot at that lot's real spec
    /// rate, so the whole lot-origin rate binding passes 0/0/0/0. Only an ORDERING fact kills it.</para>
    /// <para>THE ARGUMENT, in one line: the company-wide net quantity is &lt;= 0 exactly when the layer
    /// stack is empty, so NOTHING created at or before that point can still be on the stack afterwards.
    /// A layer naming an earlier token is claiming units that were provably consumed.</para>
    /// <para>DERIVED BY A PURE QUANTITY WALK — no rate, no cost, no layer arithmetic — so it cannot share
    /// a mistake with the VALUE branch it audits. The quantities it walks are pinned independently by
    /// CHECK 5 (the quantity oracle) and CHECK 4 (calibration).</para>
    /// </summary>
    public static string PostDryLots(Scenario s, ItemSpec item, DateOnly asOf)
    {
        // ROUND 10 — walked with THE DEBT GATE (see FlatNet): where no key is short, a shortfall is
        // DISCARDED rather than owed, so the stack does not go below empty and the dry point is the
        // outward that emptied it, not a later one. Unchanged on every single-key book.
        var ordered = FlatStream(s, item, asOf);
        var (before, final) = FlatNet(ordered);

        var lastDry = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            var after = i + 1 < ordered.Count ? before[i + 1] : final;
            if (after <= 0m) lastDry = i;
        }

        if (lastDry < 0) return "*";                        // never dry: the ordering rule constrains nothing
        return string.Join(';', ordered.Skip(lastDry + 1).Select(t => t.Token));
    }

    /// <summary>
    /// THE INVENTED-POPULATION FACT — whether this (item, as-of) rests on a rule NOTHING calibrates,
    /// derived FROM THE SPEC rather than read off the tag the reference emits about itself.
    /// <para>AUDIT #5 FINDING [3] (LOW). CHECK 4c's coverage half iterates <c>RefProvenance</c>, which
    /// Reference sets from its OWN replay. The total-collapse case was already defended (zero INVENTED
    /// subjects is a harness failure), but a PARTIAL retag was not: if a refactor stopped setting
    /// CountWithDebtOutstanding, G6-001's three subjects would silently drop from INVENTED to BRIEF,
    /// coverage would still pass on G7's remaining six, and nothing would move except a table nobody
    /// diffs. The comparator now asserts the emitted population EQUALS this one, and pins its size.</para>
    /// <para>INDEPENDENT BY CONSTRUCTION: this is a PURE QUANTITY WALK. It touches no rate, no cost and no
    /// layer arithmetic — the only spec facts it reads besides quantities are whether an inward carries a
    /// rate at all and whether a movement is a count. The equivalence it relies on is exact and one line:
    /// the reference's outstanding debt is positive at an event precisely when the company-wide net
    /// quantity before that event is negative (an inward always repays the debt before adding a layer, so
    /// debt and layers are never both non-zero, and net = layer quantity - debt throughout).</para>
    /// <list type="bullet">
    ///   <item>a physical COUNT taken while the net is negative -> the count-with-debt rule fired;</item>
    ///   <item>an UNRATED inward arriving while the net is negative -> the unrated-repayment rule fired.</item>
    /// </list>
    /// Either one makes the subject INVENTED. Returns false otherwise (CALIBRATED or BRIEF — a distinction
    /// this fact deliberately does not make, because only the INVENTED population is what it is pinning).
    /// </summary>
    public static bool InventedByRule(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var ordered = FlatStream(s, item, asOf);
        var (before, _) = FlatNet(ordered);

        for (var i = 0; i < ordered.Count; i++)
        {
            // The test is on the state BEFORE the event — that is when the reference reads st.Debt > 0.
            if (before[i] < 0m && ordered[i].Kind == MoveKind.Count) return true;
            if (before[i] < 0m && ordered[i].Kind == MoveKind.Inward && !ordered[i].Rated) return true;
        }
        return false;
    }

    /// <summary>
    /// THE DEBT-SHAPE FACT — what actually happened to this (item, as-of), derived from the SPEC, as the
    /// <c>;</c>-joined names of the properties that hold ("none" when none do).
    /// <para>AUDIT #6, LOW [1]. CHECK 4c's clause coverage was asserted entirely from SELF-DECLARED LABELS:
    /// <c>Goldens.RequiredClauses</c> was checked against <c>Goldens.All.Concat(Goldens.Issue)
    /// .Select(g =&gt; g.Clause)</c>, a projection of the very table under audit. Nothing anywhere asked
    /// whether a golden tagged <c>issue:debt-outstanding</c> is actually taken with a debt outstanding, so
    /// a table that pinned the RIGHT numbers under the WRONG labels reported full clause coverage while
    /// leaving a clause genuinely unexercised — and re-tagging a golden was enough to manufacture coverage
    /// out of nothing. Self-attestation is the finding that has recurred in every audit of this harness;
    /// this closes the last instance of it. The comparator now derives the property HERE and requires the
    /// label to be TRUE of its subject; a mismatch is a HARNESS failure (exit 3).</para>
    /// <para>INDEPENDENT BY CONSTRUCTION, exactly like <see cref="InventedByRule"/>: this is a PURE
    /// QUANTITY WALK. It reads quantities, whether a movement is a count, and whether an inward carries a
    /// rate at all. No rate value, no cost, no layer arithmetic — so it cannot share a mistake with the
    /// branch whose labels it audits. The equivalence it rests on is the same one-liner: the reference's
    /// outstanding debt is positive at an event precisely when the company-wide net quantity before that
    /// event is negative, because an inward always repays the debt before adding a layer, so debt and
    /// layers are never both non-zero and net = layer quantity - debt throughout.</para>
    /// <list type="bullet">
    ///   <item><c>everDebt</c> — the net went negative at some point, so a debt existed.</item>
    ///   <item><c>debtAtAsOf</c> — the net is STILL negative at the as-of date, so a debt is outstanding.</item>
    ///   <item><c>repaidByRatedInward</c> — a RATED inward arrived while the net was negative.</item>
    ///   <item><c>repaidByUnratedInward</c> — an UNRATED inward arrived while the net was negative.</item>
    ///   <item><c>repaidAcrossMultipleInwards</c> — an inward arrived while the net was negative and was
    ///     swallowed WHOLE by the debt (net stayed &lt;= 0), so it left no surviving layer and the recovery
    ///     had to continue into a later lot. This is the property the <c>repay:multiple-lots</c> tag names;
    ///     note it must be <c>&lt;= 0</c>, not <c>&lt; 0</c> — on G2-004 a 20-unit lot clears a 20-unit debt
    ///     exactly, contributes nothing, and the surviving stock comes entirely from the NEXT lot.</item>
    ///   <item><c>countWithDebt</c> — a physical count taken while the net was negative.</item>
    ///   <item><c>countAfterRepay</c> — a physical count taken with the net non-negative on a book that had
    ///     already carried a debt.</item>
    ///   <item><c>debtFromEmptyStack</c> — an outward taken against a net of EXACTLY zero, so the debt was
    ///     created outright rather than by draining a layer.</item>
    ///   <item><c>twoSuccessiveOverdraws</c> — an outward taken while the net was ALREADY negative, so the
    ///     debt accumulated without an intervening repayment.</item>
    /// </list>
    /// </summary>
    public static string DebtShape(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var ordered = FlatStream(s, item, asOf);
        var (before, final) = FlatNet(ordered);
        var flags = new SortedSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var b = before[i];                      // the state the reference reads as st.Debt
            var after = i + 1 < ordered.Count ? before[i + 1] : final;
            switch (e.Kind)
            {
                case MoveKind.Count:
                    if (b < 0m) flags.Add("countWithDebt");
                    else if (flags.Contains("everDebt")) flags.Add("countAfterRepay");
                    break;

                case MoveKind.Inward:
                    if (b < 0m)
                    {
                        flags.Add(e.Rated ? "repaidByRatedInward" : "repaidByUnratedInward");
                        // Swallowed whole: no surplus joined, so the surviving stock came from a LATER lot.
                        if (b + e.Qty <= 0m) flags.Add("repaidAcrossMultipleInwards");
                    }
                    break;

                default:
                    if (e.Qty > 0m)
                    {
                        if (b == 0m) flags.Add("debtFromEmptyStack");
                        if (b < 0m) flags.Add("twoSuccessiveOverdraws");
                    }
                    break;
            }
            if (after < 0m) flags.Add("everDebt");
        }

        if (final < 0m) flags.Add("debtAtAsOf");
        return flags.Count == 0 ? "none" : string.Join(';', flags);
    }

    private static string Plain(decimal d)
        => d.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The company-wide aggregate facts for a scenario as of a date — the union across every item. This is
    /// what check 9 judges <c>TotalClosingStockValue</c> against (audit H3: the actual Balance-Sheet figure
    /// all three previous failures broke was emitted and excluded from every check).
    /// </summary>
    public static AsOfFacts Aggregate(Scenario s, DateOnly asOf)
    {
        AsOfFacts? acc = null;
        foreach (var item in s.Items)
        {
            var f = For(s, item, asOf);
            acc = acc is null ? f : AsOfFacts.Union(acc, f);
        }
        return acc!;
    }
}
