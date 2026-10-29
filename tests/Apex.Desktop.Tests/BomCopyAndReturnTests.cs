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
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.9 — Ctrl+C COPY BoM, AND ESCAPE BACK TO THE M BOM LIST (wave 9, track fb).</b>
///
/// <para><b>THE TWO OPEN CLAUSES.</b> (1) <i>"In the Stock Item Components (BOM) screen, press Ctrl+C (Copy BoM)."</i>
/// had zero hits in <c>src/</c>. (2) IV-227: Escape after a create or an alteration on the components screen fell
/// to the menu, not back to the M BOM list.</para>
///
/// <para><b>FIDELITY (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/,
/// opened BY CONTENT 2026-10-29: <i>"In the Copy BoM Details screen, select the stock item and component of BoM."</i>;
/// <i>"The components of Standard BoM will get copied to Customer B BoM."</i>; <i>"Users have to define the Unit of
/// Manufacture and Quantity of inputs (components)."</i>. The page's screenshot of the result shows each copied line
/// with Godown, Type of Item and Quantity filled and the Unit of manufacture EMPTY.</para>
///
/// <para><b>THE BAR.</b> Keyboard only, through the window's real key handlers. Every positive assertion is on the
/// book re-read from SQLite through a FRESH <see cref="CompanyStorage"/>. Quantities, rates and the source's unit
/// of manufacture are odd (0.375, 12.35, 5) so a dropped field or a copied unit cannot pass by accident.</para>
/// </summary>
public sealed class BomCopyAndReturnTests
{
    // ================================================================= harness (the BomNameListTests shape)

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexFbBomCopy_" + Guid.NewGuid().ToString("N"));
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

    private static List<string> RealisedNames(MainWindow window)
    {
        var list = Named<ItemsControl>(window, "BomNameListRows");
        Assert.NotNull(list);
        return Descendants(list!).OfType<TextBlock>()
            .Where(t => t.DataContext is BomNameRow row && t.Text == row.Name)
            .Select(t => t.Text!)
            .ToList();
    }

    private static List<string> RealisedCopyOptions(MainWindow window)
    {
        var list = Named<ItemsControl>(window, "CopyBomDetailsOptions");
        Assert.NotNull(list);
        return Descendants(list!).OfType<TextBlock>()
            .Where(t => t.DataContext is CopyBomOption o && t.Text == o.Name)
            .Select(t => t.Text!)
            .ToList();
    }

    private static object? Focused(MainWindow window) => window.FocusManager?.GetFocusedElement();

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    private sealed record Seeded(
        Guid Table, Guid Leg, Guid Top, Guid Sawdust, Guid Chair, Guid Seat, Guid Offcut, Guid Store,
        Guid StandardId, Guid ExportId, Guid ChairBasicId, string UnitSymbol);

    /// <summary>
    /// "Table" with "Standard" (unit of mfg 5: 4 Leg @ Store, 1 Top, 0.375 Sawdust as SCRAP at ₹12.35) and
    /// "Export" (1 Leg); "Chair" with "Basic" (2 Seat, 0.125 Offcut as BY-PRODUCT at 7.5 %).
    /// </summary>
    private static Seeded Seed(MainWindowViewModel vm, string dir, bool defineType)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var table = inv.CreateStockItem("Table", group.Id, unit.Id);
        var leg = inv.CreateStockItem("Leg", group.Id, unit.Id);
        var top = inv.CreateStockItem("Top", group.Id, unit.Id);
        var sawdust = inv.CreateStockItem("Sawdust", group.Id, unit.Id);
        var chair = inv.CreateStockItem("Chair", group.Id, unit.Id);
        var seat = inv.CreateStockItem("Seat", group.Id, unit.Id);
        var offcut = inv.CreateStockItem("Offcut", group.Id, unit.Id);
        var store = inv.CreateGodown("Store");

        var svc = new BomService(c);
        var std = svc.CreateBom(table.Id, "Standard", 5m, new[]
        {
            new BomLine(BomLineType.Component, leg.Id, 4m, store.Id),
            new BomLine(BomLineType.Component, top.Id, 1m),
            new BomLine(BomLineType.Scrap, sawdust.Id, 0.375m, rate: Money.FromRupees(12.35m)),
        }).Id;
        var exp = svc.CreateBom(table.Id, "Export", 1m, new[]
        {
            new BomLine(BomLineType.Component, leg.Id, 1m),
        }).Id;
        var basic = svc.CreateBom(chair.Id, "Basic", 1m, new[]
        {
            new BomLine(BomLineType.Component, seat.Id, 2m),
            new BomLine(BomLineType.ByProduct, offcut.Id, 0.125m, percentOfFinishedGoodCost: 7.5m),
        }).Id;
        c.SetComponentsBom = true;
        c.DefineBomComponentType = defineType;
        new CompanyStorage(dir).Save(c);
        return new Seeded(table.Id, leg.Id, top.Id, sawdust.Id, chair.Id, seat.Id, offcut.Id, store.Id, std, exp, basic, unit.Symbol);
    }

    /// <summary>Gateway → Create → Stock Item → arrow onto <paramref name="item"/> → Ctrl+Enter → tick
    /// "Alter Components (BOM)" with Space → Ctrl+A. Lands on the M BOM name list.</summary>
    private static BomNameListViewModel OpenNameListByKeyboard(MainWindow window, MainWindowViewModel vm, string item = "Table")
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Stock Item");
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        for (var i = 0; i < 12 && vm.StockItemMaster!.HighlightedRow?.Name != item; i++)
            Key(window, PhysicalKey.ArrowDown);
        Assert.Equal(item, vm.StockItemMaster!.HighlightedRow?.Name);
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.True(vm.StockItemMaster!.IsAltering);

        var box = Named<CheckBox>(window, "StockItemAlterComponentsBom");
        Assert.NotNull(box);
        box!.Focus();
        Dispatcher.UIThread.RunJobs();
        Key(window, PhysicalKey.Space);
        Assert.True(box.IsChecked);

        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
        return vm.BomNameList!;
    }

    /// <summary>Types into the focused control as real keystrokes (letters and space only).</summary>
    private static void TypeIntoFocused(MainWindow window, string text)
    {
        foreach (var ch in text)
        {
            var key = ch == ' ' ? PhysicalKey.Space : (PhysicalKey)Enum.Parse(typeof(PhysicalKey), char.ToUpperInvariant(ch).ToString());
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyTextInput(ch.ToString());
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Ctrl+C from the components screen, through the real key handler.</summary>
    private static CopyBomDetailsViewModel PressCopyBom(MainWindow window, MainWindowViewModel vm)
    {
        Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
        Key(window, PhysicalKey.C, RawInputModifiers.Control);
        Assert.Equal(Screen.CopyBomDetails, vm.CurrentScreen);
        Assert.Equal("Copy BoM Details", Named<TextBlock>(window, "CopyBomDetailsCaption")?.Text);
        return vm.CopyBomDetails!;
    }

    private static void AssertStandardUntouched(Company reloaded, Seeded s)
    {
        var std = reloaded.FindBillOfMaterials(s.StandardId);
        Assert.NotNull(std);
        Assert.Equal("Standard", std!.Name);
        Assert.Equal(5m, std.UnitOfManufacture);
        Assert.Equal(3, std.Lines.Count);
        Assert.Equal(0.375m, std.Lines.Single(l => l.ComponentStockItemId == s.Sawdust).QuantityPerBlock);
    }

    /// <summary>The three lines of "Standard", asserted field by field on a re-read BOM.</summary>
    private static void AssertHasStandardsLines(Company reloaded, BillOfMaterials bom, Seeded s)
    {
        Assert.Equal(3, bom.Lines.Count);
        var leg = bom.Lines.Single(l => l.ComponentStockItemId == s.Leg);
        Assert.Equal(BomLineType.Component, leg.LineType);
        Assert.Equal(4m, leg.QuantityPerBlock);
        Assert.Equal(s.Store, leg.GodownId);
        var top = bom.Lines.Single(l => l.ComponentStockItemId == s.Top);
        Assert.Equal(BomLineType.Component, top.LineType);
        Assert.Equal(1m, top.QuantityPerBlock);
        Assert.Null(top.GodownId);
        var dust = bom.Lines.Single(l => l.ComponentStockItemId == s.Sawdust);
        Assert.Equal(BomLineType.Scrap, dust.LineType);
        Assert.Equal(0.375m, dust.QuantityPerBlock);
        Assert.Equal(12.35m, dust.Rate!.Value.Amount);
        Assert.Null(dust.PercentOfFinishedGoodCost);
        // The quantity's UNIT is the component item's own unit (a BOM line stores none); it survives the reload.
        Assert.All(bom.Lines, l => Assert.Equal(s.UnitSymbol, reloaded.FindUnit(reloaded.FindStockItem(l.ComponentStockItemId)!.BaseUnitId)!.Symbol));
    }

    // ================================================================= 1. Copy BoM into a NEW BOM

    [AvaloniaFact]
    public void CtrlC_copies_every_line_of_the_chosen_BOM_into_a_new_BOM_and_the_unit_of_manufacture_is_left_to_the_operator()
    {
        const string company = "Fb Copy New Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            OpenNameListByKeyboard(window, vm);
            TypeIntoFocused(window, "Customer B");
            Key(window, PhysicalKey.Enter);
            Assert.False(vm.BomMaster!.IsAltering);
            Assert.Equal("Customer B", vm.BomMaster!.Name);

            var copy = PressCopyBom(window, vm);
            // Stock Item is pre-set to the item being edited; its list is the items that HAVE a BOM.
            Assert.Equal("Table", Named<TextBlock>(window, "CopyBomDetailsItem")?.Text);
            Assert.Equal("List of Stock Items", Named<TextBlock>(window, "CopyBomDetailsListCaption")?.Text);
            Assert.Equal(new[] { "Chair", "Table" }, RealisedCopyOptions(window));
            Key(window, PhysicalKey.Enter);                       // Stock Item -> Component of BOM
            Assert.True(copy.IsBomFieldActive);
            Assert.Equal("List of BoM", Named<TextBlock>(window, "CopyBomDetailsListCaption")?.Text);
            Assert.Equal(new[] { "Standard", "Export" }, RealisedCopyOptions(window));
            Assert.Equal("Standard", Named<TextBlock>(window, "CopyBomDetailsBom")?.Text);
            Key(window, PhysicalKey.Enter);                       // copy

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            var bom = vm.BomMaster!;
            Assert.Equal(3, bom.Lines.Count(l => !l.IsBlank));
            Assert.Equal(string.Empty, bom.UnitOfManufactureText);   // NOT the source's 5

            // The unit of manufacture is the operator's to define: Ctrl+A without it is refused, nothing written.
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("unit of manufacture", bom.Message ?? string.Empty);
            Assert.Null(LoadBack(dir, company).FindBomByName(s.Table, "Customer B"));

            bom.UnitOfManufactureText = "2";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", bom.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            var made = reloaded.FindBomByName(s.Table, "Customer B");
            Assert.NotNull(made);
            Assert.Equal(2m, made!.UnitOfManufacture);
            AssertHasStandardsLines(reloaded, made, s);
            Assert.Equal(4, reloaded.BillsOfMaterials.Count);
            AssertStandardUntouched(reloaded, s);
            Assert.Single(reloaded.FindBillOfMaterials(s.ExportId)!.Lines);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 2. Copy BoM on ALTER: in place

    [AvaloniaFact]
    public void CtrlC_on_an_alteration_replaces_its_lines_and_saves_IN_PLACE_keeping_the_id_and_the_siblings()
    {
        const string company = "Fb Copy Alter Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            var list = OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.ArrowDown);
            Assert.Equal("Export", list.HighlightedRow?.Name);
            Key(window, PhysicalKey.Enter);
            Assert.True(vm.BomMaster!.IsAltering);
            Assert.Equal("Export", vm.BomMaster!.Name);

            PressCopyBom(window, vm);
            Key(window, PhysicalKey.Enter);
            // The BOM being altered is not offered as a copy of itself.
            Assert.Equal(new[] { "Standard" }, RealisedCopyOptions(window));
            Key(window, PhysicalKey.Enter);

            var bom = vm.BomMaster!;
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.True(bom.IsAltering);
            bom.UnitOfManufactureText = "3";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", bom.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(3, reloaded.BillsOfMaterials.Count);
            Assert.Equal(2, reloaded.BomsFor(s.Table).Count());
            var export = reloaded.FindBillOfMaterials(s.ExportId);
            Assert.NotNull(export);                                // the ORIGINAL id
            Assert.Equal("Export", export!.Name);
            Assert.Equal(3m, export.UnitOfManufacture);
            AssertHasStandardsLines(reloaded, export, s);
            AssertStandardUntouched(reloaded, s);
            Assert.Equal(2, reloaded.FindBillOfMaterials(s.ChairBasicId)!.Lines.Count);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 3. a DIFFERENT item's BOM, F12 type switch OFF

    [AvaloniaFact]
    public void CtrlC_from_another_items_BOM_keeps_its_By_Product_type_and_percent_even_with_the_type_switch_off()
    {
        const string company = "Fb Copy Other Item Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: false);
            OpenNameListByKeyboard(window, vm);
            TypeIntoFocused(window, "Rustic");
            Key(window, PhysicalKey.Enter);

            var copy = PressCopyBom(window, vm);
            Assert.Equal("Table", copy.SelectedItemName);
            Key(window, PhysicalKey.ArrowUp);                     // Table -> Chair (wraps)
            Assert.Equal("Chair", Named<TextBlock>(window, "CopyBomDetailsItem")?.Text);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(new[] { "Basic" }, RealisedCopyOptions(window));
            Key(window, PhysicalKey.Enter);

            var bom = vm.BomMaster!;
            bom.UnitOfManufactureText = "1";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", bom.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            var made = reloaded.FindBomByName(s.Table, "Rustic");
            Assert.NotNull(made);
            Assert.Equal(s.Table, made!.StockItemId);
            Assert.Equal(2, made.Lines.Count);
            Assert.Equal(2m, made.Lines.Single(l => l.ComponentStockItemId == s.Seat).QuantityPerBlock);
            var offcut = made.Lines.Single(l => l.ComponentStockItemId == s.Offcut);
            Assert.Equal(BomLineType.ByProduct, offcut.LineType);
            Assert.Equal(0.125m, offcut.QuantityPerBlock);
            Assert.Equal(7.5m, offcut.PercentOfFinishedGoodCost);
            Assert.Null(offcut.Rate);
            Assert.Equal(2, reloaded.FindBillOfMaterials(s.ChairBasicId)!.Lines.Count);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 4. Escape on Copy BoM Details copies nothing

    [AvaloniaFact]
    public void Escape_on_Copy_BoM_Details_returns_to_the_components_screen_with_nothing_copied()
    {
        const string company = "Fb Copy Esc Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            var list = OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.Enter);                       // Export, 1 line
            var bom = vm.BomMaster!;
            PressCopyBom(window, vm);
            Key(window, PhysicalKey.Enter);
            Key(window, PhysicalKey.Escape);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.Same(bom, vm.BomMaster);
            Assert.Null(vm.CopyBomDetails);
            Assert.Equal("Stock Item Components (BOM) Alteration", Named<TextBlock>(window, "BomMasterCaption")?.Text);
            Assert.Equal(1, bom.Lines.Count(l => !l.IsBlank));
            Assert.Equal("1", bom.UnitOfManufactureText);
            Assert.Same(list, vm.BomNameList);                    // the list beneath stays bound
            Assert.Single(LoadBack(dir, company).FindBillOfMaterials(s.ExportId)!.Lines);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 5. no collision

    /// <summary>Ctrl+C belongs to the components screen only: on the M BOM list it does nothing, and from inside a
    /// quantity box of the components screen it still opens Copy BoM Details (the screen-level chord).</summary>
    [AvaloniaFact]
    public void CtrlC_is_scoped_to_the_components_screen_and_fires_from_its_text_boxes()
    {
        const string company = "Fb Copy Scope Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            Seed(vm, dir, defineType: true);
            OpenNameListByKeyboard(window, vm);
            var columns = vm.Columns.Count;
            Key(window, PhysicalKey.C, RawInputModifiers.Control);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
            Assert.Null(vm.CopyBomDetails);
            Assert.Equal(columns, vm.Columns.Count);

            Key(window, PhysicalKey.Enter);                       // Standard
            var qty = Descendants(window).OfType<TextBox>()
                .First(t => t.DataContext is BomLineRowViewModel { IsBlank: false } && t.IsEffectivelyVisible);
            qty.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(qty, Focused(window));
            Key(window, PhysicalKey.C, RawInputModifiers.Control);
            Assert.Equal(Screen.CopyBomDetails, vm.CurrentScreen);
            // Focus left the components screen's box, so the arrows below drive Copy BoM Details.
            Assert.NotSame(qty, Focused(window));
            Key(window, PhysicalKey.ArrowDown);
            Assert.Equal("Chair", vm.CopyBomDetails!.SelectedItemName);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 6. IV-227: Escape back to the M BOM list

    [AvaloniaFact]
    public void Escape_after_an_alteration_returns_to_the_M_BOM_list_and_a_second_Escape_leaves_it()
    {
        const string company = "Fb Return Alter Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            var list = OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.Enter);                       // Export
            // The list stays drawn beneath; its new-name box must NOT keep the caret, or letters typed on the
            // components screen would edit a name on the screen behind it.
            Assert.NotSame(Named<TextBox>(window, "BomNameListNewName"), Focused(window));
            TypeIntoFocused(window, "x");
            Assert.Equal(string.Empty, list.NewName ?? string.Empty);
            vm.BomMaster!.Lines.First(l => !l.IsBlank).QuantityText = "0.375";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", vm.BomMaster!.Message ?? string.Empty);

            Key(window, PhysicalKey.Escape);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
            Assert.Same(list, vm.BomNameList);
            Assert.Null(vm.BomMaster);
            Assert.Equal("Stock Item Multi Components (M BOM)", Named<TextBlock>(window, "BomNameListCaption")?.Text);
            Assert.Equal(new[] { "Standard", "Export" }, RealisedNames(window));
            Assert.Equal("Export", list.HighlightedRow?.Name);    // the highlight stays on the BOM just altered
            Assert.Same(Named<TextBox>(window, "BomNameListNewName"), Focused(window));

            // The list is live: arrow up and Enter opens Standard.
            Key(window, PhysicalKey.ArrowUp);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.Equal("Standard", vm.BomMaster!.Name);
            Key(window, PhysicalKey.Escape);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);

            Key(window, PhysicalKey.Escape);
            Assert.NotEqual(Screen.BomNameList, vm.CurrentScreen);
            Assert.Null(vm.BomNameList);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(0.375m, reloaded.FindBillOfMaterials(s.ExportId)!.Lines.Single().QuantityPerBlock);
            AssertStandardUntouched(reloaded, s);
        }
        finally { Cleanup(window, dir); }
    }

    [AvaloniaFact]
    public void Escape_after_a_create_returns_to_the_M_BOM_list_which_now_lists_the_new_BOM()
    {
        const string company = "Fb Return Create Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            var list = OpenNameListByKeyboard(window, vm);
            TypeIntoFocused(window, "Economy");
            Key(window, PhysicalKey.Enter);
            var line = vm.BomMaster!.Lines[^1];
            line.SelectedItem = line.ItemOptions.First(i => i.Id == s.Top);
            line.QuantityText = "0.125";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", vm.BomMaster!.Message ?? string.Empty);

            Key(window, PhysicalKey.Escape);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
            Assert.Same(list, vm.BomNameList);
            Assert.Equal(new[] { "Standard", "Export", "Economy" }, RealisedNames(window));
            Assert.Equal("Economy", list.HighlightedRow?.Name);
            Assert.Equal(string.Empty, Named<TextBox>(window, "BomNameListNewName")?.Text ?? string.Empty);

            // Enter now opens the created BOM for ALTERATION (it exists), not a second create.
            Key(window, PhysicalKey.Enter);
            Assert.True(vm.BomMaster!.IsAltering);
            Assert.Equal("Economy", vm.BomMaster!.Name);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(3, reloaded.BomsFor(s.Table).Count());
            Assert.Equal(0.125m, reloaded.FindBomByName(s.Table, "Economy")!.Lines.Single().QuantityPerBlock);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Review W9-fb F1: Left is Escape's pair (the same Back() arm shape), and the components screen opens with focus
    /// on its root, which is exactly where Left pops a column. Before the fix Left landed on the M BOM list with NO
    /// focused element, so a new BOM name typed there was silently dropped.
    /// </summary>
    [AvaloniaFact]
    public void Left_from_the_components_screen_returns_to_the_M_BOM_list_with_the_caret_in_the_new_name_box()
    {
        const string company = "Fb Return Left Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, defineType: true);
            var list = OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.Enter);                       // Standard
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.IsNotType<TextBox>(Focused(window));            // Left only pops when not typing

            Key(window, PhysicalKey.ArrowLeft);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);
            Assert.Same(list, vm.BomNameList);
            Assert.Same(Named<TextBox>(window, "BomNameListNewName"), Focused(window));

            TypeIntoFocused(window, "Night");
            Assert.Equal("Night", Named<TextBox>(window, "BomNameListNewName")?.Text);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.False(vm.BomMaster!.IsAltering);
            Assert.Equal("Night", vm.BomMaster!.Name);
            Assert.Null(LoadBack(dir, company).FindBomByName(s.Table, "Night"));   // nothing saved by navigation
        }
        finally { Cleanup(window, dir); }
    }
}
