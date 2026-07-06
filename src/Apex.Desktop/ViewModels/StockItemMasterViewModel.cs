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

/// <summary>A stock-item row for the existing-items list on the master screen.</summary>
public sealed class StockItemListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Valuation { get; init; } = string.Empty;
    public string OpeningValue { get; init; } = string.Empty;
}

/// <summary>
/// One option in a "Category" / "Godown" picker where a blank first entry means "none / default".
/// <see cref="Category"/> is null for the "(none)" option.
/// </summary>
public sealed class OptionalStockCategoryOption
{
    public StockCategory? Category { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Category is null;
}

/// <summary>A valuation-method option for the picker (label + the enum value).</summary>
public sealed class ValuationMethodOption
{
    public StockValuationMethod Method { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A GST taxability option for the picker (label + the enum value).</summary>
public sealed class GstTaxabilityOption
{
    public GstTaxability Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// A GST rate-slab option for the item's rate picker: a seeded slab (0/5/18/40%) or the "(none)" entry
/// that leaves the item's rate unresolved (resolved from the sales/purchase ledger or company instead).
/// </summary>
public sealed class GstRateOption
{
    /// <summary>The integrated rate in basis points, or null for "(none) — unresolved here".</summary>
    public int? RateBasisPoints { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => RateBasisPoints is null;
}

/// <summary>
/// The Stock-Item creation master ("Masters → Create → Inventory Masters → Stock Item", catalog §9; RQ-6).
/// Captures a name + optional alias, a required <b>Under</b> stock group, an optional <b>Category</b>, a
/// required base <b>Unit</b>, GST placeholders (HSN/SAC + Taxable), a <b>Valuation method</b> (default
/// Average Cost, DP-1), and simple reorder fields (reorder level + minimum order qty). It also captures a
/// simple <b>Opening Balance</b> (all optional; if a quantity is entered): Godown (default Main Location),
/// Quantity, Rate, and an optional Batch/Lot label — wired to <see cref="InventoryService.AddOpeningBalance"/>.
///
/// <para>Pre-validates: opening-balance <b>Rate to the paisa</b> (2 dp), <b>Quantity / Reorder / MOQ to 6
/// dp</b>, and required fields present, BEFORE calling the engine — then wraps the engine calls in try/catch
/// and surfaces any domain error to <see cref="Message"/> so nothing crashes the UI. When the item is
/// created but a later opening-balance step fails, the item still persists and the message explains the skip.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class StockItemMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    /// <remarks>Stock-item export normally uses the bespoke
    /// <see cref="Services.MasterListTabularProjector.ProjectStockItems"/>; this snapshot is the generic-path
    /// equivalent (identical columns, Opening Value as a numeric cell).</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Stock Items",
        new[]
        {
            MasterListColumn.Text("Name"),
            MasterListColumn.Text("Under"),
            MasterListColumn.Text("Unit"),
            MasterListColumn.Text("Valuation"),
            MasterListColumn.Number("Opening Value"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Name, r.Under, r.Unit, r.Valuation, r.OpeningValue,
        }).ToList());

    /// <summary>The stock groups an item can sit under (required — the item needs one).</summary>
    public ObservableCollection<StockGroup> Groups { get; } = new();

    /// <summary>The category options: "(none)" plus every existing category (optional axis).</summary>
    public ObservableCollection<OptionalStockCategoryOption> CategoryOptions { get; } = new();

    /// <summary>The units an item can be measured in (required base unit).</summary>
    public ObservableCollection<Unit> Units { get; } = new();

    /// <summary>The godowns the opening balance can sit in (defaults to Main Location).</summary>
    public ObservableCollection<Godown> Godowns { get; } = new();

    /// <summary>The valuation methods offered (Average Cost first / default, DP-1).</summary>
    public ObservableCollection<ValuationMethodOption> ValuationMethods { get; } = new();

    /// <summary>The existing stock items, refreshed after each create.</summary>
    public ObservableCollection<StockItemListRow> Existing { get; } = new();

    // ---- Item identity ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private StockGroup? _selectedGroup;
    [ObservableProperty] private OptionalStockCategoryOption? _selectedCategory;
    [ObservableProperty] private Unit? _selectedUnit;
    [ObservableProperty] private ValuationMethodOption? _selectedValuation;
    [ObservableProperty] private string _hsnSacCode = string.Empty;
    [ObservableProperty] private bool _isTaxable;
    [ObservableProperty] private string _reorderLevelText = string.Empty;
    [ObservableProperty] private string _minimumOrderQtyText = string.Empty;

    // ---- Batch switches (Phase 6 Cluster 1; RQ-2) — only offered when the company flag is on ----
    [ObservableProperty] private bool _maintainInBatches;
    [ObservableProperty] private bool _trackManufacturingDate;
    [ObservableProperty] private bool _useExpiryDates;

    /// <summary>
    /// True iff the company flag <see cref="Company.MaintainBatchwiseDetails"/> is on (RQ-52) — the three item
    /// batch switches (<see cref="MaintainInBatches"/> / <see cref="TrackManufacturingDate"/> /
    /// <see cref="UseExpiryDates"/>) are surfaced only then, per the F11/F12 config-driven visibility model.
    /// </summary>
    public bool ShowBatchSwitches => _company.MaintainBatchwiseDetails;

    // ---- GST details (catalog §12; phase4 RQ-8) — only offered when GST is enabled ----
    [ObservableProperty] private GstTaxabilityOption? _taxability;
    [ObservableProperty] private GstRateOption? _gstRate;

    /// <summary>The GST taxability options (Taxable / Exempt / Nil-Rated / Non-GST).</summary>
    public ObservableCollection<GstTaxabilityOption> Taxabilities { get; } = new();

    /// <summary>The GST rate options: "(none)" plus the company's seeded slabs (0/5/18/40%).</summary>
    public ObservableCollection<GstRateOption> GstRates { get; } = new();

    /// <summary>True iff GST is enabled for the company — the item-GST sub-form is only offered then.</summary>
    public bool GstEnabled => _company.GstEnabled;

    // ---- Opening balance (all optional) ----
    [ObservableProperty] private Godown? _openingGodown;
    [ObservableProperty] private string _openingQuantityText = string.Empty;
    [ObservableProperty] private string _openingRateText = string.Empty;
    [ObservableProperty] private string _openingBatchLabel = string.Empty;

    [ObservableProperty] private string? _message;

    public StockItemMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.AverageCost, Display = "Average Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.Fifo, Display = "FIFO" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.Lifo, Display = "LIFO" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.StandardCost, Display = "Standard Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.LastPurchaseCost, Display = "Last Purchase Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.LastSaleCost, Display = "Last Sale Cost" });
        SelectedValuation = ValuationMethods.First();

        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.Taxable, Display = "Taxable" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.Exempt, Display = "Exempt" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.NilRated, Display = "Nil-Rated" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.NonGst, Display = "Non-GST" });
        Taxability = Taxabilities.First();

        GstRates.Add(new GstRateOption { RateBasisPoints = null, Display = "◦ (none)" });
        var slabs = _company.Gst?.RateSlabs ?? Array.Empty<GstRateSlab>();
        foreach (var slab in slabs.OrderBy(s => s.RateBasisPoints))
            GstRates.Add(new GstRateOption { RateBasisPoints = slab.RateBasisPoints, Display = slab.Label });
        GstRate = GstRates.First();

        RefreshPickers();
        RefreshList();
    }

    /// <summary>True once at least one stock group AND one unit exist — an item needs both to be created.</summary>
    public bool CanCreate => Groups.Count > 0 && Units.Count > 0;

    /// <summary>
    /// Ctrl+A create: validates the required name/group/unit, pre-validates reorder/MOQ to 6 dp and any
    /// opening-balance quantity (6 dp) + rate (paisa/2 dp), then creates the item via the engine and
    /// persists. If an opening quantity is entered, adds the opening allocation too. Any domain error is
    /// surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A stock item name is required.";
            return false;
        }
        if (SelectedGroup is null)
        {
            Message = "Pick a stock group to place the item under (create a Stock Group first).";
            return false;
        }
        if (SelectedUnit is null)
        {
            Message = "Pick a base unit for the item (create a Unit first).";
            return false;
        }

        // Pre-validate reorder level + minimum order qty (6 dp) so the engine's domain error never fires.
        if (!TryParseOptionalQuantity(ReorderLevelText, "Reorder level", out var reorderLevel))
            return false;
        if (!TryParseOptionalQuantity(MinimumOrderQtyText, "Minimum order quantity", out var minimumOrderQty))
            return false;

        // Pre-validate the opening balance (only if a quantity was entered).
        var wantsOpening = !string.IsNullOrWhiteSpace(OpeningQuantityText);
        decimal openingQty = 0m;
        Money openingRate = Money.Zero;
        if (wantsOpening)
        {
            if (!TryParseQuantity(OpeningQuantityText, "Opening quantity", out openingQty) || openingQty < 0m)
            {
                if (Message is null) Message = "Opening quantity must be a number ≥ 0.";
                return false;
            }
            if (OpeningGodown is null)
            {
                Message = "Pick a godown for the opening stock (default: Main Location).";
                return false;
            }
            if (!TryParseRate(OpeningRateText, out var rate) || rate < 0m)
            {
                Message = "Opening rate must be a number ≥ 0 (₹ per unit).";
                return false;
            }
            openingRate = Money.FromRupees(rate);
            if (!openingRate.IsPaisaExact)
            {
                Message = $"Opening stock rate {rate} must be to the paisa (2 decimal places).";
                return false;
            }
        }

        var categoryId = SelectedCategory?.Category?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var hsn = string.IsNullOrWhiteSpace(HsnSacCode) ? null : HsnSacCode.Trim();
        var valuation = SelectedValuation?.Method ?? StockValuationMethod.AverageCost;

        // GST details (only offered when GST is enabled). Pre-validate the HSN length + taxable/rate pairing
        // BEFORE the engine so a bad value is a friendly message, not a crash.
        StockItemGstDetails? gstBlock = null;
        var isTaxableFlag = IsTaxable;
        if (GstEnabled)
        {
            var taxability = (Taxability ?? Taxabilities.First()).Value;
            var rateBp = GstRate?.RateBasisPoints;

            if (hsn is not null && (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit)))
            {
                Message = $"HSN/SAC '{hsn}' must be 4, 6 or 8 digits (numeric).";
                return false;
            }
            // A non-taxable item carries no positive rate (the engine rejects that pairing).
            if (taxability != GstTaxability.Taxable) rateBp = null;

            gstBlock = new StockItemGstDetails
            {
                HsnSac = hsn,
                Taxability = taxability,
                RateBasisPoints = rateBp,
                SupplyType = GstSupplyType.Goods,
            };
            isTaxableFlag = taxability == GstTaxability.Taxable; // keep the Phase-3 placeholder consistent
        }

        StockItem item;
        try
        {
            var service = new InventoryService(_company);
            item = service.CreateStockItem(name, SelectedGroup.Id, SelectedUnit.Id, categoryId, alias,
                valuation, hsn, isTaxableFlag, reorderLevel, minimumOrderQty);
            if (gstBlock is not null)
            {
                gstBlock.EnsureValid();  // backstop; already pre-validated above
                item.Gst = gstBlock;
            }

            // Batch switches (RQ-2) — captured only when the company flag is on; the three switches are
            // independent (Use-Expiry may be on without Track-Mfg, subtlety a). When the company flag is off the
            // switches stay false so an existing (non-batch) company is byte-identical (ER-13).
            if (ShowBatchSwitches)
            {
                item.MaintainInBatches = MaintainInBatches;
                item.TrackManufacturingDate = TrackManufacturingDate;
                item.UseExpiryDates = UseExpiryDates;
            }

            if (wantsOpening && openingQty > 0m)
            {
                var batch = string.IsNullOrWhiteSpace(OpeningBatchLabel) ? null : OpeningBatchLabel.Trim();
                service.AddOpeningBalance(item.Id, OpeningGodown!.Id, openingQty, openingRate, batch);
            }

            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshPickers();
        RefreshList();
        var openingNote = wantsOpening && openingQty > 0m
            ? $" with opening {openingQty:0.######} {SelectedUnit.Symbol}"
            : string.Empty;
        Message = $"Stock item '{name}' created under {SelectedGroup.Name}{openingNote}.";
        Name = string.Empty;
        Alias = string.Empty;
        HsnSacCode = string.Empty;
        IsTaxable = false;
        ReorderLevelText = string.Empty;
        MinimumOrderQtyText = string.Empty;
        OpeningQuantityText = string.Empty;
        OpeningRateText = string.Empty;
        OpeningBatchLabel = string.Empty;
        Taxability = Taxabilities.First();
        GstRate = GstRates.First();
        MaintainInBatches = false;
        TrackManufacturingDate = false;
        UseExpiryDates = false;
        _onChanged();
        return true;
    }

    // ---- parsing helpers ----

    private bool TryParseOptionalQuantity(string? text, string label, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!TryParseQuantity(text, label, out var q))
            return false;               // Message set by TryParseQuantity
        if (q < 0m)
        {
            Message = $"{label} must be ≥ 0.";
            return false;
        }
        value = q;
        return true;
    }

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
        var groupId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Groups.Add(g);
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == groupId) ?? Groups.FirstOrDefault();

        var catId = SelectedCategory?.Category?.Id;
        CategoryOptions.Clear();
        CategoryOptions.Add(new OptionalStockCategoryOption { Category = null, Display = "◦ (none)" });
        foreach (var c in _company.StockCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            CategoryOptions.Add(new OptionalStockCategoryOption { Category = c, Display = c.Name });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Category?.Id == catId)
                           ?? CategoryOptions.FirstOrDefault();

        var unitId = SelectedUnit?.Id;
        Units.Clear();
        foreach (var u in _company.Units.OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            Units.Add(u);
        SelectedUnit = Units.FirstOrDefault(u => u.Id == unitId) ?? Units.FirstOrDefault();

        var godownId = OpeningGodown?.Id;
        Godowns.Clear();
        foreach (var g in _company.Godowns.OrderByDescending(g => g.IsMainLocation)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Godowns.Add(g);
        OpeningGodown = Godowns.FirstOrDefault(g => g.Id == godownId)
                        ?? Godowns.FirstOrDefault(g => g.IsMainLocation)
                        ?? Godowns.FirstOrDefault();

        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        Existing.Clear();
        var service = new InventoryService(_company);
        foreach (var item in _company.StockItems)
        {
            var group = _company.FindStockGroup(item.StockGroupId)?.Name ?? "—";
            var unit = _company.FindUnit(item.BaseUnitId)?.Symbol ?? "—";
            var opening = service.OpeningValueOf(item.Id);
            Existing.Add(new StockItemListRow
            {
                Name = item.Name,
                Under = group,
                Unit = unit,
                Valuation = ValuationLabel(item.ValuationMethod),
                OpeningValue = opening == Money.Zero
                    ? "—"
                    : "₹" + opening.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture),
            });
        }
    }

    private static string ValuationLabel(StockValuationMethod method) => method switch
    {
        StockValuationMethod.AverageCost => "Average Cost",
        StockValuationMethod.Fifo => "FIFO",
        StockValuationMethod.Lifo => "LIFO",
        StockValuationMethod.StandardCost => "Standard Cost",
        StockValuationMethod.LastPurchaseCost => "Last Purchase Cost",
        StockValuationMethod.LastSaleCost => "Last Sale Cost",
        _ => method.ToString(),
    };
}
