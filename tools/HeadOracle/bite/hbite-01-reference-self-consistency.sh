# HARNESS INTEGRITY — REFERENCE SELF-CONSISTENCY. Restores the exact bug the reference had in its first
# draft: after an over-draw, a physical count topped the layer stack up by (counted + debt), so the
# reference valued 23 units while reporting a closing quantity of 8. Calibration cannot see this (N*
# books never carry a debt); only the self-consistency invariant convicts it.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    st.Debt = 0m;
                    var current = SumQty(st.Layers);' \
'                    var current = SumQty(st.Layers) - st.Debt;
                    st.Debt = 0m;'
