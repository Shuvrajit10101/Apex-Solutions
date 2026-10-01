using System;
using System.Collections.Generic;
using System.Globalization;
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
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.4 — THE MARKET VALUATION AUTO-FILLED RATE IS REALLY ON THE SCREEN, reached with real
/// keystrokes through the real <see cref="MainWindow"/>.</b>
///
/// <para><b>Why a rendered test and not only the view-model ones.</b> This project has repeatedly shipped a
/// working capability with no route in, and <see cref="MarketValuationService"/> was the newest instance: a
/// service with zero production callers. A view-model assertion alone would pass on a build where the figure
/// never reaches a rendered control. So the route here is the operator's own — <b>Gateway → F8 (Sales) → Ctrl+H
/// (Change Mode → Item Invoice)</b>, both <b>real key presses through the window</b> — and the Rate
/// <see cref="TextBox"/> that carries the figure must be <b>realised, effectively visible and laid out with
/// non-zero bounds</b> before the test believes an operator can see it.</para>
///
/// <para><b>Red on today's main</b>, where nothing calls the service and the box renders empty.</para>
/// </summary>
public sealed class MarketValuationSalesRouteTests
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

    /// <summary>A realised, visible, laid-out control. All three together are what "the operator can see it"
    /// means — present-but-collapsed and visible-but-zero-sized each satisfy only one.</summary>
    private static T? LiveControl<T>(MainWindow window, Func<T, bool> match) where T : Control =>
        Descendants(window).OfType<T>().FirstOrDefault(c =>
            c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && match(c));

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Everything a <see cref="TextBlock"/> actually paints, including the <c>Run</c> inlines a composite caption
    /// is built from — <c>TextBlock.Text</c> is <c>null</c> on an inline-built block, so matching on it alone
    /// would miss exactly the captions this screen uses for its totals.
    /// </summary>
    private static string RenderedText(TextBlock t)
    {
        if (!string.IsNullOrEmpty(t.Text)) return t.Text!;
        if (t.Inlines is null) return string.Empty;
        return string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text ?? string.Empty));
    }

    /// <summary>The item-invoice line's Rate box, identified by its own line view model rather than by position —
    /// several screens carry a <c>RateText</c> box with the same placeholder.</summary>
    private static TextBox? LiveRateBox(MainWindow window) =>
        LiveControl<TextBox>(window,
            t => t.DataContext is InventoryVoucherLineViewModel && t.PlaceholderText == "0.00");

    // ---------------------------------------------------------------- seeding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// A book with "Widget" (opening 100 Nos @ ₹100/unit) on the given market valuation method, a Sales ledger, a
    /// customer, and <b>one posted sale of 20 Nos @ ₹180</b> — so the Last Sales Price is ₹180.00 by hand.
    /// </summary>
    private static Kit Seed(MainWindowViewModel vm, string companyName, MarketValuationMethod method)
    {
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        item.MarketValuationMethod = method;
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");

        var kit = new Kit
        {
            Vm = vm, ItemId = item.Id, GodownId = c.MainLocation!.Id, CustomerId = customer.Id,
        };

        // One posted sale at a TYPED rate (typing dirties the line, so no auto-fill can displace ₹180).
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
        var seedLine = entry.InventoryLines[0];
        seedLine.SelectedItem = entry.StockItems.Single(i => i.Id == item.Id);
        seedLine.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.GodownId);
        seedLine.QuantityText = "20";
        seedLine.RateText = "180";
        Assert.True(entry.Accept(), entry.Message);

        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
        return kit;
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexMktValSalesRoute_" + Guid.NewGuid().ToString("N"));
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

    // ================================================================= the figure is on the screen

    /// <summary>
    /// 🔴 <b>REAL F8, REAL Ctrl+H, AND THE RATE BOX ON SCREEN READS 180.00.</b> The item's Market Valuation
    /// Method is Last Sales Price and its only sale was 20 Nos @ ₹180, so <b>₹180.00</b> is the figure an operator
    /// must find waiting in the Rate column — hand-computed, and deliberately different from the ₹100/unit cost
    /// so a costing/market mix-up cannot pass this test.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "PhaseGate")]
    public void F8_then_CtrlH_renders_a_sales_line_whose_rate_is_auto_filled_from_the_market_valuation_method()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "MV Rendered Co", MarketValuationMethod.LastSalesPrice);

            Key(window, PhysicalKey.F8);                                   // Sales
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Sales, vm.VoucherEntry!.Type.BaseType);

            Key(window, PhysicalKey.H, RawInputModifiers.Control);         // Change Mode → Item Invoice
            Pump(window);
            var entry = vm.VoucherEntry!;
            Assert.True(entry.IsItemInvoice, "Ctrl+H did not reach item-invoice mode, so there is no route in.");

            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            var line = entry.InventoryLines[0];
            line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
            line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            line.QuantityText = "5";
            Pump(window);

            var rateBox = LiveRateBox(window);
            Assert.True(rateBox is not null,
                "The item-invoice Rate box is not realised, visible and laid out on the Sales screen.");
            Assert.Equal("180.00", rateBox!.Text);                          // ON SCREEN, not just in the view model

            // And it is a real figure, not decoration: the rendered items total follows it. 5 × 180 = 900.00.
            var totalBox = LiveControl<TextBlock>(window, t => RenderedText(t).Contains("900.00"));
            Assert.True(totalBox is not null,
                "The stamped rate did not reach a rendered total — 5 × 180 = 900.00 is nowhere on the screen.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>AND THE SILENT HALF: an At Zero Price item renders an EMPTY Rate box on the same route.</b> That is
    /// the state of every item in every book that existed before schema v65 (<c>AtZeroPrice</c> is ordinal 0), so
    /// this is the assertion that says the change is invisible to an existing book (ER-13) — measured on the
    /// rendered control, not argued from the default.
    /// </summary>
    [AvaloniaFact]
    public void An_at_zero_price_item_renders_an_empty_rate_box_on_the_same_route()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "MV Rendered Zero Co", MarketValuationMethod.AtZeroPrice);

            Key(window, PhysicalKey.F8);
            Key(window, PhysicalKey.H, RawInputModifiers.Control);
            Pump(window);
            var entry = vm.VoucherEntry!;
            Assert.True(entry.IsItemInvoice);

            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            var line = entry.InventoryLines[0];
            line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
            line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            line.QuantityText = "5";
            Pump(window);

            var rateBox = LiveRateBox(window);
            Assert.True(rateBox is not null, "The item-invoice Rate box is not on the screen at all.");
            Assert.True(string.IsNullOrEmpty(rateBox!.Text),
                $"An At Zero Price item pre-filled the Rate box with '{rateBox.Text}' — an existing book changed.");
        }
        finally { Cleanup(window, dir); }
    }
}
