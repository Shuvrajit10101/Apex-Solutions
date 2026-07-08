using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-10 (RQ-14/16 report/master-list export): the E / Alt+E Export action
/// projects the CURRENT report into a <see cref="TabularExport"/> (money as exact Number cells) and writes the
/// chosen CSV / XLSX / PDF via <c>Apex.Ledger.Io</c>.
///
/// <para>The writers themselves are trusted (covered by <c>Apex.Ledger.Io.Tests</c>); these tests pin the thin
/// Avalonia layer: the shell opens an export column, the projected + written bytes are RFC-4180 CSV (with a
/// BOM) / a valid OPC XLSX / a PDF, money exports as a real number a spreadsheet can sum, the format switch and
/// filename/timestamp are honoured, and nothing carries "tally" (RQ-13). A write seam captures the bytes so the
/// projection is asserted without touching disk; one test also writes to a real temp path.</para>
/// </summary>
public sealed class ExportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ExportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexExportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel ShellWithReport(ReportKind kind)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(kind);
        return vm;
    }

    /// <summary>An export VM over the shell's open report with a captured-bytes write seam (no disk).</summary>
    private static ExportViewModel Capture(MainWindowViewModel shell, out Captured cap,
        ExportFormat format = ExportFormat.Csv, bool timestamp = false, DateTime? now = null)
    {
        var captured = new Captured();
        var vm = new ExportViewModel(shell.Reports!, folder: "C:\\Out",
            now: now ?? new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (path, bytes) => { captured.Path = path; captured.Bytes = bytes; })
        {
            Format = format,
            AppendTimestamp = timestamp,
        };
        cap = captured;
        return vm;
    }

    private sealed class Captured
    {
        public string? Path;
        public byte[] Bytes = Array.Empty<byte>();
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    // ---------------------------------------------------------------- CSV: RFC-4180 + BOM + content

    [Fact]
    public void Csv_export_starts_with_bom_carries_title_rows_and_no_tally()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap, ExportFormat.Csv);

        Assert.True(vm.Apply());
        var bytes = cap.Bytes;
        Assert.NotEmpty(bytes);

        // UTF-8 BOM prefix so Excel opens it Unicode (RQ-18).
        Assert.True(bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2]);

        var text = Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
        Assert.Contains("Particulars", text);           // the header record
        Assert.Contains("Grand Total", text);           // the total row survives as data
        Assert.Contains("\r\n", text);                  // CRLF record separators
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase); // RQ-13 de-brand
    }

    [Fact]
    public void Csv_export_round_trips_and_money_is_an_exact_number_cell()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());

        var records = ParseCsv(cap.Bytes);
        Assert.True(records.Count >= 2);
        Assert.Equal("Particulars", records[0][0]);     // header

        // Every money cell that is present is an invariant 2dp number (a spreadsheet reads it as a real number,
        // so it can sum the column) — never a grid string like "1,05,000.00" with a comma group separator.
        foreach (var rec in records.Skip(1))
            for (int i = 1; i < rec.Count; i++)
            {
                var cell = rec[i];
                if (cell.Length == 0) continue;
                Assert.DoesNotContain(",", cell);        // no Indian grouping leaked into the number
                Assert.True(decimal.TryParse(cell, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out _), $"'{cell}' is not a plain number");
            }
    }

    [Fact]
    public void Csv_export_quotes_a_field_with_a_comma_or_quote()
    {
        // A synthetic tabular model exercised through the CSV writer the export path uses. A field with a comma
        // (a party name "Ace, Traders") must be quoted, and an embedded quote doubled.
        var export = new TabularExport("Ledger List",
            new[] { new TabularColumn("Party"), new TabularColumn("Amount", CellType.Number) },
            new[]
            {
                TabularRow.Of(TabularCell.Text("Ace, Traders"), TabularCell.Number(1050.50m)),
                TabularRow.Of(TabularCell.Text("A \"quoted\" name"), TabularCell.Number(2000m)),
            });

        var records = ParseCsv(CsvWriter.Write(export));
        Assert.Equal("Ace, Traders", records[1][0]);     // the comma survives the quote/parse round-trip
        Assert.Equal("1050.50", records[1][1]);
        Assert.Equal("A \"quoted\" name", records[2][0]); // the embedded quote survives
    }

    // ---------------------------------------------------------------- XLSX: valid OPC + numeric money cell

    [Fact]
    public void Xlsx_export_unzips_to_the_required_opc_parts_and_is_well_formed()
    {
        var shell = ShellWithReport(ReportKind.BalanceSheet);
        var vm = Capture(shell, out var cap, ExportFormat.Xlsx);
        Assert.True(vm.Apply());

        using var zip = new ZipArchive(new MemoryStream(cap.Bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        foreach (var required in new[]
                 {
                     "[Content_Types].xml", "_rels/.rels",
                     "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml",
                 })
            Assert.Contains(required, names);

        // Every part is well-formed XML.
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            var doc = new XmlDocument();
            doc.Load(s);                                  // throws if not well-formed
            Assert.NotNull(doc.DocumentElement);
        }
    }

    [Fact]
    public void Xlsx_money_cell_is_a_numeric_cell_and_no_entry_contains_tally()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap, ExportFormat.Xlsx);
        Assert.True(vm.Apply());

        using var zip = new ZipArchive(new MemoryStream(cap.Bytes), ZipArchiveMode.Read);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!;
        string sheetXml;
        using (var r = new StreamReader(sheet.Open())) sheetXml = r.ReadToEnd();

        // A money value is a numeric cell: <c ..><v>NNN.NN</v></c> with NO t="inlineStr" wrapping the number,
        // so the spreadsheet stores a real number. (The grid's Robert TB has a five-figure cash balance.)
        Assert.Contains("<v>", sheetXml);
        Assert.Matches(@"<c[^>]*><v>\d+\.\d{2}</v></c>", sheetXml);

        foreach (var entry in zip.Entries)
        {
            Assert.DoesNotContain("tally", entry.FullName, StringComparison.OrdinalIgnoreCase);
            using var r = new StreamReader(entry.Open());
            Assert.DoesNotContain("tally", r.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- format switch + filename/timestamp

    [Fact]
    public void Format_switch_changes_the_extension_and_the_written_bytes()
    {
        var shell = ShellWithReport(ReportKind.ProfitAndLoss);

        var csv = Capture(shell, out var csvCap, ExportFormat.Csv);
        Assert.True(csv.Apply());
        Assert.EndsWith(".csv", csvCap.Path);
        Assert.Equal(Utf8Bom[0], csvCap.Bytes[0]);        // CSV BOM

        var xlsx = Capture(shell, out var xlsxCap, ExportFormat.Xlsx);
        Assert.True(xlsx.Apply());
        Assert.EndsWith(".xlsx", xlsxCap.Path);
        Assert.Equal("PK", AsLatin1(xlsxCap.Bytes[..2])); // ZIP/OPC local-file-header magic

        var pdf = Capture(shell, out var pdfCap, ExportFormat.Pdf);
        Assert.True(pdf.Apply());
        Assert.EndsWith(".pdf", pdfCap.Path);
        Assert.StartsWith("%PDF-", AsLatin1(pdfCap.Bytes));
    }

    [Fact]
    public void Filename_and_timestamp_suffix_are_honoured()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap, ExportFormat.Xlsx, timestamp: true,
            now: new DateTime(2026, 7, 6, 12, 0, 0));
        vm.FileName = "My Trial Balance";

        Assert.Equal("My Trial Balance_20260706-1200.xlsx", vm.ResolvedFileName);
        Assert.True(vm.Apply());
        Assert.EndsWith("My Trial Balance_20260706-1200.xlsx", cap.Path);
    }

    [Fact]
    public void Default_filename_is_derived_from_the_report_title()
    {
        var shell = ShellWithReport(ReportKind.BalanceSheet);
        var vm = new ExportViewModel(shell.Reports!, "C:\\Out", new DateTime(2026, 7, 6), writeBytes: null);
        Assert.Equal("Balance Sheet", vm.FileName);       // the report title, sanitized
        Assert.Equal("Balance Sheet.csv", vm.ResolvedFileName);
    }

    // ---------------------------------------------------------------- shell wiring + real-disk write

    [Fact]
    public void OpenExport_is_a_noop_without_a_report_and_does_not_stack()
    {
        var empty = new MainWindowViewModel(_storage);
        empty.OpenExport();                               // no report open
        Assert.Null(empty.ExportPanel);

        var shell = ShellWithReport(ReportKind.DayBook);
        shell.OpenExport();
        var first = shell.ExportPanel;
        Assert.NotNull(first);
        Assert.Equal(Screen.Export, shell.CurrentScreen);
        shell.OpenExport();                               // re-press: must not stack a second panel
        Assert.Same(first, shell.ExportPanel);
    }

    [Fact]
    public void Apply_writes_a_real_file_to_disk()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = new ExportViewModel(shell.Reports!, _tempDir, new DateTime(2026, 7, 6), writeBytes: null)
        {
            Format = ExportFormat.Csv,
            FileName = "tb",
        };

        Assert.True(vm.Apply());
        var path = Path.Combine(_tempDir, "tb.csv");
        Assert.True(File.Exists(path));

        var onDisk = File.ReadAllBytes(path);
        Assert.Equal(Utf8Bom[0], onDisk[0]);
        Assert.DoesNotContain("tally", AsLatin1(onDisk), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exported", vm.Status);
    }

    // ---------------------------------------------------------------- inline RFC-4180 parser (for round-trips)

    /// <summary>Parses RFC-4180 CSV bytes (UTF-8, optional BOM) back into records of fields.</summary>
    private static List<List<string>> ParseCsv(byte[] bytes)
    {
        int start = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);

        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { /* swallow; the \n ends the record */ }
            else if (c == '\n')
            {
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
