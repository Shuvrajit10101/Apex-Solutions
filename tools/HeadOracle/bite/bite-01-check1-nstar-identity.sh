# CHECK 1 — never-negative byte identity. Adds one paisa to every FIFO/LIFO closing value, which the
# N* families see immediately.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; value += 0.01m;"
