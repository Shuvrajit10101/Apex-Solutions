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
/// Wave 7, track dm — the REVIEWER's own probes, on fixtures the builder did not write. Each one drives the real
/// window's key handlers and asserts on the book re-read from SQLite through a FRESH store handle.
/// </summary>
public sealed class W7DmReviewerProbeTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW7DmReview_" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------ T2-158(2), the non-numeric factor route

    /// <summary>
    /// A NON-NUMERIC alternate-unit factor is a refused alternate unit too. It is refused by a <c>return false</c>
    /// inside the try, after <c>CreateStockItem</c> has already added the item — not by a throw — so a roll-back
    /// that lives only in the catch never sees it.
    /// </summary>
    [AvaloniaFact]
    public void A_non_numeric_alternate_factor_leaves_no_phantom_item_and_the_retry_succeeds()
    {
        const string company = "W7 Dm Review Phantom Co";
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

            m.Name = "Sprocket";
            m.SelectedUnit = m.Units.First(u => u.Symbol == "Nos");
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            m.AlternateUnitConversionText = "ten";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Contains("must be a number", m.Message ?? string.Empty);
            Assert.DoesNotContain(vm.Company!.StockItems, i => i.Name == "Sprocket");

            m.AlternateUnitConversionText = "12";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message ?? string.Empty);

            var item = Assert.Single(LoadBack(dir, company).StockItems, i => i.Name == "Sprocket");
            Assert.Equal(12m, item.AlternateUnitConversion);
        }
        finally { Cleanup(w, dir); }
    }

    // ------------------------------------------------------------------ census 3.9, a carve-out line survives

    /// <summary>
    /// A BOM saved with a Scrap line (rate 5.00) while "Define type of component for BOM" was on, then that F12
    /// switch turned off. Opening the BOM through Alter Components (BOM) and accepting it UNCHANGED must not turn
    /// the Scrap output into a consumed Component or drop its carve-out rate.
    /// </summary>
    [AvaloniaFact]
    public void An_unchanged_components_alteration_keeps_a_scrap_line_after_the_type_switch_is_off()
    {
        const string company = "W7 Dm Review Scrap Co";
        var (w, vm, dir) = NewWindow(company);
        try
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
            var bom = new BomService(c).CreateBom(desk.Id, "Main", 1m, new[]
            {
                new BomLine(BomLineType.Component, plank.Id, 3m),
                new BomLine(BomLineType.Scrap, offcut.Id, 1m, null, Money.FromRupees(5m)),
            });
            c.DefineBomComponentType = false;   // the operator later switches the F12 option off
            new CompanyStorage(dir).Save(c);

            vm.ShowStockItemAlter(desk.Id);
            Dispatcher.UIThread.RunJobs();
            vm.StockItemMaster!.AlterComponentsBom = true;
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.True(vm.BomMaster!.IsAltering);

            // Accept the BOM exactly as loaded — twice, since the screen stays over the BOM after a save and the
            // second accept runs against the picker lists the first save rebuilt.
            for (var pass = 0; pass < 2; pass++)
            {
                Key(w, PhysicalKey.A, RawInputModifiers.Control);
                Assert.Contains("altered", vm.BomMaster!.Message ?? string.Empty);

                var reloaded = Assert.Single(LoadBack(dir, company).BomsFor(desk.Id));
                Assert.Equal(bom.Id, reloaded.Id);
                var scrap = Assert.Single(reloaded.Lines, l => l.ComponentStockItemId == offcut.Id);
                Assert.Equal(BomLineType.Scrap, scrap.LineType);
                Assert.Equal(5m, scrap.Rate?.Amount);
                Assert.Equal(BomLineType.Component,
                    Assert.Single(reloaded.Lines, l => l.ComponentStockItemId == plank.Id).LineType);
            }
        }
        finally { Cleanup(w, dir); }
    }

    // ------------------------------------------------------------------ census 3.12, P1c on a fixture of my own

    /// <summary>
    /// P1c on a GROUP-scoped, fully ADVANCED definition (40 / 10, 2 Weeks, Lower) — every field the builder's
    /// Item-scoped fixture does not exercise. Create over it keying only 99, Ctrl+A: nothing saved, the screen is
    /// an alteration showing the saved figures; Ctrl+A again with nothing re-keyed must keep the id and every
    /// figure, re-read through a fresh handle.
    /// </summary>
    [AvaloniaFact]
    public void Create_over_an_occupied_advanced_group_definition_keeps_every_field()
    {
        const string company = "W7 Dm Review Reorder Co";
        var (w, vm, dir) = NewWindow(company);
        try
        {
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var g = inv.CreateStockGroup("Hardware");
            var def = new ReorderLevelsService(c).Create(ReorderScope.Group, g.Id, reorderAdvanced: true, 40m,
                minQtyAdvanced: true, 10m, 2, ExpiryPeriodUnit.Weeks, ReorderCriteria.Lower);
            new CompanyStorage(dir).Save(c);

            vm.ShowReorderLevelsMaster();
            Dispatcher.UIThread.RunJobs();
            var m = vm.ReorderLevels!;
            Assert.False(m.IsAltering);
            m.SelectedScope = m.Scopes.First(s => s.Scope == ReorderScope.Group);
            m.SelectedTarget = m.Targets.First(t => t.Id == g.Id);
            m.ReorderQuantityText = "99";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);

            Assert.True(m.IsAltering);
            Assert.Contains("'Hardware'", m.Message ?? string.Empty);
            Assert.Equal("40", m.ReorderQuantityText);
            var untouched = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(def.Id, untouched.Id);
            Assert.Equal(40m, untouched.ReorderQuantity);

            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            var after = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(def.Id, after.Id);
            Assert.True(after.ReorderAdvanced);
            Assert.True(after.MinQtyAdvanced);
            Assert.Equal(40m, after.ReorderQuantity);
            Assert.Equal(10m, after.MinOrderQuantity);
            Assert.Equal(2, after.PeriodCount);
            Assert.Equal(ExpiryPeriodUnit.Weeks, after.PeriodUnit);
            Assert.Equal(ReorderCriteria.Lower, after.Criteria);
        }
        finally { Cleanup(w, dir); }
    }
}
