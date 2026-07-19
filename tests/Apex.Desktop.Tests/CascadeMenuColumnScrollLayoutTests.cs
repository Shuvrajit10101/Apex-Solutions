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
/// 🔴 THE CASCADE MENU COLUMN MUST BE ABLE TO REPORT ITS OWN OVERFLOW.
///
/// <para><b>The defect these lock (measured, not inferred).</b> The shared Miller menu-column body was
/// <c>&lt;ScrollViewer VerticalScrollBarVisibility="Auto" Padding="0,6"&gt;</c>. Avalonia deducts that Padding from
/// the viewport it offers the child but never measures the child beyond it, so <c>Extent</c> came out as exactly
/// <c>Viewport − 12</c> at EVERY window height — measured on the root Gateway column: 1920x1080 <c>E=943/V=955</c>,
/// 1600x900 <c>E=763/V=775</c>, 1366x768 <c>E=631/V=643</c>, 1280x720 <c>E=594/V=595</c>. The comparison that drives
/// scrollbar visibility, <c>Extent &gt; Viewport</c>, was therefore STRUCTURALLY IMPOSSIBLE: no scrollbar could ever
/// appear and <c>maxOffset</c> was pinned at 0, no matter how much content the column held.</para>
///
/// <para>Two measured consequences. (1) The root Gateway column's items want 606px; at 1280x720 only 595px exists,
/// so <b>"Quit — Change Company"</b> ended 11px below the clip with no way to reach it — on the ~157 screens that
/// keep the Gateway column on screen. (2) Worse and size-independent: in a menu column long enough to overflow at
/// any size — e.g. the Day-Book <b>Alt+A Add-Voucher picker</b> on a company with many voucher types — the last row
/// stayed <b>16px past the clip at ALL FOUR heights even after scrolling to maximum offset</b>, because
/// <c>maxOffset</c> itself understated the content by the swallowed padding. A voucher type the operator could not
/// select, on a 1920x1080 monitor.</para>
///
/// <para>The fix moves the 6px breathing space to a <c>Margin</c> on the scrolled ItemsControl, so it is part of the
/// measured content and <c>Extent</c> tells the truth. These tests measure layout only — no Skia, no
/// <c>CaptureRenderedFrame</c>, no <c>TextLayout</c> — so they stay green on the 3-OS CI runners.</para>
/// </summary>
public sealed class CascadeMenuColumnScrollLayoutTests
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

    /// <summary>The Miller pane's clip edge — the pane is what clips, not the window.</summary>
    private static double ClipBottom(MainWindow window)
    {
        var cascade = Descendants(window).OfType<ScrollViewer>()
            .FirstOrDefault(s => s.Name == "CascadeScroller");
        Assert.True(cascade != null, "CascadeScroller not found.");
        var presenter = Descendants(cascade!).OfType<ScrollContentPresenter>().First();
        return presenter.TranslatePoint(default, window)!.Value.Y + presenter.Bounds.Height;
    }

    /// <summary>
    /// The REALISED, VISIBLE menu ROW for a label. Menu-row labels are painted as three <c>Run</c>s inside one
    /// TextBlock (the bare-letter hotkey is coloured), so <c>TextBlock.Text</c> is null for every one of them — the
    /// row must be found through its bound <see cref="MenuItemViewModel"/> instead.
    ///
    /// <para><b>Why the visibility/height filter is load-bearing.</b> The item template in <c>MainWindow.axaml</c>
    /// emits TWO Borders classed <c>menuRow</c> per item, sharing one DataContext: a section-header Border
    /// (<c>IsVisible="{Binding IsHeader}"</c>) and the selectable row (<c>IsVisible="{Binding IsSelectable}"</c>).
    /// For a normal row the header is the FIRST in visual order and is collapsed — <c>IsVisible=False</c>,
    /// <c>Bounds.Height=0</c> — pinned at the row's TOP edge. A plain first-match therefore returned that PHANTOM and
    /// every bottom-edge measurement understated the real row by its full height. Measured on the Day-Book Alt+A
    /// picker, fully scrolled, at 1920x1080: phantom bottom=1015.0 (33.0 above the clip) versus real bottom=1040.0
    /// (8.0 above it) — identical 25.0px understatement at 1600x900, 1366x768 and 1280x720. A reachability assertion
    /// reading the phantom cannot fail on an unreachable row, so it locks nothing.</para>
    ///
    /// <para>Filtering on <c>IsVisible &amp;&amp; Bounds.Height &gt; 0</c> leaves exactly one candidate per item, so
    /// the first match is now deterministically the realised row. First (not last) is kept so that a label occurring
    /// in more than one cascade column still resolves to the earliest column, as before.</para>
    /// </summary>
    private static Control? MenuRow(Visual scope, string label)
    {
        foreach (var v in Descendants(scope))
            if (v is Border b && b.Classes.Contains("menuRow")
                && b.DataContext is MenuItemViewModel m && m.Label == label
                && b.IsVisible && b.Bounds.Height > 0)
                return b;
        return null;
    }

    /// <summary>The first vertically-enabled ScrollViewer above a control.</summary>
    private static ScrollViewer? OwningScroller(Visual from)
    {
        for (Visual? p = from; p != null; p = p.GetVisualParent())
            if (p is ScrollViewer sv && sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                return sv;
        return null;
    }

    /// <summary>The scrolled content of a menu column — the ItemsControl the ScrollViewer hosts.</summary>
    private static ItemsControl ScrolledContent(ScrollViewer sv) =>
        Descendants(sv).OfType<ItemsControl>().First();

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Boot(int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCascadeScroll_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        vm.NewCompanyName = "Cascade Scroll Co";
        vm.CreateCompany();
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A — THE MECHANISM, stated exactly. A menu column's scroller must not UNDERSTATE the height of the content it
    /// is scrolling. With <c>Padding="0,6"</c> on the ScrollViewer, <c>Extent</c> came out as exactly the content's
    /// desired height MINUS 12 — the swallowed padding — measured at every one of the four heights. That 12px is the
    /// whole defect: it is content the scroller does not know exists, so it can neither reveal it nor scroll to it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Menu_column_extent_does_not_understate_its_content(int width, int height)
    {
        var (window, vm, tempDir) = Boot(width, height);
        try
        {
            // A picker long enough to overflow at every size: 30 extra active voucher types.
            for (var i = 1; i <= 30; i++)
                vm.Company!.AddVoucherType(new VoucherType(
                    Guid.NewGuid(), $"Bulk Type {i:00}", VoucherBaseType.Sales));
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.OpenAddVoucherFromReport();
            Pump(window);

            var row = MenuRow(window, "Bulk Type 30");
            Assert.True(row != null, "The Add-Voucher picker did not realise its last voucher type.");
            var scroller = OwningScroller(row!);
            Assert.True(scroller != null, "The Add-Voucher picker column has no vertical scroller.");

            var content = ScrolledContent(scroller!);
            Assert.True(
                scroller!.Extent.Height >= content.DesiredSize.Height - 0.5,
                $"The menu column's scroller reports Extent {scroller.Extent.Height:F0} for content that needs " +
                $"{content.DesiredSize.Height:F0} at {width}x{height} — it is understating its own content by " +
                $"{content.DesiredSize.Height - scroller.Extent.Height:F0}px, which is therefore unscrollable.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the user-facing consequence of A on the Gateway. The root column with every feature switched on needs
    /// 606px; at 1280x720 its viewport is 595px. A column whose content exceeds its viewport MUST offer a real
    /// maxOffset. Before the fix it reported <c>E=594 / V=595 / maxOffset=0</c> — an 11px overflow that no scrollbar
    /// announced and no gesture could reach, on the ~157 screens that keep the Gateway column on show.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void An_overflowing_menu_column_offers_a_real_scroll_range(int width, int height)
    {
        var (window, vm, tempDir) = Boot(width, height);
        try
        {
            // The tallest real root column: every feature that adds a Gateway row switched on.
            vm.Company!.Gst = new GstConfig
            {
                Enabled = true,
                HomeStateCode = "19",
                RegistrationType = GstRegistrationType.Regular,
            };
            vm.Company!.Tds = new TdsConfig { Enabled = true };
            vm.Company!.Tcs = new TcsConfig { Enabled = true };
            vm.Company!.PayrollEnabled = true;
            vm.Company!.PayrollStatutoryEnabled = true;
            vm.ShowGateway();
            Pump(window);

            var row = MenuRow(window, "Quit — Change Company");
            Assert.True(row != null, "The Gateway root column did not realise its Quit row.");
            var scroller = OwningScroller(row!);
            Assert.True(scroller != null, "The Gateway root column has no vertical scroller.");

            var content = ScrolledContent(scroller!);
            var maxOffset = Math.Max(0, scroller!.Extent.Height - scroller.Viewport.Height);

            // Only meaningful where the column genuinely overflows its viewport (1280x720 for this column).
            if (content.DesiredSize.Height > scroller.Viewport.Height + 0.5)
                Assert.True(
                    maxOffset > 0,
                    $"The Gateway column needs {content.DesiredSize.Height:F0}px in a " +
                    $"{scroller.Viewport.Height:F0}px viewport at {width}x{height}, yet maxOffset is " +
                    $"{maxOffset:F0} — the overflow is unreachable by any gesture.");

            Assert.True(
                scroller.Extent.Height >= content.DesiredSize.Height - 0.5,
                $"The Gateway column's scroller understates its content by " +
                $"{content.DesiredSize.Height - scroller.Extent.Height:F0}px at {width}x{height}.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — REACHABILITY in a long picker. Scroll to maximum offset and require the LAST voucher type to land inside
    /// the pane clip. Before the fix it stayed 16px past the clip at all four heights even fully scrolled, because
    /// maxOffset itself understated the content — a voucher type the operator simply could not select.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Last_voucher_type_in_a_long_picker_is_reachable(int width, int height)
    {
        var (window, vm, tempDir) = Boot(width, height);
        try
        {
            for (var i = 1; i <= 30; i++)
                vm.Company!.AddVoucherType(new VoucherType(
                    Guid.NewGuid(), $"Bulk Type {i:00}", VoucherBaseType.Sales));
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.OpenAddVoucherFromReport();
            Pump(window);

            var clipBottom = ClipBottom(window);
            var row = MenuRow(window, "Bulk Type 30");
            Assert.True(row != null, "The Add-Voucher picker did not realise its last voucher type.");
            var scroller = OwningScroller(row!);
            Assert.True(scroller != null, "The Add-Voucher picker column has no vertical scroller.");

            scroller!.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            Pump(window);

            var bottom = row!.TranslatePoint(default, window)!.Value.Y + row.Bounds.Height;
            Assert.True(
                bottom <= clipBottom + 0.5,
                $"Fully scrolled, the last voucher type still ends at y={bottom:F0} below the pane clip at " +
                $"y={clipBottom:F0} ({width}x{height}) — it cannot be selected.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// C — the Gateway's own last row. "Quit — Change Company" is the final row of the root column, which stays on
    /// screen behind almost every other screen in the cascade; at 1280x720 it ended 11px below the clip.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Gateway_quit_row_is_reachable_at_every_window_height(int width, int height)
    {
        var (window, vm, tempDir) = Boot(width, height);
        try
        {
            // The tallest real root column: every feature that adds a Gateway row switched on.
            vm.Company!.Gst = new GstConfig
            {
                Enabled = true,
                HomeStateCode = "19",
                RegistrationType = GstRegistrationType.Regular,
            };
            vm.Company!.Tds = new TdsConfig { Enabled = true };
            vm.Company!.Tcs = new TcsConfig { Enabled = true };
            vm.Company!.PayrollEnabled = true;
            vm.Company!.PayrollStatutoryEnabled = true;
            vm.ShowGateway();
            Pump(window);

            var clipBottom = ClipBottom(window);
            var row = MenuRow(window, "Quit — Change Company");
            Assert.True(row != null, "The Gateway root column did not realise its Quit row.");
            var scroller = OwningScroller(row!);
            Assert.True(scroller != null, "The Gateway root column has no vertical scroller.");

            scroller!.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            Pump(window);

            var bottom = row!.TranslatePoint(default, window)!.Value.Y + row.Bounds.Height;
            Assert.True(
                bottom <= clipBottom + 0.5,
                $"Fully scrolled, \"Quit — Change Company\" still ends at y={bottom:F0} below the pane clip at " +
                $"y={clipBottom:F0} ({width}x{height}) — it is unreachable on every screen that keeps the " +
                "Gateway column on show.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// D — the Miller cascade invariant must survive the change: drilling in keeps the EARLIER columns on screen,
    /// and no column collapses. This is the regression guard for a fix that touches shared shell chrome.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void Cascade_keeps_prior_columns_and_none_collapse(int width, int height)
    {
        var (window, vm, tempDir) = Boot(width, height);
        try
        {
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.OpenAddVoucherFromReport();
            Pump(window);

            Assert.True(vm.Columns.Count >= 3,
                $"The cascade collapsed to {vm.Columns.Count} column(s); prior panes must persist.");

            var columns = Descendants(window).OfType<Border>()
                .Where(b => b.DataContext is GatewayColumn && b.Bounds.Width > 0)
                .ToList();
            Assert.True(columns.Count >= 3,
                $"Only {columns.Count} cascade column(s) rendered; earlier panes vanished.");

            foreach (var c in columns)
            {
                Assert.True(c.Bounds.Width >= 100,
                    $"A cascade column collapsed to {c.Bounds.Width:F0}px wide at {width}x{height}.");
                Assert.True(c.Bounds.Height > 0,
                    $"A cascade column collapsed to zero height at {width}x{height}.");
            }
        }
        finally { Cleanup(window, tempDir); }
    }
}
