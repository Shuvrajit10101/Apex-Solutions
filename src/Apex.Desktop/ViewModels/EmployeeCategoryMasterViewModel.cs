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
public sealed class EmployeeCategoryListRow
{
    public string Name { get; init; } = string.Empty;
    public string Allocates { get; init; } = string.Empty;
    public string Employees { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
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
public sealed partial class EmployeeCategoryMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

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

        try
        {
            var service = new PayrollService(_company);
            service.CreateEmployeeCategory(name, AllocateRevenueItems, AllocateNonRevenueItems);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Employee category '{name}' created.";
        Name = string.Empty;
        AllocateRevenueItems = true;
        AllocateNonRevenueItems = false;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
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
                Name = c.Name,
                Allocates = allocates,
                Employees = count == 0 ? "—" : count.ToString(),
                Kind = c.IsPredefined ? "Predefined" : "User",
            });
        }
    }
}
