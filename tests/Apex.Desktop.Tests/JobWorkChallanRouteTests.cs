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
/// 🔴 <b>CENSUS 5.11 (wave 9, IV-260) — THE JOB-WORK DELIVERY CHALLAN, REACHED BY A USER AND READ OFF THE EMITTED PDF.</b>
/// Before this wave a Material Out with <i>Print voucher after saving</i> on raised a notice saying the product did
/// not print the challan CBIC Circular 38/12/2018 para 8.4(i) requires ("in terms of rules 45 and 55"). These tests
/// key the movement through the REAL Ctrl+A, then REOPEN the book from disk in a fresh shell, drill it from the Day
/// Book with Enter, and press the real Ctrl+P and W.
///
/// <para><b>Every particular of the rule is asserted on a Tj operand</b> (cbic-gst.gov.in/gst-invoice-rules.html,
/// "Transportation of goods without issue of invoice" (a)-(i)): number and date, consigner name / address / GSTIN,
/// consignee name / address / GSTIN, HSN, description, quantity, taxable value, tax rate and amount, place of supply,
/// signature.</para>
///
/// <para><b>Figures worked BY HAND.</b> Opening stock 100 Nos of Brass Flange at ₹33.33 (₹3,333.00), so the issue
/// rate is ₹33.33. The order sends 20 Nos ⇒ taxable value 20 × 33.33 = <b>₹666.60</b>. At 18%, 666.60 × 0.18 = 119.988 ⇒
/// <b>₹119.99</b>; intra-State (27 → 27) the house split (<c>GstService.ComputeLineTax</c>) gives CGST =
/// round(59.995) = <b>60.00</b> and SGST = 119.99 − 60.00 = <b>59.99</b>.</para>
/// </summary>
public sealed class JobWorkChallanRouteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexJwChallan_" + Guid.NewGuid().ToString("N"));

    private const string HomeGstin = "27AAPFU0939F1ZV";
    private const string WorkerGstin = "27AAACR5055K1Z5";
    private const string WorkerName = "Precision Platers & Anodisers Private Limited";

    private static void Pump(MainWindow w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private sealed record Kit(MainWindowViewModel Vm, StockItem Flange, StockItem Fg, Godown Main, Godown Worker,
        Domain.Ledger JobWorker);

    private Kit Seed(string name, string workerState = "27", string workerGstin = WorkerGstin)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = HomeGstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.Address = "12 Dock Road\nMumbai";
        c.Pin = "400001";
        vm.ShowGstConfig();
        vm.GstConfig!.EnableJobOrderProcessing = true;   // the F11 feature, through its own screen
        vm.Back();

        var masters = new InventoryService(c);
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var flange = masters.CreateStockItem("Brass Flange", masters.CreateStockGroup("Raw Materials").Id, nos.Id);
        flange.Gst = new StockItemGstDetails { HsnSac = "741220", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        masters.AddOpeningBalance(flange.Id, c.MainLocation!.Id, 100m, Money.FromRupees(33.33m));   // rate per Nos
        var fg = masters.CreateStockItem("Valve Body", masters.CreateStockGroup("Finished Goods").Id, nos.Id);
        var worker = new Godown(Guid.NewGuid(), "Platers Site", thirdParty: true);
        c.AddGodown(worker);

        var jobWorker = new Domain.Ledger(Guid.NewGuid(), WorkerName, c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = workerGstin, StateCode = workerState },
            Mailing = new PartyMailingDetails { Address = "Plot 7, MIDC\nThane", Pincode = "400601" },
        };
        c.AddLedger(jobWorker);
        return new Kit(vm, flange, fg, c.MainLocation!, worker, jobWorker);
    }

    private static VoucherType TypeOf(Kit k, VoucherBaseType b) =>
        k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == b && t.IsActive);

    /// <summary>Keys a Job Work Out Order (10 Valve Body, 20 Brass Flange) and accepts it with the real Ctrl+A.</summary>
    private static void KeyOutOrder(MainWindow w, Kit k)
    {
        k.Vm.OpenJobWorkOrder(JobWorkDirection.Out);
        var e = k.Vm.JobWorkOrderEntry!;
        e.FinishedGood = e.StockItems.Single(i => i.Id == k.Fg.Id);
        e.FinishedGoodQtyText = "10";
        e.OrderNo = "JW/41";
        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == k.Flange.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == k.Main.Id);
        l0.QuantityText = "20";
        Pump(w);
        Assert.True(e.CanAccept, e.Message);
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
    }

    /// <summary>Keys the Material Out Main → Platers Site against the order, the job worker as party, Ctrl+A.</summary>
    private static void KeyMaterialOut(MainWindow w, Kit k, bool withParty = true)
    {
        k.Vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var mo = k.Vm.MaterialMovementEntry!;
        mo.SourceGodown = mo.Godowns.Single(g => g.Id == k.Main.Id);
        mo.SelectedOrder = mo.Orders.Single(o => o.Order?.OrderNo == "JW/41");
        mo.DestinationGodown = mo.Godowns.Single(g => g.Id == k.Worker.Id);
        if (withParty) mo.SelectedParty = mo.Parties.Single(p => p.Ledger?.Id == k.JobWorker.Id);
        Pump(w);
        Assert.True(mo.CanAccept, mo.Message);
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
    }

    /// <summary>Every Rule 55 particular, read off the emitted Tj operands. Parentheses inside a PDF string are
    /// escaped, so "Maharashtra (27)" is drawn as <c>Maharashtra \(27\)</c>.</summary>
    private static void AssertTheRule55Particulars(string s, string number)
    {
        Assert.Contains(JobWorkChallanPrintProjector.Title, s, StringComparison.Ordinal);
        Assert.Contains("(Challan No. " + number + "   Date ", s, StringComparison.Ordinal);                 // (a)
        Assert.Contains("(Consigner \\(Principal\\): ", s, StringComparison.Ordinal);                         // (b)
        Assert.Contains("(12 Dock Road) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(PIN: 400001) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(GSTIN: " + HomeGstin + ") Tj", s, StringComparison.Ordinal);
        Assert.Contains("(Consignee \\(Job Worker\\): " + WorkerName + ") Tj", s, StringComparison.Ordinal);  // (c)
        Assert.Contains("(Plot 7, MIDC) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(PIN: 400601) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(GSTIN/UIN: " + WorkerGstin + ") Tj", s, StringComparison.Ordinal);
        Assert.Contains("(1. Brass Flange) Tj", s, StringComparison.Ordinal);                                 // (d)
        Assert.Contains("(741220) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(20 Nos) Tj", s, StringComparison.Ordinal);                                          // (e)
        Assert.Contains("(666.60) Tj", s, StringComparison.Ordinal);                                          // (f)
        Assert.Contains("(18%) Tj", s, StringComparison.Ordinal);                                             // (g)
        Assert.Contains("(119.99) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(Central tax: 60.00   State tax: 59.99) Tj", s, StringComparison.Ordinal);
        Assert.Contains("(Place of Supply: Maharashtra \\(27\\)) Tj", s, StringComparison.Ordinal);           // (h)
        Assert.Contains("(Signature of the principal ____________________) Tj", s, StringComparison.Ordinal); // (i)
        Assert.Contains("(" + JobWorkChallanPrintProjector.WithoutPaymentOfTax + ") Tj", s, StringComparison.Ordinal);
        // NOT the Delivery-Note layout: none of its columns or its consignee acknowledgement.
        Assert.DoesNotContain("Tracking No.", s, StringComparison.Ordinal);
        Assert.DoesNotContain("(Godown) Tj", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Received the above goods", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE ROW'S OPEN CLAUSE, CLOSED: Material Out with Print-after-saving on OPENS THE CHALLAN.</b> Keyed with
    /// the real Ctrl+A; the preview that opens is the job-work challan for the movement just saved, and every Rule 55
    /// particular is on its bytes.
    /// <para><b>Mutation (M1):</b> restoring the wave-8 refusal in <c>DeliveryChallanPrintProjector.Project</c>
    /// (returning <c>new DeliveryChallanProjection(null, NotAChallan)</c> for a Material Out instead of routing it to
    /// <c>JobWorkChallanPrintProjector</c>) reddens this at <c>Assert.NotNull(preview)</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Material_out_with_print_after_saving_opens_the_job_work_challan()
    {
        var k = Seed("JW Challan PAS Co");
        TypeOf(k, VoucherBaseType.MaterialOut).PrintAfterSaving = true;
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            KeyOutOrder(w, k);
            KeyMaterialOut(w, k);

            var preview = k.Vm.PrintPreview;
            Assert.NotNull(preview);
            Assert.Equal(Screen.PrintPreview, k.Vm.CurrentScreen);
            Assert.Equal(JobWorkChallanPrintProjector.Title, preview!.ReportTitle);
            var c = k.Vm.Company!;
            var movement = c.InventoryVouchers.Single(v => v.TypeId == TypeOf(k, VoucherBaseType.MaterialOut).Id);
            AssertTheRule55Particulars(Encoding.Latin1.GetString(preview.PdfBytes), c.FormatVoucherNumber(movement));
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE RELOADED BOOK, THE DRILL, Ctrl+P AND W.</b> The movement is keyed in one shell; a SECOND shell over a
    /// fresh <see cref="CompanyStorage"/> opens the book from disk through Company Select, drills the Material Out from
    /// the Day Book with Enter, and presses Ctrl+P: the preview is the challan, with every particular. Then Escape and
    /// W: the WhatsApp share document saved to disk is the same challan.
    /// <para><b>Mutations:</b> (M1) as above reddens this at the preview assertion; (M2) dropping the job worker's
    /// GSTIN from the consignee block (<c>"GSTIN/UIN: " + GstinText(null)</c>) reddens it at the consignee GSTIN
    /// Tj; (M3) computing the tax with <c>interState: true</c> regardless of routing reddens it at the
    /// <c>Central tax</c> Tj.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_reloaded_material_out_prints_and_shares_its_challan_from_the_drill()
    {
        const string name = "JW Challan Drill Co";
        {
            var k = Seed(name);
            var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
            try
            {
                w.Show();
                KeyOutOrder(w, k);
                KeyMaterialOut(w, k);
                Assert.Null(k.Vm.PrintPreview);     // the type's flag is off: saving alone opens nothing
            }
            finally { w.Close(); }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        try
        {
            window.Show();
            vm.ShowCompanySelect();
            vm.Menu.Single(m => m.Label == name).Activate();
            Pump(window);
            var c = vm.Company!;
            var outType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialOut && t.IsActive);
            var movement = c.InventoryVouchers.Single(v => v.TypeId == outType.Id);
            var number = c.FormatVoucherNumber(movement);

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            var row = vm.Reports!.Rows.SingleOrDefault(r => r.DrillInventoryVoucherId == movement.Id);
            Assert.NotNull(row);
            vm.Reports!.SelectedRow = row;
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            var preview = vm.PrintPreview;
            Assert.NotNull(preview);
            Assert.Equal(JobWorkChallanPrintProjector.Title, preview!.ReportTitle);
            AssertTheRule55Particulars(Encoding.Latin1.GetString(preview.PdfBytes), number);
            Assert.DoesNotContain("Day Book", Encoding.Latin1.GetString(preview.PdfBytes), StringComparison.Ordinal);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Directory.CreateDirectory(_dir);
            var docPath = Path.Combine(_dir, "jw-share.pdf");
            Assert.True(vm.SaveWhatsAppDocument(docPath));
            AssertTheRule55Particulars(Encoding.Latin1.GetString(File.ReadAllBytes(docPath)), number);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// A Material Out that names no job worker (neither its own party nor its order's) is REFUSED BY NAME rather than
    /// addressed to nobody, and opens no preview. <b>Mutation (M4):</b> deleting the <c>Consignee(...) is not</c>
    /// refusal in <c>JobWorkChallanPrintProjector.Project</c> (run as: falling back to the company's first ledger)
    /// opens a preview and reddens this.
    /// </summary>
    [AvaloniaFact]
    public void A_material_out_naming_no_job_worker_is_refused_by_name()
    {
        var k = Seed("JW Challan No Party Co");
        TypeOf(k, VoucherBaseType.MaterialOut).PrintAfterSaving = true;
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            KeyOutOrder(w, k);
            KeyMaterialOut(w, k, withParty: false);
            Assert.Null(k.Vm.PrintPreview);
            Assert.Equal(JobWorkChallanPrintProjector.NoJobWorker, k.Vm.Notice);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Rule 55(h) asks for the place of supply "in case of inter-State movement" — the case this test builds: the job
    /// worker is in Karnataka (29), the principal in Maharashtra (27). The tax is then wholly INTEGRATED tax, 119.99,
    /// and the place of supply is marked inter-State. <b>Mutation (M5):</b> computing the line tax with
    /// <c>interState: false</c> regardless reddens this at the Integrated-tax Tj.
    /// </summary>
    [AvaloniaFact]
    public void An_inter_state_material_out_states_its_place_of_supply_and_integrated_tax()
    {
        var k = Seed("JW Challan IGST Co", workerState: "29", workerGstin: "29AAACR5055K1Z1");
        TypeOf(k, VoucherBaseType.MaterialOut).PrintAfterSaving = true;
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            KeyOutOrder(w, k);
            KeyMaterialOut(w, k);
            var s = Encoding.Latin1.GetString(k.Vm.PrintPreview!.PdfBytes);
            Assert.Contains("(Place of Supply: Karnataka \\(29\\)  \\(inter-State\\)) Tj", s, StringComparison.Ordinal);
            Assert.Contains("(Integrated tax: 119.99) Tj", s, StringComparison.Ordinal);
            Assert.Contains("(119.99) Tj", s, StringComparison.Ordinal);
            Assert.DoesNotContain("Central tax", s, StringComparison.Ordinal);
            Assert.Contains("(GSTIN/UIN: 29AAACR5055K1Z1) Tj", s, StringComparison.Ordinal);
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
