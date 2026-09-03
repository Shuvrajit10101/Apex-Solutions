using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// Raised when a backup cannot be taken, or a backup archive cannot be restored. The message is written to be
/// shown verbatim to a user — it names what is wrong and, for a version refusal, both versions involved.
/// </summary>
public sealed class CompanyBackupException : Exception
{
    public CompanyBackupException(string message) : base(message) { }
    public CompanyBackupException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// What a backup archive carries about itself: the container format, the <b>schema version</b> of the captured
/// company database, who it belongs to, when it was taken, and an integrity digest of the payload.
/// </summary>
/// <param name="FormatVersion">The archive container format (<see cref="CompanyBackup.FormatVersion"/>).</param>
/// <param name="SchemaVersion">The <c>schema_version</c> of the captured company database — the stamp a restore
/// checks against <see cref="Schema.CurrentVersion"/> before it will touch anything.</param>
/// <param name="CompanyName">The company's display name at the time of the backup (informational).</param>
/// <param name="SourceFileName">The source <c>.db</c> file name (informational).</param>
/// <param name="TakenAtUtc">When the snapshot was taken, in UTC.</param>
/// <param name="DatabaseBytes">The exact byte length of the captured database.</param>
/// <param name="DatabaseSha256">Lower-case hex SHA-256 of the captured database bytes.</param>
public sealed record BackupManifest(
    int FormatVersion,
    int SchemaVersion,
    string CompanyName,
    string SourceFileName,
    DateTimeOffset TakenAtUtc,
    long DatabaseBytes,
    string DatabaseSha256);

/// <summary>
/// <b>Backup and restore of a company database</b> — the mitigation <c>plan.md</c> names for its own top-ranked
/// data-loss risk R-7, carved out of the otherwise-excluded Phase 10.
///
/// <para><b>A backup is NOT a file copy.</b> A naive <c>File.Copy</c> of a live SQLite database can capture a
/// <i>torn</i> file: pages half-written by an in-flight transaction, with the rollback journal (or WAL) either not
/// copied at all or copied at a different instant. Such a copy is not reliably restorable — SQLite's own
/// documentation is explicit that copying a database while a transaction is active produces a corrupt copy. This
/// class therefore uses the <b>SQLite Online Backup API</b> (<c>sqlite3_backup_step</c>, surfaced by
/// Microsoft.Data.Sqlite as <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>): it copies the source
/// page-by-page while holding a read lock, and any page the source changes mid-copy is re-copied, so the destination
/// is a <b>transactionally-consistent snapshot of the source as of a single instant</b> — never a half-applied
/// transaction, and never missing committed work that is still sitting in a WAL. The snapshot is then verified with
/// <c>PRAGMA integrity_check</c> before it is archived, so a backup that is already corrupt is refused at the moment
/// it is taken rather than discovered on the day it is needed.</para>
///
/// <para><b>The archive.</b> A backup is a single ZIP file (conventionally <c>.apexbak</c>) holding exactly two
/// entries: <c>company.db</c> (the snapshot) and <c>manifest.json</c> (a <see cref="BackupManifest"/>). The manifest
/// stamps the <b>schema version</b>, so a restore can refuse — with a clear message — a backup this build cannot
/// handle, and carries a SHA-256 of the payload so a truncated or edited archive is refused rather than restored
/// over live data.</para>
///
/// <para><b>The restore is validate-then-swap.</b> Every check (container format, schema version, digest,
/// <c>integrity_check</c>, and a cross-check of the manifest's stamp against the payload's own
/// <c>schema_version</c> row) runs against a temporary file <i>beside</i> the target. The target is touched only
/// once every check has passed, and then by an atomic replace — so a refused or failed restore leaves the existing
/// company file exactly as it was (NFR-8: no destructive op without confirmation).</para>
///
/// <para><b>Nothing about the target is degraded until the replacement is in place.</b> Stale <c>-wal</c>/
/// <c>-shm</c>/<c>-journal</c> sidecars of the target must not survive the swap — a leftover hot journal belonging
/// to the <i>replaced</i> database would be rolled back into the restored one and corrupt it — but they are the
/// only thing that can recover a half-written company file, and a hot journal beside the company file is exactly
/// the state a user runs a restore FROM. So they are <b>parked aside, not deleted</b>: renamed, then the swap, and
/// only once the swap has succeeded are the parked files destroyed. If the swap throws — the target is held open
/// by a scanner, a second instance, or a leaked handle — every parked file is moved back under its own name before
/// the exception leaves. A failed restore therefore costs the user nothing at all: not the file, and not its
/// ability to recover itself.</para>
///
/// <para><b>Older backups are accepted; newer ones are refused.</b> The store migrates a database forward one
/// version at a time (<c>MigrateVNToVN+1</c>), so any archive from v1 up to <see cref="Schema.CurrentVersion"/>
/// restores and is migrated on first open. An archive stamped <i>above</i> the running build's
/// <see cref="Schema.CurrentVersion"/> was written by a newer build whose tables this one cannot read; restoring it
/// would either fail on open or, worse, silently drop data. It is refused by name and number.</para>
/// </summary>
public static class CompanyBackup
{
    /// <summary>The archive container format this build writes and can read. Bumped only if the layout changes.</summary>
    public const int FormatVersion = 1;

    /// <summary>The conventional file extension for a company backup archive (including the dot).</summary>
    public const string ArchiveExtension = ".apexbak";

    /// <summary>The ZIP entry holding the database snapshot.</summary>
    internal const string DatabaseEntryName = "company.db";

    /// <summary>The ZIP entry holding the <see cref="BackupManifest"/> as JSON.</summary>
    internal const string ManifestEntryName = "manifest.json";

    /// <summary>
    /// Takes a consistent, version-stamped backup of the company database at <paramref name="sourceDatabasePath"/>
    /// and writes it to <paramref name="backupPath"/>, overwriting any file already there — <b>except</b> a company
    /// database, which is refused (see the destination guard below). Returns the manifest that was written.
    /// </summary>
    /// <param name="sourceDatabasePath">The company <c>.db</c> to snapshot. Must exist.</param>
    /// <param name="backupPath">The archive to write. Its folder is created if missing. Must not be the source,
    /// and must not be an existing company database.</param>
    /// <param name="takenAtUtc">The timestamp to stamp into the manifest (injected — the adapter has no clock).</param>
    /// <exception cref="CompanyBackupException">The source is missing/unreadable, is not a company database, the
    /// destination is a company database (or is the source itself), or the snapshot fails its integrity check.</exception>
    public static BackupManifest Create(string sourceDatabasePath, string backupPath, DateTimeOffset takenAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabasePath))
            throw new CompanyBackupException("No company database was given to back up.");
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new CompanyBackupException("No destination was given for the backup.");
        if (!File.Exists(sourceDatabasePath))
            throw new CompanyBackupException(
                $"There is no company database at '{sourceDatabasePath}'. Nothing has been backed up.");

        // THE DESTINATION GUARD. Create ends in File.Move(partial, backupPath, overwrite: true) — it will replace
        // whatever is sitting at the destination. A destination that is itself a company database must therefore
        // be refused outright, because overwriting it destroys a company and still reports a successful backup.
        // The worst case is destination == source: the file being protected is replaced by a ZIP of itself, and
        // the caller is told the backup worked.
        if (SamePath(backupPath, sourceDatabasePath))
            throw new CompanyBackupException(
                "A backup cannot be written over the company database it is a backup of — that would destroy it. " +
                "Choose a different file. Nothing has been backed up.");
        if (LooksLikeCompanyDatabase(backupPath))
            throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is an Apex Solutions company database, not a backup file. " +
                "Writing the backup there would destroy that company. Choose a different file. " +
                "Nothing has been backed up.");

        var backupDir = Path.GetDirectoryName(Path.GetFullPath(backupPath));
        if (!string.IsNullOrEmpty(backupDir)) Directory.CreateDirectory(backupDir);

        // Both temporaries live beside the ARCHIVE (not in %TEMP%), so the final rename is a same-volume move
        // and cannot half-copy. `staging` holds the raw snapshot; `partial` holds the archive as it is built.
        var staging = Path.GetFullPath(backupPath) + ".apex-backup-tmp";
        var partial = Path.GetFullPath(backupPath) + ".apex-backup-part";
        SafeDelete(staging);
        SafeDelete(partial);

        try
        {
            int schemaVersion;
            string companyName;

            using (var source = Open(sourceDatabasePath, SqliteOpenMode.ReadWrite))
            {
                // Read the stamp + the company name FIRST: it also proves the source really is a company database
                // (a JPEG renamed .db fails here, before anything is written).
                schemaVersion = ReadSchemaVersion(source)
                    ?? throw new CompanyBackupException(
                        $"'{Path.GetFileName(sourceDatabasePath)}' is not an Apex Solutions company database " +
                        "(it carries no data-format stamp). Nothing has been backed up.");
                companyName = ReadCompanyName(source) ?? Path.GetFileNameWithoutExtension(sourceDatabasePath);

                // ---- THE MECHANISM. sqlite3_backup_step, not File.Copy. Page-by-page under a read lock, with
                // any page the source changes mid-copy re-copied, so the destination is a transactionally
                // consistent snapshot of a single instant — never a half-applied transaction.
                using var destination = Open(staging, SqliteOpenMode.ReadWriteCreate);
                source.BackupDatabase(destination);

                // Verify the snapshot before it is archived: a backup that is already corrupt must fail LOUDLY
                // now, not be discovered on the day it is needed.
                var integrity = IntegrityCheck(destination);
                if (!integrity.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                    throw new CompanyBackupException(
                        $"The snapshot failed its integrity check ({integrity}). The company database may be " +
                        "damaged; no backup has been written.");

                var snapshotVersion = ReadSchemaVersion(destination);
                if (snapshotVersion != schemaVersion)
                    throw new CompanyBackupException(
                        "The snapshot did not match the company database it was taken from. " +
                        "No backup has been written.");
            }
            SqliteConnection.ClearAllPools();

            // Hash and archive by STREAMING. A real company database is not something to hold in memory twice
            // over — a File.ReadAllBytes here would turn a large company into an OutOfMemoryException at exactly
            // the moment the user most needs the backup to work.
            string digest;
            long length;
            using (var snapshot = File.OpenRead(staging))
            {
                length = snapshot.Length;
                digest = Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
            }

            var manifest = new BackupManifest(
                FormatVersion: FormatVersion,
                SchemaVersion: schemaVersion,
                CompanyName: companyName,
                SourceFileName: Path.GetFileName(sourceDatabasePath),
                TakenAtUtc: takenAtUtc.ToUniversalTime(),
                DatabaseBytes: length,
                DatabaseSha256: digest);

            // Build the archive under a temporary name and only then put it in place. A backup that fails
            // half-way must not leave a truncated file sitting where a good one used to be — that is how a
            // yesterday's-backup-that-worked becomes a today's-backup-that-does-not.
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                using (var entry = zip.CreateEntry(ManifestEntryName, CompressionLevel.NoCompression).Open())
                    entry.Write(Encoding.UTF8.GetBytes(WriteManifestJson(manifest)));
                using (var entry = zip.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal).Open())
                using (var snapshot = File.OpenRead(staging))
                    snapshot.CopyTo(entry);
            }

            File.Move(partial, backupPath, overwrite: true);
            return manifest;
        }
        catch (CompanyBackupException) { throw; }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 /* SQLITE_BUSY */ or 6 /* SQLITE_LOCKED */)
        {
            throw new CompanyBackupException(
                "The company data file is busy — another operation is part-way through writing to it. " +
                "No backup has been written; try again in a moment. (Nothing has been changed.)", ex);
        }
        catch (SqliteException ex)
        {
            throw new CompanyBackupException(
                $"Could not read the company database: {ex.Message}. Nothing has been backed up.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            throw new CompanyBackupException($"Could not write the backup: {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            SafeDelete(staging);
            SafeDelete(partial);
        }
    }

    /// <summary>
    /// Reads (and validates the shape of) the manifest inside <paramref name="backupPath"/> without restoring
    /// anything — what the Restore screen shows before the user commits.
    /// </summary>
    /// <exception cref="CompanyBackupException">Not a readable archive, or the manifest is missing/malformed.</exception>
    public static BackupManifest ReadManifest(string backupPath)
    {
        using var zip = OpenArchive(backupPath);
        return ReadManifest(zip, backupPath);
    }

    /// <summary>
    /// Restores <paramref name="backupPath"/> over <paramref name="targetDatabasePath"/>, replacing it. Every
    /// validation runs before the target is touched, and the swap itself parks the target's <c>-wal</c>/
    /// <c>-shm</c>/<c>-journal</c> aside rather than deleting them, putting them back if the swap throws — so on
    /// <b>any</b> failure, refused or thrown, the target and its crash-recovery sidecars are left exactly as they
    /// were. Returns the manifest that was restored.
    /// </summary>
    /// <exception cref="CompanyBackupException">The archive is unreadable, corrupt, of an unknown container format,
    /// or stamped with a schema version this build cannot handle.</exception>
    public static BackupManifest Restore(string backupPath, string targetDatabasePath)
    {
        if (string.IsNullOrWhiteSpace(targetDatabasePath))
            throw new CompanyBackupException("No company database was given to restore into.");

        var target = Path.GetFullPath(targetDatabasePath);
        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        // Staged BESIDE the target, so the final swap is a same-volume rename and can never half-copy.
        var staging = target + ".apex-restore-tmp";
        SafeDelete(staging);

        try
        {
            BackupManifest manifest;
            string payloadDigest;
            long payloadLength;

            using (var zip = OpenArchive(backupPath))
            {
                manifest = ReadManifest(zip, backupPath);

                // (1) Container format — a future layout this build does not know how to unpack.
                if (manifest.FormatVersion > FormatVersion)
                    throw new CompanyBackupException(
                        $"This backup uses a newer archive format (v{manifest.FormatVersion}); this build reads " +
                        $"up to v{FormatVersion}. Update Apex Solutions, then restore again. Nothing has been changed.");
                if (manifest.FormatVersion < 1)
                    throw new CompanyBackupException(
                        $"This backup declares an invalid archive format (v{manifest.FormatVersion}). " +
                        "The archive may be damaged. Nothing has been changed.");

                // (2) THE SCHEMA STAMP — the refusal this whole manifest exists for.
                if (!CanRestoreSchemaVersion(manifest.SchemaVersion))
                    throw new CompanyBackupException(UnsupportedSchemaMessage(manifest.SchemaVersion));

                var entry = zip.GetEntry(DatabaseEntryName)
                    ?? throw new CompanyBackupException(
                        $"'{Path.GetFileName(backupPath)}' does not contain a company database. " +
                        "The archive may be damaged. Nothing has been changed.");

                // Expand STRAIGHT to the staging file, hashing as the bytes go past — never into memory. The
                // staging file sits beside the target and is deleted on every exit path, so a failure here
                // costs a temp file, not the company.
                using var stream = entry.Open();
                using var file = File.Create(staging);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[81_920];
                int read;
                payloadLength = 0;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    file.Write(buffer, 0, read);
                    payloadLength += read;
                }
                payloadDigest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            // (3) Integrity of the payload against the stamp it was archived with.
            if (payloadLength != manifest.DatabaseBytes
                || !payloadDigest.Equals(manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new CompanyBackupException(
                    $"'{Path.GetFileName(backupPath)}' is damaged — its contents do not match the checksum " +
                    "recorded when the backup was taken. Nothing has been changed.");

            // (4) The payload must open as a real database, pass integrity_check, and its OWN schema_version row
            //     must agree with the manifest's stamp. A manifest that contradicts its payload is a damaged (or
            //     hand-edited) archive and must never be restored over live data.
            using (var check = Open(staging, SqliteOpenMode.ReadWrite))
            {
                var integrity = IntegrityCheck(check);
                if (!integrity.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                    throw new CompanyBackupException(
                        $"'{Path.GetFileName(backupPath)}' is damaged — the company database inside it failed its " +
                        $"integrity check ({integrity}). Nothing has been changed.");

                var actual = ReadSchemaVersion(check);
                if (actual is null)
                    throw new CompanyBackupException(
                        $"'{Path.GetFileName(backupPath)}' is damaged — the company database inside it carries no " +
                        "data-format stamp. Nothing has been changed.");
                if (actual != manifest.SchemaVersion)
                    throw new CompanyBackupException(
                        $"'{Path.GetFileName(backupPath)}' is damaged — it says it holds data format " +
                        $"v{manifest.SchemaVersion} but actually holds v{actual}. Nothing has been changed.");
            }

            // ---- Everything has passed. ONLY NOW is the target touched. ----
            // Release every pooled handle first: Microsoft.Data.Sqlite pools connections, so a store that was
            // disposed can still be holding the target file open, and the replace would fail on Windows.
            SqliteConnection.ClearAllPools();

            // A leftover -wal/-shm/-journal beside the target belongs to the database being REPLACED. Left in
            // place, SQLite would apply it to the restored file on first open and corrupt it — so they must go.
            //
            // But they must NOT go until the swap has actually happened. Those sidecars are the ONLY thing that
            // can roll a half-written company file back, and the state a user reaches for a restore in is very
            // often exactly that: a hot journal beside a company file a crash caught mid-save. Deleting them and
            // THEN discovering the target cannot be replaced (held open by a scanner, a second instance, or a
            // leaked handle) would leave the user with no restore AND no way back — the one outcome this whole
            // feature exists to prevent. So: park them aside, swap, and only destroy them once the swap is done.
            // If anything throws, every parked file goes back under its own name before the exception leaves.
            var parked = new List<(string Original, string Parked)>();
            try
            {
                foreach (var suffix in SidecarSuffixes)
                {
                    var sidecar = target + suffix;
                    if (!File.Exists(sidecar)) continue;
                    var aside = sidecar + ParkedSuffix;
                    SafeDelete(aside);
                    File.Move(sidecar, aside, overwrite: true);
                    parked.Add((sidecar, aside));
                }

                File.Move(staging, target, overwrite: true);
            }
            catch
            {
                Unpark(parked);
                throw;
            }

            // The swap succeeded: the parked sidecars belong to a database that no longer exists here.
            foreach (var (_, aside) in parked) SafeDelete(aside);
            return manifest;
        }
        catch (CompanyBackupException) { throw; }
        catch (SqliteException ex)
        {
            throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is damaged — the company database inside it could not be " +
                $"opened ({ex.Message}). Nothing has been changed.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            throw new CompanyBackupException($"Could not restore the backup: {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            SafeDelete(staging);
        }
    }

    /// <summary>The SQLite sidecar files that belong to a database file and must not outlive a restore.</summary>
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];

    /// <summary>
    /// Suffix a target's sidecar wears while it is parked across the swap. It exists only between the park and
    /// either the swap succeeding (parked files deleted) or throwing (parked files moved back), so a file left
    /// wearing it is the fingerprint of a process killed mid-swap — and it is named to be recognisable as such.
    /// </summary>
    private const string ParkedSuffix = ".apex-restore-old";

    /// <summary>
    /// Puts every parked sidecar back under its own name. Best effort by design: this runs while an exception is
    /// already on its way out, and throwing a second one here would replace a diagnosable failure ("could not
    /// replace the company file") with an undiagnosable one. A file that cannot be moved back keeps its
    /// <see cref="ParkedSuffix"/> name, so nothing is destroyed either way.
    /// </summary>
    private static void Unpark(List<(string Original, string Parked)> parked)
    {
        foreach (var (original, aside) in parked)
        {
            try { if (!File.Exists(original) && File.Exists(aside)) File.Move(aside, original); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
    }

    /// <summary>
    /// True if a backup stamped <paramref name="schemaVersion"/> can be restored by this build: any version from
    /// v1 up to and including <see cref="Schema.CurrentVersion"/> (the store migrates it forward on open).
    /// </summary>
    public static bool CanRestoreSchemaVersion(int schemaVersion)
        => schemaVersion >= 1 && schemaVersion <= Schema.CurrentVersion;

    /// <summary>The refusal message shown when a backup's schema version is outside what this build handles.</summary>
    public static string UnsupportedSchemaMessage(int schemaVersion) =>
        schemaVersion > Schema.CurrentVersion
            ? $"This backup was taken by a newer version of Apex Solutions (data format v{schemaVersion}). " +
              $"This build reads up to v{Schema.CurrentVersion}. Update Apex Solutions, then restore again — " +
              "restoring it here would lose data. Nothing has been changed."
            : $"This backup's data format (v{schemaVersion}) is not one this build recognises " +
              $"(it reads v1 to v{Schema.CurrentVersion}). The archive may be damaged. Nothing has been changed.";

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// How long a backup waits for a lock before giving up, in seconds. A transaction big enough to spill its
    /// dirty pages holds an EXCLUSIVE lock, and the backup then cannot read at all. Waiting the ADO.NET default
    /// (30s) freezes the screen; failing in a few seconds with "try again in a moment" is the honest answer, and
    /// it is far better than the alternative a file copy offers — succeeding, and writing a torn archive.
    /// </summary>
    private const int LockWaitSeconds = 5;

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
        }.ToString())
        {
            DefaultTimeout = LockWaitSeconds,
        };
        conn.Open();
        return conn;
    }

    /// <summary>The <c>schema_version</c> stamp, or null when the file carries no such table (not our database).</summary>
    private static int? ReadSchemaVersion(SqliteConnection conn)
    {
        using var probe = conn.CreateCommand();
        probe.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version';";
        if (Convert.ToInt64(probe.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return null;

        using var read = conn.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var value = read.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>The stored company's display name (informational for the manifest), or null when there is none.</summary>
    private static string? ReadCompanyName(SqliteConnection conn)
    {
        using var probe = conn.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='companies';";
        if (Convert.ToInt64(probe.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return null;

        using var read = conn.CreateCommand();
        read.CommandText = "SELECT name FROM companies ORDER BY name LIMIT 1;";
        return read.ExecuteScalar() as string;
    }

    private static string IntegrityCheck(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "unknown";
    }

    /// <summary>
    /// True when both strings name the same file on disk. Deliberately TOTAL: an unusable path cannot be
    /// canonicalised, and this guard must never be the thing that turns a bad-path error into a different
    /// exception than the one the caller would otherwise have seen, so it falls back to a literal comparison.
    /// </summary>
    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException
                                      or UnauthorizedAccessException)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// True when a file already exists at <paramref name="path"/> and it is one of OUR company databases — it
    /// opens as SQLite and carries a <c>schema_version</c> stamp. Anything else (no file, a previous
    /// <c>.apexbak</c> ZIP, a text file, a database that is not ours) is false, so an ordinary
    /// backup-over-yesterday's-backup is unaffected. TOTAL, for the same reason as <see cref="SamePath"/>: a
    /// destination this cannot inspect is not reported as a company.
    /// </summary>
    private static bool LooksLikeCompanyDatabase(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var probe = Open(path, SqliteOpenMode.ReadOnly);
            return ReadSchemaVersion(probe) is not null;
        }
        catch (SqliteException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            return false;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort — a locked staging file is cleaned up on the next run */ }
        catch (UnauthorizedAccessException) { /* ditto */ }
        catch (ArgumentException) { /* an unusable path cannot be holding a stale temp file */ }
        catch (NotSupportedException) { /* ditto */ }
    }

    private static ZipArchive OpenArchive(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new CompanyBackupException("No backup file was given.");
        if (!File.Exists(backupPath))
            throw new CompanyBackupException($"There is no backup file at '{backupPath}'.");

        try { return ZipFile.OpenRead(backupPath); }
        catch (InvalidDataException ex)
        {
            throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is not an Apex Solutions backup file. " +
                "Nothing has been changed.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CompanyBackupException($"Could not open '{backupPath}': {ex.Message}", ex);
        }
    }

    private static BackupManifest ReadManifest(ZipArchive zip, string backupPath)
    {
        var entry = zip.GetEntry(ManifestEntryName)
            ?? throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is not an Apex Solutions backup file (no manifest). " +
                "Nothing has been changed.");

        string json;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            json = reader.ReadToEnd();

        ManifestDto? dto;
        try { dto = JsonSerializer.Deserialize<ManifestDto>(json, ManifestJsonOptions); }
        catch (JsonException ex)
        {
            throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is damaged — its manifest could not be read. " +
                "Nothing has been changed.", ex);
        }

        if (dto is null || dto.DatabaseSha256 is null)
            throw new CompanyBackupException(
                $"'{Path.GetFileName(backupPath)}' is damaged — its manifest is incomplete. " +
                "Nothing has been changed.");

        return new BackupManifest(
            dto.FormatVersion,
            dto.SchemaVersion,
            dto.CompanyName ?? string.Empty,
            dto.SourceFileName ?? string.Empty,
            dto.TakenAtUtc,
            dto.DatabaseBytes,
            dto.DatabaseSha256);
    }

    private static string WriteManifestJson(BackupManifest manifest) =>
        JsonSerializer.Serialize(new ManifestDto
        {
            FormatVersion = manifest.FormatVersion,
            SchemaVersion = manifest.SchemaVersion,
            CompanyName = manifest.CompanyName,
            SourceFileName = manifest.SourceFileName,
            TakenAtUtc = manifest.TakenAtUtc,
            DatabaseBytes = manifest.DatabaseBytes,
            DatabaseSha256 = manifest.DatabaseSha256,
        }, ManifestJsonOptions);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The on-disk shape of <c>manifest.json</c> — a mutable DTO so the JSON stays camelCase and stable.</summary>
    private sealed class ManifestDto
    {
        public int FormatVersion { get; set; }
        public int SchemaVersion { get; set; }
        public string? CompanyName { get; set; }
        public string? SourceFileName { get; set; }
        public DateTimeOffset TakenAtUtc { get; set; }
        public long DatabaseBytes { get; set; }
        public string? DatabaseSha256 { get; set; }
    }
}
