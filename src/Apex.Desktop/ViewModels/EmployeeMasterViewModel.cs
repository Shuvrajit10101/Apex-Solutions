using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An employee row for the existing-employees list on the master screen.</summary>
public sealed class EmployeeListRow
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string Regime { get; init; } = string.Empty;
}

/// <summary>An employee-group picker option (required — an employee must belong to a group).</summary>
public sealed class EmployeeGroupOption
{
    public EmployeeGroup Group { get; init; } = null!;
    public string Display { get; init; } = string.Empty;
}

/// <summary>An employee-category picker option: "None" or an existing category (optional classification).</summary>
public sealed class EmployeeCategoryOption
{
    public EmployeeCategory? Category { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Category is null;
}

/// <summary>An income-tax regime picker option (New / Old — the employee's §192 election, DP-2).</summary>
public sealed class TaxRegimeOption
{
    public TaxRegime Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The Employee creation master ("Masters → Create → Payroll Masters → Employee"; Phase 8 slice 1; RQ-2). An
/// employee is a distinct payroll master (mirrors a party <see cref="Ledger"/>) that sits under a <b>required</b>
/// <see cref="EmployeeGroup"/> and an optional <see cref="EmployeeCategory"/>. Captures the full identity +
/// statutory-identifier + bank field set the model supports (RQ-2): employee number, date of joining, designation,
/// gender, PAN / UAN / ESI number (structurally validated at the engine boundary when set), the applicable
/// income-tax regime, and — under a "Location, bank &amp; statutory details" expander — date of birth, location,
/// function, Aadhaar, PF account number and bank account/name/IFSC (consumed by the later PF/ESI and
/// salary-payment slices). Creates via the <see cref="PayrollService"/> (unique name; existing group/category;
/// PAN/UAN/ESI validation) and persists.
///
/// <para>Only reachable when Payroll is enabled. The group picker is required — the screen tells the user to
/// create an Employee Group first when none exist. MVVM boundary: references the domain + persistence but no
/// Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class EmployeeMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Employees",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Group"),
            MasterListColumn.Text("Designation"), MasterListColumn.Text("Regime"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Group, r.Designation, r.Regime }).ToList());

    /// <summary>The employee-group options (required picker); empty until at least one group exists.</summary>
    public ObservableCollection<EmployeeGroupOption> GroupOptions { get; } = new();

    /// <summary>The employee-category options: "None" plus every existing category.</summary>
    public ObservableCollection<EmployeeCategoryOption> CategoryOptions { get; } = new();

    /// <summary>The gender options (blank / Male / Female / Other).</summary>
    public ObservableCollection<string> GenderOptions { get; } = new() { "—", "Male", "Female", "Other" };

    /// <summary>The income-tax regime options (New / Old).</summary>
    public ObservableCollection<TaxRegimeOption> Regimes { get; } = new();

    /// <summary>The existing employees, refreshed after each create.</summary>
    public ObservableCollection<EmployeeListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private EmployeeGroupOption? _selectedGroup;
    [ObservableProperty] private EmployeeCategoryOption? _selectedCategory;
    [ObservableProperty] private string _employeeNumber = string.Empty;
    [ObservableProperty] private string _dateOfJoiningText = string.Empty;
    [ObservableProperty] private string _designation = string.Empty;
    [ObservableProperty] private string? _selectedGender;
    [ObservableProperty] private string _pan = string.Empty;
    [ObservableProperty] private string _uan = string.Empty;
    [ObservableProperty] private string _esiNumber = string.Empty;
    [ObservableProperty] private TaxRegimeOption? _selectedRegime;

    // Additional statutory / identity / bank fields (RQ-2 "all identity/statutory/bank fields"; consumed by the
    // later PF/ECR, ESI and salary-payment bank-allocation slices). All optional and free-set on the entity.
    [ObservableProperty] private string _dateOfBirthText = string.Empty;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _function = string.Empty;
    [ObservableProperty] private string _aadhaar = string.Empty;
    [ObservableProperty] private string _pfAccountNumber = string.Empty;
    [ObservableProperty] private string _bankAccountNumber = string.Empty;
    [ObservableProperty] private string _bankName = string.Empty;
    [ObservableProperty] private string _bankIfsc = string.Empty;

    [ObservableProperty] private string? _message;

    public EmployeeMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Regimes.Add(new TaxRegimeOption { Value = TaxRegime.New, Display = "New Regime" });
        Regimes.Add(new TaxRegimeOption { Value = TaxRegime.Old, Display = "Old Regime" });
        SelectedRegime = Regimes.First();
        SelectedGender = GenderOptions.First();

        RefreshPickers();
        RefreshList();
    }

    /// <summary>True once at least one employee group exists (a group is required to create an employee).</summary>
    public bool CanCreate => GroupOptions.Count > 0;

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty and a group is chosen, parses an optional date of
    /// joining, then creates the employee via the engine (which validates uniqueness, the group/category
    /// references and any PAN/UAN/ESI supplied), sets the remaining descriptive fields, and persists. Any
    /// domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "An employee name is required.";
            return false;
        }
        if (SelectedGroup is null)
        {
            Message = "Pick an Employee Group (create one first under Payroll Masters → Employee Group).";
            return false;
        }
        if (!TryParseOptionalDate(DateOfJoiningText, "Date of joining", out var doj)) return false;
        if (!TryParseOptionalDate(DateOfBirthText, "Date of birth", out var dob)) return false;

        try
        {
            var service = new PayrollService(_company);
            var employee = service.CreateEmployee(
                name,
                SelectedGroup.Group.Id,
                SelectedCategory?.Category?.Id,
                BlankToNull(EmployeeNumber),
                BlankToNull(Pan),
                BlankToNull(Uan),
                BlankToNull(EsiNumber),
                doj);

            employee.Designation = BlankToNull(Designation);
            employee.Gender = SelectedGender is null or "—" ? null : SelectedGender;
            employee.ApplicableTaxRegime = (SelectedRegime ?? Regimes.First()).Value;
            employee.DateOfBirth = dob;
            employee.Location = BlankToNull(Location);
            employee.Function = BlankToNull(Function);
            employee.Aadhaar = BlankToNull(Aadhaar);
            employee.PfAccountNumber = BlankToNull(PfAccountNumber);
            employee.BankAccountNumber = BlankToNull(BankAccountNumber);
            employee.BankName = BlankToNull(BankName);
            employee.BankIfsc = BlankToNull(BankIfsc);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Employee '{name}' created under {SelectedGroup.Group.Name}.";
        Name = string.Empty;
        EmployeeNumber = string.Empty;
        DateOfJoiningText = string.Empty;
        Designation = string.Empty;
        SelectedGender = GenderOptions.First();
        Pan = string.Empty;
        Uan = string.Empty;
        EsiNumber = string.Empty;
        DateOfBirthText = string.Empty;
        Location = string.Empty;
        Function = string.Empty;
        Aadhaar = string.Empty;
        PfAccountNumber = string.Empty;
        BankAccountNumber = string.Empty;
        BankName = string.Empty;
        BankIfsc = string.Empty;
        SelectedCategory = CategoryOptions.FirstOrDefault();
        SelectedRegime = Regimes.First();
        _onChanged();
        return true;
    }

    /// <summary>Parses an optional yyyy-MM-dd date; blank ⇒ null (ok). On a malformed value, sets a friendly
    /// <see cref="Message"/> naming the field and returns false so <see cref="Create"/> aborts without crashing.</summary>
    private bool TryParseOptionalDate(string? text, string fieldLabel, out DateOnly? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;
        if (!DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            Message = $"{fieldLabel} must be a valid date (e.g. 2025-04-01), or blank.";
            return false;
        }
        value = parsed;
        return true;
    }

    private void RefreshPickers()
    {
        var previousGroupId = SelectedGroup?.Group.Id;
        GroupOptions.Clear();
        foreach (var g in _company.EmployeeGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            GroupOptions.Add(new EmployeeGroupOption { Group = g, Display = g.Name });
        SelectedGroup = GroupOptions.FirstOrDefault(o => o.Group.Id == previousGroupId)
                        ?? GroupOptions.FirstOrDefault();

        var previousCatId = SelectedCategory?.Category?.Id;
        CategoryOptions.Clear();
        CategoryOptions.Add(new EmployeeCategoryOption { Category = null, Display = "◦ None" });
        foreach (var c in _company.EmployeeCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            CategoryOptions.Add(new EmployeeCategoryOption { Category = c, Display = c.Name });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Category?.Id == previousCatId)
                           ?? CategoryOptions.FirstOrDefault();

        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var e in _company.Employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var group = _company.FindEmployeeGroup(e.EmployeeGroupId)?.Name ?? "—";
            Existing.Add(new EmployeeListRow
            {
                Name = e.Name,
                Group = group,
                Designation = string.IsNullOrWhiteSpace(e.Designation) ? "—" : e.Designation!,
                Regime = e.ApplicableTaxRegime == TaxRegime.Old ? "Old" : "New",
            });
        }
    }

    private static string? BlankToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
