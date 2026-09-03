# HARNESS INTEGRITY — "A CHECK THAT EVALUATED NOTHING MUST FAIL" (audit H1). Silently disables check 8
# so it reports 0 violations on every family. v1 would have printed a table full of reassuring zeroes;
# the reworked comparator asserts a NON-ZERO evaluated-subject count for every check and calls itself
# BROKEN instead.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Program.cs" \
'        if (costBased && ceiling is { } cap8 && floorSpend is { } flo8' \
'        if (false && costBased && ceiling is { } cap8 && floorSpend is { } flo8'
