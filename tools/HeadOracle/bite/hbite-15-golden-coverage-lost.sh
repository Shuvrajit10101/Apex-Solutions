# HARNESS INTEGRITY — A GOLDEN QUIETLY REMOVED, LEAVING AN INVENTED SUBJECT UNPINNED.
#
# CHECK 4c is the ONLY validation the reference's debt branch has, so the way it fails in practice is not
# a wrong constant — it is a MISSING one. Someone adds a corpus scenario that reaches the count-with-debt
# or unrated-repayment path, no golden is written for it, and the table still prints "32 goldens, 0
# mismatches, PASS" while a brand-new subject is judged by nothing at all. That is exactly how the CHECK 4b
# hole existed: a silent `continue` over a subject nobody had pinned.
#
# This simulates it in the only direction a mutation can: delete GT-11, the golden pinning
# G6-001/Widget/Fifo/2024-04-20 (a physical count taken with a debt outstanding — INVENTED provenance).
# The COVERAGE assertion must convict, even though every REMAINING golden still passes.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Goldens.cs" \
'        new("GT-11", "G6-001", "Widget", "Fifo", "2024-04-20", 7816, DebtClause.CountWithDebt,
            "In 10@100.13 -> Out 25 (debt 15) -> Count 8: debt written off, stack empty, 8 topped up by the chain "
            + "(running avg 0 -> STANDARD COST 9.77): 8 x 9.77 = 78.16"),' \
'        // <-- POISON: GT-11 deleted. G6-001/Widget/Fifo/2024-04-20 is now pinned by nothing.'
