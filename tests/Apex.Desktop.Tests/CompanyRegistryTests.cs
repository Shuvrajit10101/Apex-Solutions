using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census row 16.1 — the REGISTRY half: the part that stops a company's FILENAME from leaking the very
/// name the vault exists to hide.</b> Every test here fails on today's main, where <c>CompanyRegistry</c> does
/// not exist and <c>CompanyStorage.ListCompanies</c> reads names straight out of <c>*.db</c> filenames.
///
/// <para><b>The two tests that matter most are the ones that read the DISK</b> —
/// <see cref="A_vaulted_company_leaves_no_trace_of_its_name_anywhere_on_disk"/> and the two crash-recovery
/// tests. An encryption feature can pass every round-trip test in the world while leaving the plaintext copy
/// sitting beside the ciphertext, and that failure is invisible from inside the API.</para>
/// </summary>
public sealed class CompanyRegistryTests : IDisposable
{
    private readonly string _dir;
    private readonly CompanyStorage _storage;

    public CompanyRegistryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "apex-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _storage = new CompanyStorage(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Company MakeCompany(string name)
    {
        var company = CompanyFactory.CreateSeeded(name);
        _storage.Save(company);
        SqliteConnection.ClearAllPools();
        return company;
    }

    private static bool AnyFileUnderContains(string dir, string needle)
    {
        SqliteConnection.ClearAllPools();
        var n = Encoding.UTF8.GetBytes(needle);
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch (IOException) { continue; }
            for (var i = 0; i + n.Length <= bytes.Length; i++)
            {
                var hit = true;
                for (var j = 0; j < n.Length; j++)
                    if (bytes[i + j] != n[j]) { hit = false; break; }
                if (hit) return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------------------------- backwards compatible

    /// <summary>
    /// 🔴 <b>An installation that predates the registry is adopted silently and keeps every name.</b> This is
    /// what makes the identity change invisible to books already on disk: for a PLAIN company the registry
    /// records precisely what the filename already said, so there is no migration step to go wrong.
    /// </summary>
    [Fact]
    public void Plain_books_already_on_disk_are_adopted_with_their_names_intact()
    {
        MakeCompany("Acme Traders");
        MakeCompany("Bright Trading");

        // Throw the registry away, exactly as an upgrade from a build that had none would present.
        File.Delete(Path.Combine(_dir, CompanyRegistry.FileName));
        SqliteConnection.ClearAllPools();

        var listed = new CompanyStorage(_dir).ListCompanies();

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, e => e.Name == "Acme Traders" && !e.IsVaulted);
        Assert.Contains(listed, e => e.Name == "Bright Trading" && !e.IsVaulted);
    }

    /// <summary>The registry file is itself never listed as a company.</summary>
    [Fact]
    public void The_registry_file_is_not_a_company()
    {
        MakeCompany("Acme Traders");
        Assert.DoesNotContain(_storage.ListCompanies(), e => e.Name.Contains("companies.index"));
    }

    // ----------------------------------------------------------------------------------------- the leak, closed

    /// <summary>
    /// 🔴🔴 <b>THE TEST THIS WHOLE ROW EXISTS FOR.</b> After a company is vaulted, its name appears in NO file
    /// anywhere under the companies directory — not in a filename, not in the encrypted book, and not in the
    /// registry that maps files to names. It is checked by scanning the raw bytes of EVERY file in the tree,
    /// which is the only way to catch the failure that matters: a plaintext copy left behind.
    ///
    /// <para>On today's main this fails at the first assertion, because the company's file is literally called
    /// <c>Bright Trading Private Limited.db</c>.</para>
    /// </summary>
    [Fact]
    public void A_vaulted_company_leaves_no_trace_of_its_name_anywhere_on_disk()
    {
        const string secret = "Bright Trading Private Limited";
        var company = MakeCompany(secret);
        var entry = _storage.ListCompanies().Single();

        Assert.True(AnyFileUnderContains(_dir, secret), "fixture: the plain book must carry the name");

        _storage.EnableVault(entry, company, "a-long-enough-passphrase");
        SqliteConnection.ClearAllPools();

        // No FILENAME carries it…
        Assert.DoesNotContain(
            Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories),
            p => Path.GetFileName(p).Contains("Bright", StringComparison.OrdinalIgnoreCase));

        // …and no file's CONTENT carries it either — book, registry or anything else.
        Assert.False(AnyFileUnderContains(_dir, secret),
            "the company name survived somewhere on disk after being vaulted");
    }

    /// <summary>
    /// The vaulted company is still LISTED — as asterisks with its number, the vendor's own behaviour — rather
    /// than vanishing. "Encrypted but unopenable" is the other way to fail this row and it is not shipped.
    /// </summary>
    [Fact]
    public void A_vaulted_company_is_listed_as_asterisks_with_its_number_still_visible()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        var number = entry.Number;

        _storage.EnableVault(entry, company, "a-long-enough-passphrase");

        var listed = new CompanyStorage(_dir).ListCompanies().Single();
        Assert.True(listed.IsVaulted);
        Assert.Equal(CompanyVault.MaskedName, listed.Name);
        Assert.Equal(number, listed.Number);
    }

    /// <summary>
    /// 🔴 <b>Enabling the vault does not leave a plaintext copy behind — the single most likely way to ship
    /// this feature broken.</b> Exactly one company file remains, and it is the encrypted one.
    /// </summary>
    [Fact]
    public void Enabling_the_vault_removes_the_plaintext_book()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        var plainPath = entry.DatabasePath;

        var vaulted = _storage.EnableVault(entry, company, "a-long-enough-passphrase");
        SqliteConnection.ClearAllPools();

        Assert.False(File.Exists(plainPath));
        Assert.Single(Directory.EnumerateFiles(_dir, "*.db"));
        Assert.True(CompanyVault.IsVaulted(vaulted.DatabasePath));
    }

    // ------------------------------------------------------------------------------------------ open and save

    /// <summary>A vaulted company opens with its passphrase, and its real name comes back out of the file.</summary>
    [Fact]
    public void A_vaulted_company_opens_with_its_passphrase_and_recovers_its_real_name()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        _storage.EnableVault(entry, company, "a-long-enough-passphrase");

        var fresh = new CompanyStorage(_dir);
        var listed = fresh.ListCompanies().Single();

        var reopened = fresh.Load(listed, "a-long-enough-passphrase");
        Assert.Equal("Acme Traders", reopened.Name);
    }

    /// <summary>Opening a vaulted company without — or with a wrong — passphrase is refused, with a message.</summary>
    [Fact]
    public void A_vaulted_company_refuses_the_wrong_passphrase_and_no_passphrase()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        _storage.EnableVault(entry, company, "a-long-enough-passphrase");

        var fresh = new CompanyStorage(_dir);
        var listed = fresh.ListCompanies().Single();

        Assert.Throws<InvalidOperationException>(() => fresh.Load(listed, null));
        Assert.Throws<InvalidOperationException>(() => fresh.Load(listed, "not-the-passphrase"));
    }

    /// <summary>
    /// 🔴 <b>SAVING A VAULTED COMPANY WRITES BACK INTO THE VAULT — it does not create a new plaintext book
    /// named after it.</b> This is the defect the open-book session on <c>CompanyStorage</c> exists to
    /// prevent: every save path in that class derives its file from the company NAME, which for a vaulted book
    /// points at a file that does not exist. Without the session the first save after opening would silently
    /// un-vault the company and put its name back into a filename.
    /// </summary>
    [Fact]
    public void Saving_an_open_vaulted_company_writes_back_into_the_vault()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        _storage.EnableVault(entry, company, "a-long-enough-passphrase");

        var fresh = new CompanyStorage(_dir);
        var listed = fresh.ListCompanies().Single();
        var reopened = fresh.Load(listed, "a-long-enough-passphrase");

        fresh.Save(reopened);
        SqliteConnection.ClearAllPools();

        Assert.Single(Directory.EnumerateFiles(_dir, "*.db"));
        Assert.False(AnyFileUnderContains(_dir, "Acme Traders"));
        Assert.Single(new CompanyStorage(_dir).ListCompanies());
    }

    /// <summary>Changing the passphrase retires the old one and the book still opens under the new one.</summary>
    [Fact]
    public void Changing_the_passphrase_keeps_exactly_one_book_and_retires_the_old_passphrase()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        var vaulted = _storage.EnableVault(entry, company, "first-passphrase");

        var changed = _storage.ChangeVaultPassphrase(vaulted, "first-passphrase", "second-passphrase");
        SqliteConnection.ClearAllPools();

        Assert.Single(Directory.EnumerateFiles(_dir, "*.db"));
        Assert.True(CompanyVault.TryOpen(changed.DatabasePath, "second-passphrase"));
        Assert.False(CompanyVault.TryOpen(changed.DatabasePath, "first-passphrase"));
    }

    /// <summary>Leaving the vault restores a plain book under the company's real name.</summary>
    [Fact]
    public void Leaving_the_vault_restores_a_plain_book_named_after_the_company()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        var vaulted = _storage.EnableVault(entry, company, "a-long-enough-passphrase");

        var plain = _storage.DisableVault(vaulted, company, "a-long-enough-passphrase");
        SqliteConnection.ClearAllPools();

        Assert.False(plain.IsVaulted);
        Assert.Equal("Acme Traders", plain.Name);
        Assert.Single(Directory.EnumerateFiles(_dir, "*.db"));
        Assert.Single(new CompanyStorage(_dir).ListCompanies());
    }

    // ----------------------------------------------------------------------------- 🔴 the crash / failure paths

    /// <summary>
    /// 🔴🔴 <b>A CRASH AFTER THE REGISTRY COMMITTED BUT BEFORE THE PLAINTEXT WAS DELETED.</b> This is the
    /// dangerous state: the vault is live, and a plaintext copy of the whole book — carrying the name in its
    /// filename AND its bytes — is still sitting in the directory. Recovery must finish the delete, and must
    /// NOT adopt that file as a second company in the meantime.
    ///
    /// <para>The crash is SIMULATED by writing the journal state by hand and putting the plaintext back, which
    /// is exactly the on-disk state a power cut at that instant produces.</para>
    /// </summary>
    [Fact]
    public void Recovery_finishes_a_commit_that_crashed_before_the_plaintext_was_removed()
    {
        const string secret = "Acme Traders";
        var company = MakeCompany(secret);
        var entry = _storage.ListCompanies().Single();
        var plainFile = Path.GetFileName(entry.DatabasePath);

        var vaulted = _storage.EnableVault(entry, company, "a-long-enough-passphrase");
        SqliteConnection.ClearAllPools();

        // Re-create the exact on-disk state of a crash between the commit and the delete.
        File.WriteAllBytes(Path.Combine(_dir, plainFile), MakeNamedPlainBook(secret));
        WriteJournal(vaulted.Number, plainFile, Path.GetFileName(vaulted.DatabasePath), "committed");

        var listed = new CompanyStorage(_dir).ListCompanies();

        Assert.Single(listed);                                   // NOT two companies
        Assert.True(listed[0].IsVaulted);
        Assert.False(File.Exists(Path.Combine(_dir, plainFile))); // the plaintext is gone
        Assert.False(AnyFileUnderContains(_dir, secret));
    }

    /// <summary>
    /// 🔴🔴 <b>A CRASH BEFORE THE REGISTRY COMMITTED.</b> The original plaintext book is still the registered
    /// one and is intact; the half-written vault file is garbage. Recovery must delete the garbage and leave
    /// the company exactly as it was — not vaulted, not lost, and listed once.
    /// </summary>
    [Fact]
    public void Recovery_unwinds_a_staging_transition_and_the_original_book_survives()
    {
        MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();
        var plainFile = Path.GetFileName(entry.DatabasePath);

        // A staged, never-committed transition: the target file exists but the registry never moved.
        var orphan = Guid.NewGuid().ToString("N") + ".db";
        File.WriteAllBytes(Path.Combine(_dir, orphan), new byte[] { 1, 2, 3, 4 });
        WriteJournal(entry.Number, plainFile, orphan, "staging");

        var listed = new CompanyStorage(_dir).ListCompanies();

        Assert.Single(listed);
        Assert.Equal("Acme Traders", listed[0].Name);
        Assert.False(listed[0].IsVaulted);
        Assert.True(File.Exists(Path.Combine(_dir, plainFile)));
        Assert.False(File.Exists(Path.Combine(_dir, orphan)));
    }

    /// <summary>
    /// A failure DURING the encryption (here: a passphrase below the floor) leaves the company completely
    /// untouched — still plaintext, still listed under its own name, with no stray file.
    /// </summary>
    [Fact]
    public void A_refused_vault_operation_leaves_the_company_exactly_as_it_was()
    {
        var company = MakeCompany("Acme Traders");
        var entry = _storage.ListCompanies().Single();

        Assert.ThrowsAny<Exception>(() => _storage.EnableVault(entry, company, "short"));
        SqliteConnection.ClearAllPools();

        var listed = new CompanyStorage(_dir).ListCompanies();
        Assert.Single(listed);
        Assert.Equal("Acme Traders", listed[0].Name);
        Assert.False(listed[0].IsVaulted);
        Assert.Single(Directory.EnumerateFiles(_dir, "*.db"));
    }

    /// <summary>
    /// 🔴 Two vaulted companies stay distinguishable — each keeps its own number — even though neither has a
    /// name. That is the whole reason the vendor keeps the number visible, and the reason the registry
    /// allocates one rather than deriving display order from the filename.
    /// </summary>
    [Fact]
    public void Two_vaulted_companies_remain_distinguishable_by_number()
    {
        var first = MakeCompany("Acme Traders");
        var second = MakeCompany("Bright Trading");

        foreach (var e in _storage.ListCompanies().ToList())
        {
            var company = e.Name == "Acme Traders" ? first : second;
            _storage.EnableVault(e, company, "passphrase-for-" + e.Number);
            _storage.CloseVault();
        }

        var listed = new CompanyStorage(_dir).ListCompanies();
        Assert.Equal(2, listed.Count);
        Assert.All(listed, e => Assert.True(e.IsVaulted));
        Assert.All(listed, e => Assert.Equal(CompanyVault.MaskedName, e.Name));
        Assert.Equal(2, listed.Select(e => e.Number).Distinct().Count());
    }

    // --------------------------------------------------------------------------------------------- helpers

    /// <summary>A plain SQLite book carrying <paramref name="name"/> in its bytes — a stand-in for the
    /// plaintext copy a crash would leave behind.</summary>
    private byte[] MakeNamedPlainBook(string name)
    {
        var tmp = Path.Combine(_dir, "scratch-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE companies(name TEXT); INSERT INTO companies VALUES($n);";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return bytes;
    }

    /// <summary>Writes a journal row by hand — the on-disk state a crash mid-transition produces.</summary>
    private void WriteJournal(int number, string source, string target, string state)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_dir, CompanyRegistry.FileName),
        }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pending (number, source_file, target_file, state) VALUES ($no, $s, $t, $st)
            ON CONFLICT(number) DO UPDATE SET source_file = $s, target_file = $t, state = $st;
            """;
        cmd.Parameters.AddWithValue("$no", number);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$t", target);
        cmd.Parameters.AddWithValue("$st", state);
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }
}
