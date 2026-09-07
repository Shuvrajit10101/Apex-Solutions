using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// <b>Verify Data as a verb of its own</b> — <see cref="CompanyBackup.Verify"/>, the check behind
/// <c>Alt+Y (Data) &gt; Split &gt; Verify Data</c> (census 16.5's precondition and census 16.6's missing
/// standalone verify).
///
/// <para>Until this shipped, <c>PRAGMA integrity_check</c> ran only as a hidden gate <i>inside</i> backup and
/// restore: there was no way to ask "is this book sound?" without writing an archive. These tests pin the four
/// answers that matter — clean, not-ours, unreadable, and damaged — and, in particular, that <b>a verification
/// never writes to the file it is checking</b>.</para>
/// </summary>
public sealed class DataVerificationTests : IDisposable
{
    private readonly string _dir;

    public DataVerificationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ApexVerifyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) { SqliteConnection.ClearAllPools(); Thread.Sleep(25); }
            catch (UnauthorizedAccessException) { SqliteConnection.ClearAllPools(); Thread.Sleep(25); }
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private string SavedCompany(string name = "Verify Co")
    {
        var c = CompanyFactory.CreateSeeded(name, new DateOnly(2025, 4, 1));
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1_234.56m);
        cash.OpeningIsDebit = true;
        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(1_234.56m), openingIsDebit: false);
        c.AddLedger(capital);

        var path = PathFor(name + ".db");
        using (var store = new SqliteCompanyStore(path)) store.Save(c);
        SqliteConnection.ClearAllPools();
        return path;
    }

    [Fact]
    public void A_sound_company_database_verifies_clean()
    {
        var path = SavedCompany();

        var result = CompanyBackup.Verify(path);

        Assert.True(result.Ok, result.Summary);
        Assert.Empty(result.Findings);
        Assert.Equal(Schema.CurrentVersion, result.SchemaVersion);
        Assert.Equal("Verify Co", result.CompanyName);
        Assert.Contains("No errors found", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 The property that lets this run on the book the operator is standing in: verification opens the file
    /// read-only and changes nothing. Proven on the BYTES, not on an intention — the whole file is hashed before
    /// and after, and its last-write time is compared too.
    /// </summary>
    [Fact]
    public void Verifying_does_not_write_to_the_file_it_checks()
    {
        var path = SavedCompany("Untouched Co");
        var before = File.ReadAllBytes(path);
        var writtenAt = File.GetLastWriteTimeUtc(path);

        Assert.True(CompanyBackup.Verify(path).Ok);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_file_that_is_not_a_company_database_is_named_as_such()
    {
        var path = PathFor("notes.db");
        File.WriteAllText(path, "This is not a database. Not even close.", Encoding.UTF8);

        var result = CompanyBackup.Verify(path);

        Assert.False(result.Ok);
        Assert.NotEmpty(result.Findings);
        Assert.Contains("notes.db", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_reported_rather_than_thrown()
    {
        var result = CompanyBackup.Verify(PathFor("nothing-here.db"));

        Assert.False(result.Ok);
        Assert.Contains("no file", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_path_is_reported_rather_than_thrown()
    {
        var result = CompanyBackup.Verify("   ");
        Assert.False(result.Ok);
        Assert.NotEmpty(result.Findings);
    }

    /// <summary>
    /// A SQLite file that is ours by stamp but carries a data-format version this build cannot read is refused
    /// by number, reusing the same message the restore path shows.
    /// </summary>
    [Fact]
    public void A_newer_data_format_is_refused_by_number()
    {
        var path = SavedCompany("Future Co");
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_version SET version = $v;";
            cmd.Parameters.AddWithValue("$v", Schema.CurrentVersion + 7);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var result = CompanyBackup.Verify(path);

        Assert.False(result.Ok);
        Assert.Equal(Schema.CurrentVersion + 7, result.SchemaVersion);
        Assert.Contains((Schema.CurrentVersion + 7).ToString(), result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The finding no other path in this codebase produces: a row pointing at a parent row that is not there.
    /// The orphan is created with foreign keys OFF (which is how such a row gets into a real file — a hand
    /// edit, or a build whose FK enforcement was off), and the check finds it with them back on.
    /// </summary>
    [Fact]
    public void An_orphan_row_is_reported()
    {
        var path = SavedCompany("Orphan Co");

        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var off = conn.CreateCommand();
            off.CommandText = "PRAGMA foreign_keys = OFF;";
            off.ExecuteNonQuery();

            using var orphan = conn.CreateCommand();
            orphan.CommandText =
                "UPDATE ledgers SET group_id = $missing WHERE name = 'Capital A/c';";
            orphan.Parameters.AddWithValue("$missing", Guid.NewGuid().ToString());
            orphan.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var result = CompanyBackup.Verify(path);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Contains("Orphan row", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("ledgers", StringComparison.Ordinal));
    }
}
