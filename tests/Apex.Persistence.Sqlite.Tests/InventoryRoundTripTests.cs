using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Inventory-masters persistence contract (phase3-inventory-requirements ER-1/ER-2/ER-3; catalog §9): a
/// company with stock groups (nested + add-quantities flag), stock categories, simple + compound units,
/// godowns (Main Location + third-party), stock items (group/category/unit + HSN/taxability + valuation
/// method + reorder) and opening balances (by godown + batch label, fractional quantities) SAVES and
/// RELOADS with schema_version = 9 — preserving every field exactly, to the paisa. The migration is proven
/// idempotent-friendly (running it twice is safe via a fresh open) and an existing v8 database upgrades to
/// v9 cleanly with its data intact.
/// </summary>
public sealed class InventoryRoundTripTests
{
    // A company with the full spread of inventory masters. Returns the item id whose opening we assert.
    private static (Company Company, Guid ItemId, Guid CompoundUnitId) SeedWithInventory()
    {
        var c = CompanyFactory.CreateSeeded("Inventory Persist Co", new DateOnly(2024, 4, 1));
        var svc = new InventoryService(c);

        var electronics = svc.CreateStockGroup("Electronics", addQuantities: true);
        var phones = svc.CreateStockGroup("Phones", electronics.Id, alias: "Ph", addQuantities: false);
        var premium = svc.CreateStockCategory("Premium");

        var nos = svc.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0, unitQuantityCode: "NOS");
        var g = svc.CreateSimpleUnit("g", "Grams", decimalPlaces: 3);
        // A compound unit "Kg" = 1000 (first = g) with a distinct tail unit; factor is exact integer.
        var kg = svc.CreateCompoundUnit("Kg", "Kilograms", g.Id, nos.Id, 1000);

        // Warehouse godowns + a third-party (job-work) location.
        var wh = svc.CreateGodown("Warehouse 2", alias: "WH2");
        var jobWork = svc.CreateGodown("Job Worker A", c.MainLocation!.Id, thirdParty: true);

        var phone = svc.CreateStockItem(
            "Smartphone", phones.Id, nos.Id, premium.Id, alias: "SP",
            valuationMethod: StockValuationMethod.Fifo,
            hsnSacCode: "8517", isTaxable: true,
            reorderLevel: 5m, minimumOrderQuantity: 20m);

        // Two opening allocations: one at Main Location (batch + dates), one at Warehouse 2 (no batch).
        svc.AddOpeningBalance(phone.Id, c.MainLocation.Id, 10m, Money.FromRupees(15000m),
            batchLabel: "LOT-2024-A", manufacturingDate: new DateOnly(2024, 1, 1), expiryDate: new DateOnly(2027, 1, 1));
        svc.AddOpeningBalance(phone.Id, wh.Id, 4m, Money.FromRupees(15000m));

        Assert.NotNull(jobWork);
        return (c, phone.Id, kg.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Inventory_masters_survive_save_reload_with_schema_version_9()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, _) = SeedWithInventory();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal(9, Schema.CurrentVersion);
                // Re-save (delete-then-insert upsert) must not trip an inventory foreign key.
                write.Save(original);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Stock groups: nesting + add-quantities flag + alias preserved.
            var electronics = reloaded.FindStockGroupByName("Electronics")!;
            var phones = reloaded.FindStockGroupByName("Phones")!;
            Assert.True(electronics.AddQuantities);
            Assert.Equal(electronics.Id, phones.ParentId);
            Assert.False(phones.AddQuantities);
            Assert.Equal("Ph", phones.Alias);

            // Category preserved.
            Assert.NotNull(reloaded.FindStockCategoryByName("Premium"));

            // Units: simple (with UQC + decimals) + compound (first/tail/factor) preserved.
            var nos = reloaded.FindUnitByName("Nos")!;
            Assert.False(nos.IsCompound);
            Assert.Equal(0, nos.DecimalPlaces);
            Assert.Equal("NOS", nos.UnitQuantityCode);
            var grams = reloaded.FindUnitByName("g")!;
            Assert.Equal(3, grams.DecimalPlaces);
            var kg = reloaded.FindUnitByName("Kg")!;
            Assert.True(kg.IsCompound);
            Assert.Equal(grams.Id, kg.FirstUnitId);
            Assert.Equal(nos.Id, kg.TailUnitId);
            Assert.Equal(1000, kg.ConversionNumerator);
            Assert.Equal(1, kg.ConversionDenominator);

            // Godowns: Main Location (seeded), Warehouse 2 (alias), Job Worker A (third-party + nested).
            var main = reloaded.MainLocation!;
            Assert.True(main.IsMainLocation);
            var wh = reloaded.FindGodownByName("Warehouse 2")!;
            Assert.Equal("WH2", wh.Alias);
            Assert.False(wh.ThirdParty);
            var jobWork = reloaded.FindGodownByName("Job Worker A")!;
            Assert.True(jobWork.ThirdParty);
            Assert.Equal(main.Id, jobWork.ParentId);

            // Stock item: every field preserved, valuation method survived.
            var phone = reloaded.FindStockItem(itemId)!;
            Assert.Equal("Smartphone", phone.Name);
            Assert.Equal("SP", phone.Alias);
            Assert.Equal(phones.Id, phone.StockGroupId);
            Assert.Equal(reloaded.FindStockCategoryByName("Premium")!.Id, phone.CategoryId);
            Assert.Equal(nos.Id, phone.BaseUnitId);
            Assert.Equal(StockValuationMethod.Fifo, phone.ValuationMethod);
            Assert.Equal("8517", phone.HsnSacCode);
            Assert.True(phone.IsTaxable);
            Assert.Equal(5m, phone.ReorderLevel);
            Assert.Equal(20m, phone.MinimumOrderQuantity);

            // Opening balances: both allocations, batch + dates + paisa-exact value survived.
            var openings = reloaded.OpeningBalancesFor(itemId).ToList();
            Assert.Equal(2, openings.Count);
            var batched = openings.Single(o => o.BatchLabel == "LOT-2024-A");
            Assert.Equal(main.Id, batched.GodownId);
            Assert.Equal(10m, batched.Quantity);
            Assert.Equal(Money.FromRupees(15000m), batched.Rate);
            Assert.Equal(Money.FromRupees(150000m), batched.Value); // 10 × 15000
            Assert.Equal(new DateOnly(2024, 1, 1), batched.ManufacturingDate);
            Assert.Equal(new DateOnly(2027, 1, 1), batched.ExpiryDate);

            var plain = openings.Single(o => o.BatchLabel is null);
            Assert.Equal(wh.Id, plain.GodownId);
            Assert.Equal(4m, plain.Quantity);
            Assert.Null(plain.ManufacturingDate);

            // Total opening value reconciles to the paisa: (10 + 4) × 15000 = 210000.
            Assert.Equal(Money.FromRupees(210000m), new InventoryService(reloaded).OpeningValueOf(itemId));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fractional_quantities_round_trip_exactly()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-frac-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Frac Co", new DateOnly(2024, 4, 1));
            var svc = new InventoryService(c);
            var grp = svc.CreateStockGroup("Bulk");
            var kg = svc.CreateSimpleUnit("Kg", "Kilograms", decimalPlaces: 4);
            var item = svc.CreateStockItem("Sugar", grp.Id, kg.Id, reorderLevel: 12.5m, minimumOrderQuantity: 100.25m);
            svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, 123.4567m, Money.FromRupees(45m));

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            var ri = reloaded.FindStockItemByName("Sugar")!;
            Assert.Equal(12.5m, ri.ReorderLevel);
            Assert.Equal(100.25m, ri.MinimumOrderQuantity);
            var ob = reloaded.OpeningBalancesFor(ri.Id).Single();
            Assert.Equal(123.4567m, ob.Quantity);
            // Value = 123.4567 × 45 = 5555.5515 → paisa-snapped to 5555.55.
            Assert.Equal(Money.FromRupees(5555.55m), ob.Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_to_v9_is_idempotent_across_reopens()
    {
        // Opening the same store repeatedly must not re-run the migration or corrupt data (idempotent).
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-idem-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, _) = SeedWithInventory();
            using (var s1 = new SqliteCompanyStore(dbPath)) s1.Save(original);

            // Reopen several times; each open runs EnsureSchema which must be a no-op at v9.
            for (var i = 0; i < 3; i++)
            {
                using var s = new SqliteCompanyStore(dbPath);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                var reloaded = s.Load(original.Id)!;
                Assert.Equal(2, reloaded.OpeningBalancesFor(itemId).Count());
                Assert.NotNull(reloaded.FindUnitByName("Kg"));
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v8_database_opens_and_auto_migrates_to_v9()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v8legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            using (var conn = OpenCreate(dbPath))
            {
                Exec(conn, LegacyV8Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (8);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V8 Co', 'Legacy V8 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(8L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath)) // opens v8 → migrates to v9
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V8 Co", loaded!.Name);
                // A migrated v8 DB has no godown rows until re-saved (masters start empty).
                Assert.Empty(loaded.Godowns);
                Assert.Empty(loaded.StockItems);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "stock_groups"), "stock_groups table was not created.");
            Assert.True(TableExists(dbPath, "stock_categories"), "stock_categories table was not created.");
            Assert.True(TableExists(dbPath, "units"), "units table was not created.");
            Assert.True(TableExists(dbPath, "godowns"), "godowns table was not created.");
            Assert.True(TableExists(dbPath, "stock_items"), "stock_items table was not created.");
            Assert.True(TableExists(dbPath, "stock_opening_balances"), "stock_opening_balances table was not created.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v8_database_then_accepts_and_round_trips_inventory_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v8then9-{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = OpenCreate(dbPath))
            {
                Exec(conn, LegacyV8Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (8);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, itemId, _) = SeedWithInventory();

            using (var store = new SqliteCompanyStore(dbPath)) // opens v8 → migrates to v9
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                Assert.Equal(StockValuationMethod.Fifo, reloaded.FindStockItem(itemId)!.ValuationMethod);
                Assert.Equal(2, reloaded.OpeningBalancesFor(itemId).Count());
                Assert.True(reloaded.FindUnitByName("Kg")!.IsCompound);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // ---- Regression: DEFECT 1 (self-FK insert order must be parent-before-child) ----

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Stock_group_parented_under_a_later_created_sibling_saves_and_reloads()
    {
        // P is created first, then C; then P is re-parented UNDER C. In Company list order P precedes C, so a
        // naive insert writes P (whose parent C does not yet exist) → self-FK violation. A topological insert
        // (parent before child) must make this Save succeed and reload with P under C.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-grptopo-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Topo Co", new DateOnly(2024, 4, 1));
            var svc = new InventoryService(c);
            var p = svc.CreateStockGroup("P");
            var child = svc.CreateStockGroup("C");
            svc.SetStockGroupParent(p.Id, child.Id); // P now sits under C, but P precedes C in list order

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            var rp = reloaded.FindStockGroupByName("P")!;
            var rc = reloaded.FindStockGroupByName("C")!;
            Assert.Equal(rc.Id, rp.ParentId);
            Assert.Null(rc.ParentId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Stock_category_parented_under_a_later_created_sibling_saves_and_reloads()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-cattopo-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Topo Cat Co", new DateOnly(2024, 4, 1));
            var svc = new InventoryService(c);
            var p = svc.CreateStockCategory("P");
            var child = svc.CreateStockCategory("C");
            svc.SetStockCategoryParent(p.Id, child.Id);

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            var rp = reloaded.FindStockCategoryByName("P")!;
            var rc = reloaded.FindStockCategoryByName("C")!;
            Assert.Equal(rc.Id, rp.ParentId);
            Assert.Null(rc.ParentId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Godown_parented_under_a_later_created_sibling_saves_and_reloads()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-godtopo-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Topo Godown Co", new DateOnly(2024, 4, 1));
            var svc = new InventoryService(c);
            var p = svc.CreateGodown("P");
            var child = svc.CreateGodown("C");
            svc.SetGodownParent(p.Id, child.Id);

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            var rp = reloaded.FindGodownByName("P")!;
            var rc = reloaded.FindGodownByName("C")!;
            Assert.Equal(rc.Id, rp.ParentId);
            Assert.Null(rc.ParentId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_hand_constructed_godown_cycle_throws_a_clean_domain_exception_on_save()
    {
        // A caller sets a two-node cycle directly on the public ParentId setters (bypassing the service
        // guards). Save must surface a clean domain InvalidOperationException — never a raw SqliteException,
        // and never an infinite loop.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-inv-godcycle-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Cycle Co", new DateOnly(2024, 4, 1));
            var svc = new InventoryService(c);
            var a = svc.CreateGodown("A");
            var b = svc.CreateGodown("B");
            a.ParentId = b.Id;
            b.ParentId = a.Id; // A → B → A, a cycle

            using var write = new SqliteCompanyStore(dbPath);
            var ex = Assert.Throws<InvalidOperationException>(() => write.Save(c));
            Assert.Contains("cycle", ex.Message);
            Assert.IsNotType<Microsoft.Data.Sqlite.SqliteException>(ex);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // ---- helpers ----

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

    private static SqliteConnection OpenCreate(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
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

    /// <summary>The pre-inventory v8 DDL (the tables the migration/load touches), kept in the test so it does
    /// not drift when the production <see cref="Schema"/> advances past v8. It includes the v8 multi-currency
    /// tables/columns but NOT the v9 inventory tables.</summary>
    private const string LegacyV8Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (
            id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, mailing_name TEXT NOT NULL,
            address TEXT NULL, country TEXT NOT NULL, state TEXT NULL, pin TEXT NULL,
            financial_year_start TEXT NOT NULL, books_begin_from TEXT NOT NULL,
            base_currency_symbol TEXT NOT NULL, base_currency_name TEXT NOT NULL,
            decimal_places INTEGER NOT NULL, decimal_unit_name TEXT NOT NULL,
            primary_cost_category TEXT NOT NULL, main_location TEXT NOT NULL,
            profit_and_loss_head_id TEXT NULL REFERENCES groups(id));
        CREATE TABLE groups (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, nature INTEGER NOT NULL, parent_id TEXT NULL REFERENCES groups(id),
            alias TEXT NULL, is_predefined INTEGER NOT NULL, is_pl_head INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE ledgers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id),
            opening_balance_paisa INTEGER NOT NULL, opening_is_debit INTEGER NOT NULL,
            alias TEXT NULL, is_predefined INTEGER NOT NULL,
            maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0, default_credit_period INTEGER NULL,
            cost_applicable INTEGER NULL,
            enable_cheque_printing INTEGER NOT NULL DEFAULT 0, cheque_bank_name TEXT NULL,
            interest_enabled INTEGER NULL, interest_rate_millis INTEGER NULL, interest_per INTEGER NULL,
            interest_on_balance INTEGER NULL, interest_applicability INTEGER NULL,
            interest_calc_from TEXT NULL, interest_style INTEGER NULL,
            interest_round_method INTEGER NULL, interest_round_decimals INTEGER NULL,
            currency_id TEXT NULL REFERENCES currencies(id));
        CREATE TABLE currencies (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            symbol TEXT NOT NULL, formal_name TEXT NOT NULL, decimal_places INTEGER NOT NULL,
            is_base INTEGER NOT NULL);
        CREATE TABLE exchange_rates (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            currency_id TEXT NOT NULL REFERENCES currencies(id), rate_date TEXT NOT NULL,
            standard_rate_micro INTEGER NOT NULL, selling_rate_micro INTEGER NULL, buying_rate_micro INTEGER NULL);
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, default_shortcut TEXT NULL,
            numbering INTEGER NOT NULL, abbreviation TEXT NULL, is_active INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE vouchers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL,
            date TEXT NOT NULL, narration TEXT NULL, party_id TEXT NULL REFERENCES ledgers(id),
            cancelled INTEGER NOT NULL, optional INTEGER NOT NULL, post_dated INTEGER NOT NULL,
            applicable_upto TEXT NULL);
        CREATE TABLE entry_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL REFERENCES vouchers(id),
            line_order INTEGER NOT NULL, ledger_id TEXT NOT NULL REFERENCES ledgers(id),
            amount_paisa INTEGER NOT NULL, side INTEGER NOT NULL,
            forex_currency_id TEXT NULL REFERENCES currencies(id),
            forex_amount_micro INTEGER NULL, forex_rate_micro INTEGER NULL);
        CREATE TABLE bill_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, ref_type INTEGER NOT NULL, name TEXT NOT NULL,
            amount_paisa INTEGER NOT NULL, due_date TEXT NULL, credit_days INTEGER NULL);
        CREATE TABLE cost_categories (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, allocate_revenue INTEGER NOT NULL, allocate_non_revenue INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE cost_centres (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            parent_id TEXT NULL REFERENCES cost_centres(id), alias TEXT NULL);
        CREATE TABLE cost_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            centre_id TEXT NOT NULL REFERENCES cost_centres(id), amount_paisa INTEGER NOT NULL);
        CREATE TABLE budgets (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, under_id TEXT NULL REFERENCES groups(id),
            period_from TEXT NOT NULL, period_to TEXT NOT NULL);
        CREATE TABLE budget_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, budget_id TEXT NOT NULL REFERENCES budgets(id),
            line_order INTEGER NOT NULL, group_id TEXT NULL REFERENCES groups(id),
            ledger_id TEXT NULL REFERENCES ledgers(id), budget_type INTEGER NOT NULL, amount_paisa INTEGER NOT NULL);
        CREATE TABLE bank_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            transaction_type INTEGER NOT NULL, instrument_number TEXT NOT NULL,
            instrument_date TEXT NULL, bank_date TEXT NULL);
        CREATE TABLE scenarios (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, include_actuals INTEGER NOT NULL);
        CREATE TABLE scenario_voucher_types (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, scenario_id TEXT NOT NULL REFERENCES scenarios(id),
            voucher_type_id TEXT NOT NULL REFERENCES voucher_types(id), is_included INTEGER NOT NULL);
        """;
}
