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

/// <summary>A reorder-definition row for the existing-definitions list on the master screen.</summary>
public sealed class ReorderLevelListRow
{
    public string Scope { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Reorder { get; init; } = string.Empty;
    public string MinQty { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Criteria { get; init; } = string.Empty;
}

/// <summary>A reorder-scope picker option (Item / Group / Category).</summary>
public sealed class ReorderScopeOption
{
    public ReorderScope Scope { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A reorder-criterion picker option (Higher / Lower).</summary>
public sealed class ReorderCriteriaOption
{
    public ReorderCriteria Criteria { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A reorder-target picker option (a stock item / group / category, per the selected scope).</summary>
public sealed class ReorderTargetOption
{
    public Guid Id { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Reorder Levels</b> master screen ("Masters → Create → Inventory Masters → Reorder Levels"; Phase 6
/// slice 6; requirements RQ-32..RQ-35/RQ-53/RQ-54; Tally-Book pp.158–162). A reorder definition is created per
/// <b>Stock Item</b>, <b>Stock Group</b> or <b>Stock Category</b> (the <b>Scope</b> picker), carrying two figures
/// the Reorder-Status report resolves — the <b>reorder level</b> and the <b>minimum order quantity</b>. Each figure
/// is independently <b>Simple</b> (a fixed typed quantity) or <b>Advanced</b> (reconciled Higher/Lower against the
/// item's consumption over a rolling period) via the screen's two toggles:
/// <list type="bullet">
///   <item><b>Alt+S</b> toggles the reorder level Simple⇄Advanced (<see cref="ReorderAdvanced"/>);</item>
///   <item><b>Alt+V</b> toggles the min-order-qty Simple⇄Advanced (<see cref="MinQtyAdvanced"/>).</item>
/// </list>
/// A single shared consumption <b>Period</b> (count + unit) and <b>Criteria</b> (Higher/Lower) govern both Advanced
/// figures (DD-1). At most one definition per (scope, target); creating for an existing (scope, target)
/// <b>replaces</b> it (upsert — RQ-32). Persists via <see cref="ReorderLevelsService"/>.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="BatchMasterViewModel"/> / <see cref="PriceLevelsViewModel"/>.</para>
/// </summary>
public sealed partial class ReorderLevelsViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Reorder Levels",
        new[]
        {
            MasterListColumn.Text("Scope"),
            MasterListColumn.Text("Target"),
            MasterListColumn.Text("Reorder Level"),
            MasterListColumn.Text("Min Order Qty"),
            MasterListColumn.Text("Period"),
            MasterListColumn.Text("Criteria"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Scope, r.Target, r.Reorder, r.MinQty, r.Period, r.Criteria,
        }).ToList());

    /// <summary>The scope options (Item / Group / Category).</summary>
    public ObservableCollection<ReorderScopeOption> Scopes { get; } = new();

    /// <summary>The target options for the selected scope (the stock items / groups / categories).</summary>
    public ObservableCollection<ReorderTargetOption> Targets { get; } = new();

    /// <summary>The consumption-period unit options (Days / Weeks / Months / Years) — reused from the batch master.</summary>
    public ObservableCollection<ExpiryPeriodUnitOption> PeriodUnits { get; } = new();

    /// <summary>The Higher / Lower criterion options (Advanced reconciliation).</summary>
    public ObservableCollection<ReorderCriteriaOption> Criteria { get; } = new();

    /// <summary>The existing reorder definitions, refreshed after each create.</summary>
    public ObservableCollection<ReorderLevelListRow> Existing { get; } = new();

    [ObservableProperty] private ReorderScopeOption? _selectedScope;
    [ObservableProperty] private ReorderTargetOption? _selectedTarget;

    /// <summary>Alt+S: reorder level Simple (false) ⇄ Advanced (true).</summary>
    [ObservableProperty] private bool _reorderAdvanced;
    [ObservableProperty] private string _reorderQuantityText = string.Empty;

    /// <summary>Alt+V: min order qty Simple (false) ⇄ Advanced (true).</summary>
    [ObservableProperty] private bool _minQtyAdvanced;
    [ObservableProperty] private string _minOrderQtyText = string.Empty;

    [ObservableProperty] private string _periodCountText = string.Empty;
    [ObservableProperty] private ExpiryPeriodUnitOption? _selectedPeriodUnit;
    [ObservableProperty] private ReorderCriteriaOption? _selectedCriteria;

    [ObservableProperty] private string? _message;

    public ReorderLevelsViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Item, Display = "Stock Item" });
        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Group, Display = "Stock Group" });
        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Category, Display = "Stock Category" });

        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Days, Display = "Days" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Weeks, Display = "Weeks" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Months, Display = "Months" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Years, Display = "Years" });
        SelectedPeriodUnit = PeriodUnits.First(u => u.Unit == ExpiryPeriodUnit.Months);

        Criteria.Add(new ReorderCriteriaOption { Criteria = ReorderCriteria.Higher, Display = "Higher (order more)" });
        Criteria.Add(new ReorderCriteriaOption { Criteria = ReorderCriteria.Lower, Display = "Lower" });
        SelectedCriteria = Criteria.First();

        SelectedScope = Scopes.First();   // triggers RefreshTargets via OnSelectedScopeChanged
        RefreshList();
    }

    /// <summary>True when the selected scope has at least one target to define a level against.</summary>
    public bool CanCreate => Targets.Count > 0;

    /// <summary>True while either figure is Advanced — drives the visibility of the Period / Criteria fields.</summary>
    public bool IsAdvanced => ReorderAdvanced || MinQtyAdvanced;

    partial void OnSelectedScopeChanged(ReorderScopeOption? value) => RefreshTargets();
    partial void OnReorderAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsAdvanced));
    partial void OnMinQtyAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsAdvanced));

    /// <summary>Alt+S: flips the reorder level between Simple and Advanced.</summary>
    public void ToggleReorderAdvanced() => ReorderAdvanced = !ReorderAdvanced;

    /// <summary>Alt+V: flips the min order qty between Simple and Advanced.</summary>
    public void ToggleMinQtyAdvanced() => MinQtyAdvanced = !MinQtyAdvanced;

    /// <summary>
    /// Ctrl+A create/upsert: validates the target + quantities (6 dp) and — when either figure is Advanced — the
    /// consumption period (count &gt; 0) and criterion, then creates (or replaces) the definition via
    /// <see cref="ReorderLevelsService.CreateOrUpdate"/> and persists. Any domain error is surfaced to
    /// <see cref="Message"/> without crashing the UI. Blank quantity fields are left unset (null).
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (SelectedScope is not { } scope)
        {
            Message = "Pick a scope (Item / Group / Category).";
            return false;
        }
        if (SelectedTarget is not { } target)
        {
            Message = $"Pick a {scope.Display.ToLowerInvariant()} to define a reorder level for.";
            return false;
        }

        if (!TryParseOptionalQuantity(ReorderQuantityText, "Reorder level", out var reorderQty)) return false;
        if (!TryParseOptionalQuantity(MinOrderQtyText, "Minimum order quantity", out var minQty)) return false;

        int? periodCount = null;
        ExpiryPeriodUnit? periodUnit = null;
        ReorderCriteria? criteria = null;
        if (ReorderAdvanced || MinQtyAdvanced)
        {
            if (!int.TryParse((PeriodCountText ?? string.Empty).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                Message = "An Advanced figure needs a consumption period count (a whole number greater than zero).";
                return false;
            }
            periodCount = count;
            periodUnit = (SelectedPeriodUnit ?? PeriodUnits.First()).Unit;
            criteria = (SelectedCriteria ?? Criteria.First()).Criteria;
        }

        try
        {
            var service = new ReorderLevelsService(_company);
            service.CreateOrUpdate(scope.Scope, target.Id, ReorderAdvanced, reorderQty,
                MinQtyAdvanced, minQty, periodCount, periodUnit, criteria);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Reorder level saved for {scope.Display.ToLowerInvariant()} '{target.Display}'.";
        ReorderQuantityText = string.Empty;
        MinOrderQtyText = string.Empty;
        PeriodCountText = string.Empty;
        _onChanged();
        return true;
    }

    private bool TryParseOptionalQuantity(string? text, string label, out decimal? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;   // blank ⇒ unset
        if (!decimal.TryParse(trimmed,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var q))
        {
            Message = $"{label} must be a number.";
            return false;
        }
        if (q < 0m)
        {
            Message = $"{label} must be ≥ 0.";
            return false;
        }
        if (!Quantities.IsWithinPrecision(q))
        {
            Message = $"{label} {q} must be to {Quantities.DecimalPlaces} decimal places.";
            return false;
        }
        value = q;
        return true;
    }

    private void RefreshTargets()
    {
        var priorId = SelectedTarget?.Id;
        Targets.Clear();
        var scope = SelectedScope?.Scope ?? ReorderScope.Item;
        switch (scope)
        {
            case ReorderScope.Item:
                foreach (var i in _company.StockItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = i.Id, Display = i.Name });
                break;
            case ReorderScope.Group:
                foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = g.Id, Display = g.Name });
                break;
            case ReorderScope.Category:
                foreach (var c in _company.StockCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = c.Id, Display = c.Name });
                break;
        }
        SelectedTarget = Targets.FirstOrDefault(t => t.Id == priorId) ?? Targets.FirstOrDefault();
        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        RefreshTargets();
        Existing.Clear();
        foreach (var d in _company.ReorderDefinitions
                     .OrderBy(d => d.Scope)
                     .ThenBy(d => TargetName(d.Scope, d.TargetId), StringComparer.OrdinalIgnoreCase))
        {
            Existing.Add(new ReorderLevelListRow
            {
                Scope = ScopeLabel(d.Scope),
                Target = TargetName(d.Scope, d.TargetId),
                Reorder = FigureLabel(d.ReorderAdvanced, d.ReorderQuantity),
                MinQty = FigureLabel(d.MinQtyAdvanced, d.MinOrderQuantity),
                Period = d.PeriodCount is { } pc && d.PeriodUnit is { } pu
                    ? $"{pc} {pu}"
                    : "—",
                Criteria = d.Criteria is { } c ? c.ToString() : "—",
            });
        }
    }

    private static string ScopeLabel(ReorderScope scope) => scope switch
    {
        ReorderScope.Item => "Item",
        ReorderScope.Group => "Group",
        ReorderScope.Category => "Category",
        _ => scope.ToString(),
    };

    private static string FigureLabel(bool advanced, decimal? qty)
    {
        var baseText = qty is { } q ? IndianFormat.Quantity(q) : "—";
        return advanced ? $"{baseText} (Adv)" : baseText;
    }

    private string TargetName(ReorderScope scope, Guid targetId) => scope switch
    {
        ReorderScope.Item => _company.FindStockItem(targetId)?.Name ?? "—",
        ReorderScope.Group => _company.FindStockGroup(targetId)?.Name ?? "—",
        ReorderScope.Category => _company.FindStockCategory(targetId)?.Name ?? "—",
        _ => "—",
    };
}
