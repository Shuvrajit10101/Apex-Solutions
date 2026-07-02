using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// Drives the exact "Load Robert Demo → Balance Sheet" path through the shell view models
/// (no UI toolkit needed), asserting the Robert baseline reconciles to the paisa: Balance
/// Sheet 1,05,000 = 1,05,000 and Trial Balance 1,37,000 = 1,37,000 (design §9; NFR-3).
/// The storage is rooted in a throwaway temp folder so the test never touches the real
/// %AppData% companies store.
/// </summary>
public sealed class DemoLoadViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public DemoLoadViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexDesktopTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    [Fact]
    public void LoadRobertDemo_then_BalanceSheet_totals_105000()
    {
        var vm = new MainWindowViewModel(_storage);

        // The company-select screen offers "Load Robert Demo" as a menu item.
        vm.LoadRobertDemo();

        // After loading, we are on the Gateway with a company open.
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.NotNull(vm.Company);
        Assert.Equal("Robert Transport Services", vm.Company!.Name);

        // Open the Balance Sheet report through the view model.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);

        // Assert straight off the engine projection the VM used.
        var bs = BalanceSheet.Build(vm.Company!, LastVoucherDate(vm));
        Assert.Equal(105000m, bs.TotalLiabilities.Amount);
        Assert.Equal(105000m, bs.TotalAssets.Amount);
        Assert.True(bs.Balanced);
        Assert.Equal(5000m, bs.NetProfitInCapital.Amount);
    }

    [Fact]
    public void LoadRobertDemo_then_TrialBalance_totals_137000()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        var tb = TrialBalance.Build(vm.Company!, LastVoucherDate(vm));
        Assert.Equal(137000m, tb.TotalDebit.Amount);
        Assert.Equal(137000m, tb.TotalCredit.Amount);
        Assert.True(tb.Balanced);
    }

    [Fact]
    public void ProfitAndLoss_netProfit_is_5000()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        var pl = ProfitAndLoss.Build(vm.Company!, LastVoucherDate(vm));
        Assert.Equal(37000m, pl.TotalIncome.Amount);
        Assert.Equal(32000m, pl.TotalExpenses.Amount);
        Assert.Equal(5000m, pl.NetProfit.Amount);
    }

    [Fact]
    public void Demo_company_persists_and_reloads_with_same_balances()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        var name = vm.Company!.Name;

        // A .db file must now exist; loading it back through the storage reproduces the totals.
        var entry = _storage.ListCompanies().Single(e => e.Name == name);
        var reloaded = _storage.Load(entry);

        var tb = TrialBalance.Build(reloaded, LastVoucherDate2(reloaded));
        Assert.Equal(137000m, tb.TotalDebit.Amount);
        Assert.Equal(137000m, tb.TotalCredit.Amount);

        var bs = BalanceSheet.Build(reloaded, LastVoucherDate2(reloaded));
        Assert.Equal(105000m, bs.TotalAssets.Amount);
        Assert.Equal(105000m, bs.TotalLiabilities.Amount);
    }

    [Fact]
    public void CreateCompany_seeds_and_opens_an_empty_company()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Acme Traders";
        vm.CreateCompany();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.NotNull(vm.Company);
        Assert.Equal("Acme Traders", vm.Company!.Name);
        // Seed contract: exactly 28 groups + 2 ledgers + 24 voucher types.
        Assert.Equal(28, vm.Company.Groups.Count);
        Assert.Equal(2, vm.Company.Ledgers.Count);
        Assert.Equal(24, vm.Company.VoucherTypes.Count);
    }

    [Fact]
    public void Keyboard_nav_wraps_and_activates()
    {
        var vm = new MainWindowViewModel(_storage);
        // The initial company-select menu has at least "Create Company" + "Load Robert Demo".
        Assert.True(vm.Menu.Count >= 2);
        Assert.Equal(0, vm.SelectedIndex);

        vm.MoveUp(); // wraps to the last item
        Assert.Equal(vm.Menu.Count - 1, vm.SelectedIndex);

        vm.MoveDown(); // wraps back to the first
        Assert.Equal(0, vm.SelectedIndex);
    }

    private static DateOnly LastVoucherDate(MainWindowViewModel vm) => LastVoucherDate2(vm.Company!);

    private static DateOnly LastVoucherDate2(Apex.Ledger.Domain.Company c)
    {
        DateOnly? last = null;
        foreach (var v in c.Vouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        return last ?? c.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
