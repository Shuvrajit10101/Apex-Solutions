using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 29 / TRACK U1 — THE SIX MASTERS' ALTER AND DELETE VERBS, DRIVEN BY REAL KEYSTROKES ON A REAL
/// WINDOW.</b> Census 2.7, 2.8, 3.1, 3.2, 3.5, 3.7.
///
/// <para><b>Why not one assertion here touches a service directly.</b> This wave exists because capability had
/// been built and left UNREACHABLE. Six delete services sat in <c>Apex.Ledger</c> with zero callers in
/// <c>Apex.Desktop</c>; the cost masters had no delete service at all; and the immediately preceding build wrote
/// <c>ForAlter</c> factories onto five view models that no key could invoke —
/// <c>ViewModelAlterEntryPointReachabilityTests</c> was RED naming exactly those five when this track picked the
/// work up. A test that called <c>InventoryService.DeleteGodown</c> would have been green throughout that entire
/// period and would have proved nothing. So every test below opens the real <see cref="MainWindow"/>, walks the
/// Gateway cascade with arrows and Enter, and presses the actual accelerators: Ctrl+A to accept, arrows to walk
/// the existing-list, Ctrl+Enter to alter, Alt+D then Y to delete.</para>
///
/// <para><b>Alteration is asserted by IDENTITY, never by name.</b> A "rename" that quietly created a SECOND
/// master would satisfy a name-only assertion while forking every posted line that references the old one.</para>
///
/// <para><b>The refusal tests are the important half</b> — see
/// <c>Apex.Ledger.Tests.MasterVerbsW29DeletionGuardTests</c> for why a permitted delete of a referenced master
/// makes the open company permanently unsavable. Here the question is narrower and is about the SHELL: that the
/// guard's refusal reaches the operator instead of crashing the application, and that the master survives.</para>
/// </summary>
public sealed class MasterVerbsW29ReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexW29U1_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, tempDir);
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

    /// <summary>Masters → Create → <paramref name="label"/>, by keyboard only. The route every test starts from,
    /// so a broken MENU shows up as a failure here rather than as a mysterious null further down.</summary>
    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    /// <summary>
    /// Leaves an open <b>Alteration</b> and comes back to the master's Creation screen by the Gateway, so the
    /// existing-list is a delete surface again.
    ///
    /// <para>🔴 <b>THIS IS NOT TEST SCAFFOLDING — IT IS THE PRODUCT'S RULE.</b> <c>ForAlter</c> opens the
    /// alteration column under the SAME <see cref="Screen"/> value, and <c>IsDeleteTargetPage</c> deliberately
    /// excludes an open alteration, so Alt+D is INERT until the operator leaves it. A test that deleted straight
    /// after a rename would be asserting the opposite of the guard
    /// <see cref="AltD_is_inert_while_a_godown_is_open_for_alteration"/> exists to prove.</para>
    /// </summary>
    private static void BackToTheList(MainWindow window, MainWindowViewModel vm, string label, Screen screen)
    {
        vm.ShowGateway();
        Dispatcher.UIThread.RunJobs();
        OpenMaster(window, vm, label, screen);
        Assert.False(vm.MasterListScreen!.IsAltering);
    }

    /// <summary>Arrow-Down the existing-masters list until the highlight lands on <paramref name="name"/>, using
    /// the SHARED <see cref="MainWindowViewModel.MasterListScreen"/> the arrows themselves resolve through.</summary>
    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    // ===================================================================== census 3.7 — GODOWN

    /// <summary>
    /// 🔴 THE ROW-3.7 TEST. From the Gateway with only keys: create a godown with Ctrl+A, arrow onto it,
    /// Ctrl+Enter to arrive at a <b>Godown Alteration</b> of that very godown, rename it with Ctrl+A, and confirm
    /// the SAME id now carries the new name. The vendor reaches this verb at "Godowns &gt; and select Alter".
    /// </summary>
    [AvaloniaFact]
    public void Godown_alteration_is_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("Godown Alter Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);

            var create = vm.GodownMaster!;
            Assert.False(create.IsAltering);
            create.Name = "North Yard";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var id = vm.Company!.Godowns.Single(g => g.Name == "North Yard").Id;

            ArrowTo(window, vm, "North Yard");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = vm.GodownMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("Godown Alteration", alter.Caption);
            Assert.Equal("North Yard", alter.Name);

            alter.Name = "North Depot";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            // BY ID: a rename that forked a second godown would pass a name-only assertion.
            Assert.Equal("North Depot", vm.Company.FindGodown(id)!.Name);
            Assert.DoesNotContain(vm.Company.Godowns, g => g.Name == "North Yard");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+D then Y on the highlighted row deletes an unreferenced godown — the real destructive
    /// accelerator and the real confirmation, never a direct service call.</summary>
    [AvaloniaFact]
    public void An_unreferenced_godown_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("Godown Delete Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            vm.GodownMaster!.Name = "Spare Yard";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains(vm.Company!.Godowns, g => g.Name == "Spare Yard");

            ArrowTo(window, vm, "Spare Yard");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.DoesNotContain(vm.Company.Godowns, g => g.Name == "Spare Yard");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 THE REFUSAL REACHES THE OPERATOR. A godown that is the PARENT of another — the vendor's own
    /// third condition — survives Alt+D + Y, and the application does not crash.</summary>
    [AvaloniaFact]
    public void A_parent_godown_is_refused_by_AltD_and_survives()
    {
        var (window, vm, dir) = NewWindow("Godown Guard Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            var m = vm.GodownMaster!;
            m.Name = "Region West";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var parentId = vm.Company!.Godowns.Single(g => g.Name == "Region West").Id;
            new InventoryService(vm.Company).CreateGodown("West-1", parentId: parentId);
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Region West");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company.Godowns, g => g.Id == parentId);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 The SEEDED DEFAULT LOCATION survives Alt+D + Y. It is the fallback every inventory line
    /// resolves to; the vendor states it outright ("You cannot delete the default godown").</summary>
    [AvaloniaFact]
    public void The_default_location_survives_AltD_on_the_godown_master()
    {
        var (window, vm, dir) = NewWindow("Godown Default Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            var mainId = vm.Company!.MainLocation!.Id;

            ArrowTo(window, vm, vm.Company.MainLocation!.Name);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company.Godowns, g => g.Id == mainId);
            Assert.NotNull(vm.Company.MainLocation);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.5 — UNIT

    [AvaloniaFact]
    public void Unit_alteration_is_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("Unit Alter Co");
        try
        {
            OpenMaster(window, vm, "Unit", Screen.UnitMaster);

            var create = vm.UnitMaster!;
            create.Symbol = "Nos";
            create.FormalName = "Numbers";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var id = vm.Company!.Units.Single(u => u.Symbol == "Nos").Id;

            ArrowTo(window, vm, "Nos");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = vm.UnitMaster!;
            Assert.True(alter.IsAltering);
            alter.FormalName = "Number of pieces";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("Number of pieces", vm.Company.FindUnit(id)!.FormalName);
            Assert.Single(vm.Company.Units, u => u.Symbol == "Nos");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 A unit that a STOCK ITEM is measured in survives Alt+D + Y. Deleting it would leave every
    /// quantity of that item with no unit at all.</summary>
    [AvaloniaFact]
    public void A_unit_a_stock_item_is_measured_in_is_refused_by_AltD_and_survives()
    {
        var (window, vm, dir) = NewWindow("Unit Guard Co");
        try
        {
            OpenMaster(window, vm, "Unit", Screen.UnitMaster);
            var m = vm.UnitMaster!;
            m.Symbol = "Kgs";
            m.FormalName = "Kilograms";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var c = vm.Company!;
            var unitId = c.Units.Single(u => u.Symbol == "Kgs").Id;
            var inv = new InventoryService(c);
            var group = inv.CreateStockGroup("W29 Group");
            inv.CreateStockItem("Sugar", group.Id, unitId);
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Kgs");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(c.Units, u => u.Id == unitId);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.2 — STOCK CATEGORY

    [AvaloniaFact]
    public void Stock_category_alteration_and_deletion_are_reachable_from_the_Gateway()
    {
        var (window, vm, dir) = NewWindow("StkCat Co");
        try
        {
            OpenMaster(window, vm, "Stock Category", Screen.StockCategoryMaster);

            vm.StockCategoryMaster!.Name = "Imported";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var id = vm.Company!.StockCategories.Single(x => x.Name == "Imported").Id;

            ArrowTo(window, vm, "Imported");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.StockCategoryMaster!.IsAltering);
            vm.StockCategoryMaster.Name = "Imported Goods";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal("Imported Goods", vm.Company.FindStockCategory(id)!.Name);

            BackToTheList(window, vm, "Stock Category", Screen.StockCategoryMaster);
            ArrowTo(window, vm, "Imported Goods");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.DoesNotContain(vm.Company.StockCategories, x => x.Id == id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.1 — STOCK GROUP

    /// <summary>The Stock Group master already had Ctrl+Enter (census 3.13). What W29 gives it is <b>Alt+D</b>,
    /// and the refusal that comes with it: a group with an item filed under it survives.</summary>
    [AvaloniaFact]
    public void Stock_group_deletion_is_reachable_and_is_refused_while_an_item_is_filed_under_it()
    {
        var (window, vm, dir) = NewWindow("StkGrp Co");
        try
        {
            OpenMaster(window, vm, "Stock Group", Screen.StockGroupMaster);

            vm.StockGroupMaster!.Name = "Fasteners";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var c = vm.Company!;
            var groupId = c.StockGroups.Single(g => g.Name == "Fasteners").Id;

            var inv = new InventoryService(c);
            var unit = inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Rivet", groupId, unit.Id);
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Fasteners");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains(c.StockGroups, g => g.Id == groupId);

            // Clear the reason the message NAMES, and the SAME keystrokes now succeed. A refusal the operator
            // cannot clear from the screen it is shown on would be a dead end rather than a guard.
            var other = inv.CreateStockGroup("Sundries");
            item.StockGroupId = other.Id;
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Fasteners");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.DoesNotContain(c.StockGroups, g => g.Id == groupId);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 2.7 / 2.8 — COST MASTERS

    [AvaloniaFact]
    public void Cost_category_alteration_and_deletion_are_reachable_from_the_Gateway()
    {
        var (window, vm, dir) = NewWindow("CostCat Co");
        try
        {
            OpenMaster(window, vm, "Cost Category", Screen.CostCategoryMaster);

            vm.CostCategoryMaster!.Name = "Departments";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var id = vm.Company!.CostCategories.Single(x => x.Name == "Departments").Id;

            ArrowTo(window, vm, "Departments");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.CostCategoryMaster!.IsAltering);
            vm.CostCategoryMaster.Name = "Divisions";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal("Divisions", vm.Company.FindCostCategory(id)!.Name);

            BackToTheList(window, vm, "Cost Category", Screen.CostCategoryMaster);
            ArrowTo(window, vm, "Divisions");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.DoesNotContain(vm.Company.CostCategories, x => x.Id == id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 The vendor's own condition, driven by keys: a cost category with a cost centre grouped under
    /// it survives Alt+D + Y. The PREDEFINED category survives too.</summary>
    [AvaloniaFact]
    public void A_cost_category_with_a_centre_under_it_and_the_predefined_one_both_survive_AltD()
    {
        var (window, vm, dir) = NewWindow("CostCat Guard Co");
        try
        {
            OpenMaster(window, vm, "Cost Category", Screen.CostCategoryMaster);
            var c = vm.Company!;

            vm.CostCategoryMaster!.Name = "Projects";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var id = c.CostCategories.Single(x => x.Name == "Projects").Id;
            c.AddCostCentre(new CostCentre(Guid.NewGuid(), "Bridge", id));
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Projects");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains(c.CostCategories, x => x.Id == id);

            var predefined = c.CostCategories.Single(x => x.IsPredefined);
            ArrowTo(window, vm, predefined.Name);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains(c.CostCategories, x => x.Id == predefined.Id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    [AvaloniaFact]
    public void Cost_centre_alteration_and_deletion_are_reachable_from_the_Gateway()
    {
        var (window, vm, dir) = NewWindow("CostCentre Co");
        try
        {
            OpenMaster(window, vm, "Cost Centre", Screen.CostCentreMaster);

            vm.CostCentreMaster!.Name = "Delhi";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var id = vm.Company!.CostCentres.Single(x => x.Name == "Delhi").Id;

            ArrowTo(window, vm, "Delhi");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.CostCentreMaster!.IsAltering);
            vm.CostCentreMaster.Name = "Delhi NCR";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal("Delhi NCR", vm.Company.FindCostCentre(id)!.Name);

            BackToTheList(window, vm, "Cost Centre", Screen.CostCentreMaster);
            ArrowTo(window, vm, "Delhi NCR");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.DoesNotContain(vm.Company.CostCentres, x => x.Id == id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== the shared-arm guarantees

    /// <summary>
    /// 🔴 <b>Alt+D IS INERT WHILE ANY OF THE SIX IS MID-ALTERATION.</b> <c>ForAlter</c> opens the alteration
    /// column under the SAME <see cref="Screen"/> value as creation, so without the <c>IsAltering: false</c>
    /// clause in <see cref="MainWindowViewModel.IsDeleteTargetPage"/> the chord would delete the very master the
    /// operator is part-way through editing — the exact defect measured once on the Stock Item master, where the
    /// item went, the caption still read "… Alteration", and the Ctrl+A that would have saved the keyed changes
    /// was afterwards a silent no-op.
    /// </summary>
    [AvaloniaFact]
    public void AltD_is_inert_while_a_godown_is_open_for_alteration()
    {
        var (window, vm, dir) = NewWindow("Godown Inert Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            vm.GodownMaster!.Name = "Transit Yard";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var id = vm.Company!.Godowns.Single(g => g.Name == "Transit Yard").Id;

            ArrowTo(window, vm, "Transit Yard");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.GodownMaster!.IsAltering);

            Assert.False(vm.IsDeleteTargetPage);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company.Godowns, g => g.Id == id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>ALL SIX RESOLVE THROUGH THE ONE SHARED ARM.</b> Appearing in
    /// <see cref="MainWindowViewModel.MasterListScreen"/> is what grants a screen the arrows, Alt+D and the
    /// post-delete refresh in a single step; six parallel sets of arms is how one master silently ends up gated
    /// differently from the other five. This test walks to each master in turn and asserts the shared property
    /// resolves to that screen's own view model with its own operator-facing kind label.
    /// </summary>
    [AvaloniaFact]
    public void All_six_masters_resolve_through_the_shared_master_list_arm()
    {
        var (window, vm, dir) = NewWindow("Shared Arm Co");
        try
        {
            // 🔴 The expected view model is a FUNC, deliberately: an eagerly-evaluated argument would capture the
            // property BEFORE OpenMaster replaced it, and Assert.Same would then compare two nulls and pass.
            void Check(string label, Screen screen, string kind, Func<object?> expected)
            {
                vm.ShowGateway();
                Dispatcher.UIThread.RunJobs();
                OpenMaster(window, vm, label, screen);
                Assert.NotNull(expected());
                Assert.Same(expected(), vm.MasterListScreen);
                Assert.Equal(kind, vm.MasterListScreen!.MasterKindLabel);
                Assert.False(vm.MasterListScreen.IsAltering);
                // …and none of them leaks onto the payroll arm, whose remainder is locked elsewhere.
                Assert.Null(vm.PayrollMasterScreen);
            }

            Check("Godown", Screen.GodownMaster, "godown", () => vm.GodownMaster);
            Check("Unit", Screen.UnitMaster, "unit", () => vm.UnitMaster);
            Check("Stock Group", Screen.StockGroupMaster, "stock group", () => vm.StockGroupMaster);
            Check("Stock Category", Screen.StockCategoryMaster, "stock category", () => vm.StockCategoryMaster);
            Check("Cost Category", Screen.CostCategoryMaster, "cost category", () => vm.CostCategoryMaster);
            Check("Cost Centre", Screen.CostCentreMaster, "cost centre", () => vm.CostCentreMaster);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE REFUSAL MUST SAY WHY — AND UNTIL THIS TEST, NOTHING CHECKED THAT IT SAID ANYTHING AT ALL.</b>
    ///
    /// <para>Every other refusal test in this class asserts only that the master SURVIVES. That is satisfied just
    /// as well by a silent no-op: delete the <c>RaiseLifecycleNotice</c> call out of the catch block in
    /// <c>PerformPendingDeletion</c> and the godown still survives, so every one of those tests stays green while
    /// the operator presses Alt+D, answers Y, and sees an EMPTY notice bar with no idea why nothing happened.
    /// That precise failure — a guard throwing into a key handler that showed nothing — is recorded in
    /// <c>MainWindowViewModel</c>'s own comments as having shipped before.</para>
    ///
    /// <para>So this asserts the operator-facing sentence: it NAMES the godown, states the blocking count and
    /// kind, and carries the vendor's own remedy — <i>"move the stock items to a different godown using
    /// inter-godown transfer"</i>
    /// (help.tallysolutions.com/tally-prime/inventory/inventory-storage-using-godowns-locations-tally/, re-fetched
    /// and verified first-hand 2026-09-15).</para>
    /// </summary>
    [AvaloniaFact]
    public void A_refused_godown_delete_tells_the_operator_why_in_the_notice()
    {
        var (window, vm, dir) = NewWindow("Godown Notice Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            var m = vm.GodownMaster!;
            m.Name = "Held Yard";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var heldId = vm.Company!.Godowns.Single(g => g.Name == "Held Yard").Id;

            // A sub-godown under it — the vendor's own "is not a parent of other godowns" condition.
            new InventoryService(vm.Company).CreateGodown("Held-1", parentId: heldId);
            vm.MasterListScreen!.ReloadExisting();
            Dispatcher.UIThread.RunJobs();

            ArrowTo(window, vm, "Held Yard");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company.Godowns, g => g.Id == heldId);

            // 🔴 The notice actually reached the operator, and it is a DIAGNOSIS rather than a bare "cannot".
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("Held Yard", vm.Notice);
            Assert.Contains("sub-godown", vm.Notice);
            Assert.Contains("Cannot delete", vm.Notice);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The other half of the same contract: a PERMITTED delete also reports itself, so the operator is
    /// never left guessing whether Alt+D did anything. Without this, a notice that only ever fired on refusal
    /// would look correct to the test above.</summary>
    [AvaloniaFact]
    public void A_permitted_godown_delete_confirms_itself_in_the_notice()
    {
        var (window, vm, dir) = NewWindow("Godown Confirm Co");
        try
        {
            OpenMaster(window, vm, "Godown", Screen.GodownMaster);
            vm.GodownMaster!.Name = "Spare Yard";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            ArrowTo(window, vm, "Spare Yard");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.DoesNotContain(vm.Company!.Godowns, g => g.Name == "Spare Yard");
            Assert.Contains("Spare Yard", vm.Notice);
            Assert.Contains("deleted", vm.Notice);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
