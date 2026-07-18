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

/// <summary>One ITC-reversal candidate row surfaced by the ITC-gate for the S7 poster (advisory, read-only).</summary>
public sealed class ItcReversalCandidateRowVm
{
    public string Reason { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Cgst { get; init; } = string.Empty;
    public string Sgst { get; init; } = string.Empty;
    public string Igst { get; init; } = string.Empty;
    public string Cess { get; init; } = string.Empty;
    public string Suggested { get; init; } = string.Empty;
}

/// <summary>
/// The <b>ITC reversal</b> report page — <b>display only</b> (Reports → Statutory Reports → GST Returns (Advanced) →
/// ITC Reversal; Phase 9 UI-1; RQ-27). Surfaces the tracked per-head <b>ECRS reversal balance</b>
/// (<see cref="GstReversalService.OutstandingReversalBalance"/> — reclaimable Rule 37/37A reversals net of reclaims)
/// and the reversal <b>candidates</b> the <see cref="ItcGateView"/> surfaces from the company's latest imported
/// GSTR-2B snapshot (§17(5)-blocked / ineligible / §16(2)(aa) / accepted-CN). It <b>posts nothing</b> — the sole
/// reversal poster is the S7b engine, not this view. When no 2B has been imported the candidate list is empty with a
/// clean note. Gated: Regular GST company (ER-13). MVVM boundary: engine only; deterministic.
/// </summary>
public sealed partial class ItcReversalReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "ITC Reversal — outstanding balance & candidates";
    [ObservableProperty] private string _subtitle = string.Empty;

    // ECRS outstanding reversal balance per head.
    [ObservableProperty] private string _balanceCgstText = "0.00";
    [ObservableProperty] private string _balanceSgstText = "0.00";
    [ObservableProperty] private string _balanceIgstText = "0.00";
    [ObservableProperty] private string _balanceCessText = "0.00";
    [ObservableProperty] private string _balanceTotalText = "0.00";

    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private string _candidatesHeader = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>The advisory reversal candidates (from the latest 2B snapshot's ITC-gate); empty when no 2B imported.</summary>
    public ObservableCollection<ItcReversalCandidateRowVm> Candidates { get; } = new();

    public ItcReversalReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        Rebuild();
    }

    /// <summary>(Re)builds the ECRS balance + the latest snapshot's reversal candidates.</summary>
    public void Rebuild()
    {
        Candidates.Clear();
        Message = null;

        var balance = new GstReversalService(_company).OutstandingReversalBalance();
        BalanceCgstText = P(balance.CgstPaisa); BalanceSgstText = P(balance.SgstPaisa);
        BalanceIgstText = P(balance.IgstPaisa); BalanceCessText = P(balance.CessPaisa);
        BalanceTotalText = P(balance.TotalPaisa);

        var snapshot = GstAdvancedSnapshots.Gstr2b(_company).FirstOrDefault();
        HasSnapshot = snapshot is not null;

        if (snapshot is null)
        {
            Subtitle = $"{_company.Name}  —  no GSTR-2B imported";
            CandidatesHeader = "Reversal candidates";
            StatusText = $"Outstanding reclaimable reversal balance (ECRS) ₹{BalanceTotalText}. " +
                         "Import a GSTR-2B to surface this period's §17(5)-blocked / ineligible / §16(2)(aa) / credit-note candidates.";
            Message = "No GSTR-2B imported — no reversal candidates to display.";
            return;
        }

        var (from, to) = GstAdvancedSnapshots.Window(snapshot.ReturnPeriod, _company.FinancialYearStart);
        ItcGateView gate;
        try
        {
            gate = ItcGateView.Build(_company, snapshot, from, to);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            Subtitle = $"{_company.Name}  —  {snapshot.ReturnPeriod}";
            CandidatesHeader = "Reversal candidates";
            StatusText = $"Outstanding reclaimable reversal balance (ECRS) ₹{BalanceTotalText}.";
            return;
        }

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

        Subtitle = $"{_company.Name}  —  candidates from GSTR-2B {snapshot.ReturnPeriod} " +
                   $"({ApexDate.Format(from)} to {ApexDate.Format(to)})  —  advisory only, posts nothing";
        CandidatesHeader = $"Reversal candidates ({Candidates.Count})";
        StatusText = $"Outstanding reclaimable reversal balance (ECRS) ₹{BalanceTotalText}  ·  {Candidates.Count} candidate(s) surfaced for review.";
    }

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
