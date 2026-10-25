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
/// 🔴 <b>CENSUS 3.12 — THE REORDER LEVEL <i>ALTER</i> VERB, ON BOTH ROUTES, DRIVEN ON A REAL WINDOW.</b>
///
/// <para><b>THE GAP THIS CLOSES, stated as the census stated it.</b> Row 3.12 read
/// <i>"alteration is an <b>upsert only</b> — creating for an existing scope+target replaces it; no Alter
/// screen"</i>, and <c>ReorderLevelsViewModel.IsAltering</c> was the literal constant <c>false</c>. Two
/// separate things were wrong behind that one sentence and each needs its own assertion:</para>
/// <list type="number">
///   <item><b>Nothing ever LOADED an existing definition.</b> Picking a target that already had one left every
///     box blank, and <c>Create</c> reads a blank quantity box as <i>unset</i>. So an operator who opened the
///     screen to raise the reorder level and pressed Ctrl+A silently WIPED the minimum order quantity, the
///     consumption period and the criterion from a definition the Reorder Status report consumes. That is a
///     data loss, not a missing convenience.</item>
///   <item><b><c>CreateOrUpdate</c> mints a fresh <c>Guid.NewGuid()</c></b> and discards the old row, so the
///     definition the operator opened is GONE and a different record stands in its place — and the arrow
///     highlight, which is restored BY ID after every rebuild, is left pointing at nothing.</item>
/// </list>
///
/// <para>🔴 <b>WHY BOTH ROUTES ARE DRIVEN SEPARATELY.</b> This screen renders exactly ONE commit button and it
/// was wired to a bare unconditional <c>Create()</c> — the precise shape of <c>T2-99</c> on the price list,
/// where five pre-existing keyboard tests stayed GREEN over a wrong-money defect because the keyboard arm was
/// already correct. A verb that passes on one route certifies nothing about the other, so the pointer test
/// below raises <c>ClickEvent</c> on the realised, effectively-visible <see cref="Button"/>.</para>
///
/// <para><b>FIDELITY (R7, ruling 14) — THE PAGE WAS OPENED AND READ BY CONTENT ON 2026-10-23.</b>
/// <c>help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/</c>.
/// The vendor's own route to the reorder level IS an alteration route: <i>"Press Alt+G (Go To) &gt; Alter
/// Master &gt; type or select Reorder Level (Stock Item) and press Enter."</i> The page attests the field set
/// this screen shows — <i>"Period: Select the consumption for various periods – Days, Weeks, Months, or
/// Years."</i>, a <i>Criteria</i> of <i>Higher</i> or <i>Lower</i>, and <i>"Minimum Order Quantity: Enter the
/// minimum quantity you want to order."</i> — and puts the two Advanced toggles on that screen as <b>Alt+S</b>
/// and <b>Alt+V</b>, which is what this screen already binds. 🔴 <b>This supersedes the repeated claim in
/// <c>MainWindowViewModel</c> and in <c>MasterVerbsW33ResidualAlterTests</c> that "the reorder page 404s":
/// the page was MOVED, not withdrawn</b>, and the old slug
/// <c>…/tally-prime/inventory-masters/reorder-level-tally/</c> is dead.</para>
///
/// <para>⚠️ <b>WHAT IS OURS AND IS LABELLED OURS.</b> The ROUTE to the alteration screen — arrows over the
/// existing-definitions list, Ctrl+Enter to open the highlighted one — is this product's own shared shape, as
/// it is for every other arm of <c>AlterHighlightedMasterListRow</c>. The on-screen commit BUTTON is ours too;
/// TallyPrime is keyboard-driven and documents no such button. What the vendor grounds is the SCREEN, its
/// fields and the fact that the reorder level is reached by an Alter Master verb at all.</para>
/// </summary>
public sealed class ReorderLevelAlterTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir, string Company) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCdReorderAlter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, dir, company);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
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

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

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

    /// <summary>Masters → Create → Reorder Levels, by keyboard only.</summary>
    private static void OpenReorderMaster(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Reorder Levels");
        Assert.Equal(Screen.ReorderLevelsMaster, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-definitions list.");
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>The realised, effectively-visible commit button the operator clicks, found by NAME (not by its
    /// label text, which would make the assertion circular).</summary>
    private static void ClickCommit(MainWindow window)
    {
        var button = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => b.Name == "ReorderLevelCommitButton" && b.IsEffectivelyVisible);
        Assert.NotNull(button);
        button!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    /// <summary>One stock item plus one ADVANCED reorder definition over it, carrying every field the screen
    /// can hold — so a field that is not loaded back, or is dropped on save, has somewhere to show up.</summary>
    private static (StockItem Item, ReorderDefinition Definition) Seed(MainWindowViewModel vm, string dir)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Cabinet", group.Id, unit.Id);

        var definition = new ReorderLevelsService(c).CreateOrUpdate(
            ReorderScope.Item, item.Id,
            reorderAdvanced: true, reorderQuantity: 100m,
            minQtyAdvanced: true, minOrderQuantity: 25m,
            periodCount: 3, periodUnit: ExpiryPeriodUnit.Months, criteria: ReorderCriteria.Higher);
        new CompanyStorage(dir).Save(c);
        return (item, definition);
    }

    // ============================================================ 1. the screen LOADS the definition back

    /// <summary>
    /// 🔴 <b>THE DATA-LOSS HALF.</b> Ctrl+Enter on the highlighted definition must open an ALTERATION screen
    /// with every stored field already in its box. On <c>main</c> there was no alteration screen at all, and the
    /// create screen showed blanks over an existing definition — which <c>Create</c> then wrote back as "unset".
    /// </summary>
    [AvaloniaFact]
    public void CtrlEnter_opens_the_reorder_definition_with_every_field_loaded_back()
    {
        var (window, vm, dir, _) = NewWindow("Cd Reorder Load Co");
        try
        {
            Seed(vm, dir);
            OpenReorderMaster(window, vm);
            ArrowTo(window, vm, "Item 'Cabinet'");

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var master = vm.ReorderLevels!;
            Assert.True(master.IsAltering, "Ctrl+Enter must put the screen into alteration mode.");
            Assert.Equal("Reorder Level Alteration", master.Caption);

            Assert.Equal(ReorderScope.Item, master.SelectedScope!.Scope);
            Assert.Equal("Cabinet", master.SelectedTarget!.Display);
            Assert.True(master.ReorderAdvanced);
            Assert.Equal("100", master.ReorderQuantityText);
            Assert.True(master.MinQtyAdvanced);
            Assert.Equal("25", master.MinOrderQtyText);
            Assert.Equal("3", master.PeriodCountText);
            Assert.Equal(ExpiryPeriodUnit.Months, master.SelectedPeriodUnit!.Unit);
            Assert.Equal(ReorderCriteria.Higher, master.SelectedCriteria!.Criteria);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 2. the KEYBOARD route alters in place

    /// <summary>
    /// 🔴 <b>THE ARTEFACT: the definition re-read from SQLite.</b> Raise the reorder level from 100 to 150 and
    /// press Ctrl+A. Exactly ONE definition must survive, under its ORIGINAL id, carrying the new figure AND
    /// every field the operator did not touch.
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_on_the_alteration_screen_updates_the_original_row_in_place()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder Key Co");
        try
        {
            var (item, original) = Seed(vm, dir);
            OpenReorderMaster(window, vm);
            ArrowTo(window, vm, "Item 'Cabinet'");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var master = vm.ReorderLevels!;
            Assert.True(master.IsAltering);
            master.ReorderQuantityText = "150";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Contains("altered", master.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            var definition = Assert.Single(reloaded.ReorderDefinitions);
            Assert.Equal(original.Id, definition.Id);          // the ORIGINAL row, not a replacement
            Assert.Equal(150m, definition.ReorderQuantity);    // the correction
            Assert.Equal(25m, definition.MinOrderQuantity);    // untouched fields survive
            Assert.Equal(3, definition.PeriodCount);
            Assert.Equal(ExpiryPeriodUnit.Months, definition.PeriodUnit);
            Assert.Equal(ReorderCriteria.Higher, definition.Criteria);
            Assert.Equal(item.Id, definition.TargetId);
            Assert.Equal(ReorderScope.Item, definition.Scope);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 3. the POINTER route alters in place too

    /// <summary>
    /// 🔴 <b>THE ROUTE THAT WAS BROKEN WHILE THE OTHER ONE WORKED.</b> Same correction, but CLICKED. Before the
    /// handler branched, the click reached a bare <c>Create()</c> — <c>CreateOrUpdate</c> would have minted a
    /// NEW id for the same (scope, target) and the screen would have reported success over a record the
    /// operator had never seen.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_alteration_screen_updates_the_original_row_in_place()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder Click Co");
        try
        {
            var (_, original) = Seed(vm, dir);
            OpenReorderMaster(window, vm);
            ArrowTo(window, vm, "Item 'Cabinet'");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var master = vm.ReorderLevels!;
            Assert.True(master.IsAltering);
            master.MinOrderQtyText = "40";
            ClickCommit(window);

            Assert.Contains("altered", master.Message ?? string.Empty);

            var reloaded = LoadBack(dir, company);
            var definition = Assert.Single(reloaded.ReorderDefinitions);
            Assert.Equal(original.Id, definition.Id);
            Assert.Equal(40m, definition.MinOrderQuantity);
            Assert.Equal(100m, definition.ReorderQuantity);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 4. Alt+D stays refused mid-alteration

    /// <summary>
    /// The shared contract every altering screen owes: Alt+D is inert while the screen is mid-alteration, so the
    /// definition being corrected cannot be deleted out from under the correction.
    /// </summary>
    [AvaloniaFact]
    public void Alt_D_is_refused_while_the_reorder_screen_is_altering()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder Guard Co");
        try
        {
            Seed(vm, dir);
            OpenReorderMaster(window, vm);
            ArrowTo(window, vm, "Item 'Cabinet'");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.ReorderLevels!.IsAltering);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Single(vm.Company!.ReorderDefinitions);
            Assert.Single(LoadBack(dir, company).ReorderDefinitions);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 4b. probe P1c — CREATE over an occupied target

    /// <summary>
    /// 🔴 <b>REVIEWER PROBE P1c, BY KEYBOARD (census 3.12's last open clause, T2-158 item 1).</b> A definition
    /// saved as 100 / 25; a fresh CREATE screen over the same (Item, Cabinet); key 150 into the reorder box; Ctrl+A.
    /// On the branch the review held, that minted a NEW id with minimum <c>NULL</c> and said "saved". Now the
    /// first Ctrl+A saves nothing, refuses by name and loads the saved figures as an Alteration; keying the
    /// correction again and pressing Ctrl+A alters IN PLACE. Both states are re-read from SQLite through a FRESH
    /// store handle.
    /// </summary>
    [AvaloniaFact]
    public void P1c_CtrlA_create_over_an_occupied_target_keeps_the_id_and_the_minimum()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder P1c Key Co");
        try
        {
            var (_, original) = Seed(vm, dir);
            OpenReorderMaster(window, vm);
            var master = vm.ReorderLevels!;
            Assert.False(master.IsAltering);
            Assert.Equal("Cabinet", master.SelectedTarget!.Display);

            master.ReorderQuantityText = "150";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Contains("already exists", master.Message ?? string.Empty);
            Assert.Contains("'Cabinet'", master.Message ?? string.Empty);
            Assert.True(master.IsAltering);
            var untouched = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(original.Id, untouched.Id);
            Assert.Equal(100m, untouched.ReorderQuantity);
            Assert.Equal(25m, untouched.MinOrderQuantity);

            Assert.Equal("25", master.MinOrderQtyText);   // the saved minimum is ON SCREEN now
            master.ReorderQuantityText = "150";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Contains("altered", master.Message ?? string.Empty);
            var altered = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(original.Id, altered.Id);
            Assert.Equal(150m, altered.ReorderQuantity);
            Assert.Equal(25m, altered.MinOrderQuantity);
            Assert.Equal(3, altered.PeriodCount);
            Assert.Equal(ReorderCriteria.Higher, altered.Criteria);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>REVIEWER PROBE P1c, BY POINTER.</b> The same sequence through the realised commit button, because a
    /// verb that holds on one route certifies nothing about the other (T2-99).
    /// </summary>
    [AvaloniaFact]
    public void P1c_click_create_over_an_occupied_target_keeps_the_id_and_the_minimum()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder P1c Click Co");
        try
        {
            var (_, original) = Seed(vm, dir);
            OpenReorderMaster(window, vm);
            var master = vm.ReorderLevels!;
            Assert.False(master.IsAltering);

            master.ReorderQuantityText = "150";
            ClickCommit(window);

            Assert.Contains("already exists", master.Message ?? string.Empty);
            Assert.True(master.IsAltering);
            var untouched = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(original.Id, untouched.Id);
            Assert.Equal(25m, untouched.MinOrderQuantity);

            master.ReorderQuantityText = "150";
            ClickCommit(window);

            var altered = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(original.Id, altered.Id);
            Assert.Equal(150m, altered.ReorderQuantity);
            Assert.Equal(25m, altered.MinOrderQuantity);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 5. a fresh drill is still a CREATE screen

    /// <summary>
    /// Re-drilling from the Gateway gives a create-mode screen again, and Ctrl+A on it still UPSERTS rather than
    /// altering — so the new branch has not swallowed the create verb the screen always had.
    /// </summary>
    [AvaloniaFact]
    public void A_fresh_drill_is_still_a_create_screen_and_CtrlA_still_creates()
    {
        var (window, vm, dir, company) = NewWindow("Cd Reorder Create Co");
        try
        {
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
            var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
            inv.CreateStockItem("Cabinet", group.Id, unit.Id);
            new CompanyStorage(dir).Save(c);

            OpenReorderMaster(window, vm);
            var master = vm.ReorderLevels!;
            Assert.False(master.IsAltering);
            Assert.Equal("Reorder Level Creation", master.Caption);

            master.ReorderQuantityText = "12";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var definition = Assert.Single(LoadBack(dir, company).ReorderDefinitions);
            Assert.Equal(12m, definition.ReorderQuantity);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
