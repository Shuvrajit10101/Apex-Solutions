using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-5 <b>Price Levels / Price Lists</b> schema contract (RQ-26..RQ-31; ER-1, ER-13, PR-11). The
/// v20→v21 bump is purely additive: it CREATEs three new tables (<c>price_levels</c>, <c>price_lists</c>,
/// <c>price_list_lines</c>) and ADDs <c>companies.enable_multiple_price_levels</c> (0/1 default 0) +
/// <c>ledgers.default_price_level_id</c> (nullable FK), leaving every existing table/row intact. Covers: a fresh
/// DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables/columns; a legacy v20 DB auto-migrates
/// forward preserving every row (flag default 0, FK NULL); the migration is idempotent across reopens; and the
/// <c>ux_price_levels_name</c> index rejects a duplicate level name per company (case-insensitive).
/// </summary>
public sealed class PriceListSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_price_level_schema()
    {
        var dbPath = TempDb("apex-pl-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 21);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.True(TableExists(dbPath, "price_levels"));
            Assert.True(TableExists(dbPath, "price_lists"));
            Assert.True(TableExists(dbPath, "price_list_lines"));

            foreach (var expected in new[] { "id", "company_id", "name" })
                Assert.Contains(expected, ColumnNames(dbPath, "price_levels"));
            foreach (var expected in new[] { "id", "company_id", "price_level_id", "stock_item_id", "applicable_from" })
                Assert.Contains(expected, ColumnNames(dbPath, "price_lists"));
            foreach (var expected in new[]
                     { "id", "price_list_id", "line_order", "from_qty_micro", "to_qty_micro",
                       "rate_paisa", "discount_percent_millis" })
                Assert.Contains(expected, ColumnNames(dbPath, "price_list_lines"));

            Assert.Contains("enable_multiple_price_levels", ColumnNames(dbPath, "companies"));
            Assert.Contains("default_price_level_id", ColumnNames(dbPath, "ledgers"));

            // Re-opening a current-version DB is a no-op.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Duplicate_level_name_per_company_is_rejected_by_the_unique_index()
    {
        var dbPath = TempDb("apex-pl-uniq");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            var company = Guid.NewGuid();
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;"); // isolate the index contract from company FK seeding

            Exec(conn, "INSERT INTO price_levels(id, company_id, name) VALUES ($id, $cid, 'Retail');",
                ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
            // A different case of the same name in the same company is rejected (COLLATE NOCASE).
            var ex = Assert.Throws<SqliteException>(() =>
                Exec(conn, "INSERT INTO price_levels(id, company_id, name) VALUES ($id, $cid, 'retail');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D"))));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            // But the SAME name under a DIFFERENT company is allowed.
            Exec(conn, "INSERT INTO price_levels(id, company_id, name) VALUES ($id, $cid, 'Retail');",
                ("$id", Guid.NewGuid().ToString("D")), ("$cid", Guid.NewGuid().ToString("D")));
            Assert.Equal(2L, CountRows(dbPath, "price_levels"));

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v20_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-pl-v20legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var ledger = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV20Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (20);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V20 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO groups(id, company_id, name) VALUES ($id, $cid, 'Sundry Debtors');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO ledgers(id, company_id, name, group_id) VALUES ($id, $cid, 'ACME', $g);",
                    ("$id", ledger.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(20L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "price_levels"));
            Assert.DoesNotContain("enable_multiple_price_levels", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v20 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "price_levels"));

            // Every existing row survived (ER-13): the flag defaults 0 and the FK defaults NULL.
            Assert.Equal("Legacy V20 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(0L, ReadLong(dbPath, "SELECT enable_multiple_price_levels FROM companies WHERE id = $id;", company));
            Assert.True(ColumnIsNull(dbPath, "SELECT default_price_level_id FROM ledgers WHERE id = $id;", ledger));

            // Reopen is idempotent (still current version, no double-ALTER crash).
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static long ReadLong(string dbPath, string sql, Guid id)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static bool ColumnIsNull(string dbPath, string sql, Guid id)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        var isNull = cmd.ExecuteScalar() is null or DBNull;
        SqliteConnection.ClearPool(conn);
        return isNull;
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static long CountRows(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        var n = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return n;
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

    private static string ReadCompanyName(string dbPath, Guid companyId)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM companies WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", companyId.ToString("D"));
        var name = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return name;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// A minimal pre-v21 (v20) DDL: enough of the schema for the v20→v21 migration (which CREATEs the three
    /// price-level tables and ALTERs <c>companies</c> / <c>ledgers</c> to add the flag + the default-level FK) plus
    /// a data-preservation assertion. The two ALTER targets carry a company + a ledger row that must survive. Kept
    /// in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV20Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id), method_of_appropriation INTEGER NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        """;
}
