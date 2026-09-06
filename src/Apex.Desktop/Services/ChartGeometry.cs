using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.Services;

/// <summary>
/// One plotted point: its caption (the x-axis label) and its value. The value is a <see cref="decimal"/>
/// because every figure in this application is money and money is never a <see cref="double"/> until the
/// last possible moment — the conversion happens once, inside <see cref="ChartGeometry"/>, when a value
/// becomes a pixel.
/// </summary>
public readonly record struct ChartPoint(string Label, decimal Value);

/// <summary>A bar's rectangle in pixel space, top-left origin, ready to place on a Canvas.</summary>
public readonly record struct BarMark(string Label, decimal Value, double X, double Y, double Width, double Height);

/// <summary>A point on a line series in pixel space.</summary>
public readonly record struct LineVertex(string Label, decimal Value, double X, double Y);

/// <summary>A horizontal gridline: its value, its already-formatted Indian-grouped caption, and its Y pixel.</summary>
public readonly record struct AxisTick(decimal Value, string Caption, double Y);

/// <summary>
/// A chart's data, and — load-bearing — <b>whether it has any</b>.
///
/// <para>🔴 <b>THE DISTINCTION THIS TYPE EXISTS TO MAKE.</b> A series of all zeros and a series with no
/// points at all are DIFFERENT FACTS and must not render the same. "This ledger moved ₹0 every month" is a
/// figure; "there is nothing here to show" is the absence of one. A chart that draws a flat line along the
/// baseline for both tells the operator the second is the first — a <b>wrong-figures</b> defect, not a
/// cosmetic one, and precisely the defect a charting library would hand us for free.
/// <see cref="IsEmpty"/> is what the view branches on to render a worded empty state instead of marks.</para>
/// </summary>
public sealed record ChartSeries(string Title, IReadOnlyList<ChartPoint> Points)
{
    /// <summary>The explicit "nothing to plot" sentinel. NOT a zero series.</summary>
    public static ChartSeries Empty(string title) => new(title, Array.Empty<ChartPoint>());

    /// <summary>True when there is no data at all. A series of zeros is NOT empty — see the type remarks.</summary>
    public bool IsEmpty => Points.Count == 0;
}

/// <summary>
/// The pure value-space → pixel-space mapping behind every chart this application draws. <b>No Avalonia type
/// appears in this file</b>, and nothing here touches a control: the whole of the geometry is a deterministic
/// function of (values, plot size), so a unit test can pin every coordinate exactly.
///
/// <para><b>Why we own this instead of taking a charting dependency.</b> The vendor surface being cloned is
/// <b>two mark types — line and bar</b> (pie charts are a documented deliberate non-feature), so a library
/// would be bought for scatter/stacked/animation/legend-layout that this clone may not ship. More decisively,
/// this project's own history says the only defence that has ever worked here is a pure, deterministic,
/// asserted-on function: three unbounded Balance-Sheet errors each passed a full green suite. If the geometry
/// is ours, <see cref="BuildBars"/> returns doubles a test pins; if it is a library's, the only available
/// assertion is a screenshot, and this repository's headless renderer cannot reliably produce one.</para>
///
/// <para><b>Sign handling.</b> The value axis spans <c>[min(0, minValue), max(0, maxValue)]</c>, so zero is
/// always on the axis and a negative series places the baseline ABOVE the bottom edge with its bars hanging
/// below it. Balance-sheet and P&amp;L series do go negative, and a chart that clipped them at zero would
/// silently under-report a loss.</para>
/// </summary>
public static class ChartGeometry
{
    /// <summary>The number of horizontal gridlines a chart aims for. Four intervals ⇒ five captions.</summary>
    public const int TargetTickCount = 4;

    /// <summary>
    /// The value-axis range a series is plotted against: always inclusive of zero, and never degenerate.
    ///
    /// <para>An all-zero series would otherwise give <c>min == max == 0</c> and a division by zero; it is
    /// widened to <c>[0, 1]</c> so every bar lands at height 0 against a real axis — which is the honest
    /// picture of "every month moved nothing", and is visibly different from the empty state.</para>
    /// </summary>
    public static (decimal Min, decimal Max) ValueRange(ChartSeries series)
    {
        if (series.IsEmpty) return (0m, 1m);

        var min = Math.Min(0m, series.Points.Min(p => p.Value));
        var max = Math.Max(0m, series.Points.Max(p => p.Value));

        if (min == max) return (min, min + 1m);   // an all-zero (or all-equal-to-zero) series
        return (min, max);
    }

    /// <summary>Maps a value to its Y pixel inside a plot of <paramref name="plotHeight"/>, top-left origin.</summary>
    public static double ValueToY(decimal value, decimal min, decimal max, double plotHeight)
    {
        if (plotHeight <= 0) return 0;
        var span = max - min;
        if (span == 0m) return plotHeight;
        var fraction = (double)((value - min) / span);
        return plotHeight - (fraction * plotHeight);
    }

    /// <summary>
    /// Bars for <paramref name="series"/> across a plot of <paramref name="plotWidth"/> ×
    /// <paramref name="plotHeight"/>. Returns an EMPTY list for an empty series — the view must render its
    /// worded empty state rather than an axis with nothing on it.
    ///
    /// <para>Each slot is <c>plotWidth / count</c> wide and the bar occupies the middle
    /// <paramref name="barFillFraction"/> of it, so bar spacing is proportional and a one-month chart is not
    /// a single bar spanning the whole panel.</para>
    /// </summary>
    public static IReadOnlyList<BarMark> BuildBars(
        ChartSeries series, double plotWidth, double plotHeight, double barFillFraction = 0.62)
    {
        if (series.IsEmpty || plotWidth <= 0 || plotHeight <= 0) return Array.Empty<BarMark>();

        var (min, max) = ValueRange(series);
        var baseline = ValueToY(0m, min, max, plotHeight);
        var slot = plotWidth / series.Points.Count;
        var barWidth = slot * Math.Clamp(barFillFraction, 0.05, 1.0);
        var pad = (slot - barWidth) / 2.0;

        var marks = new List<BarMark>(series.Points.Count);
        for (var i = 0; i < series.Points.Count; i++)
        {
            var p = series.Points[i];
            var y = ValueToY(p.Value, min, max, plotHeight);
            // A negative value's rectangle hangs BELOW the baseline; a positive one rises to it from above.
            var top = Math.Min(y, baseline);
            var height = Math.Abs(baseline - y);
            marks.Add(new BarMark(p.Label, p.Value, (i * slot) + pad, top, barWidth, height));
        }
        return marks;
    }

    /// <summary>
    /// Vertices for a line series, one per point, placed at the CENTRE of each slot so a line chart and a bar
    /// chart of the same data align column for column. Empty for an empty series.
    /// </summary>
    public static IReadOnlyList<LineVertex> BuildLine(ChartSeries series, double plotWidth, double plotHeight)
    {
        if (series.IsEmpty || plotWidth <= 0 || plotHeight <= 0) return Array.Empty<LineVertex>();

        var (min, max) = ValueRange(series);
        var slot = plotWidth / series.Points.Count;

        var vertices = new List<LineVertex>(series.Points.Count);
        for (var i = 0; i < series.Points.Count; i++)
        {
            var p = series.Points[i];
            vertices.Add(new LineVertex(p.Label, p.Value,
                                        (i * slot) + (slot / 2.0),
                                        ValueToY(p.Value, min, max, plotHeight)));
        }
        return vertices;
    }

    /// <summary>The Y pixel of the zero line — where a bar chart's bars stand and a negative series hangs from.</summary>
    public static double BaselineY(ChartSeries series, double plotHeight)
    {
        var (min, max) = ValueRange(series);
        return ValueToY(0m, min, max, plotHeight);
    }

    /// <summary>
    /// Horizontal gridlines at "nice" round values covering the series range, captioned through
    /// <see cref="IndianFormat"/> so a chart axis reads in <b>lakh/crore grouping</b> like every other figure
    /// in this application — never in the thousands grouping a general-purpose charting library would apply.
    ///
    /// <para>Empty for an empty series: gridlines captioned with amounts, drawn over no data, would imply a
    /// measured zero.</para>
    /// </summary>
    public static IReadOnlyList<AxisTick> BuildAxisTicks(ChartSeries series, double plotHeight)
    {
        if (series.IsEmpty || plotHeight <= 0) return Array.Empty<AxisTick>();

        var (min, max) = ValueRange(series);
        var step = NiceStep((max - min) / TargetTickCount);
        if (step <= 0m) return Array.Empty<AxisTick>();

        var first = Math.Floor(min / step) * step;
        var ticks = new List<AxisTick>();
        // 🔴 STRICTLY <= max, with NO half-step slack. A slack term let the loop emit one tick ABOVE the
        // series maximum, which mapped to a NEGATIVE Y (-1.5e-7 in the case that caught it) — a gridline
        // captioned with an amount, drawn off the top of the plot, for a value no data reaches. The top of
        // the range simply goes uncaptioned when it is not a multiple of the step; that is honest, and a
        // gridline outside the data is not.
        for (var v = first; v <= max; v += step)
        {
            if (v < min) continue;
            // 🔴 AmountAlways, NOT Amount. `IndianFormat.Amount` renders exactly zero as the EMPTY STRING —
            // that is the report-GRID blank-at-zero convention, and it is wrong on an axis. The axis is
            // documented above to always include zero, and `first` is always a multiple of `step`, so the zero
            // tick is ALWAYS emitted; with Amount it was ALWAYS captioned with nothing. On the one Default-
            // dashboard series that genuinely goes negative ("Sales less purchases by month") the single
            // unlabelled gridline was therefore the one dividing profit from loss — the reader could not locate
            // zero from the captions. AmountAlways exists for exactly this case ("a zero is a meaningful
            // balance rather than a blank cell") and every_axis_tick_is_captioned_including_zero locks it.
            ticks.Add(new AxisTick(v, IndianFormat.AmountAlways(v), ValueToY(v, min, max, plotHeight)));
            if (ticks.Count > 32) break;      // a hard stop; no data shape may spin this loop
        }
        return ticks;
    }

    /// <summary>
    /// Rounds a raw step up to the next 1 / 2 / 5 × 10ⁿ — the standard "nice number" ladder, so an axis reads
    /// 0.00 / 50,000.00 / 1,00,000.00 rather than 0.00 / 23,847.00 / 47,694.00. Note the ladder has no 2.5 rung,
    /// so a raw step of 23,847 rounds to 50,000, not to 25,000.
    ///
    /// <para>(The captions carry paisa because <see cref="IndianFormat.AmountAlways"/> is the application's one
    /// always-render money format; this comment previously showed them without, and a comment that states a
    /// caption the code does not produce is how a false claim gets believed.)</para>
    /// </summary>
    public static decimal NiceStep(decimal raw)
    {
        if (raw <= 0m) return 0m;

        var magnitude = 1m;
        while (raw >= 10m) { raw /= 10m; magnitude *= 10m; }
        while (raw < 1m) { raw *= 10m; magnitude /= 10m; }

        var lead = raw <= 1m ? 1m : raw <= 2m ? 2m : raw <= 5m ? 5m : 10m;
        return lead * magnitude;
    }
}
