using System;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Persistence.Sqlite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first <b>"Restore Company"</b> panel (Gateway → Data → Backup / Restore → Restore Company): puts a
/// <c>.apexbak</c> archive back over a company's database.
///
/// <para>Restore is the one genuinely destructive operation in the product — it replaces a whole company file —
/// so the panel is deliberately two-step (NFR-8, "no destructive op without confirmation"):</para>
/// <list type="number">
/// <item><b>Examine</b> reads the archive's manifest <i>without touching anything</i> and shows what is inside:
/// which company, when it was taken, and which data format it holds. If this build cannot handle that data
/// format, the panel says so here and <see cref="CanRestore"/> stays false — the Restore button never arms.</item>
/// <item><b>Restore</b> requires <see cref="Confirmed"/> to have been ticked, then replaces the target company's
/// database and reopens the restored company. Every validation inside <see cref="CompanyBackup.Restore"/> runs
/// against a staging file first, so a refusal leaves the existing company byte-identical to what it was.</item>
/// </list>
///
/// <para>Thin layer only (ER-12): it picks the archive + the target company and calls
/// <see cref="CompanyBackup.ReadManifest"/> / <see cref="CompanyBackup.Restore"/>. It holds no restore logic.</para>
/// </summary>
public sealed partial class RestoreCompanyViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Action<Company>? _onRestored;

    public string Title => "Restore Company";

    /// <summary>The company whose database will be replaced (its name), shown under the panel heading.</summary>
    public string DocumentTitle => TargetCompanyName;

    /// <summary>The data format (schema version) this build can read up to.</summary>
    public int SupportedDataFormatVersion => Schema.CurrentVersion;

    /// <summary>The archive to restore from (an absolute path typed/pasted into the panel).</summary>
    [ObservableProperty] private string _filePath = string.Empty;

    /// <summary>The company whose <c>.db</c> the archive will be written over.</summary>
    [ObservableProperty] private string _targetCompanyName = string.Empty;

    /// <summary>The user's explicit acknowledgement that the target company will be overwritten.</summary>
    [ObservableProperty] private bool _confirmed;

    /// <summary>A status line: what the examined archive holds, the refusal reason, or the success summary.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True once a manifest has been read successfully and this build can handle its data format.</summary>
    [ObservableProperty] private bool _canRestore;

    /// <summary>True once a restore has completed in this panel session.</summary>
    [ObservableProperty] private bool _succeeded;

    /// <summary>The examined archive's company name, or empty before Examine.</summary>
    [ObservableProperty] private string _backupCompanyName = string.Empty;

    /// <summary>The examined archive's timestamp rendered for display, or empty before Examine.</summary>
    [ObservableProperty] private string _backupTakenAt = string.Empty;

    /// <summary>The examined archive's data-format (schema) version, or 0 before Examine.</summary>
    [ObservableProperty] private int _backupDataFormatVersion;

    /// <summary>Shell ctor: target the open company and notify the shell when the restore lands.</summary>
    public RestoreCompanyViewModel(Company company, CompanyStorage storage, Action<Company>? onRestored)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onRestored = onRestored;
        TargetCompanyName = company?.Name ?? string.Empty;
    }

    partial void OnTargetCompanyNameChanged(string value)
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(TargetPath));
    }

    partial void OnFilePathChanged(string value)
    {
        // Any change of source invalidates a previous Examine — the button must be re-armed deliberately.
        CanRestore = false;
        Succeeded = false;
        BackupCompanyName = string.Empty;
        BackupTakenAt = string.Empty;
        BackupDataFormatVersion = 0;
    }

    /// <summary>The database file the restore will replace.</summary>
    public string TargetPath => string.IsNullOrWhiteSpace(TargetCompanyName)
        ? string.Empty
        : _storage.PathForName(TargetCompanyName);

    /// <summary>True when the Restore button is armed: examined, supported, and explicitly confirmed.</summary>
    public bool IsArmed => CanRestore && Confirmed;

    partial void OnCanRestoreChanged(bool value) => OnPropertyChanged(nameof(IsArmed));
    partial void OnConfirmedChanged(bool value) => OnPropertyChanged(nameof(IsArmed));

    /// <summary>
    /// Step 1 — read the archive's manifest and report what is inside. Touches nothing on disk. Sets
    /// <see cref="CanRestore"/> only when this build can handle the archive's data format.
    /// </summary>
    public bool Examine()
    {
        CanRestore = false;
        Succeeded = false;
        try
        {
            var manifest = CompanyBackup.ReadManifest(FilePath);

            BackupCompanyName = manifest.CompanyName;
            BackupTakenAt = manifest.TakenAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");
            BackupDataFormatVersion = manifest.SchemaVersion;

            if (!CompanyBackup.CanRestoreSchemaVersion(manifest.SchemaVersion))
            {
                Status = CompanyBackup.UnsupportedSchemaMessage(manifest.SchemaVersion);
                return false;
            }

            CanRestore = true;
            Status = $"Backup of '{manifest.CompanyName}' taken {BackupTakenAt} " +
                     $"(data format v{manifest.SchemaVersion}, {manifest.DatabaseBytes:#,0} bytes). " +
                     $"Restoring replaces '{TargetCompanyName}' — tick the confirmation to continue.";
            return true;
        }
        catch (CompanyBackupException ex)
        {
            Status = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Status = "Could not read the backup: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Step 2 — replace the target company's database with the archive's contents and reopen the restored
    /// company. Refuses unless <see cref="Examine"/> has passed and <see cref="Confirmed"/> is ticked.
    /// Returns true on success and sets a status line either way.
    /// </summary>
    public bool Apply()
    {
        Succeeded = false;

        if (!CanRestore)
        {
            Status = "Choose a backup file and select Examine first. Nothing has been changed.";
            return false;
        }
        if (!Confirmed)
        {
            Status = $"Restoring replaces the whole of '{TargetCompanyName}' and cannot be undone. " +
                     "Tick the confirmation to continue. Nothing has been changed.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(TargetCompanyName))
        {
            Status = "No company was selected to restore into. Nothing has been changed.";
            return false;
        }

        try
        {
            var manifest = CompanyBackup.Restore(FilePath, TargetPath);

            // Reopen the restored file so the shell is showing the restored figures, not the replaced ones.
            var reloaded = _storage.Load(new CompanyEntry(TargetCompanyName, TargetPath));

            Succeeded = true;
            Status = $"Restored '{manifest.CompanyName}' (taken {BackupTakenAt}, data format " +
                     $"v{manifest.SchemaVersion}) over '{TargetCompanyName}'.";

            _onRestored?.Invoke(reloaded);
            return true;
        }
        catch (CompanyBackupException ex)
        {
            Status = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Status = "Could not restore: " + ex.Message;
            return false;
        }
    }
}
