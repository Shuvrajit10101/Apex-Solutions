using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One GSTR-1 Table-9A (B2BA) ordinary-invoice amendment row (original → revised → differential).</summary>
public sealed class Gstr1Amend9ARowVm
{
    public string Party { get; init; } = string.Empty;
    public string DocNo { get; init; } = string.Empty;
    public string OriginalDate { get; init; } = string.Empty;
    public string OriginalTaxable { get; init; } = string.Empty;
    public string RevisedTaxable { get; init; } = string.Empty;
    public string DifferentialTaxable { get; init; } = string.Empty;
    public string DifferentialTax { get; init; } = string.Empty;
}

/// <summary>One GSTR-1 Table-9C (CDNRA/CDNURA) amended credit/debit-note row (signed revised tax).</summary>
public sealed class Gstr1Amend9CRowVm
{
    public string NoteType { get; init; } = string.Empty;
    public string OriginalInvoice { get; init; } = string.Empty;
    public string OriginalDate { get; init; } = string.Empty;
    public string NoteDate { get; init; } = string.Empty;
    public string RevisedTaxable { get; init; } = string.Empty;
    public string RevisedTax { get; init; } = string.Empty;
}

/// <summary>
/// The <b>GSTR-1 / 3B amendments</b> report page (Reports → Statutory Reports → GST Returns (Advanced) → Amendments;
/// Phase 9 UI-1; RQ-29; DP-33). A read-only projection over the pure <see cref="Gstr1Amendments"/> and
/// <see cref="Gstr3bCorrectionAdvisory"/> engines for a chosen financial year: the GSTR-1 amendment tables — 9A
/// (advisory amended B2B, original→revised→differential) and 9C (amended credit/debit notes, signed revised tax) —
/// plus the 3B-correction advisory (there is no direct 3B amendment; a correction flows via GSTR-1A or the
/// subsequent period, and from Jul-2025 the 3B outward tables are auto-populated + hard-locked). A Composition /
/// GST-off company yields a not-applicable (empty) projection (ER-13). It posts nothing. MVVM boundary: engine only.
/// </summary>
public sealed partial class GstAmendmentsReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "GSTR-1 / 3B Amendments";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _applicable;

    // GSTR-3B correction advisory.
    [ObservableProperty] private string _correctionCountText = "0";
    [ObservableProperty] private string _correctionTaxText = "0.00";
    [ObservableProperty] private string _correctionTaxableText = "0.00";
    [ObservableProperty] private bool _requiresCorrection;
    [ObservableProperty] private string _mechanismText = string.Empty;

    [ObservableProperty] private string _statusText = string.Empty;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the amendments can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The Table-9A (B2BA) ordinary-invoice amendment rows.</summary>
    public ObservableCollection<Gstr1Amend9ARowVm> Table9A { get; } = new();

    /// <summary>The Table-9C (CDNRA/CDNURA) amended credit/debit-note rows.</summary>
    public ObservableCollection<Gstr1Amend9CRowVm> Table9C { get; } = new();

    public GstAmendmentsReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the amendment tables.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built GSTR-1 amendment tables (rebuilt on selection change).</summary>
    public Gstr1Amendments Amendments { get; private set; } = default!;

    /// <summary>The currently-built 3B-correction advisory (rebuilt on selection change).</summary>
    public Gstr3bCorrectionAdvisory Advisory { get; private set; } = default!;

    /// <summary>(Re)builds the amendment tables + 3B advisory for the selected financial year.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Table9A.Clear();
        Table9C.Clear();

        Amendments = Gstr1Amendments.Build(_company, fyFrom, fyTo);
        Advisory = Gstr3bCorrectionAdvisory.Build(_company, fyFrom, fyTo);
        Applicable = Amendments.Applicable;
        MechanismText = Advisory.Mechanism;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  —  amendments of prior periods declared this year";

        if (!Amendments.Applicable)
        {
            CorrectionCountText = "0"; CorrectionTaxText = CorrectionTaxableText = "0.00";
            RequiresCorrection = false;
            StatusText = "Not applicable — a Composition / GST-off company files no GSTR-1 amendments.";
            return;
        }

        foreach (var r in Amendments.Table9A)
            Table9A.Add(new Gstr1Amend9ARowVm
            {
                Party = r.OriginalPartyGstin ?? "—",
                DocNo = r.OriginalDocNumber.ToString(CultureInfo.InvariantCulture),
                OriginalDate = r.OriginalDocDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                OriginalTaxable = A(r.OriginalTaxableValue),
                RevisedTaxable = A(r.RevisedTaxableValue),
                DifferentialTaxable = A(r.DifferentialTaxableValue),
                DifferentialTax = A(r.DifferentialTax),
            });

        foreach (var r in Amendments.Table9C)
            Table9C.Add(new Gstr1Amend9CRowVm
            {
                NoteType = r.NoteType.ToString(),
                OriginalInvoice = r.OriginalInvoiceNumber ?? "—",
                OriginalDate = r.OriginalInvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                NoteDate = r.NoteDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                RevisedTaxable = A(r.RevisedTaxableValue),
                RevisedTax = A(r.RevisedTax),
            });

        CorrectionCountText = Advisory.CorrectionCount.ToString(CultureInfo.InvariantCulture);
        CorrectionTaxText = A(Advisory.PriorPeriodCorrectionTax);
        CorrectionTaxableText = A(Advisory.PriorPeriodCorrectionTaxable);
        RequiresCorrection = Advisory.RequiresCorrection;

        StatusText = $"Table 9A: {Table9A.Count} amended B2B (advisory)  ·  Table 9C: {Table9C.Count} amended CDN  ·  " +
                     $"3B correction: {CorrectionCountText} item(s), net tax ₹{CorrectionTaxText}.";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
