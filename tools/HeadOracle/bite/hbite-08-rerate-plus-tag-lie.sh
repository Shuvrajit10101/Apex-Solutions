# HARNESS INTEGRITY — THE SAME RE-RATING POISON, PLUS THE TAG THAT USED TO EXCUSE IT.
#   (audit #3 finding [3], MEDIUM — self-attestation is not evidence)
#
# The first value invariant contained `if (srcs[i] == "RunningAverage") { raHere++; continue; }`, which
# skipped the rate test ENTIRELY for that layer — on the strength of a tag produced by Reference.cs, the
# very file whose arithmetic the invariant exists to audit. The audit called that hole latent, not live,
# and asked for it to be closed by DERIVING reachability from the SPEC.
#
# This bite makes it live: the re-rating poison of hbite-07 PLUS a stamp of RateSource.RunningAverage on
# the layer it corrupts. Under the old rule that layer became unauditable while the counter still read 0.
# It must now be convicted TWICE over:
#   * the origin lot carries an EXPLICIT rate, so the best-available-cost chain is UNREACHABLE for it and
#     the tag is itself a defect;
#   * and the rate is not that lot's rate.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;' \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;
        public decimal DrainedRate;   // <-- POISON scaffolding'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'            var take = Math.Min(layer.Qty, remaining);
            st.RunQty -= take;' \
'            var take = Math.Min(layer.Qty, remaining);
            st.DrainedRate = layer.Unit;   // <-- POISON scaffolding
            st.RunQty -= take;'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        if (e.Rate is null) st.DebtRepaidByUnratedInward = true;
                        else st.DebtRepaidByRatedInward = true;
                    }' \
'                        if (e.Rate is null) st.DebtRepaidByUnratedInward = true;
                        else st.DebtRepaidByRatedInward = true;
                        if (st.DrainedRate > 0m)
                        {
                            unit = st.DrainedRate;              // <-- RE-RATING POISON
                            src = RateSource.RunningAverage;    // <-- AND THE TAG THAT USED TO EXCUSE IT
                        }
                    }'
