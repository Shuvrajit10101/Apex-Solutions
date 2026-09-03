# HARNESS INTEGRITY — POISON THE DEBT-AWARE AverageCost ORACLE.  (audit #3 finding [2], HIGH)
#
# This is the adversary's own mutation, re-run against the fix. It trebles the cost added to the
# debt-aware moving-average pool. Before CHECK 4b existed, this rewrote 148 of 184 reported magnitudes —
# inventing AverageCost "defects" on books that NEVER GO NEGATIVE (N1-002, N5-001, E1-001) and moving the
# headline G2-004 figure — while PART A still printed "HARNESS INTEGRITY : SOUND", because CHECK 4's
# calibration derives its engine twin by stripping the "Ref" prefix and RefClosingValueDebtAwarePaisa
# maps to ClosingValueDebtAwarePaisa, which no engine emits, so the lookup silently `continue`d.
#
# That column is now the oracle CHECK 2 CONVICTS HEAD WITH, so it cannot stay unvalidated.
# CHECK 4b must convict this: a never-negative book carries NO DEBT, so every clause that distinguishes
# RunAverageDebtAware from RunAverage is dead code there and the two MUST agree with HEAD exactly.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    if (add > 0m)
                    {
                        qty += add;
                        cost += add * unit;
                    }' \
'                    if (add > 0m)
                    {
                        qty += add;
                        cost += add * unit * 3m;   // <-- POISON: treble the debt-aware pool cost
                    }'
