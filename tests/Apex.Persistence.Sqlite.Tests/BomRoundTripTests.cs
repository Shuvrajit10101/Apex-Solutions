using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Bill-of-Materials + Manufacturing-Journal persistence contract (Phase 6 slice 2; RQ-9..RQ-15, DP-3, ER-1): a
/// company carrying a multi-line <see cref="BillOfMaterials"/> (Component + carved-out Scrap/Co-Product lines with
/// an explicit paisa rate AND a percent basis), a finished good flagged <see cref="StockItem.SetComponents"/>, a
/// user-created Manufacturing-Journal voucher type (<see cref="VoucherType.UseAsManufacturingJournal"/>), and a
/// posted Manufacturing Journal SAVES and RELOADS to the paisa — every BOM header + line, both master flags, and
/// the manufactured finished-good stock value survive byte-identically. A non-manufacturing company is
/// unaffected (ER-13).
/// </summary>
public sealed class BomRoundTripTests
{
    private static readonly DateOnly Fy = new(2024, 4, 1);
    private static readonly DateOnly MfgDate = new(2024, 4, 15);

    // A trading company with a manufactured finished good: a "Standard" BOM (per a block of 10 units) with two
    // Component lines, a Scrap carve-out at ₹2/unit, and a Co-Product carve-out at 5% of FG cost; a
    // Manufacturing-Journal voucher type; and a posted manufacture of 20 units (with a ₹100 labour add-on).
    private static (Company Company, Guid FgId, Guid BomId, Guid MfgTypeId) SeedWithBom()
    {
        var c = CompanyFactory.CreateSeeded("BOM Persist Co", Fy);
        var inv = new InventoryService(c);

        var grp = inv.CreateStockGroup("Assemblies");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var main = c.MainLocation!;

        var fg = inv.CreateStockItem("Assembled Gadget", grp.Id, nos.Id);
        var compA = inv.CreateStockItem("Raw Part A", grp.Id, nos.Id);
        var compB = inv.CreateStockItem("Raw Part B", grp.Id, nos.Id);
        var scrap = inv.CreateStockItem("Metal Scrap", grp.Id, nos.Id);
        var co = inv.CreateStockItem("Side Product", grp.Id, nos.Id);
        co.StandardCost = Money.FromRupees(3m);

        // Enough component stock so the manufacture never goes negative.
        inv.AddOpeningBalance(compA.Id, main.Id, 1000m, Money.FromRupees(10m));
        inv.AddOpeningBalance(compB.Id, main.Id, 1000m, Money.FromRupees(5m));

        var lines = new[]
        {
            new BomLine(BomLineType.Component, compA.Id, quantityPerBlock: 2m, godownId: main.Id),
            new BomLine(BomLineType.Component, compB.Id, quantityPerBlock: 3m),
            new BomLine(BomLineType.Scrap, scrap.Id, quantityPerBlock: 1m, godownId: main.Id,
                rate: Money.FromRupees(2m)),
            new BomLine(BomLineType.CoProduct, co.Id, quantityPerBlock: 1m, percentOfFinishedGoodCost: 5m),
        };
        var bom = new BomService(c).CreateBom(fg.Id, "Standard", unitOfManufacture: 10m, lines);

        var mfg = new ManufacturingJournalService(c);
        var mfgType = mfg.CreateManufacturingJournalType("Manufacturing Journal");
        mfg.Manufacture(mfgType.Id, bom.Id, quantity: 20m, date: MfgDate,
            consumptionGodownId: main.Id, productionGodownId: main.Id,
            additionalCosts: new[] { new ManufacturingAdditionalCost("Labour", Money.FromRupees(100m)) });

        return (c, fg.Id, bom.Id, mfgType.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Bom_masters_manufacturing_journal_and_flags_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-bom-{Guid.NewGuid():N}.db");
        try
        {
            var (original, fgId, bomId, mfgTypeId) = SeedWithBom();

            // The finished good's stock value as manufactured, to reconcile after reload (paisa-exact).
            var originalFgValue = new StockValuationService(original).ClosingValue(fgId, new DateOnly(2024, 4, 30));

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                // Re-save (delete-then-insert upsert) must not trip a BOM/BOM-line foreign key.
                write.Save(original);
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // --- BOM header + finished-good Set-Components flag (RQ-9/RQ-10) ---
            var fg = reloaded.FindStockItem(fgId)!;
            Assert.True(fg.SetComponents);

            var bom = reloaded.FindBillOfMaterials(bomId)!;
            Assert.Equal("Standard", bom.Name);
            Assert.Equal(10m, bom.UnitOfManufacture);
            Assert.Equal(fgId, bom.StockItemId);
            Assert.Equal(4, bom.Lines.Count);

            // --- BOM lines: types, per-block quantities, godowns, and carve-out value bases (DP-3) ---
            var compA = reloaded.FindStockItemByName("Raw Part A")!;
            var aLine = bom.Lines.Single(l => l.ComponentStockItemId == compA.Id);
            Assert.Equal(BomLineType.Component, aLine.LineType);
            Assert.Equal(2m, aLine.QuantityPerBlock);
            Assert.Equal(reloaded.MainLocation!.Id, aLine.GodownId);
            Assert.Null(aLine.Rate);
            Assert.Null(aLine.PercentOfFinishedGoodCost);

            var compB = reloaded.FindStockItemByName("Raw Part B")!;
            var bLine = bom.Lines.Single(l => l.ComponentStockItemId == compB.Id);
            Assert.Equal(3m, bLine.QuantityPerBlock);
            Assert.Null(bLine.GodownId); // resolve-at-posting

            var scrap = reloaded.FindStockItemByName("Metal Scrap")!;
            var scrapLine = bom.Lines.Single(l => l.ComponentStockItemId == scrap.Id);
            Assert.Equal(BomLineType.Scrap, scrapLine.LineType);
            Assert.Equal(Money.FromRupees(2m), scrapLine.Rate);       // paisa-exact carve-out rate
            Assert.Null(scrapLine.PercentOfFinishedGoodCost);

            var co = reloaded.FindStockItemByName("Side Product")!;
            var coLine = bom.Lines.Single(l => l.ComponentStockItemId == co.Id);
            Assert.Equal(BomLineType.CoProduct, coLine.LineType);
            Assert.Null(coLine.Rate);
            Assert.Equal(5m, coLine.PercentOfFinishedGoodCost);      // percent basis (millis) round-trips

            // Line ORDER is preserved (the recipe order is load-bearing).
            Assert.Equal(BomLineType.Component, bom.Lines[0].LineType);
            Assert.Equal(compA.Id, bom.Lines[0].ComponentStockItemId);
            Assert.Equal(BomLineType.CoProduct, bom.Lines[3].LineType);

            // --- Manufacturing-Journal voucher type flag (RQ-11) ---
            var mfgType = reloaded.FindVoucherType(mfgTypeId)!;
            Assert.True(mfgType.UseAsManufacturingJournal);
            Assert.True(mfgType.IsManufacturingJournal);
            Assert.Equal(VoucherBaseType.StockJournal, mfgType.BaseType);

            // --- The posted Manufacturing Journal survived as a Stock-Journal inventory voucher ---
            var mfgVoucher = reloaded.InventoryVouchers.Single(v => v.TypeId == mfgTypeId);
            Assert.NotEmpty(mfgVoucher.Allocations);            // components consumed (source, outward)
            Assert.NotEmpty(mfgVoucher.DestinationAllocations); // FG + carve-outs produced (destination, inward)

            // --- The manufactured finished-good stock value reconciles to the paisa (PR-4) ---
            var reloadedFgValue = new StockValuationService(reloaded).ClosingValue(fgId, new DateOnly(2024, 4, 30));
            Assert.Equal(originalFgValue.Quantity, reloadedFgValue.Quantity);
            Assert.Equal(originalFgValue.Value, reloadedFgValue.Value);
            Assert.Equal(20m, reloadedFgValue.Quantity);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Multiple_boms_per_item_round_trip()
    {
        // RQ-9: a finished good may own several named BOMs — both must survive with their distinct lines.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-bom-multi-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Multi BOM Co", Fy);
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("G");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var fg = inv.CreateStockItem("Widget", grp.Id, nos.Id);
            var compA = inv.CreateStockItem("Part A", grp.Id, nos.Id);
            var compB = inv.CreateStockItem("Part B", grp.Id, nos.Id);

            var bomService = new BomService(c);
            bomService.CreateBom(fg.Id, "Standard", unitOfManufacture: 1m,
                new[] { new BomLine(BomLineType.Component, compA.Id, quantityPerBlock: 2m) });
            bomService.CreateBom(fg.Id, "Economy", unitOfManufacture: 1m,
                new[] { new BomLine(BomLineType.Component, compB.Id, quantityPerBlock: 3m) });

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            var rfg = reloaded.FindStockItemByName("Widget")!;
            var boms = reloaded.BomsFor(rfg.Id).OrderBy(b => b.Name).ToList();
            Assert.Equal(2, boms.Count);
            Assert.Equal("Economy", boms[0].Name);
            Assert.Equal("Standard", boms[1].Name);
            Assert.Equal(3m, boms[0].Lines.Single().QuantityPerBlock);
            Assert.Equal(2m, boms[1].Lines.Single().QuantityPerBlock);
            Assert.True(rfg.SetComponents);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Non_manufacturing_company_has_no_boms_and_default_flags()
    {
        // ER-13: a company with no BOM reloads with no BOMs, Set-Components off, and no Manufacturing-Journal type.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-nobom-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", Fy);
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("G");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
            inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 5m, Money.FromRupees(10m));

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            Assert.Empty(reloaded.BillsOfMaterials);
            Assert.False(reloaded.FindStockItemByName("Widget")!.SetComponents);
            Assert.DoesNotContain(reloaded.VoucherTypes, t => t.UseAsManufacturingJournal);
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
