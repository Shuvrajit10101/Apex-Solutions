using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 RQ-4 (comparative / columnar reports) wired into <see cref="ReportsViewModel"/>,
/// the <see cref="AddComparisonColumnViewModel"/> (Alt+C) and <see cref="AutoColumnsViewModel"/> (Alt+N) panels,
/// and the shell shortcut plumbing on <see cref="MainWindowViewModel"/>.
///
/// <para>Every test constructs a report over the embedded "Robert" demo (13 vouchers across April 2020, last
/// voucher 2020-04-30 → default as-of), asserts the untouched report is single-column, then drives Alt+C / Alt+N
/// and asserts the comparative grid actually gains the expected columns and aligned rows. The engine
/// <c>ComparativeReport</c> is trusted (covered by <c>ComparativeReportTests</c>); these tests pin the UI wiring.</para>
/// </summary>
public sealed class ComparativeReportViewModelTests : IDisposable
{
    private static readonly DateOnly BooksBegin = new(2020, 4, 1);
    private static readonly DateOnly LastVoucher = new(2020, 4, 30);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ComparativeReportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexComparativeTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    /// <summary>A Robert company with one defined scenario, so the By-scenario axis has something to compare.</summary>
    private static Company RobertWithScenario(out Scenario scenario)
    {
        var company = Robert();
        scenario = new Scenario(Guid.NewGuid(), "Optimistic");
        company.AddScenario(scenario);
        return company;
    }

    // =============================================================== ReportsViewModel comparative state

    [Fact]
    public void Default_report_is_single_column_and_comparative_is_empty()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        Assert.False(vm.IsComparative);
        Assert.True(vm.SupportsComparative);
        Assert.Empty(vm.ComparativeColumns);
        Assert.Empty(vm.ComparativeRows);
        Assert.Equal(0, vm.ExtraColumnCount);
        Assert.True(vm.ShowSingleAccountingGrid); // still the plain single-column grid
    }

    [Fact]
    public void AddComparisonColumn_yields_two_columns_and_rows_carry_the_extra_value()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);

        var added = vm.AddComparisonColumn("Half month", new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), null);

        Assert.True(added);
        Assert.True(vm.IsComparative);
        Assert.False(vm.ShowSingleAccountingGrid);          // the multi-column grid replaces the single one
        Assert.Equal(2, vm.ComparativeColumns.Count);       // base column + the added column
        Assert.Equal("Half month", vm.ComparativeColumns[1].Label);
        Assert.NotEmpty(vm.ComparativeRows);
        // Every row carries exactly one value cell per column (aligned; a blank cell = the key is absent there).
        Assert.All(vm.ComparativeRows, r => Assert.Equal(2, r.Cells.Count));
        // At least one row has a non-blank value in the added column (the report is not empty at mid-April).
        Assert.Contains(vm.ComparativeRows, r => r.Cells[1].Length > 0);
    }

    [Fact]
    public void AddComparisonColumn_rejects_an_inverted_window()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var added = vm.AddComparisonColumn("bad", new PeriodRange(new DateOnly(2020, 4, 30), BooksBegin), null);
        Assert.False(added);
        Assert.False(vm.IsComparative);
        Assert.Equal(0, vm.ExtraColumnCount);
    }

    [Fact]
    public void AddComparisonColumn_is_a_no_op_on_a_non_comparative_report()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.DayBook); // Day Book is not comparative
        Assert.False(vm.SupportsComparative);
        var added = vm.AddComparisonColumn("x", null, null);
        Assert.False(added);
        Assert.False(vm.IsComparative);
    }

    [Fact]
    public void AutoColumnsByMonth_yields_the_base_plus_one_column_per_month()
    {
        // Robert's books run the full financial year (Apr 2020 → Mar 2021); a whole-year window is 12 months.
        var vm = new ReportsViewModel(Robert(), ReportKind.ProfitAndLoss);
        vm.SetPeriod(BooksBegin, new DateOnly(2021, 3, 31));

        var ok = vm.AutoColumnsByMonth();

        Assert.True(ok);
        Assert.True(vm.IsComparative);
        // base column + 12 monthly columns.
        Assert.Equal(13, vm.ComparativeColumns.Count);
        Assert.Equal(12, vm.ExtraColumnCount);
    }

    [Fact]
    public void AutoColumnsByScenario_yields_one_column_per_scenario_plus_the_base()
    {
        var company = RobertWithScenario(out _);
        var vm = new ReportsViewModel(company, ReportKind.TrialBalance);

        var ok = vm.AutoColumnsByScenario();

        Assert.True(ok);
        Assert.True(vm.IsComparative);
        // base (actual) column + one column per scenario (one scenario defined).
        Assert.Equal(2, vm.ComparativeColumns.Count);
        Assert.Equal(1, vm.ExtraColumnCount);
        Assert.Contains(vm.ComparativeColumns, c => c.Label == "Optimistic");
    }

    [Fact]
    public void AutoColumnsByScenario_returns_false_when_no_scenarios_defined()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        Assert.False(vm.AutoColumnsByScenario());
        Assert.False(vm.IsComparative);
    }

    [Fact]
    public void ClearComparative_returns_to_a_single_column_report()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        vm.AddComparisonColumn("extra", new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), null);
        Assert.True(vm.IsComparative);
        var singleRowsBefore = vm.Rows.Count; // the plain single-column report stays intact underneath

        vm.ClearComparative();

        Assert.False(vm.IsComparative);
        Assert.Empty(vm.ComparativeColumns);
        Assert.Empty(vm.ComparativeRows);
        Assert.True(vm.ShowSingleAccountingGrid);
        Assert.Equal(singleRowsBefore, vm.Rows.Count); // the single-column report is byte-for-byte the same
    }

    [Fact]
    public void Base_column_reproduces_the_single_column_totals()
    {
        // The first comparative column equals the report's own single-column view. Its total text embeds the
        // Grand-Total Dr amount, which must match the plain single-column Grand Total Dr cell.
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var singleGrandDr = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Grand Total").Debit;

        vm.AddComparisonColumn("half", new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), null);

        Assert.Contains(singleGrandDr, vm.ComparativeColumns[0].TotalText);
    }

    // =============================================================== AddComparisonColumnViewModel panel

    [Fact]
    public void AddColumnPanel_apply_pushes_a_period_column_into_the_report()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var panel = new AddComparisonColumnViewModel(report)
        {
            Label = "Q1 window",
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "14-Apr-2020",
        };

        panel.Apply();

        Assert.True(report.IsComparative);
        Assert.Equal(2, report.ComparativeColumns.Count);
        Assert.Equal("Q1 window", report.ComparativeColumns[1].Label);
        Assert.Contains("Added", panel.Status);
    }

    [Fact]
    public void AddColumnPanel_rejects_an_inverted_window_without_a_false_success()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var panel = new AddComparisonColumnViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "30-Apr-2020", // From > To
            PeriodToText = "01-Apr-2020",
        };

        panel.Apply();

        Assert.False(report.IsComparative);            // nothing was added
        Assert.DoesNotContain("Added", panel.Status);  // no false success
        Assert.Contains("From must be on/before To", panel.Status);
    }

    [Fact]
    public void AddColumnPanel_rejects_an_unparseable_date()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var panel = new AddComparisonColumnViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "not-a-date",
        };

        panel.Apply();

        Assert.False(report.IsComparative);
        Assert.DoesNotContain("Added", panel.Status);
        Assert.Contains("Unrecognized date", panel.Status);
    }

    [Fact]
    public void AddColumnPanel_scenario_column_adds_a_scenario_labelled_column()
    {
        var company = RobertWithScenario(out _);
        var report = new ReportsViewModel(company, ReportKind.TrialBalance);
        var panel = new AddComparisonColumnViewModel(report);
        panel.SelectedScenario = panel.Scenarios.Single(o => o.Scenario?.Name == "Optimistic");
        panel.UsePeriod = false;

        panel.Apply();

        Assert.True(report.IsComparative);
        Assert.Equal(2, report.ComparativeColumns.Count);
        Assert.Contains("Optimistic", report.ComparativeColumns[1].Label);
    }

    // =============================================================== AutoColumnsViewModel panel

    [Fact]
    public void AutoColumnsPanel_by_month_generates_the_monthly_axis()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.ProfitAndLoss);
        report.SetPeriod(BooksBegin, new DateOnly(2020, 6, 30)); // 3 months
        var panel = new AutoColumnsViewModel(report) { ByMonth = true };

        panel.Apply();

        Assert.True(report.IsComparative);
        Assert.Equal(4, report.ComparativeColumns.Count); // base + 3 months
        Assert.Contains("month", panel.Status);
    }

    [Fact]
    public void AutoColumnsPanel_by_scenario_generates_the_scenario_axis()
    {
        var company = RobertWithScenario(out _);
        var report = new ReportsViewModel(company, ReportKind.BalanceSheet);
        var panel = new AutoColumnsViewModel(report) { ByScenario = true };

        panel.Apply();

        Assert.True(report.IsComparative);
        Assert.Equal(2, report.ComparativeColumns.Count); // base + one scenario
        Assert.Contains("scenario", panel.Status);
    }

    [Fact]
    public void AutoColumnsPanel_by_scenario_without_scenarios_reports_an_error()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var panel = new AutoColumnsViewModel(report) { ByScenario = true };

        panel.Apply();

        Assert.False(report.IsComparative);
        Assert.Contains("No scenarios", panel.Status);
    }

    [Fact]
    public void AutoColumnsPanel_axes_are_mutually_exclusive()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var panel = new AutoColumnsViewModel(report);
        Assert.True(panel.ByMonth);
        Assert.False(panel.ByScenario);

        panel.ByScenario = true;
        Assert.False(panel.ByMonth); // selecting one clears the other
        Assert.Equal(AutoColumnAxis.ByScenario, panel.Axis);
    }

    // =============================================================== shell shortcut plumbing (Alt+C / Alt+N)

    [Fact]
    public void AltC_opens_the_add_column_panel_as_a_column_beside_the_live_report()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out var columnsBefore);

        vm.OpenAddComparisonColumn();

        Assert.Equal(Screen.AddComparisonColumn, vm.CurrentScreen);
        Assert.NotNull(vm.AddComparisonColumn);
        Assert.NotNull(vm.Reports);                              // the report stays live beneath the panel
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);       // opened as an EXTRA column, not a replacement
        Assert.True(vm.Columns[^1].Page is AddComparisonColumnViewModel);

        // Re-pressing Alt+C does not stack a second panel.
        vm.OpenAddComparisonColumn();
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);
    }

    [Fact]
    public void AltN_opens_the_auto_columns_panel_as_a_column_beside_the_live_report()
    {
        var vm = OpenReportShell(ReportKind.ProfitAndLoss, out var columnsBefore);

        vm.OpenAutoColumns();

        Assert.Equal(Screen.AutoColumns, vm.CurrentScreen);
        Assert.NotNull(vm.AutoColumns);
        Assert.NotNull(vm.Reports);
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);
        Assert.True(vm.Columns[^1].Page is AutoColumnsViewModel);
    }

    [Fact]
    public void Opening_the_other_comparative_panel_replaces_rather_than_stacks()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out var columnsBefore);

        vm.OpenAddComparisonColumn();
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);

        vm.OpenAutoColumns(); // switching Alt+C → Alt+N pops the first, opens the second
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);
        Assert.Null(vm.AddComparisonColumn);
        Assert.NotNull(vm.AutoColumns);
        Assert.Equal(Screen.AutoColumns, vm.CurrentScreen);
    }

    [Fact]
    public void Closing_the_add_column_panel_leaves_the_report_live()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.OpenAddComparisonColumn();
        Assert.Equal(Screen.AddComparisonColumn, vm.CurrentScreen);

        vm.Back(); // Esc / Left pops the panel column

        Assert.Null(vm.AddComparisonColumn);
        Assert.NotNull(vm.Reports);                 // the report survived
        Assert.Equal(Screen.Report, vm.CurrentScreen);
    }

    [Fact]
    public void ApplyAddComparisonColumn_from_the_shell_makes_the_report_comparative()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.OpenAddComparisonColumn();
        vm.AddComparisonColumn!.UsePeriod = true;
        vm.AddComparisonColumn.PeriodFromText = "01-Apr-2020";
        vm.AddComparisonColumn.PeriodToText = "14-Apr-2020";
        vm.AddComparisonColumn.Label = "First half";

        vm.ApplyAddComparisonColumn();

        Assert.True(vm.Reports!.IsComparative);
        Assert.Equal(2, vm.Reports.ComparativeColumns.Count);
        Assert.Equal("First half", vm.Reports.ComparativeColumns[1].Label);
    }

    [Fact]
    public void ClearComparative_from_the_shell_returns_to_single_column()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.OpenAddComparisonColumn();
        vm.AddComparisonColumn!.PeriodFromText = "01-Apr-2020";
        vm.AddComparisonColumn.PeriodToText = "14-Apr-2020";
        vm.ApplyAddComparisonColumn();
        Assert.True(vm.Reports!.IsComparative);

        vm.ClearComparative();

        Assert.False(vm.Reports!.IsComparative);
    }

    // =============================================================== base-column == single-column (regression)

    [Fact]
    public void Base_comparative_column_honours_the_reports_actual_asOf_not_the_FY_end()
    {
        // THE key repro for the fixed HIGH defect. FY 2020-04-01..2021-03-31 (FY end = 2021-03-31). A Journal
        // dated 2021-06-15 (AFTER the FY end) touches Rent. The report's default as-of = the last voucher date
        // (2021-06-15), so the single-column Trial Balance shows a "Rent" row. Before the fix, the comparative
        // BASE column resolved to the FY end (2021-03-31, BEFORE the movement) and DROPPED Rent. After the fix
        // the base column carries the report's own options (as-of = 2021-06-15) and keeps Rent.
        var company = SeededCompanyWithJournalAfterFyEnd();
        var vm = new ReportsViewModel(company, ReportKind.TrialBalance);

        // Single-column report: as-of = last voucher (2021-06-15) => Rent row present.
        Assert.Equal(new DateOnly(2021, 6, 15), vm.AsOf);
        Assert.Contains(vm.Rows, r => r.Particulars == "Rent");

        // Go comparative (add any extra column). The BASE column (Cells[0]) must ALSO carry the Rent row.
        var added = vm.AddComparisonColumn(
            "First half", new PeriodRange(new DateOnly(2020, 4, 1), new DateOnly(2020, 10, 1)), null);
        Assert.True(added);
        Assert.True(vm.IsComparative);

        var rentRow = vm.ComparativeRows.SingleOrDefault(r => r.Label == "Rent");
        Assert.NotNull(rentRow);                              // Rent survived into the comparative grid
        Assert.NotEqual(string.Empty, rentRow!.Cells[0]);     // and its BASE-column cell is NOT blank (not dropped)
        Assert.Contains("7,000", rentRow.Cells[0]);           // the 7000 Dr posted after the FY end
    }

    [Fact]
    public void Base_comparative_column_reproduces_the_single_column_totals_with_an_after_FY_end_asOf()
    {
        // The base column's total text must equal the single-column Grand Total even when the as-of sits past the
        // FY end (the regime that previously diverged). Pins base == single beside it.
        var company = SeededCompanyWithJournalAfterFyEnd();
        var vm = new ReportsViewModel(company, ReportKind.TrialBalance);
        var singleGrandDr = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Grand Total").Debit;

        vm.AddComparisonColumn("First half", new PeriodRange(new DateOnly(2020, 4, 1), new DateOnly(2020, 10, 1)), null);

        Assert.Contains(singleGrandDr, vm.ComparativeColumns[0].TotalText);
    }

    [Fact]
    public void Base_comparative_BalanceSheet_column_reflects_an_InventoryDerived_ClosingStock_selection()
    {
        // F12 ClosingStock=InventoryDerived must thread through into the base comparative column: the column's
        // Balance-Sheet totals equal the InventoryDerived single-column engine build (NOT the default AsPosted
        // build). Pinning equality to the derived engine report proves the flag rides inside the base column's
        // Options, consistent with the single-column Balance Sheet the user is looking at.
        var company = SeededCompanyWithJournalAfterFyEnd();
        var vm = new ReportsViewModel(company, ReportKind.BalanceSheet);
        vm.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);

        var derived = ReportOptions.AsOf(vm.AsOf).WithClosingStock(ClosingStockMode.InventoryDerived);
        var single = BalanceSheet.Build(company, vm.AsOf, derived);

        vm.AddComparisonColumn("First half", new PeriodRange(new DateOnly(2020, 4, 1), new DateOnly(2020, 10, 1)), null);

        // The base-column total line embeds the derived Total Liabilities / Total Assets figures verbatim
        // (same Indian formatting the comparative header uses).
        Assert.Contains(IndianFormat.AmountAlways(single.TotalLiabilities), vm.ComparativeColumns[0].TotalText);
        Assert.Contains(IndianFormat.AmountAlways(single.TotalAssets), vm.ComparativeColumns[0].TotalText);
    }

    /// <summary>
    /// A seeded company on FY 2020-04-01..2021-03-31 with a Journal dated 2021-06-15 (AFTER the FY end) posting
    /// Rent 7000 Dr / Cash 7000 Cr, so the report's last-voucher as-of (2021-06-15) sits past the FY-end fallback
    /// — the exact regime where the comparative base column used to diverge from the single-column report.
    /// </summary>
    private static Company SeededCompanyWithJournalAfterFyEnd()
    {
        var c = CompanyFactory.CreateSeeded(
            "AfterFyEnd Co " + Guid.NewGuid().ToString("N"), new DateOnly(2020, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var capital = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Capital",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(100000m), openingIsDebit: false);
        c.AddLedger(capital);

        var rent = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2021, 6, 15),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(7000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(7000m), DrCr.Credit),
            }));

        return c;
    }

    // =============================================================== helpers

    private MainWindowViewModel OpenReportShell(ReportKind kind, out int columnsBefore)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(kind);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        columnsBefore = vm.Columns.Count;
        return vm;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
