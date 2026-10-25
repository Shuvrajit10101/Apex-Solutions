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
/// 🔴 <b>CENSUS 3.11, THE LAST OPEN CLAUSE — "no route deletes a whole price list for a (level, item) pair",
/// DRIVEN BY REAL KEYSTROKES AND BY THE REAL BUTTON.</b>
///
/// <para><b>WHY A SERVICE TEST WOULD NOT HAVE CLOSED IT.</b> The clause is about a ROUTE, not about an engine
/// method. This project has graded an engine with no reachable caller as progress four times, and
/// <c>Company.RemovePriceList</c> itself sat in the engine with zero callers anywhere in the product until wave
/// 33. So every test below opens the real <see cref="MainWindow"/>, walks the Gateway cascade with arrows and
/// Enter, and presses the real accelerator — or clicks the real, effectively-visible button.</para>
///
/// <para>🔴 <b>AND WHY IT MATTERS MORE THAN TIDINESS.</b> A (level, item) pair with any surviving version is
/// PRICED: <c>PriceResolver</c> offers the latest version whose Applicable-From is ≤ the voucher date. Alt+D on
/// this screen withdraws the ONE highlighted version, so removing a re-priced list one row at a time falls the
/// pair back onto the OLDEST rate — live again on every later invoice. The assertions below therefore end at
/// <c>PriceResolver</c> on the book RE-READ FROM SQLITE, not at a row count.</para>
///
/// <para>⚠️ <b>FIDELITY (R7, ruling 14): THE VERB AND THE CHORD ARE BOTH <u>OURS</u>.</b>
/// <c>help.tallysolutions.com/tally-prime/sales-purchase-voucher-entry/selling-buying-prices/</c> was opened and
/// read BY CONTENT on 2026-10-23. It documents creating a price list, revising one — <i>"Press Alt+G (Go To) &gt;
/// Alter Master &gt; Price List (Stock Group or Stock Category) &gt; press Alt+R (Revise Price)."</i> — and
/// altering one — <i>"You can alter a price list by overwriting the details entered in the Price List
/// screen."</i> — and it <b>never mentions deleting a price list at all</b>. That silence is recorded as
/// silence: it is not read as permission and not as prohibition, and <b>no vendor sentence is claimed</b> for
/// either the act or the Alt+Shift+D chord. The existing per-version Alt+D on this screen is ours on the same
/// footing.</para>
/// </summary>
public sealed class PriceListWholeListDeleteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir, string Company) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCdWholeList_" + Guid.NewGuid().ToString("N"));
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

    private static void EnablePriceLevels(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.EnableMultiplePriceLevels = true;
        vm.Back();
        vm.ShowGateway();
        Dispatcher.UIThread.RunJobs();
    }

    private static void OpenPriceListMaster(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Price List");
        Assert.Equal(Screen.PriceListsMaster, vm.CurrentScreen);
        Assert.NotNull(vm.PriceLists);
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>The realised, effectively-visible button, found by NAME rather than by label text so the
    /// assertion cannot be satisfied by a button that stopped naming its verb.</summary>
    private static void ClickDeleteAll(MainWindow window)
    {
        var button = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => b.Name == "PriceListDeleteAllButton" && b.IsEffectivelyVisible);
        Assert.NotNull(button);
        button!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    /// <summary>One level, two items, and THREE dated versions on the (Wholesale, Widget) pair plus one on
    /// (Wholesale, Hinge) — so an over-reaching verb has a neighbour to damage and an under-reaching one has a
    /// survivor to leave behind.</summary>
    private static (PriceLevel Level, StockItem Widget, StockItem Hinge) Seed(MainWindowViewModel vm, string dir)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var widget = inv.CreateStockItem("Widget", group.Id, unit.Id);
        var hinge = inv.CreateStockItem("Hinge", group.Id, unit.Id);

        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");
        svc.AddOrReviseList(level.Id, widget.Id, new DateOnly(2024, 4, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) });
        svc.AddOrReviseList(level.Id, widget.Id, new DateOnly(2024, 7, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(110m)) });
        svc.AddOrReviseList(level.Id, widget.Id, new DateOnly(2024, 10, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(120m)) });
        svc.AddOrReviseList(level.Id, hinge.Id, new DateOnly(2024, 4, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(10m)) });
        new CompanyStorage(dir).Save(c);
        return (level, widget, hinge);
    }

    /// <summary>Point both pickers at the pair under test — the screen scopes its history to them, and the
    /// whole-list verb is armed against exactly that scope.</summary>
    private static void ScopeTo(MainWindowViewModel vm, PriceLevel level, StockItem item)
    {
        var lists = vm.PriceLists!;
        lists.SelectedLevel = lists.Levels.First(l => l.Id == level.Id);
        lists.SelectedItem = lists.Items.First(i => i.Id == item.Id);
        Dispatcher.UIThread.RunJobs();
    }

    // ============================================================ 1. the KEYBOARD route

    /// <summary>
    /// 🔴 <b>THE CLAUSE, CLOSED BY KEYSTROKE.</b> Alt+Shift+D then Y on the (Wholesale, Widget) pair. All three
    /// dated versions go in one act; the neighbouring (Wholesale, Hinge) list is untouched; and — the artefact —
    /// the pair is priced at NO date on the book re-read from SQLite, while Hinge still prices.
    /// </summary>
    [AvaloniaFact]
    public void Alt_Shift_D_then_Y_withdraws_every_version_of_the_scoped_price_list()
    {
        var (window, vm, dir, company) = NewWindow("Cd WholeList Key Co");
        try
        {
            EnablePriceLevels(vm);
            var (level, widget, hinge) = Seed(vm, dir);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);
            Assert.Equal(3, vm.PriceLists!.ScopedVersionCount);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt | RawInputModifiers.Shift);
            Assert.True(vm.IsAcceptPromptOpen, "Alt+Shift+D must put the confirmation up.");
            Assert.Contains("WHOLE price list", vm.AcceptPromptText ?? string.Empty);
            Assert.Contains("3 dated version", vm.AcceptPromptText ?? string.Empty);

            Key(window, PhysicalKey.Y);

            var reloaded = LoadBack(dir, company);
            Assert.Empty(reloaded.PriceLists.Where(
                pl => pl.PriceLevelId == level.Id && pl.StockItemId == widget.Id));

            // 🔴 THE ARTEFACT: no date is still priced for the withdrawn pair — one survivor would show here.
            foreach (var on in new[] { new DateOnly(2024, 5, 1), new DateOnly(2024, 8, 1), new DateOnly(2024, 11, 1) })
                Assert.Null(PriceResolver.Resolve(reloaded, level.Id, widget.Id, 5m, on));

            // …and the neighbour the operator did not name is still priced.
            Assert.NotNull(PriceResolver.Resolve(reloaded, level.Id, hinge.Id, 5m, new DateOnly(2024, 5, 1)));
            Assert.Equal(1, reloaded.PriceLists.Count);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 2. the POINTER route

    /// <summary>
    /// The button goes through the SAME <c>RequestDeleteWholePriceList</c> the chord does, so it arms the
    /// confirmation rather than deleting outright — the pointer/keyboard agreement T2-99 was filed over, on the
    /// one screen it was filed against.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_the_delete_price_list_button_arms_the_same_confirmation_and_Y_withdraws_it()
    {
        var (window, vm, dir, company) = NewWindow("Cd WholeList Click Co");
        try
        {
            EnablePriceLevels(vm);
            var (level, widget, _) = Seed(vm, dir);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);

            ClickDeleteAll(window);
            Assert.True(vm.IsAcceptPromptOpen, "The button must ARM the confirmation, never delete outright.");
            // Nothing has gone yet — the question is the guard.
            Assert.Equal(4, vm.Company!.PriceLists.Count);

            Key(window, PhysicalKey.Y);

            var reloaded = LoadBack(dir, company);
            Assert.Empty(reloaded.PriceLists.Where(
                pl => pl.PriceLevelId == level.Id && pl.StockItemId == widget.Id));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 3. N leaves everything standing

    /// <summary>Answering N withdraws nothing — on disk and in the aggregate. A destructive verb whose refusal
    /// is not tested is a verb whose confirmation is decoration.</summary>
    [AvaloniaFact]
    public void Answering_N_leaves_every_version_in_place()
    {
        var (window, vm, dir, company) = NewWindow("Cd WholeList No Co");
        try
        {
            EnablePriceLevels(vm);
            var (level, widget, _) = Seed(vm, dir);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt | RawInputModifiers.Shift);
            Key(window, PhysicalKey.N);

            Assert.Equal(4, vm.Company!.PriceLists.Count);
            Assert.Equal(4, LoadBack(dir, company).PriceLists.Count);
            Assert.NotNull(PriceResolver.Resolve(vm.Company!, level.Id, widget.Id, 5m, new DateOnly(2024, 11, 1)));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 4. plain Alt+D keeps its old meaning

    /// <summary>
    /// 🔴 <b>THE NON-REGRESSION THAT MAKES THE NEW CHORD HONEST.</b> Plain Alt+D on the same screen must still
    /// withdraw the ONE highlighted dated version and leave the rest priced. If the two chords ever collapsed
    /// into one meaning, this is where it would show — and the difference is a live invoice rate.
    /// </summary>
    [AvaloniaFact]
    public void Plain_Alt_D_still_withdraws_only_the_highlighted_version()
    {
        var (window, vm, dir, company) = NewWindow("Cd WholeList OneVer Co");
        try
        {
            EnablePriceLevels(vm);
            var (level, widget, _) = Seed(vm, dir);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);

            Key(window, PhysicalKey.ArrowDown);
            Assert.NotNull(vm.MasterListScreen!.HighlightedMasterRow);
            var highlighted = vm.MasterListScreen!.HighlightedMasterRow!.MasterId;

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            var reloaded = LoadBack(dir, company);
            Assert.DoesNotContain(reloaded.PriceLists, pl => pl.Id == highlighted);
            Assert.Equal(2, reloaded.PriceLists.Count(
                pl => pl.PriceLevelId == level.Id && pl.StockItemId == widget.Id));
            // Still priced — the pair survived, which is exactly the difference from the whole-list verb.
            Assert.NotNull(PriceResolver.Resolve(reloaded, level.Id, widget.Id, 5m, new DateOnly(2024, 5, 1)));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ 5. the gates

    /// <summary>A pair with no price list at all is a quiet no-op — a confirmation about nothing trains an
    /// operator to answer prompts without reading them.</summary>
    [AvaloniaFact]
    public void A_pair_with_no_price_list_puts_no_question_up()
    {
        var (window, vm, dir, _) = NewWindow("Cd WholeList Empty Co");
        try
        {
            EnablePriceLevels(vm);
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
            var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
            var widget = inv.CreateStockItem("Widget", group.Id, unit.Id);
            var level = new PriceListService(c).CreateLevel("Wholesale");
            new CompanyStorage(dir).Save(c);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);
            Assert.Equal(0, vm.PriceLists!.ScopedVersionCount);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt | RawInputModifiers.Shift);
            Assert.False(vm.IsAcceptPromptOpen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE PAIR IS RE-CHECKED WHEN Y IS PRESSED, NOT ONLY WHEN THE QUESTION GOES UP.</b> The pickers can
    /// move while a confirmation is on screen. A question read over "Wholesale / Widget" must not execute
    /// against "Wholesale / Hinge" — so moving the item picker after arming makes the Y a no-op, and BOTH lists
    /// survive.
    /// </summary>
    [AvaloniaFact]
    public void Moving_the_pair_after_arming_makes_the_confirmation_inert()
    {
        var (window, vm, dir, company) = NewWindow("Cd WholeList Moved Co");
        try
        {
            EnablePriceLevels(vm);
            var (level, widget, hinge) = Seed(vm, dir);

            OpenPriceListMaster(window, vm);
            ScopeTo(vm, level, widget);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt | RawInputModifiers.Shift);
            Assert.True(vm.IsAcceptPromptOpen);

            ScopeTo(vm, level, hinge);          // the operator moves the picker under the live question
            Key(window, PhysicalKey.Y);

            var reloaded = LoadBack(dir, company);
            Assert.Equal(4, reloaded.PriceLists.Count);
            Assert.NotNull(PriceResolver.Resolve(reloaded, level.Id, widget.Id, 5m, new DateOnly(2024, 5, 1)));
            Assert.NotNull(PriceResolver.Resolve(reloaded, level.Id, hinge.Id, 5m, new DateOnly(2024, 5, 1)));
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
