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

    /// <summary>Whether this account is in the print job. Space toggles it; the panel drives Select All / None.</summary>
    [ObservableProperty] private bool _isSelected;

    public MultiAccountRowViewModel(Guid ledgerId, string name, string groupName)
    {
        LedgerId = ledgerId;
        Name = name ?? string.Empty;
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
/// <para>No clock: the "as at" date is supplied by the shell, so the panel stays deterministic in tests.</para>
/// </summary>
public sealed partial class MultiAccountPrintViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _asOf;

    public string Title => "Multi-Account Printing";

    /// <summary>Every account in the company, in name order, each selectable.</summary>
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
            var row = new MultiAccountRowViewModel(l.Id, l.Name, GroupNameOf(company, l));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MultiAccountRowViewModel.IsSelected))
                    OnPropertyChanged(nameof(SelectedCount));
            };
            Accounts.Add(row);
        }
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
    }

    /// <summary>Puts every account in the job.</summary>
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
