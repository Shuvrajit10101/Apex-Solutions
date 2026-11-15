using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v68 → v69 (census row <b>5.7</b>, Optional voucher on the pure-stock aggregate; row <b>5.8</b> rides the
/// same rung with the <c>SetPostDated = 5</c> edit-log ordinal, which needs no DDL): ONE column,
/// <c>inventory_vouchers.optional</c>.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/,
/// opened by content 2026-11-02: <i>"Any voucher in TallyPrime can be marked as Optional by pressing Ctrl+L
/// (Optional) during voucher entry or in alteration mode."</i></para>
///
/// <para>🔴 <b>What this file proves beyond "the column exists":</b> (1) a REAL v68 book holding posted stock
/// vouchers with allocation, order-line and physical-line children climbs the rung with every row intact and every
/// voucher live; (2) the migrated contract equals the fresh one; (3) the downgrade keeps the PRIMARY KEY of an FK
/// parent; (4) an Optional mark and a Post-Dated clear made through the engine verbs are read back off a FRESH store
/// handle together with their edit-log verbs, and the closing stock moves with them.</para>
/// </summary>
public sealed class InventoryOptionalV69SchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private sealed record Seeded(Company Company, Guid ItemId, Guid GodownId, Guid ReceiptId, Guid PostDatedId);

    /// <summary>A company with a Receipt Note (7 in), a post-dated Delivery Note (2 out), a Purchase Order and a
    /// Physical Stock count — so every child table of <c>inventory_vouchers</c> holds rows.</summary>
    private static Seeded Seed(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("V69 Goods");
        var unit = masters.CreateSimpleUnit("V69Nos", "V69 Numbers");
        var item = masters.CreateStockItem("V69 Widget", group.Id, unit.Id);
        var godown = c.MainLocation!;
        masters.AddOpeningBalance(item.Id, godown.Id, 10m, Money.FromRupees(100.37m));

        VoucherType Type(VoucherBaseType bt) => c.VoucherTypes.First(t => t.BaseType == bt);
        var posting = new InventoryPostingService(c);

        var receipt = posting.Post(new InventoryVoucher(Guid.NewGuid(), Type(VoucherBaseType.ReceiptNote).Id,
            new DateOnly(2024, 4, 10),
            new[] { new InventoryAllocation(item.Id, godown.Id, 7m, StockDirection.Inward, Money.FromRupees(55.01m)) },
            number: 0, narration: "receipt", partyId: null, cancelled: false, postDated: false));

        var delivery = posting.Post(new InventoryVoucher(Guid.NewGuid(), Type(VoucherBaseType.DeliveryNote).Id,
            new DateOnly(2024, 4, 20),
            new[] { new InventoryAllocation(item.Id, godown.Id, 2m, StockDirection.Outward, Money.FromRupees(80.99m)) },
            number: 0, narration: "pdc", partyId: null, cancelled: false, postDated: true));

        posting.Post(InventoryVoucher.Order(Guid.NewGuid(), Type(VoucherBaseType.PurchaseOrder).Id,
            new DateOnly(2024, 4, 12), new[] { new OrderLine(item.Id, godown.Id, 3m, Money.FromRupees(51.11m)) },
            number: 0, narration: null, partyId: null, cancelled: false, postDated: false));

        posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(), Type(VoucherBaseType.PhysicalStock).Id,
            new DateOnly(2024, 6, 30), new[] { new PhysicalStockLine(item.Id, godown.Id, 15m, null) },
            number: 0, narration: null, cancelled: false, postDated: false));

        return new Seeded(c, item.Id, godown.Id, receipt.Id, delivery.Id);
    }

    /// <summary>
    /// 🔴 <b>THE MIGRATION TEST ON A REAL v68 BOOK.</b> Walked down to v68 (proved: marker 68, column absent), then
    /// re-opened by the production store: it must climb to v69 with every header and every child row intact, every
    /// stock voucher live (optional = 0, no back-fill), the closing stock unchanged, and no FK violation.
    ///
    /// <para><b>Mutation:</b> deleting the <c>if (version == 68)</c> rung in <c>SqliteCompanyStore</c> reddens this
    /// (the store throws "schema version 68 is not supported").</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_real_v68_book_climbs_to_v69_with_every_stock_voucher_and_child_intact_and_live()
    {
        var path = TempDbFile.NewPath("apex-v69-real");
        try
        {
            var s = Seed("Legacy Stock Optional Co");
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            var headers = ReadScalar(path, "SELECT COUNT(*) FROM inventory_vouchers;");
            var allocations = ReadScalar(path, "SELECT COUNT(*) FROM inventory_allocations;");
            Assert.Equal(4L, headers);
            Assert.True(allocations >= 2);
            var closingBefore = new InventoryLedger(s.Company).OnHand(s.ItemId, s.GodownId, new DateOnly(2024, 5, 31));

            using (var conn = Open(path)) { SchemaDowngrade.V69ToV68(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(68L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("optional", ColumnNames(path, "inventory_vouchers"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;

            Assert.Equal(69L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Contains("optional", ColumnNames(path, "inventory_vouchers"));
            Assert.Equal(headers, ReadScalar(path, "SELECT COUNT(*) FROM inventory_vouchers;"));
            Assert.Equal(allocations, ReadScalar(path, "SELECT COUNT(*) FROM inventory_allocations;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM inventory_vouchers WHERE optional <> 0;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));

            Assert.Equal(4, reloaded.InventoryVouchers.Count);
            Assert.All(reloaded.InventoryVouchers, v => Assert.False(v.Optional));
            Assert.True(reloaded.FindInventoryVoucher(s.PostDatedId)!.PostDated);
            Assert.Equal(closingBefore,
                new InventoryLedger(reloaded).OnHand(s.ItemId, s.GodownId, new DateOnly(2024, 5, 31)));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>The migrated <c>inventory_vouchers</c> contract equals the fresh one, column for column.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v68_to_v69_matches_CreateV1_on_inventory_vouchers()
    {
        var migratedPath = TempDbFile.NewPath("apex-v69-migrated");
        var freshPath = TempDbFile.NewPath("apex-v69-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Stock Optional Co", FyStart));
            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Migrated Stock Optional Co", FyStart));

            using (var conn = Open(migratedPath)) { SchemaDowngrade.V69ToV68(conn); SqliteConnection.ClearPool(conn); }
            Assert.DoesNotContain("optional", ColumnNames(migratedPath, "inventory_vouchers"));

            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal(TableInfo(freshPath, "inventory_vouchers"), TableInfo(migratedPath, "inventory_vouchers"));
            Assert.Equal(Schema.V69InventoryVoucherColumns, new[] { "optional" });
            Assert.DoesNotContain("UPDATE ", Schema.MigrateV68ToV69);
            Assert.Equal(69, Schema.CurrentVersion);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <c>inventory_vouchers</c> is an FK PARENT. The downgrade must keep its PRIMARY KEY and its indexes, leave
    /// every child row pointing at a live header, and the file must climb back up and save again cleanly.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v69_downgrade_preserves_the_primary_key_index_and_every_child_row()
    {
        var path = TempDbFile.NewPath("apex-v69-fk");
        try
        {
            var s = Seed("FK Stock Optional Co");
            new InventoryPostingService(s.Company).SetOptional(s.ReceiptId, true);
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            var indexesBefore = IndexNames(path, "inventory_vouchers").OrderBy(n => n).ToList();

            using (var conn = Open(path)) { SchemaDowngrade.V69ToV68(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "inventory_vouchers"));
            Assert.Equal(indexesBefore, IndexNames(path, "inventory_vouchers").OrderBy(n => n).ToList());
            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;
            Assert.Equal(69L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            // What a downgrade means: the Optional mark is lost, the vouchers are not.
            Assert.All(reloaded.InventoryVouchers, v => Assert.False(v.Optional));
            Assert.Equal(4, reloaded.InventoryVouchers.Count);
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE WRITER AND READER ARE BOTH LIVE, AND THE FLAGS MOVE THE BOOK.</b> <c>SetOptional(true)</c> on the
    /// Receipt Note and <c>SetPostDated(false)</c> on the Delivery Note, saved by the production store and read
    /// back off a FRESH handle: the raw column holds 1 on exactly one row, the reloaded voucher is Optional, the
    /// post-dated one is current, both edit-log verbs survive with their own ordinals (4 and 5), and the closing
    /// stock no longer carries the 7 received.
    ///
    /// <para><b>Mutations:</b> binding <c>$opt</c> to <c>0</c> in the writer reddens the raw-SQL assertion;
    /// dropping <c>loaded.Optional = h.Optional</c> in the reader reddens the reloaded-voucher assertion; removing
    /// <c>|| v.Optional</c> from <c>InventoryLedger.Counts</c> reddens the on-hand assertion.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Optional_and_post_dated_moves_are_read_back_off_a_fresh_store_handle_with_their_verbs()
    {
        var path = TempDbFile.NewPath("apex-v69-roundtrip");
        try
        {
            var s = Seed("Round Trip Stock Optional Co");
            var asOf = new DateOnly(2024, 5, 31);
            Assert.Equal(15m, new InventoryLedger(s.Company).OnHand(s.ItemId, s.GodownId, asOf));   // 10 + 7 − 2

            var posting = new InventoryPostingService(s.Company);
            posting.SetOptional(s.ReceiptId, true);
            posting.SetPostDated(s.PostDatedId, false);
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);

            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM inventory_vouchers WHERE optional = 1;"));
            Assert.Equal(1L, ReadScalar(path,
                $"SELECT optional FROM inventory_vouchers WHERE id = '{s.ReceiptId:D}';"));
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_edit_log WHERE verb = 4;"));
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_edit_log WHERE verb = 5;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;
            Assert.True(reloaded.FindInventoryVoucher(s.ReceiptId)!.Optional);
            Assert.False(reloaded.FindInventoryVoucher(s.PostDatedId)!.PostDated);
            Assert.Contains(reloaded.VoucherEditLog,
                e => e.VoucherId == s.ReceiptId && e.Verb == VoucherEditVerb.SetOptional);
            Assert.Contains(reloaded.VoucherEditLog,
                e => e.VoucherId == s.PostDatedId && e.Verb == VoucherEditVerb.SetPostDated);
            Assert.Equal(8m, new InventoryLedger(reloaded).OnHand(s.ItemId, s.GodownId, asOf));     // 10 − 2

            // Regularise (Ctrl+L again) on the reloaded book, save, reload: the 7 come back.
            new InventoryPostingService(reloaded).SetOptional(s.ReceiptId, false);
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Company again;
            using (var store = new SqliteCompanyStore(path)) again = store.Load(s.Company.Id)!;
            Assert.False(again.FindInventoryVoucher(s.ReceiptId)!.Optional);
            Assert.Equal(15m, new InventoryLedger(again).OnHand(s.ItemId, s.GodownId, asOf));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= helpers

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
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

    private static List<string> IndexNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$t", table);
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
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
}
