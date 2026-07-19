using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 A SALES INVOICE MUST BE RAISEABLE ON A LAPTOP — the Stock Item column may never collapse.
///
/// <para><b>The defect this locks (measured, not inferred).</b> The item-invoice lines grid used the inline
/// shape <c>ColumnDefinitions="*,184,120,Auto,72,92,36,Auto,Auto,Auto"</c>, where the leading <c>*</c> is the
/// Stock Item picker. With a Sales item-invoice on a company that has the Actual/Billed split and price levels
/// enabled — the populated fixture's configuration, and an ordinary one — the FIXED columns sum to 648px
/// (184 Godown + 120 Qty + 84 Qty-Billed + 72 Rate + 92 Batch + 36 chevron + 60 Disc%). The Miller cascade
/// floors a page column at 640px, leaving ~630px of content, so the star had nothing left and was starved to
/// EXACTLY 0px: the Stock Item picker VANISHED and the core document of the product could not be created.</para>
///
/// <para><b>Measured before the fix</b> (populated fixture, Sales, item-invoice mode): star column = <b>0px</b>
/// at 1280x720, 1366x768 AND 1600x900; only 1920x1080 gave it 317px. The audit brief called it at 1366 and
/// below — it was in fact already gone at 1600x900. After the fix: 242px at the three narrow sizes and an
/// unchanged 317px at 1920x1080.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> Column presence is a layout fact, so this measures
/// <see cref="ColumnDefinition.ActualWidth"/> directly. It deliberately does NOT measure text advance widths:
/// the committed headless harness resolves no real font and substitutes a stub whose glyphs are ~82% wider
/// than the shipped Consolas, which makes any width-of-text assertion a fiction. Column arithmetic is
/// font-independent and therefore identical headless and shipped. No Skia, no
/// <c>CaptureRenderedFrame</c>, no <c>TextLayout</c>/<c>TextRuns</c>.</para>
/// </summary>
public sealed class ItemInvoiceStockItemColumnTests
{
    /// <summary>
    /// The narrowest usable Stock Item picker. Below this a real fixture item name
    /// ("M.S. Hex Head Bolt …", 62 chars) has no room to disambiguate itself from its neighbours, and at 0 the
    /// control is simply absent. Matches the <c>MinWidth</c> declared on the column in MainWindow.axaml.
    /// </summary>
    private const double StockItemFloor = 220d;

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Opens a Sales voucher in ITEM-INVOICE mode on the populated fixture company — which has
    /// <c>UseSeparateActualBilledQuantity</c> and <c>EnableMultiplePriceLevels</c> on, the exact combination
    /// that pushes the fixed columns to 648px. A thin seed with those flags off would not reproduce this.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenSalesItemInvoice(
        int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexItemCol_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();

        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        vm.ShowVouchersMenu();
        vm.OpenVoucher(VoucherBaseType.Sales);

        var entry = vm.VoucherEntry!;
        if (!entry.IsItemInvoice) entry.ToggleItemInvoice();
        Pump(window);

        Assert.True(entry.IsItemInvoice, "Sales did not enter item-invoice mode.");
        Assert.True(
            entry.ShowActualBilledColumns && entry.ShowPriceLevelSelector,
            "The fixture no longer enables the Actual/Billed split + price levels, so this test would no " +
            "longer stress the widest column configuration it exists to guard.");
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

    /// <summary>Every visible grid that carries the item-lines column shape (the header band and each row).</summary>
    private static List<Grid> LineGrids(MainWindow window) =>
        Descendants(window).OfType<Grid>()
            .Where(g => g.IsEffectivelyVisible
                     && g.ColumnDefinitions.Count == 10
                     && g.ColumnDefinitions[0].Width.IsStar)
            .ToList();

    // ---------------------------------------------------------------- A: the column exists

    /// <summary>
    /// A — the defect itself. The Stock Item column must keep a usable width at every window size. At 0px the
    /// picker is not merely cramped, it is absent, and a sales invoice cannot be raised at all.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Stock_item_column_keeps_a_usable_width_at_every_window_size(int width, int height)
    {
        var (window, _, tempDir) = OpenSalesItemInvoice(width, height);
        try
        {
            var grids = LineGrids(window);
            Assert.True(grids.Count > 0, $"No item-lines grid rendered at {width}x{height}.");

            foreach (var grid in grids)
                Assert.True(
                    grid.ColumnDefinitions[0].ActualWidth >= StockItemFloor - 0.5,
                    $"The Stock Item column is {grid.ColumnDefinitions[0].ActualWidth:F0}px at " +
                    $"{width}x{height} (floor {StockItemFloor:F0}). At 0px the picker is GONE and no sales " +
                    "invoice can be created; the fixed columns alone need 648px of the ~630px this pane has.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the guarantee, pinned at the declaration. The floor must live on the column itself, not on an
    /// ancestor's width: the inline <c>ColumnDefinitions</c> string cannot express <c>MinWidth</c>, so a future
    /// edit that "simplifies" the long form back to the string silently removes the floor and the column
    /// starves again the moment another gated column is added.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void Stock_item_column_declares_its_own_minimum_width(int width, int height)
    {
        var (window, _, tempDir) = OpenSalesItemInvoice(width, height);
        try
        {
            foreach (var grid in LineGrids(window))
                Assert.True(
                    grid.ColumnDefinitions[0].MinWidth >= StockItemFloor - 0.5,
                    $"The Stock Item column declares MinWidth {grid.ColumnDefinitions[0].MinWidth:F0} at " +
                    $"{width}x{height}. Without a floor ON THE COLUMN, a star column starves to zero as soon " +
                    "as the fixed columns exceed the pane.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// C — reachability of the overflow, and alignment. The header band and the rows share ONE horizontal
    /// scroller so they can never drift apart, and that scroller must actually be able to reach the columns
    /// pushed past the pane edge (Disc %, and Landed Rate/Value on a Purchase).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Item_lines_header_and_rows_stay_column_aligned_and_overflow_is_reachable(int width, int height)
    {
        var (window, _, tempDir) = OpenSalesItemInvoice(width, height);
        try
        {
            var grids = LineGrids(window);
            var widths = grids.Select(g => g.ColumnDefinitions[0].ActualWidth).ToList();
            var drift = widths.Max() - widths.Min();

            // Tolerance = one vertical scrollbar gutter. The header band is pinned OUTSIDE the rows'
            // vertical scroller, so it never sees the ~10px that scroller reserves for its bar; that
            // residual is inherent to the pinned-header shape used throughout this file and is not a
            // defect this test can close. What it DOES catch is structural drift: the row Border's
            // Padding="6,4" was not mirrored on the header, which put every column boundary in the band
            // 22px right of the cells beneath it. Anything beyond a gutter means header and rows are no
            // longer being laid out to the same width.
            Assert.True(
                drift <= 12.5,
                $"The item-lines header and rows disagree on the Stock Item column width by {drift:F0}px at " +
                $"{width}x{height} ({string.Join(" / ", widths.Select(x => x.ToString("F0")))}) — more than a " +
                "scrollbar gutter, so the column boundaries in the header band no longer line up with the " +
                "cells beneath them.");

            // The wrapper that owns horizontal scrolling for both. Walk UP from a line grid and take the
            // NEAREST horizontal scroller — searching the whole window instead would match the Miller shell's
            // own CascadeScroller (also Auto/Disabled), which contains these grids but scrolls the entire
            // cascade, not the invoice columns. That mistake made an earlier version of this test pass with
            // the wrapper's own horizontal scrolling switched off.
            ScrollViewer? wrapper = null;
            for (Visual? p = grids[0]; p != null; p = p.GetVisualParent())
                if (p is ScrollViewer sv && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                {
                    wrapper = sv;
                    break;
                }

            Assert.True(
                wrapper != null && wrapper.Name != "CascadeScroller",
                $"The item-lines grids have no horizontal scroller of their own at {width}x{height}: the " +
                "fixed columns alone exceed the pane, so whatever does not fit is clipped away with no way " +
                "to reach it.");

            var overflow = wrapper!.Extent.Width - wrapper.Viewport.Width;
            if (overflow <= 0.5) return; // everything fits — nothing to reach

            wrapper.Offset = new Vector(overflow, 0);
            Pump(window);
            Assert.True(
                wrapper.Offset.X >= overflow - 0.5,
                $"{overflow:F0}px of item-line columns overflow the pane at {width}x{height} but could not " +
                $"be scrolled to (offset stuck at {wrapper.Offset.X:F0}).");
        }
        finally { Cleanup(window, tempDir); }
    }
}
