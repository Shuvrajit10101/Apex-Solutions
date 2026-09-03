# HARNESS INTEGRITY — RESURRECT THE DRAINED LOT'S UNITS AFTER A REPAYMENT.
#                      (audit #4 finding [1](2), HIGH — the ORDERING assertion)
#
# THE SHAPE THAT DEFEATS EVERY RATE TEST. This poison re-attributes part of the repayment surplus to the
# lot that ran out, AT THAT LOT'S OWN GENUINE SPEC RATE, and never claims more units than that lot ever
# supplied. So the entire round-3/round-4 rate binding acquits it:
#   * the origin lot EXISTS in FactInwardLots                              -> pass
#   * the layer does not exceed what that lot supplied (10 <= 10)          -> pass
#   * the layer's rate EQUALS that lot's explicit spec rate (100.13)       -> pass
#   * the AGGREGATE per-lot bound (finding [5]) is satisfied too (10 <= 10)-> pass
#   * quantities are preserved exactly, so self-consistency and (a) pass   -> pass
# Every layer is TRUTHFULLY bound to a real lot at that lot's real rate. The lie is not in any rate; it is
# in TIME. Those units were provably consumed before the repaying lot ever arrived.
#
# Only an ORDERING fact can say so, and that is what FactPostDryLots is: a PURE QUANTITY walk in Facts.cs
# establishing that the company-wide net quantity was <= 0 at the last dry point, so the stack was empty
# there and nothing created at or before it can still be surviving.
#
# Audit #4 recorded that this assertion had NOT been built, and that the round-4 harness caught the
# equivalent poison only incidentally, via a layer-COUNT census cell — a count-preserving variant escaped
# entirely. This variant preserves total quantity.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 1. remember what the drained lot was
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;' \
'        /// <summary>Quantity added by count-ups (units nobody bought) — feeds the spend ceiling.</summary>
        public decimal CountUpQty;
        public decimal DrainedQty;      // <-- POISON scaffolding
        public decimal DrainedRate;     // <-- POISON scaffolding
        public string DrainedOrigin = string.Empty;   // <-- POISON scaffolding'

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'            var take = Math.Min(layer.Qty, remaining);
            st.RunQty -= take;' \
'            var take = Math.Min(layer.Qty, remaining);
            if (take > st.DrainedQty)                    // <-- POISON scaffolding
            {
                st.DrainedQty = take;
                st.DrainedRate = layer.Unit;
                st.DrainedOrigin = layer.Origin;
            }
            st.RunQty -= take;'

# 2. THE POISON: split the repayment surplus, giving part of it back to the drained lot — truthfully
#    bound to that lot, at that lot's real rate, within that lot's real size. Total quantity unchanged.
python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    if (qty > 0m)
                    {
                        st.Layers.Add(new Layer(qty, unit, src, e.Origin));
                        st.RunQty += qty;
                        st.RunCost += qty * unit;
                    }
                    break;' \
'                    if (qty > 0m)
                    {
                        var resurrect = 0m;   // <-- POISON
                        if (st.DrainedOrigin.Length > 0 && st.DrainedQty > 0m)
                        {
                            resurrect = Math.Min(st.DrainedQty, qty);
                            st.Layers.Add(new Layer(resurrect, st.DrainedRate, RateSource.Explicit, st.DrainedOrigin));
                            st.RunQty += resurrect;
                            st.RunCost += resurrect * st.DrainedRate;
                            st.DrainedOrigin = string.Empty;
                        }
                        if (qty - resurrect > 0m)
                        {
                            st.Layers.Add(new Layer(qty - resurrect, unit, src, e.Origin));
                            st.RunQty += qty - resurrect;
                            st.RunCost += (qty - resurrect) * unit;
                        }
                    }
                    break;'
