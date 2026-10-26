using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11, WAVE 8 (IV-238) — <i>Print voucher after saving</i> on the JOB WORK ORDER and MATERIAL IN /
/// MATERIAL OUT creation doors</b>, which the wave-7 census recorded as IGNORING the flag.
/// help.tallysolutions.com/voucher-types-tally/, opened by content 2026-10-26: <i>"Enable Print voucher after saving
/// to automatically open the Voucher Printing screen for the voucher, once it is saved."</i> — no base-type
/// restriction is stated.
///
/// <para>⚠️ <b>What these doors now do, stated exactly.</b> They route the voucher the screen just committed through
/// the SAME inventory printing door the Delivery Note uses (<c>OpenInventoryVoucherPrintingAfterSave</c>). This
/// product prints no document for a job work order or a material movement (IV-161: no TallyPrime page opened says
/// what they print — help.tallysolutions.com/principal-manufacturer/ and /job-worker/ were opened by content and
/// mention no printing), so the flag raises that door's NAMED notice rather than being silently ignored. Each door
/// has a flag-off CONTROL so the notice cannot pass by being raised on every save.</para>
/// </summary>
public sealed class PrintAfterSavingJobWorkDoorsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexPasJw_" + Guid.NewGuid().ToString("N"));

    private static void Pump(MainWindow w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed record Kit(MainWindowViewModel Vm, StockItem CompA, StockItem Fg, Godown Main, Godown Worker);

    private Kit Seed(string name)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        vm.GstConfig!.EnableJobOrderProcessing = true;   // the F11 feature, through its own screen
        vm.Back();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var unit = masters.CreateSimpleUnit("Nos", "Numbers");
        var compA = masters.CreateStockItem("CompA", masters.CreateStockGroup("Raw Materials").Id, unit.Id);
        masters.AddOpeningBalance(compA.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5013.37m));
        var fg = masters.CreateStockItem("PC", masters.CreateStockGroup("Finished Goods").Id, unit.Id);
        var worker = new Godown(Guid.NewGuid(), "Worker Site", thirdParty: true);
        c.AddGodown(worker);
        return new Kit(vm, compA, fg, c.MainLocation!, worker);
    }

    private static VoucherType TypeOf(Kit k, VoucherBaseType b) =>
        k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == b && t.IsActive);

    private Company Reload(string name)
    {
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Keys a Job Work Out Order (10 PC, 20 CompA) and accepts it with the REAL Ctrl+A.</summary>
    private static JobWorkOrderEntryViewModel KeyOutOrder(MainWindow w, Kit k)
    {
        k.Vm.OpenJobWorkOrder(JobWorkDirection.Out);
        var e = k.Vm.JobWorkOrderEntry!;
        Assert.Equal(Screen.JobWorkOrderEntry, k.Vm.CurrentScreen);
        e.FinishedGood = e.StockItems.Single(i => i.Id == k.Fg.Id);
        e.FinishedGoodQtyText = "10";
        e.OrderNo = "DKP/789";
        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == k.CompA.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == k.Main.Id);
        l0.QuantityText = "20";
        Pump(w);
        Assert.True(e.CanAccept, e.Message);
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
        return e;
    }

    /// <summary>
    /// 🔴 A Job Work Out Order saved with the flag on is routed to the printing door FOR THE VOUCHER IT SAVED: the
    /// screen's <c>SavedVoucherId</c> is the order on the RELOADED book, and the door's named notice is raised (this
    /// product prints no job-work document — IV-161). <b>Mutations:</b> (J1) deleting the
    /// <c>OpenInventoryVoucherPrintingAfterSave(saved)</c> call in <c>OpenJobWorkOrder</c> reddens the notice
    /// assertion; (J2) not writing <c>SavedVoucherId</c> in <c>JobWorkOrderEntryViewModel.Accept</c> reddens the id
    /// assertion.
    /// </summary>
    [AvaloniaFact]
    public void A_job_work_order_with_the_flag_is_routed_to_the_printing_door_for_that_order()
    {
        var k = Seed("PAS JW Co");
        TypeOf(k, VoucherBaseType.JobWorkOutOrder).PrintAfterSaving = true;
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var e = KeyOutOrder(w, k);
            var order = Assert.Single(Reload("PAS JW Co").InventoryVouchers);
            Assert.Equal(order.Id, e.SavedVoucherId);
            Assert.Equal(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            Assert.Null(k.Vm.PrintPreview);
        }
        finally { w.Close(); }
    }

    /// <summary>THE CONTROL for the job-work door: flag off, the order saves and nothing is said.
    /// <b>Mutation (J3):</b> dropping <c>type.PrintAfterSaving &amp;&amp;</c> from the door's condition reddens this.</summary>
    [AvaloniaFact]
    public void A_job_work_order_without_the_flag_raises_nothing()
    {
        var k = Seed("PAS JW Ctl Co");
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            KeyOutOrder(w, k);
            Assert.Single(Reload("PAS JW Ctl Co").InventoryVouchers);
            Assert.NotEqual(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            Assert.Null(k.Vm.PrintPreview);
        }
        finally { w.Close(); }
    }

    /// <summary>Keys a Material Out transfer Main → Worker Site against the order and accepts it.</summary>
    private static MaterialMovementEntryViewModel KeyMaterialOut(MainWindow w, Kit k)
    {
        k.Vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var mo = k.Vm.MaterialMovementEntry!;
        Assert.Equal(Screen.MaterialMovementEntry, k.Vm.CurrentScreen);
        mo.SourceGodown = mo.Godowns.Single(g => g.Id == k.Main.Id);
        mo.SelectedOrder = mo.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
        mo.DestinationGodown = mo.Godowns.Single(g => g.Id == k.Worker.Id);
        Pump(w);
        Assert.True(mo.CanAccept, mo.Message);
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
        return mo;
    }

    /// <summary>
    /// 🔴 Material Out with the flag on is routed to the printing door for the movement it saved.
    /// <b>Mutations:</b> (J4) deleting the call in <c>OpenMaterialMovement</c> reddens the notice; (J5) not writing
    /// <c>SavedVoucherId</c> in <c>MaterialMovementEntryViewModel.Accept</c> reddens the id assertion.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Material_out_reads_the_flag(bool flag)
    {
        var k = Seed("PAS MO Co " + flag);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            KeyOutOrder(w, k);                                     // the order (its own type's flag is off)
            Assert.NotEqual(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            TypeOf(k, VoucherBaseType.MaterialOut).PrintAfterSaving = flag;
            var mo = KeyMaterialOut(w, k);

            var book = Reload("PAS MO Co " + flag);
            var outType = book.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialOut && t.IsActive);
            var movement = book.InventoryVouchers.Single(v => v.TypeId == outType.Id);
            Assert.Equal(movement.Id, mo.SavedVoucherId);
            if (flag) Assert.Equal(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            else Assert.NotEqual(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            Assert.Null(k.Vm.PrintPreview);
        }
        finally { w.Close(); }
    }

    /// <summary>Material In (the consuming receipt of finished goods) reads the flag through the same door.</summary>
    [AvaloniaFact]
    public void Material_in_with_the_flag_is_routed_to_the_printing_door()
    {
        var k = Seed("PAS MI Co");
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            KeyOutOrder(w, k);
            KeyMaterialOut(w, k);
            TypeOf(k, VoucherBaseType.MaterialIn).PrintAfterSaving = true;

            k.Vm.OpenMaterialMovement(VoucherBaseType.MaterialIn);
            var mi = k.Vm.MaterialMovementEntry!;
            mi.DestinationGodown = mi.Godowns.Single(g => g.Id == k.Main.Id);
            mi.SourceGodown = mi.Godowns.Single(g => g.Id == k.Worker.Id);
            mi.SelectedOrder = mi.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
            Pump(w);
            Assert.True(mi.CanAccept, mi.Message);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            var book = Reload("PAS MI Co");
            var inType = book.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialIn && t.IsActive);
            Assert.Equal(book.InventoryVouchers.Single(v => v.TypeId == inType.Id).Id, mi.SavedVoucherId);
            Assert.Equal(DeliveryChallanPrintProjector.NotAChallan, k.Vm.Notice);
            Assert.Null(k.Vm.PrintPreview);
        }
        finally { w.Close(); }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
