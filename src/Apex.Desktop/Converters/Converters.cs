using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Apex.Ledger;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Converters;

/// <summary>Uppercases a string for dim section headers (MASTERS / TRANSACTIONS / REPORTS).</summary>
public sealed class UpperConverter : IValueConverter
{
    public static readonly UpperConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.ToUpperInvariant() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a bool (IsSubItem) to a left-indent margin so submenu children sit under the header.</summary>
public sealed class SubIndentConverter : IValueConverter
{
    public static readonly SubIndentConverter Instance = new();

    private static readonly Thickness Indented = new(18, 0, 0, 0);
    private static readonly Thickness Flush = new(0);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Indented : Flush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a column's <c>IsMenu</c> flag to its fixed pixel width: a narrow 300 px for a menu column,
/// a wider 560 px for a page column (so reports/vouchers/the chart render clean without clipping).
/// </summary>
public sealed class ColumnWidthConverter : IValueConverter
{
    public static readonly ColumnWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 300.0 : 640.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a column's <c>IsActive</c> flag to its header band brush: brand navy for the focused column,
/// a muted slate-navy for an inactive (earlier) column — so the eye reads which column has focus.
/// </summary>
public sealed class ColumnHeaderBrushConverter : IValueConverter
{
    public static readonly ColumnHeaderBrushConverter Instance = new();

    private static readonly IBrush ActiveNavy = new SolidColorBrush(Color.Parse("#1B3A6B"));
    private static readonly IBrush InactiveNavy = new SolidColorBrush(Color.Parse("#8494B0"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveNavy : InactiveNavy;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a numeric depth/indent to a left-margin <see cref="Thickness"/> for the account tree.</summary>
public sealed class IndentToThicknessConverter : IValueConverter
{
    public static readonly IndentToThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value switch
        {
            double d => d,
            int i => i,
            _ => 0.0,
        };
        return new Thickness(left, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="DrCr"/> value to its short label ("Dr" / "Cr") for combo display.</summary>
public sealed class DrCrLabelConverter : IValueConverter
{
    public static readonly DrCrLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DrCr d ? (d == DrCr.Debit ? "Dr" : "Cr") : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="Apex.Ledger.Domain.BillRefType"/> to its label ("New Ref" / "Agst Ref" /
/// "Advance" / "On Account") for the "Type of Ref" combo in the bill-wise sub-panel.
/// </summary>
public sealed class BillRefTypeLabelConverter : IValueConverter
{
    public static readonly BillRefTypeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Apex.Ledger.Domain.BillRefType t
            ? t switch
            {
                Apex.Ledger.Domain.BillRefType.NewRef => "New Ref",
                Apex.Ledger.Domain.BillRefType.AgstRef => "Agst Ref",
                Apex.Ledger.Domain.BillRefType.Advance => "Advance",
                Apex.Ledger.Domain.BillRefType.OnAccount => "On Account",
                _ => t.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps (IsSelected, IsHighlighted) → an Outstandings bill-row background: amber for the row under the
/// keyboard highlight, a pale-green tick tint for a spacebar-selected row, a blend when both, else white.
/// Lets the user see at a glance which bills are picked for Ctrl+B settlement.
/// </summary>
public sealed class OutstandingRowBrushConverter : IMultiValueConverter
{
    public static readonly OutstandingRowBrushConverter Instance = new();

    private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#FFE9A8"));
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#CDEBCB"));
    private static readonly IBrush SelectedAndHighlight = new SolidColorBrush(Color.Parse("#BFE0B0"));
    private static readonly IBrush None = Brushes.White;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Count > 0 && values[0] is true;
        var highlighted = values.Count > 1 && values[1] is true;
        if (selected && highlighted) return SelectedAndHighlight;
        if (selected) return Selected;
        if (highlighted) return Highlight;
        return None;
    }
}

/// <summary>True when the bound <see cref="Screen"/> equals the converter parameter (screen name).</summary>
public sealed class ScreenEqualsConverter : IValueConverter
{
    public static readonly ScreenEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Screen s && parameter is string p
           && string.Equals(s.ToString(), p, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a bool (IsSelected) to the amber highlight brush, else transparent.</summary>
public sealed class SelectedToBrushConverter : IValueConverter
{
    public static readonly SelectedToBrushConverter Instance = new();

    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFD54F"));
    private static readonly IBrush None = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Amber : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps (IsSelected, IsActiveColumn) → the cascade row's highlight brush: bright amber when the row is
/// selected in the FOCUSED column, a dim "selected-but-inactive" slate for a selected row in an EARLIER
/// column, and transparent otherwise. Keeps only the active column visibly bright.
/// </summary>
public sealed class SelectedActiveToBrushConverter : IMultiValueConverter
{
    public static readonly SelectedActiveToBrushConverter Instance = new();

    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFD54F"));
    private static readonly IBrush InactiveSlate = new SolidColorBrush(Color.Parse("#DfE4EC"));
    private static readonly IBrush None = Brushes.Transparent;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Count > 0 && values[0] is true;
        var active = values.Count > 1 && values[1] is true;
        if (!selected) return None;
        return active ? Amber : InactiveSlate;
    }
}

/// <summary>Maps a bool (IsTotal/IsHeader) to Bold, else Normal font weight.</summary>
public sealed class BoolToWeightConverter : IValueConverter
{
    public static readonly BoolToWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a bool (IsSelected) to the ink foreground when selected, else navy.</summary>
public sealed class SelectedToForegroundConverter : IValueConverter
{
    public static readonly SelectedToForegroundConverter Instance = new();

    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush Navy = new SolidColorBrush(Color.Parse("#1B3A6B"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Ink : Navy;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a bool (IsBalanced) to the difference-text foreground: ink when balanced, alert-red when
/// out of balance — so an unbalanced voucher's difference reads clearly as an error.
/// </summary>
public sealed class BalancedToBrushConverter : IValueConverter
{
    public static readonly BalancedToBrushConverter Instance = new();

    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush AlertRed = new SolidColorBrush(Color.Parse("#B00020"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Ink : AlertRed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
