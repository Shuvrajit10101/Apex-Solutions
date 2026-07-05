namespace Apex.Ledger.Reports;

/// <summary>The row-ordering key for a report view (RQ-3). <see cref="None"/> = source order.</summary>
public enum ReportSortKey
{
    /// <summary>No sort — rows stay in the order the projection produced them (the default).</summary>
    None = 0,

    /// <summary>Sort by the row's name/label (ordinal, culture-invariant, case-insensitive).</summary>
    Name = 1,

    /// <summary>Sort by the row's amount <b>magnitude</b> (absolute paisa).</summary>
    Amount = 2,
}

/// <summary>
/// The Phase-5 slice-2 report VIEW parameters (RQ-3 sort &amp; filter). A pure, immutable value carried
/// alongside a report's already-computed rows: it never recomputes figures and never touches a report's
/// Grand Total. It is a <b>view over rows</b> — sort re-orders and filter hides, but neither adds, removes
/// from, nor rebalances the underlying statement. Money stays integer-paisa throughout.
/// </summary>
/// <remarks>
/// <para><b>Defaults reproduce the pre-slice output byte-for-byte:</b> <see cref="SortKey"/> =
/// <see cref="ReportSortKey.None"/>, no bounds, no name filter — <see cref="Apply{T}"/> then returns the
/// source list unchanged (reference-identical items, source order).</para>
/// <para><b>Filter is a VIEW.</b> The report's TotalDebit/TotalCredit/GrandTotal are computed by the report
/// build over the FULL row set; applying a filter to the row list does NOT rebalance those totals. The kept
/// view may therefore not itself "balance" — that is intentional; the totals belong to the unfiltered report.</para>
/// </remarks>
public sealed record ReportSortFilter
{
    /// <summary>The sort key (default <see cref="ReportSortKey.None"/> = source order).</summary>
    public ReportSortKey SortKey { get; init; } = ReportSortKey.None;

    /// <summary>Ascending when true (the default), descending when false. Ignored when <see cref="SortKey"/> is None.</summary>
    public bool Ascending { get; init; } = true;

    /// <summary>Lower bound (inclusive) on the row's <b>magnitude</b>, or <c>null</c> for no lower bound.</summary>
    public Money? Min { get; init; }

    /// <summary>Upper bound (inclusive) on the row's <b>magnitude</b>, or <c>null</c> for no upper bound.</summary>
    public Money? Max { get; init; }

    /// <summary>Keep only rows whose name contains this substring (case-insensitive, culture-invariant),
    /// or <c>null</c>/empty for no name filter.</summary>
    public string? NameContains { get; init; }

    /// <summary>The identity view: no sort, no filter (<see cref="Apply{T}"/> returns the source unchanged).</summary>
    public static readonly ReportSortFilter None = new();

    /// <summary>True when this carries no active sort and no active filter — i.e. the identity view.</summary>
    public bool IsIdentity =>
        SortKey == ReportSortKey.None &&
        Min is null && Max is null &&
        string.IsNullOrEmpty(NameContains);

    // ---- Fluent "with" helpers (records are immutable; these return copies) ----

    public ReportSortFilter WithSort(ReportSortKey key, bool ascending) =>
        this with { SortKey = key, Ascending = ascending };
    public ReportSortFilter WithRange(Money? min, Money? max) => this with { Min = min, Max = max };
    public ReportSortFilter WithNameContains(string? nameContains) => this with { NameContains = nameContains };

    /// <summary>
    /// Applies this view to <paramref name="rows"/>: FILTER first (a view that hides rows), then SORT
    /// (a stable re-order of what survives). <paramref name="nameOf"/> maps a row to its label,
    /// <paramref name="amountOf"/> to the magnitude-bearing amount. When <see cref="IsIdentity"/> the source
    /// list is returned unchanged (reference-identical items, source order) so nothing regresses.
    /// </summary>
    public IReadOnlyList<T> Apply<T>(IEnumerable<T> rows, Func<T, string> nameOf, Func<T, Money> amountOf)
    {
        if (IsIdentity)
            return rows as IReadOnlyList<T> ?? rows.ToList();

        var filtered = ReportConfig.FilterRows(rows, Min, Max, NameContains, nameOf, amountOf);
        return ReportConfig.SortRows(filtered, SortKey, Ascending, nameOf, amountOf);
    }
}
