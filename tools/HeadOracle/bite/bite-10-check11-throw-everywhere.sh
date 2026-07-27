# CHECK 11 — EXCEPTION ASYMMETRY (audit C3, PROVEN exploit). Throws on every negative-stock valuation.
# v1 certified this CLEAN and credited it with 16 'resolved' violations, because a row that vanished
# into an exception looked exactly like a row that had been fixed.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_CLOSINGVALUE" "        var cost = CostContext.For(item, events);
        { $NEG_DETECT if (__neg) throw new InvalidOperationException(\"negative-stock valuation is not supported\"); }

        var value = item.ValuationMethod switch"
