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

/// <summary>One IFF (Invoice Furnishing Facility) month window row of a QRMP quarter (M1 or M2).</summary>
public sealed class QrmpIffRowVm
{
    public string Quarter { get; init; } = string.Empty;
    public string Month { get; init; } = string.Empty;
    public string Invoices { get; init; } = string.Empty;
    public string TaxableValue { get; init; } = string.Empty;
    public string Cap { get; init; } = string.Empty;
    public bool ExceedsCap { get; init; }

    /// <summary>True when the month's IFF value is within the ₹50 lakh cap — the positive-sense flag the grid's
    /// balanced/alert brush binds to (the converter maps true → ink, false → alert red, and ignores any parameter).</summary>
    public bool WithinCap => !ExceedsCap;
}

/// <summary>One M1/M2 PMT-06 monthly-tax suggestion row of a QRMP quarter (three bases + already deposited).</summary>
public sealed class QrmpPmt06RowVm
{
    public string Quarter { get; init; } = string.Empty;
    public string Month { get; init; } = string.Empty;
    public string FixedSum35 { get; init; } = string.Empty;
    public string FixedSum100 { get; init; } = string.Empty;
    public string SelfAssessment { get; init; } = string.Empty;
    public string AlreadyDeposited { get; init; } = string.Empty;
}

/// <summary>
/// The <b>QRMP / IFF</b> cadence report page (Reports → Statutory Reports → GST Returns (Advanced) → QRMP / IFF;
/// Phase 9 UI-1; RQ-17; DP-19). A read-only projection over the pure <see cref="GstQrmp"/> engine for a chosen
/// financial year: the FY's four quarters, and for each the M1/M2 <b>IFF</b> B2B windows (invoice count + taxable
/// value + ₹50 lakh cap flag) and the M1/M2 <b>PMT-06</b> monthly-tax suggestions (both fixed-sum bases — 35% of the
/// preceding quarter's cash and 100% of its last month's cash — plus self-assessment, and the cash already
/// deposited). A Monthly filer / Composition / GST-off company yields a not-applicable (empty) projection (ER-13). It
/// posts nothing. MVVM boundary: engine only; deterministic.
/// </summary>
public sealed partial class QrmpReportViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "QRMP / IFF Cadence";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _applicable;
    [ObservableProperty] private string _statusText = string.Empty;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the cadence can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The eight IFF month windows (M1/M2 across the four quarters).</summary>
    public ObservableCollection<QrmpIffRowVm> IffRows { get; } = new();

    /// <summary>The eight PMT-06 monthly-tax suggestions (M1/M2 across the four quarters).</summary>
    public ObservableCollection<QrmpPmt06RowVm> Pmt06Rows { get; } = new();

    public QrmpReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the cadence.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built QRMP projection (rebuilt on selection change).</summary>
    public GstQrmp Projection { get; private set; } = default!;

    /// <summary>(Re)builds the QRMP/IFF cadence for the selected financial year.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        IffRows.Clear();
        Pmt06Rows.Clear();

        // v61 (census 6.23): GstQrmp folds through GSTR-1 / GSTR-3B, which REFUSE an unscoped build on a
        // multi-registration company — QRMP is a per-registration election and its IFF/PMT-06 figures cannot be
        // derived from a return combining two GSTINs. Surfaced as this page's status rather than as a crash.
        try
        {
            Projection = GstQrmp.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Applicable = false;
            StatusText = ex.Message;
            return;
        }
        Applicable = Projection.Applicable;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}";

        if (!Projection.Applicable)
        {
            StatusText = "Not applicable — QRMP applies to a Regular filer who elected Quarterly (Return) / Monthly (Payment).";
            return;
        }

        foreach (var q in Projection.Quarters)
        {
            var qLabel = $"Q{q.Index} · {q.From.ToString("MMM", CultureInfo.InvariantCulture)}–{q.To.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
            IffRows.Add(Iff(qLabel, q.Month1Iff));
            IffRows.Add(Iff(qLabel, q.Month2Iff));
            Pmt06Rows.Add(Pmt(qLabel, q.Month1Pmt06));
            Pmt06Rows.Add(Pmt(qLabel, q.Month2Pmt06));
        }

        StatusText = $"QRMP filer — {Projection.Quarters.Count} quarters. IFF furnishes M1/M2 B2B early (cap ₹50 lakh/month); " +
                     "PMT-06 pays M1/M2 tax in cash (pick a fixed-sum base or self-assessment).";
    }

    private static QrmpIffRowVm Iff(string quarter, GstIffWindow w) => new()
    {
        Quarter = quarter,
        Month = w.From.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        Invoices = w.InvoiceCount.ToString(CultureInfo.InvariantCulture),
        TaxableValue = A(w.TaxableValue),
        Cap = w.ExceedsCap ? "Over ₹50 L" : "Within cap",
        ExceedsCap = w.ExceedsCap,
    };

    private static QrmpPmt06RowVm Pmt(string quarter, GstPmt06Suggestion s) => new()
    {
        Quarter = quarter,
        Month = s.MonthFrom.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        FixedSum35 = A(s.FixedSum35PercentPriorQuarter),
        FixedSum100 = A(s.FixedSum100PercentLastMonth),
        SelfAssessment = A(s.SelfAssessment),
        AlreadyDeposited = A(s.AlreadyDeposited),
    };

    /// <summary>
    /// <b>Census 6.19 — the snapshot that gives this screen an exit.</b> Named in 6.19's "QRMP/IFF" clause as
    /// one of the six screens that never adopted <see cref="IMasterListExportSource"/>. The general arm
    /// (<c>TopMasterExportSource()</c>) already gated both E / Alt+E and P / Ctrl+P on this interface; this
    /// method is the adoption.
    ///
    /// <para>🔴 <b>This export does NOT become an IFF upload, and 6.19 says so in as many words:</b> "QRMP is a
    /// PMT-06 advisory only; its IFF rows are a window view, not an upload artefact." Nothing here is a filable
    /// JSON — this is the cadence an operator reads and hands on. Exporting it closes the output dead end the
    /// row records; it does not close the upload gap, which stays open and is reported as such.</para>
    ///
    /// <para><b>The cap column travels with the IFF rows on purpose.</b> An IFF taxable value means nothing
    /// without the ₹50-lakh cap it is measured against — <see cref="QrmpIffRowVm.ExceedsCap"/> is the reason
    /// the screen exists — so the cap and an explicit over-cap marker ride beside the value rather than being
    /// left to a reader who no longer has the screen in front of them.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>(IffRows.Count + Pmt06Rows.Count + 4);

        static IReadOnlyList<string> Section(string label) => new[]
        {
            label, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        };

        rows.Add(Section("IFF (Invoice Furnishing Facility) — window view, not an upload artefact"));
        foreach (var r in IffRows)
            rows.Add(new[]
            {
                r.Quarter, r.Month, r.Invoices, r.TaxableValue, r.Cap,
                r.ExceedsCap ? "Exceeds cap" : string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty,
            });

        rows.Add(Section("PMT-06 (advisory)"));
        foreach (var r in Pmt06Rows)
            rows.Add(new[]
            {
                r.Quarter, r.Month, string.Empty, string.Empty, string.Empty, string.Empty,
                r.FixedSum35, r.FixedSum100, r.SelfAssessment, r.AlreadyDeposited,
            });

        if (!string.IsNullOrWhiteSpace(StatusText))
            rows.Add(Section(StatusText));

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("Quarter"),
                MasterListColumn.Text("Month"),
                MasterListColumn.Number("Invoices"),
                MasterListColumn.Number("Taxable Value"),
                MasterListColumn.Number("Cap"),
                MasterListColumn.Text("Cap Status"),
                MasterListColumn.Number("Fixed Sum 35%"),
                MasterListColumn.Number("Fixed Sum 100%"),
                MasterListColumn.Number("Self-Assessment"),
                MasterListColumn.Number("Already Deposited"),
            },
            rows);
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
