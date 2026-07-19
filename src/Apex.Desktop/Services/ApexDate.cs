using System;
using System.Globalization;

namespace Apex.Desktop.Services;

/// <summary>
/// The single, app-wide date contract (WI-5). Every UI date field renders through <see cref="Format"/>
/// and parses through <see cref="TryParse(string?, DateOnly, out DateOnly)"/>, so the whole app shows ONE
/// canonical spelling and reads user input ONE way.
/// <para>
/// <b>Canonical:</b> <c>dd-MMM-yyyy</c> (e.g. <c>03-Apr-2024</c>) — a 4-digit, unambiguous year (chosen over
/// Tally's <c>dd-MMM-yy</c> to remove year ambiguity in an accounting app). Storage is already ISO
/// <c>DateOnly</c>; this type governs only what the user SEES and TYPES.
/// </para>
/// <para>
/// <b>Day-first, never month-first.</b> A numeric date is read the Indian way — <c>03/04/2024</c> is
/// <b>3-Apr</b>, not 4-Mar. This is deliberate and explicit: the parser NEVER falls through to
/// <see cref="CultureInfo.InvariantCulture"/>'s <c>MM/dd</c> short-date convention (the historical silent-misread
/// bug), and it is independent of the machine's ambient culture, so it behaves identically on every CI OS.
/// </para>
/// </summary>
public static class ApexDate
{
    /// <summary>The one canonical display/echo format, app-wide.</summary>
    public const string Canonical = "dd-MMM-yyyy";

    /// <summary>
    /// The ordered, explicitly day-first ladder tried after separators (<c>/ . space</c>) are normalized to
    /// <c>-</c>. Month-name and ISO forms come first (they cannot collide with a numeric day-first date),
    /// then the numeric <c>dd-MM</c> forms, then a compact 8-digit form. <b>There is deliberately no bare
    /// <c>MM/dd</c> or ambient-culture fallback.</b>
    /// </summary>
    private static readonly string[] Ladder =
    {
        // Month-name forms (canonical first).
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMM-yy", "d-MMM-yy",
        // ISO — unambiguous, cannot collide with a day-first numeric date (4-digit lead).
        "yyyy-MM-dd", "yyyy-M-d",
        // Numeric, DAY-FIRST. 4-digit-year variants first so "03-04-2024" reads the year, not "dd-MM-yy".
        "dd-MM-yyyy", "d-M-yyyy", "dd-MM-yy", "d-M-yy",
        // Compact, no separators, day-first (e.g. 03042024).
        "ddMMyyyy",
    };

    /// <summary>Renders <paramref name="d"/> in the one canonical format (<c>dd-MMM-yyyy</c>).</summary>
    public static string Format(DateOnly d) => d.ToString(Canonical, CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders the DATE PART of a timestamp in the same canonical format, so an audit/import timestamp shown
    /// to the user reads identically to every other date on screen.
    /// </summary>
    public static string Format(DateTimeOffset ts) => Format(DateOnly.FromDateTime(ts.DateTime));

    /// <summary>
    /// The one lenient, day-first parser. Accepts the canonical <c>dd-MMM-yyyy</c>; common day-first numeric
    /// forms (<c>dd/MM/yyyy</c>, <c>dd-MM-yyyy</c>, <c>d/M/yy</c>, <c>dd.MM.yyyy</c>, compact <c>ddMMyyyy</c>);
    /// ISO <c>yyyy-MM-dd</c>; and partial input completed from <paramref name="context"/> — a bare day
    /// (<c>15</c>) takes the context's month and year, a bare day+month (<c>15/04</c>) takes the context's year.
    /// Returns <see langword="false"/> — never a wrong-but-plausible date — on anything it cannot read
    /// day-first. On <see langword="false"/> the caller MUST keep the prior value and surface a rejection
    /// (never silently discard).
    /// </summary>
    /// <param name="text">The raw user text.</param>
    /// <param name="context">Supplies the month/year for partial input (typically the field's current value).</param>
    /// <param name="date">The parsed date on success; otherwise <see langword="default"/>.</param>
    public static bool TryParse(string? text, DateOnly context, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Normalize every accepted separator to '-' so one ladder covers '/', '.', ' ' and '-'.
        var norm = Normalize(text);

        // Full (3-part / month-name / ISO / compact) forms.
        if (DateOnly.TryParseExact(norm, Ladder, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        var parts = norm.Split('-', StringSplitOptions.RemoveEmptyEntries);

        // Bare day → complete month + year from context.
        if (parts.Length == 1 && parts[0].Length is 1 or 2 && IsAllDigits(parts[0]))
        {
            var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (day >= 1 && day <= DateTime.DaysInMonth(context.Year, context.Month))
            {
                date = new DateOnly(context.Year, context.Month, day);
                return true;
            }
            return false;
        }

        // Day + month → complete the year from context, then re-run the ladder (also handles "15-Apr").
        if (parts.Length == 2)
        {
            var completed = $"{norm}-{context.Year.ToString("D4", CultureInfo.InvariantCulture)}";
            if (DateOnly.TryParseExact(completed, Ladder, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        date = default;
        return false;
    }

    /// <summary>
    /// Convenience overload for fields with no natural context (partial input completes from today). Prefer
    /// the context overload on working-date fields so a bare day completes within the date being edited.
    /// </summary>
    public static bool TryParse(string? text, out DateOnly date) =>
        TryParse(text, DateOnly.FromDateTime(DateTime.Today), out date);

    /// <summary>The one rejection message, naming the canonical format the whole app agrees on.</summary>
    public static string ErrorFor(string? input)
    {
        var shown = (input ?? string.Empty).Trim();
        return shown.Length == 0
            ? $"Enter a date in {Canonical} format (e.g. 01-Apr-2020)."
            : $"\"{shown}\" is not a valid date. Use {Canonical} (e.g. 01-Apr-2020) or a day-first d/M/yyyy.";
    }

    private static string Normalize(string text)
    {
        var t = text.Trim();
        var buf = new char[t.Length];
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            buf[i] = c is '/' or '.' or ' ' ? '-' : c;
        }
        return new string(buf);
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
            if (c is < '0' or > '9') return false;
        return true;
    }
}
