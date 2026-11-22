using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// T2-177 (wave 12, track he, IV-355..357): the two stock figures the <b>inventory-derived</b> closing-stock basis
/// needs besides the derived closing value itself, shared by <see cref="ProfitAndLoss"/> and
/// <see cref="BalanceSheet"/> so the two statements can never disagree about them.
///
/// <para><b>Ground.</b> Vendor (TallyPrime), help.tallysolutions.com/balance-sheet-tally/ (last updated 2026-11-05),
/// opened by content 2026-11-22: <i>"TallyPrime considers your stock summary balance as your closing stock."</i> A
/// manually set closing stock is the route for a company that has switched <i>Integrate Accounts and Inventory</i>
/// off. So on the integrated (inventory-derived) basis the stock summary — item-master openings plus stock
/// movements — governs the stock figures, and whatever is posted to a Stock-in-Hand LEDGER does not. The page does
/// not say how the opening stock is charged or what becomes of the income side of a posted closing-stock journal;
/// the two rules below are OURS (IV-355, IV-356) and are recorded as such in docs/invented-vs-cloned.md §2AF.</para>
/// </summary>
public static class InventoryDerivedStock
{
    /// <summary>
    /// IV-355: the opening stock charged to the trading account on the inventory-derived basis — the stock summary's
    /// opening, not a Stock-in-Hand ledger's opening balance.
    /// <list type="bullet">
    /// <item>No window, or a window starting on/before books-begin: Σ of every item-master opening allocation's value
    /// (<see cref="InventoryService.AddOpeningBalance"/>; qty × rate to the paisa), exactly what the operator
    /// entered.</item>
    /// <item>A window starting after books-begin (Alt+F2): the derived stock value as-at the day BEFORE the window,
    /// valued by each item's own method — the same function that values the closing stock, so the window's trading
    /// account covers exactly the window.</item>
    /// </list>
    /// </summary>
    public static decimal OpeningValuation(Company company, DateOnly? windowFrom)
    {
        if (windowFrom is { } from && from > company.BooksBeginFrom)
            return new StockValuationService(company).TotalClosingStockValue(from.AddDays(-1)).Amount;

        var total = 0m;
        foreach (var b in company.StockOpeningBalances)
            total += b.Value.Amount;
        return total;
    }

    /// <summary>The Σ of every Stock-in-Hand ledger's signed (Dr +) opening balance.</summary>
    public static decimal StockLedgerSignedOpenings(Company company)
    {
        var total = 0m;
        foreach (var ledger in company.Ledgers)
            if (ClassificationRules.IsStockInHandLedger(ledger, company))
                total += ledger.SignedOpening;
        return total;
    }

    /// <summary>
    /// IV-356: on the inventory-derived basis every posting to a Stock-in-Hand ledger is ignored (the derived value
    /// replaces it), so the P&amp;L side of that same posting must be ignored too — otherwise a book that ALSO posts
    /// its closing stock (Dr Stock-in-Hand / Cr a Direct Income adjustment ledger) counts closing stock twice and the
    /// Balance Sheet stops balancing (T2-177 finding 3, probe P4).
    ///
    /// <para>Returns, per P&amp;L ledger id, the SIGNED amount (Dr +) to remove from that ledger's figure. In each
    /// counted voucher whose Stock-in-Hand legs net to S ≠ 0, the P&amp;L legs on the OPPOSITE side are the contra;
    /// they are removed up to |S| in total, pro rata when they exceed it (rounded to the paisa, remainder on the last
    /// leg). A two-leg closing-stock journal is therefore removed exactly. The voucher filter is the one
    /// <see cref="LedgerBalances"/> uses for the same figure: in-window movement when a window is set, else the
    /// cumulative as-of figure under the optional scenario.</para>
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> StockPostingContra(
        Company company, DateOnly to, Scenario? scenario, DateOnly? windowFrom)
    {
        var result = new Dictionary<Guid, decimal>();
        var stockLedgers = new HashSet<Guid>();
        var profitAndLossLedgers = new HashSet<Guid>();
        foreach (var ledger in company.Ledgers)
        {
            if (ClassificationRules.IsStockInHandLedger(ledger, company))
            {
                stockLedgers.Add(ledger.Id);
                continue;
            }
            var group = company.FindGroup(ledger.GroupId);
            if (group is not null && ClassificationRules.IsProfitAndLossGroup(group, company))
                profitAndLossLedgers.Add(ledger.Id);
        }
        if (stockLedgers.Count == 0) return result;

        foreach (var v in company.Vouchers)
        {
            bool counts;
            if (windowFrom is { } from)
                counts = LedgerBalances.CountsAsOf(v, to, company.FindVoucherType(v.TypeId)?.BaseType) && v.Date >= from;
            else if (scenario is not null)
                counts = LedgerBalances.CountsAsOf(v, to, scenario, company);
            else
                counts = LedgerBalances.CountsAsOf(v, to, company.FindVoucherType(v.TypeId)?.BaseType);
            if (!counts) continue;

            var stock = 0m;
            foreach (var line in v.Lines)
                if (stockLedgers.Contains(line.LedgerId))
                    stock += line.Signed;
            if (stock == 0m) continue;

            var contra = v.Lines
                .Where(l => profitAndLossLedgers.Contains(l.LedgerId) && Math.Sign(l.Signed) == -Math.Sign(stock))
                .ToList();
            if (contra.Count == 0) continue;

            var contraTotal = contra.Sum(l => Math.Abs(l.Signed));
            var target = Math.Min(Math.Abs(stock), contraTotal);
            var allocated = 0m;
            for (var i = 0; i < contra.Count; i++)
            {
                var magnitude = i == contra.Count - 1
                    ? target - allocated
                    : Math.Round(Math.Abs(contra[i].Signed) * target / contraTotal, 2, MidpointRounding.AwayFromZero);
                allocated += magnitude;
                var signed = Math.Sign(contra[i].Signed) * magnitude;
                result[contra[i].LedgerId] = result.TryGetValue(contra[i].LedgerId, out var prior) ? prior + signed : signed;
            }
        }
        return result;
    }
}
