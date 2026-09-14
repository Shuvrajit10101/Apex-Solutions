using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A cost-centre row for the existing-centres list on the master screen.
/// <para>W28 C2/C3 — carries <see cref="IMasterListRow"/>, which is what makes Ctrl+Enter and Alt+D able to name
/// the master this row displays.</para></summary>
public sealed partial class CostCentreListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class CostCentreMasterViewModel
    : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // --------------------------------------------- W28 C3: alteration state (census 2.8)

    /// <summary>The id of the cost centre being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading — it says which VERB is running, because the form is identical in both
    /// modes.</summary>
    public string Caption => IsAltering ? "Cost Centre Alteration" : "Cost Centre Creation";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing cost centre — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve.
    /// </summary>
    public static CostCentreMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid centreId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindCostCentre(centreId) is not { } centre) return null;

        var vm = new CostCentreMasterViewModel(company, storage, onChanged);
        vm._editingId = centreId;
        vm.LoadFrom(centre);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>
    /// Loads an existing centre's values into the form.
    ///
    /// <para>🔴 <b>ORDER IS LOAD-BEARING HERE and it is the one place this screen could go quietly wrong.</b>
    /// The category must be assigned BEFORE the parent, because <see cref="OnSelectedCategoryChanged"/> rebuilds
    /// the parent picker (parents are category-scoped) and resets the selection to Primary. Setting the parent
    /// first and the category second would therefore discard the parent every time — an alter that silently
    /// flattened a nested centre to the top of its category the moment the operator pressed Ctrl+A.</para>
    ///
    /// <para>The parent is then re-found by ID with an explicit Primary fallback, never by position, so a centre
    /// whose parent has since moved category is shown as Primary rather than pointed at whatever is first.</para>
    /// </summary>
    public void LoadFrom(CostCentre centre)
    {
        ArgumentNullException.ThrowIfNull(centre);
        Name = centre.Name;
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == centre.CategoryId) ?? SelectedCategory;
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Centre?.Id == centre.ParentId)
            ?? ParentOptions.FirstOrDefault(o => o.IsPrimary);
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames, re-categorises and re-parents the cost centre this screen was opened over,
    /// via <see cref="CostMasterService.AlterCostCentre"/> — which enforces the unique name excluding itself, a
    /// parent in the SAME category, and no nesting cycle.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing cost centre.";
            return false;
        }
        if (SelectedCategory is null)
        {
            Message = "Pick a cost category.";
            return false;
        }

        try
        {
            var altered = new CostMasterService(_company).AlterCostCentre(
                _editingId, Name, SelectedCategory.Id, SelectedParent?.Centre?.Id);
            _storage.Save(_company);
            var underLabel = SelectedParent is { IsPrimary: false } p ? p.Centre!.Name : "Primary";
            Message = $"Cost centre '{altered.Name}' altered — under {underLabel} ({SelectedCategory.Name}).";
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

    // ------------------------------------------- W28 C2: the shared Alt+D arm (census 2.8)

    /// <inheritdoc/>
    public string MasterKindLabel => "cost centre";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting()
    {
        RefreshParentOptions();
        RefreshList();
    }

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>MasterDeletionRules.EnsureCostCentreDeletable</c>'s: the vendor's allocation condition, plus
    /// sub-centres and any godown designated as this centre's job/project (census 9.6).</remarks>
    public void DeleteMaster(Guid id) => new CostMasterService(_company).DeleteCostCentre(id);

    // ------------------------------------------------- keyboard selection over the existing list

    private PayrollMasterHighlight<CostCentreListRow>? _highlight;

    private PayrollMasterHighlight<CostCentreListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<CostCentreListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The highlighted existing-centre row, or <c>null</c>. Ctrl+Enter opens Cost Centre Alteration;
    /// Alt+D deletes it.</summary>
    public CostCentreListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

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
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var centre in _company.CostCentres)
        {
            var category = _company.FindCostCategory(centre.CategoryId);
            var under = centre.ParentId is { } pid
                ? _company.FindCostCentre(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new CostCentreListRow
            {
                MasterId = centre.Id,
                Name = centre.Name,
                Category = category?.Name ?? "—",
                Under = under,
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
