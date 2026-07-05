using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Core-GST persistence contract (phase4 ER-1): a GST-enabled company — config (GSTIN, home state, reg type,
/// periodicity), seeded 0/5/18/40 slabs, the six auto-created tax ledgers, party/item/sales-purchase GST
/// details, and a posted GST invoice whose tax lines carry <see cref="GstLineTax"/> — SAVES and RELOADS at
/// schema_version 13, preserving every GST field to the paisa. A legacy v12 database opens and auto-migrates
/// to v13 (GST-off, data intact) and then accepts GST data. A non-GST company round-trips unchanged.
/// </summary>
public sealed class GstRoundTripTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static Company SeedGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("GST Persist Co", new DateOnly(2024, 4, 1));
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = new DateOnly(2024, 4, 1),
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var invSvc = new InventoryService(c);
        var item = invSvc.CreateStockItem("Widget", invSvc.CreateStockGroup("Goods").Id, invSvc.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "123456", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Goods };
        var main = c.MainLocation!.Id;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        sales.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Local Debtor", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "27" };
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(sales); c.AddLedger(debtor); c.AddLedger(purchases); c.AddLedger(creditor);

        var ledgers = new LedgerService(c);
        // Stock first.
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, new DateOnly(2024, 4, 5),
            new[] { new EntryLine(purchases.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(500m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(50m)) }));

        // Intra-state GST sale: 10 @ ₹100 = ₹1000; CGST 90 + SGST 90; party 1180.
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, interState: false, GstTaxDirection.Output);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        saleLines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, new DateOnly(2024, 4, 10), saleLines,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(100m)) }));

        return c;
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Gst_config_masters_and_tax_lines_survive_save_reload_at_v13()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-gst-{Guid.NewGuid():N}.db");
        try
        {
            var original = SeedGstCompany();
            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal(13, Schema.CurrentVersion);
                write.Save(original); // re-save (delete-then-insert) must not trip a GST FK
            }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Company GST config survived.
            Assert.True(reloaded.GstEnabled);
            Assert.Equal(GstinMaharashtra, reloaded.Gst!.Gstin);
            Assert.Equal("27", reloaded.Gst.HomeStateCode);
            Assert.Equal(GstRegistrationType.Regular, reloaded.Gst.RegistrationType);
            Assert.Equal(GstReturnPeriodicity.Quarterly, reloaded.Gst.Periodicity);
            Assert.Equal(new DateOnly(2024, 4, 1), reloaded.Gst.ApplicableFrom);

            // Slabs 0/5/18/40 survived.
            Assert.Equal(new[] { 0, 500, 1800, 4000 }, reloaded.Gst.RateSlabs.Select(s => s.RateBasisPoints).OrderBy(x => x).ToArray());

            // Six tax ledgers survived with classification.
            var gst = new GstService(reloaded);
            foreach (var dir in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
                foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
                    Assert.NotNull(gst.FindTaxLedger(head, dir));

            // Party GST details survived.
            var debtor = reloaded.FindLedgerByName("Local Debtor")!;
            Assert.Equal(GstinGujarat, debtor.PartyGst!.Gstin);
            Assert.Equal("27", debtor.PartyGst.StateCode);
            Assert.Equal(GstRegistrationType.Regular, debtor.PartyGst.RegistrationType);

            // Sales-ledger GST details survived.
            var sales = reloaded.FindLedgerByName("Sales")!;
            Assert.Equal(1800, sales.SalesPurchaseGst!.RateBasisPoints);
            Assert.Equal(GstTaxability.Taxable, sales.SalesPurchaseGst.Taxability);

            // Item GST details survived.
            var item = reloaded.FindStockItemByName("Widget")!;
            Assert.Equal("123456", item.Gst!.HsnSac);
            Assert.Equal(1800, item.Gst.RateBasisPoints);
            Assert.Equal(GstSupplyType.Goods, item.Gst.SupplyType);

            // Tax lines survived on the sale voucher with their GstLineTax detail.
            var taxLines = reloaded.Vouchers.SelectMany(v => v.Lines).Where(l => l.HasGst).ToList();
            Assert.Equal(2, taxLines.Count); // CGST + SGST
            Assert.All(taxLines, l => Assert.Equal(900, l.Gst!.RateBasisPoints)); // 9% each head
            Assert.All(taxLines, l => Assert.Equal(Money.FromRupees(1000m), l.Gst!.TaxableValue));
            Assert.Contains(taxLines, l => l.Gst!.TaxHead == GstTaxHead.Central);
            Assert.Contains(taxLines, l => l.Gst!.TaxHead == GstTaxHead.State);
            Assert.All(taxLines, l => Assert.Equal(Money.FromRupees(90m), l.Amount));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_non_gst_company_round_trips_with_no_gst_state()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-nogst-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", new DateOnly(2024, 4, 1));
            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);
            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath)) reloaded = read.Load(c.Id)!;
            Assert.False(reloaded.GstEnabled);
            Assert.Null(reloaded.Gst);
            Assert.DoesNotContain(reloaded.Ledgers, l => l.GstClassification is not null);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v12_database_opens_and_auto_migrates_to_v13_data_intact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v12legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV12Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (12);");
                Exec(conn, """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V12 Co', 'Legacy V12 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """, ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(12L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V12 Co", loaded!.Name);
                Assert.False(loaded.GstEnabled); // migrated GST-off
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "gst_rate_slabs"));
            Assert.True(ColumnExists(dbPath, "companies", "gst_enabled"));
            Assert.True(ColumnExists(dbPath, "ledgers", "gst_tax_head"));
            Assert.True(ColumnExists(dbPath, "stock_items", "gst_taxability"));
            Assert.True(ColumnExists(dbPath, "entry_lines", "gst_taxable_value_paisa"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v12_database_then_accepts_and_round_trips_gst_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v12then13-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV12Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (12);");
                SqliteConnection.ClearPool(conn);
            }

            var original = SeedGstCompany();
            using (var store = new SqliteCompanyStore(dbPath)) // opens v12 → migrates to v13
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }
            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                Assert.True(reloaded.GstEnabled);
                Assert.Equal(GstinMaharashtra, reloaded.Gst!.Gstin);
                Assert.Equal(4, reloaded.Gst.RateSlabs.Count);
            }
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

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$c;";
        cmd.Parameters.AddWithValue("$c", column);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
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

    /// <summary>
    /// The pre-GST v12 DDL (the tables the v12→v13 migration and load touch), kept in the test so it does not
    /// drift when the production <see cref="Schema"/> advances past v12. It has the v8 currency + v11
    /// standard-cost + v12 voucher_inventory_lines shapes, but NOT any v13 GST columns/tables.
    /// </summary>
    private const string LegacyV12Ddl = """
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
        CREATE TABLE currencies (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            symbol TEXT NOT NULL, formal_name TEXT NOT NULL, decimal_places INTEGER NOT NULL, is_base INTEGER NOT NULL);
        CREATE TABLE exchange_rates (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            currency_id TEXT NOT NULL REFERENCES currencies(id), rate_date TEXT NOT NULL,
            standard_rate_micro INTEGER NOT NULL, selling_rate_micro INTEGER NULL, buying_rate_micro INTEGER NULL);
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
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, default_shortcut TEXT NULL,
            numbering INTEGER NOT NULL, abbreviation TEXT NULL, is_active INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL, affects_accounts INTEGER NOT NULL DEFAULT 0, affects_stock INTEGER NOT NULL DEFAULT 0);
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
            forex_currency_id TEXT NULL REFERENCES currencies(id), forex_amount_micro INTEGER NULL, forex_rate_micro INTEGER NULL);
        CREATE TABLE bill_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, ref_type INTEGER NOT NULL, name TEXT NOT NULL,
            amount_paisa INTEGER NOT NULL, due_date TEXT NULL, credit_days INTEGER NULL);
        CREATE TABLE cost_categories (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, allocate_revenue INTEGER NOT NULL, allocate_non_revenue INTEGER NOT NULL, is_predefined INTEGER NOT NULL);
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
            name TEXT NOT NULL, under_id TEXT NULL REFERENCES groups(id), period_from TEXT NOT NULL, period_to TEXT NOT NULL);
        CREATE TABLE budget_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, budget_id TEXT NOT NULL REFERENCES budgets(id),
            line_order INTEGER NOT NULL, group_id TEXT NULL REFERENCES groups(id),
            ledger_id TEXT NULL REFERENCES ledgers(id), budget_type INTEGER NOT NULL, amount_paisa INTEGER NOT NULL);
        CREATE TABLE bank_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            transaction_type INTEGER NOT NULL, instrument_number TEXT NOT NULL, instrument_date TEXT NULL, bank_date TEXT NULL);
        CREATE TABLE scenarios (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, include_actuals INTEGER NOT NULL);
        CREATE TABLE scenario_voucher_types (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, scenario_id TEXT NOT NULL REFERENCES scenarios(id),
            voucher_type_id TEXT NOT NULL REFERENCES voucher_types(id), is_included INTEGER NOT NULL);
        CREATE TABLE units (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            symbol TEXT NOT NULL, formal_name TEXT NOT NULL, is_compound INTEGER NOT NULL, uqc TEXT NULL,
            decimal_places INTEGER NOT NULL, first_unit_id TEXT NULL, tail_unit_id TEXT NULL,
            conversion_numerator INTEGER NULL, conversion_denominator INTEGER NULL);
        CREATE TABLE stock_groups (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, parent_id TEXT NULL REFERENCES stock_groups(id), alias TEXT NULL, add_quantities INTEGER NOT NULL DEFAULT 1);
        CREATE TABLE stock_categories (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, parent_id TEXT NULL REFERENCES stock_categories(id), alias TEXT NULL);
        CREATE TABLE godowns (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, parent_id TEXT NULL REFERENCES godowns(id), alias TEXT NULL,
            third_party INTEGER NOT NULL DEFAULT 0, is_main_location INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE stock_items (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, stock_group_id TEXT NOT NULL REFERENCES stock_groups(id),
            category_id TEXT NULL REFERENCES stock_categories(id), base_unit_id TEXT NOT NULL REFERENCES units(id),
            alias TEXT NULL, valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL,
            is_taxable INTEGER NOT NULL DEFAULT 0, reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL,
            standard_cost_paisa INTEGER NULL);
        CREATE TABLE stock_opening_balances (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            stock_item_id TEXT NOT NULL REFERENCES stock_items(id), godown_id TEXT NOT NULL REFERENCES godowns(id),
            batch_label TEXT NULL, quantity_micro INTEGER NOT NULL, rate_paisa INTEGER NOT NULL,
            mfg_date TEXT NULL, expiry_date TEXT NULL);
        CREATE TABLE inventory_vouchers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL, date TEXT NOT NULL,
            narration TEXT NULL, party_id TEXT NULL REFERENCES ledgers(id),
            cancelled INTEGER NOT NULL DEFAULT 0, post_dated INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE inventory_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order INTEGER NOT NULL, role INTEGER NOT NULL, stock_item_id TEXT NOT NULL REFERENCES stock_items(id),
            godown_id TEXT NOT NULL REFERENCES godowns(id), unit_id TEXT NULL REFERENCES units(id),
            quantity_micro INTEGER NOT NULL, direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL);
        CREATE TABLE order_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL REFERENCES stock_items(id),
            godown_id TEXT NOT NULL REFERENCES godowns(id), quantity_micro INTEGER NOT NULL, rate_paisa INTEGER NULL);
        CREATE TABLE physical_stock_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL REFERENCES stock_items(id),
            godown_id TEXT NOT NULL REFERENCES godowns(id), counted_qty_micro INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE voucher_inventory_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL REFERENCES vouchers(id),
            line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL REFERENCES stock_items(id),
            godown_id TEXT NOT NULL REFERENCES godowns(id), quantity_micro INTEGER NOT NULL, direction INTEGER NOT NULL,
            rate_paisa INTEGER NOT NULL, batch_label TEXT NULL);
        """;
}
