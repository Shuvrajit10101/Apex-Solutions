using System;
using System.Linq;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// The pure geometry behind census row 14.3.
///
/// <para>🔴 <b>Every test in this file is a COMPILE ERROR against the state it was written for.</b> Before it,
/// this application had no charting library and — measured — <b>zero chart primitives</b>: a grep for
/// <c>&lt;Polyline</c>, <c>&lt;Path</c>, <c>&lt;PathGeometry</c>, <c>&lt;Canvas</c>, <c>StreamGeometry</c>,
/// <c>DrawingContext</c> and <c>RenderTargetBitmap</c> across every <c>.cs</c> and <c>.axaml</c> in <c>src/</c>
/// returned not one file. That is the strongest available form of "fails on main".</para>
///
/// <para><b>Why the geometry is ours and not a library's.</b> The surface being cloned is two mark types, line
/// and bar (pie charts are a documented deliberate non-feature). A library's only testable output here is a
/// screenshot, and this repository cannot render-test reliably. Owning the mapping makes every coordinate a
/// number a test can pin — which is the only defence that has ever worked on this project, where three
/// unbounded Balance-Sheet errors each passed a full green suite.</para>
/// </summary>
public sealed class ChartGeometryTests
{
    private static ChartSeries S(params decimal[] values) =>
        new("Series", values.Select((v, i) => new ChartPoint($"M{i + 1}", v)).ToList());

    // ------------------------------------------------------------------ 🔴 the wrong-figures lock

    /// <summary>
    /// 🔴 <b>THE ONE TEST TO KEEP IF ONLY ONE SURVIVES REVIEW.</b> "Every month moved ₹0" and "there is nothing
    /// here to show" are DIFFERENT FACTS. A chart that draws a flat line along the baseline for both reports the
    /// second as the first — a wrong-figures defect, not a cosmetic one, and exactly what a general-purpose
    /// charting library does by default.
    /// </summary>
    [Fact]
    public void A_series_of_all_zeros_is_not_the_same_as_an_empty_series()
    {
        var zeros = S(0m, 0m, 0m);
        var empty = ChartSeries.Empty("Series");

        Assert.False(zeros.IsEmpty);
        Assert.True(empty.IsEmpty);

        // The zero series PLOTS: three real bars, all of height zero, standing on a real axis.
        var zeroBars = ChartGeometry.BuildBars(zeros, 300, 100);
        Assert.Equal(3, zeroBars.Count);
        Assert.All(zeroBars, b => Assert.Equal(0d, b.Height, 6));

        // The empty series produces NOTHING to draw — no bars, no line, no captioned gridlines. The view must
        // render its worded empty state instead.
        Assert.Empty(ChartGeometry.BuildBars(empty, 300, 100));
        Assert.Empty(ChartGeometry.BuildLine(empty, 300, 100));
        Assert.Empty(ChartGeometry.BuildAxisTicks(empty, 100));
    }

    /// <summary>An all-zero series must not divide by zero, and must not collapse its own axis.</summary>
    [Fact]
    public void An_all_zero_series_has_a_real_axis_and_does_not_divide_by_zero()
    {
        var (min, max) = ChartGeometry.ValueRange(S(0m, 0m));
        Assert.Equal(0m, min);
        Assert.True(max > min, "a degenerate range would divide by zero when a value became a pixel");
    }

    // ------------------------------------------------------------------ the mapping

    [Fact]
    public void Bars_map_values_to_pixel_heights_proportionally()
    {
        var bars = ChartGeometry.BuildBars(S(100m, 50m, 0m), plotWidth: 300, plotHeight: 100);

        Assert.Equal(3, bars.Count);
        Assert.Equal(100d, bars[0].Height, 6);   // the maximum fills the plot
        Assert.Equal(50d, bars[1].Height, 6);    // half the value, half the height
        Assert.Equal(0d, bars[2].Height, 6);

        // Slots are equal and bars sit inside them in order, left to right.
        Assert.True(bars[0].X < bars[1].X && bars[1].X < bars[2].X);
        Assert.Equal(bars[1].X - bars[0].X, bars[2].X - bars[1].X, 6);
        Assert.All(bars, b => Assert.True(b.Width > 0 && b.Width <= 100d));
    }

    /// <summary>
    /// 🔴 Balance-sheet and P&amp;L series DO go negative. The baseline must lift off the bottom edge and the
    /// negative bar must hang BELOW it — a chart that clipped at zero would silently under-report a loss.
    /// </summary>
    [Fact]
    public void Negative_values_place_the_baseline_off_the_bottom_edge()
    {
        var series = S(100m, -100m);
        var baseline = ChartGeometry.BaselineY(series, plotHeight: 100);
        Assert.Equal(50d, baseline, 6);          // zero sits halfway up a [-100, 100] axis

        var bars = ChartGeometry.BuildBars(series, plotWidth: 200, plotHeight: 100);
        Assert.Equal(0d, bars[0].Y, 6);          // +100 tops out at the ceiling
        Assert.Equal(50d, bars[0].Height, 6);    // and stands ON the baseline
        Assert.Equal(50d, bars[1].Y, 6);         // -100 starts AT the baseline
        Assert.Equal(50d, bars[1].Height, 6);    // and hangs to the floor
    }

    [Fact]
    public void The_axis_always_includes_zero_even_when_every_value_is_positive()
    {
        var (min, max) = ChartGeometry.ValueRange(S(500m, 900m));
        Assert.Equal(0m, min);
        Assert.Equal(900m, max);
    }

    [Fact]
    public void Line_vertices_sit_at_slot_centres_so_a_line_and_a_bar_chart_align()
    {
        var series = S(10m, 20m, 30m);
        var line = ChartGeometry.BuildLine(series, plotWidth: 300, plotHeight: 100);
        var bars = ChartGeometry.BuildBars(series, plotWidth: 300, plotHeight: 100);

        Assert.Equal(3, line.Count);
        for (var i = 0; i < 3; i++)
            Assert.Equal(bars[i].X + (bars[i].Width / 2.0), line[i].X, 6);
    }

    // ------------------------------------------------------------------ the axis captions

    /// <summary>
    /// 🔴 An axis in this application reads in <b>Indian lakh/crore grouping</b>, like every other figure on
    /// every other screen — never in the thousands grouping a general-purpose charting library applies. This is
    /// the concrete reason the tick algorithm is ours: a library's would be right about the maths and wrong
    /// about the country.
    /// </summary>
    [Fact]
    public void Axis_ticks_use_Indian_lakh_crore_grouping()
    {
        var ticks = ChartGeometry.BuildAxisTicks(S(0m, 10_00_000m), plotHeight: 100);

        Assert.NotEmpty(ticks);
        var tenLakh = ticks.FirstOrDefault(t => t.Value == 10_00_000m);
        Assert.NotEqual(default, tenLakh);
        // 10,00,000 — two digits, then two, then three. NOT 1,000,000.
        Assert.Contains("10,00,000", tenLakh.Caption, StringComparison.Ordinal);
        Assert.DoesNotContain("1,000,000", tenLakh.Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Nice_steps_round_up_the_one_two_five_ladder()
    {
        Assert.Equal(1m, ChartGeometry.NiceStep(0.9m));
        Assert.Equal(2m, ChartGeometry.NiceStep(1.4m));
        Assert.Equal(5m, ChartGeometry.NiceStep(4.2m));
        Assert.Equal(10m, ChartGeometry.NiceStep(9.1m));
        // The ladder has no 2.5 rung, so 23,847 goes to 50,000 — NOT to 25,000. This assertion was written the
        // other way round first and the code was right: recorded here so the rung is not "fixed" back in.
        Assert.Equal(50000m, ChartGeometry.NiceStep(23_847m));
        Assert.Equal(0m, ChartGeometry.NiceStep(0m));
    }

    /// <summary>Ticks must cover the range and must be ordered, with no runaway loop on any data shape.</summary>
    [Fact]
    public void Axis_ticks_cover_the_range_and_terminate()
    {
        foreach (var series in new[] { S(1m), S(-5m, 5m), S(0.0001m, 0.0002m), S(99_99_99_999m) })
        {
            var ticks = ChartGeometry.BuildAxisTicks(series, 150);
            Assert.NotEmpty(ticks);
            Assert.True(ticks.Count <= 33, "the tick loop must be hard-bounded");
            var (min, max) = ChartGeometry.ValueRange(series);
            Assert.All(ticks, t => Assert.InRange(t.Value, min, max));
        }
    }

    /// <summary>A zero-sized plot (a collapsed column, a tile laid out before measure) must yield nothing
    /// rather than NaN or Infinity coordinates that would poison the visual tree.</summary>
    [Fact]
    public void A_zero_sized_plot_yields_no_marks_and_never_a_NaN()
    {
        Assert.Empty(ChartGeometry.BuildBars(S(1m, 2m), 0, 100));
        Assert.Empty(ChartGeometry.BuildBars(S(1m, 2m), 100, 0));
        Assert.Empty(ChartGeometry.BuildLine(S(1m, 2m), 0, 0));

        var bars = ChartGeometry.BuildBars(S(1m, 2m), 100, 100);
        Assert.All(bars, b =>
        {
            Assert.False(double.IsNaN(b.X) || double.IsNaN(b.Y), "a NaN coordinate would poison the tree");
            Assert.False(double.IsInfinity(b.Height) || double.IsInfinity(b.Width));
        });
    }
}
