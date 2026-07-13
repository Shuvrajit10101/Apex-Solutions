using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One selectable employee on the Tax-Declaration screen: the stable id, name + number, PAN and the elected
/// income-tax regime label. Selecting it loads that employee's Form-12BB declaration into the editable fields.</summary>
public sealed partial class TaxDeclarationEmployeeVm : ViewModelBase
{
    public Guid EmployeeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Pan { get; init; } = string.Empty;
    public string Regime { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The per-employee <b>Income-Tax Declaration (Form 12BB)</b> screen (Masters → Create → Payroll Masters → Income Tax
/// Declaration; Phase 8 slice 7; RQ-12; catalog §14). The investment / exemption / prior-income figures an employee
/// declares so the §192 engine can estimate the salary TDS. Pick an employee (its elected regime + PAN + age band
/// show read-only), enter the declared figures and <b>Save</b> (Ctrl+A) — the declaration round-trips through
/// <see cref="Company.AddTaxDeclaration"/> and persists. The <b>old-regime</b> Chapter-VI-A / HRA / §24(b) fields are
/// only relevant to an old-regime employee (a note surfaces this); the <b>new regime</b> uses only §80CCD(2)
/// employer-NPS + the standard deduction, so the other fields are captured but ignored by the engine.
///
/// <para>Gated: only reachable when §192 salary TDS is enabled (the menu item + the open path are gated on
/// <see cref="Company.SalaryTdsEnabled"/>), so a non-salary-TDS company never reaches it (ER-13). MVVM boundary:
/// engine + persistence only, no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class TaxDeclarationViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "Income Tax Declaration (Form 12BB)";
    [ObservableProperty] private string _subtitle = string.Empty;

    // The selected employee's read-only identity + regime.
    [ObservableProperty] private string _regimeText = string.Empty;
    [ObservableProperty] private string _panText = string.Empty;
    [ObservableProperty] private string _ageBandText = string.Empty;
    [ObservableProperty] private string _regimeNote = string.Empty;
    [ObservableProperty] private bool _isOldRegime;

    // Declared figures (decimal rupees). Old-regime Chapter VI-A / exemptions + the both-regime employer-NPS +
    // other-income / previous-employer carry-in. Blank ⇒ ₹0.
    [ObservableProperty] private string _section80CText = string.Empty;
    [ObservableProperty] private string _section80DText = string.Empty;
    [ObservableProperty] private string _section80CCD1BText = string.Empty;
    [ObservableProperty] private string _section80CCD2EmployerText = string.Empty;
    [ObservableProperty] private string _houseRentAllowanceExemptText = string.Empty;
    [ObservableProperty] private string _homeLoanInterest24bText = string.Empty;
    [ObservableProperty] private string _otherIncomeText = string.Empty;
    [ObservableProperty] private string _previousEmployerSalaryText = string.Empty;
    [ObservableProperty] private string _previousEmployerTdsText = string.Empty;

    [ObservableProperty] private bool _hasEmployee;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private int _highlightedIndex = -1;

    private TaxDeclarationEmployeeVm? _selectedEmployee;

    /// <summary>The FY-end used to resolve the old-regime senior-citizen age band (only the first nil band shifts).</summary>
    private readonly DateOnly _fyEnd;

    /// <summary>The employees to capture a declaration for (name-ordered, selectable in a ListBox).</summary>
    public ObservableCollection<TaxDeclarationEmployeeVm> Employees { get; } = new();

    public TaxDeclarationViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        var fyStart = _company.FinancialYearStart;
        _fyEnd = new DateOnly(fyStart.Year + 1, 3, 31);
        Subtitle = $"{_company.Name}  —  FY {fyStart.Year}-{(fyStart.Year + 1) % 100:00}  ·  "
                   + "declared investments / exemptions drive the §192 salary-TDS estimate";

        foreach (var e in _company.Employees.OrderBy(e => e.Name, StringComparer.Ordinal))
            Employees.Add(new TaxDeclarationEmployeeVm
            {
                EmployeeId = e.Id,
                Name = e.Name,
                Number = string.IsNullOrWhiteSpace(e.EmployeeNumber) ? "—" : e.EmployeeNumber!,
                Pan = string.IsNullOrWhiteSpace(e.Pan) ? "—" : e.Pan!,
                Regime = e.ApplicableTaxRegime == TaxRegime.Old ? "Old" : "New",
            });

        HighlightedIndex = Employees.Count > 0 ? 0 : -1;
        SelectedEmployee = Employees.FirstOrDefault();
    }

    /// <summary>The employee whose declaration is being edited; changing it loads that employee's saved figures and
    /// keeps the list highlight in sync (whether the change came from the list or a direct assignment).</summary>
    public TaxDeclarationEmployeeVm? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (!SetProperty(ref _selectedEmployee, value)) return;
            LoadDeclaration();
            var idx = value is null ? -1 : Employees.IndexOf(value);
            if (HighlightedIndex != idx) HighlightedIndex = idx; // no recursion: OnHighlightedIndexChanged re-sets the same value
        }
    }

    /// <summary>Moves the employee highlight (Up/Down within the page); wraps. Keeps a live ListBox selection.</summary>
    public void MoveHighlight(int direction)
    {
        if (Employees.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Employees.Count) % Employees.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Employees.Count; i++)
            Employees[i].IsHighlighted = i == value;
        if (value >= 0 && value < Employees.Count) SelectedEmployee = Employees[value];
    }

    /// <summary>Loads the selected employee's persisted declaration (all ₹0 when none) into the editable fields and
    /// refreshes the read-only regime / PAN / age-band context.</summary>
    private void LoadDeclaration()
    {
        Message = null;
        var sel = SelectedEmployee;
        if (sel is null || _company.FindEmployee(sel.EmployeeId) is not { } emp)
        {
            HasEmployee = false;
            RegimeText = PanText = AgeBandText = RegimeNote = string.Empty;
            StatusText = "No employees yet — add an employee before capturing a declaration.";
            return;
        }

        HasEmployee = true;
        IsOldRegime = emp.ApplicableTaxRegime == TaxRegime.Old;
        RegimeText = IsOldRegime ? "Old Regime" : "New Regime (default)";
        PanText = string.IsNullOrWhiteSpace(emp.Pan) ? "PANNOTAVBL — §206AA 20% floor applies" : emp.Pan!;
        AgeBandText = AgeBandLabel(SalaryIncomeTax.AgeBandFor(emp.DateOfBirth, _fyEnd));
        RegimeNote = IsOldRegime
            ? "Old regime — the declared Chapter VI-A deductions, HRA exemption and §24(b) interest below reduce taxable income."
            : "New regime — only §80CCD(2) employer-NPS + the standard deduction apply; the other declared figures are captured but ignored.";

        var d = _company.FindTaxDeclaration(sel.EmployeeId);
        Section80CText = R(d?.Section80C);
        Section80DText = R(d?.Section80D);
        Section80CCD1BText = R(d?.Section80CCD1B);
        Section80CCD2EmployerText = R(d?.Section80CCD2Employer);
        HouseRentAllowanceExemptText = R(d?.HouseRentAllowanceExempt);
        HomeLoanInterest24bText = R(d?.HomeLoanInterest24b);
        OtherIncomeText = R(d?.OtherIncome);
        PreviousEmployerSalaryText = R(d?.PreviousEmployerSalary);
        PreviousEmployerTdsText = R(d?.PreviousEmployerTds);

        StatusText = d is null
            ? "No declaration on file — all figures default to ₹0."
            : "Declaration loaded — edit and Save (Ctrl+A) to update.";
    }

    /// <summary>
    /// Ctrl+A save: parses every declared figure (blank ⇒ ₹0; rejects a negative / non-numeric value with a friendly
    /// message), builds the <see cref="TaxDeclaration"/> for the selected employee, attaches it via
    /// <see cref="Company.AddTaxDeclaration"/> (replacing any prior row) and persists. Returns true on success.
    /// </summary>
    public bool Save()
    {
        Message = null;
        var sel = SelectedEmployee;
        if (sel is null) { Message = "Pick an employee first."; return false; }

        if (!TryMoney(Section80CText, "80C", out var c80) ||
            !TryMoney(Section80DText, "80D", out var d80) ||
            !TryMoney(Section80CCD1BText, "80CCD(1B)", out var ccd1b) ||
            !TryMoney(Section80CCD2EmployerText, "80CCD(2)", out var ccd2) ||
            !TryMoney(HouseRentAllowanceExemptText, "HRA exemption", out var hra) ||
            !TryMoney(HomeLoanInterest24bText, "§24(b) interest", out var loan) ||
            !TryMoney(OtherIncomeText, "other income", out var other) ||
            !TryMoney(PreviousEmployerSalaryText, "previous-employer salary", out var prevSal) ||
            !TryMoney(PreviousEmployerTdsText, "previous-employer TDS", out var prevTds))
            return false;

        var decl = new TaxDeclaration
        {
            EmployeeId = sel.EmployeeId,
            Section80C = c80,
            Section80D = d80,
            Section80CCD1B = ccd1b,
            Section80CCD2Employer = ccd2,
            HouseRentAllowanceExempt = hra,
            HomeLoanInterest24b = loan,
            OtherIncome = other,
            PreviousEmployerSalary = prevSal,
            PreviousEmployerTds = prevTds,
        };

        try
        {
            _company.AddTaxDeclaration(decl);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        StatusText = $"Declaration saved for {sel.Name} — the §192 estimate will use it on the next pay run.";
        Message = null;
        _onChanged();
        return true;
    }

    /// <summary>Parses a declared rupee figure: blank ⇒ ₹0; a negative or non-numeric value fails with a message.</summary>
    private bool TryMoney(string? text, string fieldLabel, out Money value)
    {
        value = Money.Zero;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var cleaned = text.Trim().Replace(",", string.Empty);
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            Message = $"'{text}' is not a valid amount for {fieldLabel} (enter a non-negative number of rupees).";
            return false;
        }
        value = new Money(amount);
        return true;
    }

    private static string R(Money? m) => m is null || m.Value.Amount == 0m
        ? string.Empty
        : m.Value.Amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static string AgeBandLabel(SalaryIncomeTax.AgeBand band) => band switch
    {
        SalaryIncomeTax.AgeBand.SuperSenior => "Super-senior (80+)",
        SalaryIncomeTax.AgeBand.Senior => "Senior (60–79)",
        _ => "Below 60",
    };
}
