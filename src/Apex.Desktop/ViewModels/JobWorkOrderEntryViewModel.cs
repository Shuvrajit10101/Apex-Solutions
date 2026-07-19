using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A "Fill Components using" picker option (Phase 6 slice 8; RQ-47): either <b>Not Applicable</b> (manual entry,
/// <see cref="Bom"/> null) or a named <b>Bill of Materials</b> of the finished good — choosing a BOM snapshots
/// its component lines into the order grid (the Slice-2 link) and records the BOM id for provenance.
/// </summary>
public sealed class JobWorkFillOption
{
    public BillOfMaterials? Bom { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNotApplicable => Bom is null;
}

/// <summary>
/// The <b>Job Work In / Out Order</b> voucher-entry screen (Phase 6 slice 8; RQ-47/RQ-50/RQ-53). One view model
/// serves both directions — its <see cref="Direction"/> (worker In / principal Out) only picks defaults and
/// labels; the actual pending-to-receive/issue behaviour is carried per component line by its
/// <see cref="JobWorkComponentTrack"/> (RQ-50), so the SAME screen and engine path serve both roles with no
/// hard-coded branch.
///
/// <para>The header captures the party, duration/nature of processing, order number and the finished good
/// (item + quantity + due + location + rate); the <b>Fill Components using</b> picker is either <i>Not
/// Applicable</i> (a manual component grid) or a <b>BOM name</b> whose lines are <b>snapshotted</b> into the
/// grid at fill time (RQ-47 design Open Question 3), keeping <c>FillComponentsBomId</c> only for provenance.
/// An order affects <b>neither accounts nor stock</b> (RQ-47) — on <see cref="Accept"/> it posts an
/// <see cref="InventoryVoucher.JobWork"/> voucher through <see cref="InventoryPostingService"/>, which classifies
/// it as an order (no on-hand, no ledger effect).</para>
///
/// <para>MVVM boundary: references the engine + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class JobWorkOrderEntryViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the order date. Assigning routes
    /// through the one shared day-first parser and echoes the canonical spelling.
    /// </summary>
    public string WorkingDateText
    {
        get => DateText;
        set => DateText = value;
    }

    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly InventoryPostingService _service;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;

    /// <summary>The Job Work voucher type this screen posts through.</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header.</summary>
    public string TypeName => _type.Name;

    /// <summary>Which side of the job-work relationship this order is (worker In / principal Out).</summary>
    public JobWorkDirection Direction { get; }

    /// <summary>Human label of the direction ("In Order — we are the job worker" / "Out Order — we are the principal").</summary>
    public string DirectionHint => Direction == JobWorkDirection.In
        ? "Job Work In Order — a principal sends us the order + materials (we are the worker)."
        : "Job Work Out Order — we delegate manufacture to a worker (we are the principal).";

    /// <summary>The stock items each item picker chooses from.</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns each godown picker chooses from.</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The party (customer/supplier) ledgers the order may reference; empty first = "(none)".</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    /// <summary>The two track options shared by every component line (Pending to Receive / Issue).</summary>
    public ObservableCollection<JobWorkTrackOption> TrackOptions { get; } = new();

    /// <summary>The "Fill Components using" options: Not Applicable, then each BOM of the finished good.</summary>
    public ObservableCollection<JobWorkFillOption> FillOptions { get; } = new();

    /// <summary>The tracked component lines (always one blank trailing row while tracking components).</summary>
    public ObservableCollection<JobWorkComponentLineViewModel> Lines { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _orderNo = string.Empty;
    [ObservableProperty] private string _durationOfProcess = string.Empty;
    [ObservableProperty] private string _natureOfProcessing = string.Empty;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private PartyOption? _selectedParty;

    [ObservableProperty] private StockItem? _finishedGood;
    [ObservableProperty] private string _finishedGoodQtyText = "1";
    [ObservableProperty] private string _finishedGoodRateText = string.Empty;
    [ObservableProperty] private string _finishedGoodDueText = string.Empty;
    [ObservableProperty] private Godown? _finishedGoodGodown;

    /// <summary>"Tracking Components = Yes" (RQ-47). Defaults on; when off the order carries no component grid.</summary>
    [ObservableProperty] private bool _trackingComponents = true;

    /// <summary>The chosen "Fill Components using" option (Not Applicable or a BOM); driving the snapshot fill.</summary>
    [ObservableProperty] private JobWorkFillOption? _selectedFill;

    /// <summary>Error/status surfaced under the form (blank grid, posting rejection).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    /// <summary>True while Accept is allowed (order no. + finished good + at least one complete component line).</summary>
    [ObservableProperty] private bool _canAccept;

    private bool _seeding;

    /// <summary>The date as editable text (dd-MMM-yyyy) for the header TextBox.</summary>
    public string DateText
    {
        get => ApexDate.Format(Date);
        set
        {
            // WI-5: shared DAY-FIRST parse; reject-and-keep rather than silently discard.
            if (ApexDate.TryParse(value, Date, out var parsed))
                Date = parsed;
            else
                Message = ApexDate.ErrorFor(value);

            OnPropertyChanged(nameof(DateText));
        }
    }

    public JobWorkOrderEntryViewModel(
        Company company,
        VoucherType type,
        JobWorkDirection direction,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled,
        DateOnly? date = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _type = type ?? throw new ArgumentNullException(nameof(type));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));
        _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));
        _service = new InventoryPostingService(company);
        Direction = direction;

        _seeding = true;

        StockItems = company.StockItems;
        Godowns = company.Godowns;

        TrackOptions.Add(new JobWorkTrackOption { Track = JobWorkComponentTrack.PendingToReceive, Display = "Pending to Receive" });
        TrackOptions.Add(new JobWorkTrackOption { Track = JobWorkComponentTrack.PendingToIssue, Display = "Pending to Issue" });

        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (none)" });
        foreach (var l in company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = Parties.FirstOrDefault();

        // Default date: last inventory-voucher date, else last accounting date, else books-begin.
        DateOnly? last = null;
        foreach (var v in company.InventoryVouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        if (last is null)
            foreach (var v in company.Vouchers)
                if (last is null || v.Date > last.Value) last = v.Date;
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);

        var main = Godowns.FirstOrDefault(g => g.IsMainLocation) ?? Godowns.FirstOrDefault();
        FinishedGoodGodown = main;
        FinishedGood = StockItems.FirstOrDefault();
        RebuildFillOptions();

        AddBlankLine();
        Title = $"{type.Name} Voucher";

        _seeding = false;
        Recalculate();
    }

    /// <summary>The default component track for this direction (RQ-47): In ⇒ Pending to Receive, Out ⇒ Pending to Issue.</summary>
    private JobWorkComponentTrack DefaultTrack =>
        Direction == JobWorkDirection.In ? JobWorkComponentTrack.PendingToReceive : JobWorkComponentTrack.PendingToIssue;

    partial void OnFinishedGoodChanged(StockItem? value)
    {
        if (_seeding) return;
        RebuildFillOptions();
        Recalculate();
    }

    partial void OnFinishedGoodQtyTextChanged(string value) { if (!_seeding) Recalculate(); }
    partial void OnOrderNoChanged(string value) { if (!_seeding) Recalculate(); }
    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(DateText));
    partial void OnTrackingComponentsChanged(bool value) { if (!_seeding) Recalculate(); }

    partial void OnSelectedFillChanged(JobWorkFillOption? value)
    {
        if (_seeding) return;
        if (value is { Bom: { } bom }) FillFromBom(bom);
        Recalculate();
    }

    /// <summary>Rebuilds the "Fill Components using" options for the selected finished good (Not Applicable + its BOMs).</summary>
    private void RebuildFillOptions()
    {
        FillOptions.Clear();
        FillOptions.Add(new JobWorkFillOption { Bom = null, Display = "Not Applicable (enter manually)" });
        if (FinishedGood is { } fg)
            foreach (var bom in _company.BomsFor(fg.Id).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                FillOptions.Add(new JobWorkFillOption { Bom = bom, Display = bom.Name });
        SelectedFill = FillOptions.FirstOrDefault();
    }

    /// <summary>
    /// Snapshots the chosen BOM's component lines into the grid (RQ-47 design Open Question 3): each BOM
    /// component becomes a tracked line scaled to the finished-good quantity, defaulted to this direction's
    /// track. The lines are copied, so a later BOM edit never rewrites the posted order; the BOM id is kept
    /// only for provenance (recorded on Accept).
    /// </summary>
    private void FillFromBom(BillOfMaterials bom)
    {
        Lines.Clear();
        var fgQty = ParsedFinishedGoodQty is { } q and > 0m ? q : 1m;
        var block = bom.UnitOfManufacture <= 0m ? 1m : bom.UnitOfManufacture;
        foreach (var line in bom.ComponentLines)
        {
            var row = NewLine();
            row.SelectedItem = _company.FindStockItem(line.ComponentStockItemId);
            var scaled = line.QuantityPerBlock / block * fgQty;
            if (Quantities.IsWithinPrecision(scaled))
                row.QuantityText = scaled.ToString("0.######", CultureInfo.InvariantCulture);
            Lines.Add(row);
        }
        AddBlankLine();
    }

    /// <summary>The parsed finished-good quantity, or null when blank/unparsable.</summary>
    private decimal? ParsedFinishedGoodQty =>
        decimal.TryParse((FinishedGoodQtyText ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var q) ? q : (decimal?)null;

    private JobWorkComponentLineViewModel NewLine()
        => new(StockItems, Godowns, TrackOptions, DefaultTrack, OnLineChanged);

    /// <summary>Adds a blank component line; keeps one trailing blank row.</summary>
    public JobWorkComponentLineViewModel AddBlankLine()
    {
        var row = NewLine();
        Lines.Add(row);
        return row;
    }

    private void OnLineChanged()
    {
        if (Lines.Count == 0 || !Lines[^1].IsBlank) AddBlankLine();
        if (!_seeding) Recalculate();
    }

    /// <summary>Recomputes whether Accept is allowed: an order number, a finished good with a valid quantity,
    /// and — when tracking components — at least one complete component line and no half-filled row.</summary>
    public void Recalculate()
    {
        var fgQty = ParsedFinishedGoodQty;
        var fgOk = FinishedGood is not null && fgQty is { } q && q > 0m && Quantities.IsWithinPrecision(q);
        var orderOk = !string.IsNullOrWhiteSpace(OrderNo);

        if (!TrackingComponents)
        {
            CanAccept = fgOk && orderOk;
            return;
        }

        var complete = Lines.Count(l => l.IsComplete);
        var halfFilled = Lines.Any(l => !l.IsBlank && !l.IsComplete);
        CanAccept = fgOk && orderOk && complete >= 1 && !halfFilled;
    }

    /// <summary>
    /// Ctrl+A accept: builds the real <see cref="JobWorkOrder"/> payload + posts an
    /// <see cref="InventoryVoucher.JobWork"/> voucher through the engine (which classifies it as an order —
    /// no stock, no accounts, RQ-47), then saves the company. Any domain error is surfaced to
    /// <see cref="Message"/> without crashing. On success surfaces the assigned number and returns to the Gateway.
    /// </summary>
    public bool Accept()
    {
        Message = null;
        Recalculate();

        if (FinishedGood is null)
        {
            Message = "Pick the finished good for this order.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OrderNo))
        {
            Message = "Enter an order number.";
            return false;
        }
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every entered component line needs an item, a godown, a track and a positive quantity.";
            return false;
        }

        // WI-5: an unreadable typed due date is refused, not silently dropped to null.
        if (Lines.FirstOrDefault(l => l.HasUnreadableDue) is { } badDue)
        {
            Message = ApexDate.ErrorFor(badDue.DueText);
            return false;
        }
        if (!string.IsNullOrWhiteSpace(FinishedGoodDueText)
            && !ApexDate.TryParse(FinishedGoodDueText, Date, out _))
        {
            Message = ApexDate.ErrorFor(FinishedGoodDueText);
            return false;
        }

        JobWorkOrder order;
        InventoryVoucher voucher;
        try
        {
            var fgQty = ParsedFinishedGoodQty ?? 0m;
            Money? fgRate = ParseMoney(FinishedGoodRateText);
            DateOnly? fgDue = ApexDate.TryParse(FinishedGoodDueText, Date, out var d) ? d : (DateOnly?)null;

            var componentLines = new List<JobWorkOrderLine>();
            if (TrackingComponents)
                foreach (var l in Lines.Where(l => l.IsComplete))
                    componentLines.Add(new JobWorkOrderLine(
                        l.SelectedItem!.Id, l.SelectedTrack!.Track, l.ParsedQuantity,
                        godownId: l.SelectedGodown!.Id, dueDate: l.ParsedDue,
                        rate: l.ParsedRate is { } r ? new Money(r) : null));

            order = new JobWorkOrder(
                Direction, OrderNo.Trim(), FinishedGood.Id, fgQty, componentLines,
                finishedGoodRate: fgRate,
                finishedGoodDueDate: fgDue,
                finishedGoodGodownId: FinishedGoodGodown?.Id,
                trackingComponents: TrackingComponents,
                fillComponentsBomId: SelectedFill?.Bom?.Id,
                durationOfProcess: string.IsNullOrWhiteSpace(DurationOfProcess) ? null : DurationOfProcess.Trim(),
                natureOfProcessing: string.IsNullOrWhiteSpace(NatureOfProcessing) ? null : NatureOfProcessing.Trim());

            voucher = InventoryVoucher.JobWork(
                Guid.NewGuid(), _type.Id, Date, order,
                number: 0, narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
                partyId: SelectedParty?.Ledger?.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        try
        {
            var posted = _service.Post(voucher);
            _storage.Save(_company);
            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {posted.Number} accepted — order {order.OrderNo}.";
            _onSaved();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    private static Money? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var v) ? new Money(v) : null;
    }

    /// <summary>Esc / Alt+X cancel: discards the in-progress order and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();
}
