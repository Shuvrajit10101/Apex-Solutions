using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-3 <b>Additional Cost of Purchase</b> schema contract (RQ-16..RQ-20; ER-1, ER-13, PR-11). The
/// v18→v19 bump is purely additive: it adds <c>voucher_types.track_additional_costs</c> (0/1 default 0) and
/// <c>ledgers.method_of_appropriation</c> (NULL default), and CREATEs the <c>additional_cost_lines</c> child table
/// (backing ONLY the Stock-Journal-transfer variant), leaving every existing table/row intact. This covers: a
/// fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the two new columns + the new table; a legacy v18
/// DB auto-migrates forward preserving every existing row (PR-11), the flag defaulting off and the method NULL
/// (ER-13); and re-opening a v19 DB is a no-op.
/// </summary>
public sealed class AdditionalCostSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_additional_cost_schema()
    {
        var dbPath = TempDb("apex-addlcost-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 19);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            // The two additive columns are present.
            Assert.Contains("track_additional_costs", ColumnNames(dbPath, "voucher_types"));
            Assert.Contains("method_of_appropriation", ColumnNames(dbPath, "ledgers"));

            // The additional-cost-lines child table exists with its exact columns.
            Assert.True(TableExists(dbPath, "additional_cost_lines"));
            var cols = ColumnNames(dbPath, "additional_cost_lines");
            foreach (var expected in new[]
                     { "id", "inventory_voucher_id", "line_order", "ledger_id", "amount_paisa" })
                Assert.Contains(expected, cols);

            // Re-opening a current-version DB is a no-op (still v19, table intact).
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "additional_cost_lines"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v18_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-addlcost-v18legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var ledger = Guid.NewGuid();
            var vtype = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV18Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (18);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V18 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO groups(id, company_id, name) VALUES ($id, $cid, 'Direct Expenses');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                // A pre-existing v18 ledger + voucher type MUST survive the additive migration untouched (ER-13).
                Exec(conn, "INSERT INTO ledgers(id, company_id, name, group_id) VALUES ($id, $cid, 'Freight', $g);",
                    ("$id", ledger.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name, base_type) VALUES ($id, $cid, 'Purchase', 2);",
                    ("$id", vtype.ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(18L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "additional_cost_lines"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v18 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "additional_cost_lines"));

            // Every existing row survived (ER-13): the company, the ledger and the voucher type are intact, with the
            // new flag defaulting OFF and the new method defaulting NULL.
            Assert.Equal("Legacy V18 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(0L, ReadLong(dbPath, "SELECT track_additional_costs FROM voucher_types WHERE id = $id;", vtype));
            Assert.True(ReadIsNull(dbPath, "SELECT method_of_appropriation FROM ledgers WHERE id = $id;", ledger));

            // The migrated DB now accepts an additional-cost line hung off an inventory voucher.
            using var conn2 = Open(dbPath);
            Exec(conn2, "PRAGMA foreign_keys = OFF;");
            var iv = Guid.NewGuid();
            Exec(conn2, "INSERT INTO inventory_vouchers(id, company_id, type_id, number, date) VALUES ($id, $cid, $t, 1, '2024-04-10');",
                ("$id", iv.ToString("D")), ("$cid", company.ToString("D")), ("$t", vtype.ToString("D")));
            Exec(conn2, """
                INSERT INTO additional_cost_lines(inventory_voucher_id, line_order, ledger_id, amount_paisa)
                VALUES ($iv, 0, $l, 20000);
                """,
                ("$iv", iv.ToString("D")), ("$l", ledger.ToString("D")));
            Assert.Equal(1L, CountRows(dbPath, "additional_cost_lines"));
            SqliteConnection.ClearPool(conn2);
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

    private static bool ReadIsNull(string dbPath, string sql, Guid id)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is null || v is DBNull;
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
    /// A minimal pre-v19 (v18) DDL: enough of the schema for the v18→v19 migration (which ALTERs
    /// <c>voucher_types</c> + <c>ledgers</c> and CREATEs <c>additional_cost_lines</c> referencing
    /// <c>inventory_vouchers</c> + <c>ledgers</c>) plus a data-preservation assertion. The <c>voucher_types</c> and
    /// <c>ledgers</c> tables are shaped as at v18 (with the earlier additive columns, WITHOUT the v19 columns) so the
    /// v18→v19 ALTERs apply cleanly. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV18Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id));
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL,
            use_as_manufacturing_journal INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE inventory_vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL, date TEXT NOT NULL);
        """;
}
