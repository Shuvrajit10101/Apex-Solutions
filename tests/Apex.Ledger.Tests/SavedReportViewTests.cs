using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The framework-agnostic saved-view config model (RQ-8 Save View, ER-9): deterministic, culture-invariant
/// serialization of the CONFIG TUPLE ONLY. A round-tripped view deserializes identical to what was saved, the
/// JSON is stable and carries no computed figure, enums are written by name, and the default view reproduces
/// the plain report configuration.
/// </summary>
public sealed class SavedReportViewTests
{
    private static SavedReportView FullView() => new()
    {
        ReportKind = "TrialBalance",
        AsOfDate = new DateOnly(2024, 4, 30),
        PeriodFrom = new DateOnly(2024, 4, 1),
        PeriodTo = new DateOnly(2024, 4, 30),
        Detailed = false,
        HideZeroBalances = true,
        ShowPercentages = true,
        ClosingStock = ClosingStockMode.InventoryDerived,
        ScenarioName = "Optimistic",
        SortKey = ReportSortKey.Amount,
        SortAscending = false,
        FilterMinRupees = 1000.50m,
        FilterMaxRupees = 250000.75m,
        FilterNameContains = "cash",
        ComparativeColumns = new List<SavedComparativeColumn>
        {
            new() { Label = "Apr", PeriodFrom = new DateOnly(2024, 4, 1), PeriodTo = new DateOnly(2024, 4, 30) },
            new() { Label = "What-if", ScenarioName = "Optimistic" },
        },
    };

    [Fact]
    public void Round_trip_deserializes_identical_to_what_was_saved()
    {
        var original = FullView();
        var restored = SavedReportView.FromJson(original.ToJson());
        Assert.Equal(original, restored); // record value-equality over the whole tuple, including columns
    }

    [Fact]
    public void Serialization_is_deterministic_and_culture_invariant()
    {
        var view = FullView();
        Assert.Equal(view.ToJson(), view.ToJson()); // stable across calls

        var prior = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture must not change the emitted decimal/date tokens.
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var json = view.ToJson();
            Assert.Contains("1000.50", json);       // decimal stays dot-formatted
            Assert.Contains("2024-04-30", json);    // date stays ISO
            Assert.Equal(view, SavedReportView.FromJson(json));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prior;
        }
    }

    [Fact]
    public void Enums_are_written_by_name_not_ordinal()
    {
        var json = FullView().ToJson();
        Assert.Contains("\"Amount\"", json);            // ReportSortKey.Amount
        Assert.Contains("\"InventoryDerived\"", json);  // ClosingStockMode.InventoryDerived
    }

    [Fact]
    public void Json_carries_only_config_no_computed_figure_keys()
    {
        var json = FullView().ToJson();
        // Config thresholds are allowed; report figure keys must never appear. (The report KIND token e.g.
        // "TrialBalance" is config — so assert figure-specific keys, not a bare "Balance".)
        foreach (var forbidden in new[] { "Debit", "Credit", "GrandTotal", "TotalDebit", "TotalCredit",
                                          "ClosingBalance", "OpeningBalance", "ClosingValue", "NetProfit", "Amount\":" })
            Assert.DoesNotContain(forbidden, json);
    }

    [Fact]
    public void Unknown_enum_names_fall_back_to_defaults_without_throwing()
    {
        // A config_json from a newer build (or a corrupted store) carries enum NAMES this build does not know
        // (e.g. a future SortKey / ClosingStock value). Deserialization must NOT throw and drop the view from
        // the saved-views list — the unknown option degrades to its sensible default, everything else loads.
        const string json =
            """{"ReportKind":"TrialBalance","AsOfDate":"2024-04-30","Detailed":false,"SortKey":"MedianMagnitude","ClosingStock":"MarkToMarket","FilterNameContains":"cash"}""";

        var view = SavedReportView.FromJson(json); // does not throw

        Assert.Equal("TrialBalance", view.ReportKind);            // known fields still load
        Assert.Equal(new DateOnly(2024, 4, 30), view.AsOfDate);
        Assert.False(view.Detailed);
        Assert.Equal("cash", view.FilterNameContains);
        Assert.Equal(ReportSortKey.None, view.SortKey);           // unknown sort key → default None
        Assert.Equal(ClosingStockMode.AsPostedLedger, view.ClosingStock); // unknown mode → default AsPostedLedger
    }

    [Fact]
    public void Default_view_reproduces_the_plain_report_configuration()
    {
        var view = new SavedReportView { ReportKind = "BalanceSheet", AsOfDate = new DateOnly(2024, 4, 1) };
        Assert.True(view.Detailed);
        Assert.False(view.HideZeroBalances);
        Assert.False(view.ShowPercentages);
        Assert.Equal(ClosingStockMode.AsPostedLedger, view.ClosingStock);
        Assert.Equal(ReportSortKey.None, view.SortKey);
        Assert.Null(view.ComparativeColumns);
        Assert.Equal(view, SavedReportView.FromJson(view.ToJson()));
    }
}
