using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace HeadOracle;

// ---------------------------------------------------------------------------------------------------
// THE CORPUS — reworked after the NOT-READY audit. Families N1..N9, G1..G10, E1.
//
// Determinism rules (binding):
//   * no clock, no random, no Guid.NewGuid() for VOUCHERS — voucher ids are new Guid(seq,0,0,byte[8]).
//     Both replays tie-break on v.Id and auto-numbering is PER VOUCHER TYPE, so two vouchers of
//     different types on the same date both get Number == 1 and ONLY the Guid breaks the tie.
//     Every movement therefore also PINS its Number (default: its Seq, which is unique in a scenario),
//     so the spec-side reference can reproduce the engine's (Date, PhysicalLast, Number, Id) order
//     without having to model auto-numbering.
//   * master Guids stay OUT of the serialised output (items are identified by name).
//   * EVERY rate and quantity is odd-paisa / odd-fraction. Round figures assert nothing: a +-0.50
//     print defect survived this project's entire life under six assertions that all used 1,180 /
//     1,300 / 5,900.
//   * dates strictly increase inside a scenario, so the (Date, PhysicalLast, Number, Id) ordering is
//     unambiguous — EXCEPT in E1, which exists precisely to probe the equal-date tie-break.
//
// Guard discipline (verified by recon):
//   * N* families go through InventoryPostingService.Post, so the guarded path is exercised too.
//   * G* families CANNOT: Post runs the no-negative guard and rolls back + rethrows on an over-draw.
//     They use Company.AddInventoryVoucher, which bypasses it — exactly how
//     tests/Apex.Ledger.Tests/ExceptionReportsTests.cs:50 builds negative on-hand today.
//
// AUDIT FIX M5 — the v1 corpus was single-godown, single-item and had no opening balance on any
// negative book. Added here:
//   (a) N9 / G9  — TWO godowns. G9 is the classic real shape: the item is POSITIVE COMPANY-WIDE but
//       NEGATIVE IN ONE GODOWN. (Note the engine's valuation replay is company-wide while its on-hand
//       replay is per (item, godown, batch) key — G9 is the scenario that pins that asymmetry.)
//   (b) MULTI-ITEM scenarios (N2-003, G10-001, G10-002) so TotalClosingStockValue accumulates over
//       more than one item and check 9 is not a tautology.
//   (c) OPENING BALANCES on negative books (G1-004, G3-002, G10-002).
// AUDIT FIX M3 — G1-003, G2-002, G8-003 ISSUE STOCK AFTER RECOVERY, so post-recovery COGS is exercised.
//   G1-001 is deliberately left untouched: it is THE CRUX whose numbers the rework brief quotes.
//
// Ctor invariants that throw if violated: rate paisa-exact and >= 0; quantity > 0 and <= 6 dp;
// counted quantity >= 0; voucher date >= BooksBeginFrom.
//
// The namespace is HeadOracle, NOT anything starting with "Apex.", because Apex.Ledger is both a
// namespace AND the parent of the type Apex.Ledger.Domain.Ledger, which would make the bare identifier
// `Ledger` ambiguous.
// ---------------------------------------------------------------------------------------------------

public enum MoveKind { Inward, Outward, Count }

/// <summary>How an item's <see cref="StockItem.StandardCost"/> is configured (family N6 varies it).</summary>
public enum StandardCostMode
{
    /// <summary>A positive, odd-paisa standard cost.</summary>
    Set,
    /// <summary>No standard cost at all (null).</summary>
    Unset,
    /// <summary>Standard cost present but EXACTLY zero — the `is { } sc` vs `sc.Amount &gt; 0m` trap.</summary>
    ExplicitZero,
}

/// <summary>
/// One movement in a scenario spec. <paramref name="Seq"/> drives the deterministic voucher Guid and,
/// by default, the voucher Number. For <see cref="MoveKind.Count"/>, <paramref name="Qty"/> is the
/// counted quantity (item base unit) and <paramref name="Rate"/> must be null.
/// <paramref name="Godown"/> is an index into the scenario's godown list (0 = Main Location).
/// <paramref name="Item"/> names the item; empty means the scenario's FIRST item.
/// </summary>
/// <param name="Batch">The batch label this movement belongs to. "" is the unbatched key. The engine's
/// on-hand register is keyed by (item, godown, BATCH) while its valuation replay is batch-blind, so a
/// batch-keyed movement is a distinct on-hand key and the SAME valuation stream (family G12).</param>
/// <param name="Invoice">Post this movement as an ITEM-INVOICE stock line on a Purchase/Sales accounting
/// voucher instead of a pure-stock InventoryVoucher. In the shipped app negative stock overwhelmingly
/// arises this way, and StockValuationService.MovementEvents merges these lines explicitly — a path no
/// scenario reached before (family G11).</param>
/// <param name="Cancelled">Post the voucher CANCELLED. It must then contribute nothing to on-hand or to
/// valuation, in either engine (family G14).</param>
/// <param name="PostDated">Post the voucher POST-DATED. Both engines reduce post-dating to the same date
/// bound, so a post-dated voucher dated on or before the as-of date counts normally; G14 asserts that
/// rather than assuming it.</param>
public sealed record Movement(
    int Seq,
    DateOnly Date,
    MoveKind Kind,
    decimal Qty,
    decimal? Rate = null,
    bool UseCompoundUnit = false,
    int Number = 0,
    int Godown = 0,
    string Item = "",
    string Batch = "",
    bool Invoice = false,
    bool Cancelled = false,
    bool PostDated = false);

/// <summary>One stock item inside a scenario, with its own master configuration and opening stock.</summary>
public sealed record ItemSpec(
    string Name,
    StandardCostMode Std = StandardCostMode.Set,
    bool CompoundUnit = false,
    decimal OpeningQty = 0m,
    decimal OpeningRate = 0m,
    int OpeningGodown = 0);

/// <summary>
/// A scenario spec. <see cref="GuardBypass"/> selects <c>Company.AddInventoryVoucher</c> (which bypasses
/// the negative-stock posting guard) instead of <c>InventoryPostingService.Post</c>.
/// </summary>
public sealed record Scenario(
    string Id,
    string Family,
    bool GuardBypass,
    IReadOnlyList<ItemSpec> Items,
    IReadOnlyList<Movement> Movements,
    IReadOnlyList<DateOnly> AsOfDates,
    IReadOnlyList<decimal> IssueProbes,
    int Godowns = 1)
{
    /// <summary>The item a movement refers to (empty <c>Item</c> means the scenario's first item).</summary>
    public ItemSpec ItemOf(Movement m)
        => m.Item.Length == 0 ? Items[0] : Items.First(i => i.Name == m.Item);

    /// <summary>Every movement that belongs to <paramref name="item"/>, in spec order.</summary>
    public IEnumerable<Movement> MovementsOf(ItemSpec item)
        => Movements.Where(m => (m.Item.Length == 0 ? Items[0].Name : m.Item) == item.Name);
}

public static class Corpus
{
    public static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>Tail units per first unit of the compound unit used by family N7 ("Doz-Nos" = 12 Nos).</summary>
    public const int CompoundNumerator = 12;

    /// <summary>Denominator of the compound conversion (1 — "12 Nos per 1 Doz").</summary>
    public const int CompoundDenominator = 1;

    /// <summary>The odd-paisa standard cost used whenever an item sets one.</summary>
    public const decimal StandardCostRupees = 9.77m;

    /// <summary>The item's standard cost as the master will carry it, or null when unset.</summary>
    public static decimal? StandardCostOf(ItemSpec i) => i.Std switch
    {
        StandardCostMode.Set => StandardCostRupees,
        StandardCostMode.ExplicitZero => 0m,
        _ => null,
    };

    /// <summary>Deterministic voucher id — the sequence number only. Never Guid.NewGuid().</summary>
    public static Guid VoucherId(int seq) => new(seq, 0, 0, new byte[8]);

    public static readonly StockValuationMethod[] Methods =
    [
        StockValuationMethod.AverageCost,
        StockValuationMethod.Fifo,
        StockValuationMethod.Lifo,
        StockValuationMethod.StandardCost,
        StockValuationMethod.LastPurchaseCost,
        StockValuationMethod.LastSaleCost,
    ];

    private static DateOnly D(int day) => new(2024, 4, day);

    private static Movement In(int seq, int day, decimal qty, decimal rate,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, rate, false, number ?? seq, godown, item);

    private static Movement InUnrated(int seq, int day, decimal qty,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, null, false, number ?? seq, godown, item);

    private static Movement InDoz(int seq, int day, decimal qty, decimal ratePerDoz,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, ratePerDoz, true, number ?? seq, godown, item);

    private static Movement Out(int seq, int day, decimal qty, decimal? rate = null,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Outward, qty, rate, false, number ?? seq, godown, item);

    private static Movement Count(int seq, int day, decimal counted,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Count, counted, null, false, number ?? seq, godown, item);

    // ---- AUDIT FIX (corpus gaps): batches, item-invoice stock lines, cancelled vouchers -------------

    /// <summary>An inward into a named BATCH of the key (item, godown, batch).</summary>
    private static Movement InBatch(int seq, int day, decimal qty, decimal rate, string batch,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, rate, false, number ?? seq, godown, item, batch);

    /// <summary>An outward out of a named BATCH of the key (item, godown, batch).</summary>
    private static Movement OutBatch(int seq, int day, decimal qty, string batch, decimal? rate = null,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Outward, qty, rate, false, number ?? seq, godown, item, batch);

    /// <summary>An outward posted as a SALES ITEM-INVOICE stock line (the commonest real route to negative stock).</summary>
    private static Movement OutInvoice(int seq, int day, decimal qty, decimal rate,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Outward, qty, rate, false, number ?? seq, godown, item, "", Invoice: true);

    /// <summary>An inward posted as a PURCHASE ITEM-INVOICE stock line.</summary>
    private static Movement InInvoice(int seq, int day, decimal qty, decimal rate,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, rate, false, number ?? seq, godown, item, "", Invoice: true);

    /// <summary>An outward whose voucher is CANCELLED — it must contribute nothing, anywhere.</summary>
    private static Movement OutCancelled(int seq, int day, decimal qty,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Outward, qty, null, false, number ?? seq, godown, item, "",
               Invoice: false, Cancelled: true);

    /// <summary>An inward whose voucher is POST-DATED (and dated in-window, so it still counts).</summary>
    private static Movement InPostDated(int seq, int day, decimal qty, decimal rate,
        int godown = 0, string item = "", int? number = null)
        => new(seq, D(day), MoveKind.Inward, qty, rate, false, number ?? seq, godown, item, "",
               Invoice: false, Cancelled: false, PostDated: true);

    private static IReadOnlyList<ItemSpec> One(
        StandardCostMode std = StandardCostMode.Set,
        bool compound = false,
        decimal openQty = 0m,
        decimal openRate = 0m,
        int openGodown = 0)
        => [new ItemSpec("Widget", std, compound, openQty, openRate, openGodown)];

    /// <summary>
    /// The corpus. 6 valuation methods x several as-of dates (inside the negative window AND after
    /// recovery) x IssueValue probes below / at / above on-hand are applied to every scenario by the
    /// emitter, so the table below only has to state the movement history.
    /// </summary>
    public static IReadOnlyList<Scenario> Scenarios { get; } =
    [
        // ============================================================ N1 — never negative, single lot
        // The byte-identity floor.
        new Scenario("N1-001", "N1", false, One(),
            [In(101, 5, 100m, 10.33m), Out(102, 10, 40m)],
            [D(7), D(12), D(20)],
            [3.5m, 60m, 1000m]),

        new Scenario("N1-002", "N1", false, One(),
            [In(111, 3, 11m, 1000.07m), Out(112, 8, 3.5m, 1213.41m)],
            [D(4), D(9), D(30)],
            [1.25m, 7.5m, 50m]),

        // ============================================================ N2 — many lots, mixed rates
        // FIFO/LIFO layer walking.
        new Scenario("N2-001", "N2", false, One(),
            [In(121, 5, 100m, 10.33m), In(122, 10, 50m, 12.07m), Out(123, 15, 80m)],
            [D(7), D(12), D(20)],
            [1.25m, 70m, 1000m]),

        new Scenario("N2-002", "N2", false, One(),
            [In(131, 2, 13.75m, 7.91m), In(132, 6, 7m, 100.13m), In(133, 9, 11m, 0.37m),
             Out(134, 14, 25m, 55.51m), Out(135, 18, 3.5m)],
            [D(7), D(12), D(20), D(30)],
            [1.25m, 3.25m, 100m]),

        // --- M5(b): MULTI-ITEM, never negative. TotalClosingStockValue must accumulate over 3 items
        // with 3 different opening/rate shapes, so check 9 is a real sum and not a copy of one item.
        new Scenario("N2-003", "N2", false,
            [
                new ItemSpec("Widget", StandardCostMode.Set),
                new ItemSpec("Gadget", StandardCostMode.Unset, OpeningQty: 9.5m, OpeningRate: 41.03m),
                new ItemSpec("Sprocket", StandardCostMode.ExplicitZero),
            ],
            [
                In(1101, 4, 20m, 10.33m, item: "Widget"),
                In(1102, 5, 6.25m, 100.13m, item: "Gadget"),
                In(1103, 6, 30m, 0.37m, item: "Sprocket"),
                Out(1104, 11, 7.5m, 55.51m, item: "Widget"),
                Out(1105, 12, 3.25m, item: "Gadget"),
                In(1106, 13, 11m, 12.07m, item: "Widget"),
                Out(1107, 17, 13.75m, item: "Sprocket"),
            ],
            [D(7), D(14), D(20)],
            [1.25m, 11.5m, 400m]),

        // ============================================================ N3 — + opening balance
        // Opening lots sort before all vouchers.
        new Scenario("N3-001", "N3", false, One(openQty: 25m, openRate: 41.03m),
            [In(141, 6, 11m, 88.61m), Out(142, 11, 30m, 120.55m)],
            [D(3), D(8), D(13), D(30)],
            [1.25m, 6m, 200m]),

        new Scenario("N3-002", "N3", false, One(openQty: 7.5m, openRate: 3.17m),
            [In(151, 4, 13.75m, 13.79m), Out(152, 9, 11m), In(153, 16, 6m, 41.03m)],
            [D(2), D(6), D(12), D(20)],
            [3.5m, 16.25m, 500m]),

        // ============================================================ N4 — physical stock count up/down
        // The Count arm.
        new Scenario("N4-001", "N4", false, One(),
            [In(161, 5, 100m, 10.33m), Count(162, 10, 60m), In(163, 15, 20m, 12.07m), Count(164, 20, 95m)],
            [D(7), D(12), D(17), D(22)],
            [3.5m, 95m, 1000m]),

        new Scenario("N4-002", "N4", false, One(),
            [In(171, 3, 40m, 13.79m), Count(172, 8, 40m), Count(173, 12, 55.5m), Out(174, 17, 5.25m)],
            [D(5), D(10), D(14), D(20)],
            [1.25m, 50.25m, 300m]),

        // ---- ROUND 8 --------------------------------------------------------------------------------
        // N4-003 — THE LAYER STACK DRAINS TO **EXACTLY ZERO**, AND IS THEN COUNTED **UP**.
        //
        // WHY IT EXISTS. Both N4-001 and N4-002 count from a POSITIVE stack, so the running average at
        // every count is positive and the best-available-cost chain returns it unchanged. That made a
        // sentence asserted in three places — the reference's rule 5, the production diff's type doc, and
        // the count-arm comment in StockValuationService — untestable: "on a never-negative book the
        // running average is positive, so the chain is inert there". IT IS FALSE. `Out 13.75` against
        // `In 13.75` drains the stack to exactly zero: `Consume` reaches `remaining == 0`, so NO DEBT is
        // created, no (item, godown, batch) on-hand ever goes negative, FactNeverNegative is 1 — and the
        // running average is nonetheless 0, so a chain applied unconditionally at the count fires here and
        // changes the value of a book CHECK 1 (byte identity) promises is untouched.
        //
        // WHAT EACH ARM SAYS. HEAD prices the 6.25 counted units at the bare running average, i.e. Rs 0.00.
        // The S-A slice under gate prices them through the chain: no standard cost is set, so the chain
        // reaches the last rated inward, 6.25 x 13.07 = Rs 81.69 — 81 rupees of Balance-Sheet asset
        // appearing on a book that never went short. CHECK 1 and CHECK 3 both see it now; before this
        // scenario existed, nothing in the harness could.
        //
        // >>> AN OPEN QUESTION THIS SCENARIO DELIBERATELY DOES NOT DECIDE. Rs 0.00 for units a storeman
        //     physically counted is itself the wiped-asset shape this programme exists to prevent. But the
        //     user's settled rule (2026-07-27) is scoped to "a debt settled by a movement carrying NO
        //     PURCHASE RATE", and a count on a book that never ran short settles no debt: it is OUTSIDE the
        //     ruling. So the reference pins HEAD's answer here, the harness convicts any engine that
        //     changes it, and the question — should a no-debt count-up impute a cost? — goes to the user as
        //     its own decision rather than riding in unannounced on a negative-stock slice.
        //
        // The standard cost is UNSET on purpose: with one set, the chain would stop at Rs 9.77 and the gap
        // would be Rs 48.85 instead of Rs 81.69, which hides how far the chain can reach.
        new Scenario("N4-003", "N4", false, One(StandardCostMode.Unset),
            [In(1601, 5, 13.75m, 13.07m), Out(1602, 10, 13.75m), Count(1603, 15, 6.25m)],
            [D(7), D(12), D(20)],
            [1.25m, 6.25m, 300m]),

        // ============================================================ N5 — unrated inward
        // NoRateInwardCost fallback chain.
        new Scenario("N5-001", "N5", false, One(),
            [In(181, 4, 20m, 10.33m), InUnrated(182, 9, 15m), Out(183, 14, 12m)],
            [D(6), D(11), D(16)],
            [3.5m, 23m, 100m]),

        new Scenario("N5-002", "N5", false, One(),
            [InUnrated(191, 3, 10m), In(192, 8, 5m, 12.07m), Out(193, 13, 3.5m)],
            [D(5), D(10), D(15)],
            [1.25m, 11.5m, 60m]),

        // ============================================================ N6 — StandardCost set/unset/zero
        // The `is { } sc` vs `sc.Amount > 0m` trap: identical movements, three master configurations.
        new Scenario("N6-001", "N6", false, One(StandardCostMode.Set),
            [In(201, 5, 30m, 10.33m), Out(202, 10, 11m)],
            [D(7), D(12), D(20)],
            [3.5m, 19m, 100m]),

        new Scenario("N6-002", "N6", false, One(StandardCostMode.Unset),
            [In(211, 5, 30m, 10.33m), Out(212, 10, 11m)],
            [D(7), D(12), D(20)],
            [3.5m, 19m, 100m]),

        new Scenario("N6-003", "N6", false, One(StandardCostMode.ExplicitZero),
            [In(221, 5, 30m, 10.33m), Out(222, 10, 11m)],
            [D(7), D(12), D(20)],
            [3.5m, 19m, 100m]),

        // ============================================================ N7 — compound units
        // RateInBase / QuantityInBase. Item base unit is Nos; inwards are stated in Doz (= 12 Nos).
        // 121.32 / 12 = 10.11 and 88.44 / 12 = 7.37 divide exactly; N7-002's 100.13 / 12 does NOT,
        // which is the interesting one.
        new Scenario("N7-001", "N7", false, One(compound: true),
            [InDoz(231, 5, 5m, 121.32m), InDoz(232, 10, 3m, 88.44m), Out(233, 15, 24m)],
            [D(7), D(12), D(20)],
            [3.5m, 72m, 500m]),

        new Scenario("N7-002", "N7", false, One(compound: true),
            [InDoz(241, 4, 7m, 100.13m), Out(242, 9, 30m), In(243, 14, 40m, 9.13m)],
            [D(6), D(11), D(16)],
            [1.25m, 94m, 300m]),

        // ============================================================ N8 — TWO godowns, never negative
        // The multi-key on-hand replay: the valuation stream merges godowns, the quantity stream does not.
        new Scenario("N8-001", "N8", false, One(),
            [In(251, 4, 30m, 10.33m, godown: 0), In(252, 6, 20m, 12.07m, godown: 1),
             Out(253, 11, 12.5m, godown: 0), Out(254, 15, 7.25m, godown: 1)],
            [D(5), D(8), D(13), D(20)],
            [1.25m, 30.25m, 200m],
            Godowns: 2),

        // ============================================================ N9 — two godowns + opening in both
        new Scenario("N9-001", "N9", false,
            [new ItemSpec("Widget", StandardCostMode.Set, OpeningQty: 6.25m, OpeningRate: 88.61m, OpeningGodown: 1)],
            [In(261, 4, 15m, 10.33m, godown: 0), Out(262, 9, 11m, godown: 0),
             Out(263, 14, 3.5m, godown: 1), In(264, 18, 8m, 41.03m, godown: 1)],
            [D(6), D(11), D(16), D(25)],
            [1.25m, 14.75m, 300m],
            Godowns: 2),

        // ============================================================ N10 — TWO GODOWNS **AND A COUNT**
        // ROUND 9. THE GAP THAT LET THREE WRONG ENGINES THROUGH: before this family the corpus had NO
        // scenario combining `Godowns: 2` with a `Count(` — `Godowns:` appeared at four line numbers and
        // `Count(` at eight, and the two sets never overlapped. Every multi-godown book was count-free and
        // every counted book was single-godown, so the interaction between an ITEM-LEVEL valuation replay
        // and a PER-KEY physical count was measured by nothing. Three separate wrong answers came out of
        // that interaction; the harness reported ENGINE VERDICT: ACCEPTED on all three.

        // N10-001 — **THE BOOK THE DEBT GATE EXISTS FOR**. It matters most because CHECK 1 PROMISES it.
        //
        // Two godowns, six vouchers, EVERY ONE OF THEM POSTED THROUGH THE REAL
        // InventoryPostingService.Post GUARD (GuardBypass is false). The minimum on-hand across g0, across
        // g1 AND across the item is 0 on every date — NOTHING goes negative anywhere, so FactNeverNegative
        // is 1 and byte identity is contractually promised.
        //   d5   g0 In    30 @ 100.13
        //   d6   g1 In    30 @ 100.13
        //   d7   g0 Count 30            -> a PER-KEY NO-OP: g0 holds exactly 30
        //   d8   g0 Out   30            -> g0 to exactly 0, drains its own layers exactly, NO debt
        //   d9   g1 Out   20            -> g1 to 10, comfortably positive
        //   d10  g1 In    20 @   7.91
        // on-hand: g0 = 0, g1 = 30, item = 30.
        //
        // WHAT EACH MODEL SAYS AT d12.
        //   THE HONEST PER-KEY FIGURE: g0 holds nothing; g1 holds 10 @ 100.13 + 20 @ 7.91 = Rs 1,159.50.
        //     No model in this repository reaches it — a per-key cost replay in which cost FLOWS ACROSS A
        //     TRANSFER would, and that is separate work (round 9 built a per-key replay WITHOUT that and
        //     it broke ordinary transfers, so it was reverted).
        //   THE UNGATED DEBT RULE: the d7 count sees a merged stack of 60 and truncates it to 30, throwing
        //     away units the OTHER godown really holds; the d8 outward empties the truncated stack; the d9
        //     outward manufactures a DEBT on a book that never went short; and the d10 inward is entirely
        //     eaten repaying it. Closing Rs 0.00 on all three methods — a WIPED ASSET, and UNBOUNDED in
        //     the surviving lot's rate (at 10,000.19 the honest figure is Rs 100,160.10 and it still says
        //     Rs 0.00).
        //   THE DEBT GATE (what ships): at the d9 outward g0 is 0 and g1 is 10 — NO key is negative — so
        //     the shortfall is an artefact of flattening and is DISCARDED exactly as HEAD discards it. No
        //     debt is ever created on this book, every debt clause is inert, and the answer is HEAD's own:
        //     Rs 158.20 on Fifo/Lifo and Rs 237.30 on AverageCost. THIS SCENARIO IS THE EVIDENCE FOR THAT:
        //     it is FactNeverNegative = 1, so CHECK 1 holds it to HEAD byte for byte.
        //
        // This is the scenario that makes the defect a HARNESS verdict instead of a paragraph in a review.
        new Scenario("N10-001", "N10", false, One(),
            [In(1701, 5, 30m, 100.13m, godown: 0), In(1702, 6, 30m, 100.13m, godown: 1),
             Count(1703, 7, 30m, godown: 0), Out(1704, 8, 30m, godown: 0),
             Out(1705, 9, 20m, godown: 1), In(1706, 10, 20m, 7.91m, godown: 1)],
            [D(7), D(9), D(12)],
            [1.25m, 30m, 400m],
            Godowns: 2),

        // N10-002 — PER-KEY AND ITEM-LEVEL DIFFER WITH **NO COUNT AND NO NEGATIVE ANYWHERE**.
        //
        // The point of this one is that the multi-key defect is NOT about physical counts at all. It is
        // about WHICH LOT an outward consumes. An outward from godown 1 must eat godown 1's layers; an
        // item-level replay lets it eat godown 0's, and on a book with different rates in the two godowns
        // that is a different closing value with nothing unusual in the book whatsoever.
        //   d5   g0 In  13.75 @ 100.13
        //   d6   g1 In  20    @   7.91
        //   d10  g1 Out  6.25
        //   d12  g0 Out  6.25
        // on-hand g0 = 7.5, g1 = 13.75, item = 21.25; never negative, guard-posted, no count.
        //   THE HONEST PER-KEY FIGURE at d20 : 7.5 x 100.13 + 13.75 x 7.91 = 750.975 + 108.7625 = Rs 859.74.
        //   ITEM-LEVEL FIFO (what ships): both outwards eat the OLDEST lot, which is g0's, so g1's cheap
        //                    lot survives whole: 1.25 x 100.13 + 20 x 7.91 = Rs 283.36.
        //   ITEM-LEVEL LIFO (what ships): both outwards eat the NEWEST lot, which is g1's: 13.75 x 100.13
        //                    + 7.5 x 7.91 = Rs 1,436.11.
        // ROUND 10 — no key is ever negative here, so no debt is possible and BOTH item-level answers are
        // HEAD's own, byte for byte (CHECK 1). The Rs 1,152.75 FIFO/LIFO spread on a book with no negative
        // stock, no count and no unusual voucher is a REAL, PRE-EXISTING property of the flattened replay;
        // this scenario pins it rather than hiding it, and GT-91/GT-91L/GT-92/GT-93 state each figure.
        new Scenario("N10-002", "N10", false, One(),
            [In(1711, 5, 13.75m, 100.13m, godown: 0), In(1712, 6, 20m, 7.91m, godown: 1),
             Out(1713, 10, 6.25m, godown: 1), Out(1714, 12, 6.25m, godown: 0)],
            [D(7), D(11), D(20)],
            [1.25m, 21.25m, 400m],
            Godowns: 2),

        // ============================================================ N11 — GODOWNS **AND** BATCHES
        // The key is (item, godown, BATCH), so a corpus that only varied the godown would still have been
        // blind to half of it. N11-001 varies both at once and holds THREE keys, one of which drains to
        // exactly zero — the shape that used to make the item-level replay create a phantom debt.
        //   keys: (g0, B-A) (g0, B-B) (g1, "")
        //   d4   g0/B-A In  13.75 @ 100.13
        //   d5   g0/B-B In  11    @   7.91
        //   d6   g1     In   6.25 @  41.03
        //   d10  g0/B-B Out 11              -> that key drains to EXACTLY zero: no debt, no negative
        //   d12  g1     Out  3.5
        // on-hand: B-A 13.75, B-B 0, g1 2.75, item 16.5; never negative anywhere; guard-posted.
        //   THE HONEST PER-KEY FIGURE at d20 : 13.75 x 100.13 + 0 + 2.75 x 41.03 = Rs 1,489.62.
        //   ITEM-LEVEL FIFO at d11 (before the g1 outward) eats the dear B-A lot with B-B's own outward:
        //                    2.75 x 100.13 + 11 x 7.91 + 6.25 x 41.03 = Rs 618.81 against the honest
        //                    Rs 1,633.23 — a Rs 1,014.42 hole in Stock-in-Hand on a book with no negative
        //                    stock, no count and no unusual voucher of any kind.
        // ROUND 10 — that hole is HEAD's too (no key is ever negative, so no debt is possible and CHECK 1
        // holds this book to HEAD byte for byte). It is a pre-existing property of the flattened replay,
        // pinned by GT-94/GT-94L/GT-95/GT-96, and it is what a per-key cost replay would repair.
        new Scenario("N11-001", "N11", false, One(),
            [InBatch(1721, 4, 13.75m, 100.13m, "B-A", godown: 0),
             InBatch(1722, 5, 11m, 7.91m, "B-B", godown: 0),
             In(1723, 6, 6.25m, 41.03m, godown: 1),
             OutBatch(1724, 10, 11m, "B-B", godown: 0),
             Out(1725, 12, 3.5m, godown: 1)],
            [D(7), D(11), D(20)],
            [1.25m, 16.5m, 300m],
            Godowns: 2),

        // ============================================================ G1 — over-draw, recover from ONE later lot
        // THE CRUX: FIFO recovery. HEAD reports 25 units @ Rs 316.40; FIFO-correct is 25 x 7.91 = Rs 197.75.
        // DO NOT EDIT G1-001 — the rework brief quotes its numbers.
        new Scenario("G1-001", "G1", true, One(),
            [In(301, 5, 10m, 100.13m, number: 1), Out(302, 10, 25m, number: 1), In(303, 15, 40m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [3.5m, 25m, 1000m]),

        new Scenario("G1-002", "G1", true, One(),
            [In(311, 3, 7m, 13.79m, number: 1), Out(312, 8, 18.5m, number: 1), In(313, 13, 30m, 41.03m, number: 2)],
            [D(5), D(10), D(16)],
            [1.25m, 18.5m, 200m]),

        // --- M3: ISSUE AFTER RECOVERY. Post-recovery COGS is only tested if stock is issued after the
        // debt has been repaid; without this a fix with a right Balance Sheet and a wrong P&L ships clean.
        new Scenario("G1-003", "G1", true, One(),
            [In(321, 5, 10m, 100.13m, number: 1), Out(322, 10, 25m, number: 1),
             In(323, 15, 40m, 7.91m, number: 2), Out(324, 20, 9.25m, 55.51m, number: 2)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 15.75m, 500m]),

        // --- M5(c): OPENING BALANCE on a negative book. The opening lot is the earliest layer, so the
        // over-draw eats it first and the debt is created against a lot that no voucher created.
        new Scenario("G1-004", "G1", true, One(openQty: 4.5m, openRate: 88.61m),
            [In(331, 5, 6m, 100.13m, number: 1), Out(332, 10, 22.5m, number: 1),
             In(333, 15, 30m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 18m, 400m]),

        // ============================================================ G2 — recover across MULTIPLE lots
        // A repaid debt must not re-rate.
        new Scenario("G2-001", "G2", true, One(),
            [In(341, 5, 10m, 100.13m, number: 1), Out(342, 10, 35m, number: 1),
             In(343, 15, 12m, 7.91m, number: 2), In(344, 20, 20m, 12.07m, number: 3)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 7m, 500m]),

        // --- M3: multi-lot recovery THEN an issue.
        new Scenario("G2-002", "G2", true, One(),
            [In(351, 5, 10m, 100.13m, number: 1), Out(352, 10, 35m, number: 1),
             In(353, 15, 12m, 7.91m, number: 2), In(354, 18, 20m, 12.07m, number: 3),
             Out(355, 22, 5.75m, 55.51m, number: 2)],
            [D(7), D(12), D(17), D(20), D(25)],
            // AUDIT FIX (duplicate probe): this list read [1.25, 1.25, 300] — 1.25 TWICE. Emit therefore
            // wrote 'IssueValue@1.25Paisa' twice per (method, asOf) and ReadTsv silently kept the last, so
            // 60 emitted rows vanished on every run and the header's two row counts disagreed with no
            // explanation. ReadTsv now THROWS on a duplicate key; the second probe is the family's OTHER
            // exactly-at-on-hand quantity (on-hand is 7 at 2024-04-20 and 1.25 at 2024-04-25), so the
            // scenario now probes at on-hand on BOTH post-recovery dates instead of probing 1.25 twice.
            [1.25m, 7m, 300m]),

        // --- A5 (read-only measurement addition): multi-lot recovery in the DEAR-then-CHEAP order.
        // G2-001/G2-002 both recover cheap-then-dear, which makes HEAD's reset-and-re-average
        // UNDERSTATE. Reversing the order is the OVERSTATEMENT direction — the failure mode this
        // project exists to prevent — and no scenario in the corpus had it. Nothing is weakened:
        // these only ADD subjects.
        new Scenario("G2-003", "G2", true, One(),
            [In(1401, 5, 10m, 100.13m, number: 1), Out(1402, 10, 35m, number: 1),
             In(1403, 15, 20m, 12.07m, number: 2), In(1404, 20, 12m, 7.91m, number: 3)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 7m, 500m]),

        // The same order with a WIDE rate spread, to show whether the error is bounded.
        new Scenario("G2-004", "G2", true, One(),
            [In(1411, 5, 5m, 1000.07m, number: 1), Out(1412, 10, 25m, number: 1),
             In(1413, 15, 20m, 1000.07m, number: 2), In(1414, 20, 30m, 0.37m, number: 3)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 30m, 500m]),

        // ============================================================ G3 — partial recovery, STILL negative
        new Scenario("G3-001", "G3", true, One(),
            [In(361, 5, 10m, 100.13m, number: 1), Out(362, 10, 25m, number: 1), In(363, 15, 5m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [3.5m, 10m, 100m]),

        // --- M5(c): opening balance, still negative at the last as-of date.
        new Scenario("G3-002", "G3", true, One(openQty: 3.25m, openRate: 41.03m),
            [In(371, 5, 5m, 100.13m, number: 1), Out(372, 10, 20.5m, number: 1), In(373, 15, 4m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 8m, 100m]),

        // ============================================================ G4 — two successive over-draws
        // Exactly what produced the 18x error.
        new Scenario("G4-001", "G4", true, One(),
            [In(381, 5, 10m, 100.13m, number: 1), Out(382, 10, 25m, number: 1), Out(383, 13, 13.5m, number: 2),
             In(384, 18, 60m, 7.91m, number: 2)],
            [D(7), D(11), D(15), D(25)],
            [3.5m, 31.5m, 1000m]),

        // ============================================================ G5 — sell from nothing, then buy back more
        // Exactly the Rs 100,100-on-1-unit case.
        new Scenario("G5-001", "G5", true, One(),
            [Out(391, 5, 11m, number: 1), In(392, 10, 40m, 100.13m, number: 1)],
            [D(7), D(12)],
            [1.25m, 29m, 500m]),

        new Scenario("G5-002", "G5", true, One(),
            [Out(401, 5, 1000m, number: 1), In(402, 10, 1001m, 100.13m, number: 1)],
            [D(7), D(12)],
            [0.5m, 1m, 5m]),

        // ============================================================ G6 — over-draw then physical count
        new Scenario("G6-001", "G6", true, One(),
            [In(411, 5, 10m, 100.13m, number: 1), Out(412, 10, 25m, number: 1), Count(413, 15, 8m)],
            [D(7), D(12), D(20)],
            [3.5m, 8m, 100m]),

        new Scenario("G6-002", "G6", true, One(),
            [In(421, 5, 10m, 100.13m, number: 1), Out(422, 10, 25m, number: 1),
             In(423, 15, 30m, 7.91m, number: 2), Count(424, 20, 12m)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 12m, 200m]),

        // ---- ROUND 8 --------------------------------------------------------------------------------
        // G6-003 — A RATED INWARD DATED **AFTER** A COUNT TAKEN WITH A DEBT OUTSTANDING, NO STANDARD COST.
        //
        // WHY IT EXISTS. In G6-001 and G6-002 the Count is the LAST movement, so nothing can arrive after
        // it, and G6-001 sets a standard cost, which short-circuits the chain before the last-rated-inward
        // link is ever reached. Between them those two facts made the whole look-ahead invisible: no
        // subject in the corpus consulted the chain and then had a rated inward land later. Both the engine
        // and (until round 8) the reference resolved "last rated inward" over the WHOLE as-of window, so
        // the units a 15-Apr count created were priced by an 18-Apr purchase.
        //
        // THE BOOK, and it is the one the valuation review reproduced with:
        //   d5   In  10 @   100.13
        //   d10  Out 25              -> drains the 10, debt 15
        //   d15  Count 8             -> debt written off, 8 units topped up by the chain
        //   d18  In   1 @ 1,000,000.03
        // Total ever spent on this item = 10 x 100.13 + 1 x 1,000,000.03 = Rs 1,001,001.33 on 9 units held.
        // The point-in-time chain prices the 8 counted units at the only rate the item had paid WHEN THEY
        // WERE COUNTED, Rs 100.13, so closing = 8 x 100.13 + 1,000,000.03 = Rs 1,000,801.07. An engine that
        // looks ahead prices them at Rs 1,000,000.03 each and closes at Rs 9,000,000.27 — nine times
        // everything the item ever cost, and unbounded in the later rate.
        //
        // THE TWO STRADDLING AS-OF DATES ARE THE POINT OF d16 AND d20. The book is IDENTICAL at both; only
        // the report date moves. A look-ahead makes the same 8 counted units worth Rs 801.04 on a 16-Apr
        // Balance Sheet and Rs 8,000,000.24 on a 20-Apr one, i.e. posting an 18-Apr purchase restates a
        // closed period whose vouchers never changed. The reference holds them at Rs 801.04 on BOTH dates,
        // so CHECK 3 sees the restatement as a disagreement on the later date alone.
        new Scenario("G6-003", "G6", true, One(StandardCostMode.Unset),
            [In(1611, 5, 10m, 100.13m, number: 1), Out(1612, 10, 25m, number: 1),
             Count(1613, 15, 8m, number: 2), In(1614, 18, 1m, 1000000.03m, number: 2)],
            [D(7), D(12), D(16), D(20)],
            [1.25m, 9m, 500m]),

        // G6-004 — THE `debt = 0m` COUNT WRITE-OFF, MADE FALSIFIABLE.
        //
        // WHY IT EXISTS. "A physical count WRITES THE DEBT OFF" is a named clause of the settled semantics
        // and NOTHING could detect its removal: G6-001 and G6-002 both end at the Count, so a debt that
        // survived it had no later inward to eat and could not manifest. Delete `debt = 0m` from either
        // count arm of the engine and every check stayed green.
        //   d5   In  10 @ 100.13
        //   d10  Out 25            -> debt 15
        //   d15  Count 8           -> debt written off; 8 units at the chain's STANDARD COST 9.77
        //   d18  In  40 @   7.91   -> no debt to repay, so all 40 survive
        // closing = 8 x 9.77 + 40 x 7.91 = 78.16 + 316.40 = Rs 394.56 on 48 units.
        // WITH the write-off removed the surviving 15-unit debt eats 15 of the 40 and the answer becomes
        // 78.16 + 25 x 7.91 = Rs 275.91 — Rs 118.65 of real asset gone, and the layer stack holds 33 units
        // while the report prints 48. GT-63/GT-63L/GT-64 pin Rs 394.56, so that mutation is now convicted.
        // The standard cost is SET here on purpose: it keeps this scenario about the WRITE-OFF and not
        // about the chain's tail, which G6-003 covers.
        new Scenario("G6-004", "G6", true, One(StandardCostMode.Set),
            [In(1621, 5, 10m, 100.13m, number: 1), Out(1622, 10, 25m, number: 1),
             Count(1623, 15, 8m, number: 2), In(1624, 18, 40m, 7.91m, number: 2)],
            [D(7), D(12), D(25)],
            [1.25m, 48m, 300m]),

        // G6-005 — A COUNT TAKEN **DOWN TO ZERO** WHILE A DEBT IS OUTSTANDING.
        //
        // WHY IT EXISTS, and what is NOT here. The review asked for two count-with-debt shapes. One of them
        // is STRUCTURALLY UNREACHABLE and is recorded as such rather than faked: a count taken with a debt
        // outstanding CANNOT find a non-empty layer stack, because a debt is only ever created after the
        // layers are drained and any inward repays it before adding a layer — at most one of (layers, debt)
        // is non-zero, always. The other shape IS reachable, and this is it: with a debt outstanding the
        // book quantity is negative, so every legal counted quantity (>= 0) raises it; the DOWNWARD end of
        // that range is a count of EXACTLY ZERO, which asserts an empty shelf and must leave an empty stack.
        //   d5   In  13.75 @ 100.13
        //   d10  Out 31.25         -> drains the 13.75, debt 17.5
        //   d15  Count 0           -> debt written off; nothing to top up; stack stays empty
        //   d18  In  21.25 @  7.91 -> no debt to repay, so all 21.25 survive
        // closing = 21.25 x 7.91 = Rs 168.09. With the write-off removed the debt 17.5 eats the lot and
        // only 3.75 units survive: Rs 29.66. A second, independent pin on the same clause as G6-004, on the
        // opposite end of the counted range.
        new Scenario("G6-005", "G6", true, One(StandardCostMode.Set),
            [In(1631, 5, 13.75m, 100.13m, number: 1), Out(1632, 10, 31.25m, number: 1),
             Count(1633, 15, 0m, number: 2), In(1634, 18, 21.25m, 7.91m, number: 2)],
            [D(7), D(12), D(25)],
            [1.25m, 21.25m, 300m]),

        // ============================================================ G7 — over-draw then unrated inward
        new Scenario("G7-001", "G7", true, One(),
            [In(431, 5, 10m, 100.13m, number: 1), Out(432, 10, 25m, number: 1), InUnrated(433, 15, 40m, number: 2)],
            [D(7), D(12), D(20)],
            [3.5m, 25m, 300m]),

        // The same shape with NO standard cost, so the fallback chain has to reach past it.
        new Scenario("G7-002", "G7", true, One(StandardCostMode.Unset),
            [In(441, 5, 10m, 100.13m, number: 1), Out(442, 10, 25m, number: 1), InUnrated(443, 15, 40m, number: 2)],
            [D(7), D(12), D(20)],
            [3.5m, 25m, 300m]),

        // ---- ROUND 8 --------------------------------------------------------------------------------
        // G7-003 — AN UNRATED INWARD REPAYS THE DEBT, AND THEN A RATED INWARD LANDS AT A WILDLY DIFFERENT
        // RATE.
        //
        // WHY IT EXISTS. G7-001 and G7-002 stop at the unrated inward. Nothing follows it, so the chain's
        // whole-window reading and its point-in-time reading give the same answer and the family passed
        // while being blind to the difference. Add ONE later purchase and the whole-window reading prices
        // the ENTIRE surplus of the unrated lot at a rate that did not exist when the lot arrived.
        //   d5   In  10 @   100.13
        //   d10  Out 25              -> drains the 10, debt 15
        //   d15  In  40 UNRATED      -> priced by the chain; repays 15, 25 survive
        //   d18  In   1 @ 9,999.99
        // Total ever spent = 10 x 100.13 + 1 x 9,999.99 = Rs 11,001.29 on 26 units held. The point-in-time
        // chain prices the unrated lot at the only rate the item had paid when it arrived, Rs 100.13, so
        // closing = 25 x 100.13 + 9,999.99 = Rs 12,503.24. A whole-window chain prices all 25 surplus units
        // at Rs 9,999.99 and holds Rs 259,999.74 — 23.6x everything the item ever cost, the same shape as
        // this project's historical failure #2 (Rs 476,000 held on Rs 26,100 ever spent).
        // As in G6-003, d16 and d20 straddle the later purchase so the restatement of the earlier date is
        // visible as a disagreement on the later one alone.
        new Scenario("G7-003", "G7", true, One(StandardCostMode.Unset),
            [In(1641, 5, 10m, 100.13m, number: 1), Out(1642, 10, 25m, number: 1),
             InUnrated(1643, 15, 40m, number: 2), In(1644, 18, 1m, 9999.99m, number: 3)],
            [D(7), D(12), D(16), D(20)],
            [1.25m, 26m, 500m]),

        // ============================================================ G8 — replenish cheaper / dearer
        // The SIGN of the error.
        new Scenario("G8-001", "G8", true, One(),
            [In(451, 5, 12m, 88.61m, number: 1), Out(452, 10, 30m, number: 1), In(453, 15, 45m, 3.17m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 27m, 400m]),

        new Scenario("G8-002", "G8", true, One(),
            [In(461, 5, 12m, 3.17m, number: 1), Out(462, 10, 30m, number: 1), In(463, 15, 45m, 88.61m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 27m, 400m]),

        // --- M3: dearer replenishment THEN an issue, so post-recovery COGS carries the dear rate.
        new Scenario("G8-003", "G8", true, One(),
            [In(471, 5, 12m, 3.17m, number: 1), Out(472, 10, 30m, number: 1),
             In(473, 15, 45m, 88.61m, number: 2), Out(474, 20, 11.5m, 120.55m, number: 2)],
            [D(7), D(12), D(17), D(25)],
            [1.25m, 15.5m, 400m]),

        // ============================================================ G9 — M5(a) THE CLASSIC REAL SHAPE
        // POSITIVE COMPANY-WIDE, NEGATIVE IN ONE GODOWN. Godown 1 is over-drawn by 6.5 units while the
        // company holds +23.25 (g0 = 30 - 0.25 = 29.75; g1 = 5.5 - 12 = -6.5). The engine's valuation
        // replay is company-wide, so its layer stack never
        // goes dry here; its on-hand replay is per key, so godown 1 reports negative. Any fix that keys
        // off "the item went negative" instead of "the company-wide layer stack went dry" changes this
        // scenario — and it must not.
        new Scenario("G9-001", "G9", true,
            [new ItemSpec("Widget", StandardCostMode.Set)],
            [In(481, 4, 30m, 10.33m, godown: 0, number: 1),
             In(482, 6, 5.5m, 100.13m, godown: 1, number: 2),
             Out(483, 11, 12m, godown: 1, number: 1),
             Out(484, 15, 0.25m, 55.51m, godown: 0, number: 2)],
            [D(5), D(8), D(13), D(20)],
            // AUDIT FIX: the "exactly at on-hand" probe read 23.5, but this book's on-hand is 23.25
            // (head.tsv: OnHandMicro 23250000), so the family had a below-probe and TWO above-probes and
            // no exactly-at-on-hand probe at all — on the one family that pins the company-positive /
            // godown-negative asymmetry, which is the shape a fix is most likely to get wrong.
            [1.25m, 23.25m, 300m],
            Godowns: 2),

        // The same shape, then the deficient godown is replenished — an intra-godown recovery that the
        // company-wide layer stack never saw as a debt.
        new Scenario("G9-002", "G9", true,
            [new ItemSpec("Widget", StandardCostMode.Set)],
            [In(491, 4, 30m, 10.33m, godown: 0, number: 1),
             Out(492, 9, 8.75m, godown: 1, number: 1),
             In(493, 14, 12m, 7.91m, godown: 1, number: 2),
             Out(494, 19, 6.5m, 55.51m, godown: 0, number: 2)],
            [D(6), D(11), D(16), D(25)],
            [1.25m, 26.75m, 300m],
            Godowns: 2),

        // ============================================================ G10 — MULTI-ITEM negative books
        // TotalClosingStockValue over items whose individual closing values are wrong in DIFFERENT
        // directions, so the aggregate cannot be right by cancellation.
        new Scenario("G10-001", "G10", true,
            [
                new ItemSpec("Widget", StandardCostMode.Set),
                new ItemSpec("Gadget", StandardCostMode.Set),
                new ItemSpec("Sprocket", StandardCostMode.Unset),
            ],
            [
                In(1201, 4, 10m, 100.13m, item: "Widget", number: 1),
                In(1202, 5, 8.5m, 3.17m, item: "Gadget", number: 2),
                In(1203, 6, 20m, 12.07m, item: "Sprocket", number: 3),
                Out(1204, 10, 25m, item: "Widget", number: 1),          // over-draw, dear -> cheap later
                Out(1205, 11, 19.75m, item: "Gadget", number: 2),       // over-draw, cheap -> dear later
                Out(1206, 12, 6.25m, 55.51m, item: "Sprocket", number: 3),
                In(1207, 16, 40m, 7.91m, item: "Widget", number: 4),
                In(1208, 17, 30m, 88.61m, item: "Gadget", number: 5),
            ],
            [D(7), D(13), D(20), D(25)],
            [1.25m, 18.75m, 600m]),

        // --- M5(b) + M5(c): multi-item, negative, WITH opening balances and a post-recovery issue.
        new Scenario("G10-002", "G10", true,
            [
                new ItemSpec("Widget", StandardCostMode.Set, OpeningQty: 5.25m, OpeningRate: 41.03m),
                new ItemSpec("Gadget", StandardCostMode.ExplicitZero, OpeningQty: 2.5m, OpeningRate: 0.37m),
            ],
            [
                In(1301, 4, 7m, 100.13m, item: "Widget", number: 1),
                Out(1302, 9, 18.75m, item: "Widget", number: 1),
                Out(1303, 10, 6.25m, item: "Gadget", number: 2),
                In(1304, 14, 25m, 7.91m, item: "Widget", number: 3),
                In(1305, 15, 11m, 13.79m, item: "Gadget", number: 4),
                Out(1306, 19, 4.5m, 55.51m, item: "Widget", number: 2),
            ],
            [D(6), D(11), D(16), D(25)],
            [1.25m, 9m, 400m]),

        // ============================================================ G11 — THE OVER-DRAW ARRIVES ON A SALES INVOICE
        // AUDIT FIX (corpus gap). Every G family built its over-draw from a Delivery Note. In the shipped
        // app negative stock overwhelmingly arises from SALES INVOICING, and StockValuationService.
        // MovementEvents merges item-invoice (Purchase/Sales) stock lines into the same stream explicitly —
        // a path NO scenario reached, so a fix that behaved differently for an invoice-borne outward was
        // measured by nothing and the harness reported full PASS.
        //
        // POST ORDER MATTERS HERE, and the movement list IS the post order. LedgerService.Post runs the
        // company-wide no-negative guard for any voucher carrying item lines, so the invoice can only be
        // posted while the book is still clean: In 30 (d5) -> the Sales invoice for 25 (d10, on-hand 5 at
        // every date, guard satisfied) -> and only THEN the guard-bypassing Delivery Note for 20 (d6),
        // which retro-drives d10 to -15. The debt is therefore created BY THE INVOICE LINE.
        //   replay: In 30 @100.13 (d5) -> Out 20 (d6) -> Out 25 INVOICE (d10, drains 10, debt 15)
        //           -> In 40 @7.91 (d15, repays 15) => 25 x 7.91 = Rs 197.75. HEAD keeps the whole 40-lot.
        new Scenario("G11-001", "G11", true, One(),
            [In(1501, 5, 30m, 100.13m, number: 1),
             OutInvoice(1502, 10, 25m, 55.51m, number: 1),
             Out(1503, 6, 20m, number: 2),
             In(1504, 15, 40m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 25m, 400m]),

        // The recovery arrives on a PURCHASE invoice too, so both directions of the item-invoice seam are
        // exercised. The VALUATION order (which is by date, not by post order) is:
        //   In 12 @100.13 (d4) -> Out 8 (d5) -> Out 9 INVOICE (d11, drains 4, debt 5)
        //   -> In 20 @7.91 PURCHASE INVOICE (d16, repays 5) => 15 x 7.91 = Rs 118.65. on-hand 12-8-9+20 = 15.
        //
        // AUDIT FIX (audit #3 finding [0], HIGH). This scenario DID NOT BUILD, on either arm, for its whole
        // life: the movement list IS the post order, and it listed the guarded PURCHASE invoice LAST, so
        // LedgerService.Post ran EnsureNoNegativeStockAnywhere over a book the d5 Delivery Note had already
        // driven to -5 at d11 and threw ("Movement would drive 'Widget' at 'Main Location' negative (on-hand
        // -5 as of 2024-04-11)"). Emit `continue`d, no engine row existed, the point oracle iterates LIVE
        // keys so it evaluated 0 subjects here, check 11 saw a SYMMETRIC exception on both arms, and the
        // recorded census had been recorded FROM that state — so the hole was blessed by the census gate.
        // The string "G11-002" appeared ZERO times in report.txt.
        //
        // The fix is the same discipline G11-001 already uses: BOTH guarded item-invoice vouchers are posted
        // while the book is still clean, and the guard-BYPASSING Delivery Note that retro-drives it negative
        // is posted LAST. Post order below: In 12 (d4) -> InInvoice 20 (d16) -> OutInvoice 9 (d11) -> Out 8
        // (d5). At each guarded post the whole timeline is non-negative (12/32, then 12/3/23), and the final
        // Delivery Note bypasses the guard. Neither the dates, the numbers, the seqs nor the quantities move,
        // so the SPEC-side replay (which sorts by date/number/seq) is bit-for-bit what the comment above says.
        // BuildOutcome is now ASSERTED (PART A) on both arms, so this can never recur silently.
        new Scenario("G11-002", "G11", true, One(),
            [In(1511, 4, 12m, 100.13m, number: 1),
             InInvoice(1514, 16, 20m, 7.91m, number: 2),
             OutInvoice(1512, 11, 9m, 55.51m, number: 1),
             Out(1513, 5, 8m, number: 2)],
            [D(6), D(12), D(20)],
            [1.25m, 15m, 300m]),

        // ============================================================ G12 — BATCHES
        // AUDIT FIX (corpus gap): every allocation passed batchLabel: null, but the engine's on-hand
        // register keys on (item, godown, BATCH) — InventoryLedger.Key — while its valuation replay is
        // batch-blind. A fix that keyed off the (item, godown, batch) tuple was measured by nothing.
        //
        // G12-001 is the batch analogue of G9: NEGATIVE IN ONE BATCH, POSITIVE COMPANY-WIDE. The
        // company-wide layer stack never goes dry, so this scenario MUST NOT MOVE.
        // ROUND 9 MOVED IT (Rs 39.55 -> Rs 79.10 on Fifo/Lifo, Rs 346.95 -> Rs 79.10 on AverageCost) by
        // re-keying valuation per batch. ROUND 10 REVERTED that, and the sentence above is true again:
        // batch B-A goes to -5 so the DEBT GATE is OPEN at the outward, but the merged stack held 30 units
        // and the outward of 25 found its units, so NO debt was created. An open gate is a permission, not
        // an instruction — this scenario is the corpus's pin on that distinction. GT-87B/GT-87C.
        //   keys: B-A = 20 - 25 = -5 ; B-B = +10 ; company +5
        //   layers: 20@100.13 + 10@7.91 -> out 25 drains all 20 and 5 of the 7.91 -> 5 @ 7.91 = Rs 39.55
        new Scenario("G12-001", "G12", true, One(),
            [InBatch(1521, 5, 20m, 100.13m, "B-A", number: 1),
             InBatch(1522, 6, 10m, 7.91m, "B-B", number: 2),
             OutBatch(1523, 10, 25m, "B-A", number: 1)],
            [D(7), D(12), D(20)],
            [1.25m, 5m, 200m]),

        // G12-002 IS company-negative, and the recovery lands in a DIFFERENT batch from the over-draw —
        // so a fix that repaid a debt per batch and one that repaid it company-wide give different answers.
        //   keys: B-A = -15 ; B-B = +40 ; company +25
        //   layers: 10@100.13 -> out 25 (debt 15) -> In 40 @7.91 repays 15 -> 25 @ 7.91 = Rs 197.75
        new Scenario("G12-002", "G12", true, One(),
            [InBatch(1531, 5, 10m, 100.13m, "B-A", number: 1),
             OutBatch(1532, 10, 25m, "B-A", number: 1),
             InBatch(1533, 15, 40m, 7.91m, "B-B", number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 25m, 300m]),

        // ============================================================ G13 — COMPOUND UNITS ON A NEGATIVE BOOK
        // AUDIT FIX (corpus gap): compound units appeared only in N7, so RateInBase x debt-repayment
        // arithmetic — the exact shape of the 12x understatement this project has already met once — was
        // untested. Lines are stated in Doz (= 12 Nos); the item's base unit is Nos.
        //   In 2 Doz @121.32 = 24 Nos @ 10.11 -> Out 30 Nos (debt 6)
        //   -> In 4 Doz @88.44 = 48 Nos @ 7.37, repays 6 => 42 x 7.37 = Rs 309.54
        new Scenario("G13-001", "G13", true, One(compound: true),
            [InDoz(1541, 5, 2m, 121.32m, number: 1),
             Out(1542, 10, 30m, number: 1),
             InDoz(1543, 15, 4m, 88.44m, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 42m, 200m]),

        // The same with a rate that does NOT divide exactly by 12 (100.13 / 12 = 8.3441666...), so the
        // debt repayment happens at a non-terminating base rate — N7-002's trap, on a negative book.
        //   In 1 Doz @100.13 = 12 Nos -> Out 20 (debt 8) -> In 40 @9.13 repays 8 => 32 x 9.13 = Rs 292.16
        new Scenario("G13-002", "G13", true, One(compound: true),
            [InDoz(1551, 4, 1m, 100.13m, number: 1),
             Out(1552, 9, 20m, number: 1),
             In(1553, 14, 40m, 9.13m, number: 2)],
            [D(6), D(11), D(16)],
            [1.25m, 32m, 300m]),

        // ============================================================ G14 — CANCELLED + POST-DATED
        // AUDIT FIX (corpus gap): no cancelled and no post-dated voucher anywhere. A cancelled voucher must
        // contribute NOTHING to on-hand or valuation in either engine; if it ever counted here, on-hand
        // would read -25 instead of +25 and check 5 would fire. Post-dating reduces to the same date bound
        // in both engines, so the post-dated recovery lot must behave exactly like a plain one — asserted,
        // not assumed.
        //   In 10 @100.13 -> Out 25 (debt 15) -> [Out 50 CANCELLED: no effect]
        //   -> In 40 @7.91 POST-DATED, repays 15 => 25 x 7.91 = Rs 197.75, on-hand 25
        new Scenario("G14-001", "G14", true, One(),
            [In(1561, 5, 10m, 100.13m, number: 1),
             Out(1562, 10, 25m, number: 1),
             OutCancelled(1563, 11, 50m, number: 2),
             InPostDated(1564, 15, 40m, 7.91m, number: 2)],
            [D(7), D(12), D(20)],
            [3.5m, 25m, 400m]),

        // ============================================================ G15 — FIFO AND LIFO GENUINELY DIVERGE
        // AUDIT #6, LOW [2]. THE HOLE: on EVERY debt subject in the corpus before this one, FIFO and LIFO
        // produced the IDENTICAL closing value and the IDENTICAL issue value, because the debt scenarios
        // never left more than ONE surviving layer. Where one layer survives there is no oldest and no
        // newest, so `lifo: true` was pinned by a single constant (GT-02, a copy of GT-01) and the LIFO
        // debt path could not be exercised INDEPENDENTLY of FIFO at all. Reference.Consume takes index 0 or
        // index Count-1 off the SAME list — swap the two and not one golden in the corpus moved. That
        // matters directly for the production slice, which has to be verified on LIFO.
        //
        // WHAT IT TAKES TO SEPARATE THEM, and why no earlier scenario did. Debt REPAYMENT is method-blind:
        // Reference.BuildStack repays out of the arriving lot at that lot's own rate before any layer is
        // created, so a repayment alone can never split FIFO from LIFO. The split needs THREE things at
        // once — (1) a debt that is created and then repaid, (2) TWO surviving layers at DIFFERENT rates
        // after the repayment, and (3) an outward AFTER both layers exist, which is the only event that
        // consults the oldest/newest end. G15-001 is the smallest book with all three.
        //
        // THE MOVEMENTS
        //   d5   In  10 @ 100.13
        //   d10  Out 25            -> drains the 10, remainder 15 becomes DEBT
        //   d15  In  40 @   7.91   -> repays the debt 15 at 7.91 (those units are COGS), surplus 25 @ 7.91
        //   d18  In  20 @  12.07   -> a SECOND surviving layer, at a different rate
        //   d22  Out 13            -> THE DIVERGENCE: FIFO eats the 7.91 end, LIFO eats the 12.07 end
        //
        // CLOSING VALUE AT d25 (on-hand 10 - 25 + 40 + 20 - 13 = 32)
        //   FIFO  layers after the d22 outward: 25-13 = 12 @ 7.91, then 20 @ 12.07
        //         12 x 7.91 = 94.92 ; 20 x 12.07 = 241.40 ; total = Rs 336.32
        //   LIFO  layers after the d22 outward: 25 @ 7.91, then 20-13 = 7 @ 12.07
        //         25 x 7.91 = 197.75 ;  7 x 12.07 =  84.49 ; total = Rs 282.24
        //   Rs 54.08 apart on ONE book — and both are two-layer stacks, so the sum is a real sum.
        //
        // ISSUE VALUES AT d25 (probes 1.25 / 32 / 500)
        //   1.25  FIFO off the OLDEST 7.91 = Rs  9.89 ; LIFO off the NEWEST 12.07 = Rs 15.09
        //   32    exactly on-hand -> the whole stack: FIFO Rs 336.32 ; LIFO Rs 282.24
        //   500   past the stack  -> stops at it:     FIFO Rs 336.32 ; LIFO Rs 282.24
        //
        // The three earlier as-of dates keep the arc visible and are IDENTICAL on both methods by
        // construction (d7 one layer, d12 debt outstanding, d20 both layers whole) — that is deliberate:
        // it isolates the divergence to the d22 outward, which is the event under test.
        // All ten numbers above are pinned as hand-derived goldens: GT-61/GT-61L/GT-62/GT-62L (closing) and
        // GI-35..GI-40 (issue). GT-62 vs GT-62L and GI-35 vs GI-36 are the pair that finally makes
        // `lifo: true` falsifiable on a debt book.
        new Scenario("G15-001", "G15", true, One(),
            [In(1571, 5, 10m, 100.13m, number: 1),
             Out(1572, 10, 25m, number: 1),
             In(1573, 15, 40m, 7.91m, number: 2),
             In(1574, 18, 20m, 12.07m, number: 3),
             Out(1575, 22, 13m, number: 2)],
            [D(7), D(12), D(20), D(25)],
            [1.25m, 32m, 500m]),

        // ============================================================ G16 — TWO GODOWNS, ONE SHORT, A COUNT
        // ROUND 9. The NEGATIVE half of the multi-godown gap: one godown genuinely goes short and a
        // physical count lands. Which godown is counted decides everything, and until now neither arm of
        // that choice existed anywhere in the corpus.

        // G16-001 — THE DESYNC, REPRODUCED VERBATIM FROM THE RE-REVIEW.
        // A count on the godown that did **NOT** go short.
        //   d5   g0 In    30 @ 100.13
        //   d10  g1 Out   40             -> g1 to -40, item net -10
        //   d15  g0 Count 30             -> the storeman counts g0 and finds exactly 30: a PER-KEY NO-OP
        //   d18  g0 In    20 @  12.00
        // on-hand: g0 = 50, g1 = -40, item = 10.
        //
        // PER KEY the book is unremarkable: g0 holds 30 @ 100.13 + 20 @ 12.00 = Rs 3,243.90 and g1 owes 40
        // units it never paid for, which carry no rate and therefore no value. Layer quantity 50 minus
        // debt 40 = 10 = the reported closing quantity, EXACTLY — which is the invariant the item-level
        // replay could not state at all (it holds 50 units of cost while reporting 10).
        //
        // THE ENGINE UNDER GATE reaches Rs 3,243.90 too, but by a route that is wrong in three places at
        // once: the item-level count writes off a debt the COUNTED key never owed, then tops the stack up
        // 30 units at the point-in-time chain rate of 100.13. HEAD reported Rs 240.00 (it topped up at a
        // running average of 0). AverageCost separates them cleanly and is where CHECK 2 convicts: the
        // reference says Rs 3,243.90, the engine says Rs 648.78.
        //
        // ROUND 10 — THIS IS THE RESIDUAL THE DEBT GATE DOES NOT FIX, AND IT IS KEPT SO IT CANNOT MOVE
        // AGAIN UNOBSERVED. g1 genuinely goes to -40, so the gate is OPEN and the debt of 10 is real. The
        // d15 count then lands on g0 — a shelf that owes nothing — and writes off a debt the counted key
        // never owed, then tops the emptied stack up through the point-in-time chain. The layers hold 50
        // units worth Rs 3,243.90 while the register reports 10: an implied Rs 324.39/unit on an item
        // whose dearest lot cost Rs 100.13. HEAD said Rs 240.00 (it priced the counted units at a running
        // average of 0 — a wipe in the other direction). NEITHER is the book. Because value and quantity
        // here describe DIFFERENT unit counts, CHECK 6's implied-unit-rate premise is false on this
        // subject and the comparator records it as STRUCTURALLY-UNSATISFIABLE, by name, with the measured
        // delta — see FactFlatNetMicro and the DESYNC list in PART A.
        //
        // THE Rs 12.00 RATE IS DELIBERATE AND IS THE ONE PLACE THIS CORPUS BREAKS ITS OWN ODD-PAISA RULE.
        // The re-review measured Rs 3,243.90 on exactly this book, and reproducing the measured book
        // verbatim is worth more here than an odd paisa: G16-002 alongside it is odd-paisa throughout.
        new Scenario("G16-001", "G16", true, One(StandardCostMode.Unset),
            [In(1731, 5, 30m, 100.13m, godown: 0, number: 1),
             Out(1732, 10, 40m, godown: 1, number: 1),
             Count(1733, 15, 30m, godown: 0, number: 2),
             In(1734, 18, 20m, 12m, godown: 0, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 10m, 300m],
            Godowns: 2),

        // G16-002 — A COUNT ON THE GODOWN THAT **DID** GO SHORT.
        //   d5   g0 In    10 @ 100.13
        //   d10  g1 Out   25            -> g1 to -25
        //   d15  g1 Count  8            -> the SHORT key is counted: its debt is written off and 8 units
        //                                  are topped up by the best-available-cost chain
        // on-hand: g0 = 10, g1 = 8, item = 18.
        //
        // THE CHAIN IS THE INTERESTING PART. Godown 1 has never received a rated inward in its life, and
        // the chain is resolved per ITEM and POINT-IN-TIME (rule 7), so it answers with the only rate the
        // item had paid when those units were counted, Rs 100.13. (A chain resolved PER KEY would reach
        // its last link and value eight physically-counted units at Rs 0.00 — the wiped-asset shape this
        // programme exists to prevent, and one of the two CRITICALs that got the per-key replay reverted.)
        //   THE HONEST PER-KEY FIGURE at d20 : 10 x 100.13 + 8 x 100.13 = Rs 1,802.34 on 18 units.
        //   WHAT SHIPS (item-level + gate): Rs 801.04 on Fifo/Lifo — it keeps the 8 topped-up units and
        //   LOSES godown 0's ten real ones, because the item-level count reconciled the whole merged stack
        //   to 8. The OTHER DIRECTION of the same residual as G16-001, and pinned for the same reason.
        //   AverageCost: Rs 1,802.34 (the pool's 100.13 x the netted 18).
        //   HEAD: Rs 0.00.
        // The StandardCost is UNSET on purpose so the chain has to reach its last link; with one set the
        // gap would be 8 x 9.77 and would hide how far the chain reaches.
        new Scenario("G16-002", "G16", true, One(StandardCostMode.Unset),
            [In(1741, 5, 10m, 100.13m, godown: 0, number: 1),
             Out(1742, 10, 25m, godown: 1, number: 1),
             Count(1743, 15, 8m, godown: 1, number: 2)],
            [D(7), D(12), D(20)],
            [1.25m, 18m, 300m],
            Godowns: 2),

        // ============================================================ E1 — equal-dated inwards, two orders
        // Same date AND same number on both inwards, so ONLY the Guid can break the tie. E1-002 inserts
        // the identical (Guid, rate) pair in the opposite order; the two scenarios must agree exactly.
        new Scenario("E1-001", "E1", true, One(),
            [In(501, 5, 10m, 10.33m, number: 1), In(502, 5, 20m, 12.07m, number: 1), Out(503, 10, 15m, number: 1)],
            [D(6), D(12)],
            [1.25m, 15m, 100m]),

        new Scenario("E1-002", "E1", true, One(),
            [In(502, 5, 20m, 12.07m, number: 1), In(501, 5, 10m, 10.33m, number: 1), Out(503, 10, 15m, number: 1)],
            [D(6), D(12)],
            [1.25m, 15m, 100m]),
    ];

    /// <summary>A built book: the company, the item ids by name, and the two engine services that read it.</summary>
    public sealed record Book(
        Company Company,
        IReadOnlyDictionary<string, Guid> ItemIds,
        StockValuationService Valuation,
        InventoryLedger OnHand);

    /// <summary>Builds a scenario against one valuation method. Fresh company each time — no shared state.</summary>
    public static Book Build(Scenario s, StockValuationMethod method)
    {
        var company = CompanyFactory.CreateSeeded("Oracle Co", FyStart);
        var masters = new InventoryService(company);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");

        Guid? compoundUnitId = null;
        if (s.Items.Any(i => i.CompoundUnit))
        {
            var doz = masters.CreateSimpleUnit("Doz", "Dozen");
            // First unit = the LARGER unit; tail = the item's base unit, so the line reduces to Nos.
            compoundUnitId = masters
                .CreateCompoundUnit("Doz-Nos", "Dozen of Numbers", doz.Id, nos.Id, CompoundNumerator)
                .Id;
        }

        // Godown 0 is always the seeded Main Location; extras are created in index order.
        var godowns = new List<Guid> { company.MainLocation!.Id };
        for (var g = 1; g < s.Godowns; g++)
            godowns.Add(masters.CreateGodown("Store " + (char)('A' + g)).Id);

        var itemIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var spec in s.Items)
        {
            var item = masters.CreateStockItem(
                spec.Name, group.Id, nos.Id,
                valuationMethod: method,
                standardCost: StandardCostOf(spec) is { } sc ? Money.FromRupees(sc) : null);
            itemIds[spec.Name] = item.Id;

            if (spec.OpeningQty > 0m)
                masters.AddOpeningBalance(item.Id, godowns[spec.OpeningGodown], spec.OpeningQty,
                    Money.FromRupees(spec.OpeningRate));
        }

        var posting = new InventoryPostingService(company);
        var receiptTypeId = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id;
        var deliveryTypeId = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote).Id;
        var physicalTypeId = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock).Id;

        // Accounting scaffolding for item-invoice movements, created ONLY when a scenario has one, so every
        // pre-existing scenario builds a byte-identical company.
        LedgerService? ledgers = null;
        Guid debtorId = Guid.Empty, salesId = Guid.Empty, creditorId = Guid.Empty, purchasesId = Guid.Empty;
        if (s.Movements.Any(m => m.Invoice))
        {
            ledgers = new LedgerService(company);
            debtorId = AddLedger(company, "Oracle Debtor", "Sundry Debtors", openingIsDebit: true);
            salesId = AddLedger(company, "Oracle Sales", "Sales Accounts", openingIsDebit: false);
            creditorId = AddLedger(company, "Oracle Creditor", "Sundry Creditors", openingIsDebit: false);
            purchasesId = AddLedger(company, "Oracle Purchases", "Purchase Accounts", openingIsDebit: true);
        }

        // The movement list IS the post order (family G11 depends on it: an item-invoice voucher runs the
        // company-wide no-negative guard, so it can only be posted while the book is still clean).
        foreach (var m in s.Movements)
        {
            var spec = s.ItemOf(m);
            var itemId = itemIds[spec.Name];
            var godownId = godowns[m.Godown];
            var batch = m.Batch.Length == 0 ? null : m.Batch;

            // ---------- ITEM-INVOICE (Purchase/Sales) stock line ----------
            if (m.Invoice)
            {
                if (m.Kind == MoveKind.Count)
                    throw new InvalidOperationException("A physical count cannot be an item-invoice line.");
                var rate = m.Rate ?? throw new InvalidOperationException("An item-invoice line must carry a rate.");
                var inward = m.Kind == MoveKind.Inward;
                var baseTypeId = company.VoucherTypes
                    .First(t => t.BaseType == (inward ? VoucherBaseType.Purchase : VoucherBaseType.Sales)).Id;

                // The pairing invariant: item-line value must equal the stock accounting leg to the paisa
                // (Sales => credit to Sales Accounts; Purchase => debit to Purchase Accounts).
                var value = Money.FromRupees(decimal.Round(m.Qty * rate, 2, MidpointRounding.AwayFromZero));
                var lines = inward
                    ? new[] { new EntryLine(purchasesId, value, DrCr.Debit), new EntryLine(creditorId, value, DrCr.Credit) }
                    : new[] { new EntryLine(debtorId, value, DrCr.Debit), new EntryLine(salesId, value, DrCr.Credit) };

                var invoiceLine = new VoucherInventoryLine(
                    itemId, godownId, m.Qty, Money.FromRupees(rate),
                    direction: inward ? StockDirection.Inward : StockDirection.Outward,
                    batchLabel: batch,
                    unitId: m.UseCompoundUnit ? compoundUnitId : null);

                ledgers!.Post(new Voucher(
                    VoucherId(m.Seq), baseTypeId, m.Date, lines,
                    number: m.Number,
                    cancelled: m.Cancelled,
                    postDated: m.PostDated,
                    inventoryLines: new[] { invoiceLine }));
                continue;
            }

            // ---------- pure-stock InventoryVoucher ----------
            InventoryVoucher voucher;
            if (m.Kind == MoveKind.Count)
            {
                voucher = InventoryVoucher.PhysicalStock(
                    VoucherId(m.Seq), physicalTypeId, m.Date,
                    new[] { new PhysicalStockLine(itemId, godownId, m.Qty, batch) },
                    number: m.Number);
            }
            else
            {
                var allocation = new InventoryAllocation(
                    itemId, godownId, m.Qty,
                    m.Kind == MoveKind.Inward ? StockDirection.Inward : StockDirection.Outward,
                    m.Rate is null ? null : Money.FromRupees(m.Rate.Value),
                    batchLabel: batch,
                    unitId: m.UseCompoundUnit ? compoundUnitId : null);

                voucher = new InventoryVoucher(
                    VoucherId(m.Seq),
                    m.Kind == MoveKind.Inward ? receiptTypeId : deliveryTypeId,
                    m.Date,
                    new[] { allocation },
                    number: m.Number,
                    cancelled: m.Cancelled,
                    postDated: m.PostDated);
            }

            if (s.GuardBypass) company.AddInventoryVoucher(voucher);
            else posting.Post(voucher);
        }

        return new Book(company, itemIds, new StockValuationService(company), new InventoryLedger(company));
    }

    /// <summary>A zero-opening ledger under a named seeded group — the accounting leg an item invoice needs.</summary>
    private static Guid AddLedger(Company company, string name, string groupName, bool openingIsDebit)
    {
        var group = company.FindGroupByName(groupName)
            ?? throw new InvalidOperationException($"Seeded group '{groupName}' not found.");
        var ledger = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit);
        company.AddLedger(ledger);
        return ledger.Id;
    }
}
