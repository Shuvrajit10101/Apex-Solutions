using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 5.7 (Optional, Ctrl+L) and 5.8 (Post-Dated, Ctrl+T) — the USER ROUTES, wave 10.</b>
///
/// <para><b>Vendor (R7, ruling 14), opened by content 2026-11-02,</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c>: <i>"Any voucher in TallyPrime can
/// be marked as Optional by pressing Ctrl+L (Optional) during voucher entry or in alteration mode"</i>; <i>"you can
/// regularise the transaction by opening it and pressing Ctrl+L"</i>; <i>"Mark vouchers Post-Dated by pressing
/// Ctrl+T (Post-Dated) button from the Button Bar."</i> / <i>"This button toggles with Current."</i>; and the two
/// registers' route <i>"Gateway of Tally &gt; Display More Reports &gt; Exception Reports &gt; Optional Vouchers."</i>
/// (and <i>Post-Dated Vouchers</i>).</para>
///
/// <para>🔴 Every chord goes through <see cref="MainWindow"/>'s real tunnel key handler, and every figure is read
/// off a book RELOADED through a fresh <see cref="CompanyStorage"/> — the flag, the on-hand quantity, the edit-log
/// verb, and the CSV bytes of the register.</para>
/// </summary>
public sealed class ProvisionalVoucherFlagsRouteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexProvFlags_" + Guid.NewGuid().ToString("N"));
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

    private static void Close(Window window, string dir)
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

    private sealed record Kit(Guid ItemId, Guid GodownId, DateOnly On, string Name);

    private static Kit Seed(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Flag Widget", masters.CreateStockGroup("Flag Goods").Id,
            masters.CreateSimpleUnit("FNos", "Flag Numbers").Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(100.37m));
        return new Kit(item.Id, c.MainLocation!.Id, c.FinancialYearStart.AddDays(5), name);
    }

    private static Company Reload(string dir, string name)
    {
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    private static decimal OnHand(Company c, Kit k)
        => new InventoryLedger(c).OnHand(k.ItemId, k.GodownId, c.FinancialYearStart.AddYears(1).AddDays(-1));

    /// <summary>Opens a Receipt Note entry, keys one line of 7 @ 55.01, presses the given chords through the real
    /// handler, then accepts. Returns the posted voucher.</summary>
    private static InventoryVoucher PostReceipt(MainWindow window, MainWindowViewModel vm, Kit k,
        params (PhysicalKey Key, RawInputModifiers Mods)[] chords)
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();
        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        Pump(window);
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        var entry = vm.InventoryVoucherEntry!;
        entry.Date = k.On;
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = "7";
        line.RateText = "55.01";
        foreach (var (key, mods) in chords) Key(window, key, mods);
        Assert.True(entry.Accept(), entry.Message);
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    private static void AlterFromDayBook(MainWindow window, MainWindowViewModel vm, Guid id, bool stock)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r =>
            stock ? r.DrillInventoryVoucherId == id : r.DrillVoucherId == id);
        Pump(window);
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.Equal(stock ? Screen.InventoryVoucherEntry : Screen.VoucherEntry, vm.CurrentScreen);
    }

    /// <summary>
    /// 🔴 <b>5.7 END TO END ON THE STOCK AGGREGATE.</b> Ctrl+L on a NEW Receipt Note (real key) posts it Optional;
    /// the reloaded book holds it Optional and its 7 are NOT on hand; the Optional Vouchers register reached from
    /// the Gateway by arrows + Enter lists it and its CSV bytes carry its number; Ctrl+Enter from the Day Book,
    /// Ctrl+L, Ctrl+A regularises it, and the book reloaded AGAIN holds it live with the 7 on hand and a
    /// SetOptional edit-log line.
    ///
    /// <para><b>Mutations:</b> dropping the <c>InventoryVoucherEntry</c> branch of
    /// <c>MainWindowViewModel.ToggleOptional</c> reddens the first reload assertion; dropping the SetOptional
    /// split from <c>AcceptAlteration</c> reddens the Ctrl+A assertion (Replace refuses the move).</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_L_marks_a_stock_voucher_optional_and_Ctrl_L_in_alteration_regularises_it_off_the_reloaded_book()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Optional Route Co");

            // The badge is live on the stock entry screen (it was lit for accounting entry only).
            vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
            Pump(window);
            Assert.True(Assert.Single(vm.ButtonBar.Where(b => b.Key == "Ctrl+L")).Enabled);
            vm.ShowGateway();
            Pump(window);

            var receipt = PostReceipt(window, vm, k, (PhysicalKey.L, RawInputModifiers.Control));

            var reloaded = Reload(dir, k.Name);
            Assert.True(reloaded.FindInventoryVoucher(receipt.Id)!.Optional);
            Assert.Equal(10m, OnHand(reloaded, k));

            // The Gateway route: Exception Reports > Optional Vouchers, by arrows and Enter.
            vm.ShowExceptionReportsMenu();
            Pump(window);
            for (var i = 0; i < 12 && vm.Menu[vm.SelectedIndex].Label != "Optional Vouchers"; i++)
                Key(window, PhysicalKey.ArrowDown);
            Assert.Equal("Optional Vouchers", vm.Menu[vm.SelectedIndex].Label);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.OptionalVouchersRegister, vm.Reports!.Kind);
            Assert.Contains(vm.Reports!.Rows, r => r.DrillInventoryVoucherId == receipt.Id);
            var csv = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(vm.Reports!)));
            Assert.Contains(vm.Company!.FormatVoucherNumber(receipt), csv);
            Assert.Contains("Receipt Note", csv);

            // Regularise: Ctrl+Enter from the Day Book, Ctrl+L, Ctrl+A.
            AlterFromDayBook(window, vm, receipt.Id, stock: true);
            Assert.True(vm.InventoryVoucherEntry!.IsOptional, "alteration did not seed the posted Optional flag");
            Key(window, PhysicalKey.L, RawInputModifiers.Control);
            Assert.False(vm.InventoryVoucherEntry!.IsOptional);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var again = Reload(dir, k.Name);
            Assert.False(again.FindInventoryVoucher(receipt.Id)!.Optional);
            Assert.Equal(17m, OnHand(again, k));
            Assert.Contains(again.VoucherEditLog,
                e => e.VoucherId == receipt.Id && e.Verb == VoucherEditVerb.SetOptional);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>5.8 ON THE STOCK AGGREGATE.</b> A Receipt Note posted with Ctrl+T, then Ctrl+Enter / Ctrl+T / Ctrl+A:
    /// the reloaded book holds it current, the Post-Dated register no longer lists it, and the edit log carries a
    /// <see cref="VoucherEditVerb.SetPostDated"/> line. Before wave 10 this move rode through <c>Replace</c> as an
    /// unlabelled Alter. <b>Mutation:</b> dropping the SetPostDated split from <c>AcceptAlteration</c> reddens the
    /// reload assertion (Replace now refuses the move).
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_T_in_stock_alteration_marks_a_post_dated_voucher_current_off_the_reloaded_book()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock PostDated Route Co");
            var receipt = PostReceipt(window, vm, k, (PhysicalKey.T, RawInputModifiers.Control));
            Assert.True(Reload(dir, k.Name).FindInventoryVoucher(receipt.Id)!.PostDated);

            AlterFromDayBook(window, vm, receipt.Id, stock: true);
            Assert.True(vm.InventoryVoucherEntry!.IsPostDated);
            Key(window, PhysicalKey.T, RawInputModifiers.Control);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var reloaded = Reload(dir, k.Name);
            Assert.False(reloaded.FindInventoryVoucher(receipt.Id)!.PostDated);
            Assert.Contains(reloaded.VoucherEditLog,
                e => e.VoucherId == receipt.Id && e.Verb == VoucherEditVerb.SetPostDated);
            var fy = reloaded.FinancialYearStart;
            Assert.DoesNotContain(
                ExceptionVouchers.Build(reloaded, ExceptionVoucherKind.PostDated, fy, fy.AddYears(1).AddDays(-1)),
                r => r.VoucherId == receipt.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>5.8 ON THE ACCOUNTING AGGREGATE — "THE FLAG CAN NEVER BE CLEARED WHEN THE CHEQUE CLEARS".</b> A
    /// post-dated Payment, Ctrl+Enter from the Day Book, Ctrl+T, Ctrl+A. Before <c>LedgerService.SetPostDated</c>
    /// the save refused with "Replace does not change a voucher's provisional state". The reloaded book holds it
    /// current with a SetPostDated line. <b>Mutation:</b> deleting the SetPostDated block in
    /// <c>VoucherEntryViewModel.CommitAlteration</c> reddens the reload assertion.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_T_in_accounting_alteration_marks_a_post_dated_cheque_current_off_the_reloaded_book()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Cheque PostDated Route Co");
            var c = vm.Company!;
            var cash = c.FindLedgerByName("Cash")!;
            var rent = new Ledger.Domain.Ledger(Guid.NewGuid(), "Office Rent PDC",
                c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
            c.AddLedger(rent);
            var payment = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id, k.On,
                new[]
                {
                    new EntryLine(rent.Id, Money.FromRupees(2_500.03m), DrCr.Debit),
                    new EntryLine(cash.Id, Money.FromRupees(2_500.03m), DrCr.Credit),
                },
                postDated: true));

            AlterFromDayBook(window, vm, payment.Id, stock: false);
            Assert.True(vm.VoucherEntry!.IsPostDated);
            Key(window, PhysicalKey.T, RawInputModifiers.Control);
            Assert.False(vm.VoucherEntry!.IsPostDated);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var reloaded = Reload(dir, k.Name);
            var onDisk = reloaded.FindVoucher(payment.Id)!;
            Assert.False(onDisk.PostDated);
            Assert.Equal(2_500.03m, onDisk.Lines[0].Amount.Amount);
            Assert.Contains(reloaded.VoucherEditLog,
                e => e.VoucherId == payment.Id && e.Verb == VoucherEditVerb.SetPostDated);
        }
        finally { Close(window, dir); }
    }
}
