using System.Text.Json;
using System.Text.Json.Serialization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// A named, persisted report <b>view</b> (RQ-8 Save View, ER-9, DP-7): the CONFIGURATION TUPLE ONLY —
/// which report, over which period/as-of, at which depth, with which sort, filter, comparative columns and
/// F12 options. It <b>never</b> carries a computed figure: a saved view is re-applied to the live company and
/// the report is recomputed from scratch, so a view can never go stale (ER-9). One view is identified per
/// company by its <see cref="Name"/> (case-insensitive upsert lives in the repository).
/// </summary>
/// <remarks>
/// <para><b>Report kind is a stable STRING</b> (<see cref="ReportKind"/>), not an enum: the Desktop's own
/// <c>ReportKind</c> enum must not leak into the engine, so the Desktop maps to/from this token. The token is
/// an opaque, stable identifier the Desktop owns (e.g. <c>"TrialBalance"</c>); the engine only round-trips it.</para>
/// <para><b>Money never appears.</b> The only monetary fields a view can carry are the sort/filter magnitude
/// bounds (<see cref="FilterMinRupees"/> / <see cref="FilterMaxRupees"/>) — user-chosen thresholds, not report
/// figures — serialized as exact invariant decimal strings. There is no place to store a computed amount.</para>
/// </remarks>
public sealed record SavedReportView
{
    /// <summary>The stable report-kind token the Desktop maps to/from its own enum (never an engine enum).</summary>
    public required string ReportKind { get; init; }

    // ---- ReportOptions tuple (period / as-of / depth / F12) --------------------------------------

    /// <summary>The as-of date (ISO yyyy-MM-dd) — always set, mirroring <see cref="ReportOptions.AsOfDate"/>.</summary>
    public required DateOnly AsOfDate { get; init; }

    /// <summary>The explicit period window start, or <c>null</c> for the default (books-begin → as-of) window.</summary>
    public DateOnly? PeriodFrom { get; init; }

    /// <summary>The explicit period window end, or <c>null</c>. Set together with <see cref="PeriodFrom"/>.</summary>
    public DateOnly? PeriodTo { get; init; }

    /// <summary>Detailed (ledger/item level) when true; summary (group roll-up) when false.</summary>
    public bool Detailed { get; init; } = true;

    /// <summary>F12: hide rows whose balance is exactly zero.</summary>
    public bool HideZeroBalances { get; init; }

    /// <summary>F12: show each row's percentage of its section/column total.</summary>
    public bool ShowPercentages { get; init; }

    /// <summary>F12: closing-stock valuation basis (<see cref="ClosingStockMode"/> ordinal, by name for stability).</summary>
    public ClosingStockMode ClosingStock { get; init; } = ClosingStockMode.AsPostedLedger;

    /// <summary>The scenario name to compute under, or <c>null</c> for the actual books. A NAME (not an id), so a
    /// view re-binds to the live company's scenario of that name on apply; unknown → actual books.</summary>
    public string? ScenarioName { get; init; }

    // ---- ReportSortFilter tuple (sort / filter) --------------------------------------------------

    /// <summary>The row sort key (<see cref="ReportSortKey"/>).</summary>
    public ReportSortKey SortKey { get; init; } = ReportSortKey.None;

    /// <summary>Ascending when true; descending when false. Ignored when <see cref="SortKey"/> is None.</summary>
    public bool SortAscending { get; init; } = true;

    /// <summary>Inclusive lower bound on a row's magnitude in RUPEES (a user threshold, not a figure), or null.</summary>
    public decimal? FilterMinRupees { get; init; }

    /// <summary>Inclusive upper bound on a row's magnitude in RUPEES (a user threshold, not a figure), or null.</summary>
    public decimal? FilterMaxRupees { get; init; }

    /// <summary>Case-insensitive name-contains filter, or <c>null</c>/empty for none.</summary>
    public string? FilterNameContains { get; init; }

    // ---- Comparative columns (RQ-4) --------------------------------------------------------------

    /// <summary>The comparative column specs, or <c>null</c>/empty for a plain single-column report.</summary>
    public IReadOnlyList<SavedComparativeColumn>? ComparativeColumns { get; init; }

    // The compiler-generated record equality compares the columns list by REFERENCE; a deserialized view holds
    // a different list instance, so it would never equal the original. Override the two members that touch the
    // collection to compare it ELEMENT-WISE, keeping value semantics for a config tuple (used by round-trip tests).
    public bool Equals(SavedReportView? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ReportKind == other.ReportKind
            && AsOfDate == other.AsOfDate
            && PeriodFrom == other.PeriodFrom
            && PeriodTo == other.PeriodTo
            && Detailed == other.Detailed
            && HideZeroBalances == other.HideZeroBalances
            && ShowPercentages == other.ShowPercentages
            && ClosingStock == other.ClosingStock
            && ScenarioName == other.ScenarioName
            && SortKey == other.SortKey
            && SortAscending == other.SortAscending
            && FilterMinRupees == other.FilterMinRupees
            && FilterMaxRupees == other.FilterMaxRupees
            && FilterNameContains == other.FilterNameContains
            && ColumnsEqual(ComparativeColumns, other.ComparativeColumns);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReportKind);
        hash.Add(AsOfDate);
        hash.Add(PeriodFrom);
        hash.Add(PeriodTo);
        hash.Add(Detailed);
        hash.Add(HideZeroBalances);
        hash.Add(ShowPercentages);
        hash.Add(ClosingStock);
        hash.Add(ScenarioName);
        hash.Add(SortKey);
        hash.Add(SortAscending);
        hash.Add(FilterMinRupees);
        hash.Add(FilterMaxRupees);
        hash.Add(FilterNameContains);
        if (ComparativeColumns is not null)
            foreach (var c in ComparativeColumns) hash.Add(c);
        return hash.ToHashCode();
    }

    private static bool ColumnsEqual(
        IReadOnlyList<SavedComparativeColumn>? a, IReadOnlyList<SavedComparativeColumn>? b)
    {
        if (a is null || a.Count == 0) return b is null || b.Count == 0; // null and empty are equivalent
        if (b is null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    // ---- (de)serialization -----------------------------------------------------------------------

    /// <summary>
    /// Deterministic, culture-invariant JSON options: no indentation, dates as ISO strings, decimals written
    /// as their own invariant tokens by <see cref="System.Text.Json"/> (never a binary float), enums BY NAME
    /// (stable across ordinal reshuffles), nulls omitted. Reused for every save so the stored text is stable.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Culture-invariant by construction: System.Text.Json writes DateOnly as "yyyy-MM-dd" and decimal
            // via its invariant round-trip formatter, independent of the thread culture.
        };
        // Enums are written BY NAME (stable across ordinal reshuffles). On READ, an unknown enum name — from a
        // newer build that added a value, or a corrupted config_json — must NOT throw and drop the whole saved
        // view from the list (ER-9 robustness); each tolerant converter falls back to its sensible default.
        o.Converters.Add(new TolerantEnumConverter<ClosingStockMode>(ClosingStockMode.AsPostedLedger));
        o.Converters.Add(new TolerantEnumConverter<ReportSortKey>(ReportSortKey.None));
        // Any other enum keeps the standard by-name behaviour.
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    /// <summary>Serializes this view to a deterministic, culture-invariant JSON string (config only, no figures).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes a view from JSON produced by <see cref="ToJson"/> (identical round-trip).</summary>
    public static SavedReportView FromJson(string json) =>
        JsonSerializer.Deserialize<SavedReportView>(json, JsonOptions)
        ?? throw new ArgumentException("Saved-view JSON deserialized to null.", nameof(json));
}

/// <summary>
/// One comparative column of a <see cref="SavedReportView"/> (RQ-4): a display label plus an optional period
/// window and an optional scenario NAME. Config only — it re-binds to the live company on apply and is never a
/// figure. Mirrors <see cref="ComparativeReport.ColumnSpec"/> minus the runtime <c>Options</c>/<c>Scenario</c>
/// object references (the Desktop rebuilds those from the saved primitives + the live company).
/// </summary>
public sealed record SavedComparativeColumn
{
    /// <summary>The column's display label.</summary>
    public required string Label { get; init; }

    /// <summary>The column's period window start, or <c>null</c> for the report's natural as-of.</summary>
    public DateOnly? PeriodFrom { get; init; }

    /// <summary>The column's period window end, or <c>null</c>. Set together with <see cref="PeriodFrom"/>.</summary>
    public DateOnly? PeriodTo { get; init; }

    /// <summary>The column's scenario name, or <c>null</c> for the actual books.</summary>
    public string? ScenarioName { get; init; }
}

/// <summary>
/// A forgiving by-name enum converter: writes the enum value by its name (like the standard string-enum
/// converter) but, on read, substitutes a supplied <see cref="_fallback"/> default instead of throwing when
/// the token is an UNKNOWN name (a value added by a newer build, or a corrupted config_json). This keeps a
/// single bad field from failing the whole saved view — it still loads, just with the sensible default for
/// that one option (ER-9: a saved view can never go stale, and must degrade gracefully).
/// </summary>
internal sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly T _fallback;

    public TolerantEnumConverter(T fallback) => _fallback = fallback;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Names (how views are written); a numeric ordinal is also accepted defensively — both fall back on
        // an unknown/out-of-range value rather than throwing.
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            return name is not null && Enum.TryParse<T>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : _fallback;
        }
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var ordinal))
        {
            var value = (T)Enum.ToObject(typeof(T), ordinal);
            return Enum.IsDefined(value) ? value : _fallback;
        }
        // Any other token type (null / object / etc.) → the sensible default, never an exception.
        return _fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
