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
/// 🔴 <b>CENSUS 3.4 / USER RULING 26 — THE MARKET VALUATION FIELD AND THE UPGRADE BANNER ARE ACTUALLY ON THE
/// SCREEN, proven by driving the real keyboard through the real <see cref="MainWindow"/>.</b>
///
/// <para><b>Why this file exists alongside the view-model tests.</b> A test that asserted
/// <c>StockItemMasterViewModel.MarketValuationMethods</c> is populated would pass on a build where the picker
/// exists in no XAML at all — and this project has shipped that exact failure repeatedly: a working capability
/// with no route in, and controls that bind but never render. The census itself records the sibling case
/// (row 3.4's own evidence cell once claimed a control was missing because the grep targeted the literal rather
/// than the binding). So every step here is a REAL keystroke, and every control must be <b>realised,
/// effectively visible and laid out with non-zero bounds</b> before the test believes an operator can use it.</para>
///
/// <para><b>Red on today's main</b>, where neither the market-valuation picker nor the banner exists.</para>
/// </summary>
public sealed class MarketValuationRouteVisibilityTests
{
    // ---------------------------------------------------------------- visual-tree harness

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>A realised, visible, laid-out control. All three conditions together are what "the operator can
    /// see and use it" means — present-but-collapsed and visible-but-zero-sized each satisfy only one.</summary>
    private static T? LiveControl<T>(MainWindow window, Func<T, bool> match) where T : Control =>
        Descendants(window).OfType<T>().FirstOrDefault(c =>
            c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && match(c));

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    /// <summary>Real arrow-Down until the active column highlights the label, then real Enter to drill in.</summary>
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

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexMktValRoute_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        return (window, vm, dir);
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

        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
    }

    // ================================================================= the field is on the screen

    /// <summary>
    /// 🔴 <b>THE MARKET VALUATION PICKER AND ITS STANDARD-PRICE BOX ARE REACHABLE FROM THE GATEWAY WITH ONLY THE
    /// KEYBOARD, AND ARE REALLY RENDERED.</b> Route: <b>Create → Stock Item</b>. Both controls must be realised,
    /// visible and laid out — a picker that exists only in the view model is a capability with no route in.
    /// </summary>
    [AvaloniaFact]
    public void The_market_valuation_field_is_reachable_and_rendered_on_the_stock_item_master()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedInventoryPrerequisites(vm, "Market Route Co");

            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Stock Item");
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Pump(window);

            var picker = LiveControl<ComboBox>(window, c => c.Name == "StockItemMarketValuationPicker");
            Assert.True(picker is not null,
                "The market-valuation picker is not realised, visible and laid out on the Stock Item master.");

            // It really carries the vendor's four methods, on screen — not just in the view model.
            Assert.Equal(4, picker!.ItemCount);

            // And the standard-PRICE box is on the same screen, distinct from the standard-COST box.
            var priceBox = LiveControl<TextBox>(window, t => t.PlaceholderText == "₹ / unit (selling)");
            Assert.True(priceBox is not null,
                "The standard-PRICE box is not realised, visible and laid out on the Stock Item master.");
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================= the banner is on the screen

    /// <summary>
    /// Seeds a saved book holding one stock item, optionally carrying the marker schema v65's migration stamps.
    /// </summary>
    private static void SeedSavedCompany(string dir, string name, bool remediated)
    {
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var company = vm.Company!;
        var masters = new InventoryService(company);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem(
            "Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.LastPurchaseCost);
        if (remediated) item.ValuationRemediatedFrom = StockValuationMethod.LastSaleCost;

        storage.Save(company);
    }

    /// <summary>
    /// 🔴 <b>THE RULING-26 WARNING IS A BANNER THE OPERATOR ACTUALLY SEES, not a string in a view model.</b>
    /// A book whose Balance Sheet moved on upgrade is opened through the real Company Info → Select Company
    /// route, and the banner's own <c>TextBlock</c> must be realised, visible, laid out and carrying the
    /// explanation. Ruling 26 required the operator be told; a notice nothing renders tells nobody.
    /// </summary>
    [AvaloniaFact]
    public void An_affected_book_shows_the_upgrade_banner_on_screen_when_it_is_opened()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexMktValBanner_" + Guid.NewGuid().ToString("N"));
        SeedSavedCompany(dir, "Remediated Co", remediated: true);

        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            vm.ShowCompanySelect();
            var entry = vm.Menu.First(m => m.Label == "Remediated Co");
            entry.Activate();
            Pump(window);

            var banner = LiveControl<TextBlock>(window, t => t.Name == "ValuationRemediationWarningText");
            Assert.True(banner is not null,
                "The ruling-26 upgrade banner is not realised, visible and laid out for an AFFECTED book.");
            Assert.Contains("Widget", banner!.Text ?? string.Empty);
            Assert.Contains("Balance Sheet", banner.Text ?? string.Empty);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>AND AN UNAFFECTED BOOK RENDERS NO BANNER AT ALL.</b> This is the half that makes the warning worth
    /// reading: a banner shown on every open is one nobody reads. The control must not merely be blank — it must
    /// not be laid out on the screen.
    /// </summary>
    [AvaloniaFact]
    public void An_unaffected_book_renders_no_upgrade_banner()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexMktValNoBanner_" + Guid.NewGuid().ToString("N"));
        SeedSavedCompany(dir, "Untouched Co", remediated: false);

        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            vm.ShowCompanySelect();
            var entry = vm.Menu.First(m => m.Label == "Untouched Co");
            entry.Activate();
            Pump(window);

            var banner = LiveControl<TextBlock>(window, t => t.Name == "ValuationRemediationWarningText");
            Assert.True(banner is null,
                "An UNAFFECTED book rendered the ruling-26 upgrade banner — a warning everyone sees is one nobody reads.");
        }
        finally { Cleanup(window, dir); }
    }
}
