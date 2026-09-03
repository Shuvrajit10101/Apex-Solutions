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
    //   * ROUND 8 — 62 cells RE-RECORDED, all UPWARD. NO cell was added, NO cell was deleted and NO cell
    //     shrank; the count stays at 327. THE REASON IS FIVE NEW CORPUS SCENARIOS, and nothing else.
    //     (16) WHY THEY WERE ADDED. Three independent review lenses reproduced the SAME engine defect —
    //          the best-available-cost chain resolved over the WHOLE as-of window, so units created by a
    //          physical count or by an unrated inward were priced by a purchase dated AFTER them — and this
    //          harness returned ENGINE VERDICT: ACCEPTED. The harness logic was not at fault; its CORPUS
    //          could not reach the defect. Every scenario that consulted the chain either ENDED at the
    //          movement that consulted it (G6-001, G6-002, G7-001, G7-002) or set a standard cost that
    //          short-circuited the chain before its last link (G6-001), and every never-negative count was
    //          taken from a POSITIVE stack (N4-001, N4-002), so the "this arm is inert on a never-negative
    //          book" premise was untestable. A green run on a corpus that cannot reach the defect is not
    //          evidence, and re-recording it as coverage would have been the worst kind of blessing.
    //          N4-003  a NEVER-NEGATIVE book whose layer stack drains to EXACTLY zero and is then counted
    //                  UP. No debt is ever created and no key ever goes negative, so it is inside CHECK 1
    //                  byte identity and inside CHECK 4 calibration — and the running average is 0 there,
    //                  which is what made the premise false.
    //          G6-003  a rated inward dated AFTER a count taken with a debt outstanding, StandardCost
    //                  UNSET so the chain must reach its last link. Two as-of dates STRADDLE that purchase,
    //                  so a look-ahead shows up as the earlier Balance Sheet moving.
    //          G6-004  a rated inward AFTER a count with a debt outstanding, which is what makes the
    //                  `debt = 0m` count write-off FALSIFIABLE: before this, deleting it from either engine
    //                  arm left every test and every check green.
    //          G6-005  a count taken DOWN to exactly zero while a debt is outstanding — the other end of
    //                  the legal counted range, and a second independent pin on the same write-off clause.
    //          G7-003  a rated inward at a wildly different rate AFTER an UNRATED inward repaid a debt.
    //          (A count taken with a debt outstanding onto a NON-EMPTY stack was also requested and is
    //          STRUCTURALLY UNREACHABLE — a debt exists only while the stack is empty. It is recorded as
    //          unreachable in the G6-005 corpus comment rather than faked into existence.)
    //     (17) WHAT MOVED. Six family-triples grew because those families gained scenarios: 6/7/8 x G6
    //          (10->24, 14->34, 10->24 per method), 6/7/8 x G7 (8->14, 12->20, 8->14) and 6/7/8 x N4
    //          (16->20, 16->22, 2->6). The whole-harness counters grew in step: BUILD.ok 312->342 (five
    //          scenarios x six methods), CHECK1.rows 9989->10419, CHECK2.rows 3263->3557, CHECK2.subjects
    //          191->208, CHECK3.subjects 382->416, CHECK3b.subjects 573->624, CHECK4.subjects 2946->3072
    //          and CHECK4b.subjects 71->74 (N4-003 is never-negative, so it is CALIBRATED, not judged),
    //          CHECK5.closingQty/onHand 1146->1248, CHECK9.totalSum/totalOracle 1038->1140,
    //          CHECK10.subjects 3438->3744, CHECK11.rows 26809->29241, SELF-CONSISTENCY.subjects 306->330,
    //          VALUE-INVARIANT.subjects 382->416, .originBoundLayers 374->392, .orderingSubjects 152->176,
    //          .orderingLayers 80->100, .perLotChecks 388->410, CHECK4c.issueStructurePairs 738->812.
    //     (18) CHECK4c GREW BY 36 CONSTANTS, which is the whole point: CHECK4c.inventedSubjects 9->27,
    //          so eighteen NEW subjects rest on a rule nothing calibrates, and the coverage assertion
    //          requires BOTH a closing and an issue golden for each. CHECK4c.goldens 88->106,
    //          CHECK4c.issueGoldens 44->62, CHECK4c.clauseVerified 132->168, and CHECK4c.goldenDigest
    //          944370617 -> 1135924260 — which is those thirty-six ADDITIONS (GT-65..GT-76 with their L
    //          twins, GI-45..GI-62) and nothing else. NO EXISTING CONSTANT WAS TOUCHED, and the run
    //          confirms it: all 168 goldens reproduce with 0 mismatches, so the round-8 reference
    //          correction did not move a single number any earlier golden had pinned.
    //     (19) CONSEQUENCE, DELIBERATE AND THE REASON THIS WAS DONE. On the S-A slice under gate the run
    //          goes from ENGINE VERDICT: ACCEPTED to REJECTED: CHECK 1 15 byte-identity diffs on N4-003,
    //          CHECK 2 3, CHECK 3 6, CHECK 9(b) 9 and CHECK 10 27, every one of them on N4-003, G6-003 or
    //          G7-003. Nothing was weakened to achieve that; five scenarios were added and 36 hand-derived
    //          constants were added with them.

    //     (20) ROUND 9 — VALUATION MOVED PER (item, godown, batch). 327 -> 364 cells; 37 cells ADDED,
    //          NONE REMOVED, 26 RE-RECORDED. This is the first re-recording in which three cells go DOWN,
    //          and they go down BY DESIGN, under the user decision of 2026-07-28. Everything is listed.
    //     (a)  WHY. `MovementEvents` flattened every godown into ONE item-level stream while the posting
    //          guard and `InventoryLedger` work per (item, godown, batch). Three different wrong answers
    //          came out of that mismatch and the harness certified all three, because the corpus had NO
    //          scenario combining `Godowns: 2` with a `Count(`. The reference now replays cost layers per
    //          key — the same key the quantity register uses — so quantity and value agree by
    //          construction (SumQty(layers) - debt == on-hand, per key, always).
    //     (b)  THE TWO CELLS THAT SHRANK, AND WHY THAT IS NOT A LOSS OF COVERAGE.
    //          CHECK1.rows 10419 -> 9428 and CHECK4.subjects 3072 -> 2736 (CHECK4b.subjects 74 -> 66).
    //          Byte identity and calibration are now scoped to never-negative AND SINGLE-KEY books
    //          (FactSingleKey, a new spec-derived fact). On a single-key book the per-key replay IS the
    //          item-level replay, so HEAD is still trusted there to the paisa. On a MULTI-key book HEAD is
    //          the arm that is wrong — on N10-001 every per-key on-hand is non-negative on every date and
    //          HEAD still reports Rs 158.20 against an honest Rs 1,159.50 — so calibrating against it would
    //          demand the defect. NOTHING WENT UNMEASURED: the rows CHECK 1 gave up are counted in the NEW
    //          CELL CHECK1b.rows = 2471, where every moved figure is printed, the QUANTITY-shaped rows are
    //          still required to be byte-identical, and every moved VALUE is pinned by a hand-derived
    //          golden. 9428 + 2471 = 11899 rows in scope, against 10419 before.
    //     (c)  WHAT GREW, AND FROM WHAT. Five new scenarios: N10-001 (THE WIPE — six guard-legal vouchers,
    //          nothing negative anywhere, engine reports Rs 0.00 against an honest Rs 1,159.50), N10-002
    //          (two godowns, no count, no debt, and per-key still differs from item-level on BOTH FIFO and
    //          LIFO), N11-001 (godowns AND batches, three keys, one draining to exactly zero), G16-001
    //          (THE DESYNC, reproduced verbatim from the re-review) and G16-002 (the count on the godown
    //          that DID go short). BUILD.ok 342->372 (five scenarios x six methods), CHECK3.subjects
    //          416->446, CHECK3b.subjects 624->669, CHECK5.closingQty/onHand 1248->1338, CHECK9
    //          .totalSum/totalOracle 1140->1230, CHECK10.subjects 3744->4014, CHECK11.rows 29241->31899,
    //          CHECK2.rows 3557->3817, CHECK2.subjects 208->223, SELF-CONSISTENCY.subjects 330->356,
    //          VALUE-INVARIANT.subjects 416->446, .originBoundLayers 392->443, .orderingSubjects 176->206,
    //          .orderingLayers 100->144, .perLotChecks 410->461, CHECK4c.issueStructurePairs 812->846.
    //          Eighteen new family-triples for 6/7/8 x {N10, N11, G16}.
    //     (d)  CHECK4c GREW BY 46 CONSTANTS: goldens 106->142, issueGoldens 62->72, clauseVerified
    //          168->214, inventedSubjects 27->30 (G16-002's three count-with-debt subjects), goldenDigest
    //          1135924260 -> 1814761323. SEVEN EXISTING CONSTANTS WERE RE-DERIVED, and they are the whole
    //          list of numbers this change moves on books that already existed: GT-25 and GT-43 (G12-002,
    //          19775 -> 31640), GI-26 (G12-002 issue@300, 19775 -> 31640), GT-27 (G9-002, 24729 -> 26846)
    //          and GI-29 (G9-002 issue@300, 26302 -> 26846). GI-27 and GI-28 did NOT move (1291 and 989)
    //          and were re-tagged only, because their clause labels had become false. NOT ONE SINGLE-KEY
    //          GOLDEN MOVED — 135 of them reproduce unchanged, which is the assertion that says this was a
    //          re-key and nothing more.
    //     (e)  THE CENSUS GATE DID ITS JOB AGAIN: it refused to judge the run until this entry was written.

    //     (21) ROUND 10 — THE PER-KEY REPLAY WAS REVERTED; WHAT SHIPS IS THE DEBT GATE. 364 -> 363 cells;
    //          ONE cell REMOVED, none added, 12 RE-RECORDED. Six of the twelve GROW and six SHRINK, and
    //          every one of them is listed with its cause.
    //     (a)  WHY THE REVERT (user decision, 2026-07-29). Round 9's per-key replay re-derived each
    //          (item, godown, batch) pool INDEPENDENTLY, so cost stopped flowing across a stock transfer.
    //          Two CRITICALs came out of it, both on books the posting guard accepts with nothing negative
    //          anywhere: an ordinary godown-to-godown Stock Journal with a blank destination rate priced
    //          the transferred units off an EMPTY pool (Rs 5,000,002.37 of Stock-in-Hand on Rs 1,000,003.73
    //          ever spent, where the ITEM-LEVEL replay reports Rs 1,000,003.73 — round 9 BROKE a case that
    //          already worked), and a physical count in a godown or batch that has never held stock booked
    //          every counted unit at Rs 0.00. Neither is reachable by any corpus scenario: the corpus
    //          builder cannot emit a Stock Journal at all.
    //     (b)  WHAT SHIPS INSTEAD. The debt rule, GATED: a shortfall in the flattened item-level layer walk
    //          becomes a DEBT only where a quantity-only per-key walk shows some (godown, batch) key
    //          genuinely negative at that point. Where none is, the shortfall is an artefact of flattening
    //          and is discarded exactly as HEAD discards it. CONSEQUENCE, AND IT IS THE POINT: a
    //          never-negative book can no longer create a debt at all, so every debt clause is inert there
    //          and byte identity to HEAD holds on EVERY never-negative book, multi-key included.
    //     (c)  CHECK1.rows 9428 -> 11792 and CHECK1b.rows (2471) REMOVED. Round 9 had to split byte
    //          identity into CHECK 1 (single-key only) and a CHECK 1b re-baseline block, because a per-key
    //          replay MOVES never-negative multi-key books. The gate removes that need: CHECK 1 is back to
    //          its full spec-derived scope — every book the spec says never goes negative — and it runs
    //          with 0 diffs on 11,792 rows across 24 scenarios, N8/N9/N10/N11 included. 9428 + 2471 = 11899
    //          against 11792 because three CHECK-1b subjects (G9-001, G9-002, G12-001) carry a genuinely
    //          negative KEY and are outside byte identity by the spec's own definition; they are judged by
    //          the point oracles and by hand-derived goldens instead.
    //     (d)  CHECK4.subjects 2736 -> 3450 and CHECK4b.subjects 66 -> 83: calibration is a NEVER-NEGATIVE
    //          scope too, so the same widening applies. SELF-CONSISTENCY.subjects 356 -> 446: the
    //          reference's layer stack is now measured on EVERY Fifo/Lifo subject against FactFlatNetMicro
    //          (Facts' own gated quantity walk) instead of being skipped where the reported closing
    //          quantity is not positive — a strictly larger population, and the item-level/per-key DESYNC
    //          it exposes is printed by name rather than swallowed.
    //     (e)  CHECK11.rows 31899 -> 31614, VALUE-INVARIANT.orderingLayers 144 -> 108,
    //          .orderingSubjects 206 -> 188, .originBoundLayers 443 -> 429, .perLotChecks 461 -> 447 —
    //          five SHRINKS, one cause: the item-level replay holds FEWER surviving layers on the
    //          multi-key books than the per-key replay did (one merged stack, not one per key), and
    //          RefDebtQtyMicro — a row round 9 emitted only because a per-key book can hold layers in one
    //          key while owing units in another — is gone with it. Nothing stopped being measured; there
    //          is less to measure on those subjects.
    //     (f)  CHECK4c: goldens 142 is UNCHANGED — not one CLOSING golden was added or deleted.
    //          issueGoldens 72 -> 73 and clauseVerified 214 -> 215: ONE issue golden was ADDED, GI-73,
    //          because G16-001's AverageCost subject joins the INVENTED population and the coverage rule
    //          requires a rule nothing calibrates to be pinned on the P&L as well as the Balance Sheet.
    //          inventedSubjects 30 -> 33 and issueStructurePairs 846 -> 866 because
    //          G16-001's three subjects join the INVENTED population (under the item-level replay its
    //          count IS taken with a debt outstanding). goldenDigest 1814761323 -> 1604562855: 27 constants
    //          were RE-DERIVED, all on MULTI-KEY books, all by hand and confirmed by an independent
    //          re-implementation. Five of them are RESTORATIONS of the pre-round-9 numbers (GT-25, GT-43,
    //          GI-26 back to 19775; GT-27 back to 24729; GI-29 back to 26302). NOT ONE SINGLE-KEY GOLDEN
    //          MOVED, which is the assertion that says the gate is inert on single-key books.
    //     (g)  WHAT IS NOT FIXED, RECORDED RATHER THAN DISCOVERED LATER: where a key IS genuinely short,
    //          the debt is still computed from the flattened stream, so the item-level/per-key desync
    //          survives (G16-001 holds 50 units of layers against a reported closing quantity of 10).
    //          CHECK 6 divides value by quantity, so on those subjects its premise is false and it is
    //          recorded as STRUCTURALLY-UNSATISFIABLE — listed by name with the measured delta, scored
    //          neither way. That is a narrow, spec-derived carve-out: 0 subjects on every single-key book
    //          and on every multi-key book with no physical count.

    //     (22) ROUND 11 — THE DEBT RULE IS CONFINED TO SINGLE-KEY ITEMS (user decision, 2026-07-29).
    //          363 cells, NONE added, NONE removed, 5 RE-RECORDED. Two grow, three change or shrink, and
    //          every one of them is listed with its cause.
    //     (a)  WHY. Round 10 gated the debt on a per-EVENT "is any (godown, batch) key negative after
    //          events 0..i" walk. That is NOT the predicate the product enforces: the posting guard
    //          samples on-hand per key PER DATE. A key that dips below zero in the morning and is square
    //          by close of business is negative to nothing a user can see, and it held the gate open for
    //          the whole span — so ONE same-day out-then-in in an unrelated third godown re-opened the
    //          full unbounded wipe (Rs 237.30 down to Rs 79.10; Rs 30,000,000.90 down to Rs 10,000,000.30
    //          at a large lot rate) on a book the real guard accepts. Item-level valuation with per-key
    //          quantity is inconsistent, and every quantity test built on top of that inconsistency has
    //          moved the defect rather than removed it. So the rule is now scoped by IDENTITY: a shortfall
    //          becomes a debt only on an item whose whole as-of-scoped movement history lives on exactly
    //          ONE key. There item-level IS per-key, arithmetically, and the rule is proven correct.
    //     (b)  NEW SPEC-DERIVED FACT, EMITTED PER (scenario, item, as-of): FactSingleKey. That is where
    //          both row growths come from and they are bookkeeping, not coverage:
    //          CHECK11.rows 31614 -> 31837 (+223, one row per emitted subject) and CHECK1.rows
    //          11792 -> 11875 (+83, the subset of those inside the never-negative scope).
    //     (c)  NEW CHECK, NO NEW CELL: CHECK 1M — byte identity on every MULTI-KEY subject, negative or
    //          not, measured HEAD ARM AGAINST LIVE ARM with the reference supplying only the scope. This
    //          is the INERTNESS claim and it is deliberately NOT measured against the reference's own
    //          predicate, which is the circularity the round-10 review convicted. It runs 4,290 rows over
    //          30 multi-key subjects in 11 scenarios (N8, N9, N10, N11, G9, G12, G16) with 0 diffs, and a
    //          FAMILY FLOOR fails the harness if the predicate ever stops seeing one of those families.
    //     (d)  CHECK4c.inventedSubjects 33 -> 27. G16-001's and G16-002's three subjects each LEAVE the
    //          INVENTED population, because no debt can be created on a multi-key item, so no count is
    //          taken with a debt outstanding there. Nothing stopped being pinned: their goldens are KEPT,
    //          re-derived and re-tagged as controls, so the residual they measure is still a constant in
    //          the table. CHECK4c.issueStructurePairs 866 -> 864 for the same reason (two probe pairs on
    //          G16-002 no longer sit at or above a positive surviving stack).
    //     (e)  CHECK4c.goldenDigest 1604562855 -> 1944624764: SEVENTEEN constants were RE-DERIVED and one
    //          more RE-TAGGED, ALL of them on MULTI-KEY books, every one by hand:
    //            GT-25/GT-43   G12-002 Fifo/Lifo   19775 -> 31640   (two BATCHES: the shortfall of 15 is
    //                          discarded and the whole 40-unit lot is stock, 40 x 7.91)
    //            GI-26         G12-002 Fifo @300   19775 -> 31640   (the same stack, issued whole)
    //            GT-97/97L     G16-001 Fifo/Lifo  324390 -> 24000   (no debt, so the count tops up at a
    //                          running average of 0.00 and only the d18 lot has a rate: 20 x 12.00)
    //            GT-98         G16-001 AverageCost 64878 -> 4800    (240.00 / 50 = 4.80, x reported 10)
    //            GI-66/67      G16-001 Fifo/Lifo  324390 -> 24000 ; GI-73 AverageCost 8110 -> 600
    //            GT-99/99L     G16-002 Fifo/Lifo   80104 -> 0       (the count reconciles an EMPTY stack)
    //            GT-100        G16-002 AverageCost 180234 -> 0 ; GI-68/69 80104 -> 0 ; GI-72 12516 -> 0
    //            GT-87D        G12-002 AverageCost 19775 UNCHANGED, re-tagged AverageDebt ->
    //                          NoDebtControl: the figure is right by a different route, and the old label
    //                          was FALSE of its subject.
    //          NOT ONE SINGLE-KEY GOLDEN MOVED — 125 closing and 62 issue constants reproduce unchanged.
    //          That is the assertion which says the change is a SCOPE change and nothing else.
    //     (f)  ONE REFERENCE CORRECTION, and it is a harness fix, not an engine one.
    //          Reference.ZeroCountUpReachable walked the item-level net UNGATED, so on a multi-key book it
    //          let the net go negative, concluded a 0-rate count-up was unreachable, and then convicted
    //          the reference's own (correct, HEAD-matching) 0-rate layer as INADMISSIBLE. It is now walked
    //          with the same gate Facts.FlatNet uses. On a single-key book it is character-for-character
    //          the walk that stood there before.
    //     (g)  WHAT IS NOT FIXED, RECORDED HERE RATHER THAN DISCOVERED LATER. A MULTI-KEY item that
    //          recovers from a GENUINE negative keeps HEAD's answer, and HEAD is wrong on those books in
    //          both directions: G16-001 closes at Rs 240.00 on a reported 10 units (Rs 24.00/unit for
    //          units that cost Rs 12.00) and G16-002 closes at Rs 0.00 on 18 units that cost Rs 1,001.30 —
    //          a wiped asset, HEAD's own, pre-dating this feature, against an honest Rs 1,802.34. The
    //          two-BATCH form of the crux, G12-002, closes at Rs 316.40 where the single-key form G1-001
    //          closes at the honest Rs 197.75. All of them are pinned by goldens and by xunit tests.
    //          Repairing them needs a per-key cost replay in which cost FLOWS ACROSS A TRANSFER; that is a
    //          larger design and it fixes a defect that was never part of this feature.

    //   * 2026-07-30 — RE-RECORDED after the negative-stock work was STOPPED and the engine reverted to
    //     HEAD (user decision, 2026-07-29). 12 cells changed; the corpus did not shrink and no scenario,
    //     family or check was removed. Two deliberate changes caused all 12:
    //       (a) THE DEBT GATE WAS REMOVED FROM THE REFERENCE. An abandoned round confined the debt rule to
    //           single-key items; that scoping put a valuation cliff at its own boundary (one internal
    //           godown transfer moved an item's whole history), so the reference now states the debt
    //           semantics UNGATED. A shortfall in the flattened item-level walk is therefore owed again on
    //           multi-key books, which moves three populations that are SCOPED BY "no debt fires here":
    //             CHECK1.rows        11875 -> 11439   FactNeverNegative now excludes the desync books,
    //                                                 where the flattened replay does create a debt. They
    //                                                 have NOT lost byte-identity cover: CHECK 1M holds
    //                                                 every multi-key subject to HEAD engine-vs-engine.
    //             CHECK4.subjects     3450 -> 3324   same scope, same cause.
    //             CHECK4b.subjects      83 -> 80     same scope, same cause.
    //           CHECK4c.inventedSubjects 27 -> 33 GREW for the same reason: those multi-key debt subjects
    //           now rest on a debt clause, so they are tagged INVENTED rather than echoing HEAD.
    //             VALUE-INVARIANT.originBoundLayers 429 -> 427, orderingLayers 108 -> 106,
    //             perLotChecks 447 -> 445, CHECK4c.issueStructurePairs 864 -> 868 — the reference's layer
    //             stacks on those same books changed shape; no invariant was weakened.
    //       (b) THE GOLDENS THAT PINNED THE ABANDONED DESIGN WERE REMOVED, and three RESTORED.
    //             CHECK4c.goldens        142 -> 133   (-9 closing)
    //             CHECK4c.issueGoldens    73 -> 65    (-8 issue)
    //             CHECK4c.clauseVerified 215 -> 198
    //             CHECK4c.goldenDigest changed accordingly.
    //           The 17 removed goldens all pinned MULTI-KEY debt subjects (N10-001, G16-001, G16-002) at
    //           the values the single-key gate produced. With no gate they assert a design that no longer
    //           exists, and the reference is not a validated oracle on those books, so no honest constant
    //           can replace them — see the note in Goldens.cs. GT-25/GT-43/GI-26 were RESTORED to their
    //           pre-gate hand derivations (Rs 197.75), which the ungated reference reproduces exactly.
    //     COVERAGE OF THE JUDGED SCOPE DID NOT SHRINK: every single-key INVENTED subject is still pinned,
    //     and the reference-backed checks now convict only there. Nothing else moved.

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
6 closing-rate band|G16|AverageCost	8
6 closing-rate band|G16|Fifo	8
6 closing-rate band|G16|LastPurchaseCost	8
6 closing-rate band|G16|LastSaleCost	8
6 closing-rate band|G16|Lifo	8
6 closing-rate band|G16|StandardCost	8
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
6 closing-rate band|G6|AverageCost	24
6 closing-rate band|G6|Fifo	24
6 closing-rate band|G6|LastPurchaseCost	24
6 closing-rate band|G6|LastSaleCost	24
6 closing-rate band|G6|Lifo	24
6 closing-rate band|G6|StandardCost	24
6 closing-rate band|G7|AverageCost	14
6 closing-rate band|G7|Fifo	14
6 closing-rate band|G7|LastPurchaseCost	14
6 closing-rate band|G7|LastSaleCost	14
6 closing-rate band|G7|Lifo	14
6 closing-rate band|G7|StandardCost	14
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
6 closing-rate band|N10|AverageCost	12
6 closing-rate band|N10|Fifo	12
6 closing-rate band|N10|LastPurchaseCost	12
6 closing-rate band|N10|LastSaleCost	12
6 closing-rate band|N10|Lifo	12
6 closing-rate band|N10|StandardCost	12
6 closing-rate band|N11|AverageCost	6
6 closing-rate band|N11|Fifo	6
6 closing-rate band|N11|LastPurchaseCost	6
6 closing-rate band|N11|LastSaleCost	6
6 closing-rate band|N11|Lifo	6
6 closing-rate band|N11|StandardCost	6
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
6 closing-rate band|N4|AverageCost	20
6 closing-rate band|N4|Fifo	20
6 closing-rate band|N4|LastPurchaseCost	20
6 closing-rate band|N4|LastSaleCost	20
6 closing-rate band|N4|Lifo	20
6 closing-rate band|N4|StandardCost	20
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
7 total-spend containment|G16|AverageCost	12
7 total-spend containment|G16|Fifo	12
7 total-spend containment|G16|Lifo	12
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
7 total-spend containment|G6|AverageCost	34
7 total-spend containment|G6|Fifo	34
7 total-spend containment|G6|Lifo	34
7 total-spend containment|G7|AverageCost	20
7 total-spend containment|G7|Fifo	20
7 total-spend containment|G7|Lifo	20
7 total-spend containment|G8|AverageCost	20
7 total-spend containment|G8|Fifo	20
7 total-spend containment|G8|Lifo	20
7 total-spend containment|G9|AverageCost	16
7 total-spend containment|G9|Fifo	16
7 total-spend containment|G9|Lifo	16
7 total-spend containment|N10|AverageCost	12
7 total-spend containment|N10|Fifo	12
7 total-spend containment|N10|Lifo	12
7 total-spend containment|N11|AverageCost	6
7 total-spend containment|N11|Fifo	6
7 total-spend containment|N11|Lifo	6
7 total-spend containment|N1|AverageCost	12
7 total-spend containment|N1|Fifo	12
7 total-spend containment|N1|Lifo	12
7 total-spend containment|N2|AverageCost	26
7 total-spend containment|N2|Fifo	26
7 total-spend containment|N2|Lifo	26
7 total-spend containment|N3|AverageCost	16
7 total-spend containment|N3|Fifo	16
7 total-spend containment|N3|Lifo	16
7 total-spend containment|N4|AverageCost	22
7 total-spend containment|N4|Fifo	22
7 total-spend containment|N4|Lifo	22
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
8 COGS conservation|G16|AverageCost	8
8 COGS conservation|G16|Fifo	8
8 COGS conservation|G16|Lifo	8
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
8 COGS conservation|G6|AverageCost	24
8 COGS conservation|G6|Fifo	24
8 COGS conservation|G6|Lifo	24
8 COGS conservation|G7|AverageCost	14
8 COGS conservation|G7|Fifo	14
8 COGS conservation|G7|Lifo	14
8 COGS conservation|G8|AverageCost	14
8 COGS conservation|G8|Fifo	14
8 COGS conservation|G8|Lifo	14
8 COGS conservation|G9|AverageCost	10
8 COGS conservation|G9|Fifo	10
8 COGS conservation|G9|Lifo	10
8 COGS conservation|N10|AverageCost	8
8 COGS conservation|N10|Fifo	8
8 COGS conservation|N10|Lifo	8
8 COGS conservation|N11|AverageCost	4
8 COGS conservation|N11|Fifo	4
8 COGS conservation|N11|Lifo	4
8 COGS conservation|N1|AverageCost	8
8 COGS conservation|N1|Fifo	8
8 COGS conservation|N1|Lifo	8
8 COGS conservation|N2|AverageCost	13
8 COGS conservation|N2|Fifo	13
8 COGS conservation|N2|Lifo	13
8 COGS conservation|N3|AverageCost	8
8 COGS conservation|N3|Fifo	8
8 COGS conservation|N3|Lifo	8
8 COGS conservation|N4|AverageCost	6
8 COGS conservation|N4|Fifo	6
8 COGS conservation|N4|Lifo	6
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
BUILD.ok	372
CHECK1.rows	11439
CHECK10.subjects	4014
CHECK11.rows	31837
CHECK2.rows	3817
CHECK2.subjects	223
CHECK3.subjects	446
CHECK3b.subjects	669
CHECK4.subjects	3324
CHECK4b.subjects	80
CHECK4c.clauseVerified	198
CHECK4c.goldenDigest	1079267208
CHECK4c.goldens	133
CHECK4c.inventedSubjects	33
CHECK4c.issueGoldens	65
CHECK4c.issueStructurePairs	868
CHECK5.closingQty	1338
CHECK5.onHand	1338
CHECK9.totalOracle	1230
CHECK9.totalSum	1230
SELF-CONSISTENCY.subjects	446
VALUE-INVARIANT.hullBlendLayers	2
VALUE-INVARIANT.orderingLayers	106
VALUE-INVARIANT.orderingSubjects	188
VALUE-INVARIANT.originBoundLayers	427
VALUE-INVARIANT.perLotChecks	445
VALUE-INVARIANT.subjects	446
";
}
