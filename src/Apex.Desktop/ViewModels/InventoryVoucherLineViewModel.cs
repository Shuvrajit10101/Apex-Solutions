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
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _batchLabel = string.Empty;

    /// <summary>
    /// True only when the batch-allocation sub-screen actually applies to this line (RQ-52 UI leak fix): the
    /// company maintains batch-wise details, the item Maintains-in-Batches, and item + godown + a positive
    /// quantity are all present. Kept in sync by the parent <see cref="InventoryVoucherEntryViewModel"/> on every
    /// change; the "⧉" batch affordance binds its visibility to this so it only shows where it does something.
    /// </summary>
    [ObservableProperty] private bool _wantsBatchAllocation;

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

    partial void OnSelectedItemChanged(StockItem? value) => _onChanged();
    partial void OnSelectedGodownChanged(Godown? value) => _onChanged();
    partial void OnQuantityTextChanged(string value) => _onChanged();
    partial void OnRateTextChanged(string value) => _onChanged();
    partial void OnBatchLabelChanged(string value) => _onChanged();

    /// <summary>The parsed quantity (0 when blank/unparsable).</summary>
    public decimal ParsedQuantity => TryParse(QuantityText, out var q) ? q : 0m;

    /// <summary>The parsed rate (null when blank; 0 or more otherwise).</summary>
    public decimal? ParsedRate =>
        string.IsNullOrWhiteSpace(RateText) ? null : (TryParse(RateText, out var r) ? r : null);

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
        && string.IsNullOrWhiteSpace(RateText)
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

            if (HasRate)
            {
                if (ParsedRate is not { } r) return false;
                if (r < 0m) return false;
                if (!new Money(r).IsPaisaExact) return false;
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
