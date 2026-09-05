using System;
using System.IO;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-25 / census row 13.6 — the four new export formats must be REACHABLE from the Export panel, not merely
/// present in the Io layer. A census row does not move off ABSENT because a writer exists; it moves when a user
/// can pick the format and get the file. These tests pin the thin Avalonia layer: the radio-style bindings the
/// panel binds to, the extension hint they drive, and the bytes <see cref="ExportViewModel.Apply"/> actually
/// writes for each format.
/// </summary>
public sealed class ExportFormatReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ExportFormatReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexExportFmt_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class Captured
    {
        public string Path = string.Empty;
        public byte[] Bytes = Array.Empty<byte>();
    }

    private ExportViewModel Export(out Captured cap, ExportFormat format)
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.OpenReport(ReportKind.TrialBalance);

        var captured = new Captured();
        cap = captured;
        return new ExportViewModel(shell.Reports!, folder: "C:\\Out",
            now: new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (path, bytes) => { captured.Path = path; captured.Bytes = bytes; })
        {
            Format = format,
        };
    }

    // ------------------------------------------------------------------ the panel's own bindings

    [Fact]
    public void Panel_exposes_a_radio_binding_for_every_new_format()
    {
        var vm = Export(out _, ExportFormat.Csv);

        vm.IsHtml = true;
        Assert.Equal(ExportFormat.Html, vm.Format);
        Assert.True(vm.IsHtml);
        Assert.False(vm.IsCsv);

        vm.IsXml = true;
        Assert.Equal(ExportFormat.Xml, vm.Format);
        Assert.False(vm.IsHtml);

        vm.IsJson = true;
        Assert.Equal(ExportFormat.Json, vm.Format);
        Assert.False(vm.IsXml);

        vm.IsAscii = true;
        Assert.Equal(ExportFormat.Ascii, vm.Format);
        Assert.False(vm.IsJson);

        // And back to a format that shipped — the group is still exclusive in both directions.
        vm.IsPdf = true;
        Assert.Equal(ExportFormat.Pdf, vm.Format);
        Assert.False(vm.IsAscii);
    }

    [Fact]
    public void Extension_hint_follows_the_chosen_format()
    {
        var vm = Export(out _, ExportFormat.Csv);

        vm.IsHtml = true; Assert.Equal("html", vm.ExtensionHint);
        vm.IsXml = true; Assert.Equal("xml", vm.ExtensionHint);
        vm.IsJson = true; Assert.Equal("json", vm.ExtensionHint);
        vm.IsAscii = true; Assert.Equal("txt", vm.ExtensionHint);
    }

    // ------------------------------------------------------------------ Apply writes the right bytes

    [Fact]
    public void Apply_writes_an_html_document_named_html()
    {
        var vm = Export(out var cap, ExportFormat.Html);

        Assert.True(vm.Apply());
        Assert.EndsWith(".html", cap.Path);
        string text = new UTF8Encoding(false).GetString(cap.Bytes);
        Assert.StartsWith("<!DOCTYPE html>", text);
        Assert.Contains("<table>", text);
    }

    [Fact]
    public void Apply_writes_well_formed_xml_named_xml()
    {
        var vm = Export(out var cap, ExportFormat.Xml);

        Assert.True(vm.Apply());
        Assert.EndsWith(".xml", cap.Path);
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(new UTF8Encoding(false).GetString(cap.Bytes));
        Assert.Equal("Report", doc.DocumentElement!.Name);
    }

    [Fact]
    public void Apply_writes_well_formed_json_named_json()
    {
        var vm = Export(out var cap, ExportFormat.Json);

        Assert.True(vm.Apply());
        Assert.EndsWith(".json", cap.Path);
        using var doc = System.Text.Json.JsonDocument.Parse(cap.Bytes);
        Assert.True(doc.RootElement.TryGetProperty("rows", out _));
    }

    [Fact]
    public void Apply_writes_bom_less_comma_delimited_text_named_txt()
    {
        var vm = Export(out var cap, ExportFormat.Ascii);

        Assert.True(vm.Apply());
        Assert.EndsWith(".txt", cap.Path);
        Assert.NotEqual(0xEF, cap.Bytes[0]);
        Assert.Contains(',', new UTF8Encoding(false).GetString(cap.Bytes));
    }

    [Fact]
    public void Every_new_format_exports_the_same_figures_the_csv_does()
    {
        // Robert's Trial Balance carries a real closing figure; whichever format the user picks, the SAME
        // projection is serialized, so a figure present in the CSV must be present in all four. This is the
        // RQ-15 fidelity guarantee stated across formats rather than per writer.
        var csvVm = Export(out var csv, ExportFormat.Csv);
        Assert.True(csvVm.Apply());
        string csvText = Encoding.UTF8.GetString(csv.Bytes);

        // Pull the first real money figure out of the CSV body (invariant, scale-preserving).
        string? figure = null;
        foreach (var field in csvText.Split(new[] { ',', '\r', '\n', '"' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (decimal.TryParse(field, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) && d != 0m)
            { figure = field; break; }
        }
        Assert.NotNull(figure);

        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Xml, ExportFormat.Json, ExportFormat.Ascii })
        {
            var vm = Export(out var cap, format);
            Assert.True(vm.Apply());
            Assert.Contains(figure!, new UTF8Encoding(false).GetString(cap.Bytes));
        }
    }

    [Fact]
    public void No_new_format_leaks_the_forbidden_brand()
    {
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Xml, ExportFormat.Json, ExportFormat.Ascii })
        {
            var vm = Export(out var cap, format);
            Assert.True(vm.Apply());
            Assert.DoesNotContain("tally", new UTF8Encoding(false).GetString(cap.Bytes),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
