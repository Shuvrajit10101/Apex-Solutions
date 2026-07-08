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

/// <summary>A Bill-of-Materials row for the existing-BOMs list on the master screen.</summary>
public sealed class BomListRow
{
    public string Name { get; init; } = string.Empty;
    public string FinishedGood { get; init; } = string.Empty;
    public string UnitOfManufacture { get; init; } = string.Empty;
    public string Components { get; init; } = string.Empty;
    public string CarveOuts { get; init; } = string.Empty;
}

/// <summary>A BOM-line-type picker option (label + the enum value) — Component / By-Product / Co-Product / Scrap.</summary>
public sealed class BomLineTypeOption
{
    public BomLineType Type { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// One editable line of the BOM component grid (Phase 6 Cluster 2; requirements RQ-9/RQ-10, DP-3): the
/// <b>component/output item</b>, an optional consumption/production <b>godown</b>, a <b>line type</b>
/// (Component / By-Product / Co-Product / Scrap — the carve-out types offered only when the F12
/// "Define type of component" config is on, RQ-10), a <b>per-block quantity</b>, and — for a carve-out line —
/// an optional <b>carve-out rate</b> (₹/unit) OR <b>percent</b> of the finished-good cost (DP-3). Repeatable so
/// one BOM lists several components + carve-outs. Parsing/validation is deferred to the parent
/// <see cref="BomMasterViewModel"/>.
///
/// <para>MVVM boundary: references only the domain, no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class BomLineRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The component/output items this line's picker chooses from (every stock item).</summary>
    public IReadOnlyList<StockItem> ItemOptions { get; }

    /// <summary>The godown options: "(auto)" (resolve at manufacture) plus every godown.</summary>
    public IReadOnlyList<OptionalGodownOption> GodownOptions { get; }

    /// <summary>The line-type options offered on THIS line (Component only, unless the F12 config is on — RQ-10).</summary>
    public IReadOnlyList<BomLineTypeOption> TypeOptions { get; }

    /// <summary>Whether the By-Product/Co-Product/Scrap type picker + carve-out fields are shown (F12 config on, RQ-10).</summary>
    public bool ShowTypePicker { get; }

    [ObservableProperty] private StockItem? _selectedItem;
    [ObservableProperty] private OptionalGodownOption? _selectedGodown;
    [ObservableProperty] private BomLineTypeOption? _selectedType;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string _carveOutRateText = string.Empty;
    [ObservableProperty] private string _carveOutPercentText = string.Empty;

    private readonly bool _ready;

    public BomLineRowViewModel(
        IReadOnlyList<StockItem> itemOptions,
        IReadOnlyList<OptionalGodownOption> godownOptions,
        IReadOnlyList<BomLineTypeOption> typeOptions,
        bool showTypePicker,
        Action onChanged)
    {
        ItemOptions = itemOptions ?? throw new ArgumentNullException(nameof(itemOptions));
        GodownOptions = godownOptions ?? throw new ArgumentNullException(nameof(godownOptions));
        TypeOptions = typeOptions ?? throw new ArgumentNullException(nameof(typeOptions));
        ShowTypePicker = showTypePicker;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        // Seed the default godown/type WITHOUT firing onChanged — a fresh blank row must not be treated as
        // "touched" (that would spawn an endless run of trailing rows and overflow the stack).
        SelectedGodown = GodownOptions.FirstOrDefault();
        SelectedType = TypeOptions.FirstOrDefault();
        _ready = true;
    }

    partial void OnSelectedItemChanged(StockItem? value) { if (_ready) _onChanged(); }
    partial void OnSelectedTypeChanged(BomLineTypeOption? value) { if (_ready) _onChanged(); }
    partial void OnQuantityTextChanged(string value) { if (_ready) _onChanged(); }
    partial void OnCarveOutRateTextChanged(string value) { if (_ready) _onChanged(); }
    partial void OnCarveOutPercentTextChanged(string value) { if (_ready) _onChanged(); }

    /// <summary>The line type this row carries (Component when the picker is hidden or unset).</summary>
    public BomLineType LineType => SelectedType?.Type ?? BomLineType.Component;

    /// <summary>True iff this row is a carved-out By-Product/Co-Product/Scrap line (carve-out fields apply).</summary>
    public bool IsCarveOut => LineType is BomLineType.ByProduct or BomLineType.CoProduct or BomLineType.Scrap;

    /// <summary>The parsed per-block quantity (0 when blank/unparsable).</summary>
    public decimal ParsedQuantity => TryParse(QuantityText, out var q) ? q : 0m;

    /// <summary>True once the row has been touched at all — a wholly blank trailing row is ignored.</summary>
    public bool IsBlank =>
        SelectedItem is null
        && string.IsNullOrWhiteSpace(QuantityText)
        && string.IsNullOrWhiteSpace(CarveOutRateText)
        && string.IsNullOrWhiteSpace(CarveOutPercentText);

    private static bool TryParse(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// The Bill-of-Materials creation master ("Masters → Create → Inventory Masters → Bill of Materials", Phase 6
/// Cluster 2; requirements RQ-9/RQ-10/RQ-54). A named <see cref="BillOfMaterials"/> for a finished good:
/// a required <b>BOM name</b> (unique <i>within</i> the finished good, RQ-9), a required <b>finished good</b>
/// item, a <b>unit of manufacture</b> (block size, e.g. 1 or 10), and a repeatable <b>component-line grid</b>
/// — component item, godown, line type {Component/ByProduct/CoProduct/Scrap}, per-block qty, and a carve-out
/// rate or percent (DP-3). Multiple BOMs per item are supported and listed.
///
/// <para>This whole screen is gated by the F12 config <see cref="Company.SetComponentsBom"/> (RQ-10/RQ-52) and
/// shows a friendly hint when it is off; the By-Product/Co-Product/Scrap line-type picker appears only when the
/// F12 <see cref="Company.DefineBomComponentType"/> config is on (RQ-10).</para>
///
/// <para>Pre-validates the name/finished good/unit-of-manufacture, every line's item + per-block qty (6 dp), and
/// any carve-out rate (paisa) BEFORE calling <see cref="BomService.CreateBom"/>, then wraps the engine call in
/// try/catch and surfaces any domain error to <see cref="Message"/> so nothing crashes the UI.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="BatchMasterViewModel"/> / <see cref="StockItemMasterViewModel"/>.</para>
/// </summary>
public sealed partial class BomMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Bills of Materials",
        new[]
        {
            MasterListColumn.Text("BOM"),
            MasterListColumn.Text("Finished Good"),
            MasterListColumn.Text("Unit of Mfg"),
            MasterListColumn.Text("Components"),
            MasterListColumn.Text("Carve-outs"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Name, r.FinishedGood, r.UnitOfManufacture, r.Components, r.CarveOuts,
        }).ToList());

    /// <summary>The finished-good items a BOM can be created for (every stock item).</summary>
    public ObservableCollection<StockItem> FinishedGoods { get; } = new();

    /// <summary>The component/output items each line's picker chooses from (every stock item).</summary>
    private readonly List<StockItem> _componentItems = new();

    /// <summary>The godown options: "(auto)" plus every godown (optional consumption/production location).</summary>
    private readonly List<OptionalGodownOption> _godownOptions = new();

    /// <summary>The line-type options offered on a line (Component; plus carve-out types when the F12 config is on).</summary>
    private readonly List<BomLineTypeOption> _typeOptions = new();

    /// <summary>The repeatable component/output lines (always one blank trailing row for the next entry).</summary>
    public ObservableCollection<BomLineRowViewModel> Lines { get; } = new();

    /// <summary>The existing BOMs, refreshed after each create.</summary>
    public ObservableCollection<BomListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private StockItem? _selectedFinishedGood;
    [ObservableProperty] private string _unitOfManufactureText = "1";
    [ObservableProperty] private string? _message;

    public BomMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        BuildPickerOptions();
        RefreshList();
        AddBlankLine();
    }

    /// <summary>True iff the F12 config "Define type of component for BOM" is on — the carve-out type picker shows (RQ-10).</summary>
    public bool ShowLineTypePicker => _company.DefineBomComponentType;

    /// <summary>True once at least one stock item exists — a BOM needs a finished good and components.</summary>
    public bool CanCreate => FinishedGoods.Count > 0;

    /// <summary>Builds the finished-good / component / godown / line-type picker options.</summary>
    private void BuildPickerOptions()
    {
        var fgId = SelectedFinishedGood?.Id;
        FinishedGoods.Clear();
        _componentItems.Clear();
        foreach (var i in _company.StockItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            FinishedGoods.Add(i);
            _componentItems.Add(i);
        }
        SelectedFinishedGood = FinishedGoods.FirstOrDefault(i => i.Id == fgId) ?? FinishedGoods.FirstOrDefault();

        _godownOptions.Clear();
        _godownOptions.Add(new OptionalGodownOption { Godown = null, Display = "◦ (auto)" });
        foreach (var g in _company.Godowns.OrderByDescending(g => g.IsMainLocation)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            _godownOptions.Add(new OptionalGodownOption { Godown = g, Display = g.Name });

        _typeOptions.Clear();
        _typeOptions.Add(new BomLineTypeOption { Type = BomLineType.Component, Display = "Component" });
        if (ShowLineTypePicker)
        {
            _typeOptions.Add(new BomLineTypeOption { Type = BomLineType.ByProduct, Display = "By-Product" });
            _typeOptions.Add(new BomLineTypeOption { Type = BomLineType.CoProduct, Display = "Co-Product" });
            _typeOptions.Add(new BomLineTypeOption { Type = BomLineType.Scrap, Display = "Scrap" });
        }

        OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>Adds a fresh blank trailing line (the always-present next-entry row).</summary>
    public BomLineRowViewModel AddBlankLine()
    {
        var line = new BomLineRowViewModel(_componentItems, _godownOptions, _typeOptions, ShowLineTypePicker, OnLineChanged);
        Lines.Add(line);
        return line;
    }

    private void OnLineChanged() => EnsureTrailingBlank();

    /// <summary>Ensures exactly one blank trailing row so the operator can always add another line.</summary>
    private void EnsureTrailingBlank()
    {
        if (Lines.Count == 0 || !Lines[^1].IsBlank)
            AddBlankLine();
    }

    /// <summary>
    /// Ctrl+A create: validates the BOM name + finished good + unit-of-manufacture, validates each entered
    /// component/output line (item present, per-block qty &gt; 0 to 6 dp, carve-out rate to the paisa), then
    /// creates the BOM via <see cref="BomService.CreateBom"/> and persists. Requires at least one Component line.
    /// Any domain error (including a per-item duplicate name) is surfaced to <see cref="Message"/> without
    /// crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A BOM name is required.";
            return false;
        }
        if (SelectedFinishedGood is null)
        {
            Message = "Pick a finished-good item for the BOM (create a Stock Item first).";
            return false;
        }
        if (!decimal.TryParse((UnitOfManufactureText ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var unitOfManufacture) || unitOfManufacture <= 0m)
        {
            Message = "The unit of manufacture must be a number greater than zero (e.g. 1 or 10).";
            return false;
        }
        if (!Quantities.IsWithinPrecision(unitOfManufacture))
        {
            Message = $"The unit of manufacture {unitOfManufacture} must be to {Quantities.DecimalPlaces} decimal places.";
            return false;
        }

        // Per-item name uniqueness (RQ-9) — surface a friendly message before the engine guard fires.
        if (_company.FindBomByName(SelectedFinishedGood.Id, name) is not null)
        {
            Message = $"A BOM '{name}' already exists for item '{SelectedFinishedGood.Name}' " +
                      "(BOM names are unique per item).";
            return false;
        }

        var active = Lines.Where(l => !l.IsBlank).ToList();
        if (active.Count == 0)
        {
            Message = "Add at least one component line.";
            return false;
        }

        var domainLines = new List<BomLine>();
        foreach (var l in active)
        {
            if (l.SelectedItem is null)
            {
                Message = "Every component line needs an item (pick one).";
                return false;
            }
            if (!decimal.TryParse((l.QuantityText ?? string.Empty).Trim(),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var qty) || qty <= 0m)
            {
                Message = $"Line '{l.SelectedItem.Name}' needs a per-block quantity greater than zero.";
                return false;
            }
            if (!Quantities.IsWithinPrecision(qty))
            {
                Message = $"Line '{l.SelectedItem.Name}' quantity {qty} must be to {Quantities.DecimalPlaces} decimal places.";
                return false;
            }

            var type = l.LineType;
            Money? rate = null;
            decimal? percent = null;
            if (type != BomLineType.Component)
            {
                // A carve-out line MAY carry a rate OR a percent (DP-3) — never both; both blank ⇒ default to
                // the item's standard cost at manufacture (the engine handles that).
                var hasRate = !string.IsNullOrWhiteSpace(l.CarveOutRateText);
                var hasPercent = !string.IsNullOrWhiteSpace(l.CarveOutPercentText);
                if (hasRate && hasPercent)
                {
                    Message = $"Carve-out line '{l.SelectedItem.Name}': enter a rate OR a percent, not both.";
                    return false;
                }
                if (hasRate)
                {
                    if (!decimal.TryParse(l.CarveOutRateText!.Trim(),
                            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out var r) || r < 0m)
                    {
                        Message = $"Carve-out line '{l.SelectedItem.Name}': rate must be a number ≥ 0.";
                        return false;
                    }
                    var money = Money.FromRupees(r);
                    if (!money.IsPaisaExact)
                    {
                        Message = $"Carve-out rate {r} must be to the paisa (2 decimal places).";
                        return false;
                    }
                    rate = money;
                }
                else if (hasPercent)
                {
                    if (!decimal.TryParse(l.CarveOutPercentText!.Trim(),
                            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out var p) || p < 0m)
                    {
                        Message = $"Carve-out line '{l.SelectedItem.Name}': percent must be a number ≥ 0.";
                        return false;
                    }
                    percent = p;
                }
            }

            try
            {
                domainLines.Add(new BomLine(type, l.SelectedItem.Id, qty, l.SelectedGodown?.Godown?.Id, rate, percent));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = ex.Message;
                return false;
            }
        }

        if (!domainLines.Any(l => l.IsComponent))
        {
            Message = "A BOM needs at least one Component line (a By-Product/Co-Product/Scrap alone is not a recipe).";
            return false;
        }

        try
        {
            var service = new BomService(_company);
            service.CreateBom(SelectedFinishedGood.Id, name, unitOfManufacture, domainLines);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"BOM '{name}' created for {SelectedFinishedGood.Name}.";
        Name = string.Empty;
        UnitOfManufactureText = "1";
        Lines.Clear();
        AddBlankLine();
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        BuildPickerOptions();
        Existing.Clear();
        foreach (var bom in _company.BillsOfMaterials
                     .OrderBy(b => _company.FindStockItem(b.StockItemId)?.Name ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var fg = _company.FindStockItem(bom.StockItemId);
            var componentNames = bom.ComponentLines
                .Select(l => _company.FindStockItem(l.ComponentStockItemId)?.Name ?? "?")
                .ToList();
            var carveOutNames = bom.CarveOutLines
                .Select(l => (_company.FindStockItem(l.ComponentStockItemId)?.Name ?? "?")
                             + " (" + CarveOutLabel(l.LineType) + ")")
                .ToList();
            Existing.Add(new BomListRow
            {
                Name = bom.Name,
                FinishedGood = fg?.Name ?? "—",
                UnitOfManufacture = bom.UnitOfManufacture.ToString("0.######", CultureInfo.InvariantCulture),
                Components = componentNames.Count > 0 ? string.Join(", ", componentNames) : "—",
                CarveOuts = carveOutNames.Count > 0 ? string.Join(", ", carveOutNames) : "—",
            });
        }
    }

    private static string CarveOutLabel(BomLineType type) => type switch
    {
        BomLineType.ByProduct => "By-Product",
        BomLineType.CoProduct => "Co-Product",
        BomLineType.Scrap => "Scrap",
        _ => "Component",
    };
}
