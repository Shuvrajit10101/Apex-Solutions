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
}
