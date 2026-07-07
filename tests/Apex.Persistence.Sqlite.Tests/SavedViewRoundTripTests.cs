using Apex.Ledger.Persistence;
using Apex.Ledger.Reports;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The saved-report-view persistence contract (RQ-8 Save View; ER-9, DP-7). A view is the CONFIG TUPLE ONLY,
/// stored per company as JSON — never a computed figure, so it can never go stale. This covers: a fresh DB
/// stamps to v14 and has <c>saved_views</c>; a legacy v13 DB auto-migrates to v14 gaining <c>saved_views</c>
/// with no data loss; save→list→get→delete round-trips; upsert by name overwrites; the stored row holds only
/// config (no figure); per-company isolation; and a round-tripped config deserializes identical.
/// </summary>
public sealed class SavedViewRoundTripTests
{
    private static SavedReportView SampleView(string kind = "TrialBalance") => new()
    {
        ReportKind = kind,
        AsOfDate = new DateOnly(2024, 4, 30),
        PeriodFrom = new DateOnly(2024, 4, 1),
        PeriodTo = new DateOnly(2024, 4, 30),
        Detailed = false,
        HideZeroBalances = true,
        ShowPercentages = true,
        ClosingStock = ClosingStockMode.InventoryDerived,
        ScenarioName = "Optimistic",
        SortKey = ReportSortKey.Amount,
        SortAscending = false,
        FilterMinRupees = 1000.50m,
        FilterMaxRupees = 250000.75m,
        FilterNameContains = "cash",
        ComparativeColumns = new List<SavedComparativeColumn>
        {
            new() { Label = "Apr", PeriodFrom = new DateOnly(2024, 4, 1), PeriodTo = new DateOnly(2024, 4, 30) },
            new() { Label = "What-if", ScenarioName = "Optimistic" },
        },
    };

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_v14_with_saved_views_table()
    {
        var dbPath = TempDb("apex-sv-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            // saved_views was introduced at v14; a fresh DB stamps to the current version (>= 14) and has it.
            Assert.True(Schema.CurrentVersion >= 14);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "saved_views"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_list_get_delete_round_trip()
    {
        var dbPath = TempDb("apex-sv-crud");
        try
        {
            var companyId = Guid.NewGuid();
            var view = SampleView();
            using var store = new SqliteCompanyStore(dbPath);

            store.Save(companyId, "My TB", view);

            var list = store.List(companyId);
            Assert.Single(list);
            Assert.Equal("My TB", list[0].Name);

            var got = store.Get(companyId, "My TB");
            Assert.NotNull(got);
            Assert.Equal(view, got);                       // identical config round-trip
            Assert.Equal(view, store.Get(companyId, "MY tb")); // name lookup is case-insensitive

            store.Delete(companyId, "my tb");              // case-insensitive delete
            Assert.Empty(store.List(companyId));
            Assert.Null(store.Get(companyId, "My TB"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_upserts_by_name_overwriting_existing()
    {
        var dbPath = TempDb("apex-sv-upsert");
        try
        {
            var companyId = Guid.NewGuid();
            using var store = new SqliteCompanyStore(dbPath);

            store.Save(companyId, "View A", SampleView("TrialBalance"));
            store.Save(companyId, "view a", SampleView("BalanceSheet")); // same name (case-insensitive) → overwrite

            var list = store.List(companyId);
            Assert.Single(list);                                          // still exactly one row
            Assert.Equal("BalanceSheet", store.Get(companyId, "View A")!.ReportKind);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Stored_row_holds_only_config_no_computed_figure()
    {
        var dbPath = TempDb("apex-sv-configonly");
        try
        {
            var companyId = Guid.NewGuid();
            using (var store = new SqliteCompanyStore(dbPath))
                store.Save(companyId, "Cfg", SampleView());

            var json = ReadConfigJson(dbPath, companyId, "Cfg");
            // The persisted config carries the report kind, period, depth, sort/filter and F12 options...
            Assert.Contains("TrialBalance", json);
            Assert.Contains("2024-04-30", json);
            // ...but never a computed report figure key. (The report KIND token e.g. "TrialBalance" is config,
            // not a figure, so we assert against figure-specific keys, not a bare "Balance".)
            foreach (var forbidden in new[] { "Debit", "Credit", "GrandTotal", "TotalDebit", "TotalCredit",
                                              "NetProfit", "ClosingValue", "ClosingBalance", "OpeningBalance" })
                Assert.DoesNotContain(forbidden, json);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Views_are_isolated_per_company()
    {
        var dbPath = TempDb("apex-sv-isolation");
        try
        {
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            using var store = new SqliteCompanyStore(dbPath);

            store.Save(companyA, "Shared Name", SampleView("TrialBalance"));

            Assert.Single(store.List(companyA));
            Assert.Empty(store.List(companyB));                 // a view in A is invisible in B
            Assert.Null(store.Get(companyB, "Shared Name"));

            // B can hold its OWN view under the same name without colliding with A's.
            store.Save(companyB, "Shared Name", SampleView("BalanceSheet"));
            Assert.Equal("TrialBalance", store.Get(companyA, "Shared Name")!.ReportKind);
            Assert.Equal("BalanceSheet", store.Get(companyB, "Shared Name")!.ReportKind);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v13_database_auto_migrates_to_v14_gaining_saved_views_no_data_loss()
    {
        var dbPath = TempDb("apex-sv-v13legacy");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV13Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (13);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V13 Co');",
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(13L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "saved_views"));

            using (var store = new SqliteCompanyStore(dbPath)) // opens v13 → migrates forward (>= v14)
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                Assert.True(TableExists(dbPath, "saved_views"));
                // Existing v13 data survived the pure CREATE-only migration.
                Assert.Equal("Legacy V13 Co", ReadCompanyName(dbPath, companyId));
                // And the migrated DB now accepts and round-trips a saved view.
                store.Save(companyId, "After Migrate", SampleView());
                Assert.Equal(SampleView(), store.Get(companyId, "After Migrate"));
            }
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) { TempDbFile.Delete(dbPath); }

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

    private static string ReadConfigJson(string dbPath, Guid companyId, string name)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT config_json FROM saved_views WHERE company_id = $cid AND name = $name;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        var json = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return json;
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
    /// A minimal pre-v14 (v13) DDL: enough of the schema for the v13→v14 migration (which only CREATEs the
    /// <c>saved_views</c> table + its unique index, referencing <c>companies(id)</c>) and a data-preservation
    /// assertion. Because opening the store migrates all the way to <see cref="Schema.CurrentVersion"/> (past
    /// v14), the four v16-touched stock-line tables (<c>stock_opening_balances</c>, <c>inventory_allocations</c>,
    /// <c>physical_stock_lines</c>, <c>voucher_inventory_lines</c>) are included in their pre-v16 shape so the
    /// v15→v16 ADD-COLUMN steps have real targets, and a pre-v18 <c>voucher_types</c> (with the v10 effect flags,
    /// without the v18 use_as_manufacturing_journal column) plus a pre-v18 <c>stock_items</c> (without
    /// set_components) so the v17→v18 ADD-COLUMN steps have real targets too. Kept in the test so it never drifts
    /// as the production <see cref="Schema"/> advances past v13.
    /// </summary>
    private const string MinimalV13Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (
            id   TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL
        );
        CREATE TABLE stock_opening_balances (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, stock_item_id TEXT NOT NULL,
            godown_id TEXT NOT NULL, batch_label TEXT NULL, quantity_micro INTEGER NOT NULL,
            rate_paisa INTEGER NOT NULL, mfg_date TEXT NULL, expiry_date TEXT NULL);
        CREATE TABLE inventory_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
            line_order INTEGER NOT NULL, role INTEGER NOT NULL, stock_item_id TEXT NOT NULL,
            godown_id TEXT NOT NULL, unit_id TEXT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL);
        CREATE TABLE physical_stock_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
            line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL,
            counted_qty_micro INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE voucher_inventory_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL,
            stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE stock_items (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
            stock_group_id TEXT NOT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL, alias TEXT NULL,
            valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL, is_taxable INTEGER NOT NULL DEFAULT 0,
            reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL, standard_cost_paisa INTEGER NULL,
            gst_hsn_sac TEXT NULL, gst_taxability INTEGER NULL, gst_rate_bp INTEGER NULL, gst_supply_type INTEGER NULL);
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL, base_type INTEGER NOT NULL,
            default_shortcut TEXT NULL, numbering INTEGER NOT NULL, abbreviation TEXT NULL,
            is_active INTEGER NOT NULL, is_predefined INTEGER NOT NULL,
            affects_accounts INTEGER NOT NULL DEFAULT 0, affects_stock INTEGER NOT NULL DEFAULT 0);
        """;
}
