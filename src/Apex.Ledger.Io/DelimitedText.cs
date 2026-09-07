using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// The single comma-delimited composition used by BOTH delimited exports (W2-25 / census 13.6): the
/// Excel-targeted <see cref="CsvWriter"/> (<c>.csv</c>, UTF-8 <b>with</b> a BOM) and the plain-text
/// <see cref="AsciiReportWriter"/> (<c>.txt</c>, no BOM). The vendor's File Format list names one delimited
/// format — <i>ASCII (Comma Delimited)</i> <c>.txt</c> — and our pre-existing <c>Csv</c> member is that format
/// under a different extension (census 13.6: <i>"renamed, not missing"</i>), so the two must never drift: the
/// RFC-4180 quoting lives here, once, and the formula-injection guard lives once in
/// <see cref="SpreadsheetFormulaGuard"/> (public, so the hand-rolled exporters outside this assembly apply the
/// SAME rule rather than a private copy of it). The only de-brand left here is on the
/// column-caption row, which is CHROME this product authored; a BODY cell is book data and ships verbatim
/// (ruling 18).
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
            fields[i] = SpreadsheetFormulaGuard.Neutralize(Debrand.Text(export.Columns[i].Header));
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
            // 🔴 RULING 18: a text cell is BOOK DATA (a party/bank/item name, a narration, a bill reference) and
            // is exported VERBATIM. The de-brand that used to run here rewrote a counterparty's legal name inside
            // the customer's own spreadsheet. The formula-injection guard stays — that is a safety property of
            // the FILE FORMAT, not an edit to the name.
            fields[i] = cell.Type == CellType.Number
                ? cell.NumberText                                  // invariant scale-preserving; empty for a valueless number cell
                : SpreadsheetFormulaGuard.Neutralize(cell.TextValue ?? string.Empty);
        }
        return fields;
    }

    // The formula-injection neutralisation used above is NOT declared here any more: it is
    // SpreadsheetFormulaGuard.Neutralize, the ONE public home for that rule, so the hand-rolled CSV exporters in
    // Apex.Desktop (which cannot see this internal class) apply the identical guard instead of growing private
    // copies that drift. The behaviour is unchanged — the code moved, byte for byte. Ordering here is unchanged
    // too: the field is neutralised when it is composed, then Quote() wraps it, so the ' lands INSIDE the quotes.

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
