# HARNESS INTEGRITY — RE-PRICE A COUNT-UP TAKEN WITH A DEBT OUTSTANDING.  (audit #4 finding [1](1), HIGH)
#
# The round-3 origin binding is real, and it convicts the naive re-rate. But `isCountUp` deliberately
# EXEMPTS a count-up layer from the lot binding (a count-up has no supplying lot to bind to), leaving it
# on the weak test: admissible outright, or inside the admissible hull. THE CRUX LIVES IN THAT EXEMPTION.
#
# This is the adversary's a10r4-h5-countup poison: price the top-up at the chain's LAST RATED INWARD link
# instead of the answer the chain actually gives, FIRED ONLY WHEN A DEBT WAS OUTSTANDING — a path no
# never-negative book can reach, so calibration is untouched. 100.13 is an admissible rate on G6-001, so:
#   qty-decomposition 0 / value-decomposition 0 / ORIGIN-WRONG-RATE 0 / INADMISSIBLE 0,
#   CHECK 4 PASS, CHECK 4b PASS, census unchanged, every engine-failure count byte-identical —
# and the demanded value on THE CRUX moves from 8 x Rs 9.77 = 7816p to 8 x Rs 100.13 = 80104p, a 10.25x
# fabrication that a reader diffing two reports would not see anywhere.
#
# CHECK 4c's GT-11 / GT-11L / GT-12 are external constants, so they convict it. CHECK 4c additionally
# asserts that EVERY subject tagged INVENTED — which is exactly the count-with-debt and unrated-repayment
# population — carries such a golden, so the exemption cannot spread to an unpinned subject.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --- the LAYER replay: only when the count landed on an outstanding debt
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    if (st.Debt > 0m) st.CountWithDebtOutstanding = true;
                    st.Debt = 0m;' \
'                    var poisonDebt = st.Debt > 0m;   // <-- POISON scaffolding
                    if (st.Debt > 0m) st.CountWithDebtOutstanding = true;
                    st.Debt = 0m;'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                        var (unit, src) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                        var add = e.Qty - current;' \
'                        var (unit, src) = chain.NoRateCostTagged(RunningAverage(st.RunQty, st.RunCost));
                        // <-- POISON: an ADMISSIBLE but WRONG rate for this particular layer
                        if (poisonDebt && chain.LastRatedInward is { } poisonRate) unit = poisonRate;
                        var add = e.Qty - current;'

# --- and the same in the debt-aware moving average, so the two AverageCost columns stay consistent
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                case Kind.Count:
                {
                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = chain.NoRateCost(0m);' \
'                case Kind.Count:
                {
                    var poisonDebt = debt > 0m;   // <-- POISON scaffolding
                    if (debt > 0m) flags.CountWithDebtOutstanding = true;
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = chain.NoRateCost(0m);
                    if (poisonDebt && chain.LastRatedInward is { } poisonRate) unit = poisonRate;   // <-- POISON'
