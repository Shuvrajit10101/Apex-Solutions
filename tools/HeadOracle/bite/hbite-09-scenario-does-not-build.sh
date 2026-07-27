# HARNESS INTEGRITY — A SCENARIO THAT DOES NOT BUILD.  (audit #3 finding [0], HIGH)
#
# This reproduces, deliberately, the exact state G11-002 was in for its whole life: Corpus.Build throws,
# Emit `continue`s, no engine row exists, the point oracle iterates LIVE keys so it evaluates 0 subjects
# there, and CHECK 11 sees a SYMMETRIC exception on both arms and passes. Before the BUILD OUTCOME gate,
# the ONLY trace was a BuildOutcome row that no check read — and once Census.cs was re-recorded from that
# state, the census gate actively BLESSED the hole.
#
# G14-001 is chosen rather than G11-002 so the bite is independent of the corpus fix it is testing.
# The BUILD OUTCOME gate must convict on the HEAD arm with exit 3: a scenario that cannot be CONSTRUCTED
# is a broken corpus, not an engine verdict.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Corpus.cs" \
'    public static Book Build(Scenario s, StockValuationMethod method)
    {
        var company = CompanyFactory.CreateSeeded("Oracle Co", FyStart);' \
'    public static Book Build(Scenario s, StockValuationMethod method)
    {
        // <-- BITE: a scenario that silently fails to construct, on BOTH arms
        if (s.Id == "G14-001") throw new InvalidOperationException("BITE: simulated corpus construction failure");
        var company = CompanyFactory.CreateSeeded("Oracle Co", FyStart);'
