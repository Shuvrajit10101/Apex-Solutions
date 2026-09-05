using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// The single comma-delimited composition used by BOTH delimited exports (W2-25 / census 13.6): the
/// Excel-targeted <see cref="CsvWriter"/> (<c>.csv</c>, UTF-8 <b>with</b> a BOM) and the plain-text
/// <see cref="AsciiReportWriter"/> (<c>.txt</c>, no BOM). The vendor's File Format list names one delimited
/// format — <i>ASCII (Comma Delimited)</i> <c>.txt</c> — and our pre-existing <c>Csv</c> member is that format
/// under a different extension (census 13.6: <i>"renamed, not missing"</i>), so the two must never drift: the
/// RFC-4180 quoting, the de-branding and the formula-injection guard live here, once.
///
/// <para>Deterministic and byte-stable: no clock, no culture leak. Number cells format invariant at their OWN
/// natural decimal scale (money at 2dp, a quantity/rate at its real precision).</para>
/// </summary>
internal static class DelimitedText
{
    /// <summary>Composes the header record plus one record per row, CRLF-separated (RFC-4180).</summary>
    public static string Compose(TabularExport export)
    {
        var sb = new StringBuilder();
        int colCount = export.Columns.Count;

        WriteRecord(sb, HeaderFields(export));
        foreach (var row in export.Rows)
            WriteRecord(sb, RowFields(row, colCount));

        return sb.ToString();
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
