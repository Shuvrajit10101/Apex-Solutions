using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Price Levels / Price Lists persistence contract (Phase 6 slice 5; RQ-26..RQ-30; ER-1, ER-13): a company
/// carrying a named price level, a party-default level on a Sundry-Debtor ledger, and TWO dated price-list
/// versions with multi-slab lines (a closed-slab version and an open-ended one, with a discount) SAVES and
/// RELOADS to the exact micro / paisa / millis — level name, the <c>EnableMultiplePriceLevels</c> flag, the
/// party <c>DefaultPriceLevelId</c>, every <c>ApplicableFrom</c> date, and the append-only history all survive.
/// A non-price-level company reloads with the defaults (flag off, no levels/lists), byte-identical (ER-13).
/// </summary>
public sealed class PriceListRoundTripTests
{
    private static readonly DateOnly Fy = new(2026, 4, 1);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private static (Company Company, Guid LevelId, Guid ItemId, Guid DebtorId) SeedWithPriceLists()
    {
        var c = CompanyFactory.CreateSeeded("Price Persist Co", Fy);
        c.EnableMultiplePriceLevels = true;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Laptop", grp.Id, nos.Id);

        var svc = new PriceListService(c);
        var retail = svc.CreateLevel("Retail");

        // Version 1 (1-Apr): closed slabs 0–2 → 16,000 and 2–4 → 14,850 (PR-7 shape).
        svc.AddOrReviseList(retail.Id, item.Id, Apr1, new[]
        {
            new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
            new PriceListSlab(2m, 4m, Money.FromRupees(14850m)),
        });
        // Version 2 (1-Jul): an open-ended top slab with a discount (exercises NULL To + millis).
        svc.AddOrReviseList(retail.Id, item.Id, Jul1, new[]
        {
            new PriceListSlab(0m, 5m, Money.FromRupees(15000m)),
            new PriceListSlab(5m, null, Money.FromRupees(14000m), discountPercent: 12.5m),
        });

        // A Sundry-Debtor party carrying Retail as its default level (RQ-30).
        var debtor = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "ACME Corp", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, defaultPriceLevelId: retail.Id);
        c.AddLedger(debtor);

        return (c, retail.Id, item.Id, debtor.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Price_levels_lists_flag_and_party_default_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-pl-rt-{Guid.NewGuid():N}.db");
        try
        {
            var (original, levelId, itemId, debtorId) = SeedWithPriceLists();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                write.Save(original); // re-save (delete-then-insert upsert) must not trip an FK
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Company flag + level master survive.
            Assert.True(reloaded.EnableMultiplePriceLevels);
            var level = reloaded.FindPriceLevel(levelId)!;
            Assert.Equal("Retail", level.Name);

            // Party default level survives.
            Assert.Equal(levelId, reloaded.FindLedger(debtorId)!.DefaultPriceLevelId);

            // Both dated versions survive (append-only history), each with exact slabs.
            var versions = reloaded.PriceListsFor(levelId, itemId).OrderBy(v => v.ApplicableFrom).ToList();
            Assert.Equal(2, versions.Count);

            var v1 = versions[0];
            Assert.Equal(Apr1, v1.ApplicableFrom);
            Assert.Equal(2, v1.Slabs.Count);
            Assert.Equal(0m, v1.Slabs[0].FromQty);
            Assert.Equal(2m, v1.Slabs[0].ToQty);
            Assert.Equal(Money.FromRupees(16000m), v1.Slabs[0].Rate);
            Assert.Equal(0m, v1.Slabs[0].DiscountPercent);
            Assert.Equal(4m, v1.Slabs[1].ToQty);
            Assert.Equal(Money.FromRupees(14850m), v1.Slabs[1].Rate);

            var v2 = versions[1];
            Assert.Equal(Jul1, v2.ApplicableFrom);
            Assert.Equal(2, v2.Slabs.Count);
            Assert.Null(v2.Slabs[1].ToQty);                              // open-ended top slab round-trips
            Assert.Equal(Money.FromRupees(14000m), v2.Slabs[1].Rate);
            Assert.Equal(12.5m, v2.Slabs[1].DiscountPercent);           // discount millis round-trips exactly

            // The resolver still reads the reloaded data (RQ-28/RQ-29): qty 3 on 1-Apr → 14,850.
            Assert.Equal(Money.FromRupees(14850m),
                PriceResolver.Resolve(reloaded, levelId, itemId, 3m, Apr1)!.Value.Rate);
            // The 1-Jul open-ended discounted slab: qty 10 → net 14,000 × (1 − 0.125) = 12,250.
            Assert.Equal(Money.FromRupees(12250m),
                PriceResolver.Resolve(reloaded, levelId, itemId, 10m, Jul1)!.Value.EffectiveUnitRate);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Non_price_level_company_reloads_with_defaults()
    {
        // ER-13: a company that never touched price levels reloads with the flag off and no levels/lists.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-nopl-{Guid.NewGuid():N}.db");
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
            Assert.False(reloaded.EnableMultiplePriceLevels);
            Assert.Empty(reloaded.PriceLevels);
            Assert.Empty(reloaded.PriceLists);
            Assert.All(reloaded.Ledgers, l => Assert.Null(l.DefaultPriceLevelId));
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
