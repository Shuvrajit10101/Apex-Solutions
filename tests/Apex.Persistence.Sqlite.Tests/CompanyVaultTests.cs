using System;
using System.IO;
using System.Text;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>Census row 16.1 — the crypto half of the Data Vault.</b> Every test here FAILS on today's main,
/// because on main the native provider is <c>e_sqlite3</c>, which has no <c>sqlcipher_export</c>, no
/// <c>PRAGMA cipher_*</c> and no notion of a page key at all — <c>CompanyVault</c> does not exist there.
///
/// <para><b>What these tests are actually for.</b> An encryption feature is the classic place to ship
/// something that LOOKS right and protects nothing: the file changes, the app still works, and nobody checks
/// whether the plaintext is really gone. So the central assertions here read the RAW BYTES of the files on
/// disk and search them for the secret, rather than trusting an API to have done its job.</para>
/// </summary>
public sealed class CompanyVaultTests : IDisposable
{
    private readonly string _dir;

    public CompanyVaultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "apex-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    /// <summary>Writes a plain book carrying a distinctive marker string in a row.</summary>
    private string MakePlain(string file, string marker)
    {
        var path = Path_(file);
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE books(name TEXT); INSERT INTO books VALUES($m); PRAGMA user_version = 63;";
            cmd.Parameters.AddWithValue("$m", marker);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static bool RawBytesContain(string path, string needle)
    {
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(path);
        var n = Encoding.UTF8.GetBytes(needle);
        for (var i = 0; i + n.Length <= bytes.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < n.Length; j++)
                if (bytes[i + j] != n[j]) { hit = false; break; }
            if (hit) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------------------------- the protection itself

    /// <summary>
    /// 🔴 <b>THE LOAD-BEARING TEST OF THE WHOLE ROW.</b> A plain book has the company name sitting in its bytes
    /// in the clear; the vaulted copy does not, and neither does it carry the SQLite header that would let any
    /// tool open it. This is read off the DISK, not asked of the API — an encryption feature that is checked
    /// only by round-tripping through its own code can pass while encrypting nothing.
    /// </summary>
    [Fact]
    public void Encrypting_a_book_removes_the_company_name_from_its_bytes_on_disk()
    {
        const string secret = "Bright Traders Private Limited";
        var plain = MakePlain("plain.db", secret);

        Assert.True(RawBytesContain(plain, secret),
            "the fixture is wrong if the plaintext book does not carry the name in the clear");
        Assert.True(RawBytesContain(plain, "SQLite format 3"));

        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "correct horse battery staple");

        Assert.False(RawBytesContain(vault, secret));
        Assert.False(RawBytesContain(vault, "SQLite format 3"));
    }

    /// <summary>The source book is NOT touched by encryption — that is the whole atomicity argument.</summary>
    [Fact]
    public void Encrypting_leaves_the_source_book_exactly_where_it_was()
    {
        var plain = MakePlain("plain.db", "Acme");
        var before = File.ReadAllBytes(plain);

        CompanyVault.Encrypt(plain, Path_("vault.db"), "passphrase-1");

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, File.ReadAllBytes(plain));
    }

    /// <summary>The right passphrase reads the rows back; the round trip is lossless.</summary>
    [Fact]
    public void The_right_passphrase_reads_the_book_back_unchanged()
    {
        const string secret = "Robert Transport";
        var plain = MakePlain("plain.db", secret);
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "passphrase-1");

        using var c = new SqliteConnection(
            CompanyVault.ConnectionString(vault, "passphrase-1", SqliteOpenMode.ReadOnly));
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM books;";
        Assert.Equal(secret, cmd.ExecuteScalar());
    }

    /// <summary>
    /// 🔴 <b>A wrong passphrase, and NO passphrase, both fail — and there is no third answer.</b> This is what
    /// makes "a forgotten passphrase means the book is gone" a true statement rather than a slogan: the file
    /// simply does not open, and nothing in this application can make it open.
    /// </summary>
    [Fact]
    public void A_wrong_passphrase_and_no_passphrase_both_fail_to_open_the_book()
    {
        var plain = MakePlain("plain.db", "Acme");
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "the-real-passphrase");

        Assert.True(CompanyVault.TryOpen(vault, "the-real-passphrase"));
        Assert.False(CompanyVault.TryOpen(vault, "the-wrong-passphrase"));
        Assert.False(CompanyVault.TryOpen(vault, null));
        Assert.True(CompanyVault.IsVaulted(vault));
        Assert.False(CompanyVault.IsVaulted(plain));
    }

    /// <summary>
    /// 🔴 The schema version rides across the encryption. <c>sqlcipher_export</c> copies tables, indexes,
    /// triggers and views — it does NOT copy the file-header counters, and this application keeps
    /// <c>Schema.CurrentVersion</c> in <c>user_version</c>. Losing it would make a vaulted book look like a v0
    /// database and re-run the whole migration ladder over objects that already exist. That is a silent,
    /// unbounded corruption, so it is pinned here.
    /// </summary>
    [Fact]
    public void The_schema_version_survives_both_directions_of_the_conversion()
    {
        var plain = MakePlain("plain.db", "Acme");   // written at user_version 63
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "passphrase-1");
        Assert.Equal(63, UserVersion(vault, "passphrase-1"));

        var back = Path_("back.db");
        CompanyVault.Decrypt(vault, "passphrase-1", back);
        Assert.Equal(63, UserVersion(back, null));
    }

    private static int UserVersion(string path, string? passphrase)
    {
        using var c = new SqliteConnection(
            CompanyVault.ConnectionString(path, passphrase, SqliteOpenMode.ReadOnly));
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Leaving the vault restores a readable plain book with the name back in the clear.</summary>
    [Fact]
    public void Decrypting_restores_a_plain_readable_book()
    {
        const string secret = "Bright Traders";
        var plain = MakePlain("plain.db", secret);
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "passphrase-1");

        var back = Path_("back.db");
        CompanyVault.Decrypt(vault, "passphrase-1", back);

        Assert.True(RawBytesContain(back, secret));
        Assert.False(CompanyVault.IsVaulted(back));
    }

    /// <summary>Changing the passphrase makes the OLD one stop working and the NEW one start.</summary>
    [Fact]
    public void Changing_the_passphrase_retires_the_old_one()
    {
        var plain = MakePlain("plain.db", "Acme");
        var v1 = Path_("v1.db");
        CompanyVault.Encrypt(plain, v1, "passphrase-one");

        var v2 = Path_("v2.db");
        CompanyVault.ChangePassphrase(v1, "passphrase-one", v2, "passphrase-two");

        Assert.True(CompanyVault.TryOpen(v2, "passphrase-two"));
        Assert.False(CompanyVault.TryOpen(v2, "passphrase-one"));
    }

    // ------------------------------------------------------------------------------------------ failure paths

    /// <summary>
    /// 🔴 <b>THE FAILURE PATH, TESTED RATHER THAN ASSERTED.</b> A wrong passphrase on the SOURCE must leave no
    /// target behind — a half-written file that nobody can open is exactly the "half-vaulted company is a lost
    /// company" shape, and it would be adopted as a company by the registry if it survived.
    /// </summary>
    [Fact]
    public void A_failed_conversion_deletes_its_partial_output_and_leaves_the_source_intact()
    {
        var plain = MakePlain("plain.db", "Acme");
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "the-real-passphrase");

        var target = Path_("target.db");
        Assert.ThrowsAny<Exception>(() => CompanyVault.Decrypt(vault, "the-wrong-passphrase", target));

        Assert.False(File.Exists(target));
        Assert.True(CompanyVault.TryOpen(vault, "the-real-passphrase"));
    }

    /// <summary>The vault never writes over a file it did not create.</summary>
    [Fact]
    public void A_conversion_refuses_to_overwrite_an_existing_file()
    {
        var plain = MakePlain("plain.db", "Acme");
        var occupied = MakePlain("occupied.db", "Someone Else");

        Assert.Throws<IOException>(() => CompanyVault.Encrypt(plain, occupied, "passphrase-1"));
        Assert.True(RawBytesContain(occupied, "Someone Else"));
    }

    // -------------------------------------------------------------------------------------- the stated policy

    /// <summary>
    /// 🔴 <b>The KDF parameters this project DOCUMENTS are the ones the shipped native actually uses.</b> A
    /// hand-lowered <c>kdf_iter</c> — or a provider bump that quietly changes the defaults — is the classic way
    /// to ship an encryption feature that is weaker than its own documentation claims. These five values are
    /// written down in <c>CompanyVault</c>'s remarks and are read back off a live keyed connection here, so the
    /// documentation cannot drift away from the code without this failing.
    /// </summary>
    [Fact]
    public void The_shipped_native_uses_the_kdf_parameters_this_class_documents()
    {
        var plain = MakePlain("plain.db", "Acme");
        var vault = Path_("vault.db");
        CompanyVault.Encrypt(plain, vault, "passphrase-1");

        using var c = new SqliteConnection(
            CompanyVault.ConnectionString(vault, "passphrase-1", SqliteOpenMode.ReadOnly));
        c.Open();

        Assert.Equal("PBKDF2_HMAC_SHA512", Pragma(c, "cipher_kdf_algorithm"));
        Assert.Equal("HMAC_SHA512", Pragma(c, "cipher_hmac_algorithm"));
        Assert.Equal("256000", Pragma(c, "kdf_iter"));
        Assert.Equal("4096", Pragma(c, "cipher_page_size"));
        Assert.StartsWith("4.", Pragma(c, "cipher_version"));
    }

    private static string Pragma(SqliteConnection c, string name)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA " + name + ";";
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    /// <summary>
    /// Two books encrypted with the SAME passphrase do not share a salt, so identical content does not produce
    /// identical files. Without a per-file salt an attacker could tell two books apart — or tell that they are
    /// the same book — without knowing the passphrase.
    /// </summary>
    [Fact]
    public void Two_books_under_one_passphrase_do_not_produce_identical_files()
    {
        var a = MakePlain("a.db", "Same Content");
        var b = MakePlain("b.db", "Same Content");
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));   // identical in the clear

        var va = Path_("va.db");
        var vb = Path_("vb.db");
        CompanyVault.Encrypt(a, va, "one-passphrase-for-both");
        CompanyVault.Encrypt(b, vb, "one-passphrase-for-both");

        // A pooled connection still holds the OS handle on Windows; reading the raw file needs it released.
        SqliteConnection.ClearAllPools();
        Assert.NotEqual(File.ReadAllBytes(va), File.ReadAllBytes(vb));
    }

    /// <summary>The weak-passphrase floor is OURS and is stated as such; it is enforced, not advisory.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("        ", false)]
    [InlineData("long-enough-passphrase", true)]
    public void The_passphrase_floor_is_enforced(string passphrase, bool acceptable)
        => Assert.Equal(acceptable, CompanyVault.DescribeWeakness(passphrase) is null);

    /// <summary>
    /// 🔴 The user-visible name of this feature never carries the vendor's brand (R7). Checked on the constant
    /// every screen, menu row and message reads from, so no screen can reintroduce it independently.
    /// </summary>
    [Fact]
    public void The_feature_name_carries_no_vendor_brand()
    {
        Assert.DoesNotContain("Tally", CompanyVault.FeatureName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", CompanyVault.IrreversibilityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", CompanyVault.MaskedName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 The irreversibility is SAID, in the string the screen actually shows. The vendor states it
    /// repeatedly; an operator who learns it after setting a passphrase has already taken the risk.
    /// </summary>
    [Fact]
    public void The_warning_says_a_forgotten_passphrase_cannot_be_recovered()
    {
        var w = CompanyVault.IrreversibilityWarning;
        Assert.Contains("never stored", w, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no reset", w, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forgotten", w, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------------- the store's own passphrase

    /// <summary>
    /// <see cref="SqliteCompanyStore"/> opens a vaulted book with its passphrase and REFUSES without it — and
    /// the refusal arrives from the CONSTRUCTOR, not from some later query. SQLCipher defers the key check to
    /// the first page read, so a store that only called <c>Open()</c> would hand back a live object that
    /// explodes somewhere else entirely.
    /// </summary>
    [Fact]
    public void The_store_opens_a_vaulted_book_only_with_its_passphrase()
    {
        var plain = Path_("company.db");
        using (var store = new SqliteCompanyStore(plain)) { }
        SqliteConnection.ClearAllPools();

        var vault = Path_("company-vault.db");
        CompanyVault.Encrypt(plain, vault, "passphrase-1");

        using (var ok = new SqliteCompanyStore(vault, "passphrase-1")) { }

        Assert.ThrowsAny<SqliteException>(() => new SqliteCompanyStore(vault, "wrong-passphrase"));
    }

    /// <summary>
    /// 🔴🔴 <b>A KNOWN, DELIBERATE LIMITATION, PINNED SO IT CANNOT BE MISTAKEN FOR A WORKING PATH: a VAULTED
    /// company CANNOT BE BACKED UP by <see cref="CompanyBackup"/>, and the attempt is REFUSED rather than
    /// half-done.</b>
    ///
    /// <para><b>Why it is refused and not supported.</b> <c>CompanyBackup.Create</c> opens the source with no
    /// passphrase and reads the company NAME out of it to stamp into the backup manifest. Neither is possible
    /// for a vaulted book, and "fixing" it naively would be worse than the refusal: a manifest carrying the
    /// company name in the clear is exactly the plaintext this row exists to remove, sitting in a file the
    /// operator is likely to copy somewhere less protected than the book itself.</para>
    ///
    /// <para><b>What this test therefore asserts is the REFUSAL, and that it is clean</b> — it throws, and it
    /// does not leave a partial archive behind for someone to mistake for a backup. This is a gap in the
    /// feature, it is reported as one, and it is written down here rather than discovered by a user whose
    /// backup silently was not one. Making vault-aware backup work is separate scope.</para>
    /// </summary>
    [Fact]
    public void A_vaulted_book_cannot_be_backed_up_and_the_refusal_leaves_no_partial_archive()
    {
        var plain = MakePlain("backup-me.db", "Vaulted Backup Traders");
        var vault = Path_("backup-me-vault.db");
        CompanyVault.Encrypt(plain, vault, "a-long-enough-passphrase");
        SqliteConnection.ClearAllPools();

        var archive = Path_("backup.zip");
        // 🔴 The REFUSAL is what is pinned, so the exception is named rather than caught as "anything".
        // ThrowsAny<Exception> would also be satisfied by a NullReferenceException from a future refactor —
        // i.e. by the feature breaking in a different way — which is not what this test is claiming.
        var refusal = Assert.ThrowsAny<Exception>(
            () => CompanyBackup.Create(vault, archive, DateTimeOffset.UnixEpoch));
        Assert.True(refusal is CompanyBackupException or SqliteException,
            $"the refusal arrived as {refusal.GetType().Name}, which is a crash rather than a refusal: "
            + refusal.Message);

        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(archive),
            "the refused backup left an archive behind; a file that looks like a backup and is not is worse "
            + "than no file at all.");

        // 🔴 And the refusal must not have leaked the name into anything it did write.
        foreach (var path in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            Assert.False(RawBytesContain(path, "Vaulted Backup Traders") && path != plain,
                $"'{Path.GetFileName(path)}' carries the company name after a refused backup of a vaulted book.");
    }
}
