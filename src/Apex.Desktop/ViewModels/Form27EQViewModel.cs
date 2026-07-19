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

/// <summary>A selectable financial year on the Form 27EQ screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class Form27EQFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable return quarter on the Form 27EQ screen (1..4 + its calendar-month window label).</summary>
public sealed class Form27EQQuarterOption
{
    public int Quarter { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One <b>collectee detail</b> row of the Form 27EQ grid (Phase 7 slice 6): PAN, name, §206C collection code
/// (+ FVU code), collection date, assessable amount received, TCS collected, rate and the PAN-applied flag — all read
/// verbatim off the posted <see cref="Form27EQCollecteeRow"/> projection (nothing recomputed here).</summary>
public sealed partial class Form27EQCollecteeRowVm : ViewModelBase
{
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CollectionCode { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string AmountReceived { get; init; } = string.Empty;
    public string Tcs { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
    public bool PanApplied { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>challan block</b> row of the Form 27EQ grid (an ITNS-281 deposit attributed to the quarter):
/// challan no, BSR code, deposit date, amount, collection code and how many collectee rows it discharges.</summary>
public sealed class Form27EQChallanRowVm
{
    public string ChallanNo { get; init; } = string.Empty;
    public string BsrCode { get; init; } = string.Empty;
    public string DepositDate { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string CollectionCode { get; init; } = string.Empty;
    public string CollecteeCount { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 27EQ</b> quarterly-TCS-return report page (Reports → GST Reports → TCS → Form 27EQ; Phase 7 slice 6;
/// catalog §13). The exact mirror of <see cref="Form26QViewModel"/> for the collector's side: a read-only projection
/// over the pure <see cref="Form27EQ"/> engine: pick a financial year + quarter and it shows the <b>collector</b>
/// block (TAN / type / responsible person from F11), the <b>challan</b> blocks, the <b>collectee</b>-wise rows and the
/// Form-27A-style <b>control totals</b> (with the FVU control-total warnings). <b>Ctrl+A</b> / the Export button writes
/// the FVU-compatible flat file via <see cref="FvuWriter"/> to the export folder (like the CSV/XLSX exports);
/// <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>Gated: only reachable when TCS is enabled (the menu item + the open path are gated on
/// <see cref="Company.TcsEnabled"/>), so a non-TCS company is byte-identical (ER-13). MVVM boundary: engine +
/// persistence only, no Avalonia types (headlessly testable). Deterministic — the FVU write takes its bytes from the
/// engine (no clock/RNG).</para>
/// </summary>
public sealed partial class Form27EQViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 27EQ — Quarterly TCS Return";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Collector (filer) block.
    [ObservableProperty] private string _collectorTan = string.Empty;
    [ObservableProperty] private string _collectorType = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;

    // Control totals (Form-27A style).
    [ObservableProperty] private string _collecteeRecordCount = string.Empty;
    [ObservableProperty] private string _challanRecordCount = string.Empty;
    [ObservableProperty] private string _totalAmountReceived = string.Empty;
    [ObservableProperty] private string _totalTcsCollected = string.Empty;
    [ObservableProperty] private string _totalDeposited = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _controlTotalsTally;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    // FVU export knobs (mirror ExportViewModel: folder + name; write is byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private Form27EQFyOption? _selectedYear;
    private Form27EQQuarterOption? _selectedQuarter;

    /// <summary>The financial years the return can be built for (the company FY + the two prior, for legacy/prior-year returns).</summary>
    public ObservableCollection<Form27EQFyOption> FinancialYears { get; } = new();

    /// <summary>The four return quarters (Q1 Apr-Jun … Q4 Jan-Mar).</summary>
    public ObservableCollection<Form27EQQuarterOption> Quarters { get; } = new();

    /// <summary>The challan blocks attributed to the selected quarter.</summary>
    public ObservableCollection<Form27EQChallanRowVm> Challans { get; } = new();

    /// <summary>The collectee-wise rows of the selected quarter (kept selectable in a ListBox).</summary>
    public ObservableCollection<Form27EQCollecteeRowVm> Collectees { get; } = new();

    public Form27EQViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Form27EQFyOption { StartYear = y });

        Quarters.Add(new Form27EQQuarterOption { Quarter = 1, Label = "Q1 (Apr-Jun)" });
        Quarters.Add(new Form27EQQuarterOption { Quarter = 2, Label = "Q2 (Jul-Sep)" });
        Quarters.Add(new Form27EQQuarterOption { Quarter = 3, Label = "Q3 (Oct-Dec)" });
        Quarters.Add(new Form27EQQuarterOption { Quarter = 4, Label = "Q4 (Jan-Mar)" });

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedQuarter = Quarters[0];

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year (its 01-Apr start year); changing it rebuilds the return.</summary>
    public Form27EQFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected return quarter (1..4); changing it rebuilds the return.</summary>
    public Form27EQQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    /// <summary>The FVU file name the export will write (derived from the selected FY + quarter, no extension).
    /// <para><b>Deliberately NOT FY-gated</b> (CA S9 closeout) — a machine file whose name may be bound by the FVU/RPU
    /// utility's conventions and whose wire format is pinned, never seen by the collectee. Contrast the per-recipient
    /// PDF certificate names, which ARE gated. See <c>Form24QViewModel.ExportFileName</c> for the full rationale.</para></summary>
    public string ExportFileName =>
        $"Form27EQ_{(SelectedYear?.Label ?? "FY").Replace('-', '_')}_{SelectedQuarter?.Quarter ?? 0}";

    /// <summary>The full FVU file name including the pinned-version <c>.txt</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".txt";

    /// <summary>The currently-built return (rebuilt on selection change). Never null after construction.</summary>
    public Form27EQ Return { get; private set; } = default!;

    /// <summary>(Re)builds the return for the selected FY + quarter and refreshes every block + the totals.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;

        // CA S9 closeout: the page heading is FY-gated, like Form24QViewModel's. The Miller cascade keeps the
        // parent menu row visible beside the page, so an ungated "Form 27EQ" heading would sit on screen
        // next to the renumbered menu label that opened it. Prior years are unchanged (ER-13).
        Title = $"Form {StatuteVocabulary.FormLabel("27EQ", fyStart)} — Quarterly TCS Return";

        var q = Form27EQ.Build(_company, fyStart, quarter);
        Return = q;

        var (from, to) = Form27EQ.QuarterWindow(fyStart, quarter);
        Subtitle = $"{_company.Name}  —  FY {q.FinancialYearLabel}  {q.QuarterLabel}  " +
                   $"({from:dd-MMM-yyyy} to {to:dd-MMM-yyyy})";

        CollectorTan = string.IsNullOrEmpty(q.Collector.Tan) ? "—" : q.Collector.Tan;
        CollectorType = q.Collector.CollectorType.ToString();
        ResponsiblePerson = BuildResponsibleLine(q.Collector);

        Challans.Clear();
        foreach (var ch in q.Challans)
            Challans.Add(new Form27EQChallanRowVm
            {
                ChallanNo = ch.ChallanNo,
                BsrCode = ch.BsrCode,
                DepositDate = ApexDate.Format(ch.DepositDate),
                Amount = IndianFormat.AmountAlways(ch.Amount),
                CollectionCode = ch.CollectionCode,
                CollecteeCount = ch.CollecteeRows.Count.ToString(CultureInfo.InvariantCulture),
            });

        Collectees.Clear();
        foreach (var r in q.Collectees)
            Collectees.Add(new Form27EQCollecteeRowVm
            {
                Pan = string.IsNullOrEmpty(r.CollecteePan) ? "PANNOTAVBL" : r.CollecteePan,
                Name = r.CollecteeName,
                CollectionCode = r.CollectionCode,
                FvuCode = r.FvuCollectionCode,
                Date = ApexDate.Format(r.CollectionDate),
                AmountReceived = IndianFormat.AmountAlways(r.AmountReceived),
                Tcs = IndianFormat.AmountAlways(r.TcsAmount),
                Rate = r.RatePercent.ToString("0.00", CultureInfo.InvariantCulture),
                PanApplied = r.PanApplied,
            });

        var totals = q.ControlTotals;
        CollecteeRecordCount = totals.CollecteeRecordCount.ToString(CultureInfo.InvariantCulture);
        ChallanRecordCount = totals.ChallanRecordCount.ToString(CultureInfo.InvariantCulture);
        TotalAmountReceived = IndianFormat.AmountAlways(totals.TotalAmountReceived);
        TotalTcsCollected = IndianFormat.AmountAlways(totals.TotalTcsCollected);
        TotalDeposited = IndianFormat.AmountAlways(totals.TotalDepositedAsPerChallans);
        ControlTotalsTally = totals.Tallies;
        IsEmpty = q.IsEmpty;

        var problems = totals.Validate();
        StatusText = q.IsEmpty
            ? "Nothing to file this quarter — no TCS collected and no challan attributed."
            : problems.Count == 0
                ? "Control totals tally — the return is ready to export to FVU."
                : string.Join("  ", problems);

        HighlightedIndex = Collectees.Count > 0 ? 0 : -1;
        Message = null;
        ExportStatus = string.Empty;
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>Moves the collectee-row highlight (Up/Down within the page); wraps. Keeps a live ListBox selection.</summary>
    public void MoveHighlight(int direction)
    {
        if (Collectees.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Collectees.Count) % Collectees.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Collectees.Count; i++)
            Collectees[i].IsHighlighted = i == value;
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the FVU-compatible flat file (<see cref="FvuWriter.Write(Form27EQ)"/>) for
    /// the current return to <see cref="ExportFolder"/> under <see cref="ExportResolvedFileName"/>. The bytes come
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

    private static string BuildResponsibleLine(Form27EQCollector c)
    {
        var name = string.IsNullOrWhiteSpace(c.ResponsiblePersonName) ? "—" : c.ResponsiblePersonName!;
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(c.ResponsiblePersonDesignation)) parts.Add(c.ResponsiblePersonDesignation!);
        if (!string.IsNullOrWhiteSpace(c.ResponsiblePersonPan)) parts.Add("PAN " + c.ResponsiblePersonPan);
        return string.Join("  ·  ", parts);
    }
}
