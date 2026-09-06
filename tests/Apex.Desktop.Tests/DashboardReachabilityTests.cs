using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Path = System.IO.Path;
using Polyline = Avalonia.Controls.Shapes.Polyline;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 14.3 — THE GRAPHICAL DASHBOARD — AND THE CLAIM IT OVERTURNS.</b>
///
/// <para><b>The state these tests were written against.</b> <c>plan.md</c> lists a "graphical dashboard" twice
/// as delivered scope (`:191` under *Modern baseline enrichments*, `:509` in a phase Modules bullet) while its
/// own work list at `:6205` carries <c>W2-35 Graphical dashboard / charts — the main view has zero vector
/// elements</c> as an <b>open, unstarted</b> item sized <b>L</b>. Measured against the tree the second is
/// right: a grep for <c>&lt;Polyline</c>, <c>&lt;Path</c>, <c>&lt;PathGeometry</c>, <c>&lt;Canvas</c>,
/// <c>StreamGeometry</c>, <c>DrawingContext</c> and <c>RenderTargetBitmap</c> across every <c>.cs</c> and
/// <c>.axaml</c> in <c>src/</c> returned <b>not one file</b>. There was no dashboard, no chart and no chart
/// primitive of any kind. Every test in this file is therefore a compile error or a red against that state.</para>
///
/// <para><b>Why these walk the realised tree.</b> A test asserting <c>vm.Dashboard is not null</c> passes
/// against a build with no <c>DataTemplate</c>, which would show the operator a blank column — the failure
/// mode this project has filed three times over. <see cref="Reaching_a_dashboard_realises_actual_chart_marks"/>
/// counts the <c>Rectangle</c>s and finds the <c>Polyline</c>; nothing less proves a chart exists.</para>
///
/// <para><b>The wrong-figures lock.</b>
/// <see cref="A_company_with_no_vouchers_renders_the_worded_empty_state_and_NO_marks"/> is the load-bearing
/// one. A company that has posted nothing must not be shown an axis with a flat line along it: "you traded
/// nothing" and "there is nothing here" are different facts, and the register engine underneath emits a ZERO
/// ROW PER MONTH regardless, so this is a live trap and not a hypothetical one.</para>
/// </summary>
public sealed class DashboardReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewCompany()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexDash_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        vm.NewCompanyName = "Dashboard Co";
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    private static IEnumerable<T> AllNamed<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().Where(x => x.Name == name);

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts one sales and one purchase voucher so the dashboards have something real to plot.
    /// Posted through <see cref="LedgerService"/>, the same door the shell uses, so the figures the dashboard
    /// reads are the figures a register would foot to.</summary>
    private static void PostSomeTrading(MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var date = c.FinancialYearStart.AddDays(10);
        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var cash = c.Ledgers.First(l => l.Name.Equals("Cash", StringComparison.OrdinalIgnoreCase));

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        post.Post(new Voucher(Guid.NewGuid(), salesType, date, new List<EntryLine>
        {
            new(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));
        post.Post(new Voucher(Guid.NewGuid(), purchaseType, date, new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(3000m), DrCr.Debit),
            new(cash.Id, Money.FromRupees(3000m), DrCr.Credit),
        }));
    }

    /// <summary>Opens Reports → Dashboard → the named dashboard through the real cascade, not by calling
    /// <c>OpenDashboard</c> — a menu item nothing dispatches is the dead-capability shape.</summary>
    private static void ReachDashboardThroughTheCascade(MainWindow window, MainWindowViewModel vm, string item)
    {
        vm.ShowDashboardMenu();
        Pump(window);
        SelectAndActivate(window, vm, item);
    }

    /// <summary>Selects the named row in the rightmost column and activates it — the two gestures Down-arrow
    /// and Enter perform. The point is that the row must be DISPATCHED to something: a menu row that opens
    /// nothing is the dead-capability shape this whole file is guarding against.</summary>
    private static void SelectAndActivate(MainWindow window, MainWindowViewModel vm, string label)
    {
        var col = vm.Columns[^1];
        var index = -1;
        for (var i = 0; i < col.Items.Count; i++)
            if (col.Items[i].IsSelectable && col.Items[i].Label == label) { index = i; break; }
        Assert.True(index >= 0, $"no selectable row labelled '{label}' in the rightmost column");

        col.SetSelected(index);
        vm.ActivateSelected();
        Pump(window);
    }

    // ------------------------------------------------------------------ the route in

    /// <summary>
    /// 🔴 The Gateway's Reports section must OFFER a Dashboard, nested under Reports rather than dumped flat.
    /// Fails against the pre-slice tree: no such menu item existed anywhere.
    /// </summary>
    [AvaloniaFact]
    public void The_Reports_section_of_the_Gateway_offers_a_Dashboard()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            var root = vm.Columns[0];
            var labels = root.Items.Select(i => i.Label).ToList();
            Assert.Contains("Dashboard", labels);

            // Nested under Reports: it must fall between the "Reports" header and the next header.
            var reportsHeader = labels.IndexOf("Reports");
            var dashboard = labels.IndexOf("Dashboard");
            Assert.True(reportsHeader >= 0 && dashboard > reportsHeader,
                        "the Dashboard row must sit inside the Reports section, not in a flat dump");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The submenu carries exactly the three dashboard types the reference product ships.</summary>
    [AvaloniaFact]
    public void The_Dashboard_submenu_offers_Default_Sales_and_Purchase()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            vm.ShowDashboardMenu();
            Pump(window);

            var items = vm.Columns[^1].Items.Select(i => i.Label).ToList();
            Assert.Contains("Default Dashboard", items);
            Assert.Contains("Sales Dashboard", items);
            Assert.Contains("Purchase Dashboard", items);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Selecting a dashboard through the real cascade opens it as its own column beside the submenu.</summary>
    [AvaloniaFact]
    public void Selecting_Default_Dashboard_opens_it_as_a_new_cascading_column()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            Assert.Equal(Screen.Dashboard, vm.CurrentScreen);
            Assert.NotNull(vm.Dashboard);
            Assert.Equal(DashboardKind.Default, vm.Dashboard!.Kind);
            Assert.Equal("Default Dashboard", vm.Columns[^1].Title);
            Assert.True(vm.Columns.Count >= 3, "the Gateway and Dashboard submenu columns must persist");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The Sales dashboard reads the SALES register, not the ledger and not the purchase register.</summary>
    [AvaloniaFact]
    public void The_Sales_dashboard_reads_the_sales_register_and_the_Purchase_one_reads_purchases()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);

            ReachDashboardThroughTheCascade(window, vm, "Sales Dashboard");
            Assert.Equal(DashboardKind.Sales, vm.Dashboard!.Kind);
            Assert.All(vm.Dashboard.Tiles, t =>
                Assert.Contains("Sales", t.Title, StringComparison.OrdinalIgnoreCase));
            // 5000 was posted to Sales; the value tile must foot to it and NOT to the purchase figure.
            var salesValues = vm.Dashboard.Tiles[0].Series.Points.Sum(p => p.Value);
            Assert.Equal(5000m, salesValues);

            vm.Back();
            Pump(window);
            SelectAndActivate(window, vm, "Purchase Dashboard");

            Assert.Equal(DashboardKind.Purchase, vm.Dashboard!.Kind);
            Assert.Equal(3000m, vm.Dashboard.Tiles[0].Series.Points.Sum(p => p.Value));
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ the marks are real

    /// <summary>
    /// 🔴 <b>The chart must actually be drawn.</b> Against the pre-slice tree there were ZERO vector elements
    /// anywhere in <c>src/</c>; this asserts real <c>Rectangle</c> bar marks and a real <c>Polyline</c> exist in
    /// the realised tree with non-degenerate geometry.
    /// </summary>
    [AvaloniaFact]
    public void Reaching_a_dashboard_realises_actual_chart_marks()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            var barHosts = AllNamed<ItemsControl>(window, "PositiveBarMarks").ToList();
            Assert.NotEmpty(barHosts);

            var bars = barHosts.SelectMany(h => Descendants(h).OfType<Rectangle>()).ToList();
            Assert.NotEmpty(bars);
            Assert.Contains(bars, r => r.Height > 0 && r.Width > 0);

            var line = Named<Polyline>(window, "LineMark");
            Assert.NotNull(line);
            Assert.NotEmpty(line!.Points);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE WRONG-FIGURES LOCK.</b> A company that has posted nothing must show the worded empty state and
    /// draw NO marks. The register engine underneath emits a zero row for every month regardless of whether a
    /// voucher exists, so without the empty/zero distinction this would render a flat line along the axis —
    /// showing "there is nothing here" as "everything was zero".
    /// </summary>
    [AvaloniaFact]
    public void A_company_with_no_vouchers_renders_the_worded_empty_state_and_NO_marks()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            Assert.Empty(vm.Company!.Vouchers);      // nothing posted at all
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            Assert.All(vm.Dashboard!.Tiles, t =>
            {
                Assert.True(t.IsEmpty, "no vouchers means nothing to chart — NOT a series of zeros");
                Assert.Empty(t.Bars);
                Assert.Empty(t.Vertices);
                Assert.Empty(t.Ticks);
                Assert.Empty(t.LinePoints);
            });

            // On screen: the worded message is visible and no bar mark is realised anywhere.
            var message = AllNamed<TextBlock>(window, "TileEmptyMessage").FirstOrDefault(t => t.IsVisible);
            Assert.NotNull(message);
            Assert.Contains("No ", message!.Text ?? string.Empty, StringComparison.Ordinal);

            var bars = AllNamed<ItemsControl>(window, "PositiveBarMarks")
                       .SelectMany(h => Descendants(h).OfType<Rectangle>());
            Assert.Empty(bars);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The counterpart: a company that DID trade, in a month where the figure is genuinely zero, still draws its
    /// axis and its marks. Zero is a measured figure and must be shown as one.
    /// </summary>
    [AvaloniaFact]
    public void A_month_with_no_trading_inside_a_year_that_did_trade_still_plots()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);                     // one month only, in a twelve-month window
            ReachDashboardThroughTheCascade(window, vm, "Sales Dashboard");

            var tile = vm.Dashboard!.Tiles[0];
            Assert.False(tile.IsEmpty);
            Assert.True(tile.Series.Points.Count > 1, "the axis must run over the whole period");
            Assert.Contains(tile.Series.Points, p => p.Value == 0m);   // the quiet months are real zeros
            Assert.NotEmpty(tile.Bars);
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ colour discipline

    /// <summary>
    /// 🔴 Every chart brush must resolve through a NAMED resource. The application pins
    /// <c>RequestedThemeVariant="Light"</c> and has one palette today, so "adapt to a dark theme" is not a
    /// testable rule — but "no chart mark carries a colour that is not one of the palette's own" is, and it is
    /// what makes a future variant a free move rather than a sweep.
    /// </summary>
    [AvaloniaFact]
    public void No_chart_mark_carries_a_colour_outside_the_application_palette()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            var palette = new[] { "BrandNavy", "BrandNavyDark", "Panel", "PanelBorder", "Cream", "Amber",
                                  "AmberBorder", "Ink", "GridLine", "AlertRed", "PositiveGreen", "AppWhite" }
                .Select(k => window.TryFindResource(k, out var v) ? v as ISolidColorBrush : null)
                .Where(b => b is not null)
                .Select(b => b!.Color)
                .ToHashSet();
            Assert.NotEmpty(palette);

            var marks = AllNamed<ItemsControl>(window, "PositiveBarMarks")
                        .Concat(AllNamed<ItemsControl>(window, "NegativeBarMarks"))
                        .SelectMany(h => Descendants(h).OfType<Rectangle>())
                        .Select(r => r.Fill)
                        .Concat(AllNamed<Polyline>(window, "LineMark").Select(p => p.Stroke))
                        .OfType<ISolidColorBrush>()
                        .ToList();

            Assert.NotEmpty(marks);
            Assert.All(marks, b => Assert.Contains(b.Color, palette));
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ Alt+C

    /// <summary>
    /// Alt+C over an open dashboard opens the tile-configuration column — through the REAL key tunnel, because
    /// Alt+C already has two other claimants (the comparative-column arm and the unscoped ledger-creation arm)
    /// and only the tunnel proves this one wins on a dashboard.
    /// </summary>
    [AvaloniaFact]
    public void AltC_on_an_open_dashboard_opens_the_tile_configuration_column()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");
            var columns = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.DashboardTileConfig, vm.CurrentScreen);
            Assert.NotNull(vm.Dashboard!.TileConfig);
            Assert.Equal(columns + 1, vm.Columns.Count);
            // 🔴 It must NOT have opened the Ledger-creation master, which is what the unscoped Alt+C arm
            // below it does on every other screen.
            Assert.Null(vm.LedgerMaster);

            Assert.NotNull(Named<TextBlock>(window, "TileConfigTitle"));
            var notice = Named<TextBlock>(window, "TileConfigNotice");
            Assert.NotNull(notice);
            Assert.Contains("not configurable", notice!.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Escape from the configuration column returns to the dashboard beneath it — and the panel must actually
    /// GO. It is bound through <c>Dashboard.TileConfig</c>, so leaving that non-null after the pop would keep it
    /// rendered over a screen the operator has already left.
    /// </summary>
    [AvaloniaFact]
    public void Escape_from_the_tile_config_returns_to_the_dashboard_and_the_panel_goes_away()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.DashboardTileConfig, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Dashboard, vm.CurrentScreen);
            Assert.NotNull(vm.Dashboard);
            Assert.Null(vm.Dashboard!.TileConfig);
            Assert.Null(Named<TextBlock>(window, "TileConfigTitle"));
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ what is deliberately absent

    /// <summary>
    /// 🔴 <b>No Print or Export control may appear on a dashboard.</b> <c>PdfWriter</c> has <c>Text</c>,
    /// <c>Line</c> and <c>Image</c> and <b>no filled-rectangle primitive</b>, so a bar chart cannot be written
    /// to PDF without either a stroke hack or a new rasteriser — neither of which this slice built. A
    /// present-but-inert Print button would be exactly the dead-capability defect the row was scoped to avoid,
    /// so the honest state is ABSENT, and this test holds it absent.
    /// </summary>
    [AvaloniaFact]
    public void The_dashboard_offers_no_print_or_export_control_because_neither_is_built()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            var tiles = Named<ItemsControl>(window, "DashboardTiles");
            Assert.NotNull(tiles);

            var verbs = Descendants(tiles!).OfType<Button>()
                                           .Select(b => b.Content?.ToString() ?? string.Empty)
                                           .ToList();
            Assert.All(verbs, v =>
            {
                Assert.False(v.Contains("Print", StringComparison.OrdinalIgnoreCase), $"inert control: {v}");
                Assert.False(v.Contains("Export", StringComparison.OrdinalIgnoreCase), $"inert control: {v}");
            });
        }
        finally { Close(window, dir); }
    }

    /// <summary>The month captions must be culture-stable. <c>MMM</c> is culture-dependent, and this axis has to
    /// read the same on a ubuntu or macos runner as on the operator's desktop.</summary>
    [AvaloniaFact]
    public void Month_captions_are_culture_stable()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Sales Dashboard");

            var labels = vm.Dashboard!.Tiles[0].Labels;
            Assert.NotEmpty(labels);
            Assert.All(labels, l =>
            {
                Assert.Matches("^[A-Z][a-z]{2}-[0-9]{2}$", l);   // "Apr-25"
            });
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 The line series is handed to the view as TYPED points, never as an "x,y x,y" string.
    ///
    /// <para>The string form was the first cut and it failed twice over: <c>Polyline.Points</c> is a collection,
    /// so binding a string left it NULL and every dashboard threw out of <c>PolylineGeometry..ctor</c> the moment
    /// it rendered — and the string form is culture-sensitive, so on a de-DE or fr-FR runner "1.5" becomes "1,5"
    /// and "1,5 2,5" parses as four coordinates instead of two. Typed points remove both. This test pins the
    /// shape so a later slice does not reintroduce the string.</para>
    /// </summary>
    [Fact]
    public void The_line_series_is_handed_over_as_typed_points_not_a_culture_sensitive_string()
    {
        var series = new ChartSeries("S", new[]
        {
            new ChartPoint("A", 1m), new ChartPoint("B", 3m), new ChartPoint("C", 2m),
        });
        var tile = new DashboardTileViewModel("Test", ChartMark.Line, series, "Amount");

        Assert.Equal(3, tile.LinePoints.Count);
        Assert.All(tile.LinePoints, p =>
        {
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y));
            Assert.InRange(p.Y, 0d, DashboardTileViewModel.PlotHeight);
            Assert.InRange(p.X, 0d, DashboardTileViewModel.PlotWidth);
        });

        // Empty means an EMPTY collection, never null — a null would throw in the renderer.
        var empty = new DashboardTileViewModel("Test", ChartMark.Line, ChartSeries.Empty("S"), "Amount");
        Assert.NotNull(empty.LinePoints);
        Assert.Empty(empty.LinePoints);
    }
}
