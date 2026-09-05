using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An employee-group row for the existing-groups list on the master screen.</summary>
public sealed partial class EmployeeGroupListRow : ObservableObject, IPayrollMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Salary { get; init; } = string.Empty;

    /// <summary>The stable identity of the group this row displays (census 7.16).</summary>
    public Guid MasterId { get; init; }

    string IMasterListRow.MasterName => Name;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// One entry in the "Under" parent picker for an employee group: "Primary" (top-level, no parent) or any
/// existing employee group. <see cref="Group"/> is null for the Primary option.
/// </summary>
public sealed class ParentEmployeeGroupOption
{
    public EmployeeGroup? Group { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Group is null;
}

/// <summary>
/// The Employee-Group creation master ("Masters → Create → Payroll Masters → Employee Group"; Phase 8 slice 1;
/// RQ-2): a name, an optional alias, an optional <b>Under</b> parent (Primary ⇒ top-level, or nest under an
/// existing group — the hierarchical department/division tree, mirroring <see cref="Group"/>), and the
/// <b>"Define salary details?"</b> flag (captured now; consumed by the later salary-structure slice). Creates
/// the group via the <see cref="PayrollService"/> (which enforces unique name + valid, non-cyclic parent) and
/// persists.
///
/// <para>Only reachable when Payroll is enabled. MVVM boundary: references the domain + persistence but no
/// Avalonia/UI types, so it is headlessly unit-testable. Mirrors <see cref="StockGroupMasterViewModel"/>.</para>
/// </summary>
public sealed partial class EmployeeGroupMasterViewModel : ViewModelBase, IMasterListExportSource, IPayrollMasterList
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly PayrollMasterHighlight<EmployeeGroupListRow> _highlight;

    /// <summary>The id of the group being ALTERED, or <see cref="Guid.Empty"/> in Create mode (7.16).</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering ? "Employee Group Alteration" : "Employee Group Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "employee group";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-group row, or <c>null</c>.</summary>
    public EmployeeGroupListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() { RefreshParentOptions(); RefreshList(); }

    /// <inheritdoc/>
    public void DeleteMaster(Guid id) => new PayrollService(_company).DeleteEmployeeGroup(id);

    /// <summary>Opens this master in <b>Alter</b> mode over an existing group — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve.</summary>
    public static EmployeeGroupMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid groupId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindEmployeeGroup(groupId) is not { } group) return null;

        var vm = new EmployeeGroupMasterViewModel(company, storage, onChanged);
        vm._editingId = groupId;
        vm.Name = group.Name;
        vm.Alias = group.Alias ?? string.Empty;
        vm.DefineSalaryDetails = group.DefineSalaryDetails;
        // A group may not be its own parent, and offering itself in the picker is how an operator finds that out
        // by being refused. Take it out of the list instead — the engine's cycle guard still has the last word.
        var self = vm.ParentOptions.FirstOrDefault(o => o.Group?.Id == groupId);
        if (self is not null) vm.ParentOptions.Remove(self);
        vm.SelectedParent = vm.ParentOptions.FirstOrDefault(o => o.Group?.Id == group.ParentId)
                            ?? vm.ParentOptions.FirstOrDefault();
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Employee Groups",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Under"), MasterListColumn.Text("Salary") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Salary }).ToList());

    /// <summary>The parent options: "Primary" plus every existing employee group.</summary>
    public ObservableCollection<ParentEmployeeGroupOption> ParentOptions { get; } = new();

    /// <summary>The existing employee groups, refreshed after each create.</summary>
    public ObservableCollection<EmployeeGroupListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentEmployeeGroupOption? _selectedParent;
    [ObservableProperty] private bool _defineSalaryDetails;
    [ObservableProperty] private string? _message;

    public EmployeeGroupMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<EmployeeGroupListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });

        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the employee group under the chosen parent
    /// (Primary ⇒ top-level) via the engine and persists. The engine also enforces uniqueness + a valid,
    /// non-cyclic parent; any domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "An employee group name is required.";
            return false;
        }

        var parentId = SelectedParent?.Group?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();

        // 7.16 — the SAME Ctrl+A runs the verb the screen is in (see the Caption the operator is reading).
        var altering = IsAltering;
        try
        {
            var service = new PayrollService(_company);
            if (altering)
                service.AlterEmployeeGroup(_editingId, name, parentId, alias, DefineSalaryDetails);
            else
                service.CreateEmployeeGroup(name, parentId, alias, DefineSalaryDetails);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Group!.Name : "Primary";
        if (altering)
        {
            RefreshList();
            Message = $"Employee group '{name}' altered (under {underLabel}).";
            _onChanged();
            return true;
        }

        RefreshParentOptions();
        RefreshList();
        Message = $"Employee group '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        DefineSalaryDetails = false;
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Group?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentEmployeeGroupOption { Group = null, Display = "◦ Primary (top-level)" });
        foreach (var g in _company.EmployeeGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentEmployeeGroupOption { Group = g, Display = g.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Group?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        // By id, never by index — an alter that renames or re-parents a group re-orders the list, and an
        // index-restored highlight would land on a NEIGHBOURING master that the next Alt+D would delete.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var g in _company.EmployeeGroups)
        {
            var under = g.ParentId is { } pid
                ? _company.FindEmployeeGroup(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new EmployeeGroupListRow
            {
                MasterId = g.Id,
                Name = g.Name,
                Under = under,
                Salary = g.DefineSalaryDetails ? "Defined" : "—",
            });
        }

        _highlight.RestoreTo(previous);
    }
}
