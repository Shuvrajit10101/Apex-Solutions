using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
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

            // The root Gateway opens on its first SELECTABLE item — never a section header.
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable);

            // Simulate real key input on the window: Down moves the highlight (skipping the
            // TRANSACTIONS / REPORTS section headers), Enter activates the highlighted item —
            // opening one of the sub-screens (chart / report / voucher / ledger, depending on the
            // item) or a Gateway submenu. We assert we left the root Gateway, then Esc back.
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable); // arrow never lands on a header
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // Either we jumped to a sub-screen, or we pushed into a Gateway submenu.
            var leftRoot = vm.CurrentScreen != Screen.Gateway
                           || vm.CurrentGatewayMenu != GatewayMenu.Root;
            Assert.True(leftRoot);

            // Esc steps back to the root Gateway.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.Equal(GatewayMenu.Root, vm.CurrentGatewayMenu);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    [AvaloniaFact]
    public void CtrlA_accepts_a_balanced_receipt_through_the_real_window_and_persists()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Headless Voucher Co";
            vm.CreateCompany();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            // A Capital ledger to credit against the seeded Cash.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Capital A/c";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // Ctrl+A creates the ledger
            var capital = vm.Company!.FindLedgerByName("Capital A/c");
            Assert.NotNull(capital);

            // Open the Receipt entry screen and fill a balanced voucher via the VM the XAML binds to.
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Receipt);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var entry = vm.VoucherEntry!;
            var cash = vm.Company!.FindLedgerByName("Cash");

            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            entry.Lines[0].AmountText = "100000";
            entry.Lines[1].SelectedLedger = capital;
            entry.Lines[1].Side = Apex.Ledger.DrCr.Credit;
            entry.Lines[1].AmountText = "100000";
            Assert.True(entry.CanAccept);

            // Ctrl+A through the real window accepts → posts → persists → back to Gateway.
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            // Reload the .db: the Receipt is persisted and the Trial Balance still balances.
            var storage = new CompanyStorage(tempDir);
            var entry2 = storage.ListCompanies().Single(e => e.Name == "Headless Voucher Co");
            var reloaded = storage.Load(entry2);
            var tb = TrialBalance.Build(reloaded, LastVoucherDate(reloaded));
            Assert.True(tb.Balanced);
            Assert.Contains(tb.Rows, r => r.LedgerName == "Capital A/c" && r.Credit.Amount == 100000m);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    [AvaloniaFact]
    public void Cascade_drills_root_into_a_balance_sheet_page_column_beside_the_menu()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.LoadRobertDemo();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.True(vm.IsGatewayCascade);

            // Walk the root column to "Balance Sheet" using real Down key presses, then drill in with
            // the Right key — the cascade must add a page column while keeping the root menu visible.
            for (var i = 0; i < vm.Menu.Count + 2; i++)
            {
                if (vm.Menu[vm.SelectedIndex].Label == "Balance Sheet") break;
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            }
            Assert.Equal("Balance Sheet", vm.Menu[vm.SelectedIndex].Label);

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

            // Two columns rendered: the root menu column (still visible) + the Balance Sheet page.
            Assert.Equal(2, vm.Columns.Count);
            Assert.True(vm.Columns[0].IsMenu);
            Assert.True(vm.Columns[1].IsPage);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // The rendered page column shows the 1,05,000 total (proves the real template bound cleanly).
            var texts = window.GetVisualDescendants()
                .OfType<Avalonia.Controls.TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToList();
            Assert.Contains(texts, t => t.Contains("1,05,000"));

            // Left removes the page column, focus returns to the (still-present) root menu column.
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Assert.Single(vm.Columns);
            Assert.Equal("Balance Sheet", vm.Menu[vm.SelectedIndex].Label);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    [AvaloniaFact]
    public void Billwise_outstandings_page_renders_the_bill_and_CtrlB_settles_it_through_the_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Headless BillWise Co";
            vm.CreateCompany();

            // Create a bill-wise party debtor (Sundry Debtors auto-enables bill-by-bill) + a Sales ledger.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Party X";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            Assert.True(vm.LedgerMaster!.MaintainBillByBill);
            vm.LedgerMaster!.DefaultCreditPeriodText = "15";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var party = vm.Company!.FindLedgerByName("Party X");
            Assert.NotNull(party);

            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Sales A/c";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sales Accounts");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var sales = vm.Company!.FindLedgerByName("Sales A/c");

            // Enter a Sales voucher with a bill-wise New-Ref allocation on the party line.
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = party;
            entry.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            entry.Lines[0].AmountText = "25000";
            Assert.True(entry.Lines[0].IsBillWise);               // the sub-panel turned on
            entry.Lines[0].BillAllocations[0].Name = "BILL-1";
            entry.Lines[0].BillAllocations[0].AmountText = "25000";
            entry.Lines[1].SelectedLedger = sales;
            entry.Lines[1].Side = Apex.Ledger.DrCr.Credit;
            entry.Lines[1].AmountText = "25000";
            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // accept

            // Open the Receivables Outstandings page and confirm the rendered page shows the bill.
            vm.OpenOutstandings(OutstandingsKind.Receivables);
            Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
            Assert.Single(vm.Outstandings!.Rows);

            // Force a layout/render pass so the page column's rows are realized before we read them.
            window.UpdateLayout();

            var texts = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToList();
            Assert.Contains(texts, t => t.Contains("BILL-1"));   // the reference rendered
            Assert.Contains(texts, t => t.Contains("25,000"));   // the pending amount rendered

            // Spacebar selects the highlighted bill; Ctrl+B settles it — real key input on the window.
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(vm.Outstandings!.Rows[0].IsSelected);
            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            // The bill is knocked off — the page is now empty.
            Assert.Empty(vm.Outstandings!.Rows);
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
