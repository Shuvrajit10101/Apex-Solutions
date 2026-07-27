# CHECK 10 — ISSUE VALUE (audit M3). Overstates the cost of every issue out of an over-drawn book by
# 50% while leaving every closing value untouched: a PERFECT Balance Sheet and a wrong P&L. v1 audited
# IssueValue with nothing at all, so this shipped clean.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_ISSUE" "        { $NEG_DETECT return new Money(__neg ? consumed * 1.5m : consumed).RoundToPaisa(); }"
