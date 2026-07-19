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
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE UNREACHABLE-FIELD RULE, locked on the Scenario master — the same defect
/// <see cref="LedgerMasterScrollLayoutTests"/> locks on the Ledger master, in the second template that carried it.
///
/// <para><b>The defect these lock (measured, not inferred).</b> The Scenario Creation page root was
/// <c>&lt;Grid RowDefinitions="Auto,Auto,Auto,*"&gt;</c> with the create form in row 1. An <c>Auto</c> row takes its
/// child's FULL desired height and never constrains it, so the form pinned at <b>680–691px</b> regardless of window
/// height. Measured page-root rows before the fix: 1920x1080 <c>36|680|22|205</c>, 1600x900 <c>36|680|22|25</c>,
/// 1366x768 <c>36|691|22|0</c>, 1280x720 <c>36|691|22|0</c> — the sibling <c>*</c> scenarios-list row was starved to
/// <b>EXACTLY 0px</b> at the two laptop sizes, and because an <c>Auto</c> row never overflows, every ScrollViewer on
/// the page reported <c>Extent == Viewport</c> with <c>maxOffset 0</c>. Nothing moved.</para>
///
/// <para>Measured consequence at 1280x720: <b>16 controls unreachable</b> — the <b>Create (Ctrl+A)</b> button ended
/// 86px below the pane clip, six voucher-type checkboxes (Rejection In/Out, Sales, Sales (POS), Sales Order, Stock
/// Journal) sat past it, and the entire scenarios list including its Scenario / Actuals / Includes header was gone.
/// A scenario could not be created on any laptop display.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> <see cref="Visual.IsEffectivelyVisible"/> stays <c>true</c>
/// for all of those controls while they are off-pane, because the clipper is an ANCESTOR — a visibility assertion
/// would prove nothing. These measure layout instead: row arithmetic, a non-starved list row, and a real scroll that
/// brings the last control inside the clip rectangle. No Skia, no <c>CaptureRenderedFrame</c>, no
/// <c>TextLayout</c> — green on the 3-OS CI runners, which have no SkiaSharp.</para>
/// </summary>
public sealed class ScenarioMasterScrollLayoutTests
{
    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// Opens Scenario Creation through the REAL navigation path at an EXPLICIT window size, so the result never
    /// depends on whatever default the headless runner happens to pick.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenScenarioMaster(int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexScenarioScroll_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();

        vm.NewCompanyName = "Scenario Scroll Co";
        vm.CreateCompany();

        vm.ShowScenarioMaster();
        Pump(window);
        return (window, vm, tempDir);
    }

    /// <summary>
    /// The Scenario page root: the 4-row Grid hosting title / form / list-header / list. Anchored on a PAGE-ONLY
    /// marker on purpose — "Scenario Creation" is also the cascade COLUMN TITLE, and matching that walks up to the
    /// shell's own 4-row Grid instead, silently measuring the wrong thing.
    /// </summary>
    private static Grid PageRoot(MainWindow window)
    {
        var marker = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Include these voucher types");
        Assert.True(marker != null, "Scenario Creation did not render its voucher-type block.");

        for (Visual? p = marker; p != null; p = p.GetVisualParent())
            if (p is Grid g && g.RowDefinitions.Count == 4)
                return g;

        throw new InvalidOperationException("Scenario page root (4-row Grid) not found.");
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A — the page root's rows must FIT the space the Miller shell gave it. With row 1 <c>Auto</c> the rows summed
    /// 749 against a 583px pane at 1280x720.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Scenario_page_rows_fit_the_pane_at_every_window_height(int width, int height)
    {
        var (window, _, tempDir) = OpenScenarioMaster(width, height);
        try
        {
            var grid = PageRoot(window);
            var rowSum = grid.RowDefinitions.Sum(r => r.ActualHeight);
            Assert.True(
                rowSum <= grid.Bounds.Height + 0.5,
                $"Scenario page rows overflow the pane at {width}x{height}: rows sum to {rowSum:F0} " +
                $"inside {grid.Bounds.Height:F0} — the excess is unreachable because nothing scrolls.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the scenarios list must keep a real share of the pane. The Auto form row starved it to EXACTLY zero at
    /// 1366x768 and 1280x720, so the operator saw no scenario list and no list header at all.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Existing_scenarios_list_row_is_not_starved(int width, int height)
    {
        var (window, _, tempDir) = OpenScenarioMaster(width, height);
        try
        {
            var grid = PageRoot(window);
            Assert.True(
                grid.RowDefinitions[3].ActualHeight > 0,
                $"The scenarios list row was starved to {grid.RowDefinitions[3].ActualHeight:F0}px " +
                $"at {width}x{height} by the create form above it.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// C — REACHABILITY, not visibility. Scroll the form to its maximum offset and require the Create button to land
    /// inside the Miller pane's clip rectangle. Compares against the <c>CascadeScroller</c>'s presenter bottom (the
    /// pane is what clips, not the window) and never consults <c>IsEffectivelyVisible</c>, which stayed <c>true</c>
    /// for this button while it sat 86px below the clip edge.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Create_button_becomes_reachable_by_scrolling_at_every_window_height(int width, int height)
    {
        var (window, _, tempDir) = OpenScenarioMaster(width, height);
        try
        {
            var grid = PageRoot(window);

            // Scope to the page root: other master templates realise an identical "Create (Ctrl+A)" button.
            var button = Descendants(grid).OfType<Button>()
                .FirstOrDefault(b => b.Content as string == "Create (Ctrl+A)");
            Assert.True(button != null, "The Scenario Creation page has no Create (Ctrl+A) button.");

            var cascade = Descendants(window).OfType<ScrollViewer>()
                .FirstOrDefault(s => s.Name == "CascadeScroller");
            Assert.True(cascade != null, "CascadeScroller not found.");
            var presenter = Descendants(cascade!).OfType<ScrollContentPresenter>().First();
            var clipBottom = presenter.TranslatePoint(default, window)!.Value.Y + presenter.Bounds.Height;

            // The form's own scroller — the first vertically-enabled ScrollViewer above the button.
            ScrollViewer? scroller = null;
            for (Visual? p = button; p != null; p = p.GetVisualParent())
                if (p is ScrollViewer sv && sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                {
                    scroller = sv;
                    break;
                }

            Assert.True(
                scroller != null,
                $"No vertically-scrollable ScrollViewer wraps the Scenario create form at {width}x{height}; " +
                "the form is taller than the pane, so its lower fields cannot be reached at all.");

            scroller!.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            Pump(window);

            var bottom = button!.TranslatePoint(default, window)!.Value.Y + button.Bounds.Height;
            Assert.True(
                bottom <= clipBottom + 0.5,
                $"After scrolling to the bottom, the Create button still ends at y={bottom:F0} " +
                $"below the pane clip at y={clipBottom:F0} ({width}x{height}) — a scenario cannot be created.");
        }
        finally { Cleanup(window, tempDir); }
    }
}
