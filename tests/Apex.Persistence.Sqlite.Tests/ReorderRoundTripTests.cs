using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Reorder-Level persistence contract (Phase 6 slice 6; RQ-32..RQ-35; ER-1, ER-13): a company carrying reorder
/// definitions for all three scopes — an Item-scoped Simple definition, a Group-scoped Advanced definition (with a
/// shared period + Higher criterion), and a Category-scoped one — SAVES and RELOADS to the exact micro figures,
/// flags, period unit/count and criterion. A company that never touched reorder levels reloads with none
/// (byte-identical, ER-13). A re-save (delete-then-insert upsert) must not trip an FK.
/// </summary>
public sealed class ReorderRoundTripTests
{
    private static readonly DateOnly Fy = new(2026, 4, 1);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Reorder_definitions_survive_save_reload_for_every_scope()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-reorder-rt-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Reorder Persist Co", Fy);
            var inv = new InventoryService(c);
            var group = inv.CreateStockGroup("Beverages");
            var category = inv.CreateStockCategory("Premium");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Cola", group.Id, nos.Id, categoryId: category.Id);

            var svc = new ReorderLevelsService(c);
            svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 20m, minOrderQuantity: 25m);
            svc.CreateOrUpdate(ReorderScope.Group, group.Id,
                reorderAdvanced: true, reorderQuantity: 100.5m,
                minQtyAdvanced: true, minOrderQuantity: 50m,
                periodCount: 3, periodUnit: ExpiryPeriodUnit.Weeks, criteria: ReorderCriteria.Higher);
            svc.CreateOrUpdate(ReorderScope.Category, category.Id, reorderQuantity: 12.345678m);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(c);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                write.Save(c); // re-save (delete-then-insert upsert) must not trip an FK
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(c.Id)!;

            Assert.Equal(3, reloaded.ReorderDefinitions.Count);

            var itemDef = reloaded.FindReorderDefinition(ReorderScope.Item, item.Id)!;
            Assert.False(itemDef.ReorderAdvanced);
            Assert.Equal(20m, itemDef.ReorderQuantity);
            Assert.Equal(25m, itemDef.MinOrderQuantity);
            Assert.Null(itemDef.PeriodUnit);
            Assert.Null(itemDef.Criteria);

            var groupDef = reloaded.FindReorderDefinition(ReorderScope.Group, group.Id)!;
            Assert.True(groupDef.ReorderAdvanced);
            Assert.True(groupDef.MinQtyAdvanced);
            Assert.Equal(100.5m, groupDef.ReorderQuantity);
            Assert.Equal(50m, groupDef.MinOrderQuantity);
            Assert.Equal(3, groupDef.PeriodCount);
            Assert.Equal(ExpiryPeriodUnit.Weeks, groupDef.PeriodUnit);
            Assert.Equal(ReorderCriteria.Higher, groupDef.Criteria);

            var catDef = reloaded.FindReorderDefinition(ReorderScope.Category, category.Id)!;
            Assert.Equal(12.345678m, catDef.ReorderQuantity);   // 6-dp micro round-trips exactly
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Company_without_reorder_definitions_reloads_with_none()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-noreorder-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", Fy);
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("G");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            inv.CreateStockItem("Widget", grp.Id, nos.Id);

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            Assert.Empty(reloaded.ReorderDefinitions);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }
}
