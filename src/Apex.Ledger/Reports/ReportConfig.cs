namespace Apex.Ledger.Reports;

/// <summary>
/// The F12 report-configuration post-processors (RQ-6). Each is a pure, read-only projection over
/// an already-computed row list — it never recomputes figures and never mutates the source. Money
/// stays integer-paisa throughout; percentages are the only decimals produced, and are guarded
/// against a zero section/column total so no divide-by-zero can occur.
/// </summary>
public static class ReportConfig
{
    /// <summary>
    /// Drops rows whose mapped magnitude is exactly zero (RQ-6 "show/hide zero balances"). A negative
    /// magnitude is <b>not</b> zero, so it is kept — only an exact <see cref="Money.Zero"/> is removed.
    /// </summary>
    public static IReadOnlyList<T> HideZeroBalances<T>(IEnumerable<T> rows, Func<T, Money> amountOf)
        => rows.Where(r => amountOf(r) != Money.Zero).ToList();

    /// <summary>
    /// Each row's percentage of the section/column total (RQ-6 "show percentages"), as a decimal in
    /// <c>[0,100]</c>. The list sums to 100 when the total is non-zero; when the total is zero every
    /// row yields 0% (guard against divide-by-zero). Percentages are computed off the exact paisa
    /// magnitudes, so no rounding drift is introduced into the underlying figures.
    /// </summary>
    public static IReadOnlyList<decimal> Percentages<T>(IEnumerable<T> rows, Func<T, Money> amountOf)
    {
        var list = rows.ToList();
        var total = 0m;
        foreach (var r in list) total += amountOf(r).Amount;

        var result = new List<decimal>(list.Count);
        if (total == 0m)
        {
            foreach (var _ in list) result.Add(0m);
            return result;
        }

        foreach (var r in list)
            result.Add(amountOf(r).Amount / total * 100m);
        return result;
    }

    /// <summary>
    /// Re-orders <paramref name="rows"/> for a report VIEW (RQ-3 sort). <see cref="ReportSortKey.None"/>
    /// returns the source list unchanged (source order, reference-identical items). <see cref="ReportSortKey.Name"/>
    /// orders by <paramref name="nameOf"/> ordinally, culture-invariantly and case-insensitively;
    /// <see cref="ReportSortKey.Amount"/> orders by the <b>magnitude</b> (absolute paisa) of
    /// <paramref name="amountOf"/>. Both are STABLE — rows with an equal key keep their source order (LINQ
    /// <c>OrderBy</c>/<c>OrderByDescending</c> are documented stable). Sorting is a pure view: it never adds,
    /// drops, nor recomputes rows, and never touches a report's Grand Total.
    /// </summary>
    public static IReadOnlyList<T> SortRows<T>(
        IEnumerable<T> rows,
        ReportSortKey key,
        bool ascending,
        Func<T, string> nameOf,
        Func<T, Money> amountOf)
    {
        switch (key)
        {
            case ReportSortKey.Name:
                return ascending
                    ? rows.OrderBy(nameOf, StringComparer.OrdinalIgnoreCase).ToList()
                    : rows.OrderByDescending(nameOf, StringComparer.OrdinalIgnoreCase).ToList();
            case ReportSortKey.Amount:
                return ascending
                    ? rows.OrderBy(r => Math.Abs(amountOf(r).Amount)).ToList()
                    : rows.OrderByDescending(r => Math.Abs(amountOf(r).Amount)).ToList();
            case ReportSortKey.None:
            default:
                return rows as IReadOnlyList<T> ?? rows.ToList();
        }
    }

    /// <summary>
    /// Filters <paramref name="rows"/> for a report VIEW (RQ-3 filter): keeps a row when its
    /// <b>magnitude</b> (<c>|<paramref name="amountOf"/>|</c>) is within the inclusive range
    /// <c>[<paramref name="min"/>, <paramref name="max"/>]</c> (either bound optional) AND its
    /// <paramref name="nameOf"/> contains <paramref name="nameContains"/> (case-insensitive,
    /// culture-invariant). An empty/whitespace <paramref name="nameContains"/> and two <c>null</c> bounds
    /// mean "no filter" — every row is kept in source order. Filtering is a pure view: it only HIDES rows,
    /// it never recomputes figures nor rebalances a report's Grand Total, so a filtered view may itself no
    /// longer balance — the totals belong to the unfiltered report.
    /// </summary>
    public static IReadOnlyList<T> FilterRows<T>(
        IEnumerable<T> rows,
        Money? min,
        Money? max,
        string? nameContains,
        Func<T, string> nameOf,
        Func<T, Money> amountOf)
    {
        var hasName = !string.IsNullOrWhiteSpace(nameContains);
        var needle = nameContains ?? string.Empty;

        // No active predicate → identity view (keep everything, source order).
        if (min is null && max is null && !hasName)
            return rows as IReadOnlyList<T> ?? rows.ToList();

        var result = new List<T>();
        foreach (var r in rows)
        {
            var mag = Math.Abs(amountOf(r).Amount);
            if (min is { } lo && mag < lo.Amount) continue;
            if (max is { } hi && mag > hi.Amount) continue;
            if (hasName &&
                nameOf(r).IndexOf(needle, StringComparison.InvariantCultureIgnoreCase) < 0) continue;
            result.Add(r);
        }
        return result;
    }
}
