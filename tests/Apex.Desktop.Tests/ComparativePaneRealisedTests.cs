using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Reports;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 11.14 / 11.17 — the comparative pane in a REALISED window.
///
/// <para>🔴 <b>Why a realised window and not the view-model collections alone.</b> Three shipped reports once
/// printed pages of entirely blank cells, and the cause was never in the data: the grids were parented inside a
/// pane whose <c>IsVisible</c> was false, so every figure was computed and none of it drawn. A view-model
/// assertion is structurally blind to that — <c>ComparativeRows</c> was full the whole time. These tests add
/// two comparison columns to the two newly-comparative flow statements and then assert, against the arranged
/// visual tree, that the pane really is on screen and that the realised header and body text runs really carry
/// text.</para>
/// </summary>
public sealed class ComparativePaneRealisedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ComparativePaneRealisedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCmpPane_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a temp dir that will not delete must not fail a test */ }
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        // Same geometry discipline as AccountingRowLayoutTests: MainWindow.axaml hardcodes Width/Height, so the
        // size is re-asserted after Show(), and two RunJobs passes let the cascade page column converge.
        var win = new MainWindow { DataContext = vm };
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1400; win.Height = 1080;
        win.Show();
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1400; win.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return win;
    }

    private static Grid ComparativePane(MainWindow win)
        => win.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ComparativePane");

    [AvaloniaTheory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    [InlineData(ReportKind.TrialBalance)]
    public void The_comparative_pane_is_REALISED_with_captions_and_cells_that_carry_text(ReportKind kind)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowGateway();
        vm.OpenReport(kind);

        var report = vm.Reports!;
        report.SetPeriod(new DateOnly(2020, 4, 1), new DateOnly(2020, 6, 30));
        Assert.True(report.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));
        Assert.Equal(4, report.ComparativeColumns.Count); // base + 3 months

        var win = Show(vm);

        // 1. THE PANE IS ACTUALLY ON SCREEN — the check the three blank-page reports failed.
        var pane = ComparativePane(win);
        Assert.True(pane.IsEffectivelyVisible, "the comparative pane is collapsed — every figure would be undrawn");
        Assert.True(pane.Bounds.Width > 0 && pane.Bounds.Height > 0,
            $"the comparative pane arranged to {pane.Bounds.Width}x{pane.Bounds.Height}");

        // 2. THE SINGLE-COLUMN GRID YIELDED. Both panes visible at once would overlay two reports.
        Assert.False(report.ShowSingleAccountingGrid);

        // 3. THE CAPTIONS ARE REALISED AND CARRY TEXT. Every column label must appear in the live tree.
        var realisedText = win.GetVisualDescendants().OfType<TextBlock>()
                              .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                              .Select(t => t.Text!)
                              .ToList();
        foreach (var column in report.ComparativeColumns)
        {
            Assert.Contains(column.Label, realisedText);
            Assert.Contains(column.TotalText, realisedText);
        }

        // 4. THE CELLS ARE REALISED AND CARRY TEXT — not a page of blanks. Take the first data row's cells
        //    from the view model and require each to be present in the realised tree.
        Assert.NotEmpty(report.ComparativeRows);
        var firstRow = report.ComparativeRows[0];
        Assert.Contains(firstRow.Label, realisedText);
        foreach (var cell in firstRow.Cells.Where(c => !string.IsNullOrWhiteSpace(c)))
            Assert.Contains(cell, realisedText);

        // 5. AND THE ASSERTION IS NOT VACUOUS: the pane really did realise its own text runs.
        var paneText = pane.GetVisualDescendants().OfType<TextBlock>()
                           .Count(t => !string.IsNullOrWhiteSpace(t.Text));
        Assert.True(paneText >= report.ComparativeColumns.Count,
            $"the comparative pane realised only {paneText} non-empty text runs");
    }

    [Fact]
    public void A_report_outside_the_comparative_set_never_shows_the_pane()
    {
        // The negative half: the pane is bound to IsComparative, so a kind the vendor excludes must leave it
        // collapsed. Asserted on the flag rather than the tree, because a collapsed pane has no bounds to read.
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowGateway();
        vm.OpenReport(ReportKind.DayBook);

        Assert.False(vm.Reports!.SupportsComparative);
        Assert.False(vm.Reports!.IsComparative);
    }
}
