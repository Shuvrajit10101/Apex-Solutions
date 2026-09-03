# HARNESS INTEGRITY — THE RE-RATING POISON THAT USES AN *ADMISSIBLE* RATE.  (audit #3 finding [1], HIGH)
#
# The first REFERENCE VALUE INVARIANT convicted a layer priced at a rate the SPEC DOES NOT CONTAIN
# (the `unit = 0m` poison). It did NOT convict the single most likely genuine mistake in this branch:
# RE-RATING the repayment surplus at the rate of the stock that ran out. That rate IS in the admissible
# set, so set-membership acquitted it. The adversary demonstrated the whole chain:
#   * PART A printed  REFERENCE SELF-CONSISTENCY PASS / REFERENCE VALUE INVARIANT ... INADMISSIBLE 0 /
#     CHECK 4 CALIBRATION PASS / HARNESS INTEGRITY : SOUND
#   * while the poisoned reference DEMANDED G1-001/Widget/Fifo/2024-04-20 = 25@100.13 = 250325p
#     against the pristine 25@7.91 = 19775p — a 12.66x fabrication on THE CRUX;
#   * and it then CONVICTED the reference-conformant engine (live=19775 vs REFERENCE=250325).
#
# This reproduces that poison exactly: Consume() remembers the rate of the layer it drained, and a lot
# that repays a debt is re-rated to it. The ORIGIN BINDING must convict: the surviving layer's units come
# from the 40 @ 7.91 lot, and FactInwardLots — derived by Facts' OWN walk of the spec, never by
# Reference.BuildStack — says that lot's rate is 7.91.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 1. a place to remember the rate of the stock that ran out
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;' \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;
        public decimal DrainedRate;   // <-- POISON scaffolding: the rate of the stock that ran out'

# 2. record it as the layers drain
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'            var take = Math.Min(layer.Qty, remaining);
            st.RunQty -= take;' \
'            var take = Math.Min(layer.Qty, remaining);
            st.DrainedRate = layer.Unit;   // <-- POISON scaffolding
            st.RunQty -= take;'

# 3. THE POISON: re-rate the repayment surplus at the OLD lot's rate (which IS admissible)
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        if (e.Rate is null) st.DebtRepaidByUnratedInward = true;
                        else st.DebtRepaidByRatedInward = true;
                    }' \
'                        if (e.Rate is null) st.DebtRepaidByUnratedInward = true;
                        else st.DebtRepaidByRatedInward = true;
                        if (st.DrainedRate > 0m) unit = st.DrainedRate;   // <-- RE-RATING POISON
                    }'
