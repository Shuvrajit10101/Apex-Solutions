using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-1 <b>batch master</b> schema contract (RQ-1..RQ-8, DP-8; ER-1, PR-11). The v15→v16 bump is
/// purely additive: it CREATEs the <c>batch_masters</c> table (a first-class batch per <c>(stock item, batch
/// number)</c>, unique WITHIN an item — not globally — with optional mfg/expiry dates, an optional expiry-period,
/// and an optional per-batch inward cost layer of qty-micros + rate-paisa) and adds a nullable <c>batch_id</c>
/// reference to the four stock-line tables, leaving every existing <c>batch_label</c> column and row intact.
/// This covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> and has <c>batch_masters</c> +
/// the four <c>batch_id</c> columns; a legacy v15 DB auto-migrates forward preserving every existing row (PR-11);
/// batch numbers are unique per item but MAY repeat across items (RQ-1); and money/qty are integer paisa/micros.
/// </summary>
public sealed class BatchMasterSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_v16_with_batch_masters_and_batch_id_columns()
    {
        var dbPath = TempDb("apex-batch-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            // Batch masters arrived at schema v16 and persist in every later version; a fresh DB is stamped to
            // the current version (repointed off a literal so later slices that bump the version stay green).
            Assert.True(Schema.CurrentVersion >= 16);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.True(TableExists(dbPath, "batch_masters"));
            // The batch master carries the per-batch inward cost layer (RQ-6/DP-8) + optional mfg/expiry.
            var cols = ColumnNames(dbPath, "batch_masters");
            foreach (var expected in new[]
                     { "id", "company_id", "stock_item_id", "batch_no", "mfg_date", "expiry_date",
                       "expiry_period", "godown_id", "inward_qty_micro", "inward_rate_paisa" })
                Assert.Contains(expected, cols);

            // The nullable batch_id reference is present on every stock-line table, and batch_label stays intact.
            foreach (var table in new[]
                     { "stock_opening_balances", "inventory_allocations", "physical_stock_lines", "voucher_inventory_lines" })
            {
                var tcols = ColumnNames(dbPath, table);
                Assert.Contains("batch_id", tcols);
                Assert.Contains("batch_label", tcols); // backward-compat column preserved (DP-10)
            }
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Batch_number_is_unique_within_an_item_but_may_repeat_across_items()
    {
        var dbPath = TempDb("apex-batch-uniq");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            var company = Guid.NewGuid();
            var itemA = Guid.NewGuid();
            var itemB = Guid.NewGuid();
            var group = Guid.NewGuid();
            var unit = Guid.NewGuid();

            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;"); // isolate the index contract from unrelated FK seeding
            InsertItem(conn, company, group, unit, itemA, "Item A");
            InsertItem(conn, company, group, unit, itemB, "Item B");

            // Same batch number on two DIFFERENT items is allowed (unique per item, not global — RQ-1).
            InsertBatch(conn, company, itemA, "B-001");
            InsertBatch(conn, company, itemB, "B-001");
            Assert.Equal(2L, CountRows(dbPath, "batch_masters"));

            // But a DUPLICATE batch number on the SAME item is rejected by the unique index.
            var ex = Assert.Throws<SqliteException>(() => InsertBatch(conn, company, itemA, "B-001"));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v15_database_auto_migrates_to_v16_preserving_every_row()
    {
        var dbPath = TempDb("apex-batch-v15legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var item = Guid.NewGuid();
            var godown = Guid.NewGuid();
            var opening = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV15Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (15);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V15 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_groups(id, company_id, name) VALUES ($id, $cid, 'G');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO units(id, company_id, symbol, formal_name, is_compound, decimal_places) VALUES ($id, $cid, 'Nos', 'Numbers', 0, 0);",
                    ("$id", unit.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO godowns(id, company_id, name) VALUES ($id, $cid, 'Main');",
                    ("$id", godown.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_items(id, company_id, name, stock_group_id, base_unit_id) VALUES ($id, $cid, 'Widget', $g, $u);",
                    ("$id", item.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")), ("$u", unit.ToString("D")));
                // A pre-existing opening balance carrying a batch_label MUST survive the additive migration.
                Exec(conn, """
                    INSERT INTO stock_opening_balances(id, company_id, stock_item_id, godown_id, batch_label, quantity_micro, rate_paisa)
                    VALUES ($id, $cid, $it, $gd, 'LOT-42', 5000000, 12345);
                    """,
                    ("$id", opening.ToString("D")), ("$cid", company.ToString("D")),
                    ("$it", item.ToString("D")), ("$gd", godown.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(15L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "batch_masters"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v15 → migrates forward to the current version

            // A legacy v15 DB migrates all the way to the current version (v16 batch + every later slice); the
            // batch_masters table is created en route, so this assertion is version-agnostic (repointed off 16L).
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "batch_masters"));

            // Every existing row survived, byte-for-byte (ER-13): the opening balance + its batch_label are intact,
            // and the new batch_id column defaulted to NULL (no row rewrite).
            Assert.Equal("Legacy V15 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(1L, CountRows(dbPath, "stock_opening_balances"));
            Assert.Equal("LOT-42", ReadOpeningBatchLabel(dbPath, opening));
            Assert.True(OpeningBatchIdIsNull(dbPath, opening));

            // The migrated DB now accepts a first-class batch master with a per-batch inward cost layer.
            using var conn2 = Open(dbPath);
            Exec(conn2, "PRAGMA foreign_keys = ON;");
            var batch = Guid.NewGuid();
            Exec(conn2, """
                INSERT INTO batch_masters(id, company_id, stock_item_id, batch_no, mfg_date, expiry_date,
                    expiry_period, godown_id, inward_qty_micro, inward_rate_paisa)
                VALUES ($id, $cid, $it, 'LOT-42', '2026-01-01', '2027-01-01', '12 Months', $gd, 5000000, 12345);
                """,
                ("$id", batch.ToString("D")), ("$cid", company.ToString("D")),
                ("$it", item.ToString("D")), ("$gd", godown.ToString("D")));
            Assert.Equal(1L, CountRows(dbPath, "batch_masters"));
            SqliteConnection.ClearPool(conn2);
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);

    private static void InsertItem(SqliteConnection conn, Guid company, Guid group, Guid unit, Guid item, string name)
    {
        Exec(conn, "INSERT OR IGNORE INTO stock_groups(id, company_id, name) VALUES ($id, $cid, 'G');",
            ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
        Exec(conn, "INSERT OR IGNORE INTO units(id, company_id, symbol, formal_name, is_compound, decimal_places) VALUES ($id, $cid, 'Nos', 'Numbers', 0, 0);",
            ("$id", unit.ToString("D")), ("$cid", company.ToString("D")));
        Exec(conn, "INSERT INTO stock_items(id, company_id, name, stock_group_id, base_unit_id) VALUES ($id, $cid, $n, $g, $u);",
            ("$id", item.ToString("D")), ("$cid", company.ToString("D")), ("$n", name),
            ("$g", group.ToString("D")), ("$u", unit.ToString("D")));
    }

    private static void InsertBatch(SqliteConnection conn, Guid company, Guid item, string batchNo)
        => Exec(conn, "INSERT INTO batch_masters(id, company_id, stock_item_id, batch_no) VALUES ($id, $cid, $it, $bn);",
            ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")),
            ("$it", item.ToString("D")), ("$bn", batchNo));

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
                names.Add(r.GetString(1)); // column 1 = name
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

    private static string ReadOpeningBatchLabel(string dbPath, Guid openingId)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT batch_label FROM stock_opening_balances WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", openingId.ToString("D"));
        var label = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return label;
    }

    private static bool OpeningBatchIdIsNull(string dbPath, Guid openingId)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT batch_id FROM stock_opening_balances WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", openingId.ToString("D"));
        var isNull = cmd.ExecuteScalar() is null or DBNull;
        SqliteConnection.ClearPool(conn);
        return isNull;
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
    /// A minimal pre-v16 (v15) DDL: enough of the schema for the v15→v16 migration (which CREATEs
    /// <c>batch_masters</c> and ALTERs the four stock-line tables to add a nullable <c>batch_id</c>) plus a
    /// data-preservation assertion. Includes <c>stock_opening_balances</c> (a row that must survive) and the
    /// other three stock-line tables the migration ALTERs, so the ADD-COLUMN steps have real targets. Kept in the
    /// test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV15Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
            nature INTEGER NOT NULL DEFAULT 0, parent_id TEXT NULL, alias TEXT NULL, is_predefined INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE stock_groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, parent_id TEXT NULL, alias TEXT NULL, add_quantities INTEGER NOT NULL DEFAULT 1);
        CREATE TABLE units (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            symbol TEXT NOT NULL, formal_name TEXT NOT NULL, is_compound INTEGER NOT NULL, uqc TEXT NULL,
            decimal_places INTEGER NOT NULL DEFAULT 0, first_unit_id TEXT NULL, tail_unit_id TEXT NULL,
            conversion_numerator INTEGER NULL, conversion_denominator INTEGER NULL);
        CREATE TABLE godowns (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, parent_id TEXT NULL, alias TEXT NULL, third_party INTEGER NOT NULL DEFAULT 0,
            is_main_location INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, stock_group_id TEXT NOT NULL REFERENCES stock_groups(id),
            category_id TEXT NULL, base_unit_id TEXT NOT NULL REFERENCES units(id), alias TEXT NULL,
            valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL, is_taxable INTEGER NOT NULL DEFAULT 0,
            reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL, standard_cost_paisa INTEGER NULL);
        CREATE TABLE stock_opening_balances (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            stock_item_id TEXT NOT NULL REFERENCES stock_items(id), godown_id TEXT NOT NULL REFERENCES godowns(id),
            batch_label TEXT NULL, quantity_micro INTEGER NOT NULL, rate_paisa INTEGER NOT NULL,
            mfg_date TEXT NULL, expiry_date TEXT NULL);
        CREATE TABLE inventory_vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL, number INTEGER NOT NULL DEFAULT 0, date TEXT NOT NULL DEFAULT '2026-01-01');
        CREATE TABLE inventory_allocations (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id), line_order INTEGER NOT NULL,
            role INTEGER NOT NULL, stock_item_id TEXT NOT NULL REFERENCES stock_items(id),
            godown_id TEXT NOT NULL REFERENCES godowns(id), unit_id TEXT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL);
        CREATE TABLE physical_stock_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id), line_order INTEGER NOT NULL,
            stock_item_id TEXT NOT NULL REFERENCES stock_items(id), godown_id TEXT NOT NULL REFERENCES godowns(id),
            counted_qty_micro INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id TEXT NOT NULL REFERENCES vouchers(id), line_order INTEGER NOT NULL,
            stock_item_id TEXT NOT NULL REFERENCES stock_items(id), godown_id TEXT NOT NULL REFERENCES godowns(id),
            quantity_micro INTEGER NOT NULL, direction INTEGER NOT NULL, rate_paisa INTEGER NOT NULL, batch_label TEXT NULL);
        """;
}
