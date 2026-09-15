using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A stock-category row for the existing-categories list on the master screen.
///
/// <para>🔴 <b>W28 C3 — THIS ROW HAD NO ID, AND THAT WAS THE WHOLE OBSTRUCTION.</b> Census row 3.5 records the
/// shape and it held here too: the operator could SEE every category but no row resolved back to the master it
/// displayed, so there was nothing for Ctrl+Enter to open and nothing for Alt+D to name. The Stock Category
/// master therefore had Create and nothing else, and <c>InventoryService.DeleteStockCategory</c> sat in the
/// engine with zero callers. <see cref="IMasterListRow"/> is what ends that, and it is the SAME contract the
/// payroll and voucher-type masters already use rather than a second one invented here.</para>
/// </summary>
public sealed partial class StockCategoryListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class StockCategoryMasterViewModel
    : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // --------------------------------------------- W28 C3: alteration state (census 3.2)

    /// <summary>The id of the stock category being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading. It says which VERB is running, because the form is identical in both modes
    /// and a heading reading "Creation" over an alteration is how an operator mistakes one for the other.</summary>
    public string Caption => IsAltering ? "Stock Category Alteration" : "Stock Category Creation";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing stock category — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve. Mirrors <see cref="StockGroupMasterViewModel.ForAlter"/>.
    /// </summary>
    public static StockCategoryMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid categoryId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindStockCategory(categoryId) is not { } category) return null;

        var vm = new StockCategoryMasterViewModel(company, storage, onChanged);
        vm._editingId = categoryId;
        vm.LoadFrom(category);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>Loads an existing category's values into the form — every field the screen offers, so an Alter
    /// that changes one thing writes the rest back unchanged instead of resetting them to the form defaults.
    /// <para>🔴 The parent option is re-found by ID and falls back to Primary. Matching by NAME would silently
    /// re-parent a category onto a same-named sibling; falling back to the first option would silently move a
    /// nested category to the top of the tree the first time it was opened.</para></summary>
    public void LoadFrom(StockCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        Name = category.Name;
        Alias = category.Alias ?? string.Empty;
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Category?.Id == category.ParentId)
            ?? ParentOptions.FirstOrDefault(o => o.IsPrimary);
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames / re-aliases / re-parents the stock category this screen was opened over, via
    /// <see cref="InventoryService.AlterStockCategory"/> (unique name excluding itself, existing and non-cyclic
    /// parent). Any domain refusal is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing stock category.";
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        try
        {
            var altered = new InventoryService(_company)
                .AlterStockCategory(_editingId, Name, SelectedParent?.Category?.Id, alias);
            _storage.Save(_company);
            var underLabel = SelectedParent is { IsPrimary: false } p ? p.Category!.Name : "Primary";
            Message = $"Stock category '{altered.Name}' altered — under {underLabel}.";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshParentOptions();
        RefreshList();
        _onChanged();
        return true;
    }

    // ------------------------------------------- W28 C2: the shared Alt+D arm (census 3.2)

    /// <inheritdoc/>
    public string MasterKindLabel => "stock category";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    /// <remarks>Refreshes the PARENT PICKER too: a deleted or renamed category must not stay on offer as an
    /// "Under" option pointing at a master that is gone.</remarks>
    public void ReloadExisting()
    {
        RefreshParentOptions();
        RefreshList();
    }

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>MasterDeletionRules.EnsureStockCategoryDeletable</c>'s: sub-categories or stock items filed under it.
    /// <c>InventoryService.DeleteStockCategory</c> has existed since the inventory slice and, until now, had
    /// <b>zero callers anywhere in Apex.Desktop</b>.</remarks>
    public void DeleteMaster(Guid id) => new InventoryService(_company).DeleteStockCategory(id);

    // ------------------------------------------------- keyboard selection over the existing list

    /// <summary>The highlight machinery, shared verbatim with every other master list rather than re-implemented
    /// (by-index restore being the trap a second copy falls into — see <see cref="PayrollMasterHighlight{TRow}"/>).
    /// </summary>
    private PayrollMasterHighlight<StockCategoryListRow>? _highlight;

    private PayrollMasterHighlight<StockCategoryListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<StockCategoryListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The highlighted existing-category row, or <c>null</c>. Ctrl+Enter on it opens Stock Category
    /// Alteration; Alt+D deletes it.</summary>
    public StockCategoryListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Stock Categories",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Under") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under }).ToList());

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
        // Keep the highlight on the SAME CATEGORY across a rebuild (a save re-renders the list): re-finding it by
        // id rather than by index means an alter that renames or re-parents a category does not silently move the
        // highlight onto a neighbouring master that the next Ctrl+Enter would open and the next Alt+D would delete.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var c in _company.StockCategories)
        {
            var under = c.ParentId is { } pid
                ? _company.FindStockCategory(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new StockCategoryListRow { MasterId = c.Id, Name = c.Name, Under = under });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
