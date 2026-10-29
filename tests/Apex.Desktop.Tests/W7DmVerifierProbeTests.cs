using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Independent verifier probes (wave 7, track dm) over the reviewer's two fixes: the stock-item roll-back that
/// moved into a finally keyed on a committed flag, and the BOM alteration that keeps saved carve-out line types
/// with the F12 type switch off. V1 (no roll-back on ALTER) and V4 (flag set after the save) pin invariants no
/// other test in the suite reddens on when mutated. Every assertion reads the book back through a fresh handle.
/// </summary>
public sealed class W7DmVerifierProbeTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW7DmVerify_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, dir);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        window.KeyReleaseQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Cleanup(MainWindow window, string dir)
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

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    // ---------------------------------------------------------------- stock item: ALTER path refusal

    [AvaloniaFact]
    public void V1_a_refused_ALTER_never_removes_the_pre_existing_item()
    {
        const string company = "W7 Verify Alter Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            inv.CreateSimpleUnit("Box", "Boxes");
            var g = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
            var bolt = inv.CreateStockItem("Bolt", g.Id, nos.Id);
            new CompanyStorage(dir).Save(c);

            vm.ShowStockItemAlter(bolt.Id);
            Dispatcher.UIThread.RunJobs();
            var m = vm.StockItemMaster!;
            Assert.True(m.IsAltering);
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            m.AlternateUnitConversionText = "xyz";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("must be a number", m.Message ?? string.Empty);
            Assert.NotNull(c.FindStockItem(bolt.Id));
            Assert.Single(c.StockItems, i => i.Id == bolt.Id);

            // a later unrelated save must not drop it
            new CompanyStorage(dir).Save(c);
            Assert.Single(LoadBack(dir, company).StockItems, i => i.Id == bolt.Id);

            m.AlternateUnitConversionText = "6";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", m.Message ?? string.Empty);
            var back = Assert.Single(LoadBack(dir, company).StockItems, i => i.Id == bolt.Id);
            Assert.Equal(6m, back.AlternateUnitConversion);
        }
        finally { Cleanup(w, dir); }
    }

    // ---------------------------------------------------------------- stock item: success is not rolled back

    [AvaloniaFact]
    public void V2_a_successful_create_with_an_alternate_unit_is_kept_in_memory_and_on_disk()
    {
        const string company = "W7 Verify Create Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var inv = new InventoryService(vm.Company!);
            inv.CreateSimpleUnit("Nos", "Numbers");
            inv.CreateSimpleUnit("Box", "Boxes");
            if (vm.Company!.StockGroups.Count == 0) inv.CreateStockGroup("Primary");
            vm.ShowStockItemMaster();
            Dispatcher.UIThread.RunJobs();
            var m = vm.StockItemMaster!;
            m.Name = "Gear";
            m.SelectedUnit = m.Units.First(u => u.Symbol == "Nos");
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            m.AlternateUnitConversionText = "4";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message ?? string.Empty);
            var live = Assert.Single(vm.Company!.StockItems, i => i.Name == "Gear");

            var back = Assert.Single(LoadBack(dir, company).StockItems, i => i.Name == "Gear");
            Assert.Equal(live.Id, back.Id);
            Assert.Equal(4m, back.AlternateUnitConversion);
        }
        finally { Cleanup(w, dir); }
    }

    // ---------------------------------------------------------------- stock item: VAT parse refusal

    [AvaloniaFact]
    public void V3_an_unparsable_VAT_rate_on_create_leaves_no_phantom_and_the_retry_saves()
    {
        const string company = "W7 Verify Vat Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var c = vm.Company!;
            new VatService(c).EnableVat(tin: "29123456789");
            var inv = new InventoryService(c);
            inv.CreateSimpleUnit("Ltr", "Litres");
            if (c.StockGroups.Count == 0) inv.CreateStockGroup("Primary");
            new CompanyStorage(dir).Save(c);
            vm.ShowStockItemMaster();
            Dispatcher.UIThread.RunJobs();
            var m = vm.StockItemMaster!;
            Assert.True(m.ShowVatBlock);
            m.Name = "Diesel";
            m.SelectedUnit = m.Units.First(u => u.Symbol == "Ltr");
            m.SelectedNonGstGoodsClass = m.NonGstGoodsClasses.Single(o => o.Value == NonGstGoodsClass.HighSpeedDiesel);
            m.VatTaxRatePercentText = "abc";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("VAT Tax rate", m.Message ?? string.Empty);
            Assert.DoesNotContain(c.StockItems, i => i.Name == "Diesel");

            m.VatTaxRatePercentText = "14.5";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message ?? string.Empty);
            var back = Assert.Single(LoadBack(dir, company).StockItems, i => i.Name == "Diesel");
            Assert.Equal(1450, back.VatTaxRateBasisPoints);
        }
        finally { Cleanup(w, dir); }
    }

    // ---------------------------------------------------------------- stock item: an uncaught store failure

    /// <summary>Pins WHERE the committed flag is set: after the save, not before it. A save that fails with an
    /// exception the catch does not handle (here SQLite refusing a read-only book) must still take the new item
    /// back out of the open company. Windows-only: it relies on the read-only file attribute.</summary>
    [AvaloniaFact]
    public void V4_a_store_failure_on_create_rolls_back_the_in_memory_item()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string company = "W7 Verify Store Co";
        var (w, vm, dir) = NewWindow(company);
        var storage = new CompanyStorage(dir);
        var path = storage.PathForName(company);
        try
        {
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var g = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
            storage.Save(c);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(path, FileAttributes.ReadOnly);

            var m = new StockItemMasterViewModel(c, storage, () => { });
            m.Name = "Washer";
            m.SelectedGroup = m.Groups.First(x => x.Id == g.Id);
            m.SelectedUnit = m.Units.First(u => u.Id == nos.Id);
            Exception? thrown = null;
            bool? ok = null;
            try { ok = m.Create(); } catch (Exception ex) { thrown = ex; }
            Assert.True(thrown is not null || ok == false, "the save into a read-only book must not report success");
            Assert.DoesNotContain(c.StockItems, i => i.Name == "Washer");

            File.SetAttributes(path, FileAttributes.Normal);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.True(m.Create(), m.Message);
            Assert.Single(LoadBack(dir, company).StockItems, i => i.Name == "Washer");
        }
        finally
        {
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
            Cleanup(w, dir);
        }
    }

    // ---------------------------------------------------------------- BOM fixtures

    private static (Guid BomId, StockItem Desk, StockItem Plank, StockItem Offcut) SeedBom(
        MainWindowViewModel vm, string dir, bool withScrap, bool switchOnAfter)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var desk = inv.CreateStockItem("Desk", group.Id, unit.Id);
        var plank = inv.CreateStockItem("Plank", group.Id, unit.Id);
        var offcut = inv.CreateStockItem("Offcut", group.Id, unit.Id);
        c.SetComponentsBom = true;
        c.DefineBomComponentType = true;
        var lines = withScrap
            ? new[]
            {
                new BomLine(BomLineType.Component, plank.Id, 3m),
                new BomLine(BomLineType.Scrap, offcut.Id, 1m, null, Money.FromRupees(5m)),
            }
            : new[]
            {
                new BomLine(BomLineType.Component, plank.Id, 3m),
                new BomLine(BomLineType.Component, offcut.Id, 1m),
            };
        var bom = new BomService(c).CreateBom(desk.Id, "Main", 1m, lines);
        c.DefineBomComponentType = switchOnAfter;
        new CompanyStorage(dir).Save(c);
        return (bom.Id, desk, plank, offcut);
    }

    private static BomMasterViewModel OpenAlterViaItem(MainWindow w, MainWindowViewModel vm, Guid itemId)
    {
        vm.ShowStockItemAlter(itemId);
        Dispatcher.UIThread.RunJobs();
        vm.StockItemMaster!.AlterComponentsBom = true;
        Key(w, PhysicalKey.A, RawInputModifiers.Control);
        // Census 3.9 (wave 8): the tick now lands on the M BOM name list; Enter opens the highlighted BOM.
        Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
        Key(w, PhysicalKey.Enter);
        Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
        Assert.True(vm.BomMaster!.IsAltering);
        return vm.BomMaster!;
    }

    [AvaloniaFact]
    public void V5_a_deliberate_scrap_to_component_change_is_kept_with_the_switch_off()
    {
        const string company = "W7 Verify Bom Change Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var (bomId, desk, _, offcut) = SeedBom(vm, dir, withScrap: true, switchOnAfter: false);
            var b = OpenAlterViaItem(w, vm, desk.Id);
            var row = b.Lines.Single(l => l.SelectedItem?.Id == offcut.Id);
            Assert.Equal(BomLineType.Scrap, row.LineType);
            Assert.True(row.ShowTypePicker);
            row.SelectedType = row.TypeOptions.Single(t => t.Type == BomLineType.Component);
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", b.Message ?? string.Empty);

            var reloaded = Assert.Single(LoadBack(dir, company).BomsFor(desk.Id));
            Assert.Equal(bomId, reloaded.Id);
            var line = Assert.Single(reloaded.Lines, l => l.ComponentStockItemId == offcut.Id);
            Assert.Equal(BomLineType.Component, line.LineType);
            Assert.Null(line.Rate);
        }
        finally { Cleanup(w, dir); }
    }

    [AvaloniaFact]
    public void V6_a_deliberate_scrap_to_byproduct_change_is_kept_with_the_switch_off()
    {
        const string company = "W7 Verify Bom ByProd Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var (_, desk, _, offcut) = SeedBom(vm, dir, withScrap: true, switchOnAfter: false);
            var b = OpenAlterViaItem(w, vm, desk.Id);
            var row = b.Lines.Single(l => l.SelectedItem?.Id == offcut.Id);
            row.SelectedType = row.TypeOptions.Single(t => t.Type == BomLineType.ByProduct);
            row.CarveOutRateText = "7.25";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", b.Message ?? string.Empty);
            var line = Assert.Single(Assert.Single(LoadBack(dir, company).BomsFor(desk.Id)).Lines,
                l => l.ComponentStockItemId == offcut.Id);
            Assert.Equal(BomLineType.ByProduct, line.LineType);
            Assert.Equal(7.25m, line.Rate?.Amount);
        }
        finally { Cleanup(w, dir); }
    }

    [AvaloniaFact]
    public void V7_a_component_only_bom_with_the_switch_off_still_hides_the_type_picker()
    {
        const string company = "W7 Verify Bom Plain Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var (_, desk, plank, offcut) = SeedBom(vm, dir, withScrap: false, switchOnAfter: false);
            var b = OpenAlterViaItem(w, vm, desk.Id);
            Assert.False(b.ShowLineTypePicker);
            Assert.All(b.Lines, l => Assert.False(l.ShowTypePicker));
            Assert.All(b.Lines, l => Assert.Single(l.TypeOptions));
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", b.Message ?? string.Empty);
            var back = Assert.Single(LoadBack(dir, company).BomsFor(desk.Id));
            Assert.All(back.Lines, l => Assert.Equal(BomLineType.Component, l.LineType));
            Assert.Equal(2, back.Lines.Count);
        }
        finally { Cleanup(w, dir); }
    }

    [AvaloniaFact]
    public void V8_with_the_switch_on_an_unchanged_alter_still_keeps_scrap()
    {
        const string company = "W7 Verify Bom On Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var (_, desk, _, offcut) = SeedBom(vm, dir, withScrap: true, switchOnAfter: true);
            var b = OpenAlterViaItem(w, vm, desk.Id);
            Assert.True(b.ShowLineTypePicker);
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", b.Message ?? string.Empty);
            var line = Assert.Single(Assert.Single(LoadBack(dir, company).BomsFor(desk.Id)).Lines,
                l => l.ComponentStockItemId == offcut.Id);
            Assert.Equal(BomLineType.Scrap, line.LineType);
            Assert.Equal(5m, line.Rate?.Amount);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>The alteration-only widening must not leak: a BOM creation screen opened in the same session,
    /// with the switch still off, offers no carve-out types.</summary>
    [AvaloniaFact]
    public void V9_the_alter_widening_does_not_leak_into_a_creation_screen()
    {
        const string company = "W7 Verify Bom NewLine Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var (_, desk, _, _) = SeedBom(vm, dir, withScrap: true, switchOnAfter: false);
            var b = OpenAlterViaItem(w, vm, desk.Id);
            Assert.True(b.ShowLineTypePicker);

            var create = new BomMasterViewModel(vm.Company!, new CompanyStorage(dir), () => { });
            Assert.False(create.ShowLineTypePicker);
            Assert.All(create.Lines, l => Assert.Single(l.TypeOptions));
        }
        finally { Cleanup(w, dir); }
    }
}
