using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One selectable choice in the panel's Voucher Type or Party list. A null <see cref="Id"/> is the
/// "all of them" row, which is a real choice rather than the absence of one.
/// </summary>
public sealed class MultiVoucherChoice
{
    public Guid? Id { get; }
    public string Name { get; }

    public MultiVoucherChoice(Guid? id, string name)
    {
        Id = id;
        Name = name ?? string.Empty;
    }

    public override string ToString() => Name;
}

/// <summary>
/// The <b>Multi-Voucher Printing</b> panel (census row 12.6, the multi-VOUCHER half): choose a voucher type, a
/// party and a period, and print every voucher in that RANGE as one collated job.
///
/// <para><b>Vendor grounding, quoted by content</b> — the three selection axes are not ours:</para>
/// <list type="bullet">
///   <item><b>Voucher type.</b> <i>"Press <b>Alt+P</b> (Print) &gt; <b>Others</b> &gt; select
///   <b>Multi-Voucher</b> &gt; press Enter"</i> (<c>help.tallysolutions.com/faq-e-way-bill-tally/</c>), then
///   <i>"Press <b>C</b> (Configure) &gt; type or select <b>Voucher Type</b> and press Enter."</i>
///   (<c>help.tallysolutions.com/gcc-sales-and-print-fta-tally/</c>, which also gives the screen its name:
///   <i>"Type or select <b>Multi-Voucher Printing</b> &gt; and press Enter"</i>).</item>
///   <item><b>Party and period.</b> <i>"You can also print, export, or e-mail multi-voucher reports for a
///   specific party and period."</i>
///   (<c>help.tallysolutions.com/tally-prime/release-notes-tally-prime/release-notes-tallyprime-2-1/</c>).</item>
/// </list>
///
/// <para>🔴 <b>WHAT THIS PANEL IS FOR, IN ONE LINE.</b> Row 12.6's multi-ACCOUNT half shipped in PR #56 and
/// its cell has said ever since that <i>"the multi-voucher (range) half of this row is not addressed — the
/// range knob selects sheets within a job, not a range of vouchers to print"</i>. That distinction is exact and
/// is preserved here: the F10 page range is row 12.4's range of SHEETS; this panel is row 12.6's range of
/// VOUCHERS. The two compose — a 40-voucher job can still be printed as sheets 3 to 7 — but neither is the
/// other, and counting the page range for this row would be a false close.</para>
///
/// <para><b>It holds no print logic</b> (ER-12): it picks a type, a party and a period, and hands the chosen
/// set to <see cref="MultiVoucherPrintProjector"/>, whose documents go to the ordinary
/// <c>PrintKind.ReportSet</c> preview that multi-account printing already uses.</para>
///
/// <para><b>No clock.</b> The opening period is supplied by the shell, so the panel is deterministic in
/// tests.</para>
/// </summary>
public sealed partial class MultiVoucherPrintViewModel : ViewModelBase
{
    private readonly Company _company;

    public string Title => MultiVoucherPrintProjector.JobTitle;

    /// <summary>The voucher types on offer, "All Voucher Types" first.</summary>
    public ObservableCollection<MultiVoucherChoice> VoucherTypes { get; } = new();

    /// <summary>The parties on offer, "All Parties" first.</summary>
    public ObservableCollection<MultiVoucherChoice> Parties { get; } = new();

    /// <summary>The vouchers the current selection covers, in print order — what the job would print.</summary>
    public ObservableCollection<MultiVoucherRowViewModel> Matches { get; } = new();

    [ObservableProperty] private MultiVoucherChoice? _selectedVoucherType;
    [ObservableProperty] private MultiVoucherChoice? _selectedParty;
    [ObservableProperty] private string _periodFromText = string.Empty;
    [ObservableProperty] private string _periodToText = string.Empty;

    /// <summary>A status line: how many vouchers are in the range, or why none are.</summary>
    [ObservableProperty] private string _status = string.Empty;

    public MultiVoucherPrintViewModel(Company company, DateOnly from, DateOnly to)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        VoucherTypes.Add(new MultiVoucherChoice(null, "All Voucher Types"));
        var types = new List<VoucherType>(company.VoucherTypes);
        types.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var t in types) VoucherTypes.Add(new MultiVoucherChoice(t.Id, t.Name));

        // 🔴 PARTY accounts only, and the predicate is the one multi-ACCOUNT printing already uses rather than a
        // second copy of the rule. Offering "Profit & Loss A/c" in a party filter would be a dropdown of
        // thirteen names of which eleven can never match a voucher's PartyId — a list that looks like a feature
        // and behaves like a dead field.
        Parties.Add(new MultiVoucherChoice(null, "All Parties"));
        var parties = new List<DomainLedger>();
        foreach (var l in company.Ledgers)
            if (MultiAccountPrintProjector.IsPartyAccount(company, l)) parties.Add(l);
        parties.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var p in parties) Parties.Add(new MultiVoucherChoice(p.Id, p.Name));

        _selectedVoucherType = VoucherTypes[0];
        _selectedParty = Parties[0];
        _periodFromText = ApexDate.Format(from);
        _periodToText = ApexDate.Format(to);

        Refresh();
    }

    partial void OnSelectedVoucherTypeChanged(MultiVoucherChoice? value) => Refresh();
    partial void OnSelectedPartyChanged(MultiVoucherChoice? value) => Refresh();
    partial void OnPeriodFromTextChanged(string value) => Refresh();
    partial void OnPeriodToTextChanged(string value) => Refresh();

    /// <summary>How many vouchers the job would print.</summary>
    public int MatchCount => Matches.Count;

    /// <summary>Whether there is anything to print at all.</summary>
    public bool HasMatches => Matches.Count > 0;

    /// <summary>
    /// Re-runs the selection and refills <see cref="Matches"/>. Every refusal sets <see cref="Status"/> and
    /// leaves the list EMPTY, so the Print button can never arm over a range the panel could not parse.
    /// </summary>
    private void Refresh()
    {
        Matches.Clear();

        if (!ApexDate.TryParse(PeriodFromText, out var from))
        {
            Status = ApexDate.ErrorFor(PeriodFromText);
            NotifyCounts();
            return;
        }
        if (!ApexDate.TryParse(PeriodToText, out var to))
        {
            Status = ApexDate.ErrorFor(PeriodToText);
            NotifyCounts();
            return;
        }
        // 🔴 An inverted range is REFUSED, not silently swapped. Swapping would print a batch the operator did
        // not ask for while telling him he got what he typed; saying so lets him fix the field he got wrong.
        if (to < from)
        {
            Status = $"The 'to' date ({ApexDate.Format(to)}) is before the 'from' date ({ApexDate.Format(from)}).";
            NotifyCounts();
            return;
        }

        var chosen = MultiVoucherPrintProjector.Select(
            _company, SelectedVoucherType?.Id, SelectedParty?.Id, from, to);

        foreach (var v in chosen)
            Matches.Add(new MultiVoucherRowViewModel(
                _company.FormatVoucherNumber(v),
                _company.FindVoucherType(v.TypeId)?.Name ?? string.Empty,
                ApexDate.Format(v.Date),
                v.PartyId is Guid pid ? _company.FindLedger(pid)?.Name ?? string.Empty : string.Empty,
                v.Cancelled));

        Status = Matches.Count == 0
            ? "No vouchers in this range. Nothing to print."
            : $"{Matches.Count} voucher(s) from {ApexDate.Format(from)} to {ApexDate.Format(to)}.";

        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(HasMatches));
    }

    /// <summary>
    /// The document SET this panel would print — one document per voucher in the range, in print order.
    /// Returns an EMPTY list (and leaves <see cref="Status"/> saying why) when the range holds nothing, so the
    /// caller never opens a preview over a blank sheet.
    /// </summary>
    public IReadOnlyList<PrintReport> BuildJob()
    {
        if (!ApexDate.TryParse(PeriodFromText, out var from) ||
            !ApexDate.TryParse(PeriodToText, out var to) ||
            to < from)
        {
            // Status already carries the parse/ordering refusal from Refresh.
            return Array.Empty<PrintReport>();
        }

        var chosen = MultiVoucherPrintProjector.Select(
            _company, SelectedVoucherType?.Id, SelectedParty?.Id, from, to);

        if (chosen.Count == 0)
        {
            Status = "No vouchers in this range. Nothing to print.";
            return Array.Empty<PrintReport>();
        }

        return MultiVoucherPrintProjector.Project(_company, chosen);
    }
}

/// <summary>One line of the panel's match list — what the operator is about to print, before he prints it.</summary>
public sealed class MultiVoucherRowViewModel
{
    public string Number { get; }
    public string VoucherTypeName { get; }
    public string DateText { get; }
    public string PartyName { get; }
    public bool IsCancelled { get; }

    public MultiVoucherRowViewModel(
        string number, string voucherTypeName, string dateText, string partyName, bool isCancelled)
    {
        Number = number ?? string.Empty;
        VoucherTypeName = voucherTypeName ?? string.Empty;
        DateText = dateText ?? string.Empty;
        PartyName = partyName ?? string.Empty;
        IsCancelled = isCancelled;
    }
}
