using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A stock-group row for the existing-groups list on the master screen.</summary>
public sealed class StockGroupListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Quantities { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the "Under" parent picker for a stock group: "Primary" (top-level, no parent) or any
/// existing stock group. <see cref="Group"/> is null for the Primary option.
/// </summary>
public sealed class ParentStockGroupOption
{
    public StockGroup? Group { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Group is null;
}

/// <summary>
/// The Stock-Group creation master ("Masters → Create → Inventory Masters → Stock Group", catalog §9;
/// RQ-1): a name, an optional alias, an optional <b>Under</b> parent (Primary ⇒ top-level, or nest under an
/// existing group), and the <b>"Should quantities be added?"</b> flag (default yes). Creates the group via
/// the <see cref="InventoryService"/> (which enforces unique name + valid, non-cyclic parent) and persists.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="CostCentreMasterViewModel"/>.</para>
/// </summary>
public sealed partial class StockGroupMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The parent options: "Primary" plus every existing stock group.</summary>
    public ObservableCollection<ParentStockGroupOption> ParentOptions { get; } = new();

    /// <summary>The existing stock groups, refreshed after each create.</summary>
    public ObservableCollection<StockGroupListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentStockGroupOption? _selectedParent;
    [ObservableProperty] private bool _addQuantities = true;
    [ObservableProperty] private string? _message;

    public StockGroupMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the stock group under the chosen parent
    /// (Primary ⇒ top-level) via the engine and persists. The engine also enforces uniqueness + a valid,
    /// non-cyclic parent; any domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A stock group name is required.";
            return false;
        }

        var parentId = SelectedParent?.Group?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();

        try
        {
            var service = new InventoryService(_company);
            service.CreateStockGroup(name, parentId, alias, AddQuantities);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Group!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Stock group '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        AddQuantities = true;
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Group?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentStockGroupOption { Group = null, Display = "◦ Primary (top-level)" });
        foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentStockGroupOption { Group = g, Display = g.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Group?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var g in _company.StockGroups)
        {
            var under = g.ParentId is { } pid
                ? _company.FindStockGroup(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new StockGroupListRow
            {
                Name = g.Name,
                Under = under,
                Quantities = g.AddQuantities ? "Added" : "Not added",
            });
        }
    }
}
