using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// Proves the cascading (Miller-columns) Gateway: column 1 is the root menu; drilling into a group
/// item (Vouchers / Create) adds a submenu column to its right while the root stays visible; drilling
/// into a page item (a report, a voucher-entry screen, the ledger master, the chart) adds a page column
/// to the right without hiding the menu columns; changing the selection in an earlier column discards
/// every column to its right; and Left/Esc removes the rightmost column, returning focus (with its
/// selection intact) to the previous column. Drives the real shell view model over throwaway storage —
/// no UI toolkit needed.
/// </summary>
public sealed class CascadingGatewayTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CascadingGatewayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCascadeTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static void SelectRootItem(MainWindowViewModel vm, string label)
    {
        // Walk the active (root) column with Down until the highlighted row is the wanted item.
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            vm.MoveDown();
        }
        Assert.Fail($"root item '{label}' was not reachable by arrow navigation");
    }

    private static GatewayColumn RightmostColumn(MainWindowViewModel vm) => vm.Columns[^1];

    // ---------------------------------------------------------------- root column

    [Fact]
    public void Gateway_opens_as_a_single_root_column_focused_on_its_first_item()
    {
        var vm = NewSeededCompany("Root Co");

        Assert.True(vm.IsGatewayCascade);
        Assert.Single(vm.Columns);                       // just the root menu column
        Assert.True(vm.Columns[0].IsMenu);
        Assert.Equal(0, vm.ActiveColumnIndex);
        Assert.True(vm.Columns[0].IsActive);
        Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable); // highlight never on a header
    }

    // ---------------------------------------------------------------- drill into a GROUP

    [Fact]
    public void Selecting_Vouchers_opens_a_submenu_column_with_the_six_types_root_stays_visible()
    {
        var vm = NewSeededCompany("Vouchers Cascade Co");
        SelectRootItem(vm, "Vouchers");
        vm.DrillIn(); // Right/Enter drills into the highlighted Vouchers group

        // Two columns now: root (left, inactive) + Vouchers submenu (right, active).
        Assert.Equal(2, vm.Columns.Count);
        Assert.True(vm.Columns[0].IsMenu);
        Assert.False(vm.Columns[0].IsActive);            // earlier column dim/inactive
        Assert.True(vm.Columns[1].IsMenu);
        Assert.True(vm.Columns[1].IsActive);             // focused column
        Assert.Equal(1, vm.ActiveColumnIndex);
        Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);

        // The submenu lists the six accounting voucher types plus an "Other Vouchers" group
        // (Reversing Journal / Memorandum nest under it — professional hierarchy).
        var items = vm.Columns[1].Items.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[] { "Contra", "Payment", "Receipt", "Journal", "Sales", "Purchase", "Other Vouchers" },
            items);

        // The root column's chosen row is still "Vouchers" (kept, shown inactive).
        Assert.Equal("Vouchers", vm.Columns[0].Selected!.Label);
    }

    [Fact]
    public void Selecting_a_voucher_type_opens_a_page_column_beside_the_menu_columns()
    {
        var vm = NewSeededCompany("Voucher Page Co");
        SelectRootItem(vm, "Vouchers");
        vm.DrillIn();

        // Highlight Receipt in the submenu column and drill in → a voucher page column appears.
        vm.MoveDown(); vm.MoveDown(); // Contra -> Payment -> Receipt
        Assert.Equal("Receipt", RightmostColumn(vm).Selected!.Label);
        vm.DrillIn();

        // Three columns: root, Vouchers submenu, Receipt page — the two menu columns stay visible.
        Assert.Equal(3, vm.Columns.Count);
        Assert.True(vm.Columns[0].IsMenu);
        Assert.True(vm.Columns[1].IsMenu);
        Assert.True(vm.Columns[2].IsPage);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherEntry);
        Assert.Equal(VoucherBaseType.Receipt, vm.VoucherEntry!.Type.BaseType);
        Assert.Same(vm.VoucherEntry, vm.Columns[2].Voucher); // page hosted inside the column
    }

    // ---------------------------------------------------------------- drill into a PAGE

    [Fact]
    public void Selecting_BalanceSheet_opens_a_page_column_on_the_right_without_hiding_the_menu()
    {
        var vm = NewSeededCompany("BS Cascade Co");
        SelectRootItem(vm, "Balance Sheet");
        vm.DrillIn();

        // Root menu column stays; a Balance Sheet page column is appended to its right.
        Assert.Equal(2, vm.Columns.Count);
        Assert.True(vm.Columns[0].IsMenu);               // menu column NOT hidden
        Assert.True(vm.Columns[1].IsPage);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);
        Assert.Same(vm.Reports, vm.Columns[1].Report);
        Assert.Equal("Balance Sheet", vm.Columns[0].Selected!.Label); // root selection intact
    }

    [Fact]
    public void ChartOfAccounts_opens_as_a_page_column_hosting_the_tree()
    {
        var vm = NewSeededCompany("Chart Cascade Co");
        SelectRootItem(vm, "Chart of Accounts");
        vm.DrillIn();

        Assert.Equal(2, vm.Columns.Count);
        Assert.True(vm.Columns[1].IsPage);
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
        Assert.NotNull(vm.ChartOfAccounts);
        Assert.Same(vm.ChartOfAccounts, vm.Columns[1].Chart);
        Assert.NotEmpty(vm.ChartOfAccounts!.Rows);
    }

    // ---------------------------------------------------------------- replacing the right column

    [Fact]
    public void Changing_the_earlier_selection_replaces_the_right_page_column()
    {
        var vm = NewSeededCompany("Replace Co");

        // Open Balance Sheet as the right page column.
        SelectRootItem(vm, "Balance Sheet");
        vm.DrillIn();
        Assert.Equal(2, vm.Columns.Count);
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);

        // Left returns focus to the root column (page column removed).
        vm.Back();
        Assert.Single(vm.Columns);
        Assert.Equal(0, vm.ActiveColumnIndex);
        Assert.Equal("Balance Sheet", vm.Menu[vm.SelectedIndex].Label); // selection intact

        // Move to Trial Balance and drill in → the right column is now the NEW report, not the old one.
        SelectRootItem(vm, "Trial Balance");
        vm.DrillIn();
        Assert.Equal(2, vm.Columns.Count);
        Assert.True(vm.Columns[1].IsPage);
        Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind); // replaced
    }

    [Fact]
    public void Moving_the_highlight_in_an_earlier_column_discards_the_columns_to_its_right()
    {
        var vm = NewSeededCompany("Discard Co");

        // Build [root -> Vouchers submenu]; focus is on the submenu.
        SelectRootItem(vm, "Vouchers");
        vm.DrillIn();
        Assert.Equal(2, vm.Columns.Count);

        // Step back to the root column, then move the highlight there — the submenu column is dropped.
        vm.Back();
        Assert.Single(vm.Columns);
        vm.MoveDown();
        Assert.Single(vm.Columns);                       // still just the root column
        Assert.Equal(0, vm.ActiveColumnIndex);
    }

    // ---------------------------------------------------------------- page-open always replaces the page

    [Fact]
    public void Opening_a_page_via_a_hotkey_while_a_page_is_open_replaces_it_never_stacks()
    {
        var vm = NewSeededCompany("No Stack Co");

        // Open Balance Sheet as the right page column → [root(menu), BalanceSheet(page)].
        SelectRootItem(vm, "Balance Sheet");
        vm.DrillIn();
        var menuColumns = vm.Columns.Count(c => c.IsMenu);
        Assert.Equal(1, menuColumns);                        // just the root menu column
        Assert.Equal(2, vm.Columns.Count);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));    // exactly one page column
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);

        // Now, WITH the Balance Sheet page still open, open another page via a hotkey path
        // (Payment F5). The stale Balance Sheet page must be REPLACED, not stacked beside it.
        vm.OpenVoucher(VoucherBaseType.Payment);

        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));    // STILL exactly one page column
        Assert.Equal(menuColumns + 1, vm.Columns.Count);     // menu columns + the single page
        Assert.Equal(2, vm.Columns.Count);
        Assert.True(vm.Columns[^1].IsPage);                  // the page is the rightmost column
        Assert.Null(vm.Reports);                             // old Balance Sheet page is gone
        Assert.NotNull(vm.VoucherEntry);                     // replaced by the Payment voucher page
        Assert.Equal(VoucherBaseType.Payment, vm.VoucherEntry!.Type.BaseType);
        Assert.Same(vm.VoucherEntry, vm.Columns[^1].Voucher);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
    }

    [Fact]
    public void Opening_a_report_hotkey_over_an_open_report_replaces_it_never_stacks()
    {
        var vm = NewSeededCompany("Report Swap Co");

        // Open Balance Sheet via its hotkey path (the "B" button / OpenReport).
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);

        // Open Trial Balance via its hotkey ("T") while Balance Sheet is still open.
        vm.OpenReport(ReportKind.TrialBalance);

        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));    // still one page column, not two
        Assert.Equal(2, vm.Columns.Count);                   // root menu + single page
        Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind); // replaced, not stacked
        Assert.True(vm.Columns[^1].IsPage);
    }

    [Fact]
    public void Arrow_drill_still_yields_exactly_one_page_column()
    {
        var vm = NewSeededCompany("Arrow Drill Co");

        SelectRootItem(vm, "Balance Sheet");
        vm.DrillIn();

        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));    // exactly one page column
        Assert.Equal(2, vm.Columns.Count);                   // root menu + single page
        Assert.True(vm.Columns[^1].IsPage);
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);
    }

    // ---------------------------------------------------------------- step-back semantics

    [Fact]
    public void Left_and_Esc_remove_the_rightmost_column_and_refocus_the_previous_one()
    {
        var vm = NewSeededCompany("Back Co");

        SelectRootItem(vm, "Vouchers");
        vm.DrillIn();                    // [root, Vouchers]
        vm.DrillIn();                    // [root, Vouchers, Contra page] (Contra is first submenu item)
        Assert.Equal(3, vm.Columns.Count);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

        // Left removes the page column → focus back on the Vouchers submenu.
        vm.Back();
        Assert.Equal(2, vm.Columns.Count);
        Assert.Equal(1, vm.ActiveColumnIndex);
        Assert.True(vm.Columns[1].IsActive);
        Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);
        Assert.Null(vm.VoucherEntry);

        // Left again removes the submenu → focus back on the root, selection intact.
        vm.Back();
        Assert.Single(vm.Columns);
        Assert.Equal(0, vm.ActiveColumnIndex);
        Assert.Equal(GatewayMenu.Root, vm.CurrentGatewayMenu);
        Assert.Equal("Vouchers", vm.Menu[vm.SelectedIndex].Label);

        // Left on the lone root column leaves the Gateway to Company Select.
        vm.Back();
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.False(vm.IsGatewayCascade);
    }

    // ---------------------------------------------------------------- headers skipped

    [Fact]
    public void Arrow_navigation_in_a_column_skips_section_headers()
    {
        var vm = NewSeededCompany("Skip Co");
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable);
            vm.MoveDown();
        }
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable);
            vm.MoveUp();
        }
    }

    // ---------------------------------------------------------------- inactive-column highlight

    [Fact]
    public void Only_the_active_column_paints_the_bright_highlight()
    {
        var vm = NewSeededCompany("Highlight Co");
        SelectRootItem(vm, "Create");
        vm.DrillIn(); // [root(inactive), Create(active)]

        // The active (Create) column's rows are flagged active; the root column's rows are inactive,
        // yet the root's chosen "Create" row is still marked selected (rendered dim, not bright).
        Assert.True(vm.Columns[1].Items.All(m => m.IsActiveColumn));
        Assert.True(vm.Columns[0].Items.All(m => !m.IsActiveColumn));
        Assert.Equal("Create", vm.Columns[0].Selected!.Label);
        Assert.True(vm.Columns[0].Selected!.IsSelected);
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
