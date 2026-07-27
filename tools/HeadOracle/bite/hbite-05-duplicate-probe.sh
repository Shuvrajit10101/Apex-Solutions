# HARNESS INTEGRITY — A DUPLICATE EMITTED KEY MUST BE FATAL, NOT SILENT.
#
# Collides two of G9-001's IssueValue probes, exactly as the real corpus collided G2-002's. Emit then
# writes 'IssueValue@1.25Paisa' and 'RefIssueValue@1.25Paisa' twice per (method, as-of).
#
# BEFORE: ReadTsv's `map[key] = value` kept the last silently. 60 emitted rows vanished from every
# comparison, the emitter said 20030 rows and the report header said 19970, and a future corpus edit
# that accidentally collided two keys would have shrunk coverage with no diagnostic at all.
# AFTER: ReadTsv throws InvalidDataException naming the key and both values, so the comparator cannot
# run on a corpus that drops rows.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Corpus.cs" \
'            [1.25m, 23.25m, 300m],' \
'            [1.25m, 1.25m, 300m],'
