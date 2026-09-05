using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to JSON (W2-25 / census 13.6 — <i>JSON (Data Exchange)</i>
/// <c>.json</c>): an object carrying the report <c>title</c>, a <c>columns</c> array declaring each caption and
/// its cell kind, and a <c>rows</c> array of <c>cells</c>.
///
/// <para><b>A number cell is a bare JSON number, not a string</b>, emitted at the decimal's own scale
/// (<c>105000.00</c>, <c>3.3333</c>), so a consumer sums it without re-parsing and the file states the same
/// paisa the grid showed (RQ-15 fidelity). JSON numbers are arbitrary-precision decimal literals per RFC 8259
/// §6, so the trailing zeros of a money figure survive the text form even though a double would lose them.</para>
///
/// <para>Deterministic and byte-stable — no clock, no culture leak. Text is de-branded (ER-11) and then escaped,
/// in that order. Output is UTF-8 with no BOM (RFC 8259 §8.1 forbids one).</para>
///
/// <para><b>This is NOT the whole-company canonical JSON.</b> <see cref="CanonicalJson"/> exports a company file
/// (census 13.3) under a different, versioned contract. This writer exports one REPORT.</para>
///
/// <para><b>Divergence, ours (ruling 9).</b> The vendor's property names and object shape are not stated by any
/// admissible source; the shape below is ours and can never join the compared set.</para>
/// </summary>
public static class JsonReportWriter
{
    /// <summary>Serializes the model to UTF-8 JSON bytes (no BOM).</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var sb = new StringBuilder();
        sb.Append("{\r\n");
        sb.Append("  \"title\": \"").Append(Escape(Debrand.Text(export.Title))).Append("\",\r\n");

        // ---- column declaration ----
        sb.Append("  \"columns\": [\r\n");
        for (int i = 0; i < export.Columns.Count; i++)
        {
            var col = export.Columns[i];
            sb.Append("    { \"header\": \"").Append(Escape(Debrand.Text(col.Header)))
              .Append("\", \"type\": \"").Append(TypeName(col.Type)).Append("\" }");
            sb.Append(i == export.Columns.Count - 1 ? "\r\n" : ",\r\n");
        }
        sb.Append("  ],\r\n");

        // ---- body ----
        sb.Append("  \"rows\": [\r\n");
        int colCount = export.Columns.Count;
        for (int r = 0; r < export.Rows.Count; r++)
        {
            var row = export.Rows[r];
            sb.Append("    { ");
            if (row.IsHeader) sb.Append("\"header\": true, ");
            if (row.IsTotal) sb.Append("\"total\": true, ");
            sb.Append("\"cells\": [");
            for (int i = 0; i < colCount; i++)
            {
                if (i > 0) sb.Append(", ");
                var cell = i < row.Cells.Count ? row.Cells[i] : TabularCell.Empty;
                if (cell.Type == CellType.Number)
                    sb.Append("{ \"type\": \"number\", \"value\": ").Append(cell.NumberText).Append(" }");
                else
                    sb.Append("{ \"type\": \"text\", \"value\": \"")
                      .Append(Escape(TabularDebrand.Cell(cell.TextValue))).Append("\" }");
            }
            sb.Append("] }");
            sb.Append(r == export.Rows.Count - 1 ? "\r\n" : ",\r\n");
        }
        sb.Append("  ]\r\n");
        sb.Append("}\r\n");

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    private static string TypeName(CellType type) => type == CellType.Number ? "number" : "text";

    /// <summary>RFC 8259 §7 string escaping: the two mandatory escapes (<c>"</c> and <c>\</c>) plus the named
    /// short forms, with every remaining control character below U+0020 emitted as <c>\uXXXX</c>. Markup
    /// metacharacters are NOT escaped — JSON has no markup — so a party name reads as it was typed.</summary>
    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4",
                        System.Globalization.CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
