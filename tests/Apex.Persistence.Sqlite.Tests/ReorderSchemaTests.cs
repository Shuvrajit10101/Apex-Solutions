using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-6 <b>Reorder Levels</b> schema contract (RQ-32..RQ-35; ER-1, ER-13). The v21→v22 bump is
/// purely additive: it CREATEs the single new table <c>reorder_definitions</c> (+ its two indexes), leaving every
/// existing table/row intact. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new table
/// and its columns; a legacy v21 DB auto-migrates forward preserving every row; the migration is idempotent
/// across reopens; and the <c>ux_reorder_definitions_scope</c> index rejects a second definition for the same
/// (scope, target).
/// </summary>
public sealed class ReorderSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_reorder_schema()
    {
        var dbPath = TempDb("apex-reorder-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 22);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.True(TableExists(dbPath, "reorder_definitions"));
            foreach (var expected in new[]
                     { "id", "company_id", "scope", "target_id", "reorder_advanced", "reorder_qty_micro",
                       "minqty_advanced", "min_order_qty_micro", "period_unit", "period_count", "criteria" })
                Assert.Contains(expected, ColumnNames(dbPath, "reorder_definitions"));

            // Re-opening a current-version DB is a no-op.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Duplicate_scope_target_is_rejected_by_the_unique_index()
    {
        var dbPath = TempDb("apex-reorder-uniq");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            var company = Guid.NewGuid();
            var target = Guid.NewGuid();
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;"); // isolate the index contract from company FK seeding

            Exec(conn, "INSERT INTO reorder_definitions(id, company_id, scope, target_id) VALUES ($id, $cid, 1, $t);",
                ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")), ("$t", target.ToString("D")));
            // A second definition for the SAME (scope, target) is rejected.
            var ex = Assert.Throws<SqliteException>(() =>
                Exec(conn, "INSERT INTO reorder_definitions(id, company_id, scope, target_id) VALUES ($id, $cid, 1, $t);",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")), ("$t", target.ToString("D"))));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            // But a DIFFERENT scope on the same target id is allowed (scope is part of the key).
            Exec(conn, "INSERT INTO reorder_definitions(id, company_id, scope, target_id) VALUES ($id, $cid, 2, $t);",
                ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")), ("$t", target.ToString("D")));
            Assert.Equal(2L, CountRows(dbPath, "reorder_definitions"));

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v21_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-reorder-v21legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var ledger = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV21Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (21);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V21 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO groups(id, company_id, name) VALUES ($id, $cid, 'Sundry Debtors');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO ledgers(id, company_id, name, group_id) VALUES ($id, $cid, 'ACME', $g);",
                    ("$id", ledger.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(21L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "reorder_definitions"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v21 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "reorder_definitions"));

            // Every existing row survived (ER-13); the new table starts empty.
            Assert.Equal("Legacy V21 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(0L, CountRows(dbPath, "reorder_definitions"));

            // Reopen is idempotent (still current version, no double-CREATE crash).
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
    /// A minimal pre-v22 (v21) DDL: just enough of the schema for the v21→v22 migration (which CREATEs the single
    /// <c>reorder_definitions</c> table + its indexes) plus a data-preservation assertion. A company + group +
    /// ledger row must survive. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV21Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id));
        -- stock_items is required because the chain now runs through the v24→v25 TDS/TCS migration, whose
        -- ALTER TABLE stock_items ADD COLUMN tcs_nature_id needs the table to exist.
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        -- voucher_types is required because the chain now runs through the v22→v23 POS migration, whose
        -- ALTER TABLE voucher_types ADD COLUMN use_for_pos needs the table to exist.
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        -- entry_lines is required because the chain now runs through the v38→v39 RCM migration, whose
        -- ALTER TABLE entry_lines ADD COLUMN gst_is_reverse_charge/gst_rcm_scheme needs the table to exist.
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        -- voucher_inventory_lines is required because the chain now runs through the v45 -> v46 item-invoice
        -- line-unit migration, whose ALTER TABLE voucher_inventory_lines ADD COLUMN unit_id needs the table to
        -- exist. A real database of this vintage always has it (created at v12); this fixture is a minimal
        -- hand-written subset, so the table is declared here for the ALTER to land on.
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, stock_item_id TEXT NOT NULL DEFAULT '', godown_id TEXT NOT NULL DEFAULT '', quantity_micro INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 0, rate_paisa INTEGER NOT NULL DEFAULT 0);
        """;
}
