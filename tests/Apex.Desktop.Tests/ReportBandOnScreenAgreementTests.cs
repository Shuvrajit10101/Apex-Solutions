using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE DOCUMENT'S COLUMN HEADINGS MUST BE THE SCREEN'S OWN COLUMN HEADINGS — ASSERTED AGAINST THE REALISED
/// WINDOW, NOT AGAINST A TABLE.
///
/// <para><b>Why a rendered comparison and not a caption list.</b> <see cref="ReportColumnBandCoverageTests"/>
/// proves every generic-path report now emits a NON-BLANK caption for every printed and exported column. That
/// is only half the requirement, and the weaker half: a plausible heading placed over the wrong figures is
/// <i>worse</i> than a blank one, because a reader believes it. Nothing in a caption table can tell the two
/// apart — both look like a tidy list of strings. The only authority that can is the grid the operator is
/// looking at when they press P or E. So this file opens each report in a REALISED
/// <see cref="MainWindow"/>, reads the <c>colHdr</c> headings that actually laid out on screen, and requires
/// the printed and exported header bands to be that same sequence. A caption cannot be invented here: to add
/// one to the band you must first put it on the grid.</para>
///
/// <para><b>It also catches the on-screen half of the blank-document defect, which nothing else did.</b> The
/// VAT Computation grid and the shared CST Declaration Forms grid are parented inside
/// <c>InventoryReportPane</c>, whose visibility is <see cref="ReportsViewModel.ShowSingleInventoryGrid"/> — and
/// neither kind is in <see cref="ReportsViewModel.IsInventoryReport"/>. Measured on this fixture before the
/// fix: all three built real rows (10 / 12 / 12) and <c>InventoryReportPane.IsEffectivelyVisible</c> was
/// <c>false</c>, so <b>not one figure was drawn</b> — the operator got the empty accounting Particulars/Dr/Cr
/// table that the <see cref="ReportsViewModel.IsAccountingReport"/> negation mis-claimed for them. A
/// view-model-flag assertion cannot see that; a visual-tree assertion cannot miss it.</para>
///
/// <para><b>One realised window, every kind, all failures reported.</b> Deliberately a single fact rather than a
/// theory: the window and the posted fixture are the expensive part, and a sweep that names every mismatch in
/// one message is more useful when a future report ships with a band that does not match its grid.</para>
/// </summary>
public sealed class ReportBandOnScreenAgreementTests
{
    [AvaloniaFact]
    public void Every_report_prints_and_exports_the_column_headings_its_own_grid_shows()
    {
        var (window, vm, tempDir) = OpenPopulatedWithVat();
        try
        {
            var failures = new List<string>();
            var compared = 0;

            foreach (var kind in Enum.GetValues<ReportKind>())
            {
                // Ask a throwaway view model whether this kind is on the generic Col1..Col8 path. The dynamic
                // matrix reports (payroll + the re-homed eight) carry a live column band already locked by
                // RehomedReportSurfaceTests, and the accounting family's band is the fixed Particulars/Dr/Cr
                // header that is not per-kind.
                if (!ReportColumnBands.NeedsBand(new ReportsViewModel(vm.Company!, kind))) continue;

                vm.OpenReport(kind);
                Pump(window);

                var report = vm.Reports;
                if (report is null) { failures.Add($"{kind}: OpenReport left no report view model."); continue; }

                var onScreen = VisibleColumnHeadings(window);
                var print = ReportPrintProjector.Project(report);
                var export = ReportTabularProjector.Project(report);

                if (onScreen.Count == 0)
                {
                    failures.Add(
                        $"{kind}: NO column heading laid out on screen at all, while the report built "
                        + $"{report.Rows.Count} rows and its document carries {print.Columns.Count} columns — the "
                        + "grid's own pane is collapsed, so every figure is computed and none of it is drawn.");
                    continue;
                }

                compared++;

                // The print path is ASCII-only on its way to the PDF writer (₹ folds to "Rs."), so both sides
                // are compared through that same fold rather than dropping the glyph from the band — which
                // would have made the document disagree with the grid about a column's unit.
                var screenBand = onScreen.Select(ReportPrintProjector.Ascii).ToArray();
                var printBand = print.Columns.Select(c => ReportPrintProjector.Ascii(c.Header)).ToArray();
                var exportBand = export.Columns.Select(c => ReportPrintProjector.Ascii(c.Header)).ToArray();

                if (!printBand.SequenceEqual(screenBand))
                    failures.Add($"{kind}: PRINTED band [{string.Join(" | ", printBand)}] is not the band on "
                        + $"screen [{string.Join(" | ", screenBand)}].");

                if (!exportBand.SequenceEqual(screenBand))
                    failures.Add($"{kind}: EXPORTED band [{string.Join(" | ", exportBand)}] is not the band on "
                        + $"screen [{string.Join(" | ", screenBand)}].");
            }

            Assert.True(failures.Count == 0,
                $"{failures.Count} report(s) do not print/export the headings their own grid shows:"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));

            // 🔴 ANTI-VACUITY. If a routing change ever stops these reports reaching the generic path, the sweep
            // above would pass by comparing nothing. The count is a floor, not a lock: a new generic report must
            // be able to raise it without editing this line.
            Assert.True(compared >= 37,
                $"only {compared} generic-path reports were compared against their own grid; the measured "
                + "surface on this fixture is 37, so the sweep has stopped reaching reports it used to cover.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// 🔴 THE THREE STATUTORY WORKING PAPERS MUST HAVE THEIR FIGURES ON SCREEN, NOT ONLY THEIR HEADINGS.
    /// A band that lays out over an empty body is the same defect one step along: the operator sees a labelled
    /// table and no numbers. Asserted on the drawn text, per report.
    /// </summary>
    [AvaloniaFact]
    public void The_VAT_and_CST_working_papers_draw_their_own_rows_on_screen()
    {
        var (window, vm, tempDir) = OpenPopulatedWithVat();
        try
        {
            foreach (var kind in new[]
                     {
                         ReportKind.VatComputation,
                         ReportKind.CstFormsReceivable,
                         ReportKind.CstFormsIssuable,
                     })
            {
                vm.OpenReport(kind);
                Pump(window);

                var report = vm.Reports!;
                Assert.True(report.Rows.Count > 0, $"{kind}: the fixture produced no rows to draw.");

                // A body figure the builder computed, taken from the report itself rather than hardcoded, so
                // this cannot pass by matching some other pane's text.
                var figure = report.Rows
                    .SelectMany(r => new[] { r.Col2, r.Col3, r.Col4, r.Col5 })
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s.Any(char.IsDigit));
                Assert.NotNull(figure);

                var drawn = Descendants(window).OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0)
                    .Select(t => t.Text ?? string.Empty)
                    .ToList();

                Assert.Contains(figure, drawn);
            }
        }
        finally { Cleanup(window, tempDir); }
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// The visible column headings of whichever report pane is showing, in reading order.
    ///
    /// <para>Restricted to the three named report panes so a heading belonging to a Miller drill column, or to
    /// the CST report's own "Set / Alter Form No." editor below the grid, cannot be mistaken for part of the
    /// band. Ordered by laid-out position — the order an operator reads them in — rather than by visual-tree
    /// order, so the assertion is about the band as it appears and not about XAML declaration order.</para>
    /// </summary>
    private static IReadOnlyList<string> VisibleColumnHeadings(MainWindow window) =>
        Descendants(window).OfType<Grid>()
            .Where(g => g.IsEffectivelyVisible
                && g.Name is "InventoryReportPane" or "GstReportPane" or "StatutoryReportPane")
            .SelectMany(pane => Descendants(pane).OfType<TextBlock>())
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("colHdr"))
            .Select(t => (Text: t.Text ?? string.Empty, Point: t.TranslatePoint(default, window) ?? default))
            .OrderBy(x => Math.Round(x.Point.Y)).ThenBy(x => x.Point.X)
            .Select(x => x.Text)
            .ToList();

    /// <summary>
    /// The populated fixture with State VAT switched on, opened through the real company-select route.
    ///
    /// <para>VAT is enabled BEFORE the company is saved, because the view model loads it back from storage: the
    /// three State-tax reports sit behind that feature gate and would otherwise show their F11 empty state
    /// instead of a band.</para>
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenPopulatedWithVat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(),
            "ApexBandScreen_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var storage = new CompanyStorage(tempDir);

        var company = PopulatedCompanyFixture.BuildRegular();
        new VatService(company).EnableVat(tin: "29123456789");
        storage.Save(company);

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var child in v.GetVisualChildren())
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }
}
