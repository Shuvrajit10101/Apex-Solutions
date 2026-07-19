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

/// <summary>One movement-bearing voucher on the e-Way Bill screen: its consignment value, the engine's coverage
/// verdict, and the state + validity of the bill raised against it.</summary>
public sealed partial class EWayCandidateRowVm : ViewModelBase
{
    public Guid VoucherId { get; init; }
    public string DocNo { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;

    /// <summary>The engine's <see cref="EWayBillService.ConsignmentValue"/> — what the ₹50,000 threshold is tested on.</summary>
    public string ConsignmentValue { get; init; } = string.Empty;

    public string Coverage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string EwbNumber { get; init; } = "—";

    /// <summary>The bill's validity, or "—" until the portal issues one.</summary>
    public string ValidUpto { get; init; } = "—";

    /// <summary>True when the engine says a bill is required (the only kind PrepareRecord accepts).</summary>
    public bool IsRequired { get; init; }

    public bool HasRecord { get; init; }
    public string Note { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Generate e-Way Bill</b> action screen (Reports → Statutory Reports → GST Actions → Generate e-Way Bill;
/// Phase 9 UI-2; RQ-25). Drives the pure <see cref="EWayBillService"/> over the company's movement-bearing vouchers:
/// <b>prepares</b> the record (<see cref="EWayBillService.PrepareRecord"/>), captures <b>Part-B</b>
/// (<see cref="EWayBillService.SetPartB"/> — transporter / mode / vehicle / distance), writes the <b>offline EWB-01
/// JSON</b> (<see cref="EWayBillJson.BuildEwb01"/>), and records what the portal returned — the <b>EWB number +
/// validity</b> (<see cref="EWayBillService.RecordPortalResponse"/>), a <b>cancellation</b>, an <b>extension</b>
/// (<see cref="EWayBillService.Extend"/>) or a <b>closure</b> (<see cref="EWayBillService.Close"/>).
///
/// <para><b>Offline-JSON mode is the default and the only mode</b> — no portal credentials; the EWB number and its
/// validity are always <b>copied</b> from the portal, never derived.</para>
///
/// <para><b>Engine guards surfaced, never crashed into:</b> the ₹50,000 threshold is <b>strict</b> (exactly ₹50,000
/// is NotRequired — only <i>more</i> triggers a bill), though an inter-state job-work / handicraft movement is
/// mandatory irrespective of value; <b>Part-B</b> is required unless the movement is intra-state and ≤ 50 km;
/// a bill can be <b>cancelled only within one day</b> of generation; and an <b>extension</b> is legal only inside the
/// ±8-hour window around its expiry, and never past the 360-day cap.</para>
///
/// <para><b>Opening this screen prepares nothing.</b> Gated: Regular GST company (ER-13).</para>
/// </summary>
public sealed partial class GenerateEWayBillViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly EWayBillService _service;
    private readonly Action<string, byte[]>? _writeBytes;
    private readonly DateOnly _today;
    private readonly Func<DateTimeOffset> _now;

    [ObservableProperty] private string _title = "Generate e-Way Bill";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private bool _hasCandidates;

    /// <summary>Where the offline EWB-01 JSON is written (defaults to Documents).</summary>
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private string? _lastJsonPath;

    // Part-B — the transport details.
    [ObservableProperty] private string _transporterId = string.Empty;
    [ObservableProperty] private EWayTransportMode _mode = EWayTransportMode.Road;
    [ObservableProperty] private string _vehicleNumber = string.Empty;
    [ObservableProperty] private string _distanceKmText = "0";
    [ObservableProperty] private string _transportDocNo = string.Empty;
    [ObservableProperty] private bool _isOverDimensionalCargo;

    /// <summary>The Ship-To GSTIN — mandatory on a bill for a voucher dated on/after 01-Aug-2026.</summary>
    [ObservableProperty] private string _shipToGstin = string.Empty;

    // The portal-response form — copied from the portal, never derived.
    [ObservableProperty] private string _ewbNumber = string.Empty;
    [ObservableProperty] private string _cancelReasonCode = "1";
    [ObservableProperty] private string _remainingDistanceKmText = "0";

    /// <summary>The transport modes a Part-B can declare.</summary>
    public ObservableCollection<EWayTransportMode> Modes { get; } =
        new((EWayTransportMode[])Enum.GetValues(typeof(EWayTransportMode)));

    /// <summary>The portal's fixed cancellation reason codes — <b>1</b> Duplicate · <b>2</b> Order cancelled ·
    /// <b>3</b> Data-entry mistake · <b>4</b> Other. The recorded statutory reason must be the one actually filed, so
    /// it is picked rather than assumed.</summary>
    public ObservableCollection<string> CancelReasonCodes { get; } = new(new[] { "1", "2", "3", "4" });

    /// <summary>The movement-bearing vouchers + their coverage / bill state.</summary>
    public ObservableCollection<EWayCandidateRowVm> Rows { get; } = new();

    /// <summary>Shell ctor.</summary>
    public GenerateEWayBillViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
        : this(company, storage, onChanged, DefaultFolder(),
               DateOnly.FromDateTime(DateTime.Today), () => DateTimeOffset.Now, writeBytes: null) { }

    /// <summary>Testable ctor: inject the folder, "today"/"now" and a write seam (deterministic + diskless).</summary>
    public GenerateEWayBillViewModel(
        Company company, CompanyStorage storage, Action? onChanged,
        string folder, DateOnly today, Func<DateTimeOffset>? now, Action<string, byte[]>? writeBytes)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _service = new EWayBillService(company);
        _writeBytes = writeBytes;
        _today = today;
        _now = now ?? (() => DateTimeOffset.Now);
        _folder = folder ?? string.Empty;
        Rebuild();
    }

    /// <summary>The highlighted voucher (what every action acts on).</summary>
    public EWayCandidateRowVm? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Rows.Count ? Rows[HighlightedIndex] : null;

    /// <summary>(Re)projects the movement vouchers + their coverage / bill state. Prepares nothing.</summary>
    public void Rebuild()
    {
        var keepIndex = HighlightedIndex;
        Rows.Clear();

        foreach (var v in _company.Vouchers.Where(v => !v.Cancelled).OrderBy(v => v.Date).ThenBy(v => v.Number))
        {
            var coverage = _service.CoverageOf(v);
            var record = _company.EWayBillRecords.FirstOrDefault(r => r.SourceVoucherId == v.Id);
            if (coverage == EWayCoverage.NotApplicable && record is null) continue;

            var required = coverage is EWayCoverage.Required or EWayCoverage.MandatoryIrrespectiveOfValue;
            Rows.Add(new EWayCandidateRowVm
            {
                VoucherId = v.Id,
                DocNo = EInvoiceService.DocumentNumberOf(v),
                Date = ApexDate.Format(v.Date),
                ConsignmentValue = IndianFormat.AmountAlways(_service.ConsignmentValue(v)),
                Coverage = CoverageLabel(coverage),
                Status = record?.Status.ToString() ?? "—",
                EwbNumber = string.IsNullOrWhiteSpace(record?.EwbNumber) ? "—" : record!.EwbNumber!,
                ValidUpto = record?.ValidUpto is { } vu
                    ? vu.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture)
                    : "—",
                IsRequired = required,
                HasRecord = record is not null,
                Note = NoteFor(coverage, record),
            });
        }

        HasCandidates = Rows.Count > 0;
        HighlightedIndex = Rows.Count == 0 ? -1 : Math.Clamp(keepIndex < 0 ? 0 : keepIndex, 0, Rows.Count - 1);
        OnHighlightedIndexChanged(HighlightedIndex);

        var generated = Rows.Count(r => r.Status == nameof(EWayStatus.Generated));
        Subtitle = $"{_company.Name}  —  offline EWB-01 mode (no portal credentials; the EWB number + validity are copied back)";
        StatusText = Rows.Count == 0
            ? "No movement requires an e-Way Bill yet (the threshold is strictly over ₹50,000)."
            : $"{Rows.Count(r => r.IsRequired)} requiring a bill  ·  {generated} generated  ·  " +
              $"{Rows.Count(r => !r.HasRecord && r.IsRequired)} awaiting an EWB-01.";
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
    [RelayCommand] private void SetPartBAction() => SetPartB();
    [RelayCommand] private void RecordPortalAction() => RecordPortalResponse();
    [RelayCommand] private void CancelAction() => Cancel();
    [RelayCommand] private void ExtendAction() => Extend();
    [RelayCommand] private void CloseAction() => Close();

    /// <summary>
    /// Prepares the highlighted voucher's e-Way Bill record, applies the typed Part-B, and writes the <b>offline
    /// EWB-01 JSON</b> for upload to the portal. The record starts <b>Pending</b>: no EWB number exists until the
    /// portal issues one and it is recorded back.
    /// </summary>
    public bool PrepareAndWriteJson()
    {
        Message = null;
        LastActionSucceeded = false;

        var row = HighlightedRow;
        if (row is null) return Fail("Highlight a movement voucher first.");
        var voucher = _company.Vouchers.FirstOrDefault(v => v.Id == row.VoucherId);
        if (voucher is null) return Fail("The voucher no longer exists.");
        if (!int.TryParse(DistanceKmText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var km))
            return Fail($"'{DistanceKmText}' is not a valid distance in km.");

        EWayBillRecord record;
        try
        {
            record = _service.PrepareRecord(voucher, _today,
                shipToGstin: string.IsNullOrWhiteSpace(ShipToGstin) ? null : ShipToGstin.Trim());
            // Part-B rides along with the preparation so the EWB-01 carries the transport details it needs.
            _service.SetPartB(record, string.IsNullOrWhiteSpace(TransporterId) ? null : TransporterId.Trim(),
                Mode, string.IsNullOrWhiteSpace(VehicleNumber) ? null : VehicleNumber.Trim(), km,
                string.IsNullOrWhiteSpace(TransportDocNo) ? null : TransportDocNo.Trim(), IsOverDimensionalCargo);
            _service.EnsureReadyToGenerate(record);
        }
        catch (InvalidOperationException ex)   // not required / already active / >180d / no Ship-To / Part-B missing
        {
            return Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        byte[] json;
        try
        {
            json = EWayBillJson.BuildEwb01(_company, voucher, record);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        var path = Path.Combine(Folder ?? string.Empty, $"EWB01_{Safe(record.DocumentNumberUpper)}.json");
        try
        {
            if (_writeBytes is not null) _writeBytes(path, json);
            else File.WriteAllBytes(path, json);
        }
        catch (Exception ex)
        {
            _storage.Save(_company);
            Rebuild();
            return Fail($"The e-Way Bill record was prepared, but the EWB-01 could not be written: {ex.Message}");
        }

        _storage.Save(_company);
        LastJsonPath = path;
        Rebuild();
        LastActionSucceeded = true;
        Message = $"EWB-01 written for {record.DocumentNumberUpper} ({json.Length:#,0} bytes) → {path}. " +
                  "Upload it to the portal, then record the EWB number it returns.";
        _onChanged();
        return true;
    }

    /// <summary>Updates Part-B on the highlighted voucher's existing record (e.g. the vehicle changed en route).</summary>
    public bool SetPartB()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (!int.TryParse(DistanceKmText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var km))
            return Fail($"'{DistanceKmText}' is not a valid distance in km.");

        try
        {
            _service.SetPartB(record!, string.IsNullOrWhiteSpace(TransporterId) ? null : TransporterId.Trim(),
                Mode, string.IsNullOrWhiteSpace(VehicleNumber) ? null : VehicleNumber.Trim(), km,
                string.IsNullOrWhiteSpace(TransportDocNo) ? null : TransportDocNo.Trim(), IsOverDimensionalCargo);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        return Succeed($"Part-B updated for {record!.DocumentNumberUpper} — " +
                       (EWayBillService.RequiresPartB(record) ? $"{Mode}, {km} km." : "intra-state ≤ 50 km (Part-B optional)."));
    }

    /// <summary>Records the portal's response — the EWB number + its validity, both copied from the portal.</summary>
    public bool RecordPortalResponse()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (string.IsNullOrWhiteSpace(EwbNumber)) return Fail("Enter the e-Way Bill number the portal returned.");

        var now = _now();
        // The portal's own validity is derived from the declared distance + ODC flag; the engine's EWayValidity
        // mirrors the statutory table, so the screen offers it as the default the taxpayer confirms.
        var validUpto = EWayValidity.ValidUpto(now, record!.DistanceKm, record.IsOverDimensionalCargo);

        try
        {
            _service.RecordPortalResponse(record, EwbNumber.Trim(), now, validUpto);
        }
        catch (InvalidOperationException ex)   // not Pending/Failed
        {
            return Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        return Succeed($"e-Way Bill {EwbNumber.Trim()} recorded for {record.DocumentNumberUpper} — " +
                       $"valid until {validUpto:dd-MMM-yyyy HH:mm}.");
    }

    /// <summary>Cancels the highlighted bill on the portal (legal only within one day of generation).</summary>
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
        catch (InvalidOperationException ex)   // not Generated / no GeneratedAt / past the +1 day window
        {
            return Fail(ex.Message);
        }

        return Succeed($"e-Way Bill cancelled for {record!.DocumentNumberUpper}.");
    }

    /// <summary>Extends the highlighted bill's validity (legal only inside the ±8h window around its expiry).</summary>
    public bool Extend()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        if (!int.TryParse(RemainingDistanceKmText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var km))
            return Fail($"'{RemainingDistanceKmText}' is not a valid remaining distance in km.");

        try
        {
            _service.Extend(record!, _now(), km);
        }
        catch (InvalidOperationException ex)   // not Generated / outside the ±8h window / past the 360-day cap
        {
            return Fail(ex.Message);
        }

        return Succeed($"Validity extended for {record!.DocumentNumberUpper} — " +
                       $"now valid until {record.ValidUpto:dd-MMM-yyyy HH:mm}.");
    }

    /// <summary>Requests closure of the highlighted bill (advisory; only for a voucher on/after 01-Aug-2026).</summary>
    public bool Close()
    {
        Message = null;
        LastActionSucceeded = false;

        if (!TryRecord(out var record, out var error)) return Fail(error!);
        var voucher = _company.Vouchers.FirstOrDefault(v => v.Id == HighlightedRow!.VoucherId);
        if (voucher is null) return Fail("The voucher no longer exists.");

        try
        {
            _service.Close(record!, voucher, _today);
        }
        catch (InvalidOperationException ex)   // before the 01-Aug-2026 mechanism / not Generated
        {
            return Fail(ex.Message);
        }

        return Succeed($"Closure requested for {record!.DocumentNumberUpper}.");
    }

    private bool TryRecord(out EWayBillRecord? record, out string? error)
    {
        record = null;
        error = null;
        var row = HighlightedRow;
        if (row is null) { error = "Highlight a movement voucher first."; return false; }
        record = _company.EWayBillRecords.FirstOrDefault(r => r.SourceVoucherId == row.VoucherId);
        if (record is null)
        {
            error = "No e-Way Bill has been prepared for this voucher yet — prepare the EWB-01 first.";
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

    private static string NoteFor(EWayCoverage coverage, EWayBillRecord? record)
    {
        if (record is { Status: EWayStatus.Cancelled })
            return $"Cancelled on {record.CancelledOn:dd-MMM-yyyy} (reason {record.CancelReasonCode}).";
        if (record is { Status: EWayStatus.Generated })
            return record.ClosureRequested
                ? $"Closure requested on {record.ClosedOn:dd-MMM-yyyy}."
                : $"In transit — {record.Mode}, {record.DistanceKm} km, vehicle {record.VehicleNumber ?? "—"}.";
        if (record is { Status: EWayStatus.Pending })
            return "EWB-01 prepared — upload it to the portal and record the number it returns.";
        return coverage switch
        {
            EWayCoverage.Required => "Consignment is over ₹50,000 — a bill is required before the movement starts.",
            EWayCoverage.MandatoryIrrespectiveOfValue =>
                "Inter-state job-work / handicraft movement — a bill is required irrespective of value.",
            EWayCoverage.NotRequired => "At or under the ₹50,000 threshold — no bill required (the threshold is strict).",
            _ => "Not applicable.",
        };
    }

    private static string CoverageLabel(EWayCoverage c) => c switch
    {
        EWayCoverage.Required => "Required",
        EWayCoverage.MandatoryIrrespectiveOfValue => "Required (any value)",
        EWayCoverage.NotRequired => "Not required",
        _ => "N/A",
    };

    private static string Safe(string name) =>
        string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string DefaultFolder()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { return string.Empty; }
    }
}
