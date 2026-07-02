using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The Robert &amp; Bright regression contract (design §9; R8). Each fixture is loaded, its
/// vouchers posted through the engine, and the engine-computed Trial Balance / P&amp;L /
/// Balance Sheet asserted against the fixture's <c>expected</c> block <b>to the paisa</b>
/// (NFR-3). These replace the Phase-0 skipped stubs.
/// </summary>
public class FixtureLoadTests
{
    // ---------------------------------------------------------------- Robert

    [Fact]
    [Trait("Category", "Fixture")]
    public void Robert_ledger_closings_match_to_the_paisa()
    {
        var f = FixtureLoader.Load("robert.json");
        AssertLedgerClosings(f);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Robert_trial_balance_matches_and_balances()
    {
        var f = FixtureLoader.Load("robert.json");
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        Assert.Equal(137000m, tb.TotalDebit.Amount);
        Assert.Equal(137000m, tb.TotalCredit.Amount);
        Assert.True(tb.Balanced);
        AssertTrialBalanceAgainstFixture(f, tb);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Robert_profit_and_loss_net_profit_is_5000()
    {
        var f = FixtureLoader.Load("robert.json");
        var pl = ProfitAndLoss.Build(f.Company, f.AsOf);

        Assert.Equal(37000m, pl.TotalIncome.Amount);
        Assert.Equal(32000m, pl.TotalExpenses.Amount);
        Assert.Equal(5000m, pl.NetProfit.Amount);

        var expPl = f.Expected.GetProperty("profitAndLoss");
        Assert.Equal(expPl.GetProperty("totalIncome").GetDecimal(), pl.TotalIncome.Amount);
        Assert.Equal(expPl.GetProperty("totalExpenses").GetDecimal(), pl.TotalExpenses.Amount);
        Assert.Equal(expPl.GetProperty("netProfit").GetDecimal(), pl.NetProfit.Amount);

        // Every expected income line matches the engine's, to the paisa.
        foreach (var inc in expPl.GetProperty("income").EnumerateObject())
        {
            var line = pl.Income.FirstOrDefault(i => i.LedgerName == inc.Name);
            Assert.True(line is not null, $"P&L income missing '{inc.Name}'.");
            Assert.Equal(inc.Value.GetDecimal(), line!.Amount.Amount);
        }

        // Every expected expense line matches the engine's, to the paisa.
        foreach (var exp in expPl.GetProperty("expenses").EnumerateObject())
        {
            var line = pl.Expenses.FirstOrDefault(e => e.LedgerName == exp.Name);
            Assert.True(line is not null, $"P&L expense missing '{exp.Name}'.");
            Assert.Equal(exp.Value.GetDecimal(), line!.Amount.Amount);
        }
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Robert_balance_sheet_matches_and_balances()
    {
        var f = FixtureLoader.Load("robert.json");
        var bs = BalanceSheet.Build(f.Company, f.AsOf);

        Assert.Equal(105000m, bs.TotalLiabilities.Amount);
        Assert.Equal(105000m, bs.TotalAssets.Amount);
        Assert.True(bs.Balanced);
        Assert.Equal(5000m, bs.NetProfitInCapital.Amount);

        var expBs = f.Expected.GetProperty("balanceSheet");
        Assert.Equal(expBs.GetProperty("totalLiabilities").GetDecimal(), bs.TotalLiabilities.Amount);
        Assert.Equal(expBs.GetProperty("totalAssets").GetDecimal(), bs.TotalAssets.Amount);

        // Each real asset line matches the fixture, to the paisa.
        Assert.Equal(40000m, bs.Assets.Single(a => a.Name == "Truck").Amount.Amount);
        Assert.Equal(22500m, bs.Assets.Single(a => a.Name == "Cash").Amount.Amount);
        Assert.Equal(32500m, bs.Assets.Single(a => a.Name == "SBI Bank").Amount.Amount);
        Assert.Equal(10000m, bs.Assets.Single(a => a.Name == "Global Traders").Amount.Amount);

        // Capital + net profit on the liabilities side.
        Assert.Equal(100000m, bs.Liabilities.Single(l => l.Name == "Robert's Capital").Amount.Amount);
        Assert.Equal(5000m, bs.Liabilities.Single(l => l.Name == "Net Profit (period)").Amount.Amount);
    }

    // ---------------------------------------------------------------- Bright

    [Fact]
    [Trait("Category", "Fixture")]
    public void Bright_ledger_closings_match_to_the_paisa()
    {
        var f = FixtureLoader.Load("bright.json");
        AssertLedgerClosings(f);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Bright_trial_balance_matches_and_balances()
    {
        var f = FixtureLoader.Load("bright.json");
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        Assert.Equal(273000m, tb.TotalDebit.Amount);
        Assert.Equal(273000m, tb.TotalCredit.Amount);
        Assert.True(tb.Balanced);
        AssertTrialBalanceAgainstFixture(f, tb);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Bright_machinery_is_net_of_10pc_depreciation()
    {
        var f = FixtureLoader.Load("bright.json");
        var machinery = f.Company.FindLedgerByName("Machinery")!;
        var bal = LedgerBalances.Closing(f.Company, machinery, f.AsOf);

        // 60000 − 10% depreciation 6000 = 54000 Dr.
        Assert.Equal(DrCr.Debit, bal.Side);
        Assert.Equal(54000m, bal.Amount.Amount);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Bright_trading_and_profit_and_loss_net_is_minus_1000()
    {
        var f = FixtureLoader.Load("bright.json");
        var pl = ProfitAndLoss.Build(f.Company, f.AsOf);

        var expTpl = f.Expected.GetProperty("tradingAndProfitAndLoss");

        // Opening/closing stock (periodic inventory), gross and net profit — to the paisa.
        Assert.Equal(expTpl.GetProperty("openingStock").GetDecimal(), pl.OpeningStock.Amount);
        Assert.Equal(expTpl.GetProperty("closingStock").GetDecimal(), pl.ClosingStock.Amount);
        Assert.Equal(15000m, pl.GrossProfit.Amount);
        Assert.Equal(expTpl.GetProperty("grossProfit").GetDecimal(), pl.GrossProfit.Amount);
        Assert.Equal(-1000m, pl.NetProfit.Amount);
        Assert.Equal(expTpl.GetProperty("netProfit").GetDecimal(), pl.NetProfit.Amount);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void Bright_balance_sheet_matches_and_balances()
    {
        var f = FixtureLoader.Load("bright.json");
        var bs = BalanceSheet.Build(f.Company, f.AsOf);

        Assert.Equal(184000m, bs.TotalLiabilities.Amount);
        Assert.Equal(184000m, bs.TotalAssets.Amount);
        Assert.True(bs.Balanced);
        Assert.Equal(-1000m, bs.NetProfitInCapital.Amount);

        // Closing stock 15000 is on the asset side; opening stock 25000 is NOT (consumed into P&L).
        var closingStockAsset = bs.Assets.FirstOrDefault(a => a.Name == "Closing Stock");
        Assert.NotNull(closingStockAsset);
        Assert.Equal(15000m, closingStockAsset!.Amount.Amount);
        Assert.DoesNotContain(bs.Assets, a => a.Name == "Opening Stock");

        var expBs = f.Expected.GetProperty("balanceSheet");
        Assert.Equal(expBs.GetProperty("totalLiabilities").GetDecimal(), bs.TotalLiabilities.Amount);
        Assert.Equal(expBs.GetProperty("totalAssets").GetDecimal(), bs.TotalAssets.Amount);
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertLedgerClosings(FixtureLoader.LoadedFixture f)
    {
        foreach (var prop in f.Expected.GetProperty("ledgerClosing").EnumerateObject())
        {
            var ledgerName = prop.Name;
            var expectedSide = prop.Value.GetProperty("side").GetString()!;
            var expectedAmount = prop.Value.GetProperty("amount").GetDecimal();

            var ledger = f.Company.FindLedgerByName(ledgerName);
            Assert.True(ledger is not null, $"Ledger '{ledgerName}' not found in engine.");

            var bal = LedgerBalances.Closing(f.Company, ledger!, f.AsOf);
            var side = bal.Side == DrCr.Debit ? "Debit" : "Credit";

            // A zero balance has no meaningful side; only compare the side for non-zero balances.
            if (expectedAmount != 0m)
                Assert.True(
                    string.Equals(side, expectedSide, StringComparison.OrdinalIgnoreCase),
                    $"Ledger '{ledgerName}' side {side} ≠ expected {expectedSide}.");

            Assert.Equal(expectedAmount, bal.Amount.Amount);
        }
    }

    private static void AssertTrialBalanceAgainstFixture(FixtureLoader.LoadedFixture f, TrialBalance tb)
    {
        var expTb = f.Expected.GetProperty("trialBalance");
        Assert.Equal(expTb.GetProperty("totalDebit").GetDecimal(), tb.TotalDebit.Amount);
        Assert.Equal(expTb.GetProperty("totalCredit").GetDecimal(), tb.TotalCredit.Amount);
        Assert.Equal(expTb.GetProperty("balanced").GetBoolean(), tb.Balanced);
    }
}
