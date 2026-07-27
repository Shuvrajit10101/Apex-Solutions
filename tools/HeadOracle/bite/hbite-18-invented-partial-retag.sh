# HARNESS INTEGRITY — A PARTIAL RETAG SHRINKS THE POPULATION CHECK 4c CLAIMS TO HAVE PINNED.
#                      (audit #5 finding [3], LOW)
#
# THE MISTAKE: stop setting CountWithDebtOutstanding. It is one line in each of the two replays and is
# exactly what a refactor of the count clause would drop by accident.
#
# NOTHING NUMERIC MOVES. Every closing value, every issue value, every quantity is identical — the flag
# is provenance metadata only. What moves is the LABEL: G6-001's three subjects fall from INVENTED (a
# rule NOTHING calibrates) to BRIEF (a rule the rework brief states), and the report stops asking the
# reader to ratify them.
#
# WHY THE ROUND-5 GATES COULD NOT SEE IT. CHECK 4c's coverage assertion iterates the emitted
# RefProvenance tag and requires every INVENTED subject to carry a golden. Making the INVENTED set
# SMALLER trivially satisfies that. The total-collapse case was defended (zero INVENTED subjects is a
# harness failure) but the partial case was not: G7's six subjects keep the count non-zero, so nothing
# fires and nothing in the report says the measured population shrank from 9 to 6.
#
# WHAT MUST CATCH IT: Facts.InventedByRule, which answers the same question from the SPEC by a pure
# quantity walk — was a count taken, or an unrated inward received, while the company-wide net quantity
# was already negative? — and the comparator's assertion that the emitted population EQUALS it, in BOTH
# directions. The size of the spec population is also pinned as census cell CHECK4c.inventedSubjects.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    if (st.Debt > 0m) st.CountWithDebtOutstanding = true;
                    st.Debt = 0m;' \
'                    // <-- POISON: the CountWithDebtOutstanding flag is no longer raised.
                    st.Debt = 0m;'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;' \
'                    // <-- POISON: same flag, the moving-average replay.
                    debt = 0m;'
