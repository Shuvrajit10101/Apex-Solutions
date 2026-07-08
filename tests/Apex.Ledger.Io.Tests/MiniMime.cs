using System.Text;
using System.Text.RegularExpressions;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// A tiny, test-only MIME parser used to independently verify the composer's output. It is intentionally
/// hand-rolled (not System.Net.Mail) so the assertions exercise the real byte structure the composer
/// produced — the required headers, the multipart boundary framing, per-part headers, and base64 decode.
/// </summary>
internal sealed class MiniMime
{
    public string ContentType { get; private init; } = "";
    public string Boundary { get; private init; } = "";
    public bool ClosingDelimiterPresent { get; private init; }
    public IReadOnlyList<MimePart> Parts { get; private init; } = System.Array.Empty<MimePart>();

    public static MiniMime Parse(byte[] eml)
    {
        // Work on the raw Latin-1 view so byte offsets and CRLFs are preserved exactly.
        string text = Encoding.Latin1.GetString(eml);
        int headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0) throw new FormatException("no header/body separator");

        string headerBlock = text.Substring(0, headerEnd);
        string body = text.Substring(headerEnd + 4);

        var headers = ParseHeaders(headerBlock);
        string ct = headers.TryGetValue("content-type", out var v) ? v : "";
        var boundaryMatch = Regex.Match(ct, "boundary=\"?([^\";]+)\"?");
        if (!boundaryMatch.Success) throw new FormatException("no multipart boundary");
        string boundary = boundaryMatch.Groups[1].Value;

        string delim = "--" + boundary;
        string closing = "--" + boundary + "--";
        bool closingPresent = body.Contains(closing);

        // Split on the boundary delimiter lines.
        var parts = new List<MimePart>();
        string[] segments = body.Split(new[] { delim }, StringSplitOptions.None);
        foreach (string seg in segments)
        {
            // Skip the preamble (before first boundary) and the closing "--\r\n" epilogue.
            if (seg.StartsWith("--")) continue;            // this is the closing delimiter tail
            string s = seg;
            if (s.StartsWith("\r\n")) s = s.Substring(2);   // strip the CRLF that follows the boundary
            else continue;                                  // not a real part (e.g. leading preamble)
            int he = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (he < 0) continue;
            var ph = ParseHeaders(s.Substring(0, he));
            string partBody = s.Substring(he + 4);
            // Trim the trailing CRLF that precedes the next boundary delimiter.
            if (partBody.EndsWith("\r\n")) partBody = partBody.Substring(0, partBody.Length - 2);
            parts.Add(new MimePart(ph, partBody));
        }

        return new MiniMime
        {
            ContentType = ct,
            Boundary = boundary,
            ClosingDelimiterPresent = closingPresent,
            Parts = parts,
        };
    }

    private static Dictionary<string, string> ParseHeaders(string block)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in block.Split("\r\n"))
        {
            int c = line.IndexOf(':');
            if (c <= 0) continue;
            map[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
        }
        return map;
    }

    /// <summary>Decodes RFC-2047 base64 encoded-words (=?utf-8?B?..?=) in a header value.</summary>
    public static string DecodeEncodedWords(string headerValue)
    {
        // RFC 2047: when two encoded-words are separated only by linear whitespace, that whitespace is
        // dropped on decode (so a split multi-word value re-joins seamlessly).
        string joined = Regex.Replace(headerValue, @"\?=\s+=\?", "?==?");
        return Regex.Replace(joined, @"=\?utf-8\?B\?([^?]*)\?=", m =>
            Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)),
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Unfolds RFC-5322 folded headers: a CRLF immediately followed by a space or tab is folding
    /// whitespace and is replaced by that single WSP (the logical value is the physical lines joined with
    /// the leading WSP retained). Returns the whole message text with headers unfolded.
    /// </summary>
    public static string Unfold(byte[] eml)
    {
        string text = Encoding.Latin1.GetString(eml);
        return Regex.Replace(text, "\r\n([ \t])", "$1");
    }

    /// <summary>Returns the raw physical lines of the message (split on CRLF, no unfolding).</summary>
    public static IReadOnlyList<string> RawLines(byte[] eml) =>
        Encoding.Latin1.GetString(eml).Split("\r\n");

    internal sealed class MimePart
    {
        private readonly Dictionary<string, string> _headers;
        private readonly string _body;

        public MimePart(Dictionary<string, string> headers, string body)
        {
            _headers = headers;
            _body = body;
        }

        public string ContentType => _headers.TryGetValue("content-type", out var v) ? v : "";
        public string TransferEncoding => _headers.TryGetValue("content-transfer-encoding", out var v) ? v : "";

        public IReadOnlyList<string> RawBodyLines =>
            _body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        public byte[] DecodeBase64()
        {
            string joined = _body.Replace("\r\n", "").Replace("\n", "").Trim();
            return Convert.FromBase64String(joined);
        }
    }
}
