using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One §206C collection code's row in the TCS Challan Reconciliation grid (Phase 7 slice 6).</summary>
public sealed partial class TcsChallanReconRow : ViewModelBase
{
    public string CollectionCode { get; init; } = string.Empty;
    public string Collected { get; init; } = string.Empty;
    public string Deposited { get; init; } = string.Empty;
    public string Remaining { get; init; } = string.Empty;

    /// <summary>"Matched" (tie), "Short" (collected &gt; deposited) or "Excess" (over-deposit / orphan).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>True when the code ties exactly (drives the tick/colour in the grid).</summary>
    public bool IsMatched { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>TCS Challan Reconciliation</b> report page (Phase 7 slice 6; catalog §13): matches TCS <b>deposits</b> (the
/// ITNS-281 challans recorded against Stat-Payment vouchers) to TCS <b>collections</b> (the posted §206C collections)
/// per collection code over the financial year, and shows — per code — how much was collected, how much deposited,
/// whether they tie, and the <b>remaining payable</b>. The exact mirror of the TDS
/// <see cref="ChallanReconciliationViewModel"/>: a read-only projection off the pure
/// <see cref="TcsChallanReconciliation"/> engine (no maths re-implemented here). Gated: only reachable when TCS is
/// enabled, so a non-TCS company is byte-identical (ER-13).
///
/// <para>MVVM boundary: references the engine + domain but no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class TcsChallanReconciliationViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _to;

    [ObservableProperty] private string _title = "TCS Challan Reconciliation";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _totalCollected = string.Empty;
    [ObservableProperty] private string _totalDeposited = string.Empty;
    [ObservableProperty] private string _totalRemaining = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isFullyReconciled;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    /// <summary>The per-code reconciliation rows (matched + unmatched), ordered by collection code.</summary>
    public ObservableCollection<TcsChallanReconRow> Rows { get; } = new();

    /// <summary>
    /// Clarifying footnote: this report windows <b>deposits by challan deposit date</b> (a cash basis). A
    /// collection deposited in a later period (collected in March, deposited in April) is fully compliant yet can
    /// read here as outstanding for the earlier window. The period-attributed (compliance) position lives in
    /// Form 27EQ and the TCS Outstandings report. Constant text, surfaced as a band under the totals.
    /// </summary>
    public string BasisNote =>
        "Deposits are shown on a cash basis, by challan deposit date. A collection deposited in a later " +
        "period (e.g. collected in March, deposited in April) may appear here as outstanding for the earlier " +
        "window even though it is compliant — see Form 27EQ and the TCS Outstandings report for the " +
        "period-attributed position.";

    public TcsChallanReconciliationViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _from = company.FinancialYearStart;
        _to = company.FinancialYearStart.AddYears(1).AddDays(-1);
        Rebuild();
    }

    /// <summary>The reconciliation window (the financial year).</summary>
    public DateOnly From => _from;

    /// <summary>The reconciliation window end (the financial year).</summary>
    public DateOnly To => _to;

    /// <summary>(Re)builds the code rows + totals from the current posted set and recorded challans.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        var recon = TcsChallanReconciliation.Build(_company, _from, _to);

        foreach (var s in recon.Codes)
        {
            Rows.Add(new TcsChallanReconRow
            {
                CollectionCode = s.CollectionCode,
                Collected = IndianFormat.AmountAlways(s.Collected),
                Deposited = IndianFormat.AmountAlways(s.Deposited),
                Remaining = IndianFormat.AmountAlways(s.Remaining),
                Status = s.IsMatched ? "Matched" : s.IsUnderpaid ? "Short" : "Excess",
                IsMatched = s.IsMatched,
            });
        }

        TotalCollected = IndianFormat.AmountAlways(recon.TotalCollected);
        TotalDeposited = IndianFormat.AmountAlways(recon.TotalDeposited);
        TotalRemaining = IndianFormat.AmountAlways(recon.TotalRemaining);
        IsFullyReconciled = recon.IsFullyReconciled;
        Subtitle = $"{_company.Name}  —  FY {_from:dd-MMM-yyyy} to {_to:dd-MMM-yyyy}";
        StatusText = Rows.Count == 0
            ? "No TCS collected or deposited yet."
            : recon.IsFullyReconciled
                ? "Fully reconciled — every code's deposits match its collections."
                : $"{recon.Unmatched.Count} code(s) unmatched — remaining payable {TotalRemaining}.";

        HighlightedIndex = Rows.Count > 0 ? 0 : -1;
        Message = Rows.Count == 0 ? StatusText : null;
    }

    /// <summary>Moves the row highlight (Up/Down within the page); wraps. Keeps a live ListBox selection.</summary>
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
}
