using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE CENTRAL EXCISE POSITION, THROUGH THE REALISED SCREEN</b> (census row 15.8).
///
/// <para><b>Why every assertion below reads the realised visual tree rather than a view-model property.</b>
/// <c>ExciseApplicability</c>'s sibling predicate, <c>NonGstGoods.AttractsCentralExcise</c>, shipped in v59
/// fully written, fully correct and <b>called by nothing in <c>src/</c></b> — the census carries an explicit
/// A12 note saying so, precisely so a later reader would not mistake it for a shipped capability. That is this
/// repository's most-repeated defect (a ~625-line cheque renderer whose only writers were test files; an export
/// format whose radio could be deleted with eight view-model tests still green). A test asserting
/// <c>master.ExcisePositionStatement</c> would pass against a build with no XAML at all and would reproduce the
/// exact bug this slice exists to close. So each test drives the shipped <see cref="MainWindow"/>, opens the
/// real Stock Item master, and asks the <b>realised, effectively-visible</b> <see cref="TextBlock"/>s what an
/// operator can actually read.</para>
///
/// <para>🔴 <b>The two tests that carry the row are the two that would look wrong to someone who had not read
/// entry 84</b>: tobacco must show "excise applies" on a screen that simultaneously REFUSES it a VAT rate, and
/// alcoholic liquor must show "excise does not apply" on a screen that simultaneously ALLOWS it one. A build
/// that reused the VAT predicate for excise would render both backwards and fail here.</para>
///
/// <para><b>Red on today's main</b>, where the Stock Item master has no excise block of any kind.</para>
/// </summary>
public sealed class CentralExcisePositionReachabilityTests
{
    // ================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) VatCompany(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexExcisePos_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        // The class-of-goods picker lives in the VAT block, so the company must have State VAT on for the
        // operator to reach it at all. That coupling is a KNOWN LIMIT of this slice, recorded on the block in
        // MainWindow.axaml: there is no company-level excise flag to gate on, and adding one is storage this
        // slice had no budget for.
        new VatService(vm.Company!).EnableVat(tin: "29123456789");

        var inventory = new InventoryService(vm.Company!);
        if (vm.Company!.Units.Count == 0) inventory.CreateSimpleUnit("Nos", "Numbers");
        if (vm.Company!.FindStockGroupByName("Primary") is null) inventory.CreateStockGroup("Primary");
        storage.Save(vm.Company!);

        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Every realised string an operator can actually READ. <c>IsEffectivelyVisible</c> folds in every ancestor,
    /// so a block sitting inside a collapsed container does not count — asserting the control's own
    /// <c>IsVisible</c> would pass for a screen that shows nothing, which is the failure this file exists to
    /// catch.
    /// </summary>
    private static IReadOnlyList<string> RealisedLabels(MainWindow w) =>
        Descendants(w).OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();

    private static StockItemMasterViewModel OpenItemMaster(MainWindow w, MainWindowViewModel vm)
    {
        vm.ShowStockItemMaster();
        Pump(w);
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
        Assert.NotNull(vm.StockItemMaster);
        return vm.StockItemMaster!;
    }

    private static void Choose(MainWindow w, StockItemMasterViewModel master, NonGstGoodsClass goodsClass)
    {
        master.SelectedNonGstGoodsClass =
            master.NonGstGoodsClasses.Single(o => o.Value == goodsClass);
        Pump(w);
    }

    // ================================================================= the block is reachable at all

    /// <summary>
    /// 🔴 <b>THE BLOCK EXISTS AND IS ON SCREEN.</b> Heading, badge and a statement of law, all realised and all
    /// visible without the operator doing anything but open the master.
    /// </summary>
    [AvaloniaFact]
    public void The_item_master_shows_a_central_excise_block()
    {
        var (w, vm, dir) = VatCompany("Excise Pos Co");
        try
        {
            OpenItemMaster(w, vm);
            var labels = RealisedLabels(w);

            Assert.Contains("Central Excise", labels);
            Assert.Contains(labels, l => l.StartsWith("Excise: ", StringComparison.Ordinal));

            // The default class is ordinary GST goods, so the statement must say excise does NOT reach them —
            // an empty panel would let an operator infer the opposite.
            Assert.Contains(labels, l => l.Contains("does NOT apply", StringComparison.Ordinal));
        }
        finally { Close(w, dir); }
    }

    // ================================================================= the asymmetry, on screen

    /// <summary>
    /// 🔴 <b>TOBACCO: "excise applies" AND "no VAT rate", on the SAME screen at the SAME time.</b> This is the
    /// case a build that reused the VAT predicate would render backwards — it would tell a tobacco trader they
    /// are an ordinary GST dealer with nothing else to consider.
    /// </summary>
    [AvaloniaFact]
    public void Tobacco_shows_excise_applies_while_the_VAT_rate_stays_refused()
    {
        var (w, vm, dir) = VatCompany("Tobacco Co");
        try
        {
            var master = OpenItemMaster(w, vm);
            Choose(w, master, NonGstGoodsClass.Tobacco);

            var labels = RealisedLabels(w);

            // The excise half.
            Assert.Contains("Excise: applies", labels);
            Assert.Contains(labels, l => l.Contains("AS WELL AS GST", StringComparison.Ordinal));

            // The VAT half, unchanged and still refusing — the two blocks visibly disagree, which is the truth.
            Assert.False(master.VatRateAllowed);
            Assert.Contains(labels, l => l.Contains("inside GST", StringComparison.OrdinalIgnoreCase));
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>ALCOHOLIC LIQUOR: "excise does NOT apply" while the VAT rate box is ENABLED.</b> The exact mirror of
    /// the tobacco case, and the one an operator is most likely to get wrong unaided — liquor is the flagship
    /// "outside GST" class, so the intuitive (and wrong) conclusion is that central excise must reach it.
    /// </summary>
    [AvaloniaFact]
    public void Alcoholic_liquor_shows_excise_does_not_apply_while_the_VAT_rate_is_allowed()
    {
        var (w, vm, dir) = VatCompany("Liquor Co");
        try
        {
            var master = OpenItemMaster(w, vm);
            Choose(w, master, NonGstGoodsClass.AlcoholicLiquorForHumanConsumption);

            var labels = RealisedLabels(w);

            Assert.Contains("Excise: does not apply", labels);
            Assert.Contains(labels, l => l.Contains("State subject", StringComparison.Ordinal));

            // VAT, meanwhile, is permitted on the very same class.
            Assert.True(master.VatRateAllowed);
            Assert.Empty(master.VatRefusalReason);
        }
        finally { Close(w, dir); }
    }

    /// <summary>A petroleum product carries both levies' "yes" — outside GST for VAT, and entry 84 for excise.</summary>
    [AvaloniaFact]
    public void High_speed_diesel_shows_excise_applies_and_cites_its_clause()
    {
        var (w, vm, dir) = VatCompany("Diesel Co");
        try
        {
            var master = OpenItemMaster(w, vm);
            Choose(w, master, NonGstGoodsClass.HighSpeedDiesel);

            var labels = RealisedLabels(w);
            Assert.Contains("Excise: applies", labels);
            Assert.Contains(labels, l => l.Contains("84(b)", StringComparison.Ordinal));
            Assert.True(master.VatRateAllowed);
        }
        finally { Close(w, dir); }
    }

    // ================================================================= it tracks the picker

    /// <summary>
    /// 🔴 <b>THE STATEMENT FOLLOWS THE PICKER.</b> The classic silent half of this defect is a block that renders
    /// once and then keeps describing the PREVIOUS class after the operator changes it — the change notification
    /// for the excise members is raised by hand in <c>OnSelectedNonGstGoodsClassChanged</c>, and deleting those
    /// three lines leaves the screen confidently wrong. This test walks tobacco → liquor → ordinary and reads the
    /// realised text at each step.
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_class_of_goods_moves_the_excise_statement_with_it()
    {
        var (w, vm, dir) = VatCompany("Switch Co");
        try
        {
            var master = OpenItemMaster(w, vm);

            Choose(w, master, NonGstGoodsClass.Tobacco);
            Assert.Contains("Excise: applies", RealisedLabels(w));

            Choose(w, master, NonGstGoodsClass.AlcoholicLiquorForHumanConsumption);
            var afterLiquor = RealisedLabels(w);
            Assert.Contains("Excise: does not apply", afterLiquor);
            Assert.DoesNotContain("Excise: applies", afterLiquor);

            Choose(w, master, NonGstGoodsClass.MotorSpirit);
            var afterPetrol = RealisedLabels(w);
            Assert.Contains("Excise: applies", afterPetrol);
            Assert.Contains(afterPetrol, l => l.Contains("84(c)", StringComparison.Ordinal));

            Choose(w, master, NonGstGoodsClass.None);
            var afterOrdinary = RealisedLabels(w);
            Assert.Contains("Excise: does not apply", afterOrdinary);
            Assert.Contains(afterOrdinary, l => l.Contains("01-Jul-2017", StringComparison.Ordinal));
        }
        finally { Close(w, dir); }
    }

    // ================================================================= the honest limit is on screen

    /// <summary>
    /// 🔴 <b>THE LIMIT IS STATED WHERE IT MATTERS, AND ONLY WHERE IT MATTERS.</b> When excise DOES reach the
    /// goods, an operator will look for a duty field; this build has none, and the screen says so rather than
    /// leaving them hunting. When excise does not reach the goods there is nothing to look for, so the caution
    /// must NOT appear — a warning shown unconditionally is noise, and noise is how real warnings stop being
    /// read.
    /// </summary>
    [AvaloniaFact]
    public void The_no_duty_recorded_caution_appears_only_when_excise_actually_applies()
    {
        var (w, vm, dir) = VatCompany("Caution Co");
        try
        {
            var master = OpenItemMaster(w, vm);

            Choose(w, master, NonGstGoodsClass.Tobacco);
            Assert.Contains(
                RealisedLabels(w),
                l => l.Contains("records no excise duty, register or return", StringComparison.Ordinal));

            Choose(w, master, NonGstGoodsClass.None);
            Assert.DoesNotContain(
                RealisedLabels(w),
                l => l.Contains("records no excise duty, register or return", StringComparison.Ordinal));
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// A company that never enabled State VAT sees no excise block — the known limit, pinned so it is a RECORDED
    /// consequence rather than a surprise. When the excise slice lands a company-level flag (v64), this test is
    /// the one that must change, which is exactly the signal the next wave wants.
    /// </summary>
    [AvaloniaFact]
    public void A_company_without_VAT_sees_no_excise_block_and_that_limit_is_pinned()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexExcisePos_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        try
        {
            vm.NewCompanyName = "Plain GST Co";
            vm.CreateCompany();

            var inventory = new InventoryService(vm.Company!);
            if (vm.Company!.Units.Count == 0) inventory.CreateSimpleUnit("Nos", "Numbers");
            if (vm.Company!.FindStockGroupByName("Primary") is null) inventory.CreateStockGroup("Primary");
            Pump(w);

            var master = OpenItemMaster(w, vm);
            Assert.False(master.ShowVatBlock);

            var labels = RealisedLabels(w);
            Assert.DoesNotContain("Central Excise", labels);
            Assert.DoesNotContain(labels, l => l.StartsWith("Excise: ", StringComparison.Ordinal));
        }
        finally { Close(w, tempDir); }
    }
}
