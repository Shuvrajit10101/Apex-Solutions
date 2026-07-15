using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-5 <b>e-Way Bill</b> schema contract (v41→v42; ER-13). The bump is additive: two new tables
/// (<c>eway_bills</c> + <c>eway_state_thresholds</c> + their indexes) and the five non-secret e-Way config columns on
/// <c>companies</c>. The live NIC path REUSES the shared <c>gst_connector_mode</c> + <c>nic_*_enc</c> columns — no new
/// secret column. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables/columns; a legacy
/// v41 DB auto-migrates forward preserving every row (new tables empty, new columns default 0/NULL/₹50,000/0/1 — ER-13)
/// and re-opening is idempotent; and a save/load round-trips a Generated e-Way record + a per-state override + config.
/// </summary>
public sealed class EWaySchemaTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateTimeOffset Gen = new(2025, 4, 10, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_tables_and_columns()
    {
        var dbPath = TempDb("apex-eway-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 42);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "eway_bills"));
            Assert.True(TableExists(dbPath, "eway_state_thresholds"));

            foreach (var col in new[]
            {
                "eway_bill_enabled", "eway_applicable_from", "eway_threshold_paisa", "eway_consignment_basis",
                "eway_intrastate_applicable",
            })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v41_database_auto_migrates_to_v42_preserving_every_row()
    {
        var dbPath = TempDb("apex-eway-v41legacy");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV41Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (41);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ('c-1', 'Legacy V41 Co');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(41L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("eway_bill_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v41 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "eway_bills"));
            Assert.True(TableExists(dbPath, "eway_state_thresholds"));
            Assert.Contains("eway_bill_enabled", ColumnNames(dbPath, "companies"));

            // Every existing row survived; new tables empty; new columns default 0/₹50,000/1 (ER-13).
            Assert.Equal("Legacy V41 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM eway_bills;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM eway_state_thresholds;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT eway_bill_enabled FROM companies LIMIT 1;"));
            Assert.Equal(5000000L, ReadScalar(dbPath, "SELECT eway_threshold_paisa FROM companies LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT eway_intrastate_applicable FROM companies LIMIT 1;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_a_generated_eway_record_and_config()
    {
        var dbPath = TempDb("apex-eway-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("e-Way Co", FyStart);
            var gst = new GstService(c);
            var config = new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
                EWayBillEnabled = true, EWayApplicableFrom = new DateOnly(2025, 4, 5),
                EWayThreshold = new Money(100000m), ConsignmentBasis = EWayConsignmentBasis.TaxablePlusExempt,
                EWayIntraStateApplicable = false,
            };
            config.AddEWayStateThreshold(new EWayStateThreshold(Guid.NewGuid(), "07", EWayTransactionType.Regular, new Money(100000m)));
            gst.EnableGst(config);

            var inv = new InventoryService(c);
            var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id,
                inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS").Id);
            item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(40000m));

            var sales = Add(c, "Sales", "Sales Accounts", false);
            // Inter-state Gujarat buyer + a ₹1,00,000 consignment (₹1,18,000 incl. IGST) so the movement is Required even
            // with the ₹1,00,000 threshold + intra-state-exempt config exercised above.
            var b2b = Add(c, "Gujarat Debtor", "Sundry Debtors", true);
            b2b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = "24AAACC1206D1ZM", StateCode = "24" };
            var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(100000m), 1800) },
                interState: true, GstTaxDirection.Output);
            var lines = new List<EntryLine>
            {
                new(b2b.Id, new Money(100000m + tax.TotalTax.Amount), DrCr.Debit),
                new(sales.Id, Money.FromRupees(100000m), DrCr.Credit),
            };
            lines.AddRange(tax.TaxLines);
            var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines, partyId: b2b.Id,
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(100000m)) }));

            var svc = new EWayBillService(c);
            var record = svc.PrepareRecord(sale, SaleDate);
            svc.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250, "LR-99", isOverDimensionalCargo: false);
            var validUpto = EWayValidity.ValidUpto(Gen, 250, false);
            svc.RecordPortalResponse(record, "231000000123", Gen, validUpto);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            // Config survived (paisa / enum / date / bool exact) + the per-state override.
            var g = reloaded.Gst!;
            Assert.True(g.EWayBillEnabled);
            Assert.Equal(new DateOnly(2025, 4, 5), g.EWayApplicableFrom);
            Assert.Equal(new Money(100000m), g.EWayThreshold);
            Assert.Equal(EWayConsignmentBasis.TaxablePlusExempt, g.ConsignmentBasis);
            Assert.False(g.EWayIntraStateApplicable);
            var t = Assert.Single(g.EWayStateThresholds);
            Assert.Equal("07", t.StateCode);
            Assert.Equal(new Money(100000m), t.Threshold);

            // The Generated record survived, re-linked to the imported voucher (number/validity/Part-B verbatim).
            var r = Assert.Single(reloaded.EWayBillRecords);
            Assert.Equal(EWayStatus.Generated, r.Status);
            Assert.Equal("231000000123", r.EwbNumber);
            Assert.Equal(Gen, r.GeneratedAt);
            Assert.Equal(validUpto, r.ValidUpto);
            Assert.Equal(EWayTransportMode.Road, r.Mode);
            Assert.Equal("MH12AB1234", r.VehicleNumber);
            Assert.Equal(250, r.DistanceKm);
            Assert.Equal("LR-99", r.TransportDocNo);
            Assert.Equal(11_800_000, r.ConsignmentValuePaisa); // ₹1,18,000 (₹1,00,000 + ₹18,000 IGST)
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

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

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A minimal pre-v42 (v41) DDL: the v41→v42 migration creates <c>eway_bills</c> (FK companies + vouchers) +
    /// <c>eway_state_thresholds</c> (FK companies) and ALTERs <c>companies</c>. <c>stock_items</c> + <c>ledgers</c> are
    /// included because the migrate-to-current chain runs through the v42→v43 §17(5) ALTER on both, which needs them.</summary>
    private const string MinimalV41Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        """;
}
