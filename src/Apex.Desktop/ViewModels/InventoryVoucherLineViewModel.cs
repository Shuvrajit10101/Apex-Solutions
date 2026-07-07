using System;
using System.Collections.Generic;
using System.Globalization;
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

    public InventoryVoucherLineViewModel(
        InventoryLineKind kind,
        IReadOnlyList<StockItem> stockItems,
        IReadOnlyList<Godown> godowns,
        Action onChanged)
    {
        Kind = kind;
        StockItems = stockItems ?? throw new ArgumentNullException(nameof(stockItems));
        Godowns = godowns ?? throw new ArgumentNullException(nameof(godowns));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        // Default the godown to the Main Location so the common single-godown case needs no picking.
        foreach (var g in godowns)
            if (g.IsMainLocation) { _selectedGodown = g; break; }
    }

    partial void OnSelectedItemChanged(StockItem? value)
    {
        // A new item starts a fresh, un-dirtied line so the Price-Level auto-fill can supply its rate.
        ClearPriceAutoFill();
        _onChanged();
    }
    partial void OnSelectedGodownChanged(Godown? value) => _onChanged();
    partial void OnQuantityTextChanged(string value) => _onChanged();
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

    partial void OnBatchLabelChanged(string value) => _onChanged();

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

    private static bool TryParse(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
