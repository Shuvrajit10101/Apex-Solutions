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

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A Job Work order option in the Material In/Out "select Order No." picker (Phase 6 slice 8; RQ-48). The
/// <see cref="IsNone"/> sentinel (<see cref="Voucher"/> null) means "no linked order"; otherwise the chosen
/// order auto-fills the movement lines (item / quantity / rate) and records the fulfilment link.
/// </summary>
public sealed class JobWorkOrderOption
{
    public InventoryVoucher? Voucher { get; init; }
    public JobWorkOrder? Order { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Voucher is null;
}

/// <summary>
/// The <b>Material Out / Material In</b> movement voucher-entry screen (Phase 6 slice 8; RQ-46/RQ-48/RQ-49/RQ-50).
/// Both directions share this one view model — the engine posts exactly the source (outward) + destination
/// (inward) allocation lines the screen carries, so principal/worker symmetry falls out with no hard-coded branch
/// (RQ-50).
///
/// <para><b>Material Out</b> dispatches materials as a <b>balanced transfer</b> — the same item leaves the origin
/// godown and enters the destination (a "Our stock with third party" godown for a principal), so company net
/// on-hand is unchanged and the stock stays on our books (RQ-46). <b>Material In</b> receives finished goods; when
/// its type carries <b>Allow Consumption</b> (auto-set by the F11 feature) it is a <b>transform</b> — the
/// components held at the third-party consumption godown are consumed (source/outward) while the finished good is
/// produced (destination/inward), leaving no phantom raw material (RQ-49). The no-negative-stock guard blocks
/// consuming material the third-party godown does not actually hold.</para>
///
/// <para>Picking an <b>Order No.</b> auto-fills the movement lines from that order (RQ-48) and records the
/// fulfilment link so the Job Work registers can net pending quantities. The user may still edit every line.
/// On <see cref="Accept"/> the screen posts an <see cref="InventoryVoucher.MaterialMovement"/> through
/// <see cref="InventoryPostingService"/>. MVVM boundary: engine + persistence only, no Avalonia types.</para>
/// </summary>
public sealed partial class MaterialMovementEntryViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the movement date. Assigning routes
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

    /// <summary>The Material In/Out voucher type this screen posts through.</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header.</summary>
    public string TypeName => _type.Name;

    /// <summary>True for a Material In (receipt/consumption); false for a Material Out (dispatch/transfer).</summary>
    public bool IsMaterialIn => _type.BaseType == VoucherBaseType.MaterialIn;

    /// <summary>True when this Material In consumes components on receipt (Allow Consumption; RQ-49).</summary>
    public bool AllowConsumption => _type.IsConsumingMaterialIn;

    /// <summary>Human hint of the movement's meaning for the header band.</summary>
    public string DirectionHint => IsMaterialIn
        ? (AllowConsumption
            ? "Material In (Allow Consumption) — receive finished goods; consume components at the third-party godown."
            : "Material In — receive materials / finished goods.")
        : "Material Out — dispatch materials as a balanced transfer (stock stays on our books).";

    /// <summary>Caption for the header source-godown picker (per direction).</summary>
    public string SourceGodownLabel => IsMaterialIn ? "Consumption Godown (third-party)" : "Source Location";

    /// <summary>Caption for the header destination-godown picker (per direction).</summary>
    public string DestinationGodownLabel => IsMaterialIn ? "Receipt Location" : "Destination Location (third-party)";

    /// <summary>The stock items each line's picker chooses from.</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns each picker chooses from.</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The party (worker/principal) ledgers the movement may reference; empty first = "(none)".</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    /// <summary>The open Job Work orders the movement may fulfil (auto-fill); "(none)" first.</summary>
    public ObservableCollection<JobWorkOrderOption> Orders { get; } = new();

    /// <summary>The source (outward / consumption) allocation lines.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> SourceLines { get; } = new();

    /// <summary>The destination (inward / receipt) allocation lines.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> DestinationLines { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private PartyOption? _selectedParty;
    [ObservableProperty] private JobWorkOrderOption? _selectedOrder;
    [ObservableProperty] private Godown? _sourceGodown;
    [ObservableProperty] private Godown? _destinationGodown;

    /// <summary>Error/status surfaced under the grid (blank rows, imbalance, posting rejection).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    /// <summary>True while Accept is allowed (at least one complete line, no half-filled row).</summary>
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

    public MaterialMovementEntryViewModel(
        Company company,
        VoucherType type,
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

        _seeding = true;

        StockItems = company.StockItems;
        Godowns = company.Godowns;

        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (none)" });
        foreach (var l in company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = Parties.FirstOrDefault();

        // Open orders (any Job Work order voucher, not cancelled) — the fulfilment target.
        Orders.Add(new JobWorkOrderOption { Voucher = null, Display = "◦ (none)" });
        foreach (var v in company.InventoryVouchers)
            if (!v.Cancelled && v.JobWorkOrder is { } jwo)
            {
                var fg = company.FindStockItem(jwo.FinishedGoodStockItemId)?.Name ?? "(item)";
                Orders.Add(new JobWorkOrderOption
                {
                    Voucher = v,
                    Order = jwo,
                    Display = $"{jwo.OrderNo} — {fg} × {jwo.FinishedGoodQuantity.ToString("0.######", CultureInfo.InvariantCulture)}",
                });
            }
        SelectedOrder = Orders.FirstOrDefault();

        DateOnly? last = null;
        foreach (var v in company.InventoryVouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        if (last is null)
            foreach (var v in company.Vouchers)
                if (last is null || v.Date > last.Value) last = v.Date;
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);

        var main = Godowns.FirstOrDefault(g => g.IsMainLocation) ?? Godowns.FirstOrDefault();
        SourceGodown = main;
        DestinationGodown = main;

        AddSourceLine();
        AddDestinationLine();
        Title = $"{type.Name} Voucher";

        _seeding = false;
        Recalculate();
    }

    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(DateText));

    partial void OnSelectedOrderChanged(JobWorkOrderOption? value)
    {
        if (_seeding) return;
        AutoFillFromOrder();
        Recalculate();
    }

    partial void OnSourceGodownChanged(Godown? value) { if (!_seeding) AutoFillFromOrder(); }
    partial void OnDestinationGodownChanged(Godown? value) { if (!_seeding) AutoFillFromOrder(); }

    /// <summary>
    /// Auto-fills the source + destination grids from the selected order (RQ-48). For a <b>Material Out</b> the
    /// components become a balanced transfer (source at the source location, destination at the third-party
    /// destination location). For a <b>Material In</b> the finished good is produced (destination at the receipt
    /// location) and — when Allow Consumption is on — the components are consumed (source at the consumption
    /// godown). A "(none)" order clears the grids back to a single blank line each. The user may still edit any line.
    /// </summary>
    private void AutoFillFromOrder()
    {
        SourceLines.Clear();
        DestinationLines.Clear();

        if (SelectedOrder?.Order is not { } order)
        {
            AddSourceLine();
            AddDestinationLine();
            Recalculate();
            return;
        }

        if (IsMaterialIn)
        {
            // Destination: the finished good received at the receipt location.
            var fg = _company.FindStockItem(order.FinishedGoodStockItemId);
            var dRow = NewLine();
            dRow.SelectedItem = fg;
            dRow.SelectedGodown = DestinationGodown;
            dRow.QuantityText = order.FinishedGoodQuantity.ToString("0.######", CultureInfo.InvariantCulture);
            if (order.FinishedGoodRate is { } fr)
                dRow.RateText = fr.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            DestinationLines.Add(dRow);

            // Source: consume the components at the third-party consumption godown (only when Allow Consumption).
            if (AllowConsumption)
                foreach (var line in order.Lines)
                {
                    var sRow = NewLine();
                    sRow.SelectedItem = _company.FindStockItem(line.ComponentStockItemId);
                    sRow.SelectedGodown = SourceGodown;
                    sRow.QuantityText = line.Quantity.ToString("0.######", CultureInfo.InvariantCulture);
                    if (line.Rate is { } r) sRow.RateText = r.Amount.ToString("0.00", CultureInfo.InvariantCulture);
                    SourceLines.Add(sRow);
                }
        }
        else
        {
            // Material Out: a balanced transfer of each component from source → third-party destination.
            foreach (var line in order.Lines)
            {
                var item = _company.FindStockItem(line.ComponentStockItemId);
                var qty = line.Quantity.ToString("0.######", CultureInfo.InvariantCulture);
                var rate = line.Rate is { } r ? r.Amount.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;

                var sRow = NewLine();
                sRow.SelectedItem = item;
                sRow.SelectedGodown = SourceGodown;
                sRow.QuantityText = qty;
                sRow.RateText = rate;
                SourceLines.Add(sRow);

                var dRow = NewLine();
                dRow.SelectedItem = item;
                dRow.SelectedGodown = DestinationGodown;
                dRow.QuantityText = qty;
                dRow.RateText = rate;
                DestinationLines.Add(dRow);
            }
        }

        AddSourceLine();
        AddDestinationLine();
    }

    private InventoryVoucherLineViewModel NewLine()
        => new(InventoryLineKind.Movement, StockItems, Godowns, Recalculate);

    /// <summary>Adds a blank source (outward) line; recomputes.</summary>
    public InventoryVoucherLineViewModel AddSourceLine()
    {
        var row = NewLine();
        SourceLines.Add(row);
        Recalculate();
        return row;
    }

    /// <summary>Adds a blank destination (inward) line; recomputes.</summary>
    public InventoryVoucherLineViewModel AddDestinationLine()
    {
        var row = NewLine();
        DestinationLines.Add(row);
        Recalculate();
        return row;
    }

    /// <summary>Recomputes whether Accept is allowed: at least one complete line (either side) and no half-filled row.</summary>
    public void Recalculate()
    {
        var complete = SourceLines.Count(l => l.IsComplete) + DestinationLines.Count(l => l.IsComplete);
        var halfFilled = SourceLines.Any(l => !l.IsBlank && !l.IsComplete)
                         || DestinationLines.Any(l => !l.IsBlank && !l.IsComplete);
        CanAccept = complete >= 1 && !halfFilled;
    }

    /// <summary>
    /// Ctrl+A accept: builds the source (outward) + destination (inward) allocations and posts an
    /// <see cref="InventoryVoucher.MaterialMovement"/> through the engine (which enforces the balance for a plain
    /// transfer, exempts a consuming Material In, and hard-blocks any move that drives on-hand negative — RQ-49),
    /// linking the selected order, then saves the company. Any domain error is surfaced to <see cref="Message"/>.
    /// </summary>
    public bool Accept()
    {
        Message = null;
        Recalculate();

        if (SourceLines.Any(l => !l.IsBlank && !l.IsComplete)
            || DestinationLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every entered line needs a stock item, a godown and a positive quantity (rate, if entered, to the paisa).";
            return false;
        }

        var source = SourceLines.Where(l => l.IsComplete)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Outward, RateOf(l), l.Batch))
            .ToList();
        var dest = DestinationLines.Where(l => l.IsComplete)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Inward, RateOf(l), l.Batch))
            .ToList();

        if (source.Count == 0 && dest.Count == 0)
        {
            Message = "Enter at least one complete line before accepting.";
            return false;
        }

        var links = SelectedOrder?.Voucher is { } ov ? new[] { ov.Id } : null;
        var narration = string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim();
        var partyId = SelectedParty?.Ledger?.Id;

        InventoryVoucher voucher;
        try
        {
            // A two-sided TRANSFORM/TRANSFER derives its destination stock value from the LIVE cost of the stock it
            // consumes/moves — never from the order's planned rate — so no phantom stock value is created (RQ-46/
            // RQ-49, ER-4). A consuming Material In produces the finished good valued = Σ live consumed cost; a
            // Material Out transfer re-adds exactly what it removes (value-neutral). A one-sided movement (a worker's
            // pure receipt or FG dispatch, RQ-50) has nothing to re-value against, so it posts its lines as entered.
            var jobWork = new JobWorkService(_company);
            if (AllowConsumption && source.Count > 0 && dest.Count > 0)
                voucher = jobWork.BuildConsumingMaterialIn(_type.Id, Date, source, dest, links, narration, partyId);
            else if (!IsMaterialIn && source.Count > 0 && dest.Count > 0)
                voucher = jobWork.BuildMaterialOutTransfer(_type.Id, Date, source, dest, links, narration, partyId);
            else
                voucher = InventoryVoucher.MaterialMovement(
                    Guid.NewGuid(), _type.Id, Date, source, dest, orderLinks: links,
                    number: 0, narration: narration, partyId: partyId);
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
            Message = $"{_type.Name} No. {posted.Number} accepted.";
            _onSaved();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    private static Money? RateOf(InventoryVoucherLineViewModel line)
        => line.HasRate && line.ParsedRate is { } r ? new Money(r) : null;

    /// <summary>Esc / Alt+X cancel: discards the in-progress movement and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();
}
