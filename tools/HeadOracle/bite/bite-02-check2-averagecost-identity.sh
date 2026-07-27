# CHECK 2 — the AverageCost POINT ORACLE. Poisons the moving-average pool reset, which only ever fires
# when the running quantity goes non-positive, i.e. on G* only. N* is untouched, so CHECK 1 and CHECK 4b
# both stay green and the bite is attributable to CHECK 2.
#
# NOTE, 2026-07-27: CHECK 2 no longer asserts BYTE IDENTITY to HEAD — the user decided AverageCost is to
# be FIXED, and a byte-lock would have forbidden the authorised fix. It is now a point oracle against the
# calibrated debt-aware reference. This bite still convicts, but for the right reason: the poisoned engine
# disagrees with the ORACLE, not merely with HEAD. (`cost = 100m` is not the debt-aware answer either.)
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_AVGRESET" "                    if (qty <= 0m) { qty = 0m; cost = 100m; }"
