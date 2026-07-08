using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Job Work persistence contract (Phase 6 slice 8; ER-1, RQ-45..RQ-49). A company with Job Order Processing
/// enabled, a Job Work Out Order (finished good + tracked component lines), a Material Out balanced transfer to a
/// third-party godown, and a Material In consumption linked back to the order — plus the three new flags — SAVES
/// and RELOADS at <see cref="Schema.CurrentVersion"/> preserving every field exactly, and the derived on-hand +
/// finished-good valuation survive the round-trip.
/// </summary>
public sealed class JobWorkRoundTripTests
{
    private static readonly DateOnly Open = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 15);
    private static readonly DateOnly D3 = new(2024, 4, 20);

    private static Guid TypeId(Company c, VoucherBaseType bt) => c.VoucherTypes.First(t => t.BaseType == bt).Id;

    private sealed record Seed(Company Company, Guid OrderId, Guid CompAId, Guid CompBId, Guid FgId,
        Guid MainId, Guid WorkerId);

    private static Seed BuildCompany()
    {
        var c = CompanyFactory.CreateSeeded("JW RoundTrip Co", Open);
        new JobWorkService(c).SetEnabled(true);

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var main = c.MainLocation!.Id;
        var worker = masters.CreateGodown("Worker Site", thirdParty: true);

        var a = masters.CreateStockItem("Comp A", grp.Id, nos.Id);
        var b = masters.CreateStockItem("Comp B", grp.Id, nos.Id);
        masters.AddOpeningBalance(a.Id, main, 10m, Money.FromRupees(30m));
        masters.AddOpeningBalance(b.Id, main, 10m, Money.FromRupees(20m));
        var fg = masters.CreateStockItem("Widget", grp.Id, nos.Id);

        var posting = new InventoryPostingService(c);

        var order = posting.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.JobWorkOutOrder), D1,
            new JobWorkOrder(JobWorkDirection.Out, "DKP/789", fg.Id, 10m,
                lines: new[]
                {
                    new JobWorkOrderLine(a.Id, JobWorkComponentTrack.PendingToIssue, 10m, godownId: main,
                        dueDate: D3, rate: Money.FromRupees(30m)),
                    new JobWorkOrderLine(b.Id, JobWorkComponentTrack.PendingToIssue, 10m, godownId: main,
                        rate: Money.FromRupees(20m)),
                },
                finishedGoodRate: Money.FromRupees(50m), finishedGoodGodownId: main,
                finishedGoodDueDate: D3, durationOfProcess: "30 days", natureOfProcessing: "Assembly"),
            narration: "Out order"));

        // Material Out transfer Main → Worker Site (balanced).
        posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.MaterialOut), D2,
            source: new[]
            {
                new InventoryAllocation(a.Id, main, 10m, StockDirection.Outward, Money.FromRupees(30m)),
                new InventoryAllocation(b.Id, main, 10m, StockDirection.Outward, Money.FromRupees(20m)),
            },
            destination: new[]
            {
                new InventoryAllocation(a.Id, worker.Id, 10m, StockDirection.Inward, Money.FromRupees(30m)),
                new InventoryAllocation(b.Id, worker.Id, 10m, StockDirection.Inward, Money.FromRupees(20m)),
            },
            orderLinks: new[] { order.Id }, narration: "Dispatch"));

        // Material In consumption (transform): consume the two components at Worker Site, produce 10 FG at Main.
        posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.MaterialIn), D3,
            source: new[]
            {
                new InventoryAllocation(a.Id, worker.Id, 10m, StockDirection.Outward, Money.FromRupees(30m)),
                new InventoryAllocation(b.Id, worker.Id, 10m, StockDirection.Outward, Money.FromRupees(20m)),
            },
            destination: new[] { new InventoryAllocation(fg.Id, main, 10m, StockDirection.Inward, Money.FromRupees(50m)) },
            orderLinks: new[] { order.Id }, narration: "Receive"));

        return new Seed(c, order.Id, a.Id, b.Id, fg.Id, main, worker.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Job_work_data_survives_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-jw-rt-{Guid.NewGuid():N}.db");
        try
        {
            var seed = BuildCompany();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(seed.Company);
                write.Save(seed.Company); // re-save (upsert) must not trip an FK
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(seed.Company.Id)!;

            // Company + voucher-type flags round-tripped.
            Assert.True(reloaded.EnableJobOrderProcessing);
            var matIn = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialIn);
            var matOut = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialOut);
            Assert.True(matIn.IsActive);
            Assert.True(matIn.UseForJobWork);
            Assert.True(matIn.AllowConsumption);
            Assert.True(matOut.UseForJobWork);
            Assert.False(matOut.AllowConsumption);

            // Three inventory vouchers (order + material out + material in).
            Assert.Equal(3, reloaded.InventoryVouchers.Count);

            // The Job Work Order payload preserved.
            var order = reloaded.FindInventoryVoucher(seed.OrderId)!;
            var jwo = order.JobWorkOrder!;
            Assert.Equal(JobWorkDirection.Out, jwo.Direction);
            Assert.Equal("DKP/789", jwo.OrderNo);
            Assert.Equal("30 days", jwo.DurationOfProcess);
            Assert.Equal("Assembly", jwo.NatureOfProcessing);
            Assert.Equal(seed.FgId, jwo.FinishedGoodStockItemId);
            Assert.Equal(10m, jwo.FinishedGoodQuantity);
            Assert.Equal(Money.FromRupees(50m), jwo.FinishedGoodRate);
            Assert.Equal(seed.MainId, jwo.FinishedGoodGodownId);
            Assert.Equal(D3, jwo.FinishedGoodDueDate);
            Assert.Equal(2, jwo.Lines.Count);
            var l0 = jwo.Lines[0];
            Assert.Equal(seed.CompAId, l0.ComponentStockItemId);
            Assert.Equal(JobWorkComponentTrack.PendingToIssue, l0.Track);
            Assert.Equal(10m, l0.Quantity);
            Assert.Equal(Money.FromRupees(30m), l0.Rate);
            Assert.Equal(D3, l0.DueDate);
            Assert.Equal(seed.MainId, l0.GodownId);

            // The Material In links back to the order.
            var matInVoucher = reloaded.InventoryVouchers.Single(v =>
                reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.MaterialIn);
            Assert.Equal(seed.OrderId, Assert.Single(matInVoucher.OrderLinks));

            // Derived state survives: no phantom RM, FG on-hand + value reconcile.
            var ledger = new InventoryLedger(reloaded);
            Assert.Equal(0m, ledger.OnHand(seed.CompAId, seed.WorkerId, D3));
            Assert.Equal(0m, ledger.OnHand(seed.CompBId, seed.WorkerId, D3));
            Assert.Equal(10m, ledger.OnHand(seed.FgId, seed.MainId, D3));
            var closing = new StockValuationService(reloaded).ClosingValue(seed.FgId, D3);
            Assert.Equal(10m, closing.Quantity);
            Assert.Equal(Money.FromRupees(500m), closing.Value); // 10×30 + 10×20 = 500
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
