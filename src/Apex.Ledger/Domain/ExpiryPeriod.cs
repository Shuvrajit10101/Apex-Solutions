using System.Globalization;

namespace Apex.Ledger.Domain;

/// <summary>
/// The unit an <see cref="ExpiryPeriod"/> counts in (Phase 6 Cluster 1; requirements RQ-4). The ordinals are
/// stable (persisted as the raw period text, e.g. "12 Months", and re-parsed).
/// </summary>
public enum ExpiryPeriodUnit
{
    Days = 0,
    Weeks = 1,
    Months = 2,
    Years = 3,
}

/// <summary>
/// An expiry expressed as a <b>relative period</b> from a batch's manufacturing date rather than an absolute
/// date (Phase 6 Cluster 1; requirements RQ-4). The operator may type either an absolute expiry date
/// <i>or</i> a period such as "12 Months"; the engine <b>resolves the period to a concrete date</b> as
/// <c>mfg + period</c>. This value type is that period plus a deterministic, culture-invariant resolver.
/// </summary>
/// <remarks>
/// <para>Resolution is exact calendar arithmetic on <see cref="DateOnly"/>: Days/Weeks add a fixed number of
/// days; Months/Years use <see cref="DateOnly.AddMonths"/>/<see cref="DateOnly.AddYears"/> so a 12-Month
/// expiry from 15-Jan-2024 resolves to 15-Jan-2025 (calendar-correct, leap-safe), never a float day-count.</para>
/// <para>The raw text (<see cref="RawText"/>) is round-trippable: <see cref="Parse"/> accepts the canonical
/// "&lt;count&gt; &lt;unit&gt;" form (case-insensitive, singular or plural unit) that <see cref="ToString"/>
/// produces, so the persisted <c>expiry_period</c> column re-hydrates losslessly.</para>
/// </remarks>
public readonly struct ExpiryPeriod : IEquatable<ExpiryPeriod>
{
    /// <summary>The whole-number count of <see cref="Unit"/>s (&gt; 0).</summary>
    public int Count { get; }

    /// <summary>The unit the <see cref="Count"/> is measured in.</summary>
    public ExpiryPeriodUnit Unit { get; }

    public ExpiryPeriod(int count, ExpiryPeriodUnit unit)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "An expiry period count must be > 0.");
        Count = count;
        Unit = unit;
    }

    /// <summary>
    /// Resolves this period to a concrete expiry date as <paramref name="manufacturingDate"/> + period
    /// (RQ-4). Deterministic and culture-invariant; Months/Years use calendar-correct arithmetic.
    /// </summary>
    public DateOnly ResolveFrom(DateOnly manufacturingDate) => Unit switch
    {
        ExpiryPeriodUnit.Days => manufacturingDate.AddDays(Count),
        ExpiryPeriodUnit.Weeks => manufacturingDate.AddDays(Count * 7),
        ExpiryPeriodUnit.Months => manufacturingDate.AddMonths(Count),
        ExpiryPeriodUnit.Years => manufacturingDate.AddYears(Count),
        _ => manufacturingDate.AddDays(Count),
    };

    /// <summary>The canonical round-trippable text, e.g. "12 Months" (singular when <see cref="Count"/> == 1).</summary>
    public override string ToString()
    {
        var unitName = Unit switch
        {
            ExpiryPeriodUnit.Days => "Day",
            ExpiryPeriodUnit.Weeks => "Week",
            ExpiryPeriodUnit.Months => "Month",
            ExpiryPeriodUnit.Years => "Year",
            _ => "Day",
        };
        return Count == 1
            ? $"{Count} {unitName}"
            : $"{Count.ToString(CultureInfo.InvariantCulture)} {unitName}s";
    }

    /// <summary>The canonical text of this period (alias of <see cref="ToString"/>).</summary>
    public string RawText => ToString();

    /// <summary>
    /// Parses the canonical "&lt;count&gt; &lt;unit&gt;" text (case-insensitive; unit singular or plural) into
    /// an <see cref="ExpiryPeriod"/>. Returns <c>null</c> for null/blank/unrecognised input rather than
    /// throwing, so a hand-typed or persisted value that is not a period (e.g. an absolute date) is simply not
    /// treated as one. Culture-invariant.
    /// </summary>
    public static ExpiryPeriod? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            return null;

        var unitToken = parts[1].ToLowerInvariant().TrimEnd('s');
        ExpiryPeriodUnit unit = unitToken switch
        {
            "day" => ExpiryPeriodUnit.Days,
            "week" => ExpiryPeriodUnit.Weeks,
            "month" => ExpiryPeriodUnit.Months,
            "year" => ExpiryPeriodUnit.Years,
            _ => (ExpiryPeriodUnit)(-1),
        };
        if ((int)unit < 0) return null;
        return new ExpiryPeriod(count, unit);
    }

    public bool Equals(ExpiryPeriod other) => Count == other.Count && Unit == other.Unit;
    public override bool Equals(object? obj) => obj is ExpiryPeriod other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Count, Unit);
    public static bool operator ==(ExpiryPeriod a, ExpiryPeriod b) => a.Equals(b);
    public static bool operator !=(ExpiryPeriod a, ExpiryPeriod b) => !a.Equals(b);
}
