using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>One selectable account row on the multi-account print panel.</summary>
public sealed partial class MultiAccountRowViewModel : ViewModelBase, IMasterListRow
{
    public Guid LedgerId { get; }
    public string Name { get; }
    public string GroupName { get; }

    /// <summary>Whether this account is in the print job. Space toggles it; the panel drives Select All / None.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>True while this row carries the keyboard highlight (Up/Down move it).</summary>
    [ObservableProperty] private bool _isHighlighted;

    // IMasterListRow — so the shared PayrollMasterHighlight machinery drives this list too, rather than the panel
    // growing a second, subtly-different highlight of its own.
    Guid IMasterListRow.MasterId => LedgerId;
    string IMasterListRow.MasterName => Name;

    public MultiAccountRowViewModel(Guid ledgerId, string name, string groupName)
    {
        LedgerId = ledgerId;
        Name = name ?? string.Empty;
        GroupName = groupName ?? string.Empty;
    }
}

/// <summary>
/// The keyboard-first <b>Multi-Account Printing</b> panel (W2-32 / W-F1; census rows 12.6 and 12.7).
///
/// <para><b>Route in</b> (W-F1): Gateway → Reports → Statements of Accounts → <b>Multi-Account Printing</b> →
/// one of <i>Ledger Accounts</i> / <i>Reminder Letters</i> / <i>Confirmation of Accounts</i>. Each leaf opens
/// this panel with <see cref="DocumentKind"/> pre-set. On the panel: Up/Down move the highlight, <b>Space</b>
/// toggles the highlighted account into the job, <b>Ctrl+Space</b> selects all (again to select none),
/// <b>Ctrl+A</b> builds the job and opens the multi-document Print Preview over it, <b>Esc</b> pops back.</para>
///
/// <para>🔴 <b>This header used to say the type was NOT REACHABLE, and that was true when it was written.</b> It
/// is retained in corrected form rather than silently swapped, because the previous correction in this file
/// records that an EARLIER header falsely claimed a route that never existed. The route asserted above is held
/// by <c>MultiAccountPrintReachabilityTests</c>, which realises the real <c>MainWindow</c>, walks the real menu
/// and reads the realised controls — not by this comment. If those tests are deleted, this claim becomes
/// unsupported again.</para>
///
/// <para><b>SCOPE (W-F1 defect D-12.6-b).</b> The panel used to list <i>every</i> ledger in the company for every
/// document kind, including Cash, Bank, tax ledgers and P&amp;L heads — a "Reminder Letter" for Output CGST is
/// nonsense. Reminder Letters and Confirmation of Accounts therefore list only <b>party</b> ledgers (those under
/// Sundry Debtors / Sundry Creditors); Ledger Accounts still lists every ledger, because an account statement of
/// any ledger is a real thing. This mirrors the vendor's own report families
/// (help.tallysolutions.com — "How to Print, Export, E-mail Multi Account Reports in TallyPrime": reminder
/// letters over <i>Bills Receivable / All Ledger Outstandings / Ledger Outstandings / Group of Account
/// Outstandings / Group Outstandings</i>) without inventing report names we cannot source.</para>
///
/// <para>No clock: the "as at" date is supplied by the shell, so the panel stays deterministic in tests.</para>
/// </summary>
public sealed partial class MultiAccountPrintViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _asOf;
    private readonly PayrollMasterHighlight<MultiAccountRowViewModel> _highlight;

    /// <summary>Set while a bulk select is running, so <see cref="SelectedCount"/> is raised ONCE for the whole
    /// pass instead of once per row (W-F1 defect D-12.6-a).</summary>
    private bool _bulkSelecting;

    public string Title => "Multi-Account Printing";

    /// <summary>The accounts in scope for the current <see cref="DocumentKind"/>, in name order, each selectable.</summary>
    public ObservableCollection<MultiAccountRowViewModel> Accounts { get; } = new();

    /// <summary>The document each selected account produces.</summary>
    [ObservableProperty] private MultiAccountDocumentKind _documentKind = MultiAccountDocumentKind.LedgerAccount;

    /// <summary>A status line shown after Print (or the reason nothing was printed).</summary>
    [ObservableProperty] private string _status = string.Empty;

    // Radio-style bindings for the document kind (one true at a time).
    public bool IsLedgerAccount
    {
        get => DocumentKind == MultiAccountDocumentKind.LedgerAccount;
        set { if (value) DocumentKind = MultiAccountDocumentKind.LedgerAccount; }
    }

    public bool IsReminderLetter
    {
        get => DocumentKind == MultiAccountDocumentKind.ReminderLetter;
        set { if (value) DocumentKind = MultiAccountDocumentKind.ReminderLetter; }
    }

    public bool IsConfirmation
    {
        get => DocumentKind == MultiAccountDocumentKind.ConfirmationOfAccounts;
        set { if (value) DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts; }
    }

    /// <summary>The period line the panel shows, so the operator can see what is being printed.</summary>
    public string PeriodText => _from.ToString("dd-MM-yyyy") + " to " + _asOf.ToString("dd-MM-yyyy");

    /// <summary>
    /// The on-screen statement of WHICH accounts this kind offers. It is shown on the panel because the list
    /// silently shrinking when the kind changes would otherwise read as accounts having gone missing.
    /// </summary>
    public string ScopeText => PartyOnly(DocumentKind)
        ? "Party accounts only (Sundry Debtors / Sundry Creditors)."
        : "All accounts in this company.";

    /// <summary>How many accounts are currently in the job.</summary>
    public int SelectedCount
    {
        get
        {
            int n = 0;
            foreach (var a in Accounts) if (a.IsSelected) n++;
            return n;
        }
    }

    /// <summary>The heading the print-preview column carries for the job this panel would build.</summary>
    public string JobTitle => MultiAccountPrintProjector.JobTitleFor(DocumentKind);

    /// <summary>The highlighted account row, or <c>null</c> when the highlight has not entered the list.</summary>
    public MultiAccountRowViewModel? HighlightedRow => _highlight.Row;

    /// <summary>Up/Down on the panel — moves the highlight, wrapping.</summary>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    public MultiAccountPrintViewModel(Company company, DateOnly from, DateOnly asOf)
        : this(company, from, asOf, MultiAccountDocumentKind.LedgerAccount) { }

    /// <summary>Opens the panel already on <paramref name="kind"/> — the shape the three menu leaves use.</summary>
    public MultiAccountPrintViewModel(
        Company company, DateOnly from, DateOnly asOf, MultiAccountDocumentKind kind)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _from = from;
        _asOf = asOf;
        _highlight = new PayrollMasterHighlight<MultiAccountRowViewModel>(
            Accounts, () => OnPropertyChanged(nameof(HighlightedRow)));
        // Assign through the field so the partial-changed hook does not rebuild the list before the highlight
        // helper exists; the explicit rebuild below does it once.
        _documentKind = kind;
        RebuildAccounts();
    }

    /// <summary>
    /// True when <paramref name="kind"/> is a document addressed to a COUNTERPARTY about its account — a reminder
    /// letter or a confirmation request. Those are only meaningful for a party ledger; a ledger account statement
    /// is meaningful for any ledger. (W-F1 defect D-12.6-b.)
    /// </summary>
    private static bool PartyOnly(MultiAccountDocumentKind kind) =>
        kind is MultiAccountDocumentKind.ReminderLetter or MultiAccountDocumentKind.ConfirmationOfAccounts;

    /// <summary>
    /// True iff <paramref name="ledger"/> sits under Sundry Debtors or Sundry Creditors.
    ///
    /// <para>🔴 <b>Design correction.</b> The W-F1 design named <c>Outstandings.KindOf</c> as the predicate that
    /// "already classifies exactly this". It does not: <c>KindOf</c> answers <i>receivable or payable</i> from the
    /// group's PRIMARY NATURE, so it returns <c>Receivable</c> for Cash, Bank and every other asset ledger and
    /// throws on a ledger whose group is missing. Using it would have left the whole chart of accounts on a
    /// reminder-letter list — the exact defect the scope filter exists to close.
    /// <c>ClassificationRules.GroupIsUnder</c> is the predicate that answers the question actually asked, resolves
    /// from the group ID rather than a name (so two groups sharing a display name are told apart) and is already
    /// used this way elsewhere in the engine.</para>
    /// </summary>
    private static bool IsPartyLedger(Company company, DomainLedger ledger) =>
        ClassificationRules.GroupIsUnder(ledger.GroupId, "Sundry Debtors", company)
        || ClassificationRules.GroupIsUnder(ledger.GroupId, "Sundry Creditors", company);

    /// <summary>
    /// (Re)builds <see cref="Accounts"/> for the current <see cref="DocumentKind"/>. Selections do not survive a
    /// kind change, deliberately: the two lists are different sets, and silently carrying a selection across
    /// would put an account in a job whose row the operator can no longer see.
    /// </summary>
    private void RebuildAccounts()
    {
        foreach (var existing in Accounts) existing.PropertyChanged -= OnRowPropertyChanged;
        Accounts.Clear();

        var ordered = new List<DomainLedger>(_company.Ledgers);
        ordered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        bool partyOnly = PartyOnly(DocumentKind);
        foreach (var l in ordered)
        {
            if (partyOnly && !IsPartyLedger(_company, l)) continue;
            var row = new MultiAccountRowViewModel(l.Id, l.Name, GroupNameOf(_company, l));
            row.PropertyChanged += OnRowPropertyChanged;
            Accounts.Add(row);
        }

        _highlight.RestoreTo(null);
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // D-12.6-a: SelectAll()/SelectNone() used to raise SelectedCount once PER ROW — O(n) redundant
        // notifications (and O(n) re-reads of an O(n) getter) on a company with many ledgers. The bulk flag
        // collapses the pass to a single notification; the single-row Space path is unchanged.
        if (_bulkSelecting) return;
        if (e.PropertyName == nameof(MultiAccountRowViewModel.IsSelected))
            OnPropertyChanged(nameof(SelectedCount));
    }

    private static string GroupNameOf(Company company, DomainLedger ledger)
    {
        foreach (var g in company.Groups)
            if (g.Id == ledger.GroupId) return g.Name;
        return string.Empty;
    }

    partial void OnDocumentKindChanged(MultiAccountDocumentKind value)
    {
        OnPropertyChanged(nameof(IsLedgerAccount));
        OnPropertyChanged(nameof(IsReminderLetter));
        OnPropertyChanged(nameof(IsConfirmation));
        OnPropertyChanged(nameof(JobTitle));
        OnPropertyChanged(nameof(ScopeText));
        RebuildAccounts();
        Status = string.Empty;
    }

    /// <summary>Puts every account in the job.</summary>
    public void SelectAll() => SetAll(true);

    /// <summary>Takes every account out of the job.</summary>
    public void SelectNone() => SetAll(false);

    /// <summary>Ctrl+Space: selects all when anything is unselected, otherwise clears the whole selection.</summary>
    public void ToggleSelectAll()
    {
        if (SelectedCount == Accounts.Count && Accounts.Count > 0) SelectNone();
        else SelectAll();
    }

    /// <summary>Space: toggles the highlighted account in or out of the job. A no-op with no highlight.</summary>
    public bool ToggleHighlighted()
    {
        if (_highlight.Row is not { } row) return false;
        row.IsSelected = !row.IsSelected;
        return true;
    }

    private void SetAll(bool selected)
    {
        _bulkSelecting = true;
        try { foreach (var a in Accounts) a.IsSelected = selected; }
        finally { _bulkSelecting = false; }
        OnPropertyChanged(nameof(SelectedCount));
    }

    /// <summary>The selected account ids, in the panel's display order (the order they will print in).</summary>
    public IReadOnlyList<Guid> SelectedLedgerIds()
    {
        var ids = new List<Guid>();
        foreach (var a in Accounts) if (a.IsSelected) ids.Add(a.LedgerId);
        return ids;
    }

    /// <summary>
    /// Builds the print job: one <see cref="PrintReport"/> per selected account, in panel order. Returns an
    /// EMPTY list (and sets <see cref="Status"/>) when nothing is selected — the caller then opens no preview,
    /// because a print job of nothing is a mistake to report, not a blank sheet to render.
    /// </summary>
    public IReadOnlyList<PrintReport> BuildJob()
    {
        var ids = SelectedLedgerIds();
        if (ids.Count == 0)
        {
            Status = "Select at least one account to print.";
            return Array.Empty<PrintReport>();
        }

        var documents = MultiAccountPrintProjector.Project(_company, ids, DocumentKind, _from, _asOf);
        Status = documents.Count == 1
            ? "1 account ready to print."
            : $"{documents.Count:#,0} accounts ready to print.";
        return documents;
    }
}
