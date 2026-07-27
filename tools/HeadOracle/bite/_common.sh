# Shared bits for the bite mutations. $ENGINE is set by bite-test.sh to the MUTATED engine copy.
set -euo pipefail
: "${ENGINE:?ENGINE must be set by bite-test.sh}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SVS="$ENGINE/Services/StockValuationService.cs"
patch() { python "$HERE/_patch.py" "$1" "$2" "$3"; }

# A C# one-liner that sets __neg when the item's own movement stream ever went negative. It is the
# discriminator between "this book over-drew" and "this book never did", so a mutation using it leaves
# the never-negative (N*) families byte-identical and the bite is attributable to ONE check.
NEG_DETECT='var __net = 0m; var __neg = false; foreach (var __e in events) { if (__e.Kind == MovementKind.Inward) __net += __e.Quantity; else if (__e.Kind == MovementKind.Outward) { __net -= __e.Quantity; if (__net < 0m) __neg = true; } else __net = __e.Quantity; }'

# Unique anchors in StockValuationService.cs.
ANCHOR_LAYERVALUE='        _ = closingQty; // layers already reconcile to closing qty via the same movement set'
ANCHOR_CLOSINGVALUE='        var cost = CostContext.For(item, events);

        var value = item.ValuationMethod switch'
ANCHOR_ISSUE='        return new Money(consumed).RoundToPaisa();'
ANCHOR_TOTAL='        foreach (var item in _company.StockItems)
            total += ClosingValue(item.Id, asOf).Value;'
ANCHOR_AVGRESET='                    if (qty <= 0m) { qty = 0m; cost = 0m; }'
