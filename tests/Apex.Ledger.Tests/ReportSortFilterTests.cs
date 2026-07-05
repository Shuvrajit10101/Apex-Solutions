using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-5 slice-2 ENGINE tests (RQ-3 report sort &amp; filter). These pin the pure, row-type-agnostic
/// post-projection helpers <see cref="ReportConfig.SortRows{T}"/> and <see cref="ReportConfig.FilterRows{T}"/>
/// and the <see cref="ReportSortFilter"/> value object that carries them. The invariants the UI stage
/// relies on:
/// <list type="bullet">
///   <item>the default (no sort, no filter) view is byte-for-byte the source row list — same items, same order;</item>
///   <item>sort-by-Name is ordinal / culture-invariant and case-insensitive; sort-by-Amount is by <b>magnitude</b>
///         (absolute paisa), ascending or descending; both are STABLE (equal keys keep source order);</item>
///   <item>the value/range filter keeps rows whose magnitude is within <c>[min,max]</c> (either bound optional,
///         inclusive); the name-substring filter is culture-invariant, case-insensitive; an empty filter keeps all;</item>
///   <item>filtering is a VIEW over rows — it NEVER rebalances a report's Grand Total / structure. Totals reflect
///         the UNFILTERED report; the filter only hides rows. (Decision pinned by test.)</item>
/// </list>
/// Robert (accounts-only) and Bright (trading) remain the regression anchors.
/// </summary>
public class ReportSortFilterTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // A small fixed row set with mixed magnitudes, signs, and casing to pin ordering/filtering precisely.
    private static TrialBalanceRow[] SampleRows() => new[]
    {
        new TrialBalanceRow("banana",  "G", new Money(300m),  Money.Zero),
        new TrialBalanceRow("Apple",   "G", new Money(-100m), Money.Zero),
        new TrialBalanceRow("cherry",  "G", new Money(200m),  Money.Zero),
        new TrialBalanceRow("apricot", "G", new Money(200m),  Money.Zero), // ties cherry on |amount|=200
    };

    // ---------------------------------------------------------------- default: identity view

    [Fact]
    public void Default_sortfilter_is_the_identity_view_same_items_same_order()
    {
        var rows = SampleRows();
        var sf = ReportSortFilter.None;

        var outRows = sf.Apply(rows, r => r.LedgerName, r => r.Debit);

        Assert.Equal(rows.Length, outRows.Count);
        for (var i = 0; i < rows.Length; i++)
            Assert.Same(rows[i], outRows[i]); // reference-identical, in source order
    }

    [Fact]
    public void SortRows_none_returns_source_order_unchanged()
    {
        var rows = SampleRows();
        var outRows = ReportConfig.SortRows(rows, ReportSortKey.None, ascending: true,
            r => r.LedgerName, r => r.Debit);

        for (var i = 0; i < rows.Length; i++)
            Assert.Same(rows[i], outRows[i]);
    }

    [Fact]
    public void FilterRows_empty_filter_keeps_all_rows_in_order()
    {
        var rows = SampleRows();
        var outRows = ReportConfig.FilterRows(rows, min: null, max: null, nameContains: null,
            r => r.LedgerName, r => r.Debit);

        for (var i = 0; i < rows.Length; i++)
            Assert.Same(rows[i], outRows[i]);
    }

    // ---------------------------------------------------------------- sort by name (ordinal, case-insensitive)

    [Fact]
    public void SortRows_by_name_ascending_is_ordinal_case_insensitive()
    {
        var rows = SampleRows();
        var sorted = ReportConfig.SortRows(rows, ReportSortKey.Name, ascending: true,
            r => r.LedgerName, r => r.Debit);

        // Apple, apricot, banana, cherry  — case-insensitive ordinal
        Assert.Equal(new[] { "Apple", "apricot", "banana", "cherry" },
            sorted.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void SortRows_by_name_descending_reverses_the_ascending_order()
    {
        var rows = SampleRows();
        var sorted = ReportConfig.SortRows(rows, ReportSortKey.Name, ascending: false,
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "cherry", "banana", "apricot", "Apple" },
            sorted.Select(r => r.LedgerName).ToArray());
    }

    // ---------------------------------------------------------------- sort by amount (magnitude), stable

    [Fact]
    public void SortRows_by_amount_ascending_is_by_magnitude_and_stable_on_ties()
    {
        var rows = SampleRows();
        var sorted = ReportConfig.SortRows(rows, ReportSortKey.Amount, ascending: true,
            r => r.LedgerName, r => r.Debit);

        // magnitudes: Apple 100, cherry 200, apricot 200, banana 300.
        // 200-ties keep SOURCE order (cherry before apricot) — stability.
        Assert.Equal(new[] { "Apple", "cherry", "apricot", "banana" },
            sorted.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void SortRows_by_amount_descending_is_by_magnitude_and_stable_on_ties()
    {
        var rows = SampleRows();
        var sorted = ReportConfig.SortRows(rows, ReportSortKey.Amount, ascending: false,
            r => r.LedgerName, r => r.Debit);

        // 300, then the 200-tie in SOURCE order (cherry before apricot), then 100.
        Assert.Equal(new[] { "banana", "cherry", "apricot", "Apple" },
            sorted.Select(r => r.LedgerName).ToArray());
    }

    // ---------------------------------------------------------------- value / range filter (magnitude, inclusive)

    [Fact]
    public void FilterRows_range_keeps_rows_within_inclusive_magnitude_bounds()
    {
        var rows = SampleRows();
        // |amount|: banana 300, Apple 100, cherry 200, apricot 200. Range [200,300] keeps 300 + both 200s.
        var kept = ReportConfig.FilterRows(rows, min: new Money(200m), max: new Money(300m), nameContains: null,
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "banana", "cherry", "apricot" },
            kept.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void FilterRows_min_only_keeps_rows_at_or_above_min_magnitude()
    {
        var rows = SampleRows();
        var kept = ReportConfig.FilterRows(rows, min: new Money(250m), max: null, nameContains: null,
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "banana" }, kept.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void FilterRows_max_only_keeps_rows_at_or_below_max_magnitude()
    {
        var rows = SampleRows();
        var kept = ReportConfig.FilterRows(rows, min: null, max: new Money(150m), nameContains: null,
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "Apple" }, kept.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void FilterRows_magnitude_bounds_use_absolute_value_so_a_negative_row_is_kept_by_a_positive_range()
    {
        var rows = SampleRows(); // Apple is -100
        var kept = ReportConfig.FilterRows(rows, min: new Money(50m), max: new Money(150m), nameContains: null,
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "Apple" }, kept.Select(r => r.LedgerName).ToArray());
    }

    // ---------------------------------------------------------------- name-substring filter

    [Fact]
    public void FilterRows_name_contains_is_case_insensitive_and_culture_invariant()
    {
        var rows = SampleRows();
        var kept = ReportConfig.FilterRows(rows, min: null, max: null, nameContains: "AP",
            r => r.LedgerName, r => r.Debit);

        // "Apple" and "apricot" both contain "ap" case-insensitively.
        Assert.Equal(new[] { "Apple", "apricot" }, kept.Select(r => r.LedgerName).ToArray());
    }

    [Fact]
    public void FilterRows_combines_range_and_name_with_AND()
    {
        var rows = SampleRows();
        // magnitude in [200,300] AND name contains "a": banana (300, has 'a'), apricot (200, has 'a');
        // cherry (200) has no 'a' so it is excluded.
        var kept = ReportConfig.FilterRows(rows, min: new Money(200m), max: new Money(300m), nameContains: "a",
            r => r.LedgerName, r => r.Debit);

        Assert.Equal(new[] { "banana", "apricot" }, kept.Select(r => r.LedgerName).ToArray());
    }

    // ---------------------------------------------------------------- combined apply (filter then sort)

    [Fact]
    public void Apply_filters_then_sorts()
    {
        var rows = SampleRows();
        var sf = new ReportSortFilter
        {
            SortKey = ReportSortKey.Amount,
            Ascending = false,
            Min = new Money(200m),
            Max = null,
            NameContains = null,
        };

        var outRows = sf.Apply(rows, r => r.LedgerName, r => r.Debit);

        // Filter |amount|>=200 -> banana(300), cherry(200), apricot(200). Sort by magnitude desc, stable:
        // banana, then the 200-tie in source order cherry, apricot.
        Assert.Equal(new[] { "banana", "cherry", "apricot" },
            outRows.Select(r => r.LedgerName).ToArray());
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void FilterRows_over_empty_rows_returns_empty()
    {
        var empty = Array.Empty<TrialBalanceRow>();
        var kept = ReportConfig.FilterRows(empty, min: new Money(1m), max: new Money(2m), nameContains: "x",
            r => r.LedgerName, r => r.Debit);
        Assert.Empty(kept);
    }

    [Fact]
    public void FilterRows_range_that_excludes_everything_returns_empty()
    {
        var rows = SampleRows();
        var kept = ReportConfig.FilterRows(rows, min: new Money(10000m), max: new Money(20000m), nameContains: null,
            r => r.LedgerName, r => r.Debit);
        Assert.Empty(kept);
    }

    [Fact]
    public void SortRows_over_empty_rows_returns_empty()
    {
        var empty = Array.Empty<TrialBalanceRow>();
        var sorted = ReportConfig.SortRows(empty, ReportSortKey.Name, ascending: true,
            r => r.LedgerName, r => r.Debit);
        Assert.Empty(sorted);
    }

    // ---------------------------------------------------------------- DECISION: filter is a VIEW, totals unchanged

    [Fact]
    [Trait("Category", "Fixture")]
    public void Filter_is_a_view_the_reports_grand_total_is_computed_from_the_full_set_before_filtering()
    {
        var f = Robert();
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        // Apply an aggressive range filter that hides most rows. The report's own TotalDebit/TotalCredit
        // (computed by Build over the FULL set) must be untouched — filtering is a VIEW over Rows, it does
        // NOT rebalance the statement. The kept view may itself no longer "balance"; that is expected and
        // intentional — the Grand Total belongs to the unfiltered report.
        var keptDr = ReportConfig.FilterRows(tb.Rows, min: new Money(100000m), max: null, nameContains: null,
            r => r.LedgerName, r => r.Debit);

        Assert.True(keptDr.Count < tb.Rows.Count, "filter should hide at least one row for this assertion to be meaningful");
        // The report totals are exactly the unfiltered totals — the filter changed nothing about them.
        Assert.Equal(tb.TotalDebit, TrialBalance.Build(f.Company, f.AsOf).TotalDebit);
        Assert.Equal(tb.TotalCredit, TrialBalance.Build(f.Company, f.AsOf).TotalCredit);
        Assert.True(tb.Balanced); // the source report is still balanced regardless of the view
    }

    // ---------------------------------------------------------------- real-report sort sanity (Bright TB)

    [Fact]
    [Trait("Category", "Fixture")]
    public void SortRows_by_amount_desc_over_a_real_trial_balance_orders_by_descending_magnitude()
    {
        var f = Bright();
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        var sorted = ReportConfig.SortRows(tb.Rows, ReportSortKey.Amount, ascending: false,
            r => r.LedgerName, r => r.Debit + r.Credit); // one column carries the magnitude per row

        for (var i = 1; i < sorted.Count; i++)
        {
            var prev = Math.Abs((sorted[i - 1].Debit + sorted[i - 1].Credit).Amount);
            var cur = Math.Abs((sorted[i].Debit + sorted[i].Credit).Amount);
            Assert.True(prev >= cur, $"row {i - 1} magnitude {prev} should be >= row {i} magnitude {cur}");
        }

        // A view-level sort never drops or adds rows.
        Assert.Equal(tb.Rows.Count, sorted.Count);
    }
}
