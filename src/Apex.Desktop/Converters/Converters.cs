using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Converters;

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
