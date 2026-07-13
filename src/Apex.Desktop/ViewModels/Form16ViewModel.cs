using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the Form 16 screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class Form16FyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>One selectable <b>employee</b> in the Form 16 picker (the certificate is issued to this person): the id,
/// PAN, name and the year's total §192 TDS. Selecting it builds that employee's Form 16.</summary>
public sealed partial class Form16EmployeeOptionVm : ViewModelBase
{
    public Guid EmployeeId { get; init; }
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TdsTotal { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>Part A</b> quarter row of the selected Form 16 (quarter label + the §192 TDS deducted that
/// quarter) — read verbatim off the <see cref="Form16QuarterRow"/> projection.</summary>
public sealed class Form16QuarterRowVm
{
    public string Quarter { get; init; } = string.Empty;
    public string TdsDeducted { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 16</b> annual salary-TDS-certificate report page (Reports → Statutory Reports → Payroll → Form 16;
/// Phase 8 slice 7; RQ-13; catalog §14) — the <b>salary sibling</b> of the Phase-7 <see cref="Form16AViewModel"/>.
/// Pick a financial year + an <b>employee</b> and it shows that employee's certificate — the deductor block (TAN /
/// responsible person from F11), <b>Part A</b> (the quarter-wise TDS summary from the four Form 24Q Annexure-I
/// filings) and <b>Part B</b> (the salary / deduction / tax computation from the Q4 Annexure-II row) — all read off
/// the pure <see cref="Form16"/> projection so the figures match Form 24Q to the rupee. <b>Ctrl+A</b> / the Export
/// button renders the deterministic, de-branded certificate PDF via the generic <see cref="ReportPdf"/>; <b>Alt+B</b>
/// saves it and returns to the menu.
///
/// <para>Gated: only reachable when §192 salary TDS is enabled (the menu item + the open path are gated on
/// <see cref="Company.SalaryTdsEnabled"/>), so a non-salary-TDS company never reaches it (ER-13). MVVM boundary:
/// engine + IO only, no Avalonia types (headlessly testable). Deterministic — the PDF bytes come straight off the
/// engine (no clock/RNG).</para>
/// </summary>
public sealed partial class Form16ViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 16 — Salary-TDS Certificate (§192)";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Deductor (issuer) block.
    [ObservableProperty] private string _deductorTan = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;
    [ObservableProperty] private string _assessmentYear = string.Empty;

    // Employee (recipient) block + Part B computation for the selected certificate.
    [ObservableProperty] private string _employeeName = string.Empty;
    [ObservableProperty] private string _employeePan = string.Empty;
    [ObservableProperty] private string _regime = string.Empty;
    [ObservableProperty] private string _grossSalary = string.Empty;
    [ObservableProperty] private string _standardDeduction = string.Empty;
    [ObservableProperty] private string _chapterVia = string.Empty;
    [ObservableProperty] private string _taxableIncome = string.Empty;
    [ObservableProperty] private string _incomeTax = string.Empty;
    [ObservableProperty] private string _surcharge = string.Empty;
    [ObservableProperty] private string _cess = string.Empty;
    [ObservableProperty] private string _totalTax = string.Empty;
    [ObservableProperty] private string _totalTdsDeducted = string.Empty;
    [ObservableProperty] private bool _hasPartB;

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;

    // PDF export knobs (mirror the Form 16A PDF export: folder + name; bytes are byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private Form16FyOption? _selectedYear;
    private SalarySectionCodeOption? _selectedSectionCode;
    private Form16EmployeeOptionVm? _selectedEmployee;

    /// <summary>The financial years the certificate can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<Form16FyOption> FinancialYears { get; } = new();

    /// <summary>The salary deductor-category options (92B private / 92A govt / 92C union-govt).</summary>
    public ObservableCollection<SalarySectionCodeOption> SectionCodes { get; } = new();

    /// <summary>The employees with §192 salary activity in the selected FY (selectable in a ListBox).</summary>
    public ObservableCollection<Form16EmployeeOptionVm> Employees { get; } = new();

    /// <summary>The Part A quarter rows of the selected employee's certificate (Q1..Q4).</summary>
    public ObservableCollection<Form16QuarterRowVm> PartA { get; } = new();

    public Form16ViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Form16FyOption { StartYear = y });

        foreach (var opt in SalarySectionCodeOption.All) SectionCodes.Add(opt);

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedSectionCode = SectionCodes.First();

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the employee list.</summary>
    public Form16FyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected salary deductor category (92B/92A/92C); changing it rebuilds the employee list.</summary>
    public SalarySectionCodeOption? SelectedSectionCode
    {
        get => _selectedSectionCode;
        set { if (SetProperty(ref _selectedSectionCode, value)) Rebuild(); }
    }

    /// <summary>The selected employee (the certificate is built for this person); changing it re-projects the detail
    /// and keeps the list highlight in sync (whether the change came from the list or a direct assignment).</summary>
    public Form16EmployeeOptionVm? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (!SetProperty(ref _selectedEmployee, value)) return;
            ProjectCertificate();
            var idx = value is null ? -1 : Employees.IndexOf(value);
            if (HighlightedIndex != idx) HighlightedIndex = idx; // no recursion: OnHighlightedIndexChanged re-sets the same value
        }
    }

    /// <summary>The certificate currently displayed (rebuilt on selection change). Null until an employee is picked.</summary>
    public Form16? Certificate { get; private set; }

    /// <summary>The PDF file name the export will write (FY + employee PAN), no extension.</summary>
    public string ExportFileName
    {
        get
        {
            var fy = (SelectedYear?.Label ?? "FY").Replace('-', '_');
            var pan = string.IsNullOrWhiteSpace(SelectedEmployee?.Pan) ? "NOPAN" : SelectedEmployee!.Pan;
            return $"Form16_{fy}_{pan}";
        }
    }

    /// <summary>The full PDF file name including the <c>.pdf</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".pdf";

    private string SectionCode => SelectedSectionCode?.Code ?? Form24Q.SalarySectionCode;

    /// <summary>(Re)builds the employee list for the selected FY (one certificate per employee with §192 activity, in
    /// name order — the Annexure II set) and re-selects the first, re-projecting its detail.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        AssessmentYear = $"{fyStart + 1}-{(fyStart + 2) % 100:00}";
        Subtitle = $"{_company.Name}  —  FY {fyStart}-{(fyStart + 1) % 100:00}  (AY {AssessmentYear})  ·  section {SectionCode}";

        // The employees who have a certificate this FY = the Annexure II set (paid / withheld this year).
        var rows = Form24Q.BuildAnnexureII(_company, fyStart);

        Employees.Clear();
        foreach (var r in rows)
            Employees.Add(new Form16EmployeeOptionVm
            {
                EmployeeId = r.EmployeeId,
                Pan = string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan!,
                Name = r.EmployeeName,
                TdsTotal = IndianFormat.AmountAlways(r.TaxDeducted),
            });

        HighlightedIndex = Employees.Count > 0 ? 0 : -1;
        SelectedEmployee = Employees.FirstOrDefault();
        ProjectCertificate();
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

    private void ProjectCertificate()
    {
        PartA.Clear();
        var sel = SelectedEmployee;
        if (sel is null)
        {
            Certificate = null;
            DeductorTan = "—"; ResponsiblePerson = "—";
            EmployeeName = "—"; EmployeePan = "—"; Regime = "—";
            GrossSalary = StandardDeduction = ChapterVia = TaxableIncome = IncomeTax = Surcharge =
                Cess = TotalTax = TotalTdsDeducted = IndianFormat.AmountAlways(Money.Zero);
            HasPartB = false;
            IsEmpty = true;
            StatusText = "No employee has §192 salary activity this year — nothing to certify.";
            RaiseExportNames();
            return;
        }

        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var cert = Form16.Build(_company, sel.EmployeeId, fyStart, SectionCode);
        Certificate = cert;

        DeductorTan = string.IsNullOrEmpty(cert.Deductor.Tan) ? "—" : cert.Deductor.Tan;
        ResponsiblePerson = BuildResponsibleLine(cert.Deductor);
        EmployeeName = cert.EmployeeName;
        EmployeePan = string.IsNullOrWhiteSpace(cert.EmployeePan) ? "PANNOTAVBL" : cert.EmployeePan!;

        foreach (var qr in cert.PartA)
            PartA.Add(new Form16QuarterRowVm
            {
                Quarter = qr.QuarterLabel,
                TdsDeducted = IndianFormat.AmountAlways(qr.TdsDeducted),
            });

        TotalTdsDeducted = IndianFormat.AmountAlways(cert.TotalTdsDeducted);

        var b = cert.PartB;
        HasPartB = b is not null;
        if (b is not null)
        {
            Regime = b.Regime == TaxRegime.Old ? "Old Regime" : "New Regime";
            GrossSalary = IndianFormat.AmountAlways(b.GrossSalary);
            StandardDeduction = IndianFormat.AmountAlways(b.StandardDeduction);
            ChapterVia = IndianFormat.AmountAlways(b.ChapterViaDeductions);
            TaxableIncome = IndianFormat.AmountAlways(b.TaxableIncome);
            IncomeTax = IndianFormat.AmountAlways(b.IncomeTax);
            Surcharge = IndianFormat.AmountAlways(b.Surcharge);
            Cess = IndianFormat.AmountAlways(b.Cess);
            TotalTax = IndianFormat.AmountAlways(b.TotalTax);
        }
        else
        {
            Regime = "—";
            GrossSalary = StandardDeduction = ChapterVia = TaxableIncome = IncomeTax = Surcharge =
                Cess = TotalTax = IndianFormat.AmountAlways(Money.Zero);
        }

        IsEmpty = cert.TotalTdsDeducted.Amount == 0m && !HasPartB;
        StatusText = IsEmpty
            ? "No §192 withholding on this employee this year — an empty certificate."
            : "Certificate ready — export to a de-branded PDF (Ctrl+A).";
        ExportStatus = string.Empty;
        RaiseExportNames();
    }

    private void RaiseExportNames()
    {
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>
    /// Ctrl+A / the Export button: renders the selected employee's Form 16 certificate to a deterministic, de-branded
    /// PDF (via the generic <see cref="ReportPdf.Render"/>) and writes it to <see cref="ExportFolder"/> under
    /// <see cref="ExportResolvedFileName"/>. The bytes come straight off the engine (byte-stable); the write goes
    /// through the injectable <paramref name="writeBytes"/> seam (null ⇒ real filesystem) so tests never touch disk.
    /// Returns true on success and sets <see cref="ExportStatus"/> either way. A no-op when no employee is selected.
    /// </summary>
    public bool ExportPdf(Action<string, byte[]>? writeBytes = null)
    {
        if (Certificate is null)
        {
            ExportStatus = "Pick an employee first — nothing to certify.";
            return false;
        }
        try
        {
            var bytes = ReportPdf.Render(BuildPrintReport(Certificate), CertificatePages.Build(_company.Name));
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
            ExportStatus = "Could not export the certificate PDF: " + ex.Message;
            return false;
        }
    }

    /// <summary>Projects the certificate into a de-branded <see cref="PrintReport"/> — the deductor + employee blocks,
    /// Part A quarter-wise TDS, and (when present) Part B salary/tax computation — for the generic PDF renderer.</summary>
    private PrintReport BuildPrintReport(Form16 cert)
    {
        var rows = new List<PrintRow>
        {
            PrintRow.Header("Deductor (TAN)", cert.Deductor.Tan ?? "—"),
            new PrintRow("Employee", cert.EmployeeName),
            new PrintRow("PAN", string.IsNullOrWhiteSpace(cert.EmployeePan) ? "PANNOTAVBL" : cert.EmployeePan!),
            new PrintRow("Assessment Year", cert.AssessmentYearLabel),
            PrintRow.Header("Part A — Quarter-wise TDS", string.Empty),
        };
        foreach (var qr in cert.PartA)
            rows.Add(new PrintRow(qr.QuarterLabel, IndianFormat.AmountAlways(qr.TdsDeducted)));
        rows.Add(PrintRow.Total("Total TDS deducted", IndianFormat.AmountAlways(cert.TotalTdsDeducted)));

        if (cert.PartB is { } b)
        {
            rows.Add(PrintRow.Header("Part B — Salary & tax computation", b.Regime == TaxRegime.Old ? "Old Regime" : "New Regime"));
            rows.Add(new PrintRow("Gross salary", IndianFormat.AmountAlways(b.GrossSalary)));
            rows.Add(new PrintRow("Standard deduction", IndianFormat.AmountAlways(b.StandardDeduction)));
            rows.Add(new PrintRow("Chapter VI-A / exemptions", IndianFormat.AmountAlways(b.ChapterViaDeductions)));
            rows.Add(new PrintRow("Total taxable income", IndianFormat.AmountAlways(b.TaxableIncome)));
            rows.Add(new PrintRow("Income tax (after §87A rebate)", IndianFormat.AmountAlways(b.IncomeTax)));
            rows.Add(new PrintRow("Surcharge", IndianFormat.AmountAlways(b.Surcharge)));
            rows.Add(new PrintRow("Health & education cess (4%)", IndianFormat.AmountAlways(b.Cess)));
            rows.Add(PrintRow.Total("Total tax liability", IndianFormat.AmountAlways(b.TotalTax)));
            rows.Add(new PrintRow("Tax deducted at source", IndianFormat.AmountAlways(b.TaxDeducted)));
        }

        return new PrintReport
        {
            Title = "Form 16 — Salary-TDS Certificate (Section 192)",
            Subtitle = $"{_company.Name}  —  FY {cert.FinancialYearLabel}  ·  AY {cert.AssessmentYearLabel}",
            Columns = new[]
            {
                new PrintColumn("Particular", 3.0, CellAlign.Left),
                new PrintColumn("Amount ₹", 1.0, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    private static string BuildResponsibleLine(Form24QDeductor d)
    {
        var name = string.IsNullOrWhiteSpace(d.ResponsiblePersonName) ? "—" : d.ResponsiblePersonName!;
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonDesignation)) parts.Add(d.ResponsiblePersonDesignation!);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonPan)) parts.Add("PAN " + d.ResponsiblePersonPan);
        return string.Join("  ·  ", parts);
    }
}
