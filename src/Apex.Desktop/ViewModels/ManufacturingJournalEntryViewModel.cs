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

/// <summary>A read-only auto-scaled component-consumption row shown in the Manufacturing Journal breakdown (RQ-12).</summary>
public sealed class ManufacturingConsumptionRow
{
    public string Item { get; init; } = string.Empty;
    public string Batch { get; init; } = string.Empty;
    public string Quantity { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>A read-only carve-out (by-product/co-product/scrap) row shown in the breakdown (RQ-13/DP-3).</summary>
public sealed class ManufacturingCarveOutRow
{
    public string Item { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Quantity { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// One additional-cost line on the Manufacturing Journal (RQ-13): a labour/overhead/freight charge that ADDS to
/// the finished good's stock value. Repeatable. Parsing/validation is deferred to the parent VM.
/// </summary>
public sealed partial class ManufacturingAdditionalCostRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;

    public ManufacturingAdditionalCostRowViewModel(Action onChanged)
        => _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    partial void OnNameChanged(string value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();

    /// <summary>True once the row has been touched at all — a wholly blank trailing row is ignored.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(AmountText);
}

/// <summary>
/// A BOM picker option: the BOM name plus a short recipe hint (unit-of-manufacture + component count).
/// </summary>
public sealed class BomOption
{
    public BillOfMaterials Bom { get; init; } = null!;
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Manufacturing Journal</b> voucher-entry screen (Phase 6 Cluster 2; requirements
/// RQ-11/RQ-12/RQ-13/RQ-15/RQ-53). Pick the <b>finished good</b> + one of its <b>BOMs</b>, enter the output
/// <b>quantity</b> + <b>date</b> + <b>consumption</b>/<b>production</b> godowns, and optionally add
/// <b>additional-cost</b> lines (labour/overhead/freight). On every change it asks the engine for a
/// <b>read-only preview</b> of the manufacture (<see cref="ManufacturingJournalService.PreviewManufacture"/>)
/// and displays the engine's figures — auto-scaled component consumption (RQ-12), Component Cost, Additional
/// Cost, Carve-outs, and the Finished-Good Value + unit rate (RQ-13). On <see cref="Accept"/> it posts through
/// <see cref="ManufacturingJournalService.Manufacture"/> (engine-routed, so it flows into Stock Summary, RQ-15).
///
/// <para><b>ER-4 — one projection.</b> The screen NEVER recomputes valuation: every figure comes from the same
/// <see cref="ManufacturingResult"/> the engine posts (the preview shares the exact costing code with the post).
/// A batch-tracked component is issued FEFO/FIFO per-lot by the engine (RQ-13/ER-5) — the preview shows the
/// per-lot consumption rows.</para>
///
/// <para>If no Manufacturing-Journal voucher type exists yet the screen creates one via
/// <see cref="ManufacturingJournalService.CreateManufacturingJournalType"/> (Alt+F7, RQ-11) and persists it.
/// MVVM boundary: references the engine + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class ManufacturingJournalEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly ManufacturingJournalService _service;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;

    /// <summary>The Manufacturing-Journal voucher type this screen posts through (Alt+F7).</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header.</summary>
    public string TypeName => _type.Name;

    /// <summary>The finished-good items with at least one BOM (the manufacturable items).</summary>
    public ObservableCollection<StockItem> FinishedGoods { get; } = new();

    /// <summary>The BOMs of the selected finished good (multiple per item are supported, RQ-9).</summary>
    public ObservableCollection<BomOption> Boms { get; } = new();

    /// <summary>The godown options for the consumption/production pickers.</summary>
    public ObservableCollection<Godown> Godowns { get; } = new();

    /// <summary>The repeatable additional-cost lines (always one blank trailing row).</summary>
    public ObservableCollection<ManufacturingAdditionalCostRowViewModel> AdditionalCosts { get; } = new();

    // ---- read-only engine breakdown (RQ-12/RQ-13) ----

    /// <summary>The auto-scaled per-component consumption rows from the engine preview (read-only, RQ-12).</summary>
    public ObservableCollection<ManufacturingConsumptionRow> Consumption { get; } = new();

    /// <summary>The carved-out by-product/co-product/scrap rows from the engine preview (read-only, RQ-13/DP-3).</summary>
    public ObservableCollection<ManufacturingCarveOutRow> CarveOuts { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private StockItem? _selectedFinishedGood;
    [ObservableProperty] private BomOption? _selectedBom;
    [ObservableProperty] private string _quantityText = "1";
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private Godown? _consumptionGodown;
    [ObservableProperty] private Godown? _productionGodown;

    // The engine breakdown figures (formatted; all from the last successful preview) — ER-4.
    [ObservableProperty] private string _componentCostText = "₹0.00";
    [ObservableProperty] private string _additionalCostText = "₹0.00";
    [ObservableProperty] private string _carveOutText = "₹0.00";
    [ObservableProperty] private string _finishedGoodValueText = "₹0.00";
    [ObservableProperty] private string _finishedGoodUnitRateText = "₹0.00";

    /// <summary>True once a valid preview has been computed (drives the Accept gate + shows the breakdown).</summary>
    [ObservableProperty] private bool _hasPreview;

    /// <summary>Error/status surfaced under the form (preview failure, blank inputs, posting rejection).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    private bool _seeding;

    /// <summary>The date as editable text (dd-MMM-yyyy) for the header TextBox (parsed on change via <see cref="Date"/>).</summary>
    public string DateText
    {
        get => Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        set
        {
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                && parsed != Date)
                Date = parsed;
        }
    }

    public ManufacturingJournalEntryViewModel(
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
        _service = new ManufacturingJournalService(company);

        _seeding = true;

        foreach (var i in company.StockItems
                     .Where(i => company.BomsFor(i.Id).Any())
                     .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            FinishedGoods.Add(i);

        foreach (var g in company.Godowns.OrderByDescending(g => g.IsMainLocation)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Godowns.Add(g);

        // Default date: last inventory-voucher date, else last accounting date, else books-begin.
        DateOnly? last = null;
        foreach (var v in company.InventoryVouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        Date = date ?? last ?? company.BooksBeginFrom;

        var mainLocation = Godowns.FirstOrDefault(g => g.IsMainLocation) ?? Godowns.FirstOrDefault();
        ConsumptionGodown = mainLocation;
        ProductionGodown = mainLocation;
        SelectedFinishedGood = FinishedGoods.FirstOrDefault();
        RebuildBoms();

        AddBlankAdditionalCost();
        Title = $"{type.Name} Voucher";

        _seeding = false;
        RefreshPreview();
    }

    /// <summary>True once at least one item carries a BOM and a godown exists — a manufacture needs both.</summary>
    public bool CanEnter => FinishedGoods.Count > 0 && Godowns.Count > 0;

    partial void OnSelectedFinishedGoodChanged(StockItem? value)
    {
        if (_seeding) return;
        RebuildBoms();
        RefreshPreview();
    }

    partial void OnSelectedBomChanged(BomOption? value) { if (!_seeding) RefreshPreview(); }
    partial void OnQuantityTextChanged(string value) { if (!_seeding) RefreshPreview(); }
    partial void OnDateChanged(DateOnly value) { if (!_seeding) RefreshPreview(); OnPropertyChanged(nameof(DateText)); }
    partial void OnConsumptionGodownChanged(Godown? value) { if (!_seeding) RefreshPreview(); }
    partial void OnProductionGodownChanged(Godown? value) { if (!_seeding) RefreshPreview(); }

    /// <summary>Rebuilds the BOM picker for the selected finished good (multiple BOMs per item, RQ-9).</summary>
    private void RebuildBoms()
    {
        Boms.Clear();
        if (SelectedFinishedGood is null) { SelectedBom = null; return; }
        foreach (var bom in _company.BomsFor(SelectedFinishedGood.Id)
                     .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var componentCount = bom.ComponentLines.Count();
            var hint = $"  (per {bom.UnitOfManufacture.ToString("0.######", CultureInfo.InvariantCulture)}, " +
                       $"{componentCount} component{(componentCount == 1 ? "" : "s")})";
            Boms.Add(new BomOption { Bom = bom, Display = bom.Name + hint });
        }
        SelectedBom = Boms.FirstOrDefault();
    }

    /// <summary>Adds a blank additional-cost line; keeps one trailing blank row.</summary>
    public ManufacturingAdditionalCostRowViewModel AddBlankAdditionalCost()
    {
        var row = new ManufacturingAdditionalCostRowViewModel(OnAdditionalCostChanged);
        AdditionalCosts.Add(row);
        return row;
    }

    private void OnAdditionalCostChanged()
    {
        if (AdditionalCosts.Count == 0 || !AdditionalCosts[^1].IsBlank)
            AddBlankAdditionalCost();
        if (!_seeding) RefreshPreview();
    }

    /// <summary>
    /// Asks the engine for a read-only preview of the manufacture and mirrors its figures into the breakdown
    /// (RQ-12/RQ-13, ER-4). Any invalid input / engine rejection clears the preview and surfaces a friendly
    /// message; a valid preview enables Accept. Never posts or persists.
    /// </summary>
    private void RefreshPreview()
    {
        Message = null;
        HasPreview = false;
        Consumption.Clear();
        CarveOuts.Clear();
        ResetBreakdownText();

        if (SelectedBom is not { } bomOption)
        {
            Message = FinishedGoods.Count == 0
                ? "No finished good has a BOM yet — create a Bill of Materials first."
                : "Pick a BOM to manufacture.";
            return;
        }
        if (ConsumptionGodown is null || ProductionGodown is null)
        {
            Message = "Pick a consumption godown and a production godown.";
            return;
        }
        if (!decimal.TryParse((QuantityText ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var qty) || qty <= 0m)
        {
            Message = "Enter an output quantity greater than zero.";
            return;
        }
        if (!Quantities.IsWithinPrecision(qty))
        {
            Message = $"Output quantity {qty} must be to {Quantities.DecimalPlaces} decimal places.";
            return;
        }

        if (!TryBuildAdditionalCosts(out var additionalCosts))
            return;   // Message already set

        ManufacturingResult preview;
        try
        {
            preview = _service.PreviewManufacture(
                _type.Id, bomOption.Bom.Id, qty, Date,
                ConsumptionGodown.Id, ProductionGodown.Id, additionalCosts);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return;
        }

        // ---- consumption rows: the source (outward) allocations are the auto-scaled component draws (RQ-12) ----
        foreach (var alloc in preview.Voucher.Allocations)
        {
            var item = _company.FindStockItem(alloc.StockItemId);
            var unit = item is not null ? _company.FindUnit(item.BaseUnitId)?.Symbol ?? string.Empty : string.Empty;
            var rate = alloc.Rate ?? Money.Zero;
            Consumption.Add(new ManufacturingConsumptionRow
            {
                Item = item?.Name ?? "?",
                Batch = string.IsNullOrWhiteSpace(alloc.BatchLabel) ? "—" : alloc.BatchLabel!,
                Quantity = alloc.Quantity.ToString("0.######", CultureInfo.InvariantCulture) +
                           (string.IsNullOrEmpty(unit) ? "" : " " + unit),
                Rate = Rupees(rate),
                Value = Rupees(Money.ForexBase(rate, alloc.Quantity)),
            });
        }

        // ---- carve-out rows: destination inward lines that are NOT the finished good (RQ-13/DP-3) ----
        foreach (var alloc in preview.Voucher.DestinationAllocations
                     .Where(a => a.StockItemId != bomOption.Bom.StockItemId))
        {
            var item = _company.FindStockItem(alloc.StockItemId);
            var unit = item is not null ? _company.FindUnit(item.BaseUnitId)?.Symbol ?? string.Empty : string.Empty;
            var rate = alloc.Rate ?? Money.Zero;
            var kind = bomOption.Bom.CarveOutLines
                .FirstOrDefault(l => l.ComponentStockItemId == alloc.StockItemId)?.LineType ?? BomLineType.ByProduct;
            CarveOuts.Add(new ManufacturingCarveOutRow
            {
                Item = item?.Name ?? "?",
                Kind = CarveOutLabel(kind),
                Quantity = alloc.Quantity.ToString("0.######", CultureInfo.InvariantCulture) +
                           (string.IsNullOrEmpty(unit) ? "" : " " + unit),
                Value = Rupees(Money.ForexBase(rate, alloc.Quantity)),
            });
        }

        ComponentCostText = Rupees(preview.ComponentCostTotal);
        AdditionalCostText = Rupees(preview.AdditionalCostTotal);
        CarveOutText = Rupees(preview.CarveOutTotal);
        FinishedGoodValueText = Rupees(preview.FinishedGoodValue);
        var fgUnit = SelectedFinishedGood is not null
            ? _company.FindUnit(SelectedFinishedGood.BaseUnitId)?.Symbol ?? string.Empty : string.Empty;
        FinishedGoodUnitRateText = Rupees(preview.FinishedGoodUnitRate) +
                                   (string.IsNullOrEmpty(fgUnit) ? "" : " / " + fgUnit);
        HasPreview = true;
    }

    private void ResetBreakdownText()
    {
        ComponentCostText = "₹0.00";
        AdditionalCostText = "₹0.00";
        CarveOutText = "₹0.00";
        FinishedGoodValueText = "₹0.00";
        FinishedGoodUnitRateText = "₹0.00";
    }

    /// <summary>Parses the additional-cost rows into engine records; sets <see cref="Message"/> and returns false on a bad amount.</summary>
    private bool TryBuildAdditionalCosts(out List<ManufacturingAdditionalCost> costs)
    {
        costs = new List<ManufacturingAdditionalCost>();
        foreach (var row in AdditionalCosts.Where(r => !r.IsBlank))
        {
            var name = (row.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                Message = "Every additional-cost line needs a name.";
                return false;
            }
            if (!decimal.TryParse((row.AmountText ?? string.Empty).Trim(),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var amt) || amt < 0m)
            {
                Message = $"Additional cost '{name}' must be a number ≥ 0.";
                return false;
            }
            var money = Money.FromRupees(amt);
            if (!money.IsPaisaExact)
            {
                Message = $"Additional cost '{name}' {amt} must be to the paisa (2 decimal places).";
                return false;
            }
            costs.Add(new ManufacturingAdditionalCost(name, money));
        }
        return true;
    }

    /// <summary>
    /// Ctrl+A accept: re-validates, then posts the manufacture through
    /// <see cref="ManufacturingJournalService.Manufacture"/> (engine-routed — it consumes scaled components,
    /// values + produces the finished good, flows into Stock Summary, RQ-15) and saves the company. Any domain
    /// error (e.g. a no-negative-stock short on components) is surfaced to <see cref="Message"/> without crashing.
    /// On success surfaces the assigned number and returns to the Gateway.
    /// </summary>
    public bool Accept()
    {
        RefreshPreview();
        if (!HasPreview)
        {
            if (Message is null) Message = "Complete the manufacture details before accepting.";
            return false;
        }

        var qty = decimal.Parse((QuantityText ?? "0").Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);
        if (!TryBuildAdditionalCosts(out var additionalCosts)) return false;

        try
        {
            var result = _service.Manufacture(
                _type.Id, SelectedBom!.Bom.Id, qty, Date,
                ConsumptionGodown!.Id, ProductionGodown!.Id, additionalCosts);
            _storage.Save(_company);
            SavedNumber = result.Voucher.Number;
            Message = $"{_type.Name} No. {result.Voucher.Number} accepted — " +
                      $"{qty.ToString("0.######", CultureInfo.InvariantCulture)} × {SelectedFinishedGood!.Name} " +
                      $"valued {Rupees(result.FinishedGoodValue)}.";
            _onSaved();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    /// <summary>Esc / Alt+X cancel: discards the in-progress voucher and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();

    private static string Rupees(Money m) =>
        "₹" + m.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string CarveOutLabel(BomLineType type) => type switch
    {
        BomLineType.ByProduct => "By-Product",
        BomLineType.CoProduct => "Co-Product",
        BomLineType.Scrap => "Scrap",
        _ => "Component",
    };
}
