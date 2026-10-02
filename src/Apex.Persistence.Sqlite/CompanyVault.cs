using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// 🔴 <b>Census row 16.1 — THE COMPANY VAULT: page-level encryption of a company's whole <c>.db</c> behind a
/// passphrase the application never stores.</b> This is the CRYPTO half of the row; the half that stops the
/// FILENAME from leaking the company name lives in <c>Apex.Desktop.Services.CompanyRegistry</c>, and neither
/// half is worth anything without the other (see "the filename is the leak", below).
///
/// <para><b>Fidelity (Ruling 14 tier 1 — <c>help.tallysolutions.com/tallyvault-for-company-tally/</c>, and
/// <c>help.tallysolutions.com/data-security-faq/</c> for the no-recovery statement; both re-read and confirmed
/// verbatim 2026-09-14).</b> The vendor's data-vault page states
/// that once the vault is set <i>"your company and all transaction details, including the company name, will
/// be securely encrypted"</i>, that a vaulted company is listed with its name <i>"displayed with a series of
/// asterisks, while the company number remains visible"</i>, and — repeatedly — that <i>"forgetting this
/// password will result in permanently losing access to your company data"</i>. All three are cloned:
/// the name is inside the encrypted file, the list shows asterisks and a stable number
/// (<c>CompanyRegistry</c>), and a lost passphrase is unrecoverable BY CONSTRUCTION (below). The vendor's
/// product name for the feature carries the "Tally" brand and is therefore never rendered here; this
/// application calls it the <b>Data Vault</b>.</para>
///
/// <para>🔴 <b>WHY THE FILENAME IS THE WHOLE PROBLEM, AND WHY ENCRYPTION ALONE DOES NOT SOLVE IT.</b> Every
/// company in this application has always lived in <c>Companies/&lt;company name&gt;.db</c> — the name IS the
/// path (<c>CompanyStorage.PathForName</c>) — and the company list has always been built by enumerating those
/// filenames. So encrypting the file contents and stopping there would hide the ledger and leave
/// <c>Companies/Acme Traders.db</c> sitting in a directory listing: the feature exists to hide the company
/// name, and the filename would still be shouting it. That is why enabling the vault MOVES the book to an
/// opaque, name-free filename and why a registry had to be built to map opaque files back to names. What an
/// attacker holding the disk can read is stated in full on <c>CompanyRegistry</c>.</para>
///
/// <para><b>THE PASSPHRASE IS NEVER STORED, ANYWHERE — not hashed, not wrapped, not verified against a
/// stored verifier (R13).</b> SQLCipher derives the page key from the passphrase and a 16-byte random salt it
/// writes into the file header, and the only test of correctness is whether page 1 decrypts to something with
/// a valid HMAC. So there is no verifier to steal and no offline oracle beyond the file itself. The
/// consequence is the one the vendor also states and this application states to the operator's face before
/// it acts: <b>a forgotten passphrase means the book is gone.</b> There is no reset, no recovery key and no
/// back door, and adding one would be the same as not having the feature.</para>
///
/// <para><b>KDF, salt and work factor — measured on the shipped native, not quoted from a blog.</b> The
/// provider is SQLitePCLRaw <c>bundle_e_sqlcipher</c> 2.1.11 (SQLCipher <b>4.5.2 community</b>). Its
/// defaults, read back out of a live keyed connection by
/// <c>CompanyVaultTests.The_shipped_native_uses_the_kdf_parameters_this_class_documents</c>, are
/// <c>PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512</c>, <c>PRAGMA kdf_iter = 256000</c>,
/// <c>PRAGMA cipher_hmac_algorithm = HMAC_SHA512</c>, <c>PRAGMA cipher_page_size = 4096</c>, with a per-file
/// random salt in the header. Those defaults are taken rather than overridden — a hand-lowered
/// <c>kdf_iter</c> is the classic way to ship an encryption feature that is weaker than it looks — and the
/// test above FAILS if a future provider bump quietly changes any of them.</para>
///
/// <para>🔴 <b>A HALF-VAULTED COMPANY IS A LOST COMPANY, so nothing here mutates a book in place.</b>
/// <see cref="Encrypt"/> and <see cref="Decrypt"/> both WRITE A NEW FILE beside the old one, verify it opens
/// and carries rows, and only then hand the caller the finished path; the source file is never touched, and
/// on any failure the new file is deleted and the source is exactly as it was. Deciding which of the two
/// files is the live book, and cleaning the other one up across a crash, is the registry's journal — see
/// <c>CompanyRegistry.Enable</c>. This class deliberately does NOT delete anything it did not create.</para>
/// </summary>
public static class CompanyVault
{
    /// <summary>
    /// The application's own name for the feature, used in every user-visible string. 🔴 The vendor's product
    /// name carries the "Tally" brand and this application must never render it (R7); the suite asserts the
    /// absence of that word in a dozen places. Named as a constant so screens, menu rows and tests all read
    /// the same string instead of restating it.
    /// </summary>
    public const string FeatureName = "Data Vault";

    /// <summary>
    /// What a vaulted company's name is rendered as in any list, since the real name is inside the encrypted
    /// file and is not recoverable without the passphrase. The vendor shows <i>"a series of asterisks"</i>;
    /// this is that series.
    /// </summary>
    public const string MaskedName = "********";

    /// <summary>
    /// 🔴 The sentence shown to the operator BEFORE the vault is set, not after. The vendor states the same
    /// thing repeatedly on its own page; this application states it on the screen where the decision is made,
    /// because an operator who learns it afterwards has already taken the risk.
    /// </summary>
    public const string IrreversibilityWarning =
        "The passphrase is never stored. If it is forgotten, this company's books cannot be opened again "
        + "by anyone — there is no reset and no recovery key. Keep it somewhere safe before you continue.";

    /// <summary>The shortest passphrase this application will accept. Ours, not the vendor's — see remarks.</summary>
    /// <remarks>
    /// The vendor's page documents no minimum. Accepting a one-character passphrase would make the 256000-round
    /// KDF irrelevant, so a floor is imposed and is labelled as OURS rather than presented as cloned behaviour.
    /// </remarks>
    public const int MinimumPassphraseLength = 8;

    /// <summary>
    /// True when <paramref name="databasePath"/> is an encrypted book — measured by trying to read its schema
    /// with NO key, which is exactly what fails on a vaulted file and succeeds on a plain one.
    /// <para><b>A file that does not exist is not vaulted</b> (returns false) rather than throwing: callers
    /// use this to decide whether to ask for a passphrase, and "there is no book here" is a different problem
    /// they report differently.</para>
    /// <para><b>A CORRUPT file also answers true</b>, because "cannot be read unkeyed" is all this can
    /// actually observe. That is why it is never the only check: <see cref="TryOpen"/> is what distinguishes
    /// "vaulted, and this is the passphrase" from "vaulted, wrong passphrase" from "not a database at all".</para>
    /// </summary>
    public static bool IsVaulted(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return false;
        return !CanRead(databasePath, passphrase: null);
    }

    /// <summary>
    /// True when <paramref name="passphrase"/> opens the book at <paramref name="databasePath"/>. Pass
    /// <c>null</c> to test the unkeyed case. This is the ONLY way to check a passphrase — there is no stored
    /// verifier to compare against (see the type remarks).
    /// </summary>
    public static bool TryOpen(string databasePath, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return false;
        return CanRead(databasePath, passphrase);
    }

    /// <summary>
    /// Builds the connection string for a book, adding <c>Password=</c> only when a passphrase is supplied.
    /// Every opener in this assembly goes through here so that "keyed" and "unkeyed" are one decision in one
    /// place; <see cref="SqliteCompanyStore"/> is the main caller.
    /// </summary>
    /// <param name="pooling">
    /// 🔴 Pass <c>false</c> for a file whose OS HANDLE MUST BE RELEASED the moment the connection is disposed.
    /// Microsoft.Data.Sqlite keeps a disposed connection in a pool and KEEPS THE FILE HANDLE OPEN with it, so a
    /// pooled file cannot be deleted, moved or its directory removed until something calls
    /// <c>ClearAllPools</c>. That is not a theory here: the company registry was pooled, and the retained
    /// handle on <c>companies.index</c> broke FOURTEEN unrelated tests whose only crime was deleting their own
    /// temp directory afterwards. The registry is a handful of rows read in milliseconds, so pooling buys it
    /// nothing and costs it that. Company BOOKS stay pooled (the default) — they are opened repeatedly and
    /// their handle lifetime is managed deliberately.
    /// </param>
    public static string ConnectionString(
        string databasePath, string? passphrase, SqliteOpenMode mode, bool pooling = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Pooling = pooling,
        };
        // An EMPTY password is not "no password" to SQLCipher — it is a key of zero length, which behaves
        // differently again. Blank is normalised to unkeyed here so no caller has to remember that.
        if (!string.IsNullOrEmpty(passphrase))
            builder.Password = passphrase;
        return builder.ToString();
    }

    /// <summary>
    /// 🔴 <b>Writes a NEW, encrypted copy of the plain book at <paramref name="sourcePath"/> to
    /// <paramref name="targetPath"/>. The source is never modified.</b> Returns nothing and throws on failure,
    /// having removed any partial target.
    ///
    /// <para><b>How.</b> SQLCipher's own <c>sqlcipher_export</c>: attach the target with a KEY and copy the
    /// whole logical database into it. That is a page-by-page re-encode through the engine, so the target is a
    /// complete, consistent database rather than a byte copy — the alternative (copy the file, then
    /// <c>PRAGMA rekey</c>) rewrites the book IN PLACE and a crash half way through leaves exactly the
    /// half-vaulted book this project refuses to ship.</para>
    ///
    /// <para><b>The written file is VERIFIED before this method returns</b> — reopened with the passphrase and
    /// read — so a caller that sees no exception is holding a file that demonstrably opens. A caller that sees
    /// an exception is holding nothing: the target is deleted on every failure path.</para>
    /// </summary>
    public static void Encrypt(string sourcePath, string targetPath, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        Export(sourcePath, sourcePassphrase: null, targetPath, targetPassphrase: passphrase);
    }

    /// <summary>
    /// The reverse of <see cref="Encrypt"/>: writes a NEW, PLAIN copy of the vaulted book to
    /// <paramref name="targetPath"/>. The encrypted source is never modified. Throws if
    /// <paramref name="passphrase"/> does not open the source.
    /// </summary>
    public static void Decrypt(string sourcePath, string passphrase, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        Export(sourcePath, sourcePassphrase: passphrase, targetPath, targetPassphrase: null);
    }

    /// <summary>
    /// Changes a vaulted book's passphrase, by writing a NEW file at <paramref name="targetPath"/> under the
    /// new passphrase. The source is never modified.
    ///
    /// <para><b>Deliberately NOT <c>PRAGMA rekey</c>.</b> Rekey rewrites every page of the live book in place;
    /// a power cut in the middle leaves a file half under each key, which no passphrase opens. This project has
    /// already recorded that a half-vaulted company is a lost company, so the safe-but-slower export is used
    /// for the change too, and the registry's journal decides which file is live.</para>
    /// </summary>
    public static void ChangePassphrase(string sourcePath, string oldPassphrase, string targetPath, string newPassphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPassphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassphrase);
        Export(sourcePath, oldPassphrase, targetPath, newPassphrase);
    }

    /// <summary>
    /// The refusal shown for a passphrase this application will not accept, or <c>null</c> when it is fine.
    /// Returned as a MESSAGE rather than thrown because every caller is a screen that must show it.
    /// </summary>
    public static string? DescribeWeakness(string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            return "A passphrase is required.";
        if (passphrase.Length < MinimumPassphraseLength)
            return $"The passphrase must be at least {MinimumPassphraseLength} characters.";
        if (passphrase.Trim().Length == 0)
            return "A passphrase of only spaces is not accepted.";
        return null;
    }

    // ------------------------------------------------------------------------------------------------ internals

    private static void Export(string sourcePath, string? sourcePassphrase, string targetPath, string? targetPassphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"There is no company book at '{sourcePath}'.", sourcePath);
        if (File.Exists(targetPath))
            throw new IOException($"'{targetPath}' already exists; the vault refuses to write over a file it did not create.");

        var targetDir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);

        try
        {
            // 🔴 ReadWriteCreate, not ReadWrite, and the reason is not obvious: SQLite propagates the main
            // connection's open flags to a database ATTACHed to it, so without SQLITE_OPEN_CREATE the ATTACH
            // below fails with error 14 "unable to open database" — on the TARGET, which does not exist yet
            // and is the whole point of the operation. The source is never created by accident: File.Exists is
            // checked above and this method throws before reaching here if it is missing.
            using (var source = new SqliteConnection(
                ConnectionString(sourcePath, sourcePassphrase, SqliteOpenMode.ReadWriteCreate)))
            {
                source.Open();

                // Reading one row proves the passphrase actually decrypts, BEFORE anything is written. Opening
                // alone does not: SQLCipher defers the key check to the first page read.
                using (var probe = source.CreateCommand())
                {
                    probe.CommandText = "SELECT count(*) FROM sqlite_schema;";
                    probe.ExecuteScalar();
                }

                using var attach = source.CreateCommand();
                // The KEY clause takes a literal, and a passphrase can contain a quote — so it is passed as a
                // bound parameter. Concatenating it into the SQL would be a quoting bug AND put the passphrase
                // into any statement log.
                attach.CommandText = targetPassphrase is null
                    ? "ATTACH DATABASE $t AS apexvault KEY '';"
                    : "ATTACH DATABASE $t AS apexvault KEY $k;";
                attach.Parameters.AddWithValue("$t", targetPath);
                if (targetPassphrase is not null)
                    attach.Parameters.AddWithValue("$k", targetPassphrase);
                attach.ExecuteNonQuery();

                using (var export = source.CreateCommand())
                {
                    export.CommandText = "SELECT sqlcipher_export('apexvault');";
                    export.ExecuteScalar();
                }

                // The user_version PRAGMA is NOT carried by sqlcipher_export — it copies tables, indexes,
                // triggers and views, not the file-header counters. This application keeps its schema version
                // in `user_version` (Schema.CurrentVersion), so losing it would make the migrated book look
                // like a v0 database and re-run the entire migration ladder over objects that already exist.
                var userVersion = ScalarInt(source, "PRAGMA main.user_version;");
                using (var carry = source.CreateCommand())
                {
                    // PRAGMA does not accept a bound parameter for its value; the value here is an int this
                    // code just read out of the same file, never user input.
                    carry.CommandText = $"PRAGMA apexvault.user_version = {userVersion};";
                    carry.ExecuteNonQuery();
                }

                using (var detach = source.CreateCommand())
                {
                    detach.CommandText = "DETACH DATABASE apexvault;";
                    detach.ExecuteNonQuery();
                }
            }

            // Release the pooled handles before the file is touched again, or the verify below (and any later
            // File.Move) hits a live handle on Windows.
            SqliteConnection.ClearAllPools();

            VerifyReadable(targetPath, targetPassphrase);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            TryDelete(targetPath);
            throw;
        }
    }

    /// <summary>
    /// Reopens a freshly written book and reads from it, so no caller is ever handed a file that does not
    /// actually open. Deletes nothing — the caller's catch does that.
    /// </summary>
    private static void VerifyReadable(string path, string? passphrase)
    {
        using var check = new SqliteConnection(ConnectionString(path, passphrase, SqliteOpenMode.ReadWrite));
        check.Open();
        using var cmd = check.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_schema;";
        var tables = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        if (tables == 0)
            throw new InvalidOperationException(
                $"The vault wrote '{path}' but it came back empty; the original book has not been touched.");
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// Opens a book read-only and reads its schema, which is the only honest test of a passphrase.
    ///
    /// <para>🔴 <b>UNPOOLED, and it must stay that way — the probe releases its own handle instead of clearing
    /// everyone's.</b> This originally ended with <c>ClearAllPools()</c> in a <c>finally</c>, to stop the
    /// probe's pooled handle blocking a later delete or move of the file on Windows. That worked, but it is a
    /// read-only probe with a PROCESS-WIDE side effect: <c>CompanyRegistry.Load</c> calls
    /// <see cref="IsVaulted"/> once per candidate file, so listing companies tore down every pooled connection
    /// in the application — including the open company's own book, which then had to be reopened and
    /// re-PRAGMA'd. Declining the pool for this one connection achieves the same handle release, costs nothing
    /// (each call opens exactly once), and leaves other connections alone.</para>
    /// </summary>
    private static bool CanRead(string databasePath, string? passphrase)
    {
        try
        {
            using var c = new SqliteConnection(
                ConnectionString(databasePath, passphrase, SqliteOpenMode.ReadOnly, pooling: false));
            c.Open();
            using var cmd = c.CreateCommand();
            // Reading the schema is what forces page 1 to be decrypted and its HMAC checked. `Open()` alone
            // succeeds on a wrong passphrase, which is why this is a SELECT and not just a connect.
            cmd.CommandText = "SELECT count(*) FROM sqlite_schema;";
            cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { /* best effort — the caller is already throwing */ }
        catch (UnauthorizedAccessException) { /* ditto; POSIX refuses on the parent directory */ }
    }
}
