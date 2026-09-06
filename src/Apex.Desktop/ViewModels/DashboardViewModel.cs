using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The three dashboards the reference product ships. Pie charts are a documented deliberate
/// non-feature there, and are therefore absent here too — see <see cref="DashboardTileViewModel"/>.</summary>
public enum DashboardKind
{
    /// <summary>Trading at a glance: sales, purchases, and the month-by-month difference between them.</summary>
    Default,

    /// <summary>Sales only: value by month, and how many sales vouchers produced it.</summary>
    Sales,

    /// <summary>Purchases only: value by month, and how many purchase vouchers produced it.</summary>
    Purchase,
}

/// <summary>
/// The two mark types this application draws. There is deliberately no third.
///
/// <para>🔴 <b>This is a real branch, not a label.</b> <see cref="DashboardTileViewModel"/>'s constructor builds
/// EITHER bars OR line vertices from it and leaves the other list empty, so a tile renders exactly one of the
/// two. It once built both regardless and the enum was dead; if a change ever makes both lists non-empty on one
/// tile, <c>A_Bar_tile_draws_no_line_and_a_Line_tile_draws_no_bars</c> reddens.</para>
/// </summary>
public enum ChartMark
{
    Bar,
    Line,
}

/// <summary>
/// One dashboard tile: a titled chart over one monthly series.
///
/// <para>🔴 <b>The tile's whole job is to be honest about having no data.</b> <see cref="IsEmpty"/> is read
/// straight off <see cref="ChartSeries.IsEmpty"/>, and the view binds the marks and the worded
/// <see cref="EmptyMessage"/> to mutually exclusive visibilities. A tile with no data must NOT draw an axis
/// with a flat line along it: "the company posted nothing" and "the company posted zero" are different
/// facts, and showing the first as the second is a wrong-figures defect.</para>
/// </summary>
public sealed partial class DashboardTileViewModel : ViewModelBase
{
    /// <summary>The plot area every tile is laid out against. Fixed so the geometry is deterministic and a
    /// test can pin exact pixels; the view gives the Canvas exactly these dimensions.</summary>
    public const double PlotWidth = 460;
    public const double PlotHeight = 150;

    public string Title { get; }
    public ChartMark Mark { get; }
    public ChartSeries Series { get; }

    /// <summary>The unit the tile's values are in, shown beside the title so a count is never read as money.</summary>
    public string ValueCaption { get; }

    /// <summary>
    /// True for the ONE tile Alt+C would configure. Maintained by <see cref="DashboardViewModel"/> whenever
    /// <c>SelectedTileIndex</c> moves, and bound to the tile's title-row highlight through the same
    /// <c>SelectedToBrushConverter</c> every menu row in the shell uses — so the operator can SEE which tile
    /// the verb is aimed at. It could not before: the index existed, nothing moved it and nothing painted it.
    /// </summary>
    [ObservableProperty] private bool _isSelected;

    public DashboardTileViewModel(string title, ChartMark mark, ChartSeries series, string valueCaption)
    {
        Title = title;
        Mark = mark;
        Series = series;
        ValueCaption = valueCaption;

        // 🔴 THE MARK TYPE BRANCHES HERE, AND IT MUST KEEP BRANCHING. A BAR TILE BUILDS BARS AND NO VERTICES;
        // A LINE TILE BUILDS VERTICES AND NO BARS. Both lists were previously built UNCONDITIONALLY and the view
        // bound both, so every tile rendered twelve rectangles AND a polyline through their tops: there was ONE
        // composite mark, not two, `Mark` was a dead knob (flipping a tile Line→Bar changed nothing on screen and
        // reddened no test), and `MarkCaption` told the operator "Bar chart" about a picture that was visibly
        // also a line. The vendor surface being cloned is LINE AND BAR — two marks — so a tile that is both is
        // not a faithful clone of either. The view needs no visibility branch: an empty mark list realises no
        // control at all, which is stronger than a collapsed one and is what the render tests assert on.
        Bars = mark == ChartMark.Bar
            ? new ReadOnlyCollection<BarMark>(
                ChartGeometry.BuildBars(series, PlotWidth, PlotHeight).ToList())
            : Array.Empty<BarMark>();
        Vertices = mark == ChartMark.Line
            ? new ReadOnlyCollection<LineVertex>(
                ChartGeometry.BuildLine(series, PlotWidth, PlotHeight).ToList())
            : Array.Empty<LineVertex>();
        Ticks = new ReadOnlyCollection<AxisTick>(
            ChartGeometry.BuildAxisTicks(series, PlotHeight).ToList());
        BaselineY = ChartGeometry.BaselineY(series, PlotHeight);
        LinePoints = BuildLinePoints(Vertices, series.Points.Count);
    }

    /// <summary>
    /// The polyline's points, with the ONE degenerate case handled: a series of exactly one point.
    ///
    /// <para>🔴 <b>A one-vertex Polyline strokes no pixels.</b> That was invisible while every tile also drew
    /// bars — the bar carried the figure — but once <see cref="ChartMark"/> became a real branch a single-point
    /// LINE tile would have rendered an axis, gridlines and nothing else: "there is no data" shown for a month
    /// that has one, which is the same wrong-figures shape as drawing a zero line for an empty series. It is
    /// unreachable on today's route (<c>OpenDashboard</c> always spans a full financial year, so every series
    /// has twelve points) and becomes reachable the moment the dashboard honours a one-month period.</para>
    ///
    /// <para>The single point is drawn as a short flat segment centred on its own slot, at its own value's Y —
    /// the level a reader would read off the axis, occupying the same width a single BAR would (the 0.62 slot
    /// fill <see cref="ChartGeometry.BuildBars"/> uses), so the two mark types stay visually comparable.
    /// <b>This is ours, not measured from the vendor</b>; the alternative was a third mark type (a point
    /// marker), and this feature's whole scope claim is that there are two.</para>
    /// </summary>
    private static Avalonia.Points BuildLinePoints(IReadOnlyList<LineVertex> vertices, int pointCount)
    {
        if (vertices.Count == 1 && pointCount > 0)
        {
            var v = vertices[0];
            var halfWidth = (PlotWidth / pointCount) * 0.62 / 2.0;
            return new Avalonia.Points(new[]
            {
                new Avalonia.Point(v.X - halfWidth, v.Y),
                new Avalonia.Point(v.X + halfWidth, v.Y),
            });
        }
        return new Avalonia.Points(vertices.Select(v => new Avalonia.Point(v.X, v.Y)));
    }

    /// <summary>🔴 True when there is NOTHING to plot — not when everything plots to zero.</summary>
    public bool IsEmpty => Series.IsEmpty;

    /// <summary>The inverse, so the view can bind marks and the empty state to disjoint visibilities.</summary>
    public bool HasMarks => !Series.IsEmpty;

    /// <summary>The worded empty state. It says the company has no such vouchers — it does NOT say "zero".</summary>
    public string EmptyMessage => $"No {Title.ToLowerInvariant()} to chart for this period.";

    /// <summary>The bar rectangles — populated only on a <see cref="ChartMark.Bar"/> tile, EMPTY on a
    /// <see cref="ChartMark.Line"/> one. See the constructor.</summary>
    public IReadOnlyList<BarMark> Bars { get; }

    /// <summary>The line vertices — populated only on a <see cref="ChartMark.Line"/> tile, EMPTY on a
    /// <see cref="ChartMark.Bar"/> one. See the constructor.</summary>
    public IReadOnlyList<LineVertex> Vertices { get; }

    /// <summary>The value-axis gridlines. Built for BOTH mark types — a chart of either kind is read against
    /// the same axis, and the axis is not a mark.</summary>
    public IReadOnlyList<AxisTick> Ticks { get; }

    public double BaselineY { get; }

    /// <summary>
    /// The bars split by sign, so the view can bind each half to a DIFFERENT named brush without a converter.
    ///
    /// <para>🔴 <b>This split is why there is no hardcoded colour in the chart XAML.</b> The application pins
    /// <c>RequestedThemeVariant="Light"</c> and has a single palette of named resources; the testable rule is
    /// therefore "every chart colour resolves through a named <c>StaticResource</c>", and a value-to-brush
    /// converter would have had to name a colour in code to satisfy it. Splitting the list instead lets
    /// <c>PositiveGreen</c>/<c>AlertRed</c> — the application's OWN existing sign semantics — be looked up in
    /// XAML like every other brush in the shell.</para>
    /// </summary>
    public IReadOnlyList<BarMark> PositiveBars => Bars.Where(b => b.Value >= 0m).ToList();

    /// <summary>The negative half — see <see cref="PositiveBars"/>.</summary>
    public IReadOnlyList<BarMark> NegativeBars => Bars.Where(b => b.Value < 0m).ToList();

    /// <summary>
    /// The line series as real <see cref="Avalonia.Point"/> values for <c>Polyline.Points</c>.
    ///
    /// <para>🔴 <b>This is deliberately NOT the "x,y x,y" string form, and the reason is cross-platform.</b>
    /// The first cut of this property built that string and bound it; two failures came out of it at once.
    /// First, <c>Polyline.Points</c> is a <see cref="Avalonia.Points"/> collection, and binding a string to it
    /// leaves it NULL — every dashboard threw <c>ArgumentNullException</c> out of
    /// <c>PolylineGeometry..ctor</c> the moment it rendered. Second, and worse if it had survived, the string
    /// form is CULTURE-SENSITIVE: on a de-DE or fr-FR runner <c>1.5</c> formats as <c>1,5</c> and "1,5 2,5"
    /// parses as four coordinates instead of two. Handing over typed points removes both failure modes rather
    /// than papering over the second with an invariant-culture format string.</para>
    ///
    /// <para>Empty (never null) for an empty series AND on every <see cref="ChartMark.Bar"/> tile, so neither
    /// renders a line rather than throwing. The single-point case is spread into two points — see
    /// <see cref="BuildLinePoints"/>, which is where that whole argument lives.</para>
    /// </summary>
    public Avalonia.Points LinePoints { get; }

    /// <summary>The tile's x-axis captions, in order.</summary>
    public IReadOnlyList<string> Labels => Series.Points.Select(p => p.Label).ToList();
}

/// <summary>
/// The <b>graphical dashboard</b> (census row 14.3), hosted as its own cascading Miller column under
/// <c>Reports → Dashboard</c>.
///
/// <para><b>Vendor scope, and what is deliberately absent.</b> The reference product ships <b>line and bar
/// charts only</b>, with pie charts a documented deliberate non-feature, and three dashboard types
/// (Default / Sales / Purchase). All three are here; there is no pie chart and no third mark type. Its own
/// route in is <c>F1 &gt; Settings &gt; Startup</c>, which has <b>no host in this application</b> — there is no
/// Settings screen and no startup-screen preference anywhere in <c>src/</c> — so the dashboard is reached from
/// <b>Reports</b> instead. That is a route we chose; it is honest and complete, and making the dashboard the
/// STARTUP screen is a separate piece of work behind a Settings screen that does not exist yet.</para>
///
/// <para><b>Every figure is read from an already-tested engine.</b> <see cref="VoucherRegister"/> supplies the
/// monthly rows for all three dashboards; this class re-computes nothing. 🔴 <b>But it does NOT take that
/// engine's zero rows at face value:</b> <c>VoucherRegister.Build</c> emits a row per month even when no
/// voucher exists, carrying zeroes, so a company with no sales at all would arrive here as a series of zeros
/// and draw a flat line along the axis. <see cref="SeriesFor"/> collapses that case to
/// <see cref="ChartSeries.Empty"/> on <c>TotalCount == 0</c>, which is the difference between "you sold
/// nothing" and "there is nothing here".</para>
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _to;

    public DashboardKind Kind { get; }

    public string Title => Kind switch
    {
        DashboardKind.Sales => "Sales Dashboard",
        DashboardKind.Purchase => "Purchase Dashboard",
        _ => "Default Dashboard",
    };

    /// <summary>The period every tile covers, spelled out so no tile is read against the wrong window.</summary>
    public string PeriodCaption => $"{ApexDate.Format(_from)} to {ApexDate.Format(_to)}";

    public ObservableCollection<DashboardTileViewModel> Tiles { get; } = new();

    /// <summary>
    /// The tile Alt+C configures. Kept as an index so the column is keyboard-navigable — and it now genuinely
    /// IS: the shell's Up/Down arrows reach <see cref="MoveTileUp"/>/<see cref="MoveTileDown"/> through
    /// <c>StepActive</c>, exactly the way the arrows move the highlight on Outstandings and the other
    /// row-selecting pages, and the highlighted tile paints its title row. Until that landed this index was a
    /// dead knob with a doc comment that claimed otherwise: nothing moved it, nothing showed it, and Alt+C
    /// could only ever configure tile zero.
    /// </summary>
    [ObservableProperty] private int _selectedTileIndex;

    /// <summary>Keeps exactly one tile's <see cref="DashboardTileViewModel.IsSelected"/> true.</summary>
    partial void OnSelectedTileIndexChanged(int value) => SyncTileSelection();

    private void SyncTileSelection()
    {
        for (var i = 0; i < Tiles.Count; i++) Tiles[i].IsSelected = i == SelectedTileIndex;
    }

    /// <summary>Non-null only while the Alt+C tile-configuration column is open over this dashboard.</summary>
    [ObservableProperty] private DashboardTileConfigViewModel? _tileConfig;

    public DashboardViewModel(Company company, DashboardKind kind, DateOnly from, DateOnly to)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        Kind = kind;
        _from = from;
        _to = to;
        Rebuild();
    }

    /// <summary>Rebuilds every tile from the engines. Idempotent; called by the ctor and after a config change.</summary>
    public void Rebuild()
    {
        var keep = SelectedTileIndex;
        Tiles.Clear();

        switch (Kind)
        {
            case DashboardKind.Sales:
                Tiles.Add(new DashboardTileViewModel(
                    "Sales value by month", ChartMark.Bar,
                    SeriesFor(VoucherRegisterKind.Sales, byValue: true), "Amount"));
                Tiles.Add(new DashboardTileViewModel(
                    "Sales vouchers by month", ChartMark.Line,
                    SeriesFor(VoucherRegisterKind.Sales, byValue: false), "Vouchers"));
                break;

            case DashboardKind.Purchase:
                Tiles.Add(new DashboardTileViewModel(
                    "Purchase value by month", ChartMark.Bar,
                    SeriesFor(VoucherRegisterKind.Purchase, byValue: true), "Amount"));
                Tiles.Add(new DashboardTileViewModel(
                    "Purchase vouchers by month", ChartMark.Line,
                    SeriesFor(VoucherRegisterKind.Purchase, byValue: false), "Vouchers"));
                break;

            default:
                Tiles.Add(new DashboardTileViewModel(
                    "Sales value by month", ChartMark.Bar,
                    SeriesFor(VoucherRegisterKind.Sales, byValue: true), "Amount"));
                Tiles.Add(new DashboardTileViewModel(
                    "Purchase value by month", ChartMark.Bar,
                    SeriesFor(VoucherRegisterKind.Purchase, byValue: true), "Amount"));
                // Named for exactly what it is. It is NOT profit: it is the difference between two register
                // footings, before cost of sales, stock movement, expenses or tax. Calling it "profit" here
                // would be a wrong-figures defect of the plainest kind.
                Tiles.Add(new DashboardTileViewModel(
                    "Sales less purchases by month", ChartMark.Line, SalesLessPurchases(), "Amount"));
                break;
        }

        SelectedTileIndex = Tiles.Count == 0 ? 0 : Math.Clamp(keep, 0, Tiles.Count - 1);
        // Called unconditionally: the setter above raises OnSelectedTileIndexChanged only when the VALUE
        // changes, and on the first build (and on any rebuild that lands on the same index) it does not — which
        // would leave a freshly-built tile list with no tile marked at all.
        SyncTileSelection();
    }

    /// <summary>
    /// A register's monthly series — value or voucher count.
    ///
    /// <para>🔴 <b>The empty/zero distinction lives here.</b> <c>VoucherRegister.Build</c> emits a zero row for
    /// every month in the window whether or not a voucher exists, so a company that has never traded would
    /// otherwise produce twelve zeros and a flat line. <c>TotalCount == 0</c> means there is nothing to chart,
    /// and that is what <see cref="ChartSeries.Empty"/> says.</para>
    /// </summary>
    private ChartSeries SeriesFor(VoucherRegisterKind kind, bool byValue)
    {
        var title = VoucherRegister.TitleOf(kind);
        var register = VoucherRegister.Build(_company, kind, _from, _to);
        if (register.TotalCount == 0) return ChartSeries.Empty(title);

        return new ChartSeries(title, register.Months
            .Select(m => new ChartPoint(MonthLabel(m.Month),
                                        byValue ? m.Value.Amount : m.VoucherCount))
            .ToList());
    }

    /// <summary>Sales footing minus purchase footing, month by month. Empty when NEITHER register has a
    /// voucher — if one of them does, the other's zeros are a real measured zero and must be drawn.</summary>
    private ChartSeries SalesLessPurchases()
    {
        var sales = VoucherRegister.Build(_company, VoucherRegisterKind.Sales, _from, _to);
        var purchases = VoucherRegister.Build(_company, VoucherRegisterKind.Purchase, _from, _to);
        if (sales.TotalCount == 0 && purchases.TotalCount == 0)
            return ChartSeries.Empty("Sales less purchases");

        var byMonth = purchases.Months.ToDictionary(m => (m.Month.Year, m.Month.Month), m => m.Value.Amount);
        return new ChartSeries("Sales less purchases", sales.Months
            .Select(m => new ChartPoint(
                MonthLabel(m.Month),
                m.Value.Amount - (byMonth.TryGetValue((m.Month.Year, m.Month.Month), out var p) ? p : 0m)))
            .ToList());
    }

    /// <summary>"Apr-25" style month captions. INVARIANT culture on purpose: the abbreviated month name is
    /// culture-dependent, and this axis must read the same on a ubuntu runner as on the operator's Windows
    /// desktop — the same reason every other date surface in this application formats explicitly.</summary>
    private static string MonthLabel(MonthWindow m) =>
        new DateOnly(m.Year, m.Month, 1)
            .ToString("MMM-yy", System.Globalization.CultureInfo.InvariantCulture);

    // ---- Alt+C: configure the highlighted tile ----

    /// <summary>Alt+C — opens the configuration panel for the highlighted tile.</summary>
    public void OpenTileConfig()
    {
        if (Tiles.Count == 0) return;
        TileConfig = new DashboardTileConfigViewModel(Tiles[SelectedTileIndex]);
    }

    /// <summary>Escape from the config panel.</summary>
    public void CloseTileConfig() => TileConfig = null;

    /// <summary>Down-arrow on an open dashboard — moves the Alt+C target to the next tile. Reached from the
    /// shell's <c>StepActive</c>, the one door every arrow key in this application goes through. Does NOT wrap:
    /// a menu column wraps because it is a ring of choices; a tile list is a short read-down column, and the
    /// vendor's own dashboard does not cycle.</summary>
    public void MoveTileDown()
    {
        if (SelectedTileIndex < Tiles.Count - 1) SelectedTileIndex++;
    }

    /// <summary>Up-arrow on an open dashboard — see <see cref="MoveTileDown"/>.</summary>
    public void MoveTileUp()
    {
        if (SelectedTileIndex > 0) SelectedTileIndex--;
    }
}

/// <summary>
/// The Alt+C tile-configuration panel. It reports what the tile IS — its series, its mark type, its period
/// coverage and its footing — rather than offering knobs the vendor's own surface has not been measured to
/// carry. 🔴 <b>Deliberately read-only.</b> Inventing a set of configuration options and shipping controls
/// that write nowhere would be the dead-capability shape this project has already filed three times; when the
/// vendor's Alt+C option set is measured, this panel is where it lands.
/// </summary>
public sealed partial class DashboardTileConfigViewModel : ViewModelBase
{
    public DashboardTileConfigViewModel(DashboardTileViewModel tile)
    {
        Tile = tile;
    }

    public DashboardTileViewModel Tile { get; }

    public string Title => "Configure Tile";

    public string TileTitle => Tile.Title;

    public string MarkCaption => Tile.Mark == ChartMark.Bar ? "Bar chart" : "Line chart";

    /// <summary>How many points the tile plots — 0 when there is nothing to plot, which the panel states in
    /// words rather than as a figure that could be read as a measured zero.</summary>
    public string PointsCaption => Tile.IsEmpty
        ? "No data points — this tile has nothing to chart for the period."
        : $"{Tile.Series.Points.Count} monthly points.";

    /// <summary>The one honest statement about what is configurable today.</summary>
    public string Notice =>
        "This panel reports what the tile shows. Chart options are not configurable yet — "
        + "no option set has been sourced for it, and controls that wrote nowhere would be worse than none.";
}
