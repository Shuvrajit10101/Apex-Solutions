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

/// <summary>A batch row for the existing-batches list on the master screen.
///
/// <para>W33 C3 (census 3.8) — carries <see cref="IMasterListRow"/> so the ONE shared
/// <see cref="IMasterListScreen"/> arm can walk it with the arrows and delete it with Alt+D.</para>
/// </summary>
public sealed partial class BatchListRow : ObservableObject, IMasterListRow
{
    public string BatchNumber { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public string Godown { get; init; } = string.Empty;
    public string MfgDate { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string OpeningValue { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <summary><inheritdoc/>
    /// <para>🔴 <b>"ABC-01 of Widget", not "ABC-01".</b> A batch number is unique <i>per item</i>, not per
    /// company — <c>BatchService.CreateBatch</c> says so in its own duplicate message — so two rows in this very
    /// list can legitimately both read "ABC-01". A confirmation naming only the number would not tell the
    /// operator which item's batch is about to go.</para></summary>
    public string MasterName => $"{BatchNumber} of {Item}";

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// A "Godown" picker option where a blank first entry means "unspecified" (<see cref="Godown"/> is null).
/// </summary>
public sealed class OptionalGodownOption
{
    public Godown? Godown { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Godown is null;
}

/// <summary>An expiry-period unit picker option (label + the enum value).</summary>
public sealed class ExpiryPeriodUnitOption
{
    public ExpiryPeriodUnit Unit { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The Batch / Lot creation master ("Masters → Create → Inventory Masters → Batch", Phase 6 Cluster 1;
/// requirements RQ-1/RQ-4/RQ-54). A first-class <see cref="BatchMaster"/> per (stock item, batch number):
/// a required Batch/Lot number, a required <b>Item</b> (only items that Maintain-in-Batches are offered),
/// an optional inward-layer <b>Godown</b>, an optional <b>Mfg date</b>, an <b>Expiry</b> entered either as an
/// absolute date OR a period (e.g. "12 Months") that the engine resolves from the mfg date (RQ-4), and an
/// optional per-batch <b>opening inward</b> cost layer (Qty + Rate, paisa-exact, RQ-6/DP-8).
///
/// <para>Batch numbers are unique <b>within an item</b> (RQ-1): a duplicate for the same item is surfaced as a
/// friendly error (the engine's own guard is the backstop). This whole screen is gated by the company
/// <see cref="Company.MaintainBatchwiseDetails"/> flag (RQ-52) and shows a friendly hint when it is off.</para>
///
/// <para>Pre-validates the opening rate to the paisa (2 dp) and quantity to 6 dp, the dates, and the
/// mutually-exclusive expiry-date vs expiry-period, BEFORE calling <see cref="BatchService.CreateBatch"/>,
/// then wraps the engine call in try/catch and surfaces any domain error to <see cref="Message"/> so nothing
/// crashes the UI.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="GodownMasterViewModel"/> / <see cref="StockItemMasterViewModel"/>.</para>
/// </summary>
public sealed partial class BatchMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 3.8): the shared master-list arm

    /// <inheritdoc/>
    public string MasterKindLabel => "batch";

    /// <inheritdoc/>
    /// <remarks>Create-only screen — no <c>ForAlter</c> factory exists for a batch — so it is never
    /// mid-alteration.</remarks>
    public bool IsAltering => false;

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>BatchService.DeleteBatch</c>'s own message: a batch whose number appears on any opening balance,
    /// inventory-voucher allocation, item-invoice line or physical-count line for the same item is refused, so
    /// no stock movement is left naming a batch that no longer exists.</remarks>
    public void DeleteMaster(Guid id) => new BatchService(_company).DeleteBatch(id);

    private PayrollMasterHighlight<BatchListRow>? _highlight;

    private PayrollMasterHighlight<BatchListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<BatchListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing batch, or null.</summary>
    public BatchListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Batches",
        new[]
        {
            MasterListColumn.Text("Batch / Lot"),
            MasterListColumn.Text("Item"),
            MasterListColumn.Text("Godown"),
            MasterListColumn.Text("Mfg Date"),
            MasterListColumn.Text("Expiry"),
            MasterListColumn.Number("Opening Value"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.BatchNumber, r.Item, r.Godown, r.MfgDate, r.Expiry, r.OpeningValue,
        }).ToList());

    /// <summary>The batch-tracked items an item can belong to (only items that Maintain-in-Batches).</summary>
    public ObservableCollection<StockItem> Items { get; } = new();

    /// <summary>The godown options: "(unspecified)" plus every godown (optional inward-layer location).</summary>
    public ObservableCollection<OptionalGodownOption> GodownOptions { get; } = new();

    /// <summary>The expiry-period unit options (Days / Weeks / Months / Years).</summary>
    public ObservableCollection<ExpiryPeriodUnitOption> ExpiryUnits { get; } = new();

    /// <summary>The existing batches, refreshed after each create.</summary>
    public ObservableCollection<BatchListRow> Existing { get; } = new();

    [ObservableProperty] private string _batchNumber = string.Empty;
    [ObservableProperty] private StockItem? _selectedItem;
    [ObservableProperty] private OptionalGodownOption? _selectedGodown;
    [ObservableProperty] private string _manufacturingDateText = string.Empty;

    /// <summary>Expiry entered as an absolute date (blank ⇒ use the period fields, if any).</summary>
    [ObservableProperty] private string _expiryDateText = string.Empty;

    /// <summary>Expiry entered as a period count (blank ⇒ use the absolute date, if any).</summary>
    [ObservableProperty] private string _expiryPeriodCountText = string.Empty;
    [ObservableProperty] private ExpiryPeriodUnitOption? _expiryPeriodUnit;

    [ObservableProperty] private string _openingQuantityText = string.Empty;
    [ObservableProperty] private string _openingRateText = string.Empty;

    [ObservableProperty] private string? _message;

    public BatchMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        ExpiryUnits.Add(new ExpiryPeriodUnitOption { Unit = Apex.Ledger.Domain.ExpiryPeriodUnit.Days, Display = "Days" });
        ExpiryUnits.Add(new ExpiryPeriodUnitOption { Unit = Apex.Ledger.Domain.ExpiryPeriodUnit.Weeks, Display = "Weeks" });
        ExpiryUnits.Add(new ExpiryPeriodUnitOption { Unit = Apex.Ledger.Domain.ExpiryPeriodUnit.Months, Display = "Months" });
        ExpiryUnits.Add(new ExpiryPeriodUnitOption { Unit = Apex.Ledger.Domain.ExpiryPeriodUnit.Years, Display = "Years" });
        ExpiryPeriodUnit = ExpiryUnits.First(u => u.Unit == Apex.Ledger.Domain.ExpiryPeriodUnit.Months);

        RefreshPickers();
        RefreshList();
    }

    /// <summary>True once at least one item that Maintains-in-Batches exists — a batch needs one to belong to.</summary>
    public bool CanCreate => Items.Count > 0;

    /// <summary>
    /// Ctrl+A create: validates the batch number + item, pre-validates the dates / period and any opening
    /// quantity (6 dp) + rate (paisa/2 dp), then creates the batch via <see cref="BatchService.CreateBatch"/>
    /// and persists. Any domain error (including a per-item duplicate) is surfaced to <see cref="Message"/>
    /// without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var number = (BatchNumber ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(number))
        {
            Message = "A batch / lot number is required.";
            return false;
        }
        if (SelectedItem is null)
        {
            Message = "Pick a batch-tracked item (turn on 'Maintain in Batches' on a Stock Item first).";
            return false;
        }

        // Per-item uniqueness (RQ-1) — surface a friendly message before the engine guard fires.
        if (_company.FindBatchByNumber(SelectedItem.Id, number) is not null)
        {
            Message = $"A batch '{number}' already exists for item '{SelectedItem.Name}' " +
                      "(batch numbers are unique per item).";
            return false;
        }

        DateOnly? mfg = null;
        if (!string.IsNullOrWhiteSpace(ManufacturingDateText))
        {
            if (!TryParseDate(ManufacturingDateText, out var m))
            {
                Message = "Manufacturing date must be a valid date (dd-MMM-yyyy).";
                return false;
            }
            mfg = m;
        }

        // Expiry: an absolute date OR a period — never both.
        DateOnly? expiryDate = null;
        ExpiryPeriod? expiryPeriod = null;
        var hasDate = !string.IsNullOrWhiteSpace(ExpiryDateText);
        var hasPeriod = !string.IsNullOrWhiteSpace(ExpiryPeriodCountText);
        if (hasDate && hasPeriod)
        {
            Message = "Enter the expiry as EITHER an absolute date OR a period, not both.";
            return false;
        }
        if (hasDate)
        {
            if (!TryParseDate(ExpiryDateText, out var e))
            {
                Message = "Expiry date must be a valid date (dd-MMM-yyyy).";
                return false;
            }
            expiryDate = e;
        }
        else if (hasPeriod)
        {
            if (!int.TryParse(ExpiryPeriodCountText.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                Message = "Expiry period count must be a whole number greater than zero.";
                return false;
            }
            if (mfg is null)
            {
                Message = "A manufacturing date is required to resolve a relative expiry period.";
                return false;
            }
            expiryPeriod = new ExpiryPeriod(count, (ExpiryPeriodUnit ?? ExpiryUnits.First()).Unit);
        }

        // Optional opening inward cost layer (Qty + Rate). Both must be present together or both absent.
        var wantsOpening = !string.IsNullOrWhiteSpace(OpeningQuantityText)
                           || !string.IsNullOrWhiteSpace(OpeningRateText);
        decimal? inwardQty = null;
        Money? inwardRate = null;
        if (wantsOpening)
        {
            if (!TryParseQuantity(OpeningQuantityText, "Opening quantity", out var qty) || qty < 0m)
            {
                if (Message is null) Message = "Opening quantity must be a number ≥ 0.";
                return false;
            }
            if (!TryParseRate(OpeningRateText, out var rate) || rate < 0m)
            {
                Message = "Opening rate must be a number ≥ 0 (₹ per unit).";
                return false;
            }
            var money = Money.FromRupees(rate);
            if (!money.IsPaisaExact)
            {
                Message = $"Opening rate {rate} must be to the paisa (2 decimal places).";
                return false;
            }
            inwardQty = qty;
            inwardRate = money;
        }

        var godownId = SelectedGodown?.Godown?.Id;

        try
        {
            var service = new BatchService(_company);
            service.CreateBatch(SelectedItem.Id, number, mfg, expiryDate, expiryPeriod,
                godownId, inwardQty, inwardRate);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Batch '{number}' created for {SelectedItem.Name}.";
        BatchNumber = string.Empty;
        ManufacturingDateText = string.Empty;
        ExpiryDateText = string.Empty;
        ExpiryPeriodCountText = string.Empty;
        OpeningQuantityText = string.Empty;
        OpeningRateText = string.Empty;
        _onChanged();
        return true;
    }

    // ---- parsing helpers ----

    // WI-5: the ONE app-wide day-first parser (was a bare InvariantCulture parse — the MM/dd misread).
    private static bool TryParseDate(string? text, out DateOnly value) => ApexDate.TryParse(text, out value);

    private bool TryParseQuantity(string? text, string label, out decimal value)
    {
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value))
        {
            Message = $"{label} must be a number.";
            return false;
        }
        if (!Quantities.IsWithinPrecision(value))
        {
            Message = $"{label} {value} must be to {Quantities.DecimalPlaces} decimal places.";
            return false;
        }
        return true;
    }

    private static bool TryParseRate(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);

    // ---- refresh ----

    private void RefreshPickers()
    {
        var itemId = SelectedItem?.Id;
        Items.Clear();
        foreach (var i in _company.StockItems
                     .Where(i => i.MaintainInBatches)
                     .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(i);
        SelectedItem = Items.FirstOrDefault(i => i.Id == itemId) ?? Items.FirstOrDefault();

        var godownId = SelectedGodown?.Godown?.Id;
        GodownOptions.Clear();
        GodownOptions.Add(new OptionalGodownOption { Godown = null, Display = "◦ (unspecified)" });
        foreach (var g in _company.Godowns.OrderByDescending(g => g.IsMainLocation)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            GodownOptions.Add(new OptionalGodownOption { Godown = g, Display = g.Name });
        SelectedGodown = GodownOptions.FirstOrDefault(o => o.Godown?.Id == godownId)
                         ?? GodownOptions.FirstOrDefault();

        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        RefreshPickers();
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var b in _company.BatchMasters
                     .OrderBy(b => _company.FindStockItem(b.StockItemId)?.Name ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(b => b.BatchNumber, StringComparer.OrdinalIgnoreCase))
        {
            var item = _company.FindStockItem(b.StockItemId);
            var godown = b.GodownId is { } gid ? _company.FindGodown(gid)?.Name ?? "—" : "—";
            var expiry = b.ResolvedExpiryDate is { } e
                ? ApexDate.Format(e)
                  + (b.ExpiryPeriod is { } p ? $" ({p.RawText})" : string.Empty)
                : "—";
            var openingValue = b.InwardQuantity is { } iq && b.InwardRate is { } ir && iq > 0m
                // Through IndianMoneyFormat.Amount, not a second copy of "#,##0.00" + the culture: that pairing is
                // the one grouping rule (drift lock D2), and a hand-rolled copy of it is how the two drifted apart.
                ? "₹" + Apex.Ledger.IndianMoneyFormat.Amount(Money.ForexBase(ir, iq))
                : "—";
            Existing.Add(new BatchListRow
            {
                MasterId = b.Id,
                BatchNumber = b.BatchNumber,
                Item = item?.Name ?? "—",
                Godown = godown,
                MfgDate = b.ManufacturingDate is { } m
                    ? ApexDate.Format(m)
                    : "—",
                Expiry = expiry,
                OpeningValue = openingValue,
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
