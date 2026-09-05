using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to a self-contained HTML document (W2-25 / census 13.6 —
/// <i>HTML (Web-Publishing)</i> <c>.html</c>): one table, a caption band from the report title, a
/// <c>&lt;thead&gt;</c> carrying the column captions, and one <c>&lt;tr&gt;</c> per row. Number cells are
/// right-aligned and carry the exact invariant figure at the decimal's own scale, so a reader sees the same
/// paisa the grid showed (RQ-15 fidelity) and a copy-paste into a spreadsheet still parses as a number.
///
/// <para>Self-contained: the stylesheet is inlined, there are no external references, no scripts and no images,
/// so the file opens correctly from a mail attachment or a file share. Deterministic and byte-stable — no clock,
/// no culture leak. Every text value is de-branded (ER-11) and then HTML-escaped, in that order, so a party name
/// can never inject markup.</para>
///
/// <para><b>Divergence, ours (ruling 9).</b> The vendor's own HTML markup is not stated by any admissible
/// source. The document shape below — the element structure, the class names <c>t</c>/<c>n</c>/<c>tot</c>/
/// <c>hdr</c>, and the inlined stylesheet — is ours and can never join the compared set.</para>
/// </summary>
public static class HtmlReportWriter
{
    /// <summary>Serializes the model to a UTF-8 HTML document (no BOM — the charset meta declares the encoding).</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string title = Escape(Debrand.Text(export.Title));
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\r\n");
        sb.Append("<html lang=\"en\">\r\n");
        sb.Append("<head>\r\n");
        sb.Append("<meta charset=\"utf-8\">\r\n");
        sb.Append("<title>").Append(title).Append("</title>\r\n");
        sb.Append("<style>\r\n").Append(Stylesheet).Append("</style>\r\n");
        sb.Append("</head>\r\n");
        sb.Append("<body>\r\n");
        sb.Append("<h1>").Append(title).Append("</h1>\r\n");
        sb.Append("<table>\r\n");

        // ---- header band ----
        sb.Append("<thead>\r\n<tr>");
        foreach (var col in export.Columns)
            sb.Append("<th class=\"").Append(ClassOf(col.Type)).Append("\">")
              .Append(Escape(Debrand.Text(col.Header))).Append("</th>");
        sb.Append("</tr>\r\n</thead>\r\n");

        // ---- body ----
        sb.Append("<tbody>\r\n");
        int colCount = export.Columns.Count;
        foreach (var row in export.Rows)
        {
            sb.Append("<tr");
            if (row.IsTotal) sb.Append(" class=\"tot\"");
            else if (row.IsHeader) sb.Append(" class=\"hdr\"");
            sb.Append('>');
            for (int i = 0; i < colCount; i++)
            {
                var cell = i < row.Cells.Count ? row.Cells[i] : TabularCell.Empty;
                // The COLUMN decides alignment (a text label parked in a number column still right-aligns with
                // its column), exactly as the PDF and XLSX writers lay the same model out.
                sb.Append("<td class=\"").Append(ClassOf(export.Columns[i].Type)).Append("\">")
                  .Append(CellHtml(cell)).Append("</td>");
            }
            sb.Append("</tr>\r\n");
        }
        sb.Append("</tbody>\r\n");

        sb.Append("</table>\r\n");
        sb.Append("</body>\r\n");
        sb.Append("</html>\r\n");

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    private const string Stylesheet =
        "body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;margin:16px;color:#111}\r\n" +
        "h1{font-size:16px;margin:0 0 10px 0}\r\n" +
        "table{border-collapse:collapse}\r\n" +
        "th,td{border:1px solid #bbb;padding:3px 7px;vertical-align:top}\r\n" +
        "th{background:#eee;text-align:left}\r\n" +
        "th.n,td.n{text-align:right;white-space:nowrap}\r\n" +
        "tr.tot td{font-weight:bold;border-top:2px solid #555}\r\n" +
        "tr.hdr td{font-weight:bold;background:#f6f6f6}\r\n";

    private static string ClassOf(CellType type) => type == CellType.Number ? "n" : "t";

    /// <summary>A number cell renders its exact invariant figure; a text cell is de-branded, escaped, and its
    /// embedded newlines become <c>&lt;br&gt;</c> so a multi-line address survives as multiple lines.</summary>
    private static string CellHtml(TabularCell cell)
    {
        if (cell.Type == CellType.Number) return cell.NumberText;
        string text = Escape(TabularDebrand.Cell(cell.TextValue));
        return text.Replace("\r\n", "<br>").Replace("\r", "<br>").Replace("\n", "<br>");
    }

    /// <summary>Escapes the five characters that can change HTML's meaning. Applied AFTER de-branding so the
    /// brand cannot be smuggled through an entity.</summary>
    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
