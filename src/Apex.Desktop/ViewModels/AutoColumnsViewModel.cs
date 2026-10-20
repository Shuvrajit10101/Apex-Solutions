using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The auto-column axis a <see cref="AutoColumnsViewModel"/> generates (RQ-4 Alt+N).</summary>
public enum AutoColumnAxis
{
    /// <summary>One column per period bucket over the report's current period, at the chosen periodicity.</summary>
    ByPeriod,

    /// <summary>One column per scenario defined on the company (alongside the base actual column).</summary>
    ByScenario,
}

/// <summary>One selectable periodicity on the Alt+N panel: the engine value plus the caption shown for it.</summary>
public sealed record PeriodicityOption(ColumnPeriodicity Periodicity, string Caption)
{
    /// <summary>The caption is what the list shows, so it is also what a bound <c>ToString</c> would print.</summary>
    public override string ToString() => Caption;
}

/// <summary>
/// The Alt+N "Auto Columns" chooser (RQ-4), hosted as its own cascading Miller-column page column to the RIGHT of
/// the report — never a stacked overlay, mirroring the other report panels. It picks an AXIS and, on
/// <see cref="Apply"/>, generates the whole column set on the live <see cref="ReportsViewModel"/> via
/// <see cref="ReportsViewModel.AutoColumnsByMonth"/> / <see cref="ReportsViewModel.AutoColumnsByScenario"/>, which
/// re-render the report as a horizontal multi-column comparative grid.
///
/// <para>Two axes are offered: <b>By period</b> (one column per bucket across the current period, at a chosen
/// periodicity — the vendor's own list: daily, weekly, fortnightly, monthly, quarterly, plus yearly) and
/// <b>By scenario</b> (one column per defined scenario). Each axis is rejected with a status rather than a
/// false success when it cannot be generated: no scenarios defined, a report that cannot be shown
/// comparatively, a report whose statement is scenario-blind, or a window that yields no buckets.</para>
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

    /// <summary>
    /// True when the current report can carry a SCENARIO column at all. Cash Flow and Funds Flow are built from
    /// unscenarioed ledger movements, so the scenario axis is withheld there rather than generating columns that
    /// silently reproduce the actual books under a scenario's name.
    /// </summary>
    public bool SupportsScenarioColumns => _report.SupportsComparativeScenarioColumns;

    /// <summary>The periodicities the By-period axis offers, in the vendor's own order of granularity.</summary>
    public ObservableCollection<PeriodicityOption> Periodicities { get; } = new(
        new List<PeriodicityOption>
        {
            new(ColumnPeriodicity.Daily, "Daily"),
            new(ColumnPeriodicity.Weekly, "Weekly"),
            new(ColumnPeriodicity.Fortnightly, "Fortnightly"),
            new(ColumnPeriodicity.Monthly, "Monthly"),
            new(ColumnPeriodicity.Quarterly, "Quarterly"),
            new(ColumnPeriodicity.Yearly, "Yearly"),
        });

    /// <summary>The chosen periodicity for the By-period axis. Defaults to Monthly, the long-shipped axis.</summary>
    [ObservableProperty] private PeriodicityOption? _selectedPeriodicity;

    /// <summary>Selects the period axis (radio-style; the two axis bools are kept mutually exclusive).</summary>
    [ObservableProperty] private bool _byPeriod = true;

    /// <summary>Selects the scenario axis (radio-style; the two axis bools are kept mutually exclusive).</summary>
    [ObservableProperty] private bool _byScenario;

    /// <summary>A short status line shown after applying (feedback / validation error).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public AutoColumnsViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        // Monthly is the axis this panel has always generated; it stays the default so the existing gesture
        // (open Alt+N, press apply) produces exactly the column set it produced before the list existed.
        SelectedPeriodicity = Periodicities.Single(p => p.Periodicity == ColumnPeriodicity.Monthly);
    }

    partial void OnByPeriodChanged(bool value)
    {
        if (value) ByScenario = false;
        else if (!ByScenario) ByScenario = true; // never leave both off
    }

    partial void OnByScenarioChanged(bool value)
    {
        if (value) ByPeriod = false;
        else if (!ByPeriod) ByPeriod = true; // never leave both off
    }

    /// <summary>The chosen axis (defaults to <see cref="AutoColumnAxis.ByPeriod"/>).</summary>
    public AutoColumnAxis Axis => ByScenario ? AutoColumnAxis.ByScenario : AutoColumnAxis.ByPeriod;

    /// <summary>The periodicity the By-period axis will use (Monthly when nothing is selected).</summary>
    public ColumnPeriodicity Periodicity => SelectedPeriodicity?.Periodicity ?? ColumnPeriodicity.Monthly;

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
            if (!SupportsScenarioColumns)
            {
                // Say WHY, not just no. This report's figures are built from unscenarioed movements, so there is
                // no scenario answer to show — a column would reproduce the actuals under the scenario's name.
                Status = "This report is built from actual movements only — it has no scenario view. Use By period.";
                return;
            }
            if (!_report.AutoColumnsByScenario())
            {
                Status = "No scenarios defined — create a scenario first, or use By period.";
                return;
            }
            Status = "Generated one column per scenario.";
            return;
        }

        var periodicity = Periodicity;
        if (!_report.AutoColumnsByPeriodicity(periodicity))
        {
            Status = "The current period yields no columns at this periodicity.";
            return;
        }

        var caption = SelectedPeriodicity?.Caption ?? "Monthly";
        Status = $"Generated {caption.ToLowerInvariant()} columns ({_report.ExtraColumnCount}).";
    }
}
