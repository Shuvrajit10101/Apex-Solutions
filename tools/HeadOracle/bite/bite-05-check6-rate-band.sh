# CHECK 6 — CLOSING-RATE BAND. Multiplies over-drawn closing values by 1,000: the Rs 100,100-for-one-unit
# shape from the first failed attempt.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; { $NEG_DETECT if (__neg) value = value * 1000m; }"
