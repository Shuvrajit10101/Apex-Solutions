using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the gratuity-provision screen (its 01-Apr start year + label).</summary>
public sealed class GratuityFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable provision-as-on month on the gratuity-provision screen (its first-of-month date + label). The
/// provision is struck as-on a period end, so the as-on date is the last day of the selected month.</summary>
public sealed class GratuityAsOnMonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => LastDay.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>One <b>employee row</b> of the gratuity provision register (Phase 8 slice 9): the member's name + number,
/// join date, completed years (with the ≥ 6-month round-up), whether vested (≥ 5 years), the Basic + DA wage base and
/// the accrued provision. Whole rupees — gratuity carries no paisa.</summary>
public sealed class GratuityRegisterRowVm
{
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DateOfJoining { get; init; } = string.Empty;
    public string CompletedYears { get; init; } = string.Empty;
    public string Vested { get; init; } = string.Empty;
    public string BasicPlusDa { get; init; } = string.Empty;
    public string AccruedGratuity { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Gratuity Provision</b> register page (Reports → Statutory Reports → Payroll → Gratuity Provision; Phase 8
/// slice 9; RQ-14; catalog §14; Payment of Gratuity Act 1972). A read-only projection over the pure
/// <see cref="GratuityProvisionRegister"/>: pick a provision-as-on date (a financial year + month end) and it shows the
/// per-employee accrual (join date, completed years, vested flag, Basic + DA, accrued gratuity), the total liability,
/// the prior posted provision balance and the delta to post. The <b>Post Provision</b> action posts the period-end
/// Dr Gratuity Expense / Cr Gratuity Provision voucher for the delta over the prior balance (through the S3 atomic
/// auto-ledger path) and persists the company; an unchanged provision is a friendly no-op.
///
/// <para>Gated: only reachable when Payroll Statutory is enabled and the establishment is enrolled for gratuity (the
/// menu item + the open path are gated on <see cref="Company.GratuityConfig"/>), so a non-gratuity company is
/// byte-identical (ER-13). Every figure is the deterministic 15 / 26 accrual the voucher posts, so the register
/// reconciles to the Gratuity Provision ledger by construction. MVVM boundary: engine + persistence only, no Avalonia
/// types (headlessly testable); deterministic — no clock/RNG.</para>
/// </summary>
public sealed partial class GratuityProvisionRegisterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "Gratuity Provision Register";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _asOnText = string.Empty;
    [ObservableProperty] private string _capText = string.Empty;

    // Footings (whole rupees, always rendered).
    [ObservableProperty] private string _totalLiabilityText = "0";
    [ObservableProperty] private string _priorProvisionText = "0";
    [ObservableProperty] private string _deltaText = "0";

    [ObservableProperty] private string _memberCountText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _canPost;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _postStatus;

    private GratuityFyOption? _selectedYear;
    private GratuityAsOnMonthOption? _selectedMonth;

    /// <summary>The financial years the provision can be struck for (the company FY + the two prior).</summary>
    public ObservableCollection<GratuityFyOption> FinancialYears { get; } = new();

    /// <summary>The 12 as-on month ends of the selected financial year (Apr … Mar).</summary>
    public ObservableCollection<GratuityAsOnMonthOption> Months { get; } = new();

    /// <summary>The per-employee gratuity accrual rows as-on the selected date.</summary>
    public ObservableCollection<GratuityRegisterRowVm> Rows { get; } = new();

    public GratuityProvisionRegisterViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GratuityFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        RebuildMonths();
        // Default the as-on to the financial-year end (the natural period-end for a provision).
        _selectedMonth = Months.LastOrDefault();

        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-derives the as-on months and rebuilds the register.</summary>
    public GratuityFyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildMonths();
            _selectedMonth = Months.LastOrDefault();
            OnPropertyChanged(nameof(SelectedMonth));
            Rebuild();
        }
    }

    /// <summary>The selected provision-as-on month (as-on = its last day); changing it rebuilds the register.</summary>
    public GratuityAsOnMonthOption? SelectedMonth
    {
        get => _selectedMonth;
        set { if (SetProperty(ref _selectedMonth, value)) Rebuild(); }
    }

    /// <summary>The provision-as-on date the register + post use — the selected month's last day.</summary>
    public DateOnly AsOn => SelectedMonth?.LastDay
                           ?? Months.LastOrDefault()?.LastDay
                           ?? _company.FinancialYearStart.AddYears(1).AddDays(-1);

    /// <summary>Re-derives the 12 as-on month options for the selected financial year (Apr of the start year … Mar).</summary>
    private void RebuildMonths()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Months.Clear();
        for (var i = 0; i < 12; i++)
            Months.Add(new GratuityAsOnMonthOption { FirstDay = first.AddMonths(i) });
    }

    /// <summary>(Re)builds the gratuity register as-on the selected date and refreshes the rows + footings.</summary>
    public void Rebuild()
    {
        var asOn = AsOn;
        AsOnText = asOn.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        CapText = _company.GratuityConfig is { } cfg
            ? IndianFormat.RupeesAlways(cfg.CapAmount.Amount)
            : IndianFormat.RupeesAlways(GratuityConfig.DefaultCapAmount);
        Subtitle = $"{_company.Name}  —  Provision as-on {AsOnText}  ·  Cap ₹{CapText}";

        Rows.Clear();
        PostStatus = string.Empty;

        long totalLiability = 0;
        if (_company.GratuityConfig is not null)
        {
            var employeeIds = _company.Employees.Select(e => e.Id).ToList();
            var reg = GratuityProvisionRegister.Build(_company, employeeIds, asOn);
            totalLiability = reg.TotalLiability;
            foreach (var r in reg.Rows)
            {
                Rows.Add(new GratuityRegisterRowVm
                {
                    EmployeeName = r.EmployeeName,
                    EmployeeNumber = string.IsNullOrWhiteSpace(r.EmployeeNumber) ? "—" : r.EmployeeNumber!,
                    DateOfJoining = r.DateOfJoining is { } d ? d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) : "—",
                    CompletedYears = r.CompletedYears.ToString(CultureInfo.InvariantCulture),
                    Vested = r.Vested ? "Yes" : "No",
                    BasicPlusDa = R(r.BasicPlusDa),
                    AccruedGratuity = R(r.AccruedGratuity),
                });
            }
        }

        // Prior provision INCLUSIVE of any voucher already dated on-or-before the as-on date (PriorGratuityProvisionBalance
        // is strictly-before, so pass as-on + 1 day). This makes the displayed delta fall to ₹0 once the provision for
        // this date has been posted — so the register never invites a duplicate post of the same period-end provision.
        var prior = _company.GratuityConfig is not null
            ? (long)Math.Round(new PayrollVoucherService(_company).PriorGratuityProvisionBalance(asOn.AddDays(1)).Amount, 0, MidpointRounding.AwayFromZero)
            : 0L;
        RefreshTotals(totalLiability, prior);
    }

    private void RefreshTotals(long totalLiability, long prior)
    {
        var delta = totalLiability - prior;
        TotalLiabilityText = R(totalLiability);
        PriorProvisionText = R(prior);
        DeltaText = (delta >= 0 ? "" : "-") + R(Math.Abs(delta));

        IsEmpty = Rows.Count == 0;
        MemberCountText = Rows.Count == 1 ? "1 employee" : $"{Rows.Count} employees";
        // Post is available only when enrolled and there is a non-zero delta over the prior posted balance.
        CanPost = _company.GratuityConfig is not null && _company.PayrollEnabled && delta != 0;

        StatusText = _company.GratuityConfig is null
            ? "Gratuity is not enabled for this company (F11 → Payroll Statutory → Gratuity)."
            : IsEmpty
                ? "No active employees to provision as-on this date."
                : delta == 0
                    ? $"Provision unchanged from the prior balance (₹{PriorProvisionText}) — nothing to post."
                    : delta > 0
                        ? $"Gratuity register ready — {MemberCountText}; provision rises by ₹{R(delta)} to ₹{TotalLiabilityText}."
                        : $"Gratuity register ready — {MemberCountText}; provision falls by ₹{R(Math.Abs(delta))} to ₹{TotalLiabilityText}.";
    }

    /// <summary>
    /// The <b>Post Provision</b> action (the screen's primary command; Ctrl+A): posts the period-end gratuity
    /// provision voucher for the delta over the prior balance (Dr Gratuity Expense / Cr Gratuity Provision, or the
    /// reverse write-back for a fall) through <see cref="PayrollVoucherService.PostGratuityProvision"/> and persists
    /// the company, then rebuilds so the prior balance and delta refresh. An unchanged provision (delta 0), gratuity
    /// not enrolled, or any domain error surfaces to <see cref="PostStatus"/> without crashing. Returns true on a
    /// successful post.
    /// </summary>
    public bool PostProvision()
    {
        PostStatus = null;
        if (_company.GratuityConfig is null)
        {
            PostStatus = "Gratuity is not enabled for this company.";
            return false;
        }

        var asOn = AsOn;
        var employeeIds = _company.Employees.Select(e => e.Id).ToList();

        // Refuse a duplicate post: if a provision voucher already carries this date (or a later balance already meets
        // the accrual), the inclusive delta is ₹0 and there is nothing new to post (guards the same-date double-post
        // the engine's strictly-before prior would otherwise allow).
        var svc = new PayrollVoucherService(_company);
        var accrued = GratuityProvision.TotalLiability(_company, employeeIds, asOn);
        var inclusivePrior = svc.PriorGratuityProvisionBalance(asOn.AddDays(1));
        if (accrued.Amount - inclusivePrior.Amount == 0m)
        {
            PostStatus = "The gratuity provision is unchanged from the posted balance — nothing to post.";
            Rebuild();
            return false;
        }

        Voucher posted;
        try
        {
            posted = svc.PostGratuityProvision(asOn, employeeIds, voucherDate: asOn);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            PostStatus = $"Could not post the gratuity provision: {ex.Message}";
            Rebuild();
            return false;
        }

        _onChanged();
        Rebuild();
        PostStatus = $"Posted gratuity provision as-on {AsOnText}: "
                     + $"Dr {IndianFormat.AmountAlways(posted.TotalDebit)} = Cr {IndianFormat.AmountAlways(posted.TotalCredit)}.";
        return true;
    }

    /// <summary>Whole-rupee Indian-grouped display of a gratuity integer figure (always rendered, even zero).</summary>
    private static string R(long value) => IndianFormat.RupeesAlways(value);
}
