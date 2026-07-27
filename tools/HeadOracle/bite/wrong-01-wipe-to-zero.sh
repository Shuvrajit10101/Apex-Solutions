#!/usr/bin/env bash
# =====================================================================================================
# WRONG ENGINE 1 — THE POSITIVE-QUANTITY FLOOR THAT WIPES AN ASSET TO Rs 0.
#
#   bash tools/HeadOracle/bite-test.sh r7-wrong-wipe tools/HeadOracle/bite/wrong-01-wipe-to-zero.sh
#
# This is the accept-probe engine — which the harness ACCEPTS — plus ONE defect, so nothing else can be
# what convicts it. The defect is the single most dangerous shape this project has met: stock that is
# physically on the shelf, with a positive closing QUANTITY, valued at NOTHING. HEAD does exactly this on
# G6-001 (8 counted units, Rs 0.00), and every "make the negative go away" fix is tempted back into it,
# because zero satisfies every absolute upper bound there is: a rate band, a spend ceiling and a COGS
# conservation rule are all bounds from ABOVE, and Rs 0 is under all of them.
#
# It is convicted only by the POINT oracles (CHECK 3, CHECK 9(b), CHECK 10) and by the hand-derived
# goldens standing behind them.
# =====================================================================================================
set -euo pipefail

[ -n "${ENGINE:-}" ] || { echo "wrong-01: \$ENGINE is not set (run me through bite-test.sh)." >&2; exit 2; }
F="$ENGINE/Services/StockValuationService.cs"
[ -f "$F" ] || { echo "wrong-01: $F not found." >&2; exit 2; }

# Start from the ACCEPTED engine so the only difference is the defect below.
bash "$(dirname "${BASH_SOURCE[0]}")/accept-probe.sh"

python - "$F" <<'PY'
import sys
path = sys.argv[1]
src = open(path, encoding="utf-8").read()

old = """        var layers = BuildLayers(events, lifo, cost);

        // Value the surviving layers (their quantities already sum to the closing quantity).
        var value = 0m;
        foreach (var l in layers)
            value += l.Quantity * l.UnitCost;
        _ = closingQty; // layers already reconcile to closing qty via the same movement set
        return new Money(value).RoundToPaisa();"""

new = """        var layers = BuildLayers(events, lifo, cost);

        // Value the surviving layers (their quantities already sum to the closing quantity).
        var value = 0m;
        foreach (var l in layers)
            value += l.Quantity * l.UnitCost;
        _ = closingQty; // layers already reconcile to closing qty via the same movement set

        // *** DELIBERATE DEFECT (bite test) — THE POSITIVE-QUANTITY FLOOR. ***
        // "The book went negative at some point, so we cannot trust a cost for it: carry it at nil."
        // This is a WIPED ASSET. Quantity stays positive and correct, so the quantity oracle is happy;
        // value goes to zero, which is under every absolute ceiling the harness computes. Only a POINT
        // oracle can see it.
        var everShort = false;
        {
            var held = 0m;
            foreach (var ev in events)
            {
                if (ev.Kind == MovementKind.Inward) held += ev.Quantity;
                else if (ev.Kind == MovementKind.Count) held = ev.Quantity;
                else { if (ev.Quantity > held) everShort = true; held -= ev.Quantity; }
            }
        }
        if (everShort) value = 0m;

        return new Money(value).RoundToPaisa();"""

assert old in src, "LayerValue block not found — accept-probe may have changed it"
src = src.replace(old, new, 1)
open(path, "w", encoding="utf-8", newline="\n").write(src)
print("wrong-01: positive-quantity floor installed (asset wiped to Rs 0 on every book that ever went short)")
PY
