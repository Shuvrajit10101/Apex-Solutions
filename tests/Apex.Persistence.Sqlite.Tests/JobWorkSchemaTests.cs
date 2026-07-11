using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-8 <b>Job Work</b> schema contract (RQ-45..RQ-51; ER-1, ER-13). The v23→v24 bump is purely
/// additive: three <c>ALTER … ADD COLUMN … DEFAULT 0</c> flags (<c>companies.enable_job_order_processing</c>,
/// <c>voucher_types.use_for_job_work</c>, <c>voucher_types.allow_consumption</c>) plus three new tables
/// (<c>job_work_orders</c>, <c>job_work_order_lines</c>, <c>material_order_links</c>) and their indexes, leaving
/// every existing table/row intact. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new
/// tables/columns; a legacy v23 DB auto-migrates forward preserving every row; and the migration is idempotent
/// across reopens.
/// </summary>
public sealed class JobWorkSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_job_work_schema()
    {
        var dbPath = TempDb("apex-jw-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 24);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.Contains("enable_job_order_processing", ColumnNames(dbPath, "companies"));
            Assert.Contains("use_for_job_work", ColumnNames(dbPath, "voucher_types"));
            Assert.Contains("allow_consumption", ColumnNames(dbPath, "voucher_types"));
            Assert.True(TableExists(dbPath, "job_work_orders"));
            Assert.True(TableExists(dbPath, "job_work_order_lines"));
            Assert.True(TableExists(dbPath, "material_order_links"));
            foreach (var expected in new[]
                     { "id", "company_id", "inventory_voucher_id", "direction", "order_no", "fg_stock_item_id",
                       "fg_qty_micro", "tracking_components", "fill_components_bom_id" })
                Assert.Contains(expected, ColumnNames(dbPath, "job_work_orders"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Job_work_order_voucher_link_is_unique()
    {
        var dbPath = TempDb("apex-jw-uniq");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;");

            var vid = Guid.NewGuid().ToString("D");
            void InsertOrder(string id) => Exec(conn,
                "INSERT INTO job_work_orders(id, company_id, inventory_voucher_id, direction, order_no, " +
                "fg_stock_item_id, fg_qty_micro) VALUES ($id, $c, $v, 1, 'O1', $fg, 1000000);",
                ("$id", id), ("$c", Guid.NewGuid().ToString("D")), ("$v", vid), ("$fg", Guid.NewGuid().ToString("D")));

            InsertOrder(Guid.NewGuid().ToString("D"));
            var ex = Assert.Throws<SqliteException>(() => InsertOrder(Guid.NewGuid().ToString("D")));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v23_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-jw-v23legacy");
        try
        {
            var company = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV23Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (23);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V23 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name, base_type, numbering, is_active, is_predefined) " +
                           "VALUES ($id, $cid, 'Material In', 19, 0, 0, 1);",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(23L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "job_work_orders"));
            Assert.DoesNotContain("enable_job_order_processing", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v23 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "job_work_orders"));
            Assert.True(TableExists(dbPath, "job_work_order_lines"));
            Assert.True(TableExists(dbPath, "material_order_links"));
            Assert.Contains("enable_job_order_processing", ColumnNames(dbPath, "companies"));
            Assert.Contains("use_for_job_work", ColumnNames(dbPath, "voucher_types"));
            Assert.Contains("allow_consumption", ColumnNames(dbPath, "voucher_types"));

            // Every existing row survived (ER-13); the new tables start empty and the flags default 0.
            Assert.Equal("Legacy V23 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(1L, CountRows(dbPath, "voucher_types"));
            Assert.Equal(0L, CountRows(dbPath, "job_work_orders"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT enable_job_order_processing FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT use_for_job_work FROM voucher_types LIMIT 1;"));

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
    /// A minimal pre-v24 (v23) DDL: just enough of the schema for the v23→v24 migration (which ALTERs
    /// <c>companies</c> + <c>voucher_types</c> and CREATEs the three job-work tables) plus a data-preservation
    /// assertion. A company and a voucher_types row must survive. Kept in the test so it never drifts as the
    /// production schema advances.
    /// </summary>
    private const string MinimalV23Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        -- ledgers + stock_items are required because the chain now runs through the v24→v25 TDS/TCS migration,
        -- whose ALTER TABLE ledgers/stock_items ADD COLUMN … need the tables to exist.
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, numbering INTEGER NOT NULL,
            is_active INTEGER NOT NULL, is_predefined INTEGER NOT NULL);
        """;
}
