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
/// One entry in the Stock Journal's transfer-class picker (census 9.9): "◦ No class" (<see cref="Class"/> null,
/// key both arms by hand) or one of the type's inter-godown transfer classes.
/// </summary>
public sealed class TransferClassOption
{
    public VoucherClass? Class { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Class is null;
}

/// <summary>
/// The reusable stock/order voucher-entry screen — one view model for all eight inventory voucher kinds:
/// Purchase Order (Ctrl+F9), Sales Order (Ctrl+F8), Receipt Note/GRN (Alt+F9), Delivery Note (Alt+F8),
/// Rejection In (Ctrl+F6), Rejection Out (Ctrl+F5), Stock Journal (Alt+F7) and Physical Stock (Ctrl+F7). It
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
///     optional Rate, optional Batch) + an optional party ledger; the inward/outward direction is fixed by
///     the type.</item>
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

    /// <summary>The party (supplier/customer) ledgers an order <b>or a movement note</b> may optionally
    /// reference; empty first = "(none)". See <see cref="ShowsParty"/> for which types show the picker.</summary>
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
    /// <summary>
    /// <b>census 5.10 — may the operator TYPE the Voucher No. on this screen?</b> True under
    /// <see cref="NumberingMethod.Manual"/> (they must — the engine never numbers a Manual voucher) and under
    /// <see cref="NumberingMethod.AutomaticManualOverride"/> (they may, over the suggested number). False under
    /// Automatic, Multi-user Auto and None, where the field stays the read-only preview it has always been.
    ///
    /// <para>Before the Voucher Type master shipped, the Voucher No. was a <c>&lt;Run&gt;</c> inside a
    /// <c>TextBlock</c> on all four entry screens and there was no way to choose Manual anyway. Now that the
    /// method is selectable, a Manual type WITHOUT this would post every voucher unnumbered — the picker would be
    /// a label over a broken book.</para>
    /// </summary>
    public bool IsVoucherNumberEditable => _type.AllowsManualNumberEntry;

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

    // ------------------------------------------- W-K1 · census 9.9 — the Stock Journal TRANSFER CLASS

    /// <summary>
    /// The inter-godown transfer classes defined on this voucher type (census 9.9), preceded by a "◦ No class"
    /// option so an operator can always get back to keying both arms by hand. Empty of real classes until one is
    /// created on the Voucher Type master.
    /// </summary>
    public ObservableCollection<TransferClassOption> TransferClassOptions { get; } = new();

    /// <summary>The class the operator picked, or the "◦ No class" option.</summary>
    [ObservableProperty] private TransferClassOption? _selectedTransferClass;

    /// <summary>
    /// The single <b>destination godown</b> the class transfers to — the vendor's whole promise for this feature:
    /// <i>"all you have to do is just enter the destination godown … and enter the item details."</i>
    /// </summary>
    [ObservableProperty] private Godown? _transferDestinationGodown;

    /// <summary>True when the class picker is shown: a Stock Journal type that actually has a transfer class.
    /// A type with none shows nothing, so a company that never defines one is byte-identical (ER-13).</summary>
    public bool ShowTransferClass => IsStockJournal && _type.InterGodownTransferClasses.Any();

    /// <summary>
    /// True when a transfer class is ACTIVE — the operator has picked one. While it is,
    /// <see cref="BuildStockJournal"/> derives the destination arm by mirroring the source, so the destination
    /// grid is hidden and the destination godown picker takes its place.
    /// </summary>
    public bool IsTransferClassActive => SelectedTransferClass?.Class is not null;

    partial void OnSelectedTransferClassChanged(TransferClassOption? value)
    {
        // 🔴 Both notifications are required. IsTransferClassActive drives the destination godown picker AND the
        // visibility of the hand-keyed destination grid; ShowsDestinationLines is its inverse. Raising only one
        // leaves the operator with both the class and the manual grid on screen, keying an arm that
        // BuildStockJournal is about to overwrite.
        OnPropertyChanged(nameof(IsTransferClassActive));
        OnPropertyChanged(nameof(ShowsDestinationLines));
        Recalculate();
    }

    partial void OnTransferDestinationGodownChanged(Godown? value) => Recalculate();

    /// <summary>True when the hand-keyed destination (produced/inward) grid is shown — a Stock Journal with NO
    /// transfer class active. Under a class the destination is derived, so showing the grid would invite the
    /// operator to key an arm that is then discarded.</summary>
    public bool ShowsDestinationLines => IsStockJournal && !IsTransferClassActive;

    private void RefreshTransferClassOptions()
    {
        TransferClassOptions.Clear();
        if (!IsStockJournal) return;
        TransferClassOptions.Add(new TransferClassOption { Class = null, Display = "◦ No class" });
        // OrdinalIgnoreCase — the same ordering on every platform of the gate. See ReadVoucherTypeClasses.
        foreach (var c in _type.InterGodownTransferClasses.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            TransferClassOptions.Add(new TransferClassOption { Class = c, Display = c.Name });
        SelectedTransferClass = TransferClassOptions.FirstOrDefault();
    }

    /// <summary>True for a Physical Stock voucher (counted-quantity lines; no rate/direction).</summary>
    public bool IsPhysicalStock => _type.BaseType == VoucherBaseType.PhysicalStock;

    /// <summary>True for a plain stock-movement note (GRN / Delivery / Rejection In / Rejection Out).</summary>
    public bool IsMovementNote =>
        _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.DeliveryNote
            or VoucherBaseType.RejectionIn or VoucherBaseType.RejectionOut;

    /// <summary>
    /// True when the party (supplier/customer) picker is shown — orders <b>and</b> movement notes, which is
    /// every voucher this screen enters that names a counterparty at all (Phase 10.10 / WF-8, R12 2026-08-07).
    /// <para><b>Why the four note types, one by one</b> — each is corpus-grounded, not inferred:
    /// <b>Receipt Note</b> takes "Party's A/c Name- Name of Party (Supplier)" [CORPUS-BOOK p.71];
    /// <b>Delivery Note</b> takes "Party's A/c Name- Name of Party (Customer)" [CORPUS-BOOK p.77];
    /// <b>Rejection In</b> takes "Ledger Account – Select customer who rejected item" [CORPUS-BOOK p.51]; and
    /// <b>Rejection Out</b> takes "Ledger Account – Select Supplier whom rejected item" [CORPUS-BOOK p.53].
    /// Naming the party does <b>not</b> make the note post an accounting entry — the corpus is explicit that a
    /// note "will not reflect in Ledger/party balance ... will only affect your stock" [CORPUS-BOOK pp.70, 76],
    /// which is exactly this product's DP-5 rule, so the field is a name and nothing more.</para>
    /// <para><b>Why NOT the other two.</b> A <b>Stock Journal</b> is an internal godown-to-godown transfer and
    /// its field list carries no party at all [CORPUS-BOOK pp.79-80]; a <b>Physical Stock</b> voucher records a
    /// counted quantity, with no counterparty in the transaction. Showing a party there would invent a field.</para>
    /// </summary>
    public bool ShowsParty => IsOrder || IsMovementNote;

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

        // Party picker: "(none)" plus every ledger — the supplier/customer is optional. Shown for orders AND
        // movement notes since Phase 10.10 / WF-8 (see ShowsParty); this list is built for every type either way,
        // because building it is cheap and gating the DATA as well as the visibility is a second place to drift.
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

        // W-K1 (census 9.9): the Stock Journal transfer classes defined on this type. Built BEFORE the first
        // Recalculate so the destination grid's visibility is right on the first render.
        RefreshTransferClassOptions();

        // Seed a first blank line (two for a Stock Journal — one on each side).
        AddLine();
        if (IsStockJournal) { AddDestinationLine(); AddAdditionalCostRow(); }
        Recalculate();
    }

    // ============================================== ALTERATION (census 4.9–4.16, 9.2) — Ctrl+Enter's door

    /// <summary>The posted voucher this screen is amending, or <c>null</c> when it is entering a new one.</summary>
    private Guid? _alteringVoucherId;

    /// <summary>
    /// True while this screen is amending a POSTED voucher rather than entering a new one. Drives the window's
    /// accept routing (Ctrl+A ⇒ <see cref="AcceptAlteration"/>) and the hard refusal in <see cref="Accept"/>.
    /// </summary>
    public bool IsAltering => _alteringVoucherId is not null;

    /// <summary>The posted voucher's id while <see cref="IsAltering"/>, else <see cref="Guid.Empty"/>.</summary>
    public Guid AlteringVoucherId => _alteringVoucherId ?? Guid.Empty;

    /// <summary>
    /// 🔴 <b>Opens this screen on a POSTED pure-stock voucher, pre-filled, or refuses BY NAME — the door census
    /// rows 4.9–4.16 were missing.</b>
    ///
    /// <para>Until this existed, <c>InventoryPostingService</c> had <c>Post</c>, <c>Cancel</c> and <c>Delete</c>
    /// but no <c>Replace</c>, so <c>VoucherEntryViewModel.ForAlter</c> refused every inventory-aggregate voucher
    /// by design and the Day Book's Ctrl+Enter could only name the limit. The engine verb and this door ship
    /// together, because either alone is useless: a service with no caller is not a feature, and a screen with no
    /// engine cannot save.</para>
    ///
    /// <para>The result is never a bare <c>null</c> and never a silent no-op — it holds either a rehydrated view
    /// model or a sentence naming why this voucher's posted shape cannot be rebuilt here. That two-sided shape is
    /// the same discipline <c>VoucherAlterationOpen</c> enforces on the accounting door, for the reason this
    /// project has filed three times: a dead key is worse than a refusal, because the operator believes the
    /// action happened.</para>
    /// </summary>
    public static InventoryVoucherAlterationOpen ForAlter(
        Company company,
        Guid voucherId,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (InventoryVoucherAlterationEligibility.RefusalFor(company, voucherId) is { } refusal)
            return InventoryVoucherAlterationOpen.Refused(refusal);

        // Both are non-null: RefusalFor returned null, which it only does after resolving each of them.
        var voucher = company.FindInventoryVoucher(voucherId)!;
        var type = company.FindVoucherType(voucher.TypeId)!;

        // 🔴 THE SHAPES THIS SCREEN DOES NOT SERVE ARE REFUSED BEFORE THE VIEW MODEL IS CONSTRUCTED, and that
        // ordering is load-bearing rather than tidy. The constructor runs the whole entry screen's set-up for
        // the voucher's TYPE — line grids, unit options, transfer classes, the first Recalculate — and a Job
        // Work order's type is not one this screen is built for. Constructing it and then refusing was measured
        // to HANG the headless window, so a Ctrl+Enter on a Job Work order in the Day Book would have hung the
        // real app. Refusing first means nothing is built for a type that has no screen here.
        if (ShapeThisScreenCannotServe(voucher) is { } shape)
            return InventoryVoucherAlterationOpen.Refused(shape);

        // The date is deliberately NOT passed to the constructor: RehydrateFrom sets it, so there is exactly one
        // writer and a test can falsify it (a voucher that is not the latest in the book would otherwise open on
        // the constructor's default and no assertion could tell).
        var entry = new InventoryVoucherEntryViewModel(company, type, storage, onSaved, onCancelled);
        return entry.RehydrateFrom(voucher) is { } shapeRefusal
            ? InventoryVoucherAlterationOpen.Refused(shapeRefusal)
            : InventoryVoucherAlterationOpen.Opened(entry);
    }

    /// <summary>
    /// 🔴 <b>The inverse of the four builders</b> — re-keys <paramref name="v"/> onto this screen, or returns a
    /// NAMED sentence saying why its posted shape cannot be rebuilt. Returning <c>null</c> is the assertion that
    /// pressing Ctrl+A immediately would rebuild a voucher identical to the one on the book.
    ///
    /// <para>🔴 <b>Every arm that refuses here refuses because re-saving would MOVE STOCK.</b> That is the whole
    /// bar for this family: the screen keys some fields and derives others, so a posted voucher carrying a field
    /// this screen does not key would have that field silently dropped by the writer — and a dropped allocation,
    /// batch, godown or order link is a corrupted closing stock, which moves the Balance Sheet. A round-trip
    /// backstop per line (see <c>InventoryVoucherLineViewModel.RehydrateFromAllocation</c>) plus the
    /// shape guards below is what makes "opened" mean "safely re-savable".</para>
    /// </summary>
    private string? RehydrateFrom(InventoryVoucher v)
    {
        ArgumentNullException.ThrowIfNull(v);

        // Re-asked here as well as in ForAlter: RehydrateFrom is the method that must be safe to call on any
        // posted voucher, and a future second caller must not be able to skip the pre-construction check.
        if (ShapeThisScreenCannotServe(v) is { } shape) return shape;

        if (!IsStockJournal && v.DestinationAllocations.Count > 0)
            return $"This {_type.Name} carries a destination (inward) side, which only a Stock Journal or a "
                 + "material movement keys, so this screen cannot rebuild it.";

        if (!IsStockJournal && v.AdditionalCostLines.Count > 0)
            return $"This {_type.Name} carries additional-cost lines, which only a Stock Journal keys on this "
                 + "screen, so re-saving it here would drop them and change the landed rate.";

        if (v.PartyId is { } partyId && Parties.FirstOrDefault(p => p.Ledger?.Id == partyId) is null)
            return "The party ledger on this voucher is no longer in this company, so the screen cannot show it.";

        // ---- the header. Date FIRST: FormattedVoucherNumber depends on it, and the number preview the
        //      constructor seeded from NextNumber must be overwritten by the voucher's OWN number.
        _alteringVoucherId = v.Id;
        Date = v.Date;
        VoucherNumber = v.Number;
        Narration = v.Narration ?? string.Empty;
        IsPostDated = v.PostDated;
        SelectedParty = v.PartyId is { } pid
            ? Parties.First(p => p.Ledger?.Id == pid)
            : Parties.First(p => p.IsNone);
        Title = $"{_type.Name} Alteration";

        // ---- the lines, by shape.
        if (IsOrder)
        {
            if (v.OrderLines.Count == 0)
                return $"This {_type.Name} has no order lines to re-key.";
            if (v.Allocations.Count > 0 || v.PhysicalLines.Count > 0)
                return $"This {_type.Name} carries stock movement lines as well as order lines, a shape this "
                     + "screen cannot rebuild.";

            Lines.Clear();
            foreach (var ol in v.OrderLines)
                if (AddLine().RehydrateFromOrderLine(ol) is { } r) return Prefix(r);
        }
        else if (IsPhysicalStock)
        {
            if (v.PhysicalLines.Count == 0)
                return $"This {_type.Name} has no counted lines to re-key.";
            if (v.Allocations.Count > 0 || v.OrderLines.Count > 0)
                return $"This {_type.Name} carries movement lines as well as counted lines, a shape this screen "
                     + "cannot rebuild.";

            Lines.Clear();
            foreach (var pl in v.PhysicalLines)
                if (AddLine().RehydrateFromPhysicalLine(pl) is { } r) return Prefix(r);
        }
        else if (IsStockJournal)
        {
            if (v.Allocations.Count == 0 || v.DestinationAllocations.Count == 0)
                return "This Stock Journal is missing one of its two sides, so the screen cannot rebuild it.";

            // 🔴 The transfer class is reset to "◦ No class" and BOTH arms are re-keyed by hand. Nothing on a
            // posted voucher records which class produced it — the class only ever mirrored the source at posting
            // time — so re-selecting one would be a guess, and BuildStockJournal would then DERIVE the
            // destination and discard whatever the book actually holds. Keying both arms reproduces the posted
            // allocations exactly, which is what the per-line backstop then proves.
            SelectedTransferClass = TransferClassOptions.FirstOrDefault(o => o.IsNone)
                                    ?? TransferClassOptions.FirstOrDefault();
            TransferDestinationGodown = null;

            Lines.Clear();
            foreach (var a in v.Allocations)
            {
                if (a.Direction != StockDirection.Outward)
                    return "This Stock Journal's source side carries an inward line, so the screen would re-key "
                         + "it in the wrong direction.";
                if (AddLine().RehydrateFromAllocation(a) is { } r) return Prefix(r);
            }

            DestinationLines.Clear();
            foreach (var a in v.DestinationAllocations)
            {
                if (a.Direction != StockDirection.Inward)
                    return "This Stock Journal's destination side carries an outward line, so the screen would "
                         + "re-key it in the wrong direction.";
                if (AddDestinationLine() is not { } row) return "The destination grid is not available on this "
                                                              + "screen, so the Stock Journal cannot be re-keyed.";
                if (row.RehydrateFromAllocation(a) is { } r) return Prefix(r);
            }

            AdditionalCosts.Clear();
            foreach (var ac in v.AdditionalCostLines)
            {
                var row = AddAdditionalCostRow();
                var led = AdditionalCostLedgers.FirstOrDefault(l => l.Id == ac.LedgerId);
                if (led is null)
                    return "One of its additional-cost ledgers is no longer marked as an additional-cost ledger "
                         + "in this company, so re-saving would drop the cost and change the landed rate.";
                row.SelectedLedger = led;
                row.AmountText = ac.Amount.Amount.ToString(CultureInfo.InvariantCulture);
                if (row.ParsedAmount != ac.Amount.Amount)
                    return $"the additional cost on '{led.Name}' cannot be re-keyed exactly "
                         + $"({ac.Amount.Amount} was posted, the screen rebuilds {row.ParsedAmount}).";
            }
            // A blank trailing row so the operator can add a cost, matching what the constructor seeds.
            AddAdditionalCostRow();
        }
        else
        {
            if (v.Allocations.Count == 0)
                return $"This {_type.Name} has no movement lines to re-key.";
            if (v.OrderLines.Count > 0 || v.PhysicalLines.Count > 0)
                return $"This {_type.Name} carries order or counted lines as well as movement lines, a shape "
                     + "this screen cannot rebuild.";

            // 🔴 The direction is DERIVED by BuildMovementNote from the base type, never keyed — so a posted line
            // whose direction disagrees with what the writer will stamp would be SILENTLY REVERSED on save,
            // turning a receipt into an issue. Asserted per line rather than assumed.
            var writerDirection = _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.RejectionIn
                ? StockDirection.Inward
                : StockDirection.Outward;

            Lines.Clear();
            foreach (var a in v.Allocations)
            {
                if (a.Direction != writerDirection)
                    return $"One of its lines was posted {a.Direction} and this screen re-keys a {_type.Name} as "
                         + $"{writerDirection}, so re-saving would reverse the stock movement.";
                if (AddLine().RehydrateFromAllocation(a) is { } r) return Prefix(r);
            }
        }

        Recalculate();
        return null;
    }

    /// <summary>
    /// 🔴 The posted shapes this screen has no grids for at all, named rather than silently mangled. Static and
    /// type-free on purpose: it is asked BEFORE a view model exists, so that a voucher whose TYPE this screen is
    /// not built for never reaches the constructor. (Census 9.2 — both of these are the Job Work family, whose
    /// two entry screens are <c>JobWorkOrderEntryViewModel</c> and <c>MaterialMovementEntryViewModel</c>.)
    /// </summary>
    private static string? ShapeThisScreenCannotServe(InventoryVoucher v)
    {
        if (v.JobWorkOrder is not null)
            return "This is a Job Work order — its finished good and component list are keyed on the Job Work "
                 + "order screen, not here, so this screen cannot re-open it. Cancel it with Alt+X or delete it "
                 + "with Alt+D and enter a corrected one.";

        if (v.OrderLinks.Count > 0)
            return "This movement is linked to a Job Work order, and this screen does not key those links — "
                 + "re-saving it here would detach the movement from the order and the order would report as "
                 + "outstanding again. Cancel it with Alt+X or delete it with Alt+D and enter a corrected one.";

        return null;
    }

    /// <summary>Wraps a line-level refusal in the one sentence every alteration refusal opens with, so the
    /// operator reads a whole statement rather than a fragment.</summary>
    private static string Prefix(string lineRefusal) =>
        "This voucher cannot be re-opened for alteration: " + lineRefusal;

    /// <summary>The kind of the primary "Lines" grid, from the voucher's base type.</summary>
    private InventoryLineKind PrimaryLineKind => _type.BaseType switch
    {
        VoucherBaseType.PurchaseOrder or VoucherBaseType.SalesOrder => InventoryLineKind.Order,
        VoucherBaseType.PhysicalStock => InventoryLineKind.Counted,
        _ => InventoryLineKind.Movement, // GRN/Delivery/Rejection/Stock-Journal source
    };

    /// <summary>
    /// True when the <b>Tracking No.</b> column is shown on this voucher's lines (census 9.8).
    /// <para>Gated on the F11 feature AND on the voucher being one the tracking number means something on: the
    /// vendor's mechanism links a <b>Receipt Note</b> to its Purchase and a <b>Delivery Note</b> to its Sales,
    /// so those two — and their Rejection counterparts, which reverse the same movement — are where it belongs.
    /// A Physical Stock count has no bill to reconcile against, and a Stock Journal moves stock between the
    /// company's own godowns with no bill at all, so offering the column there would invite a reference that no
    /// report could ever pair with anything.</para>
    /// </summary>
    public bool ShowTrackingNumber =>
        _company.UseTrackingNumbers
        && _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.DeliveryNote
            or VoucherBaseType.RejectionIn or VoucherBaseType.RejectionOut;

    /// <summary>
    /// True when the <b>Cost Tracking Number</b> column is shown (census 9.7). Unlike
    /// <see cref="ShowTrackingNumber"/> this is NOT restricted by base type: the vendor's cost tracking follows
    /// a lot across its <i>whole</i> lifecycle, and a Stock Journal that consumes a lot into a manufactured item
    /// is part of that lifecycle. Restricting it would silently break the chain exactly where goods are
    /// transformed.
    /// </summary>
    public bool ShowCostTrackingNumber => _company.EnableCostTracking;

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

        // W-K1 (census 9.8 / 9.7): keep both tracking columns in sync with their F11 gate on EVERY line, source
        // and destination alike.
        // 🔴 Doing it HERE rather than once at construction is what makes the columns appear the moment the
        // operator turns the feature on in F11 without re-entering the voucher — and, more importantly, makes
        // them DISAPPEAR (and stop posting, via the ShowTrackingNumber guard on the line's Tracking property)
        // the moment it is turned off. A construction-time-only assignment leaves a line posting a value from a
        // column that is no longer on screen.
        foreach (var l in Lines.Concat(DestinationLines))
        {
            l.ShowTrackingNumber = ShowTrackingNumber;
            l.ShowCostTrackingNumber = ShowCostTrackingNumber;
        }

        var completeLines = Lines.Count(l => l.IsComplete);
        var halfFilled = Lines.Any(l => !l.IsBlank && !l.IsComplete);

        if (IsStockJournal && IsTransferClassActive)
        {
            // W-K1 (census 9.9) — under a transfer class the destination arm is DERIVED, so there is nothing to
            // balance and no destination rows to police. It balances BY CONSTRUCTION: InterGodownTransfer.Mirror
            // reproduces each source line's quantity and unit exactly, so asserting the old source==destination
            // check here would test the mirroring against itself and could never fail. What CAN be wrong is the
            // one thing the operator supplies — the destination godown — so that is what is gated.
            var srcOnly = Lines.Count(l => l.IsComplete);
            var sourceBase = SumBase(Lines);
            var sameGodown = TransferDestinationGodown is { } d
                             && Lines.Any(l => l.IsComplete && l.SelectedGodown?.Id == d.Id);

            IsBalanced = true;
            BalanceText = TransferDestinationGodown is null
                ? "Pick the destination godown this transfer class moves the stock to."
                : sameGodown
                    ? $"A line already sits in {TransferDestinationGodown.Name} — a transfer cannot move stock "
                      + "from a godown to itself."
                    : srcOnly >= 1
                        ? $"Transfer — {Qty(sourceBase)} (base unit) to {TransferDestinationGodown.Name}; the "
                          + "destination lines are mirrored from the source."
                        : "Enter the source (consumed) lines; the destination is mirrored automatically.";

            AdditionalCostTotalText = IndianFormat.AmountAlways(AdditionalCostsTotal());
            // 🔴 sameGodown is refused HERE as well as in InterGodownTransfer.Mirror. The engine guard is the one
            // that cannot be bypassed; this one is what stops the operator reaching Accept and being told by an
            // exception. Both are needed — dropping the engine guard would let a caller other than this screen
            // post a self-transfer, and dropping this one turns a foreseeable mistake into a thrown error.
            CanAccept = srcOnly >= 1 && !halfFilled && sourceBase > 0m
                        && TransferDestinationGodown is not null && !sameGodown;
            return;
        }

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

    /// <summary>Quantity with the ONE grouping rule (drift lock D2) — previously Western-grouped against the
    /// invariant culture while <c>IndianFormat.Quantity</c> grouped the Indian way for the same quantity.</summary>
    private static string Qty(decimal q) => Apex.Ledger.IndianMoneyFormat.Quantity(q);

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

        // 🔴 HARD REFUSAL ON AN ALTERING SCREEN, exactly as VoucherEntryViewModel.Accept refuses one, and for the
        // same measured reason. Accept BUILDS WITH A FRESH Guid AND number 0: taken on an altering screen it would
        // post a SECOND voucher and leave the original standing, which is a duplicated stock movement — closing
        // stock double-counted and the Balance Sheet moved. AcceptAlteration is the verb for this screen.
        if (IsAltering)
        {
            Message = "This screen is altering a posted voucher — press Ctrl+A to save the alteration. "
                    + "Accepting it as a new entry would post a second voucher and leave the original standing.";
            return false;
        }

        // Reject half-filled (touched-but-incomplete) rows up front with a clear message.
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete)
            || DestinationLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = IsPhysicalStock
                ? "Every entered line needs a stock item, a godown and a counted quantity ≥ 0."
                : "Every entered line needs a stock item, a godown and a positive quantity (rate, if entered, to the paisa).";
            return false;
        }

        if (!TryBuild(Guid.NewGuid(), number: 0, cancelled: false, out var voucher)) return false;

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

    /// <summary>
    /// 🔴 <b>Ctrl+A on an ALTERING screen — rebuilds the voucher under its OWN id and number and hands it to
    /// <c>InventoryPostingService.Replace</c>.</b> Census rows 4.9–4.16.
    ///
    /// <para><b>It shares <see cref="TryBuild"/> with <see cref="Accept"/>, deliberately.</b> The four builders
    /// are the only place that knows how a keyed grid becomes a posted voucher; a second copy for alteration is
    /// how the printed shape and the altered shape drift apart. The ONLY difference between the two verbs is the
    /// three values handed in — the id, the number and the cancelled flag — and the engine call at the end.</para>
    ///
    /// <para><b>🔴 A FAILED SAVE ROLLS THE SWAP BACK</b>, exactly as the accounting screen's
    /// <c>CommitAlteration</c> does. <c>Replace</c> mutates the in-memory aggregate and the save happens after it,
    /// so without this the books would hold the amended movement, the .db the original, and every later save would
    /// carry the divergence — a silently wrong closing stock on the next reload.</para>
    /// </summary>
    public bool AcceptAlteration()
    {
        Message = null;

        if (_alteringVoucherId is not { } alteringId)
        {
            Message = "This screen is entering a new voucher, not altering a posted one — press Enter to accept it.";
            return false;
        }

        if (_company.FindInventoryVoucher(alteringId) is not { } existing)
        {
            Message = "That voucher is no longer in this company's books — it may have been deleted since this "
                    + "screen was opened. Nothing was altered.";
            return false;
        }

        if (Lines.Any(l => !l.IsBlank && !l.IsComplete)
            || DestinationLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = IsPhysicalStock
                ? "Every entered line needs a stock item, a godown and a counted quantity ≥ 0."
                : "Every entered line needs a stock item, a godown and a positive quantity (rate, if entered, to the paisa).";
            return false;
        }

        // The number and the cancelled flag are carried from the POSTED voucher, never read off the screen:
        // Replace refuses a change to either by name, and re-reading them from the book is what makes the screen
        // unable to ask for one by accident.
        if (!TryBuild(existing.Id, existing.Number, existing.Cancelled, out var replacement)) return false;

        try
        {
            _service.Replace(existing.Id, replacement);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot alter: {ex.Message}";
            return false;
        }

        try
        {
            _storage.Save(_company);
        }
        catch
        {
            // Roll the in-memory swap back so the books and the .db cannot disagree. Replace is safe to call a
            // second time here: it only ever reads its replacement argument and writes the list slot, so
            // `existing` still holds exactly the figures it was posted with.
            //
            // 🔴 KNOWN DEFECT, NAMED HERE RATHER THAN LEFT TO BE REDISCOVERED — AND IT IS NOT MINE ALONE.
            // The rollback goes through Replace, so it appends a SECOND VoucherEditVerb.Alter entry recording an
            // alteration that never committed. `VoucherEntryViewModel.CommitAlteration` has the identical shape
            // and the identical defect on the accounting side, which is why it is not fixed here: fixing one
            // door would leave the two audit logs telling different stories about the same event.
            //
            // It is bounded, and the bound is why it was not worth diverging from the accounting door to fix in
            // this slice: the only way to reach it is a THROWING SAVE, and a throwing save persists nothing —
            // including the log — so neither entry reaches disk. The exposure is a later successful save in the
            // SAME session carrying both bogus entries forward.
            //
            // The correct fix is the one `DiscardUncommittedCancel` already models for Alt+X: an
            // out-parameter on Replace handing back the entry it appended, plus a bounded
            // DiscardUncommittedAlteration that unwinds the swap AND the line together. It belongs in a slice
            // that changes BOTH engines at once.
            _service.Replace(replacement.Id, existing);
            throw;
        }

        SavedNumber = existing.Number;
        Message = $"{_type.Name} No. {_company.FormatVoucherNumber(replacement)} altered.";
        _onSaved();
        return true;
    }

    /// <summary>
    /// The one build path both verbs use: dispatches on the base type to the four builders and turns a friendly
    /// pre-validation failure into <see cref="Message"/>. Returns false with <see cref="Message"/> set.
    /// </summary>
    private bool TryBuild(Guid id, int number, bool cancelled, out InventoryVoucher voucher)
    {
        var narration = string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim();
        try
        {
            voucher = _type.BaseType switch
            {
                VoucherBaseType.PurchaseOrder or VoucherBaseType.SalesOrder => BuildOrder(narration, id, number, cancelled),
                VoucherBaseType.PhysicalStock => BuildPhysical(narration, id, number, cancelled),
                VoucherBaseType.StockJournal => BuildStockJournal(narration, id, number, cancelled),
                _ => BuildMovementNote(narration, id, number, cancelled),
            };
            return true;
        }
        catch (InvalidValidationException ex)
        {
            Message = ex.Message; // a friendly pre-validation failure (blank grid, imbalance, …)
            voucher = null!;
            return false;
        }
    }

    // ---------------------------------------------------------------- builders

    private InventoryVoucher BuildOrder(string? narration, Guid id, int number, bool cancelled)
    {
        var lines = CompleteLines(Lines)
            .Select(l => new OrderLine(l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, RateOf(l)))
            .ToList();
        if (lines.Count == 0) throw Blank();

        return InventoryVoucher.Order(
            id, _type.Id, Date, lines,
            number: number, narration: narration, partyId: SelectedParty?.Ledger?.Id,
            cancelled: cancelled, postDated: IsPostDated);
    }

    private InventoryVoucher BuildMovementNote(string? narration, Guid id, int number, bool cancelled)
    {
        var direction = _type.BaseType is VoucherBaseType.ReceiptNote or VoucherBaseType.RejectionIn
            ? StockDirection.Inward
            : StockDirection.Outward;

        var allocations = CompleteLines(Lines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, direction, RateOf(l), l.Batch, l.UnitId,
                // W-K1 (census 9.8 / 9.7): the operator's Tracking No. and Cost Tracking Number. This is the
                // GOODS half of the tracking mechanism — the Purchase/Sales bill supplies the other half. Both
                // read null when their column is hidden (see InventoryVoucherLineViewModel.Tracking).
                l.Tracking, l.CostTracking))
            .ToList();
        if (allocations.Count == 0) throw Blank();

        // 🔴 The party is captured on a movement note, exactly as BuildOrder captures it on an order (Phase
        // 10.10 / WF-8 root-cause fix, R12 2026-08-07). Omitting it here is the defect that made a note in this
        // product unable to name a party at all — TallyPrime's Receipt Note and Delivery Note both take
        // "Party's A/c Name" [CORPUS-BOOK pp.71, 77] and its Rejection In/Out both take a "Ledger Account"
        // naming the customer/supplier [CORPUS-BOOK pp.51, 53]. Downstream, OrderFulfilment keys its cohort on
        // (PartyId, StockItemId): with no party reachable on a note, that cohort missed on every real book and
        // retired NOTHING, so the Order Register reported every delivered order fully outstanding for ever.
        // Like BuildOrder's, the party is OPTIONAL — "(none)" posts a null, which is a real shape, not an error.
        return new InventoryVoucher(
            id, _type.Id, Date, allocations,
            number: number, narration: narration, partyId: SelectedParty?.Ledger?.Id,
            cancelled: cancelled, postDated: IsPostDated);
    }

    private InventoryVoucher BuildStockJournal(string? narration, Guid id, int number, bool cancelled)
    {
        var source = CompleteLines(Lines)
            .Select(l => new InventoryAllocation(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Outward, RateOf(l), l.Batch, l.UnitId,
                // W-K1 (census 9.7): the cost tracking number follows a lot through a Stock Journal too — a
                // transformation is part of the lot's lifecycle. There is no Tracking No. here: a Stock Journal
                // moves stock between the company's OWN godowns and there is no bill to reconcile against.
                costTrackingNumber: l.CostTracking))
            .ToList();
        // W-K1 (census 9.9): under an inter-godown TRANSFER CLASS the destination arm is DERIVED from the source
        // rather than keyed. That is the vendor's whole promise for the class — name one destination godown, key
        // only the source, and every line is mirrored to it including batch, rate and unit. Doing it here, at
        // the posting boundary, is what makes the two arms unable to disagree; deriving it into the visible
        // destination grid instead would let a later keystroke edit one arm out of step with the other.
        List<InventoryAllocation> dest;
        if (SelectedTransferClass?.Class is not null)
        {
            if (TransferDestinationGodown is not { } destination)
                throw new InvalidValidationException(
                    "Pick the destination godown this transfer class moves the stock to.");
            dest = InterGodownTransfer.Mirror(source, destination.Id).ToList();
        }
        else
        {
            dest = CompleteLines(DestinationLines)
                .Select(l => new InventoryAllocation(
                    l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, StockDirection.Inward, RateOf(l), l.Batch, l.UnitId,
                    costTrackingNumber: l.CostTracking))
                .ToList();
        }

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
            id, _type.Id, Date, source, dest,
            number: number, narration: narration, cancelled: cancelled, postDated: IsPostDated,
            additionalCostLines: additionalCostLines.Count > 0 ? additionalCostLines : null);
    }

    private InventoryVoucher BuildPhysical(string? narration, Guid id, int number, bool cancelled)
    {
        var lines = CompleteLines(Lines)
            .Select(l => new PhysicalStockLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, l.Batch))
            .ToList();
        if (lines.Count == 0) throw Blank();

        return InventoryVoucher.PhysicalStock(
            id, _type.Id, Date, lines,
            number: number, narration: narration, cancelled: cancelled, postDated: IsPostDated);
    }

    private static IEnumerable<InventoryVoucherLineViewModel> CompleteLines(
        IEnumerable<InventoryVoucherLineViewModel> lines) => lines.Where(l => l.IsComplete);

    private static Money? RateOf(InventoryVoucherLineViewModel line)
        => line.HasRate && line.ParsedRate is { } r ? new Money(r) : null;

    private static InvalidValidationException Blank()
        => new("Enter at least one complete line before accepting.");

    /// <summary>Ctrl+T — toggles the post-dated flag for this voucher.</summary>
    public void TogglePostDated() => IsPostDated = !IsPostDated;

    /// <summary>Esc / the Cancel button: discards the in-progress voucher and returns to the Gateway. (Alt+X
    /// stopped reaching here in Phase 10.11 S3 — it now cancels a POSTED voucher from a report.)</summary>
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
