using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>One already-imported statement snapshot listed on the Import screen.</summary>
public sealed class ImportedSnapshotRowVm
{
    public string Statement { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string ImportedAt { get; init; } = string.Empty;
    public string Lines { get; init; } = string.Empty;
    public string Tax { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Import GSTR-2B</b> action screen (Reports → Statutory Reports → GST Actions → Import GSTR-2B; Phase 9 UI-2;
/// RQ-13). Reads a portal-downloaded GSTR-2B / 2A JSON from a chosen file, parses it with the pure
/// <see cref="Gstr2bJsonParser"/>, and — only on an explicit Import — materialises it into the company through
/// <see cref="Gstr2bImportService.Import"/>. The imported snapshot is what the 2B reconciliation, the ITC gate and the
/// IMS screens all read.
///
/// <para><b>All-or-nothing (RQ-21/23).</b> The parser is <b>result-typed</b>, not exception-throwing: a malformed
/// document, a wrong root, a bad period/date, or — the one that matters most — a <b>GSTIN that is not this
/// company's</b> comes back as <c>Parsed: false</c> with an error code, and <b>never</b> as a partial statement. The
/// screen reports the code + message and imports nothing; the company is untouched.</para>
///
/// <para>File access follows the app's existing import convention (<see cref="ImportDataViewModel"/>): an absolute
/// path typed into the panel, with an injectable read seam so tests supply bytes without touching disk.</para>
///
/// <para><b>Opening this screen imports nothing.</b> Gated: Regular GST company (ER-13).</para>
/// </summary>
public sealed partial class ImportGstr2bViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly Func<string, byte[]>? _readBytes;
    private readonly Func<DateTimeOffset> _now;

    [ObservableProperty] private string _title = "Import GSTR-2B";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastImportSucceeded;

    /// <summary>The portal JSON to read (an absolute path typed/pasted into the panel).</summary>
    [ObservableProperty] private string _filePath = string.Empty;

    /// <summary>Which statement the file is (GSTR-2B — the static ITC gatekeeper — or the dynamic GSTR-2A).</summary>
    [ObservableProperty] private GstStatementType _statementType = GstStatementType.Gstr2b;

    /// <summary>The parser's error code from the last failed import (e.g. <c>GSTIN_MISMATCH</c>), else null.</summary>
    [ObservableProperty] private string? _errorCode;

    /// <summary>The statements this file could be imported as.</summary>
    public ObservableCollection<GstStatementType> StatementTypes { get; } =
        new(new[] { GstStatementType.Gstr2b, GstStatementType.Gstr2a });

    /// <summary>Every snapshot already imported into the company (latest first).</summary>
    public ObservableCollection<ImportedSnapshotRowVm> Imported { get; } = new();

    /// <summary>Shell ctor: import into <paramref name="company"/>, persist via <paramref name="storage"/> on success.</summary>
    public ImportGstr2bViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
        : this(company, storage, onChanged, readBytes: null, now: null) { }

    /// <summary>Testable ctor: inject a read seam + a clock so tests import deterministically without a real file.</summary>
    public ImportGstr2bViewModel(
        Company company, CompanyStorage storage, Action? onChanged,
        Func<string, byte[]>? readBytes, Func<DateTimeOffset>? now)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _readBytes = readBytes;
        _now = now ?? (() => DateTimeOffset.Now);
        Rebuild();
    }

    /// <summary>(Re)lists the already-imported snapshots. Imports nothing.</summary>
    public void Rebuild()
    {
        Imported.Clear();
        foreach (var s in _company.Gstr2bSnapshots.OrderByDescending(s => s.ImportedAt))
            Imported.Add(new ImportedSnapshotRowVm
            {
                Statement = s.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A",
                Period = s.ReturnPeriod,
                ImportedAt = s.ImportedAt.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                Lines = s.Lines.Count.ToString(CultureInfo.InvariantCulture),
                Tax = R(s.SummaryIgstPaisa + s.SummaryCgstPaisa + s.SummarySgstPaisa + s.SummaryCessPaisa),
            });

        var gstin = _company.Gst?.Gstin ?? "—";
        Subtitle = $"{_company.Name}  —  GSTIN {gstin}";
        StatusText = Imported.Count == 0
            ? "No statement imported yet — choose the portal's GSTR-2B JSON and import it."
            : $"{Imported.Count} statement(s) imported. The 2B reconciliation, ITC gate and IMS screens read them.";
    }

    // ---------------------------------------------------------------- the explicit action (the only mutator)

    [RelayCommand] private void ImportAction() => Import();

    /// <summary>
    /// Reads + parses the chosen file and, only if it parses cleanly <b>and</b> is addressed to this company's GSTIN,
    /// materialises the snapshot into the company. Any failure imports nothing at all (all-or-nothing).
    /// </summary>
    public bool Import()
    {
        Message = null;
        ErrorCode = null;
        LastImportSucceeded = false;

        if (string.IsNullOrWhiteSpace(FilePath))
            return Fail(null, "Enter the path of the portal's GSTR-2B JSON file.");

        var gstin = _company.Gst?.Gstin;
        if (string.IsNullOrWhiteSpace(gstin))
            return Fail(null, "This company has no GSTIN — enable GST before importing a statement.");

        byte[] bytes;
        try
        {
            bytes = _readBytes is not null ? _readBytes(FilePath) : File.ReadAllBytes(FilePath);
        }
        catch (Exception ex)
        {
            return Fail(null, "Could not read the file: " + ex.Message);
        }

        // The parser is result-typed: a malformed / wrong-GSTIN file comes back Parsed:false, never a half statement.
        var result = Gstr2bJsonParser.Parse(bytes, StatementType, gstin!);
        if (!result.Parsed || result.Statement is null)
            return Fail(result.ErrorCode, Explain(result) + " Nothing was imported — the company is unchanged.");

        var snapshot = Gstr2bImportService.Import(_company, result.Statement, _now());
        _storage.Save(_company);
        Rebuild();

        LastImportSucceeded = true;
        Message = $"Imported {(snapshot.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A")} " +
                  $"{snapshot.ReturnPeriod} — {snapshot.Lines.Count} line(s), tax " +
                  $"₹{R(snapshot.SummaryIgstPaisa + snapshot.SummaryCgstPaisa + snapshot.SummarySgstPaisa + snapshot.SummaryCessPaisa)}.";
        _onChanged();
        return true;
    }

    /// <summary>Turns the parser's error code into an explanation that says what to do about it.</summary>
    private string Explain(Gstr2bImportResult r) => r.ErrorCode switch
    {
        "MALFORMED_JSON" => "The file is not valid JSON.",
        "BAD_ROOT" => "The file's root is not a JSON object — this is not a portal statement.",
        "NO_GSTIN" => "The file carries no GSTIN.",
        "GSTIN_MISMATCH" => $"This statement is addressed to a different GSTIN — it is not {_company.Gst?.Gstin}'s 2B.",
        "BAD_PERIOD" => "The file's return period is malformed (expected MMyyyy or yyyy-MM).",
        "BAD_GENDT" => "The file's generation date is malformed.",
        "BAD_SUMMARY" => "The file's ITC summary carries a non-numeric amount.",
        "BAD_LINE" => "A document line in the file is malformed.",
        _ => r.ErrorMessage ?? "The file could not be parsed.",
    };

    private bool Fail(string? code, string message)
    {
        ErrorCode = code;
        Message = code is null ? message : $"{code}: {message}";
        LastImportSucceeded = false;
        return false;
    }

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
