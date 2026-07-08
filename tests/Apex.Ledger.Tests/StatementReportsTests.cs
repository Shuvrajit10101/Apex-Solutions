using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// RQ-5 (part 1) — the three Statements reports (Cash Flow, Funds Flow, Ratio Analysis). Each is a
/// pure, UI-independent projection composed from the existing statement builders / LedgerBalances.
/// The two deterministic fixtures (Robert accounts-only, Bright trading) are the regression baseline (R8).
/// </summary>
public class StatementReportsTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // ---------------------------------------------------------------- Cash Flow

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_bright_net_movement_reconciles_opening_to_closing_cash_and_bank()
    {
        var f = Bright();
        var from = new DateOnly(2021, 4, 1);
        var to = f.AsOf;

        var cf = CashFlow.Build(f.Company, new PeriodRange(from, to));

        // Bright: Cash 20,000 → 27,000 (+7,000); HDFC Bank 50,000 → 53,000 (+3,000).
        // Opening cash+bank = 70,000; closing = 80,000; net movement = +10,000.
        Assert.Equal(Money.FromRupees(70000m), cf.OpeningBalance);
        Assert.Equal(Money.FromRupees(80000m), cf.ClosingBalance);
        Assert.Equal(Money.FromRupees(10000m), cf.NetCashFlow);

        // The reconciliation identity: opening + net = closing, to the paisa.
        Assert.Equal(cf.ClosingBalance, cf.OpeningBalance + cf.NetCashFlow);
        Assert.True(cf.Reconciles);

        // Net = inflows − outflows.
        Assert.Equal(cf.NetCashFlow, cf.TotalInflows - cf.TotalOutflows);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_robert_reconciles_and_lines_net_to_movement()
    {
        var f = Robert();
        var cf = CashFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        Assert.True(cf.Reconciles);
        Assert.Equal(cf.ClosingBalance, cf.OpeningBalance + cf.NetCashFlow);

        // Every inflow/outflow section line's signed sum equals the net movement.
        var lineNet = 0m;
        foreach (var s in cf.Inflows) lineNet += s.Amount.Amount;
        foreach (var s in cf.Outflows) lineNet -= s.Amount.Amount;
        Assert.Equal(cf.NetCashFlow.Amount, lineNet);
    }

    // ---------------------------------------------------------------- Funds Flow

    [Fact]
    [Trait("Category", "Fixture")]
    public void FundsFlow_bright_sources_equal_applications()
    {
        var f = Bright();
        var ff = FundsFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        // A funds-flow statement always balances: total sources == total applications.
        Assert.Equal(ff.TotalSources, ff.TotalApplications);
        Assert.True(ff.Balanced);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void FundsFlow_robert_sources_equal_applications()
    {
        var f = Robert();
        var ff = FundsFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        Assert.Equal(ff.TotalSources, ff.TotalApplications);
        Assert.True(ff.Balanced);
    }

    // ---------------------------------------------------------------- Ratio Analysis

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_bright_gross_profit_percent_and_current_ratio()
    {
        var f = Bright();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // Working capital figures (Bright, closing):
        //   Current Assets = Cash 27,000 + HDFC 53,000 + Closing Stock 15,000 + Debtor 35,000 = 130,000
        //   Current Liabilities = Sundry Creditors 35,000
        //   Working capital = 95,000; Current ratio = 130000/35000 ≈ 3.714…
        Assert.Equal(Money.FromRupees(130000m), ra.CurrentAssets);
        Assert.Equal(Money.FromRupees(35000m), ra.CurrentLiabilities);
        Assert.Equal(Money.FromRupees(95000m), ra.WorkingCapital);
        Assert.Equal(Math.Round(130000m / 35000m, 4), Math.Round(ra.CurrentRatio!.Value, 4));

        // Quick ratio = (CA − closing stock) / CL = (130000 − 15000)/35000 = 115000/35000.
        Assert.Equal(Math.Round(115000m / 35000m, 4), Math.Round(ra.QuickRatio!.Value, 4));

        // Gross profit % = GP / Sales × 100 = 15,000 / 73,000 × 100.
        Assert.Equal(Money.FromRupees(15000m), ra.GrossProfit);
        Assert.Equal(Math.Round(15000m / 73000m * 100m, 4), Math.Round(ra.GrossProfitPercent!.Value, 4));

        // Net profit % = NP / Sales × 100 = −1,000 / 73,000 × 100 (Bright makes a small loss).
        Assert.Equal(Math.Round(-1000m / 73000m * 100m, 4), Math.Round(ra.NetProfitPercent!.Value, 4));
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_bright_new_ratios_match_hand_computed_values()
    {
        var f = Bright();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // Principal-group figures we newly expose (Bright, closing):
        //   Sundry Debtors  = Ram & Co 35,000 ; Sundry Creditors = Shyam Traders 35,000
        //   Capital Account = Bright's Capital 150,000 (EXCLUDES the folded −1,000 period net profit)
        Assert.Equal(Money.FromRupees(35000m), ra.SundryDebtors);
        Assert.Equal(Money.FromRupees(35000m), ra.SundryCreditors);
        Assert.Equal(Money.FromRupees(150000m), ra.CapitalAccount);
        // Proprietor's funds = Capital + Nett Profit = 150,000 + (−1,000) = 149,000.
        Assert.Equal(Money.FromRupees(149000m), ra.ProprietorsFunds);

        // Working Capital Turnover = Sales / Working Capital = 73,000 / 95,000.
        Assert.Equal(Math.Round(73000m / 95000m, 4), Math.Round(ra.WorkingCapitalTurnover!.Value, 4));

        // Operating Cost % = 100 − Nett Profit % = 100 − (−1,000/73,000×100) ≈ 101.3699…
        var expectedNetProfitPct = -1000m / 73000m * 100m;
        Assert.Equal(Math.Round(100m - expectedNetProfitPct, 4), Math.Round(ra.OperatingCostPercent!.Value, 4));

        // Receivables Turnover (days) = Sundry Debtors ÷ Sales × days-in-period.
        // Default window = books-begin 2021-04-01 → as-of 2022-03-31 (inclusive) = 365 days.
        //   35,000 ÷ 73,000 × 365 = 12,775,000 ÷ 73,000 = 175 days exactly.
        Assert.Equal(175m, Math.Round(ra.ReceivablesTurnoverDays!.Value, 4));

        // Return on Working Capital % = Nett Profit ÷ Working Capital × 100 = −1,000 / 95,000 × 100.
        Assert.Equal(Math.Round(-1000m / 95000m * 100m, 4), Math.Round(ra.ReturnOnWorkingCapitalPercent!.Value, 4));

        // CORRECTED Return on Investment % = Nett Profit ÷ (Capital + Nett Profit) × 100
        //   = −1,000 / (150,000 + (−1,000)) × 100 = −1,000 / 149,000 × 100  (NOT capital-employed incl. loans).
        Assert.Equal(Math.Round(-1000m / 149000m * 100m, 4), Math.Round(ra.ReturnOnInvestmentPercent!.Value, 4));

        // Debt/Equity = Loans ÷ (Capital + Nett Profit); Bright has no loans → 0 / 149,000 = 0.
        Assert.Equal(0m, ra.DebtEquityRatio!.Value);

        // The two render-ready columns are populated and label-complete.
        Assert.Contains(ra.PrincipalGroups, g => g.Label == "Sundry Debtors");
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Working Capital Turnover" && r.Unit == RatioUnit.Ratio);
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Receivables Turnover (days)" && r.Unit == RatioUnit.Days);
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Operating Cost %" && r.Unit == RatioUnit.Percent);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_guards_divide_by_zero()
    {
        // Robert (transport, accounts-only) has NO Sales Accounts and NO Stock-in-Hand, so every ratio whose
        // denominator is Sales or Stock is a guarded divide-by-zero → null (N/A), never a throw. Balance-based
        // ratios (proprietor's funds / current liabilities non-zero) still yield a value.
        var f = Robert();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // The projection never throws and yields a full row set; each ratio is either a value or null.
        Assert.NotNull(ra);

        // A ratio is null ONLY when its own denominator is zero. Robert has Sales = 0 and Stock = 0, so
        // ratios dividing BY Sales or Stock are null; ratios dividing by a non-zero balance are a real number.
        Assert.Equal(Money.Zero, ra.Sales);

        // Denominator = Sales (0) → null.
        Assert.Null(ra.GrossProfitPercent);
        Assert.Null(ra.NetProfitPercent);
        Assert.Null(ra.OperatingCostPercent);           // derives from Nett Profit % → null when Sales = 0
        Assert.Null(ra.ReceivablesTurnoverDays);

        // Denominator = Stock-in-Hand (0) → null.
        Assert.Null(ra.InventoryTurnover);

        // Denominator = Working Capital (65,000 ≠ 0) → a real number, NOT null (the guard only nulls a
        // ZERO denominator). Working-Capital Turnover numerator is Sales (0) → exactly 0.
        Assert.Equal(0m, ra.WorkingCapitalTurnover!.Value);          // Sales 0 ÷ WC 65,000 = 0
        // Return on Working Capital = Nett Profit 5,000 ÷ WC 65,000 × 100 (Robert earns a small profit).
        Assert.Equal(Math.Round(5000m / 65000m * 100m, 4), Math.Round(ra.ReturnOnWorkingCapitalPercent!.Value, 4));

        // ROI denominator = Capital + Nett Profit ≠ 0 → present (no throw).
        Assert.NotNull(ra.ReturnOnInvestmentPercent);
        _ = ra.DebtEquityRatio;
    }
}
