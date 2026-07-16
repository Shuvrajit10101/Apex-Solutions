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
                // Item-invoice stock lines were added at v12; the current version has since advanced. A fresh
                // DB is stamped straight to the current version and the round-trip is unaffected.
                write.Save(original);
                write.Save(original); // re-save (upsert) must not trip an FK
                // Stamped to the current schema version (the single source of truth, so a future version bump
                // never re-breaks this test).
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
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
            TempDbFile.Delete(dbPath);
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
            TempDbFile.Delete(dbPath);
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
            TempDbFile.Delete(dbPath);
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
            TempDbFile.Delete(dbPath);
        }
    }

    // ---- helpers ----

    /// <summary>Downgrades a freshly-saved database to a clean v11 shape: drop the v12 table + every later
    /// (v13 GST, v14 saved-views) artifact and set the version marker back to 11, modelling a store that
    /// predates this slice. The re-open then migrates v11→v12→v13→v14 cleanly (its bare CREATE TABLE / ADD
    /// COLUMN steps must not collide with a table/column a fresh save at the current version already created).</summary>
    private static void DowngradeToV11(string dbPath)
    {
        using var conn = Open(dbPath);
        Exec(conn, "PRAGMA foreign_keys = OFF;");
        // Drop the v26 TDS withholding-detail table (+ index) so the reopen's v25→v26 CREATE TABLE does not collide.
        Exec(conn, "DROP INDEX IF EXISTS ix_tds_lines_entry_line;");
        // Drop the v28 TCS collection-detail table (+ index) so the reopen's v27→v28 CREATE TABLE does not collide.
        Exec(conn, "DROP INDEX IF EXISTS ix_tcs_lines_entry_line;");
        Exec(conn, "DROP TABLE IF EXISTS tcs_lines;");
        // Drop the v29 TCS challan tables (+ index) so the reopen's v28→v29 CREATE TABLE does not collide.
        Exec(conn, "DROP INDEX IF EXISTS ix_tcs_challan_voucher_links_challan;");
        Exec(conn, "DROP TABLE IF EXISTS tcs_challan_voucher_links;");
        Exec(conn, "DROP INDEX IF EXISTS ix_tcs_challans_company;");
        Exec(conn, "DROP TABLE IF EXISTS tcs_challans;");
        Exec(conn, "DROP TABLE IF EXISTS challan_voucher_links;");
        Exec(conn, "DROP TABLE IF EXISTS tds_challans;");
        Exec(conn, "DROP TABLE IF EXISTS tds_lines;");
        Exec(conn, "DROP TABLE IF EXISTS voucher_inventory_lines;");
        // Drop the v19 additional-cost table (+ its index) so the reopen's v18→v19 CREATE TABLE does not collide
        // with a table a fresh save at the current version already created.
        Exec(conn, "DROP INDEX IF EXISTS ix_additional_cost_lines_voucher;");
        Exec(conn, "DROP TABLE IF EXISTS additional_cost_lines;");
        // Drop the v14 saved-views table + its unique index so the reopen's v13→v14 CREATE TABLE does not
        // collide with an already-present table (this is a faithful v11 shape that predates every later slice).
        Exec(conn, "DROP INDEX IF EXISTS ux_saved_views_company_name;");
        Exec(conn, "DROP TABLE IF EXISTS saved_views;");
        // Drop the v15 smtp_profile table so the reopen's v14→v15 CREATE TABLE does not collide.
        Exec(conn, "DROP TABLE IF EXISTS smtp_profile;");
        // Drop the v21 price-level tables + their indexes so the reopen's v20→v21 CREATE TABLE does not collide
        // (the companies/ledgers rebuild below strips the v21 ALTER columns back to the v11 shape).
        Exec(conn, "DROP INDEX IF EXISTS ix_price_list_lines_list;");
        Exec(conn, "DROP INDEX IF EXISTS ix_price_lists_level_item;");
        Exec(conn, "DROP INDEX IF EXISTS ix_price_lists_company;");
        Exec(conn, "DROP INDEX IF EXISTS ux_price_levels_name;");
        Exec(conn, "DROP INDEX IF EXISTS ix_price_levels_company;");
        Exec(conn, "DROP TABLE IF EXISTS price_list_lines;");
        Exec(conn, "DROP TABLE IF EXISTS price_lists;");
        Exec(conn, "DROP TABLE IF EXISTS price_levels;");
        // Drop the v22 reorder-definitions table + its indexes so the reopen's v21→v22 CREATE TABLE does not
        // collide with a table a fresh save at the current version already created.
        Exec(conn, "DROP INDEX IF EXISTS ux_reorder_definitions_scope;");
        Exec(conn, "DROP INDEX IF EXISTS ix_reorder_definitions_company;");
        Exec(conn, "DROP TABLE IF EXISTS reorder_definitions;");
        // Drop the v23 POS tables + their index so the reopen's v22→v23 CREATE TABLE does not collide with a table
        // a fresh save at the current version already created (the voucher_types rebuild below strips use_for_pos).
        Exec(conn, "DROP INDEX IF EXISTS ix_pos_tender_allocations_voucher;");
        Exec(conn, "DROP TABLE IF EXISTS pos_tender_allocations;");
        Exec(conn, "DROP TABLE IF EXISTS pos_tender_ledger_defaults;");
        Exec(conn, "DROP TABLE IF EXISTS pos_voucher_type_config;");
        // Drop the v24 Job Work tables + their indexes so the reopen's v23→v24 CREATE TABLE does not collide with a
        // table a fresh save at the current version already created (the companies/voucher_types rebuilds strip the
        // v24 ALTER columns).
        Exec(conn, "DROP INDEX IF EXISTS ix_material_order_links_order;");
        Exec(conn, "DROP INDEX IF EXISTS ix_material_order_links_voucher;");
        Exec(conn, "DROP INDEX IF EXISTS ix_job_work_order_lines_order;");
        Exec(conn, "DROP INDEX IF EXISTS ix_job_work_orders_item;");
        Exec(conn, "DROP INDEX IF EXISTS ux_job_work_orders_voucher;");
        Exec(conn, "DROP INDEX IF EXISTS ix_job_work_orders_company;");
        Exec(conn, "DROP TABLE IF EXISTS material_order_links;");
        Exec(conn, "DROP TABLE IF EXISTS job_work_order_lines;");
        Exec(conn, "DROP TABLE IF EXISTS job_work_orders;");
        // v25 (TDS/TCS) master tables — drop so re-migrating v11→v25 does not hit "table already exists".
        Exec(conn, "DROP TABLE IF EXISTS nature_of_payment;");
        Exec(conn, "DROP TABLE IF EXISTS nature_of_goods;");
        // v35 PT slab-band table (+ its index) — drop so the reopen's v34→v35 CREATE TABLE does not collide (the
        // companies rebuild below strips the v35 pt_* columns back to the v12 shape).
        Exec(conn, "DROP INDEX IF EXISTS ix_pt_slab_bands_company;");
        Exec(conn, "DROP TABLE IF EXISTS pt_slab_bands;");
        // v36 §192 tax-declaration table (+ its index) — drop so the reopen's v35→v36 CREATE TABLE does not collide
        // (the companies rebuild below strips the v36 salary_tds_enabled column back to the v12 shape).
        Exec(conn, "DROP INDEX IF EXISTS ix_employee_tax_declarations_company;");
        Exec(conn, "DROP TABLE IF EXISTS employee_tax_declarations;");
        // v38 GST 2.0 rate-history + Compensation-Cess tables (+ their indexes) — drop so the reopen's v37→v38 CREATE
        // TABLE does not collide (the stock_items/ledgers rebuild in DowngradeStripV13 strips the v38 columns too).
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_rate_history_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_rate_history;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_cess_rates_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_cess_rates;");
        // v39 RCM category + document + §34-CDN + advance tables (+ their indexes) — drop so the reopen's v38→v39 CREATE
        // TABLE does not collide (the stock_items/ledgers/entry_lines/voucher_types rebuild below strips the v39 columns).
        Exec(conn, "DROP INDEX IF EXISTS ix_rcm_categories_company;");
        Exec(conn, "DROP TABLE IF EXISTS rcm_categories;");
        Exec(conn, "DROP INDEX IF EXISTS ix_rcm_documents_company;");
        Exec(conn, "DROP TABLE IF EXISTS rcm_documents;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_cdn_links_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_cdn_links;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_advance_receipts_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_advance_receipts;");
        // v41 e-invoice IRP-artefact table (+ its index) — drop so the reopen's v40→v41 CREATE TABLE does not collide
        // (the companies rebuild in DowngradeStripV13 strips the v41 e-invoice/B2C/mode/nic_*_enc columns too).
        Exec(conn, "DROP INDEX IF EXISTS ix_einvoice_records_company;");
        Exec(conn, "DROP TABLE IF EXISTS einvoice_records;");
        // v42 e-Way Bill tables (+ indexes) — drop so the reopen's v41→v42 CREATE TABLE does not collide (the companies
        // rebuild strips the v42 eway_* config columns too).
        Exec(conn, "DROP INDEX IF EXISTS ix_eway_bills_company;");
        Exec(conn, "DROP TABLE IF EXISTS eway_bills;");
        Exec(conn, "DROP INDEX IF EXISTS ix_eway_state_thresholds_company;");
        Exec(conn, "DROP TABLE IF EXISTS eway_state_thresholds;");
        // v44 GST set-off / reversal / challan / DRC-03 tables (+ indexes) — drop (child-first: itc_reversals FKs
        // gst_drc03 + gstr2b_lines) so the reopen's v43→v44 CREATE TABLE does not collide (the entry_lines/voucher_types
        // rebuilds strip the v44 gst_adjustment_kind / is_gst_stat_adjustment columns too).
        Exec(conn, "DROP INDEX IF EXISTS ix_itc_reversals_company;");
        Exec(conn, "DROP INDEX IF EXISTS ux_itc_reversals_key;");
        Exec(conn, "DROP TABLE IF EXISTS itc_reversals;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_drc03_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_drc03;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_challans_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_challans;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_setoff_lines_voucher;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gst_setoff_lines_company;");
        Exec(conn, "DROP TABLE IF EXISTS gst_setoff_lines;");
        // v43 GSTR-2B inbound tables (+ indexes) — drop (child-first) so the reopen's v42→v43 CREATE TABLE does not
        // collide (the stock_items/ledgers rebuild in DowngradeStripV13 strips the v43 §17(5) columns too).
        Exec(conn, "DROP INDEX IF EXISTS ix_gstr2b_recon_line;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gstr2b_recon_company;");
        Exec(conn, "DROP TABLE IF EXISTS gstr2b_recon;");
        Exec(conn, "DROP INDEX IF EXISTS ix_ims_status_line;");
        Exec(conn, "DROP INDEX IF EXISTS ix_ims_status_company;");
        Exec(conn, "DROP TABLE IF EXISTS ims_status;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gstr2b_lines_snapshot;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gstr2b_lines_company;");
        Exec(conn, "DROP TABLE IF EXISTS gstr2b_lines;");
        Exec(conn, "DROP INDEX IF EXISTS ix_gstr2b_snapshots_company;");
        Exec(conn, "DROP TABLE IF EXISTS gstr2b_snapshots;");
        // v31 Pay-head / salary-structure tables — drop (child-first) so the reopen's v30→v31 CREATE TABLE does not collide.
        Exec(conn, "DROP TABLE IF EXISTS payroll_lines;");
        Exec(conn, "DROP TABLE IF EXISTS attendance_entries;");
        Exec(conn, "DROP TABLE IF EXISTS salary_structure_lines;");
        Exec(conn, "DROP TABLE IF EXISTS salary_structures;");
        Exec(conn, "DROP TABLE IF EXISTS pay_head_computation_slabs;");
        Exec(conn, "DROP TABLE IF EXISTS pay_head_computation;");
        Exec(conn, "DROP TABLE IF EXISTS pay_heads;");
        // v30 Payroll master tables — drop so the reopen's v29→v30 CREATE TABLE does not collide (the companies
        // rebuild below strips the v30 payroll_enabled/payroll_statutory_enabled columns back to the v12 shape).
        Exec(conn, "DROP TABLE IF EXISTS employees;");
        Exec(conn, "DROP TABLE IF EXISTS attendance_types;");
        Exec(conn, "DROP TABLE IF EXISTS payroll_units;");
        Exec(conn, "DROP TABLE IF EXISTS employee_groups;");
        Exec(conn, "DROP TABLE IF EXISTS employee_categories;");
        // Drop the v17 Bill-of-Materials tables + their indexes so the reopen's v16→v17 CREATE TABLE does not
        // collide with a table a fresh save at the current version already created.
        Exec(conn, "DROP INDEX IF EXISTS ix_bom_lines_bom;");
        Exec(conn, "DROP INDEX IF EXISTS ux_bom_item_name;");
        Exec(conn, "DROP INDEX IF EXISTS ix_bom_item;");
        Exec(conn, "DROP INDEX IF EXISTS ix_bom_company;");
        Exec(conn, "DROP TABLE IF EXISTS bom_lines;");
        Exec(conn, "DROP TABLE IF EXISTS bill_of_materials;");
        // Rebuild voucher_types WITHOUT the v18 use_as_manufacturing_journal column (a fresh save at the current
        // version created it) so the reopen's v17→v18 ALTER ADD COLUMN does not collide. A v11 voucher_types still
        // carries the v10 affects_accounts / affects_stock columns (v10 predates v11), so keep those.
        Exec(conn, """
            CREATE TABLE voucher_types_v17 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
                base_type INTEGER NOT NULL, default_shortcut TEXT NULL, numbering INTEGER NOT NULL,
                abbreviation TEXT NULL, is_active INTEGER NOT NULL, is_predefined INTEGER NOT NULL,
                affects_accounts INTEGER NOT NULL DEFAULT 0, affects_stock INTEGER NOT NULL DEFAULT 0);
            INSERT INTO voucher_types_v17 SELECT id, company_id, name, base_type, default_shortcut, numbering,
                abbreviation, is_active, is_predefined, affects_accounts, affects_stock FROM voucher_types;
            DROP TABLE voucher_types;
            ALTER TABLE voucher_types_v17 RENAME TO voucher_types;
            """);
        DowngradeStripV16(conn);
        DowngradeStripV13(conn);
        Exec(conn, "UPDATE schema_version SET version = 11;");
        SqliteConnection.ClearPool(conn);
    }

    /// <summary>Strips the v16 batch artifacts so a downgraded DB is a faithful pre-batch shape and the reopen's
    /// MigrateV15ToV16 (CREATE batch_masters + ADD COLUMN batch_id) does not collide. Drops the batch_masters
    /// table + its indexes and rebuilds the three stock-line tables that gained a nullable batch_id via ALTER —
    /// stock_opening_balances, inventory_allocations, physical_stock_lines — to their pre-v16 shape. (v11's
    /// voucher_inventory_lines is dropped separately and recreated by v11→v12 without batch_id.) SQLite pre-3.35
    /// has no DROP COLUMN, so the table-rewrite idiom is used.</summary>
    internal static void DowngradeStripV16(SqliteConnection conn)
    {
        Exec(conn, "DROP INDEX IF EXISTS ux_batch_masters_item_no;");
        Exec(conn, "DROP INDEX IF EXISTS ix_batch_masters_item;");
        Exec(conn, "DROP INDEX IF EXISTS ix_batch_masters_company;");
        Exec(conn, "DROP TABLE IF EXISTS batch_masters;");

        // stock_opening_balances: rebuild without the v16 batch_id column.
        Exec(conn, """
            CREATE TABLE stock_opening_balances_v15 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, stock_item_id TEXT NOT NULL,
                godown_id TEXT NOT NULL, batch_label TEXT NULL, quantity_micro INTEGER NOT NULL,
                rate_paisa INTEGER NOT NULL, mfg_date TEXT NULL, expiry_date TEXT NULL);
            INSERT INTO stock_opening_balances_v15 SELECT id, company_id, stock_item_id, godown_id, batch_label,
                quantity_micro, rate_paisa, mfg_date, expiry_date FROM stock_opening_balances;
            DROP TABLE stock_opening_balances;
            ALTER TABLE stock_opening_balances_v15 RENAME TO stock_opening_balances;
            """);

        // inventory_allocations: rebuild without the v16 batch_id column.
        Exec(conn, """
            CREATE TABLE inventory_allocations_v15 (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
                line_order INTEGER NOT NULL, role INTEGER NOT NULL, stock_item_id TEXT NOT NULL,
                godown_id TEXT NOT NULL, unit_id TEXT NULL, quantity_micro INTEGER NOT NULL,
                direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL);
            INSERT INTO inventory_allocations_v15 SELECT id, inventory_voucher_id, line_order, role, stock_item_id,
                godown_id, unit_id, quantity_micro, direction, rate_paisa, batch_label FROM inventory_allocations;
            DROP TABLE inventory_allocations;
            ALTER TABLE inventory_allocations_v15 RENAME TO inventory_allocations;
            """);

        // physical_stock_lines: rebuild without the v16 batch_id column.
        Exec(conn, """
            CREATE TABLE physical_stock_lines_v15 (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
                line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL,
                counted_qty_micro INTEGER NOT NULL, batch_label TEXT NULL);
            INSERT INTO physical_stock_lines_v15 SELECT id, inventory_voucher_id, line_order, stock_item_id,
                godown_id, counted_qty_micro, batch_label FROM physical_stock_lines;
            DROP TABLE physical_stock_lines;
            ALTER TABLE physical_stock_lines_v15 RENAME TO physical_stock_lines;
            """);
    }

    /// <summary>Strips the v13 core-GST artifacts (the gst_rate_slabs table + the GST columns on companies /
    /// ledgers / stock_items / entry_lines) so a downgraded DB is a faithful pre-GST shape and the reopen's
    /// MigrateV12ToV13 (bare CREATE TABLE + ADD COLUMN) does not collide. SQLite pre-3.35 has no DROP COLUMN,
    /// so tables that gained columns via ALTER are rebuilt to their pre-v13 shape.</summary>
    internal static void DowngradeStripV13(SqliteConnection conn)
    {
        Exec(conn, "DROP TABLE IF EXISTS gst_rate_slabs;");

        // companies: rebuild without the 6 GST config columns (keep the FK-nullable pl-head as-is).
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

        // ledgers: rebuild without the 9 GST columns.
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

        // stock_items: rebuild without the 4 GST columns.
        Exec(conn, """
            CREATE TABLE stock_items_v12 (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
                stock_group_id TEXT NOT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL, alias TEXT NULL,
                valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL, is_taxable INTEGER NOT NULL DEFAULT 0,
                reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL, standard_cost_paisa INTEGER NULL);
            INSERT INTO stock_items_v12 SELECT id, company_id, name, stock_group_id, category_id, base_unit_id,
                alias, valuation_method, hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro,
                standard_cost_paisa FROM stock_items;
            DROP TABLE stock_items;
            ALTER TABLE stock_items_v12 RENAME TO stock_items;
            """);

        // entry_lines: rebuild without the 3 GST columns.
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
