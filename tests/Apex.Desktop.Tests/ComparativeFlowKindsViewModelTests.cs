using System;
using System.Collections.Generic;
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
/// Census 11.14 / 11.17 (cluster C3) BINDING tests. The engine was widened first (the map could not be widened
/// alone without binding Alt+C to a kind the engine cannot compose); these drive the keystroke and assert on the
/// REALISED grid, the exported model and the print model — never on an internal that was just set.
///
/// <para>They also MEASURE the coverage rather than claiming it: <see cref="Alt_C_coverage_is_counted_by_driving_every_report_kind"/>
/// walks every <see cref="ReportKind"/>, presses Alt+C on each, and pins the count of kinds on which a column
/// actually appears.</para>
/// </summary>
public sealed class ComparativeFlowKindsViewModelTests : IDisposable
{
    private static readonly DateOnly BooksBegin = new(2020, 4, 1);
    private static readonly DateOnly LastVoucher = new(2020, 4, 30);

    private readonly string _tempDir;

    public ComparativeFlowKindsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCmpFlow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a temp dir that will not delete must not fail a test */ }
    }

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    // =============================================================== the measured count

    [Fact]
    public void Alt_C_coverage_is_counted_by_driving_every_report_kind()
    {
        // 🔴 COUNTED, NOT COPIED. Every ReportKind is constructed and Alt+C is actually pressed on it; a kind
        // counts as comparative only when a column REALLY appears in the grid afterwards. The census has carried
        // a stale ReportKind fraction four times; this test re-derives both halves on every run, so the figure
        // in the commit message cannot drift away from the code.
        var kinds = Enum.GetValues<ReportKind>();
        var worked = new List<ReportKind>();
        var refusedByVendorRule = new List<ReportKind>();

        foreach (var kind in kinds)
        {
            ReportsViewModel vm;
            try { vm = new ReportsViewModel(Robert(), kind); }
            catch (Exception) { continue; } // a kind this fixture cannot build is out of scope for the count
            if (ReportsViewModel.IsVendorExcludedFromComparative(kind)) refusedByVendorRule.Add(kind);

            var added = vm.AddComparisonColumn("B", new PeriodRange(BooksBegin, LastVoucher), scenario: null);
            // Presence of the flag is not enough — the grid must actually be in comparative mode with columns.
            if (added && vm.IsComparative && vm.ComparativeColumns.Count >= 2) worked.Add(kind);
        }

        // The comparative set, measured: the original four plus the two flow statements this branch added.
        Assert.Equal(
            new[]
            {
                ReportKind.TrialBalance, ReportKind.BalanceSheet, ReportKind.ProfitAndLoss,
                ReportKind.StockSummary, ReportKind.CashFlow, ReportKind.FundsFlow,
            }.OrderBy(k => k.ToString()).ToArray(),
            worked.OrderBy(k => k.ToString()).ToArray());

        Assert.Equal(6, worked.Count);
        // The vendor's carve-out is non-empty and none of its members got a column.
        Assert.NotEmpty(refusedByVendorRule);
        Assert.Empty(worked.Intersect(refusedByVendorRule));

        // And the denominator is the enum's own size, re-derived here rather than quoted.
        Assert.True(kinds.Length > 90, $"ReportKind has {kinds.Length} members — re-derive, do not copy.");
    }

    // =============================================================== the two new kinds, realised

    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    public void Alt_C_on_a_flow_statement_renders_captioned_columns_with_populated_cells(ReportKind kind)
    {
        // 🔴 CHECK REACHABILITY, NOT PRESENCE: three reports once printed pages of entirely blank cells. Assert
        // the CAPTIONS and the CELLS, and that the single-column grid actually yields the pane.
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, LastVoucher);
        Assert.True(vm.SupportsComparative);
        Assert.True(vm.ShowSingleAccountingGrid);

        Assert.True(vm.AddComparisonColumn("Apr 2020", new PeriodRange(BooksBegin, LastVoucher), scenario: null));

        Assert.True(vm.IsComparative);
        Assert.False(vm.ShowSingleAccountingGrid); // the single-column grid yields to the comparative pane
        Assert.Equal(2, vm.ComparativeColumns.Count);
        Assert.NotEmpty(vm.ComparativeRows);

        // Captions: every column has a label AND a non-empty total line (the kind-specific arm is reached).
        Assert.All(vm.ComparativeColumns, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
            Assert.False(string.IsNullOrWhiteSpace(c.TotalText));
        });
        Assert.Equal("Apr 2020", vm.ComparativeColumns[1].Label);

        // Cells: each row has one cell per column, and at least one row is fully populated — i.e. the grid is
        // not a page of blanks. Both columns cover the same window here, so every cell should carry a figure.
        Assert.All(vm.ComparativeRows, r => Assert.Equal(vm.ComparativeColumns.Count, r.Cells.Count));
        Assert.All(vm.ComparativeRows, r => Assert.All(r.Cells, cell => Assert.False(string.IsNullOrWhiteSpace(cell))));

        // The side suffix is on the cell, because a comparative grid has no section headings to carry it.
        var expectedSuffixes = kind == ReportKind.CashFlow ? new[] { " In", " Out" } : new[] { " Src", " App" };
        Assert.All(vm.ComparativeRows, r => Assert.All(r.Cells,
            cell => Assert.True(expectedSuffixes.Any(cell.EndsWith),
                $"cell '{cell}' carries no side suffix — an outflow would read as an inflow")));
    }

    [Fact]
    public void A_cash_flow_column_total_carries_the_statements_own_reconciliation()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);
        vm.SetPeriod(BooksBegin, LastVoucher);
        vm.AddComparisonColumn("Apr", new PeriodRange(BooksBegin, LastVoucher), scenario: null);

        foreach (var c in vm.ComparativeColumns)
        {
            Assert.Contains("Open", c.TotalText);
            Assert.Contains("Net", c.TotalText);
            Assert.Contains("Close", c.TotalText);
        }
    }

    [Fact]
    public void A_funds_flow_column_total_shows_both_sides_so_an_imbalance_would_be_visible()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.FundsFlow);
        vm.SetPeriod(BooksBegin, LastVoucher);
        vm.AddComparisonColumn("Apr", new PeriodRange(BooksBegin, LastVoucher), scenario: null);

        foreach (var c in vm.ComparativeColumns)
        {
            Assert.Contains("Sources", c.TotalText);
            Assert.Contains("Applic.", c.TotalText);
        }
    }

    // =============================================================== the vendor's carve-out

    [Theory]
    [InlineData(ReportKind.DayBook)]
    [InlineData(ReportKind.OptionalVouchersRegister)]
    [InlineData(ReportKind.CancelledVouchersRegister)]
    [InlineData(ReportKind.PostDatedVouchersRegister)]
    [InlineData(ReportKind.Gstr1)]
    [InlineData(ReportKind.Gstr3b)]
    [InlineData(ReportKind.TaxAnalysis)]
    public void The_vendors_Day_Book_and_GST_return_carve_out_is_a_RULE_not_an_accident(ReportKind kind)
    {
        // The shortcut reference gives both chords with the same carve-out: "Alt+C — To add a new column (except
        // in Day Book and GST Returns)" and "Alt+N — To auto repeat columns (except in Day Book and GST
        // Returns)". Before this, these kinds were excluded only by NOT being among the four mapped ones — so a
        // widening pass was free to add them. Now the rule refuses them explicitly.
        Assert.True(ReportsViewModel.IsVendorExcludedFromComparative(kind));

        var vm = new ReportsViewModel(Robert(), kind);
        Assert.False(vm.SupportsComparative);
        Assert.False(vm.AddComparisonColumn("B", new PeriodRange(BooksBegin, LastVoucher), scenario: null));
        Assert.False(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));
        Assert.False(vm.AutoColumnsByScenario());
        Assert.False(vm.IsComparative);
        Assert.Empty(vm.ComparativeColumns);
    }

    [Theory]
    [InlineData(ReportKind.TrialBalance)]
    [InlineData(ReportKind.BalanceSheet)]
    [InlineData(ReportKind.ProfitAndLoss)]
    [InlineData(ReportKind.StockSummary)]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    public void The_carve_out_does_not_catch_a_kind_that_should_be_comparative(ReportKind kind)
        => Assert.False(ReportsViewModel.IsVendorExcludedFromComparative(kind));

    // =============================================================== scenario columns

    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    public void A_scenario_column_on_a_flow_statement_is_declined_rather_than_silently_duplicated(ReportKind kind)
    {
        // 🔴 The statements read unscenarioed movements, so a scenario column would reproduce the ACTUAL books
        // under the scenario's name. The view model declines the gesture (so the panel can explain it) instead of
        // letting the engine's refusal surface as an exception on a keystroke.
        var company = Robert();
        var scenario = new Scenario(Guid.NewGuid(), "Optimistic");
        company.AddScenario(scenario);

        var vm = new ReportsViewModel(company, kind);
        Assert.True(vm.SupportsComparative);
        Assert.False(vm.SupportsComparativeScenarioColumns);

        Assert.False(vm.AddComparisonColumn("What-if", period: null, scenario: scenario));
        Assert.False(vm.AutoColumnsByScenario());
        Assert.False(vm.IsComparative);

        // A PERIOD column on the same report is accepted — the refusal is narrow.
        Assert.True(vm.AddComparisonColumn("Apr", new PeriodRange(BooksBegin, LastVoucher), scenario: null));
        Assert.True(vm.IsComparative);
    }

    [Theory]
    [InlineData(ReportKind.TrialBalance)]
    [InlineData(ReportKind.BalanceSheet)]
    [InlineData(ReportKind.ProfitAndLoss)]
    public void The_scenario_capable_kinds_still_accept_a_scenario_column(ReportKind kind)
    {
        var company = Robert();
        var scenario = new Scenario(Guid.NewGuid(), "Optimistic");
        company.AddScenario(scenario);

        var vm = new ReportsViewModel(company, kind);
        Assert.True(vm.SupportsComparativeScenarioColumns);
        Assert.True(vm.AddComparisonColumn("What-if", period: null, scenario: scenario));
        Assert.Equal(2, vm.ComparativeColumns.Count);
    }

    [Fact]
    public void A_scenario_left_over_from_a_previous_report_does_not_blow_up_Alt_C_on_a_flow_statement()
    {
        // 🔴 A REAL CRASH THIS BRANCH WOULD OTHERWISE HAVE SHIPPED. The report options keep whatever scenario the
        // picker last applied, and the picker only rewrites them when the PICKER changes — not when the report
        // kind does. So: view a Trial Balance under a scenario, switch to Cash Flow, press Alt+C. The base column
        // would have carried the stale scenario into the engine and hit its scenario refusal, throwing on the
        // keystroke. The base column strips it instead, which also matches what the single-column Cash Flow does
        // (it is built from a window and ignores the scenario entirely).
        var company = Robert();
        var scenario = new Scenario(Guid.NewGuid(), "Optimistic");
        company.AddScenario(scenario);

        var vm = new ReportsViewModel(company, ReportKind.TrialBalance);
        vm.SelectedScenario = vm.Scenarios.First(s => s.Scenario is not null);
        Assert.NotNull(vm.SelectedScenario!.Scenario);

        vm.Show(ReportKind.CashFlow); // the scenario is now stale inside the options

        Assert.True(vm.AddComparisonColumn("Apr", new PeriodRange(BooksBegin, LastVoucher), scenario: null));
        Assert.True(vm.IsComparative);
        Assert.Equal(2, vm.ComparativeColumns.Count);
        Assert.NotEmpty(vm.ComparativeRows);
    }

    // =============================================================== the periodicity axis

    [Theory]
    [InlineData(ColumnPeriodicity.Monthly, 12)]
    [InlineData(ColumnPeriodicity.Quarterly, 4)]
    [InlineData(ColumnPeriodicity.Fortnightly, 27)]
    [InlineData(ColumnPeriodicity.Weekly, 53)]
    [InlineData(ColumnPeriodicity.Yearly, 1)]
    public void Alt_N_repeats_columns_at_each_vendor_periodicity(ColumnPeriodicity periodicity, int buckets)
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);
        vm.SetPeriod(new DateOnly(2020, 4, 1), new DateOnly(2021, 3, 31)); // a 365-day financial year

        Assert.True(vm.AutoColumnsByPeriodicity(periodicity));

        Assert.True(vm.IsComparative);
        Assert.Equal(buckets, vm.ExtraColumnCount);
        Assert.Equal(buckets + 1, vm.ComparativeColumns.Count); // base column stays first
        // Every generated column is captioned and carries a total line — no blank headers.
        Assert.All(vm.ComparativeColumns, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
            Assert.False(string.IsNullOrWhiteSpace(c.TotalText));
        });
        // Every row is aligned to the column count (a short row would render as a silent left-shift).
        Assert.All(vm.ComparativeRows, r => Assert.Equal(vm.ComparativeColumns.Count, r.Cells.Count));
    }

    [Fact]
    public void Alt_N_by_month_is_unchanged_by_the_periodicity_rework()
    {
        // AutoColumnsByMonth is the long-shipped entry point and now delegates; the shipped gesture must not move.
        var a = new ReportsViewModel(Robert(), ReportKind.ProfitAndLoss);
        a.SetPeriod(BooksBegin, new DateOnly(2020, 6, 30));
        Assert.True(a.AutoColumnsByMonth());

        var b = new ReportsViewModel(Robert(), ReportKind.ProfitAndLoss);
        b.SetPeriod(BooksBegin, new DateOnly(2020, 6, 30));
        Assert.True(b.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));

        Assert.Equal(a.ComparativeColumns.Count, b.ComparativeColumns.Count);
        for (var i = 0; i < a.ComparativeColumns.Count; i++)
            Assert.Equal(a.ComparativeColumns[i].Label, b.ComparativeColumns[i].Label);
    }

    [Fact]
    public void The_Alt_N_panel_offers_the_vendor_periodicities_and_defaults_to_monthly()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.FundsFlow);
        vm.SetPeriod(new DateOnly(2020, 4, 1), new DateOnly(2021, 3, 31));
        var panel = new AutoColumnsViewModel(vm);

        // The vendor's own list, plus Yearly, all reachable from the panel.
        Assert.Equal(
            new[]
            {
                ColumnPeriodicity.Daily, ColumnPeriodicity.Weekly, ColumnPeriodicity.Fortnightly,
                ColumnPeriodicity.Monthly, ColumnPeriodicity.Quarterly, ColumnPeriodicity.Yearly,
            },
            panel.Periodicities.Select(p => p.Periodicity).ToArray());
        Assert.All(panel.Periodicities, p => Assert.False(string.IsNullOrWhiteSpace(p.Caption)));

        // 🔴 REACHABILITY, NOT PRESENCE. The ComboBox carries no ItemTemplate (one would put a trimming
        // TextBlock inside a horizontal StackPanel, which measures at infinite width — XamlLayoutInvariantTests
        // rejects it). With no template Avalonia renders each item through ToString(), so the caption only
        // reaches the screen if ToString() IS the caption. A record's generated ToString would print
        // "PeriodicityOption { Periodicity = Monthly, Caption = Monthly }" into the dropdown instead.
        Assert.All(panel.Periodicities, p => Assert.Equal(p.Caption, p.ToString()));
        Assert.Equal("Fortnightly", panel.Periodicities.Single(
            p => p.Periodicity == ColumnPeriodicity.Fortnightly).ToString());

        // Monthly preselected — the shipped "open Alt+N, Generate" gesture is unchanged.
        Assert.Equal(ColumnPeriodicity.Monthly, panel.Periodicity);
        Assert.True(panel.ByPeriod);

        panel.SelectedPeriodicity = panel.Periodicities.Single(p => p.Periodicity == ColumnPeriodicity.Quarterly);
        panel.Apply();

        Assert.Equal(4, vm.ExtraColumnCount);
        Assert.Contains("quarterly", panel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Alt_N_panel_explains_a_withheld_scenario_axis_instead_of_generating_nothing()
    {
        var company = Robert();
        company.AddScenario(new Scenario(Guid.NewGuid(), "Optimistic"));

        var vm = new ReportsViewModel(company, ReportKind.CashFlow);
        var panel = new AutoColumnsViewModel(vm) { ByScenario = true };
        Assert.False(panel.SupportsScenarioColumns);

        panel.Apply();

        Assert.False(vm.IsComparative);
        Assert.Contains("actual movements only", panel.Status);
    }

    // =============================================================== print + export

    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    [InlineData(ReportKind.TrialBalance)]
    public void The_EXPORT_of_a_comparative_report_carries_every_column_on_screen(ReportKind kind)
    {
        // 🔴 THE DEFECT THIS FIXES. In comparative mode both projectors read the SINGLE-COLUMN rows the view
        // model keeps intact underneath, so a five-column comparison exported as ONE column under the same
        // title — nothing threw, nothing was blank, the file was simply a different report.
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, new DateOnly(2020, 6, 30));
        Assert.True(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));
        Assert.Equal(4, vm.ComparativeColumns.Count); // base + 3 months

        var export = ReportTabularProjector.Project(vm);

        // One Particulars column plus one per comparison column — captioned exactly as the screen captions them.
        Assert.Equal(vm.ComparativeColumns.Count + 1, export.Columns.Count);
        Assert.Equal("Particulars", export.Columns[0].Header);
        for (var i = 0; i < vm.ComparativeColumns.Count; i++)
            Assert.Equal(vm.ComparativeColumns[i].Label, export.Columns[i + 1].Header);

        // A leading totals row, then one row per comparative row — every row the width of the band.
        Assert.Equal(vm.ComparativeRows.Count + 1, export.Rows.Count);
        Assert.All(export.Rows, r => Assert.Equal(export.Columns.Count, r.Cells.Count));

        var totals = export.Rows[0];
        Assert.True(totals.IsTotal);
        for (var i = 0; i < vm.ComparativeColumns.Count; i++)
            Assert.Equal(vm.ComparativeColumns[i].TotalText, totals.Cells[i + 1].TextValue);

        // The data cells are the screen's cells verbatim, including the sign suffix.
        for (var r = 0; r < vm.ComparativeRows.Count; r++)
        {
            var screen = vm.ComparativeRows[r];
            var exported = export.Rows[r + 1];
            Assert.Contains(screen.Label, exported.Cells[0].TextValue);
            for (var i = 0; i < screen.Cells.Count; i++)
                Assert.Equal(screen.Cells[i], exported.Cells[i + 1].TextValue);
        }
    }

    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    [InlineData(ReportKind.TrialBalance)]
    public void The_PRINT_model_of_a_comparative_report_carries_every_column_on_screen(ReportKind kind)
    {
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, new DateOnly(2020, 6, 30));
        Assert.True(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));

        var print = ReportPrintProjector.Project(vm);

        Assert.Equal(vm.ComparativeColumns.Count + 1, print.Columns.Count);
        Assert.Equal("Particulars", print.Columns[0].Header);
        for (var i = 0; i < vm.ComparativeColumns.Count; i++)
        {
            // The print projector ASCII-folds every caption it emits — the PDF writer renders a non-ASCII glyph
            // as '?', so the en-dash in a period label becomes a hyphen. That fold is the projector's long-
            // standing contract, not a dropped caption, so the comparison applies it rather than fighting it.
            var expected = vm.ComparativeColumns[i].Label.Replace('–', '-').Replace('—', '-');
            Assert.Equal(expected, print.Columns[i + 1].Header);
            Assert.False(string.IsNullOrWhiteSpace(print.Columns[i + 1].Header));
        }

        Assert.Equal(vm.ComparativeRows.Count + 1, print.Rows.Count);
        Assert.All(print.Rows, r => Assert.Equal(print.Columns.Count, r.Cells.Count));
        Assert.True(print.Rows[0].IsTotal);

        // 🔴 Not a page of blank cells: every data row carries a figure in every column (both/all columns here
        // cover windows inside the same book, and at minimum the row must not be entirely empty).
        foreach (var row in print.Rows.Skip(1))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Cells[0]));
            Assert.Contains(row.Cells.Skip(1), c => !string.IsNullOrWhiteSpace(c));
        }
    }

    [Fact]
    public void Clearing_the_comparative_columns_returns_print_and_export_to_the_single_column_report()
    {
        // The branch must be reversible: Alt+C then "Single Column" has to put both artefacts back exactly.
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);
        vm.SetPeriod(BooksBegin, LastVoucher);

        var beforeExport = ReportTabularProjector.Project(vm);
        var beforePrint = ReportPrintProjector.Project(vm);

        vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly);
        Assert.NotEqual(beforeExport.Columns.Count, ReportTabularProjector.Project(vm).Columns.Count);

        vm.ClearComparative();

        Assert.False(vm.IsComparative);
        var afterExport = ReportTabularProjector.Project(vm);
        var afterPrint = ReportPrintProjector.Project(vm);
        Assert.Equal(beforeExport.Columns.Count, afterExport.Columns.Count);
        Assert.Equal(beforeExport.Rows.Count, afterExport.Rows.Count);
        Assert.Equal(beforePrint.Columns.Count, afterPrint.Columns.Count);
        Assert.Equal(beforePrint.Rows.Count, afterPrint.Rows.Count);
    }
}
