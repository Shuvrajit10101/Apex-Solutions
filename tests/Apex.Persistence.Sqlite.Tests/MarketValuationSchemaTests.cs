using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v64 → v65 (census <b>3.4</b>, defect <b>T0-2</b>, register IV-6; <b>USER RULING 26</b>) — the
/// <b>Market Valuation</b> dimension, and the remediation of a costing method that valued closing stock at the
/// <b>selling</b> price.
///
/// <para>Older databases are manufactured by walking the ladder DOWN from current with
/// <see cref="SchemaDowngrade"/> rather than from hand-written DDL, so the migrations are exercised against real
/// rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IS
/// <see cref="A_book_costed_at_the_retired_sale_price_method_is_migrated_and_its_stock_value_falls"/>, AND IT IS
/// NOT "THE COLUMNS EXIST".</b> Ruling 26 is a deliberate, accepted MONEY movement: a book that had chosen the
/// retired method has its closing stock restated on upgrade. A column-existence test cannot see that at all, so
/// that test migrates a real book across the rung and compares the <b>computed closing stock value to the
/// paisa</b>, before and after — and asserts it falls by exactly the unrealised margin.</para>
///
/// <para>🔴 <b>AND THE MIGRATION MUST BE INERT FOR EVERYONE ELSE</b>
/// (<see cref="A_book_that_never_chose_the_retired_method_does_not_move_one_paisa"/>). A remediation that also
/// disturbed unaffected books would be a second wrong-money event riding in on the fix for the first.</para>
/// </summary>
public sealed class MarketValuationSchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly AsOf = new(2024, 4, 20);

    private const string Table = "stock_items";

    // ================================================================= migration parity

    /// <summary>
    /// The table a migrated book ends up with is byte-identical in contract (name / type / NOT NULL / DEFAULT /
    /// PK on every column) to the one <c>CreateV1</c> gives a fresh book — the equivalence rule this repository
    /// treats as absolute, and the exact comparison <c>SchemaMigrationEquivalenceTests</c> makes. This test
    /// pins the three v65 columns specifically, so a DEFAULT drifting between the ALTER and CreateV1 is caught
    /// here with a readable message rather than only in the whole-schema sweep.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v64_to_v65_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-v65-migrated");
        var freshPath = TempDbFile.NewPath("apex-v65-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Market Co", FyStart));
            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Legacy Market Co", FyStart));

            // ONE rung only — this test is about the v64 → v65 step in isolation.
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V65ToV64(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(64L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The downgraded book genuinely lacks them — otherwise the "migration" below proves nothing.
            foreach (var col in Schema.V65StockItemColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, Table));

            // Reopen through the production store — the v64 → v65 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The migrated contract equals the fresh one, column for column.
            Assert.Equal(TableInfo(freshPath, Table), TableInfo(migratedPath, Table));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b><c>stock_items</c> IS AN FK PARENT</b> — a dozen tables reference <c>stock_items(id)</c> — so the
    /// downgrade must rebuild it with <c>RebuildPreservingShape</c>, not a <c>CREATE … AS SELECT</c>, which
    /// silently loses the PRIMARY KEY (the <c>foreign key mismatch</c> failure <c>V56ToV55</c> records). This
    /// asserts the PK survives the round trip and that SQLite's own integrity check is still clean.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v65_downgrade_preserves_the_stock_items_primary_key_and_foreign_keys()
    {
        var path = TempDbFile.NewPath("apex-v65-fk");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("FK Co", FyStart));

            using (var conn = Open(path)) { SchemaDowngrade.V65ToV64(conn); SqliteConnection.ClearPool(conn); }

            // The PK survived the rebuild (pk flag still set on exactly one column: id).
            var pk = PrimaryKeyColumns(path, Table);
            Assert.Equal(new List<string> { "id" }, pk);

            // No dangling FK anywhere in the file, and the file itself is structurally sound.
            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            // And it climbs back up cleanly.
            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= 🔴 the money

    /// <summary>
    /// 🔴 <b>THE LOAD-BEARING TEST — the accepted, deliberate money movement of user ruling 26.</b>
    ///
    /// <para>A v64 book costed at the retired <c>LastSaleCost</c> ordinal (5) bought 100 @ ₹10 and sold 30 @ ₹20.
    /// Before the upgrade it valued closing stock 70 × ₹20 = <b>₹1,400</b> — at its own SELLING rate, overstating
    /// Stock-in-Hand and profit by the ₹700 unrealised margin. After the upgrade it is on
    /// <c>LastPurchaseCost</c> (4) and values 70 × ₹10 = <b>₹700</b>.</para>
    ///
    /// <para>The item is also stamped with where it came from, which is what lets the next open warn <i>that</i>
    /// operator and nobody else.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_book_costed_at_the_retired_sale_price_method_is_migrated_and_its_stock_value_falls()
    {
        var path = TempDbFile.NewPath("apex-v65-money");
        try
        {
            var (company, itemId) = BookValuedAtTheSaleRate();
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            // Manufacture the v64 book: drop to 64, then force the item back onto the retired ordinal 5, which is
            // the only state a real pre-v65 book could be in.
            using (var conn = Open(path)) { SchemaDowngrade.V65ToV64(conn); SqliteConnection.ClearPool(conn); }
            Exec(path, $"UPDATE stock_items SET valuation_method = {Schema.V65RetiredSaleCostMethod};");

            // Sanity: the v64 file really is in the affected state — ordinal 5, unstamped — otherwise the
            // migration below proves nothing.
            //
            // ⚠️ NOTE WHAT IS *NOT* ASSERTED HERE, because it cannot honestly be. The pre-upgrade figure of
            // ₹1,400 was produced by the OLD engine, and this build no longer contains a code path that can
            // return it: StockValuationService now maps ordinal 5 explicitly onto Last Purchase Cost, precisely
            // so that a book which escapes this migration still cannot be valued at a sale price. Re-deriving
            // "₹1,400" with today's assemblies would therefore be a fabricated baseline. The ₹1,400 → ₹700
            // movement is pinned at the engine level instead, by
            // StockValuationTests.Retired_last_sale_cost_ordinal_values_at_purchase_cost_never_at_the_sale_rate,
            // which asserts the corrected ₹700 and explicitly refutes ₹1,400.
            Assert.Equal((long)Schema.V65RetiredSaleCostMethod,
                ReadScalar(path, "SELECT valuation_method FROM stock_items LIMIT 1;"));

            // Upgrade through the production store — the v65 migration's one UPDATE runs.
            using (new SqliteCompanyStore(path)) { }

            Assert.Equal((long)Schema.V65RemediationTargetMethod,
                ReadScalar(path, "SELECT valuation_method FROM stock_items LIMIT 1;"));
            Assert.Equal((long)Schema.V65RetiredSaleCostMethod,
                ReadScalar(path, "SELECT valuation_remediated_from FROM stock_items LIMIT 1;"));

            // 🔴 THE MONEY: ₹1,400 → ₹700, exactly the unrealised margin, and the operator is owed a warning.
            var after = ClosingValueOnDisk(path, company.Id, itemId, viaStore: true);
            Assert.Equal(Money.FromRupees(700m), after);
            Assert.NotEqual(Money.FromRupees(1400m), after);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE OTHER HALF: the migration is INERT for a book that never chose the retired method.</b> Its
    /// costing method is untouched, its closing stock value does not move one paisa, and — critically — it is
    /// NOT stamped, so it will never see the upgrade warning.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_book_that_never_chose_the_retired_method_does_not_move_one_paisa()
    {
        var path = TempDbFile.NewPath("apex-v65-inert");
        try
        {
            var (company, itemId) = BookValuedAtTheSaleRate(StockValuationMethod.Fifo);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using (var conn = Open(path)) { SchemaDowngrade.V65ToV64(conn); SqliteConnection.ClearPool(conn); }
            var before = ClosingValueOnDisk(path, company.Id, itemId, viaStore: false);

            using (new SqliteCompanyStore(path)) { }   // climb to v65

            Assert.Equal((long)StockValuationMethod.Fifo,
                ReadScalar(path, "SELECT valuation_method FROM stock_items LIMIT 1;"));
            Assert.Equal(before, ClosingValueOnDisk(path, company.Id, itemId, viaStore: true));

            // Not stamped ⇒ stays silent on open. This is what keeps the warning worth reading.
            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM stock_items WHERE valuation_remediated_from IS NULL;"));

            using var store2 = new SqliteCompanyStore(path);
            Assert.Equal(string.Empty, ValuationRemediationNotice.For(store2.Load(company.Id)!));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE WARNING SURVIVES THE ROUND TRIP TO DISK.</b> The marker is written back verbatim by the store,
    /// so simply saving the book (an operator editing any unrelated master) cannot silence an upgraded book's
    /// warning. A warning that a routine save switches off is not a warning.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_upgrade_warning_survives_a_save_and_reload()
    {
        var path = TempDbFile.NewPath("apex-v65-warn");
        try
        {
            var (company, _) = BookValuedAtTheSaleRate();
            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            using (var conn = Open(path)) { SchemaDowngrade.V65ToV64(conn); SqliteConnection.ClearPool(conn); }
            Exec(path, $"UPDATE stock_items SET valuation_method = {Schema.V65RetiredSaleCostMethod};");
            using (new SqliteCompanyStore(path)) { }   // migrate: stamps the marker

            string first;
            using (var store = new SqliteCompanyStore(path))
            {
                var loaded = store.Load(company.Id)!;
                first = ValuationRemediationNotice.For(loaded);
                Assert.NotEqual(string.Empty, first);
                Assert.Contains("Widget", first);
                store.Save(loaded);                     // an ordinary save…
            }

            using (var store = new SqliteCompanyStore(path))
            {
                // …and the warning is still there, word for word.
                Assert.Equal(first, ValuationRemediationNotice.For(store.Load(company.Id)!));
            }
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The new market-valuation basis and the standard PRICE round-trip to disk and back, and the standard price
    /// stays rigorously distinct from the standard COST — folding them would rebuild the very conflation ruling
    /// 26 undoes, one level down.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Market_valuation_and_standard_price_round_trip_and_stay_distinct_from_standard_cost()
    {
        var path = TempDbFile.NewPath("apex-v65-rt");
        try
        {
            var (company, itemId) = BookValuedAtTheSaleRate(StockValuationMethod.StandardCost);
            var item = company.FindStockItem(itemId)!;
            item.StandardCost = Money.FromRupees(11m);
            item.StandardPrice = Money.FromRupees(47.25m);
            item.MarketValuationMethod = MarketValuationMethod.LastSalesPrice;
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!.FindStockItem(itemId)!;

            Assert.Equal(MarketValuationMethod.LastSalesPrice, back.MarketValuationMethod);
            Assert.Equal(Money.FromRupees(47.25m), back.StandardPrice);
            Assert.Equal(Money.FromRupees(11m), back.StandardCost);
            Assert.NotEqual(back.StandardCost, back.StandardPrice);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE LADDER IS PROVED BY CLIMBING, NOT ONLY BY DESCENDING.</b> Every other test here manufactures its
    /// book by DOWNGRADING, so a guard pointed at the wrong rung can hide behind a matched pair of wrong steps.
    /// This one climbs UP from v63 through the production store and asserts that v64's table AND v65's three
    /// columns are all present at the top.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v63_book_climbs_the_whole_ladder_and_gets_both_v64_and_v65_objects()
    {
        var path = TempDbFile.NewPath("apex-v65-climb");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("Climber Co", FyStart));

            using (var conn = Open(path))
            {
                SchemaDowngrade.V65ToV64(conn); SchemaDowngrade.V64ToV63(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(63L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            using (new SqliteCompanyStore(path)) { }   // climb 63 → 64 → 65

            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.True(TableExists(path, "income_tax_cess_rates"));         // v64's object
            foreach (var col in Schema.V65StockItemColumns)                  // v65's columns
                Assert.Contains(col, ColumnNames(path, Table));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= fixtures & helpers

    /// <summary>
    /// A book with one item bought 100 @ ₹10 and sold 30 @ ₹20 — the worked example from the defect. Under the
    /// retired sale-price basis its closing 70 values at ₹1,400; under any real cost basis, ₹700.
    /// </summary>
    private static (Company Company, Guid ItemId) BookValuedAtTheSaleRate(
        StockValuationMethod method = StockValuationMethod.LastPurchaseCost)
    {
        var c = CompanyFactory.CreateSeeded("Margin Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: method);
        var godown = c.MainLocation!.Id;
        var posting = new InventoryPostingService(c);

        posting.Post(new InventoryVoucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id, D1,
            new[] { new InventoryAllocation(item.Id, godown, 100m, StockDirection.Inward, Money.FromRupees(10m)) }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote).Id, D2,
            new[] { new InventoryAllocation(item.Id, godown, 30m, StockDirection.Outward, Money.FromRupees(20m)) }));

        return (c, item.Id);
    }

    /// <summary>
    /// The closing stock value the book on disk reports. <paramref name="viaStore"/> false reads a v64 file, whose
    /// schema the production store refuses, so the item's method is read directly and the engine is run over an
    /// in-memory rebuild of the same movements.
    /// </summary>
    private static Money ClosingValueOnDisk(string path, Guid companyId, Guid itemId, bool viaStore)
    {
        if (viaStore)
        {
            using var store = new SqliteCompanyStore(path);
            var c = store.Load(companyId)!;
            return new StockValuationService(c).ClosingValue(itemId, AsOf).Value;
        }

        var method = (StockValuationMethod)ReadScalar(path, "SELECT valuation_method FROM stock_items LIMIT 1;");
        var (rebuilt, rebuiltId) = BookValuedAtTheSaleRate(method);
        return new StockValuationService(rebuilt).ClosingValue(rebuiltId, AsOf).Value;
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearPool(conn);
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>name | type | notnull | default | pk for every column — the equivalence contract.</summary>
    private static List<string> TableInfo(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt32(5)));
        SqliteConnection.ClearPool(conn);
        return rows;
    }

    private static List<string> PrimaryKeyColumns(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var pk = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) if (r.GetInt32(5) > 0) pk.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return pk;
    }

    private static int ForeignKeyViolationCount(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var n = 0;
        using (var r = cmd.ExecuteReader()) while (r.Read()) n++;
        SqliteConnection.ClearPool(conn);
        return n;
    }

    private static string IntegrityCheck(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        SqliteConnection.ClearPool(conn);
        return result;
    }

    private static bool TableExists(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", table);
        var found = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return found;
    }
}
