# HARNESS INTEGRITY — THE CALIBRATION GATE (check 4). Poisons the reference's FIFO/LIFO closing value by
# one paisa EVERYWHERE. On the G* books that would look like a defect in the engine; the calibration
# gate catches it on the N* books where HEAD is trusted, and reports the REFERENCE as the wrong party.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'        foreach (var l in st.Layers) value += l.Qty * l.Unit;
        return Paisa(value);' \
'        foreach (var l in st.Layers) value += l.Qty * l.Unit;
        return Paisa(value) + 0.01m;'
