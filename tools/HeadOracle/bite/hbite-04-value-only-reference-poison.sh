# HARNESS INTEGRITY — THE VALUE-ONLY REFERENCE POISON.
#
# This is the mutation the audit used to prove PART A had no VALUE invariant. It sets the surviving
# remainder of a debt-repaying lot to unit 0, so:
#   * every QUANTITY is untouched  => REFERENCE SELF-CONSISTENCY passes;
#   * N* books never carry a debt  => CHECK 4 CALIBRATION passes (0 mismatches);
#   * the report used to print "HARNESS INTEGRITY : SOUND" while the poisoned reference DEMANDED
#     G1-001/Widget/Fifo/2024-04-20 = 0p where the pristine reference says 19775p (25 x Rs 7.91).
# A builder who complied would wipe Rs 197.75 of real asset per crux book while the harness printed
# CHECK 3 PASS.
#
# The REFERENCE VALUE INVARIANT must now convict it: 0 is not a rate the spec contains for that book
# (its admissible set is {7.91, 9.77, 100.13}), and the layer's emitted source tag is Explicit, so it
# cannot be excused as a running-average blend.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        st.Debt -= repay;
                        qty -= repay;' \
'                        st.Debt -= repay;
                        qty -= repay;
                        unit = 0m;   // <-- VALUE-ONLY POISON: quantities untouched, value destroyed'
