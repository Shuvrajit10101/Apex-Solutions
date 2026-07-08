using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The Alt+F12 report Sort/Filter panel (RQ-3), hosted as its own cascading Miller-column page column to the
/// right of the report it views — never a stacked overlay, mirroring the F12 <see cref="ReportConfigViewModel"/>.
/// It edits a pending <see cref="ReportSortFilter"/> VIEW and, on <see cref="Apply"/>, pushes it into the live
/// <see cref="ReportsViewModel"/> which re-projects the same report through the engine. The view is a pure
/// row-level lens: sort re-orders the row-bearing sections, filter hides out-of-range/name-mismatched rows,
/// and neither recomputes a figure nor touches the report's Grand Total (which stays computed over the full,
/// unfiltered set). Clearing resets to <see cref="ReportSortFilter.None"/>.
///
/// <para>Amounts are entered as rupees (integers or decimals) and parsed to integer-paisa <see cref="Money"/>
/// via <see cref="Money.FromRupees(decimal)"/>; a blank bound means "unbounded" (null). A non-blank but
/// unparseable amount is rejected with a validation status — the panel never falsely claims it applied.
/// The panel opens seeded from the report's current view, so opening → applying with no edits is a no-op that
/// preserves the default output exactly.</para>
/// </summary>
public sealed partial class ReportSortFilterViewModel : ViewModelBase
{
    private readonly ReportsViewModel _report;

    /// <summary>The column title / heading for the sort-filter panel.</summary>
    public string Title => "Sort & Filter — Alt+F12";

    /// <summary>The report this panel views (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    /// <summary>True when the current report kind actually honours the sort/filter view (else the panel is inert).</summary>
    public bool SupportsSortFilter => _report.SupportsSortFilter;

    // ---- sort ----

    /// <summary>The sort-key options offered by the panel (None / Name / Amount).</summary>
    public ObservableCollection<SortKeyOption> SortKeys { get; } = new();

    /// <summary>The chosen sort key (defaults to <see cref="ReportSortKey.None"/> = source order).</summary>
    [ObservableProperty] private SortKeyOption? _selectedSortKey;

    /// <summary>Ascending when true (the default), descending when false. Ignored when the key is None.</summary>
    [ObservableProperty] private bool _ascending = true;

    // ---- value/range + name filter ----

    /// <summary>The minimum amount (rupees) as text; blank = no lower bound.</summary>
    [ObservableProperty] private string _minText = string.Empty;

    /// <summary>The maximum amount (rupees) as text; blank = no upper bound.</summary>
    [ObservableProperty] private string _maxText = string.Empty;

    /// <summary>Keep only rows whose name contains this substring (case-insensitive); blank = no name filter.</summary>
    [ObservableProperty] private string _nameContains = string.Empty;

    /// <summary>A short status line shown after applying / clearing (feedback that the view re-ran).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public ReportSortFilterViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));

        foreach (var key in new[] { ReportSortKey.None, ReportSortKey.Name, ReportSortKey.Amount })
            SortKeys.Add(new SortKeyOption(key));

        SeedFromReport();
    }

    /// <summary>Seeds every field from the report's current view (so a no-op apply changes nothing).</summary>
    private void SeedFromReport()
    {
        var v = _report.SortFilter;

        foreach (var opt in SortKeys)
            if (opt.Key == v.SortKey)
            {
                SelectedSortKey = opt;
                break;
            }

        Ascending = v.Ascending;
        MinText = v.Min is { } lo ? FmtRupees(lo) : string.Empty;
        MaxText = v.Max is { } hi ? FmtRupees(hi) : string.Empty;
        NameContains = v.NameContains ?? string.Empty;
    }

    /// <summary>
    /// Applies the panel's sort/filter to the live report and re-projects it (RQ-3). Amount bounds are
    /// validated FIRST: a non-blank but unparseable Min/Max, or an inverted [Min &gt; Max] range, is rejected
    /// with a clear status and the report's view is left untouched (no false "applied"). A blank bound is a
    /// null (unbounded) bound. On success the composed <see cref="ReportSortFilter"/> is pushed to the report.
    /// </summary>
    public void Apply()
    {
        if (!TryParseRupees(MinText, out var min))
        {
            Status = "Unrecognized minimum amount. Enter rupees (e.g. 1000 or 1000.50), or leave it blank.";
            return;
        }
        if (!TryParseRupees(MaxText, out var max))
        {
            Status = "Unrecognized maximum amount. Enter rupees (e.g. 5000 or 5000.75), or leave it blank.";
            return;
        }
        if (min is { } lo && max is { } hi && lo.Amount > hi.Amount)
        {
            Status = "Invalid range: minimum must be less than or equal to maximum.";
            return;
        }

        var key = SelectedSortKey?.Key ?? ReportSortKey.None;
        var name = string.IsNullOrWhiteSpace(NameContains) ? null : NameContains.Trim();

        var view = ReportSortFilter.None
            .WithSort(key, Ascending)
            .WithRange(min, max)
            .WithNameContains(name);

        _report.ApplySortFilter(view);
        Status = view.IsIdentity ? "Cleared — showing all rows in source order." : "Applied — view updated.";
    }

    /// <summary>Clears the panel back to the identity view (no sort, no filter) and re-projects the report.</summary>
    public void Clear()
    {
        SelectedSortKey = FindKey(ReportSortKey.None);
        Ascending = true;
        MinText = string.Empty;
        MaxText = string.Empty;
        NameContains = string.Empty;

        _report.ClearSortFilter();
        Status = "Cleared — showing all rows in source order.";
    }

    private SortKeyOption? FindKey(ReportSortKey key)
    {
        foreach (var opt in SortKeys)
            if (opt.Key == key) return opt;
        return null;
    }

    // Money.Amount is the rupee value as an exact decimal (paisa-exact, ≤ 2 dp), not integer paisa; a bound
    // therefore round-trips as plain rupees text.
    private static string FmtRupees(Money m) =>
        m.Amount.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a rupees amount to integer-paisa <see cref="Money"/>. A blank/whitespace value is a valid
    /// "unbounded" bound (returns <c>true</c> with <paramref name="value"/> null). A non-blank but
    /// unparseable or negative value returns <c>false</c> (rejected — magnitudes are non-negative).
    /// </summary>
    private static bool TryParseRupees(string? text, out Money? value)
    {
        value = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true; // blank = unbounded

        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var rupees))
            return false;
        if (rupees < 0m) return false; // magnitude bounds are non-negative

        value = Money.FromRupees(rupees);
        return true;
    }
}

/// <summary>One sort-key option for the Alt+F12 panel combo (label + engine key).</summary>
public sealed class SortKeyOption
{
    public ReportSortKey Key { get; }
    public string Display { get; }

    public SortKeyOption(ReportSortKey key)
    {
        Key = key;
        Display = key switch
        {
            ReportSortKey.None => "None (source order)",
            ReportSortKey.Name => "Name (alphabetical)",
            ReportSortKey.Amount => "Amount (magnitude)",
            _ => key.ToString(),
        };
    }
}
