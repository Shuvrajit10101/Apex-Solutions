using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-5 slice RQ-4 ENGINE tests: the <see cref="ComparativeReport"/> composes the existing
/// single-column report builders (Trial Balance, P&amp;L, Balance Sheet, Stock Summary) across an
/// ordered list of <see cref="ComparativeReport.ColumnSpec"/> columns (each a period and/or scenario),
/// merging their rows by a stable key aligned to the column order and keeping each column's own totals.
/// These pin the invariants the UI stage relies on:
/// <list type="bullet">
/// <item>two identical column specs produce two identical columns (no per-column drift);</item>
/// <item>a key present in only one column shows a blank (null) value in the other;</item>
/// <item>each column's totals equal the corresponding single-column report's totals (Robert &amp; Bright);</item>
/// <item>monthly split columns sum back to the full-period single column;</item>
/// <item>a scenario column differs from the actual column exactly where the scenario changes figures;</item>
/// <item>a single-spec comparative reproduces the plain single-column report (no regression).</item>
/// </list>
/// </summary>
public class ComparativeReportTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>A whole-books period spec ending at the fixture as-of.</summary>
    private static ComparativeReport.ColumnSpec FullPeriod(FixtureLoader.LoadedFixture f, string label)
        => new(label, new PeriodRange(f.Company.BooksBeginFrom, f.AsOf), Scenario: null);

    // ============================================================ Trial Balance =================

    [Fact]
    [Trait("Category", "Fixture")]
    public void TrialBalance_two_identical_specs_produce_two_identical_columns()
    {
        var f = Robert();
        var spec = FullPeriod(f, "A");
        var spec2 = FullPeriod(f, "B");

        var cmp = ComparativeReport.Build(f.Company, ComparativeReportKind.TrialBalance, new[] { spec, spec2 });

        Assert.Equal(2, cmp.Columns.Count);
        // Every row's two values are identical (same period + scenario => same figure).
        foreach (var row in cmp.Rows)
        {
            Assert.Equal(row.Values[0], row.Values[1]);
        }
        // Per-column totals match each other.
        Assert.Equal(cmp.Columns[0].TotalDebit, cmp.Columns[1].TotalDebit);
        Assert.Equal(cmp.Columns[0].TotalCredit, cmp.Columns[1].TotalCredit);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void TrialBalance_single_spec_reproduces_the_plain_single_column_report()
    {
        var f = Bright();
        var single = TrialBalance.Build(f.Company, f.AsOf);

        var cmp = ComparativeReport.Build(
            f.Company, ComparativeReportKind.TrialBalance, new[] { FullPeriod(f, "Current") });

        Assert.Single(cmp.Columns);
        Assert.Equal(single.TotalDebit, cmp.Columns[0].TotalDebit);
        Assert.Equal(single.TotalCredit, cmp.Columns[0].TotalCredit);

        // Every single-column row is present in the comparative with the same value. The comparative stores a
        // SIGNED closing (Dr positive, Cr negative) so two columns compare like-for-like, so a credit row maps
        // to the negated credit magnitude.
        foreach (var sr in single.Rows)
        {
            var signed = sr.Debit != Money.Zero ? sr.Debit : -sr.Credit;
            var crow = cmp.Rows.Single(r => r.Label == sr.LedgerName);
            Assert.Equal(signed, crow.Values[0]);
        }
        Assert.Equal(single.Rows.Count, cmp.Rows.Count);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void TrialBalance_per_column_totals_equal_the_single_column_report_totals_robert_and_bright()
    {
        foreach (var f in new[] { Robert(), Bright() })
        {
            var single = TrialBalance.Build(f.Company, f.AsOf);
            var cmp = ComparativeReport.Build(
                f.Company, ComparativeReportKind.TrialBalance, new[] { FullPeriod(f, "Col") });

            Assert.Equal(single.TotalDebit, cmp.Columns[0].TotalDebit);
            Assert.Equal(single.TotalCredit, cmp.Columns[0].TotalCredit);
            // The comparative column, like the single report, balances.
            Assert.Equal(cmp.Columns[0].TotalDebit, cmp.Columns[0].TotalCredit);
        }
    }

    // ============================================================ blank-alignment ===============

    [Fact]
    public void A_key_present_in_only_one_column_shows_blank_in_the_other()
    {
        // Two scenarios: an "actual" scenario (no provisional) and an "including" scenario that surfaces a
        // provisional Journal touching the Rent ledger. Rent is absent (blank) in the actual column but
        // present in the including column.
        var c = ScenarioSeed(out var rent, out var cash);
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id,
            new DateOnly(2024, 4, 10),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
            }, optional: true));

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var including = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(including);

        var asOf = new DateOnly(2024, 4, 30);
        var period = new PeriodRange(new DateOnly(2024, 4, 1), asOf);

        var cmp = ComparativeReport.Build(c, ComparativeReportKind.TrialBalance, new[]
        {
            new ComparativeReport.ColumnSpec("Actual", period, Scenario: null),
            new ComparativeReport.ColumnSpec("Provisional", period, including),
        });

        var rentRow = cmp.Rows.Single(r => r.Label == "Rent");
        Assert.Null(rentRow.Values[0]);                            // absent in actual => blank
        Assert.Equal(Money.FromRupees(5000m), rentRow.Values[1]);  // present under the scenario
    }

    // ============================================================ monthly split ================

    [Fact]
    [Trait("Category", "Fixture")]
    public void ProfitAndLoss_monthly_split_columns_sum_back_to_the_full_period_single_column()
    {
        // Robert is accounts-only (no stock), so P&L is pure income − expense flow: the monthly in-window
        // movements partition the period exactly and the monthly net profits sum to the full-period net.
        // (For a periodic-inventory trader like Bright, opening/closing-stock re-valuation per window makes
        // monthly nets non-additive — an inherent property of periodic inventory, not of the comparative merge.)
        var f = Robert(); // 2020-04-01 .. 2020-04-30 (single month of movement)

        // Widen the split window to a full year so the monthly-column machinery is exercised across 12 months.
        var period = new PeriodRange(f.Company.BooksBeginFrom, new DateOnly(2021, 3, 31));
        var monthly = ComparativeReport.MonthlyColumns(period);
        Assert.Equal(12, monthly.Count);

        var cmp = ComparativeReport.Build(f.Company, ComparativeReportKind.ProfitAndLoss, monthly);

        // The monthly net-profit column totals sum to the full-period single-column net profit over the same window.
        var full = ProfitAndLoss.Build(f.Company, period.To, ReportOptions.ForPeriod(period)).NetProfit;
        var monthlySum = cmp.Columns.Aggregate(Money.Zero, (acc, col) => acc + col.NetProfit);
        Assert.Equal(full, monthlySum);
    }

    // ============================================================ scenario columns =============

    [Fact]
    public void Scenario_column_differs_from_the_actual_column_by_exactly_the_provisional_amount()
    {
        var c = ScenarioSeed(out var rent, out var cash);
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id,
            new DateOnly(2024, 4, 10),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
            }, optional: true));

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(scenario);

        // Scenario surfacing flows through the cumulative (as-of) build (SignedClosing honours the scenario),
        // so use no-period specs — both columns run books-begin → the company financial-year end.
        var cmp = ComparativeReport.Build(c, ComparativeReportKind.ProfitAndLoss, new[]
        {
            new ComparativeReport.ColumnSpec("Actual", Period: null, Scenario: null),
            new ComparativeReport.ColumnSpec("Provisional", Period: null, scenario),
        });

        // Actual net profit vs scenario net profit differ by the 5000 provisional rent charge.
        var actualNet = cmp.Columns[0].NetProfit;
        var scenarioNet = cmp.Columns[1].NetProfit;
        Assert.Equal(Money.FromRupees(5000m), actualNet - scenarioNet);
    }

    // ============================================================ Balance Sheet ================

    [Fact]
    [Trait("Category", "Fixture")]
    public void BalanceSheet_single_spec_per_column_totals_equal_the_single_column_report()
    {
        var f = Bright();
        var single = BalanceSheet.Build(f.Company, f.AsOf);

        var cmp = ComparativeReport.Build(
            f.Company, ComparativeReportKind.BalanceSheet, new[] { FullPeriod(f, "Current") });

        Assert.Equal(single.TotalLiabilities, cmp.Columns[0].TotalLiabilities);
        Assert.Equal(single.TotalAssets, cmp.Columns[0].TotalAssets);
        Assert.Equal(cmp.Columns[0].TotalLiabilities, cmp.Columns[0].TotalAssets);
    }

    // ============================================================ Stock Summary ================

    [Fact]
    [Trait("Category", "Fixture")]
    public void StockSummary_single_spec_per_column_total_equals_the_single_column_report()
    {
        var f = Bright();
        var single = StockSummary.Build(f.Company, f.AsOf);

        var cmp = ComparativeReport.Build(
            f.Company, ComparativeReportKind.StockSummary, new[] { FullPeriod(f, "Current") });

        Assert.Equal(single.TotalClosingValue, cmp.Columns[0].StockClosingValue);
        Assert.Equal(single.Rows.Count, cmp.Rows.Count);
    }

    // ============================================================ Options carrier (base-column) =

    [Fact]
    public void ColumnSpec_with_Options_carrying_an_asOf_after_FY_end_honours_that_asOf_not_the_FY_end()
    {
        // FY 2024-04-01 => financial-year end = 2025-03-31 (the no-Options/no-Period fallback). Post a movement
        // AFTER that FY end. A ColumnSpec whose Options carry an explicit as-of past the FY end must build AS-OF
        // that as-of (so the movement is included), NOT the FY-end fallback (which would exclude it).
        var c = ScenarioSeed(out var rent, out var cash);
        var svc = new LedgerService(c);

        var afterFyEnd = new DateOnly(2025, 6, 10);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, afterFyEnd,
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
            }));

        var asOf = new DateOnly(2025, 6, 30); // strictly after the 2025-03-31 FY end
        var options = ReportOptions.AsOf(asOf);

        // Base-column style spec: Options carries the as-of; the spec's own Period is null.
        var withOptions = ComparativeReport.Build(c, ComparativeReportKind.TrialBalance, new[]
        {
            new ComparativeReport.ColumnSpec("As-of after FY end", Period: null, Scenario: null, Options: options),
        });
        var rentWithOptions = withOptions.Rows.Single(r => r.Label == "Rent");
        Assert.Equal(Money.FromRupees(5000m), rentWithOptions.Values[0]); // as-of honoured => movement included

        // The legacy no-Options spec falls back to the FY end (2025-03-31), which is BEFORE the movement, so Rent
        // is either absent (blank) or zero — never the 5000 the as-of column captured. This pins the contract.
        var legacy = ComparativeReport.Build(c, ComparativeReportKind.TrialBalance, new[]
        {
            new ComparativeReport.ColumnSpec("FY-end fallback", Period: null, Scenario: null),
        });
        var rentLegacy = legacy.Rows.SingleOrDefault(r => r.Label == "Rent");
        Assert.True(rentLegacy is null || rentLegacy.Values[0] is null || rentLegacy.Values[0] == Money.Zero);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void ColumnSpec_Options_ClosingStock_threads_through_to_the_BalanceSheet_column()
    {
        // A ColumnSpec whose Options request ClosingStock=InventoryDerived must build the Balance-Sheet column with
        // that valuation basis — so the column's TotalAssets equals the InventoryDerived single-column build (NOT
        // merely the default AsPostedLedger build). Assert equality to the derived single-column report to pin the
        // contract even where posted==derived numerically for Bright.
        var f = Bright();
        var derivedOptions = ReportOptions.AsOf(f.AsOf).WithClosingStock(ClosingStockMode.InventoryDerived);
        var derivedSingle = BalanceSheet.Build(f.Company, f.AsOf, derivedOptions);

        var cmp = ComparativeReport.Build(f.Company, ComparativeReportKind.BalanceSheet, new[]
        {
            new ComparativeReport.ColumnSpec("Derived", Period: null, Scenario: null, Options: derivedOptions),
        });

        Assert.Equal(derivedSingle.TotalAssets, cmp.Columns[0].TotalAssets);
        Assert.Equal(derivedSingle.TotalLiabilities, cmp.Columns[0].TotalLiabilities);
    }

    // ---- local scenario company (mirrors ScenarioTests.Seed, minimal) -----------------------

    private static Company ScenarioSeed(out Domain.Ledger rent, out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("Comparative Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(1000000m), openingIsDebit: false);
        c.AddLedger(capital);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        return c;
    }
}
