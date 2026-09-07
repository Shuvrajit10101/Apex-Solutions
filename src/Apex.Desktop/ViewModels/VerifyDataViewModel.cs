using System;
using System.Collections.ObjectModel;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Persistence.Sqlite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first <b>"Verify Data"</b> panel (Gateway → Data → Split → Verify Data): checks the open
/// company's database in place and lists what it finds.
///
/// <para><b>Vendor route (R7):</b> <i>"Press <b>Alt+Y</b> (Data) &gt; <b>Split</b> &gt; <b>Verify Data</b>,
/// select your company, and resolve any identified errors before proceeding"</i>
/// (<c>help.tallysolutions.com/split-company-data-tally/</c>). <b>The divergence, labelled as ours:</b> the
/// vendor lets you pick any company from a list; this panel verifies the company that is <b>open</b>, because
/// this shell has one company loaded at a time (census 14.9's Select semantics are the open primitive, and
/// nothing here is going to invent them). The file being checked is named on the panel, so what is verified is
/// never in doubt.</para>
///
/// <para>Thin layer only (ER-12): it calls <see cref="CompanyBackup.Verify"/> and renders the result. It holds
/// no checking logic, and it <b>writes nothing</b> — the check opens the database read-only.</para>
/// </summary>
public sealed partial class VerifyDataViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;

    public string Title => "Verify Data";

    /// <summary>The company being verified, shown under the panel heading.</summary>
    public string DocumentTitle => _company.Name;

    /// <summary>The database file this check reads — shown so what is verified is never in doubt.</summary>
    public string SourcePath => _storage.PathForName(_company.Name);

    /// <summary>A status line: the clean summary, or the reason it is not clean. Empty until Verify is run.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True when the last run found nothing wrong (drives the success styling).</summary>
    [ObservableProperty] private bool _succeeded;

    /// <summary>True once a check has been run in this panel session — so "no findings" reads as a result
    /// rather than as an empty screen.</summary>
    [ObservableProperty] private bool _hasRun;

    /// <summary>One row per problem found; empty when the data is clean.</summary>
    public ObservableCollection<string> Findings { get; } = new();

    public VerifyDataViewModel(Company company, CompanyStorage storage)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Ctrl+A / the Verify button: run the check and render it. Persists the open aggregate first, for the same
    /// reason the backup panel does — verifying the file while the vouchers typed five minutes ago are still
    /// only in memory checks a file the operator does not have.
    /// </summary>
    public bool Apply()
    {
        Succeeded = false;
        Findings.Clear();
        HasRun = true;

        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (SaveFailure.IsReportable(ex))
        {
            Status = "Could not save the open company before verifying: " + ex.Message;
            Findings.Add(Status);
            return false;
        }

        var result = CompanyBackup.Verify(SourcePath);
        foreach (var finding in result.Findings) Findings.Add(finding);
        Status = result.Summary;
        Succeeded = result.Ok;
        return result.Ok;
    }
}
