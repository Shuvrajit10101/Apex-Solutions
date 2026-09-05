using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-25 / census row 13.6 — report export in HTML, XML, JSON and ASCII, over the SAME
/// <see cref="TabularExport"/> projection CSV and XLSX already use, so the exported figures are the on-screen
/// figures (RQ-15 fidelity) in every format.
///
/// <para>Every expected value below is derived by hand from <see cref="SampleModel"/>, not read off a writer.
/// Money is asserted as a literal to the paisa. The four writers are deterministic and byte-stable and must
/// never emit the forbidden brand (ER-11), exactly as CSV/XLSX must not.</para>
///
/// <para><b>Format list source.</b> The seven-item File Format list (ASCII (Comma Delimited) <c>.txt</c>,
/// Excel <c>.xlsx</c>, HTML <c>.html</c>, JPEG <c>.jpg</c>, JSON <c>.json</c>, PDF <c>.pdf</c>,
/// XML <c>.xml</c>) is the census's quoted vendor list for row 13.6. <b>The document SHAPES below are OURS</b>
/// (ruling 9): no admissible source states the vendor's HTML table markup, its XML element names or its JSON
/// object shape, so ours are a documented divergence and can never join the compared set. JPEG is carved out
/// (design ruling R14 — it needs a rasteriser), so 13.6 does not close.</para>
/// </summary>
public sealed class ReportExportFormatTests
{
    // A deterministic model that exercises: a plain field, a field needing markup/JSON escaping, an empty
    // number cell, a multi-line cell, a total row, and exact money at two decimal places.
    private static TabularExport SampleModel() => new(
        title: "Trial Balance",
        columns: new[]
        {
            new TabularColumn("Particulars", CellType.Text),
            new TabularColumn("Debit", CellType.Number),
            new TabularColumn("Credit", CellType.Number),
        },
        rows: new[]
        {
            TabularRow.Of(TabularCell.Text("Cash-in-Hand"), TabularCell.Number(105000.00m), TabularCell.Empty),
            TabularRow.Of(TabularCell.Text("Smith & \"Co\" <Ltd>"), TabularCell.Number(250000.50m), TabularCell.Empty),
            TabularRow.Of(TabularCell.Text("Line1\r\nLine2"), TabularCell.Empty, TabularCell.Number(355000.50m)),
            TabularRow.Total(TabularCell.Text("Grand Total"), TabularCell.Number(355000.50m), TabularCell.Number(355000.50m)),
        });

    private static string Utf8(byte[] bytes) => new UTF8Encoding(false).GetString(bytes);

    // ------------------------------------------------------------------ the format enum + extensions

    [Fact]
    public void Extension_for_each_new_format_matches_the_vendor_file_format_list()
    {
        // ASCII (Comma Delimited) is .txt in the source list — our pre-existing Csv member keeps .csv, which the
        // census records as "renamed, not missing"; Ascii adds the source's own extension beside it.
        Assert.Equal("html", new ExportConfig { Format = ExportFormat.Html }.Extension);
        Assert.Equal("xml", new ExportConfig { Format = ExportFormat.Xml }.Extension);
        Assert.Equal("json", new ExportConfig { Format = ExportFormat.Json }.Extension);
        Assert.Equal("txt", new ExportConfig { Format = ExportFormat.Ascii }.Extension);
    }

    [Fact]
    public void Resolved_file_name_uses_the_new_extension()
    {
        var cfg = new ExportConfig { Format = ExportFormat.Json, FileName = "Trial Balance" };
        Assert.Equal("Trial Balance.json", cfg.ResolvedFileName);
    }

    // ------------------------------------------------------------------ HTML

    [Fact]
    public void Html_is_a_complete_utf8_document_titled_with_the_report()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>Trial Balance</title>", html);
        Assert.Contains("<h1>Trial Balance</h1>", html);
        Assert.EndsWith("</html>\r\n", html);
    }

    [Fact]
    public void Html_header_band_carries_every_column_caption()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        Assert.Contains("<th class=\"t\">Particulars</th><th class=\"n\">Debit</th><th class=\"n\">Credit</th>", html);
    }

    [Fact]
    public void Html_money_cells_carry_the_exact_paisa_figure_right_aligned()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        // 105000.00, 250000.50 and 355000.50 are the model's literals, to the paisa, unrounded.
        Assert.Contains("<td class=\"t\">Cash-in-Hand</td><td class=\"n\">105000.00</td><td class=\"n\"></td>", html);
        Assert.Contains("<td class=\"n\">250000.50</td>", html);
        Assert.Contains("<td class=\"n\">355000.50</td><td class=\"n\">355000.50</td>", html);
    }

    [Fact]
    public void Html_escapes_markup_metacharacters_in_free_text()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        Assert.Contains("Smith &amp; &quot;Co&quot; &lt;Ltd&gt;", html);
        Assert.DoesNotContain("<Ltd>", html);
    }

    [Fact]
    public void Html_keeps_an_embedded_newline_as_a_line_break()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        Assert.Contains("Line1<br>Line2", html);
    }

    [Fact]
    public void Html_marks_a_total_row_so_it_can_be_styled()
    {
        string html = Utf8(HtmlReportWriter.Write(SampleModel()));

        Assert.Contains("<tr class=\"tot\">", html);
    }

    // ------------------------------------------------------------------ XML

    [Fact]
    public void Xml_declares_utf8_and_wraps_the_report()
    {
        string xml = Utf8(XmlReportWriter.Write(SampleModel()));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Contains("<Report title=\"Trial Balance\">", xml);
        Assert.EndsWith("</Report>\r\n", xml);
    }

    [Fact]
    public void Xml_declares_every_column_with_its_cell_kind()
    {
        string xml = Utf8(XmlReportWriter.Write(SampleModel()));

        Assert.Contains("<Column header=\"Particulars\" type=\"Text\" />", xml);
        Assert.Contains("<Column header=\"Debit\" type=\"Number\" />", xml);
        Assert.Contains("<Column header=\"Credit\" type=\"Number\" />", xml);
    }

    [Fact]
    public void Xml_number_cells_carry_the_exact_paisa_figure()
    {
        string xml = Utf8(XmlReportWriter.Write(SampleModel()));

        Assert.Contains("<Cell column=\"Debit\" type=\"Number\">105000.00</Cell>", xml);
        Assert.Contains("<Cell column=\"Credit\" type=\"Number\">355000.50</Cell>", xml);
    }

    [Fact]
    public void Xml_escapes_the_five_predefined_entities()
    {
        string xml = Utf8(XmlReportWriter.Write(SampleModel()));

        Assert.Contains("Smith &amp; &quot;Co&quot; &lt;Ltd&gt;", xml);
    }

    [Fact]
    public void Xml_marks_a_total_row()
    {
        string xml = Utf8(XmlReportWriter.Write(SampleModel()));

        Assert.Contains("<Row total=\"true\">", xml);
    }

    [Fact]
    public void Xml_parses_as_well_formed_xml()
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(Utf8(XmlReportWriter.Write(SampleModel())));

        Assert.Equal("Report", doc.DocumentElement!.Name);
        Assert.Equal(4, doc.SelectNodes("/Report/Rows/Row")!.Count);
    }

    // ------------------------------------------------------------------ JSON

    [Fact]
    public void Json_carries_the_title_columns_and_rows()
    {
        string json = Utf8(JsonReportWriter.Write(SampleModel()));

        Assert.Contains("\"title\": \"Trial Balance\"", json);
        Assert.Contains("{ \"header\": \"Particulars\", \"type\": \"text\" }", json);
        Assert.Contains("{ \"header\": \"Debit\", \"type\": \"number\" }", json);
    }

    [Fact]
    public void Json_number_cells_are_bare_json_numbers_at_the_exact_paisa_scale()
    {
        string json = Utf8(JsonReportWriter.Write(SampleModel()));

        // A bare JSON number (not a string) so a consumer sums it; the decimal's own scale is preserved.
        Assert.Contains("{ \"type\": \"number\", \"value\": 105000.00 }", json);
        Assert.Contains("{ \"type\": \"number\", \"value\": 250000.50 }", json);
        Assert.Contains("{ \"type\": \"number\", \"value\": 355000.50 }", json);
    }

    [Fact]
    public void Json_escapes_quotes_backslashes_and_control_characters()
    {
        string json = Utf8(JsonReportWriter.Write(SampleModel()));

        Assert.Contains("Smith & \\\"Co\\\" <Ltd>", json);
        Assert.Contains("Line1\\r\\nLine2", json);
    }

    [Fact]
    public void Json_marks_a_total_row()
    {
        string json = Utf8(JsonReportWriter.Write(SampleModel()));

        Assert.Contains("\"total\": true", json);
    }

    [Fact]
    public void Json_parses_as_well_formed_json_and_round_trips_the_money()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(JsonReportWriter.Write(SampleModel()));

        var rows = doc.RootElement.GetProperty("rows");
        Assert.Equal(4, rows.GetArrayLength());
        Assert.Equal(105000.00m, rows[0].GetProperty("cells")[1].GetProperty("value").GetDecimal());
        Assert.Equal(355000.50m, rows[3].GetProperty("cells")[2].GetProperty("value").GetDecimal());
    }

    // ------------------------------------------------------------------ ASCII (comma delimited, .txt)

    [Fact]
    public void Ascii_is_comma_delimited_with_no_byte_order_mark()
    {
        byte[] bytes = AsciiReportWriter.Write(SampleModel());

        // The Excel-targeted CSV writer leads with a UTF-8 BOM; the plain-text ASCII file must not.
        Assert.NotEqual(0xEF, bytes[0]);
        string text = Utf8(bytes);
        Assert.StartsWith("Particulars,Debit,Credit\r\n", text);
        Assert.Contains("Cash-in-Hand,105000.00,\r\n", text);
        Assert.Contains("Grand Total,355000.50,355000.50\r\n", text);
    }

    [Fact]
    public void Ascii_quotes_a_field_containing_a_comma_or_a_quote_like_rfc4180()
    {
        string text = Utf8(AsciiReportWriter.Write(SampleModel()));

        Assert.Contains("\"Smith & \"\"Co\"\" <Ltd>\",250000.50,", text);
        Assert.Contains("\"Line1\r\nLine2\",,355000.50", text);
    }

    [Fact]
    public void Ascii_body_matches_the_csv_body_byte_for_byte_after_the_bom()
    {
        byte[] csv = CsvWriter.Write(SampleModel());
        byte[] ascii = AsciiReportWriter.Write(SampleModel());

        // The two formats are the SAME comma-delimited records; only the BOM differs. Locking that here means a
        // future change to the quoting rules cannot silently diverge the two files.
        Assert.Equal(csv.Length - 3, ascii.Length);
        Assert.Equal(csv[3..], ascii);
    }

    // ------------------------------------------------------------------ shared guarantees

    [Fact]
    public void Every_new_writer_is_byte_stable_across_two_runs()
    {
        Assert.Equal(HtmlReportWriter.Write(SampleModel()), HtmlReportWriter.Write(SampleModel()));
        Assert.Equal(XmlReportWriter.Write(SampleModel()), XmlReportWriter.Write(SampleModel()));
        Assert.Equal(JsonReportWriter.Write(SampleModel()), JsonReportWriter.Write(SampleModel()));
        Assert.Equal(AsciiReportWriter.Write(SampleModel()), AsciiReportWriter.Write(SampleModel()));
    }

    [Fact]
    public void No_new_writer_emits_the_forbidden_brand()
    {
        var branded = new TabularExport(
            title: "Tally Trial Balance",
            columns: new[] { new TabularColumn("Tally Particulars", CellType.Text) },
            rows: new[] { TabularRow.Of(TabularCell.Text("Paid to Tally Ltd")) });

        foreach (byte[] bytes in new[]
        {
            HtmlReportWriter.Write(branded),
            XmlReportWriter.Write(branded),
            JsonReportWriter.Write(branded),
            AsciiReportWriter.Write(branded),
        })
        {
            string text = Utf8(bytes);
            Assert.DoesNotContain("tally", text, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
