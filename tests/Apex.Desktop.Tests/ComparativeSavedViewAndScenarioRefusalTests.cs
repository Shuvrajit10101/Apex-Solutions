using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 11.14 / 11.17 — the two gaps an adversarial review of the comparative widening found and closed:
/// the <b>saved-view</b> column-spec site that did not go through <c>ColumnOptions</c>, and the Alt+C panel
/// blaming the PERIOD for a SCENARIO refusal.
///
/// <para>🔴 <b>Why the saved view is its own site.</b> <c>ReportsViewModel.ColumnOptions</c> was introduced to
/// stop any column spec handing the engine a scenario on a scenario-blind kind, and its summary names three
/// call sites — the base spec, Alt+C, and the Alt+N axes. <see cref="ReportsViewModel.ApplySavedView"/> is a
/// FOURTH, and it is the only one that can carry a scenario in from outside the session: it rebuilds
/// <c>_options</c> from the view's stored <c>ScenarioName</c> with no supports-scenario gate, and restores each
/// column's own <c>ScenarioName</c> with none either. On a Cash Flow / Funds Flow view that also carries
/// columns, either one reached the engine's scenario refusal and threw <see cref="ArgumentException"/> out of
/// the Alt+K handler, which the shell does not catch.</para>
/// </summary>
public sealed class ComparativeSavedViewAndScenarioRefusalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ComparativeSavedViewAndScenarioRefusalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCmpSaved_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a temp dir that will not delete must not fail a test */ }
    }

    private Company Reloaded(string tag)
    {
        var c = DemoData.BuildRobert("Sv" + tag + Guid.NewGuid().ToString("N")[..6]);
        c.AddScenario(new Scenario(Guid.NewGuid(), "Optimistic"));
        _storage.Save(c);
        return _storage.Load(_storage.ListCompanies().Single(e => e.Name == c.Name));
    }

    /// <summary>A saved view whose stored SCENARIO NAME lands on a scenario-blind kind must not throw.</summary>
    [Theory]
    [InlineData(ReportKind.CashFlow, "CashFlow")]
    [InlineData(ReportKind.FundsFlow, "FundsFlow")]
    public void A_saved_view_carrying_a_scenario_and_columns_applies_to_a_flow_statement_without_throwing(
        ReportKind kind, string token)
    {
        var book = Reloaded("A");
        var vm = new ReportsViewModel(book, kind);

        var view = new SavedReportView
        {
            ReportKind = token,
            AsOfDate = new DateOnly(2021, 3, 31),
            PeriodFrom = new DateOnly(2020, 4, 1),
            PeriodTo = new DateOnly(2021, 3, 31),
            ScenarioName = "Optimistic",
            ComparativeColumns = new List<SavedComparativeColumn>
            {
                new() { Label = "Q1", PeriodFrom = new DateOnly(2020, 4, 1), PeriodTo = new DateOnly(2020, 6, 30) },
            },
        };

        var thrown = Record.Exception(() => vm.ApplySavedView(view));
        Assert.Null(thrown);
        // And it really applied: the base column plus the one saved column are on screen.
        Assert.True(vm.IsComparative);
        Assert.Equal(2, vm.ComparativeColumns.Count);
        Assert.Contains(vm.ComparativeColumns, c => c.Label == "Q1");
        Assert.NotEmpty(vm.ComparativeRows);
    }

    /// <summary>The same, with the scenario stored on the COLUMN rather than on the view.</summary>
    [Theory]
    [InlineData(ReportKind.CashFlow, "CashFlow")]
    [InlineData(ReportKind.FundsFlow, "FundsFlow")]
    public void A_saved_view_whose_COLUMN_carries_a_scenario_applies_to_a_flow_statement_without_throwing(
        ReportKind kind, string token)
    {
        var book = Reloaded("B");
        var vm = new ReportsViewModel(book, kind);

        var view = new SavedReportView
        {
            ReportKind = token,
            AsOfDate = new DateOnly(2021, 3, 31),
            ComparativeColumns = new List<SavedComparativeColumn>
            {
                new()
                {
                    Label = "What-if",
                    PeriodFrom = new DateOnly(2020, 4, 1),
                    PeriodTo = new DateOnly(2020, 6, 30),
                    ScenarioName = "Optimistic",
                },
            },
        };

        var thrown = Record.Exception(() => vm.ApplySavedView(view));
        Assert.Null(thrown);
        Assert.True(vm.IsComparative);
        Assert.Equal(2, vm.ComparativeColumns.Count);
    }

    /// <summary>A comparative kind that DOES take scenarios keeps honouring a saved scenario column.</summary>
    [Fact]
    public void A_saved_scenario_column_is_still_honoured_on_a_kind_that_supports_scenarios()
    {
        var book = Reloaded("C");
        var vm = new ReportsViewModel(book, ReportKind.TrialBalance);

        var view = new SavedReportView
        {
            ReportKind = "TrialBalance",
            AsOfDate = new DateOnly(2021, 3, 31),
            ComparativeColumns = new List<SavedComparativeColumn>
            {
                new() { Label = "What-if", ScenarioName = "Optimistic" },
            },
        };

        var thrown = Record.Exception(() => vm.ApplySavedView(view));
        Assert.Null(thrown);
        Assert.True(vm.SupportsComparativeScenarioColumns);
        Assert.Equal(2, vm.ComparativeColumns.Count);
        Assert.Contains(vm.ComparativeColumns, c => c.Label == "What-if");
    }

    /// <summary>
    /// The Alt+C panel must name the field it refused. It used to answer a SCENARIO refusal with
    /// "Could not add the column (check the period)." on a panel whose period was perfectly valid.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    public void The_Alt_C_panel_names_the_scenario_when_it_refuses_a_scenario_column(ReportKind kind)
    {
        var book = Reloaded("D");
        var vm = new ReportsViewModel(book, kind);

        var panel = new AddComparisonColumnViewModel(vm);
        Assert.False(panel.SupportsScenarioColumns);
        panel.SelectedScenario = panel.Scenarios.First(s => s.Scenario is not null);
        panel.Apply();

        Assert.False(vm.IsComparative);
        Assert.Contains("scenario", panel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("check the period", panel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", panel.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And the panel still adds a plain PERIOD column on the same kinds — the refusal is narrow.</summary>
    [Theory]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.FundsFlow)]
    public void The_Alt_C_panel_still_adds_a_period_column_on_a_scenario_blind_kind(ReportKind kind)
    {
        var book = Reloaded("E");
        var vm = new ReportsViewModel(book, kind);

        var panel = new AddComparisonColumnViewModel(vm)
        {
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "30-Jun-2020",
            Label = "Q1",
        };
        panel.Apply();

        Assert.True(vm.IsComparative);
        Assert.Contains(vm.ComparativeColumns, c => c.Label == "Q1");
        Assert.NotEmpty(vm.ComparativeRows);
    }

    /// <summary>A scenario-capable kind's Alt+C panel is unchanged: it still offers and applies a scenario.</summary>
    [Fact]
    public void The_Alt_C_panel_still_applies_a_scenario_column_on_a_scenario_capable_kind()
    {
        var book = Reloaded("F");
        var vm = new ReportsViewModel(book, ReportKind.TrialBalance);

        var panel = new AddComparisonColumnViewModel(vm);
        Assert.True(panel.SupportsScenarioColumns);
        panel.SelectedScenario = panel.Scenarios.First(s => s.Scenario is not null);
        panel.Apply();

        Assert.True(vm.IsComparative);
        Assert.Contains("Added", panel.Status, StringComparison.OrdinalIgnoreCase);
    }
}
