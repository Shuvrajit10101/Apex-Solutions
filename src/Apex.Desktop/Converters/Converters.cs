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
/// LEGACY / floor source. Maps a column's <c>IsMenu</c> flag to a fixed pixel width: 300 px for a menu
/// column, 640 px for a page column. The live cascade no longer binds <c>Width</c> to this converter —
/// C4 replaced it with the viewport-aware <see cref="CascadeColumnWidthConverter"/> (page column fills
/// the leftover viewport, menu columns content-size within a clamp). It is RETAINED because the page
/// value (640) is the invariant-test FLOOR: <c>XamlLayoutInvariantTests.PageColumnContentWidth</c> reads
/// <c>Convert(false)</c> so the static budget checks measure against the page column's *minimum* width
/// (the smallest it is ever laid out at, when the shell floors it at MinWidth). Do not delete.
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
/// C4 — viewport-aware, POSITION-aware cascade column width. Replaces the old fixed
/// <see cref="ColumnWidthConverter"/> on the Miller-column Border so the TERMINAL (rightmost) PAGE column
/// fills the leftover viewport (no dead cream to its right) while MENU columns content-size (clamped by
/// <see cref="ColumnMinWidthConverter"/> / <see cref="ColumnMaxWidthConverter"/>).
///
/// <para>Bound with four values (in order): <c>IsMenu</c>, the cascade ScrollViewer's
/// <c>Bounds.Width</c> (the viewport), this column's own <c>ContentPresenter.Bounds.X</c> (its left
/// offset = cumulative width of every column to its left), and <c>IsLast</c> (true only for the rightmost
/// column). Resolution:</para>
/// <list type="bullet">
/// <item>A MENU column returns <c>NaN</c> ⇒ <c>Width=Auto</c> (content-sized within the Min/Max clamp).</item>
/// <item>A NON-TERMINAL PAGE column (another column sits to its right — e.g. a report with a
/// ledger-vouchers drill column, or an F12 config panel) returns the bounded <see cref="PageFloor"/> so
/// the report and its drill can be shown SIDE-BY-SIDE when the viewport is wide enough; the horizontal
/// ScrollViewer is the fallback when even two floored columns overflow. (F1 fix: without this a greedy
/// report ate the whole viewport and its drill could never sit beside it.)</item>
/// <item>The TERMINAL PAGE column returns <c>max(PageFloor, viewport − ownX − RightMargin)</c> — it
/// absorbs exactly the viewport to the right of every column before it, never shrinking below
/// <see cref="PageFloor"/> so the ScrollViewer scrolls (instead of squeezing) when the columns don't fit.</item>
/// </list>
/// </summary>
public sealed class CascadeColumnWidthConverter : IMultiValueConverter
{
    public static readonly CascadeColumnWidthConverter Instance = new();

    /// <summary>The page column's minimum width — identical to the old fixed page width, so every
    /// static invariant-test budget (638 = 640 − 2px border) stays valid; the semantics merely shift
    /// from "page is always 640" to "page is AT LEAST 640".</summary>
    public const double PageFloor = 640.0;

    /// <summary>Holds the content extent this far under the viewport at the fit boundary so the
    /// horizontal scrollbar does not flicker. Matches the column Border's <c>Margin="0,0,6,0"</c>.</summary>
    private const double RightMargin = 6.0;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is true) return double.NaN; // MENU -> Auto (content-sized)

        var viewport = values.Count > 1 && values[1] is double vp && !double.IsNaN(vp) ? vp : 0.0;
        var ownX = values.Count > 2 && values[2] is double x && !double.IsNaN(x) ? x : 0.0;
        // IsLast defaults to TRUE (fill-viewport) when not supplied, so the legacy 3-value callers and the
        // common single-terminal-page case keep the dead-cream-killing behaviour unchanged.
        var isLast = values.Count <= 3 || values[3] is not false;

        // A non-terminal page column keeps a bounded width so a report + its drill both fit; only the
        // rightmost page column fills the leftover viewport.
        if (!isLast) return PageFloor;

        // Before the first arrange the viewport/ownX are 0 -> fall back to the floor; they settle to real
        // values within a couple of arrange passes (ownX depends only on the preceding columns, never on
        // the terminal page's own width, so this converges — it is not circular).
        return viewport <= 0 ? PageFloor : Math.Max(PageFloor, viewport - ownX - RightMargin);
    }
}

/// <summary>C4 — the cascade column's MinWidth floor: 260 px for a menu column (so short menus keep a
/// readable minimum and DB-name pickers do not collapse), <see cref="CascadeColumnWidthConverter.PageFloor"/>
/// for a page column. Bound to <c>IsMenu</c>.</summary>
public sealed class ColumnMinWidthConverter : IValueConverter
{
    public static readonly ColumnMinWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 260.0 : CascadeColumnWidthConverter.PageFloor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>C4 — the cascade column's MaxWidth clamp: 380 px for a menu column (LOAD-BEARING — a
/// content-sized column inside a horizontal StackPanel is measured at INFINITE width, so without this a
/// very long ledger name would size its menu column arbitrarily wide, inflate the page column's own
/// offset and push it off-screen), unbounded for a page column. Bound to <c>IsMenu</c>.</summary>
public sealed class ColumnMaxWidthConverter : IValueConverter
{
    public static readonly ColumnMaxWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 380.0 : double.PositiveInfinity;

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

/// <summary>Maps a <see cref="Apex.Ledger.Domain.BudgetType"/> to its human label for the Type picker.</summary>
public sealed class BudgetTypeLabelConverter : IValueConverter
{
    public static readonly BudgetTypeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Apex.Ledger.Domain.BudgetType t
            ? Apex.Desktop.ViewModels.BudgetMasterViewModel.TypeLabel(t)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a Budget Variance figure by its sign: over budget (IsOver) reads red, under budget (IsUnder)
/// reads green, and an exactly-on-budget figure reads the neutral ink colour. Bound with two flags
/// (IsOver, IsUnder) so the variance amount stands out at a glance in the report grid.
/// </summary>
public sealed class VarianceToBrushConverter : IMultiValueConverter
{
    public static readonly VarianceToBrushConverter Instance = new();

    private static readonly IBrush Over = new SolidColorBrush(Color.Parse("#B00020"));   // over budget → red
    private static readonly IBrush Under = new SolidColorBrush(Color.Parse("#2E7D32"));  // under budget → green
    private static readonly IBrush OnTarget = new SolidColorBrush(Color.Parse("#1A1A1A"));

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOver = values.Count > 0 && values[0] is true;
        var isUnder = values.Count > 1 && values[1] is true;
        if (isOver) return Over;
        if (isUnder) return Under;
        return OnTarget;
    }
}

/// <summary>
/// Maps a <see cref="Apex.Ledger.Domain.BankTransactionType"/> to its label ("Cheque/DD" / "NEFT" /
/// "RTGS" / "Cash" / "Other") for the "Transaction Type" combo in the bank-allocation sub-panel.
/// </summary>
public sealed class BankTxTypeLabelConverter : IValueConverter
{
    public static readonly BankTxTypeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Apex.Ledger.Domain.BankTransactionType t
            ? t switch
            {
                Apex.Ledger.Domain.BankTransactionType.ChequeOrDD => "Cheque/DD",
                Apex.Ledger.Domain.BankTransactionType.NEFT => "NEFT",
                Apex.Ledger.Domain.BankTransactionType.RTGS => "RTGS",
                Apex.Ledger.Domain.BankTransactionType.Cash => "Cash",
                Apex.Ledger.Domain.BankTransactionType.Other => "Other",
                _ => t.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a statement-import result row by its <see cref="Apex.Desktop.ViewModels.StatementRowKind"/>:
/// a pale-green tint for a matched row, a pale-amber tint for an unmatched statement row, a pale-red tint
/// for an unmatched book transaction. Bound with the row's <c>Kind</c>.
/// </summary>
public sealed class StatementRowBrushConverter : IValueConverter
{
    public static readonly StatementRowBrushConverter Instance = new();

    private static readonly IBrush Matched = new SolidColorBrush(Color.Parse("#EAF7EA"));
    private static readonly IBrush UnmatchedStatement = new SolidColorBrush(Color.Parse("#FBF3DD"));
    private static readonly IBrush UnmatchedBook = new SolidColorBrush(Color.Parse("#FBE9E9"));
    private static readonly IBrush None = Brushes.White;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Apex.Desktop.ViewModels.StatementRowKind k
            ? k switch
            {
                Apex.Desktop.ViewModels.StatementRowKind.Matched => Matched,
                Apex.Desktop.ViewModels.StatementRowKind.UnmatchedStatement => UnmatchedStatement,
                Apex.Desktop.ViewModels.StatementRowKind.UnmatchedBook => UnmatchedBook,
                _ => None,
            }
            : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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

/// <summary>Maps a bool (IsNumeric) to a text alignment: Right for a numeric/money column, Left otherwise.
/// Used by the Phase-8 payroll matrix so each cell aligns to its column without a per-cell template switch.</summary>
public sealed class BoolToTextAlignmentConverter : IValueConverter
{
    public static readonly BoolToTextAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TextAlignment.Right : TextAlignment.Left;

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
/// Maps a batch-age row's <c>IsExpired</c> flag to its foreground (Phase 6 Cluster 1; RQ-8): a distinct
/// alert-red for an already past-expiry batch so it reads apart from the merely near-expiry rows; the neutral
/// ink otherwise.
/// </summary>
public sealed class ExpiryRowToBrushConverter : IValueConverter
{
    public static readonly ExpiryRowToBrushConverter Instance = new();

    private static readonly IBrush Expired = new SolidColorBrush(Color.Parse("#B00020"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1A1A1A"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Expired : Ink;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a batch-allocation line's <c>IsExpired</c> flag to its non-blocking-warning foreground (Phase 6
/// Cluster 1; RQ-7): alert-red for an already-expired batch, amber for a merely near-expiry one.
/// </summary>
public sealed class ExpiryWarningToBrushConverter : IValueConverter
{
    public static readonly ExpiryWarningToBrushConverter Instance = new();

    private static readonly IBrush Expired = new SolidColorBrush(Color.Parse("#B00020"));
    private static readonly IBrush NearExpiry = new SolidColorBrush(Color.Parse("#B5730E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Expired : NearExpiry;

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
