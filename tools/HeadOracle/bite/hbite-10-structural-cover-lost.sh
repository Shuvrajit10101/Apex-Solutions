# HARNESS INTEGRITY — A STRUCTURALLY-UNSATISFIABLE FAMILY LEFT WITH NO POINT ORACLE.
#   (audit #3 finding [4], MEDIUM)
#
# On the structurally-unsatisfiable subjects, checks 6, 7 and 8 STAND DOWN — correctly: their premise
# cannot be met by any value, and the magnitude then rises WITH the closing value, so scoring them points
# the wrong way. The exclusion predicate was independently verified SOUND and must NOT be narrowed.
#
# But it means the point oracle is the sole remaining defence there, and the adversary proved the cost:
# an engine wrong ONLY on those subjects (value x5) passed CHECK 6, CHECK 7 and CHECK 8 together — three
# PASSes over an inflated crux — and only CHECK 3 and CHECK 9(b) convicted it. Nothing in the report said
# "the absolute checks have been switched off here".
#
# This bite removes the AverageCost point oracle's reference column for the G1 family, so those subjects
# land in the bucket with checks 6/7/8 stood down AND no point oracle left. The COVER ASSERTION must say
# so, by name, instead of the report printing three PASSes over an unmeasured value.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Program.cs" \
'                    if (mn == "AverageCost")
                        Row(s.Id, item.Name, mn, d, "RefClosingValueDebtAwarePaisa",
                            Paisa(Reference.DebtAwareAverageValue(s, item, asOf)));' \
'                    if (mn == "AverageCost" && s.Family != "G1")   // <-- BITE: G1 loses its AverageCost oracle
                        Row(s.Id, item.Name, mn, d, "RefClosingValueDebtAwarePaisa",
                            Paisa(Reference.DebtAwareAverageValue(s, item, asOf)));'
