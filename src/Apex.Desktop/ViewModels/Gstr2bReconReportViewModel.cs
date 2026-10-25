using System;
using System.Collections.Generic;
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

    /// <summary>The portal − books taxable-value variance, formatted; empty for every bucket but Partial mismatch.</summary>
    public string TaxableVariance { get; init; } = string.Empty;

    /// <summary>The portal − books tax variance (cess folded in), formatted; empty for every bucket but Partial mismatch.</summary>
    public string TaxVariance { get; init; } = string.Empty;

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
public sealed partial class Gstr2bReconReportViewModel : ViewModelBase, IMasterListExportSource
{
    /// <summary>The four bucket captions, in the order the screen lists them. Shared by the grid and the export so
    /// the two cannot name a bucket differently.</summary>
    public const string MatchedCaption = "Matched";
    public const string PartialCaption = "Partial mismatch";
    public const string InPortalOnlyCaption = "In portal only";
    public const string InBooksOnlyCaption = "In books only";

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
            ScopedRegistration = null;
            Subtitle = $"{_company.Name}  —  no GSTR-2B imported";
            StatusText = "No GSTR-2B imported yet — import a 2B statement to reconcile it against the purchase register.";
            Message = "No GSTR-2B imported.";
            HighlightedIndex = -1;
            return;
        }

        var tolerance = _company.Gst?.ReconTolerance ?? ReconTolerance.Exact;
        var (from, to) = GstAdvancedSnapshots.Window(snap.ReturnPeriod, _company.FinancialYearStart);
        // 🔴 v61 (census 6.23) made the reconciler REFUSE an unscoped run on a multi-registration company, because a
        // books register folded over two GSTINs cannot be matched against one registration's 2B — and this screen
        // used to call it unscoped, so on every book with a branch the whole page was that refusal. A GSTR-2B is
        // issued to ONE registered person (CGST Rules, rule 60(7)) and since schema v66 the imported statement
        // carries the registration it was imported for (Gstr2bSnapshot.OwningRegistrationId, never null: the
        // primary when unanchored). So the reconciliation is scoped to the SELECTED STATEMENT'S OWN registration:
        // its books side is exactly the registration whose portal statement it is being matched against. On a
        // single-registration book that id is the primary, which every voucher already attributes to (ER-13).
        // The vendor's own default differs and is recorded as ours in docs/invented-vs-cloned.md (IV-175).
        // A refusal from the engine is still surfaced as the page's message rather than reaching the shell.
        ScopedRegistration = ResolveRegistration(snap.OwningRegistrationId);
        Gstr2bReconciliationReport report;
        try
        {
            report = Gstr2bReconciler.Reconcile(_company, snap, from, to, tolerance, snap.OwningRegistrationId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Report = null;
            Rows.Clear();
            HighlightedIndex = -1;
            Message = ex.Message;
            return;
        }
        Report = report;

        foreach (var m in report.Matched)
            Rows.Add(FromMatch(MatchedCaption, m, clean: true));
        foreach (var m in report.PartialMismatches)
            Rows.Add(FromMatch(PartialCaption, m, clean: false));
        foreach (var line in report.InPortalOnly)
            Rows.Add(FromPortalLine(line));
        foreach (var b in report.InBooksOnly)
            Rows.Add(FromBooksEntry(b));

        MatchedCount = report.MatchedCount;
        PartialCount = report.PartialMismatchCount;
        InPortalOnlyCount = report.InPortalOnlyCount;
        InBooksOnlyCount = report.InBooksOnlyCount;

        Subtitle = $"{_company.Name}{RegistrationSuffix}  —  GSTR-2B {snap.ReturnPeriod} " +
                   $"({ApexDate.Format(from)} to {ApexDate.Format(to)})  —  advisory only";
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
        Date = ApexDate.Format(m.Line.DocDate),
        TaxableValue = R(m.Line.TaxableValuePaisa),
        Tax = R(m.Line.TotalTaxPaisa),
        Note = clean
            ? "Matched to a booked purchase."
            : $"Value variance (portal − books): taxable ₹{R(m.TaxableVariancePaisa)}, tax ₹{R(m.TaxVariancePaisa)}.",
        IsClean = clean,
        TaxableVariance = clean ? string.Empty : R(m.TaxableVariancePaisa),
        TaxVariance = clean ? string.Empty : R(m.TaxVariancePaisa),
    };

    private Gstr2bReconRowVm FromPortalLine(Gstr2bLine line) => new()
    {
        Bucket = InPortalOnlyCaption,
        Supplier = line.SupplierGstin,
        DocNo = line.DocNumber,
        Date = ApexDate.Format(line.DocDate),
        TaxableValue = R(line.TaxableValuePaisa),
        Tax = R(line.TotalTaxPaisa),
        Note = "Supplier filed, not booked — verify / record the purchase.",
        IsClean = false,
    };

    private Gstr2bReconRowVm FromBooksEntry(ReconBooksEntry b) => new()
    {
        Bucket = InBooksOnlyCaption,
        Supplier = b.SupplierGstin,
        DocNo = b.SupplierDocNumber ?? "—",
        Date = ApexDate.Format(b.Date),
        TaxableValue = R(b.TaxableValuePaisa),
        Tax = R(b.TotalTaxPaisa),
        Note = "Booked, supplier not filed — §16(2)(aa) ITC ineligible this period.",
        IsClean = false,
    };

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));

    /// <summary>
    /// The registration the current reconciliation is scoped to — the selected statement's own (see
    /// <see cref="Rebuild"/>) — or null when no statement is selected.
    /// </summary>
    public GstRegistration? ScopedRegistration { get; private set; }

    private GstRegistration? ResolveRegistration(Guid id) =>
        (_company.Gst?.AllRegistrations ?? []).FirstOrDefault(r => r.Id == id);

    /// <summary>Names the registration on the page once the book holds more than one; empty otherwise, so a
    /// single-registration book's subtitle is byte-identical (ER-13).</summary>
    private string RegistrationSuffix =>
        _company.Gst is { IsMultiRegistration: true } && ScopedRegistration is { } r
            ? $"  —  {r.Name}" + (string.IsNullOrWhiteSpace(r.Gstin) ? string.Empty : $" ({r.Gstin})")
            : string.Empty;

    /// <summary>
    /// <b>Census 6.16 — the reconciliation can now be printed and exported.</b> Implementing
    /// <see cref="IMasterListExportSource"/> is what <c>MainWindowViewModel.TopMasterExportSource()</c> reads, so
    /// P / Ctrl+P (print preview), E / Ctrl+E (export current) and Alt+E (the export menu's Current row) all reach
    /// this page — the gap the row was regraded down for: <i>"a reconciliation an operator cannot hand to an
    /// auditor"</i>. The vendor's own route to the same verb is <i>"Press Alt+E (Export) &gt; Current"</i>
    /// (help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/, opened by content 2026-10-25).
    ///
    /// <para><b>EVERY bucket is emitted, empty ones included, each headed by its caption and count.</b> A
    /// reconciliation that silently drops an empty bucket cannot be told apart from one that never computed it;
    /// "In books only (0)" is a statement, a missing heading is not.</para>
    ///
    /// <para><b>The identity column is "GSTIN  doc no", not the bucket.</b> The printed page gives the first column
    /// ~2.4x the width of the others, and the supplier GSTIN is the one field an auditor matches on — so it rides
    /// there, whole, while the bucket is the section heading above its rows. The partial-mismatch variances, which
    /// the grid states only inside its note sentence, get two numeric columns of their own so a spreadsheet can
    /// sum them and the printed page cannot clip them out of a sentence.</para>
    ///
    /// <para><b>The statement's period, recipient and scope lead the document</b>: two months of the same book
    /// export to files that cannot otherwise be told apart, and on a multi-registration book the figures are
    /// one registration's.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        const int width = 6;
        var rows = new List<IReadOnlyList<string>>();
        void Text(string label) => rows.Add(Pad(label));

        var snap = SelectedSnapshot?.Snapshot;
        if (snap is not null)
        {
            Text($"GSTR-2B {snap.ReturnPeriod}");
            Text($"Issued to {snap.RecipientGstin}");
            if (_company.Gst is { IsMultiRegistration: true } && ScopedRegistration is { } reg)
                Text($"Registration: {reg.Name}");
            var (from, to) = GstAdvancedSnapshots.Window(snap.ReturnPeriod, _company.FinancialYearStart);
            Text($"Window {ApexDate.Format(from)} to {ApexDate.Format(to)}");
            Text("Advisory only — posts nothing");
        }

        if (Report is not null)
        {
            foreach (var (caption, count) in new[]
                     {
                         (MatchedCaption, MatchedCount),
                         (PartialCaption, PartialCount),
                         (InPortalOnlyCaption, InPortalOnlyCount),
                         (InBooksOnlyCaption, InBooksOnlyCount),
                     })
            {
                Text($"{caption} ({count})");
                foreach (var r in Rows.Where(r => r.Bucket == caption))
                    rows.Add(new[] { $"{r.Supplier}  {r.DocNo}", r.Date, r.TaxableValue, r.Tax, r.TaxableVariance, r.TaxVariance });
            }
        }

        if (!string.IsNullOrWhiteSpace(StatusText)) Text(StatusText);
        if (!string.IsNullOrWhiteSpace(Message)) Text(Message!);

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("Particulars"),
                MasterListColumn.Text("Date"),
                MasterListColumn.Number("Taxable value"),
                MasterListColumn.Number("Tax"),
                MasterListColumn.Number("Taxable variance"),
                MasterListColumn.Number("Tax variance"),
            },
            rows);

        static string[] Pad(string label)
        {
            var row = new string[width];
            row[0] = label;
            for (var i = 1; i < width; i++) row[i] = string.Empty;
            return row;
        }
    }
}
