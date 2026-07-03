using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A cost-category row for the existing-categories list on the master screen.</summary>
public sealed class CostCategoryListRow
{
    public string Name { get; init; } = string.Empty;
    public string Allocates { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The Cost-Category creation master ("Masters → Create → Cost Category", catalog §6): pick a name and the
/// two allocation flags ("Allocate Revenue Items" / "Allocate Non-Revenue Items" — at least one must be
/// Yes), create the category on the current company, and see it appear in the list. Persists the company
/// to its <c>.db</c> via <see cref="CompanyStorage.Save"/> on create.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="LedgerMasterViewModel"/>.</para>
/// </summary>
public sealed partial class CostCategoryMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The existing cost categories, refreshed after each create (seeded Primary included).</summary>
    public ObservableCollection<CostCategoryListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>"Allocate Revenue Items" (catalog §6) — may allocate P&amp;L (income/expense) lines. On by default.</summary>
    [ObservableProperty] private bool _allocateRevenueItems = true;

    /// <summary>"Allocate Non-Revenue Items" (catalog §6) — may allocate balance-sheet lines. Off by default.</summary>
    [ObservableProperty] private bool _allocateNonRevenueItems;

    [ObservableProperty] private string? _message;

    public CostCategoryMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, unique, and at least one allocation flag is on,
    /// then adds the category and persists. Refreshes the list and clears the name for the next entry.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A cost category name is required.";
            return false;
        }
        if (!AllocateRevenueItems && !AllocateNonRevenueItems)
        {
            Message = "A cost category must allocate revenue and/or non-revenue items (at least one must be Yes).";
            return false;
        }
        if (_company.FindCostCategoryByName(name) is not null)
        {
            Message = $"A cost category named '{name}' already exists.";
            return false;
        }

        var category = new CostCategory(
            Guid.NewGuid(), name,
            allocateRevenueItems: AllocateRevenueItems,
            allocateNonRevenueItems: AllocateNonRevenueItems);

        _company.AddCostCategory(category);
        _storage.Save(_company);

        RefreshList();
        Message = $"Cost category '{name}' created.";
        Name = string.Empty;
        AllocateRevenueItems = true;
        AllocateNonRevenueItems = false;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var c in _company.CostCategories)
        {
            var allocates = (c.AllocateRevenueItems, c.AllocateNonRevenueItems) switch
            {
                (true, true) => "Revenue + Non-Revenue",
                (true, false) => "Revenue",
                (false, true) => "Non-Revenue",
                _ => "—",
            };
            Existing.Add(new CostCategoryListRow
            {
                Name = c.Name,
                Allocates = allocates,
                Kind = c.IsPredefined ? "Predefined" : "User",
            });
        }
    }
}
