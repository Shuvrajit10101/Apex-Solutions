using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 12.7 — THE DRILL ROUTE TO THE DELIVERY CHALLAN, AND THE WRONG DOCUMENT THAT STOOD WHERE IT
/// SHOULD HAVE BEEN.</b> Driven through the REAL <see cref="MainWindow"/> key handler, because the row's clause
/// is "no drill route reaches it" and a projector nobody can press a key to reach closes nothing — this file's
/// own subject matter is the <c>T2-40</c> failure mode (a correct projector reachable by nobody).
///
/// <para>🔴 <b>WHAT WAS MEASURED BEFORE THE FIX, AND WHY IT IS WORSE THAN AN ABSENCE.</b>
/// <c>MainWindowViewModel.OpenPrintPreview</c> had no arm for <see cref="Screen.InventoryVoucherDetail"/>.
/// That screen is a drill column raised FROM a register, so <c>Reports</c> is still live beneath it and
/// <c>IsReportContext</c> — which excludes <c>VoucherDetail</c> and <c>LedgerVouchers</c> but NOT this screen
/// — stayed TRUE. So Ctrl+P on a drilled Delivery Note fell through to the <c>Reports is not null</c> arm and
/// previewed <b>the whole Day Book</b>: every other party's movements, on a sheet raised from one voucher and
/// destined for one consignee. Same class as the Day Book this product once SHARED to a third party in place
/// of one invoice. <see cref="Ctrl_P_on_a_drilled_delivery_note_prints_the_challan_and_not_the_register"/>
/// asserts the cure on the PDF bytes, including that the other consignee is absent.</para>
///
/// <para><b>GROUNDING (R7, ruling 14).</b> <c>help.tallysolutions.com/keyboard-shortcuts-tally-prime/</c>:
/// <b>Ctrl+P</b> is <i>"To print the current voucher or report"</i>, and <b>Alt+F8</b> is
/// <i>"To open Delivery Note"</i> under <i>"F10 &gt; Inventory Vouchers"</i>.
/// <c>help.tallysolutions.com/tally-prime/sales-process/faq-sales/</c>: <i>"A delivery note is an inventory
/// document/challan sent with the goods to be delivered to the customer."</i></para>
/// </summary>
public sealed class DeliveryChallanRouteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexChallanRoute_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private sealed record Kit(Guid WidgetId, Guid GadgetId, Guid GodownId, Guid OursId, Guid TheirsId);

    private const string Ours = "Coastal Marine Supplies";
    private const string Theirs = "Himalayan Textiles Cooperative";

    private static Kit Seed(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var widget = masters.CreateStockItem("Anchor Chain", group.Id, nos.Id);
        var gadget = masters.CreateStockItem("Merino Yarn", group.Id, nos.Id);
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));
        masters.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));

        var debtors = c.FindGroupByName("Sundry Debtors")!.Id;
        var ours = new Domain.Ledger(Guid.NewGuid(), Ours, debtors, Money.Zero, openingIsDebit: true);
        var theirs = new Domain.Ledger(Guid.NewGuid(), Theirs, debtors, Money.Zero, openingIsDebit: true);
        c.AddLedger(ours);
        c.AddLedger(theirs);

        return new Kit(widget.Id, gadget.Id, c.MainLocation!.Id, ours.Id, theirs.Id);
    }

    /// <summary>Posts one pure-stock voucher through the REAL entry screen, so it is genuinely posted.</summary>
    private static InventoryVoucher PostThroughTheScreen(
        MainWindowViewModel vm, Kit k, VoucherBaseType baseType, Guid itemId, Guid? partyId, decimal qty)
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();

        vm.OpenInventoryVoucher(baseType);
        var entry = vm.InventoryVoucherEntry!;
        entry.SelectedParty = partyId is { } pid
            ? entry.Parties.Single(p => p.Ledger?.Id == pid)
            : entry.Parties.Single(p => p.IsNone);

        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        if (!entry.IsPhysicalStock) line.RateText = "250";
        if (entry.IsStockJournal)
        {
            var dest = entry.DestinationLines[0];
            dest.SelectedItem = entry.StockItems.Single(i => i.Id == k.GadgetId);
            dest.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            dest.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
            dest.RateText = "250";
        }

        Assert.True(entry.Accept(), $"{baseType}: {entry.Message}");
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    /// <summary>Opens the Day Book, highlights the pure-stock row and drills it with bare Enter.</summary>
    private static void DrillFromTheDayBook(MainWindow window, MainWindowViewModel vm, Guid stockVoucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        var row = vm.Reports!.Rows.SingleOrDefault(r => r.DrillInventoryVoucherId == stockVoucherId);
        Assert.NotNull(row);
        vm.Reports!.SelectedRow = row;
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
        Assert.Equal(stockVoucherId, vm.InventoryVoucherDetail!.VoucherId);
    }

    // ============================================================================================

    /// <summary>
    /// 🔴 <b>THE CLAUSE "NO DRILL ROUTE REACHES IT", CLOSED AND ASSERTED ON THE EMITTED BYTES.</b> Two Delivery
    /// Notes to two consignees; drill one from the Day Book and press the vendor's own Ctrl+P. The preview that
    /// opens is the CHALLAN for the drilled voucher — and it carries nothing belonging to the other consignee,
    /// which is what the old fall-through to the register did carry.
    ///
    /// <para><b>Mutation (run):</b> deleting the <c>Screen.InventoryVoucherDetail</c> arm from
    /// <c>MainWindowViewModel.OpenPrintPreview</c> makes the preview the Day Book again — the title assertion
    /// and both <c>DoesNotContain</c> assertions fail.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_on_a_drilled_delivery_note_prints_the_challan_and_not_the_register()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Challan Route Co");
            var mine = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.WidgetId, k.OursId, 4m);
            PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.GadgetId, k.TheirsId, 88m);

            DrillFromTheDayBook(window, vm, mine.Id);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            var preview = vm.PrintPreview;
            Assert.NotNull(preview);
            Assert.Equal(DeliveryChallanPrintProjector.Title, preview!.ReportTitle);

            string s = Encoding.Latin1.GetString(preview.PdfBytes);
            Assert.Contains("Delivery Challan", s, StringComparison.Ordinal);
            Assert.Contains(Ours, s, StringComparison.Ordinal);
            Assert.Contains("Anchor Chain", s, StringComparison.Ordinal);
            // The register's own heading, and the other consignee's row, must be nowhere on this sheet.
            Assert.DoesNotContain("Day Book", s, StringComparison.Ordinal);
            Assert.DoesNotContain(Theirs, s, StringComparison.Ordinal);
            Assert.DoesNotContain("Merino Yarn", s, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE OTHER HALF OF THE CURE: A PURE-STOCK VOUCHER WITH NO DOCUMENT NOW SAYS SO INSTEAD OF PRINTING
    /// SOMEBODY ELSE'S DATA.</b> A Stock Journal has no printed document on any TallyPrime page we opened, so
    /// Ctrl+P on one raises the notice and opens NO preview — where before this slice it previewed the whole
    /// Day Book. The notice is asserted REALISED into the visual tree, not merely set on a property.
    ///
    /// <para><b>Mutation:</b> replacing the arm's <c>return</c> with a fall-through reopens the register
    /// preview and fails both the screen assertion and the <c>DoesNotContain</c> below.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_on_a_drilled_stock_journal_raises_a_notice_and_opens_no_preview()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Challan Refusal Co");
            var journal = PostThroughTheScreen(
                vm, k, VoucherBaseType.StockJournal, k.WidgetId, partyId: null, qty: 3m);
            PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.GadgetId, k.TheirsId, 88m);

            DrillFromTheDayBook(window, vm, journal.Id);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.Null(vm.PrintPreview);
            Assert.Contains(
                RenderedText(window),
                t => t.Contains(DeliveryChallanPrintProjector.NotAChallan, StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// A Delivery Note posted with "(none)" for the party is refused by name — the operator is told WHY no
    /// challan appeared, rather than being handed a document addressed to nobody or left with a dead key.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_on_a_delivery_note_with_no_party_names_the_reason()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Challan No Party Co");
            var anonymous = PostThroughTheScreen(
                vm, k, VoucherBaseType.DeliveryNote, k.WidgetId, partyId: null, qty: 2m);

            DrillFromTheDayBook(window, vm, anonymous.Id);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.Null(vm.PrintPreview);
            Assert.Contains(
                RenderedText(window),
                t => t.Contains(DeliveryChallanPrintProjector.NoParty, StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();
}
