# CHECK 5 — QUANTITY ORACLE (audit H2). Reports quantity 0 and value 0 for every book that ever went
# negative: a real asset silently leaves the Balance Sheet. Every VALUE check is satisfied by zero.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_CLOSINGVALUE" "        var cost = CostContext.For(item, events);
        { $NEG_DETECT if (__neg) return StockClosingValuation.Zero; }

        var value = item.ValuationMethod switch"
