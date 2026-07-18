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

    // Provident-Fund details (Phase 8 slice 4; RQ-9) — only meaningful (and only shown) while Payroll Statutory is
    // on. Marking a member PF-applicable requires a valid 12-digit UAN (^\d{12}$); the ECR keys the member on it.
    [ObservableProperty] private bool _pfApplicable;
    [ObservableProperty] private bool _pfContributeOnHigherWages;
    [ObservableProperty] private string _pfJoinDateText = string.Empty;

    // Employees'-State-Insurance details (Phase 8 slice 5; RQ-10) — only meaningful (and only shown) while Payroll
    // Statutory is on. Marking a member ESI-applicable requires a valid 10-digit IP / Insurance Number (^\d{10}$);
    // the monthly contribution file keys the member on it (NOT the 17-digit establishment employer code). A
    // person-with-disability enjoys the higher ₹25,000 coverage ceiling (against the ordinary ₹21,000).
    [ObservableProperty] private bool _esiApplicable;
    [ObservableProperty] private bool _esiPersonWithDisability;

    [ObservableProperty] private string? _message;

    /// <summary>True iff the per-employee PF details block should render — only while Payroll Statutory is on. A
    /// non-statutory company never sees the PF fields (ER-13). Fixed for the page's lifetime (the company's
    /// statutory flag does not change while an Employee-master page column is open).</summary>
    public bool ShowPfDetails => _company.PayrollStatutoryEnabled;

    /// <summary>True iff the per-employee ESI details block should render — only while Payroll Statutory is on. A
    /// non-statutory company never sees the ESI fields (ER-13).</summary>
    public bool ShowEsiDetails => _company.PayrollStatutoryEnabled;

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
        if (!TryParseOptionalDate(PfJoinDateText, "PF join date", out var pfJoin)) return false;

        // A PF-applicable member is keyed on its 12-digit UAN — pre-validate BEFORE creating so a bad value is a
        // friendly message and never leaves a half-created employee on the in-memory company.
        var wantsPf = PfApplicable && ShowPfDetails;
        if (wantsPf && !IsTwelveDigits(Uan))
        {
            Message = "A PF-applicable employee needs a valid 12-digit UAN (e.g. 100000000001).";
            return false;
        }

        // An ESI-applicable member is keyed on its 10-digit IP / Insurance Number — pre-validate the same way.
        var wantsEsi = EsiApplicable && ShowEsiDetails;
        if (wantsEsi && !IsTenDigits(EsiNumber))
        {
            Message = "An ESI-applicable employee needs a valid 10-digit IP number (e.g. 3100123456).";
            return false;
        }

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
            // PF details (re-validates the UAN as a backstop; throws a friendly message if it slips through).
            if (wantsPf)
                service.SetEmployeePfDetails(employee.Id, applicable: true,
                    contributeOnHigherWages: PfContributeOnHigherWages, pfJoinDate: pfJoin);
            // ESI details (re-validates the 10-digit IP number as a backstop); a person-with-disability gets the
            // higher ₹25,000 coverage ceiling.
            if (wantsEsi)
                service.SetEmployeeEsiDetails(employee.Id, applicable: true, personWithDisability: EsiPersonWithDisability);
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
        PfApplicable = false;
        PfContributeOnHigherWages = false;
        PfJoinDateText = string.Empty;
        EsiApplicable = false;
        EsiPersonWithDisability = false;
        SelectedCategory = CategoryOptions.FirstOrDefault();
        SelectedRegime = Regimes.First();
        _onChanged();
        return true;
    }

    /// <summary>True iff <paramref name="value"/> is exactly 12 digits (the EPFO UAN shape, <c>^\d{12}$</c>).</summary>
    private static bool IsTwelveDigits(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length != 12) return false;
        foreach (var ch in t) if (ch is < '0' or > '9') return false;
        return true;
    }

    /// <summary>True iff <paramref name="value"/> is exactly 10 digits (the ESIC IP-number shape, <c>^\d{10}$</c>).</summary>
    private static bool IsTenDigits(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length != 10) return false;
        foreach (var ch in t) if (ch is < '0' or > '9') return false;
        return true;
    }

    /// <summary>Parses an optional yyyy-MM-dd date; blank ⇒ null (ok). On a malformed value, sets a friendly
    /// <see cref="Message"/> naming the field and returns false so <see cref="Create"/> aborts without crashing.</summary>
    private bool TryParseOptionalDate(string? text, string fieldLabel, out DateOnly? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;
        // WI-5: shared DAY-FIRST parse (was a bare InvariantCulture parse — the MM/dd misread).
        if (!ApexDate.TryParse(trimmed, out var parsed))
        {
            Message = $"{fieldLabel}: {ApexDate.ErrorFor(trimmed)}";
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
