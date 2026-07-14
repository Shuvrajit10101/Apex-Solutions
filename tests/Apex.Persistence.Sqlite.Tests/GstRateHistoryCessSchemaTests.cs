using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-1 <b>GST 2.0 rate-history + Compensation-Cess</b> schema contract (v37→v38; ER-13). The bump is
/// additive: two new tables (<c>gst_rate_history</c>, <c>gst_cess_rates</c>) + their indexes, and seven ALTER-added
/// columns each on <c>stock_items</c> and <c>ledgers</c>. Covers: a fresh DB stamps to
/// <see cref="Schema.CurrentVersion"/> with the new tables/columns; a legacy v37 DB auto-migrates forward preserving
/// every row (new tables empty, new columns default 0/NULL — ER-13) and re-opening is idempotent; a save/load
/// round-trips a cess-bearing company (rate history + cess rates + item RSP/cess paisa-exact).
/// </summary>
public sealed class GstRateHistoryCessSchemaTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_tables_and_columns()
    {
        var dbPath = TempDb("apex-gst2-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 38);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.True(TableExists(dbPath, "gst_rate_history"));
            Assert.True(TableExists(dbPath, "gst_cess_rates"));

            foreach (var col in new[] { "gst_valuation_basis", "cess_applicable", "cess_valuation_mode", "cess_rate_bp",
                                        "cess_per_unit_paisa", "cess_rsp_factor_millis", "rsp_paisa" })
                Assert.Contains(col, ColumnNames(dbPath, "stock_items"));

            foreach (var col in new[] { "sp_gst_valuation_basis", "sp_cess_applicable", "sp_cess_valuation_mode",
                                        "sp_cess_rate_bp", "sp_cess_per_unit_paisa", "sp_cess_rsp_factor_millis", "sp_rsp_paisa" })
                Assert.Contains(col, ColumnNames(dbPath, "ledgers"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v37_database_auto_migrates_to_v38_preserving_every_row()
    {
        var dbPath = TempDb("apex-gst2-v37legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV37Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (37);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V37 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_items(id, name) VALUES ('item-1', 'Widget');");
                Exec(conn, "INSERT INTO ledgers(id, name) VALUES ('led-1', 'Sales');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(37L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("cess_applicable", ColumnNames(dbPath, "stock_items"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v37 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "gst_rate_history"));
            Assert.True(TableExists(dbPath, "gst_cess_rates"));
            Assert.Contains("cess_applicable", ColumnNames(dbPath, "stock_items"));
            Assert.Contains("sp_cess_applicable", ColumnNames(dbPath, "ledgers"));

            // Every existing row survived; the new tables are empty and the new columns default 0/NULL (ER-13).
            Assert.Equal("Legacy V37 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal("Widget", ReadScalarStr(dbPath, "SELECT name FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gst_rate_history;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gst_cess_rates;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT cess_applicable FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT gst_valuation_basis FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT sp_cess_applicable FROM ledgers LIMIT 1;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_a_cess_bearing_company()
    {
        var dbPath = TempDb("apex-gst2-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Advanced GST Co", new DateOnly(2025, 4, 1));
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = new DateOnly(2025, 4, 1), Periodicity = GstReturnPeriodicity.Monthly,
            });
            gst.SeedAdvancedGst();

            var inv = new InventoryService(c);
            var pan = inv.CreateStockItem("Pan Masala", inv.CreateStockGroup("Tobacco").Id, inv.CreateSimpleUnit("Pkt", "Packets").Id);
            pan.Gst = new StockItemGstDetails
            {
                HsnSac = "21069020", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
                ValuationBasis = GstValuationBasis.RetailSalePrice, CessApplicable = true,
                CessValuationMode = CessValuationMode.RetailSalePriceFactor, CessRspFactorMillis = 320,
                RetailSalePrice = new Money(100m),
            };

            var historyCount = c.Gst!.RateHistory.Count;
            var cessCount = c.Gst!.CessRates.Count;

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            Assert.Equal(historyCount, reloaded.Gst!.RateHistory.Count);
            Assert.Equal(cessCount, reloaded.Gst!.CessRates.Count);

            // A specific dated rate-history row survived (car 40% from 22-Sep-2025).
            Assert.Contains(reloaded.Gst!.RateHistory, h => h.HsnSac == "8703" && h.RateBasisPoints == 4000
                && h.EffectiveFrom == new DateOnly(2025, 9, 22) && h.EffectiveTo is null);
            // The legacy car row survived with its closed window.
            Assert.Contains(reloaded.Gst!.RateHistory, h => h.HsnSac == "8703" && h.RateBasisPoints == 2800
                && h.EffectiveTo == new DateOnly(2025, 9, 21) && h.RateClass == GstRateClass.Legacy);
            // A cess row survived paisa-exact (coal ₹400/tonne, specific).
            Assert.Contains(reloaded.Gst!.CessRates, r => r.HsnSac == "2701"
                && r.ValuationMode == CessValuationMode.Specific && r.CessPerUnit == new Money(400m));

            // The item's RSP + cess fields survived.
            var item = reloaded.FindStockItemByName("Pan Masala")!;
            Assert.Equal(GstValuationBasis.RetailSalePrice, item.Gst!.ValuationBasis);
            Assert.True(item.Gst.CessApplicable);
            Assert.Equal(CessValuationMode.RetailSalePriceFactor, item.Gst.CessValuationMode);
            Assert.Equal(320, item.Gst.CessRspFactorMillis);
            Assert.Equal(new Money(100m), item.Gst.RetailSalePrice);
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

    /// <summary>A minimal pre-v38 (v37) DDL: enough for the v37→v38 migration (two CREATE TABLE + ALTERs on
    /// <c>stock_items</c>/<c>ledgers</c>) plus data-preservation assertions. Kept in the test so it never drifts as the
    /// production schema advances.</summary>
    private const string MinimalV37Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;
}
