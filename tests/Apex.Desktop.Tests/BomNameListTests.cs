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
/// 🔴 <b>CENSUS 3.9 — THE M BOM NAME LIST, DRIVEN ON A REAL WINDOW (wave 8, track eb).</b>
///
/// <para><b>THE OPEN CLAUSE:</b> <i>"the M-BOM name list is UNBUILT, so an item's SECOND and later BOMs cannot be
/// altered anywhere"</i>, and a further BOM could not be added from the item either. The tick on Stock Item
/// Alteration opened only the item's FIRST BOM by name.</para>
///
/// <para><b>FIDELITY (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/,
/// opened BY CONTENT 2026-10-26: <i>"In the Stock Item Multi Components (M BOM) screen, enter the name of the new
/// BOM and press Enter."</i> Opening a LISTED name for alteration is ours (IV-225).</para>
///
/// <para><b>THE BAR.</b> Every positive assertion is on the book re-read from SQLite through a FRESH store handle:
/// the altered BOM keeps its id, the OTHER BOM of the item is byte-for-byte untouched, and a create adds exactly
/// one BOM. The whole route is keyboard-only through the window's real key handlers. Quantities are deliberately
/// odd (2.375, 0.125) so a scale or rounding slip cannot pass by accident.</para>
/// </summary>
public sealed class BomNameListTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexEbBomNames_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The BOM names as REALISED on screen (the TextBlocks inside the list), in display order.</summary>
    private static List<string> RealisedNames(MainWindow window)
    {
        var list = Named<ItemsControl>(window, "BomNameListRows");
        Assert.NotNull(list);
        return Descendants(list!).OfType<TextBlock>()
            .Where(t => t.DataContext is BomNameRow row && t.Text == row.Name)
            .Select(t => t.Text!)
            .ToList();
    }

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    private sealed record Seeded(Guid Table, Guid Leg, Guid Top, Guid Screw, Guid StandardId, Guid ExportId);

    /// <summary>"Table" with TWO BOMs: "Standard" (4 Leg + 1 Top) and "Export" (4 Leg + 1 Top + 2.375 Screw),
    /// created in that order. Odd quantities so a mis-scaled save cannot pass.</summary>
    private static Seeded Seed(MainWindowViewModel vm, string dir, bool withBoms = true)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var table = inv.CreateStockItem("Table", group.Id, unit.Id);
        var leg = inv.CreateStockItem("Leg", group.Id, unit.Id);
        var top = inv.CreateStockItem("Top", group.Id, unit.Id);
        var screw = inv.CreateStockItem("Screw", group.Id, unit.Id);
        Guid std = Guid.Empty, exp = Guid.Empty;
        if (withBoms)
        {
            var svc = new BomService(c);
            std = svc.CreateBom(table.Id, "Standard", 1m, new[]
            {
                new BomLine(BomLineType.Component, leg.Id, 4m),
                new BomLine(BomLineType.Component, top.Id, 1m),
            }).Id;
            exp = svc.CreateBom(table.Id, "Export", 1m, new[]
            {
                new BomLine(BomLineType.Component, leg.Id, 4m),
                new BomLine(BomLineType.Component, top.Id, 1m),
                new BomLine(BomLineType.Component, screw.Id, 2.375m),
            }).Id;
        }
        c.SetComponentsBom = true;
        new CompanyStorage(dir).Save(c);
        return new Seeded(table.Id, leg.Id, top.Id, screw.Id, std, exp);
    }

    /// <summary>Gateway → Create → Stock Item → arrow onto "Table" → Ctrl+Enter → tick "Alter Components (BOM)"
    /// with Space → Ctrl+A. Keyboard only. Lands on the M BOM name list.</summary>
    private static BomNameListViewModel OpenNameListByKeyboard(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Stock Item");
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        for (var i = 0; i < 10 && vm.StockItemMaster!.HighlightedRow?.Name != "Table"; i++)
            Key(window, PhysicalKey.ArrowDown);
        Assert.Equal("Table", vm.StockItemMaster!.HighlightedRow?.Name);
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
        Assert.Equal("Stock Item Multi Components (M BOM)", Named<TextBlock>(window, "BomNameListCaption")?.Text);
        return vm.BomNameList!;
    }

    /// <summary>Focuses the realised new-name box and types into it as keystrokes.</summary>
    private static void TypeNewName(MainWindow window, string text)
    {
        var box = Named<TextBox>(window, "BomNameListNewName");
        Assert.NotNull(box);
        box!.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, box.Text);
    }

    private static void AssertStandardUntouched(Company reloaded, Seeded s)
    {
        var std = reloaded.FindBillOfMaterials(s.StandardId);
        Assert.NotNull(std);
        Assert.Equal("Standard", std!.Name);
        Assert.Equal(1m, std.UnitOfManufacture);
        Assert.Equal(2, std.Lines.Count);
        Assert.Equal(4m, std.Lines.Single(l => l.ComponentStockItemId == s.Leg).QuantityPerBlock);
        Assert.Equal(1m, std.Lines.Single(l => l.ComponentStockItemId == s.Top).QuantityPerBlock);
    }

    // ================================================================= 1. the SECOND BOM is alterable, in place

    [AvaloniaFact]
    public void The_second_BOM_of_an_item_is_reached_by_arrow_and_Enter_and_altered_in_place()
    {
        const string company = "Eb Bom Names Second Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            var list = OpenNameListByKeyboard(window, vm);

            Assert.Equal(new[] { "Standard", "Export" }, RealisedNames(window));
            Assert.Equal("Standard", list.HighlightedRow?.Name);

            Key(window, PhysicalKey.ArrowDown);
            Assert.Equal("Export", list.HighlightedRow?.Name);
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            var bom = vm.BomMaster!;
            Assert.True(bom.IsAltering);
            Assert.Equal("Stock Item Components (BOM) Alteration", Named<TextBlock>(window, "BomMasterCaption")?.Text);
            Assert.Equal("Export", bom.Name);
            Assert.Equal(3, bom.Lines.Count(l => !l.IsBlank));

            // Correct the screw quantity on EXPORT only.
            bom.Lines.Single(l => l.SelectedItem?.Id == s.Screw).QuantityText = "0.125";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", bom.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(2, reloaded.BomsFor(s.Table).Count());
            Assert.Equal(2, reloaded.BillsOfMaterials.Count);
            var export = reloaded.FindBillOfMaterials(s.ExportId);
            Assert.NotNull(export);                                    // the ORIGINAL id, not an appended BOM
            Assert.Equal("Export", export!.Name);
            Assert.Equal(3, export.Lines.Count);
            Assert.Equal(0.125m, export.Lines.Single(l => l.ComponentStockItemId == s.Screw).QuantityPerBlock);
            Assert.Equal(4m, export.Lines.Single(l => l.ComponentStockItemId == s.Leg).QuantityPerBlock);
            AssertStandardUntouched(reloaded, s);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Up from the first name wraps to the last — the arrows walk the whole list both ways.</summary>
    [AvaloniaFact]
    public void ArrowUp_from_the_first_name_wraps_to_the_last_and_Enter_opens_it()
    {
        const string company = "Eb Bom Names Wrap Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            var list = OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.ArrowUp);
            Assert.Equal("Export", list.HighlightedRow?.Name);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(s.ExportId, vm.Company!.FindBomByName(s.Table, vm.BomMaster!.Name)!.Id);
            Assert.True(vm.BomMaster!.IsAltering);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 2. a NEW name creates a further BOM

    [AvaloniaFact]
    public void Typing_a_new_name_and_Enter_creates_a_further_BOM_of_the_item_and_leaves_the_others_alone()
    {
        const string company = "Eb Bom Names New Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            OpenNameListByKeyboard(window, vm);
            TypeNewName(window, "Economy");
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            var bom = vm.BomMaster!;
            Assert.False(bom.IsAltering);
            Assert.True(bom.IsItemFixed);
            Assert.False(bom.CanChangeFinishedGood);
            Assert.Equal("Stock Item Components (BOM) Creation", Named<TextBlock>(window, "BomMasterCaption")?.Text);
            Assert.Equal("Economy", bom.Name);
            Assert.Equal(s.Table, bom.SelectedFinishedGood!.Id);

            var line = bom.Lines[^1];
            line.SelectedItem = line.ItemOptions.First(i => i.Id == s.Leg);
            line.QuantityText = "2.375";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", bom.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(3, reloaded.BomsFor(s.Table).Count());
            var economy = reloaded.FindBomByName(s.Table, "Economy");
            Assert.NotNull(economy);
            Assert.Equal(s.Table, economy!.StockItemId);
            Assert.Equal(2.375m, Assert.Single(economy.Lines).QuantityPerBlock);
            Assert.Equal(3, reloaded.FindBillOfMaterials(s.ExportId)!.Lines.Count);
            AssertStandardUntouched(reloaded, s);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Typing a name the item ALREADY has (in another case) opens THAT BOM for alteration — it never
    /// starts a create that would only bounce off the per-item uniqueness rule.</summary>
    [AvaloniaFact]
    public void Typing_an_existing_name_opens_that_BOM_for_alteration_not_a_duplicate_create()
    {
        const string company = "Eb Bom Names Typed Existing Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            OpenNameListByKeyboard(window, vm);
            TypeNewName(window, "export");
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.True(vm.BomMaster!.IsAltering);
            Assert.Equal("Export", vm.BomMaster!.Name);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(2, reloaded.BillsOfMaterials.Count);
            Assert.NotNull(reloaded.FindBillOfMaterials(s.ExportId));
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 3. an item with no BOM yet

    [AvaloniaFact]
    public void An_item_with_no_BOM_lists_nothing_refuses_a_bare_Enter_and_creates_under_a_typed_name()
    {
        const string company = "Eb Bom Names Empty Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir, withBoms: false);
            var list = OpenNameListByKeyboard(window, vm);
            Assert.True(list.HasNoBoms);
            Assert.Empty(RealisedNames(window));

            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.BomNameList, vm.CurrentScreen);       // nothing to open: stays, says why
            Assert.Equal("Enter the name of the new BOM.", list.Message);

            TypeNewName(window, "Standard");
            Key(window, PhysicalKey.Enter);
            var bom = vm.BomMaster!;
            Assert.True(bom.IsItemFixed);
            var line = bom.Lines[^1];
            line.SelectedItem = line.ItemOptions.First(i => i.Id == s.Top);
            line.QuantityText = "1";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var reloaded = LoadBack(dir, company);
            var only = Assert.Single(reloaded.BomsFor(s.Table));
            Assert.Equal("Standard", only.Name);
            Assert.True(reloaded.FindStockItem(s.Table)!.SetComponents);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 3b. the caret starts in the new-name box (review F1)

    /// <summary>Types <paramref name="text"/> as REAL keystrokes into whatever holds focus — no programmatic
    /// <c>Focus()</c> — so the test sees exactly what an operator's fingers would.</summary>
    private static void TypeIntoFocused(MainWindow window, string text)
    {
        foreach (var ch in text)
        {
            var key = (PhysicalKey)Enum.Parse(typeof(PhysicalKey), char.ToUpperInvariant(ch).ToString());
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyTextInput(ch.ToString());
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static object? Focused(MainWindow window) => window.FocusManager?.GetFocusedElement();

    /// <summary>
    /// Review F1 (MEDIUM). The vendor's literal step — <i>"enter the name of the new BOM and press Enter"</i> — must
    /// create the typed BOM. Before the fix nothing held focus when the list opened, the letters went nowhere, and
    /// Enter opened the HIGHLIGHTED "Standard" for ALTERATION. Asserted on the realised focus, and on the book
    /// re-read through a fresh store: three BOMs, the new one under the typed name, Standard untouched.
    /// </summary>
    [AvaloniaFact]
    public void The_list_opens_with_the_caret_in_the_new_name_box_so_typing_then_Enter_creates_the_typed_BOM()
    {
        const string company = "Eb Bom Names Caret Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            var list = OpenNameListByKeyboard(window, vm);

            var box = Named<TextBox>(window, "BomNameListNewName");
            Assert.NotNull(box);
            Assert.Same(box, Focused(window));

            TypeIntoFocused(window, "Economy");
            Assert.Equal("Economy", box!.Text);
            Assert.Equal("Economy", list.NewName);
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            var bom = vm.BomMaster!;
            Assert.False(bom.IsAltering);
            Assert.Equal("Economy", bom.Name);
            var line = bom.Lines[^1];
            line.SelectedItem = line.ItemOptions.First(i => i.Id == s.Screw);
            line.QuantityText = "0.125";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(3, reloaded.BomsFor(s.Table).Count());
            var economy = reloaded.FindBomByName(s.Table, "Economy");
            Assert.NotNull(economy);
            Assert.Equal(0.125m, Assert.Single(economy!.Lines).QuantityPerBlock);
            AssertStandardUntouched(reloaded, s);
            Assert.Equal(3, reloaded.FindBillOfMaterials(s.ExportId)!.Lines.Count);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Review F1, second half: with the caret in the new-name box the arrows must STILL walk the existing names
    /// (the shell's arrow arms skip a focused TextBox). ArrowDown moves the highlight to "Export" while focus stays
    /// in the box, and Enter with nothing typed opens Export — not Standard — for alteration.
    /// </summary>
    [AvaloniaFact]
    public void Arrows_walk_the_names_while_the_caret_is_in_the_new_name_box()
    {
        const string company = "Eb Bom Names Caret Arrows Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            var list = OpenNameListByKeyboard(window, vm);
            var box = Named<TextBox>(window, "BomNameListNewName");
            Assert.Same(box, Focused(window));

            Key(window, PhysicalKey.ArrowDown);
            Assert.Equal("Export", list.HighlightedRow?.Name);
            Assert.Same(box, Focused(window));
            Key(window, PhysicalKey.ArrowUp);
            Assert.Equal("Standard", list.HighlightedRow?.Name);
            Key(window, PhysicalKey.ArrowDown);

            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.BomMaster, vm.CurrentScreen);
            Assert.True(vm.BomMaster!.IsAltering);
            Assert.Equal(s.ExportId, vm.Company!.FindBomByName(s.Table, vm.BomMaster!.Name)!.Id);
            Assert.Equal("Export", vm.BomMaster!.Name);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Review F4 (LOW). <see cref="BomMasterViewModel.ForNewBomOfItem"/> asked for an item that is not a finished-good
    /// candidate used to fall back to the FIRST item with the picker unlocked — a new BOM for one item defaulting to
    /// an unrelated one. It now refuses.
    /// </summary>
    [AvaloniaFact]
    public void ForNewBomOfItem_refuses_an_unknown_item_rather_than_defaulting_to_another()
    {
        const string company = "Eb Bom Names Unknown Item Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            var s = Seed(vm, dir);
            var known = BomMasterViewModel.ForNewBomOfItem(vm.Company!, new CompanyStorage(dir), s.Table, "Economy", () => { });
            Assert.NotNull(known);
            Assert.Equal(s.Table, known!.SelectedFinishedGood!.Id);
            Assert.True(known.IsItemFixed);

            var unknown = BomMasterViewModel.ForNewBomOfItem(vm.Company!, new CompanyStorage(dir), Guid.NewGuid(), "Economy", () => { });
            Assert.Null(unknown);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= 4. Escape leaves without writing

    [AvaloniaFact]
    public void Escape_on_the_name_list_leaves_it_and_writes_nothing()
    {
        const string company = "Eb Bom Names Esc Co";
        var (window, vm, dir) = NewWindow(company);
        try
        {
            Seed(vm, dir);
            OpenNameListByKeyboard(window, vm);
            Key(window, PhysicalKey.Escape);
            Assert.NotEqual(Screen.BomNameList, vm.CurrentScreen);
            Assert.Null(vm.BomNameList);
            Assert.Equal(2, LoadBack(dir, company).BillsOfMaterials.Count);
        }
        finally { Cleanup(window, dir); }
    }
}
