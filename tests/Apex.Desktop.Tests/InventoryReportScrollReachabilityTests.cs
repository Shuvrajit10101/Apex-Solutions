using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE UNREACHABLE-MONEY RULE, locked on the whole inventory-report family.
///
/// <para><b>The defect these lock (measured on the populated fixture, not inferred).</b> All ELEVEN inventory
/// report bodies are direct children of <c>InventoryReportPane</c> and none sets <c>Grid.Row</c>, so every one
/// of them landed in row 0. That row was <c>Auto</c>, which Avalonia measures at INFINITE height and never
/// constrains — so each body's own <c>*</c> list row grew to its FULL content height and the
/// <see cref="ScrollViewer"/> inside it reported <c>Extent == Viewport</c> with <c>maxOffset 0</c>: a scroller
/// that can never scroll and can never show a bar. The surplus was then clipped by the Miller pane with no cue
/// that anything existed below the cut.</para>
///
/// <para><b>Measured before the fix</b> (Regular fixture company, 4 window sizes): ALL ELEVEN reports reported
/// <c>Extent == Viewport</c> and <c>maxOffset == 0</c>. Three had enough rows to actually strand content —
/// <b>Stock Summary</b> (725px of rows in a 539/587/719px pane at 1280x720 / 1366x768 / 1600x900),
/// <b>Price List</b> (2,400px of rows in a 539–899px pane, stranded at ALL FOUR sizes including 1920x1080),
/// and <b>Reorder Status</b> (550px in a 539px pane at 1280x720). The other eight were LATENT: structurally
/// identical, merely short of rows today. Stock Summary was the financial-misread case the audit ranked worst
/// — the <c>Grand Total 20,92,482.50</c> row sat at y=842 against a pane clip at y=688, so a user read a
/// closing-stock figure with roughly 3.4 lakh on screen out of 20.9 lakh and nothing indicating a cut.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> <see cref="Visual.IsEffectivelyVisible"/> stays
/// <c>true</c> for a row clipped by an ANCESTOR, so a visibility assertion proves nothing. These tests measure
/// layout instead: the scroller's own <c>Extent</c>/<c>Viewport</c> arithmetic, and — for the Grand Total — a
/// real scroll to the maximum offset followed by a rectangle comparison against the Miller pane's clip. No
/// Skia, no <c>CaptureRenderedFrame</c>, no <c>TextLayout</c>/<c>TextRuns</c>: green on the 3-OS CI runners,
/// which have no SkiaSharp.</para>
/// </summary>
public sealed class InventoryReportScrollReachabilityTests
{
    /// <summary>
    /// Every report body parented directly by <c>InventoryReportPane</c>. The list is exhaustive by
    /// construction — one entry per <c>IsVisible</c>-gated child of that pane — so a future report added to the
    /// pane without a matching entry here is the only way to escape this lock.
    /// </summary>
    public static IEnumerable<object[]> InventoryReports() =>
        from kind in new[]
        {
            ReportKind.StockSummary, ReportKind.GodownSummary, ReportKind.StockItemMovement,
            ReportKind.ReorderStatus, ReportKind.PhysicalStockRegister, ReportKind.OrderRegister,
            ReportKind.ReceiptNoteRegister, ReportKind.JobWorkInOrderBook, ReportKind.Batchwise,
            ReportKind.BatchAgeAnalysis, ReportKind.PriceList,
        }
        from size in Sizes
        select new object[] { kind, size[0], size[1] };

    private static readonly int[][] Sizes =
    {
        new[] { 1920, 1080 }, new[] { 1600, 900 }, new[] { 1366, 768 }, new[] { 1280, 720 },
    };

    // ---------------------------------------------------------------- scaffolding

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Opens the REALISTICALLY POPULATED fixture company (28 stock items, 51 vouchers, real price levels)
    /// through the real company-select path at an EXPLICIT window size. A thin seed would make every one of
    /// these assertions vacuous — a pane only strands rows it actually has.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenPopulated(int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInvScroll_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
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

    private static Grid Pane(MainWindow window) =>
        Descendants(window).OfType<Grid>().First(g => g.Name == "InventoryReportPane");

    /// <summary>The bottom edge of the Miller pane — the thing that actually clips a report row.</summary>
    private static double ClipBottom(MainWindow window)
    {
        var cascade = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "CascadeScroller");
        var presenter = Descendants(cascade).OfType<ScrollContentPresenter>().First();
        return presenter.TranslatePoint(default, window)!.Value.Y + presenter.Bounds.Height;
    }

    /// <summary>
    /// The scroller that actually carries the report rows: the tallest vertically-ENABLED scroller inside the
    /// pane. Deliberately ignores <c>VerticalScrollBarVisibility="Disabled"</c> scrollers (the horizontal
    /// wrappers), because an offset assigned to one of those moves under programmatic assignment while the
    /// user cannot operate it — crediting it would prove reachability that does not exist.
    /// </summary>
    private static ScrollViewer? RowScroller(Grid pane) =>
        Descendants(pane).OfType<ScrollViewer>()
            .Where(s => s.IsEffectivelyVisible && s.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
            .OrderByDescending(s => s.Extent.Height)
            .FirstOrDefault();

    // ---------------------------------------------------------------- A: nothing is stranded

    /// <summary>
    /// A — the family invariant. Content taller than the pane MUST be reachable by a user-operable scroll.
    /// This is the purest statement of the defect: before the fix every one of the eleven reported
    /// <c>Extent == Viewport</c>, so overflow was structurally unscrollable rather than merely unscrolled.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(InventoryReports))]
    public void Inventory_report_rows_are_never_stranded_beyond_a_scroller_that_cannot_scroll(
        ReportKind kind, int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.OpenReport(kind);
            Pump(window);

            var pane = Pane(window);
            var scroller = RowScroller(pane);
            Assert.True(scroller != null, $"{kind} has no vertically-scrollable row scroller at {width}x{height}.");

            // THE detectable signature of the defect. When the row scroller is measured inside an
            // unconstrained (Auto) track it sizes its VIEWPORT to the whole content, so Extent == Viewport
            // and maxOffset is pinned at 0 no matter how much content there is. A viewport larger than the
            // pane that clips it is therefore proof the scroller was never constrained — and it is checkable
            // on all eleven reports, including the eight that are merely too short to strand rows today.
            Assert.True(
                scroller!.Viewport.Height <= pane.Bounds.Height + 0.5,
                $"{kind} at {width}x{height}: row-scroller viewport is {scroller.Viewport.Height:F0}px " +
                $"inside a {pane.Bounds.Height:F0}px pane. It was measured unconstrained, so it reports " +
                $"Extent {scroller.Extent.Height:F0} == Viewport and maxOffset 0 — anything past the pane " +
                "edge is unreachable and unsignalled.");

            // And when content genuinely exceeds the viewport, the surplus must be reachable by a real,
            // user-operable scroll (this scroller is not a Disabled one — see RowScroller).
            var overflow = scroller.Extent.Height - scroller.Viewport.Height;
            if (overflow <= 0.5) return; // fits — nothing to reach

            scroller.Offset = new Vector(0, overflow);
            Pump(window);
            Assert.True(
                scroller.Offset.Y >= overflow - 0.5,
                $"{kind} at {width}x{height}: {overflow:F0}px of surplus rows could not be scrolled to " +
                $"(offset stuck at {scroller.Offset.Y:F0}).");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the structural cause, pinned directly. <c>InventoryReportPane</c> must expose exactly ONE row track
    /// and it must be a STAR. With <c>Auto</c> the row is measured at infinite height, which is what made
    /// <c>Extent == Viewport</c> possible on all eleven reports at once. Asserting the shape (not just the
    /// symptom) is what stops the regression returning via a report that happens to be short today.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Inventory_report_pane_row_track_is_a_single_star(int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.OpenReport(ReportKind.StockSummary);
            Pump(window);

            var pane = Pane(window);
            Assert.True(
                pane.RowDefinitions.Count == 1,
                $"InventoryReportPane declares {pane.RowDefinitions.Count} row tracks at {width}x{height}. " +
                "Every report body is parented at row 0 with no Grid.Row, so a second track only creates a " +
                "way for row 0 to become Auto again.");
            Assert.True(
                pane.RowDefinitions[0].Height.IsStar,
                $"InventoryReportPane row 0 is '{pane.RowDefinitions[0].Height}' at {width}x{height}, not a " +
                "star. An Auto track is measured at INFINITE height, so every report's inner scroller " +
                "reports Extent == Viewport and no scrollbar can ever appear.");

            Assert.True(
                pane.RowDefinitions[0].ActualHeight <= pane.Bounds.Height + 0.5,
                $"InventoryReportPane row 0 measured {pane.RowDefinitions[0].ActualHeight:F0}px inside a " +
                $"{pane.Bounds.Height:F0}px pane at {width}x{height} — the excess is unreachable because " +
                "nothing constrains or scrolls it.");
        }
        finally { Cleanup(window, tempDir); }
    }

    // ---------------------------------------------------------------- C: the money is visible

    /// <summary>
    /// C — 🔴 THE ACCEPTANCE BAR FOR THE WORST DEFECT IN THE APP: a user must be able to SEE the whole
    /// closing-stock Grand Total, not merely be told a scrollbar exists.
    ///
    /// <para>Scrolls the Stock Summary row scroller to its maximum offset — the same place the user's own
    /// scrollbar drag ends — and requires the LAST row's rectangle to land fully inside the Miller pane's clip.
    /// Compares against the pane clip rather than the window height, because the pane is what clips. Never
    /// consults <see cref="Visual.IsEffectivelyVisible"/>, which stayed <c>true</c> for this row even when it
    /// sat 154px below the cut.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Stock_summary_grand_total_is_reachable_at_every_window_height(int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.OpenReport(ReportKind.StockSummary);
            Pump(window);

            var total = vm.Reports!.Rows.LastOrDefault(r => r.IsTotal);
            Assert.True(total != null, "Stock Summary produced no Grand Total row — nothing to reach.");
            Assert.False(
                string.IsNullOrWhiteSpace(total!.Col6),
                "The Grand Total's closing value is blank, which reads as zero rather than as off-screen.");

            var pane = Pane(window);
            var scroller = RowScroller(pane);
            Assert.True(scroller != null, $"Stock Summary has no row scroller at {width}x{height}.");

            // Drive it exactly as the user's scrollbar would: to the very bottom.
            scroller!.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            Pump(window);

            var list = Descendants(window).OfType<ListBox>().First(l => l.Name == "StockSummaryList");
            list.ScrollIntoView(total);
            Pump(window);

            var container = list.ContainerFromItem(total);
            Assert.True(
                container != null,
                $"The Grand Total row is not realised at {width}x{height} even scrolled to the bottom.");

            var top = container!.TranslatePoint(default, window)!.Value.Y;
            var bottom = top + container.Bounds.Height;
            var clip = ClipBottom(window);

            Assert.True(
                bottom <= clip + 0.5 && top >= -0.5,
                $"Scrolled fully, the Grand Total row ('{total.Col1}' = {total.Col6}) occupies " +
                $"y={top:F0}..{bottom:F0} against a pane clip at y={clip:F0} at {width}x{height} — the " +
                "closing-stock total is unreadable, and nothing on screen says so.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// D — Price List, the second CRITICAL. 96 rows (2,400px) that reported <c>Extent == Viewport == 2400</c>
    /// at EVERY size including 1920x1080: a price list that could not be scrolled at all. Also pins that the
    /// rate columns stay present and reachable — the horizontal wrapper is legitimate and must keep working,
    /// which is what makes Rate / Disc % / Net Rate reachable in a narrow pane rather than clipped away.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Price_list_scrolls_vertically_and_keeps_its_rate_columns(int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.OpenReport(ReportKind.PriceList);
            Pump(window);

            var pane = Pane(window);
            var scroller = RowScroller(pane);
            Assert.True(scroller != null, $"Price List has no row scroller at {width}x{height}.");

            Assert.True(
                scroller!.Extent.Height > scroller.Viewport.Height + 0.5,
                $"Price List reports Extent {scroller.Extent.Height:F0} <= Viewport " +
                $"{scroller.Viewport.Height:F0} at {width}x{height} with " +
                $"{vm.Reports!.Rows.Count} rows — it will never scroll, so the prices below the cut are " +
                "unreachable.");

            Assert.True(
                scroller.Viewport.Height <= pane.Bounds.Height + 0.5,
                $"Price List viewport ({scroller.Viewport.Height:F0}px) exceeds its pane " +
                $"({pane.Bounds.Height:F0}px) at {width}x{height} — it was measured unconstrained, which is " +
                "exactly how Extent == Viewport arises.");

            // Every priced column must still exist with a real width; the wrapper scrolls to them.
            var grid = Descendants(pane).OfType<Grid>()
                .First(g => g.IsEffectivelyVisible && g.ColumnDefinitions.Count == 8 && g.MinWidth == 1080);
            foreach (var (i, name) in new[] { (5, "Rate"), (6, "Disc %"), (7, "Net Rate") })
                Assert.True(
                    grid.ColumnDefinitions[i].ActualWidth > 1,
                    $"Price List '{name}' column collapsed to " +
                    $"{grid.ColumnDefinitions[i].ActualWidth:F0}px at {width}x{height}.");
        }
        finally { Cleanup(window, tempDir); }
    }
}
