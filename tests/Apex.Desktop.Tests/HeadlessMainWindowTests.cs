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

    [AvaloniaFact]
    public void Cost_masters_and_reports_render_through_the_real_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Headless Cost Co";
            vm.CreateCompany();

            // Create a cost category through the real master page (Ctrl+A on the window creates it).
            vm.ShowCostCategoryMaster();
            Assert.Equal(Screen.CostCategoryMaster, vm.CurrentScreen);
            vm.CostCategoryMaster!.Name = "Departments";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var cat = vm.Company!.FindCostCategoryByName("Departments");
            Assert.NotNull(cat);

            // Create a cost centre under it through the real master page.
            vm.ShowCostCentreMaster();
            Assert.Equal(Screen.CostCentreMaster, vm.CurrentScreen);
            vm.CostCentreMaster!.SelectedCategory =
                vm.CostCentreMaster!.Categories.Single(c => c.Id == cat!.Id);
            vm.CostCentreMaster!.Name = "Delhi Branch";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var delhi = vm.Company!.FindCostCentreByName("Delhi Branch");
            Assert.NotNull(delhi);
            window.UpdateLayout();

            // The Cost Centre master page rendered the new centre + its category.
            var centreTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(centreTexts, t => t.Contains("Delhi Branch"));

            // A cost-applicable expense ledger.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Salaries";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var salaries = vm.Company!.FindLedgerByName("Salaries");
            var cash = vm.Company!.FindLedgerByName("Cash");

            // Payment: Dr Salaries 30,000 (cost-applicable, allocated to Delhi) / Cr Cash 30,000.
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Payment);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = salaries;
            entry.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            entry.Lines[0].AmountText = "30000";
            Assert.True(entry.Lines[0].IsCostApplicable);            // the cost sub-panel turned on
            var alloc = entry.Lines[0].CostAllocations[0];
            alloc.SelectedCategory = alloc.Categories.Single(c => c.Id == cat!.Id);
            alloc.SelectedCentre = alloc.Centres.Single(c => c.Id == delhi!.Id);
            alloc.AmountText = "30000";
            Assert.True(entry.Lines[0].CostSplitOk);
            entry.Lines[1].SelectedLedger = cash;
            entry.Lines[1].Side = Apex.Ledger.DrCr.Credit;
            entry.Lines[1].AmountText = "30000";
            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // accept
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            // The Cost Centre Break-up report page renders the centre + its total.
            vm.OpenCostReport(CostReportKind.CostCentreBreakup);
            Assert.Equal(Screen.CostReport, vm.CurrentScreen);
            window.UpdateLayout();
            var reportTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(reportTexts, t => t.Contains("Delhi Branch"));
            Assert.Contains(reportTexts, t => t.Contains("30,000"));
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    [AvaloniaFact]
    public void Multi_currency_master_forex_voucher_and_forex_report_render_through_the_real_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Headless Forex Co";
            vm.CreateCompany();
            var fyStart = vm.Company!.FinancialYearStart;

            // Create a USD currency + a rate through the real Currency master page (Ctrl+A creates the currency).
            vm.ShowCurrencyMaster();
            Assert.Equal(Screen.CurrencyMaster, vm.CurrentScreen);
            vm.CurrencyMaster!.Symbol = "$";
            vm.CurrencyMaster!.FormalName = "USD";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var usd = vm.Company!.FindCurrencyByName("USD");
            Assert.NotNull(usd);
            vm.CurrencyMaster!.RateDateText =
                fyStart.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
            vm.CurrencyMaster!.StandardRateText = "83";
            Assert.True(vm.CurrencyMaster!.CreateRate());
            window.UpdateLayout();

            // The Currency master page rendered the USD currency + the rate.
            var curTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(curTexts, t => t.Contains("USD"));
            Assert.Contains(curTexts, t => t.Contains("83"));

            // A USD export-sales ledger (via the Ledger master's Currency picker) + a base cash line.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Export Sales";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sales Accounts");
            vm.LedgerMaster!.SelectedCurrency =
                vm.LedgerMaster!.CurrencyChoices.Single(c => c.Display.Contains("USD"));
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var exportSales = vm.Company!.FindLedgerByName("Export Sales");
            Assert.NotNull(exportSales);
            Assert.True(exportSales!.IsForeignCurrency);
            var cash = vm.Company!.FindLedgerByName("Cash");

            // Enter a Receipt with a forex line: Cash Dr 83,000; Export Sales Cr (US$1,000 @ 83).
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Date = fyStart.AddDays(9);
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            entry.Lines[0].AmountText = "83000";
            var fx = entry.Lines[1];
            fx.SelectedLedger = exportSales;
            fx.Side = Apex.Ledger.DrCr.Credit;
            Assert.True(fx.IsForexLine);                        // the forex sub-panel turned on
            fx.ForexAmountText = "1000";
            Assert.Equal("83000", fx.AmountText);              // base auto-computed = forex × rate
            window.UpdateLayout();

            // The forex sub-panel rendered its fields.
            var lineTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(lineTexts, t => t.Contains("Forex Details"));
            Assert.Contains(lineTexts, t => t.Contains("83,000"));

            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // accept
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.Contains(vm.Company!.Vouchers.Single().Lines, l => l.HasForex);

            // The Forex Gain/Loss report page renders the ledger revalued (nil at the same 83 rate).
            vm.OpenForexReport();
            Assert.Equal(Screen.ForexReport, vm.CurrentScreen);
            window.UpdateLayout();
            var reportTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(reportTexts, t => t.Contains("Export Sales"));
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    // ---------------------------------------------------------------- RQ-7 defect-1: keyboard Enter drills

    /// <summary>
    /// Defect 1: pressing Enter (real key input on the window) on a highlighted TB ledger row must DRILL —
    /// the Window's tunnel-stage Enter handler now routes to the report drill when focus sits in the
    /// Tag="drill" ListBox, BEFORE its generic cascade Enter handling consumes the key. Previously only mouse
    /// double-click worked. This drives the real XAML + key pipeline, not the VM method directly.
    /// </summary>
    [AvaloniaFact]
    public void Keyboard_enter_on_a_highlighted_trial_balance_row_drills_through_the_real_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            window.UpdateLayout();

            // Highlight the first drillable ledger row via the rendered accounting-report (Tag="drill") ListBox.
            // Setting the ListBox SelectedItem propagates through the two-way SelectedRow binding the shell
            // reads on Enter — the exact path a mouse click / arrow key produces at runtime.
            var drillRow = vm.Reports!.Rows.First(r => r.IsDrillable);
            var listBox = window.GetVisualDescendants()
                .OfType<ListBox>()
                .First(lb => Equals(lb.Tag, "drill") && ReferenceEquals(lb.ItemsSource, vm.Reports!.Rows));
            listBox.SelectedItem = drillRow;
            window.UpdateLayout();
            Assert.Same(drillRow, vm.Reports!.SelectedRow);   // the two-way binding pushed the selection

            var before = vm.Columns.Count;

            // Real Enter key on the window — this is the mechanism render should verify manually too.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // Enter DRILLED (not swallowed by cascade nav): a ledger-vouchers column opened for Cash.
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerVouchers);
            Assert.Equal(before + 1, vm.Columns.Count);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Defect 3: while a drill column (ledger-vouchers) is the active pane, the report-parameter shortcuts
    /// (here F12 and Alt+F2 pressed as real keys) must be INERT — they must not open the report config panel
    /// nor change the underlying report's period — because IsReportContext is now false on a drill column.
    /// </summary>
    [AvaloniaFact]
    public void Report_shortcuts_do_not_fire_while_a_drill_column_is_active_through_the_real_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);
            var drillRow = vm.Reports!.Rows.First(r => r.IsDrillable);
            vm.DrillReport(drillRow);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            window.UpdateLayout();

            var columnsBefore = vm.Columns.Count;

            // F12 on a drill column must NOT open the report Configuration panel.
            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Assert.Null(vm.ReportConfig);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);

            // Alt+F2 on a drill column must NOT open the report period panel / change the report.
            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.Alt);
            Assert.Null(vm.ReportConfig);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.False(vm.IsReportContext);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Phase 7 slice 3: the "TDS Stat Payment" menu item advertises a "Ctrl+F" accelerator — pressing it as real key
    /// input on the window must actually open the deposit page (not a dead shortcut). Gated on TDS being enabled.
    /// </summary>
    [AvaloniaFact]
    public void CtrlF_opens_the_tds_stat_payment_page_through_the_real_window()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Headless TDS Co";
            vm.CreateCompany();
            new Apex.Ledger.Services.TdsTcsService(vm.Company!)
                .EnableTds(new Apex.Ledger.Domain.TdsConfig { Tan = "MUMA12345B" });
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            // Ctrl+F — the accelerator the menu item shows — opens the TDS Stat Payment (deposit) page.
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
            Assert.Equal(Screen.TdsStatPayment, vm.CurrentScreen);
            Assert.NotNull(vm.TdsStatPayment);
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
