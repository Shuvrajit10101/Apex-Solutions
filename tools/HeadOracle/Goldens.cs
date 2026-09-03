namespace HeadOracle;

// ===================================================================================================
// CHECK 4c — THE HAND-DERIVED DEBT-BRANCH GOLDENS.
//
// WHY THIS FILE EXISTS — THE RECURSION AUDIT #4 NAMED, AND HOW IT TERMINATES.
//
//   CHECK 2 issues ENGINE VERDICTS from the debt-aware AverageCost reference. CHECK 4b calibrates that
//   reference — but ONLY on FactNeverNegative=1 books, and a never-negative book NEVER CARRIES A DEBT,
//   so on exactly those books every clause that distinguishes RunAverageDebtAware from RunAverage is
//   DEAD CODE. The clauses that decide all six of CHECK 2's convictions are therefore validated by
//   NOTHING. That hole cannot be closed with more calibration against HEAD: HEAD HAS NO CORRECT DEBT
//   BEHAVIOUR TO CALIBRATE AGAINST. That is the recursion.
//
//   It terminates here, and only here. The tables below are sets of LITERAL EXPECTED PAISA VALUES for
//   subjects where the debt clauses actually FIRE. Each one was:
//     (a) DERIVED BY HAND, movement by movement, and written up step by step in the round-5/round-6
//         reports so a human reviewer can check the arithmetic WITHOUT trusting any code in this
//         repository. Nothing went into these tables that could not be derived by hand.
//     (b) CROSS-CHECKED by a THIRD implementation — an out-of-band Python replay written from the corpus
//         movement lists alone, sharing no line of code with Reference.cs or Program.cs. All agree.
//     (c) Compared against the C# reference. All agree there too. Any disagreement was to be resolved
//         by HAND ARITHMETIC, never by picking a side; none arose.
//
//   WHAT THIS DOES AND DOES NOT BUY, STATED HONESTLY. It does NOT make the reference provably right.
//   It makes the reference wrong ONLY IF a human derivation and two independent implementations are all
//   wrong THE SAME WAY. That is the terminal state of this line of argument, and it is the honest one.
//
// ---------------------------------------------------------------------------------------------------
// ROUND 6 — WHY THERE ARE NOW TWO TABLES.
//
//   Audit #5 finding [0] (HIGH). The round-5 table pinned CLOSING VALUES ONLY. CHECK 10 — 143 engine
//   convictions on the clean run — is judged against `RefIssueValue@<probe>Paisa`, whose Fifo/Lifo arm is
//   a SEPARATE consume loop (Reference.IssueValue) that CHECK 4 calibrates only on never-negative books.
//   So the exact argument this file was built to answer for closing values still held, unanswered, for
//   issue values. The adversary demonstrated it: a poison that issued at the debt-aware pool average
//   whenever the book had ever carried a debt rewrote 68 of the 120 reported CHECK 10 demands — Rs 197.75
//   to Rs 7,910.00 on the crux, a 40x fabrication that also silently dropped the stock cap — while
//   CHECK 4, CHECK 4b and CHECK 4c all printed PASS and PART A printed HARNESS INTEGRITY : SOUND.
//   A builder whose Balance Sheet was right and whose P&L was wrong would have been certified.
//
//   <see cref="Issue"/> closes it the same way <see cref="All"/> closed the closing-value branch:
//   hand-derived literal constants on the RefIssueValue columns, asserted as a HARNESS failure, covering
//   every debt clause that can reach an issue —
//     * an issue taken WHILE A DEBT IS OUTSTANDING (no layer may survive under a debt, so the answer is
//       exactly 0 on Fifo/Lifo — and the average arm falls back to the standard cost);
//     * an issue taken AFTER RECOVERY (post-recovery COGS: a correct Balance Sheet with a wrong P&L is a
//       real and specific risk, which is why corpus additions G1-003/G2-002/G8-003 exist);
//     * an issue DRAWN ACROSS A REPAID LAYER — a probe at or above on-hand must stop at the surviving
//       stack, because the repaid units went to COGS when the debt was settled and are not there to sell;
//     * the AverageCost issue path, which is NOT capped by the stack and so cannot be pinned by the
//       structural assertion below.
//
//   PLUS a structural assertion in the comparator that needs no constants at all: on every Fifo/Lifo
//   subject, a probe >= the closing quantity must yield EXACTLY the closing value, and any probe must
//   yield at most the closing value. That single rule kills all 68 of the adversary's fabricated rows.
//
//   ROUND 6 ALSO WIDENED THE CLOSING TABLE from 32 to 84 constants (audit #5 finding [4], LOW): every
//   subject the harness actually CONVICTS on is now directly pinned — all 70 CHECK 3 convictions and all
//   6 CHECK 2 convictions — because those are the numbers that get quoted as evidence.
//
// COVERAGE IS ASSERTED, NOT ASSUMED. The comparator requires that
//   * EVERY subject the reference tags RefProvenance=INVENTED carries a golden. An INVENTED subject is
//     one resting on a rule NOTHING calibrates (a physical count taken with a debt outstanding, or an
//     unrated inward repaying a debt); an unpinned one is precisely the exposure this file closes.
//     Round 6 additionally derives that population FROM THE SPEC (Facts.InventedByRule, a pure quantity
//     walk) and asserts the emitted tags equal it, so a PARTIAL retag cannot quietly shrink it.
//   * every G-family that has any BRIEF or INVENTED subject carries at least one golden.
//   * every clause tag below — closing AND issue — is present at least once.
//   A shortfall on any of them is a HARNESS failure (exit 3).
//
// TWO GATES PROTECT THE CONSTANTS THEMSELVES (audit #5 finding [1], MEDIUM). Editing a constant to match
// the code is the ONE thing this file forbids and was the one thing no gate detected: the census pinned
// the NUMBER of goldens, never their VALUES.
//   * <see cref="Digest"/> is folded into the census as CHECK4c.goldenDigest, so ANY edit to ANY constant
//     changes a recorded cell and must be justified in Census.cs's recording log.
//   * the comparator parses the LAST rupee figure out of each <c>Working</c> string and asserts it equals
//     the constant x 100. An edited constant with an unedited derivation is then a mechanical failure
//     rather than something a reader has to notice.
//
// FAILURE MODE. A golden the reference does not reproduce is a HARNESS failure (exit 3) — "the reference
// is wrong, fix the reference", exactly like CHECK 4 and CHECK 4b. It is never an engine verdict: these
// constants judge the ORACLE, not src/.
// ===================================================================================================

/// <summary>Which debt clause a golden exists to pin. Every tag must be exercised at least once.</summary>
public static class DebtClause
{
    public const string RepayRated = "repay:rated-inward";
    public const string RepayMultiLot = "repay:multiple-lots";
    public const string RepayUnrated = "repay:unrated-inward";
    public const string CountWithDebt = "count:debt-outstanding";
    public const string CountAfterRepay = "count:after-repayment";
    public const string DebtOutstanding = "debt:still-outstanding";
    public const string DebtFromEmpty = "debt:created-from-empty-stack";
    public const string DebtAccumulated = "debt:two-successive-overdraws";
    public const string AverageDebt = "average:debt-path";
    public const string NoDebtControl = "control:no-company-wide-debt";

    // ---- ROUND 6: the ISSUE-VALUE clauses (audit #5 finding [0]).
    /// <summary>An issue taken while a debt is still outstanding — nothing may be drawn.</summary>
    public const string IssueUnderDebt = "issue:debt-outstanding";
    /// <summary>An issue taken AFTER the debt was repaid — post-recovery COGS.</summary>
    public const string IssuePostRecovery = "issue:after-recovery";
    /// <summary>An issue whose probe reaches past the surviving stack into units the repayment consumed.</summary>
    public const string IssueAcrossRepaid = "issue:across-a-repaid-layer";
    /// <summary>The AverageCost issue arm, which is NOT capped by the stack.</summary>
    public const string IssueAverage = "issue:average-path";
    /// <summary>A no-debt control: FIFO must take the oldest layer and LIFO the newest.</summary>
    public const string IssueControl = "issue:no-debt-control";
}

/// <summary>
/// One hand-derived golden. <paramref name="Paisa"/> is the LITERAL expected value in paisa.
/// <paramref name="Probe"/> is empty for a CLOSING-value golden and holds the issue quantity (exactly as
/// it appears in the emitted column name <c>RefIssueValue@&lt;probe&gt;Paisa</c>) for an ISSUE golden.
/// <paramref name="Working"/> is the one-line derivation, printed in the report next to the assertion so
/// the reviewer sees the arithmetic and the constant side by side; the comparator also asserts that its
/// final rupee figure equals <paramref name="Paisa"/> / 100.
/// </summary>
public sealed record Golden(
    string Id,
    string Scenario,
    string Item,
    string Method,
    string AsOf,
    long Paisa,
    string Clause,
    string Working,
    string Probe = "")
{
    /// <summary>The emitted measure column this golden is asserted against, for one reference column.</summary>
    public bool IsIssue => Probe.Length > 0;

    /// <summary>The tab-joined (scenario, item, method, asOf) stem that identifies the subject.</summary>
    public string Stem => string.Join('\t', [Scenario, Item, Method, AsOf]);
}

public static class Goldens
{
    /// <summary>
    /// THE SETTLED RULE the nine INVENTED subjects (GT-11/11L/12, GT-14/14L/15, GT-16/16L/16A, and the
    /// issue goldens GI-16/GI-17/GI-18/GI-19/GI-20 that draw on them) depend on. It is quoted here, beside
    /// the constants, so a reader can EVALUATE the rule without having to obtain the ruling — audit #5
    /// finding [2]. The attribution is kept and is true; the rule is now stated precisely as well.
    /// </summary>
    public const string SettledRule =
        "A debt settled by a movement carrying NO PURCHASE RATE — an unrated inward, or a physical count " +
        "taken while a debt is outstanding — is valued through the engine's EXISTING best-available-cost " +
        "chain: running average -> strictly-positive StandardCost -> last rated inward -> 0. " +
        "(DECIDED BY THE USER ON 2026-07-27; recorded in the repository in parallel. Nothing is invented — " +
        "the reference applies the rule the engine already applies to an unrated inward. HEAD instead uses " +
        "the running average ALONE, which is 0 immediately after an over-draw, so HEAD values genuinely-held " +
        "units at Rs 0.00.)";

    /// <summary>
    /// THE CLOSING-VALUE TABLE. Hand-derived; Python-confirmed; do not edit a constant without redoing BOTH
    /// (and the census's goldenDigest cell will refuse to let you do it quietly).
    /// The <c>Working</c> column is the hand derivation in one line — layers/pool at the as-of date,
    /// then the multiplication, then the paisa snap (away from zero).
    /// </summary>
    public static readonly IReadOnlyList<Golden> All =
    [
        // ---------------------------------------------------- repayment by a RATED inward (single lot)
        new("GT-01", "G1-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "In 10@100.13 -> Out 25 drains 10, debt 15 -> In 40@7.91 repays 15, 25 survive: 25 x 7.91 = 197.75"),
        new("GT-02", "G1-001", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "one layer only, so LIFO drains identically: 25 x 7.91 = 197.75"),
        new("GT-03", "G1-001", "Widget", "AverageCost", "2024-04-20", 19775, DebtClause.AverageDebt,
            "pool 10@100.13 -> Out 25 empties it, debt 15 -> In 40@7.91 repays 15, pool 25 x 7.91 = 197.75; avg 7.91 x 25 = 197.75"),

        // ---------------------------------------------------- repayment ACROSS MULTIPLE LOTS
        new("GT-04", "G2-001", "Widget", "Fifo", "2024-04-25", 8449, DebtClause.RepayMultiLot,
            "In 10@100.13 -> Out 35, debt 25 -> In 12@7.91 repays 12 (debt 13, no layer) -> In 20@12.07 repays 13, 7 survive: 7 x 12.07 = 84.49"),
        new("GT-05", "G2-001", "Widget", "AverageCost", "2024-04-25", 8449, DebtClause.AverageDebt,
            "same debt walk in the pool: nothing joins until the 12.07 lot; pool 7 x 12.07 = 84.49; avg 12.07 x 7 = 84.49"),
        new("GT-06", "G2-003", "Widget", "AverageCost", "2024-04-25", 5537, DebtClause.AverageDebt,
            "debt 25 -> In 20@12.07 repays 20 (debt 5) -> In 12@7.91 repays 5, 7 join at 7.91: avg 7.91 x 7 = 55.37"),
        new("GT-07", "G2-004", "Widget", "AverageCost", "2024-04-25", 1110, DebtClause.AverageDebt,
            "In 5@1000.07 -> Out 25, debt 20 -> In 20@1000.07 repays ALL 20 (nothing joins) -> In 30@0.37 joins whole: avg 0.37 x 30 = 11.10"),
        new("GT-08", "G2-002", "Widget", "AverageCost", "2024-04-25", 1509, DebtClause.AverageDebt,
            "pool 7@12.07 after recovery -> Out 5.75 at 12.07 leaves 1.25; 12.07 x 1.25 = 15.0875 -> paisa away-from-zero 15.09"),

        // ---------------------------------------------------- TWO successive over-draws
        new("GT-09", "G4-001", "Widget", "Fifo", "2024-04-25", 24917, DebtClause.DebtAccumulated,
            "In 10@100.13 -> Out 25 (debt 15) -> Out 13.5 (debt 28.5) -> In 60@7.91 repays 28.5, 31.5 survive: 31.5 x 7.91 = 249.165 -> 249.17"),
        new("GT-10", "G4-001", "Widget", "AverageCost", "2024-04-25", 24917, DebtClause.AverageDebt,
            "pool empty at the first over-draw, debt accumulates to 28.5; 31.5 join at 7.91: avg 7.91 x 31.5 = 249.165 -> 249.17"),

        // ---------------------------------------------------- a COUNT taken with a DEBT OUTSTANDING
        // GT-11/GT-11L/GT-12 are valid under Goldens.SettledRule, quoted at the top of this class.
        new("GT-11", "G6-001", "Widget", "Fifo", "2024-04-20", 7816, DebtClause.CountWithDebt,
            "In 10@100.13 -> Out 25 (debt 15) -> Count 8: debt written off, stack empty, 8 topped up by the chain "
            + "(running avg 0 -> STANDARD COST 9.77): 8 x 9.77 = 78.16"),
        new("GT-11L", "G6-001", "Widget", "Lifo", "2024-04-20", 7816, DebtClause.CountWithDebt,
            "identical: the count reconciles to a total, not to a particular layer: 8 x 9.77 = 78.16"),
        new("GT-12", "G6-001", "Widget", "AverageCost", "2024-04-20", 7816, DebtClause.AverageDebt,
            "count writes the debt off; pool average is 0 so the chain answers 9.77; pool 8 x 9.77 = 78.16"),
        new("GT-13", "G6-002", "Widget", "Fifo", "2024-04-25", 9492, DebtClause.CountAfterRepay,
            "debt 15 repaid by In 30@7.91 leaving 15@7.91 -> Count 12 is a count DOWN, consumes 3: 12 x 7.91 = 94.92"),

        // ---------------------------------------------------- repayment by an UNRATED inward
        // GT-14..GT-16A are valid under Goldens.SettledRule, quoted at the top of this class.
        new("GT-14", "G7-001", "Widget", "Fifo", "2024-04-20", 24425, DebtClause.RepayUnrated,
            "In 10@100.13 -> Out 25 (debt 15) -> UNRATED In 40 priced by the chain (running avg 0 -> STANDARD COST 9.77), "
            + "repays 15, 25 survive: 25 x 9.77 = 244.25"),
        new("GT-14L", "G7-001", "Widget", "Lifo", "2024-04-20", 24425, DebtClause.RepayUnrated,
            "one layer only: 25 x 9.77 = 244.25"),
        new("GT-15", "G7-001", "Widget", "AverageCost", "2024-04-20", 24425, DebtClause.AverageDebt,
            "pool empty at the over-draw so the chain answers 9.77 for the unrated lot: avg 9.77 x 25 = 244.25"),
        new("GT-16", "G7-002", "Widget", "Fifo", "2024-04-20", 250325, DebtClause.RepayUnrated,
            "identical shape but NO standard cost, so the chain reaches past it to the LAST RATED INWARD 100.13: 25 x 100.13 = 2503.25"),
        new("GT-16L", "G7-002", "Widget", "Lifo", "2024-04-20", 250325, DebtClause.RepayUnrated,
            "one layer only: 25 x 100.13 = 2503.25"),
        new("GT-16A", "G7-002", "Widget", "AverageCost", "2024-04-20", 250325, DebtClause.AverageDebt,
            "same chain link in the pool: avg 100.13 x 25 = 2503.25"),

        // ---------------------------------------------------- a DEBT STILL OUTSTANDING at the as-of date
        new("GT-17", "G3-001", "Widget", "Fifo", "2024-04-20", 0, DebtClause.DebtOutstanding,
            "In 10@100.13 -> Out 25 (debt 15) -> In 5@7.91 repays only 5, debt 10 REMAINS and no layer is created; "
            + "on-hand -10 so closing qty is 0 and the value must be exactly 0"),
        new("GT-18", "G3-002", "Widget", "Fifo", "2024-04-20", 0, DebtClause.DebtOutstanding,
            "opening 3.25@41.03 + In 5@100.13 = 8.25 all drained by Out 20.5 (debt 12.25) -> In 4@7.91 repays 4, debt 8.25 remains: 0"),

        // ---------------------------------------------------- a debt created from an EMPTY stack
        new("GT-19", "G5-002", "Widget", "Fifo", "2024-04-12", 10013, DebtClause.DebtFromEmpty,
            "Out 1000 against an EMPTY stack creates debt 1000 outright -> In 1001@100.13 repays 1000, 1 survives: 1 x 100.13 = 100.13"),

        // ---------------------------------------------------- opening-lot debt, post-recovery issue, rounding
        new("GT-20", "G10-002", "Widget", "Fifo", "2024-04-25", 11074, DebtClause.RepayRated,
            "opening 5.25@41.03 + In 7@100.13 = 12.25 drained by Out 18.75 (debt 6.5) -> In 25@7.91 repays 6.5 leaving 18.5 "
            + "-> Out 4.5 consumes 4.5 at 7.91: 14 x 7.91 = 110.74"),
        new("GT-21", "G10-002", "Gadget", "Fifo", "2024-04-25", 9998, DebtClause.RepayRated,
            "opening 2.5@0.37 drained by Out 6.25 (debt 3.75) -> In 11@13.79 repays 3.75, 7.25 survive: "
            + "7.25 x 13.79 = 99.9775 -> paisa away-from-zero 99.98"),

        // ---------------------------------------------------- COMPOUND units on a negative book
        new("GT-22", "G13-001", "Widget", "Fifo", "2024-04-20", 30954, DebtClause.RepayRated,
            "2 Doz @121.32 = 24 base @10.11 -> Out 30 (debt 6) -> 4 Doz @88.44 = 48 base @7.37 repays 6, 42 survive: 42 x 7.37 = 309.54"),
        new("GT-23", "G13-002", "Widget", "Fifo", "2024-04-16", 29216, DebtClause.RepayRated,
            "1 Doz @100.13 = 12 base @100.13/12 (non-terminating) -> Out 20 (debt 8) -> In 40@9.13 repays 8, 32 survive: 32 x 9.13 = 292.16"),

        // ---------------------------------------------------- the ITEM-INVOICE seam
        new("GT-24", "G11-002", "Widget", "Fifo", "2024-04-20", 11865, DebtClause.RepayRated,
            "valuation order is by DATE, not post order: In 12@100.13 (d4) -> Out 8 (d5) -> Out 9 INVOICE (d11) drains 4, debt 5 "
            + "-> In 20@7.91 PURCHASE INVOICE (d16) repays 5, 15 survive: 15 x 7.91 = 118.65"),

        // ---------------------------------------------------- BATCH / CANCELLED / POST-DATED seams
        // ROUND 9 RE-DERIVED. This constant used to read 19775 with the derivation "valuation is batch-BLIND,
        // ROUND 11 RE-DERIVED TO 31640 — AND THIS ONE IS THE RESIDUAL, NOT A FIX. G12-002 puts the
        // over-draw in batch B-A and the recovery lot in batch B-B, so the item lives on TWO keys and the
        // DEBT RULE DOES NOT APPLY (it is confined to single-key items, where item-level IS per-key). The
        // engine therefore gives HEAD's answer here: the shortfall of 15 is DISCARDED at the outward and
        // the whole 40-unit lot becomes stock.
        //   In 10 @ 100.13 (B-A) -> Out 25 (B-A) drains 10, discards 15 -> In 40 @ 7.91 (B-B) lands whole
        //   -> 40 x 7.91 = 316.40 on a reported closing quantity of 25 (an implied Rs 12.66/unit).
        // THE SINGLE-KEY FORM OF THE SAME BOOK IS G1-001 AND IT CLOSES AT THE HONEST Rs 197.75 (GT-01).
        // The Rs 118.65 gap between them is exactly what this scope does not buy, pinned so it is visible.
        new("GT-25", "G12-002", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "valuation is batch-BLIND, so the B-B recovery repays the B-A debt company-wide: 25 x 7.91 = 197.75"),
        new("GT-26", "G14-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "the CANCELLED Out 50 contributes nothing; the POST-DATED In 40@7.91 repays the debt 15 normally: 25 x 7.91 = 197.75"),

        // ---------------------------------------------------- the OVERSTATEMENT direction
        new("GT-28", "G8-002", "Widget", "Fifo", "2024-04-20", 239247, DebtClause.RepayRated,
            "In 12@3.17 -> Out 30 (debt 18) -> In 45@88.61 repays 18 at the DEAR rate, 27 survive: 27 x 88.61 = 2392.47"),

        // ---------------------------------------------------- ROUND 9 RE-DERIVED: the debt IS per godown
        // This constant used to read 24729 under the clause control:no-company-wide-debt, with the
        // derivation "godown 1 goes to -8.75 but the COMPANY-WIDE stack never runs dry, so no debt exists".
        // That was the whole mistake in one sentence. Godown 1 DID run dry — it sold 8.75 units it did not
        // have — and letting godown 0's layers absorb that is exactly how an outward in one warehouse came
        // to consume another warehouse's lot. Per key:
        //   key (g0,""): In 30 @ 10.33 -> Out 6.5 (d19)          => 23.5 @ 10.33 = 242.755
        // ROUND 10 RESTORED to 24729 with the per-key revert. The company-wide layer stack NEVER goes dry
        // on this book (g0's 30 units cover the g1 outward), so no debt is ever created and the clause is
        // the no-debt control again. Godown 1 does go to -8.75 and the DEBT GATE is therefore open at that
        // event — but a gate that is open only permits a debt; it does not create one, and the merged stack
        // had the units. This is the book that proves the gate is not a licence.
        //   In 30 @ 10.33 -> Out 8.75 -> In 12 @ 7.91 -> Out 6.5, FIFO taking the oldest both times:
        //   30 - 8.75 - 6.5 = 14.75 @ 10.33 = 152.3675 and the whole 12 @ 7.91 = 94.92
        new("GT-27", "G9-002", "Widget", "Fifo", "2024-04-25", 24729, DebtClause.NoDebtControl,
            "both outwards take the oldest lot: 14.75 x 10.33 + 12 x 7.91 = 152.3675 + 94.92 = 247.2875 "
            + "-> 247.29"),

        // ===============================================================================================
        // ROUND 6 ADDITIONS (audit #5 finding [4], LOW) — every subject the harness CONVICTS on is now
        // directly pinned: all 70 CHECK 3 convictions and all 6 CHECK 2 convictions. Round 5 pinned 19 of
        // the 70, which the report's phrasing let a reader over-read as "the debt branch is pinned".
        // On EVERY corpus subject that carries a company-wide debt the surviving stack holds exactly ONE
        // layer (a dry point empties the stack), so LIFO drains identically to FIFO and the "L" twins carry
        // the same constant. That is a fact about this corpus and is stated rather than hidden.
        // ===============================================================================================

        new("GT-29", "G1-002", "Widget", "Fifo", "2024-04-16", 75906, DebtClause.RepayRated,
            "In 7@13.79 -> Out 18.5 drains 7, debt 11.5 -> In 30@41.03 repays 11.5, 18.5 survive: 18.5 x 41.03 = 759.055 -> 759.06"),
        new("GT-29L", "G1-002", "Widget", "Lifo", "2024-04-16", 75906, DebtClause.RepayRated,
            "one layer only: 18.5 x 41.03 = 759.055 -> 759.06"),
        new("GT-30", "G1-003", "Widget", "Fifo", "2024-04-17", 19775, DebtClause.RepayRated,
            "before the d20 issue this is G1-001's stream: debt 15 repaid by In 40@7.91, 25 survive: 25 x 7.91 = 197.75"),
        new("GT-30L", "G1-003", "Widget", "Lifo", "2024-04-17", 19775, DebtClause.RepayRated,
            "one layer only: 25 x 7.91 = 197.75"),
        new("GT-31", "G1-003", "Widget", "Fifo", "2024-04-25", 12458, DebtClause.RepayRated,
            "then Out 9.25 (d20) consumes 9.25 of the 25 at 7.91, leaving 15.75: 15.75 x 7.91 = 124.5825 -> 124.58"),
        new("GT-31L", "G1-003", "Widget", "Lifo", "2024-04-25", 12458, DebtClause.RepayRated,
            "one layer only: 15.75 x 7.91 = 124.5825 -> 124.58"),
        new("GT-32", "G1-004", "Widget", "Fifo", "2024-04-20", 14238, DebtClause.RepayRated,
            "opening 4.5@88.61 + In 6@100.13 = 10.5 drained by Out 22.5 (debt 12) -> In 30@7.91 repays 12, 18 survive: 18 x 7.91 = 142.38"),
        new("GT-32L", "G1-004", "Widget", "Lifo", "2024-04-20", 14238, DebtClause.RepayRated,
            "one layer only: 18 x 7.91 = 142.38"),

        new("GT-33", "G10-001", "Gadget", "Fifo", "2024-04-20", 166144, DebtClause.RepayRated,
            "In 8.5@3.17 -> Out 19.75 drains 8.5, debt 11.25 -> In 30@88.61 repays 11.25 at the DEAR rate, 18.75 survive: "
            + "18.75 x 88.61 = 1661.4375 -> 1661.44"),
        new("GT-33L", "G10-001", "Gadget", "Lifo", "2024-04-20", 166144, DebtClause.RepayRated,
            "one layer only: 18.75 x 88.61 = 1661.4375 -> 1661.44"),
        new("GT-34", "G10-001", "Gadget", "Fifo", "2024-04-25", 166144, DebtClause.RepayRated,
            "nothing moves after d17, so the d25 figure is the d20 figure: 18.75 x 88.61 = 1661.4375 -> 1661.44"),
        new("GT-34L", "G10-001", "Gadget", "Lifo", "2024-04-25", 166144, DebtClause.RepayRated,
            "one layer only: 18.75 x 88.61 = 1661.4375 -> 1661.44"),
        new("GT-35", "G10-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "In 10@100.13 -> Out 25 (debt 15) -> In 40@7.91 repays 15, 25 survive: 25 x 7.91 = 197.75"),
        new("GT-35L", "G10-001", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "one layer only: 25 x 7.91 = 197.75"),
        new("GT-36", "G10-001", "Widget", "Fifo", "2024-04-25", 19775, DebtClause.RepayRated,
            "nothing moves after d16: 25 x 7.91 = 197.75"),
        new("GT-36L", "G10-001", "Widget", "Lifo", "2024-04-25", 19775, DebtClause.RepayRated,
            "one layer only: 25 x 7.91 = 197.75"),

        new("GT-37", "G10-002", "Gadget", "Fifo", "2024-04-16", 9998, DebtClause.RepayRated,
            "opening 2.5@0.37 drained by Out 6.25 (debt 3.75) -> In 11@13.79 (d15) repays 3.75, 7.25 survive: "
            + "7.25 x 13.79 = 99.9775 -> 99.98"),
        new("GT-37L", "G10-002", "Gadget", "Lifo", "2024-04-16", 9998, DebtClause.RepayRated,
            "one layer only: 7.25 x 13.79 = 99.9775 -> 99.98"),
        new("GT-38", "G10-002", "Gadget", "Lifo", "2024-04-25", 9998, DebtClause.RepayRated,
            "the LIFO twin of GT-21; nothing moves after d15: 7.25 x 13.79 = 99.9775 -> 99.98"),
        new("GT-39", "G10-002", "Widget", "Fifo", "2024-04-16", 14634, DebtClause.RepayRated,
            "opening 5.25@41.03 + In 7@100.13 = 12.25 drained by Out 18.75 (debt 6.5) -> In 25@7.91 (d14) repays 6.5, "
            + "18.5 survive: 18.5 x 7.91 = 146.335 -> 146.34"),
        new("GT-39L", "G10-002", "Widget", "Lifo", "2024-04-16", 14634, DebtClause.RepayRated,
            "one layer only: 18.5 x 7.91 = 146.335 -> 146.34"),
        new("GT-40", "G10-002", "Widget", "Lifo", "2024-04-25", 11074, DebtClause.RepayRated,
            "the LIFO twin of GT-20, after Out 4.5 (d19): 14 x 7.91 = 110.74"),

        new("GT-41", "G11-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "valuation order is by DATE: In 30@100.13 (d5) -> Out 20 (d6) leaves 10 -> Out 25 SALES INVOICE (d11) "
            + "drains 10, debt 15 -> In 40@7.91 (d15) repays 15, 25 survive: 25 x 7.91 = 197.75"),
        new("GT-41L", "G11-001", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "one layer at every point: 25 x 7.91 = 197.75"),
        new("GT-42", "G11-002", "Widget", "Lifo", "2024-04-20", 11865, DebtClause.RepayRated,
            "the LIFO twin of GT-24: 15 x 7.91 = 118.65"),
        // ROUND 11 RE-DERIVED to 31640 with GT-25 — the multi-key residual, not a fix. See GT-25.
        new("GT-43", "G12-002", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "the LIFO twin of GT-25, still batch-blind: 25 x 7.91 = 197.75"),
        new("GT-44", "G13-001", "Widget", "Lifo", "2024-04-20", 30954, DebtClause.RepayRated,
            "the LIFO twin of GT-22: 42 x 7.37 = 309.54"),
        new("GT-45", "G13-002", "Widget", "Lifo", "2024-04-16", 29216, DebtClause.RepayRated,
            "the LIFO twin of GT-23: 32 x 9.13 = 292.16"),
        new("GT-46", "G14-001", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "the LIFO twin of GT-26: 25 x 7.91 = 197.75"),
        new("GT-47", "G2-001", "Widget", "Lifo", "2024-04-25", 8449, DebtClause.RepayMultiLot,
            "the LIFO twin of GT-04; the 12@7.91 lot is entirely consumed by the debt so only one layer ever exists: 7 x 12.07 = 84.49"),

        new("GT-48", "G2-002", "Widget", "Fifo", "2024-04-20", 8449, DebtClause.RepayMultiLot,
            "before the d22 issue: debt 25 -> In 12@7.91 repays 12 (no layer) -> In 20@12.07 repays 13, 7 survive: 7 x 12.07 = 84.49"),
        new("GT-48L", "G2-002", "Widget", "Lifo", "2024-04-20", 8449, DebtClause.RepayMultiLot,
            "one layer only: 7 x 12.07 = 84.49"),
        new("GT-49", "G2-002", "Widget", "Fifo", "2024-04-25", 1509, DebtClause.RepayMultiLot,
            "then Out 5.75 (d22) consumes 5.75 of the 7 at 12.07, leaving 1.25: 1.25 x 12.07 = 15.0875 -> 15.09"),
        new("GT-49L", "G2-002", "Widget", "Lifo", "2024-04-25", 1509, DebtClause.RepayMultiLot,
            "one layer only: 1.25 x 12.07 = 15.0875 -> 15.09"),
        new("GT-50", "G2-003", "Widget", "Fifo", "2024-04-25", 5537, DebtClause.RepayMultiLot,
            "DEAR-then-CHEAP: debt 25 -> In 20@12.07 repays 20 (no layer) -> In 12@7.91 repays 5, 7 join at 7.91: 7 x 7.91 = 55.37"),
        new("GT-50L", "G2-003", "Widget", "Lifo", "2024-04-25", 5537, DebtClause.RepayMultiLot,
            "one layer only: 7 x 7.91 = 55.37"),
        new("GT-51", "G2-004", "Widget", "Fifo", "2024-04-25", 1110, DebtClause.RepayMultiLot,
            "In 5@1000.07 -> Out 25 (debt 20) -> In 20@1000.07 repays ALL 20, nothing joins -> In 30@0.37 joins whole: 30 x 0.37 = 11.10"),
        new("GT-51L", "G2-004", "Widget", "Lifo", "2024-04-25", 1110, DebtClause.RepayMultiLot,
            "one layer only: 30 x 0.37 = 11.10"),
        new("GT-52", "G4-001", "Widget", "Lifo", "2024-04-25", 24917, DebtClause.DebtAccumulated,
            "the LIFO twin of GT-09: 31.5 x 7.91 = 249.165 -> 249.17"),

        new("GT-53", "G5-001", "Widget", "Fifo", "2024-04-12", 290377, DebtClause.DebtFromEmpty,
            "Out 11 against an EMPTY stack creates debt 11 outright -> In 40@100.13 repays 11, 29 survive: 29 x 100.13 = 2903.77"),
        new("GT-53L", "G5-001", "Widget", "Lifo", "2024-04-12", 290377, DebtClause.DebtFromEmpty,
            "one layer only: 29 x 100.13 = 2903.77"),
        new("GT-54", "G5-002", "Widget", "Lifo", "2024-04-12", 10013, DebtClause.DebtFromEmpty,
            "the LIFO twin of GT-19: 1 x 100.13 = 100.13"),
        new("GT-55", "G6-002", "Widget", "Fifo", "2024-04-17", 11865, DebtClause.RepayRated,
            "before the d20 count: debt 15 repaid by In 30@7.91, 15 survive: 15 x 7.91 = 118.65"),
        new("GT-55L", "G6-002", "Widget", "Lifo", "2024-04-17", 11865, DebtClause.RepayRated,
            "one layer only: 15 x 7.91 = 118.65"),
        new("GT-56", "G8-001", "Widget", "Fifo", "2024-04-20", 8559, DebtClause.RepayRated,
            "In 12@88.61 -> Out 30 drains 12, debt 18 -> In 45@3.17 repays 18 at the CHEAP rate, 27 survive: 27 x 3.17 = 85.59"),
        new("GT-56L", "G8-001", "Widget", "Lifo", "2024-04-20", 8559, DebtClause.RepayRated,
            "one layer only: 27 x 3.17 = 85.59"),
        new("GT-57", "G8-002", "Widget", "Lifo", "2024-04-20", 239247, DebtClause.RepayRated,
            "the LIFO twin of GT-28, the OVERSTATEMENT direction: 27 x 88.61 = 2392.47"),
        new("GT-58", "G8-003", "Widget", "Fifo", "2024-04-17", 239247, DebtClause.RepayRated,
            "before the d20 issue: debt 18 repaid by In 45@88.61, 27 survive: 27 x 88.61 = 2392.47"),
        new("GT-58L", "G8-003", "Widget", "Lifo", "2024-04-17", 239247, DebtClause.RepayRated,
            "one layer only: 27 x 88.61 = 2392.47"),
        new("GT-59", "G8-003", "Widget", "Fifo", "2024-04-25", 137346, DebtClause.RepayRated,
            "then Out 11.5 (d20) consumes 11.5 of the 27 at 88.61, leaving 15.5: 15.5 x 88.61 = 1373.455 "
            + "-> away-from-zero (an EXACT midpoint) -> 1373.46"),
        new("GT-59L", "G8-003", "Widget", "Lifo", "2024-04-25", 137346, DebtClause.RepayRated,
            "one layer only: 15.5 x 88.61 = 1373.455 -> 1373.46"),

        // CHECK 2's sixth conviction, previously the only one not directly pinned.
        new("GT-60", "G2-002", "Widget", "AverageCost", "2024-04-20", 8449, DebtClause.AverageDebt,
            "the pool before the d22 issue: nothing joins until the 12.07 lot; avg 12.07 x 7 = 84.49"),

        // ---------------------------------------------------- ROUND 7 / AUDIT #6 LOW [2]
        // THE ONLY SUBJECT IN THE CORPUS WHERE FIFO AND LIFO GENUINELY DISAGREE ON A DEBT BOOK.
        // Read the "LIFO twin" derivations above: every one of them says "one layer only". That is not a
        // coincidence of wording — before G15-001 every debt scenario left a SINGLE surviving layer, so
        // oldest-first and newest-first picked the same units and the whole LIFO debt path was pinned by
        // constants that FIFO would have satisfied too. Reference.Consume differs between the methods in
        // exactly one place (index 0 vs index Count-1 of the same list) and nothing in the corpus could
        // tell the two apart. The production slice has to be verified on LIFO, so that gap is closed here.
        //
        // G15-001 replay, in full (dates are distinct, so ordering is by date alone):
        //   d5   In  10 @ 100.13                 layers [10@100.13]
        //   d10  Out 25         drains the 10, remainder 15 becomes DEBT      layers []   debt 15
        //   d15  In  40 @   7.91  repays the debt 15 AT 7.91 (those 15 units are COGS, never a layer),
        //                         surplus 25 joins                            layers [25@7.91]
        //   d18  In  20 @  12.07  no debt outstanding, joins whole            layers [25@7.91, 20@12.07]
        //   d22  Out 13         <-- THE ONLY EVENT THAT CONSULTS AN END OF THE STACK
        // on-hand at d25 = 10 - 25 + 40 + 20 - 13 = 32, on BOTH methods. Same units, same book, two
        // different closing values and two different issue values — that is the whole point.
        new("GT-61", "G15-001", "Widget", "Fifo", "2024-04-20", 43915, DebtClause.RepayRated,
            "THE CONTROL, before the divergence: at d20 both layers are still WHOLE, so FIFO and LIFO MUST "
            + "agree here — nothing has been taken off either end yet. 25 x 7.91 + 20 x 12.07 = "
            + "197.75 + 241.40 = 439.15"),
        new("GT-61L", "G15-001", "Widget", "Lifo", "2024-04-20", 43915, DebtClause.RepayRated,
            "the same two whole layers, and a whole stack has no oldest and no newest: 197.75 + 241.40 = 439.15"),
        new("GT-62", "G15-001", "Widget", "Fifo", "2024-04-25", 33632, DebtClause.RepayRated,
            "THE DIVERGENCE, FIFO half. The d22 Out 13 comes off the OLDEST layer, so 25@7.91 is cut to "
            + "12@7.91 and the 20@12.07 layer is untouched: 12 x 7.91 = 94.92 and 20 x 12.07 = 241.40, "
            + "so 94.92 + 241.40 = 336.32"),
        new("GT-62L", "G15-001", "Widget", "Lifo", "2024-04-25", 28224, DebtClause.RepayRated,
            "THE DIVERGENCE, LIFO half. The SAME Out 13 comes off the NEWEST layer, so 20@12.07 is cut to "
            + "7@12.07 and the 25@7.91 layer is untouched: 25 x 7.91 = 197.75 and 7 x 12.07 = 84.49, "
            + "so 197.75 + 84.49 = 282.24"),

        // ================================================================ ROUND 8 — THE LOOK-AHEAD BLIND SPOT
        // WHY THESE EXIST. Three independent review lenses reproduced the SAME defect and the harness said
        // ACCEPTED, because no corpus subject could reach it. The chain's last-rated-inward link was resolved
        // over the WHOLE as-of window in both the engine AND the reference, so units created by a physical
        // count or by an unrated inward were priced by a purchase dated AFTER them. Every scenario that
        // consulted the chain either ENDED at that movement (G6-001, G6-002, G7-001, G7-002) or set a
        // standard cost that short-circuited the chain before the link was reached (G6-001). Corpus subjects
        // G6-003 and G7-003 put a rated inward AFTER the chain-consulting movement, with NO standard cost.
        //
        // The reference was corrected (Reference.Chain.At — the chain is now point-in-time); THESE CONSTANTS
        // ARE THE EXTERNAL ANCHOR ON THAT CORRECTION, so it cannot be un-done or re-widened silently. Note
        // what "fix the reference, never the constant" means here: the constants below were derived from the
        // MOVEMENT LISTS by hand FIRST, and the reference was then required to reproduce them.

        // ---------------------------------------------------- G6-003: a COUNT with a debt, then a PURCHASE
        // d5 In 10@100.13 | d10 Out 25 (debt 15) | d15 Count 8 | d18 In 1@1000000.03.  StandardCost UNSET.
        // Ever spent on this item = 10 x 100.13 + 1 x 1000000.03 = Rs 1001001.33 on 9 units held. An engine
        // that looks ahead prices the 8 counted units at the d18 rate and reports Rs 9000000.27 — NINE TIMES
        // everything the item ever cost, and unbounded in that later rate.
        new("GT-65", "G6-003", "Widget", "Fifo", "2024-04-16", 80104, DebtClause.CountWithDebt,
            "NO standard cost here, so the chain must reach its last link. In 10@100.13 -> Out 25 drains the "
            + "10 and leaves debt 15 -> the d15 Count 8 writes the debt off and tops the EMPTY stack up by 8. "
            + "The chain is POINT-IN-TIME: running average 0, no standard cost, and the only rated inward that "
            + "had LANDED by d15 is the d5 lot, so the rate is 100.13. 8 x 100.13 = 801.04"),
        new("GT-65L", "G6-003", "Widget", "Lifo", "2024-04-16", 80104, DebtClause.CountWithDebt,
            "a count reconciles to a TOTAL, not to a particular layer, and there is exactly one layer, so LIFO "
            + "is identical: 8 x 100.13 = 801.04"),
        new("GT-66", "G6-003", "Widget", "AverageCost", "2024-04-16", 80104, DebtClause.AverageDebt,
            "the pool empties at the Out 25 (debt 15); the count writes the debt off and refills the pool at "
            + "the same point-in-time chain rate: avg 100.13 x 8 = 801.04"),
        new("GT-67", "G6-003", "Widget", "Fifo", "2024-04-20", 100080107, DebtClause.CountWithDebt,
            "THE LOOK-AHEAD TEST, and the book is IDENTICAL to GT-65 — only the REPORT DATE moved past the "
            + "d18 purchase. The 8 counted units were priced WHEN THEY WERE COUNTED, at 100.13, and the d18 "
            + "lot joins the stack whole: 8 x 100.13 + 1 x 1000000.03 = 801.04 + 1000000.03 = 1000801.07"),
        new("GT-67L", "G6-003", "Widget", "Lifo", "2024-04-20", 100080107, DebtClause.CountWithDebt,
            "nothing is consumed after d18, so the LIFO stack holds the same two whole layers: "
            + "801.04 + 1000000.03 = 1000801.07"),
        new("GT-68", "G6-003", "Widget", "AverageCost", "2024-04-20", 100080107, DebtClause.AverageDebt,
            "the pool holds 801.04 after the count; the d18 lot adds 1000000.03 over 9 units, so the average "
            + "is 111200.1188 recurring and the value is that average x 9 = 1000801.07"),

        // ---------------------------------------------------- G6-004: the `debt = 0m` COUNT WRITE-OFF
        // d5 In 10@100.13 | d10 Out 25 (debt 15) | d15 Count 8 | d18 In 40@7.91.  StandardCost 9.77.
        // Deleting the count write-off from either engine arm left all nine of the slice's own tests green,
        // because G6-001 and G6-002 both END at the count and a surviving debt had nothing left to eat.
        // Here it has 40 units to eat: WITHOUT the write-off the answer is 78.16 + 25 x 7.91 = Rs 275.91 and
        // the layer stack holds 33 units while the report prints 48.
        new("GT-69", "G6-004", "Widget", "Fifo", "2024-04-25", 39456, DebtClause.CountWithDebt,
            "the standard cost 9.77 IS set here, so the chain stops there and this constant is about the "
            + "WRITE-OFF, not the chain's tail. In 10@100.13 -> Out 25 (debt 15) -> Count 8 writes the debt "
            + "off and tops up 8 units at 9.77 -> In 40@7.91 therefore finds NO debt to repay and all 40 "
            + "survive: 8 x 9.77 + 40 x 7.91 = 78.16 + 316.40 = 394.56"),
        new("GT-69L", "G6-004", "Widget", "Lifo", "2024-04-25", 39456, DebtClause.CountWithDebt,
            "nothing is consumed after the d18 inward, so LIFO holds the same two whole layers: "
            + "78.16 + 316.40 = 394.56"),
        new("GT-70", "G6-004", "Widget", "AverageCost", "2024-04-25", 39456, DebtClause.AverageDebt,
            "the pool is 8 x 9.77 = 78.16 after the count, then the 40@7.91 lot joins whole for a pool of "
            + "78.16 + 316.40 = 394.56 over 48 units: avg 8.22 x 48 = 394.56"),

        // ---------------------------------------------------- G6-005: a count taken DOWN, to EXACTLY ZERO
        // d5 In 13.75@100.13 | d10 Out 31.25 (debt 17.5) | d15 Count 0 | d18 In 21.25@7.91.  StandardCost 9.77.
        // The DOWNWARD end of the legal counted range while a debt is outstanding: with a debt the book
        // quantity is negative, so every legal count (>= 0) RAISES it, and a count of exactly 0 is the
        // smallest. It asserts an empty shelf and must leave an empty stack. A second, independent pin on the
        // same write-off clause as G6-004: without it the debt 17.5 eats the recovery lot and only 3.75 units
        // survive, worth Rs 29.66.
        new("GT-71", "G6-005", "Widget", "Fifo", "2024-04-25", 16809, DebtClause.CountWithDebt,
            "In 13.75@100.13 -> Out 31.25 drains the 13.75 and leaves debt 17.5 -> the d15 Count 0 asserts an "
            + "EMPTY SHELF: the debt is written off, there is nothing to top up, and the stack stays empty -> "
            + "In 21.25@7.91 finds no debt, so all 21.25 units survive: 21.25 x 7.91 = 168.0875 -> "
            + "away-from-zero 168.09"),
        new("GT-71L", "G6-005", "Widget", "Lifo", "2024-04-25", 16809, DebtClause.CountWithDebt,
            "one surviving layer only, so LIFO is identical: 21.25 x 7.91 = 168.0875 -> 168.09"),
        new("GT-72", "G6-005", "Widget", "AverageCost", "2024-04-25", 16809, DebtClause.AverageDebt,
            "the count writes the debt off and sets the pool to 0 units, so nothing of the 100.13 lot carries "
            + "forward; the 21.25@7.91 lot then joins whole: avg 7.91 x 21.25 = 168.0875 -> 168.09"),

        // ---------------------------------------------------- G7-003: UNRATED inward, then a PURCHASE
        // d5 In 10@100.13 | d10 Out 25 (debt 15) | d15 In 40 UNRATED | d18 In 1@9999.99.  StandardCost UNSET.
        // Ever spent = 10 x 100.13 + 1 x 9999.99 = Rs 11001.29 on 26 units held. A whole-window chain prices
        // the ENTIRE 25-unit surplus of the unrated lot at the d18 rate and holds Rs 259999.74 — 23.6x
        // everything the item ever cost, the same shape as this project's historical failure #2.
        new("GT-73", "G7-003", "Widget", "Fifo", "2024-04-16", 250325, DebtClause.RepayUnrated,
            "NO standard cost. In 10@100.13 -> Out 25 (debt 15) -> the d15 inward carries NO purchase rate, so "
            + "the POINT-IN-TIME chain prices it: running average 0, no standard cost, and the last rated "
            + "inward that had LANDED by d15 is the d5 lot, so 100.13. It repays 15 and 25 survive: "
            + "25 x 100.13 = 2503.25"),
        new("GT-73L", "G7-003", "Widget", "Lifo", "2024-04-16", 250325, DebtClause.RepayUnrated,
            "one surviving layer only, so LIFO is identical: 25 x 100.13 = 2503.25"),
        new("GT-74", "G7-003", "Widget", "AverageCost", "2024-04-16", 250325, DebtClause.AverageDebt,
            "the same walk in the pool: the unrated lot is priced at 100.13, 15 of its units repay the debt "
            + "and 25 join: avg 100.13 x 25 = 2503.25"),
        new("GT-75", "G7-003", "Widget", "Fifo", "2024-04-20", 1250324, DebtClause.RepayUnrated,
            "THE LOOK-AHEAD TEST, on the same book as GT-73 with the report date moved past the d18 purchase. "
            + "Adding In 1@9999.99 must not RE-PRICE the d15 unrated lot, which was costed at the only rate "
            + "the item had paid when it arrived: 25 x 100.13 + 1 x 9999.99 = 2503.25 + 9999.99 = 12503.24"),
        new("GT-75L", "G7-003", "Widget", "Lifo", "2024-04-20", 1250324, DebtClause.RepayUnrated,
            "nothing is consumed, so the LIFO stack holds the same two whole layers: "
            + "2503.25 + 9999.99 = 12503.24"),
        new("GT-76", "G7-003", "Widget", "AverageCost", "2024-04-20", 1250324, DebtClause.AverageDebt,
            "the pool holds 2503.25 after the unrated lot repaid the debt; the d18 lot adds 9999.99 over 26 "
            + "units, so the average is 480.8938 recurring and the value is that average x 26 = 12503.24"),

        // ===============================================================================================
        // ROUND 10 — THE MULTI-KEY BOOKS UNDER THE ITEM-LEVEL REPLAY + THE DEBT GATE.
        //
        // ROUND 9 re-keyed valuation to (item, godown, batch) and re-derived this whole block per key.
        // THAT WAS REVERTED (user decision 2026-07-29): a per-key replay that re-derives each key
        // independently makes cost stop flowing across a stock transfer, which re-priced transferred units
        // off an empty pool (Rs 5,000,002.37 of stock on Rs 1,000,003.73 ever spent, on a book the
        // item-level replay valued exactly right) and valued a count in a never-used godown at Rs 0.00.
        //
        // So every constant below is the ITEM-LEVEL answer again — the merged, batch-blind, godown-blind
        // layer stack — with ONE new rule on top: THE DEBT GATE. A shortfall in the merged walk becomes a
        // debt only where some (godown, batch) key is genuinely negative at that point. Where no key is
        // short the shortfall is an artefact of flattening and is DISCARDED, exactly as HEAD discards it.
        //
        // WHAT THAT BUYS, AND IT IS THE POINT OF THE SLICE: on the five NEVER-NEGATIVE multi-key books
        // below (N8-001, N9-001, N10-001, N10-002, N11-001) no debt can be created at all, so every debt
        // clause is inert and each of these constants is HEAD's own number. CHECK 1 asserts that
        // byte-for-byte over the whole row set; these goldens pin the arithmetic behind it.
        //
        // WHAT IT DOES NOT BUY, STATED HERE RATHER THAN DISCOVERED LATER: where a key IS genuinely short,
        // the debt is still computed from the flattened stream, so the item-level/per-key DESYNC survives.
        // G16-001 below holds 50 units of layers against a reported closing quantity of 10, and N10-001 at
        // d9 holds nothing against a reported 10. Those deltas are printed by the harness (REFERENCE
        // SELF-CONSISTENCY, "ITEM-LEVEL/PER-KEY DESYNC") instead of being smoothed away.
        // ===============================================================================================

        // ---------------------------------------------------- N8-001 — two godowns, never negative, no count
        // MERGED stack in arrival order: 30 @ 10.33 (g0, d4) then 20 @ 12.07 (g1, d6).
        // Outwards: 12.5 (g0, d11) and 7.25 (g1, d15). No key ever negative => no debt is possible.
        new("GT-77", "N8-001", "Widget", "Lifo", "2024-04-13", 40043, DebtClause.NoDebtControl,
            "at d13 only the 12.5 outward has landed and LIFO takes the NEWEST lot: 30 x 10.33 + 7.5 x "
            + "12.07 = 309.90 + 90.525 = 400.425 -> 400.43"),
        new("GT-78", "N8-001", "Widget", "Fifo", "2024-04-20", 34728, DebtClause.NoDebtControl,
            "FIFO takes the OLDEST for both outwards, 12.5 + 7.25 = 19.75 off the 30: 10.25 x 10.33 + 20 x "
            + "12.07 = 105.8825 + 241.40 = 347.2825 -> 347.28"),
        new("GT-78L", "N8-001", "Widget", "Lifo", "2024-04-20", 31292, DebtClause.NoDebtControl,
            "LIFO takes the NEWEST for both, 19.75 off the 20: 30 x 10.33 + 0.25 x 12.07 = 309.90 + 3.0175 "
            + "= 312.9175 -> 312.92"),
        new("GT-79", "N8-001", "Widget", "AverageCost", "2024-04-20", 33354, DebtClause.NoDebtControl,
            "one pool: (309.90 + 241.40) / 50 = 11.026, and an outward at the average leaves it unchanged, "
            + "so 30.25 x 11.026 = 333.5365 -> 333.54"),

        // ---------------------------------------------------- N9-001 — two godowns + an OPENING lot in g1
        // MERGED stack: OPENING 6.25 @ 88.61 (g1, sorts first) then 15 @ 10.33 (g0, d4), later 8 @ 41.03
        // (g1, d18). Outwards 11 (g0, d9) and 3.5 (g1, d14). Never negative anywhere.
        new("GT-80", "N9-001", "Widget", "Fifo", "2024-04-11", 10588, DebtClause.NoDebtControl,
            "FIFO eats the OPENING lot first: 11 takes all 6.25 @ 88.61 and 4.75 of the 10.33 lot, leaving "
            + "10.25 x 10.33 = 105.8825 -> 105.88"),
        new("GT-81", "N9-001", "Widget", "Fifo", "2024-04-16", 6973, DebtClause.NoDebtControl,
            "the d14 outward takes 3.5 more off the same lot: 6.75 x 10.33 = 69.7275 -> 69.73"),
        new("GT-82", "N9-001", "Widget", "Fifo", "2024-04-25", 39797, DebtClause.NoDebtControl,
            "the d18 replenishment lands whole: 6.75 x 10.33 + 8 x 41.03 = 69.7275 + 328.24 = 397.9675 "
            + "-> 397.97"),
        new("GT-82L", "N9-001", "Widget", "Lifo", "2024-04-25", 88722, DebtClause.NoDebtControl,
            "LIFO eats the NEWEST, so both outwards come off the 15 @ 10.33 lot and the dear opening lot "
            + "survives: 6.25 x 88.61 + 0.5 x 10.33 + 8 x 41.03 = 553.8125 + 5.165 + 328.24 = 887.2175 "
            + "-> 887.22"),
        new("GT-83", "N9-001", "Widget", "AverageCost", "2024-04-25", 55338, DebtClause.NoDebtControl,
            "the pool is (553.8125 + 154.95) = 708.7625 over 21.25 = 33.3535294...; the two outwards leave "
            + "the average alone and reduce it to 6.75 units = 225.1363235...; the d18 lot adds 328.24 over "
            + "8 more, so 553.3763235... over 14.75 units is the value: 553.3763... -> 553.38"),

        // ---------------------------------------------------- G9-001 — POSITIVE COMPANY-WIDE, SHORT IN g1
        // g1 goes to -6.5, so THE DEBT GATE IS OPEN at that outward. It does not follow that a debt exists:
        // the merged stack held 35.5 units when the 12 was drawn, so nothing was short of a layer and no
        // debt was created. This is the book that shows the gate PERMITS a debt, it does not manufacture
        // one — the property the family was written for ("this scenario must not move") and it does not.
        new("GT-84", "G9-001", "Widget", "Fifo", "2024-04-13", 73666, DebtClause.NoDebtControl,
            "the d11 outward of 12 takes the OLDEST lot: 18 x 10.33 + 5.5 x 100.13 = 185.94 + 550.715 = "
            + "736.655 -> 736.66"),
        new("GT-85", "G9-001", "Widget", "Fifo", "2024-04-20", 73407, DebtClause.NoDebtControl,
            "the d15 outward of 0.25 takes the same lot: 17.75 x 10.33 + 5.5 x 100.13 = 183.3575 + 550.715 "
            + "= 734.0725 -> 734.07"),
        new("GT-85L", "G9-001", "Widget", "Lifo", "2024-04-20", 24017, DebtClause.NoDebtControl,
            "LIFO takes the NEWEST, so the 12 swallows the whole 5.5 @ 100.13 and 6.5 of the 10.33 lot, and "
            + "the 0.25 follows: 23.25 x 10.33 = 240.1725 -> 240.17"),
        new("GT-86", "G9-001", "Widget", "AverageCost", "2024-04-20", 56364, DebtClause.NoDebtControl,
            "one pool: (309.90 + 550.715) / 35.5 = 24.2426760...; outwards at the average leave it "
            + "unchanged, so 23.25 x 24.2426760... = 563.6422... -> 563.64"),

        // ---------------------------------------------------- G9-002 / G12-001 / G12-002 — the twins
        // Same shape: one key goes short (so the gate opens) while the MERGED stack never runs dry (so no
        // debt is created). G12-002 is the exception — there the merged stack DOES run dry, the debt is
        // real, and the recovery lot repays it company-wide (GT-25/GT-43/GT-87D).
        new("GT-87", "G9-002", "Widget", "Lifo", "2024-04-25", 26302, DebtClause.NoDebtControl,
            "the d9 outward of 8.75 has only the 10.33 lot to take, then the d19 outward of 6.5 takes the "
            + "NEWEST, which is the 12 @ 7.91: 21.25 x 10.33 + 5.5 x 7.91 = 219.5125 + 43.505 = 263.0175 "
            + "-> 263.02"),
        new("GT-87A", "G9-002", "Widget", "AverageCost", "2024-04-25", 25296, DebtClause.NoDebtControl,
            "the pool is 21.25 x 10.33 = 219.5125 after the first outward, then the 12 @ 7.91 lot makes it "
            + "314.4325 over 33.25 = 9.4566165...; the last outward leaves the average alone: 26.75 x "
            + "9.4566165... = 252.9645... -> 252.96"),
        new("GT-87B", "G12-001", "Widget", "Fifo", "2024-04-20", 3955, DebtClause.NoDebtControl,
            "batch B-A goes to -5 so the gate opens, but the merged stack holds 30 and the outward of 25 "
            + "finds its units: FIFO drains the whole 20 @ 100.13 and 5 of the 7.91 lot, leaving "
            + "5 x 7.91 = 39.55"),
        new("GT-87C", "G12-001", "Widget", "Lifo", "2024-04-20", 50065, DebtClause.NoDebtControl,
            "LIFO drains the whole 10 @ 7.91 and 15 of the 100.13 lot instead: 5 x 100.13 = 500.65"),
        // ROUND 11 RE-TAGGED, CONSTANT UNCHANGED. G12-002 is MULTI-KEY (batches B-A and B-B), so no debt
        // is created and the AverageCost arm is HEAD's: the over-draw empties the pool, the In 40 @ 7.91
        // refills it at 7.91, and the value is that average x the REPORTED closing quantity of 25. The
        // figure is the same 197.75 the debt rule would have produced — but by a different route, so the
        // 'average:debt-path' label was false of it and the clause-coverage claim built on it was wrong.
        // (The FIFO/LIFO twins GT-25/GT-43 do NOT agree with it: they close at 316.40, because the layer
        // arm keeps the whole 40-unit lot while the pool arm multiplies by the reported quantity.)
        new("GT-87D", "G12-002", "Widget", "AverageCost", "2024-04-20", 19775, DebtClause.AverageDebt,
            "no debt is created on a multi-key item: the Out 25 empties the pool, the In 40 @ 7.91 refills "
            + "it to an average of 7.91, and the value is that average x the reported closing quantity of "
            + "25: 25 x 7.91 = 197.75"),

        // ===============================================================================================
        // THE MULTI-GODOWN FAMILIES — the books the corpus did not have at all before round 9, KEPT.
        // The subjects pinned below are never-negative on every key, so no debt fires on them under ANY
        // scoping and their constants are stable evidence: CHECK 1 holds them to HEAD to the byte.
        // The multi-key subjects that DO go short carry no golden — see the note further down.
        // ===============================================================================================

        // ---------------------------------------------------- N10-001 — THE ITEM-LEVEL / PER-KEY DESYNC
        //   d5 g0 In 30 @ 100.13 | d6 g1 In 30 @ 100.13 | d7 g0 Count 30 (a PER-KEY no-op)
        //   d8 g0 Out 30 | d9 g1 Out 20 | d10 g1 In 20 @ 7.91
        // Guard-posted; minimum on-hand across g0, g1 and the item is 0 on every date — NOTHING here is
        // negative to the product, yet the merged replay goes short anyway. The d7 count sees 60 merged
        // units and truncates the stack to 30, throwing away units the OTHER godown really holds; the d8
        // outward empties that truncated stack and the d9 outward finds nothing. That shortfall is the
        // DESYNC, not a negative: MovementEvents flattens a physical count to item level while
        // InventoryLedger.ApplyToKey applies it per (item, godown, batch).
        // The honest per-key figure is Rs 1,159.50. The item-level replay cannot reach it, HEAD reports
        // Rs 158.20, and the reference (debt ungated) reports Rs 0.00 — three different numbers, none of
        // which a hand derivation can call correct while valuation is keyed differently from quantity.
        // ONLY GT-90 survives here, and only because it pins a date where the answer is 0 either way.
        new("GT-90", "N10-001", "Widget", "Fifo", "2024-04-09", 0, DebtClause.DebtFromEmpty,
            "at d9, before the replenishment: the d7 count truncated the merged stack to 30, the d8 outward "
            + "emptied it, and the d9 outward of 20 therefore draws against a net of EXACTLY ZERO — a debt "
            + "of 20 created from an empty stack and still outstanding here. On-hand reports 10, but no "
            + "cost layer survives to carry any of it, so the closing value is 0.00"),

        // ---------------------------------------------------- N10-002 — no count, no debt, keys still differ
        //   g0 In 13.75 @ 100.13 (d5) | g1 In 20 @ 7.91 (d6) | g1 Out 6.25 (d10) | g0 Out 6.25 (d12)
        // Never negative anywhere, so nothing here can create a debt. The merged replay lets each outward
        // eat whichever end of the ITEM's stack its method chooses, so FIFO and LIFO disagree by
        // Rs 1,152.75 on a book with no negative stock, no count and no unusual voucher of any kind. That
        // is a real pre-existing property of the engine and these constants pin it rather than hide it.
        new("GT-91", "N10-002", "Widget", "Fifo", "2024-04-20", 28336, DebtClause.NoDebtControl,
            "both outwards take the OLDEST lot, which is g0's: 1.25 x 100.13 + 20 x 7.91 = 125.1625 + "
            + "158.20 = 283.3625 -> 283.36"),
        new("GT-91L", "N10-002", "Widget", "Lifo", "2024-04-20", 143611, DebtClause.NoDebtControl,
            "both outwards take the NEWEST lot, which is g1's: 13.75 x 100.13 + 7.5 x 7.91 = 1376.7875 + "
            + "59.325 = 1436.1125 -> 1436.11"),
        new("GT-92", "N10-002", "Widget", "AverageCost", "2024-04-20", 96647, DebtClause.NoDebtControl,
            "one pool: (1376.7875 + 158.20) / 33.75 = 45.4811111...; 21.25 x 45.4811111... = 966.4736... "
            + "-> 966.47"),
        new("GT-93", "N10-002", "Widget", "Fifo", "2024-04-11", 90918, DebtClause.NoDebtControl,
            "at d11 only g1's outward has landed and FIFO still takes g0's lot: 7.5 x 100.13 + 20 x 7.91 = "
            + "750.975 + 158.20 = 909.175 -> 909.18"),

        // ---------------------------------------------------- N11-001 — godowns AND batches, three keys
        //   (g0,B-A) In 13.75 @ 100.13 (d4) | (g0,B-B) In 11 @ 7.91 (d5) | (g1,"") In 6.25 @ 41.03 (d6)
        //   (g0,B-B) Out 11 (d10) -> that key drains to EXACTLY zero | (g1,"") Out 3.5 (d12)
        // Never negative on any of the three keys, so no debt is possible here either.
        new("GT-94", "N11-001", "Widget", "Fifo", "2024-04-20", 33752, DebtClause.NoDebtControl,
            "the two outwards total 14.5 and FIFO takes the OLDEST, which is B-A's dear lot: it is emptied "
            + "and 0.75 comes off the 7.91 lot, leaving 10.25 x 7.91 + 6.25 x 41.03 = 81.0775 + 256.4375 = "
            + "337.515 -> 337.52"),
        new("GT-94L", "N11-001", "Widget", "Lifo", "2024-04-20", 139854, DebtClause.NoDebtControl,
            "LIFO takes the NEWEST: the d10 outward of 11 swallows the whole 6.25 @ 41.03 and 4.75 of the "
            + "7.91 lot, then the d12 outward of 3.5 takes 3.5 more of it: 13.75 x 100.13 + 2.75 x 7.91 = "
            + "1376.7875 + 21.7525 = 1398.54"),
        new("GT-95", "N11-001", "Widget", "AverageCost", "2024-04-20", 91561, DebtClause.NoDebtControl,
            "one pool: (1376.7875 + 87.01 + 256.4375) / 31 = 55.4914516...; 16.5 x 55.4914516... = "
            + "915.6089... -> 915.61"),
        new("GT-96", "N11-001", "Widget", "Fifo", "2024-04-11", 61881, DebtClause.NoDebtControl,
            "at d11 only the 11-unit outward has landed and FIFO takes it off B-A's lot: 2.75 x 100.13 + "
            + "11 x 7.91 + 6.25 x 41.03 = 275.3575 + 87.01 + 256.4375 = 618.805 -> 618.81"),

        // ===============================================================================================
        // WHY THE MULTI-KEY DEBT SUBJECTS CARRY NO GOLDEN. (2026-07-29 — read this before adding one.)
        // ===============================================================================================
        // A golden is a HAND-DERIVED TRUTH that the reference must reproduce; CHECK 4c convicts the
        // reference when it does not. Issuing one therefore asserts that the reference is ENTITLED to an
        // answer on that book.
        //
        // The reference is a validated oracle on SINGLE-KEY books only (Reference.cs's header states the
        // evidence and the limits). On a multi-key book where a debt actually fires, the item-level model
        // it uses is KNOWN WRONG — it is the model whose per-key alternative broke ordinary godown
        // transfers — so no hand derivation can be honest about what the number OUGHT to be.
        //
        // An abandoned round confined the debt rule to single-key items and pinned those multi-key subjects
        // at the values that scoping produced (N10-001 at Rs 158.20/237.30, G16-001 at Rs 240.00/48.00,
        // G16-002 at Rs 0.00, and G12-002 edited from Rs 197.75 to Rs 316.40). Those constants recorded a
        // design that no longer exists, so they are GONE rather than re-derived. G12-002's originals were
        // RESTORED: GT-25/GT-43/GI-26 are back at Rs 197.75, the derivation that stood before the edit and
        // that the ungated reference reproduces exactly — evidence for the rule that a constant is never
        // edited to match the code.
        //
        // These subjects have NOT left the harness. They are still in the corpus, still replayed, still
        // compared, and still PRINTED by the reference-backed checks — as INFORMATIONAL lines that carry no
        // verdict. What is gone is the pretence that a number could be justified for them.
        // ===============================================================================================

        // G16-001 (count on the SOLVENT godown) and G16-002 (count on the SHORT godown) are the two
        // directions of that unjudgeable case and are covered by the note above. Both remain in the
        // corpus; their shapes are recorded in Corpus.cs and the measured spreads in tools/HeadOracle/
        // README.md. For the record, the numbers the three candidate models produce on G16-002 — a book
        // that spent Rs 1,001.30 and reports 18 units on hand — are Rs 0.00 (HEAD and the flattened
        // replay), and Rs 1,802.34 (the honest per-key figure that neither model computes).
    ];

    /// <summary>
    /// THE ISSUE-VALUE TABLE (audit #5 finding [0], HIGH). Same discipline, same failure mode, asserted
    /// against <c>RefIssueValue@&lt;Probe&gt;Paisa</c>.
    /// <para>THE TWO ARMS BEHAVE DIFFERENTLY AND BOTH ARE PINNED. Fifo/Lifo walk the SURVIVING layers and
    /// STOP when the stack is exhausted, so an issue can never cost more than the closing value — the units
    /// a repayment consumed went to COGS when the debt was settled and are not there to sell. The average
    /// arm issues at the closing unit rate (the standard cost when there is no stock) and is NOT capped,
    /// which is why the comparator's structural assertion cannot reach it and constants must.</para>
    /// </summary>
    public static readonly IReadOnlyList<Golden> Issue =
    [
        // ---------------------------------------------------- THE CRUX, all three arms
        new("GI-01", "G1-001", "Widget", "Fifo", "2024-04-20", 2769, DebtClause.IssuePostRecovery,
            "the stack after recovery is 25@7.91; issue 3.5 -> 3.5 x 7.91 = 27.685 -> away-from-zero 27.69",
            Probe: "3.5"),
        new("GI-02", "G1-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.IssuePostRecovery,
            "issue EXACTLY the on-hand 25 -> the whole stack -> 25 x 7.91 = 197.75", Probe: "25"),
        new("GI-03", "G1-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.IssueAcrossRepaid,
            "issue 1000 against 25 on hand: the walk STOPS at the stack. The 15 units the 7.91 lot repaid "
            + "are already COGS and are NOT available again, so this must be the closing value: 25 x 7.91 = 197.75",
            Probe: "1000"),
        new("GI-04", "G1-001", "Widget", "Lifo", "2024-04-20", 19775, DebtClause.IssueAcrossRepaid,
            "one layer only, so LIFO stops at the same place: 25 x 7.91 = 197.75", Probe: "1000"),
        new("GI-05", "G1-001", "Widget", "AverageCost", "2024-04-20", 2769, DebtClause.IssueAverage,
            "closing 197.75 over 25 units -> rate 7.91; issue 3.5 -> 3.5 x 7.91 = 27.685 -> 27.69", Probe: "3.5"),
        new("GI-06", "G1-001", "Widget", "AverageCost", "2024-04-20", 791000, DebtClause.IssueAverage,
            "the average arm is NOT capped by the stack: rate 7.91 x 1000 = 7910.00", Probe: "1000"),

        // ---------------------------------------------------- an issue WHILE A DEBT IS OUTSTANDING
        new("GI-07", "G2-001", "Widget", "Fifo", "2024-04-17", 0, DebtClause.IssueUnderDebt,
            "at d17 the In 12@7.91 has repaid only 12 of the debt 25, so debt 13 REMAINS and NO layer exists; "
            + "nothing can be issued: 0", Probe: "1.25"),
        new("GI-10", "G3-001", "Widget", "Fifo", "2024-04-20", 0, DebtClause.IssueUnderDebt,
            "debt 10 still outstanding and no layer survives under a debt, so an issue costs exactly 0", Probe: "3.5"),
        new("GI-11", "G3-001", "Widget", "AverageCost", "2024-04-20", 3420, DebtClause.IssueAverage,
            "on-hand is -10 so closing qty is 0; with no stock the average arm issues at the STANDARD COST: "
            + "3.5 x 9.77 = 34.195 -> away-from-zero 34.20", Probe: "3.5"),

        // ---------------------------------------------------- an issue drawn ACROSS a repaid layer
        new("GI-08", "G2-001", "Widget", "Fifo", "2024-04-25", 1509, DebtClause.IssuePostRecovery,
            "after full recovery the stack is 7@12.07 — the ENTIRE 12@7.91 lot went to the debt. Issue 1.25 at "
            + "12.07 (NOT at 7.91): 1.25 x 12.07 = 15.0875 -> 15.09", Probe: "1.25"),
        new("GI-09", "G2-001", "Widget", "Fifo", "2024-04-25", 8449, DebtClause.IssueAcrossRepaid,
            "issue 500 against 7 on hand: the 12@7.91 lot was entirely consumed by the debt and must not "
            + "reappear, so the answer is the whole surviving stack: 7 x 12.07 = 84.49", Probe: "500"),

        // ---------------------------------------------------- POST-RECOVERY COGS (right BS, wrong P&L)
        new("GI-12", "G1-003", "Widget", "Fifo", "2024-04-25", 989, DebtClause.IssuePostRecovery,
            "debt 15 repaid by In 40@7.91 leaving 25, then Out 9.25 leaves 15.75@7.91; issue 1.25 -> "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        new("GI-13", "G1-003", "Widget", "Fifo", "2024-04-25", 12458, DebtClause.IssueAcrossRepaid,
            "issue 500 against 15.75 on hand -> the whole stack: 15.75 x 7.91 = 124.5825 -> 124.58", Probe: "500"),
        new("GI-14", "G8-003", "Widget", "Fifo", "2024-04-25", 11076, DebtClause.IssuePostRecovery,
            "the OVERSTATEMENT direction: debt 18 repaid at the DEAR 88.61 leaving 27, then Out 11.5 leaves "
            + "15.5@88.61; issue 1.25 -> 1.25 x 88.61 = 110.7625 -> 110.76", Probe: "1.25"),
        new("GI-15", "G8-003", "Widget", "Fifo", "2024-04-25", 137346, DebtClause.IssuePostRecovery,
            "issue EXACTLY the on-hand 15.5 -> 15.5 x 88.61 = 1373.455 -> away-from-zero (an EXACT midpoint) "
            + "-> 1373.46", Probe: "15.5"),
        new("GI-23", "G10-002", "Widget", "Fifo", "2024-04-25", 989, DebtClause.IssuePostRecovery,
            "an OPENING-lot debt of 6.5 repaid by In 25@7.91, then Out 4.5 leaves 14@7.91; issue 1.25 -> "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),

        // ---------------------------------------------------- a COUNT taken with a DEBT OUTSTANDING
        // GI-16/GI-17/GI-18 are valid under Goldens.SettledRule, quoted at the top of this class.
        new("GI-16", "G6-001", "Widget", "Fifo", "2024-04-20", 7816, DebtClause.IssueAcrossRepaid,
            "the count wrote the debt off and left 8@9.77; issue 100 stops at the stack: 8 x 9.77 = 78.16",
            Probe: "100"),
        new("GI-17", "G6-001", "Widget", "Fifo", "2024-04-20", 3420, DebtClause.IssuePostRecovery,
            "issue 3.5 of the 8 counted units at the chain's 9.77: 3.5 x 9.77 = 34.195 -> 34.20", Probe: "3.5"),
        new("GI-18", "G6-001", "Widget", "AverageCost", "2024-04-20", 97700, DebtClause.IssueAverage,
            "closing 78.16 over 8 units -> rate 9.77; the average arm is uncapped: 9.77 x 100 = 977.00",
            Probe: "100"),

        // ---------------------------------------------------- repayment by an UNRATED inward
        // GI-19/GI-20 are valid under Goldens.SettledRule, quoted at the top of this class.
        new("GI-19", "G7-002", "Widget", "Fifo", "2024-04-20", 250325, DebtClause.IssueAcrossRepaid,
            "no standard cost, so the chain priced the unrated lot at the LAST RATED INWARD 100.13; issue 25 "
            + "= the whole stack: 25 x 100.13 = 2503.25", Probe: "25"),
        new("GI-20", "G7-001", "Widget", "Lifo", "2024-04-20", 3420, DebtClause.IssuePostRecovery,
            "the chain priced the unrated lot at the STANDARD COST 9.77; issue 3.5 -> 3.5 x 9.77 = 34.195 -> 34.20",
            Probe: "3.5"),

        // ---------------------------------------------------- the AverageCost crux, wide rate spread
        new("GI-21", "G2-004", "Widget", "AverageCost", "2024-04-25", 18500, DebtClause.IssueAverage,
            "closing 11.10 over 30 units -> rate 0.37; uncapped: 0.37 x 500 = 185.00", Probe: "500"),

        // ---------------------------------------------------- accumulated debt / debt from an empty stack
        new("GI-22", "G4-001", "Widget", "Fifo", "2024-04-25", 24917, DebtClause.IssueAcrossRepaid,
            "debt accumulated to 28.5 across two over-draws and was repaid by In 60@7.91 leaving 31.5; issue "
            + "1000 stops at the stack: 31.5 x 7.91 = 249.165 -> 249.17", Probe: "1000"),
        new("GI-24", "G5-002", "Widget", "Fifo", "2024-04-12", 5007, DebtClause.IssuePostRecovery,
            "Out 1000 from empty created debt 1000, repaid by In 1001@100.13 leaving 1 unit; issue 0.5 -> "
            + "0.5 x 100.13 = 50.065 -> away-from-zero (an EXACT midpoint) -> 50.07", Probe: "0.5"),
        new("GI-25", "G5-002", "Widget", "Fifo", "2024-04-12", 10013, DebtClause.IssueAcrossRepaid,
            "issue 5 against 1 on hand: the 1000 repaid units are COGS, not stock, so the walk stops at "
            + "1 x 100.13 = 100.13", Probe: "5"),
        // ROUND 11 RE-DERIVED to 31640 with GT-25 — the multi-key residual. The item lives on two BATCHES,
        // so no debt is ever created, the shortfall of 15 is discarded and the whole 40-unit lot is stock.
        // The probe of 300 reaches all of it, which is 40 units against a reported closing quantity of 25.
        new("GI-26", "G12-002", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.IssueAcrossRepaid,
            "batch-blind: the B-B recovery repaid the B-A debt; issue 300 stops at 25 x 7.91 = 197.75",
            Probe: "300"),

        // ---------------------------------------------------- CONTROLS: FIFO takes the OLDEST, LIFO the NEWEST
        // ROUND 10. G9-002's merged layer stack NEVER runs dry, so the book carries no debt at all and
        // these are genuine no-debt controls again (round 9 had retagged them post-recovery because a
        // per-key replay gave godown 1 a debt of its own). The 1.25 constants are unchanged to the paisa
        // through both rounds, which is worth having on the record.
        new("GI-27", "G9-002", "Widget", "Fifo", "2024-04-25", 1291, DebtClause.IssueControl,
            "the surviving stack in ARRIVAL order is 14.75@10.33 (d4) then 12@7.91 (d14), so an issue of "
            + "1.25 comes off the OLDEST: 1.25 x 10.33 = 12.9125 -> 12.91", Probe: "1.25"),
        new("GI-28", "G9-002", "Widget", "Lifo", "2024-04-25", 989, DebtClause.IssueControl,
            "the LIFO stack is 21.25@10.33 then 5.5@7.91 and the issue comes off the NEWEST: "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        // ROUND 10 RESTORED to 26302 (round 9 had re-derived it to 26846 per key).
        new("GI-29", "G9-002", "Widget", "Lifo", "2024-04-25", 26302, DebtClause.IssueControl,
            "issue 300 against a surviving stack of 26.75 -> the whole stack: 21.25 x 10.33 + 5.5 x 7.91 = "
            + "219.5125 + 43.505 = 263.0175 -> 263.02", Probe: "300"),

        // ---------------------------------------------------- EVERY INVENTED SUBJECT, ON THE P&L SIDE TOO
        // The nine subjects resting on Goldens.SettledRule are pinned on the Balance Sheet by GT-11..GT-16A.
        // These five complete the set on the ISSUE side, so the comparator can require BOTH — a rule nothing
        // calibrates must not be anchored on closing value alone, which is the whole shape of finding [0].
        new("GI-30", "G6-001", "Widget", "Lifo", "2024-04-20", 7816, DebtClause.IssueAcrossRepaid,
            "LIFO sees the same single counted layer 8@9.77; issue 100 stops at the stack: 8 x 9.77 = 78.16",
            Probe: "100"),
        new("GI-31", "G7-001", "Widget", "Fifo", "2024-04-20", 24425, DebtClause.IssueAcrossRepaid,
            "the unrated lot was priced at the STANDARD COST 9.77 and 25 survived the repayment; issue 25 = "
            + "the whole stack: 25 x 9.77 = 244.25", Probe: "25"),
        new("GI-32", "G7-001", "Widget", "AverageCost", "2024-04-20", 3420, DebtClause.IssueAverage,
            "closing 244.25 over 25 units -> rate 9.77; issue 3.5 -> 3.5 x 9.77 = 34.195 -> 34.20", Probe: "3.5"),
        new("GI-33", "G7-002", "Widget", "Lifo", "2024-04-20", 250325, DebtClause.IssueAcrossRepaid,
            "no standard cost, so the chain reached the LAST RATED INWARD 100.13; issue 25 = the whole stack: "
            + "25 x 100.13 = 2503.25", Probe: "25"),
        new("GI-34", "G7-002", "Widget", "AverageCost", "2024-04-20", 35046, DebtClause.IssueAverage,
            "closing 2503.25 over 25 units -> rate 100.13; issue 3.5 -> 3.5 x 100.13 = 350.455 -> 350.46",
            Probe: "3.5"),

        // ---------------------------------------------------- ROUND 7 / AUDIT #6 LOW [2]
        // THE P&L HALF OF THE FIFO/LIFO DIVERGENCE. See the G15-001 block in Goldens.All for the replay.
        // The surviving stacks at d25 hold the SAME 32 units and differ only in which end the d22 outward
        // ate, so these six constants are the first in the corpus where the LIFO issue walk cannot be
        // satisfied by the FIFO answer.
        //   FIFO stack: 12@7.91 then 20@12.07        LIFO stack: 25@7.91 then 7@12.07
        new("GI-35", "G15-001", "Widget", "Fifo", "2024-04-25", 989, DebtClause.IssuePostRecovery,
            "an issue of 1.25 off the FIFO stack comes from the OLDEST layer, which is the 7.91 one: "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        new("GI-36", "G15-001", "Widget", "Lifo", "2024-04-25", 1509, DebtClause.IssuePostRecovery,
            "the SAME issue of 1.25 off the LIFO stack comes from the NEWEST layer, which is the 12.07 one — "
            + "this is the pair GI-35/GI-36 that makes the LIFO branch falsifiable: "
            + "1.25 x 12.07 = 15.0875 -> 15.09", Probe: "1.25"),
        new("GI-37", "G15-001", "Widget", "Fifo", "2024-04-25", 33632, DebtClause.IssuePostRecovery,
            "issue EXACTLY the on-hand 32 -> the whole FIFO stack: 12 x 7.91 + 20 x 12.07 = "
            + "94.92 + 241.40 = 336.32", Probe: "32"),
        new("GI-38", "G15-001", "Widget", "Lifo", "2024-04-25", 28224, DebtClause.IssuePostRecovery,
            "issue EXACTLY the same on-hand 32 -> the whole LIFO stack, the same 32 units carrying "
            + "different rates: 25 x 7.91 + 7 x 12.07 = 197.75 + 84.49 = 282.24", Probe: "32"),
        new("GI-39", "G15-001", "Widget", "Fifo", "2024-04-25", 33632, DebtClause.IssueAcrossRepaid,
            "issue 500 against 32 on hand: the 15 units the 7.91 lot handed to the debt went to COGS at d15 "
            + "and cannot be sold again, so the FIFO walk stops at the stack: 336.32", Probe: "500"),
        new("GI-40", "G15-001", "Widget", "Lifo", "2024-04-25", 28224, DebtClause.IssueAcrossRepaid,
            "the same cap on the LIFO side, at the LIFO stack's own value: 282.24", Probe: "500"),

        // THE CROSSOVER PAIR. At d20 the d22 outward has not happened yet, so BOTH layers are whole —
        // [25@7.91, 20@12.07], on-hand 45 — and an issue of 32 has to cut THROUGH a layer boundary from
        // whichever end its method starts at. That makes GI-43/GI-44 the only constants in the corpus where
        // a partial multi-layer draw is pinned in both directions on a debt book.
        // Note what the two dates produce: FIFO at d20 = 282.24 = LIFO at d25, and LIFO at d20 = 336.32 =
        // FIFO at d25. The SAME two numbers appear under OPPOSITE methods, so an engine that merely swapped
        // FIFO for LIFO reproduces both values and is caught only because the DATES do not match.
        new("GI-41", "G15-001", "Widget", "Fifo", "2024-04-20", 989, DebtClause.IssuePostRecovery,
            "both layers whole; FIFO starts at the OLDEST: 1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        new("GI-42", "G15-001", "Widget", "Lifo", "2024-04-20", 1509, DebtClause.IssuePostRecovery,
            "the same book and the same probe; LIFO starts at the NEWEST: 1.25 x 12.07 = 15.0875 -> 15.09",
            Probe: "1.25"),
        new("GI-43", "G15-001", "Widget", "Fifo", "2024-04-20", 28224, DebtClause.IssuePostRecovery,
            "32 of the 45 on hand, FIFO: the whole 25@7.91 layer then 7 of the 12.07 one — "
            + "25 x 7.91 = 197.75 plus 7 x 12.07 = 84.49, so 197.75 + 84.49 = 282.24", Probe: "32"),
        new("GI-44", "G15-001", "Widget", "Lifo", "2024-04-20", 33632, DebtClause.IssuePostRecovery,
            "32 of the same 45 on hand, LIFO: the whole 20@12.07 layer then 12 of the 7.91 one — "
            + "20 x 12.07 = 241.40 plus 12 x 7.91 = 94.92, so 241.40 + 94.92 = 336.32", Probe: "32"),

        // ================================================================ ROUND 8 — THE P&L HALF
        // The coverage rule requires BOTH sides for every INVENTED subject: a rule nothing calibrates,
        // anchored on the Balance Sheet alone, is the exact shape audit #5 found. See the ROUND 8 block in
        // Goldens.All for the four books and why the corpus could not reach the defect before.

        // ---------------------------------------------------- G6-003 (count with a debt, then a purchase)
        new("GI-45", "G6-003", "Widget", "Fifo", "2024-04-16", 80104, DebtClause.IssueAcrossRepaid,
            "issue 500 against 8 on hand: the 10 units the Out 25 drained went to COGS at d10 and are not "
            + "there to sell again, so the walk stops at the counted stack: 8 x 100.13 = 801.04", Probe: "500"),
        new("GI-46", "G6-003", "Widget", "Lifo", "2024-04-16", 80104, DebtClause.IssueAcrossRepaid,
            "one layer only, so the LIFO walk stops at the same place: 8 x 100.13 = 801.04", Probe: "500"),
        new("GI-47", "G6-003", "Widget", "AverageCost", "2024-04-16", 12516, DebtClause.IssueAverage,
            "closing 801.04 over 8 units gives the snapped rate 100.13; issue 1.25 -> 1.25 x 100.13 = "
            + "125.1625 -> away-from-zero 125.16", Probe: "1.25"),
        new("GI-48", "G6-003", "Widget", "Fifo", "2024-04-20", 100080107, DebtClause.IssueAcrossRepaid,
            "issue EXACTLY the on-hand 9 -> the whole stack, the 8 counted units at their COUNT-DATE rate "
            + "plus the d18 lot: 8 x 100.13 + 1 x 1000000.03 = 1000801.07", Probe: "9"),
        new("GI-49", "G6-003", "Widget", "Lifo", "2024-04-20", 100080107, DebtClause.IssueAcrossRepaid,
            "the same 9 units taken from the other end of the stack, so the same total: 1000801.07",
            Probe: "9"),
        new("GI-50", "G6-003", "Widget", "AverageCost", "2024-04-20", 100080108, DebtClause.IssueAverage,
            "closing 1000801.07 over 9 units is 111200.1188 recurring, which SNAPS to a rate of 111200.12, "
            + "and the average arm multiplies the SNAPPED rate: issuing exactly the on-hand 9 therefore costs "
            + "111200.12 x 9 = 1000801.08 — one paisa ABOVE the closing value. That is the honest consequence "
            + "of the snap, and it is pinned here so a rounding-mode change on this arm cannot ship green",
            Probe: "9"),

        // ---------------------------------------------------- G6-004 (the count write-off, then 40 units)
        new("GI-51", "G6-004", "Widget", "Fifo", "2024-04-25", 1221, DebtClause.IssuePostRecovery,
            "the FIFO stack is 8@9.77 (the counted units) then 40@7.91 (the recovery lot); an issue of 1.25 "
            + "comes off the OLDEST layer, which is the COUNTED one: 1.25 x 9.77 = 12.2125 -> 12.21",
            Probe: "1.25"),
        new("GI-52", "G6-004", "Widget", "Lifo", "2024-04-25", 989, DebtClause.IssuePostRecovery,
            "the pair with GI-51 that makes the LIFO branch falsifiable on a count-with-debt book: the SAME "
            + "probe on the SAME book comes off the NEWEST layer, the recovery lot: 1.25 x 7.91 = 9.8875 "
            + "-> 9.89", Probe: "1.25"),
        new("GI-53", "G6-004", "Widget", "AverageCost", "2024-04-25", 1028, DebtClause.IssueAverage,
            "closing 394.56 over 48 units gives the rate 8.22; issue 1.25 -> 1.25 x 8.22 = 10.275, an EXACT "
            + "midpoint, so away-from-zero gives 10.28", Probe: "1.25"),

        // ---------------------------------------------------- G6-005 (the count DOWN to exactly zero)
        new("GI-54", "G6-005", "Widget", "Fifo", "2024-04-25", 989, DebtClause.IssuePostRecovery,
            "the count to zero left the stack EMPTY, so the only layer is the 21.25@7.91 recovery lot and an "
            + "issue of 1.25 can only come from it: 1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        new("GI-55", "G6-005", "Widget", "Lifo", "2024-04-25", 16809, DebtClause.IssueAcrossRepaid,
            "issue EXACTLY the on-hand 21.25: the 13.75 units the over-draw drained are COGS and the count "
            + "wrote the remaining shortfall off, so the walk stops at the single surviving layer: "
            + "21.25 x 7.91 = 168.0875 -> 168.09", Probe: "21.25"),
        new("GI-56", "G6-005", "Widget", "AverageCost", "2024-04-25", 237300, DebtClause.IssueAverage,
            "the average arm is NOT capped by the 21.25 on hand, which is why constants and not the "
            + "structural assertion have to pin it: rate 7.91 x 300 = 2373.00", Probe: "300"),

        // ---------------------------------------------------- G7-003 (unrated inward, then a purchase)
        new("GI-57", "G7-003", "Widget", "Fifo", "2024-04-16", 250325, DebtClause.IssueAcrossRepaid,
            "issue 26 against 25 on hand: the 15 units the unrated lot handed to the debt went to COGS at "
            + "d15, so the walk stops at the surviving stack: 25 x 100.13 = 2503.25", Probe: "26"),
        new("GI-58", "G7-003", "Widget", "Lifo", "2024-04-16", 250325, DebtClause.IssueAcrossRepaid,
            "one layer only, so the LIFO walk stops at the same place: 25 x 100.13 = 2503.25", Probe: "26"),
        new("GI-59", "G7-003", "Widget", "AverageCost", "2024-04-16", 12516, DebtClause.IssueAverage,
            "closing 2503.25 over 25 units gives the rate 100.13; issue 1.25 -> 1.25 x 100.13 = 125.1625 "
            + "-> 125.16", Probe: "1.25"),
        new("GI-60", "G7-003", "Widget", "Fifo", "2024-04-20", 12516, DebtClause.IssuePostRecovery,
            "both layers whole at d20; FIFO starts at the OLDEST, which is the unrated lot priced at its own "
            + "arrival-date rate: 1.25 x 100.13 = 125.1625 -> 125.16", Probe: "1.25"),
        new("GI-61", "G7-003", "Widget", "Lifo", "2024-04-20", 1002502, DebtClause.IssuePostRecovery,
            "the SAME probe from the NEWEST end takes the whole 1@9999.99 lot and then 0.25 of the 100.13 "
            + "one: 9999.99 + 0.25 x 100.13 = 9999.99 + 25.0325 = 10025.0225 -> 10025.02", Probe: "1.25"),
        new("GI-62", "G7-003", "Widget", "AverageCost", "2024-04-20", 1250314, DebtClause.IssueAverage,
            "the mirror image of the rate snap pinned on the closing side of G6-003, in the opposite "
            + "direction. Closing 12503.24 over 26 units is 480.8938 recurring, which SNAPS to a rate of "
            + "480.89, and the average arm multiplies the SNAPPED rate: issuing exactly the on-hand 26 "
            + "therefore costs 480.89 x 26 = 12503.14 — ten paisa BELOW the closing value", Probe: "26"),

        // ================================================================ THE MULTI-KEY P&L HALF
        // These pin the ISSUE arm of the multi-key families where NO KEY IS EVER NEGATIVE, so no debt can
        // exist under any scoping and the issue:no-debt-control tag is true of its subject. The multi-key
        // subjects that DO go short carry no issue golden, for the reason given in the closing table.

        // ---------------------------------------------------- N10-002 (two godowns, no count, no debt)
        new("GI-70", "N10-002", "Widget", "Fifo", "2024-04-20", 12516, DebtClause.IssueControl,
            "no key ever goes short, so nothing is owed anywhere; the FIFO stack is 1.25@100.13 (g0, d5) "
            + "then 20@7.91 (g1, d6) and the issue takes the OLDEST: 1.25 x 100.13 = 125.1625 -> 125.16",
            Probe: "1.25"),
        new("GI-71", "N10-002", "Widget", "Lifo", "2024-04-20", 989, DebtClause.IssueControl,
            "the LIFO stack is 13.75@100.13 then 7.5@7.91 and the issue takes the NEWEST: 1.25 x 7.91 = "
            + "9.8875 -> 9.89", Probe: "1.25"),

        // ---------------------------------------------------- N11-001 (godowns AND batches)
        new("GI-65", "N11-001", "Widget", "Lifo", "2024-04-20", 989, DebtClause.IssueControl,
            "no key ever owes a unit; the LIFO stack is 13.75@100.13 (B-A, d4) then 2.75@7.91 (B-B, d5, "
            + "what its own outward left after LIFO had eaten the g1 lot), and the issue takes the NEWEST: "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),

        // G16-001 and G16-002 carry no issue golden either: the issue arm walks the same surviving layers
        // the closing arm values, so a constant here would rest on the same unjustifiable closing figure.
    ];

    /// <summary>Every clause tag that must appear at least once across BOTH tables.</summary>
    public static readonly IReadOnlyList<string> RequiredClauses =
    [
        DebtClause.RepayRated, DebtClause.RepayMultiLot, DebtClause.RepayUnrated,
        DebtClause.CountWithDebt, DebtClause.CountAfterRepay, DebtClause.DebtOutstanding,
        DebtClause.DebtFromEmpty, DebtClause.DebtAccumulated, DebtClause.AverageDebt,
        DebtClause.NoDebtControl,
        // ROUND 6 — the issue arm. Each of these must be pinned by at least one constant, so the branch
        // audit #5 finding [0] demonstrated can never again be judged with nothing behind it.
        DebtClause.IssueUnderDebt, DebtClause.IssuePostRecovery, DebtClause.IssueAcrossRepaid,
        DebtClause.IssueAverage, DebtClause.IssueControl,
    ];

    /// <summary>
    /// A STABLE digest over the CONSTANTS THEMSELVES — audit #5 finding [1] (MEDIUM). It is recorded as a
    /// census cell, so editing any constant (the one shortcut this file forbids) changes a recorded number
    /// and has to be justified in Census.cs's log, which is where a reviewer actually looks.
    /// <para>FNV-1a over the ordered tuples, folded to a positive int because a census cell is an int. Not
    /// <c>string.GetHashCode</c>, which is randomised per process and would make the cell meaningless.
    /// The <c>Working</c> prose is deliberately EXCLUDED so re-wording a derivation is not a census event;
    /// prose is tied to the constant by the separate Working-consistency assertion in the comparator.</para>
    /// </summary>
    public static int Digest()
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var g in All.Concat(Issue))
            {
                var s = string.Join('|', [g.Id, g.Scenario, g.Item, g.Method, g.AsOf, g.Probe,
                    g.Paisa.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Clause]) + "\n";
                foreach (var ch in s) { h ^= ch; h *= 1099511628211UL; }
            }
            return (int)((h ^ (h >> 32)) & 0x7FFFFFFFUL);
        }
    }
}
