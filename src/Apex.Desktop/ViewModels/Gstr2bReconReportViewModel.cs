using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One flattened row of the GSTR-2B reconciliation (its bucket + the portal/books identity + amounts).</summary>
public sealed partial class Gstr2bReconRowVm : ViewModelBase
{
    public string Bucket { get; init; } = string.Empty;
    public string Supplier { get; init; } = string.Empty;
    public string DocNo { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string TaxableValue { get; init; } = string.Empty;
    public string Tax { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;

    /// <summary>True for the two "clean" buckets (Matched), drives the tick colour in the grid.</summary>
    public bool IsClean { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>GSTR-2B reconciliation</b> report page (Reports → Statutory Reports → GST Returns (Advanced) → GSTR-2B
/// Reconciliation; Phase 9 UI-1; RQ-13). A read-only projection over the pure <see cref="Gstr2bReconciler"/> for a
/// chosen imported 2B snapshot: the four advisory buckets — <b>Matched</b>, <b>Partial mismatch</b>, <b>In portal
/// only</b> (supplier filed, not booked) and <b>In books only</b> (booked, supplier not filed ⇒ §16(2)(aa)
/// candidate) — flattened into a highlightable row list. It <b>posts nothing</b> (ER-14). When no 2B has been
/// imported it shows a clean empty state. Gated: Regular GST company (ER-13). MVVM boundary: engine only; deterministic.
/// </summary>
public sealed partial class Gstr2bReconReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "GSTR-2B Reconciliation";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private int _matchedCount;
    [ObservableProperty] private int _partialCount;
    [ObservableProperty] private int _inPortalOnlyCount;
    [ObservableProperty] private int _inBooksOnlyCount;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    private Gstr2bSnapshotOption? _selectedSnapshot;

    /// <summary>The imported GSTR-2B snapshots available to reconcile (latest first).</summary>
    public ObservableCollection<Gstr2bSnapshotOption> Snapshots { get; } = new();

    /// <summary>The four buckets flattened into one highlightable row list (Matched → Partial → Portal-only → Books-only).</summary>
    public ObservableCollection<Gstr2bReconRowVm> Rows { get; } = new();

    public Gstr2bReconReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var snap in GstAdvancedSnapshots.Gstr2b(company))
            Snapshots.Add(new Gstr2bSnapshotOption { Snapshot = snap });

        _selectedSnapshot = Snapshots.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected 2B snapshot; changing it re-reconciles.</summary>
    public Gstr2bSnapshotOption? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set { if (SetProperty(ref _selectedSnapshot, value)) Rebuild(); }
    }

    /// <summary>The currently-built reconciliation report, or null when no 2B is imported.</summary>
    public Gstr2bReconciliationReport? Report { get; private set; }

    /// <summary>(Re)reconciles the selected 2B snapshot against the booked purchase register.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        Message = null;
        MatchedCount = PartialCount = InPortalOnlyCount = InBooksOnlyCount = 0;

        var snap = SelectedSnapshot?.Snapshot;
        HasSnapshot = snap is not null;
        if (snap is null)
        {
            Report = null;
            Subtitle = $"{_company.Name}  —  no GSTR-2B imported";
            StatusText = "No GSTR-2B imported yet — import a 2B statement to reconcile it against the purchase register.";
            Message = "No GSTR-2B imported.";
            HighlightedIndex = -1;
            return;
        }

        var tolerance = _company.Gst?.ReconTolerance ?? ReconTolerance.Exact;
        var (from, to) = GstAdvancedSnapshots.Window(snap.ReturnPeriod, _company.FinancialYearStart);
        var report = Gstr2bReconciler.Reconcile(_company, snap, from, to, tolerance);
        Report = report;

        foreach (var m in report.Matched)
            Rows.Add(FromMatch("Matched", m, clean: true));
        foreach (var m in report.PartialMismatches)
            Rows.Add(FromMatch("Partial mismatch", m, clean: false));
        foreach (var line in report.InPortalOnly)
            Rows.Add(FromPortalLine(line));
        foreach (var b in report.InBooksOnly)
            Rows.Add(FromBooksEntry(b));

        MatchedCount = report.MatchedCount;
        PartialCount = report.PartialMismatchCount;
        InPortalOnlyCount = report.InPortalOnlyCount;
        InBooksOnlyCount = report.InBooksOnlyCount;

        Subtitle = $"{_company.Name}  —  GSTR-2B {snap.ReturnPeriod} " +
                   $"({from.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} to {to.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)})  —  advisory only";
        StatusText = $"Matched {MatchedCount}  ·  partial mismatch {PartialCount}  ·  in portal only {InPortalOnlyCount}  ·  " +
                     $"in books only {InBooksOnlyCount} (§16(2)(aa) reversal candidates).";
        HighlightedIndex = Rows.Count > 0 ? 0 : -1;
        Message = Rows.Count == 0 ? "The 2B snapshot and the books reconcile cleanly — nothing to review." : null;
    }

    /// <summary>Moves the row highlight (Up/Down within the page); wraps. Keeps a live selection.</summary>
    public void MoveHighlight(int direction)
    {
        if (Rows.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Rows.Count) % Rows.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsHighlighted = i == value;
    }

    private Gstr2bReconRowVm FromMatch(string bucket, ReconMatch m, bool clean) => new()
    {
        Bucket = bucket,
        Supplier = m.Line.SupplierGstin,
        DocNo = m.Line.DocNumber,
        Date = m.Line.DocDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        TaxableValue = R(m.Line.TaxableValuePaisa),
        Tax = R(m.Line.TotalTaxPaisa),
        Note = clean
            ? "Matched to a booked purchase."
            : $"Value variance (portal − books): taxable ₹{R(m.TaxableVariancePaisa)}, tax ₹{R(m.TaxVariancePaisa)}.",
        IsClean = clean,
    };

    private Gstr2bReconRowVm FromPortalLine(Gstr2bLine line) => new()
    {
        Bucket = "In portal only",
        Supplier = line.SupplierGstin,
        DocNo = line.DocNumber,
        Date = line.DocDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        TaxableValue = R(line.TaxableValuePaisa),
        Tax = R(line.TotalTaxPaisa),
        Note = "Supplier filed, not booked — verify / record the purchase.",
        IsClean = false,
    };

    private Gstr2bReconRowVm FromBooksEntry(ReconBooksEntry b) => new()
    {
        Bucket = "In books only",
        Supplier = b.SupplierGstin,
        DocNo = b.SupplierDocNumber ?? "—",
        Date = b.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        TaxableValue = R(b.TaxableValuePaisa),
        Tax = R(b.TotalTaxPaisa),
        Note = "Booked, supplier not filed — §16(2)(aa) ITC ineligible this period.",
        IsClean = false,
    };

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
