using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
// NOT dead, despite CompanyStorage having gone with the posting path: IndianFormat (used by Rebuild) lives here.
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

/// <summary>One party's validated Against-Reference allocations for a pre-loaded settlement voucher.</summary>
/// <param name="Party">The bill-by-bill party whose bills are being settled.</param>
/// <param name="Allocations">Agst-Ref allocations, already checked against genuinely open bills and capped at
/// each bill's pending amount by <see cref="BillSettlementService.BuildSettlementAllocations"/>.</param>
public readonly record struct SettlementPartyPreload(
    DomainLedger Party, IReadOnlyList<BillAllocation> Allocations);

/// <summary>
/// Everything the shell needs to open a settlement voucher from an Outstandings selection: which voucher type
/// to open, the report as-of the bills were validated against, and the per-party allocations to stamp.
/// <b>Describes a voucher to be offered — it posts nothing.</b>
/// </summary>
public sealed record SettlementPreload(
    VoucherType Type, DateOnly AsOf, IReadOnlyList<SettlementPartyPreload> Parties);

/// <summary>
/// The Outstandings page (catalog §5, Statements of Accounts → Outstandings → Receivables / Payables): the
/// open bills for every bill-by-bill party of one side, with each bill's due date, pending amount and ageing.
/// The spacebar multi-selects bills; <b>Alt+A</b> then offers a settlement voucher for them
/// (<see cref="BuildSettlementPreload"/>).
///
/// <para><b>This page used to POST (Phase 10.11 S2 / VL-4 / register row IV-5).</b> A <c>SettleSelected</c>
/// bound to <b>Ctrl+B</b> knocked every selected bill off for its <i>full</i> pending amount through a ledger
/// literally named "Cash", dated at <see cref="AsOf"/>, with no preview, no confirmation and no undo — and it
/// refused outright when no ledger was named "Cash". In TallyPrime <b>Ctrl+B is "Basis of Values"</b>, a report
/// option that changes how figures are DISPLAYED and writes nothing [TallyHelp keyboard-shortcuts]; TallyPrime's
/// Bills Outstanding has no settlement action at all [CORPUS-SG p.92 §5.5]. This class now only <i>describes</i>
/// a settlement — the operator confirms the date, the cash/bank ledger and every per-bill amount on a real
/// voucher screen, and a part payment (which the old path could not express) is just a typed figure.</para>
///
/// <para>MVVM boundary: references the engine but no Avalonia types ⇒ headlessly testable. It no longer holds
/// storage or a change callback, because it no longer writes anything.</para>
/// </summary>
public sealed partial class OutstandingsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _asOf;

    [ObservableProperty] private OutstandingsKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _totalText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;

    /// <summary>The open-bill rows for this side (Receivables or Payables), as of the report date.</summary>
    public ObservableCollection<OutstandingRowViewModel> Rows { get; } = new();

    public OutstandingsViewModel(Company company, OutstandingsKind kind)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
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
    /// Alt+A — describes the settlement voucher the spacebar-selected bills imply, for the shell to OPEN and the
    /// operator to confirm. Returns null (with <see cref="Message"/> set) when there is nothing to offer.
    ///
    /// <para><b>It writes nothing.</b> The cash/bank ledger is deliberately absent from the result: choosing it
    /// is the operator's, which is the whole point of the fix — the deleted Ctrl+B path hard-coded a ledger named
    /// "Cash". The date is absent so the entry screen's ordinary default stands and a settlement is dated exactly
    /// like any hand-keyed Receipt.</para>
    ///
    /// <para><b>Do not read that as date PROTECTION — it is not, and an earlier draft of this comment claimed it
    /// was.</b> <see cref="AsOf"/> is the maximum voucher date in the company (<see cref="ComputeAsOf"/>) and the
    /// entry screen's own default is <i>the same expression</i> (<c>VoucherEntryViewModel</c>'s constructor:
    /// <c>Date = date ?? company.Vouchers.Max(v =&gt; v.Date) ?? BooksBeginFrom</c>). An open bill implies at least
    /// one voucher, so the two are equal on every reachable path: a book whose newest voucher is a 31-Mar year-end
    /// journal DOES open the settlement dated 31-Mar. What actually differs from the deleted Ctrl+B path is that
    /// the date is now on screen in an editable field the operator confirms, instead of being stamped invisibly on
    /// a voucher that was already posted. <c>The_preloaded_date_is_the_ordinary_entry_default_not_a_shielded_one</c>
    /// pins that equality so this comment cannot drift back.</para>
    ///
    /// <para>Amounts are seeded at each bill's FULL pending, because that is the common case and it is the figure
    /// TallyPrime itself captures ("Amount will be captured automatically as per the amount entered earlier",
    /// CORPUS-SG p.92 §5.5) — but they are now ordinary editable fields, so a PART payment is expressible, which
    /// it was not through Ctrl+B.</para>
    /// </summary>
    public SettlementPreload? BuildSettlementPreload()
    {
        Message = null;
        var selected = SelectedRows;
        if (selected.Count == 0)
        {
            Message = "Select one or more bills with the spacebar, then press Alt+A to settle them.";
            return null;
        }

        // Receipt for receivables (money in), Payment for payables (money out).
        var wantType = Kind == OutstandingsKind.Receivables ? VoucherBaseType.Receipt : VoucherBaseType.Payment;
        // Resolve through the SAME rule as every navigation route (VoucherTypeResolver): only an ACTIVE,
        // non-specialised type is ever used, and the seeded predefined series wins. A settlement is an ordinary
        // Receipt/Payment the operator could have keyed by hand, so if they could not have OPENED it (F6 refuses
        // on a deactivated series) we must not open one for them either. This rule predates the Ctrl+B removal and
        // is preserved verbatim, message included — it is what VoucherTypeNavigationIdentityTests locks.
        var vType = VoucherTypeResolver.ResolveForEntry(_company, wantType);
        if (vType is null)
        {
            Message = VoucherTypeResolver.NoActiveTypeMessage(_company, wantType);
            return null;
        }

        // Build through BillSettlementService, never by reading Bill.Pending straight into an allocation: it is
        // the only code that validates a reference against a genuinely open bill and caps the knock at its
        // pending amount, so calling it here proves the selection is still settleable at the moment we offer it.
        var service = new BillSettlementService(_company);
        var parties = new List<SettlementPartyPreload>();
        try
        {
            // One Particulars line per party — several parties settle on ONE voucher against one cash/bank side.
            foreach (var group in selected.GroupBy(r => r.Bill.LedgerId))
            {
                var party = _company.FindLedger(group.Key);
                if (party is null) continue;

                var knocks = group.Select(r => new BillSettlementService.Knock(r.Bill.Reference, r.Bill.Pending));
                parties.Add(new SettlementPartyPreload(
                    party, service.BuildSettlementAllocations(party, _asOf, knocks)));
            }
        }
        catch (InvalidOperationException ex)
        {
            Message = $"Cannot settle the selected bills: {ex.Message}";
            return null;
        }

        if (parties.Count == 0)
        {
            Message = "The selected bills have no party ledger to settle against.";
            return null;
        }

        return new SettlementPreload(vType, _asOf, parties);
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
