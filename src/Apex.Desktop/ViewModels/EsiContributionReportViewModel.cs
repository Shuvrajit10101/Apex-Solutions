using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the ESI monthly-contribution screen (its 01-Apr start year + label).</summary>
public sealed class EsiContributionFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable wage month on the ESI monthly-contribution screen (its first-of-month date + label). ESI is
/// a monthly return, so the contribution file is built for one wage month at a time.</summary>
public sealed class EsiContributionMonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>One <b>Insured-Person (IP) row</b> of the ESI monthly-contribution grid (Phase 8 slice 5): IP number,
/// name, no. of days and the whole-rupee ESI figures — contribution wages and the employee (0.75%) / employer
/// (3.25%) shares, each rounded up independently exactly as the payroll voucher posts (nothing re-derived here
/// beyond the deterministic ESI split of the wages the report already reads back).</summary>
public sealed class EsiContributionRowVm
{
    public string IpNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Days { get; init; } = string.Empty;
    public string EsiWages { get; init; } = string.Empty;
    public string EmployeeContribution { get; init; } = string.Empty;
    public string EmployerContribution { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// The <b>ESI Monthly Contribution</b> report page (Reports → Statutory Reports → Payroll → ESI Monthly
/// Contribution; Phase 8 slice 5; RQ-10; catalog §14). A read-only projection over the pure
/// <see cref="EsiMonthlyContribution"/> engine: pick a financial year + wage month and it shows the establishment
/// employer code, the per-IP rows (IP number, name, days, ESI wages, EE 0.75% / ER 3.25% contributions) and the
/// EE / ER / total footings. <b>Ctrl+A</b> / the Export button writes the ESIC monthly-contribution offline file
/// via <see cref="EsiContributionWriter"/> to the export folder (mirroring the PF ECR export); <b>Alt+B</b> saves
/// that file and returns to the menu.
///
/// <para>Gated: only reachable when Payroll Statutory is enabled (the menu item + the open path are gated on
/// <see cref="Company.PayrollStatutoryEnabled"/>), so a non-payroll company is byte-identical (ER-13). The EE / ER
/// split is the deterministic <see cref="EsiContribution"/> computation of the same wages the file carries (each
/// side ceiled independently, the ≤ ₹176 daily-wage waiver applied), so the grid reconciles to the books and the
/// file by construction. MVVM boundary: engine + persistence only, no Avalonia types (headlessly testable);
/// deterministic — no clock/RNG.</para>
/// </summary>
public sealed partial class EsiContributionReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "ESI Monthly Contribution (ESIC)";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _employerCode = string.Empty;
    [ObservableProperty] private string _wageMonthText = string.Empty;

    // Contribution footings (whole rupees, always rendered).
    [ObservableProperty] private string _totalWagesText = "0";
    [ObservableProperty] private string _totalEmployeeText = "0";
    [ObservableProperty] private string _totalEmployerText = "0";
    [ObservableProperty] private string _grandTotalText = "0";

    [ObservableProperty] private string _memberCountText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    // Export knobs (mirror the PF ECR export: folder + name; write is byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private EsiContributionFyOption? _selectedYear;
    private EsiContributionMonthOption? _selectedMonth;

    /// <summary>The financial years the return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<EsiContributionFyOption> FinancialYears { get; } = new();

    /// <summary>The 12 wage months of the selected financial year (Apr … Mar).</summary>
    public ObservableCollection<EsiContributionMonthOption> Months { get; } = new();

    /// <summary>The per-IP contribution rows of the selected wage month.</summary>
    public ObservableCollection<EsiContributionRowVm> Members { get; } = new();

    public EsiContributionReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new EsiContributionFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        RebuildMonths();
        _selectedMonth = Months.FirstOrDefault();

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-derives the wage months and rebuilds the return.</summary>
    public EsiContributionFyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildMonths();
            _selectedMonth = Months.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMonth));
            Rebuild();
        }
    }

    /// <summary>The selected wage month; changing it rebuilds the return for that month.</summary>
    public EsiContributionMonthOption? SelectedMonth
    {
        get => _selectedMonth;
        set { if (SetProperty(ref _selectedMonth, value)) Rebuild(); }
    }

    /// <summary>The contribution file name the export will write (from the employer code + FY + month, no extension).</summary>
    public string ExportFileName
    {
        get
        {
            var estab = string.IsNullOrWhiteSpace(_company.EsiConfig?.EmployerCode)
                ? "ESI" : _company.EsiConfig!.EmployerCode!.Trim();
            var month = (SelectedMonth?.FirstDay ?? _company.FinancialYearStart).ToString("yyyy_MM", CultureInfo.InvariantCulture);
            return $"{estab}_{month}";
        }
    }

    /// <summary>The full contribution file name including the <c>.csv</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".csv";

    /// <summary>The currently-built ESI monthly-contribution return (rebuilt on selection change). Never null after
    /// construction.</summary>
    public EsiContributionReturn Return { get; private set; } = default!;

    /// <summary>Re-derives the 12 wage-month options for the selected financial year (Apr of the start year … Mar).</summary>
    private void RebuildMonths()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Months.Clear();
        for (var i = 0; i < 12; i++)
            Months.Add(new EsiContributionMonthOption { FirstDay = first.AddMonths(i) });
    }

    /// <summary>(Re)builds the ESI return for the selected FY + wage month and refreshes the members + footings.</summary>
    public void Rebuild()
    {
        var month = SelectedMonth ?? Months.FirstOrDefault();
        var from = month?.FirstDay ?? _company.FinancialYearStart;
        var to = month?.LastDay ?? from.AddMonths(1).AddDays(-1);

        WageMonthText = from.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        EmployerCode = string.IsNullOrWhiteSpace(_company.EsiConfig?.EmployerCode)
            ? "—" : _company.EsiConfig!.EmployerCode!;
        Subtitle = $"{_company.Name}  —  Wage month {WageMonthText}  ·  Employer code {EmployerCode}";

        Members.Clear();
        Message = null;
        ExportStatus = string.Empty;

        var memberIds = _company.Employees.Where(e => e.EsiApplicable).Select(e => e.Id).ToList();

        EsiContributionReturn esi;
        try
        {
            esi = EsiMonthlyContribution.Build(_company, memberIds, from, to);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // An ESI-applicable member without a valid 10-digit IP number, or without an effective structure for the
            // month — surface it and show an empty return rather than crash the page.
            Return = new EsiContributionReturn(_company.EsiConfig?.EmployerCode, to, Array.Empty<EsiContributionRow>());
            Message = ex.Message;
            RefreshTotals(0, 0, 0);
            return;
        }

        Return = esi;

        // The offline file carries only the ESI wages (ESIC computes the split on upload); the on-screen report also
        // shows the EE (0.75%) / ER (3.25%) split — the SAME deterministic EsiContribution computation the voucher
        // posts, on the wages the return already carries (each side ceiled independently, ≤ ₹176 waiver applied).
        var eeBp = _company.EsiConfig?.EmployeeRateBasisPoints ?? EsiConfig.DefaultEmployeeRateBasisPoints;
        var erBp = _company.EsiConfig?.EmployerRateBasisPoints ?? EsiConfig.DefaultEmployerRateBasisPoints;

        long totalWages = 0, totalEe = 0, totalEr = 0;
        foreach (var r in esi.Rows)
        {
            var wages = r.TotalMonthlyWages;
            long ee = 0, er = 0;
            if (wages > 0L)
            {
                var avgDaily = r.NoOfDays > 0 ? (decimal)wages / r.NoOfDays : wages;
                var c = EsiContribution.ComputeMember(wages, avgDaily, eeBp, erBp);
                ee = (long)c.EmployeeContribution.Amount;
                er = (long)c.EmployerContribution.Amount;
            }
            totalWages += wages;
            totalEe += ee;
            totalEr += er;

            Members.Add(new EsiContributionRowVm
            {
                IpNumber = r.IpNumber,
                Name = r.IpName,
                Days = r.NoOfDays.ToString(CultureInfo.InvariantCulture),
                EsiWages = R(wages),
                EmployeeContribution = R(ee),
                EmployerContribution = R(er),
                Reason = string.IsNullOrWhiteSpace(r.ReasonForZeroWages) ? "—" : r.ReasonForZeroWages!,
            });
        }

        RefreshTotals(totalWages, totalEe, totalEr);
    }

    private void RefreshTotals(long totalWages, long totalEe, long totalEr)
    {
        TotalWagesText = R(totalWages);
        TotalEmployeeText = R(totalEe);
        TotalEmployerText = R(totalEr);
        GrandTotalText = R(totalEe + totalEr);

        IsEmpty = Members.Count == 0;
        MemberCountText = Members.Count == 1 ? "1 member" : $"{Members.Count} members";
        StatusText = IsEmpty
            ? "No ESI-applicable members this month — nothing to file."
            : $"ESI contribution ready — {MemberCountText}; total remittance ₹{GrandTotalText}.";
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the ESIC monthly-contribution offline file
    /// (<see cref="EsiContributionWriter.Write"/>) for the current return to <see cref="ExportFolder"/> under
    /// <see cref="ExportResolvedFileName"/>. The bytes come straight off the engine (byte-stable, de-branded); the
    /// write goes through the injectable <paramref name="writeBytes"/> seam (null ⇒ real filesystem) so tests never
    /// touch disk. Returns true on success and sets <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportReturn(Action<string, byte[]>? writeBytes = null)
    {
        try
        {
            var bytes = EsiContributionWriter.Write(Return);
            var folder = ExportFolder ?? string.Empty;
            var path = string.IsNullOrEmpty(folder)
                ? ExportResolvedFileName
                : Path.Combine(folder, ExportResolvedFileName);

            if (writeBytes is not null) writeBytes(path, bytes);
            else File.WriteAllBytes(path, bytes);

            ExportStatus = $"Exported {bytes.Length:#,0} bytes to {path}";
            return true;
        }
        catch (Exception ex)
        {
            ExportStatus = "Could not export the ESI contribution file: " + ex.Message;
            return false;
        }
    }

    /// <summary>Whole-rupee Indian-grouped display of an ESI integer figure (always rendered, even zero).</summary>
    private static string R(long value) => IndianFormat.RupeesAlways(value);
}
