using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-7 (RQ-8 Save View) wired into <see cref="ReportsViewModel"/>
/// (<c>ToSavedView</c> / <c>ApplySavedView</c> + the stable kind-token map), the <see cref="SaveViewViewModel"/>
/// / <see cref="SavedViewsViewModel"/> panels, and the <see cref="CompanyStorage"/> per-company saved-view store.
///
/// <para>The load-bearing guarantee: a view captures the CONFIGURATION TUPLE ONLY, and applying it into a FRESH
/// report reproduces the exact same on-screen figures (ER-9 — the report is recomputed, never loaded). Tests
/// drive over the embedded "Robert" demo; a company is saved to disk first so the per-company <c>.db</c> exists
/// (saved views live in that same file, giving per-company isolation for free).</para>
/// </summary>
public sealed class SavedViewsViewModelTests : IDisposable
{
    private static readonly DateOnly BooksBegin = new(2020, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public SavedViewsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSavedViewTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    /// <summary>A Robert demo persisted to its own .db (so its saved-views table is reachable).</summary>
    private Company Robert()
    {
        var c = DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));
        _storage.Save(c);
        return c;
    }

    private Company RobertWithScenario(out Scenario scenario)
    {
        var c = DemoData.BuildRobert("RobertS " + Guid.NewGuid().ToString("N"));
        scenario = new Scenario(Guid.NewGuid(), "Optimistic");
        c.AddScenario(scenario);
        _storage.Save(c);
        return c;
    }

    /// <summary>A figure-only snapshot of the single-column report's rows (the visible numbers, not identity).</summary>
    private static List<string> RowSnapshot(ReportsViewModel vm) =>
        vm.Rows.Select(r =>
            $"{r.Particulars}|{r.Secondary}|Dr={r.Debit}|Cr={r.Credit}|Amt={r.Amount}|" +
            $"{r.Col1}|{r.Col2}|{r.Col3}|{r.Col4}|{r.Col5}|{r.Col6}|H={r.IsHeader}|T={r.IsTotal}")
        .ToList();

    // =============================================================== kind-token map (stable, round-trips)

    [Fact]
    public void Kind_tokens_round_trip_for_every_report_kind()
    {
        foreach (ReportKind kind in Enum.GetValues(typeof(ReportKind)))
        {
            var token = ReportsViewModel.TokenFor(kind);
            Assert.False(string.IsNullOrEmpty(token));
            Assert.Equal(kind, ReportsViewModel.KindFor(token));
        }
        Assert.Null(ReportsViewModel.KindFor("NoSuchReportToken")); // unknown token → null (skip, no crash)
    }

    // =============================================================== ToSavedView captures the config tuple

    [Fact]
    public void ToSavedView_captures_the_full_configuration_tuple()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        vm.SetPeriod(BooksBegin, new DateOnly(2020, 4, 14));
        vm.ToggleDetailed(); // → summary
        vm.ApplyConfiguration(hideZero: true, showPercentages: true, ClosingStockMode.InventoryDerived);
        vm.ApplySortFilter(ReportSortFilter.None
            .WithSort(ReportSortKey.Amount, ascending: false)
            .WithRange(Money.FromRupees(100m), Money.FromRupees(9000m))
            .WithNameContains("cash"));

        var view = vm.ToSavedView();

        Assert.Equal("TrialBalance", view.ReportKind);
        Assert.Equal(BooksBegin, view.PeriodFrom);
        Assert.Equal(new DateOnly(2020, 4, 14), view.PeriodTo);
        Assert.False(view.Detailed);               // summary
        Assert.True(view.HideZeroBalances);
        Assert.True(view.ShowPercentages);
        Assert.Equal(ClosingStockMode.InventoryDerived, view.ClosingStock);
        Assert.Equal(ReportSortKey.Amount, view.SortKey);
        Assert.False(view.SortAscending);
        Assert.Equal(100m, view.FilterMinRupees);
        Assert.Equal(9000m, view.FilterMaxRupees);
        Assert.Equal("cash", view.FilterNameContains);
        Assert.Null(view.ComparativeColumns);      // no comparative columns added
    }

    // =============================================================== save → open reproduces the exact figures

    [Fact]
    public void Saving_then_opening_into_a_fresh_report_reproduces_the_exact_config_and_rows()
    {
        var company = Robert();

        // Configure a report by hand, snapshot its rows, then save the view.
        var configured = new ReportsViewModel(company, ReportKind.TrialBalance);
        configured.SetPeriod(BooksBegin, new DateOnly(2020, 4, 20));
        configured.ApplyConfiguration(hideZero: true, showPercentages: false, ClosingStockMode.AsPostedLedger);
        configured.ApplySortFilter(ReportSortFilter.None.WithSort(ReportSortKey.Name, ascending: true));
        var expectedRows = RowSnapshot(configured);

        _storage.SaveView(company, "Q1 view", configured.ToSavedView());

        // Open a FRESH report of the same kind and apply the loaded view — it must recompute to the SAME figures.
        var loaded = _storage.GetView(company, "Q1 view");
        Assert.NotNull(loaded);

        var reopened = new ReportsViewModel(company, ReportKind.TrialBalance);
        reopened.ApplySavedView(loaded!);

        // Config re-applied.
        Assert.Equal(new PeriodRange(BooksBegin, new DateOnly(2020, 4, 20)), reopened.Period);
        Assert.True(reopened.HideZeroBalances);
        Assert.Equal(ReportSortKey.Name, reopened.SortFilter.SortKey);
        // Figures identical — proof the view carried config only and the report recomputed (ER-9).
        Assert.Equal(expectedRows, RowSnapshot(reopened));
    }

    [Fact]
    public void Opening_a_view_reproduces_comparative_columns()
    {
        var company = Robert();
        var configured = new ReportsViewModel(company, ReportKind.TrialBalance);
        configured.AddComparisonColumn("Half month", new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), null);
        Assert.True(configured.IsComparative);

        _storage.SaveView(company, "Compare", configured.ToSavedView());
        var view = _storage.GetView(company, "Compare")!;
        Assert.NotNull(view.ComparativeColumns);
        Assert.Single(view.ComparativeColumns!);

        var reopened = new ReportsViewModel(company, ReportKind.TrialBalance);
        reopened.ApplySavedView(view);

        Assert.True(reopened.IsComparative);
        Assert.Equal(1, reopened.ExtraColumnCount);
        Assert.Equal(2, reopened.ComparativeColumns.Count);        // base + the saved column
        Assert.Equal("Half month", reopened.ComparativeColumns[1].Label);
    }

    [Fact]
    public void Opening_a_view_rebinds_the_scenario_by_name()
    {
        var company = RobertWithScenario(out var scenario);
        var configured = new ReportsViewModel(company, ReportKind.TrialBalance);
        configured.SelectedScenario = configured.Scenarios.First(o => o.Scenario?.Name == "Optimistic");

        var view = configured.ToSavedView();
        Assert.Equal("Optimistic", view.ScenarioName); // a NAME, not an id/object

        var reopened = new ReportsViewModel(company, ReportKind.TrialBalance);
        reopened.ApplySavedView(view);
        Assert.Equal(scenario, reopened.SelectedScenario?.Scenario); // re-bound to the live scenario of that name
    }

    // =============================================================== SavedViewsViewModel: list / open / delete

    [Fact]
    public void SavedViews_panel_lists_the_companys_views_ordered_by_name()
    {
        var company = Robert();
        _storage.SaveView(company, "Zeta", new ReportsViewModel(company, ReportKind.BalanceSheet).ToSavedView());
        _storage.SaveView(company, "Alpha", new ReportsViewModel(company, ReportKind.TrialBalance).ToSavedView());

        var panel = new SavedViewsViewModel(company, _storage);

        Assert.Equal(2, panel.Views.Count);
        Assert.Equal("Alpha", panel.Views[0].Name);  // ordered by name (case-insensitive)
        Assert.Equal("Zeta", panel.Views[1].Name);
        Assert.Equal("TrialBalance", panel.Views[0].KindLabel);
    }

    [Fact]
    public void SavedViews_panel_open_raises_the_view_for_the_shell_to_apply()
    {
        var company = Robert();
        _storage.SaveView(company, "MyBS",
            new ReportsViewModel(company, ReportKind.BalanceSheet).ToSavedView());

        var panel = new SavedViewsViewModel(company, _storage);
        SavedReportView? opened = null;
        panel.OpenRequested += v => opened = v;

        panel.Selected = panel.Views.First(i => i.Name == "MyBS");
        panel.Open();

        Assert.NotNull(opened);
        Assert.Equal("BalanceSheet", opened!.ReportKind);
    }

    [Fact]
    public void SavedViews_panel_delete_removes_one_view()
    {
        var company = Robert();
        _storage.SaveView(company, "Keep", new ReportsViewModel(company, ReportKind.TrialBalance).ToSavedView());
        _storage.SaveView(company, "Drop", new ReportsViewModel(company, ReportKind.BalanceSheet).ToSavedView());

        var panel = new SavedViewsViewModel(company, _storage);
        panel.Selected = panel.Views.First(i => i.Name == "Drop");
        panel.Delete();

        Assert.Single(panel.Views);
        Assert.Equal("Keep", panel.Views[0].Name);
        Assert.Null(_storage.GetView(company, "Drop"));
    }

    [Fact]
    public void Save_upserts_by_name_leaving_a_single_row()
    {
        var company = Robert();
        var v1 = new ReportsViewModel(company, ReportKind.TrialBalance);
        _storage.SaveView(company, "Same", v1.ToSavedView());

        var v2 = new ReportsViewModel(company, ReportKind.BalanceSheet); // different config, same name
        _storage.SaveView(company, "Same", v2.ToSavedView());

        var all = _storage.ListViews(company);
        Assert.Single(all);
        Assert.Equal("BalanceSheet", all[0].View.ReportKind); // the overwrite won
    }

    // =============================================================== per-company isolation at the VM layer

    [Fact]
    public void Saved_views_are_isolated_per_company()
    {
        var a = Robert();
        var b = Robert(); // a different company (its own .db file)

        _storage.SaveView(a, "OnlyInA", new ReportsViewModel(a, ReportKind.TrialBalance).ToSavedView());

        var panelA = new SavedViewsViewModel(a, _storage);
        var panelB = new SavedViewsViewModel(b, _storage);

        Assert.Contains(panelA.Views, i => i.Name == "OnlyInA");
        Assert.Empty(panelB.Views); // company B never sees company A's view
        Assert.Null(_storage.GetView(b, "OnlyInA"));
    }

    // =============================================================== SaveViewViewModel panel

    [Fact]
    public void SaveViewPanel_apply_stores_the_view_and_a_blank_name_is_rejected()
    {
        var company = Robert();
        var report = new ReportsViewModel(company, ReportKind.TrialBalance);

        var blank = new SaveViewViewModel(report, company, _storage) { Name = "   " };
        Assert.False(blank.Apply());                 // blank name rejected
        Assert.Empty(_storage.ListViews(company));

        var named = new SaveViewViewModel(report, company, _storage) { Name = "Named" };
        Assert.True(named.Apply());                  // saved
        Assert.Single(_storage.ListViews(company));
        Assert.NotNull(_storage.GetView(company, "Named"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a lingering SQLite handle on Windows — best-effort cleanup */ }
    }
}
