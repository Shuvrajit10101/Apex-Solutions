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
/// The reusable stock/order voucher-entry screen — one view model for all eight inventory voucher kinds:
/// Purchase Order (Ctrl+F9), Sales Order (Ctrl+F8), Receipt Note/GRN (Alt+F9), Delivery Note (Alt+F8),
/// Rejection In (Ctrl+F6), Rejection Out (Ctrl+F5), Stock Journal (Alt+F7) and Physical Stock (F10 menu). It
/// mirrors <see cref="VoucherEntryViewModel"/> (the accounting Dr/Cr entry) but posts to the <b>separate</b>
/// <see cref="InventoryVoucher"/> aggregate through <see cref="InventoryPostingService"/> — there is NO Dr/Cr
/// balancing (a stock/order voucher posts no accounting entry, DP-5), and a stock movement's direction is
/// implied by the type, not chosen per line.
///
/// <para>Per-type line shape:
/// <list type="bullet">
///   <item><b>PO / SO</b> — order lines (Item, Godown, Qty, optional Rate) + an optional party ledger; no
///     stock/accounts effect.</item>
///   <item><b>GRN / Delivery / Rejection In / Rejection Out</b> — allocation lines (Item, Godown, Qty,
///     optional Rate, optional Batch); the inward/outward direction is fixed by the type.</item>
///   <item><b>Stock Journal</b> — a Source (consumption/outward) list + a Destination (production/inward)
///     list, both editable; Accept is blocked until they balance in the base unit.</item>
///   <item><b>Physical Stock</b> — counted-quantity lines (Item, Godown, Counted Qty ≥ 0, optional Batch).</item>
/// </list></para>
///
/// <para>MVVM boundary: references the engine + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. On <see cref="Accept"/> it builds the real <see cref="InventoryVoucher"/> and posts it
/// (the engine rejects a content/type mismatch, an unbalanced Stock Journal, or any movement that would drive
/// on-hand negative — nothing persists on failure), then saves the whole company aggregate.</para>
/// </summary>
public sealed partial class InventoryVoucherEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly InventoryPostingService _service;
    private readonly InventoryLedger _ledger;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;

    /// <summary>The voucher type this screen is entering (Purchase Order, Receipt Note, …).</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header.</summary>
    public string TypeName => _type.Name;

    /// <summary>The stock items each line's picker chooses from.</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns each line's picker chooses from.</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The party (supplier/customer) ledgers a PO/SO may optionally reference; empty first = "(none)".</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    /// <summary>The primary editable lines: order lines (PO/SO), stock-movement source lines, or counted lines.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> Lines { get; } = new();

    /// <summary>The Stock-Journal <b>destination</b> (production/inward) lines; empty for every other type.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> DestinationLines { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private PartyOption? _selectedParty;

    /// <summary>Ctrl+T — marks the voucher post-dated (excluded from on-hand until its date is reached).</summary>
    [ObservableProperty] private bool _isPostDated;

    /// <summary>Error/status line surfaced under the grid (rejected posting, blank rows, imbalance, …).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    /// <summary>Live text: whether the Stock-Journal source and destination balance in the base unit.</summary>
    [ObservableProperty] private string _balanceText = string.Empty;

    /// <summary>True when the Stock Journal's source total equals its destination total (base unit); else false.</summary>
    [ObservableProperty] private bool _isBalanced = true;

    /// <summary>True while Accept is allowed (at least one complete line, no half-filled row, SJ balanced).</summary>
    [ObservableProperty] private bool _canAccept;

    /// <summary>The date as editable text (dd-MMM-yyyy) for the header TextBox (parsed on Accept via <see cref="Date"/>).</summary>
    public string DateText
    {
        get => Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        set
        {
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                Date = parsed;
        }
    }

    // -------- type classification for the view (which panels to show) --------

    /// <summary>True for a Purchase-Order / Sales-Order (order lines + optional party; no stock effect).</summary>
    public bool IsOrder => VoucherEffects.IsOrderBaseType(_type.BaseType);

    /// <summary>True for a Stock Journal (two lists: source consumption + destination production).</summary>
    public bool IsStockJournal => _type.BaseType == VoucherBaseType.StockJournal;

    /// <summary>True for a Physical Stock voucher (counted-quantity lines; no rate/direction).</summary>
    public bool IsPhysicalStock => _type.BaseType == VoucherBaseType.PhysicalStock;

    /// <summary>True for a plain stock-movement note (GRN / Delivery / Rejection In / Rejection Out).</summary>
    public bool IsMovementNote =>
        _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.DeliveryNote
            or VoucherBaseType.RejectionIn or VoucherBaseType.RejectionOut;

    /// <summary>Whether the single-list "Lines" grid shows a Rate column (order / movement, not physical).</summary>
    public bool LinesShowRate => !IsPhysicalStock;

    /// <summary>Human hint of the implied direction for a movement note ("Inward"/"Outward"); blank otherwise.</summary>
    public string DirectionHint => _type.BaseType switch
    {
        VoucherBaseType.ReceiptNote or VoucherBaseType.RejectionIn => "Inward (increases on-hand)",
        VoucherBaseType.DeliveryNote or VoucherBaseType.RejectionOut => "Outward (decreases on-hand)",
        _ => string.Empty,
    };

    /// <summary>The column caption for the quantity field (Counted vs Quantity).</summary>
    public string QuantityHeader => IsPhysicalStock ? "Counted Qty" : "Quantity";

    public InventoryVoucherEntryViewModel(
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
        _ledger = new InventoryLedger(company);

        StockItems = company.StockItems;
        Godowns = company.Godowns;

        // Party pickers (PO/SO only): "(none)" plus every ledger — the supplier/customer is optional.
        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (none)" });
        foreach (var l in company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = Parties.FirstOrDefault();

        // Default date: last inventory-voucher date, else last accounting-voucher date, else books-begin.
        DateOnly? last = null;
        foreach (var v in company.InventoryVouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        if (last is null)
            foreach (var v in company.Vouchers)
                if (last is null || v.Date > last.Value) last = v.Date;
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);
        Title = $"{type.Name} Voucher";

        // Seed a first blank line (two for a Stock Journal — one on each side).
        AddLine();
        if (IsStockJournal) AddDestinationLine();
        Recalculate();
    }

    /// <summary>The kind of the primary "Lines" grid, from the voucher's base type.</summary>
    private InventoryLineKind PrimaryLineKind => _type.BaseType switch
    {
        VoucherBaseType.PurchaseOrder or VoucherBaseType.SalesOrder => InventoryLineKind.Order,
        VoucherBaseType.PhysicalStock => InventoryLineKind.Counted,
        _ => InventoryLineKind.Movement, // GRN/Delivery/Rejection/Stock-Journal source
    };

    /// <summary>Adds a blank primary line (order / source-movement / counted); recomputes Accept-enabled.</summary>
    public InventoryVoucherLineViewModel AddLine()
    {
        var line = new InventoryVoucherLineViewModel(PrimaryLineKind, StockItems, Godowns, Recalculate);
        Lines.Add(line);
        Recalculate();
        return line;
    }

    /// <summary>Removes a primary line (keeping at least one); recomputes.</summary>
    public void RemoveLine(InventoryVoucherLineViewModel line)
    {
        if (Lines.Count <= 1) return;
        Lines.Remove(line);
        Recalculate();
    }

    /// <summary>Adds a blank Stock-Journal destination (inward) line; recomputes. No-op off a Stock Journal.</summary>
    public InventoryVoucherLineViewModel? AddDestinationLine()
    {
        if (!IsStockJournal) return null;
        var line = new InventoryVoucherLineViewModel(InventoryLineKind.Movement, StockItems, Godowns, Recalculate);
        DestinationLines.Add(line);
        Recalculate();
        return line;
    }

    /// <summary>Removes a Stock-Journal destination line (keeping at least one); recomputes.</summary>
    public void RemoveDestinationLine(InventoryVoucherLineViewModel line)
    {
        if (DestinationLines.Count <= 1) return;
        DestinationLines.Remove(line);
        Recalculate();
    }

    /// <summary>
    /// Recomputes the Stock-Journal balance indicator (source base total vs destination base total) and
    /// whether Accept is allowed: at least one complete line, no half-filled (touched-but-incomplete) row,
    /// and — for a Stock Journal — the two sides balanced in the base unit.
    /// </summary>
    public void Recalculate()
    {
        var completeLines = Lines.Count(l => l.IsComplete);
        var halfFilled = Lines.Any(l => !l.IsBlank && !l.IsComplete);

        if (IsStockJournal)
        {
            var srcComplete = DestinationLines.Count(l => l.IsComplete);
            var destHalf = DestinationLines.Any(l => !l.IsBlank && !l.IsComplete);

            var source = SumBase(Lines);
            var dest = SumBase(DestinationLines);
            IsBalanced = source == dest;

            var diff = source - dest;
            if (IsBalanced && source > 0m)
                BalanceText = $"Balanced — source {Qty(source)} = destination {Qty(dest)} (base unit)";
            else if (source == 0m && dest == 0m)
                BalanceText = "Enter source (consumed) and destination (produced) lines that balance.";
            else if (diff > 0m)
                BalanceText = $"Source exceeds destination by {Qty(diff)} (base unit) — must balance.";
            else
                BalanceText = $"Destination exceeds source by {Qty(-diff)} (base unit) — must balance.";

            CanAccept = completeLines >= 1 && srcComplete >= 1 && !halfFilled && !destHalf
                        && IsBalanced && source > 0m;
            return;
        }

        // Single-list types (order / movement note / physical stock).
        BalanceText = string.Empty;
        IsBalanced = true;
        CanAccept = completeLines >= 1 && !halfFilled;
    }

    /// <summary>Σ of the complete lines' base-unit quantities (compound units normalised via the engine).</summary>
    private decimal SumBase(IEnumerable<InventoryVoucherLineViewModel> lines)
    {
        var sum = 0m;
        foreach (var l in lines)
            if (l.IsComplete && l.SelectedItem is not null && l.SelectedGodown is not null)
                sum += l.ParsedQuantity; // lines are entered in the item's base unit (no compound-unit UI yet)
        return sum;
    }

    private static string Qty(decimal q) => q.ToString("#,##0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ctrl+A accept: pre-validates (friendly message, before the engine), builds the real
    /// <see cref="InventoryVoucher"/> for the type, and posts it via <see cref="InventoryPostingService.Post"/>
    /// (which enforces content-matches-type, the Stock-Journal balance, and the no-negative-stock hard block —
    /// nothing persists on failure), then saves the company. Any domain error is surfaced to
    /// <see cref="Message"/> without crashing the UI. On success surfaces the assigned number and returns to
    /// the Gateway.
    /// </summary>
    public bool Accept()
    {
        Message = null;

        // Reject half-filled (touched-but-incomplete) rows up front with a clear message.
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete)
            || DestinationLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = IsPhysicalStock
                ? "Every entered line needs a stock item, a godown and a counted quantity ≥ 0."
                : "Every entered line needs a stock item, a godown and a positive quantity (rate, if entered, to the paisa).";
            return false;
        }

        var narration = string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim();
        InventoryVoucher voucher;

        try
        {
            voucher = _type.BaseType switch
            {
                VoucherBaseType.PurchaseOrder or VoucherBaseType.SalesOrder => BuildOrder(narration),
                VoucherBaseType.PhysicalStock => BuildPhysical(narration),
                VoucherBaseType.StockJournal => BuildStockJournal(narration),
                _ => BuildMovementNote(narration),
            };
        }
        catch (InvalidValidationException ex)
        {
            Message = ex.Message; // a friendly pre-validation failure (blank grid, imbalance, …)
            return false;
        }

        try
        {
            var posted = _service.Post(voucher); // throws on type/content/imbalance/negative — never persisted
            _storage.Save(_company);             // persist the whole aggregate to the .db
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

    // ---------------------------------------------------------------- builders

    private InventoryVoucher BuildOrder(string? narration)
    {
        var lines = CompleteLines(Lines)
            .Select(l => new OrderLine(l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, RateOf(l)))
            .ToList();
        if (lines.Count == 0) throw Blank();

        return InventoryVoucher.Order(
            Guid.NewGuid(), _type.Id, Date, lines,
            number: 0, narration: narration, partyId: SelectedParty?.Ledger?.Id, postDated: IsPostDated);
    }

    private InventoryVoucher BuildMovementNote(string? narration)
    {
        var direction = _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.RejectionIn
            ? StockDirection.Inward
            : StockDirection.Outward;

        var allocations = CompleteLines(Lines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, direction, RateOf(l), l.Batch))
            .ToList();
        if (allocations.Count == 0) throw Blank();

        return new InventoryVoucher(
            Guid.NewGuid(), _type.Id, Date, allocations,
            number: 0, narration: narration, postDated: IsPostDated);
    }

    private InventoryVoucher BuildStockJournal(string? narration)
    {
        var source = CompleteLines(Lines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Outward, RateOf(l), l.Batch))
            .ToList();
        var dest = CompleteLines(DestinationLines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Inward, RateOf(l), l.Batch))
            .ToList();

        if (source.Count == 0 || dest.Count == 0)
            throw new InvalidValidationException(
                "A Stock Journal needs at least one source (consumed) line and one destination (produced) line.");

        // Pre-check the balance before the engine so the message is friendly (the engine also enforces it).
        var srcBase = source.Sum(a => a.Quantity);
        var destBase = dest.Sum(a => a.Quantity);
        if (srcBase != destBase)
            throw new InvalidValidationException(
                $"Stock Journal is out of balance — source {Qty(srcBase)} ≠ destination {Qty(destBase)} (base unit). Not saved.");

        return InventoryVoucher.StockJournal(
            Guid.NewGuid(), _type.Id, Date, source, dest,
            number: 0, narration: narration, postDated: IsPostDated);
    }

    private InventoryVoucher BuildPhysical(string? narration)
    {
        var lines = CompleteLines(Lines)
            .Select(l => new PhysicalStockLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, l.Batch))
            .ToList();
        if (lines.Count == 0) throw Blank();

        return InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), _type.Id, Date, lines,
            number: 0, narration: narration, postDated: IsPostDated);
    }

    private static IEnumerable<InventoryVoucherLineViewModel> CompleteLines(
        IEnumerable<InventoryVoucherLineViewModel> lines) => lines.Where(l => l.IsComplete);

    private static Money? RateOf(InventoryVoucherLineViewModel line)
        => line.HasRate && line.ParsedRate is { } r ? new Money(r) : null;

    private static InvalidValidationException Blank()
        => new("Enter at least one complete line before accepting.");

    /// <summary>Ctrl+T — toggles the post-dated flag for this voucher.</summary>
    public void TogglePostDated() => IsPostDated = !IsPostDated;

    /// <summary>Esc / Alt+X cancel: discards the in-progress voucher and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();
}

/// <summary>One option in a PO/SO party (supplier/customer) picker; a blank first entry means "(none)".</summary>
public sealed class PartyOption
{
    public DomainLedger? Ledger { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Ledger is null;
}

/// <summary>
/// A friendly pre-validation failure raised inside the entry VM before the engine is touched (blank grid,
/// Stock-Journal imbalance). Its message is surfaced verbatim to <see cref="InventoryVoucherEntryViewModel.Message"/>.
/// Kept internal to the entry flow — it never escapes to the engine.
/// </summary>
internal sealed class InvalidValidationException : Exception
{
    public InvalidValidationException(string message) : base(message) { }
}
