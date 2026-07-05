using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// Proves the reorganised Gateway is a proper hierarchy, not a flat dump: three non-selectable
/// section headers (MASTERS / TRANSACTIONS / REPORTS) with the items nested under each; a
/// "Vouchers" submenu that leads to the six accounting voucher types; arrow navigation that skips
/// section headers; and a Chart-of-Accounts tree that nests the 13 sub-groups under their 15
/// primary parents (and ledgers under their group). Drives the real shell view model over a
/// throwaway storage folder — no UI toolkit needed.
/// </summary>
public sealed class GatewayHierarchyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GatewayHierarchyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGatewayTests_" + Guid.NewGuid().ToString("N"));
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

    private static string[] HeaderLabels(MainWindowViewModel vm) =>
        vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();

    private static string[] ItemLabels(MainWindowViewModel vm) =>
        vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();

    // ---------------------------------------------------------------- sections

    [Fact]
    public void Gateway_exposes_the_sections_with_their_items_nested()
    {
        var vm = NewSeededCompany("Sections Co");

        // Exactly the section headers, in order (Statutory sits under Masters; GST config lives there).
        Assert.Equal(new[] { "Masters", "Statutory", "Transactions", "Reports" }, HeaderLabels(vm));

        // Each section's items are present and reachable as selectable rows.
        var items = ItemLabels(vm);
        Assert.Contains("Create", items);              // Masters
        Assert.Contains("Chart of Accounts", items);   // Masters
        Assert.Contains("GST", items);                 // Statutory
        Assert.Contains("Vouchers", items);            // Transactions
        Assert.Contains("Day Book", items);            // Transactions
        Assert.Contains("Balance Sheet", items);       // Reports
        Assert.Contains("Profit & Loss A/c", items);   // Reports
        Assert.Contains("Trial Balance", items);       // Reports
    }

    [Fact]
    public void Section_headers_are_not_selectable_and_selection_starts_on_the_first_item()
    {
        var vm = NewSeededCompany("Header Co");

        // The first row is the MASTERS header — non-selectable — so selection lands below it.
        Assert.True(vm.Menu[0].IsHeader);
        Assert.False(vm.Menu[0].IsSelectable);
        Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable);
        Assert.NotEqual(0, vm.SelectedIndex);
    }

    // ---------------------------------------------------------------- arrow skips headers

    [Fact]
    public void Arrow_navigation_skips_section_headers()
    {
        var vm = NewSeededCompany("Arrow Co");

        // Walking the whole menu with Down must never land the highlight on a header.
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable,
                $"highlight landed on a non-selectable row at step {i}");
            vm.MoveDown();
        }

        // Same going up.
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            Assert.True(vm.Menu[vm.SelectedIndex].IsSelectable);
            vm.MoveUp();
        }
    }

    // ---------------------------------------------------------------- Vouchers submenu

    [Fact]
    public void Vouchers_leads_to_the_six_voucher_types()
    {
        var vm = NewSeededCompany("Vouchers Co");

        // Enter the Vouchers submenu (Transactions → Vouchers).
        vm.ShowVouchersMenu();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);

        // The six accounting voucher types are listed under the VOUCHERS header, then an "Inventory"
        // section with the Order/Inventory voucher groups, then an "Other Vouchers" group (Reversing
        // Journal / Memorandum nest under it).
        Assert.Equal(new[] { "Vouchers", "Inventory", "Other Vouchers" }, HeaderLabels(vm));
        Assert.Equal(
            new[] { "Contra", "Payment", "Receipt", "Journal", "Sales", "Purchase",
                    "Order Vouchers", "Inventory Vouchers", "Other Vouchers" },
            ItemLabels(vm));

        // The six accounting types carry their F-key hint (F4..F9); each row is a submenu child.
        var hints = vm.Menu.Where(m => m.IsSelectable).Take(6).Select(m => m.Hint).ToArray();
        Assert.Equal(new[] { "F4", "F5", "F6", "F7", "F8", "F9" }, hints);
        Assert.All(vm.Menu.Where(m => m.IsSelectable), m => Assert.True(m.IsSubItem));
    }

    [Fact]
    public void Selecting_a_voucher_type_opens_voucher_entry_for_that_type()
    {
        var vm = NewSeededCompany("Open Voucher Co");
        vm.ShowVouchersMenu();

        // Highlight "Receipt" (3rd selectable item) and activate it.
        var receiptIndex = 0;
        for (var i = 0; i < vm.Menu.Count; i++)
            if (vm.Menu[i].IsSelectable && vm.Menu[i].Label == "Receipt") { receiptIndex = i; break; }

        // Drive selection via the public arrow API so we prove the highlight path works.
        vm.MoveDown(); vm.MoveDown(); // Contra -> Payment -> Receipt
        Assert.Equal("Receipt", vm.Menu[vm.SelectedIndex].Label);

        vm.ActivateSelected();
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        Assert.Equal(VoucherBaseType.Receipt, vm.VoucherEntry!.Type.BaseType);
    }

    [Fact]
    public void Esc_steps_back_from_vouchers_submenu_to_root_gateway_then_company_select()
    {
        var vm = NewSeededCompany("Esc Co");
        vm.ShowVouchersMenu();
        Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);

        // Esc pops the submenu back to the root Gateway.
        vm.Back();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Root, vm.CurrentGatewayMenu);

        // Esc again leaves the Gateway to Company Select.
        vm.Back();
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
    }

    // ---------------------------------------------------------------- Create submenu

    [Fact]
    public void Create_submenu_lists_ledger_and_opens_the_ledger_master()
    {
        var vm = NewSeededCompany("Create Co");
        vm.ShowCreateMenu();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);
        Assert.Contains("Ledger", ItemLabels(vm));

        // Activating the first item (Ledger) opens the Ledger-creation master.
        vm.ActivateSelected();
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        Assert.NotNull(vm.LedgerMaster);
    }

    // ---------------------------------------------------------------- Chart of Accounts nesting

    [Fact]
    public void ChartOfAccounts_nests_subgroups_under_their_primary_parents()
    {
        var vm = NewSeededCompany("Chart Co");
        vm.ShowChartOfAccounts();
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
        var rows = vm.ChartOfAccounts!.Rows;

        // Every primary head sits at depth 0; every sub-group sits deeper than depth 0.
        Assert.All(rows.Where(r => r.Kind == ChartNodeKind.Primary), r => Assert.Equal(0, r.Depth));
        Assert.All(rows.Where(r => r.Kind == ChartNodeKind.SubGroup), r => Assert.True(r.Depth >= 1));

        // A known sub-group ("Bank Accounts") must render AFTER and DEEPER than its primary parent
        // ("Current Assets"), and before the next primary head — i.e. it is nested under it.
        var currentAssetsIdx = IndexOfGroup(rows, "Current Assets");
        var bankAccountsIdx = IndexOfGroup(rows, "Bank Accounts");
        Assert.True(currentAssetsIdx >= 0 && bankAccountsIdx >= 0);
        Assert.True(bankAccountsIdx > currentAssetsIdx, "sub-group must come after its parent");
        Assert.True(rows[bankAccountsIdx].Depth > rows[currentAssetsIdx].Depth,
            "sub-group must be indented deeper than its primary parent");

        // No primary head appears between the parent and the nested sub-group.
        var primariesBetween = rows
            .Skip(currentAssetsIdx + 1)
            .Take(bankAccountsIdx - currentAssetsIdx - 1)
            .Count(r => r.Kind == ChartNodeKind.Primary);
        Assert.Equal(0, primariesBetween);
    }

    [Fact]
    public void ChartOfAccounts_nests_ledgers_under_their_group()
    {
        var vm = NewSeededCompany("Chart Ledger Co");

        // Add a ledger under a known sub-group, then open the chart.
        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "HDFC Bank";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
        Assert.True(vm.LedgerMaster!.Create());

        vm.ShowChartOfAccounts();
        var rows = vm.ChartOfAccounts!.Rows;

        var bankIdx = IndexOfGroup(rows, "Bank Accounts");
        var ledgerIdx = rows.ToList().FindIndex(r => r.Kind == ChartNodeKind.Ledger && r.Name == "HDFC Bank");
        Assert.True(bankIdx >= 0 && ledgerIdx >= 0);
        Assert.True(ledgerIdx > bankIdx, "ledger must render after its group");
        Assert.True(rows[ledgerIdx].Depth > rows[bankIdx].Depth, "ledger must be indented under its group");
    }

    private static int IndexOfGroup(System.Collections.Generic.IEnumerable<ChartRow> rows, string name)
    {
        var list = rows.ToList();
        return list.FindIndex(r => r.IsGroup && r.Name == name);
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
