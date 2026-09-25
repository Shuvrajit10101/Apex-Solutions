using System;
using System.Collections.Generic;
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
public sealed partial class GstAmendmentsReportViewModel : ViewModelBase, IMasterListExportSource
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

        // v61 (census 6.23): both projections fold through GSTR-1 / GSTR-3B, which REFUSE an unscoped build on a
        // multi-registration company — amendments of one registration's prior periods cannot be derived from a
        // return combining two GSTINs. This screen does not yet carry a registration selector, so the refusal is
        // surfaced as its own status rather than reaching the shell as an unhandled exception.
        try
        {
            Amendments = Gstr1Amendments.Build(_company, fyFrom, fyTo);
            Advisory = Gstr3bCorrectionAdvisory.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Applicable = false;
            CorrectionCountText = "0"; CorrectionTaxText = CorrectionTaxableText = "0.00";
            StatusText = ex.Message;
            return;
        }
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
                DocNo = r.OriginalDocNumber,
                OriginalDate = ApexDate.Format(r.OriginalDocDate),
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
                OriginalDate = ApexDate.Format(r.OriginalInvoiceDate),
                NoteDate = ApexDate.Format(r.NoteDate),
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

    /// <summary>
    /// <b>Census 6.19 — the snapshot that gives this screen an exit.</b> Named in 6.19's "GST Amendments"
    /// clause among the six screens that never adopted <see cref="IMasterListExportSource"/>, which is what
    /// <c>TopMasterExportSource()</c> gates both E / Alt+E and P / Ctrl+P on. This method is the adoption.
    ///
    /// <para><b>Table 9A and Table 9C share one column set under a section label.</b> They are different
    /// amendment mechanisms — 9A revises an already-filed invoice, 9C is the credit/debit-note route — and an
    /// amendment working paper is read for the DIFFERENTIAL, so both the original and the revised figure have
    /// to survive the export. A 9C row has no differential taxable of its own (the note IS the correction), so
    /// that cell is left empty rather than filled with a zero a reader would total.</para>
    ///
    /// <para><b>The 3B correction footing rides too.</b> <see cref="RequiresCorrection"/> and
    /// <see cref="MechanismText"/> are the screen's conclusion — whether a correction is owed and by which
    /// mechanism — and a sheet of amendment rows without it does not say what must actually be done.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>(Table9A.Count + Table9C.Count + 6);

        static IReadOnlyList<string> Section(string label) => new[]
        {
            label, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        };

        rows.Add(Section("Table 9A — amended invoices"));
        foreach (var r in Table9A)
            rows.Add(new[]
            {
                "9A", r.Party, r.DocNo, r.OriginalDate, string.Empty,
                r.OriginalTaxable, r.RevisedTaxable, r.DifferentialTaxable, r.DifferentialTax,
            });

        rows.Add(Section("Table 9C — credit / debit notes"));
        foreach (var r in Table9C)
            rows.Add(new[]
            {
                "9C", r.NoteType, r.OriginalInvoice, r.OriginalDate, r.NoteDate,
                string.Empty, r.RevisedTaxable, string.Empty, r.RevisedTax,
            });

        rows.Add(Section("GSTR-3B correction"));
        rows.Add(new[]
        {
            "3B", "Correction items", CorrectionCountText, string.Empty, string.Empty,
            string.Empty, CorrectionTaxableText, string.Empty, CorrectionTaxText,
        });
        rows.Add(Section(RequiresCorrection
            ? $"A 3B correction IS required. {MechanismText}"
            : $"No 3B correction required. {MechanismText}"));

        if (!string.IsNullOrWhiteSpace(StatusText))
            rows.Add(Section(StatusText));

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("Table"),
                MasterListColumn.Text("Party / Note Type"),
                MasterListColumn.Text("Document"),
                MasterListColumn.Text("Original Date"),
                MasterListColumn.Text("Note Date"),
                MasterListColumn.Number("Original Taxable"),
                MasterListColumn.Number("Revised Taxable"),
                MasterListColumn.Number("Differential Taxable"),
                MasterListColumn.Number("Tax"),
            },
            rows);
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
