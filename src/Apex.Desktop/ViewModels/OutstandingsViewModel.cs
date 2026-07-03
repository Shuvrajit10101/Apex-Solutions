using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>Which side of Outstandings a page shows: money owed to us vs money we owe.</summary>
public enum OutstandingsKind
{
    Receivables,
    Payables,
}

/// <summary>
/// The Outstandings page (catalog §5, Statements of Accounts → Outstandings → Receivables / Payables): the
/// open bills for every bill-by-bill party of one side, with each bill's due date, pending amount and
/// ageing. It supports <b>Bill Settlement (Ctrl+B)</b>: the spacebar multi-selects bills and Ctrl+B
/// knocks them off through the engine (<see cref="BillSettlementService"/>) against a cash contra, then the
/// report refreshes so settled bills disappear.
///
/// <para>MVVM boundary: references the engine + persistence but no Avalonia types ⇒ headlessly testable.
/// Settlement posts a Receipt (for receivables) or a Payment (for payables) that credits/debits the party
/// bill-by-bill and contras Cash, exactly the voucher a user would enter by hand.</para>
/// </summary>
public sealed partial class OutstandingsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly DateOnly _asOf;
    private readonly Action _onChanged;

    [ObservableProperty] private OutstandingsKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _totalText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    /// <summary>The open-bill rows for this side (Receivables or Payables), as of the report date.</summary>
    public ObservableCollection<OutstandingRowViewModel> Rows { get; } = new();

    public OutstandingsViewModel(
        Company company, CompanyStorage storage, OutstandingsKind kind, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _kind = kind;
        _asOf = ComputeAsOf(company);
        Rebuild();
    }

    /// <summary>The report's as-of date (last voucher date, else the financial-year end).</summary>
    public DateOnly AsOf => _asOf;

    /// <summary>(Re)builds the rows from the current posted set and refreshes the total.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        var report = Outstandings.Build(_company, _asOf);
        var bills = Kind == OutstandingsKind.Receivables ? report.Receivables : report.Payables;
        var total = Kind == OutstandingsKind.Receivables ? report.TotalReceivable : report.TotalPayable;

        Title = Kind == OutstandingsKind.Receivables ? "Bills Receivable" : "Bills Payable";
        Subtitle = $"{_company.Name}  —  as at {_asOf:dd-MMM-yyyy}";

        foreach (var b in bills)
            Rows.Add(new OutstandingRowViewModel(b).WithAgeing(_asOf));

        TotalText = IndianFormat.Amount(total);
        HighlightedIndex = Rows.Count > 0 ? 0 : -1;
        Message = Rows.Count == 0
            ? (Kind == OutstandingsKind.Receivables ? "No pending receivables." : "No pending payables.")
            : null;
    }

    /// <summary>Moves the row highlight (Up/Down within the page); wraps.</summary>
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

    /// <summary>Spacebar: toggles the multi-select flag on the highlighted bill.</summary>
    public void ToggleSelectHighlighted()
    {
        if (HighlightedIndex < 0 || HighlightedIndex >= Rows.Count) return;
        var row = Rows[HighlightedIndex];
        row.IsSelected = !row.IsSelected;
    }

    /// <summary>The bills currently spacebar-selected.</summary>
    public IReadOnlyList<OutstandingRowViewModel> SelectedRows =>
        Rows.Where(r => r.IsSelected).ToList();

    /// <summary>
    /// Ctrl+B — Bill Settlement. Knocks off every spacebar-selected bill for its full pending amount via
    /// a single settlement voucher per party (party knock-off vs a Cash contra), posted through the engine.
    /// On success the report is rebuilt (settled bills disappear) and the company is persisted. No-op with a
    /// clear message when nothing is selected. Returns true iff at least one bill was settled.
    /// </summary>
    public bool SettleSelected()
    {
        Message = null;
        var selected = SelectedRows;
        if (selected.Count == 0)
        {
            Message = "Select one or more bills with the spacebar, then press Ctrl+B to settle.";
            return false;
        }

        var cash = _company.FindLedgerByName("Cash");
        if (cash is null)
        {
            Message = "No 'Cash' ledger to settle through.";
            return false;
        }

        // The settlement side uses Receipt for receivables (money in) and Payment for payables (money out).
        var wantType = Kind == OutstandingsKind.Receivables ? VoucherBaseType.Receipt : VoucherBaseType.Payment;
        var vType = _company.VoucherTypes.FirstOrDefault(t => t.BaseType == wantType && t.IsActive)
                    ?? _company.VoucherTypes.FirstOrDefault(t => t.BaseType == wantType);
        if (vType is null)
        {
            Message = $"No '{wantType}' voucher type is configured for this company.";
            return false;
        }

        var service = new BillSettlementService(_company);
        var settledCount = 0;

        try
        {
            // Group the selected bills by party — one settlement voucher per party.
            foreach (var group in selected.GroupBy(r => r.Bill.LedgerId))
            {
                var party = _company.FindLedger(group.Key);
                if (party is null) continue;

                var knocks = group
                    .Select(r => new BillSettlementService.Knock(r.Bill.Reference, r.Bill.Pending))
                    .ToList();

                service.SettleAndPost(
                    party, cash, vType.Id, _asOf, knocks,
                    narration: $"Bill settlement ({Kind}).");
                settledCount += knocks.Count;
            }
        }
        catch (Exception ex)
        {
            Message = $"Settlement failed: {ex.Message}";
            return false;
        }

        _storage.Save(_company);
        Rebuild();
        _onChanged();
        Message = $"Settled {settledCount} bill{(settledCount == 1 ? string.Empty : "s")}.";
        return true;
    }

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }
}
