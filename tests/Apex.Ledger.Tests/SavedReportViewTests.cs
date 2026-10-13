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
        // T2-125(iv) — the two parameters Save View used to drop. In FullView() so EVERY test in this file, not
        // only the new one below, exercises them (the round trip, the determinism pass, the culture pass, and
        // the no-figures key scan).
        VoucherTypeFilterId = new Guid("8f14e45f-ceea-467a-9575-0a0b2c3d4e5f"),
        ShowNarration = true,
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
        // T2-125(iv): the two new parameters default to "no narrowing, no narration", which is what every view
        // saved BEFORE they existed implies — so an old blob, which omits both keys, loads identically (ER-13).
        Assert.Null(view.VoucherTypeFilterId);
        Assert.False(view.ShowNarration);
        Assert.Equal(view, SavedReportView.FromJson(view.ToJson()));
    }

    /// <summary>
    /// 🔴 <b>T2-125(iv) — THE TWO DROPPED PARAMETERS, AND THE THREE WAYS THIS COULD HAVE GONE GREEN WHILE STILL
    /// DROPPING THEM.</b>
    /// <list type="number">
    ///   <item><b>This record overrides <c>Equals</c> BY HAND.</b> A property left out of that override is
    ///   invisible to every round-trip test in this file: two views differing only in it compare EQUAL. So the
    ///   first assertion below is that two views differing ONLY in these fields are <b>not</b> equal — without
    ///   it, `Assert.Equal(original, restored)` above would pass over a dropped field.</item>
    ///   <item><b>A key must actually be in the JSON.</b> Asserted by name, so a property that exists on the
    ///   record but is never serialised is caught.</item>
    ///   <item><b>An OLD blob must still load.</b> A pre-change <c>config_json</c> omits both keys entirely;
    ///   that is simulated verbatim rather than by constructing a default view, because the two are only
    ///   equivalent if the reader tolerates the absence — which is the thing being tested.</item>
    /// </list>
    /// <para><b>No migration was needed for any of this</b>, contrary to <c>T2-125(iv)</c>: <c>saved_views</c>
    /// stores the whole view as one <c>config_json</c> column.</para>
    /// </summary>
    [Fact]
    public void The_two_dropped_report_parameters_round_trip_and_are_part_of_value_equality()
    {
        var withBoth = FullView();

        // (1) VALUE EQUALITY — the hand-written Equals must see them.
        Assert.NotEqual(withBoth, withBoth with { VoucherTypeFilterId = null });
        Assert.NotEqual(withBoth, withBoth with { ShowNarration = false });
        Assert.NotEqual(withBoth.GetHashCode(), (withBoth with { ShowNarration = false }).GetHashCode());

        // (2) THE KEYS ARE EMITTED, and the round trip is exact.
        var json = withBoth.ToJson();
        Assert.Contains("\"VoucherTypeFilterId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ShowNarration\"", json, StringComparison.Ordinal);
        var restored = SavedReportView.FromJson(json);
        Assert.Equal(withBoth.VoucherTypeFilterId, restored.VoucherTypeFilterId);
        Assert.True(restored.ShowNarration);

        // (3) ER-13 — a blob written BEFORE these two existed still loads, with the defaults.
        var legacy = SavedReportView.FromJson(
            """{"ReportKind":"DayBook","AsOfDate":"2024-04-30","Detailed":true}""");
        Assert.Equal("DayBook", legacy.ReportKind);
        Assert.Null(legacy.VoucherTypeFilterId);
        Assert.False(legacy.ShowNarration);
    }
}
