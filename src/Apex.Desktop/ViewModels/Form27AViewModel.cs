using System;
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

/// <summary>A selectable return the Form 27A control chart can verify: the form code ("26Q" TDS / "27EQ" TCS) and its
/// display label.</summary>
public sealed class Form27AReturnOption
{
    public string FormCode { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One <b>control-total</b> row of the Form 27A chart (a label + its value + whether it is the headline tax
/// figure) — read verbatim off the <see cref="Form27A"/> projection.</summary>
public sealed class Form27AControlRowVm
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsHeadline { get; init; }
}

/// <summary>
/// The <b>Form 27A</b> return-control-chart report page (Reports → GST Reports → TDS/TCS → Form 27A; Phase 7 slice 7;
/// catalog §13). Pick a <b>return</b> (Form 26Q TDS or Form 27EQ TCS) + a financial year + quarter, and it shows the
/// return's control totals — record counts, total amount, total tax, total deposited — plus the FVU-style cross-check
/// (the <b>tally status</b>: AGREE, or the mismatch messages). Every figure comes verbatim off the pure
/// <see cref="Form27A"/> projection, so it tallies with the underlying 26Q/27EQ by construction — this is the pre-FVU
/// control-total tally a filer verifies. <b>Ctrl+A</b> / the Export button renders the deterministic, de-branded chart
/// PDF via <see cref="Form27APdf"/> to the export folder; <b>Alt+B</b> saves it and returns to the menu.
///
/// <para>Gated: the Returns list holds only the enabled taxes' forms (TDS→26Q when <see cref="Company.TdsEnabled"/>,
/// TCS→27EQ when <see cref="Company.TcsEnabled"/>), so a non-TDS/TCS company never reaches it (ER-13).</para>
/// </summary>
public sealed partial class Form27AViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 27A — Return Control Chart";
    [ObservableProperty] private string _subtitle = string.Empty;

    [ObservableProperty] private string _returnFormName = string.Empty;
    [ObservableProperty] private string _tan = string.Empty;
    [ObservableProperty] private string _amountInWords = string.Empty;
    [ObservableProperty] private bool _tallies;
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private Form27AReturnOption? _selectedReturn;
    private CertificateFyOption? _selectedYear;
    private CertificateQuarterOption? _selectedQuarter;

    /// <summary>The returns this chart can verify (only the enabled taxes' forms — ER-13).</summary>
    public ObservableCollection<Form27AReturnOption> Returns { get; } = new();
    public ObservableCollection<CertificateFyOption> FinancialYears { get; } = new();
    public ObservableCollection<CertificateQuarterOption> Quarters { get; } = new();

    /// <summary>The control-total rows shown in the chart.</summary>
    public ObservableCollection<Form27AControlRowVm> ControlTotals { get; } = new();

    /// <summary>The FVU-style cross-check mismatch messages (empty when the return tallies).</summary>
    public ObservableCollection<string> ValidationMessages { get; } = new();

    /// <summary>Builds the control-chart page. <paramref name="initialForm"/> ("26Q"/"27EQ") pre-selects a return when
    /// present + enabled; otherwise the first enabled return is used.</summary>
    public Form27AViewModel(Company company, string? initialForm = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        if (company.TdsEnabled)
            Returns.Add(new Form27AReturnOption { FormCode = "26Q", Label = "Form 26Q (TDS)" });
        if (company.TcsEnabled)
            Returns.Add(new Form27AReturnOption { FormCode = "27EQ", Label = "Form 27EQ (TCS)" });

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new CertificateFyOption { StartYear = y });

        Quarters.Add(new CertificateQuarterOption { Quarter = 1, Label = "Q1 (Apr-Jun)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 2, Label = "Q2 (Jul-Sep)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 3, Label = "Q3 (Oct-Dec)" });
        Quarters.Add(new CertificateQuarterOption { Quarter = 4, Label = "Q4 (Jan-Mar)" });

        _selectedReturn = Returns.FirstOrDefault(r => r.FormCode == initialForm) ?? Returns.FirstOrDefault();
        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedQuarter = Quarters[0];

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    public Form27AReturnOption? SelectedReturn
    {
        get => _selectedReturn;
        set { if (SetProperty(ref _selectedReturn, value)) Rebuild(); }
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

    /// <summary>The control chart currently displayed (rebuilt on selection change). Null only when no return is enabled.</summary>
    public Form27A? Chart { get; private set; }

    public string ExportFileName
    {
        get
        {
            var form = SelectedReturn?.FormCode ?? "26Q";
            var fy = (SelectedYear?.Label ?? "FY").Replace('-', '_');
            var q = SelectedQuarter?.Quarter ?? 0;
            return $"Form27A_{form}_{fy}_Q{q}";
        }
    }

    public string ExportResolvedFileName => ExportFileName + ".pdf";

    /// <summary>(Re)builds the control chart for the selected return + FY + quarter and refreshes every row + status.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var quarter = SelectedQuarter?.Quarter ?? 1;
        var form = SelectedReturn?.FormCode ?? "26Q";

        Form27A? chart = form switch
        {
            "27EQ" when _company.TcsEnabled => Form27A.FromForm27EQ(Form27EQ.Build(_company, fyStart, quarter)),
            "26Q" when _company.TdsEnabled => Form27A.FromForm26Q(Form26Q.Build(_company, fyStart, quarter)),
            _ => null,
        };
        Chart = chart;

        ControlTotals.Clear();
        ValidationMessages.Clear();

        if (chart is null)
        {
            Subtitle = _company.Name;
            ReturnFormName = "—"; Tan = "—"; AmountInWords = string.Empty;
            Tallies = false;
            StatusText = "Enable TDS or TCS to build a return control chart.";
            RaiseExportNames();
            return;
        }

        bool tcs = chart.ReturnFormName == "27EQ";
        string party = tcs ? "collectee" : "deductee";
        string tax = tcs ? "TCS collected" : "TDS deducted";
        string amount = tcs ? "amount received" : "amount paid";

        Subtitle = $"{_company.Name}  —  Form {chart.ReturnFormName}  FY {chart.FinancialYearLabel}  {chart.QuarterLabel}  " +
                   $"({chart.From:dd-MMM-yyyy} to {chart.To:dd-MMM-yyyy})";
        ReturnFormName = "Form " + chart.ReturnFormName;
        Tan = string.IsNullOrEmpty(chart.Tan) ? "—" : chart.Tan;

        ControlTotals.Add(new Form27AControlRowVm { Label = $"Count of {party} records", Value = chart.DeducteeRecordCount.ToString(CultureInfo.InvariantCulture) });
        ControlTotals.Add(new Form27AControlRowVm { Label = "Count of challan records", Value = chart.ChallanRecordCount.ToString(CultureInfo.InvariantCulture) });
        ControlTotals.Add(new Form27AControlRowVm { Label = $"Total {amount}", Value = IndianFormat.AmountAlways(chart.TotalAmount) });
        ControlTotals.Add(new Form27AControlRowVm { Label = $"Total {tax}", Value = IndianFormat.AmountAlways(chart.TotalTax), IsHeadline = true });
        ControlTotals.Add(new Form27AControlRowVm { Label = "Total tax deposited (as per challans)", Value = IndianFormat.AmountAlways(chart.TotalDeposited) });
        ControlTotals.Add(new Form27AControlRowVm { Label = $"Tax deposited against this quarter's {party} records", Value = IndianFormat.AmountAlways(chart.TotalTaxDepositedForQuarter) });

        AmountInWords = IndianAmountInWords.Convert(chart.TotalTax.Amount);
        Tallies = chart.Tallies;

        foreach (var m in chart.ControlValidationMessages)
            ValidationMessages.Add(m);

        StatusText = chart.Tallies
            ? "Control totals AGREE — the return cross-checks and is clear for FVU validation."
            : "Control totals DO NOT agree — resolve the items below before FVU validation.";

        ExportStatus = string.Empty;
        RaiseExportNames();
    }

    private void RaiseExportNames()
    {
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>
    /// Ctrl+A / the Export button: renders the Form 27A control chart to a deterministic, de-branded PDF
    /// (<see cref="Form27APdf.Render"/>) and writes it to <see cref="ExportFolder"/> under
    /// <see cref="ExportResolvedFileName"/> through the injectable <paramref name="writeBytes"/> seam.
    /// </summary>
    public bool ExportPdf(Action<string, byte[]>? writeBytes = null)
    {
        if (Chart is null)
        {
            ExportStatus = "No return selected — nothing to chart.";
            return false;
        }
        try
        {
            var bytes = Form27APdf.Render(Chart, CertificatePages.Build(_company.Name));
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
            ExportStatus = "Could not export the control chart PDF: " + ex.Message;
            return false;
        }
    }
}
