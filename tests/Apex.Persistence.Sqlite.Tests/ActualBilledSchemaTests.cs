using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-6 slice-4 <b>Zero-valued &amp; Actual-vs-Billed quantity</b> schema + persistence contract
/// (RQ-21..RQ-25; ER-1, ER-13, DP-7; PR-6). The v19→v20 bump is purely additive: four nullable qty columns
/// (<c>actual_qty_micro</c> / <c>billed_qty_micro</c> on both <c>voucher_inventory_lines</c> and
/// <c>inventory_allocations</c>) + <c>companies.use_separate_actual_billed_qty</c> (0/1 default 0) +
/// <c>voucher_types.allow_zero_valued</c> (0/1 default 0), leaving every existing table/row intact. Covers: a
/// fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new columns; a legacy v19 DB auto-migrates
/// forward preserving every row (flags default off, qty columns NULL); the migration is idempotent across
/// reopens; and a company with an Actual/Billed (60/50) line + a zero-valued free-goods line round-trips to the
/// micro/paisa.
/// </summary>
public sealed class ActualBilledSchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    // ---------------------------------------------------------------- fresh stamp

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_actual_billed_schema()
    {
        var dbPath = TempDb("apex-ab-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 20);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            // The two flag columns.
            Assert.Contains("use_separate_actual_billed_qty", ColumnNames(dbPath, "companies"));
            Assert.Contains("allow_zero_valued", ColumnNames(dbPath, "voucher_types"));
            // The four Actual/Billed qty columns on BOTH stock-line tables (DP-7).
            foreach (var table in new[] { "voucher_inventory_lines", "inventory_allocations" })
            {
                var cols = ColumnNames(dbPath, table);
                Assert.Contains("actual_qty_micro", cols);
                Assert.Contains("billed_qty_micro", cols);
            }

            // Re-opening a current-version DB is a no-op.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    // ---------------------------------------------------------------- legacy v19 → v20

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v19_database_auto_migrates_to_current_version_preserving_every_row()
    {
        var dbPath = TempDb("apex-ab-v19legacy");
        try
        {
            var company = Guid.NewGuid();
            var group = Guid.NewGuid();
            var ledger = Guid.NewGuid();
            var vtype = Guid.NewGuid();

            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV19Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (19);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V19 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO groups(id, company_id, name) VALUES ($id, $cid, 'Purchase Accounts');",
                    ("$id", group.ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO ledgers(id, company_id, name, group_id) VALUES ($id, $cid, 'Purchases', $g);",
                    ("$id", ledger.ToString("D")), ("$cid", company.ToString("D")), ("$g", group.ToString("D")));
                Exec(conn, "INSERT INTO voucher_types(id, company_id, name, base_type) VALUES ($id, $cid, 'Purchase', 2);",
                    ("$id", vtype.ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(19L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("use_separate_actual_billed_qty", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v19 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            // Every existing row survived (ER-13): the flags default OFF and the qty columns default NULL.
            Assert.Equal("Legacy V19 Co", ReadCompanyName(dbPath, company));
            Assert.Equal(0L, ReadLong(dbPath, "SELECT use_separate_actual_billed_qty FROM companies WHERE id = $id;", company));
            Assert.Equal(0L, ReadLong(dbPath, "SELECT allow_zero_valued FROM voucher_types WHERE id = $id;", vtype));
            Assert.Contains("actual_qty_micro", ColumnNames(dbPath, "voucher_inventory_lines"));
            Assert.Contains("billed_qty_micro", ColumnNames(dbPath, "inventory_allocations"));

            // Reopen is idempotent (still current version, no double-ALTER crash).
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    // ---------------------------------------------------------------- Actual/Billed + zero-valued round-trip (PR-6)

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Actual_billed_and_zero_valued_lines_survive_save_reload_to_the_micro()
    {
        var dbPath = TempDb("apex-ab-roundtrip");
        try
        {
            var (original, itemId, freeItemId, mainId) = SeedWithActualBilled();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                write.Save(original); // re-save (upsert) must not trip an FK
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // The company F11 flag survives.
            Assert.True(reloaded.UseSeparateActualBilledQuantity);
            // The voucher-type zero-valued flag survives.
            Assert.True(reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).AllowZeroValuedTransactions);

            var purchase = reloaded.Vouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Purchase);

            // The 60/50 Actual-Billed line survives exactly (Actual, Billed, Value).
            var ab = purchase.InventoryLines.Single(l => l.StockItemId == itemId);
            Assert.Equal(60m, ab.Quantity);            // Actual
            Assert.Equal(50m, ab.BilledQuantity);      // Billed
            Assert.Equal(Money.FromRupees(3500m), ab.Value);   // 50 × 70

            // The zero-valued free-goods line survives (rate 0, value 0, stock qty preserved).
            var free = purchase.InventoryLines.Single(l => l.StockItemId == freeItemId);
            Assert.Equal(3m, free.Quantity);
            Assert.Equal(Money.Zero, free.Rate);
            Assert.Equal(Money.Zero, free.Value);

            // Derived stock survives: 60 units of the A/B item valued ₹3,500; 3 free units valued ₹0.
            var valuation = new StockValuationService(reloaded);
            Assert.Equal(60m, new InventoryLedger(reloaded).OnHand(itemId, mainId, AsOf));
            Assert.Equal(Money.FromRupees(3500m), valuation.ClosingValue(itemId, AsOf).Value);
            Assert.Equal(3m, new InventoryLedger(reloaded).OnHand(freeItemId, mainId, AsOf));
            Assert.Equal(Money.Zero, valuation.ClosingValue(freeItemId, AsOf).Value);
        }
        finally { Delete(dbPath); }
    }

    // ---- fixture: a company whose Purchase carries a 60/50 A/B line + a zero-valued free-goods line ----

    private static (Company Company, Guid ItemId, Guid FreeItemId, Guid MainId) SeedWithActualBilled()
    {
        var c = CompanyFactory.CreateSeeded("Actual Billed Co", FyStart);
        c.UseSeparateActualBilledQuantity = true;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Kg", "Kilogram");
        var rice = masters.CreateStockItem("Basmati Rice", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var gift = masters.CreateStockItem("Gift Pack", grp.Id, nos.Id, valuationMethod: StockValuationMethod.AverageCost);
        var main = c.MainLocation!.Id;

        var purchases = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        var creditor = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(purchases);
        c.AddLedger(creditor);

        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        purchaseType.AllowZeroValuedTransactions = true;

        // 60 kg received / 50 kg billed @ ₹70 = ₹3,500, plus a free Gift Pack (3 @ ₹0). Dr Purchases 3,500 / Cr Creditor 3,500.
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), purchaseType.Id, D1, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(3500m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(3500m), DrCr.Credit),
        }, inventoryLines: new[]
        {
            new VoucherInventoryLine(rice.Id, main, 60m, Money.FromRupees(70m), billedQuantity: 50m),
            new VoucherInventoryLine(gift.Id, main, 3m, Money.Zero),
        }));

        return (c, rice.Id, gift.Id, main);
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static long ReadLong(string dbPath, string sql, Guid id)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        var v = Convert.ToInt64(cmd.ExecuteScalar());
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
    /// A minimal pre-v20 (v19) DDL: enough of the schema for the v19→v20 migration (which ADDs the two flag
    /// columns to <c>companies</c> / <c>voucher_types</c> and the four Actual/Billed qty columns to
    /// <c>voucher_inventory_lines</c> + <c>inventory_allocations</c>) plus a data-preservation assertion. The
    /// tables are shaped as at v19 (with the earlier additive columns, WITHOUT the v20 columns) so the v19→v20
    /// ALTERs apply cleanly. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV19Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE groups (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id), method_of_appropriation INTEGER NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL,
            use_as_manufacturing_journal INTEGER NOT NULL DEFAULT 0, track_additional_costs INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE inventory_vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL, date TEXT NOT NULL);
        CREATE TABLE inventory_allocations (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL, role INTEGER NOT NULL,
            stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL, unit_id TEXT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL, batch_id TEXT NULL);
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL,
            quantity_micro INTEGER NOT NULL, direction INTEGER NOT NULL, rate_paisa INTEGER NOT NULL,
            batch_label TEXT NULL, batch_id TEXT NULL);
        """;
}
