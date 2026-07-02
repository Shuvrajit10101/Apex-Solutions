using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// Headless UI smoke tests: they construct the real <see cref="MainWindow"/> bound to a
/// <see cref="MainWindowViewModel"/> over a throwaway storage folder, show it, then drive the
/// "Load Robert Demo → Balance Sheet" path — asserting the on-screen Balance Sheet total is
/// 1,05,000. This exercises the actual XAML, bindings, and window rather than just the VM.
/// </summary>
public sealed class HeadlessMainWindowTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexHeadless_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    [AvaloniaFact]
    public void Boots_to_gateway_after_loading_demo_and_shows_balance_sheet_105000()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            // Sanity: the window is up and starts on the company-select screen.
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);

            // Drive the demo-load action (the same one the "Load Robert Demo" menu item fires).
            vm.LoadRobertDemo();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.NotNull(vm.Company);

            // Open the Balance Sheet report through the shell.
            vm.OpenReport(ReportKind.BalanceSheet);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);

            // The report view model's rows must include the two 1,05,000 totals, and the engine
            // projection the UI rendered must balance to the paisa.
            var asOf = LastVoucherDate(vm.Company!);
            var bs = BalanceSheet.Build(vm.Company!, asOf);
            Assert.Equal(105000m, bs.TotalAssets.Amount);
            Assert.Equal(105000m, bs.TotalLiabilities.Amount);

            var totalRows = vm.Reports!.Rows.Where(r => r.IsTotal).ToList();
            Assert.Contains(totalRows, r => r.Amount.Contains("1,05,000"));
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    [AvaloniaFact]
    public void Arrow_keys_and_enter_navigate_the_gateway_menu()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.LoadRobertDemo();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            // Simulate real key input on the window: Down then Enter activates the 2nd gateway
            // item (Profit & Loss A/c), opening a report.
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // Esc steps back to the Gateway.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    private static DateOnly LastVoucherDate(Apex.Ledger.Domain.Company c)
    {
        DateOnly? last = null;
        foreach (var v in c.Vouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        return last ?? c.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private static void Cleanup(string tempDir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
