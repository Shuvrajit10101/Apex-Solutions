using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The line shape an <see cref="InventoryVoucherLineViewModel"/> row edits — it decides which quantity
/// column is shown/validated. All three share Stock Item + Godown pickers and an optional Batch label; they
/// differ only in the quantity field:
/// <list type="bullet">
///   <item><see cref="Order"/> — a PO/SO order line: Qty (ordered) + optional Rate, no stock effect.</item>
///   <item><see cref="Movement"/> — a GRN/Delivery/Rejection/Stock-Journal allocation line: Qty (moved,
///     &gt; 0) + optional Rate; the <b>direction</b> is fixed by the voucher type (implied), not chosen
///     here.</item>
///   <item><see cref="Counted"/> — a Physical-Stock count line: Counted Qty (≥ 0), no rate.</item>
/// </list>
/// </summary>
public enum InventoryLineKind
{
    /// <summary>PO/SO order line (Qty ordered + optional Rate; no stock effect).</summary>
    Order,

    /// <summary>Stock-movement allocation line (Qty moved &gt; 0 + optional Rate; direction implied by type).</summary>
    Movement,

    /// <summary>Physical-Stock count line (Counted Qty ≥ 0; no rate).</summary>
    Counted,
}

/// <summary>
/// One editable line in the inventory/order voucher-entry grid: the picked <see cref="Stock Item"/>, the
/// <see cref="Godown"/>, a quantity typed as text, an optional per-unit rate (paisa-exact) and an optional
/// batch/lot label. It mirrors <see cref="VoucherLineViewModel"/> (the accounting Dr/Cr line) but for the
/// separate <see cref="InventoryVoucher"/> aggregate — there is no Dr/Cr side (a stock movement's direction
/// is implied by the voucher type). Parsing/validation is deferred to the parent
/// <see cref="InventoryVoucherEntryViewModel"/>; this class only holds the editable state and raises change
/// notifications so the parent's live totals/Accept-enabled recompute as the user types.
///
/// <para>MVVM boundary: references only the domain, no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class InventoryVoucherLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>Which quantity column this line edits (order / movement / counted).</summary>
    public InventoryLineKind Kind { get; }

    /// <summary>The stock items the picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns the picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    [ObservableProperty] private StockItem? _selectedItem;
    [ObservableProperty] private Godown? _selectedGodown;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string _billedQuantityText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _discountText = string.Empty;
    [ObservableProperty] private string _batchLabel = string.Empty;

    // --------------------------------------------------------------- price-level auto-fill (slice 5; RQ-30)

    /// <summary>
    /// When the parent auto-fill is writing <see cref="RateText"/> / <see cref="DiscountText"/> it sets this
    /// so the change is NOT recorded as an operator edit — the classic "auto-fill clobbers the manual edit"
    /// trap is avoided by only ever marking the field dirty for a genuine user keystroke.
    /// </summary>
    private bool _suppressDirty;

    /// <summary>
    /// True once the operator has typed into the Rate field themselves (RQ-30). The Price-Level auto-fill
    /// writes a resolved rate ONLY when the line is not user-dirty, so an operator override always sticks
    /// through a later Qty / Price-Level re-resolve. Reset by <see cref="ClearPriceAutoFill"/> when the item
    /// changes (a new item starts a fresh, un-dirtied line).
    /// </summary>
    [ObservableProperty] private bool _isRateUserDirty;

    /// <summary>True once the operator has typed into the Discount field themselves (RQ-30) — same sticky rule.</summary>
    [ObservableProperty] private bool _isDiscountUserDirty;

    /// <summary>
    /// True when this line shows the gated <b>Price Level</b> Discount % column (slice 5; RQ-30). Kept in sync by
    /// the parent <see cref="VoucherEntryViewModel"/>: on only when the company's "Enable multiple Price Levels"
    /// flag is on AND this is a Sales item-invoice line. Off ⇒ the Discount column collapses and the line is
    /// byte-identical to a non-price-level line (ER-13).
    /// </summary>
    [ObservableProperty] private bool _showDiscount;

    /// <summary>
    /// True when this line shows the separate <b>Billed</b> quantity column alongside the <b>Actual</b>
    /// quantity (Book pp.145–147; Phase 6 slice 4 RQ-22). Kept in sync by the parent
    /// <see cref="VoucherEntryViewModel"/>: on only when the company's "Use separate Actual &amp; Billed Qty"
    /// flag (<see cref="Company.UseSeparateActualBilledQuantity"/>) is on <b>and</b> this is a Sales/Purchase
    /// item-invoice (Movement) line. Off ⇒ the Billed column collapses and <see cref="ParsedBilledQuantity"/>
    /// ≡ <see cref="ParsedActualQuantity"/> (byte-identical to a non-A/B line, ER-13).
    /// </summary>
    [ObservableProperty] private bool _showActualBilled;

    /// <summary>
    /// True only when the batch-allocation sub-screen actually applies to this line (RQ-52 UI leak fix): the
    /// company maintains batch-wise details, the item Maintains-in-Batches, and item + godown + a positive
    /// quantity are all present. Kept in sync by the parent <see cref="InventoryVoucherEntryViewModel"/> on every
    /// change; the "⧉" batch affordance binds its visibility to this so it only shows where it does something.
    /// </summary>
    [ObservableProperty] private bool _wantsBatchAllocation;

    /// <summary>
    /// True when this line should show the read-only <b>landed</b> (effective) rate + value columns (Book
    /// pp.133–141; Phase 6 slice 3 ER-4). Kept in sync by the parent VM: on when the voucher tracks additional
    /// costs (a Purchase item-invoice with a tracked type, or a Stock-Journal transfer with additional-cost
    /// lines). Off ⇒ the Auto landed columns collapse to zero width, so an untracked screen is byte-unchanged.
    /// </summary>
    [ObservableProperty] private bool _showLanded;

    /// <summary>The engine's landed unit rate for this line (read-only display; blank until computed, ER-4).</summary>
    [ObservableProperty] private string _landedRateText = string.Empty;

    /// <summary>The engine's landed value for this line = purchase value + apportioned additional cost (read-only).</summary>
    [ObservableProperty] private string _landedValueText = string.Empty;

    /// <summary>True when this line's kind carries a per-unit Rate column (Order / Movement, not Counted).</summary>
    public bool ShowsRate => Kind is InventoryLineKind.Order or InventoryLineKind.Movement;

    /// <summary>True when this line's kind carries a Batch column (Movement / Counted, not Order).</summary>
    public bool ShowsBatch => Kind is InventoryLineKind.Movement or InventoryLineKind.Counted;

    // --------------------------------------------------------------- line unit (WI-10 slice B)

    /// <summary>
    /// Every unit defined in the company (set by the parent; empty when the parent supplies none). The
    /// per-line <see cref="UnitOptions"/> are filtered out of this by the picked item's base unit.
    /// </summary>
    public IReadOnlyList<Unit> AllUnits { get; }

    /// <summary>
    /// The units this line's quantity may legally be stated in: the picked item's own <b>base</b> unit
    /// first, followed by every <b>compound</b> unit that reduces to it (i.e. whose
    /// <see cref="Unit.BaseMeasureUnitId"/> is that base unit) — so an item held in Nos offers "Nos" and
    /// "Doz-Nos", never "Kg-g". Empty until an item is picked. This is precisely the filter the
    /// <see cref="Unit.BaseMeasureUnitId"/> direction fix makes correct.
    /// </summary>
    public ObservableCollection<Unit> UnitOptions { get; } = new();

    /// <summary>
    /// The unit the typed <see cref="QuantityText"/> is stated in. Defaults to the item's base unit, so an
    /// untouched line behaves exactly as it did before line units existed.
    /// </summary>
    [ObservableProperty] private Unit? _selectedUnit;

    /// <summary>
    /// True when this line has a real choice of unit — the item's base unit plus at least one compound unit
    /// reducing to it. With no alternative the picker is hidden and the line is byte-identical to a
    /// pre-line-unit line (ER-13).
    /// </summary>
    public bool ShowUnit => UnitOptions.Count > 1;

    /// <summary>
    /// The unit id to stamp on the posted <see cref="InventoryAllocation"/>, or <c>null</c> when the quantity
    /// is already in the item's base unit. Returning null for the base unit (and whenever the picker is
    /// hidden) keeps an unchanged line's persisted + exported shape byte-identical to before this feature
    /// (ER-13) — and it is the <b>gated-field discipline</b>: a unit is written only when the picker is
    /// actually shown, so a hidden picker can never silently stamp a unit onto the line.
    /// </summary>
    public Guid? UnitId =>
        ShowUnit && SelectedUnit is { } u && SelectedItem is { } item && u.Id != item.BaseUnitId
            ? u.Id
            : null;

    /// <summary>
    /// The typed quantity converted into the stock item's <b>base</b> unit — the quantity the engine
    /// accumulates on hand. "2 Doz-Nos" ⇒ 24 Nos. Equals <see cref="ParsedQuantity"/> whenever the line is in
    /// the base unit.
    /// </summary>
    public decimal ParsedQuantityInBaseUnit =>
        UnitId is not null && SelectedUnit is { } u ? u.QuantityInBaseMeasure(ParsedQuantity) : ParsedQuantity;

    /// <summary>
    /// Rebuilds <see cref="UnitOptions"/> for the currently picked item and re-defaults
    /// <see cref="SelectedUnit"/> when the previous pick no longer applies (a different item's units).
    /// </summary>
    private void RefreshUnitOptions()
    {
        var previous = SelectedUnit;
        UnitOptions.Clear();

        if (SelectedItem is { } item && AllUnits.Count > 0)
        {
            var baseUnit = AllUnits.FirstOrDefault(u => u.Id == item.BaseUnitId);
            if (baseUnit is not null)
            {
                UnitOptions.Add(baseUnit);
                foreach (var u in AllUnits)
                    if (u.IsCompound && u.BaseMeasureUnitId == item.BaseUnitId)
                        UnitOptions.Add(u);
            }
        }

        OnPropertyChanged(nameof(ShowUnit));
        // Keep the operator's pick when it is still legal for the new item; otherwise fall back to the base
        // unit (the first option) so the line always states a unit it can actually be converted from.
        SelectedUnit = previous is not null && UnitOptions.Any(u => u.Id == previous.Id)
            ? UnitOptions.First(u => u.Id == previous.Id)
            : UnitOptions.FirstOrDefault();
    }

    public InventoryVoucherLineViewModel(
        InventoryLineKind kind,
        IReadOnlyList<StockItem> stockItems,
        IReadOnlyList<Godown> godowns,
        Action onChanged,
        IReadOnlyList<Unit>? units = null)
    {
        Kind = kind;
        StockItems = stockItems ?? throw new ArgumentNullException(nameof(stockItems));
        Godowns = godowns ?? throw new ArgumentNullException(nameof(godowns));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        AllUnits = units ?? Array.Empty<Unit>();

        // Default the godown to the Main Location so the common single-godown case needs no picking.
        foreach (var g in godowns)
            if (g.IsMainLocation) { _selectedGodown = g; break; }
    }

    partial void OnSelectedItemChanged(StockItem? value)
    {
        // A new item starts a fresh, un-dirtied line so the Price-Level auto-fill can supply its rate.
        ClearPriceAutoFill();
        // …and a fresh item cannot keep the previous item's batch allocation: a batch number is unique WITHIN an
        // item (RQ-1), so carrying it over would stamp another item's lot onto this one.
        ClearBatchAllocations();
        RefreshUnitOptions();
        _onChanged();
    }

    partial void OnSelectedUnitChanged(Unit? value) => _onChanged();

    partial void OnSelectedGodownChanged(Godown? value)
    {
        // Batch balances are per godown (spec §4.3), so moving the line to another godown invalidates the split.
        ClearBatchAllocations();
        _onChanged();
    }

    partial void OnQuantityTextChanged(string value)
    {
        // The sub-screen guarantees Σ batch qty = the line qty at the moment it was accepted (C-29). Editing the
        // quantity afterwards breaks that, so the stale split is dropped rather than left to post a quantity the
        // operator can no longer see — re-open the sub-screen to re-allocate.
        if (_batchAllocations.Count > 0 && _batchAllocations.Sum(a => a.Quantity) != ParsedQuantity)
            ClearBatchAllocations();
        _onChanged();
    }

    partial void OnBilledQuantityTextChanged(string value) => _onChanged();

    partial void OnRateTextChanged(string value)
    {
        // Only a genuine operator keystroke marks the line dirty; an auto-fill write is suppressed (RQ-30).
        if (!_suppressDirty) IsRateUserDirty = true;
        _onChanged();
    }

    partial void OnDiscountTextChanged(string value)
    {
        if (!_suppressDirty) IsDiscountUserDirty = true;
        _onChanged();
    }

    partial void OnBatchLabelChanged(string value)
    {
        // Typing over the label the sub-screen wrote is an explicit override: the committed allocation no longer
        // describes what the operator wants, so it is dropped rather than posted behind the new text. Suppressed
        // while the sub-screen is writing the label itself (or it would erase the allocation it just committed).
        if (!_writingBatchLabel && _batchAllocations.Count > 0)
        {
            _batchLabelFromAllocation = false;
            _batchAllocations = Array.Empty<BatchAllocation>();
            OnPropertyChanged(nameof(BatchAllocations));
            OnPropertyChanged(nameof(HasBatchSplit));
        }
        _onChanged();
    }

    /// <summary>
    /// Writes the Price-Level auto-fill values (RQ-30) WITHOUT marking the line dirty. The parent calls this only
    /// for a line that is not operator-dirtied, so an override is never clobbered. Setting
    /// <paramref name="rate"/>/<paramref name="discount"/> to null leaves that field untouched.
    /// </summary>
    public void ApplyPriceAutoFill(string? rate, string? discount)
    {
        _suppressDirty = true;
        try
        {
            if (rate is not null && !IsRateUserDirty) RateText = rate;
            if (discount is not null && !IsDiscountUserDirty) DiscountText = discount;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    /// <summary>Resets the auto-fill dirty flags (a fresh item line) without touching the typed text.</summary>
    public void ClearPriceAutoFill()
    {
        IsRateUserDirty = false;
        IsDiscountUserDirty = false;
    }

    /// <summary>The parsed quantity (0 when blank/unparsable). This is the <b>Actual</b> (stock) quantity.</summary>
    public decimal ParsedQuantity => TryParse(QuantityText, out var q) ? q : 0m;

    /// <summary>The parsed <b>Actual</b> (stock) quantity — the same value as <see cref="ParsedQuantity"/>,
    /// named to make the Actual/Billed split explicit at the call site (Phase 6 slice 4 RQ-22/RQ-23).</summary>
    public decimal ParsedActualQuantity => ParsedQuantity;

    /// <summary>
    /// The parsed <b>Billed</b> quantity — the quantity the <b>accounts</b> (and GST) are updated with (RQ-23).
    /// When the A/B column is shown (<see cref="ShowActualBilled"/>) and a billed value is typed, it is used;
    /// otherwise it defaults to the <b>Actual</b> quantity, so a feature-off line is byte-identical (ER-13).
    /// </summary>
    public decimal ParsedBilledQuantity =>
        ShowActualBilled && TryParse(BilledQuantityText, out var b) ? b : ParsedActualQuantity;

    /// <summary>The parsed rate (null when blank; 0 or more otherwise).</summary>
    public decimal? ParsedRate =>
        string.IsNullOrWhiteSpace(RateText) ? null : (TryParse(RateText, out var r) ? r : null);

    /// <summary>
    /// The parsed Price-Level discount percent (slice 5; RQ-30/DP-A). Only participates when the gated
    /// <see cref="ShowDiscount"/> column is shown AND a value is typed; otherwise 0, so the value path is
    /// byte-identical to a non-price-level line (<c>value = qty × rate</c>, ER-13).
    /// </summary>
    public decimal ParsedDiscountPercent =>
        ShowDiscount && !string.IsNullOrWhiteSpace(DiscountText) && TryParse(DiscountText, out var d) ? d : 0m;

    /// <summary>
    /// The net per-unit rate after any Price-Level discount (DP-A): <c>rate × (1 − discount/100)</c>, rounded to
    /// the paisa deterministically. When the discount is 0 / the column is hidden this equals the raw rate exactly
    /// (a paisa-exact rate rounds to itself), so the existing <c>value = qty × rate</c> invariant is preserved and
    /// posting/valuation are untouched. Null when no rate is typed.
    /// </summary>
    public Money? EffectiveRate =>
        ParsedRate is { } r
            ? new Money(r * (1m - ParsedDiscountPercent / 100m)).RoundToPaisa()
            : (Money?)null;

    /// <summary>True when a rate was typed (so the parent must validate it is paisa-exact + ≥ 0).</summary>
    public bool HasRate => ShowsRate && !string.IsNullOrWhiteSpace(RateText);

    /// <summary>The trimmed batch label, or null when blank / not a batch-carrying kind.</summary>
    public string? Batch =>
        ShowsBatch && !string.IsNullOrWhiteSpace(BatchLabel) ? BatchLabel.Trim() : null;

    // --------------------------------------------------------------- batch allocation (G-5; BOOK pp.130–132)

    private IReadOnlyList<BatchAllocation> _batchAllocations = Array.Empty<BatchAllocation>();

    /// <summary>
    /// True while <see cref="BatchLabel"/> was WRITTEN BY the sub-screen rather than typed by the operator. It is
    /// what makes dropping a stale allocation safe: the derived label — which may be the summary "Multi (N)",
    /// not a real batch number — is cleared with it, so a discarded split can never leave "Multi (2)" behind to
    /// be posted as if it were a batch. An operator-typed label is never touched.
    /// </summary>
    private bool _batchLabelFromAllocation;

    /// <summary>Suppresses the "operator retyped the label" reaction while the sub-screen writes it itself.</summary>
    private bool _writingBatchLabel;

    /// <summary>
    /// The batch allocations committed for this line by the batch-allocation sub-screen (G-5; BOOK pp.130–132
    /// <b>[verified-A1]</b>). <b>Empty</b> for every line that never opened the sub-screen — which is every line
    /// on a non-batch item — so such a line posts exactly as it did before this feature existed (ER-13).
    ///
    /// <para>Their quantities are guaranteed by the sub-screen to sum to <see cref="ParsedQuantity"/> (C-29), so
    /// splitting a line across several batches never invents or loses stock.</para>
    /// </summary>
    public IReadOnlyList<BatchAllocation> BatchAllocations => _batchAllocations;

    /// <summary>
    /// True when this line was allocated across MORE THAN ONE batch — the only case in which the posted shape
    /// differs from a pre-slice line (one grid line becomes one posted item line per batch). A single-batch
    /// allocation posts as exactly one line carrying that batch number, identical in shape to a hand-typed label.
    /// </summary>
    public bool HasBatchSplit => _batchAllocations.Count > 1;

    /// <summary>
    /// Writes the sub-screen's committed allocations onto this line and reflects them in the grid's Batch/Lot
    /// cell — one batch shows its number, several show a "Multi (N)" summary (the same convention the stock
    /// screens already use). Passing an empty list clears the allocation back to the free-text label.
    /// </summary>
    public void SetBatchAllocations(IReadOnlyList<BatchAllocation> allocations)
    {
        _batchAllocations = allocations ?? Array.Empty<BatchAllocation>();
        if (_batchAllocations.Count > 0)
        {
            _writingBatchLabel = true;
            try
            {
                BatchLabel = _batchAllocations.Count == 1
                    ? _batchAllocations[0].BatchNumber
                    : $"Multi ({_batchAllocations.Count})";
            }
            finally
            {
                _writingBatchLabel = false;
            }
            _batchLabelFromAllocation = true;
        }
        OnPropertyChanged(nameof(BatchAllocations));
        OnPropertyChanged(nameof(HasBatchSplit));
        _onChanged();
    }

    /// <summary>
    /// Drops any committed batch allocation (a fresh item / godown, or a quantity the split no longer matches),
    /// along with the label the sub-screen derived from it — never a label the operator typed themselves.
    /// </summary>
    public void ClearBatchAllocations()
    {
        if (_batchAllocations.Count == 0) return;
        _batchAllocations = Array.Empty<BatchAllocation>();
        OnPropertyChanged(nameof(BatchAllocations));
        OnPropertyChanged(nameof(HasBatchSplit));
        if (_batchLabelFromAllocation)
        {
            _batchLabelFromAllocation = false;
            BatchLabel = string.Empty;      // raises its own change notification
        }
    }

    /// <summary>
    /// The <b>one</b> definition of this line's extended value (ER-4 — one source per figure), used by the live
    /// totals, by GST/TCS and by the posting so they can never disagree: <c>rate × Billed qty</c>, snapped to the
    /// paisa. <see cref="Money.Zero"/> when no rate is typed.
    ///
    /// <para><b>A batch split deliberately does NOT enter here.</b> Allocating the line across lots decides WHICH
    /// units move, never what they are worth — the goods and the rate are the same either way, so the customer
    /// must be billed the same figure split or unsplit. Valuing a split as Σ (rate × each batch quantity) breaks
    /// that: <see cref="Money.ForexBase"/> snaps every product to the paisa, so N batch rows round N times where
    /// the line rounds once, and Σ-of-rounded ≠ rounded-of-Σ the moment a batch quantity is fractional (1.5 ×
    /// ₹19.75 = ₹29.625 twice ⇒ ₹59.26 against the line's own ₹59.25). Keeping the ONE definition on the Billed
    /// basis also keeps a short-billed line honest (RQ-23): the batch rows always carry the ACTUAL quantity, so
    /// summing them would put the invoice total and the GST/TCS base on the Actual basis.</para>
    ///
    /// <para>The posted rows are held to this figure at the posting boundary — see
    /// <c>VoucherEntryViewModel.TryAppendSplitBatchLines</c>, which refuses a split whose rows cannot foot to it
    /// rather than letting the two diverge.</para>
    /// </summary>
    public Money LineValue =>
        EffectiveRate is { } rate ? Money.ForexBase(rate, ParsedBilledQuantity) : Money.Zero;

    /// <summary>
    /// True once the row has been touched at all (any field). A wholly blank row is ignored by the parent so
    /// the always-present blank trailing row never blocks Accept.
    /// </summary>
    public bool IsBlank =>
        SelectedItem is null
        && string.IsNullOrWhiteSpace(QuantityText)
        && string.IsNullOrWhiteSpace(BilledQuantityText)
        && string.IsNullOrWhiteSpace(RateText)
        && string.IsNullOrWhiteSpace(DiscountText)
        && string.IsNullOrWhiteSpace(BatchLabel);

    /// <summary>
    /// True when the row is fully and validly specified for its kind: an item + a godown picked, and a
    /// quantity that parses within precision and satisfies the kind's sign rule (Order/Movement need &gt; 0,
    /// a Counted line allows ≥ 0). A typed rate must be paisa-exact + ≥ 0. This is the parent's Accept gate.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            if (SelectedItem is null || SelectedGodown is null) return false;
            if (!TryParse(QuantityText, out var qty)) return false;
            if (!Quantities.IsWithinPrecision(qty)) return false;
            if (Kind == InventoryLineKind.Counted ? qty < 0m : qty <= 0m) return false;

            // Billed quantity (only when the A/B column is shown, RQ-22): when typed it must parse, be ≥ 0 and
            // 6-dp exact (no upper bound vs Actual — RQ-25); when blank it defaults to Actual, so no extra rule.
            if (ShowActualBilled && !string.IsNullOrWhiteSpace(BilledQuantityText))
            {
                if (!TryParse(BilledQuantityText, out var billed)) return false;
                if (billed < 0m) return false;
                if (!Quantities.IsWithinPrecision(billed)) return false;
            }

            if (HasRate)
            {
                if (ParsedRate is not { } r) return false;
                if (r < 0m) return false;
                if (!new Money(r).IsPaisaExact) return false;
            }

            // Price-Level discount (only when the gated column is shown): a typed value must parse and be in
            // [0, 100). Blank/hidden ⇒ no rule, so a non-price-level line is byte-identical (ER-13).
            if (ShowDiscount && !string.IsNullOrWhiteSpace(DiscountText))
            {
                if (!TryParse(DiscountText, out var disc)) return false;
                if (disc < 0m || disc >= 100m) return false;
            }
            return true;
        }
    }

    // =============================================================== Phase 10.11 S5e — the rehydration INVERSE

    /// <summary>
    /// <b>The inverse of the item-line writer</b> - the <c>new VoucherInventoryLine(...)</c> call in
    /// <c>VoucherEntryViewModel.AcceptItemInvoice</c> and <c>PosBillingViewModel.Accept</c>: re-keys this blank row
    /// from a POSTED <see cref="VoucherInventoryLine"/> so that re-running the writer reproduces it exactly.
    /// Returns <c>null</c> on success, or a <b>named refusal</b> when the posted line carries something this row
    /// cannot express today.
    ///
    /// <para>&#x1F534; <b>REHYDRATION IS FLAT - one grid row per POSTED line, and a batch split is NEVER
    /// reconstructed.</b> A line allocated across N batches posts N item rows
    /// (<c>VoucherEntryViewModel.TryAppendSplitBatchLines</c>), so N posted rows fit two keyed states: one split
    /// row, or N separate rows. They are not, however, two different VOUCHERS: the guard at that posting boundary
    /// refuses any split whose per-batch values do not foot to the line value, and forces Billed = Actual on it, so
    /// every split that actually posts is <b>value-identical</b> to the N-separate-rows keying. Rehydrating flat is
    /// therefore a true inverse of the POSTED shape; the only thing lost is the operator's knowledge that the
    /// sub-screen was used, and no figure depends on it.</para>
    ///
    /// <para>&#x1F534; <b>The Price-Level discount is what this method cannot invert, and it is CAUGHT rather than
    /// assumed away.</b> <see cref="EffectiveRate"/> is <c>rate x (1 - discount/100)</c> and only the PRODUCT is
    /// posted, so on a line whose <see cref="ShowDiscount"/> column is live the list rate is unrecoverable. That
    /// family is refused at the door (<c>VoucherAlterationEligibility</c>) - but the closing round-trip check below
    /// is the backstop that would catch it here too, because it compares what the writer will REBUILD against what
    /// was POSTED. No assumption about which screens carry a discount is load-bearing in this method.</para>
    /// </summary>
    public string? RehydrateFrom(VoucherInventoryLine posted)
    {
        ArgumentNullException.ThrowIfNull(posted);

        var item = StockItems.FirstOrDefault(i => i.Id == posted.StockItemId);
        if (item is null)
            return "one of its item lines moves a stock item that is no longer in this company, so the entry "
                 + "screen cannot show it.";

        var godown = Godowns.FirstOrDefault(g => g.Id == posted.GodownId);
        if (godown is null)
            return $"the location one of its '{item.Name}' lines moved through is no longer in this company, so "
                 + "the entry screen cannot show it.";

        // Assigning the item is what rebuilds UnitOptions (and clears the price auto-fill dirt), so it comes first -
        // exactly as VoucherLineViewModel.RehydrateFrom assigns the ledger before filling the panels it opens.
        SelectedItem = item;
        SelectedGodown = godown;

        if (RehydrateUnit(posted, item) is { } unitRefusal) return unitRefusal;

        QuantityText = ExactDecimalText(posted.Quantity);

        // Billed is written ONLY when it differs from Actual: with the A/B columns off, ParsedBilledQuantity is
        // DEFINED as Actual, so leaving the field blank is what keeps a feature-off line byte-identical (ER-13).
        if (posted.BilledQuantity != posted.Quantity)
        {
            if (!ShowActualBilled)
                return $"'{item.Name}' was billed {posted.BilledQuantity} against an actual {posted.Quantity}, "
                     + "and the separate Actual/Billed quantity columns are switched off on this company - so the "
                     + "screen would re-bill the actual quantity and move the invoice value.";
            BilledQuantityText = ExactDecimalText(posted.BilledQuantity);
        }

        RateText = ExactDecimalText(posted.Rate.Amount);
        BatchLabel = posted.BatchLabel ?? string.Empty;

        // &#x1F534; THE WHOLE POINT OF AN INVERSE: what the writer will rebuild must be what was posted. Each figure
        // is compared against the value this row now REBUILDS (not against the text it holds), so a lossy render, a
        // hidden gate or a live discount column is caught HERE rather than at the store or, worse, nowhere.
        //
        // &#x1F534; MUTATION RESULT, RECORDED RATHER THAN CLAIMED. Deleting the RATE comparison alone reddens
        // NOTHING on today's inputs, and that is not a gap in the tests - it is what the guard being a BACKSTOP
        // means. Every reachable input satisfies it by construction: VoucherInventoryLine's own constructor
        // refuses a rate that is not paisa-exact, ExactDecimalText is lossless, and the one lossy shape
        // (EffectiveRate = rate x (1 - discount/100)) is refused at the door by VoucherAlterationEligibility. It is
        // kept because the door is the thing most likely to be widened later, and because it DOES fire on a
        // perturbed rate: injecting a nonce into RateText above reddens 13 tests in the alteration suite, and with
        // this comparison removed the same nonce reddens the byte-identical export comparisons instead - i.e. the
        // defect escapes the screen and is caught only at the end of the round trip.
        if (ParsedQuantity != posted.Quantity)
            return $"the actual quantity on '{item.Name}' cannot be re-keyed exactly ({posted.Quantity} was "
                 + $"posted, the screen rebuilds {ParsedQuantity}).";
        if (ParsedBilledQuantity != posted.BilledQuantity)
            return $"the billed quantity on '{item.Name}' cannot be re-keyed exactly ({posted.BilledQuantity} was "
                 + $"posted, the screen rebuilds {ParsedBilledQuantity}).";
        if (EffectiveRate is not { } rebuiltRate || rebuiltRate.Amount != posted.Rate.Amount)
            return $"the rate on '{item.Name}' cannot be re-keyed exactly ({posted.Rate.Amount} was posted, the "
                 + $"screen rebuilds {RebuiltRateText()}) - the list rate and the price-level discount are not "
                 + "recoverable from a posted effective rate.";
        if (Batch != posted.BatchLabel)
            return $"the batch on '{item.Name}' cannot be re-keyed exactly ('{posted.BatchLabel}' was posted, the "
                 + $"screen rebuilds '{Batch}').";
        if (UnitId != posted.UnitId)
            return $"the unit on '{item.Name}' cannot be re-keyed exactly, so its quantity and rate would be "
                 + "restated in a different unit from the one they were posted in.";

        return null;
    }

    private string RebuiltRateText() =>
        EffectiveRate is { } r ? r.Amount.ToString(CultureInfo.InvariantCulture) : "nothing";

    /// <summary>
    /// Re-keys <see cref="SelectedUnit"/> from the posted line. A posted <c>null</c> unit means "the item's own
    /// base unit", which <see cref="RefreshUnitOptions"/> has already selected and which <see cref="UnitId"/>
    /// renders back as <c>null</c> - so nothing is written for it. A posted unit the item no longer offers (its
    /// compound unit deleted or repointed) is refused by name: silently falling back to the base unit would
    /// restate a "2 Doz @ 10" line as "2 Nos @ 10".
    /// </summary>
    private string? RehydrateUnit(VoucherInventoryLine posted, StockItem item)
    {
        if (posted.UnitId is not { } unitId) return null;

        var unit = UnitOptions.FirstOrDefault(u => u.Id == unitId);
        if (unit is null || !ShowUnit)
            return $"one of its '{item.Name}' lines states its quantity and rate in a unit this item no longer "
                 + "offers, so the screen cannot re-key it without restating the line in another unit.";

        SelectedUnit = unit;
        return null;
    }

    /// <summary>
    /// Renders <paramref name="value"/> so that parsing it back yields the SAME decimal - the same lossless-render
    /// discipline <c>VoucherLineViewModel.ExactDecimalText</c> follows on the plain grid, and for the same reason:
    /// a tidy fixed-places format silently truncates, and a truncated quantity or rate moves money.
    /// </summary>
    private static string ExactDecimalText(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
