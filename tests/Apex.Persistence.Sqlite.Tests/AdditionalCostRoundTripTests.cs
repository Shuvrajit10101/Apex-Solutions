using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Additional-Cost-of-Purchase persistence contract (Phase 6 slice 3; RQ-16..RQ-20, ER-1, ER-13): a company with a
/// Purchase voucher type flagged <see cref="VoucherType.TrackAdditionalCosts"/>, two additional-cost Direct-Expenses
/// ledgers (one <see cref="MethodOfAppropriation.ByQuantity"/>, one <see cref="MethodOfAppropriation.ByValue"/>),
/// and a Stock-Journal transfer carrying <see cref="InventoryVoucher.AdditionalCostLines"/> SAVES and RELOADS
/// count-exact and paisa-exact. A plain company keeps its flags off and its methods NULL (ER-13).
/// </summary>
public sealed class AdditionalCostRoundTripTests
{
    private static readonly DateOnly Fy = new(2024, 4, 1);
    private static readonly DateOnly TransferDate = new(2024, 4, 10);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Track_flag_ledger_methods_and_stock_journal_additional_cost_lines_survive_save_reload()
    {
        var dbPath = TempDbFile.NewPath("apex-addlcost");
        try
        {
            var c = CompanyFactory.CreateSeeded("Additional Cost Persist Co", Fy);
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("Goods");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
            var main = c.MainLocation!.Id;
            var dest = inv.CreateGodown("Godown 2").Id;
            inv.AddOpeningBalance(item.Id, main, 10m, Money.FromRupees(100m));

            // Purchase voucher type tracks additional costs (RQ-16).
            var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
            purchaseType.TrackAdditionalCosts = true;

            // Two additional-cost Direct-Expenses ledgers — one per method (RQ-16).
            var deGrp = c.FindGroupByName("Direct Expenses")!;
            var freightByQty = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Freight (by qty)", deGrp.Id, Money.Zero,
                openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByQuantity);
            var packingByValue = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Packing (by value)", deGrp.Id, Money.Zero,
                openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByValue);
            c.AddLedger(freightByQty);
            c.AddLedger(packingByValue);

            // A Stock-Journal transfer carrying two additional-cost lines (RQ-20).
            var sjType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal).Id;
            var transferId = Guid.NewGuid();
            c.AddInventoryVoucher(InventoryVoucher.StockJournal(transferId, sjType, TransferDate,
                source: new[] { new InventoryAllocation(item.Id, main, 10m, StockDirection.Outward, Money.FromRupees(100m)) },
                destination: new[] { new InventoryAllocation(item.Id, dest, 10m, StockDirection.Inward, Money.FromRupees(100m)) },
                additionalCostLines: new[]
                {
                    new AdditionalCostLine(freightByQty.Id, Money.FromRupees(200m)),
                    new AdditionalCostLine(packingByValue.Id, Money.FromRupees(55.55m)),
                }));

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(c);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                write.Save(c); // re-save (delete-then-insert) must not trip an FK on additional_cost_lines
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(c.Id)!;

            // --- Voucher-type track flag (RQ-16) ---
            Assert.True(reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).TrackAdditionalCosts);

            // --- Ledger methods (both values) round-trip; a plain ledger stays NULL (ER-13) ---
            var rFreight = reloaded.FindLedgerByName("Freight (by qty)")!;
            var rPacking = reloaded.FindLedgerByName("Packing (by value)")!;
            Assert.Equal(MethodOfAppropriation.ByQuantity, rFreight.MethodOfAppropriation);
            Assert.True(rFreight.IsAdditionalCostLedger);
            Assert.Equal(MethodOfAppropriation.ByValue, rPacking.MethodOfAppropriation);
            Assert.Null(reloaded.FindLedgerByName("Cash")!.MethodOfAppropriation);

            // --- Stock-Journal additional-cost lines (RQ-20): count-exact + paisa-exact + order preserved ---
            var transfer = reloaded.FindInventoryVoucher(transferId)!;
            Assert.Equal(2, transfer.AdditionalCostLines.Count);
            Assert.Equal(rFreight.Id, transfer.AdditionalCostLines[0].LedgerId);
            Assert.Equal(Money.FromRupees(200m), transfer.AdditionalCostLines[0].Amount);
            Assert.Equal(rPacking.Id, transfer.AdditionalCostLines[1].LedgerId);
            Assert.Equal(Money.FromRupees(55.55m), transfer.AdditionalCostLines[1].Amount);

            // --- The transfer's landed destination value reconciles to the paisa after reload (PR-5) ---
            var originalVal = new StockValuationService(c).ClosingValue(item.Id, new DateOnly(2024, 4, 30));
            var reloadedVal = new StockValuationService(reloaded).ClosingValue(item.Id, new DateOnly(2024, 4, 30));
            Assert.Equal(originalVal.Value, reloadedVal.Value);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_plain_company_reloads_with_the_flag_off_no_method_and_no_additional_cost_lines()
    {
        var dbPath = TempDbFile.NewPath("apex-addlcost-plain");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", Fy);
            var inv = new InventoryService(c);
            var grp = inv.CreateStockGroup("Goods");
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            inv.CreateStockItem("Widget", grp.Id, nos.Id);

            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);

            using var read = new SqliteCompanyStore(dbPath);
            var reloaded = read.Load(c.Id)!;
            Assert.DoesNotContain(reloaded.VoucherTypes, t => t.TrackAdditionalCosts);
            Assert.DoesNotContain(reloaded.Ledgers, l => l.IsAdditionalCostLedger);
            Assert.All(reloaded.InventoryVouchers, v => Assert.Empty(v.AdditionalCostLines));
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
