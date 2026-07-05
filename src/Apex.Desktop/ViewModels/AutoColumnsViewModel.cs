using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The auto-column axis a <see cref="AutoColumnsViewModel"/> generates (RQ-4 Alt+N).</summary>
public enum AutoColumnAxis
{
    /// <summary>One column per calendar month over the report's current period.</summary>
    ByMonth,

    /// <summary>One column per scenario defined on the company (alongside the base actual column).</summary>
    ByScenario,
}

/// <summary>
/// The Alt+N "Auto Columns" chooser (RQ-4), hosted as its own cascading Miller-column page column to the RIGHT of
/// the report — never a stacked overlay, mirroring the other report panels. It picks an AXIS and, on
/// <see cref="Apply"/>, generates the whole column set on the live <see cref="ReportsViewModel"/> via
/// <see cref="ReportsViewModel.AutoColumnsByMonth"/> / <see cref="ReportsViewModel.AutoColumnsByScenario"/>, which
/// re-render the report as a horizontal multi-column comparative grid.
///
/// <para>Two axes are offered: <b>By month</b> (one column per calendar month across the current period) and
/// <b>By scenario</b> (one column per defined scenario). The scenario axis is rejected with a status when the
/// company has no scenarios, so the panel never falsely claims it generated columns.</para>
/// </summary>
public sealed partial class AutoColumnsViewModel : ViewModelBase
{
    private readonly ReportsViewModel _report;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Auto Columns — Alt+N";

    /// <summary>The report this panel generates columns for (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    /// <summary>True when the current report kind can be shown comparatively (else the panel is inert).</summary>
    public bool SupportsComparative => _report.SupportsComparative;

    /// <summary>True when the company has at least one scenario (drives whether the By-scenario axis is offered).</summary>
    public bool HasScenarios => _report.Scenarios.Count > 1; // index 0 is the "Actual" option

    /// <summary>Selects the monthly axis (radio-style; the two axis bools are kept mutually exclusive).</summary>
    [ObservableProperty] private bool _byMonth = true;

    /// <summary>Selects the scenario axis (radio-style; the two axis bools are kept mutually exclusive).</summary>
    [ObservableProperty] private bool _byScenario;

    /// <summary>A short status line shown after applying (feedback / validation error).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public AutoColumnsViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    partial void OnByMonthChanged(bool value)
    {
        if (value) ByScenario = false;
        else if (!ByScenario) ByScenario = true; // never leave both off
    }

    partial void OnByScenarioChanged(bool value)
    {
        if (value) ByMonth = false;
        else if (!ByMonth) ByMonth = true; // never leave both off
    }

    /// <summary>The chosen axis (defaults to <see cref="AutoColumnAxis.ByMonth"/>).</summary>
    public AutoColumnAxis Axis => ByScenario ? AutoColumnAxis.ByScenario : AutoColumnAxis.ByMonth;

    /// <summary>
    /// Generates the chosen auto-column axis on the live report (RQ-4). By-scenario is rejected with a status when
    /// the company has no scenarios (nothing added, no false success). On success the report re-renders as the
    /// multi-column comparative grid and the status confirms it.
    /// </summary>
    public void Apply()
    {
        if (!SupportsComparative)
        {
            Status = "This report cannot be shown comparatively.";
            return;
        }

        if (Axis == AutoColumnAxis.ByScenario)
        {
            if (!_report.AutoColumnsByScenario())
            {
                Status = "No scenarios defined — create a scenario first, or use By month.";
                return;
            }
            Status = "Generated one column per scenario.";
            return;
        }

        _report.AutoColumnsByMonth();
        Status = "Generated one column per month.";
    }
}
