using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-3 <b>Composition scheme</b> schema contract (v39→v40; ER-13). The bump is additive: two ALTER-added
/// columns on <c>companies</c> (<c>composition_sub_type</c>, <c>composition_opt_in_date</c>), no new table. Covers: a
/// fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the two new columns; a legacy v39 DB auto-migrates
/// forward preserving every row (new columns default NULL — ER-13) and re-opening is idempotent; and a save/load
/// round-trips a Composition company (sub-type + opt-in date exact).
/// </summary>
public sealed class CompositionSchemaTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_columns()
    {
        var dbPath = TempDb("apex-comp-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 40);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "composition_sub_type", "composition_opt_in_date" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v39_database_auto_migrates_to_v40_preserving_every_row()
    {
        var dbPath = TempDb("apex-comp-v39legacy");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV39Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (39);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ('c-1', 'Legacy V39 Co');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(39L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("composition_sub_type", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v39 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("composition_sub_type", ColumnNames(dbPath, "companies"));
            Assert.Contains("composition_opt_in_date", ColumnNames(dbPath, "companies"));

            // Every existing row survived; the new columns default NULL (ER-13).
            Assert.Equal("Legacy V39 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies WHERE composition_sub_type IS NULL;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies WHERE composition_opt_in_date IS NULL;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_a_composition_company()
    {
        var dbPath = TempDb("apex-comp-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Composition Co", new DateOnly(2025, 4, 1));
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
                CompositionSubType = CompositionSubType.Restaurant, CompositionOptInDate = new DateOnly(2025, 4, 1),
                ApplicableFrom = new DateOnly(2025, 4, 1), Periodicity = GstReturnPeriodicity.Quarterly,
            });

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            Assert.Equal(GstRegistrationType.Composition, reloaded.Gst!.RegistrationType);
            Assert.Equal(CompositionSubType.Restaurant, reloaded.Gst!.CompositionSubType);
            Assert.Equal(new DateOnly(2025, 4, 1), reloaded.Gst!.CompositionOptInDate);

            // A composition company has NO auto-created GST tax ledgers (gated off at EnableGst).
            Assert.DoesNotContain(reloaded.Ledgers, l => l.GstClassification is not null);
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);
    private static long ReadSchemaVersion(string dbPath) => ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;");

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static string ReadScalarStr(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A minimal pre-v40 (v39) DDL: the v39→v40 migration only ALTERs <c>companies</c>, so a bare
    /// <c>schema_version</c> + <c>companies</c> is enough to exercise the migration and its data-preservation.</summary>
    private const string MinimalV39Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;
}
