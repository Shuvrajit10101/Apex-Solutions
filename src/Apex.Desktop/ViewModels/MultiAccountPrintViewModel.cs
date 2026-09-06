using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>One selectable account row on the multi-account print panel.</summary>
public sealed partial class MultiAccountRowViewModel : ViewModelBase
{
    public Guid LedgerId { get; }
    public string Name { get; }
    public string GroupName { get; }

    /// <summary>
    /// 🔴 The account this row stands for, kept so eligibility can be asked of the ONE rule
    /// (<see cref="MultiAccountPrintProjector.MayProduce"/>) at the moment the kind changes.
    ///
    /// <para>It used to be a single cached <c>IsPartyAccount</c> boolean, and that shape could not survive the
    /// rule growing a second answer: a reminder letter is receivable-side only while a confirmation of accounts
    /// is either side, so "is this row offered?" has a different answer per kind. One boolean cannot carry two
    /// answers. Holding the ledger keeps the panel and <see cref="MultiAccountPrintProjector.Project"/>
    /// asking literally the same predicate, so the two gates cannot drift.</para>
    /// </summary>
    public DomainLedger Ledger { get; }

    /// <summary>Whether this account is in the print job. Space toggles it; the panel drives Select All / None.</summary>
    [ObservableProperty] private bool _isSelected;

    public MultiAccountRowViewModel(DomainLedger ledger, string groupName)
    {
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        LedgerId = ledger.Id;
        Name = ledger.Name ?? string.Empty;
        GroupName = groupName ?? string.Empty;
    }
}

/// <summary>
/// The keyboard-first <b>Multi-Account Printing</b> panel (W2-32 / census 12.6), hosted as its own page column
/// under <b>Reports → Statements of Accounts</b>.
///
/// <para><b>THE ROUTE, NAMED SO IT CAN BE CHECKED</b> — every link of it exists, and
/// <c>MultiAccountPrintReachabilityTests</c> walks it from the menu rather than constructing this type:
/// <list type="number">
///   <item><c>MainWindowViewModel.BuildStatementsOfAccountsColumn()</c> carries the "Multi-Account Printing"
///     page item (nested under its parent section — never a flat dump);</item>
///   <item>the menu dispatch calls <c>MainWindowViewModel.OpenMultiAccountPrint()</c>;</item>
///   <item>that sets <c>Screen.MultiAccountPrint</c> and the <c>MultiAccountPrint</c> shell member;</item>
///   <item><c>MainWindow.axaml</c> holds the <c>DataTemplate</c> bound to this type — a real
///     <c>CheckBox</c> per account, so <see cref="MultiAccountRowViewModel.IsSelected"/> is reachable;</item>
///   <item><c>Ctrl+A</c> (and the Print button) reach <c>MainWindowViewModel.PrintMultiAccountJob()</c>, which
///     hands <see cref="BuildJob"/>'s document SET to
///     <c>PrintPreviewViewModel(IReadOnlyList&lt;PrintReport&gt;, string)</c> →
///     <c>ReportPdf.Render</c>'s multi-document overload.</item>
/// </list></para>
///
/// <para>🔴 <b>THE HISTORY IS KEPT BECAUSE IT IS THE LESSON.</b> This type and
/// <see cref="MultiAccountPrintProjector"/> first shipped as ~432 lines with <b>zero references</b> — no shell
/// member, no menu route, no template, no test — and the row was rightly REFUSED and filed as <c>T2-40</c>. It
/// was the third instance of this project's most repeated defect (<c>CompanyStorage.Rename()</c>,
/// <c>CostReports.BuildLedgerBreakup</c>): careful, correct-looking, unreachable code counted as delivered. The
/// missing link was small and specific — there was no way to get a document SET into a print preview, so the
/// panel had nobody to hand its job to. <b>A projection with no opener is not a feature.</b></para>
///
/// <para>🔴 <b>WHAT THIS PANEL DOES NOT CLOSE.</b> Naming a census row records what code is FOR; it never moves
/// the row, and two halves of the two rows named above are still absent:
/// <list type="bullet">
///   <item><b>12.6's multi-VOUCHER (range) printing.</b> This panel iterates ACCOUNTS. Nothing in the product
///     iterates a set of vouchers into one job — "print vouchers 10 to 25" has no route. (W2-31's F10 page range
///     is row 12.4's range of SHEETS and is not this.)</item>
///   <item><b>12.7's DELIVERY CHALLAN</b> — the Delivery Note voucher printed — and the <c>Alt+P</c> / <c>Alt+E</c>
///     bulk menus the reference product reaches the reminder letter and the confirmation of accounts from
///     (<c>T2-20</c>, the shared menu shell upstream of nine rows). This panel prints; it has no export arm.</item>
/// </list>
/// Two of 12.7's three documents now have a route, and 12.6's account half is done. Both rows are
/// <b>PARTIAL</b>.</para>
///
/// <para>No clock: the "as at" date is supplied by the shell, so the panel stays deterministic in tests.</para>
/// </summary>
public sealed partial class MultiAccountPrintViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _asOf;

    public string Title => "Multi-Account Printing";

    /// <summary>
    /// The accounts currently OFFERED, in name order, each selectable — the set the panel's list binds to.
    ///
    /// <para>🔴 <b>It is not always every account, and that is the point.</b> The rule is
    /// <see cref="MultiAccountPrintProjector.MayProduce"/>, asked here and again inside
    /// <see cref="MultiAccountPrintProjector.Project"/>: for
    /// <see cref="MultiAccountDocumentKind.LedgerAccount"/> it is every account, because a statement is meaningful
    /// for any of them; for the confirmation of accounts it is the PARTY accounts; and for the reminder letter,
    /// whose text asks for settlement, it is the RECEIVABLE side only. The panel previously offered all thirteen
    /// accounts of a demo company for every kind and ticked them all on Select All, so two keystrokes produced
    /// "Reminder Letter — To: Cash", "To: Freight Income" and "To: Profit &amp; Loss A/c", each with a Total
    /// outstanding of 0.00, and the confirmation added "Confirmed by ____ Date ____" to a nominal account. That is
    /// a document the books cannot support, reachable on the DEFAULT path.</para>
    /// </summary>
    public ObservableCollection<MultiAccountRowViewModel> Accounts { get; } = new();

    /// <summary>
    /// Every account in the company, whatever the document kind — the master list <see cref="Accounts"/> is
    /// projected from. Held so that switching kind back and forth restores the wider list without rebuilding
    /// rows (and so a row's selection survives a there-and-back switch when it stays eligible).
    /// </summary>
    private readonly List<MultiAccountRowViewModel> _allAccounts = new();

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

    /// <summary>
    /// The period line the panel shows, so the operator can see what is being printed.
    ///
    /// <para>🔴 <b>Invariant culture, deliberately</b> — for the reason spelled out at
    /// <c>MultiAccountPrintProjector.Day</c>. A bare <c>ToString("dd-MM-yyyy")</c> here read the ambient calendar
    /// and showed <c>01-04-2563</c> on a Thai machine and <c>08-08-1441</c> on <c>ar-SA</c>, so the panel
    /// disagreed with the paper it was about to print as well as with the books.</para>
    /// </summary>
    public string PeriodText =>
        _from.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
        + " to " + _asOf.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

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

    public MultiAccountPrintViewModel(Company company, DateOnly from, DateOnly asOf)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _from = from;
        _asOf = asOf;

        var ordered = new List<DomainLedger>(company.Ledgers);
        ordered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var l in ordered)
        {
            var row = new MultiAccountRowViewModel(l, GroupNameOf(company, l));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MultiAccountRowViewModel.IsSelected))
                    OnPropertyChanged(nameof(SelectedCount));
            };
            _allAccounts.Add(row);
        }

        RebuildOfferedAccounts();
    }

    /// <summary>
    /// Refills <see cref="Accounts"/> from <see cref="_allAccounts"/> for the current
    /// <see cref="DocumentKind"/>, and DESELECTS every row that has just left the offered set.
    ///
    /// <para>The deselection is the load-bearing half. Leaving a dropped row selected would keep it in
    /// <see cref="SelectedLedgerIds"/> and therefore in the job, so an operator who ticked Cash for a ledger
    /// statement and then switched to Reminder Letter would print a letter to Cash <b>from a row he could no
    /// longer see to untick</b> — worse than the defect this filter removes, because it would also be invisible.
    /// </para>
    /// </summary>
    private void RebuildOfferedAccounts()
    {
        Accounts.Clear();
        foreach (var row in _allAccounts)
        {
            // The SAME predicate Project applies — never a second copy of the rule.
            if (!MultiAccountPrintProjector.MayProduce(DocumentKind, _company, row.Ledger))
            {
                row.IsSelected = false;               // it is leaving the list — it must leave the job with it
                continue;
            }
            Accounts.Add(row);
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasOfferedAccounts));
        OnPropertyChanged(nameof(OfferedAccountsNote));
    }

    /// <summary>Whether any account is offered at all for the current kind.</summary>
    public bool HasOfferedAccounts => Accounts.Count > 0;

    /// <summary>
    /// The line under the list saying WHICH accounts it is showing — so an operator who switches to Reminder
    /// Letter and sees eleven of his thirteen accounts disappear is told why, rather than left to guess that the
    /// panel is broken.
    /// </summary>
    public string OfferedAccountsNote => DocumentKind switch
    {
        // The reminder letter asks for settlement, so it is offered on the RECEIVABLE side only — see
        // MultiAccountPrintProjector.IsReceivableAccount. Saying so is the whole point of this line: an operator
        // who expects his suppliers in the list is told why they are not, rather than left thinking the panel
        // has lost them.
        MultiAccountDocumentKind.ReminderLetter => Accounts.Count > 0
            ? "Receivable accounts only (Sundry Debtors) — this letter asks the recipient to settle what is owed to us."
            : "No account exists under Sundry Debtors, so there is nobody this letter could ask for payment.",
        MultiAccountDocumentKind.ConfirmationOfAccounts => Accounts.Count > 0
            ? "Party accounts only (Sundry Debtors / Sundry Creditors) — this document is addressed to a counterparty."
            : "No party account exists (Sundry Debtors / Sundry Creditors), so there is nobody to address this document to.",
        _ => "All accounts.",
    };

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
        // The offered set depends on the kind — see RebuildOfferedAccounts.
        RebuildOfferedAccounts();
    }

    /// <summary>
    /// Puts every OFFERED account in the job — the ones on screen, never the hidden non-party rows. Two
    /// keystrokes (switch to Reminder Letter, Select All) must not be able to produce a letter to Sales Account.
    /// </summary>
    public void SelectAll() { foreach (var a in Accounts) a.IsSelected = true; }

    /// <summary>Takes every account out of the job.</summary>
    public void SelectNone() { foreach (var a in Accounts) a.IsSelected = false; }

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
            // Distinguish "you have not chosen" from "there is nothing to choose": on a company with no debtor
            // or creditor the Reminder Letter list is EMPTY, and telling that operator to "select at least one
            // account" would send him looking for a row that does not exist.
            Status = HasOfferedAccounts
                ? "Select at least one account to print."
                : OfferedAccountsNote;
            return Array.Empty<PrintReport>();
        }

        var documents = MultiAccountPrintProjector.Project(_company, ids, DocumentKind, _from, _asOf);
        Status = documents.Count == 1
            ? "1 account ready to print."
            : $"{documents.Count:#,0} accounts ready to print.";
        return documents;
    }
}
