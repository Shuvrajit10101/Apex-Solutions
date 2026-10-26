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
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11 — <i>Print voucher after saving</i> on the THREE save paths that printed nothing</b>: Alt+2
/// duplicate, the inventory-voucher screen and the POS bill. The census gap, verbatim: <i>"The other three print
/// nothing"</i>. <c>PrintAfterSavingTests</c> already pins the accounting entry door; this file pins the rest.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/voucher-types-tally/, opened by content
/// 2026-10-25: <i>"Print voucher after saving"</i> … <i>"to automatically open the Voucher Printing screen for the
/// voucher, once it is saved."</i> Nothing on that page restricts the option to a voucher kind.</para>
///
/// <para><b>Every positive test asserts WHICH voucher the opened preview is for</b> — its
/// <see cref="PrintPreviewViewModel.SourceVoucherId"/> against the id read off the book, AND the rendered bytes
/// against that voucher's own number / party — and a null preview fails it. Every door has a CONTROL (flag off ⇒
/// no preview) so "the preview opened" cannot pass by opening on every save.</para>
/// </summary>
public sealed class PrintAfterSavingDoorsTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexPasDoors_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, dir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static void Close(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var ledger = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero,
            openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static Company Reload(string dir, string name)
    {
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    private static string PdfText(PrintPreviewViewModel p) => Encoding.Latin1.GetString(p.PdfBytes);

    // ====================================================================== (1) Alt+2 duplicate

    /// <summary>Seeds one Receipt (Dr Cash / Cr Capital 4,321.00) with the flag OFF, then sets the flag as asked
    /// and duplicates it through the REAL Alt+2 on the Day Book and accepts with the REAL Ctrl+A.</summary>
    private static Guid DuplicateAReceipt(MainWindow window, MainWindowViewModel vm, string name, bool flag)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var capital = AddLedger(vm.Company!, "Capital A/c", "Capital Account");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = vm.Company!.FinancialYearStart.AddDays(5);
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "4321";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "4321";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        var source = Assert.Single(vm.Company!.Vouchers);
        Assert.Null(vm.PrintPreview);   // the flag was off for the seed

        vm.Company!.VoucherTypes.Single(t => t.Id == source.TypeId).PrintAfterSaving = flag;

        vm.OpenReport(ReportKind.DayBook);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == source.Id);
        Key(window, PhysicalKey.Digit2, RawInputModifiers.Alt);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        Assert.False(vm.VoucherEntry!.IsAltering);
        Key(window, PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(2, vm.Company!.Vouchers.Count);
        return source.Id;
    }

    /// <summary>
    /// 🔴 Alt+2 posts a NEW voucher of the type, so the flag opens the printing screen for THE DUPLICATE — not for
    /// the source it was copied from. <b>Mutation:</b> deleting the <c>OpenVoucherPrintingAfterSave(saved)</c>
    /// call in <c>ShowVoucherDuplicate</c>'s <c>onSaved</c> reddens this at <c>Assert.NotNull(vm.PrintPreview)</c>;
    /// passing the SOURCE id instead reddens the id and byte assertions.
    /// </summary>
    [AvaloniaFact]
    public void Alt2_duplicate_with_the_flag_opens_the_printing_screen_for_the_new_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var sourceId = DuplicateAReceipt(window, vm, "PAS Dup Co", flag: true);

            var book = Reload(dir, "PAS Dup Co");
            var duplicate = book.Vouchers.Single(v => v.Id != sourceId);
            Assert.Equal(2, duplicate.Number);

            Assert.NotNull(vm.PrintPreview);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.Equal(duplicate.Id, vm.PrintPreview!.SourceVoucherId);
            Assert.NotEqual(sourceId, vm.PrintPreview!.SourceVoucherId);
            var pdf = PdfText(vm.PrintPreview!);
            Assert.Contains("(No: 2) Tj", pdf, StringComparison.Ordinal);
            Assert.DoesNotContain("(No: 1) Tj", pdf, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>THE CONTROL for the duplicate door. <b>Mutation:</b> dropping the <c>Type.PrintAfterSaving</c>
    /// test from the duplicate's condition reddens this.</summary>
    [AvaloniaFact]
    public void Alt2_duplicate_without_the_flag_opens_no_printing_screen()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            DuplicateAReceipt(window, vm, "PAS Dup Control Co", flag: false);
            Assert.Null(vm.PrintPreview);
            Assert.NotEqual(Screen.PrintPreview, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    // ====================================================================== (2) inventory vouchers

    private sealed record StockKit(Guid ItemId, Guid GodownId, DomainLedger Party);

    private static StockKit SeedStock(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        var party = AddLedger(c, "Kolkata Retail Mart", "Sundry Debtors");
        return new StockKit(item.Id, c.MainLocation!.Id, party);
    }

    private static InventoryVoucherEntryViewModel KeyStockVoucher(MainWindowViewModel vm, StockKit k,
        VoucherBaseType baseType)
    {
        vm.OpenInventoryVoucher(baseType);
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        var entry = vm.InventoryVoucherEntry!;
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = "7";
        line.RateText = "120";
        var party = entry.Parties.FirstOrDefault(p => p.Ledger?.Id == k.Party.Id);
        if (party is not null) entry.SelectedParty = party;
        return entry;
    }

    /// <summary>
    /// 🔴 A Delivery Note created through <c>OpenInventoryVoucher</c> with the flag on opens the delivery challan
    /// preview for THAT voucher — its id off the reloaded book, and the consignee on the emitted bytes.
    /// <b>Mutation:</b> deleting the <c>OpenInventoryVoucherPrintingAfterSave(saved)</c> call reddens this at
    /// <c>Assert.NotNull(vm.PrintPreview)</c>; not writing <c>SavedVoucherId</c> in the entry VM's accept reddens
    /// it the same way.
    /// </summary>
    [AvaloniaFact]
    public void A_delivery_note_with_the_flag_opens_its_challan_on_save()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedStock(vm, "PAS DN Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.DeliveryNote && t.IsActive)
                .PrintAfterSaving = true;
            KeyStockVoucher(vm, k, VoucherBaseType.DeliveryNote);
            Pump(window);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var dn = Assert.Single(Reload(dir, "PAS DN Co").InventoryVouchers);
            Assert.NotNull(vm.PrintPreview);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.Equal(dn.Id, vm.PrintPreview!.SourceVoucherId);
            Assert.Contains("Kolkata Retail Mart", PdfText(vm.PrintPreview!), StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>THE CONTROL for the inventory door: same Delivery Note, flag off, no preview.</summary>
    [AvaloniaFact]
    public void A_delivery_note_without_the_flag_opens_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedStock(vm, "PAS DN Control Co");
            KeyStockVoucher(vm, k, VoucherBaseType.DeliveryNote);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Single(vm.Company!.InventoryVouchers);
            Assert.Null(vm.PrintPreview);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Every OTHER stock/order kind this test can key with a single line has NO printed document in this product
    /// (the drilled-voucher Ctrl+P arm says so by name). With the flag on, saving one must SAY that, by the same
    /// named notice, rather than silently print nothing — and must not open somebody else's document.
    /// <b>Mutation:</b> replacing the <c>RaiseLifecycleNotice(...)</c> in
    /// <c>OpenInventoryVoucherPrintingAfterSave</c> with a bare <c>return</c> reddens every row.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.RejectionIn)]
    [InlineData(VoucherBaseType.RejectionOut)]
    [InlineData(VoucherBaseType.PurchaseOrder)]
    [InlineData(VoucherBaseType.SalesOrder)]
    public void Other_stock_kinds_with_the_flag_say_they_have_no_printed_document(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedStock(vm, $"PAS {baseType} Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == baseType && t.IsActive).PrintAfterSaving = true;
            var entry = KeyStockVoucher(vm, k, baseType);
            Assert.True(entry.Accept(), entry.Message);

            Assert.Single(vm.Company!.InventoryVouchers);
            Assert.Null(vm.PrintPreview);
            Assert.Equal(DeliveryChallanPrintProjector.NotAChallan, vm.Notice);
        }
        finally { Close(window, dir); }
    }

    // ====================================================================== (3) the POS bill

    private static Voucher PostPosBill(MainWindow window, MainWindowViewModel vm, string name, bool typeFlag)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        var masters = new InventoryService(c);
        var widget = masters.CreateStockItem("Till Widget", masters.CreateStockGroup("Goods").Id,
            masters.CreateSimpleUnit("Nos", "Numbers").Id);
        AddLedger(c, "Retail Sales", "Sales Accounts");
        AddLedger(c, "Till Cash", "Cash-in-Hand");

        vm.OpenPosBilling();
        Pump(window);
        var pos = vm.PosBilling!;
        Assert.False(pos.PrintAfterSave);   // the POS config's own switch stays OFF throughout
        pos.Type.PrintAfterSaving = typeFlag;
        pos.Date = FyStart.AddDays(9);
        pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Name == "Retail Sales");
        pos.CashRow.SelectedLedger = c.Ledgers.Single(l => l.Name == "Till Cash");
        var line = pos.Items[0];
        line.SelectedItem = pos.StockItems.Single(i => i.Id == widget.Id);
        line.SelectedGodown = pos.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        line.QuantityText = "3";
        line.RateText = "250";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        return Assert.Single(c.Vouchers);
    }

    /// <summary>
    /// 🔴 The voucher TYPE's flag on a POS type now opens the bill's receipt — before this wave only the POS
    /// config's own switch did. <b>Mutation:</b> reverting <c>if (PrintAfterSave || _type.PrintAfterSaving)</c> to
    /// <c>if (PrintAfterSave)</c> in <c>PosBillingViewModel.Accept</c> reddens this at
    /// <c>Assert.NotNull(vm.PrintPreview)</c>.
    /// </summary>
    [AvaloniaFact]
    public void A_pos_bill_with_the_voucher_type_flag_opens_its_receipt_for_that_bill()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var bill = PostPosBill(window, vm, "PAS POS Co", typeFlag: true);
            var persisted = Reload(dir, "PAS POS Co").Vouchers.Single();
            Assert.Equal(bill.Id, persisted.Id);

            Assert.NotNull(vm.PrintPreview);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.Equal(persisted.Id, vm.PrintPreview!.SourceVoucherId);
            Assert.Contains("750.00", PdfText(vm.PrintPreview!), StringComparison.Ordinal);   // 3 x 250
        }
        finally { Close(window, dir); }
    }

    /// <summary>THE CONTROL for the POS door: neither switch on, no receipt.</summary>
    [AvaloniaFact]
    public void A_pos_bill_with_neither_switch_opens_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            PostPosBill(window, vm, "PAS POS Control Co", typeFlag: false);
            Assert.Null(vm.PrintPreview);
        }
        finally { Close(window, dir); }
    }
}
