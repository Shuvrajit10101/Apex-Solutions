using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A batch pick option for a line's Batch/Lot picker: an existing batch of the item, or "New Number".</summary>
public sealed class BatchPickOption
{
    /// <summary>The existing batch this option refers to; null for the "◦ New Number…" entry.</summary>
    public BatchMaster? Batch { get; init; }

    /// <summary>The label shown in the picker.</summary>
    public string Display { get; init; } = string.Empty;

    /// <summary>True for the "New Number" option (the operator types a fresh batch number).</summary>
    public bool IsNew => Batch is null;
}

/// <summary>
/// One editable line of the batch-allocation sub-screen (Phase 6 Cluster 1; requirements RQ-3/RQ-7): a
/// Batch/Lot (pick an existing batch or "New Number" + a typed number), an optional Mfg Dt., an Expiry (shown
/// for an existing batch, editable for a new one), a Quantity and an optional Rate. Repeatable so ONE item
/// line allocates across several batches (Σ batch qty = line qty). A non-blocking expiry
/// <see cref="Warning"/> (RQ-7) is surfaced when an expired / near-expiry batch is picked. Parsing/validation
/// is deferred to the parent <see cref="BatchAllocationViewModel"/>.
///
/// <para>MVVM boundary: references only the domain, no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class BatchAllocationLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The batch options for this item's picker (existing batches + a leading "New Number" entry).</summary>
    public IReadOnlyList<BatchPickOption> BatchOptions { get; }

    [ObservableProperty] private BatchPickOption? _selectedBatch;
    [ObservableProperty] private string _newBatchNumber = string.Empty;
    [ObservableProperty] private string _manufacturingDateText = string.Empty;
    [ObservableProperty] private string _expiryText = string.Empty;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;

    /// <summary>The non-blocking expiry warning shown when an expired / near-expiry batch is picked (RQ-7); null when clear.</summary>
    [ObservableProperty] private string? _warning;

    /// <summary>True while the warning marks an already-EXPIRED batch (drives the distinct red flag vs the amber near-expiry).</summary>
    [ObservableProperty] private bool _isExpired;

    public BatchAllocationLineViewModel(IReadOnlyList<BatchPickOption> batchOptions, Action onChanged)
    {
        BatchOptions = batchOptions ?? throw new ArgumentNullException(nameof(batchOptions));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    partial void OnSelectedBatchChanged(BatchPickOption? value)
    {
        // Selecting an existing batch pre-fills its mfg/expiry (read-only convenience); "New Number" clears them.
        if (value?.Batch is { } b)
        {
            NewBatchNumber = string.Empty;
            ManufacturingDateText = b.ManufacturingDate is { } m
                ? m.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : string.Empty;
            ExpiryText = b.ResolvedExpiryDate is { } e
                ? e.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : string.Empty;
        }
        _onChanged();
    }

    partial void OnNewBatchNumberChanged(string value) => _onChanged();
    partial void OnManufacturingDateTextChanged(string value) => _onChanged();
    partial void OnExpiryTextChanged(string value) => _onChanged();
    partial void OnQuantityTextChanged(string value) => _onChanged();
    partial void OnRateTextChanged(string value) => _onChanged();

    /// <summary>The effective batch/lot number for this line: the picked existing batch's number, or the typed new one.</summary>
    public string? BatchNumber => SelectedBatch is { IsNew: false, Batch: { } b }
        ? b.BatchNumber
        : string.IsNullOrWhiteSpace(NewBatchNumber) ? null : NewBatchNumber.Trim();

    /// <summary>The parsed quantity (0 when blank/unparsable).</summary>
    public decimal ParsedQuantity => TryParse(QuantityText, out var q) ? q : 0m;

    /// <summary>True once the row has been touched at all (any field) — a wholly blank trailing row is ignored.</summary>
    public bool IsBlank =>
        SelectedBatch is null
        && string.IsNullOrWhiteSpace(NewBatchNumber)
        && string.IsNullOrWhiteSpace(QuantityText)
        && string.IsNullOrWhiteSpace(RateText);

    private static bool TryParse(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// The batch-allocation sub-screen shown at voucher entry when the picked item Maintains-in-Batches (Phase 6
/// Cluster 1; requirements RQ-3/RQ-7/RQ-54). Opened after item + godown + quantity are known, it captures one
/// or more batch lines — Batch/Lot No (existing or New), Mfg Dt., Expiry, Qty, Rate — so ONE item line can
/// allocate across SEVERAL batches, with the invariant <b>Σ batch qty = line qty</b> surfaced live
/// (<see cref="RemainingText"/> / <see cref="IsBalanced"/>).
///
/// <para>On open it <b>defaults the selection</b> using the engine's
/// <see cref="BatchStockService.DefaultIssueSelection"/> (FEFO when the item uses expiry, else FIFO — DP-1) for
/// an OUTWARD line, so the common case needs no typing. Picking an expired / near-expiry batch raises a
/// <b>non-blocking</b> warning via <see cref="BatchStockService.ExpiryWarningFor"/> (RQ-7, warn-not-block) — the
/// screen never blocks the issue on it.</para>
///
/// <para>Committing (<see cref="Apply"/>) writes the batch lines back to the owning voucher line through the
/// supplied callback (the engine does the real posting); this VM never posts or persists itself. MVVM boundary:
/// references the engine but no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class BatchAllocationViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly BatchStockService _batches;
    private readonly StockItem _item;
    private readonly Godown _godown;
    private readonly decimal _lineQuantity;
    private readonly DateOnly _asOf;
    private readonly bool _isOutward;
    private readonly int _nearExpiryDays;
    private readonly Action<IReadOnlyList<BatchAllocation>>? _onCommitted;

    /// <summary>The batch pick options (existing batches of this item + a leading "New Number" entry).</summary>
    private readonly List<BatchPickOption> _batchOptions = new();

    /// <summary>The repeatable batch-allocation lines (always one blank trailing row for the next entry).</summary>
    public ObservableCollection<BatchAllocationLineViewModel> Lines { get; } = new();

    /// <summary>The header the sub-screen shows: item + godown + the quantity being allocated.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>The item name (header line).</summary>
    public string ItemName => _item.Name;

    /// <summary>The godown name (header line).</summary>
    public string GodownName => _godown.Name;

    /// <summary>The quantity to allocate, formatted for the header.</summary>
    public string LineQuantityText =>
        _lineQuantity.ToString("0.######", CultureInfo.InvariantCulture) + " " +
        (_company.FindUnit(_item.BaseUnitId)?.Symbol ?? string.Empty);

    /// <summary>Live "allocated / remaining" text so the operator sees Σ batch qty against the line qty.</summary>
    [ObservableProperty] private string _remainingText = string.Empty;

    /// <summary>True when Σ batch qty exactly equals the line qty (the Accept gate for the sub-screen).</summary>
    [ObservableProperty] private bool _isBalanced;

    /// <summary>Error/status surfaced under the grid (unbalanced, bad number, over-precision, …).</summary>
    [ObservableProperty] private string? _message;

    public BatchAllocationViewModel(
        Company company,
        StockItem item,
        Godown godown,
        decimal lineQuantity,
        DateOnly asOf,
        bool isOutward,
        int nearExpiryDays = 30,
        Action<IReadOnlyList<BatchAllocation>>? onCommitted = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _godown = godown ?? throw new ArgumentNullException(nameof(godown));
        _lineQuantity = lineQuantity;
        _asOf = asOf;
        _isOutward = isOutward;
        _nearExpiryDays = nearExpiryDays;
        _onCommitted = onCommitted;
        _batches = new BatchStockService(company);

        Title = $"Batch Allocation — {item.Name}";

        BuildBatchOptions();
        SeedDefaultSelection();
        Recompute();
    }

    /// <summary>Builds the batch picker options: a leading "New Number" entry, then the item's existing batches.</summary>
    private void BuildBatchOptions()
    {
        _batchOptions.Clear();
        _batchOptions.Add(new BatchPickOption { Batch = null, Display = "◦ New Number…" });
        foreach (var b in _company.BatchesFor(_item.Id)
                     .OrderBy(b => b.BatchNumber, StringComparer.OrdinalIgnoreCase))
            _batchOptions.Add(new BatchPickOption { Batch = b, Display = BatchLabel(b) });
    }

    private static string BatchLabel(BatchMaster b)
    {
        var expiry = b.ResolvedExpiryDate is { } e
            ? "  (exp " + e.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) + ")"
            : string.Empty;
        return b.BatchNumber + expiry;
    }

    /// <summary>
    /// Pre-fills the batch lines from the engine's default issue selection for an OUTWARD line (FEFO/FIFO per
    /// the item switch, DP-1). For an INWARD line there is no existing stock to draw from, so a single blank
    /// line is seeded for the operator to enter the received batch. Always leaves one blank trailing row.
    /// </summary>
    private void SeedDefaultSelection()
    {
        Lines.Clear();

        if (_isOutward && _lineQuantity > 0m)
        {
            var plan = _batches.DefaultIssueSelection(_item.Id, _godown.Id, _lineQuantity, _asOf);
            foreach (var pick in plan)
            {
                var line = NewLine();
                var option = _batchOptions.FirstOrDefault(o => o.Batch?.BatchNumber == pick.Batch);
                if (option is not null) line.SelectedBatch = option;
                else line.NewBatchNumber = pick.Batch;
                line.QuantityText = pick.Quantity.ToString("0.######", CultureInfo.InvariantCulture);
                if (pick.UnitCost.Amount > 0m)
                    line.RateText = pick.UnitCost.Amount.ToString("0.00", CultureInfo.InvariantCulture);
                Lines.Add(line);
            }
        }

        AddBlankLine();
    }

    /// <summary>Adds a fresh blank trailing line (the always-present next-entry row).</summary>
    public void AddBlankLine() => Lines.Add(NewLine());

    private BatchAllocationLineViewModel NewLine() =>
        new(_batchOptions, OnLineChanged);

    private void OnLineChanged()
    {
        EnsureTrailingBlank();
        RefreshWarnings();
        Recompute();
    }

    /// <summary>Ensures exactly one blank trailing row so the operator can always add another batch.</summary>
    private void EnsureTrailingBlank()
    {
        if (Lines.Count == 0 || !Lines[^1].IsBlank)
            AddBlankLine();
    }

    /// <summary>
    /// Re-derives the non-blocking expiry warning on each non-blank line (RQ-7): a picked expired batch flags a
    /// distinct "EXPIRED" warning; a near-expiry batch an amber "expires in N days" note. New (not-yet-created)
    /// batches carry no history, so no warning.
    /// </summary>
    private void RefreshWarnings()
    {
        foreach (var line in Lines)
        {
            line.Warning = null;
            line.IsExpired = false;
            if (line.IsBlank) continue;
            if (line.SelectedBatch is not { IsNew: false }) continue;   // only an existing batch can be expired
            if (line.BatchNumber is not { } number) continue;

            var warn = _batches.ExpiryWarningFor(_item.Id, number, _asOf, _nearExpiryDays);
            if (warn is null) continue;

            line.IsExpired = warn.Kind == BatchExpiryKind.Expired;
            var on = warn.ExpiryDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
            line.Warning = warn.Kind == BatchExpiryKind.Expired
                ? $"EXPIRED on {on} ({-warn.DaysToExpiry} day(s) ago) — issuing anyway is allowed."
                : $"Near expiry: expires {on} (in {warn.DaysToExpiry} day(s)).";
        }
    }

    /// <summary>Recomputes the allocated / remaining totals and the balanced flag / message.</summary>
    private void Recompute()
    {
        var allocated = Lines.Where(l => !l.IsBlank).Sum(l => l.ParsedQuantity);
        var remaining = _lineQuantity - allocated;
        IsBalanced = remaining == 0m && allocated == _lineQuantity;

        var unit = _company.FindUnit(_item.BaseUnitId)?.Symbol ?? string.Empty;
        RemainingText = IsBalanced
            ? $"Allocated {allocated.ToString("0.######", CultureInfo.InvariantCulture)} {unit} — balanced."
            : $"Allocated {allocated.ToString("0.######", CultureInfo.InvariantCulture)} of " +
              $"{_lineQuantity.ToString("0.######", CultureInfo.InvariantCulture)} {unit} " +
              $"(remaining {remaining.ToString("0.######", CultureInfo.InvariantCulture)}).";
    }

    /// <summary>
    /// The current batch allocations (one per non-blank line), the projection a caller reads back. Each carries
    /// the batch number, quantity and optional rate. This is a pure snapshot — it neither posts nor persists.
    /// </summary>
    public IReadOnlyList<BatchAllocation> Allocations => Lines
        .Where(l => !l.IsBlank && l.BatchNumber is not null && l.ParsedQuantity > 0m)
        .Select(l => new BatchAllocation(l.BatchNumber!, l.ParsedQuantity, ParseRate(l.RateText)))
        .ToList();

    private static Money? ParseRate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var r) && r >= 0m
            ? Money.FromRupees(r)
            : null;
    }

    /// <summary>
    /// Ctrl+A / Commit: validates every non-blank line has a batch number and a quantity to 6 dp, that a typed
    /// rate is paisa-exact, and that Σ batch qty = the line quantity (RQ-3). On success it hands the allocations
    /// to the owning line via the commit callback and returns true; on failure it surfaces a friendly message
    /// and returns false (nothing is committed). Never posts or persists — the engine owns that.
    /// </summary>
    public bool Apply()
    {
        Message = null;
        var active = Lines.Where(l => !l.IsBlank).ToList();
        if (active.Count == 0)
        {
            Message = "Add at least one batch allocation.";
            return false;
        }

        foreach (var l in active)
        {
            if (l.BatchNumber is null)
            {
                Message = "Every batch line needs a Batch / Lot number (pick one or type a new number).";
                return false;
            }
            if (!decimal.TryParse((l.QuantityText ?? string.Empty).Trim(),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var qty) || qty <= 0m)
            {
                Message = $"Batch '{l.BatchNumber}' needs a quantity greater than zero.";
                return false;
            }
            if (!Quantities.IsWithinPrecision(qty))
            {
                Message = $"Batch '{l.BatchNumber}' quantity {qty} must be to {Quantities.DecimalPlaces} decimal places.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(l.RateText))
            {
                if (ParseRate(l.RateText) is not { } rate || !rate.IsPaisaExact)
                {
                    Message = $"Batch '{l.BatchNumber}' rate must be ≥ 0 and to the paisa (2 decimal places).";
                    return false;
                }
            }
        }

        Recompute();
        if (!IsBalanced)
        {
            Message = "The batch quantities must add up to the line quantity before this allocation is accepted.";
            return false;
        }

        _onCommitted?.Invoke(Allocations);
        Message = "Batch allocation accepted.";
        return true;
    }
}

/// <summary>
/// One committed batch allocation carried back from the sub-screen to the owning voucher line (Phase 6
/// Cluster 1; RQ-3): the batch/lot number, the quantity allocated to it, and an optional per-unit rate.
/// </summary>
public sealed record BatchAllocation(string BatchNumber, decimal Quantity, Money? Rate);
