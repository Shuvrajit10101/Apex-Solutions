using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Composes a byte-stable RFC-5322 / MIME <c>multipart/mixed</c> email (<c>.eml</c>) carrying a text body
/// and one or more attachments (RQ-25/26). Framework-agnostic and deterministic: the <c>Date</c> and
/// <c>Message-ID</c> are supplied by the caller and the MIME boundary is fixed, so the same
/// <see cref="EmlMessage"/> always re-composes to the identical bytes. There is NO clock, NO RNG and NO
/// network here — live SMTP send is deferred; the UI persists the returned bytes or hands them to the OS
/// mail client.
/// </summary>
public static class EmlComposer
{
    // A fixed boundary keeps the output deterministic (ER-8 forbids RNG). It is deliberately long and
    // dashed so it cannot collide with base64 payload lines (base64 alphabet excludes '-').
    private const string Boundary = "----=_ApexSolutions_Boundary_000000000000";
    private const string Crlf = "\r\n";

    // RFC 5322: a line (excluding the CRLF) MUST NOT exceed 998 octets (hard limit) and SHOULD stay at
    // or below 78 (soft limit). We fold at the soft limit where feasible and never cross the hard limit.
    private const int SoftLineLimit = 78;

    // RFC 2047: an encoded-word (the whole "=?charset?B?..?=" token) MUST NOT exceed 75 characters.
    private const int MaxEncodedWord = 75;

    /// <summary>
    /// Builds the complete <c>.eml</c> for <paramref name="message"/>. Header <i>values</i> that contain
    /// non-ASCII are RFC-2047 (base64) encoded and split into &lt;=75-char encoded-words; ASCII header text
    /// has CR/LF/control chars stripped (header-injection defence) and structural fields (addr-spec, Date,
    /// Message-ID) are rejected if they carry such chars. Long header lines are folded to the RFC-5322
    /// limits. The text body is UTF-8 base64 (wrapped at 76 columns); each attachment is base64 wrapped at
    /// 76 columns with an <c>attachment</c> disposition (non-ASCII file names encoded per RFC 2231).
    /// </summary>
    public static byte[] Compose(EmlMessage message)
    {
        var sb = new StringBuilder();

        // -------- top-level headers --------
        sb.Append("MIME-Version: 1.0").Append(Crlf);
        // Date and Message-ID are structural single-token fields: an embedded CR/LF/control would let a
        // caller inject headers, so we reject rather than silently mangle them.
        sb.Append("Date: ").Append(RequireNoControl(message.Date, nameof(message.Date))).Append(Crlf);
        sb.Append("Message-ID: ").Append(RequireNoControl(message.MessageId, nameof(message.MessageId))).Append(Crlf);
        // Address lists join atoms with a comma; encoded-word sequences (Subject) join with whitespace only.
        sb.Append(FoldHeader("From", new[] { FormatAddress(message.From) }, comma: true));
        sb.Append(FoldHeader("To", FormatAddressAtoms(message.To), comma: true));
        if (message.Cc.Count > 0)
            sb.Append(FoldHeader("Cc", FormatAddressAtoms(message.Cc), comma: true));
        sb.Append(FoldHeader("Subject", EncodeHeaderAtoms(message.Subject), comma: false));
        sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(Boundary).Append('"').Append(Crlf);
        sb.Append(Crlf);

        // A short human-readable preamble (ignored by MIME clients, harmless to a raw reader).
        sb.Append("This is a multi-part message in MIME format.").Append(Crlf);

        // -------- text/plain body part --------
        sb.Append("--").Append(Boundary).Append(Crlf);
        sb.Append("Content-Type: text/plain; charset=utf-8").Append(Crlf);
        sb.Append("Content-Transfer-Encoding: base64").Append(Crlf);
        sb.Append(Crlf);
        sb.Append(WrapBase64(Encoding.UTF8.GetBytes(message.Body)));
        sb.Append(Crlf);

        // -------- attachment parts --------
        foreach (var att in message.Attachments)
        {
            sb.Append("--").Append(Boundary).Append(Crlf);
            sb.Append("Content-Type: ").Append(RequireNoControl(att.MimeType, "MimeType")).Append(Crlf);
            sb.Append("Content-Transfer-Encoding: base64").Append(Crlf);
            sb.Append(FormatContentDisposition(att.FileName));
            sb.Append(Crlf);
            sb.Append(WrapBase64(att.Content));
            sb.Append(Crlf);
        }

        // -------- closing delimiter --------
        sb.Append("--").Append(Boundary).Append("--").Append(Crlf);

        // The header text is ASCII (encoded-words fold non-ASCII); base64 bodies are ASCII. Latin-1 keeps
        // every char as a single byte so the produced bytes are exactly what we assembled.
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ---- address formatting ----

    // Each address becomes a single foldable atom; the folder inserts CRLF+WSP between atoms as needed.
    private static string[] FormatAddressAtoms(IReadOnlyList<EmlAddress> addresses)
    {
        var parts = new string[addresses.Count];
        for (int i = 0; i < addresses.Count; i++) parts[i] = FormatAddress(addresses[i]);
        return parts;
    }

    private static string FormatAddress(EmlAddress a)
    {
        // An addr-spec is structural: a CR/LF/control there is a header-injection attempt → reject.
        string addr = RequireNoControl(a.Address, "Address");
        if (string.IsNullOrEmpty(a.DisplayName))
            return addr;
        // "Display Name <addr>", with the display name RFC-2047 encoded when non-ASCII (else CR/LF/control
        // stripped). Join the possibly-multiple encoded-words with a space so the whole phrase is one atom.
        return string.Join(" ", EncodeHeaderAtoms(a.DisplayName)) + " <" + addr + ">";
    }

    // ---- RFC 2047 encoded-word (base64) for non-ASCII header values ----

    /// <summary>
    /// Turns a free-text header value into one or more foldable atoms. ASCII text is stripped of
    /// CR/LF/NUL/control chars (header-injection defence) and returned as a single atom. Non-ASCII text is
    /// RFC-2047 base64 encoded and split into multiple encoded-words each &lt;=75 chars, on UTF-8 character
    /// boundaries so multi-byte chars are never split across words.
    /// </summary>
    private static string[] EncodeHeaderAtoms(string value)
    {
        if (IsAscii(value))
            return new[] { StripControl(value) };

        // Budget for the base64 payload inside one "=?utf-8?B?<payload>?=" word: 75 - overhead.
        const string prefix = "=?utf-8?B?";
        const string suffix = "?=";
        int overhead = prefix.Length + suffix.Length;           // 12
        int maxB64 = MaxEncodedWord - overhead;                 // 63 base64 chars
        int maxB64Multiple4 = maxB64 - (maxB64 % 4);            // 60 → whole base64 quanta only
        int maxRawBytes = maxB64Multiple4 / 4 * 3;              // 45 UTF-8 bytes per word

        var atoms = new List<string>();
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        int pos = 0;
        while (pos < utf8.Length)
        {
            // Take up to maxRawBytes bytes but never split a UTF-8 multi-byte sequence.
            int take = System.Math.Min(maxRawBytes, utf8.Length - pos);
            take = AdjustToCharBoundary(utf8, pos, take);
            string b64 = Convert.ToBase64String(utf8, pos, take);
            atoms.Add(prefix + b64 + suffix);
            pos += take;
        }
        return atoms.ToArray();
    }

    // Shrink <paramref name="take"/> so the window [pos, pos+take) does not end in the middle of a UTF-8
    // multi-byte sequence. Continuation bytes are 10xxxxxx (0x80..0xBF); a lead byte is anything else.
    private static int AdjustToCharBoundary(byte[] utf8, int pos, int take)
    {
        if (pos + take >= utf8.Length) return take;             // end of data — nothing to split
        while (take > 0 && (utf8[pos + take] & 0xC0) == 0x80)   // next byte is a continuation byte
            take--;                                             // back up until we're at a lead byte
        return take == 0 ? System.Math.Min(1, utf8.Length - pos) : take;
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s) if (c > 0x7F) return false;
        return true;
    }

    // ---- header-injection defence ----

    // Remove CR, LF, NUL and other C0/C1 control chars from free-text header values so they cannot break
    // out of the current header and inject a new one (e.g. a hidden "Bcc:").
    private static string StripControl(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (c is not ('\r' or '\n' or '\0') && !(c < 0x20 || c == 0x7F))
                sb.Append(c);
        return sb.ToString();
    }

    // For a STRUCTURAL single-token header value (addr-spec, Date, Message-ID, MIME type) an embedded
    // CR/LF/control is invalid and only ever appears in an injection attempt → reject loudly.
    private static string RequireNoControl(string value, string paramName)
    {
        foreach (char c in value)
            if (c is '\r' or '\n' or '\0' || c < 0x20 || c == 0x7F)
                throw new ArgumentException(
                    $"{paramName} contains a control character (CR/LF/NUL) and would inject a header.",
                    paramName);
        return value;
    }

    // ---- RFC 5322 header folding ----

    // Build "Name: atom1 atom2 ..." folding with CRLF+WSP so no physical line crosses the 998-octet hard
    // limit and, where feasible, stays within the 78-octet soft limit. For an address list (comma=true)
    // atoms are separated by a comma; for an encoded-word sequence (a Subject, comma=false) atoms are
    // separated only by the folding whitespace (RFC 2047: adjacent encoded-words separated solely by WSP
    // re-join on decode with the WSP dropped — a comma there would corrupt the value). A fold point is
    // inserted BEFORE an atom; a single atom shorter than the hard limit therefore always fits on its own
    // continuation line. Returns the header terminated by a trailing CRLF.
    private static string FoldHeader(string name, IReadOnlyList<string> atoms, bool comma)
    {
        string sep = comma ? "," : "";
        var sb = new StringBuilder();
        sb.Append(name).Append(':');
        int lineLen = name.Length + 1;                          // current physical line length so far

        for (int i = 0; i < atoms.Count; i++)
        {
            string atom = atoms[i];
            bool notLast = i < atoms.Count - 1;
            // The chunk we are about to append is " atom" (leading space) plus a trailing comma if more
            // atoms follow. We fold BEFORE the atom when it would push us past the soft limit.
            int atomWithComma = 1 + atom.Length + (notLast ? sep.Length : 0);
            if (lineLen + atomWithComma > SoftLineLimit && lineLen > name.Length + 1)
            {
                sb.Append(Crlf).Append(' ');                    // fold: CRLF + single WSP
                lineLen = 1;                                    // the leading WSP
                sb.Append(atom);
                lineLen += atom.Length;
            }
            else
            {
                sb.Append(' ').Append(atom);
                lineLen += 1 + atom.Length;
            }
            if (notLast)
            {
                sb.Append(sep);
                lineLen += sep.Length;
            }
        }
        sb.Append(Crlf);
        return sb.ToString();
    }

    // ---- Content-Disposition with RFC 2231 file-name encoding ----

    // Emit an attachment Content-Disposition. For an ASCII name we keep the classic quoted filename="...".
    // For a non-ASCII name we additionally emit an RFC-2231 filename*=UTF-8''<pct-encoded> parameter (the
    // authoritative one) plus an ASCII-fallback filename="..." for clients that ignore the extended form.
    private static string FormatContentDisposition(string rawName)
    {
        string name = StripControl(rawName);
        if (IsAscii(name))
        {
            return "Content-Disposition: attachment; filename=\"" + name.Replace("\"", "'") + "\"" + Crlf;
        }
        string ascii = AsciiFallback(name);
        string ext = Rfc2231Encode(name);
        return "Content-Disposition: attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + ext + Crlf;
    }

    // Replace every non-ASCII char with '_' so the legacy filename="..." stays ASCII-only.
    private static string AsciiFallback(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(c > 0x7F ? '_' : (c == '"' ? '\'' : c));
        return sb.ToString();
    }

    // RFC 2231 / RFC 5987 value-chars: attr-char set kept literal; everything else percent-encoded from its
    // UTF-8 bytes (upper-case hex).
    private static string Rfc2231Encode(string name)
    {
        const string attrChars = "!#$&+-.^_`|~"; // plus ALPHA/DIGIT, handled below
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            char c = (char)b;
            bool safe = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                        || (c >= '0' && c <= '9') || attrChars.IndexOf(c) >= 0;
            if (safe) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    // ---- base64 wrapped at 76 columns (RFC 2045) ----

    private static string WrapBase64(byte[] data)
    {
        // Base64FormattingOptions.InsertLineBreaks wraps at 76 chars, but uses the platform newline; we
        // normalize to CRLF so the output is byte-stable across OSes.
        string raw = Convert.ToBase64String(data);
        var sb = new StringBuilder(raw.Length + raw.Length / 76 * 2 + 2);
        for (int i = 0; i < raw.Length; i += 76)
        {
            int len = System.Math.Min(76, raw.Length - i);
            sb.Append(raw, i, len).Append(Crlf);
        }
        return sb.ToString();
    }
}
