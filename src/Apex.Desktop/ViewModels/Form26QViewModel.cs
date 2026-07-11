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

/// <summary>A selectable financial year on the Form 26Q screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class Form26QFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable return quarter on the Form 26Q screen (1..4 + its calendar-month window label).</summary>
public sealed class Form26QQuarterOption
{
    public int Quarter { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One <b>deductee detail</b> row of the Form 26Q grid (Phase 7 slice 4): PAN, name, section (+ FVU code),
/// deduction date, assessable amount paid, TDS withheld, rate and the PAN-applied flag — all read verbatim off the
/// posted <see cref="Form26QDeducteeRow"/> projection (nothing recomputed here).</summary>
public sealed partial class Form26QDeducteeRowVm : ViewModelBase
{
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string AmountPaid { get; init; } = string.Empty;
    public string Tds { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
    public bool PanApplied { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>challan block</b> row of the Form 26Q grid (an ITNS-281 deposit attributed to the quarter):
/// challan no, BSR code, deposit date, amount, section and how many deductee rows it discharges.</summary>
public sealed class Form26QChallanRowVm
{
    public string ChallanNo { get; init; } = string.Empty;
    public string BsrCode { get; init; } = string.Empty;
    public string DepositDate { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string DeducteeCount { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 26Q</b> quarterly-TDS-return report page (Reports → GST Reports → TDS → Form 26Q; Phase 7 slice 4;
/// catalog §13). A read-only projection over the pure <see cref="Form26Q"/> engine: pick a financial year + quarter
/// and it shows the <b>deductor</b> block (TAN / type / responsible person from F11), the <b>challan</b> blocks, the
/// <b>deductee</b>-wise rows and the Form-27A-style <b>control totals</b> (with the FVU control-total warnings).
/// <b>Ctrl+A</b> / the Export button writes the FVU-compatible flat file via <see cref="FvuWriter"/> to the export
/// folder (like the CSV/XLSX exports); <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>Gated: only reachable when TDS is enabled (the menu item + the open path are gated on
/// <see cref="Company.TdsEnabled"/>), so a non-TDS company is byte-identical (ER-13). MVVM boundary: engine +
/// persistence only, no Avalonia types (headlessly testable). Deterministic — the FVU write takes its bytes from
/// the engine (no clock/RNG).</para>
/// </summary>
public sealed partial class Form26QViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 26Q — Quarterly TDS Return";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Deductor (filer) block.
    [ObservableProperty] private string _deductorTan = string.Empty;
    [ObservableProperty] private string _deductorType = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;

    // Control totals (Form-27A style).
    [ObservableProperty] private string _deducteeRecordCount = string.Empty;
    [ObservableProperty] private string _challanRecordCount = string.Empty;
    [ObservableProperty] private string _totalAmountPaid = string.Empty;
    [ObservableProperty] private string _totalTdsDeducted = string.Empty;
    [ObservableProperty] private string _totalDeposited = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _controlTotalsTally;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    // FVU export knobs (mirror ExportViewModel: folder + name; write is byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private Form26QFyOption? _selectedYear;
    private Form26QQuarterOption? _selectedQuarter;

    /// <summary>The financial years the return can be built for (the company FY + the two prior, for legacy/prior-year returns).</summary>
    public ObservableCollection<Form26QFyOption> FinancialYears { get; } = new();

    /// <summary>The four return quarters (Q1 Apr-Jun … Q4 Jan-Mar).</summary>
    public ObservableCollection<Form26QQuarterOption> Quarters { get; } = new();

    /// <summary>The challan blocks attributed to the selected quarter.</summary>
    public ObservableCollection<Form26QChallanRowVm> Challans { get; } = new();

    /// <summary>The deductee-wise rows of the selected quarter (kept selectable in a ListBox).</summary>
    public ObservableCollection<Form26QDeducteeRowVm> Deductees { get; } = new();

    public Form26QViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Form26QFyOption { StartYear = y });

        Quarters.Add(new Form26QQuarterOption { Quarter = 1, Label = "Q1 (Apr-Jun)" });
        Quarters.Add(new Form26QQuarterOption { Quarter = 2, Label = "Q2 (Jul-Sep)" });
        Quarters.Add(new Form26QQuarterOption { Quarter = 3, Label = "Q3 (Oct-Dec)" });
        Quarters.Add(new Form26QQuarterOption { Quarter = 4, Label = "Q4 (Jan-Mar)" });

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedQuarter = Quarters[0];

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year (its 01-Apr start year); changing it rebuilds the return.</summary>
    public Form26QFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected return quarter (1..4); changing it rebuilds the return.</summary>
    public Form26QQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    /// <summary>The FVU file name the export will write (derived from the selected FY + quarter, no extension).</summary>
    public string ExportFileName =>
        $"Form26Q_{(SelectedYear?.Label ?? "FY").Replace('-', '_')}_{SelectedQuarter?.Quarter ?? 0}";

    /// <summary>The full FVU file name including the pinned-version <c>.txt</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".txt";

    /// <summary>The currently-built return (rebuilt on selection change). Never null after construction.</summary>
    public Form26Q Return { get; private set; } = default!;

    /// <summary>(Re)builds the return for the selected FY + quarter and refreshes every block + the totals.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;

        var q = Form26Q.Build(_company, fyStart, quarter);
        Return = q;

        var (from, to) = Form26Q.QuarterWindow(fyStart, quarter);
        Subtitle = $"{_company.Name}  —  FY {q.FinancialYearLabel}  {q.QuarterLabel}  " +
                   $"({from:dd-MMM-yyyy} to {to:dd-MMM-yyyy})";

        DeductorTan = string.IsNullOrEmpty(q.Deductor.Tan) ? "—" : q.Deductor.Tan;
        DeductorType = q.Deductor.DeductorType.ToString();
        ResponsiblePerson = BuildResponsibleLine(q.Deductor);

        Challans.Clear();
        foreach (var ch in q.Challans)
            Challans.Add(new Form26QChallanRowVm
            {
                ChallanNo = ch.ChallanNo,
                BsrCode = ch.BsrCode,
                DepositDate = ch.DepositDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                Amount = IndianFormat.AmountAlways(ch.Amount),
                Section = ch.Section,
                DeducteeCount = ch.DeducteeRows.Count.ToString(CultureInfo.InvariantCulture),
            });

        Deductees.Clear();
        foreach (var r in q.Deductees)
            Deductees.Add(new Form26QDeducteeRowVm
            {
                Pan = string.IsNullOrEmpty(r.DeducteePan) ? "PANNOTAVBL" : r.DeducteePan,
                Name = r.DeducteeName,
                Section = r.SectionCode,
                FvuCode = r.FvuSectionCode,
                Date = r.DeductionDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                AmountPaid = IndianFormat.AmountAlways(r.AmountPaid),
                Tds = IndianFormat.AmountAlways(r.TdsAmount),
                Rate = r.RatePercent.ToString("0.00", CultureInfo.InvariantCulture),
                PanApplied = r.PanApplied,
            });

        var totals = q.ControlTotals;
        DeducteeRecordCount = totals.DeducteeRecordCount.ToString(CultureInfo.InvariantCulture);
        ChallanRecordCount = totals.ChallanRecordCount.ToString(CultureInfo.InvariantCulture);
        TotalAmountPaid = IndianFormat.AmountAlways(totals.TotalAmountPaid);
        TotalTdsDeducted = IndianFormat.AmountAlways(totals.TotalTdsDeducted);
        TotalDeposited = IndianFormat.AmountAlways(totals.TotalDepositedAsPerChallans);
        ControlTotalsTally = totals.Tallies;
        IsEmpty = q.IsEmpty;

        var problems = totals.Validate();
        StatusText = q.IsEmpty
            ? "Nothing to file this quarter — no TDS deducted and no challan attributed."
            : problems.Count == 0
                ? "Control totals tally — the return is ready to export to FVU."
                : string.Join("  ", problems);

        HighlightedIndex = Deductees.Count > 0 ? 0 : -1;
        Message = null;
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
    /// Ctrl+A / the Export button: writes the FVU-compatible flat file (<see cref="FvuWriter.Write"/>) for the
    /// current return to <see cref="ExportFolder"/> under <see cref="ExportResolvedFileName"/>. The bytes come
    /// straight off the engine (byte-stable, de-branded); the write goes through the injectable
    /// <paramref name="writeBytes"/> seam (null ⇒ real filesystem) so tests never touch disk. Returns true on
    /// success and sets <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportFvu(Action<string, byte[]>? writeBytes = null)
    {
        try
        {
            var bytes = FvuWriter.Write(Return);
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
            ExportStatus = "Could not export FVU file: " + ex.Message;
            return false;
        }
    }

    private static string BuildResponsibleLine(Form26QDeductor d)
    {
        var name = string.IsNullOrWhiteSpace(d.ResponsiblePersonName) ? "—" : d.ResponsiblePersonName!;
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonDesignation)) parts.Add(d.ResponsiblePersonDesignation!);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonPan)) parts.Add("PAN " + d.ResponsiblePersonPan);
        return string.Join("  ·  ", parts);
    }
}
