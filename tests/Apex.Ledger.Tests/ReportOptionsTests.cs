using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-5 slice-1 ENGINE tests (RQ-1 period/as-of, RQ-2 detailed↔summary, RQ-6 F12 config).
/// These pin the pure value types (<see cref="PeriodRange"/>, <see cref="ReportOptions"/>,
/// <see cref="ReportConfig"/>, <see cref="ReportGrouping"/>) and the period-movement Trial
/// Balance, and prove the invariants the UI stage will rely on: the default (no period, no
/// config) build is byte-for-byte the legacy as-of build; a summary roll-up never changes a
/// report's totals; hide-zero removes only exact-zero rows; percentages sum to 100 (or all 0
/// when the total is 0, never dividing by zero); the closing-stock basis passes through.
/// Robert (accounts-only) and Bright (trading) remain the regression anchors.
/// </summary>
public class ReportOptionsTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // ---------------------------------------------------------------- RQ-1: period Trial Balance = closing AS AT To
    //
    // Defect-B fix: a period Trial Balance is a CLOSING-balance statement AS AT the period-end date
    // (opening carried forward), exactly like its sibling Balance Sheet — NOT an in-window movement.
    // period.From selects the as-at upper bound only; it never removes opening-only ledgers.

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_period_trial_balance_with_To_equal_asof_equals_the_default_asof_trial_balance()
    {
        var f = Robert();

        // A period TB whose To is the as-of date must be byte-for-byte the plain as-of TB (opening carried
        // forward): the closing-balance statement does not depend on From.
        var asOf = TrialBalance.Build(f.Company, f.AsOf);
        var period = TrialBalance.Build(
            f.Company, ReportOptions.ForPeriod(new PeriodRange(f.Company.BooksBeginFrom, f.AsOf)));

        Assert.Equal(asOf.TotalDebit, period.TotalDebit);
        Assert.Equal(asOf.TotalCredit, period.TotalCredit);
        Assert.Equal(asOf.Rows.Count, period.Rows.Count);
        foreach (var ar in asOf.Rows)
        {
            var pr = period.Rows.Single(r => r.LedgerName == ar.LedgerName);
            Assert.Equal(ar.Debit, pr.Debit);
            Assert.Equal(ar.Credit, pr.Credit);
        }
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_period_trial_balance_retains_opening_only_ledgers_bright_capital()
    {
        var f = Bright();

        // Bright's Capital (150000 Cr) is opening-only — it has no vouchers. In a period TB (any From) it
        // MUST still appear as 150000 Cr, because the TB carries opening balances forward (Defect-B fix).
        // A mid-year window that starts AFTER the books begin is the sharp case.
        var period = TrialBalance.Build(
            f.Company, ReportOptions.ForPeriod(new PeriodRange(new DateOnly(2021, 6, 1), f.AsOf)));

        var capital = period.Rows.SingleOrDefault(r => r.LedgerName == "Bright's Capital");
        Assert.NotNull(capital);
        Assert.Equal(Money.Zero, capital!.Debit);
        Assert.Equal(Money.FromRupees(150000m), capital.Credit);
        Assert.True(period.Balanced);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_period_trial_balance_To_before_a_voucher_excludes_that_vouchers_effect()
    {
        var f = Robert();

        // Robert voucher #13 (2020-04-30 Contra) deposits 10000 Cash → SBI Bank. A period TB whose To is
        // BEFORE that date must show the pre-deposit closing balances; a To on/after it shows the effect.
        // We prove the Contra is excluded from the closing by comparing SBI Bank's closing across the boundary.
        var beforeDeposit = TrialBalance.Build(
            f.Company, ReportOptions.ForPeriod(new PeriodRange(f.Company.BooksBeginFrom, new DateOnly(2020, 4, 29))));
        var afterDeposit = TrialBalance.Build(
            f.Company, ReportOptions.ForPeriod(new PeriodRange(f.Company.BooksBeginFrom, new DateOnly(2020, 4, 30))));

        var sbiBefore = beforeDeposit.Rows.Single(r => r.LedgerName == "SBI Bank");
        var sbiAfter = afterDeposit.Rows.Single(r => r.LedgerName == "SBI Bank");

        // The 2020-04-30 Contra debits SBI Bank 10000, so its closing is 10000 higher once To reaches that day.
        Assert.Equal(sbiBefore.Debit + Money.FromRupees(10000m), sbiAfter.Debit);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_default_options_build_equals_legacy_asof_build_unchanged()
    {
        var f = Robert();

        // The default ReportOptions (no explicit period) must reproduce the legacy as-of Trial Balance
        // exactly — same rows, same totals — so nothing regresses when the UI adopts the options object.
        var legacy = TrialBalance.Build(f.Company, f.AsOf);
        var viaOptions = TrialBalance.Build(f.Company, ReportOptions.AsOf(f.AsOf));

        Assert.Equal(legacy.TotalDebit, viaOptions.TotalDebit);
        Assert.Equal(legacy.TotalCredit, viaOptions.TotalCredit);
        Assert.Equal(legacy.Rows.Count, viaOptions.Rows.Count);
        foreach (var lr in legacy.Rows)
        {
            var vr = viaOptions.Rows.Single(r => r.LedgerName == lr.LedgerName);
            Assert.Equal(lr.Debit, vr.Debit);
            Assert.Equal(lr.Credit, vr.Credit);
        }
    }

    // ---------------------------------------------------------------- RQ-1: windowed Profit & Loss honours From
    //
    // Defect-A fix: when a period is set, P&L income/expense figures are the in-window MOVEMENT over
    // [From, To], not the cumulative books-begin→To closing.

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_windowed_profit_and_loss_honours_period_from_robert()
    {
        var f = Robert();

        // Whole-period P&L (books-begin → asOf) captures every income/expense voucher.
        var whole = ProfitAndLoss.Build(
            f.Company, f.AsOf, ReportOptions.ForPeriod(new PeriodRange(f.Company.BooksBeginFrom, f.AsOf)));

        // A mid-window P&L [2020-04-15, asOf] drops the pre-window vouchers: Freight Income 12000 (04-10),
        // Insurance 6000 (04-05), Diesel 4000 (04-08), and the 04-14 credit diesel purchase 5000. So both
        // TotalIncome and TotalExpenses must be strictly LESS than the whole-period figures — proving From
        // is honoured (before the fix they were identical because both used cumulative closing to 'to').
        var mid = ProfitAndLoss.Build(
            f.Company, f.AsOf, ReportOptions.ForPeriod(new PeriodRange(new DateOnly(2020, 4, 15), f.AsOf)));

        Assert.True(mid.TotalIncome < whole.TotalIncome,
            $"mid income {mid.TotalIncome} should be < whole {whole.TotalIncome}");
        Assert.True(mid.TotalExpenses < whole.TotalExpenses,
            $"mid expenses {mid.TotalExpenses} should be < whole {whole.TotalExpenses}");
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ1_full_window_profit_and_loss_still_reconciles_bright_gross_and_net()
    {
        var f = Bright();

        // A FULL-period P&L (From = books-begin, To = asOf) must still reconcile to Bright's paisa-exact
        // Gross Profit 15000 and Net Profit -1000 — proving the windowed path does not regress the common
        // case (a full-year window is the everyday report).
        var pl = ProfitAndLoss.Build(
            f.Company, f.AsOf, ReportOptions.ForPeriod(new PeriodRange(f.Company.BooksBeginFrom, f.AsOf)));

        Assert.Equal(Money.FromRupees(15000m), pl.GrossProfit);
        Assert.Equal(Money.FromRupees(-1000m), pl.NetProfit);

        // And the default (no-period) build stays identical to the full-window build (no regression).
        var noPeriod = ProfitAndLoss.Build(f.Company, f.AsOf, ReportOptions.AsOf(f.AsOf));
        Assert.Equal(noPeriod.GrossProfit, pl.GrossProfit);
        Assert.Equal(noPeriod.NetProfit, pl.NetProfit);
    }

    // ---------------------------------------------------------------- RQ-2: detailed == summary totals

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ2_trial_balance_summary_debit_and_credit_totals_equal_detailed_robert()
        => AssertTbSummaryEqualsDetailed(Robert());

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ2_trial_balance_summary_debit_and_credit_totals_equal_detailed_bright()
        => AssertTbSummaryEqualsDetailed(Bright());

    private static void AssertTbSummaryEqualsDetailed(FixtureLoader.LoadedFixture f)
    {
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        // Group-level roll-up of each column must sum to the detailed column total, to the paisa (RQ-2).
        var summaryDr = ReportGrouping.RollUp(tb.Rows, r => r.GroupName, r => r.Debit);
        var summaryCr = ReportGrouping.RollUp(tb.Rows, r => r.GroupName, r => r.Credit);

        var sumDr = summaryDr.Aggregate(Money.Zero, (a, r) => a + r.Amount);
        var sumCr = summaryCr.Aggregate(Money.Zero, (a, r) => a + r.Amount);

        Assert.Equal(tb.TotalDebit, sumDr);
        Assert.Equal(tb.TotalCredit, sumCr);
        Assert.Equal(sumDr, sumCr); // still balanced after roll-up
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ2_balance_sheet_summary_totals_equal_detailed_bright()
    {
        var f = Bright();
        var bs = BalanceSheet.Build(f.Company, f.AsOf);

        var liabRoll = ReportGrouping.RollUp(bs.Liabilities, l => l.GroupName, l => l.Amount);
        var assetRoll = ReportGrouping.RollUp(bs.Assets, a => a.GroupName, a => a.Amount);

        var sumLiab = liabRoll.Aggregate(Money.Zero, (a, r) => a + r.Amount);
        var sumAsset = assetRoll.Aggregate(Money.Zero, (a, r) => a + r.Amount);

        Assert.Equal(bs.TotalLiabilities, sumLiab);
        Assert.Equal(bs.TotalAssets, sumAsset);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ2_profit_and_loss_summary_totals_equal_detailed_robert()
    {
        var f = Robert();
        var pl = ProfitAndLoss.Build(f.Company, f.AsOf);

        var incomeRoll = ReportGrouping.RollUp(pl.Income, i => i.LedgerName, i => i.Amount);
        var expenseRoll = ReportGrouping.RollUp(pl.Expenses, e => e.LedgerName, e => e.Amount);

        var sumIncome = incomeRoll.Aggregate(Money.Zero, (a, r) => a + r.Amount);
        var sumExpense = expenseRoll.Aggregate(Money.Zero, (a, r) => a + r.Amount);

        Assert.Equal(pl.TotalIncome, sumIncome);
        Assert.Equal(pl.TotalExpenses, sumExpense);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ2_stock_summary_summary_closing_value_equals_detailed_bright()
    {
        var f = Bright();
        var ss = StockSummary.Build(f.Company, f.AsOf);

        var roll = ReportGrouping.RollUp(ss.Rows, r => r.GroupName, r => r.ClosingValue);
        var sum = roll.Aggregate(Money.Zero, (a, r) => a + r.Amount);

        Assert.Equal(ss.TotalClosingValue, sum);
    }

    // ---------------------------------------------------------------- RQ-6: hide-zero

    [Fact]
    public void RQ6_hide_zero_removes_only_exact_zero_rows_and_keeps_negatives()
    {
        var rows = new[]
        {
            new TrialBalanceRow("Cash", "Cash-in-Hand", new Money(1000m), Money.Zero),
            new TrialBalanceRow("Suspense", "Suspense A/c", Money.Zero, Money.Zero),
            new TrialBalanceRow("Adjustment", "Current Liabilities", new Money(-250m), Money.Zero),
        };

        var kept = ReportConfig.HideZeroBalances(rows, r => r.Debit);

        // Only the exact-zero-debit row is dropped; the negative-magnitude row survives.
        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, r => r.LedgerName == "Cash");
        Assert.Contains(kept, r => r.LedgerName == "Adjustment");
        Assert.DoesNotContain(kept, r => r.LedgerName == "Suspense");
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ6_hide_zero_on_a_real_trial_balance_never_removes_a_nonzero_row()
    {
        var f = Robert();
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        // The engine already suppresses zero ledgers, so hide-zero on the Dr column is a no-op for any
        // ledger that has a debit; the credit-only rows (zero debit) are the ones dropped. The kept set
        // must contain no row whose debit is zero.
        var kept = ReportConfig.HideZeroBalances(tb.Rows, r => r.Debit);
        Assert.All(kept, r => Assert.NotEqual(Money.Zero, r.Debit));
        Assert.True(kept.Count <= tb.Rows.Count);
    }

    // ---------------------------------------------------------------- RQ-6: percentages

    [Fact]
    public void RQ6_percentages_sum_to_100_when_total_is_nonzero()
    {
        var rows = new[]
        {
            new TrialBalanceRow("A", "G", new Money(2500m), Money.Zero),
            new TrialBalanceRow("B", "G", new Money(7500m), Money.Zero),
        };

        var pct = ReportConfig.Percentages(rows, r => r.Debit);

        Assert.Equal(2, pct.Count);
        Assert.Equal(25m, pct[0]);
        Assert.Equal(75m, pct[1]);
        Assert.Equal(100m, pct.Sum());
    }

    [Fact]
    public void RQ6_percentages_are_all_zero_and_never_divide_by_zero_when_total_is_zero()
    {
        var rows = new[]
        {
            new TrialBalanceRow("A", "G", Money.Zero, Money.Zero),
            new TrialBalanceRow("B", "G", Money.Zero, Money.Zero),
        };

        var pct = ReportConfig.Percentages(rows, r => r.Debit);

        Assert.Equal(2, pct.Count);
        Assert.All(pct, p => Assert.Equal(0m, p));
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ6_percentages_over_a_real_report_column_sum_to_100_to_the_paisa()
    {
        var f = Bright();
        var bs = BalanceSheet.Build(f.Company, f.AsOf);

        var pct = ReportConfig.Percentages(bs.Assets, a => a.Amount);

        // Exact ratios of arbitrary paisa amounts against their total sum to 100 only up to the last
        // decimal digit (e.g. 99.9999…99); the shares are exact, the residual is pure decimal-division
        // tail. Assert the sum is 100 to well within a paisa — the UI rounds each share to 2 dp anyway.
        Assert.True(Math.Abs(pct.Sum() - 100m) < 0.0001m);
    }

    // ---------------------------------------------------------------- RQ-6: closing-stock basis pass-through

    [Fact]
    [Trait("Category", "Fixture")]
    public void RQ6_closing_stock_basis_passes_through_options_to_profit_and_loss_and_balance_sheet()
    {
        // AsPostedLedger keeps the manual closing-stock journal; InventoryDerived derives it. Both yield
        // Bright's ₹15,000 closing stock and −₹1,000 net — the option must select the basis and the
        // downstream build must honour it (RQ-6 "closing-stock valuation basis pass-through").
        var asPosted = Bright();
        var derived = FixtureLoader.Load("bright.json", skipManualClosingStock: true);

        var plAsPosted = ProfitAndLoss.Build(
            asPosted.Company, asPosted.AsOf, ReportOptions.AsOf(asPosted.AsOf));
        var plDerived = ProfitAndLoss.Build(
            derived.Company, derived.AsOf,
            ReportOptions.AsOf(derived.AsOf).WithClosingStock(ClosingStockMode.InventoryDerived));

        Assert.Equal(Money.FromRupees(15000m), plAsPosted.ClosingStock);
        Assert.Equal(Money.FromRupees(15000m), plDerived.ClosingStock);
        Assert.Equal(plAsPosted.NetProfit, plDerived.NetProfit);

        var bsDerived = BalanceSheet.Build(
            derived.Company, derived.AsOf,
            ReportOptions.AsOf(derived.AsOf).WithClosingStock(ClosingStockMode.InventoryDerived));
        Assert.True(bsDerived.Balanced);
        Assert.Equal(Money.FromRupees(184000m), bsDerived.TotalAssets);
    }
}
