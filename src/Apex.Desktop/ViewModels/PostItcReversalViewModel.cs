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
using CommunityToolkit.Mvvm.Input;
using Reversal = Apex.Ledger.Services.GstReversalService;

namespace Apex.Desktop.ViewModels;

/// <summary>The reversal rule the Post-ITC-Reversal screen is posting under (drives which inputs the form needs).</summary>
public enum ItcReversalPostKind
{
    /// <summary>Rule 42 — monthly common-credit apportionment (needs the C2 pool + the exempt/total turnover).</summary>
    Rule42,

    /// <summary>Rule 42 — the annual FY true-up (a signed delta against the year's monthly postings).</summary>
    Rule42AnnualTrueUp,

    /// <summary>Rule 43 — capital-goods apportionment over 60 months (needs the capital good's voucher).</summary>
    Rule43,

    /// <summary>Rule 37 — non-payment to the supplier within 180 days (reclaimable).</summary>
    Rule37,

    /// <summary>Rule 37A — the supplier did not pay the tax to the government (reclaimable).</summary>
    Rule37A,
}

/// <summary>
/// One selectable <b>source voucher</b> for a reversal that is anchored to a specific document: the capital-goods
/// purchase a Rule-43 60-month schedule apportions, or the supplier purchase a Rule-37 / 37A reversal reverses.
/// The engine keys those reversals on <c>(rule, period, sourceVoucherId)</c>, so this pick is load-bearing — it is
/// both the audit trail and the idempotency key, and it can never be guessed.
/// </summary>
public sealed class ItcReversalSourceRowVm
{
    public Guid VoucherId { get; init; }
    public string DocNo { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Party { get; init; } = string.Empty;

    /// <summary>The forward ITC availed on this purchase — what a blank-amount Rule 37 / 37A post reverses.</summary>
    public string InputTax { get; init; } = string.Empty;

    /// <summary>What the picker shows in the combo (one line: doc-no · date · party · ITC).</summary>
    public string Label => $"{DocNo}  ·  {Date}" + (Party.Length > 0 ? $"  ·  {Party}" : string.Empty) +
                           $"  ·  ITC ₹{InputTax}";

    public override string ToString() => Label;
}

/// <summary>One already-posted reversal row (its rule / period / heads) + whether it is reclaimable.</summary>
public sealed partial class PostedReversalRowVm : ViewModelBase
{
    public Guid ReversalId { get; init; }
    public string Rule { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;

    /// <summary>True for a Rule-37 / 37A reversal not yet reclaimed — the only kind <see cref="Reversal.Reclaim"/> takes.</summary>
    public bool IsReclaimable { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Post ITC Reversal</b> action screen (Reports → Statutory Reports → GST Actions → Post ITC Reversal; Phase 9
/// UI-2; RQ-27). The interactive twin of the UI-1 <see cref="ItcReversalReportViewModel"/> projection: it posts real
/// reversals through the pure <see cref="Reversal"/> engine —
/// <see cref="Reversal.PostRule42"/> / <see cref="Reversal.PostRule42AnnualTrueUp"/> / <see cref="Reversal.PostRule43"/>
/// / <see cref="Reversal.PostRule37"/> / <see cref="Reversal.PostRule37A"/>, straight from an
/// <see cref="ItcGateView"/> <b>candidate</b> via <see cref="Reversal.PostFromCandidate"/>, and
/// <see cref="Reversal.Reclaim"/> against the tracked ECRS balance.
///
/// <para><b>Engine guards surfaced, never crashed into:</b> a <b>reclaim may never exceed the outstanding ECRS
/// balance</b> (the engine caps it per head against the company-wide tracked balance, not against the single
/// reversal); only an un-reclaimed <b>Rule 37 / 37A</b> row is reclaimable at all; Rule 42/43 need a positive total
/// turnover with the exempt turnover inside <c>[0, F]</c>; and a <b>§16(2)(aa)</b> candidate is a <b>deferral</b> —
/// <see cref="Reversal.PostFromCandidate"/> posts nothing for it, so the screen says so rather than reporting a
/// posting that never happened.</para>
///
/// <para><b>Opening this screen posts nothing</b> — it only projects the balance, the candidates and the history.
/// Gated: Regular GST company (ER-13).</para>
/// </summary>
public sealed partial class PostItcReversalViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly Reversal _reversal;

    [ObservableProperty] private string _title = "Post ITC Reversal";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;

    // The tracked ECRS balance (the cap a reclaim may never exceed).
    [ObservableProperty] private string _balanceCgstText = "0.00";
    [ObservableProperty] private string _balanceSgstText = "0.00";
    [ObservableProperty] private string _balanceIgstText = "0.00";
    [ObservableProperty] private string _balanceCessText = "0.00";
    [ObservableProperty] private string _balanceTotalText = "0.00";

    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private bool _hasCandidates;
    [ObservableProperty] private int _candidateIndex = -1;
    [ObservableProperty] private int _highlightedIndex = -1;

    /// <summary>The picked source voucher in <see cref="SourceVouchers"/>, or -1 for none. <b>-1 means the anchored
    /// rules refuse to post</b> — never that the screen picks one for you.</summary>
    [ObservableProperty] private int _selectedSourceIndex = -1;

    // The posting form.
    [ObservableProperty] private ItcReversalPostKind _kind = ItcReversalPostKind.Rule42;
    [ObservableProperty] private string _period = string.Empty;
    [ObservableProperty] private string _cgstText = string.Empty;
    [ObservableProperty] private string _sgstText = string.Empty;
    [ObservableProperty] private string _igstText = string.Empty;
    [ObservableProperty] private string _cessText = string.Empty;
    [ObservableProperty] private string _exemptTurnoverText = string.Empty;
    [ObservableProperty] private string _totalTurnoverText = string.Empty;

    /// <summary>The reversal rules the form can post under.</summary>
    public ObservableCollection<ItcReversalPostKind> Kinds { get; } =
        new((ItcReversalPostKind[])Enum.GetValues(typeof(ItcReversalPostKind)));

    /// <summary>The advisory reversal candidates from the latest 2B (each postable in one click).</summary>
    public ObservableCollection<ItcReversalCandidateRowVm> Candidates { get; } = new();

    /// <summary>
    /// The purchases the current rule can anchor to — <b>Rule 43</b>: the capital good whose credit is apportioned over
    /// 60 months; <b>Rule 37 / 37A</b>: the supplier purchase whose ITC is reversed. Empty for Rule 42, which derives
    /// its own amount from the C2 pool and needs no source document.
    /// </summary>
    public ObservableCollection<ItcReversalSourceRowVm> SourceVouchers { get; } = new();

    /// <summary>Every reversal already posted (newest period first) — the reclaim acts on the highlighted one.</summary>
    public ObservableCollection<PostedReversalRowVm> Posted { get; } = new();

    private IReadOnlyList<ItcReversalCandidate> _rawCandidates = Array.Empty<ItcReversalCandidate>();

    /// <summary>The raw ITC-gate candidates behind <see cref="Candidates"/> (same order) — each carries the source
    /// voucher / 2B line it is about.</summary>
    public IReadOnlyList<ItcReversalCandidate> RawCandidates => _rawCandidates;

    public PostItcReversalViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _reversal = new Reversal(company);
        _period = company.FinancialYearStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Rebuild();
    }

    /// <summary>The highlighted already-posted reversal (what <see cref="Reclaim"/> acts on).</summary>
    public PostedReversalRowVm? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Posted.Count ? Posted[HighlightedIndex] : null;

    /// <summary>The picked source voucher, or null — in which case the anchored rules <b>refuse to post</b>.</summary>
    public ItcReversalSourceRowVm? SelectedSource =>
        SelectedSourceIndex >= 0 && SelectedSourceIndex < SourceVouchers.Count
            ? SourceVouchers[SelectedSourceIndex]
            : null;

    /// <summary>True when the chosen rule is anchored to a specific document (Rule 43 / 37 / 37A) and therefore needs
    /// a pick from <see cref="SourceVouchers"/>. Rule 42 derives from the C2 pool and needs none.</summary>
    public bool NeedsSourceVoucher =>
        Kind is ItcReversalPostKind.Rule43 or ItcReversalPostKind.Rule37 or ItcReversalPostKind.Rule37A;

    /// <summary>The picker's caption — the two anchored rules pick genuinely different things.</summary>
    public string SourceVoucherLabel => Kind == ItcReversalPostKind.Rule43
        ? "Capital good"
        : "Supplier purchase";

    partial void OnSelectedSourceIndexChanged(int value) => OnPropertyChanged(nameof(SelectedSource));

    partial void OnKindChanged(ItcReversalPostKind value)
    {
        OnPropertyChanged(nameof(NeedsSourceVoucher));
        OnPropertyChanged(nameof(SourceVoucherLabel));
        BuildSourceVouchers();
    }

    partial void OnCandidateIndexChanged(int value)
    {
        // The promised first clause, finally wired: a candidate that names its own source voucher pre-selects it, so
        // the reversal anchors to the asset / supplier the candidate is actually about.
        var i = CandidateSourceIndex();
        if (i >= 0) SelectedSourceIndex = i;
    }

    /// <summary>The picker index of the highlighted candidate's own source voucher, or -1 (a 2B-line-keyed candidate
    /// carries no voucher, and a voucher outside the current rule's picker cannot be anchored to).</summary>
    private int CandidateSourceIndex()
    {
        var vid = CandidateIndex >= 0 && CandidateIndex < _rawCandidates.Count
            ? _rawCandidates[CandidateIndex].VoucherId
            : null;
        return vid is null ? -1 : IndexOfSource(vid.Value);
    }

    private int IndexOfSource(Guid voucherId)
    {
        for (var i = 0; i < SourceVouchers.Count; i++)
            if (SourceVouchers[i].VoucherId == voucherId) return i;
        return -1;
    }

    /// <summary>
    /// (Re)builds the source-voucher picker for the current rule. Rule 43 offers every <b>purchase</b> carrying
    /// forward ITC (the user names which one is the capital good — the books carry no capital-goods flag); Rule 37 /
    /// 37A narrows that to purchases with a <b>supplier</b> (the party you owe). A sales invoice is never offered:
    /// its output tax would be silently mistaken for ITC by the engine's forward-ITC default.
    /// <para>Keeps the current pick when the new list still contains it, else falls back to the highlighted
    /// candidate's own voucher, else selects nothing — because nothing is exactly what the anchored rules must post
    /// when the anchor is unknown.</para>
    /// </summary>
    private void BuildSourceVouchers()
    {
        var keep = SelectedSource?.VoucherId;
        SourceVouchers.Clear();

        if (NeedsSourceVoucher)
        {
            foreach (var v in _company.Vouchers
                         .Where(v => !v.Cancelled && IsPurchase(v))
                         .Where(v => Kind == ItcReversalPostKind.Rule43 || v.PartyId is not null)
                         .OrderBy(v => v.Date).ThenBy(v => v.Number))
            {
                var itc = ForwardInputTaxOf(v);
                if (itc == 0) continue;   // nothing to apportion / reverse — never offer it as an anchor
                SourceVouchers.Add(new ItcReversalSourceRowVm
                {
                    VoucherId = v.Id,
                    DocNo = EInvoiceService.DocumentNumberOf(_company, v),
                    Date = ApexDate.Format(v.Date),
                    Party = v.PartyId is { } pid ? _company.FindLedger(pid)?.Name ?? string.Empty : string.Empty,
                    InputTax = R(itc),
                });
            }
        }

        var i = keep is not null ? IndexOfSource(keep.Value) : -1;
        if (i < 0) i = CandidateSourceIndex();
        SelectedSourceIndex = i;
        OnPropertyChanged(nameof(SelectedSource));
    }

    private bool IsPurchase(Voucher v) =>
        _company.VoucherTypes.FirstOrDefault(t => t.Id == v.TypeId)?.BaseType == VoucherBaseType.Purchase;

    /// <summary>The total forward (non-RCM, non-adjustment) input tax posted on a voucher, in paisa — mirrors the
    /// engine's own default so the figure the picker shows is exactly what a blank-amount Rule 37 / 37A would
    /// reverse.</summary>
    private static long ForwardInputTaxOf(Voucher voucher)
    {
        var total = 0L;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge || g.Adjustment is not null) continue;
            total += (long)Math.Round(line.Amount.Amount * 100m, MidpointRounding.AwayFromZero);
        }
        return total;
    }

    /// <summary>(Re)projects the ECRS balance, the 2B candidates and the posted-reversal history. Posts nothing.</summary>
    public void Rebuild()
    {
        var keepIndex = HighlightedIndex;
        Candidates.Clear();
        Posted.Clear();

        var balance = _reversal.OutstandingReversalBalance();
        BalanceCgstText = R(balance.CgstPaisa); BalanceSgstText = R(balance.SgstPaisa);
        BalanceIgstText = R(balance.IgstPaisa); BalanceCessText = R(balance.CessPaisa);
        BalanceTotalText = R(balance.TotalPaisa);

        // The candidates the ITC gate surfaces from the latest 2B (advisory until explicitly posted).
        var snapshot = GstAdvancedSnapshots.Gstr2b(_company).FirstOrDefault();
        HasSnapshot = snapshot is not null;
        _rawCandidates = Array.Empty<ItcReversalCandidate>();
        if (snapshot is not null)
        {
            var (from, to) = GstAdvancedSnapshots.Window(snapshot.ReturnPeriod, _company.FinancialYearStart);
            try
            {
                var gate = ItcGateView.Build(_company, snapshot, from, to);
                _rawCandidates = gate.ReversalCandidates.ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = ex.Message;
            }

            foreach (var c in _rawCandidates)
                Candidates.Add(new ItcReversalCandidateRowVm
                {
                    Reason = ReasonLabel(c.Reason),
                    Description = c.Description,
                    Cgst = R(c.CgstPaisa), Sgst = R(c.SgstPaisa), Igst = R(c.IgstPaisa), Cess = R(c.CessPaisa),
                    Suggested = IndianFormat.AmountAlways(c.SuggestedReversal),
                });
        }
        HasCandidates = Candidates.Count > 0;
        if (CandidateIndex >= Candidates.Count) CandidateIndex = Candidates.Count > 0 ? 0 : -1;
        else if (CandidateIndex < 0 && Candidates.Count > 0) CandidateIndex = 0;

        // The anchored rules' picker — built AFTER the candidates, so a candidate that names its own source voucher
        // can pre-select it.
        BuildSourceVouchers();

        // The posted history. A Rule-37/37A row already reclaimed cannot be reclaimed again.
        var reclaimedIds = _company.ItcReversals
            .Where(r => r.ReclaimOfId is not null)
            .Select(r => r.ReclaimOfId!.Value)
            .ToHashSet();

        foreach (var r in _company.ItcReversals.OrderByDescending(r => r.Period, StringComparer.Ordinal))
        {
            var isReclaim = r.ReclaimOfId is not null;
            var reclaimable = !isReclaim
                              && r.Rule is ItcReversalRule.Rule37 or ItcReversalRule.Rule37A
                              && !reclaimedIds.Contains(r.Id);
            Posted.Add(new PostedReversalRowVm
            {
                ReversalId = r.Id,
                Rule = isReclaim ? "Reclaim" : RuleLabel(r.Rule),
                Period = r.Period,
                Total = R(r.CgstPaisa + r.SgstPaisa + r.IgstPaisa + r.CessPaisa),
                Bucket = BucketLabel(r.Table4bBucket),
                IsReclaimable = reclaimable,
                Note = isReclaim
                    ? "A reclaim of an earlier Rule 37 / 37A reversal (Table 4(D)(1))."
                    : reclaimable
                        ? "Reclaimable once the supplier is paid / the tax reaches the government."
                        : r.Rule is ItcReversalRule.Rule37 or ItcReversalRule.Rule37A
                            ? "Already reclaimed."
                            : "Not reclaimable — a permanent apportionment / blocked-credit reversal.",
            });
        }

        HighlightedIndex = Posted.Count == 0 ? -1 : Math.Clamp(keepIndex < 0 ? 0 : keepIndex, 0, Posted.Count - 1);
        OnHighlightedIndexChanged(HighlightedIndex);

        Subtitle = $"{_company.Name}  —  outstanding reclaimable reversal balance (ECRS) ₹{BalanceTotalText}";
        StatusText = $"ECRS balance ₹{BalanceTotalText}  ·  {Candidates.Count} candidate(s) from the latest GSTR-2B  ·  " +
                     $"{Posted.Count} reversal row(s) posted. A reclaim can never exceed the ECRS balance.";
    }

    /// <summary>Moves the posted-row highlight (Up/Down within the page); wraps.</summary>
    public void MoveHighlight(int direction)
    {
        if (Posted.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Posted.Count) % Posted.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Posted.Count; i++)
            Posted[i].IsHighlighted = i == value;
        OnPropertyChanged(nameof(HighlightedRow));
    }

    // ---------------------------------------------------------------- the explicit actions (the only mutators)

    [RelayCommand] private void PostAction() => Post();
    [RelayCommand] private void PostCandidateAction() => PostCandidate();
    [RelayCommand] private void ReclaimAction() => Reclaim();

    /// <summary>
    /// Posts the form's reversal under the chosen rule. Rule 42/43 derive their own amount from the C2/Tc pool + the
    /// exempt/total turnover; Rule 37/37A take the typed per-head amounts (or, left blank, the source voucher's own
    /// forward ITC). A zero-amount reversal posts nothing — the engine returns null and the screen says so.
    /// </summary>
    public bool Post()
    {
        Message = null;
        LastActionSucceeded = false;

        if (string.IsNullOrWhiteSpace(Period)) return Fail("Enter the return period (yyyy-MM).");
        if (!TryParseAmount(out var amount, out var amountError)) return Fail(amountError!);

        var date = PeriodEnd(Period);
        if (date is null) return Fail($"'{Period}' is not a valid return period — use yyyy-MM (e.g. 2024-04).");

        // The engine is idempotent per (rule, period, source): a re-run returns the EXISTING row rather than posting a
        // second one. Snapshot the ids so a "posting" that merely handed back a pre-existing row is reported as what it
        // is, instead of as a tranche that was never written.
        var before = _company.ItcReversals.Select(r => r.Id).ToHashSet();

        try
        {
            ItcReversal? posted;
            switch (Kind)
            {
                case ItcReversalPostKind.Rule42:
                case ItcReversalPostKind.Rule42AnnualTrueUp:
                {
                    if (!TryParseTurnover(out var exempt, out var total, out var tErr)) return Fail(tErr!);
                    var basis = new Reversal.Rule42Basis(amount, exempt, total);
                    posted = Kind == ItcReversalPostKind.Rule42
                        ? _reversal.PostRule42(Period.Trim(), basis, date.Value)
                        : _reversal.PostRule42AnnualTrueUp(Period.Trim(), basis, date.Value);
                    break;
                }
                case ItcReversalPostKind.Rule43:
                {
                    if (!TryParseTurnover(out var exempt, out var total, out var tErr)) return Fail(tErr!);
                    // Rule 43 apportions ONE SPECIFIC capital good's credit over 60 months, and the engine keys the
                    // schedule on (rule, period, sourceVoucherId). Guessing that anchor is not a cosmetic slip: it
                    // writes an audit-false SourceVoucherId AND collides the key across assets, so the second capital
                    // good's tranche is silently swallowed by the first's row. It must be the user's own pick.
                    var source = SelectedSource;
                    if (source is null)
                        return Fail(SourceVouchers.Count == 0
                            ? "Rule 43 needs a capital-goods purchase to apportion, and this company has none " +
                              "carrying input tax."
                            : "Select the capital-goods purchase this Rule 43 tranche apportions — the 60-month " +
                              "schedule is keyed to the asset, so it cannot be guessed.");
                    posted = _reversal.PostRule43(Period.Trim(), source.VoucherId,
                        new Reversal.Rule43Basis(amount, exempt, total), date.Value);
                    break;
                }
                case ItcReversalPostKind.Rule37:
                case ItcReversalPostKind.Rule37A:
                {
                    // Same anchoring contract: the reversal is keyed to the purchase whose ITC it reverses, so the
                    // supplier must be named rather than assumed to be whoever happens to come first in the books.
                    var source = SelectedSource;
                    if (source is null)
                        return Fail(SourceVouchers.Count == 0
                            ? "Rule 37 / 37A needs a supplier's purchase voucher to reverse, and this company has " +
                              "none carrying input tax."
                            : "Select the supplier's purchase this Rule 37 / 37A reversal reverses — the reversal is " +
                              "keyed to that purchase, so it cannot be guessed.");
                    var amounts = amount.IsZero ? (Reversal.ReversalAmount?)null : amount;
                    posted = Kind == ItcReversalPostKind.Rule37
                        ? _reversal.PostRule37(source.VoucherId, Period.Trim(), date.Value, amounts)
                        : _reversal.PostRule37A(source.VoucherId, Period.Trim(), date.Value, amounts);
                    break;
                }
                default:
                    return Fail($"Unsupported reversal rule '{Kind}'.");
            }

            if (posted is null)
                return Fail($"Nothing to reverse for {Period.Trim()} — the computed reversal is zero.");

            // Nothing was written: the engine handed back the row this (rule, period, source) already has. Saying
            // "posted" here is exactly how a dropped tranche used to pass for a successful one.
            if (before.Contains(posted.Id))
                return Fail($"{RuleLabel(posted.Rule)} was ALREADY posted for {posted.Period} against this source — " +
                            $"₹{R(posted.CgstPaisa + posted.SgstPaisa + posted.IgstPaisa + posted.CessPaisa)} into " +
                            $"{posted.Table4bBucket}. Nothing was posted now. To post a different tranche, change the " +
                            $"period or select a different {(Kind == ItcReversalPostKind.Rule43 ? "capital good" : "purchase")}.");

            return Succeed($"{RuleLabel(posted.Rule)} reversal posted for {posted.Period} — " +
                           $"₹{R(posted.CgstPaisa + posted.SgstPaisa + posted.IgstPaisa + posted.CessPaisa)} " +
                           $"into {posted.Table4bBucket}.");
        }
        catch (ArgumentException ex) { return Fail(ex.Message); }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }
    }

    /// <summary>
    /// Posts the <b>highlighted candidate</b> straight from the ITC gate (§17(5)-blocked / ineligible / accepted-CN)
    /// head-for-head. A <b>§16(2)(aa)</b> candidate is a <b>deferral</b>, not a reversal — the engine posts nothing for
    /// it, and the screen reports that rather than a phantom posting.
    /// </summary>
    public bool PostCandidate()
    {
        Message = null;
        LastActionSucceeded = false;

        if (CandidateIndex < 0 || CandidateIndex >= _rawCandidates.Count)
            return Fail("Highlight a reversal candidate first.");
        if (string.IsNullOrWhiteSpace(Period)) return Fail("Enter the return period (yyyy-MM).");
        var date = PeriodEnd(Period);
        if (date is null) return Fail($"'{Period}' is not a valid return period — use yyyy-MM (e.g. 2024-04).");

        var candidate = _rawCandidates[CandidateIndex];
        try
        {
            var posted = _reversal.PostFromCandidate(candidate, Period.Trim(), date.Value);
            if (posted is null)
                return Fail(candidate.Reason == ItcReversalReason.Section16_2aaNotInPortal
                    ? "§16(2)(aa) is a DEFERRAL, not a reversal — the credit is simply not claimable this period and " +
                      "becomes claimable once the supplier files. Nothing is posted."
                    : "The candidate carries no reversible amount — nothing was posted.");

            return Succeed($"{ReasonLabel(candidate.Reason)} reversal posted for {posted.Period} — " +
                           $"₹{R(posted.CgstPaisa + posted.SgstPaisa + posted.IgstPaisa + posted.CessPaisa)}.");
        }
        catch (ArgumentOutOfRangeException ex) { return Fail(ex.Message); }
        catch (ArgumentException ex) { return Fail(ex.Message); }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }
    }

    /// <summary>
    /// <b>Reclaims</b> the highlighted Rule-37 / 37A reversal (the supplier was paid / the tax reached the
    /// government). The engine caps the reclaim at the tracked <b>ECRS balance</b> per head — a reclaim that would
    /// exceed it is refused outright, and that refusal is surfaced here rather than thrown.
    /// </summary>
    public bool Reclaim()
    {
        Message = null;
        LastActionSucceeded = false;

        var row = HighlightedRow;
        if (row is null) return Fail("Highlight a posted reversal to reclaim.");
        if (!row.IsReclaimable)
            return Fail("Only an un-reclaimed Rule 37 / 37A reversal can be reclaimed — " +
                        "a Rule 42 / 43 / §17(5) apportionment is permanent.");
        if (string.IsNullOrWhiteSpace(Period)) return Fail("Enter the return period (yyyy-MM) to reclaim into.");
        var date = PeriodEnd(Period);
        if (date is null) return Fail($"'{Period}' is not a valid return period — use yyyy-MM (e.g. 2024-04).");
        if (!TryParseAmount(out var amount, out var amountError)) return Fail(amountError!);

        try
        {
            var amounts = amount.IsZero ? (Reversal.ReversalAmount?)null : amount;
            var posted = _reversal.Reclaim(row.ReversalId, Period.Trim(), date.Value, amounts);
            return Succeed($"Reclaimed ₹{R(posted.CgstPaisa + posted.SgstPaisa + posted.IgstPaisa + posted.CessPaisa)} " +
                           $"into {posted.Period} (Table 4(D)(1)).");
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }   // incl. the ECRS cap
        catch (ArgumentException ex) { return Fail(ex.Message); }
    }

    private bool Succeed(string message)
    {
        _storage.Save(_company);
        Rebuild();
        LastActionSucceeded = true;
        Message = message;
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    /// <summary>Parses the four per-head rupee boxes into a <see cref="Reversal.ReversalAmount"/>; blank ⇒ zero.</summary>
    private bool TryParseAmount(out Reversal.ReversalAmount amount, out string? error)
    {
        amount = default;
        error = null;
        if (!Paisa(CgstText, "CGST", out var cgst, ref error)) return false;
        if (!Paisa(SgstText, "SGST", out var sgst, ref error)) return false;
        if (!Paisa(IgstText, "IGST", out var igst, ref error)) return false;
        if (!Paisa(CessText, "Cess", out var cess, ref error)) return false;
        amount = new Reversal.ReversalAmount(cgst, sgst, igst, cess);
        return true;
    }

    private bool TryParseTurnover(out long exempt, out long total, out string? error)
    {
        exempt = total = 0;
        error = null;
        if (!Paisa(ExemptTurnoverText, "Exempt turnover", out exempt, ref error)) return false;
        if (!Paisa(TotalTurnoverText, "Total turnover", out total, ref error)) return false;
        return true;
    }

    private static bool Paisa(string text, string label, out long paisa, ref string? error)
    {
        paisa = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;   // blank ⇒ zero
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var r))
        {
            error = $"{label}: '{text}' is not a valid rupee amount.";
            return false;
        }
        var scaled = r * 100m;
        if (scaled != decimal.Truncate(scaled))
        {
            error = $"{label}: '{text}' is finer than a paisa.";
            return false;
        }
        paisa = (long)scaled;
        return true;
    }

    /// <summary>The last day of a <c>yyyy-MM</c> period (the reversal's posting date), or null when malformed.</summary>
    private static DateOnly? PeriodEnd(string period)
    {
        var p = period.Trim();
        if (p.Length >= 7
            && int.TryParse(p.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(p.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && month is >= 1 and <= 12)
            return new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        return null;
    }

    private static string RuleLabel(ItcReversalRule rule) => rule switch
    {
        ItcReversalRule.Rule42 => "Rule 42",
        ItcReversalRule.Rule43 => "Rule 43",
        ItcReversalRule.Rule37 => "Rule 37",
        ItcReversalRule.Rule37A => "Rule 37A",
        _ => rule.ToString(),
    };

    private static string ReasonLabel(ItcReversalReason reason) => reason switch
    {
        ItcReversalReason.Section17_5Blocked => "§17(5) blocked",
        ItcReversalReason.Ineligible => "Ineligible (4D)",
        ItcReversalReason.Section16_2aaNotInPortal => "§16(2)(aa) not in 2B",
        ItcReversalReason.ImsAcceptedCreditNote => "Accepted CN/DN",
        _ => reason.ToString(),
    };

    // A user-facing GSTR-3B table label — never the raw enum name (which surfaced as
    // "Table4B1" / "Table4B2" / "Table4D1" in the posted-reversal grid's Bucket column).
    private static string BucketLabel(Table4bBucket bucket) => bucket switch
    {
        Table4bBucket.Table4B1 => "Table 4(B)(1)",
        Table4bBucket.Table4B2 => "Table 4(B)(2)",
        Table4bBucket.Table4D1 => "Table 4(D)(1)",
        _ => bucket.ToString(),
    };

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
