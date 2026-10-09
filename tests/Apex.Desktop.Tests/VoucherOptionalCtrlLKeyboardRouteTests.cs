using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W45 REVIEW — THE VENDOR'S Ctrl+L (Regular) ROUTE IS REACHABLE FROM THE KEYBOARD.</b> Driven through the
/// real <see cref="MainWindow"/> tunnel handler on a real posted book; nothing here calls <c>ForAlter</c> or
/// <c>ToggleOptional</c> directly, which is the whole point.
///
/// <para><b>The defect this locks, MEASURED.</b> The engine verb and the screen's accept path were both built and
/// the branch's own Desktop tests were green — because every one of them called <c>open.Entry.ToggleOptional()</c>
/// on the view model and so proved nothing about the chord. Driven through the real keyboard, Ctrl+L on an
/// alteration opened by real Ctrl+Enter from the Day Book was swallowed by the <b>Save View</b> arm: measured,
/// <c>CurrentScreen</c> went to <c>Screen.SaveView</c> and <c>ScreenTitle</c> to "Save View — Ctrl+L" while the
/// button bar above the screen was painting "Ctrl+L  Optional", and <c>ToggleOptional</c> never ran.
/// <c>MainWindowViewModel.ShowVoucherAlteration</c> arrives through <c>OpenDrillColumn</c>, which deliberately
/// does NOT clear <c>Reports</c>, so <c>IsReportContext</c> is TRUE on the alteration column — the exact premise
/// the Save View arm's own comment asserted could never happen.</para>
///
/// <para><b>R7 grounding, opened by content 2026-10-09.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c>: <i>"Once the actual date of such
/// transaction occurs you can regularise the transaction by opening it and pressing Ctrl+L (Regular)."</i>
/// <c>help.tallysolutions.com/keyboard-shortcuts-tally-prime/</c> lists <c>Ctrl+L</c> as <i>"To mark a voucher as
/// Optional"</i> under Vouchers &amp; Masters — and Save View is a REPORTS chord, not a voucher one.</para>
///
/// <para><b>It bites.</b> Removing the <c>Screen.VoucherEntry</c> exclusion from the Save View arm fails it at the
/// <c>alter.IsOptional</c> assertion, with the swallowing screen named in the failure message.</para>
/// </summary>
public sealed class VoucherOptionalCtrlLKeyboardRouteTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexRvwCtrlL_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
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
        var group = c.FindGroupByName(groupName)!;
        var l = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    private static decimal Closing(Company c, DomainLedger l) =>
        LedgerBalances.SignedClosing(c, l, c.FinancialYearStart.AddYears(1));

    private static (MainWindowViewModel Vm, DomainLedger Rent, Voucher V) Seed(
        MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        var rent = AddLedger(c, "Rent", "Indirect Expenses");
        var landlord = AddLedger(c, "Landlord", "Sundry Creditors");

        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Date = FyStart.AddDays(7);
        e.Lines[0].SelectedLedger = rent;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "8431.55";
        e.Lines[1].SelectedLedger = landlord;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "8431.55";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(c.Vouchers);
        Assert.Equal(8431.55m, Closing(c, rent));
        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
        Pump(window);
        return (vm, rent, c.Vouchers[0]);
    }

    /// <summary>
    /// The vendor route, keys only: Day Book -> Ctrl+Enter (alter) -> Ctrl+L -> Ctrl+A.
    /// Asserts on the voucher read back off the book and on the Rent closing.
    /// </summary>
    [AvaloniaFact]
    public void Real_Ctrl_L_on_an_altering_screen_opened_from_the_Day_Book_reaches_ToggleOptional()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (_, rent, posted) = Seed(window, vm, "Rvw CtrlL Co");
            var c = vm.Company!;

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == posted.Id);
            Pump(window);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var alter = vm.VoucherEntry!;
            Assert.True(alter.IsAltering);
            Assert.False(alter.IsOptional);

            // Diagnostic: is the report still bound underneath the alteration column?
            var reportStillBound = vm.Reports is not null;
            var reportContext = vm.IsReportContext;

            // ---- THE REAL CHORD ----
            Key(window, PhysicalKey.L, RawInputModifiers.Control);

            Assert.True(
                alter.IsOptional,
                $"Ctrl+L did not reach ToggleOptional on the altering screen. "
                + $"Reports bound underneath = {reportStillBound}; IsReportContext = {reportContext}; "
                + $"CurrentScreen after Ctrl+L = {vm.CurrentScreen}; ScreenTitle = {vm.ScreenTitle}");

            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.True(c.FindVoucher(posted.Id)!.Optional);
            Assert.Equal(0m, Closing(c, rent));
        }
        finally { Close(window, dir); }
    }
}
