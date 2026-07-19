using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v45 → v46 (WI-10 Gap 2 — the <b>item-invoice line unit</b>). Proves the three things a version bump
/// owes: the new <c>voucher_inventory_lines.unit_id</c> column round-trips a real line unit; a genuine v45
/// database <b>carrying rows</b> migrates up leaving every row intact and the new column NULL (ER-13 — no
/// backfill, no rewrite); and the store refuses nothing it used to accept.
///
/// <para>The "genuine v45 database" is manufactured with <see cref="SchemaDowngrade.V46ToV45"/> rather than
/// hand-written DDL, so the migration is exercised against real rows instead of an empty schema — which is
/// exactly where the interesting failures are not.</para>
/// </summary>
public sealed class ItemLineUnitSchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_item_invoice_line_unit_round_trips_through_the_store()
    {
        var dbPath = TempDbFile.NewPath("apex-wi10gap2-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Unit Line Co", FyStart);
            var (itemId, godownId, dozNosId) = SeedEggAndDozen(company);

            var salesType = company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
            var customer = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Customer", company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
            var sales = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Sales", company.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
            company.AddLedger(customer);
            company.AddLedger(sales);

            // "2 Doz @ ₹10" = ₹20 — the value leg the pairing invariant checks against.
            var voucher = new Voucher(
                Guid.NewGuid(), salesType.Id, FyStart,
                new[]
                {
                    new EntryLine(customer.Id, Money.FromRupees(20m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(20m), DrCr.Credit),
                },
                partyId: customer.Id,
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(itemId, godownId, 2m, Money.FromRupees(10m),
                        StockDirection.Outward, null, null, dozNosId),
                });
            new LedgerService(company).Post(voucher);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // The column carries the unit id verbatim.
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_inventory_lines WHERE unit_id IS NOT NULL;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            var il = Assert.Single(loaded.Vouchers.Single(v => v.TypeId == salesType.Id).InventoryLines);

            Assert.Equal(dozNosId, il.UnitId);
            Assert.Equal(2m, il.Quantity);
            Assert.Equal(Money.FromRupees(10m), il.Rate);
            Assert.Equal(Money.FromRupees(20m), il.Value);   // value is per the LINE unit — unchanged by v46
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_real_v45_database_with_data_migrates_to_v46_leaving_every_row_intact()
    {
        var dbPath = TempDbFile.NewPath("apex-wi10gap2-v45legacy");
        try
        {
            // 1) A genuine current-version company carrying a real item-invoice line (no line unit — the only
            //    kind a v45 database can hold).
            var company = CompanyFactory.CreateSeeded("Legacy Lines Co", FyStart);
            var (itemId, godownId, _) = SeedEggAndDozen(company);

            var purchaseType = company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
            var supplier = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Supplier", company.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
            var purchases = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Purchases", company.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
            company.AddLedger(supplier);
            company.AddLedger(purchases);
            new LedgerService(company).Post(new Voucher(
                Guid.NewGuid(), purchaseType.Id, FyStart,
                new[]
                {
                    new EntryLine(purchases.Id, Money.FromRupees(240m), DrCr.Debit),
                    new EntryLine(supplier.Id, Money.FromRupees(240m), DrCr.Credit),
                },
                partyId: supplier.Id,
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(itemId, godownId, 24m, Money.FromRupees(10m), StockDirection.Inward),
                })); 

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            var lineCountBefore = ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_inventory_lines;");
            Assert.True(lineCountBefore > 0);

            // 2) Downgrade to a genuine v45 shape (drop unit_id, stamp version 45).
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V46ToV45(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(45L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("unit_id", ColumnNames(dbPath, "voucher_inventory_lines"));

            // 3) Reopen through the production store — the v45 → v46 migration runs.
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Contains("unit_id", ColumnNames(dbPath, "voucher_inventory_lines"));

            // Every pre-existing row survived and the new column reads NULL (no backfill, no rewrite — ER-13).
            Assert.Equal(lineCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_inventory_lines;"));
            Assert.Equal(lineCountBefore,
                ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_inventory_lines WHERE unit_id IS NULL;"));

            // And the migrated database still loads, with no unit conjured onto any line.
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            var il = Assert.Single(loaded.Vouchers.Single(v => v.TypeId == purchaseType.Id).InventoryLines);
            Assert.Null(il.UnitId);
            Assert.Equal(24m, il.Quantity);
            Assert.Equal(Money.FromRupees(240m), il.Value);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    /// <summary>Seeds a Nos-measured "Egg" plus a Doz-Nos compound unit (1 Doz = 12 Nos).</summary>
    private static (Guid ItemId, Guid GodownId, Guid DozNosId) SeedEggAndDozen(Company company)
    {
        var inv = new InventoryService(company);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var doz = inv.CreateSimpleUnit("Doz", "Dozens");
        var dozNos = inv.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var egg = inv.CreateStockItem("Egg", grp.Id, nos.Id);
        // Opening stock so an outward item-invoice line does not trip the no-negative-stock guard.
        inv.AddOpeningBalance(egg.Id, company.MainLocation!.Id, 240m, Money.FromRupees(1m));
        return (egg.Id, company.MainLocation!.Id, dozNos.Id);
    }

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
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
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }
}
