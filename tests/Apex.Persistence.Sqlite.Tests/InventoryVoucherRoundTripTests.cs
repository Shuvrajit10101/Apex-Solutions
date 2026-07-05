using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Inventory &amp; order voucher persistence contract (phase3-inventory-requirements ER-1/ER-2/ER-3, RQ-8..RQ-15;
/// catalog §10): a company with a GRN, a Delivery, a Rejection In/Out, a Stock Journal (source + destination),
/// a Physical Stock count and a Purchase/Sales Order — plus the two effect flags on every voucher type —
/// SAVES and RELOADS at schema_version = 10 preserving every field exactly, and the derived on-hand survives
/// the round-trip. The v9→v10 migration is proven idempotent (repeated reopens) and an existing v9 database
/// upgrades to v10 cleanly with its inventory-master data intact.
/// </summary>
public sealed class InventoryVoucherRoundTripTests
{
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 20);

    private static Guid TypeId(Company c, VoucherBaseType bt) => c.VoucherTypes.First(t => t.BaseType == bt).Id;

    // A company with the full spread of stock/order vouchers over one Nos item + two godowns.
    private static (Company Company, Guid ItemId, Guid MainId, Guid Wh2Id) SeedWithVouchers()
    {
        var c = CompanyFactory.CreateSeeded("Inv Voucher Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("Warehouse 2");
        var main = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, main, 10m, Money.FromRupees(100m));

        var posting = new InventoryPostingService(c);
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D1,
            new[] { new InventoryAllocation(item.Id, main, 5m, StockDirection.Inward, Money.FromRupees(120m), "LOT-1") },
            narration: "GRN"));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.DeliveryNote), D1,
            new[] { new InventoryAllocation(item.Id, main, 4m, StockDirection.Outward) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.RejectionIn), D1,
            new[] { new InventoryAllocation(item.Id, main, 2m, StockDirection.Inward) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(c, VoucherBaseType.RejectionOut), D1,
            new[] { new InventoryAllocation(item.Id, main, 1m, StockDirection.Outward) }));
        posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), TypeId(c, VoucherBaseType.StockJournal), D2,
            source: new[] { new InventoryAllocation(item.Id, main, 3m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item.Id, wh2.Id, 3m, StockDirection.Inward) }));
        posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), TypeId(c, VoucherBaseType.PhysicalStock), D2,
            new[] { new PhysicalStockLine(item.Id, main, 7m, null) }));
        posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(c, VoucherBaseType.PurchaseOrder), D1,
            new[] { new OrderLine(item.Id, main, 100m, Money.FromRupees(90m)) }));
        posting.Post(InventoryVoucher.Order(Guid.NewGuid(), TypeId(c, VoucherBaseType.SalesOrder), D1,
            new[] { new OrderLine(item.Id, main, 50m, null) }));

        return (c, item.Id, main, wh2.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Inventory_vouchers_survive_save_reload_with_schema_version_10()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-invv-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, mainId, wh2Id) = SeedWithVouchers();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                // The current version has advanced past v10 (v11 added the per-item standard-cost column; v12
                // added item-invoice stock lines; v13 added core GST); a fresh DB is stamped straight to it and
                // inventory-voucher round-trip is unaffected.
                Assert.Equal(13, Schema.CurrentVersion);
                write.Save(original);
                write.Save(original); // re-save (upsert) must not trip an inventory-voucher FK
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Effect flags round-tripped on every voucher type.
            var grnType = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
            Assert.True(grnType.AffectsStock);
            Assert.False(grnType.AffectsAccounts);
            var poType = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PurchaseOrder);
            Assert.False(poType.AffectsStock);
            Assert.False(poType.AffectsAccounts);
            var saleType = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
            Assert.True(saleType.AffectsAccounts);

            // All 8 inventory vouchers reloaded.
            Assert.Equal(8, reloaded.InventoryVouchers.Count);

            // GRN allocation fields (rate + batch + unit-null + direction) preserved.
            var grn = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.ReceiptNote);
            var gl = Assert.Single(grn.Allocations);
            Assert.Equal(5m, gl.Quantity);
            Assert.Equal(StockDirection.Inward, gl.Direction);
            Assert.Equal(Money.FromRupees(120m), gl.Rate);
            Assert.Equal("LOT-1", gl.BatchLabel);
            Assert.Equal("GRN", grn.Narration);

            // Stock Journal: source (outward) + destination (inward) preserved as distinct roles.
            var sj = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.StockJournal);
            Assert.Equal(StockDirection.Outward, Assert.Single(sj.Allocations).Direction);
            Assert.Equal(StockDirection.Inward, Assert.Single(sj.DestinationAllocations).Direction);
            Assert.Equal(mainId, sj.Allocations[0].GodownId);
            Assert.Equal(wh2Id, sj.DestinationAllocations[0].GodownId);

            // Physical Stock line preserved.
            var ps = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.PhysicalStock);
            Assert.Equal(7m, Assert.Single(ps.PhysicalLines).CountedQuantity);

            // Order lines preserved (rate + null-rate).
            var po = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.PurchaseOrder);
            Assert.Equal(Money.FromRupees(90m), Assert.Single(po.OrderLines).Rate);
            var so = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.SalesOrder);
            Assert.Null(Assert.Single(so.OrderLines).Rate);

            // Derived on-hand survives. The GRN landed in batch "LOT-1"; the unbatched bucket carries opening
            // + the unbatched movements and is reset to 7 by the D2 physical count (DP-3). So the unbatched
            // bucket = 7, the LOT-1 batch bucket = 5, and Main's total = 12. WH2 = 3 (stock-journal dest).
            var ledger = new InventoryLedger(reloaded);
            Assert.Equal(7m, ledger.OnHand(itemId, mainId, (string?)null, D2)); // unbatched bucket, count-reset
            Assert.Equal(5m, ledger.OnHand(itemId, mainId, "LOT-1", D2));       // batch bucket from the GRN
            Assert.Equal(12m, ledger.OnHand(itemId, mainId, D2));              // total across batches
            Assert.Equal(3m, ledger.OnHand(itemId, wh2Id, D2));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_to_v10_is_idempotent_across_reopens()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-invv-idem-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, mainId, _) = SeedWithVouchers();
            using (var s1 = new SqliteCompanyStore(dbPath)) s1.Save(original);

            for (var i = 0; i < 3; i++)
            {
                using var s = new SqliteCompanyStore(dbPath);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                var reloaded = s.Load(original.Id)!;
                Assert.Equal(8, reloaded.InventoryVouchers.Count);
                Assert.Equal(12m, new InventoryLedger(reloaded).OnHand(itemId, mainId, D2));
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v9_database_opens_and_auto_migrates_to_v10_with_data_intact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v9legacy-{Guid.NewGuid():N}.db");
        try
        {
            // Build a real v9 database by saving with a store, then hand-rolling the version marker back to 9
            // and dropping the v10 objects — simulating a store that predates this slice.
            var companyId = Guid.NewGuid();
            var (seed, itemId, _, _) = SeedWithVouchers();
            // Save at current version, then downgrade the on-disk artefact to a clean v9 shape.
            using (var s = new SqliteCompanyStore(dbPath)) s.Save(seed);
            DowngradeToV9(dbPath);

            Assert.Equal(9L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "inventory_vouchers"));

            using (var store = new SqliteCompanyStore(dbPath)) // opens v9 → migrates to v10
            {
                var loaded = store.Load(seed.Id);
                Assert.NotNull(loaded);
                // Master data intact after upgrade.
                Assert.NotNull(loaded!.FindStockItem(itemId));
                Assert.Single(loaded.Godowns, g => g.Name == "Warehouse 2");
                // A migrated v9 DB has no inventory-voucher rows until re-saved (the v10 tables start empty).
                Assert.Empty(loaded.InventoryVouchers);
                // Voucher types exist; their effect flags default to off until re-saved.
                Assert.Equal(24, loaded.VoucherTypes.Count);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "inventory_vouchers"));
            Assert.True(TableExists(dbPath, "inventory_allocations"));
            Assert.True(TableExists(dbPath, "order_lines"));
            Assert.True(TableExists(dbPath, "physical_stock_lines"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v9_database_then_accepts_and_round_trips_inventory_vouchers()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v9then10-{Guid.NewGuid():N}.db");
        try
        {
            var (seed, itemId, mainId, _) = SeedWithVouchers();
            using (var s = new SqliteCompanyStore(dbPath)) s.Save(seed);
            DowngradeToV9(dbPath);
            Assert.Equal(9L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath)) // v9 → v10
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(seed); // now the effect flags + inventory vouchers persist under v10
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(seed.Id)!;
                Assert.Equal(8, reloaded.InventoryVouchers.Count);
                Assert.True(reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote).AffectsStock);
                Assert.Equal(12m, new InventoryLedger(reloaded).OnHand(itemId, mainId, D2));
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // ---- helpers ----

    /// <summary>
    /// Downgrades a freshly-saved database to a clean v9 shape: drop the v10 tables, drop the two
    /// voucher_types effect columns, and set schema_version back to 9. This models a store that predates the
    /// v10 slice, so the production migration path is exercised on the next open.
    /// </summary>
    private static void DowngradeToV9(string dbPath)
    {
        using var conn = Open(dbPath);
        Exec(conn, "PRAGMA foreign_keys = OFF;");
        Exec(conn, "DROP TABLE IF EXISTS inventory_allocations;");
        Exec(conn, "DROP TABLE IF EXISTS order_lines;");
        Exec(conn, "DROP TABLE IF EXISTS physical_stock_lines;");
        Exec(conn, "DROP TABLE IF EXISTS inventory_vouchers;");
        // Drop the v12 item-invoice table too, so the reopen's v11→v12 CREATE TABLE does not collide with an
        // already-present table (this is a faithful v9 shape that predates every later slice).
        Exec(conn, "DROP TABLE IF EXISTS voucher_inventory_lines;");
        // Rebuild voucher_types WITHOUT the two v10 effect columns (SQLite pre-3.35 has no DROP COLUMN; use a
        // table rewrite that is robust across versions).
        Exec(conn, """
            CREATE TABLE voucher_types_v9 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
                base_type INTEGER NOT NULL, default_shortcut TEXT NULL, numbering INTEGER NOT NULL,
                abbreviation TEXT NULL, is_active INTEGER NOT NULL, is_predefined INTEGER NOT NULL);
            """);
        Exec(conn, """
            INSERT INTO voucher_types_v9
                (id, company_id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined)
            SELECT id, company_id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined
            FROM voucher_types;
            """);
        Exec(conn, "DROP TABLE voucher_types;");
        Exec(conn, "ALTER TABLE voucher_types_v9 RENAME TO voucher_types;");
        // Rebuild stock_items WITHOUT the v11 standard_cost_paisa column, so this is a faithful v9 shape and
        // the reopen's v10→v11 ALTER TABLE ADD COLUMN does not collide with an already-present column.
        Exec(conn, """
            CREATE TABLE stock_items_v9 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
                stock_group_id TEXT NOT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL,
                alias TEXT NULL, valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL,
                is_taxable INTEGER NOT NULL DEFAULT 0, reorder_level_micro INTEGER NULL,
                min_order_qty_micro INTEGER NULL);
            """);
        Exec(conn, """
            INSERT INTO stock_items_v9
                (id, company_id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                 hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro)
            SELECT id, company_id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                 hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro
            FROM stock_items;
            """);
        Exec(conn, "DROP TABLE stock_items;");
        Exec(conn, "ALTER TABLE stock_items_v9 RENAME TO stock_items;");

        // Strip the v13 core-GST artifacts (table + columns on companies/ledgers/entry_lines) so this is a
        // faithful pre-GST shape and the reopen's MigrateV12ToV13 does not collide with an existing table/column.
        // stock_items is already rebuilt (above) without GST columns, so only these three tables + the table remain.
        Exec(conn, "DROP TABLE IF EXISTS gst_rate_slabs;");
        Exec(conn, """
            CREATE TABLE companies_v12 (
                id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, mailing_name TEXT NOT NULL,
                address TEXT NULL, country TEXT NOT NULL, state TEXT NULL, pin TEXT NULL,
                financial_year_start TEXT NOT NULL, books_begin_from TEXT NOT NULL,
                base_currency_symbol TEXT NOT NULL, base_currency_name TEXT NOT NULL,
                decimal_places INTEGER NOT NULL, decimal_unit_name TEXT NOT NULL,
                primary_cost_category TEXT NOT NULL, main_location TEXT NOT NULL,
                profit_and_loss_head_id TEXT NULL);
            INSERT INTO companies_v12 SELECT id, name, mailing_name, address, country, state, pin,
                financial_year_start, books_begin_from, base_currency_symbol, base_currency_name,
                decimal_places, decimal_unit_name, primary_cost_category, main_location, profit_and_loss_head_id
                FROM companies;
            DROP TABLE companies;
            ALTER TABLE companies_v12 RENAME TO companies;
            """);
        Exec(conn, """
            CREATE TABLE ledgers_v12 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL, group_id TEXT NOT NULL,
                opening_balance_paisa INTEGER NOT NULL, opening_is_debit INTEGER NOT NULL, alias TEXT NULL,
                is_predefined INTEGER NOT NULL, maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0,
                default_credit_period INTEGER NULL, cost_applicable INTEGER NULL,
                enable_cheque_printing INTEGER NOT NULL DEFAULT 0, cheque_bank_name TEXT NULL,
                interest_enabled INTEGER NULL, interest_rate_millis INTEGER NULL, interest_per INTEGER NULL,
                interest_on_balance INTEGER NULL, interest_applicability INTEGER NULL, interest_calc_from TEXT NULL,
                interest_style INTEGER NULL, interest_round_method INTEGER NULL, interest_round_decimals INTEGER NULL,
                currency_id TEXT NULL);
            INSERT INTO ledgers_v12 SELECT id, company_id, name, group_id, opening_balance_paisa, opening_is_debit,
                alias, is_predefined, maintain_bill_by_bill, default_credit_period, cost_applicable,
                enable_cheque_printing, cheque_bank_name, interest_enabled, interest_rate_millis, interest_per,
                interest_on_balance, interest_applicability, interest_calc_from, interest_style,
                interest_round_method, interest_round_decimals, currency_id FROM ledgers;
            DROP TABLE ledgers;
            ALTER TABLE ledgers_v12 RENAME TO ledgers;
            """);
        Exec(conn, """
            CREATE TABLE entry_lines_v12 (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL,
                ledger_id TEXT NOT NULL, amount_paisa INTEGER NOT NULL, side INTEGER NOT NULL,
                forex_currency_id TEXT NULL, forex_amount_micro INTEGER NULL, forex_rate_micro INTEGER NULL);
            INSERT INTO entry_lines_v12 SELECT id, voucher_id, line_order, ledger_id, amount_paisa, side,
                forex_currency_id, forex_amount_micro, forex_rate_micro FROM entry_lines;
            DROP TABLE entry_lines;
            ALTER TABLE entry_lines_v12 RENAME TO entry_lines;
            """);

        Exec(conn, "UPDATE schema_version SET version = 9;");
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
