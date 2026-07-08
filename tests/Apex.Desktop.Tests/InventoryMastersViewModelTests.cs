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
/// End-to-end coverage for the Inventory Masters UI surfaced in the cascade (catalog §9; RQ-1..RQ-6): each
/// of the five create screens (Stock Group, Stock Category, Unit, Godown, Stock Item) creates a master
/// through the real shell view models, persists it to a throwaway .db, lists it, and enforces its
/// pre-validation (unit decimals 0–4, compound base≠tail, opening-balance rate to the paisa, quantities to
/// 6 dp) without crashing the UI. Also proves the masters nest under Masters → Create → Inventory Masters,
/// each opening as exactly ONE page column. Drives the real VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class InventoryMastersViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InventoryMastersViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInventoryTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        Assert.Equal(Screen.StockGroupMaster, vm.CurrentScreen);
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        return vm.Company!.FindStockGroupByName(name)!;
    }

    private Unit CreateSimpleUnit(MainWindowViewModel vm, string symbol, string formal, int decimals = 0)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = formal;
        m.DecimalPlacesText = decimals.ToString();
        Assert.True(m.Create());
        return vm.Company!.FindUnitByName(symbol)!;
    }

    // ---------------------------------------------------------------- (1) Stock Group

    [Fact]
    public void Stock_group_is_created_through_the_master_and_persists()
    {
        const string companyName = "Stock Group Co";
        var vm = NewSeededCompany(companyName);

        var fg = CreateGroup(vm, "Finished Goods");
        Assert.True(fg.IsPrimary);
        Assert.Contains(vm.StockGroupMaster!.Existing, r => r.Name == "Finished Goods" && r.Under == "Primary");

        // A child group nests under the first (hierarchical parent picker).
        vm.ShowStockGroupMaster();
        var m2 = vm.StockGroupMaster!;
        var parentFg = m2.ParentOptions.Single(p => p.Group?.Id == fg.Id);
        m2.SelectedParent = parentFg;
        m2.Name = "Widgets";
        m2.AddQuantities = false;
        Assert.True(m2.Create());
        var child = vm.Company!.FindStockGroupByName("Widgets")!;
        Assert.Equal(fg.Id, child.ParentId);
        Assert.False(child.AddQuantities);

        // PERSISTED: reload and both survive with the parent link.
        var reloaded = Reload(companyName);
        var rFg = reloaded.FindStockGroupByName("Finished Goods");
        var rChild = reloaded.FindStockGroupByName("Widgets");
        Assert.NotNull(rFg);
        Assert.NotNull(rChild);
        Assert.Equal(rFg!.Id, rChild!.ParentId);
    }

    [Fact]
    public void Stock_group_requires_a_name_and_rejects_duplicates()
    {
        var vm = NewSeededCompany("Stock Group Validate Co");
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;

        m.Name = "  ";
        Assert.False(m.Create());                       // blank name rejected
        Assert.NotNull(m.Message);

        m.Name = "Raw Materials";
        Assert.True(m.Create());
        m.Name = "Raw Materials";
        Assert.False(m.Create());                       // duplicate rejected (engine message surfaced)
        Assert.Contains("already exists", m.Message!);
    }

    // ---------------------------------------------------------------- (2) Stock Category

    [Fact]
    public void Stock_category_is_created_through_the_master_and_persists()
    {
        const string companyName = "Stock Cat Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowStockCategoryMaster();
        Assert.Equal(Screen.StockCategoryMaster, vm.CurrentScreen);
        var m = vm.StockCategoryMaster!;
        m.Name = "Premium";
        m.Alias = "PREM";
        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Name == "Premium");

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindStockCategoryByName("Premium"));
    }

    // ---------------------------------------------------------------- (3) Unit (simple + compound)

    [Fact]
    public void Simple_unit_is_created_and_persists()
    {
        const string companyName = "Unit Simple Co";
        var vm = NewSeededCompany(companyName);

        var nos = CreateSimpleUnit(vm, "Nos", "Numbers", decimals: 0);
        Assert.False(nos.IsCompound);
        Assert.Equal(0, nos.DecimalPlaces);
        Assert.Contains(vm.UnitMaster!.Existing, r => r.Symbol == "Nos" && r.Kind == "Simple");

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindUnitByName("Nos"));
    }

    [Fact]
    public void Unit_decimals_outside_0_to_4_are_rejected_before_the_engine()
    {
        var vm = NewSeededCompany("Unit Decimals Co");
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = "Kg";
        m.FormalName = "Kilograms";
        m.DecimalPlacesText = "7";                      // out of 0–4 range
        Assert.False(m.Create());
        Assert.Contains("between 0 and 4", m.Message!);
        Assert.Null(vm.Company!.FindUnitByName("Kg"));  // not created
    }

    [Fact]
    public void Compound_unit_is_created_from_two_simple_units()
    {
        const string companyName = "Unit Compound Co";
        var vm = NewSeededCompany(companyName);

        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        CreateSimpleUnit(vm, "Pcs", "Pieces");           // a second simple unit so compound is buildable

        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        Assert.True(m.CanBuildCompound);
        m.IsCompound = true;
        m.Symbol = "Dozen";
        m.FormalName = "Dozens";
        m.FirstUnit = m.SimpleUnits.Single(u => u.Id == nos.Id);
        m.TailUnit = m.SimpleUnits.Single(u => u.Symbol == "Pcs");
        m.ConversionFactorText = "12";
        Assert.True(m.Create());

        var dozen = vm.Company!.FindUnitByName("Dozen")!;
        Assert.True(dozen.IsCompound);
        Assert.Equal(12, dozen.ConversionNumerator);

        var reloaded = Reload(companyName);
        Assert.True(reloaded.FindUnitByName("Dozen")!.IsCompound);
    }

    [Fact]
    public void Compound_unit_with_same_first_and_tail_is_rejected()
    {
        var vm = NewSeededCompany("Unit Compound Same Co");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        CreateSimpleUnit(vm, "Pcs", "Pieces");

        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = true;
        m.Symbol = "Bad";
        m.FormalName = "Bad Unit";
        m.FirstUnit = m.SimpleUnits.Single(u => u.Id == nos.Id);
        m.TailUnit = m.SimpleUnits.Single(u => u.Id == nos.Id);   // same as first
        m.ConversionFactorText = "10";
        Assert.False(m.Create());
        Assert.Contains("must be different", m.Message!);
        Assert.Null(vm.Company!.FindUnitByName("Bad"));
    }

    // ---------------------------------------------------------------- (4) Godown

    [Fact]
    public void Godown_is_created_under_main_location_and_persists()
    {
        const string companyName = "Godown Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowGodownMaster();
        Assert.Equal(Screen.GodownMaster, vm.CurrentScreen);
        var m = vm.GodownMaster!;
        // The seeded Main Location is listed and offered as a parent.
        Assert.Contains(m.Existing, r => r.Kind == "Main Location");
        var mainOption = m.ParentOptions.Single(p => p.Godown?.IsMainLocation == true);
        m.SelectedParent = mainOption;
        m.Name = "Warehouse 2";
        m.ThirdParty = true;
        Assert.True(m.Create());

        var wh = vm.Company!.FindGodownByName("Warehouse 2")!;
        Assert.True(wh.ThirdParty);
        Assert.Equal(vm.Company!.MainLocation!.Id, wh.ParentId);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindGodownByName("Warehouse 2"));
    }

    // ---------------------------------------------------------------- (5) Stock Item + opening balance

    [Fact]
    public void Stock_item_is_created_with_opening_balance_and_persists()
    {
        const string companyName = "Stock Item Co";
        var vm = NewSeededCompany(companyName);
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateSimpleUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        var m = vm.StockItemMaster!;
        Assert.True(m.CanCreate);                        // a group + a unit both exist
        m.Name = "Widget-X";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        // Opening: 100 Nos @ ₹25.50 at Main Location.
        Assert.NotNull(m.OpeningGodown);
        Assert.True(m.OpeningGodown!.IsMainLocation);
        m.OpeningQuantityText = "100";
        m.OpeningRateText = "25.50";
        m.OpeningBatchLabel = "LOT-1";
        Assert.True(m.Create());

        var item = vm.Company!.FindStockItemByName("Widget-X")!;
        Assert.Equal(StockValuationMethod.AverageCost, item.ValuationMethod);   // DP-1 default
        var service = new InventoryService(vm.Company!);
        Assert.Equal(Money.FromRupees(2550m), service.OpeningValueOf(item.Id));  // 100 × 25.50

        // Listed with its opening value.
        Assert.Contains(m.Existing, r => r.Name == "Widget-X" && r.OpeningValue.Contains("2,550"));

        // PERSISTED: reload and the item + its opening allocation survive.
        var reloaded = Reload(companyName);
        var rItem = reloaded.FindStockItemByName("Widget-X");
        Assert.NotNull(rItem);
        var rService = new InventoryService(reloaded);
        Assert.Equal(Money.FromRupees(2550m), rService.OpeningValueOf(rItem!.Id));
    }

    [Fact]
    public void Stock_item_opening_rate_must_be_to_the_paisa()
    {
        var vm = NewSeededCompany("Stock Item Rate Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateSimpleUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Gadget";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.OpeningQuantityText = "10";
        m.OpeningRateText = "25.505";                    // sub-paisa rate → rejected pre-engine
        Assert.False(m.Create());
        Assert.Contains("paisa", m.Message!);
        Assert.Null(vm.Company!.FindStockItemByName("Gadget"));   // nothing created
    }

    [Fact]
    public void Stock_item_reorder_beyond_six_decimals_is_rejected()
    {
        var vm = NewSeededCompany("Stock Item Reorder Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateSimpleUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Sprocket";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.ReorderLevelText = "1.1234567";                // 7 dp → beyond 6 dp precision
        Assert.False(m.Create());
        Assert.Contains("decimal places", m.Message!);
        Assert.Null(vm.Company!.FindStockItemByName("Sprocket"));
    }

    [Fact]
    public void Stock_item_requires_a_group_and_a_unit()
    {
        var vm = NewSeededCompany("Stock Item Missing Co");

        // No stock groups or units seeded → the master cannot create yet.
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.False(m.CanCreate);
        m.Name = "Orphan";
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        Assert.Empty(vm.Company!.StockItems);
    }

    // ---------------------------------------------------------------- (6) cascade nav correctness

    [Fact]
    public void Inventory_masters_nest_under_masters_create_and_open_as_single_page_columns()
    {
        var vm = NewSeededCompany("Inventory Nav Co");

        vm.ShowCreateMenu();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);
        var createLabels = vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Contains("Stock Group", createLabels);
        Assert.Contains("Stock Category", createLabels);
        Assert.Contains("Unit", createLabels);
        Assert.Contains("Godown", createLabels);
        Assert.Contains("Stock Item", createLabels);
        // The "Inventory Masters" section header is present (professional hierarchy).
        Assert.Contains(vm.Menu, x => x.IsHeader && x.Label == "Inventory Masters");

        // Opening each master adds exactly ONE page column, replacing the previous one.
        vm.ShowStockGroupMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Same(vm.StockGroupMaster, vm.Columns[^1].StockGroupMaster);

        vm.ShowStockCategoryMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.StockGroupMaster);
        Assert.Same(vm.StockCategoryMaster, vm.Columns[^1].StockCategoryMaster);

        vm.ShowUnitMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Same(vm.UnitMaster, vm.Columns[^1].UnitMaster);

        vm.ShowGodownMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Same(vm.GodownMaster, vm.Columns[^1].GodownMaster);

        vm.ShowStockItemMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Same(vm.StockItemMaster, vm.Columns[^1].StockItemMaster);

        // Esc steps back off the page to the Create submenu.
        vm.Back();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);
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
