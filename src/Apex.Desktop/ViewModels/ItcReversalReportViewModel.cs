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
public sealed partial class ItcReversalReportViewModel : ViewModelBase, IMasterListExportSource
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

    /// <summary>The registrations this company holds (census 6.23) — its own first, then any additional.</summary>
    public ObservableCollection<GstRegistration> Registrations { get; } = new();

    /// <summary>Whether the registration picker is worth showing — only once the company holds more than one.</summary>
    public bool ShowsRegistrationPicker => Registrations.Count > 1;

    private GstRegistration? _selectedRegistration;

    public ItcReversalReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var registration in company.Gst?.AllRegistrations ?? [])
            Registrations.Add(registration);
        _selectedRegistration = Registrations.FirstOrDefault();

        Rebuild();
    }

    /// <summary>
    /// 🔴 <b>The registration this page is read for</b> — a real choice, replacing a hard-coded primary. Changing it
    /// re-projects BOTH figures on the page.
    /// </summary>
    public GstRegistration? SelectedRegistration
    {
        get => _selectedRegistration;
        set { if (SetProperty(ref _selectedRegistration, value)) Rebuild(); }
    }

    /// <summary>
    /// 🔴 <b>The one scope every figure on this page is read under.</b> It used to be the literal
    /// <c>GstRegistration.PrimaryId</c> with no picker anywhere and the registration named nowhere, which replaced
    /// the engine's honest refusal with a <b>silently incomplete</b> list: a branch registration's §17(5)-blocked
    /// and Table-4(D) ineligible credit never surfaced as a candidate at all, beside an ECRS balance computed over
    /// the WHOLE book — two figures over two different populations side by side with nothing saying so. Both are now
    /// read under this one id, and <see cref="RegistrationSuffix"/> prints it. Falls back to the primary, never to
    /// <c>null</c> (the engine refuses an unscoped projection); byte-identical on a single-registration book (ER-13).
    /// </summary>
    private Guid ScopedRegistrationId => _selectedRegistration?.Id ?? GstRegistration.PrimaryId;

    /// <summary>Names the registration on the page whenever there is more than one to name; empty otherwise, so a
    /// single-registration book's subtitle is unchanged (ER-13).</summary>
    private string RegistrationSuffix =>
        ShowsRegistrationPicker && _selectedRegistration is { } r
            ? $"  —  {r.Name}" + (string.IsNullOrWhiteSpace(r.Gstin) ? string.Empty : $" ({r.Gstin})")
            : string.Empty;

    /// <summary>(Re)builds the ECRS balance + the latest snapshot's reversal candidates.</summary>
    public void Rebuild()
    {
        Candidates.Clear();
        Message = null;

        // 🔴 SCOPED TO THE SAME REGISTRATION AS THE CANDIDATES BELOW. The ECRS is a portal statement per GSTIN, so a
        // whole-book balance printed beside a registration-scoped candidate list is two populations in one frame.
        var balance = new GstReversalService(_company).OutstandingReversalBalance(ScopedRegistrationId);
        BalanceCgstText = P(balance.CgstPaisa); BalanceSgstText = P(balance.SgstPaisa);
        BalanceIgstText = P(balance.IgstPaisa); BalanceCessText = P(balance.CessPaisa);
        BalanceTotalText = P(balance.TotalPaisa);

        var snapshot = GstAdvancedSnapshots.Gstr2b(_company).FirstOrDefault();
        HasSnapshot = snapshot is not null;

        if (snapshot is null)
        {
            Subtitle = $"{_company.Name}{RegistrationSuffix}  —  no GSTR-2B imported";
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
            // 🔴 THE REGISTRATION SCOPE MUST TRAVEL — and it must be the CHOSEN one, not a hard-coded primary.
            // ItcGateView.Build scopes its own three legs correctly, but a caller that passes no registrationId
            // hands all three a null, and GstReportSupport.EnsureRegistrationScoped turns that into a throw for any
            // IsMultiRegistration book — every book with a branch and every ISD company, which holds at least two
            // registrations by construction — which the catch below swallowed into a message, making this whole
            // candidate surface unreachable rather than wrong. Pinning it to the PRIMARY made it reachable but
            // silently incomplete instead: the branch's blocked and ineligible credit could never surface, and so
            // could never be reversed. It is now whatever the operator selected (ScopedRegistrationId), which
            // defaults to the primary and is PRINTED in the subtitle, so the page can never show one registration's
            // list while reading as though it covered the book.
            gate = ItcGateView.Build(_company, snapshot, from, to, ScopedRegistrationId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            Subtitle = $"{_company.Name}{RegistrationSuffix}  —  {snapshot.ReturnPeriod}";
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

        Subtitle = $"{_company.Name}{RegistrationSuffix}  —  candidates from GSTR-2B {snapshot.ReturnPeriod} " +
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

    /// <summary>
    /// <b>Census 6.19 — the snapshot that gives this screen an exit.</b> The third of the three view models
    /// 6.19 names explicitly as deriving from <see cref="ViewModelBase"/> alone. The general arm
    /// (<c>TopMasterExportSource()</c>, gating both <c>IsExportablePage</c> and <c>IsPrintablePage</c>) was
    /// already general; only the adoption was missing here, so this method is the entire fix.
    ///
    /// <para><b>The outstanding BALANCE leads, and the candidates follow it labelled as suggestions.</b> Those
    /// are two different kinds of number and the distinction is the whole meaning of this screen: the balance is
    /// reversal already recognised and outstanding, the candidates are amounts the S7 poster MIGHT reverse and
    /// which nothing here has posted. Exported as one undifferentiated block they would read as a single
    /// reversal total, which is the misreading most likely to reach a return.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>(Candidates.Count + 4);

        static IReadOnlyList<string> Section(string label)
            => new[] { label, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty };

        rows.Add(new[]
        {
            "Outstanding reversal balance", string.Empty,
            BalanceCgstText, BalanceSgstText, BalanceIgstText, BalanceCessText, BalanceTotalText,
        });

        if (Candidates.Count > 0)
        {
            rows.Add(Section(string.IsNullOrWhiteSpace(CandidatesHeader)
                ? "Reversal candidates (advisory — nothing posted)"
                : CandidatesHeader));
            foreach (var c in Candidates)
                rows.Add(new[] { c.Reason, c.Description, c.Cgst, c.Sgst, c.Igst, c.Cess, c.Suggested });
        }

        if (!string.IsNullOrWhiteSpace(StatusText))
            rows.Add(Section(StatusText));

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("Reason"),
                MasterListColumn.Text("Description"),
                MasterListColumn.Number("CGST"),
                MasterListColumn.Number("SGST"),
                MasterListColumn.Number("IGST"),
                MasterListColumn.Number("Cess"),
                MasterListColumn.Number("Suggested"),
            },
            rows);
    }

    private static string P(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
