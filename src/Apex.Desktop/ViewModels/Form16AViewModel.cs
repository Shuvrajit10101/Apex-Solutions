using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on a certificate screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class CertificateFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable return quarter on a certificate screen (1..4 + its calendar-month window label).</summary>
public sealed class CertificateQuarterOption
{
    public int Quarter { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One selectable <b>deductee</b> in the Form 16A picker (the party the certificate is issued to): PAN, name
/// and the quarter's total TDS withheld on them. Selecting it builds that deductee's certificate.</summary>
public sealed partial class Form16ADeducteeOptionVm : ViewModelBase
{
    public Guid LedgerId { get; init; }
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TdsTotal { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>TDS summary</b> row of the selected Form 16A (section + FVU code, date, assessable amount paid,
/// rate, TDS) — read verbatim off the <see cref="Form16ADeductionRow"/> projection.</summary>
public sealed class Form16ADeductionRowVm
{
    public string Section { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string AmountPaid { get; init; } = string.Empty;
    public string Tds { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
}

/// <summary>One <b>challan / deposit</b> row of the selected Form 16A (challan no, BSR code, deposit date, TDS
/// deposited for this deductee).</summary>
public sealed class Form16AChallanRowVm
{
    public string ChallanNo { get; init; } = string.Empty;
    public string BsrCode { get; init; } = string.Empty;
    public string DepositDate { get; init; } = string.Empty;
    public string TdsDeposited { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 16A</b> TDS-certificate report page (Reports → GST Reports → TDS → Form 16A; Phase 7 slice 7; catalog
/// §13; s.203 r.31). Pick a financial year + quarter and a <b>deductee</b>, and it shows that deductee's certificate —
/// the deductor block (name / TAN / PAN / responsible person from F11), the deductee block, the quarter's TDS summary
/// rows, the challan/deposit rows and the totals — all read verbatim off the pure <see cref="Form16A"/> projection (so
/// the figures match Form 26Q to the paisa). <b>Ctrl+A</b> / the Export button renders the deterministic, de-branded
/// certificate PDF via <see cref="Form16APdf"/> to the export folder; <b>Alt+B</b> saves it and returns to the menu.
///
/// <para>Gated: only reachable when TDS is enabled (the menu item + the open path are gated on
/// <see cref="Company.TdsEnabled"/>), so a non-TDS company never reaches it (ER-13). MVVM boundary: engine + IO only,
/// no Avalonia types (headlessly testable). Deterministic — the PDF bytes come straight off the engine (no clock/RNG).</para>
/// </summary>
public sealed partial class Form16AViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 16A — TDS Certificate";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Deductor (issuer) block.
    [ObservableProperty] private string _deductorName = string.Empty;
    [ObservableProperty] private string _deductorTan = string.Empty;
    [ObservableProperty] private string _deductorPan = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;

    // Deductee (recipient) block + totals for the selected certificate.
    [ObservableProperty] private string _deducteeName = string.Empty;
    [ObservableProperty] private string _deducteePan = string.Empty;
    [ObservableProperty] private string _totalAmountPaid = string.Empty;
    [ObservableProperty] private string _totalTdsDeducted = string.Empty;
    [ObservableProperty] private string _totalTdsDeposited = string.Empty;
    [ObservableProperty] private string _amountInWords = string.Empty;

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;

    // PDF export knobs (mirror the Form 26Q FVU export: folder + name; bytes are byte-stable off the engine).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private CertificateFyOption? _selectedYear;
    private CertificateQuarterOption? _selectedQuarter;
    private Form16ADeducteeOptionVm? _selectedDeductee;

    /// <summary>The financial years the certificate can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<CertificateFyOption> FinancialYears { get; } = new();

    /// <summary>The four return quarters (Q1 Apr-Jun … Q4 Jan-Mar).</summary>
    public ObservableCollection<CertificateQuarterOption> Quarters { get; } = new();

    /// <summary>The deductees with a withholding in the selected quarter (selectable in a ListBox).</summary>
    public ObservableCollection<Form16ADeducteeOptionVm> Deductees { get; } = new();

    /// <summary>The TDS summary rows of the selected deductee's certificate.</summary>
    public ObservableCollection<Form16ADeductionRowVm> Deductions { get; } = new();

    /// <summary>The challan/deposit rows of the selected deductee's certificate.</summary>
    public ObservableCollection<Form16AChallanRowVm> Challans { get; } = new();

    public Form16AViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new CertificateFyOption { StartYear = y });

        Quarters.Add(new CertificateQuarterOption { Quarter = 1, Label = "Q1 (Apr-Jun)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 2, Label = "Q2 (Jul-Sep)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 3, Label = "Q3 (Oct-Dec)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 4, Label = "Q4 (Jan-Mar)" });

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedQuarter = Quarters[0];

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the deductee list.</summary>
    public CertificateFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected return quarter (1..4); changing it rebuilds the deductee list.</summary>
    public CertificateQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    /// <summary>The selected deductee (the certificate is built for this party); changing it re-projects the detail.</summary>
    public Form16ADeducteeOptionVm? SelectedDeductee
    {
        get => _selectedDeductee;
        set { if (SetProperty(ref _selectedDeductee, value)) ProjectCertificate(); }
    }

    /// <summary>The certificate currently displayed (rebuilt on selection change). Null until a deductee is picked.</summary>
    public Form16A? Certificate { get; private set; }

    /// <summary>The PDF file name the export will write (FY + quarter + deductee PAN), no extension.</summary>
    public string ExportFileName
    {
        get
        {
            var fy = (SelectedYear?.Label ?? "FY").Replace('-', '_');
            var q = SelectedQuarter?.Quarter ?? 0;
            var pan = string.IsNullOrWhiteSpace(SelectedDeductee?.Pan) ? "NOPAN" : SelectedDeductee!.Pan;
            return $"Form16A_{fy}_Q{q}_{pan}";
        }
    }

    /// <summary>The full PDF file name including the <c>.pdf</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".pdf";

    /// <summary>(Re)builds the deductee list for the selected FY + quarter (one certificate per deductee, in
    /// first-appearance order) and re-selects the first, re-projecting its detail.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;

        _certs = Form16A.BuildAll(_company, fyStart, quarter);

        var (from, to) = Form26Q.QuarterWindow(fyStart, quarter);
        Subtitle = $"{_company.Name}  —  FY {FyLabel(fyStart)}  Q{quarter}  " +
                   $"({from:dd-MMM-yyyy} to {to:dd-MMM-yyyy})";

        Deductees.Clear();
        foreach (var cert in _certs)
            Deductees.Add(new Form16ADeducteeOptionVm
            {
                LedgerId = cert.Deductee.LedgerId,
                Pan = string.IsNullOrEmpty(cert.Deductee.Pan) ? "PANNOTAVBL" : cert.Deductee.Pan!,
                Name = cert.Deductee.Name,
                TdsTotal = IndianFormat.AmountAlways(cert.TotalTdsDeducted),
            });

        HighlightedIndex = Deductees.Count > 0 ? 0 : -1;
        SelectedDeductee = Deductees.FirstOrDefault();
        ProjectCertificate();
    }

    private System.Collections.Generic.IReadOnlyList<Form16A> _certs = Array.Empty<Form16A>();

    private void ProjectCertificate()
    {
        var cert = SelectedDeductee is null
            ? null
            : _certs.FirstOrDefault(c => c.Deductee.LedgerId == SelectedDeductee.LedgerId);
        Certificate = cert;

        Deductions.Clear();
        Challans.Clear();

        if (cert is null)
        {
            DeductorName = _company.Name;
            DeductorTan = "—"; DeductorPan = "—"; ResponsiblePerson = "—";
            DeducteeName = "—"; DeducteePan = "—";
            TotalAmountPaid = TotalTdsDeducted = TotalTdsDeposited = IndianFormat.AmountAlways(Money.Zero);
            AmountInWords = string.Empty;
            IsEmpty = true;
            StatusText = "No deductee has a TDS withholding in this quarter — nothing to certify.";
            RaiseExportNames();
            return;
        }

        DeductorName = cert.Deductor.Name;
        DeductorTan = string.IsNullOrEmpty(cert.Deductor.Tan) ? "—" : cert.Deductor.Tan;
        DeductorPan = string.IsNullOrWhiteSpace(cert.Deductor.Pan) ? "—" : cert.Deductor.Pan!;
        ResponsiblePerson = BuildResponsibleLine(cert.Deductor);

        DeducteeName = cert.Deductee.Name;
        DeducteePan = string.IsNullOrWhiteSpace(cert.Deductee.Pan) ? "PANNOTAVBL" : cert.Deductee.Pan!;

        foreach (var d in cert.Deductions)
            Deductions.Add(new Form16ADeductionRowVm
            {
                Section = d.SectionCode,
                FvuCode = d.FvuSectionCode,
                Date = d.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                AmountPaid = IndianFormat.AmountAlways(d.AmountPaid),
                Tds = IndianFormat.AmountAlways(d.TdsAmount),
                Rate = d.RatePercent.ToString("0.00", CultureInfo.InvariantCulture),
            });

        foreach (var ch in cert.Challans)
            Challans.Add(new Form16AChallanRowVm
            {
                ChallanNo = ch.ChallanNo,
                BsrCode = ch.BsrCode,
                DepositDate = ch.DepositDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                TdsDeposited = IndianFormat.AmountAlways(ch.TdsDeposited),
            });

        TotalAmountPaid = IndianFormat.AmountAlways(cert.TotalAmountPaid);
        TotalTdsDeducted = IndianFormat.AmountAlways(cert.TotalTdsDeducted);
        TotalTdsDeposited = IndianFormat.AmountAlways(cert.TotalTdsDeposited);
        AmountInWords = IndianAmountInWords.Convert(cert.TotalTdsDeducted.Amount);
        IsEmpty = cert.IsEmpty;
        StatusText = cert.IsEmpty
            ? "No withholding on this deductee this quarter — an empty certificate."
            : "Certificate ready — export to a de-branded PDF (Ctrl+A).";

        ExportStatus = string.Empty;
        RaiseExportNames();
    }

    private void RaiseExportNames()
    {
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>Moves the deductee highlight (Up/Down within the page); wraps. Keeps a live ListBox selection.</summary>
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
        if (value >= 0 && value < Deductees.Count) SelectedDeductee = Deductees[value];
    }

    /// <summary>
    /// Ctrl+A / the Export button: renders the selected deductee's Form 16A certificate to a deterministic,
    /// de-branded PDF (<see cref="Form16APdf.Render"/>) and writes it to <see cref="ExportFolder"/> under
    /// <see cref="ExportResolvedFileName"/>. The bytes come straight off the engine (byte-stable); the write goes
    /// through the injectable <paramref name="writeBytes"/> seam (null ⇒ real filesystem) so tests never touch disk.
    /// Returns true on success and sets <see cref="ExportStatus"/> either way. A no-op with a friendly status when no
    /// deductee is selected.
    /// </summary>
    public bool ExportPdf(Action<string, byte[]>? writeBytes = null)
    {
        if (Certificate is null)
        {
            ExportStatus = "Pick a deductee first — nothing to certify.";
            return false;
        }
        try
        {
            var bytes = Form16APdf.Render(Certificate, CertificatePages.Build(_company.Name));
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

    private string FyLabel(int start) => $"{start}-{(start + 1) % 100:00}";

    private static string BuildResponsibleLine(Form16ADeductorBlock d)
    {
        var name = string.IsNullOrWhiteSpace(d.ResponsiblePersonName) ? "—" : d.ResponsiblePersonName!;
        var sb = new StringBuilder(name);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonDesignation)) sb.Append("  ·  ").Append(d.ResponsiblePersonDesignation);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonPan)) sb.Append("  ·  PAN ").Append(d.ResponsiblePersonPan);
        return sb.ToString();
    }
}
