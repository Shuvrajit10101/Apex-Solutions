using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-1 <b>Payroll masters + F11 config</b> schema contract (ER-1, ER-13). The v29→v30 bump is
/// purely additive: two <c>ALTER TABLE companies</c> (the Payroll + Payroll-Statutory toggles, <c>DEFAULT 0</c>)
/// plus five new master tables (<c>employee_categories</c>, <c>employee_groups</c>, <c>payroll_units</c>,
/// <c>attendance_types</c>, <c>employees</c>) with their company indexes, leaving every existing table/row intact.
/// Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables/columns; a legacy v29 DB
/// auto-migrates forward preserving every row (the flags default 0, the tables start empty — ER-13); and the
/// migration is idempotent across reopens.
/// </summary>
public sealed class PayrollSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_payroll_schema()
    {
        var dbPath = TempDb("apex-payroll-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 30);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.Contains("payroll_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("payroll_statutory_enabled", ColumnNames(dbPath, "companies"));

            foreach (var t in new[] { "employee_categories", "employee_groups", "payroll_units", "attendance_types", "employees" })
                Assert.True(TableExists(dbPath, t), $"{t} table was not created.");

            foreach (var col in new[] { "employee_group_id", "employee_category_id", "pan", "uan", "esi_number", "tax_regime", "function" })
                Assert.Contains(col, ColumnNames(dbPath, "employees"));
            foreach (var col in new[] { "allocate_revenue", "allocate_non_revenue" })
                Assert.Contains(col, ColumnNames(dbPath, "employee_categories"));
            Assert.Contains("define_salary_details", ColumnNames(dbPath, "employee_groups"));
            Assert.Contains("is_compound", ColumnNames(dbPath, "payroll_units"));
            Assert.Contains("kind", ColumnNames(dbPath, "attendance_types"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v29_database_auto_migrates_to_v30_preserving_every_row()
    {
        var dbPath = TempDb("apex-payroll-v29legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV29Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (29);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V29 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO ledgers(id, company_id, name) VALUES ($id, $cid, 'Cash');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(29L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "employees"));
            Assert.DoesNotContain("payroll_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v29 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "employees"));
            Assert.True(TableExists(dbPath, "employee_groups"));
            Assert.True(TableExists(dbPath, "payroll_units"));
            Assert.True(TableExists(dbPath, "attendance_types"));
            Assert.True(TableExists(dbPath, "employee_categories"));
            Assert.Contains("payroll_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("payroll_statutory_enabled", ColumnNames(dbPath, "companies"));

            // Every existing row survived (ER-13); the new tables start empty and the flags default 0.
            Assert.Equal("Legacy V29 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "ledgers"));
            Assert.Equal(0L, CountRows(dbPath, "employees"));
            Assert.Equal(0L, CountRows(dbPath, "employee_groups"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT payroll_enabled FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT payroll_statutory_enabled FROM companies LIMIT 1;"));

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

    /// <summary>A minimal pre-v30 (v29) DDL: just enough for the v29→v30 migration (which ALTERs <c>companies</c>
    /// and CREATEs the five payroll master tables) plus a data-preservation assertion. Kept in the test so it never
    /// drifts as the production schema advances.</summary>
    private const string MinimalV29Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        -- voucher_types + entry_lines are required because the chain now runs through the v38→v39 RCM migration,
        -- whose ALTER TABLE voucher_types/entry_lines ADD COLUMN … need the tables to exist.
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        -- voucher_inventory_lines is required because the chain now runs through the v45 -> v46 item-invoice
        -- line-unit migration, whose ALTER TABLE voucher_inventory_lines ADD COLUMN unit_id needs the table to
        -- exist. A real database of this vintage always has it (created at v12); this fixture is a minimal
        -- hand-written subset, so the table is declared here for the ALTER to land on.
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, stock_item_id TEXT NOT NULL DEFAULT '', godown_id TEXT NOT NULL DEFAULT '', quantity_micro INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 0, rate_paisa INTEGER NOT NULL DEFAULT 0);
        """;
}
