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

            // 🔴 `Named<Polyline>` (the FIRST one in the tree) is deliberately not used here. Every tile realises
            // a LineMark element; only a ChartMark.Line tile gives it points, and on the Default dashboard the
            // first two tiles are bars. This test used to assert on the first LineMark and passed only because
            // EVERY tile drew a line as well as its bars — the composite-mark defect. It now asserts what it
            // always meant: somewhere on this dashboard a polyline is genuinely stroked.
            var strokedLines = AllNamed<Polyline>(window, "LineMark")
                               .Where(p => (p.Points?.Count ?? 0) >= 2).ToList();
            Assert.NotEmpty(strokedLines);
            Assert.All(strokedLines, p => Assert.All(p.Points,
                pt => Assert.False(double.IsNaN(pt.X) || double.IsNaN(pt.Y))));
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
            var text = notice!.Text ?? string.Empty;
            Assert.Contains("not configurable", text, StringComparison.OrdinalIgnoreCase);

            // 🔴 THE NOTICE MUST NOT TELL THE OPERATOR THE OPTION SET IS UNKNOWN. It used to say "no option set
            // has been sourced for it" — false: the reference product's Alt+C options are published
            // (help.tallysolutions.com/dashboard-in-tallyprime/ — name, value type, display type, graph type,
            // show percentages, scale factor, sorting method, position of tile). Reporting a KNOWN gap as an
            // unmeasured unknown is how a row gets graded Complete on a panel that configures nothing, so the
            // notice must name what is missing and must never again claim it was never sourced.
            Assert.DoesNotContain("sourced", text, StringComparison.OrdinalIgnoreCase);
            Assert.All(new[] { "graph type", "scale factor", "sorting", "position" },
                       o => Assert.Contains(o, text, StringComparison.OrdinalIgnoreCase));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Escape from the configuration column returns to the dashboard beneath it — and the panel must actually
    /// GO. Clearing <c>Dashboard.TileConfig</c> on the pop is what lets Alt+C be pressed a SECOND time:
    /// <c>OpenDashboardTileConfig</c> refuses while it is non-null, so a stale value would make the verb inert
    /// for the rest of that dashboard's life.
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

    // ============================================================ the four defects the first cut shipped green
    //
    // 🔴 EVERY TEST BELOW WAS ADDED BECAUSE THE SUITE ABOVE PASSED WHILE THE DEFECT WAS ON SCREEN. Each names
    // what the old assertion could not see. Read that line before weakening one.

    /// <summary>The nearest <see cref="GatewayColumn"/> a realised control sits inside — i.e. WHICH Miller column
    /// actually painted it. Walking up for this is the whole point: a window-wide <c>Named&lt;T&gt;</c> search
    /// finds a control no matter which column drew it, which is exactly how the overlay defect below passed.</summary>
    private static GatewayColumn? OwningColumn(Visual v)
    {
        for (Visual? cur = v; cur is not null; cur = cur.GetVisualParent())
            if (cur is Control { DataContext: GatewayColumn gc }) return gc;
        return null;
    }

    /// <summary>
    /// 🔴 <b>DEFECT 1 — Alt+C PAINTED ITS PANEL OVER THE CHART AND PUSHED A BLANK COLUMN.</b>
    ///
    /// <para>The page templates live inside the <c>GatewayColumn</c> DataTemplate, so each is evaluated ONCE PER
    /// COLUMN. The panel was bound to <c>Dashboard.TileConfig</c> — <c>(Page as DashboardViewModel).TileConfig</c>
    /// — which is non-null on the DASHBOARD column, so the panel drew itself on top of the chart the operator
    /// was reading while the column Alt+C actually pushed rendered nothing but its header strip. Every sibling
    /// page here binds its own <c>Page as X</c> projection; this one now binds <c>DashboardTileConfig</c>, the
    /// property the same diff had added and left with zero consumers.</para>
    ///
    /// <para><b>Why the old test could not see it:</b> it asserted <c>Named&lt;TextBlock&gt;(window,
    /// "TileConfigTitle")</c> is non-null — a WINDOW-WIDE search that is satisfied by the panel appearing
    /// anywhere at all, including on top of the wrong column. This one asserts the OWNING COLUMN.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_C_renders_the_config_panel_INSIDE_the_column_it_pushed()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);

            var title = Named<TextBlock>(window, "TileConfigTitle");
            Assert.NotNull(title);

            var owner = OwningColumn(title!);
            Assert.NotNull(owner);
            Assert.IsType<DashboardTileConfigViewModel>(owner!.Page);
            Assert.Same(vm.Columns[^1], owner);      // and it is the column Alt+C pushed, the rightmost one

            // 🔴 THE OVERLAY HALF, stated as the two facts that were both false. NO realised copy of the config
            // panel may sit in a dashboard column, and no realised copy of the tiles may sit in the config
            // column. Before the fix the panel was drawn in BOTH — over the chart, and (blank) in its own.
            Assert.All(AllNamed<TextBlock>(window, "TileConfigTitle"),
                       t => Assert.IsType<DashboardTileConfigViewModel>(OwningColumn(t)?.Page));
            Assert.All(AllNamed<ItemsControl>(window, "DashboardTiles"),
                       c => Assert.IsType<DashboardViewModel>(OwningColumn(c)?.Page));

            // The dashboard beneath survives (Miller columns persist) and is a DIFFERENT column.
            var tiles = Named<ItemsControl>(window, "DashboardTiles");
            Assert.NotNull(tiles);
            var tilesOwner = OwningColumn(tiles!);
            Assert.NotNull(tilesOwner);
            Assert.NotSame(owner, tilesOwner);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>DEFECT 2 — `ChartMark` WAS A DEAD KNOB: EVERY TILE DREW BARS *AND* A POLYLINE.</b>
    ///
    /// <para>The tile built <c>Bars</c>, <c>Vertices</c> and <c>LinePoints</c> unconditionally and the view bound
    /// all three, so a "Bar chart" tile carried a navy line through the tops of its bars and a "Line chart" tile
    /// was a full bar chart with a line over it. There was ONE composite mark, not the two the vendor ships, and
    /// the Alt+C panel's <c>MarkCaption</c> mis-described what the operator was looking at. Nothing guarded it:
    /// flipping a tile from Line to Bar left the whole file green.</para>
    ///
    /// <para>Asserted at BOTH levels — the view model's lists, and the marks actually realised on screen.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_Bar_tile_draws_no_line_and_a_Line_tile_draws_no_bars()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            var tiles = vm.Dashboard!.Tiles;
            Assert.Contains(tiles, t => t.Mark == ChartMark.Bar);
            Assert.Contains(tiles, t => t.Mark == ChartMark.Line);

            foreach (var t in tiles)
            {
                Assert.False(t.IsEmpty, "the fixture trades, so every Default tile has data");
                if (t.Mark == ChartMark.Bar)
                {
                    Assert.NotEmpty(t.Bars);
                    Assert.Empty(t.Vertices);
                    Assert.Empty(t.LinePoints);          // a bar tile strokes NO line
                }
                else
                {
                    Assert.NotEmpty(t.Vertices);
                    Assert.NotEmpty(t.LinePoints);
                    Assert.Empty(t.Bars);                // a line tile fills NO rectangle
                    Assert.Empty(t.PositiveBars);
                    Assert.Empty(t.NegativeBars);
                }
            }

            // On screen: the realised rectangle count is the BAR tiles' bars and nothing else, and exactly the
            // LINE tiles stroke a polyline (a Polyline with fewer than two points draws no pixels).
            var host = Named<ItemsControl>(window, "DashboardTiles");
            Assert.NotNull(host);

            var realisedBars = AllNamed<ItemsControl>(window, "PositiveBarMarks")
                               .Concat(AllNamed<ItemsControl>(window, "NegativeBarMarks"))
                               .SelectMany(h => Descendants(h).OfType<Rectangle>())
                               .Count();
            Assert.Equal(tiles.Where(t => t.Mark == ChartMark.Bar).Sum(t => t.Bars.Count), realisedBars);

            var strokedLines = AllNamed<Polyline>(window, "LineMark").Count(p => (p.Points?.Count ?? 0) >= 2);
            Assert.Equal(tiles.Count(t => t.Mark == ChartMark.Line), strokedLines);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>DEFECT 3 — THE ZERO GRIDLINE WAS CAPTIONED WITH AN EMPTY STRING ON EVERY CHART.</b>
    ///
    /// <para><c>IndianFormat.Amount</c> renders exactly zero as <c>string.Empty</c> — the report-GRID
    /// blank-at-zero convention, wrongly applied to an axis caption. The axis always includes zero and the first
    /// tick is always a multiple of the step, so the zero tick was ALWAYS emitted and ALWAYS blank. On the one
    /// Default-dashboard series that goes negative, the only unlabelled gridline was the one dividing profit
    /// from loss. The old <c>ChartGeometryTests</c> asserted one caption at 10,00,000 and never looked at zero.</para>
    /// </summary>
    [Fact]
    public void Every_axis_tick_is_captioned_including_zero_on_a_mixed_sign_series()
    {
        var mixed = new ChartSeries("Sales less purchases", new[]
        {
            new ChartPoint("Apr-25", -2000m), new ChartPoint("May-25", 4000m), new ChartPoint("Jun-25", 0m),
        });

        var ticks = ChartGeometry.BuildAxisTicks(mixed, DashboardTileViewModel.PlotHeight);
        Assert.NotEmpty(ticks);
        Assert.All(ticks, t => Assert.False(string.IsNullOrWhiteSpace(t.Caption),
                                            $"the gridline at {t.Value} is drawn with no caption"));

        var zero = Assert.Single(ticks, t => t.Value == 0m);   // the axis always spans zero
        Assert.Equal("0.00", zero.Caption);

        // The positive-only case too: it also always emits a zero tick, and it was blank as well.
        var positive = new ChartSeries("Sales", new[] { new ChartPoint("Apr-25", 5000m) });
        Assert.All(ChartGeometry.BuildAxisTicks(positive, DashboardTileViewModel.PlotHeight),
                   t => Assert.False(string.IsNullOrWhiteSpace(t.Caption)));
    }

    /// <summary>
    /// 🔴 <b>DEFECT 4 — THE BADGE AND THE KEY DISAGREED ABOUT WHAT Alt+C DOES.</b>
    ///
    /// <para>The key tunnel routes Alt+C on a dashboard to the tile configuration; <c>BuildButtonBar</c> had no
    /// Dashboard arm, so the bar advertised an ENABLED "Alt+C  Create Ledger". Pressing the chord opened the tile
    /// config, clicking the badge opened the Ledger master — two doors for one advertised chord doing different
    /// things. The old test checked the KEY (<c>Assert.Null(vm.LedgerMaster)</c>) and never read the badge.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_Alt_C_badge_on_a_dashboard_says_what_the_Alt_C_key_does()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            // Exactly ONE Alt+C row — the shell's key lookup takes the first match, so a second would shadow it.
            var badge = Assert.Single(vm.ButtonBar, b => b.Key == "Alt+C");
            Assert.Equal("Configure Tile", badge.Caption);
            Assert.True(badge.Enabled, "an enabled chord whose badge is dim is as bad as the reverse");

            // The BUTTON runs the same door the KEY runs.
            badge.Action();
            Pump(window);
            Assert.Equal(Screen.DashboardTileConfig, vm.CurrentScreen);
            Assert.Null(vm.LedgerMaster);

            // And off a dashboard the badge goes back to being the Ledger-creation master.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            vm.ShowGateway();
            Pump(window);
            var gateway = Assert.Single(vm.ButtonBar, b => b.Key == "Alt+C");
            Assert.NotEqual("Configure Tile", gateway.Caption);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>DEFECT 5 — Alt+C COULD ONLY EVER CONFIGURE TILE ZERO.</b>
    ///
    /// <para><c>SelectedTileIndex</c> existed and its doc comment said it was "kept as an index so the column is
    /// keyboard-navigable". Nothing moved it: <c>MoveTileUp</c>/<c>MoveTileDown</c> had zero callers in
    /// <c>src/</c>, no key arm touched it, and no control painted a selection — so the operator could not have
    /// seen which tile was targeted even if it had moved. The arrows now reach it through <c>StepActive</c>, the
    /// one arrow door every row-selecting page in this shell uses, and the tile's title row is highlighted.</para>
    /// </summary>
    [AvaloniaFact]
    public void Arrows_move_the_tile_highlight_and_Alt_C_configures_the_HIGHLIGHTED_tile()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            PostSomeTrading(vm);
            ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");

            var tiles = vm.Dashboard!.Tiles;
            Assert.True(tiles.Count >= 3, "the Default dashboard carries three tiles");
            Assert.Equal(0, vm.Dashboard.SelectedTileIndex);
            Assert.True(tiles[0].IsSelected);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(1, vm.Dashboard.SelectedTileIndex);
            Assert.Single(tiles, t => t.IsSelected);            // exactly one, and it moved
            Assert.True(tiles[1].IsSelected);

            // It is PAINTED — an invisible highlight is not a highlight. Exactly one tile header carries a
            // non-transparent selection brush.
            var highlighted = AllNamed<Grid>(window, "DashboardTileHeader")
                              .Count(g => g.Background is SolidColorBrush { Color.A: > 0 });
            Assert.Equal(1, highlighted);

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(0, vm.Dashboard.SelectedTileIndex);

            // …and Alt+C configures whichever tile is highlighted, not always tile zero.
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, vm.Dashboard.SelectedTileIndex);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.DashboardTileConfig, vm.CurrentScreen);
            Assert.Equal(tiles[2].Title, vm.Dashboard.TileConfig!.TileTitle);
            Assert.NotEqual(tiles[0].Title, vm.Dashboard.TileConfig!.TileTitle);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// A one-point LINE series must still stroke something. A single-vertex <c>Polyline</c> draws no pixels, and
    /// once <see cref="ChartMark"/> became a real branch a one-month line tile would have shown an axis and
    /// nothing else — "there is no data" for a month that has some, the same wrong-figures shape as drawing a
    /// zero line for an empty series. Unreachable on today's full-financial-year route; live the moment the
    /// dashboard honours a shorter period, which is an open R12 question.
    /// </summary>
    [Fact]
    public void A_single_point_line_series_still_strokes_a_visible_segment()
    {
        var one = new ChartSeries("S", new[] { new ChartPoint("Apr-25", 4000m) });
        var tile = new DashboardTileViewModel("One month", ChartMark.Line, one, "Amount");

        Assert.Single(tile.Vertices);                       // the geometry stays one-vertex-per-point
        Assert.Equal(2, tile.LinePoints.Count);             // the VIEW gets a strokeable segment
        Assert.Equal(tile.LinePoints[0].Y, tile.LinePoints[1].Y, 6);      // flat, at the point's own level
        Assert.Equal(tile.Vertices[0].Y, tile.LinePoints[0].Y, 6);
        Assert.True(tile.LinePoints[1].X > tile.LinePoints[0].X, "a zero-width segment strokes nothing either");
        Assert.All(tile.LinePoints, p => Assert.InRange(p.X, 0d, DashboardTileViewModel.PlotWidth));

        // An EMPTY series is still empty — the fix must not manufacture a segment out of nothing.
        var empty = new DashboardTileViewModel("None", ChartMark.Line, ChartSeries.Empty("S"), "Amount");
        Assert.Empty(empty.LinePoints);
    }

    /// <summary>
    /// 🔴 <b>WHICH ACCELERATOR EACH ROOT GATEWAY ROW PAINTS — pinned, because adding "Dashboard" MOVED TWO
    /// LETTERS THE OPERATOR HAD ALREADY LEARNED.</b>
    ///
    /// <para>Adding a row to the root column runs <c>GatewayColumn</c>'s rehousing pass over the WHOLE column,
    /// and the pass re-houses whatever incumbents it must to serve everybody. <c>GatewayHotKeyRehousingTests</c>
    /// proves the algorithm correct and its assignments unique — but nothing pinned WHICH letter each row ends
    /// up with, and the root column is the one users have memorised. Measured on the real window, 2026-09-07,
    /// the Dashboard row cost two: <b>Day Book moved D → a</b> (D went to Dashboard) and <b>Alter Company moved
    /// A → l</b> (A went to Day Book). That is a genuine, if small, cost of the feature, and it should be a
    /// visible decision rather than a silent side effect — so it is written down here.</para>
    ///
    /// <para>This test is a CHANGE DETECTOR, not a claim that this particular map is right. If a later slice
    /// adds a root row and this reddens, that is the test working: read which letters moved, decide whether the
    /// move is acceptable, and update the map deliberately.</para>
    ///
    /// <para>🔴 <b>2026-09-07 — it did exactly that, and it changed a slice's design.</b> Census row 16.5 added a
    /// root row under Data. Labelled <b>"Split"</b> it cost <b>Create its C</b> (the augmenting pass re-housed
    /// Create onto 'e' and gave C to Chart of Accounts) — the most-memorised accelerator on the Gateway, and it
    /// also turned <c>MenuHotKeyAndAcceptTests.The_first_free_letter_wins_and_a_collision_falls_through_to_the_next</c>
    /// red. The row was therefore labelled with the vendor's own name for the feature, <b>"Split Company
    /// Data"</b>, whose 'm' (in "Company") is free in pass 1 — so the new row claims a letter <b>nobody had</b>
    /// and <b>not one incumbent letter moved</b>. The one line added below is the whole diff. Keeping the
    /// shorter label would have been a silent usability regression that this test, and only this test, caught.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_root_Gateway_column_paints_exactly_these_accelerators()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            var actual = string.Join(" | ", vm.Columns[0].Items
                .Where(i => i.IsSelectable)
                .Select(i => $"{i.Label}={(i.HasHotKey ? i.HotKey.ToString() : "-")}"));

            const string expected =
                "Create=C | Alter Company=l | Chart of Accounts=h | GST & Taxation=G | Vouchers=V | Banking=n | "
                + "Day Book=a | Balance Sheet=B | Profit & Loss A/c=P | Trial Balance=T | Account Books=u | "
                + "Statements=S | Statements of Accounts=e | Inventory Reports=I | GST Reports=R | "
                + "Exception Reports=x | Dashboard=D | Backup / Restore=k | Split Company Data=m | "
                + "Quit — Change Company=Q";

            Assert.Equal(expected, actual);

            // No row is starved, and no letter is handed out twice — the two properties the rehousing pass
            // exists to guarantee, asserted here on the REAL column rather than a synthetic one.
            var keys = vm.Columns[0].Items.Where(i => i.IsSelectable && i.HasHotKey)
                                          .Select(i => char.ToUpperInvariant(i.HotKey!.Value)).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.DoesNotContain(vm.Columns[0].Items.Where(i => i.IsSelectable), i => !i.HasHotKey);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>A DASHBOARD MUST NOT SCROLL SIDEWAYS — the chart has to fit the width of its own column.</b>
    ///
    /// <para>The plot is a fixed 460×150 plus an 86px axis-caption gutter and 12px tile padding, and the Miller
    /// column it sits in is a FIXED width, so whether it fits does not depend on the window: it either always
    /// fits or never does. This project carries a large open UI-truncation catalogue and 125/150% DPI is
    /// untested, so a new rendering surface gets this pinned before it can drift.</para>
    ///
    /// <para><b>MEASURED, 2026-09-07, and reported honestly.</b> A review finding said the tile extent was 650
    /// against a 626 viewport at both standard viewports, i.e. a horizontal scrollbar on every fresh dashboard.
    /// <b>That does not reproduce on this tree.</b> Measured through the realised ScrollViewer in all four
    /// states (1440×900 and 1280×720, with and without the Alt+C column open) the extent width is
    /// <c>626</c> against a <c>626</c> viewport every time — the content fits, with 546px of chart inside
    /// ~600px of usable tile. The finding is recorded as not-reproduced rather than silently dropped, and this
    /// test is what makes that claim checkable and keeps it true.</para>
    ///
    /// <para><b>The VERTICAL scroll is real and is deliberate, so it is not asserted against.</b> At 1280×720 the
    /// extent is 675 against a 531 viewport: three stacked charts do not fit 531px, and they should not be
    /// squeezed to ~100px each to pretend otherwise — a vertically scrolling list of tiles is what the
    /// ScrollViewer is for, and a fourth tile would overflow any fixed height anyway. What is NOT claimed
    /// anywhere is that this chart is RESPONSIVE. It is not: the plot is a fixed size and does not follow the
    /// available width. Making it follow is real work and stays open.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_dashboard_fits_its_column_horizontally_at_the_standard_viewports()
    {
        foreach (var (w, h) in new[] { (1440d, 900d), (1280d, 720d) })
        foreach (var withConfig in new[] { false, true })
        {
            var (window, vm, dir) = NewCompany();
            try
            {
                window.Width = w;
                window.Height = h;
                PostSomeTrading(vm);
                ReachDashboardThroughTheCascade(window, vm, "Default Dashboard");
                if (withConfig)
                {
                    window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
                    Pump(window);
                }

                var tiles = Named<ItemsControl>(window, "DashboardTiles");
                Assert.NotNull(tiles);
                var scroller = tiles!.FindAncestorOfType<ScrollViewer>();
                Assert.NotNull(scroller);

                // Not vacuous: the viewport must be a real measured width, not the 0 an unlaid-out tree reports.
                Assert.True(scroller!.Viewport.Width > 100,
                            $"the ScrollViewer was never laid out ({scroller.Viewport}) — this test would "
                            + "otherwise pass on nothing at all");

                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 0.5,
                            $"at {w}x{h} (config column open: {withConfig}) the dashboard overflows its column "
                            + $"horizontally: extent {scroller.Extent.Width} > viewport {scroller.Viewport.Width}");
            }
            finally { Close(window, dir); }
        }
    }
}
