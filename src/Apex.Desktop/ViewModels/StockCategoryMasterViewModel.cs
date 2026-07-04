using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A stock-category row for the existing-categories list on the master screen.</summary>
public sealed class StockCategoryListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the "Under" parent picker for a stock category: "Primary" (top-level) or any existing
/// category. <see cref="Category"/> is null for the Primary option.
/// </summary>
public sealed class ParentStockCategoryOption
{
    public StockCategory? Category { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Category is null;
}

/// <summary>
/// The Stock-Category creation master ("Masters → Create → Inventory Masters → Stock Category", catalog §9;
/// RQ-2): a name, an optional alias, and an optional parent category (an independent classification axis,
/// orthogonal to Stock Groups). Creates the category via the <see cref="InventoryService"/> (unique name +
/// valid, non-cyclic parent) and persists.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="CostCentreMasterViewModel"/>.</para>
/// </summary>
public sealed partial class StockCategoryMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The parent options: "Primary" plus every existing category.</summary>
    public ObservableCollection<ParentStockCategoryOption> ParentOptions { get; } = new();

    /// <summary>The existing stock categories, refreshed after each create.</summary>
    public ObservableCollection<StockCategoryListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentStockCategoryOption? _selectedParent;
    [ObservableProperty] private string? _message;

    public StockCategoryMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the category under the chosen parent
    /// (Primary ⇒ top-level) via the engine and persists. The engine enforces uniqueness + a valid,
    /// non-cyclic parent; any domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A stock category name is required.";
            return false;
        }

        var parentId = SelectedParent?.Category?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();

        try
        {
            var service = new InventoryService(_company);
            service.CreateStockCategory(name, parentId, alias);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Category!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Stock category '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Category?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentStockCategoryOption { Category = null, Display = "◦ Primary (top-level)" });
        foreach (var c in _company.StockCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentStockCategoryOption { Category = c, Display = c.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Category?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var c in _company.StockCategories)
        {
            var under = c.ParentId is { } pid
                ? _company.FindStockCategory(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new StockCategoryListRow { Name = c.Name, Under = under });
        }
    }
}
