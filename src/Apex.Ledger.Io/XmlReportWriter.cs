using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to XML (W2-25 / census 13.6 — <i>XML (Data Interchange)</i>
/// <c>.xml</c>): a <c>&lt;Report&gt;</c> root carrying the title, a <c>&lt;Columns&gt;</c> declaration naming
/// each caption and its cell kind, and a <c>&lt;Rows&gt;</c> body of <c>&lt;Cell&gt;</c> elements that name
/// their column, so a consumer can read the file by column NAME rather than by position.
///
/// <para>Number cells carry the exact invariant figure at the decimal's own scale (money to the paisa), so the
/// interchange file states the same figures the grid showed (RQ-15 fidelity). Deterministic and byte-stable —
/// no clock, no culture leak. Text is de-branded (ER-11) and then escaped, in that order.</para>
///
/// <para><b>This is NOT the whole-company canonical XML.</b> <see cref="CanonicalXml"/> exports a company file
/// (census 13.3) and is a different surface with a different, versioned contract. This writer exports one
/// REPORT and must never be confused for it.</para>
///
/// <para><b>Divergence, ours (ruling 9).</b> The vendor's element names and document shape are not stated by any
/// admissible source; the shape below is ours and can never join the compared set.</para>
/// </summary>
public static class XmlReportWriter
{
    /// <summary>Serializes the model to UTF-8 XML bytes (no BOM — the declaration states the encoding).</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
        sb.Append("<Report title=\"").Append(Escape(Debrand.Text(export.Title))).Append("\">\r\n");

        // ---- column declaration ----
        sb.Append("  <Columns>\r\n");
        foreach (var col in export.Columns)
            sb.Append("    <Column header=\"").Append(Escape(Debrand.Text(col.Header)))
              .Append("\" type=\"").Append(col.Type).Append("\" />\r\n");
        sb.Append("  </Columns>\r\n");

        // ---- body ----
        sb.Append("  <Rows>\r\n");
        int colCount = export.Columns.Count;
        foreach (var row in export.Rows)
        {
            sb.Append("    <Row");
            if (row.IsHeader) sb.Append(" header=\"true\"");
            if (row.IsTotal) sb.Append(" total=\"true\"");
            sb.Append(">\r\n");
            for (int i = 0; i < colCount; i++)
            {
                var cell = i < row.Cells.Count ? row.Cells[i] : TabularCell.Empty;
                string column = Escape(Debrand.Text(export.Columns[i].Header));
                if (cell.Type == CellType.Number)
                {
                    sb.Append("      <Cell column=\"").Append(column).Append("\" type=\"Number\">")
                      .Append(cell.NumberText).Append("</Cell>\r\n");
                }
                else
                {
                    string text = Escape(TabularDebrand.Cell(cell.TextValue));
                    if (text.Length == 0)
                        sb.Append("      <Cell column=\"").Append(column).Append("\" type=\"Text\" />\r\n");
                    else
                        sb.Append("      <Cell column=\"").Append(column).Append("\" type=\"Text\">")
                          .Append(text).Append("</Cell>\r\n");
                }
            }
            sb.Append("    </Row>\r\n");
        }
        sb.Append("  </Rows>\r\n");
        sb.Append("</Report>\r\n");

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    /// <summary>Escapes the five XML predefined entities. The apostrophe and quote are escaped everywhere (not
    /// only inside attributes) so one routine serves both positions and the output is safe wherever it lands.</summary>
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
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
