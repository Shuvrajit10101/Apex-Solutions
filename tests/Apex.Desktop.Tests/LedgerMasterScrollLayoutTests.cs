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
/// 🔴 THE UNREACHABLE-FIELD RULE, locked on the Ledger master.
///
/// <para><b>The defect these lock (measured, not inferred).</b> The Ledger Creation page root was
/// <c>&lt;Grid RowDefinitions="Auto,Auto,Auto,*"&gt;</c> with the create form in row 1. An <c>Auto</c> row takes
/// its FULL desired height, so with GST + TDS + TCS enabled the form row measured <b>1101px</b> inside a pane
/// only <b>942px</b> tall at 1920x1080 — a 219px overflow that (a) starved the sibling <c>*</c> existing-ledgers
/// row to <b>zero</b> and (b) never surfaced, because an <c>Auto</c> row never constrains its child, so nothing
/// "overflowed" and no scrollbar could appear. The only <see cref="ScrollViewer"/> on the whole ancestor chain
/// was the Miller-column <c>CascadeScroller</c>, which is deliberately
/// <c>VerticalScrollBarVisibility="Disabled"</c>.</para>
///
/// <para>Measured consequence: the operator could not reach <b>Party PAN</b>, <b>Deductee Type</b>,
/// <b>Collectee Type</b>, <b>Deduct TDS in the same voucher</b>, the entire <b>Interest Calculation</b> block,
/// or the <b>Create (Ctrl+A)</b> button — at ANY window height, since the form's absolute layout is
/// height-invariant and only the pane's clip boundary moves. At 1366x768 the last thing drawn was a sliced
/// <c>Registration</c> combo, matching the user's screenshot exactly. Fields a user cannot reach are fields a
/// user cannot fill.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> <see cref="Visual.IsEffectivelyVisible"/> stays
/// <c>true</c> for every one of those controls even while they are off-pane, because the clipper is an
/// ANCESTOR — so a visibility assertion proves nothing about reachability. These tests therefore measure
/// layout: row arithmetic, a non-starved list row, and a real scroll that actually brings the last control
/// inside the clip rectangle. No Skia, no <c>CaptureRenderedFrame</c>, no <c>TextLayout</c> — this stays green
/// on the 3-OS CI runners, which have no SkiaSharp.</para>
/// </summary>
public sealed class LedgerMasterScrollLayoutTests
{
    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Opens Ledger Creation through the REAL navigation path on a company with GST + TDS + TCS on — the
    /// configuration that makes the form tall — at an EXPLICIT window size, so the result never depends on
    /// whatever default the headless runner happens to pick.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenLedgerMaster(int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexLedgerScroll_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();

        vm.NewCompanyName = "Ledger Scroll Co";
        vm.CreateCompany();
        vm.Company!.Gst = new GstConfig
        {
            Enabled = true,
            HomeStateCode = "19",
            RegistrationType = GstRegistrationType.Regular,
        };
        vm.Company!.Tds = new TdsConfig { Enabled = true };
        vm.Company!.Tcs = new TcsConfig { Enabled = true };

        vm.ShowCreateMenu();
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");

        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>The Ledger page root: the 4-row Grid hosting header / form / list-header / list.</summary>
    private static Grid PageRoot(MainWindow window)
    {
        var marker = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => t.Text != null && t.Text.Contains("GST Details"));
        Assert.True(marker != null, "Ledger Creation did not render its GST Details block.");

        for (Visual? p = marker; p != null; p = p.GetVisualParent())
            if (p is Grid g && g.RowDefinitions.Count == 4)
                return g;

        throw new InvalidOperationException("Ledger page root (4-row Grid) not found.");
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A — the page root's rows must FIT the space the Miller shell gave it. This is the purest statement of
    /// the defect: with row 1 <c>Auto</c> the rows summed 1161 against a 942px pane.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Ledger_page_rows_fit_the_pane_at_every_window_height(int width, int height)
    {
        var (window, _, tempDir) = OpenLedgerMaster(width, height);
        try
        {
            var grid = PageRoot(window);
            var rowSum = grid.RowDefinitions.Sum(r => r.ActualHeight);
            Assert.True(
                rowSum <= grid.Bounds.Height + 0.5,
                $"Ledger page rows overflow the pane at {width}x{height}: rows sum to {rowSum:F0} " +
                $"inside {grid.Bounds.Height:F0} — the excess is unreachable because nothing scrolls.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the existing-ledgers list must keep a real share of the pane. The Auto form row starved it to
    /// exactly zero, so the operator saw no ledger list at all.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Existing_ledgers_list_row_is_not_starved(int width, int height)
    {
        var (window, _, tempDir) = OpenLedgerMaster(width, height);
        try
        {
            var grid = PageRoot(window);
            Assert.True(
                grid.RowDefinitions[3].ActualHeight > 0,
                $"The existing-ledgers row was starved to {grid.RowDefinitions[3].ActualHeight:F0}px " +
                $"at {width}x{height} by the create form above it.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// C — REACHABILITY, not visibility. Scroll the form to its maximum offset and require the LAST control on
    /// the page — the Create button — to land inside the Miller pane's clip rectangle. Deliberately compares
    /// against the <c>CascadeScroller</c>'s presenter bottom rather than the window height (the pane is what
    /// clips), and never consults <c>IsEffectivelyVisible</c>, which is <c>true</c> for this button even when
    /// it sits 162px below the clip edge.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Create_button_becomes_reachable_by_scrolling_at_every_window_height(int width, int height)
    {
        var (window, _, tempDir) = OpenLedgerMaster(width, height);
        try
        {
            var grid = PageRoot(window);

            // Scope to the page root: other master templates realise an identical "Create (Ctrl+A)" button.
            var button = Descendants(grid).OfType<Button>()
                .FirstOrDefault(b => b.Content as string == "Create (Ctrl+A)");
            Assert.True(button != null, "The Ledger Creation page has no Create (Ctrl+A) button.");

            // The clipper: the Miller cascade's content presenter.
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
                $"No vertically-scrollable ScrollViewer wraps the Ledger create form at {width}x{height}; " +
                "the form is taller than the pane, so its lower fields cannot be reached at all.");

            Assert.True(
                scroller!.Extent.Height > scroller.Viewport.Height,
                $"The Ledger form scroller reports Extent {scroller.Extent.Height:F0} <= Viewport " +
                $"{scroller.Viewport.Height:F0} at {width}x{height}, so it will never scroll.");

            scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
            Pump(window);

            var top = button!.TranslatePoint(default, window)!.Value.Y;
            var bottom = top + button.Bounds.Height;
            Assert.True(
                bottom <= clipBottom + 0.5,
                $"After scrolling to the bottom, the Create button still ends at y={bottom:F0} " +
                $"below the pane clip at y={clipBottom:F0} ({width}x{height}) — it is unreachable.");
        }
        finally { Cleanup(window, tempDir); }
    }
}
