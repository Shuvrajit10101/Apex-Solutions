using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-2 <b>Pay Heads + Salary Structures</b> schema contract (ER-13). The v30→v31 bump is purely
/// additive: five new tables (<c>pay_heads</c>, <c>pay_head_computation</c>, <c>pay_head_computation_slabs</c>,
/// <c>salary_structures</c>, <c>salary_structure_lines</c>) with their indexes — no ALTER, no companies column,
/// no row rewrites. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables; a legacy
/// v30 DB auto-migrates forward preserving every row (the tables start empty — ER-13); and the migration is
/// idempotent across reopens.
/// </summary>
public sealed class PayHeadSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_pay_head_schema()
    {
        var dbPath = TempDb("apex-payhead-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 31);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var t in new[] { "pay_heads", "pay_head_computation", "pay_head_computation_slabs", "salary_structures", "salary_structure_lines" })
                Assert.True(TableExists(dbPath, t), $"{t} table was not created.");

            foreach (var col in new[] { "pay_head_type", "calculation_type", "affects_net_salary", "under_group_id", "income_tax_component", "use_for_gratuity", "rounding_method", "attendance_type_id" })
                Assert.Contains(col, ColumnNames(dbPath, "pay_heads"));
            Assert.Contains("component_pay_head_id", ColumnNames(dbPath, "pay_head_computation"));
            Assert.Contains("rate_basis_points", ColumnNames(dbPath, "pay_head_computation_slabs"));
            foreach (var col in new[] { "scope", "scope_id", "effective_from", "start_type" })
                Assert.Contains(col, ColumnNames(dbPath, "salary_structures"));
            Assert.Contains("amount_paisa", ColumnNames(dbPath, "salary_structure_lines"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v30_database_auto_migrates_to_v31_preserving_every_row()
    {
        var dbPath = TempDb("apex-payhead-v30legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV30Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (30);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V30 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO employees(id, company_id, name) VALUES ($id, $cid, 'Rajkumar');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(30L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "pay_heads"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v30 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            foreach (var t in new[] { "pay_heads", "pay_head_computation", "pay_head_computation_slabs", "salary_structures", "salary_structure_lines" })
                Assert.True(TableExists(dbPath, t), $"{t} table was not created on upgrade.");

            // Every existing row survived (ER-13); the new tables start empty.
            Assert.Equal("Legacy V30 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "employees"));
            Assert.Equal(0L, CountRows(dbPath, "pay_heads"));
            Assert.Equal(0L, CountRows(dbPath, "salary_structures"));

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

    /// <summary>A minimal pre-v31 (v30) DDL: just enough for the v30→v31 migration (which only CREATEs the five
    /// pay-head / salary-structure tables) plus a data-preservation assertion. Kept in the test so it never drifts
    /// as the production schema advances.</summary>
    private const string MinimalV30Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE employees (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        -- voucher_types + entry_lines are required because the chain now runs through the v38→v39 RCM migration,
        -- whose ALTER TABLE voucher_types/entry_lines ADD COLUMN … need the tables to exist.
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        """;
}
