# CHECK 8 — COGS CONSERVATION. Values over-drawn stock at Rs 48.03/unit: inside the rate band (check 6
# silent) and below total spend (check 7 silent), but it leaves implied COGS of Rs 4.68/unit on an item
# whose cheapest rate ever paid was Rs 7.91 — cost of goods sold that nobody ever bought.
# MEASURED on G1-001/Widget/Fifo/2024-04-20: checks 6 AND 7 stay silent, check 8 alone is
# INTRODUCED (COGS 467.8p/unit, 322.12p below the floor, magnitude 8,053p). Complete isolation.
# Scenarios with narrower bands do also trip check 6 at Rs 48.03; the claim is about the subject.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; { $NEG_DETECT if (__neg) value = closingQty * 48.03m; }"
