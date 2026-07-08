using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to a valid minimal OPC / XLSX (SpreadsheetML) package (RQ-17),
/// built entirely with the framework's <see cref="ZipArchive"/> — no third-party NuGet (DP-2). The package
/// contains exactly the parts a spreadsheet needs to open the workbook without a repair prompt:
///
/// <list type="bullet">
///   <item><c>[Content_Types].xml</c> — content-type defaults + overrides for every part.</item>
///   <item><c>_rels/.rels</c> — package relationship to the workbook.</item>
///   <item><c>xl/workbook.xml</c> — one sheet named after the export title.</item>
///   <item><c>xl/_rels/workbook.xml.rels</c> — workbook → worksheet relationship.</item>
///   <item><c>xl/worksheets/sheet1.xml</c> — the cell data; number cells are real numeric cells.</item>
/// </list>
///
/// <para>Text cells are written as <c>inlineStr</c> (self-contained, no shared-string table needed). Number
/// cells are <c>&lt;c&gt;&lt;v&gt;123.45&lt;/v&gt;&lt;/c&gt;</c> (SpreadsheetML's default cell type is
/// numeric), with the value formatted invariant to two decimal places, so the spreadsheet stores a real
/// number and can sum it. Deterministic and byte-stable: the ZIP entries are written in a fixed order with a
/// fixed last-write time (no clock, no RNG), so the same model always produces identical bytes.</para>
/// </summary>
public static class XlsxWriter
{
    // A fixed, DOS-epoch-safe timestamp for every ZIP entry so bytes are stable (no clock).
    private static readonly DateTimeOffset FixedTime =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Serializes the model to a valid minimal XLSX package as bytes.</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string sheetName = SheetName(export.Title);

        // Build every part's text up front so the ZIP write is a simple deterministic loop.
        var parts = new (string Name, string Content)[]
        {
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRels()),
            ("xl/workbook.xml", Workbook(sheetName)),
            ("xl/_rels/workbook.xml.rels", WorkbookRels()),
            ("xl/worksheets/sheet1.xml", Sheet(export)),
        };

        using var ms = new MemoryStream();
        // leaveOpen so we can read ms.ToArray() after the archive is disposed/flushed.
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                // No compression => deterministic bytes independent of the deflate implementation, and set a
                // fixed timestamp so the ZIP local/central headers don't carry the wall clock.
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                entry.LastWriteTime = FixedTime;
                using var es = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                es.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- OPC parts

    private const string XmlDecl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private static string ContentTypes() =>
        XmlDecl +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "</Types>";

    private static string RootRels() =>
        XmlDecl +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string Workbook(string sheetName) =>
        XmlDecl +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets>" +
        "<sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "</sheets>" +
        "</workbook>";

    private static string WorkbookRels() =>
        XmlDecl +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "</Relationships>";

    private static string Sheet(TabularExport export)
    {
        int colCount = export.Columns.Count;
        var sb = new StringBuilder();
        sb.Append(XmlDecl);
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetData>");

        int rowNum = 1;

        // Header row: every header is a text (inlineStr) cell.
        AppendRow(sb, rowNum++, colCount, i => TextCellXml(RowRef(i, 1), Debrand.Text(export.Columns[i].Header)));

        foreach (var row in export.Rows)
        {
            int r = rowNum++;
            AppendRow(sb, r, colCount, i =>
            {
                var cell = i < row.Cells.Count ? row.Cells[i] : TabularCell.Empty;
                string cellRef = RowRef(i, r);
                if (cell.Type == CellType.Number && cell.HasNumber)
                    return NumberCellXml(cellRef, cell.NumberValue);
                string text = TabularDebrand.Cell(cell.TextValue);
                return text.Length == 0 ? string.Empty : TextCellXml(cellRef, text);
            });
        }

        sb.Append("</sheetData>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowNum, int colCount, Func<int, string> cellXml)
    {
        sb.Append("<row r=\"").Append(rowNum.ToString(CultureInfo.InvariantCulture)).Append("\">");
        for (int i = 0; i < colCount; i++)
            sb.Append(cellXml(i)); // an empty string skips a blank cell (valid in SpreadsheetML)
        sb.Append("</row>");
    }

    private static string TextCellXml(string cellRef, string text) =>
        "<c r=\"" + cellRef + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" +
        XmlEscape(text) + "</t></is></c>";

    private static string NumberCellXml(string cellRef, decimal value) =>
        // Default cell type is numeric; <v> carries the exact value at its OWN natural scale (RQ-15): money
        // stays 2dp (355000.50), a quantity/rate keeps its real precision (10.125), a whole quantity stays
        // whole (5). Plain invariant ToString round-trips the stored scale with a dot separator, no grouping.
        "<c r=\"" + cellRef + "\"><v>" + value.ToString(CultureInfo.InvariantCulture) + "</v></c>";

    // ---------------------------------------------------------------- A1 references

    /// <summary>The A1-style reference for zero-based column <paramref name="col"/> at one-based row
    /// <paramref name="row"/> (e.g. col 0 row 1 => "A1", col 26 row 3 => "AA3").</summary>
    private static string RowRef(int col, int row)
        => ColumnLetter(col) + row.ToString(CultureInfo.InvariantCulture);

    private static string ColumnLetter(int col)
    {
        var sb = new StringBuilder();
        int n = col + 1; // 1-based
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            n = (n - 1) / 26;
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A safe worksheet name: de-branded, XML-safe, ≤ 31 chars, none of the characters Excel forbids
    /// in a sheet name (<c>: \ / ? * [ ]</c>). Falls back to "Sheet1" if empty.</summary>
    private static string SheetName(string title)
    {
        string t = Debrand.Text(title);
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
            sb.Append(c is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? ' ' : c);
        string cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0) return "Sheet1";
        return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
    }

    private static string XmlEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            // Drop any character XML 1.0 forbids BEFORE escaping. Only tab (0x09), LF (0x0A), CR (0x0D) and
            // 0x20+ are legal; a stray control char (0x00–0x08, 0x0B, 0x0C, 0x0E–0x1F, 0x7F) in a ledger name /
            // narration would produce malformed sheet1.xml that Excel refuses to open without a repair prompt
            // (RQ-17). We silently omit it rather than replace, so a clean name is unchanged.
            if (!IsLegalXmlChar(c)) continue;
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

    /// <summary>True if <paramref name="c"/> is a character XML 1.0 permits in element content: tab, LF, CR,
    /// or any code point at or above 0x20 except the delete control (0x7F). Surrogates are left to the writer's
    /// UTF-8 encoding, which pairs them; the forbidden C0 controls and 0x7F are excluded here.</summary>
    private static bool IsLegalXmlChar(char c)
        => c == '\t' || c == '\n' || c == '\r' || (c >= ' ' && c != '');
}
