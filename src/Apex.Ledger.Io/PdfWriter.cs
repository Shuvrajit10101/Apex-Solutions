using System.Globalization;
using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// A minimal, dependency-free PDF 1.4 writer. Hand-rolls the object model (catalog, page tree, pages,
/// per-page content streams, one standard-14 Helvetica font), an xref table and a trailer. Coordinates
/// are in PDF points with the origin at the bottom-left of the page (PDF's native convention).
///
/// <para>Determinism: no timestamps, no RNG, no culture-sensitive formatting — every number is written
/// with <see cref="CultureInfo.InvariantCulture"/>, so rendering the same input twice yields
/// byte-identical output (NFR / stability requirement). Metadata is de-branded: /Producer and /Creator
/// are always "Apex Solutions".</para>
///
/// <para>Usage: create a writer, call <see cref="BeginPage"/> for each page, draw with
/// <see cref="Text"/> / <see cref="Line"/>, then <see cref="Build"/> to get the bytes.</para>
/// </summary>
public sealed class PdfWriter
{
    private const string Producer = "Apex Solutions";
    private const string Creator = "Apex Solutions";

    private readonly List<PageBuffer> _pages = new();
    private PageBuffer? _current;

    /// <summary>Metadata /Title. De-branded by the caller; never contains a third-party brand.</summary>
    public string DocumentTitle { get; init; } = "Apex Solutions Report";

    /// <summary>Starts a new page of the given width/height (points) and makes it current.</summary>
    public void BeginPage(double width, double height)
    {
        _current = new PageBuffer(width, height);
        _pages.Add(_current);
    }

    /// <summary>
    /// Shows a single line of text with the regular Helvetica font at the given size, positioned by its
    /// bottom-left baseline point (x, y). Text is escaped per the PDF literal-string rules.
    /// </summary>
    public void Text(double x, double y, string text, double fontSize) => Text(x, y, text, fontSize, bold: false);

    /// <summary>
    /// Shows a single line of text with either the regular ("/F1", Helvetica) or bold ("/F2",
    /// Helvetica-Bold) font at the given size, positioned by its bottom-left baseline point (x, y).
    /// Text is WinAnsi-encoded and escaped per the PDF literal-string rules.
    /// </summary>
    public void Text(double x, double y, string text, double fontSize, bool bold)
    {
        var page = Require();
        var content = page.Content;
        content.Append("BT\n");
        content.Append(bold ? "/F2 " : "/F1 ").Append(Num(fontSize)).Append(" Tf\n");
        content.Append(Num(x)).Append(' ').Append(Num(y)).Append(" Td\n");
        content.Append('(').Append(EscapeString(text)).Append(") Tj\n");
        content.Append("ET\n");
    }

    /// <summary>Strokes a straight line from (x1,y1) to (x2,y2) at the given line width.</summary>
    public void Line(double x1, double y1, double x2, double y2, double lineWidth = 0.5)
    {
        var page = Require();
        var content = page.Content;
        content.Append(Num(lineWidth)).Append(" w\n");
        content.Append(Num(x1)).Append(' ').Append(Num(y1)).Append(" m\n");
        content.Append(Num(x2)).Append(' ').Append(Num(y2)).Append(" l\n");
        content.Append("S\n");
    }

    /// <summary>Number of pages started so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>
    /// Assembles the complete PDF document as bytes: header, all indirect objects (catalog, page-tree,
    /// font, per-page page objects + content streams, info dictionary), the cross-reference table, the
    /// trailer and the %%EOF marker. Deterministic and self-contained.
    /// </summary>
    public byte[] Build()
    {
        // Object numbering plan (1-based):
        //   1 = Catalog
        //   2 = Pages (page tree)
        //   3 = Font F1 (Helvetica, regular)
        //   4 = Font F2 (Helvetica-Bold)
        //   5 = Info dictionary
        //   then for each page i (0-based): pageObj = 6 + 2*i, contentObj = 7 + 2*i
        int pageObjStart = 6;
        int totalObjects = 5 + _pages.Count * 2;

        // Build content strings first so we can compute lengths.
        var bytes = new List<byte>();
        var offsets = new long[totalObjects + 1]; // 1-based; index 0 unused

        // Latin1 maps each char 0x00–0xFF to the same byte, so structural ASCII is unchanged while any
        // WinAnsi byte (0x80–0xFF) emitted by EscapeString into metadata strings (e.g. an em-dash in the
        // /Title) survives verbatim instead of being folded to '?'.
        void AppendAscii(string s) => bytes.AddRange(Encoding.Latin1.GetBytes(s));

        // --- header ---
        AppendAscii("%PDF-1.4\n");
        // binary comment so tools treat the file as binary; fixed bytes -> deterministic.
        bytes.AddRange(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        void StartObject(int id)
        {
            offsets[id] = bytes.Count;
            AppendAscii(id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
        }
        void EndObject() => AppendAscii("endobj\n");

        // --- 1: Catalog ---
        StartObject(1);
        AppendAscii("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObject();

        // --- 2: Pages ---
        StartObject(2);
        var kids = new StringBuilder();
        for (int i = 0; i < _pages.Count; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(pageObjStart + i * 2).Append(" 0 R");
        }
        AppendAscii("<< /Type /Pages /Count " + _pages.Count.ToString(CultureInfo.InvariantCulture)
            + " /Kids [" + kids + "] >>\n");
        EndObject();

        // --- 3: Font F1 (Helvetica, standard-14, no embedding) ---
        StartObject(3);
        AppendAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\n");
        EndObject();

        // --- 4: Font F2 (Helvetica-Bold, standard-14, no embedding) ---
        StartObject(4);
        AppendAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\n");
        EndObject();

        // --- 5: Info (de-branded metadata) ---
        StartObject(5);
        AppendAscii("<< /Producer (" + EscapeString(Producer) + ") /Creator (" + EscapeString(Creator)
            + ") /Title (" + EscapeString(DocumentTitle) + ") >>\n");
        EndObject();

        // --- per-page objects ---
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            int pageObj = pageObjStart + i * 2;
            int contentObj = pageObj + 1;

            StartObject(pageObj);
            AppendAscii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                + Num(page.Width) + " " + Num(page.Height) + "] "
                + "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> "
                + "/Contents " + contentObj.ToString(CultureInfo.InvariantCulture) + " 0 R >>\n");
            EndObject();

            // Latin1 (ISO-8859-1) maps each char 0x00–0xFF to the same byte 1:1, so the WinAnsi bytes
            // produced by EscapeString (which already emits the correct CP1252 code unit as a char) survive
            // verbatim into the content stream. ASCII would have folded 0x80–0xFF back to '?'.
            var contentBytes = Encoding.Latin1.GetBytes(page.Content.ToString());
            StartObject(contentObj);
            AppendAscii("<< /Length " + contentBytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\n");
            AppendAscii("stream\n");
            bytes.AddRange(contentBytes);
            AppendAscii("endstream\n");
            EndObject();
        }

        // --- xref ---
        long xrefOffset = bytes.Count;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append("0 ").Append((totalObjects + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        xref.Append("0000000000 65535 f \n");
        for (int id = 1; id <= totalObjects; id++)
        {
            xref.Append(offsets[id].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        AppendAscii(xref.ToString());

        // --- trailer ---
        AppendAscii("trailer\n");
        AppendAscii("<< /Size " + (totalObjects + 1).ToString(CultureInfo.InvariantCulture)
            + " /Root 1 0 R /Info 5 0 R >>\n");
        AppendAscii("startxref\n");
        AppendAscii(xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n");
        AppendAscii("%%EOF\n");

        return bytes.ToArray();
    }

    private PageBuffer Require() =>
        _current ?? throw new InvalidOperationException("Call BeginPage before drawing.");

    /// <summary>Formats a coordinate/number invariantly, trimming trailing zeros for stable bytes.</summary>
    private static string Num(double v)
    {
        // Round to 3 dp to avoid float noise, then strip trailing zeros for compact, stable output.
        double r = Math.Round(v, 3, MidpointRounding.AwayFromZero);
        if (r == 0) r = 0; // normalize -0
        string s = r.ToString("0.###", CultureInfo.InvariantCulture);
        return s;
    }

    /// <summary>
    /// Fallback byte for a Unicode character that has no WinAnsi (CP1252) representation (e.g. the Indian
    /// Rupee sign ₹ U+20B9). Deterministic; the UI projector already folds such glyphs to ASCII (₹ → "Rs.")
    /// before they reach the writer, so this is only a last-resort guard.
    /// </summary>
    private const char WinAnsiFallback = '?';

    /// <summary>
    /// Escapes a string for a PDF literal string AND encodes it to WinAnsi (Windows-1252 / CP1252): every
    /// output char is a code unit in 0x00–0xFF whose value IS its WinAnsi byte (the content/metadata is later
    /// serialized with Latin1 so those bytes survive verbatim). Backslash-escapes '(' ')' '\' and maps the
    /// common control chars. ASCII (0x20–0x7E) is identity; the Latin-1 range (0xA0–0xFF) is identity; the
    /// CP1252-specific glyphs in 0x80–0x9F (em/en dash, curly quotes, bullet, ellipsis, …) are mapped to their
    /// real WinAnsi byte so they render as the correct glyph rather than '?'. Characters not representable in
    /// CP1252 fall back to <see cref="WinAnsiFallback"/>. Deterministic and byte-stable.
    /// </summary>
    internal static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c is >= (char)32 and <= (char)126)
                        sb.Append(c);                       // printable ASCII: identity
                    else if (c < 32)
                        sb.Append(WinAnsiFallback);         // other control chars: drop
                    else
                        sb.Append((char)ToWinAnsi(c));      // non-ASCII: WinAnsi byte (or fallback)
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Maps a Unicode code unit to its WinAnsi (CP1252) byte. Returns <see cref="WinAnsiFallback"/> for any
    /// character with no CP1252 representation. Covers the Latin-1 range (identity) plus the CP1252-specific
    /// assignments in 0x80–0x9F.
    /// </summary>
    internal static int ToWinAnsi(char c)
    {
        // Latin-1 range (0x00A0–0x00FF): WinAnsi byte == Unicode code point.
        if (c is >= (char)0x00A0 and <= (char)0x00FF)
            return c;

        // CP1252 code points 0x80–0x9F carry printable glyphs (unlike Latin-1's C1 controls).
        return c switch
        {
            '€' => 0x80, // € euro
            '‚' => 0x82, // ‚ single low-9 quote
            'ƒ' => 0x83, // ƒ florin
            '„' => 0x84, // „ double low-9 quote
            '…' => 0x85, // … horizontal ellipsis
            '†' => 0x86, // † dagger
            '‡' => 0x87, // ‡ double dagger
            'ˆ' => 0x88, // ˆ modifier circumflex
            '‰' => 0x89, // ‰ per-mille
            'Š' => 0x8A, // Š S caron
            '‹' => 0x8B, // ‹ single left angle quote
            'Œ' => 0x8C, // Œ OE ligature
            'Ž' => 0x8E, // Ž Z caron
            '‘' => 0x91, // ' left single quote
            '’' => 0x92, // ' right single quote
            '“' => 0x93, // " left double quote
            '”' => 0x94, // " right double quote
            '•' => 0x95, // • bullet
            '–' => 0x96, // – en dash
            '—' => 0x97, // — em dash
            '˜' => 0x98, // ˜ small tilde
            '™' => 0x99, // ™ trademark
            'š' => 0x9A, // š s caron
            '›' => 0x9B, // › single right angle quote
            'œ' => 0x9C, // œ oe ligature
            'ž' => 0x9E, // ž z caron
            'Ÿ' => 0x9F, // Ÿ Y diaeresis
            _ => WinAnsiFallback,
        };
    }

    /// <summary>Approximate width of a Helvetica string at a font size, in points (for right/center align).</summary>
    internal static double MeasureHelvetica(string s, double fontSize)
    {
        // Average-width heuristic in 1/1000 em. Good enough for column alignment of tabular reports;
        // deterministic and font-file-free.
        double units = 0;
        foreach (char c in s)
            units += HelveticaWidth(c);
        return units / 1000.0 * fontSize;
    }

    private static int HelveticaWidth(char c)
    {
        // Coarse per-class widths (1/1000 em) — narrow punctuation/digits, wide caps.
        if (c == ' ') return 278;
        if (c is '.' or ',' or ':' or ';' or '\'' or '|' or '!' or 'i' or 'l' or 'j') return 250;
        if (c is >= '0' and <= '9') return 556;
        if (c is '-' or '/') return 333;
        if (c is '–' or '—') return 556;   // en/em dash are wide in Helvetica
        if (c is '…') return 1000;          // ellipsis glyph ~ 3 dots
        if (c is >= 'A' and <= 'Z') return 667;
        return 500; // lower-case & everything else
    }

    /// <summary>The three-dot ASCII ellipsis appended when a cell is truncated to fit its column.</summary>
    internal const string Ellipsis = "...";

    /// <summary>
    /// Truncates <paramref name="text"/> so that its measured Helvetica width at <paramref name="fontSize"/>
    /// does not exceed <paramref name="maxWidth"/> points, appending an ellipsis when it has to cut. Returns
    /// the original text when it already fits (or when <paramref name="maxWidth"/> is non-positive there is
    /// nothing sensible to draw, so an empty string). Deterministic — used to keep cell text inside its column
    /// box so long labels never overflow into the neighbouring column or past the page edge.
    /// </summary>
    internal static string FitToWidth(string text, double maxWidth, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (maxWidth <= 0) return string.Empty;
        if (MeasureHelvetica(text, fontSize) <= maxWidth) return text;

        double ellipsisW = MeasureHelvetica(Ellipsis, fontSize);
        // If even the ellipsis alone will not fit, emit as many leading chars as physically fit (no ellipsis).
        if (ellipsisW > maxWidth)
        {
            var raw = new StringBuilder();
            double w = 0;
            foreach (char c in text)
            {
                double cw = MeasureHelvetica(c.ToString(), fontSize);
                if (w + cw > maxWidth) break;
                raw.Append(c);
                w += cw;
            }
            return raw.ToString();
        }

        double budget = maxWidth - ellipsisW;
        var sb = new StringBuilder();
        double used = 0;
        foreach (char c in text)
        {
            double cw = MeasureHelvetica(c.ToString(), fontSize);
            if (used + cw > budget) break;
            sb.Append(c);
            used += cw;
        }
        return sb.Append(Ellipsis).ToString();
    }

    private sealed class PageBuffer
    {
        public double Width { get; }
        public double Height { get; }
        public StringBuilder Content { get; } = new();

        public PageBuffer(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }
}
