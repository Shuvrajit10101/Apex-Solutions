using System;
using System.IO;
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
///
/// <para><b>With ONE addition it does own: the restored company is checked before the panel calls it a
/// success.</b> <see cref="CompanyBackup"/> validates the ARCHIVE (checksum, integrity, data-format stamp)
/// and nothing about the company row inside it, and a file-level swap is the one desktop write that does not
/// pass through <c>CompanyStorage.Save</c>'s validation floor. So <see cref="Apply"/> takes a pre-restore
/// copy of the target, and afterwards either rolls it back (the archive holds a company this build cannot
/// open) or reports (the archive holds one it can open but could not save). The reasoning for treating those
/// two cases differently is written at the call site.</para>
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

        // 🔴 THE ONE DESKTOP WRITE THAT DOES NOT PASS THROUGH CompanyStorage.Save — so its guard does not
        // apply here, and this is where that gap is closed. CompanyBackup.Restore is a FILE-LEVEL swap of the
        // .db: it verifies the archive's checksum, integrity and data-format stamp, but nothing looks at the
        // company row INSIDE it. An archive holding a company the save floor would refuse therefore used to
        // land on disk unchecked, and the two failures are not the same shape:
        //   • a company that cannot be LOADED at all (books-begin before the year start — the aggregate is
        //     rebuilt through Company's constructor) leaves the user with an unopenable book AND no way back,
        //     because the file it replaced is gone. That is rolled back, from the safety copy taken below.
        //   • a company that loads but carries a value Save refuses (a bad PIN from a build that predates the
        //     floor) is KEPT — refusing to restore it would deny disaster recovery to the exact book that
        //     needs it — and reported, so the operator learns it must be corrected before the next save
        //     rather than meeting it as an exception on an unrelated screen.
        var safety = TargetPath + ".apex-prerestore";
        SafeDelete(safety);
        var haveSafety = false;
        try
        {
            if (File.Exists(TargetPath))
            {
                File.Copy(TargetPath, safety, overwrite: true);
                haveSafety = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No safety copy is a reason to say so, not a reason to refuse: CompanyBackup.Restore's own
            // staging means a refusal still leaves the target untouched. Only the post-swap rollback is lost.
            haveSafety = false;
        }

        try
        {
            var manifest = CompanyBackup.Restore(FilePath, TargetPath);

            // Reopen the restored file so the shell is showing the restored figures, not the replaced ones.
            Company reloaded;
            try
            {
                reloaded = _storage.Load(new CompanyEntry(TargetCompanyName, TargetPath));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                var rolledBack = RollBack(safety, haveSafety);
                Status = $"'{System.IO.Path.GetFileName(FilePath)}' holds a company this build cannot open "
                       + $"({ex.Message}) "
                       + (rolledBack
                            ? $"'{TargetCompanyName}' has been put back exactly as it was."
                            : $"⚠ '{TargetCompanyName}' could NOT be put back — its database is now the "
                              + "archive's, and it cannot be opened.");
                return false;
            }

            Succeeded = true;
            Status = $"Restored '{manifest.CompanyName}' (taken {BackupTakenAt}, data format " +
                     $"v{manifest.SchemaVersion}) over '{TargetCompanyName}'.";

            // The company opened, but it may still carry a header value CompanyStorage.Save refuses. Say so
            // here rather than letting the next save on any screen throw.
            try
            {
                reloaded.EnsureValid();
            }
            catch (ArgumentException ex)
            {
                Status += " ⚠ The restored company carries a header value this build refuses to save: "
                        + ex.Message
                        + " Correct it in Company Alteration before saving anything on this book.";
            }

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
        finally
        {
            SafeDelete(safety);
        }
    }

    /// <summary>
    /// Puts the pre-restore copy of the target database back over the file the restore just wrote. Returns
    /// true only when the target really is the original again — the caller words its refusal on the answer,
    /// because "nothing has been changed" is a promise and must not be made when it cannot be kept.
    /// </summary>
    private bool RollBack(string safety, bool haveSafety)
    {
        if (!haveSafety || !File.Exists(safety)) return false;
        try
        {
            // The failed Load left a pooled handle on the target; Windows will not replace an open file.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Move(safety, TargetPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort — a locked temp file is cleaned up on the next run */ }
    }
}
