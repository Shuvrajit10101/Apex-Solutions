using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for RQ-5 (part 1) — the three Statements reports (Cash Flow, Funds Flow, Ratio Analysis)
/// wired into <see cref="ReportsViewModel"/> and nested under the Reports → Statements cascade in
/// <see cref="MainWindowViewModel"/>. The engine projections are trusted (covered by the engine
/// <c>StatementReportsTests</c>); these tests pin the UI wiring: each report opens, builds non-empty rows on
/// the Robert demo, reconciles a headline figure the engine guarantees, and is reachable through the menu.
/// </summary>
public sealed class StatementReportsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatementReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatementsTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    // =============================================================== ReportsViewModel: rows built + reconcile

    [Fact]
    public void CashFlow_opens_as_an_accounting_report_with_non_empty_rows()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);

        Assert.Equal(ReportKind.CashFlow, vm.Kind);
        Assert.Equal("Cash Flow", vm.Title);
        Assert.True(vm.IsAccountingReport);        // renders through the Particulars/Amount grid
        Assert.True(vm.ShowSingleAccountingGrid);
        Assert.False(vm.IsInventoryReport);
        Assert.False(vm.IsGstReport);
        Assert.NotEmpty(vm.Rows);
        // The statement always carries its opening/closing/net footer lines.
        Assert.Contains(vm.Rows, r => r.Particulars == "Opening Balance");
        Assert.Contains(vm.Rows, r => r.Particulars == "Net Cash Flow");
        Assert.Contains(vm.Rows, r => r.Particulars == "Closing Balance");
    }

    [Fact]
    public void CashFlow_rows_reconcile_opening_plus_net_to_closing()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.CashFlow);

        // The engine guarantees Opening + Net == Closing; the VM renders those three lines verbatim.
        var period = new PeriodRange(company.BooksBeginFrom, vm.AsOf);
        var cf = CashFlow.Build(company, period);
        Assert.True(cf.Reconciles);

        var opening = vm.Rows.Single(r => r.Particulars == "Opening Balance").Amount;
        var net = vm.Rows.Single(r => r.Particulars == "Net Cash Flow").Amount;
        var closing = vm.Rows.Single(r => r.Particulars == "Closing Balance").Amount;

        Assert.Equal(IndianFormat.AmountAlways(cf.OpeningBalance), opening);
        Assert.Equal(IndianFormat.AmountAlways(cf.NetCashFlow), net);
        Assert.Equal(IndianFormat.AmountAlways(cf.ClosingBalance), closing);
    }

    [Fact]
    public void FundsFlow_opens_with_balanced_total_sources_and_applications()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.FundsFlow);

        Assert.Equal("Funds Flow", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.NotEmpty(vm.Rows);
        Assert.Contains(vm.Rows, r => r.Particulars == "Sources of Funds");
        Assert.Contains(vm.Rows, r => r.Particulars == "Applications of Funds");

        // The funds-flow statement always balances: the rendered Total Sources == Total Applications.
        var ff = FundsFlow.Build(company, new PeriodRange(company.BooksBeginFrom, vm.AsOf));
        Assert.True(ff.Balanced);
        var totalSources = vm.Rows.Single(r => r.Particulars == "Total Sources").Amount;
        var totalApplications = vm.Rows.Single(r => r.Particulars == "Total Applications").Amount;
        Assert.Equal(totalSources, totalApplications);
        Assert.Equal(IndianFormat.AmountAlways(ff.TotalSources), totalSources);
    }

    [Fact]
    public void RatioAnalysis_renders_a_present_ratio_and_guards_divide_by_zero_as_na()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.RatioAnalysis);

        Assert.Equal("Ratio Analysis", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.NotEmpty(vm.Rows);

        var ra = RatioAnalysis.Build(company, vm.AsOf, ReportOptions.AsOf(vm.AsOf));

        // Working Capital money is always rendered (even when a derived ratio is n/a). Exclude the section
        // header of the same name — the value row is the non-header one carrying the amount.
        var workingCapitalRow = vm.Rows.Single(r => r.Particulars == "Working Capital" && !r.IsHeader);
        Assert.Equal(IndianFormat.AmountAlways(ra.WorkingCapital), workingCapitalRow.Amount);

        // Return on Investment % is present (proprietor's funds are non-zero for Robert) and rendered as a %.
        Assert.NotNull(ra.ReturnOnInvestmentPercent);
        var roiRow = vm.Rows.Single(r => r.Particulars == "Return on Investment %");
        Assert.Equal(
            ra.ReturnOnInvestmentPercent!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%",
            roiRow.Amount);

        // Robert has no stock → inventory turnover is a guarded divide-by-zero → renders "N/A" (never a crash).
        Assert.Null(ra.InventoryTurnover);
        Assert.Equal("N/A", vm.Rows.Single(r => r.Particulars == "Inventory Turnover").Amount);

        // The expanded (Tally-faithful) ratio set is rendered: Working Capital Turnover has a value row that
        // matches the engine (Robert has working capital and no stock, so it is a real 2-dp ratio, not N/A).
        Assert.NotNull(ra.WorkingCapitalTurnover);
        var wctRow = vm.Rows.Single(r => r.Particulars == "Working Capital Turnover");
        Assert.Equal(
            ra.WorkingCapitalTurnover!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            wctRow.Amount);
    }

    [Fact]
    public void RatioAnalysis_renders_tally_two_column_layout_with_unit_suffixes()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.RatioAnalysis);

        // The Tally layout: a "Principal Groups" section header followed by a "Principal Ratios" section header.
        var headers = vm.Rows.Where(r => r.IsHeader).Select(r => r.Particulars).ToList();
        Assert.Contains("Principal Groups", headers);
        Assert.Contains("Principal Ratios", headers);
        Assert.True(headers.IndexOf("Principal Groups") < headers.IndexOf("Principal Ratios"),
            "Principal Groups must render above Principal Ratios (Tally two-column order).");

        // Every principal-group figure and principal-ratio label from the engine has a matching rendered row.
        var ra = RatioAnalysis.Build(Robert(), vm.AsOf, ReportOptions.AsOf(vm.AsOf));
        foreach (var g in ra.PrincipalGroups)
            Assert.Contains(vm.Rows, r => !r.IsHeader && r.Particulars == g.Label);
        foreach (var r in ra.PrincipalRatios)
            Assert.Contains(vm.Rows, row => row.Particulars == r.Label);

        // Unit suffixes: a days ratio ends " days" or "N/A"; a percent ends "%" or "N/A"; a plain ratio is a
        // 2-dp number or "N/A".
        var recvRow = vm.Rows.Single(r => r.Particulars == "Receivables Turnover (days)");
        Assert.True(recvRow.Amount == "N/A" || recvRow.Amount.EndsWith(" days"),
            $"Receivables Turnover should render ' days' or 'N/A' but was '{recvRow.Amount}'.");
        var opCostRow = vm.Rows.Single(r => r.Particulars == "Operating Cost %");
        Assert.True(opCostRow.Amount == "N/A" || opCostRow.Amount.EndsWith("%"),
            $"Operating Cost % should render '%' or 'N/A' but was '{opCostRow.Amount}'.");
    }

    [Fact]
    public void CashFlow_honours_the_slice1_period_selection()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.CashFlow);

        // A half-month window (Alt+F2) re-projects over that period; the closing line matches the engine build.
        var from = company.BooksBeginFrom;
        var to = company.BooksBeginFrom.AddDays(14);
        vm.SetPeriod(from, to);

        var cf = CashFlow.Build(company, new PeriodRange(from, to));
        var closing = vm.Rows.Single(r => r.Particulars == "Closing Balance").Amount;
        Assert.Equal(IndianFormat.AmountAlways(cf.ClosingBalance), closing);
    }

    // =============================================================== shell nav wiring (Reports → Statements)

    [Fact]
    public void Statements_menu_lists_the_three_reports_under_one_section()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        vm.ShowStatementsMenu();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Statements, vm.CurrentGatewayMenu);

        // One "Financial Statements" section header, three report items — never a flat dump.
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Financial Statements" }, headers);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Cash Flow", "Funds Flow", "Ratio Analysis" }, items);
    }

    [Theory]
    [InlineData("Cash Flow", ReportKind.CashFlow)]
    [InlineData("Funds Flow", ReportKind.FundsFlow)]
    [InlineData("Ratio Analysis", ReportKind.RatioAnalysis)]
    public void Activating_a_statements_item_opens_that_report_proving_labels_match_routing(
        string label, ReportKind expected)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowStatementsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
        Assert.NotEmpty(vm.Reports.Rows);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
