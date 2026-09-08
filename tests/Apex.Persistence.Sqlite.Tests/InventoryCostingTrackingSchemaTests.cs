using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v57 → v58 (W-K1; census 9.6 Job Costing · 9.7 Item Cost Tracking · 9.8 Tracking Numbers · 9.9 Stock
/// Journal Voucher Class) — <b>Inventory costing &amp; tracking</b>: three <c>companies</c> feature flags, the
/// <c>godowns.job_cost_centre_id</c> job/project link, the <c>tracking_number</c> + <c>cost_tracking_number</c>
/// pair on BOTH stock-line tables, and the <c>voucher_type_classes</c> table.
///
/// <para>The "genuine v57 database" is manufactured with <see cref="SchemaDowngrade.V58ToV57"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>The load-bearing test in this file is
/// <see cref="Tracking_number_round_trips_on_BOTH_line_tables"/>.</b> Census row 9.8's entire mechanism is that
/// the SAME string appears on a Receipt Note's movement (<c>inventory_allocations</c>) and on the Purchase bill's
/// item line (<c>voucher_inventory_lines</c>) — two different tables written by two different insert paths. A
/// column added to one and forgotten on the other would leave the feature half-built in a way no single-table
/// test could see, and the Bills Pending report would then show every receipt as permanently unbilled.</para>
/// </summary>
public sealed class InventoryCostingTrackingSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    /// <summary>Fails on today's main, where <see cref="Schema.CurrentVersion"/> is 57 and none of these columns
    /// or tables exists in either database.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v57_to_v58_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-k1-v57-migrated");
        var freshPath = TempDbFile.NewPath("apex-k1-v58-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Track Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Track Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(57L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Every v58 object really is absent from the manufactured v57 book.
            foreach (var col in Schema.V58CompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "companies"));
            foreach (var col in Schema.V58GodownColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "godowns"));
            foreach (var col in Schema.V58TrackingLineColumns)
            {
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "inventory_allocations"));
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "voucher_inventory_lines"));
            }
            foreach (var t in Schema.V58ClassTables) Assert.False(TableExists(migratedPath, t));

            // Reopen through the production store — the v57 → v58 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the objects THIS migration adds are compared: the downgrade's rebuild is not asked to
            // reproduce pre-existing declarations byte for byte. Whole-schema parity across the full v1→current
            // chain is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V58CompanyColumns)
                Assert.Equal(ColumnContract(freshPath, "companies", col),
                             ColumnContract(migratedPath, "companies", col));
            foreach (var col in Schema.V58GodownColumns)
                Assert.Equal(ColumnContract(freshPath, "godowns", col),
                             ColumnContract(migratedPath, "godowns", col));
            foreach (var col in Schema.V58TrackingLineColumns)
            {
                Assert.Equal(ColumnContract(freshPath, "inventory_allocations", col),
                             ColumnContract(migratedPath, "inventory_allocations", col));
                Assert.Equal(ColumnContract(freshPath, "voucher_inventory_lines", col),
                             ColumnContract(migratedPath, "voucher_inventory_lines", col));
            }
            foreach (var t in Schema.V58ClassTables)
            {
                Assert.True(TableExists(migratedPath, t));
                foreach (var col in ColumnNames(freshPath, t))
                    Assert.Equal(ColumnContract(freshPath, t, col), ColumnContract(migratedPath, t, col));
            }
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 The migration back-fills nothing: every migrated company reads all three features OFF, every godown is
    /// an ordinary location, and no stock line carries either tracking datum — which is exactly what a v57 book
    /// was (ER-13). Turning a feature ON during an upgrade would surface reports and fields the operator never
    /// asked for.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v57_book_reads_every_feature_off_and_nothing_tracked()
    {
        var path = TempDbFile.NewPath("apex-k1-backfill");
        try
        {
            var legacy = CompanyFactory.CreateSeeded("Backfill Track Co", FyStart);
            new InventoryService(legacy).CreateGodown("Site A");
            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            Assert.False(loaded.UseTrackingNumbers);
            Assert.False(loaded.EnableCostTracking);
            Assert.False(loaded.EnableJobCosting);
            Assert.NotEmpty(loaded.Godowns);
            Assert.All(loaded.Godowns, g => Assert.Null(g.JobCostCentreId));
            Assert.All(loaded.VoucherTypes, t => Assert.Empty(t.Classes));

            // And the stored cells really are NULL — not a string dressed up as "unset".
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM godowns WHERE job_cost_centre_id IS NOT NULL;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round-trip

    /// <summary>
    /// 🔴 <b>THE LOAD-BEARING TEST (census 9.8).</b> One tracking number is keyed on a Receipt Note's movement
    /// (<c>inventory_allocations</c>) AND on the Purchase bill's item line (<c>voucher_inventory_lines</c>). Both
    /// must survive the round trip, because the whole mechanism is the two strings matching: a column persisted
    /// on one table and dropped on the other would leave the receipt permanently unreconcilable and no
    /// single-table assertion would notice.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Tracking_number_round_trips_on_BOTH_line_tables()
    {
        var path = TempDbFile.NewPath("apex-k1-tracking-roundtrip");
        try
        {
            var c = Book("Tracking Co", out var item, out var godown, out var supplier);
            c.UseTrackingNumbers = true;
            c.EnableCostTracking = true;

            // The GOODS side — a Receipt Note movement.
            var note = new InventoryVoucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Receipt Note")!.Id, new DateOnly(2025, 4, 5),
                new[]
                {
                    new InventoryAllocation(item.Id, godown.Id, 40m, StockDirection.Inward,
                        rate: Money.FromRupees(10m), trackingNumber: "TRK/1", costTrackingNumber: "LOT/1"),
                },
                number: 1);
            c.AddInventoryVoucher(note);

            // The BILL side — a Purchase item-invoice quoting the SAME tracking number.
            new LedgerService(c).Post(PurchaseBill(c, supplier, item, godown, new DateOnly(2025, 4, 9), 40m, 10m, "TRK/1", "LOT/1"));

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            // The raw cells on BOTH tables.
            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM inventory_allocations WHERE tracking_number = 'TRK/1' AND cost_tracking_number = 'LOT/1';"));
            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM voucher_inventory_lines WHERE tracking_number = 'TRK/1' AND cost_tracking_number = 'LOT/1';"));

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var reloadedNote = Assert.Single(loaded.InventoryVouchers);
            var alloc = Assert.Single(reloadedNote.Allocations);
            Assert.Equal("TRK/1", alloc.TrackingNumber);
            Assert.Equal("LOT/1", alloc.CostTrackingNumber);

            var bill = Assert.Single(loaded.Vouchers);
            var line = Assert.Single(bill.InventoryLines);
            Assert.Equal("TRK/1", line.TrackingNumber);
            Assert.Equal("LOT/1", line.CostTrackingNumber);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>An untracked line round-trips as NULL on both columns — not as the empty string, which would
    /// make "not entered" a distinct report bucket from "no value" (census 9.8).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_blank_tracking_number_stores_NULL_not_an_empty_string()
    {
        var path = TempDbFile.NewPath("apex-k1-blank-tracking");
        try
        {
            var c = Book("Blank Track Co", out var item, out var godown, out _);
            c.AddInventoryVoucher(new InventoryVoucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Receipt Note")!.Id, new DateOnly(2025, 4, 5),
                // "   " must normalise to null in the domain object, before persistence ever sees it.
                new[] { new InventoryAllocation(item.Id, godown.Id, 5m, StockDirection.Inward, trackingNumber: "   ") },
                number: 1));

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM inventory_allocations WHERE tracking_number IS NULL;"));
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM inventory_allocations WHERE tracking_number = '';"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>Census 9.6 — a godown's job/project cost centre round-trips, and a second Save neither
    /// duplicates the godown nor loses the link.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Job_cost_centre_round_trips_and_survives_a_second_save()
    {
        var path = TempDbFile.NewPath("apex-k1-jobcc-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Job Co", FyStart);
            c.EnableJobCosting = true;
            var centre = new CostCentre(Guid.NewGuid(), "Bridge Project",
                c.CostCategories.First().Id);
            c.AddCostCentre(centre);
            var inventory = new InventoryService(c);
            var site = inventory.CreateGodown("Site A", jobCostCentreId: centre.Id);
            var plain = inventory.CreateGodown("Site B");

            using var store = new SqliteCompanyStore(path);
            store.Save(c);
            store.Save(c);   // the delete-all + re-insert snapshot must not duplicate or drop the link

            Assert.Equal(1L, ReadScalar(path, $"SELECT COUNT(*) FROM godowns WHERE id = '{site.Id:D}';"));

            var loaded = store.Load(c.Id)!;
            Assert.True(loaded.EnableJobCosting);
            Assert.Equal(centre.Id, loaded.FindGodown(site.Id)!.JobCostCentreId);
            Assert.True(loaded.FindGodown(site.Id)!.IsJobProject);
            Assert.Null(loaded.FindGodown(plain.Id)!.JobCostCentreId);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>Census 9.9 — a named voucher class on the Stock Journal type round-trips with its flag, and a
    /// second Save keeps exactly one row (the class table is cleared before <c>voucher_types</c> on every save,
    /// so a missing clear would break the foreign key here).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Voucher_class_round_trips_and_survives_a_second_save()
    {
        var path = TempDbFile.NewPath("apex-k1-class-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Class Co", FyStart);
            var journal = c.FindVoucherTypeByName("Stock Journal")!;
            new VoucherTypeService(c).AddClass(journal.Id, "Transfer", useClassForInterGodownTransfers: true);

            using var store = new SqliteCompanyStore(path);
            store.Save(c);
            store.Save(c);

            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_type_classes;"));

            var loaded = store.Load(c.Id)!;
            var reloaded = loaded.FindVoucherType(journal.Id)!;
            var cls = Assert.Single(reloaded.Classes);
            Assert.Equal("Transfer", cls.Name);
            Assert.True(cls.UseClassForInterGodownTransfers);
            Assert.Single(reloaded.InterGodownTransferClasses);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= downgrade

    /// <summary>
    /// The downgrade drops every v58 object and keeps every row of the tables it rebuilds. It is deliberately
    /// NOT information-preserving: the tracking numbers, the job link and the classes are gone, so re-migrating
    /// up must bring the book back UNTRACKED rather than resurrect figures from nowhere.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v58_to_v57_drops_every_v58_object_and_keeps_the_rows()
    {
        var path = TempDbFile.NewPath("apex-k1-downgrade");
        try
        {
            var c = Book("Downgrade Track Co", out var item, out var godown, out var supplier);
            c.UseTrackingNumbers = true;
            c.EnableJobCosting = true;
            var centre = new CostCentre(Guid.NewGuid(), "Bridge Project", c.CostCategories.First().Id);
            c.AddCostCentre(centre);
            new InventoryService(c).SetGodownJobCostCentre(godown.Id, centre.Id);
            new VoucherTypeService(c)
                .AddClass(c.FindVoucherTypeByName("Stock Journal")!.Id, "Transfer", true);

            c.AddInventoryVoucher(new InventoryVoucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Receipt Note")!.Id, new DateOnly(2025, 4, 5),
                new[] { new InventoryAllocation(item.Id, godown.Id, 40m, StockDirection.Inward, trackingNumber: "TRK/1") },
                number: 1));
            new LedgerService(c).Post(PurchaseBill(c, supplier, item, godown, new DateOnly(2025, 4, 9), 40m, 10m, "TRK/1", null));

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            var godownsBefore = ReadScalar(path, "SELECT COUNT(*) FROM godowns;");
            var allocsBefore = ReadScalar(path, "SELECT COUNT(*) FROM inventory_allocations;");
            var linesBefore = ReadScalar(path, "SELECT COUNT(*) FROM voucher_inventory_lines;");
            var companiesBefore = ReadScalar(path, "SELECT COUNT(*) FROM companies;");

            using (var conn = Open(path)) { SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(57L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(godownsBefore, ReadScalar(path, "SELECT COUNT(*) FROM godowns;"));
            Assert.Equal(allocsBefore, ReadScalar(path, "SELECT COUNT(*) FROM inventory_allocations;"));
            Assert.Equal(linesBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_inventory_lines;"));
            Assert.Equal(companiesBefore, ReadScalar(path, "SELECT COUNT(*) FROM companies;"));

            foreach (var col in Schema.V58CompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(path, "companies"));
            foreach (var col in Schema.V58GodownColumns)
                Assert.DoesNotContain(col, ColumnNames(path, "godowns"));
            foreach (var col in Schema.V58TrackingLineColumns)
            {
                Assert.DoesNotContain(col, ColumnNames(path, "inventory_allocations"));
                Assert.DoesNotContain(col, ColumnNames(path, "voucher_inventory_lines"));
            }
            foreach (var t in Schema.V58ClassTables) Assert.False(TableExists(path, t));

            // 🔴 The rebuilt tables must still be USABLE, which is the failure mode a bare column check misses:
            // RebuildPreservingShape exists because a CREATE … AS SELECT rebuild silently loses the PRIMARY KEY,
            // after which the next child insert fails with "foreign key mismatch". Re-migrating up and saving
            // again is the cheapest end-to-end proof that neither godowns nor companies lost its key.
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;
            Assert.False(loaded.UseTrackingNumbers);
            Assert.All(loaded.Godowns, g => Assert.Null(g.JobCostCentreId));
            Assert.All(loaded.InventoryVouchers, v =>
                Assert.All(v.Allocations, a => Assert.Null(a.TrackingNumber)));
            reopened.Save(loaded);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ---- helpers (the shape every schema test file here open-codes) ----

    private static Company Book(string name, out StockItem item, out Godown godown, out Domain.Ledger supplier)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var inventory = new InventoryService(c);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
        item = inventory.CreateStockItem("Widget", group.Id, unit.Id);
        godown = c.Godowns.First();
        supplier = new Domain.Ledger(Guid.NewGuid(), "Supplier Ltd",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(supplier);
        return c;
    }

    /// <summary>A minimal Purchase item-invoice: Dr Purchase Accounts, Cr the supplier, with one item line
    /// carrying the tracking data. Deliberately hand-built rather than posted through the service, so this
    /// schema test stays about PERSISTENCE and does not fail for an unrelated posting-rule change.</summary>
    private static Voucher PurchaseBill(
        Company c, Domain.Ledger supplier, StockItem item, Godown godown,
        DateOnly date, decimal qty, decimal rate, string? tracking, string? costTracking)
    {
        // "Purchase Accounts" is a seeded GROUP, not a ledger, so the ledger is created on first use.
        var purchases = c.FindLedgerByName("Purchases");
        if (purchases is null)
        {
            purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases",
                c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
            c.AddLedger(purchases);
        }
        var value = Money.FromRupees(qty * rate);
        return new Voucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Purchase")!.Id, date,
            new[]
            {
                new EntryLine(purchases.Id, value, DrCr.Debit),
                new EntryLine(supplier.Id, value, DrCr.Credit),
            },
            number: 1,
            partyId: supplier.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, godown.Id, qty, Money.FromRupees(rate),
                    StockDirection.Inward, trackingNumber: tracking, costTrackingNumber: costTracking),
            });
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        return conn;
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return Convert.ToInt64(v);
    }

    private static bool TableExists(string path, string table) =>
        ReadScalar(path, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';") == 1L;

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>(type, notnull, default, pk) for one column — the same contract tuple the other schema tests
    /// compare, so a migration that adds a column with a different declaration fails here.</summary>
    private static string ColumnContract(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var contract = "(absent)";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    contract = $"{r.GetString(2)}|{r.GetInt32(3)}|{(r.IsDBNull(4) ? "-" : r.GetString(4))}|{r.GetInt32(5)}";
        SqliteConnection.ClearPool(conn);
        return contract;
    }
}
