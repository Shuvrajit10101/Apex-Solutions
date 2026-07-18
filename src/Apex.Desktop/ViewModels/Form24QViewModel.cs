using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the Form 24Q screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class Form24QFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable return quarter on the Form 24Q screen (1..4 + its calendar-month window label).</summary>
public sealed class Form24QQuarterOption
{
    public int Quarter { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One <b>Annexure I</b> deductee row of the Form 24Q grid (Phase 8 slice 7): employee PAN + name, the salary
/// section code, the deduction date and the §192 TDS withheld — read verbatim off the posted
/// <see cref="Form24QDeducteeRow"/> projection (nothing recomputed here).</summary>
public sealed partial class Form24QDeducteeRowVm : ViewModelBase
{
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Tds { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>Annexure II</b> row of the Form 24Q grid (Q4 only): the employee's full-year salary + tax
/// computation — regime, gross salary, standard deduction, Chapter VI-A deductions, taxable income, income-tax,
/// surcharge, cess, total tax and the year's tax deducted — read verbatim off the <see cref="Form24QAnnexureIIRow"/>
/// projection.</summary>
public sealed class Form24QAnnexureIIRowVm
{
    public string Name { get; init; } = string.Empty;
    public string Pan { get; init; } = string.Empty;
    public string Regime { get; init; } = string.Empty;
    public string GrossSalary { get; init; } = string.Empty;
    public string StandardDeduction { get; init; } = string.Empty;
    public string ChapterVia { get; init; } = string.Empty;
    public string TaxableIncome { get; init; } = string.Empty;
    public string IncomeTax { get; init; } = string.Empty;
    public string Surcharge { get; init; } = string.Empty;
    public string Cess { get; init; } = string.Empty;
    public string TotalTax { get; init; } = string.Empty;
    public string TaxDeducted { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 24Q</b> quarterly salary-TDS-return report page (Reports → Statutory Reports → Payroll → Form 24Q;
/// Phase 8 slice 7; RQ-13; catalog §14) — the <b>salary sibling</b> of <see cref="Form26QViewModel"/>. A read-only
/// projection over the pure <see cref="Form24Q"/> engine: pick a financial year + quarter (+ the salary deductor
/// category 92B/92A/92C) and it shows the <b>deductor</b> block (TAN / type / responsible person reused from F11
/// Enable TDS), the <b>Annexure I</b> deductee rows (filed every quarter) and — in <b>Q4</b> — the <b>Annexure II</b>
/// per-employee annual salary + tax computation that drives Form 16 Part B, with the control totals. <b>Ctrl+A</b> /
/// the Export button writes the deterministic offline flat file; <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>Gated: only reachable when §192 salary TDS is enabled (the menu item + the open path are gated on
/// <see cref="Company.SalaryTdsEnabled"/>), so a non-salary-TDS company is byte-identical (ER-13). Every figure is
/// read off the posted §192 deduction lines / recomputed by the engine over the actual annual income, so the return
/// reconciles to the salary-TDS-payable postings by construction. MVVM boundary: engine + persistence only, no
/// Avalonia types (headlessly testable). Deterministic — no clock/RNG.</para>
/// </summary>
public sealed partial class Form24QViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 24Q — Quarterly Salary-TDS Return (§192)";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Deductor (filer) block — reused from the Phase-7 deductor config.
    [ObservableProperty] private string _deductorTan = string.Empty;
    [ObservableProperty] private string _deductorType = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;

    // Control totals.
    [ObservableProperty] private string _deducteeRecordCount = string.Empty;
    [ObservableProperty] private string _totalTdsDeducted = string.Empty;
    [ObservableProperty] private string _annexureIiCount = string.Empty;
    [ObservableProperty] private string _annexureIiTaxDeducted = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isQ4;
    [ObservableProperty] private int _highlightedIndex = -1;

    // Export knobs (mirror the Form 26Q FVU export: folder + name; bytes are byte-stable off the engine figures).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private Form24QFyOption? _selectedYear;
    private Form24QQuarterOption? _selectedQuarter;
    private SalarySectionCodeOption? _selectedSectionCode;

    /// <summary>The financial years the return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<Form24QFyOption> FinancialYears { get; } = new();

    /// <summary>The four return quarters (Q1 Apr-Jun … Q4 Jan-Mar).</summary>
    public ObservableCollection<Form24QQuarterOption> Quarters { get; } = new();

    /// <summary>The salary deductor-category options (92B private / 92A govt / 92C union-govt).</summary>
    public ObservableCollection<SalarySectionCodeOption> SectionCodes { get; } = new();

    /// <summary>The Annexure I deductee-wise rows of the selected quarter (kept selectable in a ListBox).</summary>
    public ObservableCollection<Form24QDeducteeRowVm> Deductees { get; } = new();

    /// <summary>The Annexure II per-employee annual rows (populated only in Q4).</summary>
    public ObservableCollection<Form24QAnnexureIIRowVm> AnnexureII { get; } = new();

    public Form24QViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Form24QFyOption { StartYear = y });

        Quarters.Add(new Form24QQuarterOption { Quarter = 1, Label = "Q1 (Apr-Jun)" });
        Quarters.Add(new Form24QQuarterOption { Quarter = 2, Label = "Q2 (Jul-Sep)" });
        Quarters.Add(new Form24QQuarterOption { Quarter = 3, Label = "Q3 (Oct-Dec)" });
        Quarters.Add(new Form24QQuarterOption { Quarter = 4, Label = "Q4 (Jan-Mar)" });

        foreach (var opt in SalarySectionCodeOption.All) SectionCodes.Add(opt);

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedQuarter = Quarters[3]; // default to Q4 so the annual Annexure II is visible on open
        _selectedSectionCode = SectionCodes.First();

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the return.</summary>
    public Form24QFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected return quarter (1..4); changing it rebuilds the return.</summary>
    public Form24QQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    /// <summary>The selected salary deductor category (92B/92A/92C); changing it rebuilds the return.</summary>
    public SalarySectionCodeOption? SelectedSectionCode
    {
        get => _selectedSectionCode;
        set { if (SetProperty(ref _selectedSectionCode, value)) Rebuild(); }
    }

    /// <summary>The FVU file name the export will write (FY + quarter), no extension.</summary>
    public string ExportFileName =>
        $"Form24Q_{(SelectedYear?.Label ?? "FY").Replace('-', '_')}_Q{SelectedQuarter?.Quarter ?? 0}";

    /// <summary>The full FVU file name including the pinned-version <c>.txt</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".txt";

    /// <summary>The currently-built return (rebuilt on selection change). Never null after construction.</summary>
    public Form24Q Return { get; private set; } = default!;

    /// <summary>(Re)builds the return for the selected FY + quarter + section code and refreshes every block + totals.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;
        var section = SelectedSectionCode?.Code ?? Form24Q.SalarySectionCode;

        var q = Form24Q.Build(_company, fyStart, quarter, section);
        Return = q;
        IsQ4 = quarter == 4;

        var (from, to) = Form24Q.QuarterWindow(fyStart, quarter);
        Subtitle = $"{_company.Name}  —  FY {q.FinancialYearLabel}  {q.QuarterLabel}  "
                   + $"({from:dd-MMM-yyyy} to {to:dd-MMM-yyyy})  ·  section {section}";

        DeductorTan = string.IsNullOrEmpty(q.Deductor.Tan) ? "—" : q.Deductor.Tan;
        DeductorType = q.Deductor.DeductorType.ToString();
        ResponsiblePerson = BuildResponsibleLine(q.Deductor);

        Deductees.Clear();
        foreach (var r in q.Deductees)
            Deductees.Add(new Form24QDeducteeRowVm
            {
                Pan = string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan!,
                Name = r.EmployeeName,
                Section = r.SectionCode,
                Date = ApexDate.Format(r.DeductionDate),
                Tds = IndianFormat.AmountAlways(r.TdsAmount),
            });

        AnnexureII.Clear();
        foreach (var r in q.AnnexureII)
            AnnexureII.Add(new Form24QAnnexureIIRowVm
            {
                Name = r.EmployeeName,
                Pan = string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan!,
                Regime = r.Regime == TaxRegime.Old ? "Old" : "New",
                GrossSalary = IndianFormat.AmountAlways(r.GrossSalary),
                StandardDeduction = IndianFormat.AmountAlways(r.StandardDeduction),
                ChapterVia = IndianFormat.AmountAlways(r.ChapterViaDeductions),
                TaxableIncome = IndianFormat.AmountAlways(r.TaxableIncome),
                IncomeTax = IndianFormat.AmountAlways(r.IncomeTax),
                Surcharge = IndianFormat.AmountAlways(r.Surcharge),
                Cess = IndianFormat.AmountAlways(r.Cess),
                TotalTax = IndianFormat.AmountAlways(r.TotalTax),
                TaxDeducted = IndianFormat.AmountAlways(r.TaxDeducted),
            });

        DeducteeRecordCount = q.Deductees.Count.ToString(CultureInfo.InvariantCulture);
        TotalTdsDeducted = IndianFormat.AmountAlways(q.TotalTdsDeducted);
        AnnexureIiCount = q.AnnexureII.Count.ToString(CultureInfo.InvariantCulture);
        AnnexureIiTaxDeducted = IndianFormat.AmountAlways(q.AnnexureIITaxDeducted);
        IsEmpty = q.IsEmpty;

        StatusText = q.IsEmpty
            ? "Nothing to file this quarter — no §192 salary TDS deducted."
            : IsQ4
                ? $"Return ready — {q.Deductees.Count} deductee row(s) + Annexure II for {q.AnnexureII.Count} employee(s)."
                : $"Return ready — {q.Deductees.Count} deductee row(s). (Annexure II is filed in Q4 only.)";

        HighlightedIndex = Deductees.Count > 0 ? 0 : -1;
        ExportStatus = string.Empty;
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>Moves the deductee-row highlight (Up/Down within the page); wraps. Keeps a live ListBox selection.</summary>
    public void MoveHighlight(int direction)
    {
        if (Deductees.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Deductees.Count) % Deductees.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Deductees.Count; i++)
            Deductees[i].IsHighlighted = i == value;
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the deterministic offline salary-TDS flat file for the current return to
    /// <see cref="ExportFolder"/> under <see cref="ExportResolvedFileName"/>. The bytes come straight off the engine
    /// figures (byte-stable, de-branded); the write goes through the injectable <paramref name="writeBytes"/> seam
    /// (null ⇒ real filesystem) so tests never touch disk. Returns true on success and sets
    /// <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportFvu(Action<string, byte[]>? writeBytes = null)
    {
        try
        {
            var bytes = BuildFlatFile();
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
            ExportStatus = "Could not export the Form 24Q file: " + ex.Message;
            return false;
        }
    }

    /// <summary>Builds the deterministic Form-24Q offline flat file (UTF-8, no BOM): a deductor header, the Annexure I
    /// deductee lines, the Q4 Annexure II annual-computation lines and the control totals. De-branded; whole figures
    /// off the engine so the file reconciles to the salary-TDS-payable postings.</summary>
    private byte[] BuildFlatFile()
    {
        var q = Return;
        var sb = new StringBuilder();
        sb.Append("FORM 24Q|SALARY TDS RETURN u/s 192\n");
        sb.Append("FY|").Append(q.FinancialYearLabel).Append("|Q|").Append(q.Quarter).Append('\n');
        sb.Append("DEDUCTOR|").Append(Field(q.Deductor.Tan)).Append('|')
          .Append(q.Deductor.DeductorType).Append('|')
          .Append(Field(q.Deductor.ResponsiblePersonName)).Append('\n');

        sb.Append("ANNEXURE I|PAN|NAME|SECTION|DATE|TDS\n");
        var seq = 0;
        foreach (var r in q.Deductees)
            sb.Append("AI|").Append(++seq).Append('|')
              .Append(Field(string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan)).Append('|')
              .Append(Field(r.EmployeeName)).Append('|')
              .Append(r.SectionCode).Append('|')
              .Append(r.DeductionDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.TdsAmount.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');

        if (q.Quarter == 4)
        {
            sb.Append("ANNEXURE II|PAN|NAME|REGIME|GROSS|STDDED|CHVIA|TAXABLE|INCOMETAX|SURCHARGE|CESS|TOTALTAX|TDS\n");
            seq = 0;
            foreach (var r in q.AnnexureII)
                sb.Append("AII|").Append(++seq).Append('|')
                  .Append(Field(string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan)).Append('|')
                  .Append(Field(r.EmployeeName)).Append('|')
                  .Append(r.Regime == TaxRegime.Old ? "OLD" : "NEW").Append('|')
                  .Append(M(r.GrossSalary)).Append('|').Append(M(r.StandardDeduction)).Append('|')
                  .Append(M(r.ChapterViaDeductions)).Append('|').Append(M(r.TaxableIncome)).Append('|')
                  .Append(M(r.IncomeTax)).Append('|').Append(M(r.Surcharge)).Append('|')
                  .Append(M(r.Cess)).Append('|').Append(M(r.TotalTax)).Append('|')
                  .Append(M(r.TaxDeducted)).Append('\n');
        }

        sb.Append("CONTROL|DEDUCTEES|").Append(q.Deductees.Count)
          .Append("|TOTALTDS|").Append(M(q.TotalTdsDeducted)).Append('\n');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    private static string M(Money m) => m.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Field(string? s) => (s ?? string.Empty).Replace('|', ' ').Trim();

    private static string BuildResponsibleLine(Form24QDeductor d)
    {
        var name = string.IsNullOrWhiteSpace(d.ResponsiblePersonName) ? "—" : d.ResponsiblePersonName!;
        var parts = new System.Collections.Generic.List<string> { name };
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonDesignation)) parts.Add(d.ResponsiblePersonDesignation!);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonPan)) parts.Add("PAN " + d.ResponsiblePersonPan);
        return string.Join("  ·  ", parts);
    }
}
