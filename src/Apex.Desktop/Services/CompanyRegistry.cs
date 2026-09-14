using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Apex.Desktop.Services;

/// <summary>What a registry row says about how a company's book is stored.</summary>
public enum CompanyStorageKind
{
    /// <summary>A plain <c>.db</c>, named after the company exactly as every book has always been.</summary>
    Plain,

    /// <summary>An encrypted book at an opaque filename. Its display name is inside the file (census 16.1).</summary>
    Vaulted,
}

/// <summary>
/// One company as the registry knows it. <paramref name="DisplayName"/> is the REAL name for a plain book and
/// <see cref="CompanyVault.MaskedName"/> for a vaulted one — the real name of a vaulted book is not knowable
/// without its passphrase, so nothing in this type can leak it.
/// </summary>
/// <param name="Number">The stable company number. The vendor keeps this visible for a vaulted company
/// precisely because the name is not; it is how an operator tells two vaulted books apart.</param>
public sealed record CompanyRecord(
    int Number,
    string FileName,
    string DisplayName,
    CompanyStorageKind Kind)
{
    /// <summary>True when this book needs a passphrase to open.</summary>
    public bool IsVaulted => Kind == CompanyStorageKind.Vaulted;
}

/// <summary>
/// 🔴 <b>Census row 16.1 — THE COMPANY REGISTRY: the thing that has to exist before a vault can mean
/// anything.</b> It is a small SQLite index at <c>Companies/companies.index</c> that maps a file on disk to a
/// company's identity, so that identity STOPS BEING THE FILENAME.
///
/// <para>🔴 <b>WHY THIS HAD TO BE BUILT, AND WHY ENCRYPTION WITHOUT IT IS A FEATURE THAT DOES NOTHING.</b>
/// Every company in this application has lived at <c>Companies/&lt;company name&gt;.db</c> since the beginning:
/// <c>CompanyStorage.PathForName</c> DERIVES the path from the name, and <c>CompanyStorage.ListCompanies</c>
/// ran the derivation backwards — it built the entire company list by reading names OUT OF FILENAMES. The
/// vendor's feature encrypts the company name along with everything else, so shipping encryption on top of
/// that scheme would have left <c>Companies/Acme Traders.db</c> in a directory listing: the exact plaintext
/// the feature exists to hide, sitting beside the ciphertext. The only two ways out were "the vaulted company
/// disappears from the list" (unopenable) and "something other than the filename carries identity". This is
/// that something.</para>
///
/// <para>🔴 <b>WHAT AN ATTACKER WITH THE DISK CAN READ — stated exactly, because a registry in plaintext
/// beside the vault would defeat the feature.</b> This file is NOT encrypted, and it deliberately holds
/// nothing that would need to be. For a VAULTED company it stores: the company NUMBER, the opaque filename
/// (a GUID), and the flag saying it is vaulted. <b>It does not store the name, any hash or wrapping of the
/// name, the passphrase, a verifier for the passphrase, any KDF material, or anything else derived from
/// them</b> — those columns are NULL, and
/// <c>CompanyRegistryTests.A_vaulted_company_leaves_no_trace_of_its_name_anywhere_on_disk</c> scans the raw
/// bytes of BOTH the registry file and the vaulted book for the name and fails if either carries it. So the
/// disclosure to an attacker holding the disk is: <i>how many companies exist, which of them are vaulted,
/// their numbers, their file sizes and their timestamps</i>. Not their names, and not their contents.
/// Companies that are NOT vaulted are unchanged — their names are in this file and in their filenames, as
/// they always were, because nobody asked for those to be secret.</para>
///
/// <para><b>The registry is a CACHE OF IDENTITY, never the book itself.</b> Losing it loses no accounting
/// data: <see cref="Load"/> rebuilds it by adopting every plain <c>.db</c> it finds, so a deleted registry
/// heals back to exactly the pre-vault behaviour for plain books. What it cannot heal is the ASSOCIATION for
/// a vaulted book — an orphaned GUID file whose row is gone is still openable with its passphrase, but the
/// application no longer lists it. That is recorded rather than hidden; it is why the registry is rewritten
/// atomically and never edited in place.</para>
///
/// <para>🔴 <b>THE JOURNAL — how a half-vaulted company is made impossible.</b> Enabling the vault means
/// writing a new encrypted file, pointing the registry at it, and deleting the plaintext. A crash between any
/// two of those steps is the failure this row's brief names as a lost company. The <c>pending</c> table
/// records the in-flight operation BEFORE anything is written, and <see cref="RecoverPending"/> — which runs
/// on EVERY <see cref="Load"/>, not just at startup — finishes or unwinds it:
/// <list type="bullet">
/// <item><b>state <c>staging</c></b> — the new file may or may not be complete and the registry still points
/// at the original. The ORIGINAL is authoritative. The half-written new file is deleted and the operation is
/// forgotten. Nothing is lost.</item>
/// <item><b>state <c>committed</c></b> — the registry row and this state changed in ONE transaction, so
/// reaching it proves the new file was written AND verified AND adopted. The NEW file is authoritative and
/// the superseded one is deleted. This is the step that removes the plaintext copy, and it is why enabling
/// the vault cannot leave one behind.</item>
/// </list>
/// There is no third state, because the single transaction is what removes the window between them.</para>
/// </summary>
public sealed class CompanyRegistry
{
    /// <summary>The registry file's name inside the Companies directory. Not a company, so never listed as one.</summary>
    public const string FileName = "companies.index";

    private readonly string _directory;
    private readonly string _indexPath;

    /// <summary>Creates a registry over the given Companies directory (created if missing).</summary>
    public CompanyRegistry(string companiesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companiesDirectory);
        _directory = companiesDirectory;
        Directory.CreateDirectory(_directory);
        _indexPath = Path.Combine(_directory, FileName);
    }

    /// <summary>The registry file's full path. Public so the leak test can read its raw bytes.</summary>
    public string IndexPath => _indexPath;

    /// <summary>Resolves a registry row's filename to a full path.</summary>
    public string PathOf(CompanyRecord record) => Path.Combine(_directory, record.FileName);

    /// <summary>
    /// The company list: every registered book, plus any plain <c>.db</c> on disk that has no row yet, adopted
    /// into one. Ordered by company number, which is the order the operator sees and the order that stays put
    /// when a company is renamed.
    ///
    /// <para><b>Adoption is what makes this change invisible to every book already on disk.</b> A user
    /// upgrading into this build has N plain <c>.db</c> files and no registry; the first call creates rows for
    /// all of them, keeping their filenames and their names exactly as they were. No migration step, no
    /// prompt, and nothing to go wrong — because for a plain book the registry records precisely what the
    /// filename already said.</para>
    ///
    /// <para><b>Adoption skips anything it cannot open unkeyed</b>, which is exactly what an orphaned vaulted
    /// file is. Adopting one would be impossible anyway (there is no name to adopt), and TRYING would
    /// mis-report it as a plain company with a GUID for a name.</para>
    /// </summary>
    public IReadOnlyList<CompanyRecord> Load()
    {
        RecoverPending();

        var rows = ReadRows();
        var known = new HashSet<string>(rows.Select(r => r.FileName), StringComparer.OrdinalIgnoreCase);

        // 🔴 A file still named by an UNRESOLVED journal row is not a company and must never be adopted as
        // one: on the Enable path that file is the doomed PLAINTEXT whose delete has not succeeded yet, and
        // adopting it would list the very name the vault is hiding. RecoverPending has already run above and
        // left behind only the rows whose delete failed, so this set is normally empty.
        var inFlight = PendingFiles();

        var adopted = new List<CompanyRecord>();
        foreach (var path in EnumerateCandidateFiles())
        {
            var file = Path.GetFileName(path);
            if (known.Contains(file) || inFlight.Contains(file))
                continue;
            // Only a book we can actually read unkeyed can be adopted — see the remarks.
            if (CompanyVault.IsVaulted(path))
                continue;
            adopted.Add(new CompanyRecord(
                Number: 0,   // assigned on insert
                FileName: file,
                DisplayName: Path.GetFileNameWithoutExtension(file),
                Kind: CompanyStorageKind.Plain));
        }

        if (adopted.Count > 0)
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            var next = NextNumber(connection, tx);
            foreach (var a in adopted)
            {
                Insert(connection, tx, next, a.FileName, a.DisplayName, vaulted: false);
                next++;
            }
            tx.Commit();
            rows = ReadRows();
        }

        return rows;
    }

    /// <summary>
    /// Records a brand-new PLAIN company. Called on the creation path so a new book is registered the moment
    /// it exists rather than being adopted later; the two produce the same row, which is what lets adoption
    /// stay a pure backfill.
    /// </summary>
    public CompanyRecord RegisterPlain(string companyName, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var existing = FindByFile(connection, tx, fileName);
        if (existing is not null)
        {
            Rename(connection, tx, existing.Number, companyName);
            tx.Commit();
            return existing with { DisplayName = companyName };
        }

        var number = NextNumber(connection, tx);
        Insert(connection, tx, number, fileName, companyName, vaulted: false);
        tx.Commit();
        return new CompanyRecord(number, fileName, companyName, CompanyStorageKind.Plain);
    }

    /// <summary>Rewrites a plain company's display name and filename after <c>CompanyStorage.Rename</c> moved it.</summary>
    public void RecordRename(string oldFileName, string newFileName, string newDisplayName)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var row = FindByFile(connection, tx, oldFileName);
        if (row is null) { tx.Commit(); return; }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE companies SET file_name = $f, display_name = $n WHERE number = $no;";
        cmd.Parameters.AddWithValue("$f", newFileName);
        cmd.Parameters.AddWithValue("$n", newDisplayName);
        cmd.Parameters.AddWithValue("$no", row.Number);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>Forgets a company (after its file was deleted). Leaves the numbers of the others alone.</summary>
    public void Forget(string fileName)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM companies WHERE file_name = $f COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$f", fileName);
        cmd.ExecuteNonQuery();
    }

    // ============================================================================== the vault transition (16.1)

    /// <summary>
    /// 🔴 <b>Puts a company INTO the vault: encrypts its book to a fresh opaque filename, switches the
    /// registry over in one transaction, and removes the plaintext.</b> Returns the new record.
    ///
    /// <para><b>The order is the whole safety argument, and every step is journalled before it happens.</b>
    /// (1) journal <c>staging</c> naming both files; (2) <c>CompanyVault.Encrypt</c> writes and VERIFIES the
    /// new file, never touching the old one; (3) ONE transaction flips the registry row to the new file, drops
    /// the display name, sets the vaulted flag AND moves the journal to <c>committed</c>; (4) the plaintext is
    /// deleted and the journal row is dropped.</para>
    ///
    /// <para><b>A crash at any point is survivable and is TESTED, not asserted.</b> Before (3) the original is
    /// still the registered book and still plaintext — recovery deletes the partial vault file and the
    /// operator simply has not vaulted yet. After (3) the vault is the registered book and recovery finishes
    /// the delete. <b>There is no instant at which the registry points at a file that has not been verified
    /// readable, and none at which both files are listed as companies.</b>
    /// <c>CompanyRegistryTests</c> kills the operation at each state and re-runs
    /// <see cref="RecoverPending"/>.</para>
    ///
    /// <para><b>Refused if the passphrase does not open the result</b> — the verify is inside
    /// <c>CompanyVault.Encrypt</c>, so a company is never left pointing at a file nobody can read.</para>
    /// </summary>
    public CompanyRecord Enable(CompanyRecord company, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(company);
        var weak = CompanyVault.DescribeWeakness(passphrase);
        if (weak is not null) throw new ArgumentException(weak, nameof(passphrase));
        if (company.IsVaulted)
            throw new InvalidOperationException("This company is already in the vault.");

        var sourceFile = company.FileName;
        var targetFile = NewOpaqueFileName();
        return Transition(company.Number, sourceFile, targetFile, vaulted: true, displayName: null,
            write: () => CompanyVault.Encrypt(Path.Combine(_directory, sourceFile),
                                              Path.Combine(_directory, targetFile), passphrase));
    }

    /// <summary>
    /// Takes a company OUT of the vault: decrypts it back to a plain book named after the company again.
    /// The same journalled order as <see cref="Enable"/>, so the same crash argument holds.
    /// <para><paramref name="plainFileName"/> is supplied by the caller because only the caller (which has
    /// just decrypted and read the book) knows the company's real name — the registry does not, by design.</para>
    /// </summary>
    public CompanyRecord Disable(CompanyRecord company, string passphrase, string plainFileName, string displayName)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!company.IsVaulted)
            throw new InvalidOperationException("This company is not in the vault.");

        var sourceFile = company.FileName;
        return Transition(company.Number, sourceFile, plainFileName, vaulted: false, displayName: displayName,
            write: () => CompanyVault.Decrypt(Path.Combine(_directory, sourceFile), passphrase,
                                              Path.Combine(_directory, plainFileName)));
    }

    /// <summary>
    /// Changes a vaulted company's passphrase. Writes a NEW opaque file under the new passphrase and switches
    /// to it under the same journal, so a crash mid-change leaves the book openable with ONE of the two
    /// passphrases and never with neither.
    /// </summary>
    public CompanyRecord ChangePassphrase(CompanyRecord company, string oldPassphrase, string newPassphrase)
    {
        ArgumentNullException.ThrowIfNull(company);
        var weak = CompanyVault.DescribeWeakness(newPassphrase);
        if (weak is not null) throw new ArgumentException(weak, nameof(newPassphrase));
        if (!company.IsVaulted)
            throw new InvalidOperationException("This company is not in the vault.");

        var sourceFile = company.FileName;
        var targetFile = NewOpaqueFileName();
        return Transition(company.Number, sourceFile, targetFile, vaulted: true, displayName: null,
            write: () => CompanyVault.ChangePassphrase(Path.Combine(_directory, sourceFile), oldPassphrase,
                                                       Path.Combine(_directory, targetFile), newPassphrase));
    }

    /// <summary>
    /// The shared journalled swap behind <see cref="Enable"/>, <see cref="Disable"/> and
    /// <see cref="ChangePassphrase"/>. Written once rather than three times because the safety argument is the
    /// same one and three copies of it would drift.
    /// </summary>
    private CompanyRecord Transition(
        int number, string sourceFile, string targetFile, bool vaulted, string? displayName, Action write)
    {
        if (string.Equals(sourceFile, targetFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The vault refuses to write a book over itself; a transition always writes a new file.");
        if (File.Exists(Path.Combine(_directory, targetFile)))
            throw new IOException($"'{targetFile}' already exists; refusing to overwrite it.");

        JournalStaging(number, sourceFile, targetFile);
        try
        {
            write();
        }
        catch
        {
            // The new file (if any) is already gone — CompanyVault deletes its own partial output. Drop the
            // journal so the next Load does not try to unwind an operation that has fully unwound itself.
            ClearJournal(number);
            throw;
        }

        // 🔴 ONE transaction: the row flip and the journal's move to `committed` are the same commit. That is
        // what makes "the registry points at the new file" and "the old file is garbage" a single fact rather
        // than two that can disagree across a crash.
        using (var connection = Open())
        using (var tx = connection.BeginTransaction())
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "UPDATE companies SET file_name = $f, display_name = $n, vaulted = $v WHERE number = $no;";
                cmd.Parameters.AddWithValue("$f", targetFile);
                cmd.Parameters.AddWithValue("$n", (object?)displayName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$v", vaulted ? 1 : 0);
                cmd.Parameters.AddWithValue("$no", number);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE pending SET state = 'committed' WHERE number = $no;";
                cmd.Parameters.AddWithValue("$no", number);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        RecoverPending();

        return new CompanyRecord(
            number, targetFile,
            displayName ?? CompanyVault.MaskedName,
            vaulted ? CompanyStorageKind.Vaulted : CompanyStorageKind.Plain);
    }

    /// <summary>
    /// 🔴 <b>Finishes or unwinds every journalled transition. Runs on every <see cref="Load"/>.</b> See the
    /// type remarks for why each state resolves the way it does. Deliberately idempotent: running it twice
    /// does nothing the second time, which is what makes it safe to call from a list refresh.
    /// </summary>
    public void RecoverPending()
    {
        List<(int Number, string Source, string Target, string State)> pending;
        using (var connection = Open())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT number, source_file, target_file, state FROM pending;";
            using var r = cmd.ExecuteReader();
            pending = new List<(int, string, string, string)>();
            while (r.Read())
                pending.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        if (pending.Count == 0) return;

        foreach (var p in pending)
        {
            // `staging` → the swap never committed, so the ORIGINAL is the book. Remove the partial new file.
            // `committed` → the swap did commit, so the NEW file is the book. Remove the superseded original,
            //               which on the Enable path is the PLAINTEXT COPY this feature must not leave behind.
            var doomed = p.State == "committed" ? p.Source : p.Target;

            // 🔴 THE JOURNAL ROW IS ONLY CLEARED IF THE FILE ACTUALLY WENT. Deleting a book can fail — a live
            // handle on Windows, an unwritable parent directory on POSIX — and SafeDelete swallows that by
            // design. Clearing the journal anyway would drop the one record that the plaintext copy is doomed,
            // and `Load`'s adoption would then take that abandoned plaintext for a NEW COMPANY and list it
            // under its real name: the precise leak this whole row exists to close, arrived at by tidying up.
            // Leaving the row is harmless — recovery is idempotent and simply retries on the next Load.
            if (SafeDelete(Path.Combine(_directory, doomed)))
                ClearJournal(p.Number);
        }
    }

    /// <summary>The files named by an unresolved journal row — neither is a company until recovery finishes.</summary>
    private HashSet<string> PendingFiles()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source_file, target_file FROM pending;";
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) { set.Add(r.GetString(0)); set.Add(r.GetString(1)); }
        return set;
    }

    // ============================================================================================== the storage

    private SqliteConnection Open()
    {
        // The registry is deliberately UNENCRYPTED — it holds nothing secret (see the type remarks) and
        // encrypting it would need a key at list time, before any passphrase has been asked for.
        var connection = new SqliteConnection(
            CompanyVault.ConnectionString(_indexPath, passphrase: null, SqliteOpenMode.ReadWriteCreate));
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS companies (
                number       INTEGER PRIMARY KEY,
                file_name    TEXT NOT NULL,
                display_name TEXT NULL,
                vaulted      INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS pending (
                number       INTEGER PRIMARY KEY,
                source_file  TEXT NOT NULL,
                target_file  TEXT NOT NULL,
                state        TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        return connection;
    }

    private List<CompanyRecord> ReadRows()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT number, file_name, display_name, vaulted FROM companies ORDER BY number;";
        using var r = cmd.ExecuteReader();
        var list = new List<CompanyRecord>();
        while (r.Read())
        {
            var vaulted = r.GetInt32(3) != 0;
            list.Add(new CompanyRecord(
                r.GetInt32(0),
                r.GetString(1),
                // 🔴 A vaulted row's display_name is NULL on disk and is rendered as asterisks HERE rather than
                // being stored — there is nothing to store, which is the point of the feature.
                vaulted || r.IsDBNull(2) ? CompanyVault.MaskedName : r.GetString(2),
                vaulted ? CompanyStorageKind.Vaulted : CompanyStorageKind.Plain));
        }
        return list;
    }

    private IEnumerable<string> EnumerateCandidateFiles()
    {
        if (!Directory.Exists(_directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(_directory, "*.db"))
            yield return path;
    }

    private CompanyRecord? FindByFile(SqliteConnection connection, SqliteTransaction tx, string fileName)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT number, file_name, display_name, vaulted FROM companies WHERE file_name = $f COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$f", fileName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var vaulted = r.GetInt32(3) != 0;
        return new CompanyRecord(
            r.GetInt32(0), r.GetString(1),
            vaulted || r.IsDBNull(2) ? CompanyVault.MaskedName : r.GetString(2),
            vaulted ? CompanyStorageKind.Vaulted : CompanyStorageKind.Plain);
    }

    private static void Rename(SqliteConnection connection, SqliteTransaction tx, int number, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE companies SET display_name = $n WHERE number = $no AND vaulted = 0;";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$no", number);
        cmd.ExecuteNonQuery();
    }

    private static int NextNumber(SqliteConnection connection, SqliteTransaction tx)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(number), 0) + 1 FROM companies;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1, CultureInfo.InvariantCulture);
    }

    private static void Insert(
        SqliteConnection connection, SqliteTransaction tx, int number, string file, string? name, bool vaulted)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO companies (number, file_name, display_name, vaulted) VALUES ($no, $f, $n, $v);";
        cmd.Parameters.AddWithValue("$no", number);
        cmd.Parameters.AddWithValue("$f", file);
        cmd.Parameters.AddWithValue("$n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$v", vaulted ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private void JournalStaging(int number, string source, string target)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pending (number, source_file, target_file, state) VALUES ($no, $s, $t, 'staging')
            ON CONFLICT(number) DO UPDATE SET source_file = $s, target_file = $t, state = 'staging';
            """;
        cmd.Parameters.AddWithValue("$no", number);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$t", target);
        cmd.ExecuteNonQuery();
    }

    private void ClearJournal(int number)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM pending WHERE number = $no;";
        cmd.Parameters.AddWithValue("$no", number);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// A fresh, name-free filename for a vaulted book. A GUID: it carries no information about the company,
    /// and it is not derived from the name (a hash of the name would be a dictionary-attackable leak of
    /// exactly the thing being hidden).
    /// </summary>
    private static string NewOpaqueFileName() => Guid.NewGuid().ToString("N") + ".db";

    /// <summary>
    /// Deletes a superseded book. 🔴 Pools are cleared first: on Windows a connection that was returned to the
    /// Microsoft.Data.Sqlite pool still holds the OS file handle, and the delete of the plaintext copy would
    /// fail with no error the operator ever sees — leaving the plaintext this feature exists to remove.
    /// </summary>
    /// <returns>True when the file is gone (or was never there) — the caller clears the journal ONLY then.</returns>
    private static bool SafeDelete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
            return !File.Exists(path);
        }
        catch (IOException) { return false; }              // in use — retry on the next Load
        catch (UnauthorizedAccessException) { return false; }  // read-only, or an unwritable POSIX parent
    }
}
