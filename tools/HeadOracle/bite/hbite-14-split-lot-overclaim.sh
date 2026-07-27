# HARNESS INTEGRITY — AN OVER-CLAIM SPLIT ACROSS SEVERAL LAYERS FROM ONE LOT.
#                      (audit #4 finding [5], LOW — the AGGREGATE per-lot bound)
#
# `perLot` was accumulated at Program.cs and NEVER READ. The only quantity constraint the origin binding
# had was PER LAYER (`lq > lot.Qty`), so a reference that spread an over-claim across several layers from
# the same lot passed with "ORIGIN / WRONG-RATE failures : 0" printed over it. That is the third
# "emitted/accumulated and never read" instance the audits have found in this file.
#
# THE POISON: re-attribute the FIRST surviving layer to the LAST layer's lot, at the LAST lot's real spec
# rate, SPLIT IN TWO so that neither half exceeds that lot's size. Every per-layer test passes:
#   * the origin lot exists                                   -> pass
#   * each individual layer is within what that lot supplied  -> pass
#   * each layer's rate IS that lot's explicit spec rate      -> pass
#   * total quantity is preserved                             -> pass
# Only the AGGREGATE bound can see it.
#
# ISOLATION NOTE, stated rather than glossed. This poison is not gated on the debt branch, so it also
# perturbs never-negative books and CHECK 4 CALIBRATION convicts those. What it demonstrates is that the
# AGGREGATE bound convicts subjects CALIBRATION CANNOT REACH — the never-dry G-family books (G9, G12-001)
# where the layer stack never runs dry and HEAD is not trusted. Read the (e-agg) lines for those.
set -euo pipefail
: "${RUNNER:?RUNNER must be set by harness-bite.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

python "$HERE/_patch.py" "$RUNNER/Reference.cs" \
'                    break;
                }
            }
        }
        return st;
    }' \
'                    break;
                }
            }
        }
        // <-- POISON: split the first layer in two and attribute BOTH halves to the LAST layer'"'"'s lot,
        // at that lot'"'"'s genuine spec rate. Quantity preserved; neither half exceeds the lot size.
        if (st.Layers.Count >= 2)
        {
            var first = st.Layers[0];
            var last = st.Layers[^1];
            var half = first.Qty / 2m;
            st.Layers[0] = new Layer(half, last.Unit, last.Src, last.Origin);
            st.Layers.Insert(1, new Layer(first.Qty - half, last.Unit, last.Src, last.Origin));
        }
        return st;
    }'
