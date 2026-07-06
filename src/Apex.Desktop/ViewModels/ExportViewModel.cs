using System;
using System.IO;
using Apex.Desktop.Services;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first "Export" panel (RQ-14/16), hosted as its own cascading Miller-column to the right of the
/// report/master-list it exports — never a stacked overlay, mirroring <see cref="ReportConfigViewModel"/> and
/// <see cref="PrintConfigViewModel"/>. It picks the output <see cref="Format"/> (CSV / XLSX / PDF), the target
/// <see cref="Folder"/> + base <see cref="FileName"/>, and an optional <see cref="AppendTimestamp"/> toggle.
///
/// <para>On <see cref="Apply"/> it projects the open report into a <see cref="TabularExport"/> (money as exact
/// Number cells so a spreadsheet can sum them; RQ-15) and calls the matching writer in <c>Apex.Ledger.Io</c>
/// (<see cref="CsvWriter"/> / <see cref="XlsxWriter"/>, or the existing <see cref="ReportPdf"/> for PDF), then
/// writes the bytes to <see cref="ExportConfig.FullPath"/>. The IO layer has <b>no clock</b> (ER-8): the
/// timestamp <i>value</i> is computed by this thin layer (from the injected "today"/now) and passed in as a
/// string, so the writers stay deterministic. All IO stays in the Io project (ER-12) — this VM only builds the
/// config + model, calls the writer, and writes the stream. Output is de-branded — never a third-party brand.</para>
/// </summary>
public sealed partial class ExportViewModel : ViewModelBase
{
    private readonly ReportsViewModel _report;

    /// <summary>The "now" the timestamp suffix is derived from — injected so the VM stays deterministic in tests
    /// (the Io layer itself has no clock; the UI passes a value in).</summary>
    private readonly DateTime _now;

    /// <summary>Optional seam so tests can capture the written bytes/path without touching disk. Null ⇒ write
    /// to the real filesystem via <see cref="File.WriteAllBytes(string, byte[])"/>.</summary>
    private readonly Action<string, byte[]>? _writeBytes;

    public string Title => "Export";

    /// <summary>The report/master-list being exported (its heading line).</summary>
    public string DocumentTitle => _report.Title;

    /// <summary>The chosen export format. Changing it refreshes the derived file name + extension hints.</summary>
    [ObservableProperty] private ExportFormat _format = ExportFormat.Csv;

    /// <summary>The target folder (defaults to the user's Documents folder in the shell).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>The base file name without extension (defaults to the report title).</summary>
    [ObservableProperty] private string _fileName = "Export";

    /// <summary>Append the current timestamp to the file name (<c>Name_yyyyMMdd-HHmm.ext</c>). Off by default.</summary>
    [ObservableProperty] private bool _appendTimestamp;

    /// <summary>A status line shown after Apply (success path + byte count, or the failure reason).</summary>
    [ObservableProperty] private string _status = string.Empty;

    // Radio-style bindings for the format choice (one true at a time).
    public bool IsCsv { get => Format == ExportFormat.Csv; set { if (value) Format = ExportFormat.Csv; } }
    public bool IsXlsx { get => Format == ExportFormat.Xlsx; set { if (value) Format = ExportFormat.Xlsx; } }
    public bool IsPdf { get => Format == ExportFormat.Pdf; set { if (value) Format = ExportFormat.Pdf; } }

    /// <summary>Shell ctor: seed from the open report; default the folder to Documents and the name to the title.</summary>
    public ExportViewModel(ReportsViewModel report)
        : this(report, DefaultFolder(), DateTime.Now, writeBytes: null) { }

    /// <summary>Testable ctor: inject the folder, the "now" used for the timestamp, and an optional write seam.</summary>
    public ExportViewModel(ReportsViewModel report, string folder, DateTime now, Action<string, byte[]>? writeBytes)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _now = now;
        _writeBytes = writeBytes;
        Folder = folder ?? string.Empty;
        FileName = SafeName(report.Title);
    }

    partial void OnFormatChanged(ExportFormat value)
    {
        OnPropertyChanged(nameof(IsCsv));
        OnPropertyChanged(nameof(IsXlsx));
        OnPropertyChanged(nameof(IsPdf));
        OnPropertyChanged(nameof(ExtensionHint));
        OnPropertyChanged(nameof(ResolvedFileName));
    }

    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(ResolvedFileName));
    partial void OnAppendTimestampChanged(bool value) => OnPropertyChanged(nameof(ResolvedFileName));

    /// <summary>The extension the chosen format will use (no dot), for the panel hint.</summary>
    public string ExtensionHint => BuildConfig().Extension;

    /// <summary>The final file name the export will write (Name[_timestamp].ext), for the panel hint.</summary>
    public string ResolvedFileName => BuildConfig().ResolvedFileName;

    /// <summary>Builds the <see cref="ExportConfig"/> from the panel's current knobs. The timestamp VALUE is
    /// formatted here from the injected "now" (the Io layer has no clock) and passed in when the toggle is on.</summary>
    public ExportConfig BuildConfig() => new()
    {
        Format = Format,
        Folder = Folder ?? string.Empty,
        FileName = string.IsNullOrWhiteSpace(FileName) ? SafeName(_report.Title) : FileName.Trim(),
        TimestampSuffix = AppendTimestamp ? _now.ToString("yyyyMMdd-HHmm") : null,
    };

    /// <summary>
    /// Projects the report to a <see cref="TabularExport"/> and writes the chosen format's bytes to the resolved
    /// path. CSV/XLSX go through <see cref="CsvWriter"/>/<see cref="XlsxWriter"/>; PDF re-uses the existing
    /// <see cref="ReportPdf"/> path via <see cref="ReportPrintProjector"/>. Returns true on success and sets a
    /// status line either way. All figures come from the on-screen rows — nothing is recomputed.
    /// </summary>
    public bool Apply()
    {
        var config = BuildConfig();
        try
        {
            byte[] bytes = Format switch
            {
                ExportFormat.Csv => CsvWriter.Write(ReportTabularProjector.Project(_report)),
                ExportFormat.Xlsx => XlsxWriter.Write(ReportTabularProjector.Project(_report)),
                ExportFormat.Pdf => ReportPdf.Render(ReportPrintProjector.Project(_report), new PageConfig
                {
                    FooterText = "Apex Solutions  -  Page {page} of {pages}",
                }),
                _ => Array.Empty<byte>(),
            };

            string path = config.FullPath;
            if (_writeBytes is not null) _writeBytes(path, bytes);
            else File.WriteAllBytes(path, bytes);

            Status = $"Exported {bytes.Length:#,0} bytes to {path}";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Could not export: " + ex.Message;
            return false;
        }
    }

    private static string DefaultFolder()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { return string.Empty; }
    }

    /// <summary>Turns a report title into a safe file-name stem (invalid path chars → '_'; blank → "Report").</summary>
    private static string SafeName(string? title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Report" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }
}
