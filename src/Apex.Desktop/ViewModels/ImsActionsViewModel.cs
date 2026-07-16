using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>One actionable GSTR-2B line on the IMS screen: its identity/amounts, the derived effective status, and
/// whether IMS can act on it at all (an RCM line is bypassed by the IMS dashboard entirely).</summary>
public sealed partial class ImsLineRowVm : ViewModelBase
{
    public Guid LineId { get; init; }
    public string Supplier { get; init; } = string.Empty;
    public string DocNo { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string TaxableValue { get; init; } = string.Empty;
    public string Tax { get; init; } = string.Empty;

    /// <summary>The derived IMS status (<see cref="ImsService.EffectiveStatus"/>) — "Accepted (deemed)" when the
    /// taxpayer never acted, since no-action deems acceptance on filing.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>True when the taxpayer explicitly recorded this decision (vs the deemed-accept default).</summary>
    public bool IsExplicit { get; init; }

    /// <summary>False for an RCM line: §3.3 supplies bypass IMS, so no Accept/Reject/Pending is offered (the engine
    /// throws if asked). Drives the "not actionable" row treatment instead of a crash.</summary>
    public bool IsActionable { get; init; }

    /// <summary>The per-row note: the declared reversal + remarks on an accepted credit note, or the RCM bypass note.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>True for a status the taxpayer is happy with (Accepted) — drives the row's status colour.</summary>
    public bool IsClean { get; init; }

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>IMS — Accept / Reject / Pending</b> action screen (Reports → Statutory Reports → GST Actions → IMS
/// (Accept / Reject / Pending); Phase 9 UI-2; RQ-13). Drives the pure <see cref="ImsService"/> over the lines of a
/// chosen imported GSTR-2B snapshot: the taxpayer highlights a line and explicitly records
/// <b>Accept</b> / <b>Reject</b> / <b>Pending</b>, or <b>clears</b> the decision back to the deemed-accept default.
///
/// <para><b>Engine guards surfaced, never crashed into</b> (each throw is caught and shown as a message, leaving the
/// prior decision untouched — the engine validates before it mutates):</para>
/// <list type="bullet">
/// <item>An <b>RCM</b> (<c>ReverseCharge</c>) line is <b>not IMS-actionable</b> — §3.3 supplies bypass the IMS
/// dashboard, so the row is rendered disabled and the actions refuse before ever reaching the engine.</item>
/// <item>An <b>Accept</b> may carry a <b>declared partial ITC reversal</b> (the Oct-2025 credit-note rule); a partial
/// (&gt; 0 paisa) requires <b>remarks</b>, and a partial + "no reversal declared" are mutually exclusive.</item>
/// <item>A declared reversal is only valid on an <b>Accepted</b> action.</item>
/// </list>
///
/// <para><b>Opening this screen posts nothing</b> — only the explicit Accept/Reject/Pending/Clear mutates, and each
/// persists the company. Gated: Regular GST company (ER-13). MVVM boundary: engine + persistence only.</para>
/// </summary>
public sealed partial class ImsActionsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "IMS — Accept / Reject / Pending";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private int _acceptedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _notActionableCount;
    [ObservableProperty] private int _highlightedIndex = -1;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;

    /// <summary>The declared partial ITC reversal (₹) to carry on an <b>Accept</b> — blank ⇒ none declared. A value
    /// &gt; 0 is a partial and the engine then <b>requires</b> <see cref="Remarks"/> (Oct-2025 credit-note rule).</summary>
    [ObservableProperty] private string _declaredReversalText = string.Empty;

    /// <summary>The remarks accompanying a declared partial reversal — mandatory when a partial is declared.</summary>
    [ObservableProperty] private string _remarks = string.Empty;

    /// <summary>"No reversal declared" on an Accept — mutually exclusive with a partial declared reversal.</summary>
    [ObservableProperty] private bool _noReversalDeclared;

    private Gstr2bSnapshotOption? _selectedSnapshot;

    /// <summary>The imported GSTR-2B snapshots whose lines IMS can act on (latest first).</summary>
    public ObservableCollection<Gstr2bSnapshotOption> Snapshots { get; } = new();

    /// <summary>The chosen snapshot's lines, each with its derived effective status.</summary>
    public ObservableCollection<ImsLineRowVm> Rows { get; } = new();

    public ImsActionsViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });

        foreach (var snap in GstAdvancedSnapshots.Gstr2b(company))
            Snapshots.Add(new Gstr2bSnapshotOption { Snapshot = snap });

        _selectedSnapshot = Snapshots.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected 2B snapshot; changing it re-projects the line list.</summary>
    public Gstr2bSnapshotOption? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set { if (SetProperty(ref _selectedSnapshot, value)) Rebuild(); }
    }

    /// <summary>The highlighted line, or null when the list is empty.</summary>
    public ImsLineRowVm? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Rows.Count ? Rows[HighlightedIndex] : null;

    /// <summary>(Re)projects the chosen snapshot's lines + their derived IMS status. Posts nothing.</summary>
    public void Rebuild()
    {
        var keepIndex = HighlightedIndex;
        Rows.Clear();
        AcceptedCount = RejectedCount = PendingCount = NotActionableCount = 0;

        var snap = SelectedSnapshot?.Snapshot;
        HasSnapshot = snap is not null;
        if (snap is null)
        {
            Subtitle = $"{_company.Name}  —  no GSTR-2B imported";
            StatusText = "No GSTR-2B imported yet — import a 2B statement to accept / reject its lines.";
            Message = "No GSTR-2B imported.";
            HighlightedIndex = -1;
            return;
        }

        foreach (var line in snap.Lines)
            Rows.Add(FromLine(line));

        AcceptedCount = Rows.Count(r => r.IsActionable && r.Status.StartsWith("Accepted", StringComparison.Ordinal));
        RejectedCount = Rows.Count(r => r.IsActionable && r.Status == "Rejected");
        PendingCount = Rows.Count(r => r.IsActionable && r.Status == "Pending");
        NotActionableCount = Rows.Count(r => !r.IsActionable);

        var (from, to) = GstAdvancedSnapshots.Window(snap.ReturnPeriod, _company.FinancialYearStart);
        Subtitle = $"{_company.Name}  —  GSTR-2B {snap.ReturnPeriod} " +
                   $"({from.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} to {to.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)})";
        StatusText = $"Accepted {AcceptedCount}  ·  rejected {RejectedCount}  ·  pending {PendingCount}" +
                     (NotActionableCount > 0 ? $"  ·  {NotActionableCount} RCM line(s) bypass IMS (§3.3)." : ".") +
                     "  A line left un-actioned is deemed accepted on filing.";

        // Keep the caret where it was across a refresh (an action re-projects the list under the user).
        HighlightedIndex = Rows.Count == 0 ? -1 : Math.Clamp(keepIndex < 0 ? 0 : keepIndex, 0, Rows.Count - 1);
        OnHighlightedIndexChanged(HighlightedIndex);
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
        OnPropertyChanged(nameof(HighlightedRow));
    }

    // ---------------------------------------------------------------- the explicit actions (the only mutators)

    // The four buttons on the page. Each is a thin void wrapper so the CommunityToolkit generator can emit an
    // ICommand (it supports only void/Task returns) while the action itself still reports success to a caller/test.
    [RelayCommand] private void AcceptLine() => Accept();
    [RelayCommand] private void RejectLine() => Reject();
    [RelayCommand] private void HoldLine() => Pending();
    [RelayCommand] private void ClearLine() => Clear();

    /// <summary>Records an explicit <b>Accept</b> on the highlighted line — optionally carrying a declared partial ITC
    /// reversal + its mandatory remarks, or a "no reversal declared" flag (Oct-2025 credit-note rule).</summary>
    public bool Accept()
    {
        long? declared = null;
        if (!string.IsNullOrWhiteSpace(DeclaredReversalText))
        {
            if (!TryParseRupeesToPaisa(DeclaredReversalText, out var paisa))
                return Fail($"'{DeclaredReversalText}' is not a valid rupee amount.");
            declared = paisa;
        }
        return Act(ImsStatus.Accepted, declared, NoReversalDeclared);
    }

    /// <summary>Records an explicit <b>Reject</b> on the highlighted line.</summary>
    public bool Reject() => Act(ImsStatus.Rejected, declaredReversalPaisa: null, noReversalDeclared: false);

    /// <summary>Records an explicit <b>Pending</b> on the highlighted line (deferred to a later period).</summary>
    public bool Pending() => Act(ImsStatus.Pending, declaredReversalPaisa: null, noReversalDeclared: false);

    /// <summary>Clears the highlighted line's decision — it reverts to the derived deemed-accept default.</summary>
    public bool Clear()
    {
        Message = null;
        LastActionSucceeded = false;

        var row = HighlightedRow;
        if (row is null) return Fail("Highlight a 2B line first.");
        if (!row.IsActionable)
            return Fail("This is a reverse-charge (RCM) line — §3.3 supplies bypass the IMS dashboard, so it carries no IMS action to clear.");

        ImsService.ClearAction(_company, row.LineId);
        return Succeed(row, "cleared — the line reverts to deemed-accept");
    }

    /// <summary>The one mutating path: validates the row is actionable, calls the engine, and surfaces any engine
    /// rejection as a message (leaving the prior decision untouched).</summary>
    private bool Act(ImsStatus status, long? declaredReversalPaisa, bool noReversalDeclared)
    {
        Message = null;
        LastActionSucceeded = false;

        var row = HighlightedRow;
        if (row is null) return Fail("Highlight a 2B line first.");

        // The engine throws for an RCM line; refuse it here so the row's disabled treatment and the message agree.
        if (!row.IsActionable)
            return Fail("This is a reverse-charge (RCM) line — §3.3 supplies bypass the IMS dashboard and cannot be accepted, rejected or held.");

        try
        {
            ImsService.SetAction(
                _company, row.LineId, status,
                remarks: string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(),
                declaredReversalPaisa: declaredReversalPaisa,
                noReversalDeclared: noReversalDeclared,
                actedOn: _company.FinancialYearStart);
        }
        catch (ArgumentException ex)      // an Oct-2025 invariant breach (partial without remarks, …)
        {
            return Fail(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        return Succeed(row, status switch
        {
            ImsStatus.Accepted => declaredReversalPaisa is > 0
                ? $"accepted with a declared ITC reversal of ₹{IndianFormat.AmountAlways(new Money(declaredReversalPaisa.Value / 100m))}"
                : noReversalDeclared ? "accepted, no ITC reversal declared" : "accepted",
            ImsStatus.Rejected => "rejected",
            ImsStatus.Pending => "held as pending",
            _ => "recorded",
        });
    }

    private bool Succeed(ImsLineRowVm row, string what)
    {
        _storage.Save(_company);
        var doc = row.DocNo;
        Rebuild();
        LastActionSucceeded = true;
        Message = $"{doc} — {what}.";
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    private ImsLineRowVm FromLine(Gstr2bLine line)
    {
        var action = _company.ImsActions.FirstOrDefault(a => a.LineId == line.Id);
        var effective = ImsService.EffectiveStatus(_company, line);
        var isExplicit = action is not null && action.Status != ImsStatus.NoAction;

        var status = !line.ReverseCharge && !isExplicit
            ? "Accepted (deemed)"
            : effective.ToString();

        return new ImsLineRowVm
        {
            LineId = line.Id,
            Supplier = line.SupplierGstin,
            DocNo = line.DocNumber,
            Date = line.DocDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
            TaxableValue = R(line.TaxableValuePaisa),
            Tax = R(line.TotalTaxPaisa),
            Status = line.ReverseCharge ? "Not actionable" : status,
            IsExplicit = isExplicit,
            IsActionable = !line.ReverseCharge,
            IsClean = !line.ReverseCharge && effective == ImsStatus.Accepted,
            Note = NoteFor(line, action),
        };
    }

    private static string NoteFor(Gstr2bLine line, ImsAction? action)
    {
        if (line.ReverseCharge)
            return "Reverse-charge (§3.3) — bypasses the IMS dashboard; the liability is discharged in cash regardless.";
        if (action is null || action.Status == ImsStatus.NoAction)
            return "No action recorded — deemed accepted on filing.";
        if (action.DeclaredReversalPaisa is > 0)
            return $"Declared ITC reversal ₹{R(action.DeclaredReversalPaisa.Value)} — {action.Remarks}";
        if (action.NoReversalDeclared)
            return "Accepted with an explicit 'no ITC reversal' declaration.";
        return string.IsNullOrWhiteSpace(action.Remarks) ? "Explicitly actioned." : action.Remarks!;
    }

    /// <summary>Parses a typed rupee amount to exact paisa; rejects anything non-numeric or sub-paisa.</summary>
    private static bool TryParseRupeesToPaisa(string text, out long paisa)
    {
        paisa = 0;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rupees))
            return false;
        var scaled = rupees * 100m;
        if (scaled != decimal.Truncate(scaled)) return false;   // sub-paisa ⇒ not representable
        paisa = (long)scaled;
        return true;
    }

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
