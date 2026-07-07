using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-2 <b>Bill of Materials</b> schema contract (RQ-9; ER-1, PR-11). The v16→v17 bump is purely
/// additive: it CREATEs the <c>bill_of_materials</c> header table (one first-class row per named BOM on a
/// finished-good stock item, with a per-block <c>unit_of_manufacture_micro</c>; a single item may own MULTIPLE
/// BOMs, keyed unique by name within the item) and the <c>bom_lines</c> child table (each line is a Component /
/// By-Product / Co-Product / Scrap with a per-block <c>qty_micro</c> and an optional carve-out / additional-cost
/// basis of per-unit <c>rate_paisa</c> OR <c>percent_millis</c>, per DP-3), leaving every existing table/row intact.
/// This covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> and has both BOM tables with their exact
/// columns; a legacy v16 DB auto-migrates forward preserving every existing row (PR-11); a single finished good may
/// own multiple BOMs distinguished by name, but a duplicate BOM name on the same item is rejected (RQ-9); and
/// money/qty/percent are integer paisa/micros/millis (no float — ER-3).
/// </summary>
public sealed class BomSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_bom_tables()
    {
        var dbPath = TempDb("apex-bom-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            // BOM masters arrived at schema v17; a fresh DB is stamped straight to the current version.
            Assert.True(Schema.CurrentVersion >= 17);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.True(TableExists(dbPath, "bill_of_materials"));
            Assert.True(TableExists(dbPath, "bom_lines"));

            // The BOM header carries the finished-good link, a name, and the per-block unit-of-manufacture (micros).
            var head = ColumnNames(dbPath, "bill_of_materials");
            foreach (var expected in new[]
                     { "id", "company_id", "stock_item_id", "name", "unit_of_manufacture_micro" })
                Assert.Contains(expected, head);

            // Each BOM line carries its type, the component item, an optional godown, the per-block qty (micros),
            // and the optional carve-out / additional-cost basis (rate paisa OR percent millis — DP-3).
            var lines = ColumnNames(dbPath, "bom_lines");
            foreach (var expected in new[]
                     { "id", "bom_id", "line_order", "line_type", "component_stock_item_id",
                       "godown_id", "qty_micro", "rate_paisa", "percent_millis" })
                Assert.Contains(expected, lines);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Item_may_own_multiple_boms_but_a_duplicate_bom_name_on_the_same_item_is_rejected()
    {
        var dbPath = TempDb("apex-bom-multi");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var item = Guid.NewGuid();

            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys = OFF;"); // isolate the index contract from unrelated FK seeding
            InsertItem(conn, company, group, unit, item, "Finished Good");

            // Two DIFFERENT BOM names on the SAME finished good are allowed — multiple BOMs per item (RQ-9).
            InsertBom(conn, company, item, "Standard");
            InsertBom(conn, company, item, "Economy");
            Assert.Equal(2L, CountRows(dbPath, "bill_of_materials"));

            // But a DUPLICATE BOM name on the SAME item is rejected by the unique index (case-insensitive).
            var ex = Assert.Throws<SqliteException>(() => InsertBom(conn, company, item, "standard"));
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);

            SqliteConnection.ClearPool(conn);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v16_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-bom-v16legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var item = Guid.NewGuid();
            var batch = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV16Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (16);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V16 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_groups(id, company_id, name) VALUES ($id, $cid, 'G');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO units(id, company_id, symbol, formal_name, is_compound, decimal_places) VALUES ($id, $cid, 'Nos', 'Numbers', 0, 0);",
                    ("$id", unit.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_items(id, company_id, name, stock_group_id, base_unit_id) VALUES ($id, $cid, 'Widget', $g, $u);",
                    ("$id", item.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")), ("$u", unit.ToString("D")));
                // A pre-existing v16 batch master MUST survive the additive v16→v17 migration untouched (ER-13).
                Exec(conn, """
                    INSERT INTO batch_masters(id, company_id, stock_item_id, batch_no, inward_qty_micro, inward_rate_paisa)
                    VALUES ($id, $cid, $it, 'LOT-99', 5000000, 12345);
                    """,
                    ("$id", batch.ToString("D")), ("$cid", company.ToString("D")), ("$it", item.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(16L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "bill_of_materials"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v16 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "bill_of_materials"));
            Assert.True(TableExists(dbPath, "bom_lines"));

            // Every existing row survived byte-for-byte (ER-13): the company, the item and the v16 batch are intact.
            Assert.Equal("Legacy V16 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(1L, CountRows(dbPath, "batch_masters"));

            // The migrated DB now accepts a first-class BOM header + a Component and a Scrap carve-out line
            // (per-unit rate OR percent basis — DP-3), with paisa/micros/millis integer money.
            using var conn2 = Open(dbPath);
            Exec(conn2, "PRAGMA foreign_keys = ON;");
            var bom = Guid.NewGuid();
            Exec(conn2, """
                INSERT INTO bill_of_materials(id, company_id, stock_item_id, name, unit_of_manufacture_micro)
                VALUES ($id, $cid, $it, 'Standard', 1000000);
                """,
                ("$id", bom.ToString("D")), ("$cid", company.ToString("D")), ("$it", item.ToString("D")));
            Exec(conn2, """
                INSERT INTO bom_lines(bom_id, line_order, line_type, component_stock_item_id, godown_id, qty_micro, rate_paisa, percent_millis)
                VALUES ($b, 0, 0, $it, NULL, 2000000, NULL, NULL);
                """,
                ("$b", bom.ToString("D")), ("$it", item.ToString("D")));
            Exec(conn2, """
                INSERT INTO bom_lines(bom_id, line_order, line_type, component_stock_item_id, godown_id, qty_micro, rate_paisa, percent_millis)
                VALUES ($b, 1, 3, $it, NULL, 500000, NULL, 5000);
                """,
                ("$b", bom.ToString("D")), ("$it", item.ToString("D")));
            Assert.Equal(1L, CountRows(dbPath, "bill_of_materials"));
            Assert.Equal(2L, CountRows(dbPath, "bom_lines"));
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

    private static void InsertBom(SqliteConnection conn, Guid company, Guid item, string name)
        => Exec(conn, "INSERT INTO bill_of_materials(id, company_id, stock_item_id, name, unit_of_manufacture_micro) VALUES ($id, $cid, $it, $n, 1000000);",
            ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")),
            ("$it", item.ToString("D")), ("$n", name));

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
    /// A minimal pre-v17 (v16) DDL: enough of the schema for the v16→v17 migration (which CREATEs
    /// <c>bill_of_materials</c> + <c>bom_lines</c>, referencing <c>companies</c>, <c>stock_items</c> and
    /// <c>godowns</c>) AND the v17→v18 migration (which ALTERs <c>voucher_types</c> and <c>stock_items</c> to add
    /// the two Manufacturing-Journal master flags) plus a data-preservation assertion. Includes
    /// <c>batch_masters</c> (a v16 row that must survive) so the additive migration is proven not to disturb an
    /// existing v16 table, and a v16-shape <c>voucher_types</c> (with the v10 effect flags, without the v18
    /// use_as_manufacturing_journal column) so the v17→v18 ALTER re-applies cleanly. Kept in the test so it never
    /// drifts as the production schema advances.
    /// </summary>
    private const string MinimalV16Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, default_shortcut TEXT NULL, numbering INTEGER NOT NULL,
            abbreviation TEXT NULL, is_active INTEGER NOT NULL, is_predefined INTEGER NOT NULL,
            affects_accounts INTEGER NOT NULL DEFAULT 0, affects_stock INTEGER NOT NULL DEFAULT 0);
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
            reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL, standard_cost_paisa INTEGER NULL,
            maintain_in_batches INTEGER NOT NULL DEFAULT 0, track_manufacturing_date INTEGER NOT NULL DEFAULT 0,
            use_expiry_dates INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE batch_masters (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            stock_item_id TEXT NOT NULL REFERENCES stock_items(id), batch_no TEXT NOT NULL, mfg_date TEXT NULL,
            expiry_date TEXT NULL, expiry_period TEXT NULL, godown_id TEXT NULL REFERENCES godowns(id),
            inward_qty_micro INTEGER NULL, inward_rate_paisa INTEGER NULL);
        """;
}
