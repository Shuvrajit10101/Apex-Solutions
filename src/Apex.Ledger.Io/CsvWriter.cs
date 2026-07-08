using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to RFC-4180 CSV bytes (RQ-18). A field that contains a comma,
/// a double-quote, a CR or an LF is wrapped in double-quotes with any embedded quote doubled; records are
/// separated by CRLF. Output is UTF-8 encoded <b>with a BOM</b> so Excel opens it in Unicode (the rupee sign
/// and any non-ASCII label survive). Number cells format invariant at their OWN natural decimal scale (money at
/// 2dp, a quantity/rate at its real precision), so a spreadsheet reads them back as real numbers. A text field
/// that begins with a spreadsheet formula trigger is neutralized (a leading <c>'</c>) so it renders as literal
/// text (OWASP CSV-injection). Deterministic and byte-stable: no clock, no culture leak.
/// </summary>
public static class CsvWriter
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Serializes the model to RFC-4180 UTF-8-with-BOM CSV bytes.</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var sb = new StringBuilder();
        int colCount = export.Columns.Count;

        // Header record.
        WriteRecord(sb, HeaderFields(export));

        // Body records.
        foreach (var row in export.Rows)
            WriteRecord(sb, RowFields(row, colCount));

        // Prepend the UTF-8 BOM.
        byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[Utf8Bom.Length + body.Length];
        System.Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
        System.Buffer.BlockCopy(body, 0, result, Utf8Bom.Length, body.Length);
        return result;
    }

    private static IReadOnlyList<string> HeaderFields(TabularExport export)
    {
        var fields = new string[export.Columns.Count];
        for (int i = 0; i < fields.Length; i++)
            fields[i] = Neutralize(Debrand.Text(export.Columns[i].Header));
        return fields;
    }

    private static IReadOnlyList<string> RowFields(TabularRow row, int colCount)
    {
        var fields = new string[colCount];
        for (int i = 0; i < colCount; i++)
        {
            if (i >= row.Cells.Count) { fields[i] = string.Empty; continue; }
            var cell = row.Cells[i];
            // A Number cell carries our OWN invariant figure (e.g. -355000.50) — it must stay a plain number a
            // spreadsheet can sum, so it is NOT injection-guarded. Only free-text (a user-typed label/narration)
            // can start with a formula trigger, so the guard is applied to text fields alone.
            fields[i] = cell.Type == CellType.Number
                ? cell.NumberText                                  // invariant scale-preserving; empty for a valueless number cell
                : Neutralize(TabularDebrand.Cell(cell.TextValue)); // de-brand (newline-safe) then guard formula injection
        }
        return fields;
    }

    /// <summary>
    /// Neutralizes CSV formula/macro injection (OWASP): a field whose FIRST character is one a spreadsheet may
    /// interpret as the start of a formula (<c>= + - @</c>) or a leading control (tab <c>0x09</c>, CR <c>0x0D</c>)
    /// is prefixed with a single quote <c>'</c> so the spreadsheet renders it as literal text rather than
    /// evaluating it. The prefix is inside the field, so <see cref="Quote"/> still yields RFC-4180-valid output
    /// and a strict parser round-trips the guarded value (with the leading <c>'</c>) verbatim.
    /// </summary>
    private static string Neutralize(string field)
    {
        if (field.Length == 0) return field;
        char c = field[0];
        return c is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + field : field;
    }

    private static void WriteRecord(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Quote(fields[i]));
        }
        sb.Append("\r\n"); // RFC-4180 CRLF record separator
    }

    /// <summary>RFC-4180 field quoting: a field containing a comma, double-quote, CR or LF is enclosed in
    /// double-quotes with embedded quotes doubled; otherwise the field is emitted verbatim.</summary>
    private static string Quote(string field)
    {
        if (field.Length == 0) return string.Empty;
        bool mustQuote = field.IndexOf(',') >= 0
            || field.IndexOf('"') >= 0
            || field.IndexOf('\r') >= 0
            || field.IndexOf('\n') >= 0;
        if (!mustQuote) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
