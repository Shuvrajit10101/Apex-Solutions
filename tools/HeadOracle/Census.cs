namespace HeadOracle;

// ---------------------------------------------------------------------------------------------------
// THE RECORDED EXPECTED CENSUS.
//
// WHY THIS FILE EXISTS
//   Every check used to assert only "evaluated > 0". That is not enough, and the audit proved it with the
//   most realistic wrong-fix shape there is: make the engine refuse the voucher at posting time. Corpus
//   .Build then throws for every G*/E1 scenario, Emit `continue`s, and those rows are simply ABSENT from
//   the live arm. The point oracle iterates the live arm's keys, so absent rows are neither evaluated nor
//   counted as mismatches. CHECK 3's subject count fell from 332 to 134 AND IT PRINTED PASS. Checks 5, 9
//   and 10 passed. Checks 6/7/8 printed "live 0/0" for E1 and every single G family and STILL printed
//   PASS, because the assertion only fired when the whole-arm sum was zero. Nothing anywhere said
//   "I measured 40% of what I measured last time".
//
//   Comparing live against head catches that. It does NOT catch a corpus or emitter regression that
//   shrinks BOTH arms identically — and the corpus is edited far more often than the engine. So the
//   expected counts are RECORDED here, in a source file, and asserted against the head arm on every run.
//
// HOW TO RE-RECORD (a deliberate act, never a side effect)
//   1. Run tools/HeadOracle/run-oracle.sh.
//   2. Take the "FULL CENSUS (head arm)" block from .oracle-work/report.txt — every line beginning
//      "    CENSUS  " — and replace Data below with those "<cell><TAB><count>" pairs.
//   3. State in the commit message WHY coverage changed. A census that shrinks without a reason is the
//      defect this file exists to catch.
//
//   A census cell is either a named counter (CHECK3.subjects) or a checks-6/7/8 triple
//   "<check>|<family>|<method>", because the collapse showed live 0/0 in every G cell while the
//   whole-arm sum stayed comfortably non-zero.
// ---------------------------------------------------------------------------------------------------

public static class ExpectedCensus
{
    /// <summary>Parses <see cref="Data"/> into (cell -> expected count). An empty recording is itself a failure.</summary>
    public static Dictionary<string, int> Parse()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in Data.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tab = line.LastIndexOf('\t');
            if (tab < 0) continue;
            if (int.TryParse(line[(tab + 1)..].Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                map[line[..tab]] = n;
        }
        return map;
    }

    // RECORDING LOG — every change to Data below, and why.
    //   * initial recording, 300 cells, from the head arm at baseline 9c2bded after the second-audit
    //     rework (corpus families G11..G14 added, G2-002 and G9-001 probes corrected).
    //   * +1 cell, 301: CHECK3b.subjects = 552. The point oracle was widened to the flat methods
    //     (StandardCost / LastPurchaseCost / LastSaleCost), which the reference computes independently
    //     via a rate chain rather than by echoing HEAD. Coverage GREW; nothing shrank. This gate caught
    //     the change on the first run after it and refused to judge until it was recorded here — which
    //     is exactly the behaviour it exists for.
    //   * 301 -> 306 cells, and 27 existing cells RE-RECORDED, after audit #3. EVERY change is a GROWTH;
    //     not one cell shrank. Three causes, all deliberate:
    //     (1) AUDIT #3 FINDING [0] — G11-002 DID NOT BUILD, on either arm, for its whole life, and this
    //         census had been recorded FROM that state, so the gate was actively blessing the hole. Its
    //         post order now lands both guarded item-invoice vouchers on a clean book. Coverage grew by
    //         exactly one scenario: CHECK3 368->374 (2 methods x 3 dates), CHECK3b 552->561 (3 x 3),
    //         CHECK5 1104->1122 (6 x 3), CHECK9 996->1014, CHECK10 3312->3366, CHECK11 24972->25677,
    //         CHECK1 9563->9776, CHECK2.rows 3170->3194, and the twelve 6/7/8|G11|* cells doubled
    //         (4->8 and 6->12) because G11 now has TWO scenarios instead of one that measured.
    //         NEW CELL: BUILD.ok = 306 — the count of (scenario x method) cells that CONSTRUCT. It is
    //         pinned so a scenario can never again vanish behind a symmetric exception.
    //     (2) AUDIT #3 FINDING [2] + the 2026-07-27 USER SCOPE DECISION that AverageCost IS to be fixed.
    //         NEW CELLS: CHECK4b.subjects = 71 (the debt-aware AverageCost oracle is now CALIBRATED
    //         against HEAD on every never-negative book) and CHECK2.subjects = 187 (CHECK 2 is now a
    //         point oracle over AverageCost, not a byte-lock to HEAD).
    //     (3) AUDIT #3 FINDINGS [1] and [3] — the value invariant now binds each layer to a SPEC LOT.
    //         NEW CELLS: VALUE-INVARIANT.originBoundLayers = 364 (layers whose rate is checked against
    //         THE rate of THE lot they came from) and VALUE-INVARIANT.hullBlendLayers = 2 (chain-priced
    //         blends, now bounded by the admissible convex hull instead of excused by their own tag).
    //         originBoundLayers is pinned precisely so the rate binding cannot quietly evaluate nothing.
    //     (4) 306 -> 307 cells. NEW CELL: CHECK4b.selfConsistency = 187. Because CHECK 2 now judges against
    //         RefClosingValueDebtAwarePaisa while CHECK 10 and CHECK 9(b) are DERIVED from
    //         RefClosingValuePaisa, Reference.Value's AverageCost branch had to become debt-aware in the
    //         same change — otherwise the harness would demand two different AverageCost answers on one
    //         subject and convict the engine CHECK 2 prescribes. PART A now asserts the two columns are
    //         equal on all 187 subjects, and this cell stops that assertion evaluating nothing.
    //         (Consequence, deliberate: CHECK 10 143 and CHECK 9(b) 68 disagreements at HEAD, up from 125
    //         and 62, because HEAD's AverageCost issue values and company totals are now judged too.)
    //   * 307 -> 310 cells after audit #4, with 3 cells RE-RECORDED and ONE DELETED. Every count change is
    //     a GROWTH; the deletion is the removal of a cell that was measuring a tautology.
    //     (5) AUDIT #4 FINDING [0] (CRITICAL) — CHECK 2 issues engine verdicts from a debt branch CHECK 4b
    //         cannot reach, because a never-negative book carries no debt and those clauses are DEAD CODE
    //         there. NEW CELL: CHECK4c.goldens = 32 — the hand-derived, Python-cross-checked literal paisa
    //         constants in Goldens.cs. It is pinned so the only validation the debt branch has can never
    //         quietly evaluate nothing, which is precisely how the CHECK 4b hole existed.
    //     (6) AUDIT #4 FINDING [1](2) — the ORDERING assertion audit #3 asked for and round 4 did not
    //         build. NEW CELLS: VALUE-INVARIANT.orderingSubjects = 146 (subjects whose book ran dry, so the
    //         rule constrains them) and VALUE-INVARIANT.orderingLayers = 72 (layers actually tested against
    //         the post-dry lot set). Both are pinned because a resurrect-the-drained-lot poison passes the
    //         entire rate binding 0/0/0/0 and this is the only thing that convicts it.
    //     (7) AUDIT #4 FINDING [5] (LOW) — `perLot` was accumulated and never read. NEW CELL:
    //         VALUE-INVARIANT.perLotChecks = 378, the (subject, lot) pairs whose AGGREGATE claimed quantity
    //         is now bounded by what that lot ever supplied.
    //     (8) DELETED CELL: CHECK4b.selfConsistency. AUDIT #4 FINDING [2] (HIGH) showed the gate it pinned
    //         was a TAUTOLOGY — both columns are Paisa(RunAverageDebtAware(...) * closingQty), the same
    //         pure function with the same arguments, so poisoning it moved both together and the gate still
    //         printed PASS. A cell that pins the size of an unfailable comparison inflates the apparent
    //         validation surface, so it is gone. CHECK4c.goldens replaces it with an EXTERNAL anchor: for
    //         every AverageCost golden BOTH columns are asserted against the same hand-derived constant.
    //     (9) RE-RECORDED: CHECK1.rows 9776 -> 9847 and CHECK11.rows 25677 -> 25864 — the new spec-derived
    //         FactPostDryLots row (one per scenario x item x as-of, 187 in total, 71 of them on
    //         never-negative books). More rows compared, none lost. CHECK2.rows is unchanged because
    //         FactPostDryLots lives on the method-less "-" row.
    //   * 310 -> 314 cells after audit #5, with 3 cells RE-RECORDED and FOUR ADDED. Every count change is
    //     a GROWTH; nothing shrank and nothing was deleted.
    //     (10) AUDIT #5 FINDING [0] (HIGH) — CHECK 4c pinned CLOSING VALUES ONLY, so the reference's ISSUE
    //          arm was the one verdict-issuing output with NO external anchor on the debt branch. The
    //          adversary rewrote 68 of the 120 reported CHECK 10 demands (40x on the crux) with PART A
    //          printing SOUND and CHECK 4c PASS. NEW CELLS: CHECK4c.issueGoldens = 34 (hand-derived literal
    //          constants on the RefIssueValue columns, covering an issue under an outstanding debt, an issue
    //          after recovery, an issue drawn across a repaid layer, and the AverageCost issue path) and
    //          CHECK4c.issueStructurePairs = 722 (the (subject, probe) pairs at or above on-hand, where the
    //          constant-free structural assertion "an issue at or above on-hand costs EXACTLY the closing
    //          value" bites — that alone convicts all 68 of the fabricated rows). All NINE subjects that
    //          rest on the settled rule now carry an ISSUE golden as well as a closing one, and the coverage
    //          assertion requires BOTH: a rule nothing calibrates anchored on the Balance Sheet alone is the
    //          exact shape finding [0] demonstrated. RE-RECORDED:
    //          CHECK4c.goldens 32 -> 84, because the closing table was widened to pin every subject the
    //          harness actually CONVICTS on (audit #5 finding [4]): all 70 CHECK 3 convictions and all 6
    //          CHECK 2 convictions, up from 19 and 5.
    //     (11) AUDIT #5 FINDING [1] (MEDIUM) — the census pinned the NUMBER of goldens and never their
    //          VALUES, so editing a constant to match the code (the ONE thing Goldens.cs forbids) was the
    //          one thing no gate detected. NEW CELL: CHECK4c.goldenDigest = 1092367358, an FNV-1a digest over
    //          the ordered (Id, Scenario, Item, Method, AsOf, Probe, Paisa, Clause) tuples of BOTH tables.
    //          Any edit to any constant changes this cell and must be justified here. (The Working prose is
    //          excluded so a re-worded derivation is not a census event; prose is tied to its constant by a
    //          separate comparator assertion that parses the last rupee figure out of it.)
    //     (12) AUDIT #5 FINDING [3] (LOW) — CHECK 4c's coverage half iterates RefProvenance, a tag the
    //          reference emits ABOUT ITSELF, and no cell pinned the INVENTED population, so a PARTIAL retag
    //          could shrink it silently. NEW CELL: CHECK4c.inventedSubjects = 9, the population derived
    //          FROM THE SPEC by Facts.InventedByRule (a pure quantity walk), which the comparator also
    //          asserts equals the emitted tags in both directions.
    //     (13) RE-RECORDED: CHECK1.rows 9847 -> 9918 and CHECK11.rows 25864 -> 26051 — the new spec-derived
    //          FactInventedByRule row (one per scenario x item x as-of, 187 in total, 71 of them on
    //          never-negative books). More rows compared, none lost. CHECK2.rows is unchanged because
    //          FactInventedByRule lives on the method-less "-" row.
    //   * 314 -> 327 cells after audit #6 (round 7), with 22 cells RE-RECORDED and THIRTEEN ADDED. EVERY
    //     count change is a GROWTH: not one cell shrank and none was deleted. Two causes.
    //     (14) AUDIT #6 [2] (LOW) — THE LIFO DEBT PATH WAS PINNED BY EXACTLY ONE CONSTANT AND THE CORPUS
    //          COULD NOT EXERCISE IT INDEPENDENTLY. On every debt subject that existed, FIFO and LIFO gave
    //          the IDENTICAL closing value and the IDENTICAL issue value, because no debt scenario left
    //          more than ONE surviving layer — and where one layer survives there is no oldest and no
    //          newest. Reference.Consume differs between the methods in exactly one place (index 0 vs index
    //          Count-1 of the same list) and swapping them moved no golden. That is a live risk for the
    //          production slice, which has to be verified on LIFO. NEW SCENARIO G15-001 (family G15): a
    //          debt created and repaid, TWO surviving layers at different rates (25@7.91 and 20@12.07), and
    //          an outward of 13 AFTER both exist — the only event that consults an end of the stack. FIFO
    //          closes at Rs 336.32, LIFO at Rs 282.24, and the issue probes split 9.89/15.09 and
    //          282.24/336.32. Measured on this run's own emitted rows: of 76 debt subjects, G15-001 is the
    //          ONLY one where FIFO and LIFO disagree at all.
    //          TWELVE NEW CELLS, the 6/7/8 x G15 x method triples (band 6 each, spend 8 each, COGS 6 each).
    //          RE-RECORDED, all upward, all from that one scenario: BUILD.ok 306->312, CHECK1.rows
    //          9918->9989, CHECK2.rows 3194->3263, CHECK2.subjects 187->191, CHECK3.subjects 374->382,
    //          CHECK3b.subjects 561->573, CHECK5.closingQty/onHand 1122->1146, CHECK9.totalSum/totalOracle
    //          1014->1038, CHECK10.subjects 3366->3438, CHECK11.rows 26051->26809,
    //          SELF-CONSISTENCY.subjects 300->306, VALUE-INVARIANT.subjects 374->382, .originBoundLayers
    //          364->374, .orderingSubjects 146->152, .orderingLayers 72->80, .perLotChecks 378->388,
    //          CHECK4c.issueStructurePairs 722->738, CHECK4c.goldens 84->88 and CHECK4c.issueGoldens 34->44
    //          (the fourteen hand-derived constants that pin the divergence: GT-61/61L/62/62L and
    //          GI-35..GI-44), and CHECK4c.goldenDigest 1092367358 -> 944370617, which is those fourteen
    //          ADDITIONS to the tables and nothing else — no existing constant was touched.
    //          (Consequence, deliberate: CHECK 2 8, CHECK 3 74, CHECK 10 155 and CHECK 9(b) 74
    //          disagreements at HEAD, up from 6/70/143/68, because HEAD is wrong on this book too.)
    //     (15) AUDIT #6 [1] (LOW) — CLAUSE COVERAGE WAS ASSERTED FROM SELF-DECLARED LABELS. The coverage
    //          gate compared Goldens.RequiredClauses against a projection of the golden table's OWN Clause
    //          tags, so it proved every required tag APPEARS and never that any of them is TRUE. Nothing
    //          asked whether a golden tagged issue:debt-outstanding is actually taken with a debt
    //          outstanding, so right numbers under wrong labels reported full coverage while leaving a
    //          clause genuinely unexercised. NEW CELL: CHECK4c.clauseVerified = 132 — every golden in both
    //          tables, each label now required to be TRUE of its own subject as judged by the new
    //          spec-derived FactDebtShape row (a PURE QUANTITY walk in Facts.cs, sharing no code with the
    //          debt VALUE branch). It is pinned so the label check can never quietly stop evaluating and
    //          let coverage revert to self-attestation. A false label is a HARNESS failure (exit 3).
    //          FactDebtShape is also why CHECK1.rows and CHECK11.rows grew by more than G15-001 alone
    //          accounts for: one more spec row per (scenario, item, as-of). More rows compared, none lost.

    /// <summary>Recorded from the head arm. See the re-recording procedure above.</summary>
    public const string Data = @"
6 closing-rate band|E1|AverageCost	8
6 closing-rate band|E1|Fifo	8
6 closing-rate band|E1|LastPurchaseCost	8
6 closing-rate band|E1|LastSaleCost	8
6 closing-rate band|E1|Lifo	8
6 closing-rate band|E1|StandardCost	8
6 closing-rate band|G10|AverageCost	23
6 closing-rate band|G10|Fifo	23
6 closing-rate band|G10|LastPurchaseCost	23
6 closing-rate band|G10|LastSaleCost	23
6 closing-rate band|G10|Lifo	23
6 closing-rate band|G10|StandardCost	23
6 closing-rate band|G11|AverageCost	8
6 closing-rate band|G11|Fifo	8
6 closing-rate band|G11|LastPurchaseCost	8
6 closing-rate band|G11|LastSaleCost	8
6 closing-rate band|G11|Lifo	8
6 closing-rate band|G11|StandardCost	8
6 closing-rate band|G12|AverageCost	10
6 closing-rate band|G12|Fifo	10
6 closing-rate band|G12|LastPurchaseCost	10
6 closing-rate band|G12|LastSaleCost	10
6 closing-rate band|G12|Lifo	10
6 closing-rate band|G12|StandardCost	10
6 closing-rate band|G13|AverageCost	8
6 closing-rate band|G13|Fifo	8
6 closing-rate band|G13|LastPurchaseCost	8
6 closing-rate band|G13|LastSaleCost	8
6 closing-rate band|G13|Lifo	8
6 closing-rate band|G13|StandardCost	8
6 closing-rate band|G14|AverageCost	4
6 closing-rate band|G14|Fifo	4
6 closing-rate band|G14|LastPurchaseCost	4
6 closing-rate band|G14|LastSaleCost	4
6 closing-rate band|G14|Lifo	4
6 closing-rate band|G14|StandardCost	4
6 closing-rate band|G15|AverageCost	6
6 closing-rate band|G15|Fifo	6
6 closing-rate band|G15|LastPurchaseCost	6
6 closing-rate band|G15|LastSaleCost	6
6 closing-rate band|G15|Lifo	6
6 closing-rate band|G15|StandardCost	6
6 closing-rate band|G1|AverageCost	18
6 closing-rate band|G1|Fifo	18
6 closing-rate band|G1|LastPurchaseCost	18
6 closing-rate band|G1|LastSaleCost	18
6 closing-rate band|G1|Lifo	18
6 closing-rate band|G1|StandardCost	18
6 closing-rate band|G2|AverageCost	18
6 closing-rate band|G2|Fifo	18
6 closing-rate band|G2|LastPurchaseCost	18
6 closing-rate band|G2|LastSaleCost	18
6 closing-rate band|G2|Lifo	18
6 closing-rate band|G2|StandardCost	18
6 closing-rate band|G3|AverageCost	4
6 closing-rate band|G3|Fifo	4
6 closing-rate band|G3|LastPurchaseCost	4
6 closing-rate band|G3|LastSaleCost	4
6 closing-rate band|G3|Lifo	4
6 closing-rate band|G3|StandardCost	4
6 closing-rate band|G4|AverageCost	4
6 closing-rate band|G4|Fifo	4
6 closing-rate band|G4|LastPurchaseCost	4
6 closing-rate band|G4|LastSaleCost	4
6 closing-rate band|G4|Lifo	4
6 closing-rate band|G4|StandardCost	4
6 closing-rate band|G5|AverageCost	4
6 closing-rate band|G5|Fifo	4
6 closing-rate band|G5|LastPurchaseCost	4
6 closing-rate band|G5|LastSaleCost	4
6 closing-rate band|G5|Lifo	4
6 closing-rate band|G5|StandardCost	4
6 closing-rate band|G6|AverageCost	10
6 closing-rate band|G6|Fifo	10
6 closing-rate band|G6|LastPurchaseCost	10
6 closing-rate band|G6|LastSaleCost	10
6 closing-rate band|G6|Lifo	10
6 closing-rate band|G6|StandardCost	10
6 closing-rate band|G7|AverageCost	8
6 closing-rate band|G7|Fifo	8
6 closing-rate band|G7|LastPurchaseCost	8
6 closing-rate band|G7|LastSaleCost	8
6 closing-rate band|G7|Lifo	8
6 closing-rate band|G7|StandardCost	8
6 closing-rate band|G8|AverageCost	14
6 closing-rate band|G8|Fifo	14
6 closing-rate band|G8|LastPurchaseCost	14
6 closing-rate band|G8|LastSaleCost	14
6 closing-rate band|G8|Lifo	14
6 closing-rate band|G8|StandardCost	14
6 closing-rate band|G9|AverageCost	16
6 closing-rate band|G9|Fifo	16
6 closing-rate band|G9|LastPurchaseCost	16
6 closing-rate band|G9|LastSaleCost	16
6 closing-rate band|G9|Lifo	16
6 closing-rate band|G9|StandardCost	16
6 closing-rate band|N1|AverageCost	12
6 closing-rate band|N1|Fifo	12
6 closing-rate band|N1|LastPurchaseCost	12
6 closing-rate band|N1|LastSaleCost	12
6 closing-rate band|N1|Lifo	12
6 closing-rate band|N1|StandardCost	12
6 closing-rate band|N2|AverageCost	26
6 closing-rate band|N2|Fifo	26
6 closing-rate band|N2|LastPurchaseCost	26
6 closing-rate band|N2|LastSaleCost	26
6 closing-rate band|N2|Lifo	26
6 closing-rate band|N2|StandardCost	26
6 closing-rate band|N3|AverageCost	16
6 closing-rate band|N3|Fifo	16
6 closing-rate band|N3|LastPurchaseCost	16
6 closing-rate band|N3|LastSaleCost	16
6 closing-rate band|N3|Lifo	16
6 closing-rate band|N3|StandardCost	16
6 closing-rate band|N4|AverageCost	16
6 closing-rate band|N4|Fifo	16
6 closing-rate band|N4|LastPurchaseCost	16
6 closing-rate band|N4|LastSaleCost	16
6 closing-rate band|N4|Lifo	16
6 closing-rate band|N4|StandardCost	16
6 closing-rate band|N5|AverageCost	12
6 closing-rate band|N5|Fifo	12
6 closing-rate band|N5|LastPurchaseCost	12
6 closing-rate band|N5|LastSaleCost	12
6 closing-rate band|N5|Lifo	12
6 closing-rate band|N5|StandardCost	12
6 closing-rate band|N6|AverageCost	18
6 closing-rate band|N6|Fifo	18
6 closing-rate band|N6|LastPurchaseCost	18
6 closing-rate band|N6|LastSaleCost	18
6 closing-rate band|N6|Lifo	18
6 closing-rate band|N6|StandardCost	18
6 closing-rate band|N7|AverageCost	12
6 closing-rate band|N7|Fifo	12
6 closing-rate band|N7|LastPurchaseCost	12
6 closing-rate band|N7|LastSaleCost	12
6 closing-rate band|N7|Lifo	12
6 closing-rate band|N7|StandardCost	12
6 closing-rate band|N8|AverageCost	8
6 closing-rate band|N8|Fifo	8
6 closing-rate band|N8|LastPurchaseCost	8
6 closing-rate band|N8|LastSaleCost	8
6 closing-rate band|N8|Lifo	8
6 closing-rate band|N8|StandardCost	8
6 closing-rate band|N9|AverageCost	8
6 closing-rate band|N9|Fifo	8
6 closing-rate band|N9|LastPurchaseCost	8
6 closing-rate band|N9|LastSaleCost	8
6 closing-rate band|N9|Lifo	8
6 closing-rate band|N9|StandardCost	8
7 total-spend containment|E1|AverageCost	8
7 total-spend containment|E1|Fifo	8
7 total-spend containment|E1|Lifo	8
7 total-spend containment|G10|AverageCost	28
7 total-spend containment|G10|Fifo	28
7 total-spend containment|G10|Lifo	28
7 total-spend containment|G11|AverageCost	12
7 total-spend containment|G11|Fifo	12
7 total-spend containment|G11|Lifo	12
7 total-spend containment|G12|AverageCost	12
7 total-spend containment|G12|Fifo	12
7 total-spend containment|G12|Lifo	12
7 total-spend containment|G13|AverageCost	12
7 total-spend containment|G13|Fifo	12
7 total-spend containment|G13|Lifo	12
7 total-spend containment|G14|AverageCost	6
7 total-spend containment|G14|Fifo	6
7 total-spend containment|G14|Lifo	6
7 total-spend containment|G15|AverageCost	8
7 total-spend containment|G15|Fifo	8
7 total-spend containment|G15|Lifo	8
7 total-spend containment|G1|AverageCost	26
7 total-spend containment|G1|Fifo	26
7 total-spend containment|G1|Lifo	26
7 total-spend containment|G2|AverageCost	34
7 total-spend containment|G2|Fifo	34
7 total-spend containment|G2|Lifo	34
7 total-spend containment|G3|AverageCost	12
7 total-spend containment|G3|Fifo	12
7 total-spend containment|G3|Lifo	12
7 total-spend containment|G4|AverageCost	8
7 total-spend containment|G4|Fifo	8
7 total-spend containment|G4|Lifo	8
7 total-spend containment|G5|AverageCost	4
7 total-spend containment|G5|Fifo	4
7 total-spend containment|G5|Lifo	4
7 total-spend containment|G6|AverageCost	14
7 total-spend containment|G6|Fifo	14
7 total-spend containment|G6|Lifo	14
7 total-spend containment|G7|AverageCost	12
7 total-spend containment|G7|Fifo	12
7 total-spend containment|G7|Lifo	12
7 total-spend containment|G8|AverageCost	20
7 total-spend containment|G8|Fifo	20
7 total-spend containment|G8|Lifo	20
7 total-spend containment|G9|AverageCost	16
7 total-spend containment|G9|Fifo	16
7 total-spend containment|G9|Lifo	16
7 total-spend containment|N1|AverageCost	12
7 total-spend containment|N1|Fifo	12
7 total-spend containment|N1|Lifo	12
7 total-spend containment|N2|AverageCost	26
7 total-spend containment|N2|Fifo	26
7 total-spend containment|N2|Lifo	26
7 total-spend containment|N3|AverageCost	16
7 total-spend containment|N3|Fifo	16
7 total-spend containment|N3|Lifo	16
7 total-spend containment|N4|AverageCost	16
7 total-spend containment|N4|Fifo	16
7 total-spend containment|N4|Lifo	16
7 total-spend containment|N5|AverageCost	10
7 total-spend containment|N5|Fifo	10
7 total-spend containment|N5|Lifo	10
7 total-spend containment|N6|AverageCost	18
7 total-spend containment|N6|Fifo	18
7 total-spend containment|N6|Lifo	18
7 total-spend containment|N7|AverageCost	12
7 total-spend containment|N7|Fifo	12
7 total-spend containment|N7|Lifo	12
7 total-spend containment|N8|AverageCost	8
7 total-spend containment|N8|Fifo	8
7 total-spend containment|N8|Lifo	8
7 total-spend containment|N9|AverageCost	8
7 total-spend containment|N9|Fifo	8
7 total-spend containment|N9|Lifo	8
8 COGS conservation|E1|AverageCost	4
8 COGS conservation|E1|Fifo	4
8 COGS conservation|E1|Lifo	4
8 COGS conservation|G10|AverageCost	21
8 COGS conservation|G10|Fifo	21
8 COGS conservation|G10|Lifo	21
8 COGS conservation|G11|AverageCost	12
8 COGS conservation|G11|Fifo	12
8 COGS conservation|G11|Lifo	12
8 COGS conservation|G12|AverageCost	8
8 COGS conservation|G12|Fifo	8
8 COGS conservation|G12|Lifo	8
8 COGS conservation|G13|AverageCost	8
8 COGS conservation|G13|Fifo	8
8 COGS conservation|G13|Lifo	8
8 COGS conservation|G14|AverageCost	4
8 COGS conservation|G14|Fifo	4
8 COGS conservation|G14|Lifo	4
8 COGS conservation|G15|AverageCost	6
8 COGS conservation|G15|Fifo	6
8 COGS conservation|G15|Lifo	6
8 COGS conservation|G1|AverageCost	18
8 COGS conservation|G1|Fifo	18
8 COGS conservation|G1|Lifo	18
8 COGS conservation|G2|AverageCost	26
8 COGS conservation|G2|Fifo	26
8 COGS conservation|G2|Lifo	26
8 COGS conservation|G3|AverageCost	8
8 COGS conservation|G3|Fifo	8
8 COGS conservation|G3|Lifo	8
8 COGS conservation|G4|AverageCost	6
8 COGS conservation|G4|Fifo	6
8 COGS conservation|G4|Lifo	6
8 COGS conservation|G5|AverageCost	4
8 COGS conservation|G5|Fifo	4
8 COGS conservation|G5|Lifo	4
8 COGS conservation|G6|AverageCost	10
8 COGS conservation|G6|Fifo	10
8 COGS conservation|G6|Lifo	10
8 COGS conservation|G7|AverageCost	8
8 COGS conservation|G7|Fifo	8
8 COGS conservation|G7|Lifo	8
8 COGS conservation|G8|AverageCost	14
8 COGS conservation|G8|Fifo	14
8 COGS conservation|G8|Lifo	14
8 COGS conservation|G9|AverageCost	10
8 COGS conservation|G9|Fifo	10
8 COGS conservation|G9|Lifo	10
8 COGS conservation|N1|AverageCost	8
8 COGS conservation|N1|Fifo	8
8 COGS conservation|N1|Lifo	8
8 COGS conservation|N2|AverageCost	13
8 COGS conservation|N2|Fifo	13
8 COGS conservation|N2|Lifo	13
8 COGS conservation|N3|AverageCost	8
8 COGS conservation|N3|Fifo	8
8 COGS conservation|N3|Lifo	8
8 COGS conservation|N4|AverageCost	2
8 COGS conservation|N4|Fifo	2
8 COGS conservation|N4|Lifo	2
8 COGS conservation|N5|AverageCost	4
8 COGS conservation|N5|Fifo	4
8 COGS conservation|N5|Lifo	4
8 COGS conservation|N6|AverageCost	12
8 COGS conservation|N6|Fifo	12
8 COGS conservation|N6|Lifo	12
8 COGS conservation|N7|AverageCost	6
8 COGS conservation|N7|Fifo	6
8 COGS conservation|N7|Lifo	6
8 COGS conservation|N8|AverageCost	4
8 COGS conservation|N8|Fifo	4
8 COGS conservation|N8|Lifo	4
8 COGS conservation|N9|AverageCost	6
8 COGS conservation|N9|Fifo	6
8 COGS conservation|N9|Lifo	6
BUILD.ok	312
CHECK1.rows	9989
CHECK10.subjects	3438
CHECK11.rows	26809
CHECK2.rows	3263
CHECK2.subjects	191
CHECK3.subjects	382
CHECK3b.subjects	573
CHECK4.subjects	2946
CHECK4b.subjects	71
CHECK4c.clauseVerified	132
CHECK4c.goldenDigest	944370617
CHECK4c.goldens	88
CHECK4c.inventedSubjects	9
CHECK4c.issueGoldens	44
CHECK4c.issueStructurePairs	738
CHECK5.closingQty	1146
CHECK5.onHand	1146
CHECK9.totalOracle	1038
CHECK9.totalSum	1038
SELF-CONSISTENCY.subjects	306
VALUE-INVARIANT.hullBlendLayers	2
VALUE-INVARIANT.orderingLayers	80
VALUE-INVARIANT.orderingSubjects	152
VALUE-INVARIANT.originBoundLayers	374
VALUE-INVARIANT.perLotChecks	388
VALUE-INVARIANT.subjects	382
";
}
