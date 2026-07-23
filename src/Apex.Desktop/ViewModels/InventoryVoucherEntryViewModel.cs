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
public sealed partial class InventoryVoucherEntryViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the voucher date. Assigning routes
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

    /// <summary>
    /// Every unit in the company. Each line filters these down to the ones its picked item can legally be
    /// stated in (its base unit + the compound units reducing to it) — see
    /// <see cref="InventoryVoucherLineViewModel.UnitOptions"/> (WI-10 slice B).
    /// </summary>
    public IReadOnlyList<Unit> Units { get; }

    /// <summary>The party (supplier/customer) ledgers a PO/SO may optionally reference; empty first = "(none)".</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    /// <summary>The primary editable lines: order lines (PO/SO), stock-movement source lines, or counted lines.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> Lines { get; } = new();

    /// <summary>The Stock-Journal <b>destination</b> (production/inward) lines; empty for every other type.</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> DestinationLines { get; } = new();

    // ---- additional cost of a Stock-Journal transfer (Book pp.133–141; catalog §11; Phase 6 slice 3 RQ-20) ----

    /// <summary>The additional-cost ledgers a transfer's rows choose from — ledgers whose
    /// <c>MethodOfAppropriation</c> is non-null (a plain Direct-Expenses ledger stays out, RQ-19).</summary>
    public IReadOnlyList<DomainLedger> AdditionalCostLedgers { get; }

    /// <summary>The repeatable additional-cost rows (ledger + amount) on a Stock-Journal transfer; one blank trailing row.</summary>
    public ObservableCollection<AdditionalCostRowViewModel> AdditionalCosts { get; } = new();

    /// <summary>True when the transfer additional-cost area is shown — only on a Stock Journal (RQ-20). Off for
    /// every other inventory voucher, so those screens are byte-unchanged (ER-13).</summary>
    public bool ShowAdditionalCosts => IsStockJournal;

    /// <summary>The running Σ of the complete additional-cost rows (paisa-exact display).</summary>
    [ObservableProperty] private string _additionalCostTotalText = "0.00";

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private PartyOption? _selectedParty;

    /// <summary>The <b>rendered</b> preview of the number Accept will post (numbering-design-v2 §4/§3, review r2-F5) —
    /// the affixed/padded "Voucher No." for the previewed <see cref="VoucherNumber"/> on the current <see cref="Date"/>,
    /// equal to what the inventory engine assigns and renders on Accept. Refreshes when the date crosses an affix-row
    /// boundary. Byte-identical to <see cref="VoucherNumber"/> with an empty numbering config.</summary>
    public string FormattedVoucherNumber =>
        Apex.Ledger.Services.VoucherNumberFormatter.Render(_type, VoucherNumber, Date);

    partial void OnVoucherNumberChanged(int value) => OnPropertyChanged(nameof(FormattedVoucherNumber));
    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(FormattedVoucherNumber));

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

    /// <summary>
    /// The voucher date as editable text, in the one canonical <see cref="ApexDate.Canonical"/> spelling (WI-5).
    /// Read by the shared DAY-FIRST parser ("03/04/2024" is 3-Apr, not the 4-Mar month-first misread).
    /// Unparseable input is rejected, never silently discarded: <see cref="Date"/> keeps its last valid value,
    /// <see cref="Message"/> names the problem, and the field snaps back to the canonical rendering.
    /// </summary>
    public string DateText
    {
        get => ApexDate.Format(Date);
        set
        {
            if (ApexDate.TryParse(value, Date, out var parsed))
                Date = parsed;
            else
                Message = ApexDate.ErrorFor(value);

            // Unconditional — this VM has no OnDateChanged at all, so without an explicit notify even a
            // SUCCESSFUL parse never echoed canonically, and a failed one left the bad text on screen.
            OnPropertyChanged(nameof(DateText));
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

    /// <summary>
    /// Raised when the batch-allocation sub-screen (Phase 6 Cluster 1; RQ-3) should open for a line whose item
    /// Maintains-in-Batches: carries the item, the godown, the line quantity, whether the movement is OUTWARD
    /// (so the sub-screen can default the FEFO/FIFO issue selection, DP-1), and a callback that writes the
    /// committed batch allocations back to the line. The shell (not this VM) owns opening the cascade column.
    /// </summary>
    public event Action<StockItem, Godown, decimal, bool,
        Action<System.Collections.Generic.IReadOnlyList<BatchAllocation>>>? BatchAllocationRequested;

    /// <summary>
    /// True for a line on which the batch-allocation sub-screen applies (RQ-3): the company maintains batch-wise
    /// details, the line's kind carries a batch (Movement / Counted), the item Maintains-in-Batches, and item +
    /// godown + a positive quantity are known. The view shows a "Batches (Alt+B)" affordance only then.
    /// </summary>
    public bool LineWantsBatchAllocation(InventoryVoucherLineViewModel line) =>
        _company.MaintainBatchwiseDetails
        && line is { ShowsBatch: true, SelectedItem: { MaintainInBatches: true }, SelectedGodown: not null }
        && line.ParsedQuantity > 0m;

    /// <summary>
    /// Whether the primary "Lines" grid of this voucher type carries OUTWARD movement (drives the FEFO/FIFO
    /// default seed in the batch sub-screen, DP-1). Outward = anything that decreases on-hand from the primary
    /// list: Delivery Note, Rejection Out, Material Out, and a <b>Stock Journal</b> (whose primary list is the
    /// source/consumption side — the destination/production list is a separate grid with no batch affordance).
    /// Mirrors the post-time direction rule (outward = not Receipt/Rejection-In inward). An INWARD line has no
    /// existing stock to draw from, so the sub-screen seeds a single blank line instead.
    /// </summary>
    private bool IsOutwardMovement =>
        _type.BaseType is VoucherBaseType.DeliveryNote or VoucherBaseType.RejectionOut
            or VoucherBaseType.MaterialOut or VoucherBaseType.StockJournal;

    /// <summary>
    /// Alt+B on a batch-tracked line — requests the batch-allocation sub-screen for it. Raises
    /// <see cref="BatchAllocationRequested"/> with the line's item/godown/qty/direction and a commit callback
    /// that writes the accepted allocations back to the line's <see cref="InventoryVoucherLineViewModel.BatchLabel"/>
    /// (a single batch keeps its number; several batches show a "Multi (N)" summary — the multi-batch expansion
    /// at post time is a later slice). A no-op unless <see cref="LineWantsBatchAllocation"/>.
    /// </summary>
    public void RequestBatchAllocation(InventoryVoucherLineViewModel line)
    {
        if (line is null || !LineWantsBatchAllocation(line)) return;
        var item = line.SelectedItem!;
        var godown = line.SelectedGodown!;
        var qty = line.ParsedQuantity;

        BatchAllocationRequested?.Invoke(item, godown, qty, IsOutwardMovement, allocations =>
        {
            if (allocations.Count == 1)
                line.BatchLabel = allocations[0].BatchNumber;
            else if (allocations.Count > 1)
                line.BatchLabel = $"Multi ({allocations.Count})";
        });
    }

    /// <summary>
    /// Alt+B keyboard entry point (NFR-2): opens the batch-allocation sub-screen for the first line on which it
    /// applies (<see cref="LineWantsBatchAllocation"/>). The view passes the focused line when a batch line has
    /// focus; this whole-screen fallback lets Alt+B work even when focus is elsewhere on the entry screen.
    /// Returns true when a sub-screen was requested, false when no line currently qualifies (a safe no-op).
    /// </summary>
    public bool RequestBatchAllocationForFirstEligibleLine()
    {
        var line = Lines.FirstOrDefault(LineWantsBatchAllocation);
        if (line is null) return false;
        RequestBatchAllocation(line);
        return true;
    }

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
        Units = company.Units;

        // Additional-cost ledgers (Book pp.133–141): the Direct-Expenses ledgers marked as additional-cost
        // ledgers (a non-null Method of Appropriation). A plain Direct-Expenses ledger stays out (RQ-19).
        AdditionalCostLedgers = company.Ledgers
            .Where(l => l.IsAdditionalCostLedger)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        if (IsStockJournal) { AddDestinationLine(); AddAdditionalCostRow(); }
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
        var line = new InventoryVoucherLineViewModel(PrimaryLineKind, StockItems, Godowns, Recalculate, Units);
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
        var line = new InventoryVoucherLineViewModel(InventoryLineKind.Movement, StockItems, Godowns, Recalculate, Units);
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

    /// <summary>Adds a blank additional-cost row (ledger + amount) on a Stock-Journal transfer; keeps one trailing blank row.</summary>
    public AdditionalCostRowViewModel AddAdditionalCostRow()
    {
        var row = new AdditionalCostRowViewModel(AdditionalCostLedgers, OnAdditionalCostChanged);
        AdditionalCosts.Add(row);
        return row;
    }

    private void OnAdditionalCostChanged()
    {
        if (AdditionalCosts.Count == 0 || !AdditionalCosts[^1].IsBlank)
            AddAdditionalCostRow();
        Recalculate();
    }

    /// <summary>The Σ of the complete additional-cost rows (paisa-exact); 0 when the area is off/untracked.</summary>
    private decimal AdditionalCostsTotal()
    {
        if (!ShowAdditionalCosts) return 0m;
        var sum = 0m;
        foreach (var r in AdditionalCosts)
            if (r.IsComplete && r.ParsedAmount is { } a) sum += a;
        return sum;
    }

    /// <summary>
    /// Stamps each complete destination line's read-only <b>landed</b> (effective) inward rate + value using the
    /// SAME engine the post/valuation uses (<see cref="AdditionalCostApportionment.ForTransfer"/>, ER-4 / RQ-20):
    /// builds a throwaway Stock-Journal carrying the destination allocations + the additional-cost lines and lets
    /// the engine apportion by each ledger's method. No-op (columns cleared) when off or no additional cost.
    /// </summary>
    private void RefreshTransferLanded()
    {
        foreach (var l in DestinationLines)
        {
            l.ShowLanded = false;
            l.LandedRateText = string.Empty;
            l.LandedValueText = string.Empty;
        }
        if (!ShowAdditionalCosts) return;

        var destComplete = DestinationLines.Where(l => l.IsComplete).ToList();
        if (destComplete.Count == 0) return;

        var costLines = new List<AdditionalCostLine>();
        foreach (var r in AdditionalCosts)
            if (r.IsComplete && r.SelectedLedger is { } led && r.ParsedAmount is { } amt)
                costLines.Add(new AdditionalCostLine(led.Id, new Money(amt)));
        if (costLines.Count == 0) return;

        var dest = destComplete
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Inward, RateOf(l), l.Batch, l.UnitId))
            .ToList();

        var temp = InventoryVoucher.StockJournal(
            Guid.NewGuid(), _type.Id, Date, Array.Empty<InventoryAllocation>(), dest,
            additionalCostLines: costLines);
        var landed = AdditionalCostApportionment.ForTransfer(_company, temp);

        for (var i = 0; i < destComplete.Count && i < landed.Count; i++)
        {
            var ll = landed[i];
            destComplete[i].ShowLanded = true;
            destComplete[i].LandedRateText = IndianFormat.AmountAlways(ll.LandedUnitRate);
            destComplete[i].LandedValueText = IndianFormat.AmountAlways(ll.LandedValue.Amount);
        }
    }

    /// <summary>
    /// Recomputes the Stock-Journal balance indicator (source base total vs destination base total) and
    /// whether Accept is allowed: at least one complete line, no half-filled (touched-but-incomplete) row,
    /// and — for a Stock Journal — the two sides balanced in the base unit.
    /// </summary>
    public void Recalculate()
    {
        // Keep each line's batch affordance in sync with the FULL gate (company flag + item + godown + qty), so
        // the "⧉" button only appears where the batch-allocation sub-screen actually applies (RQ-52 UI leak fix).
        foreach (var l in Lines)
            l.WantsBatchAllocation = LineWantsBatchAllocation(l);

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

            AdditionalCostTotalText = IndianFormat.AmountAlways(AdditionalCostsTotal());
            // Stamp the read-only destination landed rate via the SAME engine the post/valuation uses (ER-4/RQ-20).
            RefreshTransferLanded();

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
                // Normalise each line into the item's base unit before summing (WI-10 slice B), so a source
                // of "1 Doz-Nos" balances a destination of "12 Nos" — exactly what the engine accumulates.
                sum += l.ParsedQuantityInBaseUnit;
        return sum;
    }

    private static string Qty(decimal q) => q.ToString("#,##0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// A built allocation's quantity normalised into the stock item's BASE unit — the same conversion the
    /// engine applies before it accumulates on hand (WI-10 slice B). Used by the Accept-time Stock-Journal
    /// balance pre-check so it compares like with like.
    /// </summary>
    private decimal QuantityInBaseUnit(InventoryAllocation a)
    {
        if (a.UnitId is not { } unitId) return a.Quantity;
        var unit = _company.FindUnit(unitId);
        return unit is null ? a.Quantity : unit.QuantityInBaseMeasure(a.Quantity);
    }

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
            Message = $"{_type.Name} No. {_company.FormatVoucherNumber(posted)} accepted.";
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
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, direction, RateOf(l), l.Batch, l.UnitId))
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
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Outward, RateOf(l), l.Batch, l.UnitId))
            .ToList();
        var dest = CompleteLines(DestinationLines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Inward, RateOf(l), l.Batch, l.UnitId))
            .ToList();

        if (source.Count == 0 || dest.Count == 0)
            throw new InvalidValidationException(
                "A Stock Journal needs at least one source (consumed) line and one destination (produced) line.");

        // Pre-check the balance before the engine so the message is friendly (the engine also enforces it).
        // Both sides are normalised into the item's BASE unit first (WI-10 slice B) — a line may now be
        // stated in a compound unit, so summing the raw quantities would compare Dozens against Nos.
        var srcBase = source.Sum(QuantityInBaseUnit);
        var destBase = dest.Sum(QuantityInBaseUnit);
        if (srcBase != destBase)
            throw new InvalidValidationException(
                $"Stock Journal is out of balance — source {Qty(srcBase)} ≠ destination {Qty(destBase)} (base unit). Not saved.");

        // Additional cost of a transfer (RQ-20): each names an additional-cost ledger + amount to apportion onto
        // the destination landed rate by the ledger's method. Only complete rows are carried.
        var additionalCostLines = new List<AdditionalCostLine>();
        foreach (var r in AdditionalCosts.Where(r => !r.IsBlank))
        {
            if (!r.IsComplete || r.SelectedLedger is not { } led || r.ParsedAmount is not { } amt)
                throw new InvalidValidationException(
                    "Every additional-cost line needs a ledger and a paisa-exact amount greater than zero.");
            additionalCostLines.Add(new AdditionalCostLine(led.Id, new Money(amt)));
        }

        return InventoryVoucher.StockJournal(
            Guid.NewGuid(), _type.Id, Date, source, dest,
            number: 0, narration: narration, postDated: IsPostDated,
            additionalCostLines: additionalCostLines.Count > 0 ? additionalCostLines : null);
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
/// One option in the Sales item-invoice <b>Price Level</b> header picker (slice 5; RQ-30). The
/// <see cref="IsNotApplicable"/> sentinel (<see cref="Level"/> null) means "no auto-fill"; otherwise the chosen
/// <see cref="PriceLevel"/> drives the per-line Rate/Discount auto-fill via the resolver.
/// </summary>
public sealed class PriceLevelSelectorOption
{
    public Apex.Ledger.Domain.PriceLevel? Level { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNotApplicable => Level is null;
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
