using System;
using System.IO;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The canonical-backup serialisation the "Export Data" panel can write.</summary>
public enum CompanyExportFormat
{
    /// <summary>Canonical JSON envelope (RQ-19) — the default, human-inspectable backup.</summary>
    Json,

    /// <summary>Canonical XML envelope (DP-4) — the same payload as JSON, serialised as XML.</summary>
    Xml,
}

/// <summary>
/// The keyboard-first "Export Data" panel (RQ-19 / DP-4): a canonical, lossless backup of the WHOLE open company —
/// masters (groups, ledgers, stock, parties, voucher types) and every voucher (accounting + item-invoice, with GST)
/// — to a versioned envelope whose money is integer paisa and whose ordering is deterministic. It complements the
/// slice-10 report/master-list export (<see cref="ExportViewModel"/>, CSV/XLSX/PDF of one open report); this panel
/// exports the entire company so it can be re-imported into a fresh company via the Import panel and reconcile to the
/// paisa (PR-4).
///
/// <para>Thin layer only (ER-12): it picks the <see cref="Format"/> (JSON / XML), the <see cref="Folder"/> +
/// <see cref="FileName"/> and an optional timestamp, then calls the matching writer in <c>Apex.Ledger.Io</c>
/// (<see cref="CanonicalJson.Export(Company)"/> / <see cref="CanonicalXml.Export(Company)"/>) and writes the bytes.
/// The IO layer has no clock (ER-8): the timestamp <i>value</i> is formatted here from the injected "now". Output is
/// de-branded and byte-stable, carrying no third-party brand text.</para>
/// </summary>
public sealed partial class ExportDataViewModel : ViewModelBase
{
    private readonly Company _company;

    /// <summary>The "now" the timestamp suffix is derived from — injected so the VM stays deterministic in tests.</summary>
    private readonly DateTime _now;

    /// <summary>Optional seam so tests can capture the written bytes/path without touching disk.</summary>
    private readonly Action<string, byte[]>? _writeBytes;

    public string Title => "Export Data";

    /// <summary>The company being backed up (its name), shown under the panel heading.</summary>
    public string DocumentTitle => _company.Name;

    /// <summary>The chosen canonical format. Changing it refreshes the derived file name + extension hints.</summary>
    [ObservableProperty] private CompanyExportFormat _format = CompanyExportFormat.Json;

    /// <summary>The target folder (defaults to the user's Documents folder in the shell).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>The base file name without extension (defaults to the company name).</summary>
    [ObservableProperty] private string _fileName = "Company";

    /// <summary>Append the current timestamp to the file name (<c>Name_yyyyMMdd-HHmm.ext</c>). Off by default.</summary>
    [ObservableProperty] private bool _appendTimestamp;

    /// <summary>A status line shown after Apply (success path + byte count, or the failure reason).</summary>
    [ObservableProperty] private string _status = string.Empty;

    // Radio-style bindings for the format choice (one true at a time).
    public bool IsJson { get => Format == CompanyExportFormat.Json; set { if (value) Format = CompanyExportFormat.Json; } }
    public bool IsXml { get => Format == CompanyExportFormat.Xml; set { if (value) Format = CompanyExportFormat.Xml; } }

    /// <summary>Shell ctor: seed from the open company; default the folder to Documents and the name to the company.</summary>
    public ExportDataViewModel(Company company)
        : this(company, DefaultFolder(), DateTime.Now, writeBytes: null) { }

    /// <summary>Testable ctor: inject the folder, the "now" used for the timestamp, and an optional write seam.</summary>
    public ExportDataViewModel(Company company, string folder, DateTime now, Action<string, byte[]>? writeBytes)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _now = now;
        _writeBytes = writeBytes;
        Folder = folder ?? string.Empty;
        FileName = SafeName(company.Name);
    }

    partial void OnFormatChanged(CompanyExportFormat value)
    {
        OnPropertyChanged(nameof(IsJson));
        OnPropertyChanged(nameof(IsXml));
        OnPropertyChanged(nameof(ExtensionHint));
        OnPropertyChanged(nameof(ResolvedFileName));
    }

    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(ResolvedFileName));
    partial void OnAppendTimestampChanged(bool value) => OnPropertyChanged(nameof(ResolvedFileName));

    /// <summary>The extension the chosen format will use (no dot), for the panel hint.</summary>
    public string ExtensionHint => Format == CompanyExportFormat.Xml ? "xml" : "json";

    /// <summary>The final file name the export will write (Name[_timestamp].ext), for the panel hint.</summary>
    public string ResolvedFileName
    {
        get
        {
            var stem = string.IsNullOrWhiteSpace(FileName) ? SafeName(_company.Name) : FileName.Trim();
            if (AppendTimestamp) stem += "_" + _now.ToString("yyyyMMdd-HHmm");
            return stem + "." + ExtensionHint;
        }
    }

    /// <summary>The absolute path the export will write to.</summary>
    public string FullPath => Path.Combine(Folder ?? string.Empty, ResolvedFileName);

    /// <summary>
    /// Serialises the whole company to the chosen canonical format via <c>Apex.Ledger.Io</c> and writes the bytes to
    /// <see cref="FullPath"/>. Returns true on success and sets a status line either way. All IO stays in the Io
    /// project (ER-12) — this VM only picks the knobs, calls the writer, and writes the stream.
    /// </summary>
    public bool Apply()
    {
        try
        {
            byte[] bytes = Format switch
            {
                CompanyExportFormat.Xml => CanonicalXml.Export(_company),
                _ => CanonicalJson.Export(_company),
            };

            var path = FullPath;
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

    /// <summary>Turns a company name into a safe file-name stem (invalid path chars → '_'; blank → "Company").</summary>
    private static string SafeName(string? name)
    {
        var stem = string.IsNullOrWhiteSpace(name) ? "Company" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }
}
