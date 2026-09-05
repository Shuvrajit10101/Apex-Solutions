using System;
using System.IO;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Persistence.Sqlite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first <b>"Backup Company"</b> panel (Gateway → Data → Backup / Restore → Backup Company): writes a
/// consistent, version-stamped snapshot of the open company's database to a single <c>.apexbak</c> archive.
///
/// <para>This is <b>not</b> the Export Data panel. Export Data serialises the in-memory aggregate to canonical
/// JSON/XML for interchange; a backup captures the <i>database file itself</i>, taken through the SQLite Online
/// Backup API so it is a transactionally-consistent snapshot even if the file is being written at that instant,
/// and stamped with the schema version so a restore can refuse an archive this build cannot handle. It is the
/// mitigation <c>plan.md</c> names for its own top-ranked data-loss risk R-7.</para>
///
/// <para>Thin layer only (ER-12): the panel picks the destination folder + file name and an optional timestamp,
/// then calls <see cref="CompanyBackup.Create"/>. It holds no backup logic. The IO layer has no clock (ER-8), so
/// the timestamp is formatted here from the injected "now".</para>
/// </summary>
public sealed partial class BackupCompanyViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;

    /// <summary>The "now" the timestamp suffix and the manifest stamp derive from — injected for determinism.</summary>
    private readonly DateTime _now;

    public string Title => "Backup Company";

    /// <summary>The company being backed up (its name), shown under the panel heading.</summary>
    public string DocumentTitle => _company.Name;

    /// <summary>The database file this backup will snapshot — shown so the user knows exactly what is captured.</summary>
    public string SourcePath => _storage.PathForName(_company.Name);

    /// <summary>The data-format (schema) version this build writes, stamped into every archive it takes.</summary>
    public int DataFormatVersion => Schema.CurrentVersion;

    /// <summary>The destination folder (defaults to the user's Documents folder in the shell).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>The base file name without extension (defaults to the company name).</summary>
    [ObservableProperty] private string _fileName = "Company";

    /// <summary>Append the current timestamp to the file name (<c>Name_yyyyMMdd-HHmm.apexbak</c>). ON by default —
    /// a backup that silently overwrites yesterday's backup is one bad day away from being no backup at all.</summary>
    [ObservableProperty] private bool _appendTimestamp = true;

    /// <summary>A status line shown after Apply (success path + size, or the failure reason).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True once a backup has been written in this panel session (drives the success styling).</summary>
    [ObservableProperty] private bool _succeeded;

    /// <summary>Shell ctor: default the folder to Documents and the name to the company.</summary>
    public BackupCompanyViewModel(Company company, CompanyStorage storage)
        : this(company, storage, DefaultFolder(), DateTime.Now) { }

    /// <summary>Testable ctor: inject the destination folder and the "now" used for the timestamp + stamp.</summary>
    public BackupCompanyViewModel(Company company, CompanyStorage storage, string folder, DateTime now)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _now = now;
        Folder = folder ?? string.Empty;
        FileName = SafeName(company.Name);
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(ResolvedFileName));
        OnPropertyChanged(nameof(FullPath));
    }

    partial void OnFileNameChanged(string value) => RaiseDerived();
    partial void OnAppendTimestampChanged(bool value) => RaiseDerived();
    partial void OnFolderChanged(string value) => RaiseDerived();

    /// <summary>The final file name the backup will write (<c>Name[_timestamp].apexbak</c>), for the panel hint.</summary>
    public string ResolvedFileName
    {
        get
        {
            var stem = string.IsNullOrWhiteSpace(FileName) ? SafeName(_company.Name) : FileName.Trim();
            if (AppendTimestamp) stem += "_" + _now.ToString("yyyyMMdd-HHmm");
            return stem + CompanyBackup.ArchiveExtension;
        }
    }

    /// <summary>The absolute path the backup will write to.</summary>
    public string FullPath => Path.Combine(Folder ?? string.Empty, ResolvedFileName);

    /// <summary>
    /// Takes the backup. Persists the open aggregate first, so the snapshot carries everything entered up to this
    /// moment rather than whatever happened to be flushed, then hands off to <see cref="CompanyBackup.Create"/>.
    /// Returns true on success and sets a status line either way.
    /// </summary>
    public bool Apply()
    {
        Succeeded = false;
        try
        {
            // Flush the in-memory aggregate before snapshotting: a backup of the file that omits the vouchers the
            // user typed five minutes ago is a trap, not a safety net.
            _storage.Save(_company);

            var manifest = CompanyBackup.Create(SourcePath, FullPath, new DateTimeOffset(_now));

            Succeeded = true;
            Status = $"Backed up {manifest.CompanyName} — {manifest.DatabaseBytes:#,0} bytes, " +
                     $"data format v{manifest.SchemaVersion} — to {FullPath}";
            return true;
        }
        catch (CompanyBackupException ex)
        {
            Status = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Status = "Could not back up: " + ex.Message;
            return false;
        }
    }

    private static string DefaultFolder()
    {
        // W2-03 bonus fix: ONE seam, so the empty-string failure mode cannot be re-introduced per screen.
        // Environment.GetFolderPath returns "" when the platform has no such folder (a Linux CI container
        // with no HOME), and an empty folder makes Path.Combine collapse to a bare file name - the file
        // lands in the process working directory, unfindable. See Services.ExportFolderDefault.
        return Apex.Desktop.Services.ExportFolderDefault.Resolve();
    }

    /// <summary>Turns a company name into a safe file-name stem (invalid path chars → '_'; blank → "Company").</summary>
    private static string SafeName(string? name)
    {
        var stem = string.IsNullOrWhiteSpace(name) ? "Company" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }
}
