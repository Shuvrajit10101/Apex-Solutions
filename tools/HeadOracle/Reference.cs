namespace HeadOracle;

// ===================================================================================================
// THE POINT ORACLE — an INDEPENDENT reference implementation of stock valuation.
//
// WHY THIS EXISTS (rework brief, "THE CONCEPTUAL FINDING THAT MATTERS MOST):
//   The absolute band checks are necessary but NOT sufficient. G1-001 — the scenario the corpus labels
//   THE CRUX — is In 10 @ Rs 100.13 -> Out 25 -> In 40 @ Rs 7.91. HEAD reports 25 units @ Rs 316.40;
//   FIFO-correct is 25 x Rs 7.91 = Rs 197.75. A 60% overstatement that passes every band check, because
//   the rate band [7.91, 100.13] is 12.7x wide and 316.40 sits under the 1,317.70 total spend.
//   A band cannot convict a wrong-but-plausible value. Only a POINT ORACLE can.
//
// WHAT IT IS
//   Textbook cost-layer arithmetic computed ONLY from the Scenario spec. This file references NOTHING
//   from Apex.Ledger — not a type, not an enum, not a service. Costing methods are plain strings. If it
//   ever compiled against the engine it would stop being an oracle and become an echo.
//
// HOW IT EARNS TRUST — THE CALIBRATION GATE (check 4, hard-fail)
//   On every N* (never-negative) scenario x all 6 costing methods x every as-of date, the reference must
//   equal HEAD EXACTLY. HEAD is trusted on never-negative books — that is the whole premise of the
//   byte-identity check. If the reference disagrees there, THE REFERENCE IS WRONG: fix the reference,
//   never the engine. Only after that gate is green does the reference become the oracle on the G* books
//   where HEAD is not trusted.
//
// ===================================================================================================
// THE SEMANTICS THIS FILE IMPLEMENTS, IN PLAIN ENGLISH
// ===================================================================================================
//
// COST LAYERS. An inward creates a cost layer (quantity, unit cost). An outward consumes layers oldest
// -first (FIFO) or newest-first (LIFO). Closing value = the sum of the surviving layers, snapped to the
// paisa. Nothing surprising — this is the textbook definition, and on a never-negative book it is
// exactly what the engine does.
//
// THE DEBT RULE (the only part that is not textbook, because over-drawn stock is not in the textbook):
//
//   1. When an outward asks for more than the layers hold, it takes everything the layers have and the
//      SHORTFALL BECOMES A DEBT QUANTITY. A debt carries NO rate — nothing has been bought to cost it.
//   2. A later inward REPAYS the debt FIRST, at that inward lot's own rate. The repaid units never
//      become a layer: they were already issued, so their cost is the incoming lot's rate and it flows
//      to COGS, not to the Balance Sheet. Only the remainder of the lot becomes a layer.
//   3. AN EXISTING DEBT IS NEVER RE-RATED. Repayment does not go back and restate anything. This is
//      the rule whose absence produced the 18x Balance-Sheet error in a previous attempt.
//   4. Invariant: a debt can only exist while the layer stack is EMPTY (a debt is created only after
//      the layers are drained, and any inward repays the debt before adding a layer). So "net book
//      quantity" is always SumQty(layers) - debt, and at most one of the two is non-zero.
//   5. A PHYSICAL COUNT reconciles to the counted total: it writes the debt off and tops the stack up
//      to the counted quantity. The topped-up units are costed by the best-available-cost chain
//      (running average -> standard cost -> last rated inward -> 0), which is the engine's own
//      documented policy for units that carry no rate. HEAD instead uses the running average alone,
//      which is 0 after an over-draw, so HEAD values those real units at Rs 0 — a wiped asset. On a
//      never-negative book the running average is positive and the two rules coincide exactly, which is
//      why this difference cannot break calibration.
//   6. An issue larger than what the layers hold costs only what the layers hold (the rest has no cost
//      to remove). Same as HEAD.
//
//   Worked, on THE CRUX (G1-001, FIFO, as of 2024-04-20):
//     In 10 @ 100.13  -> layers [(10, 100.13)]                     debt 0
//     Out 25          -> layers []               , 15 short        debt 15
//     In 40 @ 7.91    -> repay 15 @ 7.91 (to COGS), layers [(25, 7.91)]  debt 0
//     closing = 25 x 7.91 = Rs 197.75.   HEAD says Rs 316.40 (it kept the whole 40-unit lot).
//
// MOVING AVERAGE — THE HISTORY MATTERS, SO READ IT.
//   <see cref="RunAverage"/> is a perpetual weighted average: an inward adds (qty, qty x rate) to the
//   pool; an outward removes qty at the CURRENT average, so the average is unchanged; a count restates
//   the pool to counted x current average; and when the pool goes non-positive it RESETS to zero. It is
//   DELIBERATELY IDENTICAL TO HEAD, INCLUDING THE RESET. It is retained ONLY because
//   <see cref="LastPurchaseRate"/> falls back to it when a book has no rated inward at all, and the
//   engine does exactly the same — changing it would make the flat methods diverge from a correct oracle.
//   IT IS NO LONGER THE AverageCost ORACLE.
//
//   <see cref="RunAverageDebtAware"/> IS. It applies the SAME debt semantics the cost-layer reference
//   applies (an over-draw is a debt; a later inward repays it at its own rate and only the surplus joins
//   the pool; an existing debt is never re-rated; a count writes the debt off and restates the pool at
//   the best-available-cost chain). On 2026-07-27, by a USER SCOPE DECISION that AverageCost IS to be
//   fixed, BOTH AverageCost columns moved onto it: RefClosingValueDebtAwarePaisa (which CHECK 2 judges
//   against) and RefClosingValuePaisa (from which CHECK 10 and CHECK 9(b) are derived). AverageCost is
//   therefore NO LONGER AN ECHO OF HEAD, and the ECHO-OF-HEAD provenance tag was retired with it.
//
//   TWO CONSEQUENCES, STATED PLAINLY BECAUSE AUDIT #4 FOUND BOTH UNDERSTATED:
//     * the two AverageCost reference columns are now THE SAME COMPUTATION BY CONSTRUCTION, so any gate
//       comparing them is a tautology and PART A no longer prints one as evidence;
//     * CHECK 4b calibrates this function only on never-negative books, where its debt clauses are DEAD
//       CODE — so calibration validates NOTHING about the clauses that produce CHECK 2's convictions.
//       That hole is closed by CHECK 4c, the HAND-DERIVED GOLDENS in Goldens.cs, and by nothing else.
//
// FLAT METHODS. StandardCost = the item's standard cost (INCLUDING an explicit zero — that is the
// `is { } sc` vs `sc.Amount > 0m` trap family N6 exists for), falling back to last-purchase when unset.
// LastPurchaseCost = the most recent RATED inward, else running average, else standard cost, else the
// last rated inward, else 0. LastSaleCost = the most recent RATED outward, else the last-purchase
// chain. Closing value = closing quantity x that single rate, snapped to the paisa.
//
// UNITS. A line stated in a compound unit carries a rate PER THE LINE'S UNIT, so base quantity =
// qty x numerator / denominator and base rate = rate x denominator / numerator, in that operand order,
// so the decimal arithmetic matches the engine's to the last digit.
//
// QUANTITY. On-hand is replayed PER (item, godown, BATCH) key — a physical count checkpoints only its
// own key. Valuation, by contrast, replays a single COMPANY-WIDE stream that is batch-blind. That
// asymmetry is the engine's (InventoryLedger.Key is (item, godown, batch); StockValuationService merges
// one stream), and the reference mirrors it deliberately: family G9 pins the godown axis and family G12
// pins the batch axis.
//
// CANCELLED VOUCHERS. A cancelled voucher never counts, in either replay — the engine's own rule
// (InventoryLedger.Counts / StockValuationService.Counts both return false on Cancelled). Family G14
// pins it. Post-dated is NOT a separate axis: both engines reduce it to the same date bound, so a
// post-dated voucher dated on or before the as-of date counts exactly like any other, and G14 asserts
// that rather than assuming it.
// ===================================================================================================

/// <summary>What the reference says about one (scenario, item, method, as-of).</summary>
public sealed record RefValuation(
    decimal OnHandBase,
    decimal ClosingQty,
    decimal ClosingValueRupees);

/// <summary>Replay-derived quantities the spend ceiling needs (units that no rated purchase paid for).</summary>
public sealed record RefImputed(decimal UnratedInwardQty, decimal CountUpQty)
{
    public decimal Total => UnratedInwardQty + CountUpQty;
}

/// <summary>
/// Where a cost layer's unit rate came from. Emitted alongside the layer breakdown so the comparator can
/// assert — WITHOUT re-deriving the reference's own arithmetic — that every surviving layer is priced at a
/// rate the SPEC contains. <see cref="RunningAverage"/> is the one source that legitimately produces a rate
/// the spec does not contain (a weighted blend), so it is reported in its own bucket rather than convicted.
/// </summary>
public enum RateSource { Explicit, RunningAverage, StandardCost, LastRatedInward, Zero }

/// <summary>
/// The NAME OF A LOT, shared by the reference (which stamps it onto every cost layer it creates) and by
/// <see cref="Facts"/> (which publishes the SPEC's lot table under the same names). The comparator joins the
/// two to answer the question audit #3 finding [1] says nothing answered: not "is this layer's rate somewhere
/// in the admissible set?" but "is it the rate of THE PARTICULAR LOT these units came from?".
/// </summary>
public static class LotToken
{
    /// <summary>The opening-balance lot — it sorts before every dated voucher.</summary>
    public const string Opening = "OPEN";

    /// <summary>A count-up layer: units a physical count created, which no lot ever supplied.</summary>
    public static string Count(int seq) => "CNT" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The token for a movement — "CNT&lt;seq&gt;" for a count, otherwise the movement's own seq.</summary>
    public static string For(Movement m)
        => m.Kind == MoveKind.Count
            ? Count(m.Seq)
            : m.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// How much of the reference's authority on one subject rests on something that was actually VALIDATED.
/// <list type="bullet">
///   <item><b>CALIBRATED</b> — the replay used only code paths an <c>N*</c> book reaches, and CHECK 4
///     asserts those agree with HEAD exactly.</item>
///   <item><b>BRIEF</b> — the replay carried a debt that a RATED inward repaid. The rework brief states
///     this rule verbatim ("an over-draw is carried as a debt quantity and a later inward repays it at
///     the incoming lot rate, and an existing debt is never re-rated"), but no <c>N*</c> book reaches it,
///     so calibration cannot see it.</item>
///   <item><b>INVENTED</b> — the replay hit a rule NEITHER calibrated NOR stated in the brief: a physical
///     count taken while a debt was outstanding, or an UNRATED inward repaying a debt. The reference
///     writes the debt off and prices the units through the best-available-cost chain; HEAD uses the
///     running average alone. Rs 9.77 for units nobody bought is a CHOICE, not a derivation.</item>
/// </list>
/// <para>THE <c>ECHO-OF-HEAD</c> TAG WAS RETIRED ON 2026-07-27 (audit #4 finding [3], MEDIUM). It was
/// applied unconditionally to all 187 AverageCost subjects and became FALSE the moment
/// <see cref="Reference.Value"/>'s AverageCost arm moved to <see cref="Reference.RunAverageDebtAware"/>:
/// the column stopped being an echo and started issuing CHECK 2's engine verdicts. Worse, it CONCEALED
/// exposure in both directions — it told the reader a verdict-issuing oracle was "not a reference at all",
/// and it kept the AverageCost subjects that rest on the count-with-debt and unrated-repayment rules OUT
/// of the INVENTED count, so a user ratifying those rules for Fifo/Lifo would not have known they were
/// also ratifying live AverageCost convictions. AverageCost is now tagged from the SAME debt flags as the
/// layer methods, recorded by RunAverageDebtAware as it replays.</para>
/// </summary>
public static class RefProvenance
{
    public const string Calibrated = "CALIBRATED";
    public const string Brief = "BRIEF";
    public const string Invented = "INVENTED";
}

public static class Reference
{
    public const string AverageCost = "AverageCost";
    public const string Fifo = "Fifo";
    public const string Lifo = "Lifo";
    public const string StandardCost = "StandardCost";
    public const string LastPurchaseCost = "LastPurchaseCost";
    public const string LastSaleCost = "LastSaleCost";

    // ---------------------------------------------------------------- event model

    private enum Kind { Inward, Outward, Count }

    /// <summary>
    /// One valuation event. <paramref name="Origin"/> NAMES THE LOT: "OPEN" for the opening lot, the
    /// movement's <c>Seq</c> (unique inside a scenario) for a voucher-borne inward or outward, and
    /// "CNT&lt;seq&gt;" for a physical count. It is carried onto every cost layer the event creates so the
    /// COMPARATOR can look the layer's rate up IN THE SPEC — see <see cref="LayerOrigins"/>.
    /// </summary>
    private readonly record struct Ev(Kind What, decimal Qty, decimal? Rate, string Origin);

    /// <summary>Base quantity of a movement (compound lines scale by numerator/denominator).</summary>
    private static decimal BaseQty(Movement m)
        => m.UseCompoundUnit
            ? m.Qty * Corpus.CompoundNumerator / Corpus.CompoundDenominator
            : m.Qty;

    /// <summary>The movement's rate re-expressed per BASE unit — the exact inverse of <see cref="BaseQty"/>.</summary>
    private static decimal? BaseRate(Movement m)
        => m.Rate is not { } r ? null
            : m.UseCompoundUnit
                ? r * Corpus.CompoundDenominator / Corpus.CompoundNumerator
                : r;

    /// <summary>
    /// The company-wide valuation event stream for one item as of a date: the opening lot first (it sorts
    /// before every dated voucher), then movements ordered by (date, physical-count-last, number, seq) —
    /// the same total order the engine's replay uses, made reproducible by the corpus pinning every
    /// voucher number and every voucher Guid.
    /// </summary>
    private static List<Ev> Events(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var tagged = new List<(DateOnly Date, int PhysicalLast, int Number, int Seq, Ev E)>();

        if (item.OpeningQty > 0m)
            tagged.Add((DateOnly.MinValue, 0, int.MinValue, int.MinValue,
                new Ev(Kind.Inward, item.OpeningQty, item.OpeningRate, LotToken.Opening)));

        foreach (var m in s.MovementsOf(item))
        {
            if (m.Cancelled) continue;          // a cancelled voucher never counts, in either engine
            if (m.Date > asOf) continue;
            var ev = m.Kind switch
            {
                MoveKind.Inward => new Ev(Kind.Inward, BaseQty(m), BaseRate(m), LotToken.For(m)),
                MoveKind.Outward => new Ev(Kind.Outward, BaseQty(m), BaseRate(m), LotToken.For(m)),
                _ => new Ev(Kind.Count, m.Qty, null, LotToken.For(m)),   // a counted quantity is always in base units
            };
            tagged.Add((m.Date, m.Kind == MoveKind.Count ? 1 : 0, m.Number, m.Seq, ev));
        }

        return tagged
            .OrderBy(t => t.Date).ThenBy(t => t.PhysicalLast).ThenBy(t => t.Number).ThenBy(t => t.Seq)
            .Select(t => t.E)
            .ToList();
    }

    // ---------------------------------------------------------------- quantity oracle

    /// <summary>
    /// Raw on-hand for an item as of a date: replayed PER (item, godown) key and summed, with a physical
    /// count checkpointing only its own key. This is the quantity a Balance Sheet reports; a fix that
    /// returns quantity 0 and value 0 is caught here (audit H2) and nowhere else.
    /// </summary>
    public static decimal OnHand(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var total = 0m;
        foreach (var (g, batch) in Keys(s, item))
        {
            // The opening lot carries no batch label, so it belongs to the "" batch of its godown.
            var running = item.OpeningQty > 0m && item.OpeningGodown == g && batch.Length == 0
                ? item.OpeningQty
                : 0m;
            foreach (var m in s.MovementsOf(item)
                         .Where(m => !m.Cancelled && m.Godown == g && m.Batch == batch && m.Date <= asOf)
                         .OrderBy(m => m.Date)
                         .ThenBy(m => m.Kind == MoveKind.Count ? 1 : 0)
                         .ThenBy(m => m.Number)
                         .ThenBy(m => m.Seq))
            {
                running = m.Kind switch
                {
                    MoveKind.Count => m.Qty,
                    MoveKind.Inward => running + BaseQty(m),
                    _ => running - BaseQty(m),
                };
            }
            total += running;
        }
        return total;
    }

    /// <summary>
    /// Every (godown, batch) key an item's movements touch, plus the opening lot's key. The engine's on-hand
    /// register is keyed by (item, godown, BATCH) — InventoryLedger.Key — so a reference that replayed per
    /// godown alone would silently agree with a fix that keyed off the wrong tuple. Family G12 exists to
    /// make this axis measurable.
    /// </summary>
    private static IEnumerable<(int Godown, string Batch)> Keys(Scenario s, ItemSpec item)
    {
        var keys = new SortedSet<(int, string)>();
        if (item.OpeningQty > 0m) keys.Add((item.OpeningGodown, ""));
        foreach (var m in s.MovementsOf(item))
        {
            if (m.Cancelled) continue;
            keys.Add((m.Godown, m.Batch));
        }
        if (keys.Count == 0) keys.Add((0, ""));
        return keys;
    }

    /// <summary>
    /// TRUE when the scenario never drives ANY (item, godown, batch) on-hand negative AND never drains the
    /// company-wide cost-layer stack into a debt, for any item, at any point in its whole movement history.
    /// <para>This is the SPEC-DERIVED predicate that scopes byte-identity (check 1) and calibration
    /// (check 4). Scoping them by "the scenario id starts with the letter N" left E1 — a genuinely
    /// never-negative family — outside BOTH, so the reference was used as an oracle on E1 without ever
    /// having been calibrated there, and an ordering change that perturbed both E1 scenarios identically
    /// would not have tripped byte identity.</para>
    /// </summary>
    public static bool NeverNegative(Scenario s)
    {
        var last = s.Movements.Count == 0 ? DateOnly.MaxValue : s.Movements.Max(m => m.Date);
        foreach (var item in s.Items)
        {
            foreach (var (g, batch) in Keys(s, item))
            {
                var running = item.OpeningQty > 0m && item.OpeningGodown == g && batch.Length == 0
                    ? item.OpeningQty
                    : 0m;
                foreach (var m in s.MovementsOf(item)
                             .Where(m => !m.Cancelled && m.Godown == g && m.Batch == batch)
                             .OrderBy(m => m.Date)
                             .ThenBy(m => m.Kind == MoveKind.Count ? 1 : 0)
                             .ThenBy(m => m.Number)
                             .ThenBy(m => m.Seq))
                {
                    running = m.Kind switch
                    {
                        MoveKind.Count => m.Qty,
                        MoveKind.Inward => running + BaseQty(m),
                        _ => running - BaseQty(m),
                    };
                    if (running < 0m) return false;
                }
            }

            var events = Events(s, item, last);
            if (BuildStack(events, lifo: false, ChainFor(item, events)).DebtEverCreated) return false;
        }
        return true;
    }

    /// <summary>The closing quantity the valuation contract reports: the rounded on-hand, or 0 when it is
    /// not positive (an item with no stock closes at exactly zero value).</summary>
    public static decimal ClosingQty(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var q = RoundQty(OnHand(s, item, asOf));
        return q > 0m ? q : 0m;
    }

    // ---------------------------------------------------------------- the cost-layer replay

    /// <summary><paramref name="Origin"/> names the LOT these units came from — see <see cref="LotToken"/>.</summary>
    private sealed record Layer(decimal Qty, decimal Unit, RateSource Src, string Origin);

    /// <summary>Mutable replay state: the surviving layers, the outstanding debt, and the running pool.</summary>
    private sealed class Stack
    {
        public readonly List<Layer> Layers = [];
        public decimal Debt;
        public decimal RunQty;
        public decimal RunCost;
        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;

        // ---- provenance flags: WHICH of the reference's rules this replay actually depended on.
        /// <summary>An outward asked for more than the layers held at least once.</summary>
        public bool DebtEverCreated;
        /// <summary>A RATED inward repaid a debt — the rule the rework brief states verbatim.</summary>
        public bool DebtRepaidByRatedInward;
        /// <summary>An UNRATED inward repaid a debt — a rule NOTHING states. Priced off the fallback chain.</summary>
        public bool DebtRepaidByUnratedInward;
        /// <summary>A physical count landed while a debt was outstanding — a rule NOTHING states.</summary>
        public bool CountWithDebtOutstanding;

        public string Provenance =>
            CountWithDebtOutstanding || DebtRepaidByUnratedInward ? RefProvenance.Invented
            : DebtEverCreated ? RefProvenance.Brief
            : RefProvenance.Calibrated;
    }

    /// <summary>
    /// The per-item cost fallbacks for a movement that carries no rate: the item's standard cost when it
    /// is strictly positive, and the most-recent rated inward anywhere in the as-of window.
    /// </summary>
    private readonly record struct Chain(decimal? PositiveStandardCost, decimal? LastRatedInward)
    {
        public decimal NoRateCost(decimal runningAverage) => NoRateCostTagged(runningAverage).Rate;

        /// <summary>The chain, plus WHICH link answered — so a surviving layer can be traced to the spec.</summary>
        public (decimal Rate, RateSource Src) NoRateCostTagged(decimal runningAverage)
        {
            if (runningAverage > 0m) return (runningAverage, RateSource.RunningAverage);
            if (PositiveStandardCost is { } sc) return (sc, RateSource.StandardCost);
            if (LastRatedInward is { } r) return (r, RateSource.LastRatedInward);
            return (0m, RateSource.Zero);
        }

        /// <summary>
        /// The rates a surviving layer is ALLOWED to carry, given nothing but the spec: every rated inward
        /// rate in the as-of window, plus a strictly-positive standard cost, plus zero ONLY when the chain
        /// has no other link to reach (which is the only way <see cref="RateSource.Zero"/> can fire).
        /// A <see cref="RateSource.RunningAverage"/> layer is legitimately outside this set (it is a blend)
        /// and is excused BY ITS TAG, not by widening the set.
        /// </summary>
        public SortedSet<decimal> Admissible(IEnumerable<decimal> ratedInwardRates)
        {
            var set = new SortedSet<decimal>(ratedInwardRates);
            if (PositiveStandardCost is { } sc) set.Add(sc);
            if (set.Count == 0) set.Add(0m);
            return set;
        }
    }

    private static Chain ChainFor(ItemSpec item, List<Ev> events)
    {
        var std = Corpus.StandardCostOf(item);
        return new Chain(std is { } s && s > 0m ? s : null, LastRatedInwardRate(events));
    }

    private static decimal? LastRatedInwardRate(List<Ev> events)
    {
        decimal? last = null;
        foreach (var e in events)
            if (e.What == Kind.Inward && e.Rate is { } r) last = r;
        return last;
    }

    private static decimal? LastRatedOutwardRate(List<Ev> events)
    {
        decimal? last = null;
        foreach (var e in events)
            if (e.What == Kind.Outward && e.Rate is { } r) last = r;
        return last;
    }

    private static decimal RunningAverage(decimal qty, decimal cost) => qty > 0m ? cost / qty : 0m;

    private static decimal SumQty(List<Layer> layers)
    {
        var q = 0m;
        foreach (var l in layers) q += l.Qty;
        return q;
    }

    /// <summary>
    /// Drains <paramref name="quantity"/> from the stack by the chosen order. Anything the layers cannot
    /// satisfy becomes DEBT — the one rule the engine does not have, and the reason this file exists.
    /// </summary>
    private static void Consume(Stack st, decimal quantity, bool lifo)
    {
        var remaining = quantity;
        while (remaining > 0m && st.Layers.Count > 0)
        {
            var idx = lifo ? st.Layers.Count - 1 : 0;
            var layer = st.Layers[idx];
            var take = Math.Min(layer.Qty, remaining);
            st.RunQty -= take;
            st.RunCost -= take * layer.Unit;
            remaining -= take;
            if (take >= layer.Qty) st.Layers.RemoveAt(idx);
            else st.Layers[idx] = new Layer(layer.Qty - take, layer.Unit, layer.Src, layer.Origin);
        }
        if (remaining > 0m) { st.Debt += remaining; st.DebtEverCreated = true; }   // <-- THE DEBT RULE
        if (st.RunQty <= 0m) { st.RunQty = 0m; st.RunCost = 0m; }
    }

    private static Stack BuildStack(List<Ev> events, bool lifo, Chain chain)
    {
        var st = new Stack();
        foreach (var e in events)
        {
            switch (e.What)
            {
                case Kind.Inward:
                {
                    var (chained, chainSrc) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                    var unit = e.Rate ?? chained;
                    var src = e.Rate is null ? chainSrc : RateSource.Explicit;
                    var qty = e.Qty;
                    if (st.Debt > 0m)
                    {
                        // Repay at the INCOMING lot's rate; repaid units go to COGS, never to a layer.
                        var repay = Math.Min(st.Debt, qty);
                        st.Debt -= repay;
                        qty -= repay;
                        if (e.Rate is null) st.DebtRepaidByUnratedInward = true;
                        else st.DebtRepaidByRatedInward = true;
                    }
                    if (qty > 0m)
                    {
                        st.Layers.Add(new Layer(qty, unit, src, e.Origin));
                        st.RunQty += qty;
                        st.RunCost += qty * unit;
                    }
                    break;
                }
                case Kind.Outward:
                    Consume(st, e.Qty, lifo);
                    break;
                case Kind.Count:
                {
                    // A physical count is an ABSOLUTE statement of what is on the shelf, so it
                    // supersedes the book entirely: the debt is written off first and the stack is
                    // then reconciled to the counted quantity. Topping up by (counted + debt) would
                    // leave the layer stack holding more units than on-hand reports — the reference's
                    // own quantity and value would stop agreeing. (The on-hand replay writes the
                    // negative off at the count too, which is why this is the only consistent rule.)
                    // On a never-negative book Debt is always 0, so this is a no-op there and
                    // calibration is unaffected.
                    if (st.Debt > 0m) st.CountWithDebtOutstanding = true;
                    st.Debt = 0m;
                    var current = SumQty(st.Layers);
                    if (e.Qty < current)
                    {
                        Consume(st, current - e.Qty, lifo);
                    }
                    else if (e.Qty > current)
                    {
                        var (unit, src) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                        var add = e.Qty - current;
                        st.Layers.Add(new Layer(add, unit, src, e.Origin));
                        st.RunQty += add;
                        st.RunCost += add * unit;
                        st.CountUpQty += add;
                    }
                    break;
                }
            }
        }
        return st;
    }

    // ---------------------------------------------------------------- moving average (HEAD-aligned by decision)

    private static (decimal Average, decimal Quantity) RunAverage(List<Ev> events, Chain chain)
    {
        var qty = 0m;
        var cost = 0m;
        foreach (var e in events)
        {
            switch (e.What)
            {
                case Kind.Inward:
                {
                    var unit = e.Rate ?? chain.NoRateCost(RunningAverage(qty, cost));
                    qty += e.Qty;
                    cost += e.Qty * unit;
                    break;
                }
                case Kind.Outward:
                {
                    var unit = RunningAverage(qty, cost);   // an outward at the average leaves it unchanged
                    qty -= e.Qty;
                    cost -= e.Qty * unit;
                    if (qty <= 0m) { qty = 0m; cost = 0m; } // pool exhausted: nothing left to average
                    break;
                }
                case Kind.Count:
                {
                    var unit = RunningAverage(qty, cost);
                    cost = e.Qty * unit;
                    qty = e.Qty;
                    break;
                }
            }
        }
        return (RunningAverage(qty, cost), qty);
    }

    // ------------------------------------------- moving average, DEBT-AWARE (genuinely independent)

    /// <summary>
    /// A moving average that applies THE SAME debt semantics as the cost-layer reference, so it is a real
    /// second opinion rather than an echo:
    /// <list type="number">
    ///   <item>an outward larger than the pool empties the pool and the shortfall becomes a DEBT;</item>
    ///   <item>a later inward repays the debt AT ITS OWN RATE — those units are already issued, so their
    ///     cost goes to COGS and only the SURPLUS joins the pool;</item>
    ///   <item>an existing debt is never re-rated;</item>
    ///   <item>a physical count writes the debt off and restates the pool to counted x (the running
    ///     average, or the best-available-cost chain when the average is nil).</item>
    /// </list>
    /// HEAD instead RESETS the pool at the over-draw and then re-averages every later inward, which makes
    /// the sign of its error the sign of the rate trend across the recovery lots: cheap-then-dear
    /// understates, dear-then-cheap OVERSTATES without bound.
    /// <para><b>THIS IS NOW FAILED ON.</b> Since the 2026-07-27 user scope decision, CHECK 2 convicts the
    /// engine against this function's output. Its DEBT CLAUSES — the four numbered above — are dead code on
    /// every book CHECK 4b can calibrate, so they are validated by CHECK 4c's hand-derived goldens alone.
    /// A change to any of them that CHECK 4c does not pin is a change nothing in this harness can see.</para>
    /// </summary>
    private static (decimal Average, decimal Quantity) RunAverageDebtAware(List<Ev> events, Chain chain)
        => RunAverageDebtAwareTraced(events, chain).Result;

    /// <summary>
    /// The debt-aware moving average, PLUS the provenance flags recording WHICH of the reference's debt
    /// rules this particular replay actually depended on. Added 2026-07-27 for audit #4 finding [3]: the
    /// AverageCost column issues CHECK 2's verdicts, so it must carry the same honest provenance the layer
    /// methods carry instead of the retired blanket "ECHO-OF-HEAD".
    /// </summary>
    private static ((decimal Average, decimal Quantity) Result, Stack Flags) RunAverageDebtAwareTraced(
        List<Ev> events, Chain chain)
    {
        var flags = new Stack();
        var qty = 0m;
        var cost = 0m;
        var debt = 0m;
        foreach (var e in events)
        {
            switch (e.What)
            {
                case Kind.Inward:
                {
                    var unit = e.Rate ?? chain.NoRateCost(RunningAverage(qty, cost));
                    var add = e.Qty;
                    if (debt > 0m)
                    {
                        var repay = Math.Min(debt, add);
                        debt -= repay;
                        add -= repay;               // repaid units are COGS, not pool
                        if (e.Rate is null) flags.DebtRepaidByUnratedInward = true;
                        else flags.DebtRepaidByRatedInward = true;
                    }
                    if (add > 0m)
                    {
                        qty += add;
                        cost += add * unit;
                    }
                    break;
                }
                case Kind.Outward:
                {
                    var unit = RunningAverage(qty, cost);
                    if (e.Qty > qty)
                    {
                        debt += e.Qty - qty;
                        flags.DebtEverCreated = true;
                        qty = 0m;
                        cost = 0m;
                    }
                    else
                    {
                        qty -= e.Qty;
                        cost -= e.Qty * unit;
                        if (qty <= 0m) { qty = 0m; cost = 0m; }
                    }
                    break;
                }
                case Kind.Count:
                {
                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = chain.NoRateCost(0m);
                    cost = e.Qty * unit;
                    qty = e.Qty;
                    break;
                }
            }
        }
        return ((RunningAverage(qty, cost), qty), flags);
    }

    /// <summary>
    /// The debt-aware moving-average CLOSING VALUE for one subject, in rupees. Emitted only for AverageCost.
    /// <para>ROUND 7 (audit #6) corrected this summary. It used to say this column existed because
    /// <see cref="Value"/> "is an echo of HEAD and therefore evidence of nothing" — false since round 4, when
    /// <see cref="Value"/>'s AverageCost arm moved onto the SAME <see cref="RunAverageDebtAware"/> call. The
    /// two columns are now identical BY CONSTRUCTION. The column is still emitted under its own name because
    /// CHECK 2 judges from it by name and CHECK 4c anchors BOTH columns to the same external hand-derived
    /// constants — which is what would convict them if they were ever un-linked.</para>
    /// </summary>
    public static decimal DebtAwareAverageValue(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var closingQty = ClosingQty(s, item, asOf);
        if (closingQty <= 0m) return 0m;
        var events = Events(s, item, asOf);
        return Paisa(RunAverageDebtAware(events, ChainFor(item, events)).Average * closingQty);
    }

    // ---------------------------------------------------------------- flat-method rates

    private static decimal LastPurchaseRate(List<Ev> events, Chain chain)
    {
        if (LastRatedInwardRate(events) is { } rated) return rated;
        var (avg, _) = RunAverage(events, chain);
        if (avg > 0m) return avg;
        return chain.PositiveStandardCost ?? chain.LastRatedInward ?? 0m;
    }

    private static decimal LastSaleRate(List<Ev> events, Chain chain)
        => LastRatedOutwardRate(events) ?? LastPurchaseRate(events, chain);

    // ---------------------------------------------------------------- public results

    /// <summary>The reference's verdict for one (scenario, item, method, as-of).</summary>
    public static RefValuation Value(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        var onHand = OnHand(s, item, asOf);
        var closingQty = RoundQty(onHand);
        if (closingQty <= 0m) return new RefValuation(onHand, 0m, 0m);

        var events = Events(s, item, asOf);
        var chain = ChainFor(item, events);

        var value = method switch
        {
            Fifo => LayerValue(BuildStack(events, lifo: false, chain)),
            Lifo => LayerValue(BuildStack(events, lifo: true, chain)),
            // 2026-07-27 USER SCOPE DECISION — AverageCost IS to be fixed, so the reference is now DEBT-AWARE
            // here too, and RefClosingValuePaisa for AverageCost has STOPPED BEING AN ECHO OF HEAD.
            // THIS HAD TO MOVE WITH CHECK 2, not after it. RefIssueValue (IssueValue() below routes every
            // non-Fifo/Lifo method through Value()) and RefTotalClosingPaisa (Emit sums ClosingValueRupees)
            // are both DERIVED from this line. Had it stayed an echo while CHECK 2 demanded the debt-aware
            // number, the harness would have demanded two different AverageCost answers on one subject and
            // CHECK 10 / CHECK 9(b) would have convicted the very engine CHECK 2 prescribes — precisely the
            // self-contradiction audit #2 finding [0] was about. PART A now ASSERTS the two columns agree.
            // Calibration is unaffected: on a never-negative book the debt clauses are dead code, which is
            // what CHECK 4b proves subject by subject rather than by assertion in a comment.
            AverageCost => Paisa(RunAverageDebtAware(events, chain).Average * closingQty),
            LastPurchaseCost => Paisa(LastPurchaseRate(events, chain) * closingQty),
            LastSaleCost => Paisa(LastSaleRate(events, chain) * closingQty),
            StandardCost => Paisa((Corpus.StandardCostOf(item) ?? LastPurchaseRate(events, chain)) * closingQty),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unknown costing method"),
        };

        return new RefValuation(onHand, closingQty, value);
    }

    /// <summary>
    /// The quantity the reference's own cost-layer stack holds. It MUST equal <see cref="ClosingQty"/>
    /// whenever the latter is positive — value and quantity have to come from the same book. Emitted as
    /// its own oracle column and asserted as a HARNESS-INTEGRITY invariant: an early draft of the debt
    /// rule topped a physical count up by (counted + debt) and held 23 units while reporting 8, and this
    /// invariant is what makes that class of mistake mechanical rather than a matter of noticing.
    /// </summary>
    public static decimal LayerQty(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        if (method is not (Fifo or Lifo)) return 0m;
        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));
        return SumQty(st.Layers);
    }

    // ------------------------------------ THE LAYER BREAKDOWN — the mechanical review artefact
    //
    // AUDIT FINDING: a VALUE-ONLY poison of the debt branch (`unit = 0m` on the surviving remainder of a
    // repaying lot) passes CHECK 4 CALIBRATION (N* never carries a debt), passes REFERENCE
    // SELF-CONSISTENCY (quantities untouched), and prints "HARNESS INTEGRITY : SOUND" — while demanding
    // Rs 0 on the crux. The quantity invariant is real; there was NO value invariant.
    //
    // These three columns supply one. They are SPEC-DERIVED (so corpus integrity pins them across arms),
    // they are consumed by the COMPARATOR (separate code from the arithmetic they audit), and together
    // they assert, without re-deriving the reference's own debt arithmetic:
    //   (a) the layer quantities sum to the reported closing quantity;
    //   (b) the layer values sum to the reported closing VALUE;
    //   (c) EVERY surviving layer's unit rate is a rate the SPEC actually contains — a rated inward rate
    //       in the as-of window, or a strictly-positive standard cost — unless its tag says the
    //       best-available-cost chain answered with a running-average BLEND, which is reported in its own
    //       bucket rather than convicted.
    // The `unit = 0m` poison writes a 0 rate onto a book whose spec contains {7.91, 100.13, 9.77}, so (c)
    // convicts it.

    /// <summary>The surviving layers as "qty@unitRate" pairs, oldest-first, joined by ';'.</summary>
    public static string LayerBreakdown(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        if (method is not (Fifo or Lifo)) return "-";
        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));
        if (st.Layers.Count == 0) return "";
        return string.Join(';', st.Layers.Select(l => Plain(l.Qty) + "@" + Plain(l.Unit)));
    }

    /// <summary>The rate SOURCE of each surviving layer, positionally aligned with the breakdown.</summary>
    public static string LayerRateSources(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        if (method is not (Fifo or Lifo)) return "-";
        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));
        return string.Join(';', st.Layers.Select(l => l.Src.ToString()));
    }

    /// <summary>
    /// The LOT each surviving layer's units came from, positionally aligned with the breakdown — see
    /// <see cref="LotToken"/>. This is the column that turns the value invariant from "is this rate in the
    /// admissible SET?" into "is this THE rate of THE lot these units came from?" (audit #3 finding [1]:
    /// a re-rating poison that prices a repayment surplus at the OLD lot's rate uses an ADMISSIBLE rate,
    /// so set-membership acquits it; a lot lookup convicts it).
    /// </summary>
    public static string LayerOrigins(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        if (method is not (Fifo or Lifo)) return "-";
        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));
        return string.Join(';', st.Layers.Select(l => l.Origin));
    }

    /// <summary>The rates the SPEC permits a surviving layer to carry, ascending, joined by ';'.</summary>
    public static string AdmissibleRates(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var events = Events(s, item, asOf);
        var rated = events.Where(e => e.What == Kind.Inward && e.Rate is not null).Select(e => e.Rate!.Value);
        return string.Join(';', ChainFor(item, events).Admissible(rated).Select(Plain));
    }

    /// <summary>
    /// CALIBRATED / BRIEF / INVENTED for one subject — see <see cref="RefProvenance"/>.
    /// <para>AverageCost is derived from the debt flags <see cref="RunAverageDebtAwareTraced"/> records,
    /// exactly as Fifo/Lifo are derived from <see cref="BuildStack"/>'s. It used to return the blanket
    /// ECHO-OF-HEAD tag, which was false from the moment <see cref="Value"/>'s AverageCost arm became
    /// debt-aware and which hid that CHECK 2's conviction on G6-001 rests on the count-with-debt rule.</para>
    /// </summary>
    public static string Provenance(Scenario s, ItemSpec item, string method, DateOnly asOf)
    {
        if (method is not (Fifo or Lifo or AverageCost))
            return RefProvenance.Calibrated;                                 // a flat rate chain; N* exercises it
        var events = Events(s, item, asOf);
        var chain = ChainFor(item, events);
        return method == AverageCost
            ? RunAverageDebtAwareTraced(events, chain).Flags.Provenance
            : BuildStack(events, lifo: method == Lifo, chain).Provenance;
    }

    private static decimal LayerValue(Stack st)
    {
        var value = 0m;
        foreach (var l in st.Layers) value += l.Qty * l.Unit;
        return Paisa(value);
    }

    /// <summary>The reference's issue value: what removing <paramref name="quantity"/> costs.</summary>
    public static decimal IssueValue(Scenario s, ItemSpec item, string method, decimal quantity, DateOnly asOf)
    {
        if (quantity <= 0m) return 0m;

        if (method is not (Fifo or Lifo))
        {
            // Flat/average methods issue at the closing unit rate; with no stock, at the standard cost.
            var closing = Value(s, item, method, asOf);
            var rate = closing.ClosingQty > 0m
                ? Paisa(closing.ClosingValueRupees / closing.ClosingQty)
                : Corpus.StandardCostOf(item) ?? 0m;
            return Paisa(rate * quantity);
        }

        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));

        var remaining = quantity;
        var consumed = 0m;
        while (remaining > 0m && st.Layers.Count > 0)
        {
            var idx = method == Lifo ? st.Layers.Count - 1 : 0;
            var layer = st.Layers[idx];
            var take = Math.Min(layer.Qty, remaining);
            consumed += take * layer.Unit;
            remaining -= take;
            if (take >= layer.Qty) st.Layers.RemoveAt(idx);
            else st.Layers[idx] = new Layer(layer.Qty - take, layer.Unit, layer.Src, layer.Origin);
        }
        return Paisa(consumed);
    }

    /// <summary>
    /// The quantities that entered stock without a rated purchase behind them, as of a date: unrated
    /// inwards, plus the units a physical count-up created. The spend ceiling has to impute a cost for
    /// these, which is what lets checks 7 and 8 run on the G6/G7 families instead of standing down
    /// (audit H1). Computed from the FIFO stack; LIFO produces the same count-up quantities because the
    /// count reconciles to a total, not to a particular layer.
    /// </summary>
    public static RefImputed Imputed(Scenario s, ItemSpec item, DateOnly asOf)
    {
        var events = Events(s, item, asOf);
        var unrated = 0m;
        foreach (var e in events)
            if (e.What == Kind.Inward && e.Rate is null) unrated += e.Qty;
        var st = BuildStack(events, lifo: false, ChainFor(item, events));
        return new RefImputed(unrated, st.CountUpQty);
    }

    // ---------------------------------------------------------------- arithmetic conventions

    /// <summary>Money snapped to the paisa, away-from-zero — the engine's only rounding convention.</summary>
    private static decimal Paisa(decimal rupees) => Math.Round(rupees, 2, MidpointRounding.AwayFromZero);

    /// <summary>A decimal written in full, invariant, never scientific — the emitted-column convention.</summary>
    private static string Plain(decimal d)
        => d.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Quantity snapped to the engine's 6-decimal-place stock precision.</summary>
    private static decimal RoundQty(decimal q) => Math.Round(q, 6, MidpointRounding.AwayFromZero);
}
