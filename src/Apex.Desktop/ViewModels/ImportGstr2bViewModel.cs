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

    /// <summary>v66 — the registration this statement was imported for (census 6.23 / T2-121(ii)). "—" when the
    /// company has no GST block. Named on the row so two periods imported for two different GSTINs are
    /// distinguishable without opening either.</summary>
    public string Registration { get; init; } = string.Empty;
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

    /// <summary>
    /// 🔴 <b>THE VENDOR'S OWN <i>GST Registration</i> FIELD ON THIS SCREEN</b> — the registration the statement is
    /// being imported FOR (schema <b>v66</b>; census 6.23; gap register <b>T2-121(ii)</b>).
    ///
    /// <para><b>R7 — THE QUESTION T2-121(ii) ASKED IS ANSWERED <i>YES</i> BY THE VENDOR, BY CONTENT.</b> It asked
    /// <i>"may a GSTR-2B be imported for a NON-PRIMARY registration?"</i>, and recorded that if the answer were
    /// left open the ₹12,000-against-a-₹6,000-note duplicate re-opens.
    /// <c>help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/</c>, opened 2026-10-13,
    /// documents the field verbatim: <i>"GST Registration: It will be selected by default. In case of multiple GST
    /// registrations, select the required one."</i> So the answer is YES, and <b>refusing the import would have
    /// been a divergence rather than caution</b> — which is why this screen gains a field rather than a refusal.</para>
    ///
    /// <para>🔴 <b>AND THE DUPLICATE IS CLOSED BY THE ANCHOR, NOT BY THE REFUSAL.</b> What re-opened it was a
    /// statement whose <c>RecipientGstin</c> matched NO registration the company holds: every GSTIN-matching
    /// lookup then returned <c>null</c>, both layers fell through to the caller's own pick, and one portal credit
    /// note became postable against two returns. A stored registration <b>id</b> cannot fall through — see
    /// <see cref="Gstr2bSnapshot.GstRegistrationId"/>. The import is permitted AND anchored; those are the two
    /// halves of the one answer.</para>
    ///
    /// <para><b>Default = the primary registration</b>, which is byte-identical to every pre-v66 import (ER-13):
    /// the old path hard-coded the primary's GSTIN and the parser refused any other.</para>
    /// </summary>
    [ObservableProperty] private GstRegistration? _selectedRegistration;

    /// <summary>Every registration a statement could be imported for — the primary plus the additional ones.</summary>
    public ObservableCollection<GstRegistration> Registrations { get; } = new();

    /// <summary>Whether the picker is offered at all — false on a single-registration book, exactly as the
    /// vendor scopes its own sentence ("in case of multiple GST registrations"). ER-13.</summary>
    public bool CanSelectRegistration => _company.Gst is { IsMultiRegistration: true };

    /// <summary>The registration id the next import will anchor the snapshot to.</summary>
    public Guid ImportRegistrationId => SelectedRegistration?.Id ?? GstRegistration.PrimaryId;

    /// <summary>
    /// The GSTIN the parser must find on the file — <b>the SELECTED registration's</b>, not the company's.
    /// <para>🔴 This one expression is what makes a branch's own 2B importable at all. Before v66 the screen read
    /// <c>_company.Gst?.Gstin</c>, so the parser's <c>GSTIN_MISMATCH</c> refused a branch's or an ISD's statement
    /// outright, which is what T2-121(ii) recorded as "the only reason (iii) is unreachable today".</para>
    /// </summary>
    private string? ImportRecipientGstin =>
        SelectedRegistration?.Gstin ?? _company.Gst?.Gstin;

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
        // v66: the vendor's GST Registration field. Repopulated here (not only in the ctor) so a registration
        // added while this screen is open appears without reopening it — the stale-constructor-list shape
        // T2-121(v) files against the ITC gate.
        Registrations.Clear();
        foreach (var r in _company.Gst?.AllRegistrations ?? Array.Empty<GstRegistration>()) Registrations.Add(r);
        // "It will be selected by default" — the vendor's own words. The default is the primary, which is what
        // every pre-v66 import wrote. A selection that has since been deleted falls back rather than persisting.
        if (SelectedRegistration is null || Registrations.All(r => r.Id != SelectedRegistration.Id))
            SelectedRegistration = Registrations.FirstOrDefault(r => r.IsPrimary) ?? Registrations.FirstOrDefault();

        Imported.Clear();
        foreach (var s in _company.Gstr2bSnapshots.OrderByDescending(s => s.ImportedAt))
            Imported.Add(new ImportedSnapshotRowVm
            {
                Statement = s.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A",
                Period = s.ReturnPeriod,
                ImportedAt = ApexDate.Format(s.ImportedAt),
                Lines = s.Lines.Count.ToString(CultureInfo.InvariantCulture),
                Tax = R(s.SummaryIgstPaisa + s.SummaryCgstPaisa + s.SummarySgstPaisa + s.SummaryCessPaisa),
                // v66: WHICH registration this statement belongs to, named on the row rather than inferred from
                // its recipient GSTIN — see Gstr2bSnapshot.GstRegistrationId for why the id, not the GSTIN.
                Registration = _company.Gst?.FindRegistration(s.OwningRegistrationId)?.Name ?? "—",
            });

        var gstin = ImportRecipientGstin ?? "—";
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

        // 🔴 v66 (T2-121(ii)): the SELECTED registration's GSTIN, not the company's. See ImportRecipientGstin.
        var gstin = ImportRecipientGstin;
        if (string.IsNullOrWhiteSpace(gstin))
            return Fail(
                null,
                CanSelectRegistration
                    ? "The selected GST registration has no GSTIN, so no statement can be addressed to it. "
                    + "Choose a registration that carries one."
                    : "This company has no GSTIN — enable GST before importing a statement.");

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

        // 🔴 v66: ANCHOR the snapshot to the chosen registration. This is the half that closes the duplicate
        // T2-121(ii) warned would re-open — not the permission question, which the vendor answers YES.
        var registrationId = ImportRegistrationId;
        var snapshot = Gstr2bImportService.Import(
            _company, result.Statement, _now(), idFactory: null, gstRegistrationId: registrationId);
        _storage.Save(_company);
        Rebuild();

        LastImportSucceeded = true;
        var forWhich = CanSelectRegistration
            ? $" for {_company.Gst?.FindRegistration(registrationId)?.Name ?? "the primary registration"}"
            : string.Empty;
        Message = $"Imported {(snapshot.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A")} " +
                  $"{snapshot.ReturnPeriod}{forWhich} — {snapshot.Lines.Count} line(s), tax " +
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
