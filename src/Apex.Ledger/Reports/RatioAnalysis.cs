using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>The presentation unit of a <see cref="PrincipalRatioLine"/> (drives the suffix a UI appends).</summary>
public enum RatioUnit
{
    /// <summary>A pure ratio, e.g. Current Ratio 3.71 — rendered as a bare number (often as "3.71 : 1").</summary>
    Ratio,
    /// <summary>A percentage already multiplied by 100, e.g. Gross Profit 20.55 — rendered with a "%" suffix.</summary>
    Percent,
    /// <summary>A count of days, e.g. Receivables Turnover 175 — rendered with a " days" suffix.</summary>
    Days,
}

/// <summary>One <b>Principal Group</b> figure (left column of Tally's Ratio Analysis): a label and a Money amount.</summary>
public sealed record PrincipalGroupLine(string Label, Money Value);

/// <summary>
/// One <b>Principal Ratio</b> (right column of Tally's Ratio Analysis): a label, a nullable value
/// (<c>null</c> = "N/A", i.e. a guarded zero denominator), and the <see cref="RatioUnit"/> that fixes how a
/// UI renders it.
/// </summary>
public sealed record PrincipalRatioLine(string Label, decimal? Value, RatioUnit Unit);

/// <summary>
/// The <b>Ratio Analysis</b> report (catalog §16), modelled on Tally Prime's actual report which is split into
/// two columns: <see cref="PrincipalGroups"/> (the key figures) on the left and <see cref="PrincipalRatios"/>
/// (the ratios that relate those figures) on the right. Every ratio guards against a zero denominator: its
/// value is <c>null</c> ("N/A") when the denominator is zero, never a divide-by-zero throw.
/// <para>Working-capital figures come from the closing Balance Sheet asset/liability lines classified by their
/// ledger's actual <b>group id</b> (Current Assets / Current Liabilities / Loans / Capital / Stock-in-Hand /
/// Sundry Debtors / Sundry Creditors). Profitability figures (gross/net profit, sales) come from the Trading &amp;
/// P&amp;L. All money is exact decimal rupees; ratios are exact decimals (percentages are ×100).</para>
/// <para><b>Verified against Tally Prime</b> (help.tallysolutions.com, Principal Ratios): Current Ratio
/// (CA:CL), Quick Ratio ((CA−Stock):CL), Debt/Equity (Loans:(Capital+NettProfit)), Gross Profit % (GP/Turnover),
/// Nett Profit % (NP/Turnover), Operating Cost % (100 − NettProfit %, i.e. operating cost as a % of Sales),
/// Receivables Turnover in days (Debtors ÷ Sales × days-in-period), Return on Investment %
/// (NettProfit ÷ (Capital + NettProfit) × 100), Return on Working Capital % (NettProfit ÷ WorkingCapital × 100),
/// Inventory Turnover (Turnover ÷ Stock), Working Capital Turnover (Sales ÷ Working Capital).</para>
/// </summary>
public sealed record RatioAnalysis(
    // ---- Principal-group figures (typed, for tests / direct access) ----
    Money CurrentAssets,
    Money CurrentLiabilities,
    Money WorkingCapital,
    Money Inventory,
    Money Sales,
    Money GrossProfit,
    Money NetProfit,
    Money ProprietorsFunds,
    Money LongTermDebt,
    Money SundryDebtors,
    Money SundryCreditors,
    Money CapitalAccount,
    // ---- Principal ratios (typed, for tests / direct access) ----
    decimal? CurrentRatio,
    decimal? QuickRatio,
    decimal? DebtEquityRatio,
    decimal? GrossProfitPercent,
    decimal? NetProfitPercent,
    decimal? OperatingCostPercent,
    decimal? ReceivablesTurnoverDays,
    decimal? ReturnOnInvestmentPercent,
    decimal? ReturnOnWorkingCapitalPercent,
    decimal? InventoryTurnover,
    decimal? WorkingCapitalTurnover,
    // ---- The two render-ready columns (Tally layout) ----
    IReadOnlyList<PrincipalGroupLine> PrincipalGroups,
    IReadOnlyList<PrincipalRatioLine> PrincipalRatios)
{
    /// <summary>Divides guarding a zero denominator (returns <c>null</c> = N/A).</summary>
    private static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0m ? (decimal?)null : numerator / denominator;

    /// <summary>Builds the Ratio-Analysis report as-at <paramref name="asOf"/> under default report options.</summary>
    public static RatioAnalysis Build(Company company, DateOnly asOf)
        => Build(company, asOf, ReportOptions.AsOf(asOf));

    /// <summary>
    /// Builds the Ratio-Analysis report as-at <paramref name="asOf"/>. The P&amp;L / sales window follows
    /// <paramref name="options"/> (RQ-1 period); the balance figures are as-at <paramref name="asOf"/>.
    /// </summary>
    public static RatioAnalysis Build(Company company, DateOnly asOf, ReportOptions options)
    {
        var bs = BalanceSheet.Build(company, asOf, options);
        var pl = ProfitAndLoss.Build(company, asOf, options);

        // Classify each Balance-Sheet asset/liability line by its ledger's ACTUAL group id (not a name match:
        // two distinct groups may share a display name). The synthetic derived Stock-in-Hand and folded
        // Net-Profit heads have a null group id — handled explicitly below.
        var currentAssets = 0m;
        var inventory = 0m;
        var sundryDebtors = 0m;
        foreach (var line in bs.Assets)
        {
            var isStock = ClassificationRules.GroupIsUnder(line.GroupId, "Stock-in-Hand", company)
                          || (line.GroupId is null && line.GroupName == "Stock-in-Hand"); // derived Stock-in-Hand head
            if (isStock)
            {
                inventory += line.Amount.Amount;
                currentAssets += line.Amount.Amount; // stock is a current asset
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Current Assets", company))
            {
                currentAssets += line.Amount.Amount;
                if (ClassificationRules.GroupIsUnder(line.GroupId, "Sundry Debtors", company))
                    sundryDebtors += line.Amount.Amount;
            }
        }

        var currentLiabilities = 0m;
        var sundryCreditors = 0m;
        var longTermDebt = 0m;
        var capitalAccount = 0m;   // Capital Account ledgers only (excludes folded Net Profit)
        var proprietorsFunds = 0m; // Capital Account + folded period Net Profit (= Tally "Capital + Nett Profit")
        foreach (var line in bs.Liabilities)
        {
            if (ClassificationRules.GroupIsUnder(line.GroupId, "Current Liabilities", company))
            {
                currentLiabilities += line.Amount.Amount;
                if (ClassificationRules.GroupIsUnder(line.GroupId, "Sundry Creditors", company))
                    sundryCreditors += line.Amount.Amount;
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Loans (Liability)", company))
            {
                longTermDebt += line.Amount.Amount;
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Capital Account", company))
            {
                capitalAccount += line.Amount.Amount;
                proprietorsFunds += line.Amount.Amount;
            }
            else if (line.GroupId is null && line.GroupName == "Profit & Loss A/c")
            {
                // The folded period Net Profit head: part of proprietor's funds, but NOT the Capital Account.
                proprietorsFunds += line.Amount.Amount;
            }
        }

        var workingCapital = currentAssets - currentLiabilities;
        var quickAssets = currentAssets - inventory;

        var sales = SalesOf(company, asOf, options);  // net turnover: ledgers under the Sales Accounts primary
        var grossProfit = pl.GrossProfit.Amount;
        var netProfit = pl.NetProfit.Amount;

        // Tally Return-on-Investment denominator = Capital Account + Nett Profit (NOT capital-employed incl. loans).
        // proprietorsFunds already equals (Capital Account + folded Net Profit), which is exactly that figure.
        var roiDenominator = proprietorsFunds;

        var netProfitPercent = Ratio(netProfit * 100m, sales);
        // Operating Cost % = operating cost as a % of Sales = 100 − Nett Profit % (Tally definition). N/A when
        // there are no sales (Nett Profit % itself is N/A).
        var operatingCostPercent = netProfitPercent is { } np ? 100m - np : (decimal?)null;

        // Receivables Turnover in days = (Sundry Debtors ÷ Sales) × days-in-period (inclusive window).
        var window = options.EffectivePeriod(company);
        var daysInPeriod = window.To.DayNumber - window.From.DayNumber + 1;
        var receivablesTurnoverDays = Ratio(sundryDebtors * daysInPeriod, sales);

        var currentRatio = Ratio(currentAssets, currentLiabilities);
        var quickRatio = Ratio(quickAssets, currentLiabilities);
        var debtEquityRatio = Ratio(longTermDebt, proprietorsFunds);
        var grossProfitPercent = Ratio(grossProfit * 100m, sales);
        var returnOnInvestmentPercent = Ratio(netProfit * 100m, roiDenominator);
        var returnOnWorkingCapitalPercent = Ratio(netProfit * 100m, workingCapital);
        var inventoryTurnover = Ratio(sales, inventory);
        var workingCapitalTurnover = Ratio(sales, workingCapital);

        var principalGroups = new List<PrincipalGroupLine>
        {
            new("Working Capital", new Money(workingCapital)),
            new("Current Assets", new Money(currentAssets)),
            new("Current Liabilities", new Money(currentLiabilities)),
            new("Sundry Debtors", new Money(sundryDebtors)),
            new("Sundry Creditors", new Money(sundryCreditors)),
            new("Stock-in-Hand", new Money(inventory)),
            new("Sales Accounts", new Money(sales)),
            new("Capital Account", new Money(capitalAccount)),
            new("Nett Profit", new Money(netProfit)),
        };

        var principalRatios = new List<PrincipalRatioLine>
        {
            new("Current Ratio", currentRatio, RatioUnit.Ratio),
            new("Quick Ratio", quickRatio, RatioUnit.Ratio),
            new("Debt / Equity Ratio", debtEquityRatio, RatioUnit.Ratio),
            new("Gross Profit %", grossProfitPercent, RatioUnit.Percent),
            new("Nett Profit %", netProfitPercent, RatioUnit.Percent),
            new("Operating Cost %", operatingCostPercent, RatioUnit.Percent),
            new("Receivables Turnover (days)", receivablesTurnoverDays, RatioUnit.Days),
            new("Return on Investment %", returnOnInvestmentPercent, RatioUnit.Percent),
            new("Return on Working Capital %", returnOnWorkingCapitalPercent, RatioUnit.Percent),
            new("Inventory Turnover", inventoryTurnover, RatioUnit.Ratio),
            new("Working Capital Turnover", workingCapitalTurnover, RatioUnit.Ratio),
        };

        return new RatioAnalysis(
            new Money(currentAssets),
            new Money(currentLiabilities),
            new Money(workingCapital),
            new Money(inventory),
            new Money(sales),
            new Money(grossProfit),
            new Money(netProfit),
            new Money(proprietorsFunds),
            new Money(longTermDebt),
            new Money(sundryDebtors),
            new Money(sundryCreditors),
            new Money(capitalAccount),
            CurrentRatio: currentRatio,
            QuickRatio: quickRatio,
            DebtEquityRatio: debtEquityRatio,
            GrossProfitPercent: grossProfitPercent,
            NetProfitPercent: netProfitPercent,
            OperatingCostPercent: operatingCostPercent,
            ReceivablesTurnoverDays: receivablesTurnoverDays,
            ReturnOnInvestmentPercent: returnOnInvestmentPercent,
            ReturnOnWorkingCapitalPercent: returnOnWorkingCapitalPercent,
            InventoryTurnover: inventoryTurnover,
            WorkingCapitalTurnover: workingCapitalTurnover,
            PrincipalGroups: principalGroups,
            PrincipalRatios: principalRatios);
    }

    /// <summary>
    /// Net sales turnover: Σ credit-side movement of every ledger whose primary ancestor is <b>Sales Accounts</b>,
    /// over the report window (or as-at when no window). Excludes the closing-stock adjustment (a Direct Income),
    /// so it is the pure turnover the profitability ratios divide by.
    /// <para><b>Known limitation (same as P&amp;L):</b> when a scenario is selected together with an explicit
    /// period window, sales fall back to un-scenarioed movement — <see cref="LedgerBalances"/> has no
    /// signed-movement overload that also applies a scenario. Scenario-aware period sales is deferred to the
    /// engine slice that adds a scenario+window movement primitive; the as-at path already honours the scenario.</para>
    /// </summary>
    private static decimal SalesOf(Company company, DateOnly asOf, ReportOptions options)
    {
        var window = options.Period;
        var sales = 0m;
        foreach (var ledger in company.Ledgers)
        {
            var group = company.FindGroup(ledger.GroupId);
            if (group is null) continue;
            if (ClassificationRules.PrimaryAncestorOf(group, company).Name != "Sales Accounts") continue;

            var signed = window is { } w
                ? LedgerBalances.SignedMovement(company, ledger, w.From, asOf)
                : LedgerBalances.SignedClosing(company, ledger, asOf, options.Scenario);
            sales += -signed; // credit magnitude
        }
        return sales;
    }
}
