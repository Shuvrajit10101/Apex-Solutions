namespace Apex.Ledger.Reports;

/// <summary>A group-level roll-up row: a key (group name) and its summed magnitude.</summary>
public sealed record GroupRollupRow(string Key, Money Amount);

/// <summary>
/// The detailed↔summary roll-up primitive (RQ-2, Alt+F1). Collapses a detailed row list into one
/// row per key (group), summing the mapped magnitudes to the paisa. The invariant the tests
/// pin is <b>Σ(summary) == Σ(detailed)</b>: a roll-up never changes a report's totals, it only
/// changes the granularity. Insertion order of first appearance is preserved so the summary
/// reads in the same order as the detailed list.
/// </summary>
public static class ReportGrouping
{
    /// <summary>
    /// Rolls <paramref name="rows"/> up by <paramref name="keyOf"/>, summing <paramref name="amountOf"/>.
    /// Returns one <see cref="GroupRollupRow"/> per distinct key, in first-seen order.
    /// </summary>
    public static IReadOnlyList<GroupRollupRow> RollUp<T>(
        IEnumerable<T> rows,
        Func<T, string> keyOf,
        Func<T, Money> amountOf)
    {
        var order = new List<string>();
        var sums = new Dictionary<string, decimal>();

        foreach (var row in rows)
        {
            var key = keyOf(row);
            if (!sums.ContainsKey(key))
            {
                sums[key] = 0m;
                order.Add(key);
            }
            sums[key] += amountOf(row).Amount;
        }

        var result = new List<GroupRollupRow>(order.Count);
        foreach (var key in order)
            result.Add(new GroupRollupRow(key, new Money(sums[key])));
        return result;
    }
}
