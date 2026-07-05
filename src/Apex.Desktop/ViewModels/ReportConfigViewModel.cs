using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The F12 report Configuration panel (RQ-1 / RQ-2 / RQ-6), hosted as its own cascading Miller-column
/// page column to the right of the report it configures — never a stacked overlay. It edits the display
/// options of the live <see cref="ReportsViewModel"/> and, on <see cref="Apply"/>, pushes them back so the
/// projection re-runs through the engine's <see cref="ReportOptions"/> (figures stay engine-computed and
/// are never mutated here).
///
/// <para>It carries the RQ-1 period/as-of fields (F2 sets the as-of; Alt+F2 sets the window), the RQ-2
/// detailed↔summary flag, and the RQ-6 hide-zero / percentages / closing-stock-basis flags, so the whole
/// report-parameter surface is keyboard-editable in one column. Dates use the app-wide <c>dd-MMM-yyyy</c>
/// text convention (a <see cref="Avalonia.Controls.TextBox"/> bound to a string), parsed on
/// <see cref="Apply"/>; an unparseable date leaves that value unchanged. The panel opens seeded from the
/// report's current state, so opening → applying with no edits is a no-op that preserves the default
/// behaviour exactly.</para>
/// </summary>
public sealed partial class ReportConfigViewModel : ViewModelBase
{
    private const string DateFormat = "dd-MMM-yyyy";

    private readonly ReportsViewModel _report;

    /// <summary>The column title / heading for the config panel.</summary>
    public string Title => "Configure — F12";

    /// <summary>The report this panel configures (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    // ---- RQ-1: period / as-of ----

    /// <summary>The as-of date (F2), as <c>dd-MMM-yyyy</c> text; applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private string _asOfText = string.Empty;

    /// <summary>Whether an explicit reporting window is used (Alt+F2). When false, books-begin → as-of.</summary>
    [ObservableProperty] private bool _usePeriod;

    /// <summary>The window start as <c>dd-MMM-yyyy</c> text (only applied when <see cref="UsePeriod"/>).</summary>
    [ObservableProperty] private string _periodFromText = string.Empty;

    /// <summary>The window end as <c>dd-MMM-yyyy</c> text (only applied when <see cref="UsePeriod"/>).</summary>
    [ObservableProperty] private string _periodToText = string.Empty;

    // ---- RQ-2: detailed / summary ----

    /// <summary>Detailed (ledger/item-level) vs summary (group roll-up). Ignored on reports that do not roll up.</summary>
    [ObservableProperty] private bool _detailed;

    /// <summary>True when the configured report supports the detailed↔summary toggle (TB / BS / P&amp;L / Stock Summary).</summary>
    public bool SupportsDetailToggle => _report.SupportsDetailToggle;

    // ---- RQ-6: F12 display config ----

    /// <summary>Hide rows whose balance is exactly zero.</summary>
    [ObservableProperty] private bool _hideZeroBalances;

    /// <summary>Show each row's percentage of its section/column total.</summary>
    [ObservableProperty] private bool _showPercentages;

    /// <summary>The closing-stock valuation basis passed through to the P&amp;L / Balance-Sheet build.</summary>
    [ObservableProperty] private ClosingStockOption? _selectedClosingStock;

    /// <summary>The closing-stock basis options offered by the panel.</summary>
    public ObservableCollection<ClosingStockOption> ClosingStockOptions { get; } = new();

    /// <summary>A short status line shown after applying (feedback that the projection re-ran).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public ReportConfigViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));

        foreach (var mode in new[] { ClosingStockMode.AsPostedLedger, ClosingStockMode.InventoryDerived })
            ClosingStockOptions.Add(new ClosingStockOption(mode));

        SeedFromReport();
    }

    /// <summary>Seeds every field from the report's current state (so a no-op apply changes nothing).</summary>
    private void SeedFromReport()
    {
        AsOfText = Fmt(_report.AsOf);

        if (_report.Period is { } p)
        {
            UsePeriod = true;
            PeriodFromText = Fmt(p.From);
            PeriodToText = Fmt(p.To);
        }
        else
        {
            UsePeriod = false;
            PeriodFromText = Fmt(_report.AsOf);
            PeriodToText = Fmt(_report.AsOf);
        }

        Detailed = _report.Detailed;
        HideZeroBalances = _report.HideZeroBalances;
        ShowPercentages = _report.ShowPercentages;

        foreach (var opt in ClosingStockOptions)
            if (opt.Mode == _report.ClosingStock)
            {
                SelectedClosingStock = opt;
                break;
            }
    }

    /// <summary>
    /// Applies the panel's settings to the live report and re-runs its projection through the engine
    /// (RQ-1/2/6). The period/as-of choice is set first (it clears/sets the window), then the display
    /// flags in one <see cref="ReportsViewModel.ApplyConfiguration"/> call. The figures stay engine-computed
    /// and are never mutated here. Unparseable dates leave that value unchanged.
    /// </summary>
    public void Apply()
    {
        // RQ-1 (Defect D): validate the period/as-of FIRST and refuse to claim success on a rejected window.
        // An unparseable date or an inverted window (From > To, which SetPeriod silently ignores) must NOT
        // report "Applied" — that falsely tells the user the report recomputed against their dates. Validate
        // before mutating anything and surface a clear error, leaving the report's period untouched.
        if (UsePeriod)
        {
            if (!TryParse(PeriodFromText, out var from) || !TryParse(PeriodToText, out var to))
            {
                Status = "Unrecognized date. Use the dd-MMM-yyyy format (e.g. 01-Apr-2020).";
                return;
            }
            if (from > to)
            {
                Status = "Invalid period: From must be on/before To.";
                return;
            }
            _report.SetPeriod(from, to);
        }
        else
        {
            if (!TryParse(AsOfText, out var asOf))
            {
                Status = "Unrecognized date. Use the dd-MMM-yyyy format (e.g. 01-Apr-2020).";
                return;
            }
            _report.SetAsOf(asOf);
        }

        // RQ-2: align detailed/summary with the panel (only meaningful where the report rolls up).
        if (SupportsDetailToggle && _report.Detailed != Detailed)
            _report.ToggleDetailed();

        // RQ-6: hide-zero / percentages / closing-stock basis (a single re-projection).
        var closingStock = SelectedClosingStock?.Mode ?? _report.ClosingStock;
        _report.ApplyConfiguration(HideZeroBalances, ShowPercentages, closingStock);

        Status = "Applied — report recomputed.";
    }

    private static string Fmt(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), DateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>One closing-stock valuation-basis option for the F12 config combo (label + engine mode).</summary>
public sealed class ClosingStockOption
{
    public ClosingStockMode Mode { get; }
    public string Display { get; }

    public ClosingStockOption(ClosingStockMode mode)
    {
        Mode = mode;
        Display = mode switch
        {
            ClosingStockMode.AsPostedLedger => "As posted (Stock-in-Hand ledger)",
            ClosingStockMode.InventoryDerived => "Inventory-derived",
            _ => mode.ToString(),
        };
    }
}
