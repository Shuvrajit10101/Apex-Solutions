# HARNESS INTEGRITY — THE ADVERSARY'S OWN MUTATION FROM AUDIT #5 FINDING [0] (HIGH), RE-RUN.
#
# THE MISTAKE: make the reference's ISSUE arm debt-aware in the wrong way — when the book ever carried a
# debt, price the issue at the debt-aware POOL AVERAGE instead of walking the surviving layers.
#
# WHY IT IS THE RIGHT SHAPE TO TEST. Reference.IssueValue's Fifo/Lifo branch contains NO debt-conditioned
# clause today, so the loop is fully exercised by calibration and the poison has to INTRODUCE the
# condition — which is exactly what happened to the AverageCost arm between rounds 3 and 4. The exposure
# was latent, not live; it goes live the day anybody makes that arm debt-aware.
#
# WHAT IT DID TO THE ROUND-5 HARNESS (the adversary's fresh output, reproduced here):
#   CHECK 4  mismatches : 0 => PASS      (never-negative books have no debt: the clause is dead code)
#   CHECK 4b mismatches : 0 => PASS      (same blindness, one column over)
#   CHECK 4c goldens evaluated 32 / mismatches : 0 => PASS   (round 5 pinned CLOSING values ONLY)
#   HARNESS INTEGRITY : SOUND, comparator exit 1 — NOT 3
# ...while 68 of the 120 reported CHECK 10 demands moved. On the crux, G1-001 Widget Fifo 2024-04-20
# IssueValue@1000 went from REFERENCE=19775 (Rs 197.75, the whole 25-unit stack) to REFERENCE=791000
# (Rs 7,910.00 — 40x, and it silently drops the stock cap, demanding COGS for 1000 units against a stack
# of 25). A builder whose Balance Sheet was right and whose P&L was wrong would have been CERTIFIED.
#
# WHAT MUST NOW HAPPEN. Two independent things convict it, and either alone is enough:
#   * CHECK 4c's ISSUE goldens (GI-01..GI-29) — literal constants on the RefIssueValue columns;
#   * the CONSTANT-FREE structural assertion — a probe at or above on-hand must cost EXACTLY the closing
#     value, because the units a repayment settled went to COGS when it was settled.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'        var events = Events(s, item, asOf);
        var st = BuildStack(events, lifo: method == Lifo, ChainFor(item, events));

        var remaining = quantity;
        var consumed = 0m;' \
'        var events = Events(s, item, asOf);
        var poisonChain = ChainFor(item, events);
        var st = BuildStack(events, lifo: method == Lifo, poisonChain);

        // <-- POISON (audit #5 finding [0]): a plausible "make the issue arm debt-aware too" change.
        // Once a debt has existed, price the issue at the debt-aware pool average and stop walking the
        // layers. It is internally consistent, it uses only rates the spec contains, and it drops the
        // stock cap entirely.
        if (st.DebtEverCreated)
            return Paisa(RunAverageDebtAware(events, poisonChain).Average * quantity);

        var remaining = quantity;
        var consumed = 0m;'
