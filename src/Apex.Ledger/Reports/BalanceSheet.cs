using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// A Balance-Sheet line: a ledger (or synthetic head) at its statement magnitude. <see cref="GroupId"/>
/// is the ledger's actual immediate group id — the reliable key for group-membership tests (two distinct
/// groups may share a name, so classifying by name alone is ambiguous). It is <c>null</c> only for the two
/// synthetic heads (derived Stock-in-Hand, folded period Net Profit) that have no single owning ledger.
/// </summary>
public sealed record BalanceSheetLine(string Name, string GroupName, Money Amount, Guid? GroupId = null);

/// <summary>
/// The Balance Sheet (design §7.4). Every non-P&amp;L ledger's closing balance, split
/// Liabilities vs Assets by its primary ancestor's nature, plus the period net profit
/// folded into the capital/P&amp;L side. Stock-in-Hand ledgers contribute only their
/// closing movement (opening stock is consumed into the trading account) under
/// <see cref="ClosingStockMode.AsPostedLedger"/>; under <see cref="ClosingStockMode.InventoryDerived"/> the
/// Stock-in-Hand asset is the single <b>derived</b> Σ item-closing-value line (RQ-25/RQ-26).
/// <see cref="TotalLiabilities"/> == <see cref="TotalAssets"/> by construction (§6).
/// </summary>
public sealed record BalanceSheet(
    IReadOnlyList<BalanceSheetLine> Liabilities,
    Money TotalLiabilities,
    IReadOnlyList<BalanceSheetLine> Assets,
    Money TotalAssets,
    Money NetProfitInCapital)
{
    public bool Balanced => TotalLiabilities == TotalAssets;

    /// <summary>
    /// Builds the Balance Sheet as of a date. When <paramref name="mode"/> is
    /// <see cref="ClosingStockMode.InventoryDerived"/> the Stock-in-Hand asset is the derived Σ item-closing
    /// value (RQ-25/RQ-26) and the folded net profit is computed on the same derived closing stock, so the
    /// statement still balances to the paisa (assets incl. derived closing stock = liabilities incl. the
    /// profit effect of closing stock). When <paramref name="scenario"/> is non-<c>null</c> the figures are
    /// computed under that scenario (catalog §7); a <c>null</c> scenario yields the plain actual sheet.
    /// </summary>
    public static BalanceSheet Build(
        Company company,
        DateOnly asOf,
        ClosingStockMode mode = ClosingStockMode.AsPostedLedger,
        Scenario? scenario = null)
    {
        return BuildCore(company, asOf, mode, scenario);
    }

    /// <summary>
    /// Builds the Balance Sheet under <see cref="ReportOptions"/> (RQ-1 as-of, RQ-6 closing-stock basis).
    /// The closing-stock valuation basis (<see cref="ReportOptions.ClosingStock"/>) and the scenario pass
    /// through unchanged. Default options reproduce the legacy build exactly.
    /// </summary>
    public static BalanceSheet Build(Company company, DateOnly asOf, ReportOptions options)
        => BuildCore(company, asOf, options.ClosingStock, options.Scenario);

    private static BalanceSheet BuildCore(
        Company company,
        DateOnly asOf,
        ClosingStockMode mode,
        Scenario? scenario)
    {
        var inventoryDerived = mode == ClosingStockMode.InventoryDerived;

        var liabilities = new List<BalanceSheetLine>();
        var assets = new List<BalanceSheetLine>();
        var totalLiab = 0m;
        var totalAsset = 0m;

        foreach (var ledger in company.Ledgers)
        {
            var group = company.FindGroup(ledger.GroupId)
                ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");

            // P&L ledgers do not appear on the Balance Sheet (their net flows into capital).
            if (ClassificationRules.IsProfitAndLossGroup(group, company))
                continue;

            // Stock-in-Hand ledgers: under InventoryDerived, every one is suppressed here and replaced by a
            // single derived Stock-in-Hand asset line below (no manual closing-stock ledger participates).
            if (ClassificationRules.IsStockInHandLedger(ledger, company))
            {
                if (inventoryDerived) continue;
                // AsPostedLedger: opening stock is consumed into the trading account, so only the closing
                // movement (closing − opening) remains on the Balance Sheet.
                var signedStock = LedgerBalances.SignedClosing(company, ledger, asOf, scenario) - ledger.SignedOpening;
                if (signedStock == 0m) continue;
                var stockNature = ClassificationRules.PrimaryNatureOf(group, company);
                if (stockNature == GroupNature.Asset)
                {
                    assets.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(signedStock), group.Id));
                    totalAsset += signedStock;
                }
                else
                {
                    liabilities.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(-signedStock), group.Id));
                    totalLiab += -signedStock;
                }
                continue;
            }

            var signed = LedgerBalances.SignedClosing(company, ledger, asOf, scenario);
            if (signed == 0m) continue;

            var primaryNature = ClassificationRules.PrimaryNatureOf(group, company);

            if (primaryNature == GroupNature.Asset)
            {
                // Assets sit on the debit side; magnitude = signed (positive when debit).
                var magnitude = signed;
                assets.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(magnitude), group.Id));
                totalAsset += magnitude;
            }
            else // Liability (Capital, Loans, Current Liabilities, Suspense, …)
            {
                var magnitude = -signed; // credit side positive
                liabilities.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(magnitude), group.Id));
                totalLiab += magnitude;
            }
        }

        // Derived Stock-in-Hand: one asset line = Σ each item's closing value by its own method (RQ-25/26).
        if (inventoryDerived)
        {
            var derived = new StockValuationService(company).TotalClosingStockValue(asOf).Amount;
            if (derived != 0m)
            {
                assets.Add(new BalanceSheetLine("Stock-in-Hand", "Stock-in-Hand", new Money(derived)));
                totalAsset += derived;
            }
        }

        // Fold the period net profit into the capital / P&L side (under the same mode + scenario).
        var pl = ProfitAndLoss.Build(company, asOf, mode, scenario);
        var netProfit = pl.NetProfit;
        liabilities.Add(new BalanceSheetLine("Net Profit (period)", "Profit & Loss A/c", netProfit));
        totalLiab += netProfit.Amount;

        return new BalanceSheet(liabilities, new Money(totalLiab), assets, new Money(totalAsset), netProfit);
    }
}
