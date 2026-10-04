using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
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

    /// <summary>The centre's alias, or an empty string when it has none (census 2.8).
    /// <para>On the list rather than only in the form so a captured alias is VISIBLE without opening the master
    /// for alteration — the alias exists to let an operator recognise a centre by its short name, which a value
    /// you can only see by altering the record cannot do.</para></summary>
    public string Alias { get; init; } = string.Empty;

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
    /// The commit button's label, matching the verb <see cref="Caption"/> names.
    ///
    /// <para>🔴 <b>Why a second property and not just the heading.</b> The commit control on every one of these
    /// master pages was the literal string <i>"Create (Ctrl+A)"</i>, on the alteration screen as well as the
    /// creation screen. The pointer path and the Ctrl+A chord now both branch on
    /// <see cref="IsAltering"/> — so pressing it on an alteration SAVES — but a button that says "Create" while
    /// it alters is the same lie the heading was, and it is the half an operator actually looks at before
    /// committing. Both are bound, so neither can drift from the verb that will run.</para>
    /// </summary>
    public string CommitLabel => IsAltering ? "Save (Ctrl+A)" : "Create (Ctrl+A)";

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
        vm.OnPropertyChanged(nameof(CommitLabel));
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
        Alias = centre.Alias ?? string.Empty;
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
                _editingId, Name, SelectedCategory.Id, SelectedParent?.Centre?.Id, Alias);
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
    /// <remarks>Alias is the SECOND column, beside the name it is an alternative for, and it is exported as well
    /// as displayed: a master list the operator exports should carry the same fields the screen shows, or the
    /// export quietly becomes a different report from the one on screen.</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Cost Centres",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Alias"),
            MasterListColumn.Text("Category"), MasterListColumn.Text("Under"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Alias, r.Category, r.Under }).ToList());

    /// <summary>The cost categories the Category picker offers (company order; Primary first).</summary>
    public ObservableCollection<CostCategory> Categories { get; } = new();

    /// <summary>The parent options for the chosen category: "Primary" plus every centre already in it.</summary>
    public ObservableCollection<ParentCentreOption> ParentOptions { get; } = new();

    /// <summary>The existing cost centres, refreshed after each create.</summary>
    public ObservableCollection<CostCentreListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>
    /// The centre's optional <b>alias</b> — a short alternative name (census 2.8).
    ///
    /// <para><b>Vendor-attested verbatim</b> on the Cost Centre screen: <i>"Name &amp; alias: Provide a name. As
    /// in other masters, you can specify multiple aliases."</i>
    /// (<c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>, read 2026-10-04).</para>
    ///
    /// <para>🔴 <b>DIVERGENCE, OURS, LABELLED:</b> the vendor permits <b>multiple</b> aliases per master; this
    /// build captures <b>one</b>, because <c>cost_centres.alias</c> is a single <c>TEXT</c> column
    /// (<c>Schema.cs:1474</c>) and widening it to a child table is a migration this slice does not own. One
    /// alias is our limit, not the vendor's rule.</para>
    ///
    /// <para><b>This is not a field nothing reads.</b> Two production paths already consume it and were waiting
    /// on the capture: <see cref="Company.FindCostCentreByName"/> resolves a centre by alias as well as by name
    /// (<c>Company.cs:1555-1557</c>), so a captured alias immediately becomes a working lookup key for import
    /// and for every name-resolution path; and the list column beside this form shows it back. An empty box is
    /// stored as <c>null</c>, never <c>""</c>, so a cleared alias cannot be matched by an empty search.</para>
    ///
    /// <para>🔴 <b>WHAT THIS SLICE DELIBERATELY DOES NOT DO, stated so the row is not read as closed.</b> The
    /// alias is <b>NOT checked for uniqueness</b> — not against other aliases and not against other masters'
    /// NAMES. So an operator can give centre B the alias "A" while a centre named "A" exists, and
    /// <see cref="Company.FindCostCentreByName"/>("A") then returns whichever of the two comes first in company
    /// order, because its predicate is <c>name == x || alias == x</c> evaluated per item.
    /// <b>This is pre-existing and product-wide, not introduced here:</b> stock group, stock category, godown,
    /// stock item, accounting group and employee group all capture an alias through the same unvalidated
    /// assignment (<c>InventoryService.cs:77, 182, 405</c>, <c>GroupService.cs:204</c>,
    /// <c>PayrollService.cs:372</c>). <b>It is NOT fixed here on purpose:</b> a cross-master alias-uniqueness
    /// rule is a decision over eight masters, it would newly refuse books that already hold colliding aliases,
    /// and <b>no vendor page states the rule</b> — the Cost Centre page says only that multiple aliases may be
    /// specified and is silent on collision. Inventing the rule here would be exactly the unsourced constraint
    /// this project has twice had to strip back out. It is reported as an open cross-cutting item and a user
    /// decision instead.</para>
    /// </summary>
    [ObservableProperty] private string _alias = string.Empty;

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

        // Normalised the same way the service normalises it on ALTER, so "created blank" and "cleared on alter"
        // store the identical null. Were this to store "", FindCostCentreByName's alias leg would match an empty
        // search string against every centre created without an alias.
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var centre = new CostCentre(Guid.NewGuid(), name, SelectedCategory.Id, parentId, alias);

        _company.AddCostCentre(centre);
        _storage.Save(_company);

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Centre!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Cost centre '{name}' created under {underLabel} ({SelectedCategory.Name}).";
        Name = string.Empty;
        // The alias must clear with the name. Leaving it on screen would hand the NEXT centre the previous
        // centre's short name — and because the alias is a lookup key, two centres sharing one would make
        // FindCostCentreByName's result depend on company order.
        Alias = string.Empty;
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
                Alias = centre.Alias ?? string.Empty,
                Category = category?.Name ?? "—",
                Under = under,
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
