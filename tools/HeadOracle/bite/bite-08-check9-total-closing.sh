# CHECK 9 — TotalClosingStockValue (audit H3). Adds 7 paisa of phantom Stock-in-Hand per negative item
# to the company aggregate ONLY, leaving every per-item figure correct. This is the actual Balance-Sheet
# number all three previous failures broke, and v1 emitted it and checked nothing.
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
patch "$SVS" "$ANCHOR_TOTAL" "        foreach (var item in _company.StockItems)
        {
            total += ClosingValue(item.Id, asOf).Value;
            if (_onHand.OnHand(item.Id, asOf) < 0m) total += new Money(0.07m);
        }"
