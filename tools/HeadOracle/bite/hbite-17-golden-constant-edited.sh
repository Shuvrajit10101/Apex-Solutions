# HARNESS INTEGRITY — THE FORBIDDEN SHORTCUT: EDIT THE CONSTANT TO MATCH THE CODE.
#                      (audit #5 finding [1], MEDIUM)
#
# Goldens.cs says, in its own text, "fix the reference, never the constant". Until round 6 the census
# pinned `CHECK4c.goldens = <count>` — how MANY constants there were, never what they SAID — so this was
# the one shortcut with a red gate in front of it and no mechanical obstacle behind it.
#
# THIS SCRIPT DOES EXACTLY WHAT A BUILDER FACING A RED CHECK 4c WOULD BE TEMPTED TO DO, IN TWO PARTS:
#   1. it re-applies hbite-12's reference poison (a count-up taken with a debt outstanding is re-priced at
#      chain.LastRatedInward instead of the best-available-cost chain), which makes the reference demand
#      Rs 801.04 on G6-001 where the hand derivation says Rs 78.16 — audit #4's 10.25x crux fabrication;
#   2. it then EDITS EVERY G6-001 CONSTANT — GT-11, GT-11L, GT-12 (closing) and GI-16, GI-17, GI-18
#      (issue) — and their printed hand derivations, so that they agree with the poisoned reference.
#
# AFTER PART 2 THE ROUND-5 GATES ARE ALL SATISFIED: the golden COUNT is unchanged (84 + 29), every
# INVENTED subject still carries a golden, every family and every clause is still exercised, CHECK 4c
# reports "mismatches : 0", and even round 6's prose-consistency assertion passes, because the derivations
# were rewritten alongside the constants. CHECK 4c PRINTS PASS OVER A FABRICATED ANCHOR.
#
# WHAT MUST CATCH IT: census cell CHECK4c.goldenDigest, an FNV-1a digest over the constants themselves.
# It is recorded in Census.cs, it moves the instant any constant changes, and a changed census cell is a
# HARNESS failure that has to be justified in the recording log — which is where a reviewer actually looks.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ---- PART 1: the reference poison (identical to hbite-12) -------------------------------------------
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                case Kind.Count:
                {
                    // A physical count is an ABSOLUTE statement of what is on the shelf, so it' \
'                case Kind.Count:
                {
                    var poisonDebt = st.Debt > 0m;   // <-- POISON scaffolding
                    // A physical count is an ABSOLUTE statement of what is on the shelf, so it'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        var (unit, src) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                        var add = e.Qty - current;' \
'                        var (unit, src) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                        if (poisonDebt && chain.LastRatedInward is { } poisonRate) unit = poisonRate;   // <-- POISON
                        var add = e.Qty - current;'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                case Kind.Count:
                {
                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = chain.NoRateCost(0m);' \
'                case Kind.Count:
                {
                    var poisonDebt = debt > 0m;   // <-- POISON scaffolding
                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = chain.NoRateCost(0m);
                    if (poisonDebt && chain.LastRatedInward is { } poisonRate) unit = poisonRate;   // <-- POISON'

# ---- PART 2: THE FORBIDDEN EDIT — every G6-001 constant moved to match the poisoned reference --------
# 8 x Rs 100.13 = Rs 801.04 (closing, and the at-or-above issue probe)
# 3.5 x Rs 100.13 = Rs 350.455 -> Rs 350.46 (the below-on-hand issue probe)
# 100 x Rs 100.13 = Rs 10,013.00 (the uncapped AverageCost issue probe)
python "$HERE/_patch.py" "$RUNNER/Goldens.cs" \
'new("GT-11", "G6-001", "Widget", "Fifo", "2024-04-20", 7816, DebtClause.CountWithDebt,
            "In 10@100.13 -> Out 25 (debt 15) -> Count 8: debt written off, stack empty, 8 topped up by the chain "
            + "(running avg 0 -> STANDARD COST 9.77): 8 x 9.77 = 78.16"),
        new("GT-11L", "G6-001", "Widget", "Lifo", "2024-04-20", 7816, DebtClause.CountWithDebt,
            "identical: the count reconciles to a total, not to a particular layer: 8 x 9.77 = 78.16"),
        new("GT-12", "G6-001", "Widget", "AverageCost", "2024-04-20", 7816, DebtClause.AverageDebt,
            "count writes the debt off; pool average is 0 so the chain answers 9.77; pool 8 x 9.77 = 78.16"),' \
'new("GT-11", "G6-001", "Widget", "Fifo", "2024-04-20", 80104, DebtClause.CountWithDebt,
            "In 10@100.13 -> Out 25 (debt 15) -> Count 8: 8 topped up at the last rated inward: 8 x 100.13 = 801.04"),
        new("GT-11L", "G6-001", "Widget", "Lifo", "2024-04-20", 80104, DebtClause.CountWithDebt,
            "identical: the count reconciles to a total: 8 x 100.13 = 801.04"),
        new("GT-12", "G6-001", "Widget", "AverageCost", "2024-04-20", 80104, DebtClause.AverageDebt,
            "count writes the debt off; the pool is restated at the last rated inward: 8 x 100.13 = 801.04"),'

python "$HERE/_patch.py" "$RUNNER/Goldens.cs" \
'new("GI-16", "G6-001", "Widget", "Fifo", "2024-04-20", 7816, DebtClause.IssueAcrossRepaid,
            "the count wrote the debt off and left 8@9.77; issue 100 stops at the stack: 8 x 9.77 = 78.16",
            Probe: "100"),
        new("GI-17", "G6-001", "Widget", "Fifo", "2024-04-20", 3420, DebtClause.IssuePostRecovery,
            "issue 3.5 of the 8 counted units at the chain'"'"'s 9.77: 3.5 x 9.77 = 34.195 -> 34.20", Probe: "3.5"),
        new("GI-18", "G6-001", "Widget", "AverageCost", "2024-04-20", 97700, DebtClause.IssueAverage,
            "closing 78.16 over 8 units -> rate 9.77; the average arm is uncapped: 9.77 x 100 = 977.00",
            Probe: "100"),' \
'new("GI-16", "G6-001", "Widget", "Fifo", "2024-04-20", 80104, DebtClause.IssueAcrossRepaid,
            "the count left 8@100.13; issue 100 stops at the stack: 8 x 100.13 = 801.04",
            Probe: "100"),
        new("GI-17", "G6-001", "Widget", "Fifo", "2024-04-20", 35046, DebtClause.IssuePostRecovery,
            "issue 3.5 of the 8 counted units at 100.13: 3.5 x 100.13 = 350.455 -> 350.46", Probe: "3.5"),
        new("GI-18", "G6-001", "Widget", "AverageCost", "2024-04-20", 1001300, DebtClause.IssueAverage,
            "closing 801.04 over 8 units -> rate 100.13; uncapped: 100.13 x 100 = 10013.00",
            Probe: "100"),'

python "$HERE/_patch.py" "$RUNNER/Goldens.cs" \
'new("GI-30", "G6-001", "Widget", "Lifo", "2024-04-20", 7816, DebtClause.IssueAcrossRepaid,
            "LIFO sees the same single counted layer 8@9.77; issue 100 stops at the stack: 8 x 9.77 = 78.16",
            Probe: "100"),' \
'new("GI-30", "G6-001", "Widget", "Lifo", "2024-04-20", 80104, DebtClause.IssueAcrossRepaid,
            "LIFO sees the same single counted layer 8@100.13; issue 100 stops at the stack: 8 x 100.13 = 801.04",
            Probe: "100"),'
