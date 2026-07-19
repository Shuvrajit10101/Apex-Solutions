using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-6 <b>GSTR-2A/2B inbound</b> schema contract (v42→v43; ER-13). The bump is additive: four new tables
/// (<c>gstr2b_snapshots</c>, <c>gstr2b_lines</c>, <c>ims_status</c>, <c>gstr2b_recon</c> + their indexes) and two §17(5)
/// columns each on <c>stock_items</c> + <c>ledgers</c>. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/>
/// with the new tables/columns; a legacy v42 DB auto-migrates forward preserving every row (new tables empty, new columns
/// default 0/0 — ER-13) and re-opening is idempotent; and a save/load round-trips an imported snapshot (owning its lines,
/// incl. cess + the ITC bifurcation) + the advisory reconciliation results (a matched voucher pointer + an in-portal-only
/// row).
/// </summary>
public sealed class Gstr2bSchemaTests
{
    private const string GstinMe = "27AAPFU0939F1ZV";
    private const string GstinSupplierA = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 10, 10);
    private static readonly DateTimeOffset Imported = new(2025, 11, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_tables_and_columns()
    {
        var dbPath = TempDb("apex-2b-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 43);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            foreach (var table in new[] { "gstr2b_snapshots", "gstr2b_lines", "ims_status", "gstr2b_recon" })
                Assert.True(TableExists(dbPath, table), $"missing table {table}");

            Assert.Contains("itc_eligibility", ColumnNames(dbPath, "stock_items"));
            Assert.Contains("blocked_credit_category", ColumnNames(dbPath, "stock_items"));
            Assert.Contains("itc_eligibility", ColumnNames(dbPath, "ledgers"));
            Assert.Contains("blocked_credit_category", ColumnNames(dbPath, "ledgers"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v42_database_auto_migrates_to_v43_preserving_every_row()
    {
        var dbPath = TempDb("apex-2b-v42legacy");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV42Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (42);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ('c-1', 'Legacy V42 Co');");
                Exec(conn, "INSERT INTO stock_items(id, name) VALUES ('s-1', 'Widget');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(42L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("itc_eligibility", ColumnNames(dbPath, "stock_items"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v42 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "gstr2b_snapshots"));
            Assert.True(TableExists(dbPath, "ims_status"));
            Assert.Contains("itc_eligibility", ColumnNames(dbPath, "stock_items"));

            // Every existing row survived; new tables empty; new columns default 0/0 (ER-13).
            Assert.Equal("Legacy V42 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gstr2b_snapshots;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gstr2b_recon;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT itc_eligibility FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT blocked_credit_category FROM stock_items LIMIT 1;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_an_imported_snapshot_and_reconciliation()
    {
        var dbPath = TempDb("apex-2b-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("2B Co", FyStart);
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });

            var supplier = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Supplier A",
                c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false, maintainBillByBill: true)
            {
                PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplierA, StateCode = "24" },
            };
            c.AddLedger(supplier);
            var purchases = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
            c.AddLedger(purchases);

            var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10000m), 1800) }, true, GstTaxDirection.Input);
            var credit = new Money(10000m + tax.TotalTax.Amount);
            var lines = new List<EntryLine>
            {
                new(purchases.Id, Money.FromRupees(10000m), DrCr.Debit),
                new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, "INV-001", credit) }),
            };
            lines.AddRange(tax.TaxLines);
            new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines, partyId: supplier.Id));

            var snapshot = new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe,
                new DateOnly(2025, 11, 14), "HASH123", Imported, 180_000, 0, 0, 20_000, new[]
                {
                    new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.B2b, "INV-001", "INV001", D1,
                        "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                    // A non-RCM unbooked supplier ⇒ InPortalOnly (the recon result that round-trips below).
                    new Gstr2bLine(Guid.NewGuid(), "09ZZZZZ0000Z1Z9", "Unbooked Co", Gstr2bDocType.B2b, "UNB-1", "UNB1", D1,
                        "27", 300_000, 54_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                    // A supplier-RCM line: PERSISTS as immutable external data but is EXCLUDED from reconciliation (finding #2).
                    new Gstr2bLine(Guid.NewGuid(), "06ZZZZZ0000Z1Z5", "Other Co", Gstr2bDocType.CreditNote, "CN-9", "CN9", D1,
                        "27", 500_000, 90_000, 0, 0, 20_000, itcAvailable: false, "P", reverseCharge: true),
                });
            c.AddGstr2bSnapshot(snapshot);

            var report = Gstr2bReconciler.Reconcile(c, snapshot, FyStart, new DateOnly(2026, 3, 31), ReconTolerance.Exact);
            foreach (var result in report.ToPersistedResults(Guid.NewGuid, Imported))
                c.AddGstr2bReconResult(result);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            // Snapshot + all three lines survived (paisa + cess + ITC bifurcation exact).
            var snap = Assert.Single(reloaded.Gstr2bSnapshots);
            Assert.Equal("2025-10", snap.ReturnPeriod);
            Assert.Equal("HASH123", snap.SourceFileHash);
            Assert.Equal(Imported, snap.ImportedAt);
            Assert.Equal(3, snap.Lines.Count);
            var tob = snap.Lines.Single(l => l.DocNumber == "CN-9");
            Assert.Equal(20_000, tob.CessPaisa);
            Assert.False(tob.ItcAvailable);
            Assert.Equal("P", tob.ItcUnavailableReason);
            Assert.True(tob.ReverseCharge);
            Assert.Equal(Gstr2bDocType.CreditNote, tob.DocType);

            // Reconciliation results survived: a Matched pointer to the purchase + an InPortalOnly row (the non-RCM
            // unbooked supplier). The supplier-RCM line is excluded from reconciliation (finding #2), so it has no result.
            var matched = reloaded.Gstr2bReconResults.Single(r => r.Bucket == ReconBucket.Matched);
            var purchase = reloaded.Vouchers.Single(v => reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Purchase);
            Assert.Equal(purchase.Id, matched.MatchedVoucherId);
            var portalOnly = reloaded.Gstr2bReconResults.Single(r => r.Bucket == ReconBucket.InPortalOnly);
            Assert.Null(portalOnly.MatchedVoucherId);
            Assert.Equal("UNB-1", snap.FindLine(portalOnly.LineId)!.DocNumber);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_recon_tolerance()
    {
        // Finding #5: a non-zero reconciliation tolerance must survive save→reload (it was silently lost before v43).
        var dbPath = TempDb("apex-2b-recontol");
        try
        {
            var c = CompanyFactory.CreateSeeded("Recon Tol Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
                ReconValueTolerance = new Money(1.50m), ReconDateWindowDays = 3,
            });

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            Assert.Equal(new Money(1.50m), reloaded.Gst!.ReconValueTolerance);
            Assert.Equal(3, reloaded.Gst!.ReconDateWindowDays);
            // Persisted verbatim (150 paisa, 3 days) — a matching parameter only, never a posted figure (ER-14).
            Assert.Equal(150L, ReadScalar(dbPath, "SELECT recon_value_tolerance_paisa FROM companies LIMIT 1;"));
            Assert.Equal(3L, ReadScalar(dbPath, "SELECT recon_date_window_days FROM companies LIMIT 1;"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_ims_actions_and_section17_5_flags()
    {
        // Phase 9 slice 6b: the ims_status table + the §17(5) columns on ledgers/stock_items persist + reload exact.
        var dbPath = TempDb("apex-2b-ims");
        try
        {
            var c = CompanyFactory.CreateSeeded("IMS Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });

            // A §17(5)-flagged Purchases ledger (Ineligible) + a §17(5)-blocked stock item (BlockedSection17_5).
            var purchases = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Purchases",
                c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true)
            {
                SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, ItcEligibility = ItcEligibility.Ineligible },
            };
            c.AddLedger(purchases);
            var inv = new InventoryService(c);
            var sg = inv.CreateStockGroup("Vehicles");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
            var car = inv.CreateStockItem("Company Car", sg.Id, nos.Id);
            car.Gst = new StockItemGstDetails
            {
                HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                ItcEligibility = ItcEligibility.BlockedSection17_5, BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
            };

            // A snapshot with two non-RCM lines + an IMS decision on each (an Accept + Oct-2025 partial reversal, and a Pending).
            var snapshot = new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe,
                new DateOnly(2025, 11, 14), "IMSHASH", Imported, 0, 0, 0, 0, new[]
                {
                    new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.CreditNote, "CN-7", "CN7", D1,
                        "27", 200_000, 36_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                    new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.B2b, "INV-9", "INV9", D1,
                        "27", 500_000, 90_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                });
            c.AddGstr2bSnapshot(snapshot);
            var cn = snapshot.Lines.Single(l => l.DocNumber == "CN-7");
            var b2b = snapshot.Lines.Single(l => l.DocNumber == "INV-9");
            ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "partial per contract",
                declaredReversalPaisa: 12_000, actedOn: new DateOnly(2025, 11, 18));
            ImsService.SetAction(c, b2b.Id, ImsStatus.Pending);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            // IMS decisions survived (status + paisa + remarks + acted-on exact), keyed to their reloaded lines.
            Assert.Equal(2, reloaded.ImsActions.Count);
            var snap = Assert.Single(reloaded.Gstr2bSnapshots);
            var rcn = snap.Lines.Single(l => l.DocNumber == "CN-7");
            var cnAction = reloaded.FindImsActionForLine(rcn.Id)!;
            Assert.Equal(ImsStatus.Accepted, cnAction.Status);
            Assert.Equal(12_000, cnAction.DeclaredReversalPaisa);
            Assert.Equal("partial per contract", cnAction.Remarks);
            Assert.Equal(new DateOnly(2025, 11, 18), cnAction.ActedOn);
            var rb2b = snap.Lines.Single(l => l.DocNumber == "INV-9");
            Assert.Equal(ImsStatus.Pending, reloaded.FindImsActionForLine(rb2b.Id)!.Status);
            Assert.Null(reloaded.FindImsActionForLine(rb2b.Id)!.DeclaredReversalPaisa);

            // §17(5) flags survived on BOTH the ledger block and the item block.
            Assert.Equal(ItcEligibility.Ineligible, reloaded.FindLedgerByName("Purchases")!.SalesPurchaseGst!.ItcEligibility);
            var reCar = reloaded.StockItems.Single(i => i.Name == "Company Car");
            Assert.Equal(ItcEligibility.BlockedSection17_5, reCar.Gst!.ItcEligibility);
            Assert.Equal(BlockedCreditCategory.MotorVehicles, reCar.Gst!.BlockedCreditCategory);
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

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A minimal pre-v43 (v42) DDL: the v42→v43 migration creates the four gstr2b/ims tables (FK companies +
    /// gstr2b_snapshots + gstr2b_lines + vouchers) and ALTERs <c>stock_items</c> + <c>ledgers</c> (the §17(5) columns), so
    /// a bare <c>schema_version</c> + <c>companies</c> + <c>vouchers</c> + <c>stock_items</c> + <c>ledgers</c> is enough to
    /// exercise the migration and its data-preservation.</summary>
    private const string MinimalV42Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        -- voucher_inventory_lines is required because the chain now runs through the v45 -> v46 item-invoice
        -- line-unit migration, whose ALTER TABLE voucher_inventory_lines ADD COLUMN unit_id needs the table to
        -- exist. A real database of this vintage always has it (created at v12); this fixture is a minimal
        -- hand-written subset, so the table is declared here for the ALTER to land on.
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, stock_item_id TEXT NOT NULL DEFAULT '', godown_id TEXT NOT NULL DEFAULT '', quantity_micro INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 0, rate_paisa INTEGER NOT NULL DEFAULT 0);
        """;
}
