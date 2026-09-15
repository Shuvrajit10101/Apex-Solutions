using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A cost-category row for the existing-categories list on the master screen.
///
/// <para>W28 C2/C3 — carries <see cref="IMasterListRow"/>. Before it, this master had Create and nothing else,
/// and the cost masters were the only two in cluster C2 with <b>no delete service in the engine at all</b>, not
/// merely one without callers.</para>
/// </summary>
public sealed partial class CostCategoryListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Allocates { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class CostCategoryMasterViewModel
    : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // --------------------------------------------- W28 C3: alteration state (census 2.7)

    /// <summary>The id of the cost category being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading — it says which VERB is running, because the form is identical in both
    /// modes.</summary>
    public string Caption => IsAltering ? "Cost Category Alteration" : "Cost Category Creation";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing cost category — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve.
    /// </summary>
    public static CostCategoryMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid categoryId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindCostCategory(categoryId) is not { } category) return null;

        var vm = new CostCategoryMasterViewModel(company, storage, onChanged);
        vm._editingId = categoryId;
        vm.LoadFrom(category);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>Loads an existing category's values into the form — every field the screen offers.</summary>
    public void LoadFrom(CostCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        Name = category.Name;
        AllocateRevenueItems = category.AllocateRevenueItems;
        AllocateNonRevenueItems = category.AllocateNonRevenueItems;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames the category and rewrites its two allocation flags, via
    /// <see cref="CostMasterService.AlterCostCategory"/> (unique name excluding itself, at least one flag Yes).
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing cost category.";
            return false;
        }

        try
        {
            var altered = new CostMasterService(_company)
                .AlterCostCategory(_editingId, Name, AllocateRevenueItems, AllocateNonRevenueItems);
            _storage.Save(_company);
            Message = $"Cost category '{altered.Name}' altered.";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        _onChanged();
        return true;
    }

    // ------------------------------------------- W28 C2: the shared Alt+D arm (census 2.7)

    /// <inheritdoc/>
    public string MasterKindLabel => "cost category";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>MasterDeletionRules.EnsureCostCategoryDeletable</c>'s: the predefined Primary category, any posted
    /// allocation along it, and the vendor's own condition that no cost centre may be grouped under it.</remarks>
    public void DeleteMaster(Guid id) => new CostMasterService(_company).DeleteCostCategory(id);

    // ------------------------------------------------- keyboard selection over the existing list

    private PayrollMasterHighlight<CostCategoryListRow>? _highlight;

    private PayrollMasterHighlight<CostCategoryListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<CostCategoryListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The highlighted existing-category row, or <c>null</c>. Ctrl+Enter opens Cost Category
    /// Alteration; Alt+D deletes it.</summary>
    public CostCategoryListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Cost Categories",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Allocates"), MasterListColumn.Text("Kind") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Allocates, r.Kind }).ToList());

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
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

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
                MasterId = c.Id,
                Name = c.Name,
                Allocates = allocates,
                Kind = c.IsPredefined ? "Predefined" : "User",
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
