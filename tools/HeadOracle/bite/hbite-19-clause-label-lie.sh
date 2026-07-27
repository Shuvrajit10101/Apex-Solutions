# HARNESS INTEGRITY — A GOLDEN RE-TAGGED ONTO A CLAUSE IT DOES NOT EXERCISE.
#
# AUDIT #6 [1] (LOW). Until round 7 the clause-coverage assertion was
#     Goldens.RequiredClauses.Where(c => !Goldens.All.Concat(Goldens.Issue).Select(g => g.Clause).Contains(c))
# — a projection of the table under audit. It proved every required tag APPEARS somewhere. It never asked
# whether any tag was TRUE of the subject carrying it, so clause coverage was self-attestation: re-tagging
# a golden manufactured coverage out of nothing, and a clause could be reported "exercised" while no
# golden anywhere actually reached it.
#
# THE MUTATION. GI-27 pins G9-002 — a book with NO company-wide debt at any point, which is precisely why
# it is the control. Re-tag it from `issue:no-debt-control` to `issue:debt-outstanding`, a clause that
# requires a debt still outstanding at the as-of date. The CONSTANT IS NOT TOUCHED, so every value in the
# table still reproduces and CHECK 4c's mismatch count stays 0.
#
# WHAT THE OLD GATE SAW: nothing. `issue:debt-outstanding` still appears (GI-07, GI-10 genuinely exercise
# it) and `issue:no-debt-control` still appears (GI-28, GI-29), so BOTH tags were present, coverage printed
# complete, and the run was HARNESS SOUND.
#
# WHAT MUST HAPPEN NOW: the label is checked against FactDebtShape — a pure quantity walk over the spec —
# G9-002 carries no debt at all, and the run must be HARNESS BROKEN (exit 3) with the CLAUSE-LABEL failure
# naming GI-27. The golden-digest census cell moves too (Clause is part of the digest, by design), which is
# the second, independent alarm on the same edit.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Goldens.cs" \
'        new("GI-27", "G9-002", "Widget", "Fifo", "2024-04-25", 1291, DebtClause.IssueControl,' \
'        new("GI-27", "G9-002", "Widget", "Fifo", "2024-04-25", 1291, DebtClause.IssueUnderDebt,'
