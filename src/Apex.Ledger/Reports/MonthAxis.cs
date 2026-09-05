namespace Apex.Ledger.Reports;

/// <summary>
/// One calendar month of a reporting window, clipped to that window. <see cref="From"/> is the later of
/// the month's first day and the window start; <see cref="To"/> is the earlier of the month's last day and
/// the window end — so the first and last buckets of a mid-month window are partial, and every bucket in
/// between is a whole month.
/// </summary>
public readonly record struct MonthWindow(int Year, int Month, DateOnly From, DateOnly To)
{
    /// <summary>Culture-free three-letter month abbreviations, indexed 1..12.</summary>
    private static readonly string[] Abbreviations =
    [
        "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    ];

    /// <summary>The bucket's display label, e.g. <c>Apr-2024</c>. Deliberately culture-free: the reports
    /// that render it are compared byte-for-byte in tests and must not move with the machine locale.</summary>
    public string Label => $"{Abbreviations[Month]}-{Year}";
}

/// <summary>
/// The month-wise axis — <b>the primitive the whole Account Books family was missing</b> (census T1-32).
///
/// <para>The wave-2/3 verification passes established, against the vendor's own published report pages,
/// that the first screen of a register and of an account book is a <b>month-wise summary</b>, and that the
/// voucher-wise listing is what a month row <i>drills into</i>. A register therefore cannot be built by
/// adding a voucher-kind filter to the <see cref="DayBook"/>, which is a flat chronological list. Every
/// month-wise report in this codebase enumerates its rows from here so they all bucket identically.</para>
/// </summary>
public static class MonthAxis
{
    /// <summary>
    /// The calendar months touched by <c>[from, to]</c>, in order, each clipped to the window. An inverted
    /// window (<paramref name="from"/> &gt; <paramref name="to"/>) yields no buckets.
    /// </summary>
    public static IReadOnlyList<MonthWindow> Months(DateOnly from, DateOnly to)
    {
        var months = new List<MonthWindow>();
        if (from > to) return months;

        var cursor = new DateOnly(from.Year, from.Month, 1);
        var guard = 0;
        while (cursor <= to)
        {
            var monthEnd = cursor.AddMonths(1).AddDays(-1);
            months.Add(new MonthWindow(
                cursor.Year,
                cursor.Month,
                cursor < from ? from : cursor,
                monthEnd > to ? to : monthEnd));

            cursor = cursor.AddMonths(1);
            if (++guard > 12_000)
                throw new InvalidOperationException("Month axis exceeded 1,000 years — window is malformed.");
        }

        return months;
    }
}
