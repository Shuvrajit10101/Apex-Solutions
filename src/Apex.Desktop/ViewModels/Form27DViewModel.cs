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

/// <summary>One selectable <b>collectee</b> in the Form 27D picker (the party the TCS certificate is issued to): PAN,
/// name and the quarter's total TCS collected from them. Selecting it builds that collectee's certificate.</summary>
public sealed partial class Form27DCollecteeOptionVm : ViewModelBase
{
    public Guid LedgerId { get; init; }
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TcsTotal { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>One <b>TCS summary</b> row of the selected Form 27D (collection code + FVU code, date, amount received,
/// rate, TCS) — read verbatim off the <see cref="Form27DCollectionRow"/> projection.</summary>
public sealed class Form27DCollectionRowVm
{
    public string CollectionCode { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string AmountReceived { get; init; } = string.Empty;
    public string Tcs { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
}

/// <summary>One <b>challan / deposit</b> row of the selected Form 27D (challan no, BSR code, deposit date, TCS
/// deposited for this collectee).</summary>
public sealed class Form27DChallanRowVm
{
    public string ChallanNo { get; init; } = string.Empty;
    public string BsrCode { get; init; } = string.Empty;
    public string DepositDate { get; init; } = string.Empty;
    public string TcsDeposited { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form 27D</b> TCS-certificate report page (Reports → GST Reports → TCS → Form 27D; Phase 7 slice 7; catalog
/// §13; s.206C r.37D) — the exact mirror of <see cref="Form16AViewModel"/> for the collector's side. Pick a financial
/// year + quarter and a <b>collectee</b>, and it shows that collectee's certificate — the collector block (name / TAN /
/// PAN / responsible person from F11), the collectee block, the quarter's TCS summary rows, the challan/deposit rows
/// and the totals — all read verbatim off the pure <see cref="Form27D"/> projection (so the figures match Form 27EQ to
/// the paisa). <b>Ctrl+A</b> / the Export button renders the deterministic, de-branded certificate PDF via
/// <see cref="Form27DPdf"/> to the export folder; <b>Alt+B</b> saves it and returns to the menu.
///
/// <para>Gated: only reachable when TCS is enabled (the menu item + the open path are gated on
/// <see cref="Company.TcsEnabled"/>), so a non-TCS company never reaches it (ER-13). MVVM boundary: engine + IO only.</para>
/// </summary>
public sealed partial class Form27DViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 27D — TCS Certificate";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Collector (issuer) block.
    [ObservableProperty] private string _collectorName = string.Empty;
    [ObservableProperty] private string _collectorTan = string.Empty;
    [ObservableProperty] private string _collectorPan = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;

    // Collectee (recipient) block + totals for the selected certificate.
    [ObservableProperty] private string _collecteeName = string.Empty;
    [ObservableProperty] private string _collecteePan = string.Empty;
    [ObservableProperty] private string _totalAmountReceived = string.Empty;
    [ObservableProperty] private string _totalTcsCollected = string.Empty;
    [ObservableProperty] private string _totalTcsDeposited = string.Empty;
    [ObservableProperty] private string _amountInWords = string.Empty;

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;

    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private CertificateFyOption? _selectedYear;
    private CertificateQuarterOption? _selectedQuarter;
    private Form27DCollecteeOptionVm? _selectedCollectee;
    private System.Collections.Generic.IReadOnlyList<Form27D> _certs = Array.Empty<Form27D>();

    public ObservableCollection<CertificateFyOption> FinancialYears { get; } = new();
    public ObservableCollection<CertificateQuarterOption> Quarters { get; } = new();

    /// <summary>The collectees with a collection in the selected quarter (selectable in a ListBox).</summary>
    public ObservableCollection<Form27DCollecteeOptionVm> Collectees { get; } = new();

    /// <summary>The TCS summary rows of the selected collectee's certificate.</summary>
    public ObservableCollection<Form27DCollectionRowVm> Collections { get; } = new();

    /// <summary>The challan/deposit rows of the selected collectee's certificate.</summary>
    public ObservableCollection<Form27DChallanRowVm> Challans { get; } = new();

    public Form27DViewModel(Company company)
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

    public CertificateFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    public CertificateQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    public Form27DCollecteeOptionVm? SelectedCollectee
    {
        get => _selectedCollectee;
        set { if (SetProperty(ref _selectedCollectee, value)) ProjectCertificate(); }
    }

    /// <summary>The certificate currently displayed. Null until a collectee is picked.</summary>
    public Form27D? Certificate { get; private set; }

    public string ExportFileName
    {
        get
        {
            var fy = (SelectedYear?.Label ?? "FY").Replace('-', '_');
            var q = SelectedQuarter?.Quarter ?? 0;
            var pan = string.IsNullOrWhiteSpace(SelectedCollectee?.Pan) ? "NOPAN" : SelectedCollectee!.Pan;
            return $"Form27D_{fy}_Q{q}_{pan}";
        }
    }

    public string ExportResolvedFileName => ExportFileName + ".pdf";

    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;

        _certs = Form27D.BuildAll(_company, fyStart, quarter);

        var (from, to) = Form27EQ.QuarterWindow(fyStart, quarter);
        Subtitle = $"{_company.Name}  —  FY {FyLabel(fyStart)}  Q{quarter}  " +
                   $"({from:dd-MMM-yyyy} to {to:dd-MMM-yyyy})";

        Collectees.Clear();
        foreach (var cert in _certs)
            Collectees.Add(new Form27DCollecteeOptionVm
            {
                LedgerId = cert.Collectee.LedgerId,
                Pan = string.IsNullOrEmpty(cert.Collectee.Pan) ? "PANNOTAVBL" : cert.Collectee.Pan!,
                Name = cert.Collectee.Name,
                TcsTotal = IndianFormat.AmountAlways(cert.TotalTcsCollected),
            });

        HighlightedIndex = Collectees.Count > 0 ? 0 : -1;
        SelectedCollectee = Collectees.FirstOrDefault();
        ProjectCertificate();
    }

    private void ProjectCertificate()
    {
        var cert = SelectedCollectee is null
            ? null
            : _certs.FirstOrDefault(c => c.Collectee.LedgerId == SelectedCollectee.LedgerId);
        Certificate = cert;

        Collections.Clear();
        Challans.Clear();

        if (cert is null)
        {
            CollectorName = _company.Name;
            CollectorTan = "—"; CollectorPan = "—"; ResponsiblePerson = "—";
            CollecteeName = "—"; CollecteePan = "—";
            TotalAmountReceived = TotalTcsCollected = TotalTcsDeposited = IndianFormat.AmountAlways(Money.Zero);
            AmountInWords = string.Empty;
            IsEmpty = true;
            StatusText = "No collectee has a TCS collection in this quarter — nothing to certify.";
            RaiseExportNames();
            return;
        }

        CollectorName = cert.Collector.Name;
        CollectorTan = string.IsNullOrEmpty(cert.Collector.Tan) ? "—" : cert.Collector.Tan;
        CollectorPan = string.IsNullOrWhiteSpace(cert.Collector.Pan) ? "—" : cert.Collector.Pan!;
        ResponsiblePerson = BuildResponsibleLine(cert.Collector);

        CollecteeName = cert.Collectee.Name;
        CollecteePan = string.IsNullOrWhiteSpace(cert.Collectee.Pan) ? "PANNOTAVBL" : cert.Collectee.Pan!;

        foreach (var d in cert.Collections)
            Collections.Add(new Form27DCollectionRowVm
            {
                CollectionCode = d.CollectionCode,
                FvuCode = d.FvuCollectionCode,
                Date = ApexDate.Format(d.Date),
                AmountReceived = IndianFormat.AmountAlways(d.AmountReceived),
                Tcs = IndianFormat.AmountAlways(d.TcsAmount),
                Rate = d.RatePercent.ToString("0.00", CultureInfo.InvariantCulture),
            });

        foreach (var ch in cert.Challans)
            Challans.Add(new Form27DChallanRowVm
            {
                ChallanNo = ch.ChallanNo,
                BsrCode = ch.BsrCode,
                DepositDate = ApexDate.Format(ch.DepositDate),
                TcsDeposited = IndianFormat.AmountAlways(ch.TcsDeposited),
            });

        TotalAmountReceived = IndianFormat.AmountAlways(cert.TotalAmountReceived);
        TotalTcsCollected = IndianFormat.AmountAlways(cert.TotalTcsCollected);
        TotalTcsDeposited = IndianFormat.AmountAlways(cert.TotalTcsDeposited);
        AmountInWords = IndianAmountInWords.Convert(cert.TotalTcsCollected.Amount);
        IsEmpty = cert.IsEmpty;
        StatusText = cert.IsEmpty
            ? "No collection on this collectee this quarter — an empty certificate."
            : "Certificate ready — export to a de-branded PDF (Ctrl+A).";

        ExportStatus = string.Empty;
        RaiseExportNames();
    }

    private void RaiseExportNames()
    {
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

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
        if (value >= 0 && value < Collectees.Count) SelectedCollectee = Collectees[value];
    }

    /// <summary>
    /// Ctrl+A / the Export button: renders the selected collectee's Form 27D certificate to a deterministic,
    /// de-branded PDF (<see cref="Form27DPdf.Render"/>) and writes it to <see cref="ExportFolder"/> under
    /// <see cref="ExportResolvedFileName"/> through the injectable <paramref name="writeBytes"/> seam.
    /// </summary>
    public bool ExportPdf(Action<string, byte[]>? writeBytes = null)
    {
        if (Certificate is null)
        {
            ExportStatus = "Pick a collectee first — nothing to certify.";
            return false;
        }
        try
        {
            var bytes = Form27DPdf.Render(Certificate, CertificatePages.Build(_company.Name));
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

    private static string BuildResponsibleLine(Form27DCollectorBlock c)
    {
        var name = string.IsNullOrWhiteSpace(c.ResponsiblePersonName) ? "—" : c.ResponsiblePersonName!;
        var sb = new StringBuilder(name);
        if (!string.IsNullOrWhiteSpace(c.ResponsiblePersonDesignation)) sb.Append("  ·  ").Append(c.ResponsiblePersonDesignation);
        if (!string.IsNullOrWhiteSpace(c.ResponsiblePersonPan)) sb.Append("  ·  PAN ").Append(c.ResponsiblePersonPan);
        return sb.ToString();
    }
}
