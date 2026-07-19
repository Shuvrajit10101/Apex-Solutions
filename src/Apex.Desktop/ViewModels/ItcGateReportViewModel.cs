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

/// <summary>One CGST/SGST/IGST triple line of the ITC-gate comparison (its label + per-head + total figures).</summary>
public sealed class ItcGateTripleRowVm
{
    public string Label { get; init; } = string.Empty;
    public string Cgst { get; init; } = string.Empty;
    public string Sgst { get; init; } = string.Empty;
    public string Igst { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public bool Emphasise { get; init; }
}

/// <summary>
/// The <b>ITC-gate advisory</b> report page (Reports → Statutory Reports → GST Returns (Advanced) → ITC Gate; Phase 9
/// UI-1; RQ-15/RQ-26; §16(2)(aa) / §17(5)). A read-only projection over the pure <see cref="ItcGateView"/> for a
/// chosen imported 2B snapshot: the ITC figures side-by-side — books eligible, §17(5)-blocked, Table-4(D) ineligible,
/// §16(2)(aa)-claimable, not-in-portal, portal 2B and 3B-claimed — plus the reversal candidates for S7. It
/// <b>posts nothing</b> (ER-14). When no 2B has been imported it shows a clean empty state. Gated: Regular GST
/// company (ER-13). MVVM boundary: engine only; deterministic.
/// </summary>
public sealed partial class ItcGateReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "ITC Gate — §16(2)(aa) / §17(5)";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private string? _message;

    private Gstr2bSnapshotOption? _selectedSnapshot;

    /// <summary>The imported GSTR-2B snapshots available (latest first).</summary>
    public ObservableCollection<Gstr2bSnapshotOption> Snapshots { get; } = new();

    /// <summary>The ITC-gate comparison rows (books-vs-2B-vs-3B + blocked / ineligible).</summary>
    public ObservableCollection<ItcGateTripleRowVm> Rows { get; } = new();

    /// <summary>The reversal candidates surfaced for the S7 poster (advisory).</summary>
    public ObservableCollection<ItcReversalCandidateRowVm> Candidates { get; } = new();

    public ItcGateReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var snap in GstAdvancedSnapshots.Gstr2b(company))
            Snapshots.Add(new Gstr2bSnapshotOption { Snapshot = snap });

        _selectedSnapshot = Snapshots.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected 2B snapshot; changing it rebuilds the ITC gate.</summary>
    public Gstr2bSnapshotOption? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set { if (SetProperty(ref _selectedSnapshot, value)) Rebuild(); }
    }

    /// <summary>The currently-built ITC-gate view, or null when no 2B is imported.</summary>
    public ItcGateView? View { get; private set; }

    /// <summary>(Re)builds the ITC-gate view for the selected 2B snapshot.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        Candidates.Clear();
        Message = null;

        var snap = SelectedSnapshot?.Snapshot;
        HasSnapshot = snap is not null;
        if (snap is null)
        {
            View = null;
            Subtitle = $"{_company.Name}  —  no GSTR-2B imported";
            StatusText = "No GSTR-2B imported yet — import a 2B statement to gate the period's ITC (§16(2)(aa)).";
            Message = "No GSTR-2B imported.";
            return;
        }

        var (from, to) = GstAdvancedSnapshots.Window(snap.ReturnPeriod, _company.FinancialYearStart);
        ItcGateView gate;
        try
        {
            gate = ItcGateView.Build(_company, snap, from, to);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            View = null;
            Message = ex.Message;
            Subtitle = $"{_company.Name}  —  GSTR-2B {snap.ReturnPeriod}";
            StatusText = "Unable to build the ITC gate for this snapshot.";
            return;
        }

        View = gate;
        // Labels are kept short enough to render in FULL in the grid's label column — the statutory citation
        // (§16(2)(aa) / §17(5) / the form table) is what identifies the row and must never ellipsis-truncate.
        Rows.Add(Triple("ITC in books — eligible", gate.BooksEligible, emphasise: true));
        Rows.Add(Triple("  of which §16(2)(aa)-claimable", gate.Claimable));
        Rows.Add(Triple("  of which not in 2B", gate.NotInPortal));
        Rows.Add(Triple("§17(5) blocked (Table 4(B)(1))", gate.BlockedItc));
        Rows.Add(Triple("Ineligible (Table 4(D))", gate.IneligibleItc));
        Rows.Add(Triple("ITC in GSTR-2B (portal)", gate.Portal2b));
        Rows.Add(Triple("ITC claimed in GSTR-3B", gate.Claimed3b, emphasise: true));

        foreach (var c in gate.ReversalCandidates)
            Candidates.Add(new ItcReversalCandidateRowVm
            {
                Reason = ReasonLabel(c.Reason),
                Description = c.Description,
                Cgst = P(c.CgstPaisa),
                Sgst = P(c.SgstPaisa),
                Igst = P(c.IgstPaisa),
                Cess = P(c.CessPaisa),
                Suggested = A(c.SuggestedReversal),
            });

        Subtitle = $"{_company.Name}  —  GSTR-2B {snap.ReturnPeriod} " +
                   $"({ApexDate.Format(from)} to {ApexDate.Format(to)})  —  advisory only, posts nothing";
        StatusText = $"Books eligible ₹{A(gate.BooksEligibleTotal)}  ·  claimable ₹{A(gate.ClaimableTotal)}  ·  " +
                     $"not-in-portal ₹{A(gate.NotInPortalTotal)}  ·  blocked ₹{A(gate.BlockedTotal)}  ·  {Candidates.Count} reversal candidate(s).";
    }

    private static ItcGateTripleRowVm Triple(string label, ItcTriple t, bool emphasise = false) => new()
    {
        Label = label,
        Cgst = A(t.Cgst),
        Sgst = A(t.Sgst),
        Igst = A(t.Igst),
        Total = A(t.Total),
        Emphasise = emphasise,
    };

    private static string ReasonLabel(ItcReversalReason reason) => reason switch
    {
        ItcReversalReason.Section17_5Blocked => "§17(5) blocked",
        ItcReversalReason.Ineligible => "Ineligible (4D)",
        ItcReversalReason.Section16_2aaNotInPortal => "§16(2)(aa) not in 2B",
        ItcReversalReason.ImsAcceptedCreditNote => "Accepted CN/DN",
        _ => reason.ToString(),
    };

    private static string P(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
