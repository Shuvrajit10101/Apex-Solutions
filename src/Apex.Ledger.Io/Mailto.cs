using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Builds an RFC-6068 <c>mailto:</c> URI for a quick, attachment-less compose (RQ-25) — the UI can hand this
/// to the OS default mail client. Deterministic and percent-encoded; no clock, no RNG. (Attachments cannot
/// travel in a mailto: URI — for those the UI writes a <c>.eml</c> via <see cref="EmlComposer"/>.)
/// </summary>
public static class Mailto
{
    /// <summary>
    /// Assembles <c>mailto:to[,to]?cc=..&amp;subject=..&amp;body=..</c>. The <c>to</c> addresses form the URI
    /// path (a comma separator between multiple addresses is percent-encoded to keep the URI unambiguous);
    /// <c>cc</c>/<c>subject</c>/<c>body</c> are added as percent-encoded query fields only when supplied.
    /// </summary>
    public static string Build(
        IReadOnlyList<string> to,
        IReadOnlyList<string>? cc,
        string? subject,
        string? body)
    {
        var sb = new StringBuilder("mailto:");

        // 'to' sits in the path. '@' and '.' are allowed there literally; a comma between addresses is
        // percent-encoded (%2C) so it is not read as a list terminator by lenient clients.
        for (int i = 0; i < to.Count; i++)
        {
            if (i > 0) sb.Append("%2C");
            sb.Append(EncodePath(to[i]));
        }

        var query = new List<string>();
        if (cc is { Count: > 0 })
            query.Add("cc=" + Encode(string.Join(",", cc)));
        if (!string.IsNullOrEmpty(subject))
            query.Add("subject=" + Encode(subject));
        if (!string.IsNullOrEmpty(body))
            query.Add("body=" + Encode(body));

        if (query.Count > 0)
            sb.Append('?').Append(string.Join("&", query));

        return sb.ToString();
    }

    // Query-field encoding (cc/subject/body): percent-encode everything outside the RFC-6068 "unreserved"
    // set (ALPHA / DIGIT / "-" / "." / "_" / "~"). Reserved characters — '@', '&', '/', ',', space and
    // control bytes — are encoded so the query cannot be mis-parsed.
    private static string Encode(string value) => PercentEncode(value, keepAt: false);

    // Path encoding for a 'to' addr-spec: '@' is legal in the mailto path, so keep it literal; encode the rest.
    private static string EncodePath(string value) => PercentEncode(value, keepAt: true);

    private static string PercentEncode(string value, bool keepAt)
    {
        var sb = new StringBuilder(value.Length * 2);
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            bool unreserved =
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c == '-' || c == '.' || c == '_' || c == '~';
            if (unreserved || (keepAt && c == '@'))
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
