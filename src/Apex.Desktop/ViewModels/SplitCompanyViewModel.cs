using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first <b>"Split Data"</b> panel (Gateway → Data → Split → Split Data): writes ONE or TWO new
/// companies from the open company's book, and leaves the open company exactly as it was.
///
/// <para><b>Vendor route and options (R7)</b> — <c>help.tallysolutions.com/split-company-data-tally/</c>:
/// <i>"Press <b>Alt+Y</b> (Data) &gt; <b>Split</b> &gt; <b>Split Data</b>"</i>; <i>"From Split Date"</i> —
/// <i>"a new company with data from the split date to the last entry date"</i>; <i>"Before Split Date"</i> —
/// <i>"from the first date of entry in the company till one day before the split date"</i>;
/// <i>"Into Two Companies"</i>; and <i>"After splitting your company, the original company will remain as it
/// is."</i></para>
///
/// <para>🔴 <b>THIS PANEL NEVER HANDS THE OPEN AGGREGATE TO THE SPLIT.</b> It passes the company's
/// <see cref="CompanyEntry"/> — a name and a file path — to <see cref="CompanySplitService"/>, which loads its
/// own copies. The engine mutates what it is given, so the open book is kept out of its reach by construction
/// rather than by care.</para>
///
/// <para>Thin layer only (ER-12): parse the date, collect the two names, show the refusals, call the service.</para>
/// </summary>
public sealed partial class SplitCompanyViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly CompanySplitService _service;
    private readonly Action<CompanyEntry>? _onSplit;

    public string Title => "Split Data";

    /// <summary>The company being split, shown under the panel heading.</summary>
    public string DocumentTitle => _company.Name;

    /// <summary>The database file the split reads from — and never writes to.</summary>
    public string SourcePath => _storage.PathForName(_company.Name);

    /// <summary>The split date, day-first (<c>dd-MMM-yyyy</c> and the other forms <c>ApexDate</c> accepts).</summary>
    [ObservableProperty] private string _splitDateText = string.Empty;

    /// <summary>Vendor option 1 — "Before Split Date". One new company, the earlier half.</summary>
    [ObservableProperty] private bool _beforeSplitDate;

    /// <summary>Vendor option 2 — "From Split Date". One new company, the later half.</summary>
    [ObservableProperty] private bool _fromSplitDate;

    /// <summary>Vendor option 3 — "Into Two Companies". Both halves; the default, because it is the only option
    /// that loses no period from the pair.</summary>
    [ObservableProperty] private bool _intoTwoCompanies = true;

    /// <summary>The name for the "before split date" company.</summary>
    [ObservableProperty] private string _beforeName = string.Empty;

    /// <summary>The name for the "from split date" company.</summary>
    [ObservableProperty] private string _fromName = string.Empty;

    /// <summary>A status line after Apply (what was written, or why nothing was).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True once a split has completed in this panel session (drives the success styling).</summary>
    [ObservableProperty] private bool _succeeded;

    /// <summary>One row per refusal, shown before anything is written.</summary>
    public ObservableCollection<string> Refusals { get; } = new();

    /// <summary>The books this panel wrote, in the order they were written.</summary>
    public ObservableCollection<string> Created { get; } = new();

    public SplitCompanyViewModel(Company company, CompanyStorage storage, Action<CompanyEntry>? onSplit = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _service = new CompanySplitService(_storage);
        _onSplit = onSplit;

        // Default the split date to the start of the company's financial year — the boundary the vendor's own
        // "Split Data based on Financial Years" framing is built around — and leave it fully editable.
        SplitDateText = ApexDate.Format(_company.FinancialYearStart);
        RefreshDefaultNames();
    }

    /// <summary>Which of the vendor's three options is selected.</summary>
    public CompanySplitMode Mode =>
        BeforeSplitDate ? CompanySplitMode.BeforeSplitDate
        : FromSplitDate ? CompanySplitMode.FromSplitDate
        : CompanySplitMode.IntoTwoCompanies;

    /// <summary>True while the selected option writes a "before split date" book (so the field is relevant).</summary>
    public bool NeedsBeforeName => Mode is CompanySplitMode.BeforeSplitDate or CompanySplitMode.IntoTwoCompanies;

    /// <summary>True while the selected option writes a "from split date" book.</summary>
    public bool NeedsFromName => Mode is CompanySplitMode.FromSplitDate or CompanySplitMode.IntoTwoCompanies;

    partial void OnBeforeSplitDateChanged(bool value) { if (value) { FromSplitDate = false; IntoTwoCompanies = false; } RaiseMode(); }
    partial void OnFromSplitDateChanged(bool value) { if (value) { BeforeSplitDate = false; IntoTwoCompanies = false; } RaiseMode(); }
    partial void OnIntoTwoCompaniesChanged(bool value) { if (value) { BeforeSplitDate = false; FromSplitDate = false; } RaiseMode(); }
    partial void OnSplitDateTextChanged(string value) => RefreshDefaultNames();

    private void RaiseMode()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(NeedsBeforeName));
        OnPropertyChanged(nameof(NeedsFromName));
    }

    /// <summary>
    /// Re-derives the two default names from the split date — but only while the operator has not typed their
    /// own. A name the user edited is never overwritten by a later date change.
    /// </summary>
    private void RefreshDefaultNames()
    {
        if (!ApexDate.TryParse(SplitDateText, out var date)) return;

        var before = CompanySplitService.DefaultBeforeName(_company.Name, date);
        var from = CompanySplitService.DefaultFromName(_company.Name, date);

        if (string.IsNullOrWhiteSpace(BeforeName) || LooksGenerated(BeforeName, before: true)) BeforeName = before;
        if (string.IsNullOrWhiteSpace(FromName) || LooksGenerated(FromName, before: false)) FromName = from;
    }

    private bool LooksGenerated(string name, bool before) =>
        name.StartsWith(_company.Name + (before ? " (to " : " (from "), StringComparison.Ordinal)
        && name.EndsWith(")", StringComparison.Ordinal);

    /// <summary>The refusals as they stand right now — what the panel shows before the operator commits.</summary>
    public bool Validate()
    {
        Refusals.Clear();
        if (!ApexDate.TryParse(SplitDateText, out var date))
        {
            Refusals.Add($"'{SplitDateText}' is not a date. Use the {ApexDate.Canonical} form.");
            return false;
        }

        foreach (var reason in _service.Check(Entry(), date, Mode, BeforeName, FromName))
            Refusals.Add(reason);
        return Refusals.Count == 0;
    }

    /// <summary>
    /// Ctrl+A / the Split button: validate, then write. On success the new books exist and the open company is
    /// untouched — this panel deliberately does NOT switch to either of them, because the operator is standing
    /// in the source book and silently moving them out of it is the one thing a split must not do.
    /// </summary>
    public bool Apply()
    {
        Succeeded = false;
        Created.Clear();

        if (!Validate())
        {
            Status = Refusals.Count == 1 ? Refusals[0] : $"{Refusals.Count} problems must be resolved first.";
            return false;
        }

        ApexDate.TryParse(SplitDateText, out var splitDate);   // Validate() has already proved this parses
        var outcome = _service.Split(Entry(), splitDate, Mode, BeforeName, FromName);
        foreach (var entry in outcome.Created)
        {
            Created.Add(entry.Name);
            _onSplit?.Invoke(entry);
        }

        Status = outcome.Message;
        Succeeded = outcome.Ok;
        if (!outcome.Ok && Refusals.Count == 0) Refusals.Add(outcome.Message);
        return outcome.Ok;
    }

    private CompanyEntry Entry() => new(_company.Name, SourcePath);
}
