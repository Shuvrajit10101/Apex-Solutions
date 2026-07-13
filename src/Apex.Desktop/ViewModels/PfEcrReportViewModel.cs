using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the PF ECR screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class PfEcrFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable wage month on the PF ECR screen (its first-of-month date + the "Apr 2025" label). PF is a
/// monthly return, so the ECR / challan is built for one wage month at a time.</summary>
public sealed class PfEcrMonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>One <b>member (detail) row</b> of the ECR grid (Phase 8 slice 4): UAN, name and the whole-rupee ECR
/// figures — gross / EPF / EPS / EDLI wages, employee EPF share, EPS contribution, employer EPF share, NCP days and
/// refund of advances — read verbatim off the pure <see cref="PfEcrMember"/> projection (nothing recomputed here).</summary>
public sealed class PfEcrMemberRowVm
{
    public string Uan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string GrossWages { get; init; } = string.Empty;
    public string EpfWages { get; init; } = string.Empty;
    public string EpsWages { get; init; } = string.Empty;
    public string EdliWages { get; init; } = string.Empty;
    public string EmployeeShareEpf { get; init; } = string.Empty;
    public string EpsContribution { get; init; } = string.Empty;
    public string EmployerShareEpf { get; init; } = string.Empty;
    public string NcpDays { get; init; } = string.Empty;
    public string RefundOfAdvances { get; init; } = string.Empty;
}

/// <summary>
/// The <b>PF ECR / Challan</b> report page (Reports → Statutory Reports → Payroll (PF) → PF ECR / Challan; Phase 8
/// slice 4; RQ-9; catalog §14). A read-only projection over the pure <see cref="PfEcr"/> engine: pick a financial
/// year + wage month and it shows the establishment code, the member-wise ECR 2.0 rows and the challan
/// account-head totals (A/c 1 EPF · A/c 2 admin · A/c 10 EPS · A/c 21 EDLI · A/c 22 EDLI-admin NIL). <b>Ctrl+A</b> /
/// the Export button writes the EPFO ECR 2.0 offline flat file via <see cref="EcrWriter"/> to the export folder
/// (mirroring the Form-26Q FVU export); <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>Gated: only reachable when Payroll Statutory is enabled (the menu item + the open path are gated on
/// <see cref="Company.PayrollStatutoryEnabled"/>), so a non-payroll company is byte-identical (ER-13). MVVM
/// boundary: engine + persistence only, no Avalonia types (headlessly testable). Deterministic — the ECR write
/// takes its bytes from the engine (no clock/RNG).</para>
/// </summary>
public sealed partial class PfEcrReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "PF ECR / Challan — Electronic Challan cum Return (EPFO)";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _establishmentCode = string.Empty;
    [ObservableProperty] private string _wageMonthText = string.Empty;

    // Challan account-head totals (whole rupees, always rendered).
    [ObservableProperty] private string _account1Text = "0";
    [ObservableProperty] private string _account2Text = "0";
    [ObservableProperty] private string _account10Text = "0";
    [ObservableProperty] private string _account21Text = "0";
    [ObservableProperty] private string _account22Text = "0";
    [ObservableProperty] private string _grandTotalText = "0";

    [ObservableProperty] private string _memberCountText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    // ECR export knobs (mirror the Form-26Q FVU export: folder + name; write is byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private PfEcrFyOption? _selectedYear;
    private PfEcrMonthOption? _selectedMonth;

    /// <summary>The financial years the ECR can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<PfEcrFyOption> FinancialYears { get; } = new();

    /// <summary>The 12 wage months of the selected financial year (Apr … Mar).</summary>
    public ObservableCollection<PfEcrMonthOption> Months { get; } = new();

    /// <summary>The member-wise ECR detail rows of the selected wage month.</summary>
    public ObservableCollection<PfEcrMemberRowVm> Members { get; } = new();

    public PfEcrReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new PfEcrFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        RebuildMonths();
        _selectedMonth = Months.FirstOrDefault();

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-derives the wage months and rebuilds the ECR.</summary>
    public PfEcrFyOption? SelectedYear
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

    /// <summary>The selected wage month; changing it rebuilds the ECR for that month.</summary>
    public PfEcrMonthOption? SelectedMonth
    {
        get => _selectedMonth;
        set { if (SetProperty(ref _selectedMonth, value)) Rebuild(); }
    }

    /// <summary>The ECR file name the export will write (from the establishment code + FY + month, no extension).</summary>
    public string ExportFileName
    {
        get
        {
            var estab = string.IsNullOrWhiteSpace(_company.PfConfig?.EstablishmentCode)
                ? "ECR" : _company.PfConfig!.EstablishmentCode!.Trim();
            var month = (SelectedMonth?.FirstDay ?? _company.FinancialYearStart).ToString("yyyy_MM", CultureInfo.InvariantCulture);
            return $"{estab}_{month}";
        }
    }

    /// <summary>The full ECR file name including the <c>.txt</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".txt";

    /// <summary>The currently-built ECR return (rebuilt on selection change). Never null after construction.</summary>
    public PfEcrReturn Return { get; private set; } = default!;

    /// <summary>Re-derives the 12 wage-month options for the selected financial year (Apr of the start year … Mar).</summary>
    private void RebuildMonths()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Months.Clear();
        for (var i = 0; i < 12; i++)
            Months.Add(new PfEcrMonthOption { FirstDay = first.AddMonths(i) });
    }

    /// <summary>(Re)builds the ECR for the selected FY + wage month and refreshes the members + challan totals.</summary>
    public void Rebuild()
    {
        var month = SelectedMonth ?? Months.FirstOrDefault();
        var from = month?.FirstDay ?? _company.FinancialYearStart;
        var to = month?.LastDay ?? from.AddMonths(1).AddDays(-1);

        WageMonthText = from.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        EstablishmentCode = string.IsNullOrWhiteSpace(_company.PfConfig?.EstablishmentCode)
            ? "—" : _company.PfConfig!.EstablishmentCode!;
        Subtitle = $"{_company.Name}  —  Wage month {WageMonthText}  ·  Establishment {EstablishmentCode}";

        Members.Clear();
        Message = null;
        ExportStatus = string.Empty;

        var memberIds = _company.Employees.Where(e => e.PfApplicable).Select(e => e.Id).ToList();

        PfEcrReturn ecr;
        try
        {
            ecr = PfEcr.Build(_company, memberIds, from, to);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // A PF-applicable member without an effective salary structure for the month, etc. — surface it and
            // show an empty return rather than crash the page.
            Return = new PfEcrReturn(_company.PfConfig?.EstablishmentCode, to,
                Array.Empty<PfEcrMember>(), new PfChallanTotals(0, 0, 0, 0, 0));
            Message = ex.Message;
            RefreshTotals();
            return;
        }

        Return = ecr;
        foreach (var m in ecr.Members)
            Members.Add(new PfEcrMemberRowVm
            {
                Uan = m.Uan,
                Name = m.Name,
                GrossWages = R(m.GrossWages),
                EpfWages = R(m.EpfWages),
                EpsWages = R(m.EpsWages),
                EdliWages = R(m.EdliWages),
                EmployeeShareEpf = R(m.EmployeeShareEpf),
                EpsContribution = R(m.EpsContribution),
                EmployerShareEpf = R(m.EmployerShareEpf),
                NcpDays = m.NcpDays.ToString(CultureInfo.InvariantCulture),
                RefundOfAdvances = R(m.RefundOfAdvances),
            });

        RefreshTotals();
    }

    private void RefreshTotals()
    {
        var t = Return.Totals;
        Account1Text = R(t.Account1);
        Account2Text = R(t.Account2);
        Account10Text = R(t.Account10);
        Account21Text = R(t.Account21);
        Account22Text = R(t.Account22);
        GrandTotalText = R(t.Account1 + t.Account2 + t.Account10 + t.Account21 + t.Account22);

        IsEmpty = Members.Count == 0;
        MemberCountText = Members.Count == 1 ? "1 member" : $"{Members.Count} members";
        StatusText = IsEmpty
            ? "No PF-applicable members with wages this month — nothing to file."
            : $"ECR ready — {MemberCountText}; total remittance ₹{GrandTotalText}.";
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the EPFO ECR 2.0 offline flat file (<see cref="EcrWriter.Write"/>) for the
    /// current return to <see cref="ExportFolder"/> under <see cref="ExportResolvedFileName"/>. The bytes come
    /// straight off the engine (byte-stable, de-branded); the write goes through the injectable
    /// <paramref name="writeBytes"/> seam (null ⇒ real filesystem) so tests never touch disk. Returns true on
    /// success and sets <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportEcr(Action<string, byte[]>? writeBytes = null)
    {
        try
        {
            var bytes = EcrWriter.Write(Return);
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
            ExportStatus = "Could not export the ECR file: " + ex.Message;
            return false;
        }
    }

    /// <summary>Whole-rupee Indian-grouped display of an ECR integer figure (always rendered, even zero).</summary>
    private static string R(long value) => IndianFormat.RupeesAlways(value);
}
