using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>One outward voucher on the e-Invoice screen: its identity, its e-invoice coverage, and the state of the
/// record raised against it (if any).</summary>
public sealed partial class EInvoiceCandidateRowVm : ViewModelBase
{
    public Guid VoucherId { get; init; }
    public string DocNo { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    /// <summary>The engine's coverage verdict (Covered / Excluded / Exempt / NotApplicable).</summary>
    public string Coverage { get; init; } = string.Empty;

    /// <summary>The record's status, or "—" when none has been prepared yet.</summary>
    public string Status { get; init; } = string.Empty;

    public string Irn { get; init; } = "—";

    /// <summary>True when the engine says this voucher must carry an e-invoice (the only kind PrepareRecord accepts).</summary>
    public bool IsCovered { get; init; }

    /// <summary>True once a record exists for the voucher (a second PrepareRecord would be refused).</summary>
    public bool HasRecord { get; init; }

    public string Note { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Generate e-Invoice</b> action screen (Reports → Statutory Reports → GST Actions → Generate e-Invoice;
/// Phase 9 UI-2; RQ-24). Drives the pure <see cref="EInvoiceService"/> over the company's outward vouchers:
/// <b>prepares</b> the record (<see cref="EInvoiceService.PrepareRecord"/>), writes the <b>offline INV-01 JSON</b>
/// (<see cref="EInvoiceJson.BuildInv01"/>) for upload to the IRP, and then records what the portal returned —
/// the <b>IRN / Ack / signed QR</b> (<see cref="EInvoiceService.RecordIrpResponse"/>), a
/// <b>failure</b> (<see cref="EInvoiceService.RecordFailure"/>), or a <b>cancellation</b>
/// (<see cref="EInvoiceService.Cancel"/>).
///
/// <para><b>Offline-JSON mode is the default and the only mode</b> — the app holds <b>zero portal credentials</b> and
/// never calls the IRP. The taxpayer uploads the JSON and types back what the portal issued; the IRN / Ack / QR are
/// always <b>copied, never derived</b>.</para>
///
/// <para><b>Engine guards surfaced, never crashed into:</b> only a <b>Covered</b> voucher can be prepared (a B2C or
/// exempt supply is Excluded — it carries no IRN); a document number is <b>never reusable</b> (not even after a
/// cancellation); an IRN can be recorded only against a <b>Pending / Failed</b> record (never over a Generated one);
/// and a cancellation is only legal <b>within one day of the Ack date</b>.</para>
///
/// <para><b>Opening this screen prepares nothing.</b> Gated: Regular GST company (ER-13).</para>
/// </summary>
public sealed partial class GenerateEInvoiceViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly EInvoiceService _service;
    private readonly Action<string, byte[]>? _writeBytes;
    private readonly DateOnly _today;

    [ObservableProperty] private string _title = "Generate e-Invoice";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private bool _hasCandidates;

    /// <summary>Where the offline INV-01 JSON is written (defaults to Documents).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>The last INV-01 file written (shown so the taxpayer knows what to upload).</summary>
    [ObservableProperty] private string? _lastJsonPath;

    // The portal-response form — every value is COPIED from the IRP, never derived.
    [ObservableProperty] private string _irn = string.Empty;
    [ObservableProperty] private string _ackNo = string.Empty;
    [ObservableProperty] private string _ackDateText = string.Empty;
    [ObservableProperty] private string _signedQr = string.Empty;
    [ObservableProperty] private string _errorCode = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _cancelReasonCode = "1";

    /// <summary>The company's outward vouchers + their coverage / record state.</summary>
    public ObservableCollection<EInvoiceCandidateRowVm> Rows { get; } = new();

    /// <summary>The IRP's fixed cancellation reason codes — <b>1</b> Duplicate · <b>2</b> Data-entry mistake ·
    /// <b>3</b> Order cancelled · <b>4</b> Other. The recorded statutory reason must be the one actually filed, so it
    /// is picked rather than assumed.</summary>
    public ObservableCollection<string> CancelReasonCodes { get; } = new(new[] { "1", "2", "3", "4" });

    /// <summary>Shell ctor.</summary>
    public GenerateEInvoiceViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
        : this(company, storage, onChanged, DefaultFolder(), DateOnly.FromDateTime(DateTime.Today), writeBytes: null) { }

    /// <summary>Testable ctor: inject the folder, "today" and a write seam so tests stay deterministic and diskless.</summary>
    public GenerateEInvoiceViewModel(
        Company company, CompanyStorage storage, Action? onChanged,
        string folder, DateOnly today, Action<string, byte[]>? writeBytes)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _service = new EInvoiceService(company);
        _writeBytes = writeBytes;
        _today = today;
        _folder = folder ?? string.Empty;
        // WI-5: seed in the ONE canonical UI spelling, so the field the user sees matches every other date field.
        _ackDateText = ApexDate.Format(today);
        Rebuild();
    }

    /// <summary>The highlighted voucher (what every action acts on).</summary>
    public EInvoiceCandidateRowVm? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Rows.Count ? Rows[HighlightedIndex] : null;

    /// <summary>(Re)projects the outward vouchers + their coverage / record state. Prepares nothing.</summary>
    public void Rebuild()
    {
        var keepIndex = HighlightedIndex;
        Rows.Clear();

        foreach (var v in _company.Vouchers.Where(v => !v.Cancelled).OrderBy(v => v.Date).ThenBy(v => v.Number))
        {
            var coverage = _service.CoverageOf(v);
            var docNo = EInvoiceService.DocumentNumberOf(v);
            var record = _company.EInvoiceRecords.FirstOrDefault(r => r.SourceVoucherId == v.Id);

            // Only outward vouchers are worth listing; everything else is NotApplicable noise.
            if (coverage == EInvoiceCoverage.NotApplicable && record is null) continue;

            Rows.Add(new EInvoiceCandidateRowVm
            {
                VoucherId = v.Id,
                DocNo = docNo,
                Date = ApexDate.Format(v.Date),
                Value = IndianFormat.AmountAlways(v.Lines.Where(l => l.Side == DrCr.Debit)
                    .Aggregate(Money.Zero, (a, l) => new Money(a.Amount + l.Amount.Amount))),
                Coverage = CoverageLabel(coverage),
                Status = record?.Status.ToString() ?? "—",
                Irn = Shorten(record?.Irn),
                IsCovered = coverage == EInvoiceCoverage.Covered,
                HasRecord = record is not null,
                Note = NoteFor(coverage, record),
            });
        }

        HasCandidates = Rows.Count > 0;
        HighlightedIndex = Rows.Count == 0 ? -1 : Math.Clamp(keepIndex < 0 ? 0 : keepIndex, 0, Rows.Count - 1);
        OnHighlightedIndexChanged(HighlightedIndex);

        var generated = Rows.Count(r => r.Status == nameof(EInvoiceStatus.Generated));
        Subtitle = $"{_company.Name}  —  offline INV-01 mode (no portal credentials; the IRN / QR are copied back from the IRP)";
        StatusText = Rows.Count == 0
            ? "No outward voucher is covered by e-invoicing yet."
            : $"{Rows.Count(r => r.IsCovered)} covered  ·  {generated} generated  ·  " +
              $"{Rows.Count(r => !r.HasRecord && r.IsCovered)} awaiting an INV-01.";
    }

    /// <summary>Moves the row highlight (Up/Down within the page); wraps.</summary>
    public void MoveHighlight(int direction)
    {
        if (Rows.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Rows.Count) % Rows.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsHighlighted = i == value;
        OnPropertyChanged(nameof(HighlightedRow));
    }

    // ---------------------------------------------------------------- the explicit actions (the only mutators)

    [RelayCommand] private void PrepareAction() => PrepareAndWriteJson();
    [RelayCommand] private void RecordIrnAction() => RecordIrpResponse();
    [RelayCommand] private void RecordFailureAction() => RecordFailure();
    [RelayCommand] private void CancelAction() => Cancel();

    /// <summary>
    /// Prepares the highlighted voucher's e-invoice record and writes its <b>offline INV-01 JSON</b> for upload to the
    /// IRP. The record starts <b>Pending</b>: no IRN exists until the portal issues one and it is recorded back.
    /// </summary>
    public bool PrepareAndWriteJson()
    {
        Message = null;
        LastActionSucceeded = false;

        var row = HighlightedRow;
        if (row is null) return Fail("Highlight an outward voucher first.");
        var voucher = _company.Vouchers.FirstOrDefault(v => v.Id == row.VoucherId);
        if (voucher is null) return Fail("The voucher no longer exists.");

        EInvoiceRecord record;
        try
        {
            record = _service.PrepareRecord(voucher);
        }
        catch (InvalidOperationException ex)   // not covered / already prepared / doc-no already used
        {
            return Fail(ex.Message);
        }

        byte[] json;
        try
        {
            json = EInvoiceJson.BuildInv01(_company, voucher);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        var path = Path.Combine(Folder ?? string.Empty, $"INV01_{Safe(record.DocumentNumberUpper)}.json");
        try
        {
            if (_writeBytes is not null) _writeBytes(path, json);
            else File.WriteAllBytes(path, json);
        }
        catch (Exception ex)
        {
            // The record was prepared but the file could not be written — say exactly that, so the taxpayer does not
            // think the INV-01 is sitting on disk waiting to be uploaded.
            _storage.Save(_company);
            Rebuild();
            return Fail($"The e-invoice record was prepared, but the INV-01 could not be written: {ex.Message}");
        }

        _storage.Save(_company);
        LastJsonPath = path;
        Rebuild();
        LastActionSucceeded = true;
        Message = $"INV-01 written for {record.DocumentNumberUpper} ({json.Length:#,0} bytes) → {path}. " +
                  "Upload it to the IRP, then record the IRN it returns.";
        _onChanged();
        return true;
    }

    /// <summary>Records the IRP's response against the highlighted voucher's record — the IRN / Ack / signed QR, all
    /// copied from the portal.</summary>
    public bool RecordIrpResponse()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (string.IsNullOrWhiteSpace(Irn)) return Fail("Enter the IRN the portal returned.");
        if (!TryParseDate(AckDateText, out var ackDate))
            return Fail($"Acknowledgement date: {ApexDate.ErrorFor(AckDateText)}");

        try
        {
            _service.RecordIrpResponse(record!, Irn.Trim(), AckNo.Trim(), ackDate, SignedQr.Trim(), signedJson: Array.Empty<byte>());
        }
        catch (InvalidOperationException ex)   // not Pending/Failed — a Generated record is immutable
        {
            return Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        return Succeed($"IRN recorded for {record!.DocumentNumberUpper} — the e-invoice is Generated.");
    }

    /// <summary>Records an IRP rejection against the highlighted voucher's record (it stays re-submittable).</summary>
    public bool RecordFailure()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (string.IsNullOrWhiteSpace(ErrorCode)) return Fail("Enter the error code the portal returned.");

        _service.RecordFailure(record!, ErrorCode.Trim(), ErrorMessage.Trim());
        return Succeed($"IRP rejection recorded for {record!.DocumentNumberUpper} — fix it and re-submit the INV-01.");
    }

    /// <summary>Cancels the highlighted voucher's e-invoice on the portal (legal only within one day of the Ack).</summary>
    public bool Cancel()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (string.IsNullOrWhiteSpace(CancelReasonCode)) return Fail("Enter the portal's cancellation reason code.");

        try
        {
            _service.Cancel(record!, _today, CancelReasonCode.Trim());
        }
        catch (InvalidOperationException ex)   // not Generated / no Ack date / past the ack+1 window
        {
            return Fail(ex.Message);
        }

        return Succeed($"e-Invoice cancelled for {record!.DocumentNumberUpper}. " +
                       "Note the document number can never be reused — raise a fresh one.");
    }

    private bool TryRecord(out EInvoiceRecord? record, out string? error)
    {
        record = null;
        error = null;
        var row = HighlightedRow;
        if (row is null) { error = "Highlight an outward voucher first."; return false; }
        record = _company.EInvoiceRecords.FirstOrDefault(r => r.SourceVoucherId == row.VoucherId);
        if (record is null)
        {
            error = "No e-invoice has been prepared for this voucher yet — prepare the INV-01 first.";
            return false;
        }
        return true;
    }

    private bool Succeed(string message)
    {
        _storage.Save(_company);
        Rebuild();
        LastActionSucceeded = true;
        Message = message;
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    private static string NoteFor(EInvoiceCoverage coverage, EInvoiceRecord? record)
    {
        if (record is { Status: EInvoiceStatus.Failed })
            return $"IRP rejected: {record.ErrorCode} — {record.ErrorMessage}";
        if (record is { Status: EInvoiceStatus.Cancelled })
            return $"Cancelled on {record.CancelledOn:dd-MMM-yyyy} (reason {record.CancelReasonCode}). The document number is spent.";
        if (record is { Status: EInvoiceStatus.Generated })
            return $"Ack {record.AckNo} on {record.AckDate:dd-MMM-yyyy}. The signed QR must be printed on the invoice.";
        if (record is { Status: EInvoiceStatus.Pending })
            return "INV-01 prepared — upload it to the IRP and record the IRN it returns.";
        return coverage switch
        {
            EInvoiceCoverage.Covered => "Covered — an e-invoice is mandatory before the invoice is issued.",
            EInvoiceCoverage.Excluded => "Excluded — B2C / zero-value supply; no IRN is raised (a B2C QR may still apply).",
            EInvoiceCoverage.Exempt => "Exempt supply — outside e-invoicing.",
            _ => "Not applicable.",
        };
    }

    private static string CoverageLabel(EInvoiceCoverage c) => c switch
    {
        EInvoiceCoverage.Covered => "Covered",
        EInvoiceCoverage.Excluded => "Excluded",
        EInvoiceCoverage.Exempt => "Exempt",
        _ => "N/A",
    };

    /// <summary>Shortens a 64-char IRN for the grid (the full value stays on the record).</summary>
    private static string Shorten(string? irn) =>
        string.IsNullOrWhiteSpace(irn) ? "—" : irn.Length <= 16 ? irn : irn[..8] + "…" + irn[^4..];

    // WI-5: the Ack Date is a TYPED UI field, so it follows the app-wide canonical contract like every other
    // UI date. (Its Io-side counterpart — the GSTN portal wire format — is deliberately NOT touched: that
    // dd-MM-yyyy is dictated externally, not a UI inconsistency.)
    private static bool TryParseDate(string text, out DateOnly date) => ApexDate.TryParse(text, out date);

    private static string Safe(string name) =>
        string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string DefaultFolder()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { return string.Empty; }
    }
}
