using System.Text.RegularExpressions;

namespace Apex.Ledger.Io;

/// <summary>
/// De-branding guard (ER-11): scrubs any third-party accounting brand out of user-supplied text before it is
/// written into a produced PDF (the /Title metadata, a title override, header/footer/narration text). The
/// renderers already never emit the brand themselves; this closes the hole where a user could type it into an
/// F12 field and have it leak into the document. Case-insensitive; deterministic and byte-stable.
/// </summary>
public static partial class Debrand
{
    [GeneratedRegex("tally", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandRegex();

    /// <summary>
    /// True when <paramref name="text"/> carries the forbidden brand (case-insensitive).
    ///
    /// <para><b>Why a predicate exists beside <see cref="Text"/>.</b> <see cref="Text"/> also collapses runs of
    /// whitespace and trims — right for a field it always owns, but a caller that scrubs an existing, shipped
    /// output stream would move every byte of every clean document just to close a hole that only opens when the
    /// brand is actually present. Testing first and scrubbing only when it is keeps the clean path byte-identical
    /// (ER-13) while still closing the hole (ER-11). <see cref="ReportPdf"/> uses it for exactly that.</para>
    /// </summary>
    public static bool Contains(string? text)
        => !string.IsNullOrEmpty(text) && BrandRegex().IsMatch(text);

    /// <summary>
    /// Returns <paramref name="text"/> with every case-insensitive occurrence of the forbidden brand removed,
    /// then collapses any doubled spaces the removal left and trims the ends. Null/blank input is passed through
    /// unchanged (as an empty string for null).
    /// </summary>
    public static string Text(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        string cleaned = BrandRegex().Replace(text, string.Empty);
        // Collapse the whitespace the removal may have doubled up (e.g. a leading removed word -> " Report").
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return cleaned;
    }
}
