using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for the tabular (CSV + XLSX) report/master-list export (RQ-14..18). A small fixed
/// <see cref="TabularExport"/> plus a Bright-style report model keep the figures deterministic; both
/// writers must be byte-stable on re-export and never emit the forbidden brand.
/// </summary>
public sealed class TabularExportTests
{
    // A small deterministic model that deliberately exercises every RFC-4180 quoting case: a plain field,
    // a field with a comma, a field with an embedded double-quote, a field with a CR/LF newline, and typed
    // Number cells carrying exact decimals (money) so a spreadsheet can sum them.
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
            TabularRow.Of(TabularCell.Text("Smith, Jones & Co"), TabularCell.Number(250000.50m), TabularCell.Empty),
            TabularRow.Of(TabularCell.Text("He said \"hi\""), TabularCell.Empty, TabularCell.Number(355000.50m)),
            TabularRow.Of(TabularCell.Text("Line1\r\nLine2"), TabularCell.Empty, TabularCell.Empty),
            TabularRow.Total(TabularCell.Text("Grand Total"), TabularCell.Number(355000.50m), TabularCell.Number(355000.50m)),
        });

    // ---------------------------------------------------------------- CSV: RFC-4180

    [Fact]
    public void Csv_starts_with_utf8_bom()
    {
        var bytes = CsvWriter.Write(SampleModel());
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void Csv_records_are_crlf_separated()
    {
        var text = CsvText(CsvWriter.Write(SampleModel()));
        Assert.Contains("\r\n", text);
        // Header + 5 data rows = 6 records; a trailing CRLF is allowed by RFC-4180.
        var records = ParseCsv(text);
        Assert.Equal(6, records.Count);
    }

    [Fact]
    public void Csv_quotes_comma_quote_and_newline_fields_and_doubles_quotes()
    {
        var text = CsvText(CsvWriter.Write(SampleModel()));
        // A comma field is wrapped in quotes.
        Assert.Contains("\"Smith, Jones & Co\"", text);
        // An embedded double-quote is doubled and the field quoted.
        Assert.Contains("\"He said \"\"hi\"\"\"", text);
        // A newline field is quoted (contains a raw CRLF inside quotes).
        Assert.Contains("\"Line1\r\nLine2\"", text);
    }

    [Fact]
    public void Csv_round_trips_through_an_rfc4180_parser_to_the_same_cells()
    {
        var records = ParseCsv(CsvText(CsvWriter.Write(SampleModel())));
        // Header row.
        Assert.Equal(new[] { "Particulars", "Debit", "Credit" }, records[0]);
        // Comma field survives verbatim.
        Assert.Equal("Smith, Jones & Co", records[2][0]);
        // Embedded quote field survives verbatim.
        Assert.Equal("He said \"hi\"", records[3][0]);
        // Newline field survives verbatim.
        Assert.Equal("Line1\r\nLine2", records[4][0]);
        // Numbers are invariant, 2dp.
        Assert.Equal("105000.00", records[1][1]);
        Assert.Equal("250000.50", records[2][1]);
        Assert.Equal("355000.50", records[3][2]);
        // Empty number cells are empty.
        Assert.Equal("", records[1][2]);
    }

    [Fact]
    public void Csv_is_byte_identical_on_re_export()
    {
        var a = CsvWriter.Write(SampleModel());
        var b = CsvWriter.Write(SampleModel());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Csv_has_zero_tally_bytes()
    {
        var text = CsvText(CsvWriter.Write(SampleModel())).ToLowerInvariant();
        Assert.DoesNotContain("tally", text);
    }

    // ---------------------------------------------------------------- Fix 1: numeric precision (RQ-15)

    // A model that mixes money (inherently 2dp) with stock quantities/rates at their real precision, so we can
    // pin that each Number cell exports at its OWN natural decimal scale — money keeps 2dp, a fractional quantity
    // keeps its digits, and a whole quantity does NOT gain an invented ".00".
    private static TabularExport PrecisionModel() => new(
        title: "Stock Summary",
        columns: new[]
        {
            new TabularColumn("Item", CellType.Text),
            new TabularColumn("Qty", CellType.Number),
            new TabularColumn("Rate", CellType.Number),
            new TabularColumn("Value", CellType.Number),
        },
        rows: new[]
        {
            // qty 10.125 (scale 3), rate 3.3333 (scale 4), value 355000.50 (money, scale 2).
            TabularRow.Of(TabularCell.Text("Widget"), TabularCell.Number(10.125m), TabularCell.Number(3.3333m), TabularCell.Number(355000.50m)),
            // a whole quantity 5 (scale 0) must stay "5" — no ".00".
            TabularRow.Of(TabularCell.Text("Gadget"), TabularCell.Number(5m), TabularCell.Empty, TabularCell.Number(1000.00m)),
        });

    [Fact]
    public void Csv_number_cells_preserve_each_value_s_own_decimal_scale()
    {
        var records = ParseCsv(CsvText(CsvWriter.Write(PrecisionModel())));
        // Row 1: fractional quantity/rate keep their real precision; money stays 2dp.
        Assert.Equal("10.125", records[1][1]);
        Assert.Equal("3.3333", records[1][2]);
        Assert.Equal("355000.50", records[1][3]);
        // Row 2: a whole quantity stays whole (no invented ".00"); a whole-rupee money is still 2dp.
        Assert.Equal("5", records[2][1]);
        Assert.Equal("1000.00", records[2][3]);
    }

    [Fact]
    public void Xlsx_number_cells_preserve_each_value_s_own_decimal_scale()
    {
        using var zip = OpenZip(XlsxWriter.Write(PrecisionModel()));
        string sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");
        // The exact source scale is carried in <v>: a fractional quantity/rate is NOT rounded to 2dp, money is 2dp.
        Assert.Contains("<v>10.125</v>", sheet);
        Assert.Contains("<v>3.3333</v>", sheet);
        Assert.Contains("<v>355000.50</v>", sheet);
        // A whole quantity stays whole in <v> (no ".00"); it must NOT have been forced to two decimals.
        Assert.Contains("<v>5</v>", sheet);
        Assert.DoesNotContain("<v>5.00</v>", sheet);
        Assert.Contains("<v>1000.00</v>", sheet);
    }

    // ---------------------------------------------------------------- Fix 2: XML-illegal control chars (RQ-17)

    [Fact]
    public void Xlsx_strips_xml_illegal_control_chars_so_the_package_opens_without_repair()
    {
        // A ledger name / narration carrying a bell (0x07) and a unit-separator (0x1F) — both forbidden in XML 1.0.
        var model = new TabularExport("Ledger",
            new[] { new TabularColumn("Particulars", CellType.Text), new TabularColumn("Amount", CellType.Number) },
            new[]
            {
                // The control chars are built from code points so no raw control byte sits in this source file.
                TabularRow.Of(TabularCell.Text("Bell" + (char)0x07 + "Name"), TabularCell.Number(100.00m)),
                TabularRow.Of(TabularCell.Text("Unit" + (char)0x1F + "Sep"), TabularCell.Number(200.00m)),
            });

        byte[] bytes = XlsxWriter.Write(model);

        using var zip = OpenZip(bytes);

        // 1) The illegal control chars were stripped from the cell text (they never reach sheet1.xml).
        string sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");
        Assert.DoesNotContain((char)0x07, sheet);
        Assert.DoesNotContain((char)0x1F, sheet);
        // The surrounding text survives, only the illegal char is dropped ("BellName", "UnitSep").
        Assert.Contains("BellName", sheet);
        Assert.Contains("UnitSep", sheet);

        // 2) EVERY part is well-formed XML — exactly what a spreadsheet checks before it will open the package
        // without a "repair" prompt. XmlDocument.Load throws XmlException on any illegal char that leaked through.
        foreach (var name in new[]
        {
            "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml",
        })
        {
            var doc = new XmlDocument();
            using var s = zip.GetEntry(name)!.Open();
            doc.Load(s);
            Assert.NotNull(doc.DocumentElement);
        }
    }

    // ---------------------------------------------------------------- Fix 4: CSV formula/macro injection (OWASP)

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@x")]
    public void Csv_text_cell_starting_with_a_formula_trigger_is_neutralized_yet_round_trips(string dangerous)
    {
        var model = new TabularExport("Ledger List",
            new[] { new TabularColumn("Name", CellType.Text) },
            new[] { TabularRow.Of(TabularCell.Text(dangerous)) });

        string csv = CsvText(CsvWriter.Write(model));
        var records = ParseCsv(csv);

        // The written field is guarded with a leading single-quote so a spreadsheet treats it as literal text.
        Assert.Equal("'" + dangerous, records[1][0]);
        // And a strict RFC-4180 parser still round-trips the (guarded) value verbatim — output stays valid CSV.
        Assert.StartsWith("'", records[1][0]);
    }

    [Fact]
    public void Csv_negative_number_cell_stays_a_plain_number_not_guarded()
    {
        // A negative MONEY value is a Number cell — it must remain a plain "-100.00" a spreadsheet can sum, NOT
        // a guarded "'-100.00" (the injection guard applies to free text only, never our own figures).
        var model = new TabularExport("TB",
            new[] { new TabularColumn("Particulars", CellType.Text), new TabularColumn("Amount", CellType.Number) },
            new[] { TabularRow.Of(TabularCell.Text("Loss"), TabularCell.Number(-100.00m)) });

        var records = ParseCsv(CsvText(CsvWriter.Write(model)));
        Assert.Equal("-100.00", records[1][1]);
    }

    // ---------------------------------------------------------------- XLSX: valid OPC package

    [Fact]
    public void Xlsx_contains_the_required_opc_parts()
    {
        using var zip = OpenZip(XlsxWriter.Write(SampleModel()));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        Assert.NotNull(zip.GetEntry("xl/_rels/workbook.xml.rels"));
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void Xlsx_parts_are_well_formed_xml()
    {
        using var zip = OpenZip(XlsxWriter.Write(SampleModel()));
        foreach (var name in new[]
        {
            "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml",
        })
        {
            var doc = new XmlDocument();
            using var s = zip.GetEntry(name)!.Open();
            doc.Load(s); // throws on malformed XML
            Assert.NotNull(doc.DocumentElement);
        }
    }

    [Fact]
    public void Xlsx_money_cell_is_a_numeric_cell_with_the_exact_value()
    {
        using var zip = OpenZip(XlsxWriter.Write(SampleModel()));
        string sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");
        // The grand-total debit 355000.50 must be a numeric cell (t="n" or default numeric) with an exact <v>.
        Assert.Contains("<v>355000.50</v>", sheet);
        Assert.Contains("<v>105000.00</v>", sheet);
        // A numeric cell must not be a string/inlineStr cell.
        Assert.DoesNotContain("t=\"inlineStr\"><is><t>355000.50", sheet);
    }

    [Fact]
    public void Xlsx_text_cell_carries_the_label()
    {
        using var zip = OpenZip(XlsxWriter.Write(SampleModel()));
        string sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");
        string shared = zip.GetEntry("xl/sharedStrings.xml") is { } e
            ? ReadEntry(zip, "xl/sharedStrings.xml") : string.Empty;
        string haystack = sheet + shared;
        // XML-escaped label present (comma field). Quotes/ampersands must be XML-escaped.
        Assert.Contains("Smith, Jones &amp; Co", haystack);
        Assert.Contains("He said &quot;hi&quot;", haystack);
    }

    [Fact]
    public void Xlsx_is_byte_identical_on_re_export()
    {
        var a = XlsxWriter.Write(SampleModel());
        var b = XlsxWriter.Write(SampleModel());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Xlsx_has_zero_tally_bytes()
    {
        using var zip = OpenZip(XlsxWriter.Write(SampleModel()));
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string content = ReadEntry(zip, entry.FullName).ToLowerInvariant();
            Assert.DoesNotContain("tally", content);
            Assert.DoesNotContain("tally", entry.FullName.ToLowerInvariant());
        }
    }

    // ---------------------------------------------------------------- ExportConfig

    [Fact]
    public void ExportConfig_builds_filename_with_passed_in_timestamp_suffix()
    {
        var cfg = new ExportConfig
        {
            Format = ExportFormat.Xlsx,
            Folder = "C:\\out",
            FileName = "Trial Balance",
            TimestampSuffix = "20260706-1200",
        };
        Assert.Equal("Trial Balance_20260706-1200.xlsx", cfg.ResolvedFileName);
        Assert.Equal("C:\\out\\Trial Balance_20260706-1200.xlsx", cfg.FullPath);
    }

    [Fact]
    public void ExportConfig_without_timestamp_uses_plain_name_and_format_extension()
    {
        var csv = new ExportConfig { Format = ExportFormat.Csv, Folder = "C:\\out", FileName = "Day Book" };
        Assert.Equal("Day Book.csv", csv.ResolvedFileName);
        var pdf = new ExportConfig { Format = ExportFormat.Pdf, Folder = "C:\\out", FileName = "Day Book" };
        Assert.Equal("Day Book.pdf", pdf.ResolvedFileName);
    }

    // ---------------------------------------------------------------- helpers

    private static string CsvText(byte[] bytes)
    {
        // Strip the UTF-8 BOM then decode.
        int start = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private static ZipArchive OpenZip(byte[] bytes)
        => new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var s = zip.GetEntry(name)!.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>A minimal RFC-4180 parser: fields may be quoted; a quoted field may contain commas,
    /// CRLF and doubled quotes; records are CRLF-separated. Returns the list of records (each a string[]).</summary>
    private static List<string[]> ParseCsv(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        bool sawAny = false;
        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; sawAny = true; continue;
            }
            if (c == '"') { inQuotes = true; i++; sawAny = true; continue; }
            if (c == ',') { fields.Add(field.ToString()); field.Clear(); i++; sawAny = true; continue; }
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                fields.Add(field.ToString()); field.Clear();
                records.Add(fields.ToArray()); fields = new List<string>();
                i += 2; sawAny = false; continue;
            }
            field.Append(c); i++; sawAny = true;
        }
        if (sawAny || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }
        return records;
    }
}
