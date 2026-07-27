# CHECK 3 — THE POINT ORACLE. Understates every over-drawn book's FIFO/LIFO closing value by 3%.
# THIS IS THE WHOLE POINT OF THE REWORK. Measured result: check 3 convicts 56 subjects; checks 7 and 8
# see NOTHING AT ALL, and check 6 sees exactly 4 of them — only G6-002, whose HEAD value happened to sit
# precisely on the band floor of Rs 7.91 so any understatement at all falls off it. On the other 52
# subjects a 3% wrong-but-plausible closing value is invisible to every absolute check, and only a point
# comparison against the calibrated reference convicts it.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; { $NEG_DETECT if (__neg) value = value * 0.97m; }"
