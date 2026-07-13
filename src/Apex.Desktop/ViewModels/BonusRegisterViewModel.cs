using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable accounting (financial) year on the bonus-register screen (its 01-Apr start year + label).
/// Statutory bonus is an annual computation, so the register is built for one accounting year at a time.</summary>
public sealed class BonusFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>One <b>employee row</b> of the statutory-bonus register (Phase 8 slice 9): the member's name + number,
/// whether eligible (≥ 30 days worked), the actual monthly Basic + DA, the §12-capped base, the applied rate and the
/// annual bonus. Whole rupees — bonus carries no paisa. A member drawing Basic + DA above ₹21,000 is excluded (no
/// row), per the Act.</summary>
public sealed class BonusRegisterRowVm
{
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string Eligible { get; init; } = string.Empty;
    public string ActualBasicDa { get; init; } = string.Empty;
    public string CappedBase { get; init; } = string.Empty;
    public string RatePercent { get; init; } = string.Empty;
    public string AnnualBonus { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Bonus</b> register page (Reports → Statutory Reports → Payroll → Bonus; Phase 8 slice 9; RQ-15; catalog §14;
/// Payment of Bonus Act 1965). A read-only projection over the pure <see cref="BonusRegister"/>: pick an accounting
/// (financial) year and it shows the per-employee bonus figures (eligibility, actual Basic + DA, the §12-capped base,
/// the applied 8.33%–20% rate, the annual bonus) and the total bonus. This is the <b>light</b> deliverable (DP-4) —
/// eligibility ≤ ₹21,000 + ≥ 30 days, the ₹7,000 / minimum-wage calc ceiling, mid-year proration, the ₹100 floor —
/// not the full allocable-surplus computation.
///
/// <para>Gated: only reachable when Payroll Statutory is enabled and the establishment is enrolled for statutory bonus
/// (the menu item + the open path are gated on <see cref="Company.BonusConfig"/>), so a non-bonus company is
/// byte-identical (ER-13). Every figure is the deterministic <see cref="Apex.Ledger.Services.StatutoryBonus"/>
/// computation over the dated salary structure, so the register is byte-stable. MVVM boundary: engine only, no Avalonia
/// types (headlessly testable); deterministic — no clock/RNG.</para>
/// </summary>
public sealed partial class BonusRegisterViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Statutory Bonus Register";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _accountingYearText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;

    // Footing (whole rupees, always rendered).
    [ObservableProperty] private string _totalBonusText = "0";

    [ObservableProperty] private string _memberCountText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = string.Empty;

    private BonusFyOption? _selectedYear;

    /// <summary>The accounting years the register can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<BonusFyOption> FinancialYears { get; } = new();

    /// <summary>The per-employee bonus rows of the selected accounting year (those within the ₹21,000 ceiling).</summary>
    public ObservableCollection<BonusRegisterRowVm> Rows { get; } = new();

    public BonusRegisterViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new BonusFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected accounting year; changing it rebuilds the register for that year.</summary>
    public BonusFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>(Re)builds the bonus register for the selected accounting year and refreshes the rows + total.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyStart = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyEnd = fyStart.AddYears(1).AddDays(-1);

        AccountingYearText = $"{fyStart:dd MMM yyyy} — {fyEnd:dd MMM yyyy}";
        RateText = _company.BonusConfig is { } cfg
            ? (cfg.RateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%"
            : "—";
        Subtitle = $"{_company.Name}  —  Accounting year {startYear}-{(startYear + 1) % 100:00}  ·  Rate {RateText}";

        Rows.Clear();

        long totalBonus = 0;
        if (_company.BonusConfig is not null)
        {
            var employeeIds = _company.Employees.Select(e => e.Id).ToList();
            var reg = BonusRegister.Build(_company, employeeIds, fyStart);
            totalBonus = reg.TotalBonus;
            foreach (var r in reg.Rows)
            {
                Rows.Add(new BonusRegisterRowVm
                {
                    EmployeeName = r.EmployeeName,
                    EmployeeNumber = string.IsNullOrWhiteSpace(r.EmployeeNumber) ? "—" : r.EmployeeNumber!,
                    Eligible = r.Eligible ? "Yes" : "No",
                    ActualBasicDa = R(r.ActualBasicDa),
                    CappedBase = R(r.CappedBase),
                    RatePercent = r.RatePercent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
                    AnnualBonus = R(r.AnnualBonus),
                });
            }
        }

        RefreshTotals(totalBonus);
    }

    private void RefreshTotals(long totalBonus)
    {
        TotalBonusText = R(totalBonus);
        IsEmpty = Rows.Count == 0;
        MemberCountText = Rows.Count == 1 ? "1 employee" : $"{Rows.Count} employees";
        StatusText = _company.BonusConfig is null
            ? "Statutory Bonus is not enabled for this company (F11 → Payroll Statutory → Bonus)."
            : IsEmpty
                ? "No bonus-eligible employees this year (within the ₹21,000 wage ceiling)."
                : $"Bonus register ready — {MemberCountText}; total bonus payable ₹{TotalBonusText}.";
    }

    /// <summary>Whole-rupee Indian-grouped display of a bonus integer figure (always rendered, even zero).</summary>
    private static string R(long value) => IndianFormat.RupeesAlways(value);
}
