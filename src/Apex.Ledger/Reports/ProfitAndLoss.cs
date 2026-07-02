using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>How the closing-stock figure is sourced (design §7.3).</summary>
public enum ClosingStockMode
{
    /// <summary>Phase 1: closing stock is whatever the fixture posts to a Stock-in-Hand ledger.</summary>
    AsPostedLedger,

    /// <summary>Phase 3: closing stock is derived from the inventory engine.</summary>
    InventoryDerived,
}

/// <summary>A named P&amp;L amount (income or expense) at its statement magnitude.</summary>
public sealed record ProfitAndLossLine(string LedgerName, Money Amount);

/// <summary>
/// The Trading + Profit &amp; Loss projection (design §7.3). Uses periodic inventory:
/// opening stock (the opening debit balance of a Stock-in-Hand ledger) is a charge to the
/// trading account, and closing stock (posted as a Stock-in-Hand ledger movement with a
/// matching income-side adjustment) is a credit. Robert has no stock ledgers, so the rule
/// degenerates to income − expense. Bright's opening-stock charge yields the −1000 net.
/// </summary>
public sealed record ProfitAndLoss(
    IReadOnlyList<ProfitAndLossLine> Income,
    Money TotalIncome,
    IReadOnlyList<ProfitAndLossLine> Expenses,
    Money TotalExpenses,
    Money OpeningStock,
    Money ClosingStock,
    Money GrossProfit,
    Money NetProfit)
{
    /// <summary>Builds the P&amp;L over a period ending at <paramref name="to"/>.</summary>
    public static ProfitAndLoss Build(
        Company company,
        DateOnly to,
        ClosingStockMode mode = ClosingStockMode.AsPostedLedger)
    {
        _ = mode; // Phase 1 supports AsPostedLedger; InventoryDerived is a Phase-3 seam.

        var income = new List<ProfitAndLossLine>();
        var expenses = new List<ProfitAndLossLine>();
        var totalIncome = 0m;
        var totalExpense = 0m;
        var openingStock = 0m;
        var closingStock = 0m;

        foreach (var ledger in company.Ledgers)
        {
            var group = company.FindGroup(ledger.GroupId)
                ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");

            // Stock-in-Hand ledgers get the trading treatment, not the P&L-group treatment.
            // Opening stock (opening debit balance) is a charge to the trading account.
            // Closing stock is injected via a paired income-side adjustment ledger (Dr asset /
            // Cr Direct-Income adjustment), so the income arm already carries the closing-stock
            // credit; the asset-ledger movement is reported for display only, never re-added.
            if (ClassificationRules.IsStockInHandLedger(ledger, company))
            {
                if (ledger.OpeningIsDebit && ledger.OpeningBalance != Money.Zero)
                    openingStock += ledger.OpeningBalance.Amount;

                var closingSigned = LedgerBalances.SignedClosing(company, ledger, to);
                var movement = closingSigned - ledger.SignedOpening;
                if (movement > 0m) closingStock += movement;
                continue;
            }

            if (!ClassificationRules.IsProfitAndLossGroup(group, company))
                continue; // Balance-Sheet ledger

            var nature = ClassificationRules.PrimaryNatureOf(group, company);
            var signed = LedgerBalances.SignedClosing(company, ledger, to);

            if (nature == GroupNature.Income)
            {
                // Income sits on the credit side; magnitude = −signed (credit is negative).
                var magnitude = -signed;
                if (magnitude != 0m)
                {
                    income.Add(new ProfitAndLossLine(ledger.Name, new Money(magnitude)));
                    totalIncome += magnitude;
                }
            }
            else // Expense
            {
                var magnitude = signed; // debit side, positive
                if (magnitude != 0m)
                {
                    expenses.Add(new ProfitAndLossLine(ledger.Name, new Money(magnitude)));
                    totalExpense += magnitude;
                }
            }
        }

        // Trading + P&L: opening stock is an added charge. Closing stock is already in the
        // income arm via its adjustment ledger, so only opening stock adjusts the totals here.
        var net = totalIncome - (totalExpense + openingStock);

        // Gross profit (periodic inventory): sales + direct income (incl. closing-stock
        // adjustment) − opening − purchases − direct expenses.
        var gross = ComputeGrossProfit(company, to, openingStock);

        return new ProfitAndLoss(
            income,
            new Money(totalIncome),
            expenses,
            new Money(totalExpense),
            new Money(openingStock),
            new Money(closingStock),
            new Money(gross),
            new Money(net));
    }

    private static decimal ComputeGrossProfit(Company company, DateOnly to, decimal openingStock)
    {
        decimal salesAndDirectIncome = 0m; // Sales + Direct Incomes (the latter includes the closing-stock adjustment)
        decimal purchasesAndDirectExpense = 0m;

        foreach (var ledger in company.Ledgers)
        {
            if (ClassificationRules.IsStockInHandLedger(ledger, company)) continue;

            var group = company.FindGroup(ledger.GroupId);
            if (group is null || !ClassificationRules.IsProfitAndLossGroup(group, company)) continue;

            var primary = ClassificationRules.PrimaryAncestorOf(group, company);
            var signed = LedgerBalances.SignedClosing(company, ledger, to);

            switch (primary.Name)
            {
                case "Sales Accounts":
                case "Direct Incomes":
                    salesAndDirectIncome += -signed; // credit magnitude
                    break;
                case "Purchase Accounts":
                case "Direct Expenses":
                    purchasesAndDirectExpense += signed; // debit magnitude
                    break;
            }
        }

        return salesAndDirectIncome - openingStock - purchasesAndDirectExpense;
    }
}
