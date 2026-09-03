#!/usr/bin/env bash
# =====================================================================================================
# THE ACCEPT-STATE PROBE — "an oracle whose ACCEPT state has never been observed is not known to have one."
#
# Run via:  bash tools/HeadOracle/bite-test.sh accept-probe tools/HeadOracle/bite/accept-probe.sh
#
# This is NOT a fix and it NEVER touches src/. It rewrites the SANDBOX copy of StockValuationService.cs
# under .oracle-work/bite/ so that its FIFO/LIFO layer replay implements the calibrated reference's debt
# semantics exactly:
#
#   1. an outward larger than the layers hold drains them and the SHORTFALL BECOMES A DEBT;
#   2. a later inward REPAYS the debt FIRST, at that inward lot's own rate — repaid units are already
#      issued, so their cost goes to COGS and only the REMAINDER becomes a layer;
#   3. an existing debt is never re-rated;
#   4. a physical count writes the debt off and tops the stack up through the best-available-cost chain
#      (running average -> standard -> last rated inward -> 0), not the running average alone.
#
# AverageCost IS NOW FIXED TOO (2026-07-27 user scope decision). Until that decision the probe left
# RunAverage alone because CHECK 2 byte-LOCKED AverageCost to HEAD; CHECK 2 is now a point oracle against
# the calibrated debt-aware reference, so a two-method probe would be REJECTED on 6 AverageCost subjects,
# 18 issue values and 6 company totals. The probe therefore also installs a DEBT-AWARE moving average and
# routes AverageValue through it — which propagates to IssueValue and TotalClosingStockValue, because both
# derive from ClosingValue. RunAverage itself is left alone deliberately: LastPurchaseRate falls back to it
# when a book has no rated inward at all, and the reference does exactly the same, so changing it would
# make the flat methods diverge from an oracle that is right.
#
# The point of the probe is to answer a question the harness could not previously answer about ITSELF:
# is exit 0 / ENGINE VERDICT ACCEPTED reachable at all? Before the check-8 structural-satisfiability
# fix it was NOT — checks 3 and 8 contradicted each other on G6-001, so the engine the point oracle
# prescribes was scored "WORSENED ... FAIL" and REJECTED.
# =====================================================================================================
set -euo pipefail

[ -n "${ENGINE:-}" ] || { echo "accept-probe: \$ENGINE is not set (run me through bite-test.sh)." >&2; exit 2; }
F="$ENGINE/Services/StockValuationService.cs"
[ -f "$F" ] || { echo "accept-probe: $F not found." >&2; exit 2; }

python - "$F" <<'PY'
import sys, re
path = sys.argv[1]
src = open(path, encoding="utf-8").read()

# ---------------------------------------------------------------- 1. the debt-aware layer replay
old_inward = """                case MovementKind.Inward:
                {
                    var unit = e.Rate ?? cost.NoRateInwardCost(RunningAverage(runningQty, runningCost));
                    layers.Add(new Layer(e.Quantity, unit));
                    runningQty += e.Quantity;
                    runningCost += e.Quantity * unit;
                    break;
                }
                case MovementKind.Outward:
                {
                    Consume(layers, e.Quantity, lifo, ref runningQty, ref runningCost);
                    break;
                }"""
new_inward = """                case MovementKind.Inward:
                {
                    var unit = e.Rate ?? cost.NoRateInwardCost(RunningAverage(runningQty, runningCost));
                    var addQty = e.Quantity;
                    if (shortfallDebt > 0m)
                    {
                        // Repay at the INCOMING lot's rate; repaid units are already issued, so their
                        // cost flows to COGS and never becomes a layer. An existing debt is never re-rated.
                        var repay = Math.Min(shortfallDebt, addQty);
                        shortfallDebt -= repay;
                        addQty -= repay;
                    }
                    if (addQty > 0m)
                    {
                        layers.Add(new Layer(addQty, unit));
                        runningQty += addQty;
                        runningCost += addQty * unit;
                    }
                    break;
                }
                case MovementKind.Outward:
                {
                    var held = SumQty(layers);
                    if (e.Quantity > held) shortfallDebt += e.Quantity - held;
                    Consume(layers, e.Quantity, lifo, ref runningQty, ref runningCost);
                    break;
                }"""
assert old_inward in src, "inward/outward block not found"
src = src.replace(old_inward, new_inward, 1)

old_count = """                case MovementKind.Count:
                {
                    var current = SumQty(layers);
                    if (e.Quantity < current)
                    {
                        Consume(layers, current - e.Quantity, lifo, ref runningQty, ref runningCost);
                    }
                    else if (e.Quantity > current)
                    {
                        var unit = RunningAverage(runningQty, runningCost);
                        var add = e.Quantity - current;
                        layers.Add(new Layer(add, unit));
                        runningQty += add;
                        runningCost += add * unit;
                    }
                    break;
                }"""
new_count = """                case MovementKind.Count:
                {
                    // A count is an ABSOLUTE statement of what is on the shelf: it supersedes the book,
                    // so the debt is written off and the stack reconciled to the counted quantity. The
                    // topped-up units carry no rate of their own, so they take the best-available-cost
                    // chain — NOT the running average alone, which is 0 after an over-draw and values
                    // real counted goods at nothing.
                    shortfallDebt = 0m;
                    var current = SumQty(layers);
                    if (e.Quantity < current)
                    {
                        Consume(layers, current - e.Quantity, lifo, ref runningQty, ref runningCost);
                    }
                    else if (e.Quantity > current)
                    {
                        var unit = cost.NoRateInwardCost(RunningAverage(runningQty, runningCost));
                        var add = e.Quantity - current;
                        layers.Add(new Layer(add, unit));
                        runningQty += add;
                        runningCost += add * unit;
                    }
                    break;
                }"""
assert old_count in src, "count block not found"
src = src.replace(old_count, new_count, 1)

old_decl = """        var layers = new List<Layer>(); // oldest-first
        var runningQty = 0m;
        var runningCost = 0m;"""
new_decl = """        var layers = new List<Layer>(); // oldest-first
        var runningQty = 0m;
        var runningCost = 0m;
        var shortfallDebt = 0m;         // ACCEPT PROBE: units issued that no lot has yet paid for"""
assert old_decl in src, "declaration block not found"
src = src.replace(old_decl, new_decl, 1)

# ---------------------------------------------------------------- 4. the debt-aware MOVING AVERAGE
# Mirrors Reference.RunAverageDebtAware exactly: an over-draw empties the pool and the shortfall becomes a
# DEBT; a later inward repays it AT ITS OWN RATE and only the SURPLUS joins the pool; a debt is never
# re-rated; a count writes the debt off and restates at the best-available-cost chain when the average is
# nil. HEAD instead RESETS the pool at the over-draw and re-averages every later inward, so the sign of its
# error is the sign of the rate trend across the recovery lots and the overstatement grows without bound.
old_avg = """    private static Money AverageValue(IReadOnlyList<MovementEvent> events, decimal closingQty, CostContext cost)
    {
        var (avg, _) = RunAverage(events, cost);
        return Money.ForexBase(new Money(avg), closingQty);
    }"""
new_avg = """    private static Money AverageValue(IReadOnlyList<MovementEvent> events, decimal closingQty, CostContext cost)
    {
        var (avg, _) = RunAverageDebtAware(events, cost);
        return Money.ForexBase(new Money(avg), closingQty);
    }

    /// <summary>ACCEPT PROBE: the moving average with the same debt semantics as the layer replay.</summary>
    private static (decimal Average, decimal Quantity) RunAverageDebtAware(
        IReadOnlyList<MovementEvent> events, CostContext ctx)
    {
        var qty = 0m;
        var cost = 0m;
        var debt = 0m;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case MovementKind.Inward:
                {
                    var unit = e.Rate ?? ctx.NoRateInwardCost(RunningAverage(qty, cost));
                    var add = e.Quantity;
                    if (debt > 0m)
                    {
                        var repay = Math.Min(debt, add);
                        debt -= repay;
                        add -= repay;   // repaid units are already issued: COGS, not pool
                    }
                    if (add > 0m)
                    {
                        qty += add;
                        cost += add * unit;
                    }
                    break;
                }
                case MovementKind.Outward:
                {
                    var unit = RunningAverage(qty, cost);
                    if (e.Quantity > qty)
                    {
                        debt += e.Quantity - qty;
                        qty = 0m;
                        cost = 0m;
                    }
                    else
                    {
                        qty -= e.Quantity;
                        cost -= e.Quantity * unit;
                        if (qty <= 0m) { qty = 0m; cost = 0m; }
                    }
                    break;
                }
                case MovementKind.Count:
                {
                    debt = 0m;
                    var unit = RunningAverage(qty, cost);
                    if (unit <= 0m) unit = ctx.NoRateInwardCost(0m);
                    cost = e.Quantity * unit;
                    qty = e.Quantity;
                    break;
                }
            }
        }
        return (RunningAverage(qty, cost), qty);
    }"""
assert old_avg in src, "AverageValue block not found"
src = src.replace(old_avg, new_avg, 1)

open(path, "w", encoding="utf-8", newline="\n").write(src)
print("accept-probe: debt-aware BuildLayers AND debt-aware moving average installed in the SANDBOX engine only")
PY
