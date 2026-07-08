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
/// End-to-end coverage for the Phase-6 Cluster-2 (BOM &amp; Manufacturing Journal) UI surfaced in the cascade
/// (requirements RQ-9..RQ-15, RQ-52, RQ-54): the F12 "Set Components (BOM)" gate, the per-item BOM switch, the
/// BOM master (create + per-item uniqueness + carve-out type picker gating), and the Manufacturing-Journal
/// voucher (auto-scaled consumption, FG value = components + add'l − carve-outs, engine-routed posting into
/// Stock Summary). Drives the real shell + page view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class BomManufacturingViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BomManufacturingViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBomTests_" + Guid.NewGuid().ToString("N"));
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

    private StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindStockGroupByName(name)!;
    }

    private Unit CreateUnit(MainWindowViewModel vm, string symbol)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = symbol;
        m.DecimalPlacesText = "0";
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindUnitByName(symbol)!;
    }

    /// <summary>Turns the F12 "Set Components (BOM)" (and optionally the type-picker) config on through the config screen.</summary>
    private void EnableBomFeature(MainWindowViewModel vm, bool defineTypes = true)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.SetComponentsBom = true;
        if (defineTypes) vm.GstConfig!.DefineBomComponentType = true;
        Assert.True(vm.Company!.SetComponentsBom);
        vm.Back();
    }

    private StockItem CreateItem(MainWindowViewModel vm, string name, StockGroup group, Unit unit,
        decimal? openingQty = null, decimal openingRate = 0m)
    {
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = name;
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        if (openingQty is { } q)
        {
            m.OpeningGodown = m.Godowns.First(g => g.IsMainLocation);
            m.OpeningQuantityText = q.ToString(System.Globalization.CultureInfo.InvariantCulture);
            m.OpeningRateText = openingRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindStockItemByName(name)!;
    }

    // ---------------------------------------------------------------- (1) F12 config gate (RQ-10/RQ-52)

    [Fact]
    public void Config_off_hides_item_bom_switch_bom_master_and_manufacturing_journal()
    {
        var vm = NewSeededCompany("BOM Off Co");

        // Off by default → the item switch is hidden.
        vm.ShowStockItemMaster();
        Assert.False(vm.StockItemMaster!.ShowBomSwitch);
        vm.Back();

        // Create menu carries no "Bill of Materials"; Inventory Vouchers carries no "Manufacturing Journal".
        vm.ShowCreateMenu();
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Bill of Materials");
        vm.ShowInventoryVouchersMenu();
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Manufacturing Journal");

        // The openers are hard no-ops while the config is off.
        vm.ShowBomMaster();
        Assert.Null(vm.BomMaster);
        vm.OpenManufacturingJournal();
        Assert.Null(vm.ManufacturingJournalEntry);
        Assert.NotEqual(Screen.ManufacturingJournalEntry, vm.CurrentScreen);
    }

    [Fact]
    public void Config_on_surfaces_item_switch_bom_master_and_manufacturing_journal()
    {
        var vm = NewSeededCompany("BOM On Co");
        EnableBomFeature(vm);

        vm.ShowStockItemMaster();
        Assert.True(vm.StockItemMaster!.ShowBomSwitch);
        vm.Back();

        // "Bill of Materials" appears under Masters → Create (nested under Inventory Masters).
        vm.ShowCreateMenu();
        Assert.Contains(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Bill of Materials");
        Assert.Contains(vm.Menu, x => x.IsHeader && x.Label == "Inventory Masters");

        // "Manufacturing Journal" appears under Vouchers → Inventory Vouchers.
        vm.ShowInventoryVouchersMenu();
        Assert.Contains(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Manufacturing Journal");
    }

    // ---------------------------------------------------------------- (2) BOM master (RQ-9/RQ-10)

    [Fact]
    public void Bom_is_created_through_the_master_and_persists()
    {
        const string companyName = "BOM Master Co";
        var vm = NewSeededCompany(companyName);
        EnableBomFeature(vm);
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var comp1 = CreateItem(vm, "Resin", raw, unit, openingQty: 100m, openingRate: 10m);
        var comp2 = CreateItem(vm, "Pigment", raw, unit, openingQty: 100m, openingRate: 4m);
        var scrap = CreateItem(vm, "Trimmings", raw, unit);
        var fg = CreateItem(vm, "Panel", fgGroup, unit);

        vm.ShowBomMaster();
        Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
        var m = vm.BomMaster!;
        Assert.True(m.CanCreate);
        Assert.True(m.ShowLineTypePicker);          // type-picker config on
        m.Name = "Standard";
        m.SelectedFinishedGood = m.FinishedGoods.Single(i => i.Id == fg.Id);
        m.UnitOfManufactureText = "1";

        // Two component lines + one scrap carve-out line.
        var l1 = m.Lines[0];
        l1.SelectedItem = l1.ItemOptions.Single(i => i.Id == comp1.Id);
        l1.QuantityText = "2";
        var l2 = m.Lines.Last(l => l.IsBlank);   // trailing blank spawned by the change
        l2.SelectedItem = l2.ItemOptions.Single(i => i.Id == comp2.Id);
        l2.QuantityText = "1";
        var l3 = m.Lines.Last(l => l.IsBlank);
        l3.SelectedItem = l3.ItemOptions.Single(i => i.Id == scrap.Id);
        l3.SelectedType = l3.TypeOptions.Single(t => t.Type == BomLineType.Scrap);
        l3.QuantityText = "1";
        l3.CarveOutRateText = "2.00";

        Assert.True(m.Create());
        Assert.Null(m.Message is { } msg && msg.Contains("must") ? msg : null);

        var bom = vm.Company!.FindBomByName(fg.Id, "Standard")!;
        Assert.Equal(1m, bom.UnitOfManufacture);
        Assert.Equal(2, bom.ComponentLines.Count());
        Assert.Single(bom.CarveOutLines);
        Assert.True(fg.SetComponents);       // creating a BOM flips the item flag on (RQ-10)
        Assert.Contains(m.Existing, r => r.Name == "Standard" && r.FinishedGood == "Panel");

        // PERSISTED: reload and the BOM survives.
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        var reloaded = _storage.Load(entry);
        Assert.NotNull(reloaded.FindBomByName(fg.Id, "Standard"));
        Assert.True(reloaded.SetComponentsBom);     // inferred from the persisted BOM (RQ-52)
    }

    [Fact]
    public void Duplicate_bom_name_within_an_item_is_rejected()
    {
        var vm = NewSeededCompany("Dupe BOM Co");
        EnableBomFeature(vm);
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var comp = CreateItem(vm, "Steel", raw, unit, openingQty: 50m, openingRate: 20m);
        var fg = CreateItem(vm, "Bracket", fgGroup, unit);

        vm.ShowBomMaster();
        var m = vm.BomMaster!;
        m.Name = "Recipe";
        m.SelectedFinishedGood = m.FinishedGoods.Single(i => i.Id == fg.Id);
        m.Lines[0].SelectedItem = m.Lines[0].ItemOptions.Single(i => i.Id == comp.Id);
        m.Lines[0].QuantityText = "1";
        Assert.True(m.Create());

        m.Name = "Recipe";
        m.SelectedFinishedGood = m.FinishedGoods.Single(i => i.Id == fg.Id);
        m.Lines[0].SelectedItem = m.Lines[0].ItemOptions.Single(i => i.Id == comp.Id);
        m.Lines[0].QuantityText = "1";
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);
    }

    [Fact]
    public void Type_picker_is_hidden_when_the_define_type_config_is_off()
    {
        var vm = NewSeededCompany("No Type Co");
        EnableBomFeature(vm, defineTypes: false);   // BOM on, type-picker off

        vm.ShowBomMaster();
        var m = vm.BomMaster!;
        Assert.False(m.ShowLineTypePicker);
        // Only the Component type is offered on a line.
        Assert.Single(m.Lines[0].TypeOptions);
        Assert.Equal(BomLineType.Component, m.Lines[0].TypeOptions[0].Type);
    }

    [Fact]
    public void Bom_needs_at_least_one_component_line()
    {
        var vm = NewSeededCompany("No Component Co");
        EnableBomFeature(vm);
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var fg = CreateItem(vm, "Widget", fgGroup, unit);

        vm.ShowBomMaster();
        var m = vm.BomMaster!;
        m.Name = "Empty";
        m.SelectedFinishedGood = m.FinishedGoods.Single(i => i.Id == fg.Id);
        Assert.False(m.Create());
        Assert.Contains("component", m.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- (3) Manufacturing Journal (RQ-11..RQ-15)

    /// <summary>Seeds a company with a 2-component BOM (+ scrap carve-out) on a finished good, with components in stock.</summary>
    private (StockItem Fg, BillOfMaterials Bom) SeedBom(MainWindowViewModel vm)
    {
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var comp1 = CreateItem(vm, "Resin", raw, unit, openingQty: 100m, openingRate: 10m);   // ₹10/unit
        var comp2 = CreateItem(vm, "Pigment", raw, unit, openingQty: 100m, openingRate: 4m);  // ₹4/unit
        var scrap = CreateItem(vm, "Trimmings", raw, unit);
        var fg = CreateItem(vm, "Panel", fgGroup, unit);

        var bomSvc = new BomService(vm.Company!);
        var bom = bomSvc.CreateBom(fg.Id, "Standard", 1m, new[]
        {
            new BomLine(BomLineType.Component, comp1.Id, 2m),   // 2 × ₹10 = ₹20
            new BomLine(BomLineType.Component, comp2.Id, 1m),   // 1 × ₹4  = ₹4
            new BomLine(BomLineType.Scrap, scrap.Id, 1m, rate: Money.FromRupees(2m)),  // carve out ₹2
        });
        _storage.Save(vm.Company!);
        return (fg, bom);
    }

    [Fact]
    public void Manufacturing_journal_shows_engine_breakdown_and_posts_into_stock_summary()
    {
        var vm = NewSeededCompany("Mfg Co");
        EnableBomFeature(vm);
        var (fg, bom) = SeedBom(vm);
        var mainLoc = vm.Company!.MainLocation!;

        vm.OpenManufacturingJournal();
        Assert.Equal(Screen.ManufacturingJournalEntry, vm.CurrentScreen);
        var e = vm.ManufacturingJournalEntry!;

        // A Manufacturing-Journal voucher type was created on first use (RQ-11).
        Assert.Contains(vm.Company!.VoucherTypes, t => t.IsManufacturingJournal);

        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "10";      // 10 units
        e.ConsumptionGodown = mainLoc;
        e.ProductionGodown = mainLoc;

        // Engine preview: consumption auto-scaled (RQ-12) — 20 Resin + 10 Pigment.
        Assert.True(e.HasPreview);
        Assert.Contains(e.Consumption, r => r.Item == "Resin" && r.Quantity.StartsWith("20"));
        Assert.Contains(e.Consumption, r => r.Item == "Pigment" && r.Quantity.StartsWith("10"));

        // Breakdown (RQ-13): components = 20×10 + 10×4 = ₹240; carve-out = 10 scrap × ₹2 = ₹20;
        // FG value = 240 + 0 − 20 = ₹220; unit rate = ₹22.00.
        Assert.Equal("₹240.00", e.ComponentCostText);
        Assert.Equal("₹0.00", e.AdditionalCostText);
        Assert.Equal("₹20.00", e.CarveOutText);
        Assert.Equal("₹220.00", e.FinishedGoodValueText);
        Assert.StartsWith("₹22.00", e.FinishedGoodUnitRateText);
        Assert.Contains(e.CarveOuts, r => r.Item == "Trimmings" && r.Kind == "Scrap");

        // Accept posts through the engine into Stock Summary (RQ-15).
        Assert.True(e.Accept());
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var valuation = new StockValuationService(vm.Company!);
        var fgClosing = valuation.ClosingValue(fg.Id, vm.Company!.InventoryVouchers.Max(v => v.Date));
        Assert.Equal(10m, fgClosing.Quantity);              // 10 finished units on hand
        Assert.Equal(220.00m, fgClosing.Value.Amount);      // ₹220 to the paisa (PR-4)
    }

    [Fact]
    public void Additional_cost_adds_to_finished_good_value_not_pl()
    {
        var vm = NewSeededCompany("Add Cost Co");
        EnableBomFeature(vm);
        var (fg, bom) = SeedBom(vm);
        var mainLoc = vm.Company!.MainLocation!;

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "10";
        e.ConsumptionGodown = mainLoc;
        e.ProductionGodown = mainLoc;

        // Add a ₹50 labour cost → FG value = 240 + 50 − 20 = ₹270.
        var costRow = e.AdditionalCosts[0];
        costRow.Name = "Labour";
        costRow.AmountText = "50.00";

        Assert.True(e.HasPreview);
        Assert.Equal("₹50.00", e.AdditionalCostText);
        Assert.Equal("₹270.00", e.FinishedGoodValueText);

        Assert.True(e.Accept());
        // The manufacturing journal books no accounting entry (no P&L expense) — it is a pure stock voucher.
        // The FG stock value carries the additional cost.
        var valuation = new StockValuationService(vm.Company!);
        var fgClosing = valuation.ClosingValue(fg.Id, vm.Company!.InventoryVouchers.Max(v => v.Date));
        Assert.Equal(270.00m, fgClosing.Value.Amount);
    }

    [Fact]
    public void Manufacture_short_of_components_is_rejected_without_posting()
    {
        var vm = NewSeededCompany("Short Co");
        EnableBomFeature(vm);
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var comp = CreateItem(vm, "Resin", raw, unit, openingQty: 5m, openingRate: 10m);   // only 5 in stock
        var fg = CreateItem(vm, "Panel", fgGroup, unit);
        var bomSvc = new BomService(vm.Company!);
        var bom = bomSvc.CreateBom(fg.Id, "Standard", 1m, new[]
        {
            new BomLine(BomLineType.Component, comp.Id, 2m),   // needs 2/unit → 20 for 10 units
        });
        _storage.Save(vm.Company!);

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "10";                       // needs 20 Resin, only 5 on hand
        e.ConsumptionGodown = vm.Company!.MainLocation!;
        e.ProductionGodown = vm.Company!.MainLocation!;

        Assert.False(e.Accept());
        Assert.NotNull(e.Message);
        // Nothing posted.
        Assert.DoesNotContain(vm.Company!.InventoryVouchers,
            v => v.DestinationAllocations.Any(a => a.StockItemId == fg.Id));
    }

    [Fact]
    public void No_bom_yet_shows_a_friendly_empty_state()
    {
        var vm = NewSeededCompany("Empty Mfg Co");
        EnableBomFeature(vm);

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        Assert.False(e.HasPreview);
        Assert.NotNull(e.Message);
        Assert.Contains("BOM", e.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.False(e.Accept());
    }

    // ---------------------------------------------------------------- (4) no "Tally" anywhere (ER-8)

    [Fact]
    public void Bom_and_manufacturing_ui_carry_no_tally_text()
    {
        var vm = NewSeededCompany("Brand Co");
        EnableBomFeature(vm);
        var (fg, bom) = SeedBom(vm);
        var mainLoc = vm.Company!.MainLocation!;

        vm.ShowBomMaster();
        foreach (var r in vm.BomMaster!.Existing)
        {
            Assert.DoesNotContain("Tally", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tally", r.FinishedGood, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tally", r.Components, StringComparison.OrdinalIgnoreCase);
        }
        vm.Back();

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "1";
        e.ConsumptionGodown = mainLoc;
        e.ProductionGodown = mainLoc;

        Assert.DoesNotContain("Tally", e.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", e.TypeName, StringComparison.OrdinalIgnoreCase);
        foreach (var r in e.Consumption)
            Assert.DoesNotContain("Tally", r.Item, StringComparison.OrdinalIgnoreCase);
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
