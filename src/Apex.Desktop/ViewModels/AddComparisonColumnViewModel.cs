using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The Alt+C "Add Comparison Column" panel (RQ-4), hosted as its own cascading Miller-column page column to the
/// RIGHT of the report it compares — never a stacked overlay, mirroring the F12 <see cref="ReportConfigViewModel"/>
/// and Alt+F12 <see cref="ReportSortFilterViewModel"/> panels. It gathers ONE extra comparison column — an
/// optional period window [From, To] and/or a scenario, plus a free-text label — and on <see cref="Apply"/> pushes
/// it into the live <see cref="ReportsViewModel"/> via <see cref="ReportsViewModel.AddComparisonColumn"/>, which
/// re-renders the report as a multi-column comparative grid.
///
/// <para>Dates use the app-wide <c>dd-MMM-yyyy</c> text convention, parsed on <see cref="Apply"/>; an unparseable
/// or inverted window is rejected with a validation status (the panel never falsely claims success). Leaving the
/// window unchecked adds an as-of (whole-books) column; leaving the scenario at "Actual" adds an actual-books
/// column. A blank label falls back to a period/scenario-derived label chosen by the report.</para>
/// </summary>
public sealed partial class AddComparisonColumnViewModel : ViewModelBase
{
    private const string DateFormat = "dd-MMM-yyyy";

    private readonly ReportsViewModel _report;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Add Column — Alt+C";

    /// <summary>The report this panel adds a column to (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    /// <summary>True when the current report kind can be shown comparatively (else the panel is inert).</summary>
    public bool SupportsComparative => _report.SupportsComparative;

    // ---- label ----

    /// <summary>The display label for the new column; blank falls back to a period/scenario-derived label.</summary>
    [ObservableProperty] private string _label = string.Empty;

    // ---- period window ----

    /// <summary>Whether the new column uses an explicit period window (else it is an as-of / whole-books column).</summary>
    [ObservableProperty] private bool _usePeriod = true;

    /// <summary>The window start as <c>dd-MMM-yyyy</c> text (only applied when <see cref="UsePeriod"/>).</summary>
    [ObservableProperty] private string _periodFromText = string.Empty;

    /// <summary>The window end as <c>dd-MMM-yyyy</c> text (only applied when <see cref="UsePeriod"/>).</summary>
    [ObservableProperty] private string _periodToText = string.Empty;

    // ---- scenario ----

    /// <summary>The scenario picker options: "Actual (no scenario)" first, then each scenario on the company.</summary>
    public ObservableCollection<ScenarioOption> Scenarios { get; } = new();

    /// <summary>The chosen scenario for the new column (null scenario = actual books).</summary>
    [ObservableProperty] private ScenarioOption? _selectedScenario;

    /// <summary>A short status line shown after applying (feedback / validation error).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public AddComparisonColumnViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));

        Scenarios.Add(ScenarioOption.Actual);
        foreach (var s in report.Scenarios)
            if (s.Scenario is not null) Scenarios.Add(s);
        SelectedScenario = Scenarios[0];

        // Seed the window from the report's current period (or its as-of when none is set), so opening the panel
        // shows the current window and a single Apply adds a like-for-like column the user can then narrow.
        if (report.Period is { } p)
        {
            UsePeriod = true;
            PeriodFromText = Fmt(p.From);
            PeriodToText = Fmt(p.To);
        }
        else
        {
            UsePeriod = false;
            PeriodFromText = Fmt(report.AsOf);
            PeriodToText = Fmt(report.AsOf);
        }
    }

    /// <summary>
    /// Validates the inputs and, on success, appends the comparison column to the live report (RQ-4). The period
    /// window is validated FIRST: a non-blank but unparseable From/To, or an inverted [From &gt; To] window, is
    /// rejected with a clear status and NOTHING is added (no false "added"). On success the report re-renders as a
    /// multi-column comparative grid and the status confirms it.
    /// </summary>
    public void Apply()
    {
        if (!SupportsComparative)
        {
            Status = "This report cannot be shown comparatively.";
            return;
        }

        PeriodRange? period = null;
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
            period = new PeriodRange(from, to);
        }

        var scenario = SelectedScenario?.Scenario;
        var added = _report.AddComparisonColumn(Label, period, scenario);
        if (!added)
        {
            Status = "Could not add the column (check the period).";
            return;
        }

        Status = "Added — report now shows a comparison column.";
    }

    private static string Fmt(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), DateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
