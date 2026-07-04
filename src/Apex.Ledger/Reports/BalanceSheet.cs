using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>A Balance-Sheet line: a ledger (or synthetic head) at its statement magnitude.</summary>
public sealed record BalanceSheetLine(string Name, string GroupName, Money Amount);

/// <summary>
/// The Balance Sheet (design §7.4). Every non-P&amp;L ledger's closing balance, split
/// Liabilities vs Assets by its primary ancestor's nature, plus the period net profit
/// folded into the capital/P&amp;L side. Stock-in-Hand ledgers contribute only their
/// closing movement (opening stock is consumed into the trading account).
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
    /// Builds the Balance Sheet as of a date. When <paramref name="scenario"/> is non-<c>null</c> the
    /// figures are computed under that scenario (catalog §7); a <c>null</c> scenario yields the plain
    /// actual Balance Sheet (unchanged behaviour).
    /// </summary>
    public static BalanceSheet Build(Company company, DateOnly asOf, Scenario? scenario = null)
    {
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

            var signed = LedgerBalances.SignedClosing(company, ledger, asOf, scenario);

            // Stock-in-Hand: opening stock is consumed into the trading account, so only the
            // closing movement (closing − opening) remains on the Balance Sheet.
            if (ClassificationRules.IsStockInHandLedger(ledger, company))
                signed -= ledger.SignedOpening;

            if (signed == 0m) continue;

            var primaryNature = ClassificationRules.PrimaryNatureOf(group, company);

            if (primaryNature == GroupNature.Asset)
            {
                // Assets sit on the debit side; magnitude = signed (positive when debit).
                var magnitude = signed;
                assets.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(magnitude)));
                totalAsset += magnitude;
            }
            else // Liability (Capital, Loans, Current Liabilities, Suspense, …)
            {
                var magnitude = -signed; // credit side positive
                liabilities.Add(new BalanceSheetLine(ledger.Name, group.Name, new Money(magnitude)));
                totalLiab += magnitude;
            }
        }

        // Fold the period net profit into the capital / P&L side (under the same scenario).
        var pl = ProfitAndLoss.Build(company, asOf, ClosingStockMode.AsPostedLedger, scenario);
        var netProfit = pl.NetProfit;
        liabilities.Add(new BalanceSheetLine("Net Profit (period)", "Profit & Loss A/c", netProfit));
        totalLiab += netProfit.Amount;

        return new BalanceSheet(liabilities, new Money(totalLiab), assets, new Money(totalAsset), netProfit);
    }
}
