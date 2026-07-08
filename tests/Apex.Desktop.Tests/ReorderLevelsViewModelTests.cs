using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// ViewModel coverage for Phase 6 slice 6 (<b>Reorder Levels</b> master + <b>Reorder Status</b> report;
/// RQ-32..RQ-37/RQ-53; gate PR-8), driven through the real shell + master/report view models on a throwaway
/// <c>.db</c>. Proves:
/// <list type="bullet">
///   <item>the Reorder-Levels master <b>creates/upserts</b> definitions per Item / Group / Category through the
///     engine and <b>persists</b> them (reload-identical);</item>
///   <item>the two independent <b>Alt+S / Alt+V</b> Simple⇄Advanced toggles flip the right flag, and an Advanced
///     figure without a period is rejected with a friendly message;</item>
///   <item>the Reorder-Status report projects the <b>seven</b> columns, the PR-8 worked example yields
///     <c>Order to be Placed = 25</c>, the <b>F8</b> "reorder only" filter hides zero-order rows, and
///     <b>Ctrl+F9</b> raises a Purchase Order pre-filled from the selected row;</item>
///   <item>nothing renders the word "Tally" (ER-8).</item>
/// </list>
/// </summary>
public sealed class ReorderLevelsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReorderLevelsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReorderTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }     // "Widget": opening 30, delivered 15 → closing 15
        public required Guid GroupId { get; init; }
        public required Guid CategoryId { get; init; }
        public required Guid GodownId { get; init; }
    }

    /// <summary>
    /// A seeded company with a "Goods" group, a "Consumables" category and a "Widget" item (under both) carrying
    /// <b>no</b> legacy reorder level — so the master definitions drive the report cleanly. Opening 30, a Delivery
    /// Note of 15 leaves closing 15 (below a reorder level of 20).
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var cat = inv.CreateStockCategory("Consumables");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id, categoryId: cat.Id);
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 30m, Money.FromRupees(100m));
        _storage.Save(c);

        var k = new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = widget.Id,
            GroupId = grp.Id,
            CategoryId = cat.Id,
            GodownId = c.MainLocation!.Id,
        };

        Post(k, VoucherBaseType.DeliveryNote, 15m);   // Widget outward → closing 15
        return k;
    }

    private void Post(Kit k, VoucherBaseType baseType, decimal qty, Guid? itemId = null)
    {
        k.Vm.OpenInventoryVoucher(baseType);
        var entry = k.Vm.InventoryVoucherEntry!;
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == (itemId ?? k.ItemId));
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(entry.Accept(), $"posting {baseType} should succeed");
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static ReorderLevelsViewModel OpenMaster(Kit k)
    {
        k.Vm.ShowReorderLevelsMaster();
        Assert.Equal(Screen.ReorderLevelsMaster, k.Vm.CurrentScreen);
        return k.Vm.ReorderLevels!;
    }

    private static void SelectScope(ReorderLevelsViewModel m, ReorderScope scope) =>
        m.SelectedScope = m.Scopes.Single(s => s.Scope == scope);

    private static void SelectTarget(ReorderLevelsViewModel m, Guid targetId) =>
        m.SelectedTarget = m.Targets.Single(t => t.Id == targetId);

    // ================================================================ (1) master create / list / persist

    [Fact]
    public void Master_creates_an_item_scoped_definition_and_lists_it()
    {
        var k = NewKit("RL Item Co");
        var m = OpenMaster(k);

        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        m.MinOrderQtyText = "25";

        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Scope == "Item" && r.Target == "Widget");

        var def = k.Vm.Company!.FindReorderDefinition(ReorderScope.Item, k.ItemId);
        Assert.NotNull(def);
        Assert.Equal(20m, def!.ReorderQuantity);
        Assert.Equal(25m, def.MinOrderQuantity);
    }

    [Fact]
    public void Master_creates_group_and_category_scoped_definitions()
    {
        var k = NewKit("RL GroupCat Co");
        var m = OpenMaster(k);

        SelectScope(m, ReorderScope.Group);
        SelectTarget(m, k.GroupId);
        m.ReorderQuantityText = "100";
        Assert.True(m.Create());

        SelectScope(m, ReorderScope.Category);
        SelectTarget(m, k.CategoryId);
        m.ReorderQuantityText = "80";
        Assert.True(m.Create());

        Assert.NotNull(k.Vm.Company!.FindReorderDefinition(ReorderScope.Group, k.GroupId));
        Assert.NotNull(k.Vm.Company!.FindReorderDefinition(ReorderScope.Category, k.CategoryId));
        Assert.Equal(2, k.Vm.Company!.ReorderDefinitions.Count);
    }

    [Fact]
    public void Master_upserts_on_the_same_scope_and_target()
    {
        var k = NewKit("RL Upsert Co");
        var m = OpenMaster(k);

        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        Assert.True(m.Create());

        // Re-create for the same (Item, Widget) with a different figure → replaces, not duplicates.
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "35";
        Assert.True(m.Create());

        Assert.Single(k.Vm.Company!.ReorderDefinitions);
        Assert.Equal(35m, k.Vm.Company!.FindReorderDefinition(ReorderScope.Item, k.ItemId)!.ReorderQuantity);
    }

    [Fact]
    public void Master_defs_persist_and_reload_identically()
    {
        var k = NewKit("RL Persist Co");
        var m = OpenMaster(k);
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        m.MinOrderQtyText = "25";
        Assert.True(m.Create());

        var reloaded = Reload(k.CompanyName);
        var def = reloaded.FindReorderDefinition(ReorderScope.Item, k.ItemId);
        Assert.NotNull(def);
        Assert.Equal(20m, def!.ReorderQuantity);
        Assert.Equal(25m, def.MinOrderQuantity);
        Assert.False(def.ReorderAdvanced);
    }

    // ================================================================ (2) Advanced toggles + validation

    [Fact]
    public void Alt_s_and_alt_v_toggle_the_two_advanced_flags_independently()
    {
        var k = NewKit("RL Toggle Co");
        var m = OpenMaster(k);

        Assert.False(m.ReorderAdvanced);
        Assert.False(m.MinQtyAdvanced);
        Assert.False(m.IsAdvanced);

        m.ToggleReorderAdvanced();               // Alt+S
        Assert.True(m.ReorderAdvanced);
        Assert.False(m.MinQtyAdvanced);
        Assert.True(m.IsAdvanced);

        m.ToggleMinQtyAdvanced();                // Alt+V
        Assert.True(m.MinQtyAdvanced);

        m.ToggleReorderAdvanced();               // Alt+S back to Simple
        Assert.False(m.ReorderAdvanced);
        Assert.True(m.MinQtyAdvanced);           // independent — min-qty stays Advanced
    }

    [Fact]
    public void Master_rejects_an_advanced_figure_without_a_period()
    {
        var k = NewKit("RL Adv Guard Co");
        var m = OpenMaster(k);
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        m.ToggleReorderAdvanced();               // Advanced but no period count typed
        m.PeriodCountText = string.Empty;

        Assert.False(m.Create());
        Assert.False(string.IsNullOrEmpty(m.Message));
        Assert.Empty(k.Vm.Company!.ReorderDefinitions);   // company not mutated on failure
    }

    [Fact]
    public void Master_creates_an_advanced_definition_with_a_period_and_criterion()
    {
        var k = NewKit("RL Adv Co");
        var m = OpenMaster(k);
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        m.ToggleReorderAdvanced();
        m.PeriodCountText = "3";
        m.SelectedPeriodUnit = m.PeriodUnits.Single(u => u.Unit == ExpiryPeriodUnit.Months);
        m.SelectedCriteria = m.Criteria.Single(c => c.Criteria == ReorderCriteria.Higher);

        Assert.True(m.Create());
        var def = k.Vm.Company!.FindReorderDefinition(ReorderScope.Item, k.ItemId)!;
        Assert.True(def.ReorderAdvanced);
        Assert.Equal(3, def.PeriodCount);
        Assert.Equal(ExpiryPeriodUnit.Months, def.PeriodUnit);
        Assert.Equal(ReorderCriteria.Higher, def.Criteria);
    }

    [Fact]
    public void Ctrl_a_creates_a_definition_via_activate_selected()
    {
        var k = NewKit("RL CtrlA Co");
        var m = OpenMaster(k);
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";

        k.Vm.ActivateSelected();   // Ctrl+A route
        Assert.NotNull(k.Vm.Company!.FindReorderDefinition(ReorderScope.Item, k.ItemId));
    }

    // ================================================================ (3) report — 7 cols, PR-8, F8, Ctrl+F9

    /// <summary>Defines a Simple reorder level 20 / MOQ 25 on the Widget item (the PR-8 setup).</summary>
    private void DefineBookExample(Kit k)
    {
        var m = OpenMaster(k);
        SelectScope(m, ReorderScope.Item);
        SelectTarget(m, k.ItemId);
        m.ReorderQuantityText = "20";
        m.MinOrderQtyText = "25";
        Assert.True(m.Create());
        k.Vm.ShowGateway();               // leave the master; the report opens from the gateway
    }

    [Fact]
    public void Reorder_status_report_projects_seven_columns()
    {
        var k = NewKit("RL Cols Co");
        DefineBookExample(k);

        k.Vm.OpenReport(ReportKind.ReorderStatus);
        var widget = k.Vm.Reports!.Rows.Single(r => r.Col1 == "Widget");

        Assert.Equal("15", widget.Col2);   // Closing
        Assert.Equal("20", widget.Col3);   // Reorder Level
        Assert.Equal("0", widget.Col4);    // Pending POs
        Assert.Equal("0", widget.Col5);    // SOs Due
        Assert.Equal("5", widget.Col6);    // Shortfall = 20 − 15
        Assert.Equal("25", widget.Col7);   // Order to be Placed (bounded below by MOQ 25)
    }

    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Reorder_status_order_to_be_placed_matches_book_example()
    {
        var k = NewKit("RL PR8 Co");
        DefineBookExample(k);

        k.Vm.OpenReport(ReportKind.ReorderStatus);
        var widget = k.Vm.Reports!.Rows.Single(r => r.Col1 == "Widget");

        // Book pp.159–161: reorder 20, MOQ 25, closing 15, no pending PO ⇒ shortfall 5, Order to be Placed 25.
        Assert.Equal("5", widget.Col6);
        Assert.Equal("25", widget.Col7);
        Assert.Equal(25m, widget.ReorderOrderQuantity);
    }

    [Fact]
    public void F8_reorder_only_filter_hides_rows_with_nothing_to_order()
    {
        var k = NewKit("RL F8 Co");
        var c = k.Vm.Company!;

        // A second item AT/BELOW its reorder level but with a pending Purchase Order that already covers the gap,
        // so its Order-to-be-Placed is 0: it shows on the report but F8 ("reorder only") must hide it.
        var inv = new InventoryService(c);
        var gadget = inv.CreateStockItem("Gadget", k.GroupId, c.FindUnitByName("Nos")!.Id);
        inv.AddOpeningBalance(gadget.Id, k.GodownId, 40m, Money.FromRupees(10m));
        _storage.Save(c);

        // Gadget reorder 50 (closing 40 ⇒ shortfall 10) but a pending PO of 15 nets it to 0-to-order; Widget
        // reorder 20 (closing 15 ⇒ order 25).
        var svc = new ReorderLevelsService(c);
        svc.CreateOrUpdate(ReorderScope.Item, gadget.Id, reorderQuantity: 50m);
        svc.CreateOrUpdate(ReorderScope.Item, k.ItemId, reorderQuantity: 20m, minOrderQuantity: 25m);
        _storage.Save(c);
        Post(k, VoucherBaseType.PurchaseOrder, 15m, itemId: gadget.Id);   // incoming PO covers the shortfall

        k.Vm.OpenReport(ReportKind.ReorderStatus);
        var gadgetRow = k.Vm.Reports!.Rows.Single(r => r.Col1 == "Gadget");
        Assert.Equal("0", gadgetRow.Col7);   // shown, but nothing to order (PO covers it)
        Assert.Contains(k.Vm.Reports!.Rows, r => r.Col1 == "Widget");

        k.Vm.ReportToggleReorderOnly();   // F8
        Assert.True(k.Vm.Reports!.ReorderOnlyFilter);
        Assert.DoesNotContain(k.Vm.Reports!.Rows, r => r.Col1 == "Gadget");  // nothing to order → hidden
        Assert.Contains(k.Vm.Reports!.Rows, r => r.Col1 == "Widget");        // still needs ordering

        k.Vm.ReportToggleReorderOnly();   // F8 again → back
        Assert.False(k.Vm.Reports!.ReorderOnlyFilter);
        Assert.Contains(k.Vm.Reports!.Rows, r => r.Col1 == "Gadget");
    }

    [Fact]
    public void Ctrl_f9_raises_a_purchase_order_prefilled_from_the_selected_row()
    {
        var k = NewKit("RL CtrlF9 Co");
        DefineBookExample(k);

        k.Vm.OpenReport(ReportKind.ReorderStatus);
        Assert.True(k.Vm.IsReorderStatusReport);
        var widget = k.Vm.Reports!.Rows.Single(r => r.Col1 == "Widget");
        k.Vm.Reports!.SelectedRow = widget;

        k.Vm.RaisePurchaseOrderFromReorder();   // Ctrl+F9

        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.InventoryVoucherEntry!;
        var line = entry.Lines[0];
        Assert.Equal(k.ItemId, line.SelectedItem!.Id);
        Assert.Equal(k.GodownId, line.SelectedGodown!.Id);   // company main location
        Assert.Equal(25m, line.ParsedQuantity);              // Order to be Placed
    }

    // ================================================================ (4) de-brand (ER-8)

    [Fact]
    public void Nothing_in_the_master_or_report_renders_tally()
    {
        var k = NewKit("RL Debrand Co");
        DefineBookExample(k);
        var m = OpenMaster(k);

        Assert.DoesNotContain(m.Scopes, s => s.Display.Contains("Tally", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(m.Criteria, s => s.Display.Contains("Tally", StringComparison.OrdinalIgnoreCase));

        k.Vm.ShowGateway();
        k.Vm.OpenReport(ReportKind.ReorderStatus);
        Assert.DoesNotContain(k.Vm.Reports!.Rows, r =>
            r.Col1.Contains("Tally", StringComparison.OrdinalIgnoreCase)
            || r.Col7.Contains("Tally", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
