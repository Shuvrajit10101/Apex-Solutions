using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v49 → v50 — the <b>negative-stock warning toggle</b> (plan.md NS-4). Proves what this version bump owes:
/// the new <c>companies.warn_on_negative_stock</c> column round-trips both states; a genuine v49 database migrates
/// up matching a fresh <see cref="Schema.CreateV1"/> on that addition; and — the point of the whole file — every
/// pre-existing v49 company migrates to warnings <b>ON</b>.
///
/// <para>⚠️ <b>THE DEFAULT-TRUE ASYMMETRY.</b> Every other flag column in this schema is <c>DEFAULT 0</c>, so
/// "column absent" and "flag off" mean the same thing and the migration back-fill is untestable in practice — you
/// cannot tell a correct back-fill from a forgotten one. This column is <c>DEFAULT 1</c>, so they mean OPPOSITE
/// things: a migration that used <c>DEFAULT 0</c> (or a loader that read a missing value as <c>default(bool)</c>)
/// would silently switch negative-stock warnings OFF on every upgraded book, and nothing would notice until a user
/// oversold without being told. <see cref="ExistingV49Companies_migrateTo_warningsON"/> is the test that fails if
/// that happens — it is deliberately asserted on a company whose flag was explicitly turned OFF before the
/// downgrade, so a back-fill that merely preserved the old value could not pass it by accident.</para>
///
/// <para>The "genuine v49 database" is manufactured with <see cref="SchemaDowngrade.V50ToV49"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
/// </summary>
public sealed class NegativeStockFlagSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= round-trip (both states)

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void WarnOnNegativeStock_roundTrips_sqlite_in_both_states()
    {
        foreach (var warn in new[] { true, false })
        {
            var dbPath = TempDbFile.NewPath($"apex-negstock-roundtrip-{warn}");
            try
            {
                var company = CompanyFactory.CreateSeeded("Neg Stock Co", FyStart);
                Assert.True(company.WarnOnNegativeStock);          // the domain default, before we touch it
                company.WarnOnNegativeStock = warn;

                using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
                Assert.Equal(warn ? 1L : 0L, ReadScalar(dbPath,
                    $"SELECT warn_on_negative_stock FROM companies WHERE id = '{company.Id:D}';"));

                using var reopened = new SqliteCompanyStore(dbPath);
                Assert.Equal(warn, reopened.Load(company.Id)!.WarnOnNegativeStock);
            }
            finally { TempDbFile.Delete(dbPath); }
        }
    }

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v49_to_v50_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-negstock-v49-migrated");
        var freshPath = TempDbFile.NewPath("apex-negstock-v50-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Neg Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            // Manufacture a genuine v49 database: save at v50, then downgrade (drop the flag column).
            var legacy = CompanyFactory.CreateSeeded("Legacy Neg Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);   // v51 GST five-level hierarchy masters
                SchemaDowngrade.V50ToV49(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(49L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("warn_on_negative_stock", ColumnNames(migratedPath, "companies"));

            // Reopen through the production store — the v49 → v50 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The migration's own addition matches the fresh CreateV1 schema exactly — same declared type, NOT NULL
            // and DEFAULT literal. THE DEFAULT IS THE POINT: this comparison is what pins DEFAULT 1 on both sides.
            // (The downgrade rebuilds companies via CREATE … AS SELECT, which erases the PRE-EXISTING columns'
            // declared type/notnull — an artifact of the downgrade helper, not the migration — so we compare only
            // what the v49→v50 migration itself adds. Whole-schema parity across the full chain is separately
            // guaranteed by SchemaMigrationEquivalenceTests.)
            foreach (var col in Schema.V50NegativeStockColumns)
            {
                var contract = ColumnContract(freshPath, "companies", col);
                Assert.Contains("default=1", contract, StringComparison.Ordinal);
                Assert.Equal(contract, ColumnContract(migratedPath, "companies", col));
            }
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= ⚠️ the default-true back-fill

    /// <summary>
    /// The promise of this bump, and the one assertion that cannot be satisfied by accident. The company is saved
    /// with the flag <b>explicitly OFF</b>, then downgraded to a genuine v49 database — at which point the setting
    /// no longer exists anywhere, which is exactly the state every real pre-v50 book is in. Migrating up must yield
    /// warnings <b>ON</b>, from the column's own <c>DEFAULT 1</c>.
    ///
    /// <para>Starting from OFF is deliberate: had the fixture been saved with the flag at its default (ON), the test
    /// would also pass against a <c>DEFAULT 0</c> migration that somehow preserved the old byte, and against a
    /// loader bug that ignored the column entirely and fell through to the domain default. Starting from OFF makes
    /// TRUE reachable only through the back-fill.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void ExistingV49Companies_migrateTo_warningsON()
    {
        var dbPath = TempDbFile.NewPath("apex-negstock-legacy-rows");
        try
        {
            var company = CompanyFactory.CreateSeeded("Legacy Book", FyStart);
            company.WarnOnNegativeStock = false;                       // explicitly OFF before the downgrade
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT warn_on_negative_stock FROM companies;"));

            // Drop back to a genuine v49 database — the column and the captured setting are gone, exactly the
            // state a user's pre-v50 database is in.
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);   // v51 GST five-level hierarchy masters
                SchemaDowngrade.V50ToV49(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies;"));   // the row survived

            // Migrate up through the production store and read the company back.
            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The stored byte is 1 …
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT warn_on_negative_stock FROM companies;"));
            // … and it survives the load path, rather than being re-derived from a bool default that happens to agree.
            Assert.True(reopened.Load(company.Id)!.WarnOnNegativeStock);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v50_to_v49_dropsColumn_preservesCompanyRow()
    {
        var dbPath = TempDbFile.NewPath("apex-negstock-downgrade");
        try
        {
            var company = CompanyFactory.CreateSeeded("Downgrade Co", FyStart);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);   // v51 GST five-level hierarchy masters
                SchemaDowngrade.V50ToV49(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(49L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies;"));
            Assert.DoesNotContain("warn_on_negative_stock", ColumnNames(dbPath, "companies"));
            // The v43 columns below it are untouched by this downgrade.
            Assert.Contains("recon_date_window_days", ColumnNames(dbPath, "companies"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>The single-column contract "name|type|notnull|default|pk" (or "&lt;absent&gt;" when the column does
    /// not exist), matching the SchemaMigrationEquivalence comparison shape.</summary>
    private static string ColumnContract(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = "<absent>";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                if (!string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) continue;
                var type = r.GetString(2);
                var notNull = r.GetInt64(3);
                var dflt = r.IsDBNull(4) ? "<null>" : r.GetString(4);
                var pk = r.GetInt64(5);
                result = $"{column} | {type} | notnull={notNull} | default={dflt} | pk={pk}";
            }
        SqliteConnection.ClearPool(conn);
        return result;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }
}
