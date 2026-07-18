using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An accounting-group row for the existing-groups list on the Group-master screen.</summary>
public sealed class AccountGroupListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Nature { get; init; } = string.Empty;
}

/// <summary>
/// The accounting-Group creation master ("Masters → Create → Group", Gateway → Create → Group; catalog §3; WI-7):
/// a name, an optional alias, and an <b>Under</b> parent picked from the 28 predefined groups (Current Assets /
/// Current Liabilities / … ) or any custom group. The <b>Nature is shown READ-ONLY and DERIVED from the parent</b>
/// (<see cref="DerivedNature"/>) — the user never types or chooses a nature, exactly as Tally derives Asset /
/// Liability / Income / Expense from the parent's primary ancestor. Creates the group via
/// <see cref="GroupService.CreateGroup"/> (unique name, existing parent, derived nature) and persists.
///
/// <para>This is what point 9's first half needs: a "Salary Payable" group under Current Liabilities holding one
/// ledger per employee — a payable that prints on the Balance-Sheet liabilities side. Schema, Io and report
/// classification already handle custom groups, so this is a pure UI + engine-service slice.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="StockGroupMasterViewModel"/>.</para>
/// </summary>
public sealed partial class AccountGroupMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Groups",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Under"), MasterListColumn.Text("Nature") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Nature }).ToList());

    /// <summary>The parent options: every existing group (28 predefined roots + any custom), name-sorted.</summary>
    public ObservableCollection<Group> ParentOptions { get; } = new();

    /// <summary>The existing groups, refreshed after each create.</summary>
    public ObservableCollection<AccountGroupListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private Group? _selectedParent;
    [ObservableProperty] private string? _message;

    public AccountGroupMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();

        // Default the Under to Current Liabilities (the WI-7 driving example — a Salary Payable head), else the
        // first group. The picker holds the actual domain instances, so this reference-matches the ComboBox item.
        SelectedParent = _company.FindGroupByName("Current Liabilities") ?? ParentOptions.FirstOrDefault();
    }

    /// <summary>
    /// The nature the new group WILL inherit from the chosen parent, shown READ-ONLY. The user never picks a
    /// nature — Tally derives Asset / Liability / Income / Expense from the parent's primary ancestor. "—" when no
    /// parent is chosen.
    /// </summary>
    public string DerivedNature => SelectedParent is { } p
        ? GroupService.DeriveNature(p, _company).ToString()
        : "—";

    partial void OnSelectedParentChanged(Group? value) => OnPropertyChanged(nameof(DerivedNature));

    /// <summary>
    /// Ctrl+A create: validates a non-empty name and a chosen parent, then creates the group under that parent via
    /// the engine (which derives the nature, enforces a unique name and an existing parent) and persists. Any
    /// domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A group name is required.";
            return false;
        }
        if (SelectedParent is null)
        {
            Message = "Pick an Under (parent) group — the nature is derived from it.";
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var parentName = SelectedParent.Name;
        var nature = DerivedNature;

        try
        {
            var service = new GroupService(_company);
            service.CreateGroup(name, SelectedParent.Id, alias);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshParentOptions();   // the new group is now selectable as a parent
        RefreshList();
        Message = $"Group '{name}' created under {parentName} ({nature}).";
        Name = string.Empty;
        Alias = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Id;
        ParentOptions.Clear();
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(g);

        // Keep the chosen parent selected across a refresh (so the user can add several groups under one head).
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Id == previousId) ?? SelectedParent;
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var under = g.ParentId is { } pid ? _company.FindGroup(pid)?.Name ?? "—" : "Primary";
            Existing.Add(new AccountGroupListRow
            {
                Name = g.Name,
                Under = under,
                // The classification the reports use — the group's primary-ancestor nature.
                Nature = ClassificationRules.PrimaryNatureOf(g, _company).ToString(),
            });
        }
    }
}
