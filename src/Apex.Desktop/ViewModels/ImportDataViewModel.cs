using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The import source format the "Import" panel can read.</summary>
public enum ImportDataFormat
{
    /// <summary>Canonical JSON envelope (RQ-19) — the full, lossless model.</summary>
    Json,

    /// <summary>Canonical XML envelope (DP-4) — the full, lossless model.</summary>
    Xml,

    /// <summary>Best-effort flat CSV of masters/vouchers by name (§DP-5).</summary>
    Csv,
}

/// <summary>
/// The keyboard-first "Import" panel (O / Alt+O; RQ-20..24): reads a canonical JSON/XML backup (or a flat CSV) from a
/// chosen file and applies it INTO the open company through the engine-routed <see cref="CompanyImportService"/> —
/// masters created via the domain master-create path, vouchers posted through the validator (ER-6). Validation is
/// before-apply and the apply is transactional (all-or-nothing, RQ-21/23): if any record is invalid nothing is
/// written and the per-record messages are surfaced. The user picks the duplicate <see cref="Policy"/>
/// (Skip / Merge-opening / Reject-batch, RQ-24).
///
/// <para>Thin layer only (ER-12): it picks file + format + policy, reads the bytes, calls the parser in
/// <c>Apex.Ledger.Io</c> (<see cref="CanonicalJson.Parse"/> / <see cref="CanonicalXml.Parse"/> /
/// <see cref="CsvImport.Parse"/> → <see cref="CsvCanonicalBridge.ToModel"/>) and hands the model to
/// <see cref="CompanyImportService.Apply"/>. It holds NO import logic. On success it persists the mutated company via
/// <see cref="CompanyStorage.Save"/> and reports the created/reused/posted counts; on failure it lists the errors and
/// the company is unchanged (the service applied nothing).</para>
/// </summary>
public sealed partial class ImportDataViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action? _onImported;

    /// <summary>Optional seam so tests can supply the file bytes without touching disk. Null ⇒ read <see cref="FilePath"/>.</summary>
    private readonly Func<string, byte[]>? _readBytes;

    public string Title => "Import";

    /// <summary>The company the import applies into (its name), shown under the panel heading.</summary>
    public string DocumentTitle => _company.Name;

    /// <summary>The source file to read (an absolute path typed/pasted into the panel).</summary>
    [ObservableProperty] private string _filePath = string.Empty;

    /// <summary>The chosen source format (JSON / XML / CSV).</summary>
    [ObservableProperty] private ImportDataFormat _format = ImportDataFormat.Json;

    /// <summary>The duplicate-master policy (RQ-24): Skip / MergeOpeningBalance / RejectBatch.</summary>
    [ObservableProperty] private DuplicatePolicy _policy = DuplicatePolicy.Skip;

    /// <summary>A status line shown after Apply (success summary, or "nothing was applied").</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Per-record error messages from the last Apply (empty on success). Shown in the panel.</summary>
    public ObservableCollection<string> Errors { get; } = new();

    // Radio-style bindings for the format choice (one true at a time).
    public bool IsJson { get => Format == ImportDataFormat.Json; set { if (value) Format = ImportDataFormat.Json; } }
    public bool IsXml { get => Format == ImportDataFormat.Xml; set { if (value) Format = ImportDataFormat.Xml; } }
    public bool IsCsv { get => Format == ImportDataFormat.Csv; set { if (value) Format = ImportDataFormat.Csv; } }

    // Radio-style bindings for the duplicate policy (one true at a time).
    public bool IsSkip { get => Policy == DuplicatePolicy.Skip; set { if (value) Policy = DuplicatePolicy.Skip; } }
    public bool IsMerge { get => Policy == DuplicatePolicy.MergeOpeningBalance; set { if (value) Policy = DuplicatePolicy.MergeOpeningBalance; } }
    public bool IsReject { get => Policy == DuplicatePolicy.RejectBatch; set { if (value) Policy = DuplicatePolicy.RejectBatch; } }

    /// <summary>Shell ctor: import into <paramref name="company"/>, persist via <paramref name="storage"/> on success.</summary>
    public ImportDataViewModel(Company company, CompanyStorage storage, Action? onImported = null)
        : this(company, storage, onImported, readBytes: null) { }

    /// <summary>Testable ctor: inject a read seam so tests supply bytes without a real file.</summary>
    public ImportDataViewModel(Company company, CompanyStorage storage, Action? onImported, Func<string, byte[]>? readBytes)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onImported = onImported;
        _readBytes = readBytes;
    }

    partial void OnFormatChanged(ImportDataFormat value)
    {
        OnPropertyChanged(nameof(IsJson));
        OnPropertyChanged(nameof(IsXml));
        OnPropertyChanged(nameof(IsCsv));
    }

    partial void OnPolicyChanged(DuplicatePolicy value)
    {
        OnPropertyChanged(nameof(IsSkip));
        OnPropertyChanged(nameof(IsMerge));
        OnPropertyChanged(nameof(IsReject));
    }

    /// <summary>
    /// Reads the chosen file, parses it by format via <c>Apex.Ledger.Io</c>, and applies it into the open company
    /// through <see cref="CompanyImportService"/> with the chosen policy. On success persists the company and reports
    /// the counts; on any failure (missing/unreadable file, malformed document, an invalid/unbalanced/dangling
    /// record) it lists the errors and leaves the company unchanged. Returns whether the import applied.
    /// </summary>
    public bool Apply()
    {
        Errors.Clear();
        Status = string.Empty;

        byte[] bytes;
        try
        {
            bytes = _readBytes is not null ? _readBytes(FilePath) : File.ReadAllBytes(FilePath);
        }
        catch (Exception ex)
        {
            Fail(new[] { "Could not read the file: " + ex.Message });
            return false;
        }

        // Parse by format (structural rejects surface here) — CSV is bridged to the same canonical model.
        CanonicalModel? model;
        IReadOnlyList<string> parseErrors;
        switch (Format)
        {
            case ImportDataFormat.Xml:
                (model, parseErrors) = CanonicalXml.Parse(bytes);
                break;
            case ImportDataFormat.Csv:
                var csv = CsvImport.Parse(bytes);
                (model, parseErrors) = CsvCanonicalBridge.ToModel(csv, _company);
                break;
            default:
                (model, parseErrors) = CanonicalJson.Parse(bytes);
                break;
        }

        if (model is null)
        {
            Fail(parseErrors.Count > 0 ? parseErrors : new[] { "The file is not a valid import document." });
            return false;
        }

        // Engine-routed, validate-before-apply, transactional apply (ER-6 / RQ-21 / RQ-23).
        var result = new CompanyImportService(_company).Apply(model, Policy);
        if (!result.Applied)
        {
            Fail(result.Errors);
            return false;
        }

        // Persist the mutated company and refresh the shell.
        _storage.Save(_company);
        _onImported?.Invoke();

        Status = $"Imported: {result.MastersCreated} master(s) created, {result.MastersReused} reused, " +
                 $"{result.VouchersPosted} voucher(s) posted.";
        return true;
    }

    private void Fail(IEnumerable<string> messages)
    {
        foreach (var m in messages) Errors.Add(m);
        Status = "Nothing was applied — the company is unchanged.";
    }
}
