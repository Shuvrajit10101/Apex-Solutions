using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census <b>5.7</b> wrong-figure residuals (wave 11, track hd, IV-334). Vendor (TallyPrime),
/// help.tallysolutions.com/create-optional-voucher/, opened by content 2026-11-19: Optional Vouchers <i>"record
/// tentative or future transactions without affecting your books of accounts"</i>, and when due you can <i>"make the
/// entry regular to include its accounting impact in the books of accounts."</i>
/// <list type="bullet">
///   <item><b>T2-170</b> — Item Cost Analysis's accounting item-invoice walk did not skip an Optional voucher, while its
///     pure-inventory walk did, so an Optional cost-tracked item invoice still moved Cost and Revenue.</item>
///   <item><b>T2-171</b> — the Material In / Out "Order No." picker offered an Optional Job Work order.</item>
/// </list>
/// Every figure is read off a book reloaded through a fresh <see cref="CompanyStorage"/>, first with the vouchers
/// Optional and then after they are regularised, so neither half can pass vacuously.
/// </summary>
public sealed class OptionalVoucherResidualTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OptionalVoucherResidualTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOptionalResidual_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Company Reload(string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    // ================================================================== T2-170

    [Fact]
    public void An_optional_cost_tracked_item_invoice_adds_no_cost_or_revenue_until_it_is_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Cost Co " + Guid.NewGuid().ToString("N")[..6], FyStart);
        c.EnableCostTracking = true;
        var inventory = new InventoryService(c);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
        var item = inventory.CreateStockItem("Widget", group.Id, unit.Id);
        var main = c.MainLocation!;

        DomainLedger L(string n, string g, bool dr)
        {
            var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id, Money.Zero, openingIsDebit: dr);
            c.AddLedger(l);
            return l;
        }

        var supplier = L("Supplier Ltd", "Sundry Creditors", false);
        var customer = L("Customer Ltd", "Sundry Debtors", true);
        var purchases = L("Purchases", "Purchase Accounts", true);
        var sales = L("Sales", "Sales Accounts", false);
        var svc = new LedgerService(c);
        var number = 0;

        Guid Bill(string type, DomainLedger nominal, DomainLedger party, StockDirection dir, decimal qty, decimal rate)
        {
            var value = Money.FromRupees(qty * rate);
            var inward = dir == StockDirection.Inward;
            var v = new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName(type)!.Id, new DateOnly(2025, 4, 9),
                new[]
                {
                    new EntryLine(nominal.Id, value, inward ? DrCr.Debit : DrCr.Credit),
                    new EntryLine(party.Id, value, inward ? DrCr.Credit : DrCr.Debit),
                },
                number: ++number, partyId: party.Id,
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(item.Id, main.Id, qty, Money.FromRupees(rate), dir,
                        costTrackingNumber: "LOT/1"),
                });
            svc.Post(v);
            return v.Id;
        }

        Bill("Purchase", purchases, supplier, StockDirection.Inward, 100m, 10.37m);   // cost 1,037.00
        Bill("Sales", sales, customer, StockDirection.Outward, 40m, 14.21m);          // revenue 568.40
        var optPurchase = Bill("Purchase", purchases, supplier, StockDirection.Inward, 7m, 55.55m);   // 388.85
        var optSale = Bill("Sales", sales, customer, StockDirection.Outward, 10m, 99.99m);            // 999.90
        svc.SetOptional(optPurchase, true);
        svc.SetOptional(optSale, true);
        _storage.Save(c);

        var optionalRow = Assert.Single(ItemCostAnalysis.BuildItems(Reload(c.Name), AsOf));
        Assert.Equal(1_037.00m, optionalRow.Cost.Amount);
        Assert.Equal(568.40m, optionalRow.Revenue.Amount);
        Assert.Equal(100m, optionalRow.InwardQuantity);
        Assert.Equal(40m, optionalRow.OutwardQuantity);

        // Regularised: both bills now count, which proves the fixture's Optional bills do reach this walk.
        svc.SetOptional(optPurchase, false);
        svc.SetOptional(optSale, false);
        _storage.Save(c);

        var liveRow = Assert.Single(ItemCostAnalysis.BuildItems(Reload(c.Name), AsOf));
        Assert.Equal(1_425.85m, liveRow.Cost.Amount);
        Assert.Equal(1_568.30m, liveRow.Revenue.Amount);
        Assert.Equal(107m, liveRow.InwardQuantity);
        Assert.Equal(50m, liveRow.OutwardQuantity);
    }

    // ================================================================== T2-171

    [Fact]
    public void The_material_order_picker_does_not_offer_an_optional_job_work_order_until_it_is_regularised()
    {
        var c = CompanyFactory.CreateSeeded("Optional Order Co " + Guid.NewGuid().ToString("N")[..6], FyStart);
        c.EnableJobOrderProcessing = true;
        var inventory = new InventoryService(c);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
        var item = inventory.CreateStockItem("Widget", group.Id, unit.Id);
        var jwType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
        var posting = new InventoryPostingService(c);

        InventoryVoucher Order(string no) => posting.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), jwType.Id, new DateOnly(2025, 4, 9),
            new JobWorkOrder(JobWorkDirection.Out, no, item.Id, 5m,
                new[] { new JobWorkOrderLine(item.Id, JobWorkComponentTrack.PendingToIssue, 5m) })));

        Order("JW-LIVE");
        var provisional = Order("JW-OPT");
        posting.SetOptional(provisional.Id, true);
        _storage.Save(c);

        Assert.Equal(new[] { "JW-LIVE" }, PickerOrderNumbers(Reload(c.Name)));

        posting.SetOptional(provisional.Id, false);
        _storage.Save(c);

        Assert.Equal(new[] { "JW-LIVE", "JW-OPT" }, PickerOrderNumbers(Reload(c.Name)).OrderBy(n => n).ToArray());
    }

    private string[] PickerOrderNumbers(Company reloaded)
    {
        var materialOut = reloaded.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialOut);
        var vm = new MaterialMovementEntryViewModel(reloaded, materialOut, _storage, () => { }, () => { });
        return vm.Orders.Where(o => !o.IsNone).Select(o => o.Order!.OrderNo).ToArray();
    }
}
