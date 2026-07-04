using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Item-invoice-mode persistence contract (catalog §10; phase3-inventory-requirements RQ-16/RQ-17; slice
/// 3.3b): a company whose Purchase/Sales vouchers carry item-invoice stock lines SAVES and RELOADS at
/// schema_version = 12 preserving every field (item, godown, qty, direction, rate, batch) exactly, and the
/// derived on-hand + valuation survive the round-trip. The v11→v12 migration is proven idempotent (repeated
/// reopens) and an existing v11 database upgrades to v12 cleanly with its data intact.
/// </summary>
public sealed class ItemInvoiceRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 20);

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, Guid groupId, Money opening, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, groupId, opening, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // A company with an item-invoice Purchase (inward, rated, batched) and an item-invoice Sales (outward).
    private static (Company Company, Guid ItemId, Guid MainId) SeedWithItemInvoices()
    {
        var c = CompanyFactory.CreateSeeded("Item Invoice Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        var main = c.MainLocation!.Id;

        var purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        var creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        var sales = AddLedger(c, "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        var debtor = AddLedger(c, "Debtor", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);

        var ledgers = new LedgerService(c);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // Purchase 10 @ ₹120 = ₹1200 (batch LOT-1), inward.
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, D1, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(1200m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(1200m), DrCr.Credit),
        }, narration: "Item purchase",
           inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(120m), batchLabel: "LOT-1") }));

        // Sales 4 @ ₹200 = ₹800, outward.
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(800m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(800m), DrCr.Credit),
        }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 4m, Money.FromRupees(200m), batchLabel: "LOT-1") }));

        return (c, item.Id, main);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Item_invoice_lines_survive_save_reload_with_schema_version_12()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-itinv-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, mainId) = SeedWithItemInvoices();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                Assert.Equal(12, Schema.CurrentVersion);
                write.Save(original);
                write.Save(original); // re-save (upsert) must not trip an FK
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Both accounting vouchers reloaded, each with its item-invoice line preserved exactly.
            var purchase = reloaded.Vouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Purchase);
            var pl = Assert.Single(purchase.InventoryLines);
            Assert.Equal(itemId, pl.StockItemId);
            Assert.Equal(mainId, pl.GodownId);
            Assert.Equal(10m, pl.Quantity);
            Assert.Equal(StockDirection.Inward, pl.Direction);
            Assert.Equal(Money.FromRupees(120m), pl.Rate);
            Assert.Equal("LOT-1", pl.BatchLabel);
            Assert.Equal("Item purchase", purchase.Narration);

            var sale = reloaded.Vouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);
            var sl = Assert.Single(sale.InventoryLines);
            Assert.Equal(4m, sl.Quantity);
            Assert.Equal(StockDirection.Outward, sl.Direction);
            Assert.Equal(Money.FromRupees(200m), sl.Rate);

            // Derived on-hand + valuation survive: 10 in − 4 out = 6 @ ₹120 (FIFO) = ₹720.
            Assert.Equal(6m, new InventoryLedger(reloaded).OnHand(itemId, mainId, "LOT-1", D2));
            Assert.Equal(Money.FromRupees(720m), new StockValuationService(reloaded).ClosingValue(itemId, D2).Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_to_v12_is_idempotent_across_reopens()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-itinv-idem-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, mainId) = SeedWithItemInvoices();
            using (var s1 = new SqliteCompanyStore(dbPath)) s1.Save(original);

            for (var i = 0; i < 3; i++)
            {
                using var s = new SqliteCompanyStore(dbPath);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                var reloaded = s.Load(original.Id)!;
                Assert.Equal(6m, new InventoryLedger(reloaded).OnHand(itemId, mainId, "LOT-1", D2));
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v11_database_opens_and_auto_migrates_to_v12_with_data_intact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v11legacy-{Guid.NewGuid():N}.db");
        try
        {
            var (seed, itemId, _) = SeedWithItemInvoices();
            using (var s = new SqliteCompanyStore(dbPath)) s.Save(seed);
            DowngradeToV11(dbPath);

            Assert.Equal(11L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "voucher_inventory_lines"));

            using (var store = new SqliteCompanyStore(dbPath)) // opens v11 → migrates to v12
            {
                var loaded = store.Load(seed.Id);
                Assert.NotNull(loaded);
                // Master + accounting data intact after upgrade.
                Assert.NotNull(loaded!.FindStockItem(itemId));
                Assert.Equal(2, loaded.Vouchers.Count);
                // A migrated v11 DB has no item-invoice-line rows until re-saved (the v12 table starts empty).
                Assert.All(loaded.Vouchers, v => Assert.False(v.HasInventoryLines));
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "voucher_inventory_lines"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v11_database_then_accepts_and_round_trips_item_invoice_lines()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v11then12-{Guid.NewGuid():N}.db");
        try
        {
            var (seed, itemId, mainId) = SeedWithItemInvoices();
            using (var s = new SqliteCompanyStore(dbPath)) s.Save(seed);
            DowngradeToV11(dbPath);
            Assert.Equal(11L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath)) // v11 → v12
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(seed); // now the item-invoice lines persist under v12
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(seed.Id)!;
                Assert.Equal(6m, new InventoryLedger(reloaded).OnHand(itemId, mainId, "LOT-1", D2));
                var purchase = reloaded.Vouchers.Single(v =>
                    reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Purchase);
                Assert.True(purchase.HasInventoryLines);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // ---- helpers ----

    /// <summary>Downgrades a freshly-saved database to a clean v11 shape: drop the v12 table and set the
    /// version marker back to 11, modelling a store that predates this slice.</summary>
    private static void DowngradeToV11(string dbPath)
    {
        using var conn = Open(dbPath);
        Exec(conn, "PRAGMA foreign_keys = OFF;");
        Exec(conn, "DROP TABLE IF EXISTS voucher_inventory_lines;");
        Exec(conn, "UPDATE schema_version SET version = 11;");
        SqliteConnection.ClearPool(conn);
    }

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

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
