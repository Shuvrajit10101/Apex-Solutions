using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🟠 DEFECT-1 LOCK — <b>Stock Item alteration is REACHABLE</b>, proven by driving the real cascade and the real
/// keyboard through <see cref="MainWindow"/>'s tunnel handler, never by constructing a view model.
///
/// <para><b>The defect this locks.</b> <c>StockItemMasterViewModel.ForAlter</c> shipped with <b>zero production
/// callers</b>: the Ctrl+A dispatch for <c>Screen.StockItemMaster</c> had no <c>IsAltering</c> branch (unlike
/// Ledger and Group), and the only Alter entry point in the app — the Chart of Accounts — is an ACCOUNTS surface
/// whose rows carry a <c>LedgerId</c>/<c>GroupId</c> and no stock-item identity at all. The shipped test built
/// <c>ForAlter</c> DIRECTLY, so it proved the MECHANISM and nothing whatsoever about reachability: the operator
/// could not get to an altering Stock Item master by any sequence of keys.</para>
///
/// <para><b>Why these tests are written this way.</b> This project's carry-forwards record the exact trap — "a
/// <c>ShowXMenu()</c> test proves nothing about reachability". So every step below is a REAL keystroke:
/// arrow-Down walks the menu columns, Enter drills them, Ctrl+A saves, Down enters the existing-items list and
/// Ctrl+Enter opens the highlighted item for alteration. Nothing here calls <c>ForAlter</c>, and nothing calls a
/// <c>Show*</c> method to skip a step.</para>
/// </summary>
public sealed class StockItemAlterReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexStockAlter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
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

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The label highlighted in the ACTIVE cascade column (the one the arrows are steering).</summary>
    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    /// <summary>
    /// Presses REAL arrow-Down until the active column highlights <paramref name="label"/>, then REAL Enter to
    /// drill into it. Fails loudly if the label is not reachable by arrows — which is the whole point.
    /// </summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label)
            {
                Key(window, PhysicalKey.Enter);
                return;
            }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    /// <summary>Seeds a company plus the one stock group and unit a stock item needs, using the real masters.</summary>
    private static void SeedInventoryPrerequisites(MainWindowViewModel vm, string companyName)
    {
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        vm.ShowStockGroupMaster();
        vm.StockGroupMaster!.Name = "Hardware";
        Assert.True(vm.StockGroupMaster!.Create(), vm.StockGroupMaster!.Message);

        vm.ShowUnitMaster();
        vm.UnitMaster!.Symbol = "Nos";
        vm.UnitMaster!.FormalName = "Numbers";
        Assert.True(vm.UnitMaster!.Create(), vm.UnitMaster!.Message);

        // Back to a clean Gateway so the navigation below starts where an operator starts.
        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
    }

    // ================================================================= THE REACHABILITY PROOF

    /// <summary>
    /// 🟠 THE DEFECT-1 TEST. From the Gateway, using only keys: drill <b>Create → Stock Item</b>, create "Widget"
    /// with Ctrl+A, arrow-Down into the existing-items list, and Ctrl+Enter to arrive at a Stock Item master that
    /// <b>IsAltering</b> the item just created, pre-filled with its values.
    ///
    /// <para><b>This test bites.</b> Removing the <c>AlterHighlightedStockItemRow</c> arm from
    /// <c>MainWindow.OnKeyDown</c> fails it at the <c>IsAltering</c> assertion — verified by doing exactly that
    /// against a checksummed backup and restoring byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Stock_item_alteration_is_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedInventoryPrerequisites(vm, "Stock Alter Reach Co");

            // ---- REAL navigation: Gateway → Create → Stock Item ----
            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Stock Item");

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            var create = vm.StockItemMaster!;
            Assert.False(create.IsAltering);
            Assert.Equal("Stock Item Creation", create.Caption);

            // ---- REAL Ctrl+A creates ----
            create.SelectedGroup = create.Groups.First(g => g.Name == "Hardware");
            create.SelectedUnit = create.Units.First(u => u.Symbol == "Nos");
            create.Name = "Widget";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var itemId = vm.Company!.FindStockItemByName("Widget")!.Id;
            Assert.Contains(vm.StockItemMaster!.Existing, r => r.StockItemId == itemId);

            // ---- REAL arrow-Down enters the existing-items list ----
            Assert.Null(vm.StockItemMaster!.HighlightedRow);          // nothing highlighted until the operator asks
            Key(window, PhysicalKey.ArrowDown);
            Assert.NotNull(vm.StockItemMaster!.HighlightedRow);
            Assert.Equal("Widget", vm.StockItemMaster!.HighlightedRow!.Name);
            Assert.True(vm.StockItemMaster!.HighlightedRow!.IsHighlighted);

            // ---- REAL Ctrl+Enter opens THAT item for alteration ----
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            var alter = vm.StockItemMaster!;
            Assert.NotSame(create, alter);
            Assert.True(alter.IsAltering);                            // ← the assertion the defect made unreachable
            Assert.Equal("Stock Item Alteration", alter.Caption);
            Assert.Equal("Widget", alter.Name);                       // pre-filled from the stored item
            Assert.Equal("Hardware", alter.SelectedGroup!.Name);
            Assert.Equal("Nos", alter.SelectedUnit!.Symbol);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The other half of Defect 1: Ctrl+A on that altering screen must run <b>Alter</b>, not Create. Without the
    /// <c>IsAltering</c> branch in the Ctrl+A dispatch it ran <c>Create()</c>, which then failed the
    /// except-self name check — the operator's edits were silently discarded behind an "already exists" message.
    ///
    /// <para><b>This test bites.</b> Reverting the dispatch to a bare <c>StockItemMaster?.Create();</c> fails it
    /// on the persisted name — verified, not assumed.</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_on_the_altering_stock_item_screen_alters_instead_of_creating()
    {
        var (window, vm, dir) = NewWindow();
        const string companyName = "Stock Alter CtrlA Co";
        try
        {
            SeedInventoryPrerequisites(vm, companyName);

            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Stock Item");

            var create = vm.StockItemMaster!;
            create.SelectedGroup = create.Groups.First(g => g.Name == "Hardware");
            create.SelectedUnit = create.Units.First(u => u.Symbol == "Nos");
            create.Name = "Widget";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var itemId = vm.Company!.FindStockItemByName("Widget")!.Id;
            var itemsBefore = vm.Company!.StockItems.Count;

            Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.StockItemMaster!.IsAltering);

            // Rename through the ALTER screen and accept with the real Ctrl+A.
            vm.StockItemMaster!.Name = "Widget Mk II";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            // The SAME item was renamed — not a second item created, and not a failed "already exists".
            Assert.Equal(itemsBefore, vm.Company!.StockItems.Count);
            Assert.Equal("Widget Mk II", vm.Company!.FindStockItem(itemId)!.Name);

            // …and it survived the round-trip to disk, saved against the stable Guid.
            var storage = new CompanyStorage(dir);
            var reloaded = storage.Load(storage.ListCompanies().Single(e => e.Name == companyName));
            Assert.Equal("Widget Mk II", reloaded.FindStockItem(itemId)!.Name);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// Ctrl+Enter must stay a no-op where there is nothing to alter, so claiming the chord does not shadow it
    /// anywhere else: on the Stock Item master with NO row highlighted, the screen must not change.
    /// </summary>
    [AvaloniaFact]
    public void CtrlEnter_with_no_row_highlighted_does_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedInventoryPrerequisites(vm, "Stock Alter NoOp Co");

            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Stock Item");

            var create = vm.StockItemMaster!;
            create.SelectedGroup = create.Groups.First(g => g.Name == "Hardware");
            create.SelectedUnit = create.Units.First(u => u.Symbol == "Nos");
            create.Name = "Widget";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            // No Down pressed — nothing is highlighted.
            Assert.Null(vm.StockItemMaster!.HighlightedRow);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.False(vm.StockItemMaster!.IsAltering);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
