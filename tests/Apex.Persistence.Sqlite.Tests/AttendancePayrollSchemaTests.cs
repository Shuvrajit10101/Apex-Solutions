using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-3 <b>Attendance + Payroll voucher</b> schema contract (ER-13). The v31→v32 bump is purely
/// additive: one <c>ALTER TABLE pay_heads ADD COLUMN employer_expense_ledger_id</c> plus two new tables
/// (<c>attendance_entries</c>, <c>payroll_lines</c>) with their indexes — no row rewrites. Covers: a fresh DB
/// stamps to <see cref="Schema.CurrentVersion"/> with the new column + tables; a legacy v31 DB auto-migrates
/// forward preserving every row (the new column defaults NULL, the tables start empty — ER-13); and the migration
/// is idempotent across reopens.
/// </summary>
public sealed class AttendancePayrollSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_attendance_payroll_schema()
    {
        var dbPath = TempDb("apex-attpay-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 32);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var t in new[] { "attendance_entries", "payroll_lines" })
                Assert.True(TableExists(dbPath, t), $"{t} table was not created.");

            Assert.Contains("employer_expense_ledger_id", ColumnNames(dbPath, "pay_heads"));
            foreach (var col in new[] { "employee_id", "attendance_type_id", "from_date", "to_date", "value_micro" })
                Assert.Contains(col, ColumnNames(dbPath, "attendance_entries"));
            foreach (var col in new[] { "entry_line_id", "employee_id", "pay_head_id", "category", "amount_micro" })
                Assert.Contains(col, ColumnNames(dbPath, "payroll_lines"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v31_database_auto_migrates_to_v32_preserving_every_row()
    {
        var dbPath = TempDb("apex-attpay-v31legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV31Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (31);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V31 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO pay_heads(id, company_id, name) VALUES ($id, $cid, 'Basic');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(31L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "attendance_entries"));
            Assert.DoesNotContain("employer_expense_ledger_id", ColumnNames(dbPath, "pay_heads"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v31 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "attendance_entries"));
            Assert.True(TableExists(dbPath, "payroll_lines"));
            Assert.Contains("employer_expense_ledger_id", ColumnNames(dbPath, "pay_heads"));

            // Every existing row survived (ER-13); the new column defaults NULL, the new tables start empty.
            Assert.Equal("Legacy V31 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(1L, CountRows(dbPath, "pay_heads"));
            Assert.Equal(0L, CountRows(dbPath, "attendance_entries"));
            Assert.Equal(0L, CountRows(dbPath, "payroll_lines"));
            Assert.True(IsDbNull(dbPath, "SELECT employer_expense_ledger_id FROM pay_heads LIMIT 1;"));

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

    private static bool IsDbNull(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is null || v is DBNull;
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

    /// <summary>A minimal pre-v32 (v31) DDL: enough for the v31→v32 migration (which ALTERs <c>pay_heads</c> and
    /// CREATEs <c>attendance_entries</c> + <c>payroll_lines</c>) plus a data-preservation assertion. Kept in the
    /// test so it never drifts as the production schema advances.</summary>
    private const string MinimalV31Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE employees (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE attendance_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL);
        CREATE TABLE pay_heads (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        """;
}
