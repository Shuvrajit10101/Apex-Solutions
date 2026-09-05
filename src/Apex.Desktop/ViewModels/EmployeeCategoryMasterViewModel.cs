using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An employee-category row for the existing-categories list on the master screen.</summary>
public sealed partial class EmployeeCategoryListRow : ObservableObject, IPayrollMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Allocates { get; init; } = string.Empty;
    public string Employees { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;

    /// <summary>The stable identity of the category this row displays — see <see cref="IPayrollMasterListRow"/>
    /// for why the payroll lists shipping without one is the whole of census row 7.16's reach problem.</summary>
    public Guid MasterId { get; init; }

    string IMasterListRow.MasterName => Name;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The Employee-Category creation master ("Masters → Create → Payroll Masters → Employee Category"; Phase 8
/// slice 1; RQ-2). An employee category is the parallel workforce-classification axis (mirrors
/// <see cref="CostCategory"/>) — pick a name, create it on the current company via the
/// <see cref="PayrollService"/> (which enforces a unique name), and see it appear in the list. Persists the
/// company to its <c>.db</c> via <see cref="CompanyStorage.Save"/> on create.
///
/// <para>Only reachable when Payroll is enabled (the Create-menu item is gated on
/// <see cref="Company.PayrollEnabled"/>). MVVM boundary: references the domain + persistence but no
/// Avalonia/UI types, so it is headlessly unit-testable. Mirrors <see cref="CostCategoryMasterViewModel"/>.</para>
/// </summary>
public sealed partial class EmployeeCategoryMasterViewModel : ViewModelBase, IMasterListExportSource, IPayrollMasterList
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly PayrollMasterHighlight<EmployeeCategoryListRow> _highlight;

    /// <summary>The id of the category being ALTERED, or <see cref="Guid.Empty"/> in Create mode (7.16).</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering ? "Employee Category Alteration" : "Employee Category Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "employee category";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-category row, or <c>null</c>. Ctrl+Enter on it opens the alteration.</summary>
    public EmployeeCategoryListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    public void DeleteMaster(Guid id) => new PayrollService(_company).DeleteEmployeeCategory(id);

    /// <summary>Opens this master in <b>Alter</b> mode over an existing category — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve.</summary>
    public static EmployeeCategoryMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid categoryId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindEmployeeCategory(categoryId) is not { } category) return null;

        var vm = new EmployeeCategoryMasterViewModel(company, storage, onChanged);
        vm._editingId = categoryId;
        vm.Name = category.Name;
        vm.AllocateRevenueItems = category.AllocateRevenueItems;
        vm.AllocateNonRevenueItems = category.AllocateNonRevenueItems;
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Employee Categories",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Allocates"),
            MasterListColumn.Text("Employees"), MasterListColumn.Text("Kind"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Allocates, r.Employees, r.Kind }).ToList());

    /// <summary>The existing employee categories, refreshed after each create.</summary>
    public ObservableCollection<EmployeeCategoryListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>"Allocate Revenue Items" (RQ-2) — may allocate P&amp;L (income/expense) lines. On by default.</summary>
    [ObservableProperty] private bool _allocateRevenueItems = true;

    /// <summary>"Allocate Non-Revenue Items" (RQ-2) — may allocate balance-sheet lines. Off by default.</summary>
    [ObservableProperty] private bool _allocateNonRevenueItems;

    [ObservableProperty] private string? _message;

    public EmployeeCategoryMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<EmployeeCategoryListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty and at least one allocation flag is on, then creates the
    /// category via the engine (which also enforces uniqueness + the "≥1 must be Yes" invariant) and persists.
    /// Any domain error is surfaced to <see cref="Message"/> without crashing the UI. Refreshes the list and
    /// resets the entry fields for the next entry.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "An employee category name is required.";
            return false;
        }
        if (!AllocateRevenueItems && !AllocateNonRevenueItems)
        {
            Message = "An employee category must allocate revenue and/or non-revenue items (at least one must be Yes).";
            return false;
        }

        // 7.16 — the SAME Ctrl+A runs the verb the screen is in. Branching here rather than adding a second
        // shell route means the accept key can never disagree with the caption the operator is reading.
        var altering = IsAltering;
        try
        {
            var service = new PayrollService(_company);
            if (altering)
                service.AlterEmployeeCategory(_editingId, name, AllocateRevenueItems, AllocateNonRevenueItems);
            else
                service.CreateEmployeeCategory(name, AllocateRevenueItems, AllocateNonRevenueItems);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        if (altering)
        {
            // The form stays on the altered category — clearing it here would read as "saved and gone".
            Message = $"Employee category '{name}' altered.";
            _onChanged();
            return true;
        }

        Message = $"Employee category '{name}' created.";
        Name = string.Empty;
        AllocateRevenueItems = true;
        AllocateNonRevenueItems = false;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        // Keep the highlight on the SAME category across a rebuild — by id, never by index: an alter that renames
        // a category re-sorts the list, and an index-restored highlight would land on a NEIGHBOURING master that
        // the next Ctrl+Enter would open and the next Alt+D would delete.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var c in _company.EmployeeCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = _company.Employees.Count(e => e.EmployeeCategoryId == c.Id);
            var allocates = (c.AllocateRevenueItems, c.AllocateNonRevenueItems) switch
            {
                (true, true) => "Revenue + Non-Revenue",
                (true, false) => "Revenue",
                (false, true) => "Non-Revenue",
                _ => "—",
            };
            Existing.Add(new EmployeeCategoryListRow
            {
                MasterId = c.Id,
                Name = c.Name,
                Allocates = allocates,
                Employees = count == 0 ? "—" : count.ToString(),
                Kind = c.IsPredefined ? "Predefined" : "User",
            });
        }

        _highlight.RestoreTo(previous);
    }
}
