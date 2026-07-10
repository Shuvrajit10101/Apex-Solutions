using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-7 <b>POS</b> schema contract (RQ-38..RQ-44; ER-1, ER-13; DP-6). The v22→v23 bump is purely
/// additive: one <c>ALTER voucher_types ADD COLUMN use_for_pos … DEFAULT 0</c> plus three new tables
/// (<c>pos_voucher_type_config</c>, <c>pos_tender_ledger_defaults</c>, <c>pos_tender_allocations</c>) and one index,
/// leaving every existing table/row intact. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with
/// the new tables/columns; a legacy v22 DB auto-migrates forward preserving every row; the migration is idempotent
/// across reopens; and the <c>pos_tender_ledger_defaults</c> composite key rejects a second default for the same
/// (voucher type, tender).
/// </summary>
public sealed class PosSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_pos_schema()
    {
        var dbPath = TempDb("apex-pos-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 23);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.Contains("use_for_pos", ColumnNames(dbPath, "voucher_types"));
            Assert.True(TableExists(dbPath, "pos_voucher_type_config"));
            Assert.True(TableExists(dbPath, "pos_tender_ledger_defaults"));
            Assert.True(TableExists(dbPath, "pos_tender_allocations"));
            foreach (var expected in new[]
                     { "voucher_id", "tender_order", "tender_type", "ledger_id", "amount_paisa", "tendered_paisa",
                       "change_paisa", "card_no", "bank_name", "cheque_no" })
                Assert.Contains(expected, ColumnNames(dbPath, "pos_tender_allocations"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Duplicate_tender_default_is_rejected_by_the_composite_key()
    {
        var dbPath = TempDb("apex-pos-uniq");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;");

            var vt = Guid.NewGuid().ToString("D");
            Exec(conn, "INSERT INTO pos_tender_ledger_defaults(voucher_type_id, tender_type, ledger_id) VALUES ($vt, 3, $l);",
                ("$vt", vt), ("$l", Guid.NewGuid().ToString("D")));
            var ex = Assert.Throws<SqliteException>(() =>
                Exec(conn, "INSERT INTO pos_tender_ledger_defaults(voucher_type_id, tender_type, ledger_id) VALUES ($vt, 3, $l);",
                    ("$vt", vt), ("$l", Guid.NewGuid().ToString("D"))));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            // A different tender_type on the same voucher type is allowed.
            Exec(conn, "INSERT INTO pos_tender_ledger_defaults(voucher_type_id, tender_type, ledger_id) VALUES ($vt, 1, $l);",
                ("$vt", vt), ("$l", Guid.NewGuid().ToString("D")));
            Assert.Equal(2L, CountRows(dbPath, "pos_tender_ledger_defaults"));

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v22_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-pos-v22legacy");
        try
        {
            var company = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV22Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (22);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V22 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name, base_type, numbering, is_active, is_predefined) " +
                           "VALUES ($id, $cid, 'Sales', 8, 0, 1, 1);",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(22L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "pos_tender_allocations"));
            Assert.DoesNotContain("use_for_pos", ColumnNames(dbPath, "voucher_types"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v22 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "pos_tender_allocations"));
            Assert.True(TableExists(dbPath, "pos_voucher_type_config"));
            Assert.True(TableExists(dbPath, "pos_tender_ledger_defaults"));
            Assert.Contains("use_for_pos", ColumnNames(dbPath, "voucher_types"));

            // Every existing row survived (ER-13); the new tables start empty and the flag defaults 0.
            Assert.Equal("Legacy V22 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(1L, CountRows(dbPath, "voucher_types"));
            Assert.Equal(0L, CountRows(dbPath, "pos_tender_allocations"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT use_for_pos FROM voucher_types LIMIT 1;"));

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
    /// A minimal pre-v23 (v22) DDL: just enough of the schema for the v22→v23 migration (which ALTERs
    /// <c>voucher_types</c> and CREATEs the three POS tables) plus a data-preservation assertion. A company and a
    /// voucher_types row must survive. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV22Ddl = """
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
