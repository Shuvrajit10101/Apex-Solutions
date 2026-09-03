# AUDIT C2 (PROVEN exploit) — MAGNITUDE-BLIND DEDUP. Multiplies every over-drawn closing value by 100,
# making violations that ALREADY EXIST AT HEAD enormously worse. v1 keyed violations by subject alone
# and discarded any live violation whose key existed at head, so it certified a mutation producing
# Rs 100,000 of phantom stock as CLEAN. The reworked comparator compares by (key, MAGNITUDE).
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; { $NEG_DETECT if (__neg) value = value * 100m; }"
