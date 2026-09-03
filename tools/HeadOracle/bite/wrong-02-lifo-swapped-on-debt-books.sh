#!/usr/bin/env bash
# =====================================================================================================
# WRONG ENGINE 2 — FIFO AND LIFO SWAPPED, BUT ONLY ON A BOOK THAT CARRIED A DEBT.
#
#   bash tools/HeadOracle/bite-test.sh r7-wrong-lifoswap \
#        tools/HeadOracle/bite/wrong-02-lifo-swapped-on-debt-books.sh
#
# This mutation exists to MEASURE audit #6's finding [2] rather than argue it. It is the accept-probe
# engine — which the harness ACCEPTS — plus one defect: whenever the layer replay ever ran short, the
# consume ORDER is inverted. FIFO takes the newest layer and LIFO takes the oldest, on debt books only.
#
# WHY THIS EXACT SHAPE. A GLOBAL FIFO/LIFO swap is caught easily, by never-negative books with two lots.
# Confining it to books that carried a debt makes it invisible everywhere the harness had cover — and
# BEFORE the G15-001 scenario was added it was invisible on the debt books too, because not one debt
# scenario left more than ONE surviving layer, and where a single layer survives there is no oldest and
# no newest to get wrong. This mutation would therefore have passed the ENTIRE harness: every check,
# every golden, exit 0.
#
# It is now convicted, and the conviction should name G15-001 and nothing else. That is the whole
# argument for the new scenario, in a form a reader can re-run.
# =====================================================================================================
set -euo pipefail

[ -n "${ENGINE:-}" ] || { echo "wrong-02: \$ENGINE is not set (run me through bite-test.sh)." >&2; exit 2; }
F="$ENGINE/Services/StockValuationService.cs"
[ -f "$F" ] || { echo "wrong-02: $F not found." >&2; exit 2; }

# Start from the ACCEPTED engine so the only difference is the defect below.
bash "$(dirname "${BASH_SOURCE[0]}")/accept-probe.sh"

python - "$F" <<'PY'
import sys
path = sys.argv[1]
src = open(path, encoding="utf-8").read()

# BuildLayers is the single entry point for both the closing stack and the issue stack, so inverting
# `lifo` there propagates to CHECK 3, CHECK 9(b) and CHECK 10 exactly as a real mistake would.
old = """    private static List<Layer> BuildLayers(IReadOnlyList<MovementEvent> events, bool lifo, CostContext cost)
    {"""

new = """    private static List<Layer> BuildLayers(IReadOnlyList<MovementEvent> events, bool lifo, CostContext cost)
    {
        // *** DELIBERATE DEFECT (bite test) — FIFO/LIFO INVERTED ON DEBT BOOKS ONLY. ***
        // Invisible on every never-negative book, and invisible on every debt book in the corpus BEFORE
        // G15-001, because no earlier debt scenario left more than one surviving layer.
        {
            var held = 0m;
            var everShort = false;
            foreach (var ev in events)
            {
                if (ev.Kind == MovementKind.Inward) held += ev.Quantity;
                else if (ev.Kind == MovementKind.Count) held = ev.Quantity;
                else { if (ev.Quantity > held) everShort = true; held -= ev.Quantity; }
            }
            if (everShort) lifo = !lifo;
        }
"""

assert old in src, "BuildLayers signature not found"
src = src.replace(old, new, 1)

# The ISSUE walk carries its OWN direction variable rather than reusing BuildLayers' flag, so a complete
# FIFO/LIFO swap has to invert it there as well; otherwise only the closing STACK is mutated and the walk
# over it still runs in the correct direction.
old_issue = """        var remaining = quantity;
        var consumed = 0m;
        while (remaining > 0m && layers.Count > 0)
        {
            var idx = item.ValuationMethod == StockValuationMethod.Lifo ? layers.Count - 1 : 0;"""

new_issue = """        // *** DELIBERATE DEFECT (bite test) — the same inversion, on the ISSUE walk. ***
        var issueLifo = item.ValuationMethod == StockValuationMethod.Lifo;
        {
            var held = 0m;
            var everShort = false;
            foreach (var ev in events)
            {
                if (ev.Kind == MovementKind.Inward) held += ev.Quantity;
                else if (ev.Kind == MovementKind.Count) held = ev.Quantity;
                else { if (ev.Quantity > held) everShort = true; held -= ev.Quantity; }
            }
            if (everShort) issueLifo = !issueLifo;
        }

        var remaining = quantity;
        var consumed = 0m;
        while (remaining > 0m && layers.Count > 0)
        {
            var idx = issueLifo ? layers.Count - 1 : 0;"""

assert old_issue in src, "IssueValue walk not found"
src = src.replace(old_issue, new_issue, 1)

open(path, "w", encoding="utf-8", newline="\n").write(src)
print("wrong-02: consume order AND issue-walk direction inverted on every book that ever ran short")
PY
