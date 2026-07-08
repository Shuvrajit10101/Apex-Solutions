using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A cost-centre row for the existing-centres list on the master screen.</summary>
public sealed class CostCentreListRow
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the "Under" parent picker: the option to nest a centre directly under its category
/// (Primary — no parent) or under another centre in the SAME category. <see cref="Centre"/> is null for
/// the Primary option.
/// </summary>
public sealed class ParentCentreOption
{
    public CostCentre? Centre { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Centre is null;
}

/// <summary>
/// The Cost-Centre creation master ("Masters → Create → Cost Centre", catalog §6): pick a name, the
/// <b>Category</b> it belongs to, and an <b>Under</b> parent (Primary ⇒ top-level, or another centre in the
/// same category — hierarchical), create the centre on the current company, and see it appear in the list.
/// Persists the company to its <c>.db</c> via <see cref="CompanyStorage.Save"/> on create.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="LedgerMasterViewModel"/>.</para>
/// </summary>
public sealed partial class CostCentreMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Cost Centres",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Category"), MasterListColumn.Text("Under") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Category, r.Under }).ToList());

    /// <summary>The cost categories the Category picker offers (company order; Primary first).</summary>
    public ObservableCollection<CostCategory> Categories { get; } = new();

    /// <summary>The parent options for the chosen category: "Primary" plus every centre already in it.</summary>
    public ObservableCollection<ParentCentreOption> ParentOptions { get; } = new();

    /// <summary>The existing cost centres, refreshed after each create.</summary>
    public ObservableCollection<CostCentreListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private CostCategory? _selectedCategory;
    [ObservableProperty] private ParentCentreOption? _selectedParent;
    [ObservableProperty] private string? _message;

    public CostCentreMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var c in _company.CostCategories)
            Categories.Add(c);
        SelectedCategory = Categories.FirstOrDefault();
        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>Rebuilds the parent picker whenever the chosen category changes (parents are category-scoped).</summary>
    partial void OnSelectedCategoryChanged(CostCategory? value) => RefreshParentOptions();

    private void RefreshParentOptions()
    {
        ParentOptions.Clear();
        ParentOptions.Add(new ParentCentreOption { Centre = null, Display = "◦ Primary (no parent)" });
        if (SelectedCategory is not null)
            foreach (var centre in _company.CostCentres.Where(c => c.CategoryId == SelectedCategory.Id))
                ParentOptions.Add(new ParentCentreOption { Centre = centre, Display = centre.Name });

        // Default to Primary (the first option) whenever the category changes.
        SelectedParent = ParentOptions.FirstOrDefault();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty + unique, a category is chosen, then adds the centre
    /// under the chosen parent (Primary ⇒ no parent) and persists. Refreshes the list + the parent picker
    /// (so the new centre can itself be a parent) and clears the name for the next entry.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A cost centre name is required.";
            return false;
        }
        if (SelectedCategory is null)
        {
            Message = "Pick a cost category.";
            return false;
        }
        if (_company.FindCostCentreByName(name) is not null)
        {
            Message = $"A cost centre named '{name}' already exists.";
            return false;
        }

        var parentId = SelectedParent?.Centre?.Id;
        var centre = new CostCentre(Guid.NewGuid(), name, SelectedCategory.Id, parentId);

        _company.AddCostCentre(centre);
        _storage.Save(_company);

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Centre!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Cost centre '{name}' created under {underLabel} ({SelectedCategory.Name}).";
        Name = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var centre in _company.CostCentres)
        {
            var category = _company.FindCostCategory(centre.CategoryId);
            var under = centre.ParentId is { } pid
                ? _company.FindCostCentre(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new CostCentreListRow
            {
                Name = centre.Name,
                Category = category?.Name ?? "—",
                Under = under,
            });
        }
    }
}
