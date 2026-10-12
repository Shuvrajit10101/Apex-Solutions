using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
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
/// 🔴 <b>T2-105 — THE POINTER ARM OF THE ALTER VERB ON THE FOUR AREA-3 MASTERS THAT HAD IT HALF-WIRED:
/// census rows 3.2 (Stock Category), 3.3 (Stock Item), 3.5 (Unit of Measure) and 3.7 (Godown).</b>
///
/// <para><b>THE DEFECT, AND WHY IT IS NOT A COSMETIC ONE.</b> Each of these four master screens renders exactly
/// ONE on-screen commit button. On <c>main</c> at <c>0854ef5</c> every one of them was hard-coded
/// <c>Content="Create (Ctrl+A)"</c> and wired to a handler that called <c>Create()</c> <b>unconditionally</b> —
/// while the view-model behind it carries a fully reachable alter mode (<c>ForAlter</c>, reached from the shared
/// <c>Ctrl+Enter</c> master-list arm). So the KEYBOARD route altered and the POINTER route created. An operator
/// who opened an Alteration screen, corrected the name and <i>clicked</i> instead of pressing the chord got a
/// <b>DUPLICATE master</b> and a success message, while the master they meant to fix kept its old value.</para>
///
/// <para>🔴 <b>WHY THESE TESTS HAD TO BE WRITTEN SEPARATELY FROM THE EXISTING ONES.</b> All four masters already
/// had GREEN keyboard alter tests (<c>MasterVerbsW29ReachabilityTests</c>,
/// <c>StockItemAlterReachabilityTests</c>) and those tests stayed green over this live defect for weeks — which
/// is exactly how rows 3.2 and 3.5 were graded <c>COMPLETE</c> and then demoted. A verb that passes on one route
/// does not certify the other. Each test below therefore drives the <b>realised, visible Button</b> and asserts
/// the <b>persisted book re-read from SQLite</b>, never a view-model flag.</para>
///
/// <para><b>FIDELITY (R7, user ruling 14) — CITED, AND THE PAGE WAS OPENED BY CONTENT ON 2026-10-11.</b>
/// <c>help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/</c> gives TallyPrime's alter route
/// verbatim as <i>"Press Alt+G (Go To) &gt; Alter Master &gt; type or select the godown name &gt; press
/// Enter"</i>, and <c>help.tallysolutions.com/stock-items-faq/</c> gives the same <i>"Alt+G (Go To) &gt; Alter
/// Master"</i> shape for a master plus <i>"Accept the screen. As always, you can press Ctrl+A to save."</i> —
/// i.e. the vendor has a DISTINCT Alter Master verb per master, and accepting an alteration screen saves the
/// alteration. That is the behaviour asserted here.</para>
///
/// <para>⚠️ <b>WHAT IS OURS AND IS LABELLED OURS.</b> TallyPrime is keyboard-driven and the vendor documents no
/// on-screen commit BUTTON for these screens at all. The button is this product's own addition, so its
/// existence is a documented divergence. What the vendor grounds is only the <i>semantics</i> the button must
/// carry once we choose to render one: accepting an alteration alters. The button's label text
/// (<i>"Save (Ctrl+A)"</i> / <i>"Create (Ctrl+A)"</i>) is likewise ours, and follows this codebase's own
/// established <c>CommitLabel</c> convention (<c>CostCentreMasterViewModel</c>,
/// <c>AccountGroupMasterViewModel</c>, <c>LedgerMasterViewModel</c>).</para>
/// </summary>
public sealed class InventoryMasterPointerAlterTests
{
    private sealed class Kit
    {
        public required MainWindow Window { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required string Dir { get; init; }
        public required string CompanyName { get; init; }
    }

    /// <summary>
    /// A company seeded THROUGH THE SERVICE with one stock group, one simple unit, one stock category, one
    /// godown and one stock item, then shown at the Gateway.
    ///
    /// <para>The prerequisite masters are seeded rather than typed because what is under test here is the
    /// ALTER pointer arm, not the create form — the create route is already covered elsewhere and driving it
    /// again would only add ways for these tests to fail for an unrelated reason.</para>
    /// </summary>
    private static Kit NewKit(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexAgPointerAlter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = company;
        vm.CreateCompany();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        masters.CreateStockCategory("Premium", parentId: null, alias: null);
        masters.CreateGodown("Depot", parentId: null);
        masters.CreateStockItem("Widget", group.Id, nos.Id);
        new CompanyStorage(dir).Save(c);

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new Kit { Window = window, Vm = vm, Dir = dir, CompanyName = company };
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

    /// <summary>Masters → Create → <paramref name="label"/>, by keyboard only.</summary>
    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    /// <summary>Arrow-Down the existing-masters list until the highlight lands on <paramref name="name"/>.</summary>
    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    /// <summary>
    /// Masters → Create → Stock Item.
    ///
    /// <para>🔴 The Stock Item master does NOT go through the shared <c>MasterListScreen</c> the other three
    /// use — it keeps its own existing-items list and its own
    /// <c>StockItemMasterViewModel.HighlightedRow</c>, and <c>Ctrl+Enter</c> is served by a dedicated
    /// <c>AlterHighlightedStockItemRow</c> arm. Asserting <c>MasterListScreen</c> here would fail on a screen
    /// that is working correctly, so this master gets its own two helpers rather than a flag on the shared
    /// one.</para>
    /// </summary>
    private static void OpenStockItemMaster(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Stock Item");
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        Assert.NotNull(vm.StockItemMaster);
    }

    /// <summary>Arrow-Down the Stock Item master's own existing-items list onto <paramref name="name"/>, and
    /// return the id that row addresses — the id the alteration must save against.</summary>
    private static Guid ArrowToStockItem(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.StockItemMaster?.HighlightedRow is { } row && row.Name == name) return row.StockItemId;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing stock-items list.");
        throw new InvalidOperationException("unreachable");
    }

    private static void AlterHighlighted(MainWindow window) =>
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// 🔴 <b>THE BUTTON THE OPERATOR ACTUALLY CLICKS, identified by NAME and asserted to be on screen.</b>
    ///
    /// <para>Selecting by <c>Name</c> rather than by label text is deliberate and is NOT interchangeable with
    /// it. Picking the button by its label would make
    /// <see cref="The_rendered_commit_button_names_the_verb_that_will_run_on_all_four_masters"/> circular — it
    /// would find a correctly-labelled button or none at all, and could never catch a label that stopped
    /// tracking the verb. Selecting by the first control sharing the view-model's <c>DataContext</c> is worse
    /// still: on the Unit screen that is the <i>Simple/Compound</i> toggle, and an earlier draft of these tests
    /// silently clicked it and asserted nothing.</para>
    ///
    /// <para><c>IsEffectivelyVisible</c> is required so a button inside a collapsed pane cannot stand in for the
    /// one the operator can actually reach.</para>
    /// </summary>
    private static Button CommitButton(MainWindow w, string name)
    {
        var button = Descendants(w).OfType<Button>()
            .FirstOrDefault(b => b.Name == name && b.IsEffectivelyVisible);
        Assert.NotNull(button);
        return button!;
    }

    /// <summary>The pointer path as the product wires it: these buttons carry a XAML <c>Click</c> handler, not a
    /// Command, so raising <c>ClickEvent</c> is the route a mouse takes through the <c>MainWindow</c>
    /// handler.</summary>
    private static void ClickCommit(MainWindow window, string buttonName)
    {
        CommitButton(window, buttonName)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    // ============================================================= 3.2 — STOCK CATEGORY

    /// <summary>
    /// 🔴 <b>CENSUS 3.2.</b> Rename "Premium" to "Premium Grade" on the Stock Category <b>Alteration</b> screen
    /// and CLICK. On <c>main</c> the click reached the bare <c>Create()</c>, which happily created a SECOND
    /// category called "Premium Grade" — leaving "Premium" untouched and the operator believing the rename
    /// landed. Asserted on the book re-read from SQLite: ONE category, the ORIGINAL id, the NEW name.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_stock_category_alteration_screen_renames_and_creates_no_duplicate()
    {
        var k = NewKit("Ag Cat Pointer Co");
        try
        {
            OpenMaster(k.Window, k.Vm, "Stock Category", Screen.StockCategoryMaster);
            ArrowTo(k.Window, k.Vm, "Premium");
            var originalId = k.Vm.MasterListScreen!.HighlightedMasterRow!.MasterId;

            AlterHighlighted(k.Window);
            var master = k.Vm.StockCategoryMaster!;
            Assert.True(master.IsAltering);

            master.Name = "Premium Grade";
            ClickCommit(k.Window, "StockCategoryCommitButton");

            // Not a refusal, and not a create.
            var message = master.Message ?? string.Empty;
            Assert.DoesNotContain("already exists", message);
            Assert.Contains("altered", message);

            // 🔴 THE ARTEFACT: re-read from SQLite. ONE category, the SAME id, the NEW name.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            var cats = reloaded.StockCategories.Where(x => x.Name.StartsWith("Premium", StringComparison.Ordinal))
                .ToList();
            Assert.Single(cats);
            Assert.Equal(originalId, cats[0].Id);
            Assert.Equal("Premium Grade", cats[0].Name);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // ============================================================= 3.5 — UNIT OF MEASURE

    /// <summary>
    /// 🔴 <b>CENSUS 3.5, and the census calls this one worse than the category case for a reason.</b> Correct a
    /// fat-fingered unit symbol "Nos" to "Pcs" on the Unit <b>Alteration</b> screen and CLICK. On <c>main</c>
    /// the click created a SECOND unit "Pcs" while every stock item stayed denominated in "Nos".
    ///
    /// <para>⚠️ The screen itself is the evidence the operator believed they were editing in place:
    /// <c>UnitMasterViewModel.IsCompositionReadOnly =&gt; IsAltering</c> deliberately LOCKS the composition
    /// fields on this screen, which is only coherent if the screen is altering.</para>
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_unit_alteration_screen_alters_the_symbol_and_creates_no_duplicate()
    {
        var k = NewKit("Ag Unit Pointer Co");
        try
        {
            OpenMaster(k.Window, k.Vm, "Unit", Screen.UnitMaster);
            ArrowTo(k.Window, k.Vm, "Nos");
            var originalId = k.Vm.MasterListScreen!.HighlightedMasterRow!.MasterId;

            AlterHighlighted(k.Window);
            var master = k.Vm.UnitMaster!;
            Assert.True(master.IsAltering);
            Assert.True(master.IsCompositionReadOnly, "The alteration screen must lock the composition.");

            master.Symbol = "Pcs";
            ClickCommit(k.Window, "UnitCommitButton");

            var message = master.Message ?? string.Empty;
            Assert.DoesNotContain("already exists", message);
            Assert.Contains("altered", message);

            // 🔴 THE ARTEFACT: exactly ONE unit survives, under the ORIGINAL id, carrying the NEW symbol — so the
            // stock item denominated in it moved with it instead of being orphaned behind a duplicate.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            Assert.Single(reloaded.Units);
            Assert.Equal(originalId, reloaded.Units[0].Id);
            Assert.Equal("Pcs", reloaded.Units[0].Symbol);
            Assert.Equal(originalId, reloaded.StockItems.Single(i => i.Name == "Widget").BaseUnitId);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // ============================================================= 3.7 — GODOWN

    /// <summary>
    /// 🔴 <b>CENSUS 3.7.</b> Rename the godown on its <b>Alteration</b> screen and CLICK. On <c>main</c> this
    /// created a duplicate godown — which splits where stock is reported to be held.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_godown_alteration_screen_renames_and_creates_no_duplicate()
    {
        var k = NewKit("Ag Godown Pointer Co");
        try
        {
            OpenMaster(k.Window, k.Vm, "Godown", Screen.GodownMaster);
            ArrowTo(k.Window, k.Vm, "Depot");
            var originalId = k.Vm.MasterListScreen!.HighlightedMasterRow!.MasterId;
            var before = k.Vm.Company!.Godowns.Count;

            AlterHighlighted(k.Window);
            var master = k.Vm.GodownMaster!;
            Assert.True(master.IsAltering);

            master.Name = "Depot North";
            ClickCommit(k.Window, "GodownCommitButton");

            var message = master.Message ?? string.Empty;
            Assert.DoesNotContain("already exists", message);
            Assert.Contains("altered", message);

            // 🔴 THE ARTEFACT: no new godown row at all, and the original id carries the new name.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            Assert.Equal(before, reloaded.Godowns.Count);
            Assert.DoesNotContain(reloaded.Godowns, g => g.Name == "Depot");
            Assert.Equal("Depot North", reloaded.Godowns.Single(g => g.Id == originalId).Name);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // ============================================================= 3.3 — STOCK ITEM

    /// <summary>
    /// 🔴 <b>CENSUS 3.3 — THE INSTANCE THAT REACHES THE BALANCE SHEET.</b> Rename the stock item on its
    /// <b>Alteration</b> screen and CLICK. On <c>main</c> the click created a SECOND stock item.
    ///
    /// <para>⚠️ The census is explicit about why this one is the worst of the four: a duplicate stock item
    /// carries its own opening balance, its own costing method and its own inventory, so it does not merely
    /// clutter a list — it SPLITS the closing-stock figure. This test asserts the split cannot happen, by
    /// requiring exactly ONE stock item under the ORIGINAL id after a reload.</para>
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_stock_item_alteration_screen_renames_and_creates_no_duplicate()
    {
        var k = NewKit("Ag Item Pointer Co");
        try
        {
            OpenStockItemMaster(k.Window, k.Vm);
            var originalId = ArrowToStockItem(k.Window, k.Vm, "Widget");

            AlterHighlighted(k.Window);
            var master = k.Vm.StockItemMaster!;
            Assert.True(master.IsAltering);

            master.Name = "Widget Mk2";
            ClickCommit(k.Window, "StockItemCommitButton");

            Assert.DoesNotContain("already exists", master.Message ?? string.Empty);

            // 🔴 THE ARTEFACT: ONE stock item, the ORIGINAL id, the NEW name — the closing-stock figure cannot
            // have been split across two items because there is only one.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            Assert.Single(reloaded.StockItems);
            Assert.Equal(originalId, reloaded.StockItems[0].Id);
            Assert.Equal("Widget Mk2", reloaded.StockItems[0].Name);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // ============================================================= THE OTHER HALF OF THE DEFECT

    /// <summary>
    /// 🔴 <b>THE CASE WHERE THE OLD CODE DID NOT DUPLICATE — IT SILENTLY LOST THE CORRECTION.</b>
    ///
    /// <para>When the operator edits a SECONDARY field and leaves the name alone, <c>Create()</c> hit the
    /// engine's own duplicate-name guard and was REFUSED — so the screen showed <i>"A unit 'Nos' already
    /// exists."</i> over an Alteration screen, and the formal-name correction never reached the book. Both
    /// halves of T2-105 are asserted, because a fix that only stopped the duplicate would still have left this
    /// one broken.</para>
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_after_editing_only_a_secondary_field_saves_it_instead_of_being_refused()
    {
        var k = NewKit("Ag Unit Secondary Co");
        try
        {
            OpenMaster(k.Window, k.Vm, "Unit", Screen.UnitMaster);
            ArrowTo(k.Window, k.Vm, "Nos");
            var originalId = k.Vm.MasterListScreen!.HighlightedMasterRow!.MasterId;

            AlterHighlighted(k.Window);
            var master = k.Vm.UnitMaster!;
            Assert.True(master.IsAltering);

            // The symbol is deliberately left alone — only the formal name is corrected.
            Assert.Equal("Nos", master.Symbol);
            master.FormalName = "Number Count";
            ClickCommit(k.Window, "UnitCommitButton");

            // 🔴 On main this read "A unit 'Nos' already exists." and nothing was written.
            Assert.DoesNotContain("already exists", master.Message ?? string.Empty);

            var reloaded = LoadBack(k.Dir, k.CompanyName);
            Assert.Single(reloaded.Units);
            Assert.Equal("Number Count", reloaded.Units.Single(u => u.Id == originalId).FormalName);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // ============================================================= THE LABEL THAT USED TO LIE

    /// <summary>
    /// 🔴 <b>THE RENDERED LABEL, on all four masters.</b> The commit control was the literal
    /// <c>Content="Create (Ctrl+A)"</c> in BOTH modes, so an Alteration screen displayed the word <i>Create</i>
    /// on the only button it offered — the same caption-that-lies class the project treated as serious at
    /// <c>T2-101</c>, and the half an operator actually reads before committing.
    ///
    /// <para>Asserted on the realised, visible <c>Button</c> found by its bound view-model, in BOTH modes, so a
    /// label that stopped tracking the verb would redden here.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_rendered_commit_button_names_the_verb_that_will_run_on_all_four_masters()
    {
        var k = NewKit("Ag Label Co");
        try
        {
            void Check(string menuLabel, Screen screen, string rowName, string buttonName)
            {
                k.Vm.ShowGateway();
                Dispatcher.UIThread.RunJobs();

                // Creation mode: the button names CREATE.
                if (screen == Screen.StockItemMaster)
                {
                    OpenStockItemMaster(k.Window, k.Vm);
                    Assert.Equal("Create (Ctrl+A)", CommitButton(k.Window, buttonName).Content as string);
                    ArrowToStockItem(k.Window, k.Vm, rowName);
                }
                else
                {
                    OpenMaster(k.Window, k.Vm, menuLabel, screen);
                    Assert.Equal("Create (Ctrl+A)", CommitButton(k.Window, buttonName).Content as string);
                    ArrowTo(k.Window, k.Vm, rowName);
                }

                // Alteration mode: the same control must name SAVE.
                AlterHighlighted(k.Window);
                Assert.Equal("Save (Ctrl+A)", CommitButton(k.Window, buttonName).Content as string);
            }

            Check("Stock Category", Screen.StockCategoryMaster, "Premium", "StockCategoryCommitButton");
            Check("Unit", Screen.UnitMaster, "Nos", "UnitCommitButton");
            Check("Godown", Screen.GodownMaster, "Depot", "GodownCommitButton");
            Check("Stock Item", Screen.StockItemMaster, "Widget", "StockItemCommitButton");
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }
}
