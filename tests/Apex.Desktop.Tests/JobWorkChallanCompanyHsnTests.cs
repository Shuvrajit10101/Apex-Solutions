using System;
using System.IO;
using System.Linq;
using System.Text;
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
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>IV-279 (wave 12, track ha, §2AB, IV-318) — THE JOB-WORK CHALLAN'S HSN, INHERITED FROM THE COMPANY RUNG.</b>
/// IV-279 recorded "The inherited case is not tested here"; wave 10 (IV-299) pinned the STOCK GROUP rung. This pins the
/// last rung a challan can reach: the item AND its stock group declare no HSN, and the company's GST details declare
/// 741220. A challan posts no value ledger, so the two ledger rungs are silent and the walk under
/// <c>SourceOfHsnSacDetails</c> reaches the company (vendor, opened by content 2026-11-22,
/// <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>: the order
/// "Ledger → Group → Stock Item → Stock Group → Company", and "The same explanation is true for even HSN/SAC
/// details"). Rule 55(d) asks the challan for the HSN (<c>cbic-gst.gov.in/gst-invoice-rules.html</c>).
///
/// <para>Keyed with the real Ctrl+A on the order and the Material Out, then re-read off the book re-opened through a
/// FRESH <see cref="CompanyStorage"/>; the assertion is on the emitted PDF's <c>Tj</c> operand.</para>
/// <para><b>Mutation H7</b> (the Company arm of <c>GstService.ResolveDocumentHsnSac</c> returns null) reddens the
/// first test at <c>(741220) Tj</c>; the second test is the control that the item's own HSN still wins.</para>
/// </summary>
public sealed class JobWorkChallanCompanyHsnTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexJwCompanyHsn_" + Guid.NewGuid().ToString("N"));
    private const string HomeGstin = "27AAPFU0939F1ZV";
    private const string WorkerGstin = "27AAACR5055K1Z5";
    private const string CompanyHsn = "741220";
    private const string ItemHsn = "741299";

    [AvaloniaFact]
    public void A_material_out_of_an_item_and_stock_group_with_no_hsn_prints_the_companys_hsn_on_the_reloaded_challan()
    {
        var s = KeyAndReprint("JW Company HSN Co", itemHsn: null);
        Assert.Contains($"({CompanyHsn}) Tj", s, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_items_own_hsn_still_wins_over_the_companys_on_the_reloaded_challan()
    {
        var s = KeyAndReprint("JW Item HSN Co", itemHsn: ItemHsn);
        Assert.Contains($"({ItemHsn}) Tj", s, StringComparison.Ordinal);
        Assert.DoesNotContain($"({CompanyHsn}) Tj", s, StringComparison.Ordinal);
    }

    private string KeyAndReprint(string name, string? itemHsn)
    {
        {
            var vm = new MainWindowViewModel(new CompanyStorage(_dir));
            vm.NewCompanyName = name;
            vm.CreateCompany();
            var c = vm.Company!;
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = HomeGstin, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            c.Gst!.DefaultGst = new MasterGstDetails { HsnSac = CompanyHsn };
            c.Address = "12 Dock Road\nMumbai";
            c.Pin = "400001";
            vm.ShowGstConfig();
            vm.GstConfig!.EnableJobOrderProcessing = true;
            vm.Back();

            var masters = new InventoryService(c);
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var raw = masters.CreateStockGroup("Raw Materials");                  // declares no HSN
            var flange = masters.CreateStockItem("Brass Flange", raw.Id, nos.Id);
            flange.Gst = new StockItemGstDetails
            { HsnSac = itemHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            masters.AddOpeningBalance(flange.Id, c.MainLocation!.Id, 100m, Money.FromRupees(33.33m));
            var fg = masters.CreateStockItem("Valve Body", masters.CreateStockGroup("Finished Goods").Id, nos.Id);
            var worker = new Godown(Guid.NewGuid(), "Platers Site", thirdParty: true);
            c.AddGodown(worker);
            var jobWorker = new Domain.Ledger(Guid.NewGuid(), "Platers", c.FindGroupByName("Sundry Creditors")!.Id,
                Money.Zero, openingIsDebit: false)
            {
                PartyGst = new PartyGstDetails
                { RegistrationType = GstRegistrationType.Regular, Gstin = WorkerGstin, StateCode = "27" },
                Mailing = new PartyMailingDetails { Address = "Plot 7, MIDC\nThane", Pincode = "400601" },
            };
            c.AddLedger(jobWorker);

            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            try
            {
                w.Show();
                vm.OpenJobWorkOrder(JobWorkDirection.Out);
                var e = vm.JobWorkOrderEntry!;
                e.FinishedGood = e.StockItems.Single(i => i.Id == fg.Id);
                e.FinishedGoodQtyText = "10";
                e.OrderNo = "JW/41";
                var l0 = e.Lines[0];
                l0.SelectedItem = l0.StockItems.Single(i => i.Id == flange.Id);
                l0.SelectedGodown = l0.Godowns.Single(g => g.Id == c.MainLocation!.Id);
                l0.QuantityText = "20";
                Pump(w);
                Assert.True(e.CanAccept, e.Message);
                w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                Pump(w);

                vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
                var mo = vm.MaterialMovementEntry!;
                mo.SourceGodown = mo.Godowns.Single(g => g.Id == c.MainLocation!.Id);
                mo.SelectedOrder = mo.Orders.Single(o => o.Order?.OrderNo == "JW/41");
                mo.DestinationGodown = mo.Godowns.Single(g => g.Id == worker.Id);
                mo.SelectedParty = mo.Parties.Single(p => p.Ledger?.Id == jobWorker.Id);
                Pump(w);
                Assert.True(mo.CanAccept, mo.Message);
                var before = c.InventoryVouchers.Count;
                w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                Pump(w);
                Assert.True(c.InventoryVouchers.Count == before + 1, mo.Message);
            }
            finally { w.Close(); }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        var book = fresh.Load(fresh.ListCompanies().Single(x => x.Name == name));
        var flangeBack = book.StockItems.Single(i => i.Name == "Brass Flange");
        Assert.Equal(itemHsn, flangeBack.Gst!.HsnSac);                               // the premise survived
        Assert.Null(book.StockGroups.Single(g => g.Id == flangeBack.StockGroupId).Gst);
        Assert.Equal(CompanyHsn, book.Gst!.DefaultGst!.HsnSac);
        var outType = book.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialOut && t.IsActive);
        var movement = book.InventoryVouchers.Single(v => v.TypeId == outType.Id);
        var projection = DeliveryChallanPrintProjector.Project(book, movement);
        Assert.Null(projection.Refusal);
        var s = Encoding.Latin1.GetString(
            Apex.Ledger.Io.ReportPdf.Render(projection.Document!, new Apex.Ledger.Io.PageConfig()));
        Assert.Contains(JobWorkChallanPrintProjector.Title, s, StringComparison.Ordinal);
        return s;
    }

    private static void Pump(MainWindow w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
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
