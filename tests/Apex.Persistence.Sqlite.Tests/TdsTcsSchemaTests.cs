using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-7 slice-1 <b>TDS/TCS masters + config</b> schema contract (ER-1, ER-13). The v24→v25 bump is purely
/// additive: thirteen <c>ALTER TABLE companies</c> + nine <c>ALTER TABLE ledgers</c> + one
/// <c>ALTER TABLE stock_items</c> (all <c>DEFAULT 0</c>/NULL) plus two new tables (<c>nature_of_payment</c>,
/// <c>nature_of_goods</c>) with their indexes, leaving every existing table/row intact. Covers: a fresh DB stamps
/// to <see cref="Schema.CurrentVersion"/> with the new tables/columns; a legacy v24 DB auto-migrates forward
/// preserving every row (the new flags default 0/NULL, the new tables start empty — ER-13); and the migration is
/// idempotent across reopens. No §206AB/§206CCA columns exist (omitted, FA2025).
/// </summary>
public sealed class TdsTcsSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_tds_tcs_schema()
    {
        var dbPath = TempDb("apex-tdstcs-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 25);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "tds_enabled", "tcs_enabled", "tan", "deductor_type",
                     "responsible_person_name", "surcharge_applicable", "cess_applicable", "tds_periodicity" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
            foreach (var col in new[] { "tds_applicable", "tds_nature_id", "deductee_type", "party_pan",
                     "deduct_in_same_voucher", "tcs_applicable", "tcs_nature_id", "collectee_type", "tds_tcs_class_kind" })
                Assert.Contains(col, ColumnNames(dbPath, "ledgers"));
            Assert.Contains("tcs_nature_id", ColumnNames(dbPath, "stock_items"));

            Assert.True(TableExists(dbPath, "nature_of_payment"));
            Assert.True(TableExists(dbPath, "nature_of_goods"));
            Assert.True(TableExists(dbPath, "tds_lines")); // v26 (Phase 7 slice 2)
            Assert.True(TableExists(dbPath, "tds_challans"));           // v27 (Phase 7 slice 3)
            Assert.True(TableExists(dbPath, "challan_voucher_links"));  // v27 (Phase 7 slice 3)
            Assert.Contains("is_stat_payment", ColumnNames(dbPath, "voucher_types")); // v27 (Phase 7 slice 3)
            Assert.True(TableExists(dbPath, "tcs_lines")); // v28 (Phase 7 slice 5)
            Assert.True(TableExists(dbPath, "tcs_challans"));               // v29 (Phase 7 slice 6)
            Assert.True(TableExists(dbPath, "tcs_challan_voucher_links"));  // v29 (Phase 7 slice 6)

            // §206AB / §206CCA were omitted (FA2025) — no such columns leaked in.
            Assert.DoesNotContain("higher_rate_206ab", ColumnNames(dbPath, "ledgers"));
            Assert.DoesNotContain("higher_rate_206cca", ColumnNames(dbPath, "ledgers"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v24_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-tdstcs-v24legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV24Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (24);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V24 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO ledgers(id, company_id, name) VALUES ($id, $cid, 'Cash');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_items(id, company_id, name) VALUES ($id, $cid, 'Widget');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(24L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "nature_of_payment"));
            Assert.DoesNotContain("tds_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v24 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "nature_of_payment"));
            Assert.True(TableExists(dbPath, "nature_of_goods"));
            Assert.Contains("tds_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("tcs_applicable", ColumnNames(dbPath, "ledgers"));
            Assert.Contains("tcs_nature_id", ColumnNames(dbPath, "stock_items"));

            // Every existing row survived (ER-13); the new tables start empty and the flags default 0.
            Assert.Equal("Legacy V24 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "ledgers"));
            Assert.Equal(1L, CountRows(dbPath, "stock_items"));
            Assert.Equal(0L, CountRows(dbPath, "nature_of_payment"));
            Assert.Equal(0L, CountRows(dbPath, "nature_of_goods"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT tds_enabled FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT tcs_enabled FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT tds_applicable FROM ledgers LIMIT 1;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v25_database_auto_migrates_to_v26_adding_tds_lines_and_preserving_rows()
    {
        var dbPath = TempDb("apex-tds-v25legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV25Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (25);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V25 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO vouchers(id, company_id) VALUES ($id, $cid);",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO entry_lines(voucher_id) VALUES ($vid);", ("$vid", Guid.NewGuid().ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(25L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "tds_lines"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v25 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "tds_lines"));
            // Every existing row survived (ER-13); the new table starts empty.
            Assert.Equal("Legacy V25 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "entry_lines"));
            Assert.Equal(0L, CountRows(dbPath, "tds_lines"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v26_database_auto_migrates_to_v27_adding_challans_and_stat_flag_preserving_rows()
    {
        var dbPath = TempDb("apex-tds-v26legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV26Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (26);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V26 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name) VALUES ($id, $cid, 'Payment');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(26L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "tds_challans"));
            Assert.DoesNotContain("is_stat_payment", ColumnNames(dbPath, "voucher_types"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v26 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "tds_challans"));
            Assert.True(TableExists(dbPath, "challan_voucher_links"));
            Assert.Contains("is_stat_payment", ColumnNames(dbPath, "voucher_types"));
            // Every existing row survived (ER-13); the new column defaults 0 and the new tables start empty.
            Assert.Equal("Legacy V26 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "voucher_types"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT is_stat_payment FROM voucher_types LIMIT 1;"));
            Assert.Equal(0L, CountRows(dbPath, "tds_challans"));
            Assert.Equal(0L, CountRows(dbPath, "challan_voucher_links"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v27_database_auto_migrates_to_v28_adding_tcs_lines_and_preserving_rows()
    {
        var dbPath = TempDb("apex-tcs-v27legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV27Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (27);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V27 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO vouchers(id, company_id) VALUES ($id, $cid);",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO entry_lines(voucher_id) VALUES ($vid);", ("$vid", Guid.NewGuid().ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(27L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "tcs_lines"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v27 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "tcs_lines"));
            // Every existing row survived (ER-13); the new table starts empty.
            Assert.Equal("Legacy V27 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "entry_lines"));
            Assert.Equal(0L, CountRows(dbPath, "tcs_lines"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v28_database_auto_migrates_to_v29_adding_tcs_challans_and_preserving_rows()
    {
        var dbPath = TempDb("apex-tcs-v28legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV28Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (28);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V28 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name) VALUES ($id, $cid, 'Payment');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(28L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "tcs_challans"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v28 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "tcs_challans"));
            Assert.True(TableExists(dbPath, "tcs_challan_voucher_links"));
            // Every existing row survived (ER-13); the new tables start empty.
            Assert.Equal("Legacy V28 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "voucher_types"));
            Assert.Equal(0L, CountRows(dbPath, "tcs_challans"));
            Assert.Equal(0L, CountRows(dbPath, "tcs_challan_voucher_links"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
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
    /// A minimal pre-v25 (v24) DDL: just enough of the schema for the v24→v25 migration (which ALTERs
    /// <c>companies</c>, <c>ledgers</c> and <c>stock_items</c> and CREATEs the two nature tables) plus a
    /// data-preservation assertion. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV24Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;

    /// <summary>A minimal pre-v26 (v25) DDL: just enough for the v25→v26 migration (which CREATEs
    /// <c>tds_lines</c> referencing <c>entry_lines</c>) plus a data-preservation assertion.</summary>
    private const string MinimalV25Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;

    /// <summary>A minimal pre-v27 (v26) DDL: just enough for the v26→v27 migration (which ALTERs
    /// <c>voucher_types</c> and CREATEs the two challan tables referencing <c>companies</c>/<c>vouchers</c>) plus a
    /// data-preservation assertion.</summary>
    private const string MinimalV26Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;

    /// <summary>A minimal pre-v28 (v27) DDL: just enough for the v27→v28 migration (which CREATEs
    /// <c>tcs_lines</c> referencing <c>entry_lines</c>) plus a data-preservation assertion.</summary>
    private const string MinimalV27Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;

    /// <summary>A minimal pre-v29 (v28) DDL: just enough for the v28→v29 migration (which CREATEs the two TCS
    /// challan tables referencing <c>companies</c>/<c>vouchers</c>) plus a data-preservation assertion.</summary>
    private const string MinimalV28Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;
}
