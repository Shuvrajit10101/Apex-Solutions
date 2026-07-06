using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Batch-master persistence contract (Phase 6 Cluster 1; RQ-1/RQ-3/RQ-6, ER-1): a company carrying first-class
/// <see cref="BatchMaster"/> records (with mfg/expiry, a resolvable expiry period, an inward-layer godown and a
/// per-batch inward cost layer) and stock items with the three batch switches SAVES and RELOADS to the paisa —
/// every batch master + every switch survives byte-identically, and per-line batch labels still reconcile. A
/// non-batch company is unaffected (ER-13).
/// </summary>
public sealed class BatchMasterRoundTripTests
{
    // A company with two batch masters on one item: one dated (mfg + absolute expiry + cost layer), one that
    // carries an expiry PERIOD (resolvable) instead of an absolute date, plus the three item switches on.
    private static (Company Company, Guid ItemId, Guid GodownId) SeedWithBatches()
    {
        var c = CompanyFactory.CreateSeeded("Batch Persist Co", new DateOnly(2024, 4, 1));
        var inv = new InventoryService(c);
        var batches = new BatchService(c);

        var grp = inv.CreateStockGroup("Pharma");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var item = inv.CreateStockItem("Paracetamol", grp.Id, nos.Id);
        item.MaintainInBatches = true;
        item.TrackManufacturingDate = true;
        item.UseExpiryDates = true;

        var godown = c.MainLocation!;

        // Batch 1: full cost layer with mfg + absolute expiry.
        batches.CreateBatch(item.Id, "LOT-A",
            manufacturingDate: new DateOnly(2024, 1, 10),
            expiryDate: new DateOnly(2026, 1, 10),
            godownId: godown.Id,
            inwardQuantity: 250m,
            inwardRate: Money.FromRupees(12.34m));

        // Batch 2: expiry expressed as a PERIOD ("18 Months") from the mfg date, no cost layer.
        batches.CreateBatch(item.Id, "LOT-B",
            manufacturingDate: new DateOnly(2024, 3, 1),
            expiryPeriod: new ExpiryPeriod(18, ExpiryPeriodUnit.Months));

        // A per-line batch allocation (opening stock carrying a batch label matching LOT-A).
        inv.AddOpeningBalance(item.Id, godown.Id, 50m, Money.FromRupees(12.34m), batchLabel: "LOT-A");

        return (c, item.Id, godown.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Batch_masters_and_item_switches_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-batch-{Guid.NewGuid():N}.db");
        try
        {
            var (original, itemId, godownId) = SeedWithBatches();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                // Re-save (delete-then-insert upsert) must not trip a batch foreign key.
                write.Save(original);
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Item switches survived.
            var item = reloaded.FindStockItem(itemId)!;
            Assert.True(item.MaintainInBatches);
            Assert.True(item.TrackManufacturingDate);
            Assert.True(item.UseExpiryDates);

            // Both batch masters survived, by number, with every field.
            var reBatches = reloaded.BatchesFor(itemId).OrderBy(b => b.BatchNumber).ToList();
            Assert.Equal(2, reBatches.Count);

            var a = reloaded.FindBatchByNumber(itemId, "LOT-A")!;
            Assert.Equal(new DateOnly(2024, 1, 10), a.ManufacturingDate);
            Assert.Equal(new DateOnly(2026, 1, 10), a.ExpiryDate);
            Assert.Null(a.ExpiryPeriod);
            Assert.Equal(godownId, a.GodownId);
            Assert.Equal(250m, a.InwardQuantity);
            Assert.Equal(Money.FromRupees(12.34m), a.InwardRate);
            Assert.Equal(new DateOnly(2026, 1, 10), a.ResolvedExpiryDate);

            var b = reloaded.FindBatchByNumber(itemId, "LOT-B")!;
            Assert.Equal(new DateOnly(2024, 3, 1), b.ManufacturingDate);
            Assert.Null(b.ExpiryDate);
            Assert.Equal(new ExpiryPeriod(18, ExpiryPeriodUnit.Months), b.ExpiryPeriod);
            Assert.Null(b.GodownId);
            Assert.Null(b.InwardQuantity);
            Assert.Null(b.InwardRate);
            // Period resolves from mfg (2024-03-01 + 18 months = 2025-09-01).
            Assert.Equal(new DateOnly(2025, 9, 1), b.ResolvedExpiryDate);

            // Per-line batch label still reconciles.
            var ob = reloaded.OpeningBalancesFor(itemId).Single();
            Assert.Equal("LOT-A", ob.BatchLabel);
            Assert.Equal(50m, ob.Quantity);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Batch_number_reused_across_two_items_round_trips()
    {
        // Batch numbers are unique WITHIN an item, not globally (RQ-1) — two items may both carry "B1".
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-batch-reuse-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Reuse Co", new DateOnly(2024, 4, 1));
            var inv = new InventoryService(c);
            var batches = new BatchService(c);
            var grp = inv.CreateStockGroup("G");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var i1 = inv.CreateStockItem("Item One", grp.Id, nos.Id);
            var i2 = inv.CreateStockItem("Item Two", grp.Id, nos.Id);
            batches.CreateBatch(i1.Id, "B1", inwardQuantity: 10m, inwardRate: Money.FromRupees(5m));
            batches.CreateBatch(i2.Id, "B1", inwardQuantity: 20m, inwardRate: Money.FromRupees(7m));

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            Assert.Equal(2, reloaded.BatchMasters.Count);
            Assert.Equal(Money.FromRupees(5m), reloaded.FindBatchByNumber(i1.Id, "B1")!.InwardRate);
            Assert.Equal(Money.FromRupees(7m), reloaded.FindBatchByNumber(i2.Id, "B1")!.InwardRate);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Non_batch_company_has_no_batch_masters_and_default_switches()
    {
        // ER-13: an item without batches reloads with all three switches off and no batch masters.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-nobatch-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", new DateOnly(2024, 4, 1));
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("G");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
            inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 5m, Money.FromRupees(10m));

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            Assert.Empty(reloaded.BatchMasters);
            var ri = reloaded.FindStockItemByName("Widget")!;
            Assert.False(ri.MaintainInBatches);
            Assert.False(ri.TrackManufacturingDate);
            Assert.False(ri.UseExpiryDates);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
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
