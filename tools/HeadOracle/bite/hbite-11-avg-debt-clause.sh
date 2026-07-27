# HARNESS INTEGRITY — A POISON CONFINED TO THE AverageCost DEBT-REPAYMENT CLAUSE.
#                      (audit #4 finding [0], CRITICAL — the recursion CHECK 4c terminates)
#
# THE MISTAKE: drop `add -= repay`, so the units that repaid the debt ALSO join the pool. It is the most
# natural slip there is in that clause — the repayment is recorded, the deduction is forgotten.
#
# WHY NOTHING BEFORE CHECK 4c COULD SEE IT:
#   * CHECK 4 calibrates on never-negative books; `debt` is never > 0 there, so this line is DEAD CODE.
#   * CHECK 4b does the same, one column over. Same blindness, by construction.
#   * the old "REFERENCE INTERNAL CONSISTENCY" gate compared two calls to the SAME function, so both
#     columns moved together and it printed PASS (that gate is now retired — finding [2]).
#   * the REFERENCE VALUE INVARIANT only audits Fifo/Lifo layer breakdowns, which this does not touch.
#
# AND IT IS NOT A HARMLESS SLIP. On G2-004 it makes the reference demand EXACTLY HEAD's 1200750p — the
# Rs 11,996.40 phantom asset this whole exercise exists to convict. A poisoned harness would print
# CHECK 2 PASS on that subject and ACQUIT the defect. Only the hand-derived golden GT-07 = 1110p can say
# otherwise, because only it is anchored OUTSIDE the code.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        var repay = Math.Min(debt, add);
                        debt -= repay;
                        add -= repay;               // repaid units are COGS, not pool' \
'                        var repay = Math.Min(debt, add);
                        debt -= repay;
                        // <-- POISON: `add -= repay;` deleted. The repaid units join the pool as well.'
