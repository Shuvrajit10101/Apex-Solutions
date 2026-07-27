# CHECK 7 — TOTAL-SPEND CONTAINMENT. Values over-drawn stock at Rs 90.11/unit. That rate is INSIDE the
# [7.91, 100.13] band, so check 6 stays silent, but 25 units x 90.11 = Rs 2,252.75 exceeds the
# Rs 1,317.70 ever spent — you cannot hold more asset than you bought.
# MEASURED on G1-001/Widget/Fifo/2024-04-20: check 6 silent, check 7 INTRODUCED (excess 93,505p).
# Check 8 necessarily fires with it: value > spend forces implied COGS negative, so check 7 can
# never be demonstrated in complete isolation. Other scenarios with narrower bands (G8's [3.17,
# 88.61]) do also trip check 6 at Rs 90.11 — the isolation claim is about the named subject.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_LAYERVALUE" "        _ = closingQty; { $NEG_DETECT if (__neg) value = closingQty * 90.11m; }"
