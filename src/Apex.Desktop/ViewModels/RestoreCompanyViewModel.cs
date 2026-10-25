using System;
using System.Collections.ObjectModel;
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

    /// <summary>
    /// The companies already on this machine, offered as restore targets. Empty on a fresh install — which is
    /// the case this panel exists for, and why the target is a free-text name rather than a pick from this list.
    /// </summary>
    public ObservableCollection<string> KnownCompanies { get; } = new();

    /// <summary>
    /// Whether <see cref="TargetCompanyName"/> names a book that is already on disk. Drives the panel's one-line
    /// warning, so the operator is told which of the two very different things is about to happen.
    ///
    /// <para><b>Vendor grounding.</b> The reference product asks the same question at the same moment:
    /// <i>"If a file with the same name already exists in the destination, you will see options to Skip or
    /// Replace"</i>, where <i>"Replace"</i> means <i>"overwrite the existing folder with the backup"</i>
    /// (<c>help.tallysolutions.com/restore-company-data/</c>). Our Skip is simply not pressing Restore.</para>
    /// </summary>
    public bool TargetExists =>
        !string.IsNullOrWhiteSpace(TargetCompanyName) && _storage.Exists(TargetCompanyName);

    /// <summary>
    /// The one line that tells the operator which of the two operations the Restore button will perform.
    /// 🔴 <b>REPLACE and CREATE are not the same act</b> and must not share a caption: one destroys a book that
    /// is on this disk, the other brings back a book that is not.
    /// </summary>
    public string TargetEffectNote =>
        string.IsNullOrWhiteSpace(TargetCompanyName)
            ? "Name the company to restore into. It need not already exist on this machine."
            : !RestoresUnderItsOwnName
                ? $"This backup holds '{BackupCompanyName}' and can only be restored as '{BackupCompanyName}'. "
                  + $"It will NOT be restored as '{TargetCompanyName}'."
                : TargetExists
                    ? $"'{TargetCompanyName}' already exists on this machine and will be REPLACED. This cannot be undone."
                    : $"'{TargetCompanyName}' does not exist on this machine and will be CREATED from the backup.";

    /// <summary>
    /// 🔴 <b>Whether the target names the SAME BOOK FILE as the company inside the examined archive.</b> True
    /// before Examine (nothing is known yet) and whenever the two names land on one <c>.db</c>.
    ///
    /// <para><b>Why a restore cannot be redirected to another NAME.</b> Every desktop write derives its file from
    /// the company's NAME (<see cref="CompanyStorage.PathForName"/>), and a restore is a file-level swap that does
    /// not rename the company row inside the archive. So restoring "Alpha Co"'s archive into
    /// "Alpha Co Copy.db" left a file holding a company called "Alpha Co"; the shell opened it, and the very next
    /// save on any screen wrote "Alpha Co.db" — deleting and re-inserting over the ORIGINAL book, which the
    /// operator never targeted and never confirmed. Measured (A12 review w6, probe P1): a book of 4 vouchers came
    /// back with 1. The vendor's own restore keeps the company's identity and asks only WHERE it goes
    /// (<i>"This is the location where your company data will be restored"</i>,
    /// <c>help.tallysolutions.com/restore-company-data/</c>), so refusing the rename takes nothing the reference
    /// product offers. Restoring as a differently-named COPY would need the restored company renamed, which is a
    /// product decision and is not taken here.</para>
    /// </summary>
    public bool RestoresUnderItsOwnName =>
        string.IsNullOrWhiteSpace(BackupCompanyName)
        || string.IsNullOrWhiteSpace(TargetCompanyName)
        || SameBookFile(BackupCompanyName, TargetCompanyName);

    /// <summary>True when the two company names land on one <c>.db</c> under this platform's own file-name rules
    /// (case-insensitive on Windows and default macOS, case-sensitive on Linux).</summary>
    private bool SameBookFile(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(_storage.PathForName(a)), Path.GetFullPath(_storage.PathForName(b)), comparison);
    }

    partial void OnBackupCompanyNameChanged(string value)
    {
        OnPropertyChanged(nameof(RestoresUnderItsOwnName));
        OnPropertyChanged(nameof(TargetEffectNote));
    }

    /// <summary>
    /// Shell ctor: target the open company and notify the shell when the restore lands.
    /// </summary>
    public RestoreCompanyViewModel(Company company, CompanyStorage storage, Action<Company>? onRestored)
        : this(storage, onRestored)
    {
        TargetCompanyName = company?.Name ?? string.Empty;
    }

    /// <summary>
    /// 🔴 <b>THE DISASTER-RECOVERY CTOR — restore with NO company open, which is the case backup exists for.</b>
    ///
    /// <para>Census row 13.2 recorded that this panel <i>"can only ever restore into the company already
    /// open"</i>, behind two independent locks: the opener returned early on a null company, and the Data menu
    /// bounced to Company Select, <i>"so on a machine with zero companies there is no route in at all"</i>
    /// (= <c>T1-7</c>, widened by <c>T1-20</c>). Both were real and both are now gone; this ctor is the half
    /// that lives here. The engine never needed changing —
    /// <see cref="CompanyBackup.Restore(string, string)"/> takes any target path, creates the directory if it
    /// is missing, and stages the swap beside the target — so the capability was always present and merely
    /// unreachable, which is the shape this project has filed against itself repeatedly.</para>
    ///
    /// <para><b>The target starts EMPTY, deliberately.</b> There is no open company to default to, and
    /// defaulting to the first name in <see cref="KnownCompanies"/> would arm a destructive replace over a book
    /// the operator never chose. <see cref="Apply"/> already refuses a blank target.</para>
    /// </summary>
    public RestoreCompanyViewModel(CompanyStorage storage, Action<Company>? onRestored)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onRestored = onRestored;

        foreach (var entry in _storage.ListCompanies())
            KnownCompanies.Add(entry.Name);
    }

    partial void OnTargetCompanyNameChanged(string value)
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(TargetExists));
        OnPropertyChanged(nameof(RestoresUnderItsOwnName));
        OnPropertyChanged(nameof(TargetEffectNote));

        // 🔴 Re-pointing the target DISARMS the confirmation. The tick means "I accept that THIS book is
        // replaced"; carrying it across a change of target would let an operator who confirmed over "Demo Co",
        // then re-pointed at "Robert Transport", press Restore and destroy a book he never confirmed. The same
        // reasoning is already applied to a change of SOURCE on OnFilePathChanged.
        Confirmed = false;
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
                     (RestoresUnderItsOwnName
                        ? $"Restoring replaces '{TargetCompanyName}' — tick the confirmation to continue."
                        : $"It restores as '{manifest.CompanyName}' only — set the company to restore into to " +
                          $"'{manifest.CompanyName}'.");
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

        // 🔴 F1 (A12 review w6) — REFUSED BEFORE ANYTHING IS TOUCHED. A restore under a name that is not the
        // archive's own writes a file whose company row names a DIFFERENT book, and the next save then lands on
        // that other book. See RestoresUnderItsOwnName. Checked here, before the swap, so the target is never even
        // copied; the row read back after the swap is checked again below, because the manifest's name is
        // informational and only the row decides where the next save goes.
        if (!RestoresUnderItsOwnName)
        {
            Status = $"This backup holds '{BackupCompanyName}' and a restore keeps that name, so it cannot be " +
                     $"restored as '{TargetCompanyName}'. Restore it as '{BackupCompanyName}'. " +
                     "Nothing has been changed.";
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
        // Captured BEFORE the swap: afterwards the file exists either way, so asking then would always answer
        // "replaced" and the operator would never be told a book had been created.
        var replacedAnExistingBook = File.Exists(TargetPath);

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
                // Inspect, not Load: Load is the OPEN verb and forgets the open vault's session, which must not
                // happen when this restore is about to be refused below and the shell stays on its book (A12
                // re-review w6, N2). The shell forgets the session itself when it switches to the restored book.
                reloaded = _storage.Inspect(new CompanyEntry(TargetCompanyName, TargetPath));
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

            // 🔴 F1, the authoritative half. The manifest's company name is informational; the company ROW just
            // loaded is what every later save derives its file from. If that row does not land on the file just
            // written (an archive whose manifest disagrees with its own database), opening it would aim the next
            // save at another book — so it is undone exactly as an unopenable archive is: the replaced book is put
            // back, or the book this restore created is removed.
            if (!SameBookFile(reloaded.Name, TargetCompanyName))
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var undone = replacedAnExistingBook ? RollBack(safety, haveSafety) : SafeDeleteCreated();
                Status = $"The company inside '{System.IO.Path.GetFileName(FilePath)}' is named '{reloaded.Name}', "
                       + $"so it cannot be restored as '{TargetCompanyName}'. "
                       + (undone
                            ? (replacedAnExistingBook
                                ? $"'{TargetCompanyName}' has been put back exactly as it was."
                                : "Nothing has been created.")
                            : $"⚠ '{TargetCompanyName}' could NOT be put back — do not open it; restore it again "
                              + "from its own backup.");
                return false;
            }

            // The restored book is put on the company register under the name it will be SAVED under — its own
            // row's name, which the check above has just proved lands on this file. (Only the display name is at
            // stake: CompanyRegistry.Load adopts any unregistered plain .db on its own, so the book would be
            // listed without this call, under its sanitised file name.)
            _storage.Registry.RegisterPlain(reloaded.Name, Path.GetFileName(TargetPath));

            Succeeded = true;
            Status = $"Restored '{manifest.CompanyName}' (taken {BackupTakenAt}, data format " +
                     $"v{manifest.SchemaVersion}) "
                   + (replacedAnExistingBook
                        ? $"over '{TargetCompanyName}', replacing it."
                        : $"as a new company, '{TargetCompanyName}'.");

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

    /// <summary>Removes the book a refused restore CREATED (there was nothing at the target before it). True when
    /// the file is gone.</summary>
    private bool SafeDeleteCreated()
    {
        try
        {
            if (File.Exists(TargetPath)) File.Delete(TargetPath);
            return !File.Exists(TargetPath);
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
