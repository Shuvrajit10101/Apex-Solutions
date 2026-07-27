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
        new("GT-25", "G12-002", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "valuation is batch-BLIND, so the B-B recovery repays the B-A debt company-wide: 25 x 7.91 = 197.75"),
        new("GT-26", "G14-001", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.RepayRated,
            "the CANCELLED Out 50 contributes nothing; the POST-DATED In 40@7.91 repays the debt 15 normally: 25 x 7.91 = 197.75"),

        // ---------------------------------------------------- the OVERSTATEMENT direction
        new("GT-28", "G8-002", "Widget", "Fifo", "2024-04-20", 239247, DebtClause.RepayRated,
            "In 12@3.17 -> Out 30 (debt 18) -> In 45@88.61 repays 18 at the DEAR rate, 27 survive: 27 x 88.61 = 2392.47"),

        // ---------------------------------------------------- CONTROL: the debt clauses must NOT fire
        new("GT-27", "G9-002", "Widget", "Fifo", "2024-04-25", 24729, DebtClause.NoDebtControl,
            "godown 1 goes to -8.75 but the COMPANY-WIDE stack never runs dry, so no debt exists: "
            + "14.75@10.33 + 12@7.91 = 152.3675 + 94.92 = 247.2875 -> 247.29"),

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
        new("GI-26", "G12-002", "Widget", "Fifo", "2024-04-20", 19775, DebtClause.IssueAcrossRepaid,
            "batch-blind: the B-B recovery repaid the B-A debt; issue 300 stops at 25 x 7.91 = 197.75",
            Probe: "300"),

        // ---------------------------------------------------- CONTROLS: FIFO takes the OLDEST, LIFO the NEWEST
        new("GI-27", "G9-002", "Widget", "Fifo", "2024-04-25", 1291, DebtClause.IssueControl,
            "no company-wide debt ever exists here; FIFO layers are 14.75@10.33 then 12@7.91, so an issue of "
            + "1.25 comes off the OLDEST: 1.25 x 10.33 = 12.9125 -> 12.91", Probe: "1.25"),
        new("GI-28", "G9-002", "Widget", "Lifo", "2024-04-25", 989, DebtClause.IssueControl,
            "LIFO layers are 21.25@10.33 then 5.5@7.91, so an issue of 1.25 comes off the NEWEST: "
            + "1.25 x 7.91 = 9.8875 -> 9.89", Probe: "1.25"),
        new("GI-29", "G9-002", "Widget", "Lifo", "2024-04-25", 26302, DebtClause.IssueControl,
            "issue 300 against 26.75 on hand -> the whole LIFO stack: 21.25 x 10.33 + 5.5 x 7.91 = "
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
