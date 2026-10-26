using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.9 — "ALTER COMPONENTS (BOM)" ON STOCK ITEM ALTERATION, DRIVEN ON A REAL WINDOW.</b>
///
/// <para><b>THE GAP, as the cell states it:</b> <i>"no <c>Alter Components (BOM)</c> tick on the Stock Item
/// Alteration screen"</i>. Create and Delete of a BOM were closed; there was no way to CHANGE one. The shape this
/// area was demoted for three times is an "alter" that really runs Create-and-append — a second BOM, or a fresh id
/// that orphans the <c>job_work_orders.fill_components_bom_id</c> foreign key. So every positive test here
/// asserts on the BOM re-read from SQLite through a FRESH store handle: exactly ONE BOM for the item, under its
/// ORIGINAL id, carrying the corrected lines.</para>
///
/// <para><b>FIDELITY (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/
/// was opened BY CONTENT on 2026-10-25: <i>"In the Stock Item Alteration screen, enable the option Alter
/// Components (BOM)"</i>. What is OURS is labelled ours: the F12-feature gating of the option (IV-185) and
/// opening the item's first BOM by name directly instead of the vendor's BOM-name list (IV-186).</para>
/// </summary>
public sealed class BomAlterComponentsTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexDmBomAlter_" + Guid.NewGuid().ToString("N"));
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

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? ActiveLabel(MainWindowViewModel vm) => vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow window, string name) where T : Control =>
        Descendants(window).OfType<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    private sealed record Seeded(Guid Table, Guid Leg, Guid Top, Guid Screw, Guid BomId);

    /// <summary>A finished good "Table" with ONE BOM "Standard" (4 Leg + 1 Top per block of 1), plus a spare
    /// component "Screw" the correction adds. The BOM feature is on because a BOM exists.</summary>
    private static Seeded Seed(MainWindowViewModel vm, string dir)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var table = inv.CreateStockItem("Table", group.Id, unit.Id);
        var leg = inv.CreateStockItem("Leg", group.Id, unit.Id);
        var top = inv.CreateStockItem("Top", group.Id, unit.Id);
        var screw = inv.CreateStockItem("Screw", group.Id, unit.Id);
        var bom = new BomService(c).CreateBom(table.Id, "Standard", 1m, new[]
        {
            new BomLine(BomLineType.Component, leg.Id, 4m),
            new BomLine(BomLineType.Component, top.Id, 1m),
        });
        c.SetComponentsBom = true;
        new CompanyStorage(dir).Save(c);
        return new Seeded(table.Id, leg.Id, top.Id, screw.Id, bom.Id);
    }

    /// <summary>Gateway → Create → Stock Item → arrow onto "Table" → Ctrl+Enter, by keyboard only.</summary>
    private static StockItemMasterViewModel OpenTableAlteration(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Stock Item");
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        for (var i = 0; i < 10 && vm.StockItemMaster!.HighlightedRow?.Name != "Table"; i++)
            Key(window, PhysicalKey.ArrowDown);
        Assert.Equal("Table", vm.StockItemMaster!.HighlightedRow?.Name);
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
        var alter = vm.StockItemMaster!;
        Assert.True(alter.IsAltering);
        return alter;
    }

    /// <summary>Ticks the realised "Alter Components (BOM)" box with the KEYBOARD (focus + Space).</summary>
    private static void TickAlterComponentsByKeyboard(MainWindow window)
    {
        var box = Named<CheckBox>(window, "StockItemAlterComponentsBom");
        Assert.NotNull(box);
        box!.Focus();
        Dispatcher.UIThread.RunJobs();
        Key(window, PhysicalKey.Space);
        Assert.True(box.IsChecked);
    }

    private static void AssertLoadedStandard(BomMasterViewModel bom, Seeded s)
    {
        Assert.True(bom.IsAltering);
        Assert.Equal("Stock Item Components (BOM) Alteration", bom.Caption);
        Assert.Equal("Standard", bom.Name);
        Assert.Equal(s.Table, bom.SelectedFinishedGood!.Id);
        Assert.Equal("1", bom.UnitOfManufactureText);
        var filled = bom.Lines.Where(l => !l.IsBlank).ToList();
        Assert.Equal(2, filled.Count);
        Assert.Equal(s.Leg, filled[0].SelectedItem!.Id);
        Assert.Equal("4", filled[0].QuantityText);
        Assert.Equal(s.Top, filled[1].SelectedItem!.Id);
        Assert.Equal("1", filled[1].QuantityText);
        Assert.True(bom.Lines[^1].IsBlank, "a blank trailing row must be present for adding a component");
    }

    private static void KeyCorrection(BomMasterViewModel bom, Seeded s)
    {
        bom.Lines.First(l => l.SelectedItem?.Id == s.Leg).QuantityText = "6";
        var blank = bom.Lines[^1];
        blank.SelectedItem = blank.ItemOptions.First(i => i.Id == s.Screw);
        blank.QuantityText = "8";
    }

    private static void AssertAlteredInPlace(Company reloaded, Seeded s)
    {
        var bom = Assert.Single(reloaded.BomsFor(s.Table));
        Assert.Equal(s.BomId, bom.Id);                        // the ORIGINAL BOM, not an appended second one
        Assert.Equal("Standard", bom.Name);
        Assert.Single(reloaded.BillsOfMaterials);
        Assert.Equal(3, bom.Lines.Count);
        Assert.Equal(6m, bom.Lines.Single(l => l.ComponentStockItemId == s.Leg).QuantityPerBlock);
        Assert.Equal(1m, bom.Lines.Single(l => l.ComponentStockItemId == s.Top).QuantityPerBlock);
        Assert.Equal(8m, bom.Lines.Single(l => l.ComponentStockItemId == s.Screw).QuantityPerBlock);
    }

    // ================================================================= 1. keyboard, end to end

    [AvaloniaFact]
    public void Ticking_Alter_Components_and_CtrlA_opens_the_BOM_prefilled_and_CtrlA_alters_it_in_place()
    {
        const string company = "Dm Bom Alter Key Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            OpenTableAlteration(window, vm);
            TickAlterComponentsByKeyboard(window);

            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            var bom = vm.BomMaster!;
            AssertLoadedStandard(bom, s);
            Assert.Equal("Stock Item Components (BOM) Alteration", Named<TextBlock>(window, "BomMasterCaption")?.Text);

            KeyCorrection(bom, s);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Contains("altered", bom.Message ?? string.Empty);
            AssertAlteredInPlace(LoadBack(dir, company), s);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= 2. pointer route agrees

    [AvaloniaFact]
    public void Clicking_the_BOM_commit_button_on_the_components_alteration_alters_in_place()
    {
        const string company = "Dm Bom Alter Click Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            OpenTableAlteration(window, vm);
            TickAlterComponentsByKeyboard(window);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var bom = vm.BomMaster!;
            AssertLoadedStandard(bom, s);

            KeyCorrection(bom, s);
            Dispatcher.UIThread.RunJobs();
            var button = Named<Button>(window, "BomCommitButton");
            Assert.NotNull(button);
            Assert.Equal("Save (Ctrl+A)", button!.Content);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("altered", bom.Message ?? string.Empty);
            AssertAlteredInPlace(LoadBack(dir, company), s);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= 3. visibility gates

    /// <summary>The option exists on the ALTERATION screen only, and only while the BOM feature is on.</summary>
    [AvaloniaFact]
    public void Alter_Components_is_offered_only_on_alteration_and_only_while_the_BOM_feature_is_on()
    {
        var (window, vm, dir) = NewWindow("Dm Bom Alter Gate Co");
        try
        {
            Seed(vm, dir);
            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Stock Item");
            Assert.False(vm.StockItemMaster!.IsAltering);
            Assert.Null(Named<CheckBox>(window, "StockItemAlterComponentsBom"));   // never on Creation

            for (var i = 0; i < 10 && vm.StockItemMaster!.HighlightedRow?.Name != "Table"; i++)
                Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.NotNull(Named<CheckBox>(window, "StockItemAlterComponentsBom"));

            // Feature off → a fresh alteration screen does not offer it, and a ticked flag opens nothing.
            vm.Company!.SetComponentsBom = false;
            vm.ShowStockItemAlter(vm.Company!.FindStockItemByName("Table")!.Id);
            Dispatcher.UIThread.RunJobs();
            var alter = vm.StockItemMaster!;
            Assert.False(alter.ShowAlterComponentsBom);
            Assert.Null(Named<CheckBox>(window, "StockItemAlterComponentsBom"));
            alter.AlterComponentsBom = true;
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.Null(vm.BomMaster);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Accepting the alteration WITHOUT the tick stays on the item — the option is a request, not a
    /// default — and the standalone BOM master opened from the Create menu is still a CREATE screen.</summary>
    [AvaloniaFact]
    public void Without_the_tick_the_item_saves_and_no_BOM_screen_opens()
    {
        var (window, vm, dir) = NewWindow("Dm Bom Alter NoTick Co");
        try
        {
            Seed(vm, dir);
            OpenTableAlteration(window, vm);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.Contains("altered", vm.StockItemMaster!.Message ?? string.Empty);
            Assert.Null(vm.BomMaster);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= 4. engine: id kept, guards hold

    [Fact]
    public void AlterBom_keeps_the_id_and_refuses_a_name_another_BOM_of_the_item_owns()
    {
        var c = new Company(Guid.NewGuid(), "Dm Bom Engine Co", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1));
        var inv = new InventoryService(c);
        var g = inv.CreateStockGroup("Primary");
        var u = inv.CreateSimpleUnit("Nos", "Numbers");
        var fg = inv.CreateStockItem("Table", g.Id, u.Id);
        var leg = inv.CreateStockItem("Leg", g.Id, u.Id);
        var svc = new BomService(c);
        var a = svc.CreateBom(fg.Id, "Standard", 1m, new[] { new BomLine(BomLineType.Component, leg.Id, 4m) });
        svc.CreateBom(fg.Id, "Export", 1m, new[] { new BomLine(BomLineType.Component, leg.Id, 5m) });

        var altered = svc.AlterBom(a.Id, "Standard", 2m, new[] { new BomLine(BomLineType.Component, leg.Id, 9m) });
        Assert.Equal(a.Id, altered.Id);
        Assert.Equal(2, c.BillsOfMaterials.Count);
        Assert.Same(altered, c.BillsOfMaterials[0]);               // swapped at its own index
        Assert.Equal(9m, c.FindBillOfMaterials(a.Id)!.Lines.Single().QuantityPerBlock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.AlterBom(a.Id, "export", 1m, new[] { new BomLine(BomLineType.Component, leg.Id, 1m) }));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("Standard", c.FindBillOfMaterials(a.Id)!.Name); // refused → untouched
    }
}
