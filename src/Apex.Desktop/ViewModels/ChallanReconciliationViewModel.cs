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

/// <summary>One section's row in the Challan Reconciliation grid (Phase 7 slice 3).</summary>
public sealed partial class ChallanReconRow : ViewModelBase
{
    public string Section { get; init; } = string.Empty;
    public string Deducted { get; init; } = string.Empty;
    public string Deposited { get; init; } = string.Empty;
    public string Remaining { get; init; } = string.Empty;

    /// <summary>"Matched" (tie), "Short" (deducted &gt; deposited) or "Excess" (over-deposit / orphan).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>True when the section ties exactly (drives the tick/colour in the grid).</summary>
    public bool IsMatched { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Challan Reconciliation (Alt+R)</b> report page (Phase 7 slice 3; catalog §13): matches TDS
/// <b>deposits</b> (the ITNS-281 challans recorded against Stat-Payment vouchers) to TDS <b>deductions</b> (the
/// posted withholdings) per income-tax section over the financial year, and shows — per section — how much was
/// deducted, how much deposited, whether they tie, and the <b>remaining payable</b>. A read-only projection off
/// the pure <see cref="ChallanReconciliation"/> engine (no maths re-implemented here). Gated: only reachable when
/// TDS is enabled, so a non-TDS company is byte-identical (ER-13).
///
/// <para>MVVM boundary: references the engine + domain but no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class ChallanReconciliationViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _to;

    [ObservableProperty] private string _title = "Challan Reconciliation";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _totalDeducted = string.Empty;
    [ObservableProperty] private string _totalDeposited = string.Empty;
    [ObservableProperty] private string _totalRemaining = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isFullyReconciled;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    /// <summary>The per-section reconciliation rows (matched + unmatched), ordered by section.</summary>
    public ObservableCollection<ChallanReconRow> Rows { get; } = new();

    public ChallanReconciliationViewModel(Company company)
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

    /// <summary>(Re)builds the section rows + totals from the current posted set and recorded challans.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        var recon = ChallanReconciliation.Build(_company, _from, _to);

        foreach (var s in recon.Sections)
        {
            Rows.Add(new ChallanReconRow
            {
                Section = s.Section,
                Deducted = IndianFormat.AmountAlways(s.Deducted),
                Deposited = IndianFormat.AmountAlways(s.Deposited),
                Remaining = IndianFormat.AmountAlways(s.Remaining),
                Status = s.IsMatched ? "Matched" : s.IsUnderpaid ? "Short" : "Excess",
                IsMatched = s.IsMatched,
            });
        }

        TotalDeducted = IndianFormat.AmountAlways(recon.TotalDeducted);
        TotalDeposited = IndianFormat.AmountAlways(recon.TotalDeposited);
        TotalRemaining = IndianFormat.AmountAlways(recon.TotalRemaining);
        IsFullyReconciled = recon.IsFullyReconciled;
        Subtitle = $"{_company.Name}  —  FY {_from:dd-MMM-yyyy} to {_to:dd-MMM-yyyy}";
        StatusText = Rows.Count == 0
            ? "No TDS deducted or deposited yet."
            : recon.IsFullyReconciled
                ? "Fully reconciled — every section's deposits match its deductions."
                : $"{recon.Unmatched.Count} section(s) unmatched — remaining payable {TotalRemaining}.";

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
