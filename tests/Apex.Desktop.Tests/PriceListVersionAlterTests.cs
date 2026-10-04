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
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.11 — ALTERING ONE DATED PRICE-LIST VERSION IN PLACE, driven by real keystrokes on a real
/// window.</b>
///
/// <para><b>THE GAP THIS CLOSES, AND WHY IT IS A WRONG-MONEY GAP.</b> Before this, a price list could only be
/// <i>revised</i> — appending a NEW version with a strictly later Applicable-From. So an operator who fat-fingered
/// a slab rate had no way to correct the version that was already live: the wrong rate stayed in force for every
/// invoice dated inside its window, and superseding it from today forward does not touch them. A price list sets
/// invoice rates, so that is money.</para>
///
/// <para><b>FIDELITY (R7 / ruling 14) — CITED, AND THE URL WAS OPENED AND READ BY CONTENT ON 2026-10-04.</b>
/// <c>help.tallysolutions.com/selling-buying-prices/</c> states <i>"You can alter a price list by overwriting the
/// details entered in the Price List screen"</i> and gives the route <i>"Press Alt+G (Go To) &gt; Alter Master
/// &gt; Price List (Stock Group or Stock Category)"</i>. 🔴 <b>This is the page that was MISSING when wave 33
/// cluster C3 withheld this verb</b> — its own comment and
/// <c>MasterVerbsW33ResidualAlterTests</c>'s header both record "price list" among the masters kept at
/// create+delete "because no source says what altering one means". A source says. Both comments are corrected in
/// this change rather than left to mislead.</para>
///
/// <para><b>What is OURS and labelled so:</b> the <i>route</i> — this product reaches an alteration by arrows +
/// Ctrl+Enter on the existing-list, its own established shape, not the vendor's Go-To menu — and the
/// <b>same-date refusal</b> (<c>PriceListService.AlterList</c>). The vendor page does not say what happens when
/// two versions of one (level, item) share an Applicable-From; we refuse it, because
/// <c>PriceResolver</c> picks "the latest ApplicableFrom ≤ voucher date" and would otherwise pick arbitrarily
/// between them. That reading is OURS and is stated as ours.</para>
///
/// <para>🔴 <b>EVERY TEST IN THIS FILE FAILS ON TODAY'S <c>main</c>.</b> There,
/// <c>PriceListsViewModel.IsAltering</c> is the CONSTANT <c>false</c>, the type has no <c>ForAlter</c> and no
/// <c>Alter</c>, <c>PriceListService</c> has no <c>AlterList</c>, and
/// <c>AlterHighlightedMasterListRow</c> has no <c>Screen.PriceListsMaster</c> case — so Ctrl+Enter on a version
/// is a silent no-op.</para>
/// </summary>
public sealed class PriceListVersionAlterTests
{
    private sealed class Kit
    {
        public required MainWindow Window { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required string Dir { get; init; }
        public required string CompanyName { get; init; }
        public required Guid LevelId { get; init; }
        public required Guid ItemId { get; init; }
    }

    /// <summary>A company with price levels on, one item, and ONE live version: 0–2 @ 16,000 / 2+ @ 14,850.</summary>
    private static Kit NewKit(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexZ3PLAlter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = company;
        vm.CreateCompany();

        var c = vm.Company!;
        c.EnableMultiplePriceLevels = true;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);

        var pls = new PriceListService(c);
        var retail = pls.CreateLevel("Retail");
        pls.AddOrReviseList(retail.Id, item.Id, c.BooksBeginFrom, new[]
        {
            new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
            new PriceListSlab(2m, null, Money.FromRupees(14850m)),
        });
        new CompanyStorage(dir).Save(c);

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new Kit
        {
            Window = window, Vm = vm, Dir = dir, CompanyName = company,
            LevelId = retail.Id, ItemId = item.Id,
        };
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

    /// <summary>🔴 THE REACHABILITY PROOF: Masters → Create → Price List, by arrows and Enter only. This project
    /// has four times graded an engine with no production caller as progress; the row is only PARTIAL-closed if a
    /// person can get here from the keyboard.</summary>
    private static void OpenPriceListMaster(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Price List");
        Assert.Equal(Screen.PriceListsMaster, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    private static void ArrowToVersion(MainWindow window, MainWindowViewModel vm)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow is not null) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail("No price-list version was reachable by arrow navigation on the history list.");
    }

    private static void AlterHighlighted(MainWindow window) =>
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

    private static void Accept(MainWindow window) => Key(window, PhysicalKey.A, RawInputModifiers.Control);

    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    // ========================================================= the RENDERED window, not the view-model flag

    /// <summary>
    /// 🔴 <b>THE TITLE THE OPERATOR ACTUALLY SEES.</b> A green <c>IsAltering</c> and a correct <c>Caption</c>
    /// property prove nothing about the window: this screen's header was the hard-coded literal
    /// <c>Text="Price List Creation"</c>, so an Alteration screen rendered the word <i>Creation</i> over a form
    /// that was about to OVERWRITE a live version. Found only by reading the XAML — every view-model assertion in
    /// this file passed while it was wrong. Asserted here on the realised <c>TextBlock</c>.
    /// </summary>
    [AvaloniaFact]
    public void The_rendered_window_titles_the_alteration_screen_Alteration_not_Creation()
    {
        var k = NewKit("Z3 PL Render Co");
        try
        {
            OpenPriceListMaster(k.Window, k.Vm);
            Assert.Equal("Price List Creation", Named<TextBlock>(k.Window, "PriceListMasterCaption")!.Text);

            ArrowToVersion(k.Window, k.Vm);
            AlterHighlighted(k.Window);
            Dispatcher.UIThread.RunJobs();

            var caption = Named<TextBlock>(k.Window, "PriceListMasterCaption");
            Assert.NotNull(caption);
            Assert.Equal("Price List Alteration", caption!.Text);
            Assert.True(caption.IsEffectivelyVisible, "The title must actually be on screen, not in a collapsed pane.");
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    // =============================================================== the capability, end to end, by keyboard

    /// <summary>
    /// 🔴 <b>THE WHOLE ROW IN ONE TEST, BY KEYBOARD ONLY, ASSERTED ON DISK AND THROUGH THE RESOLVER.</b>
    /// Arrows to the version, Ctrl+Enter to open it, retype the top slab rate, Ctrl+A to accept — and the
    /// corrected rate is what the NEXT invoice is offered, with the history still holding ONE version.
    /// </summary>
    [AvaloniaFact]
    public void A_price_list_version_is_corrected_with_CtrlEnter_then_CtrlA_and_the_correction_persists()
    {
        var k = NewKit("Z3 PL Alter Co");
        try
        {
            OpenPriceListMaster(k.Window, k.Vm);
            ArrowToVersion(k.Window, k.Vm);

            var versionId = k.Vm.MasterListScreen!.HighlightedMasterRow!.MasterId;
            AlterHighlighted(k.Window);

            Assert.Equal(Screen.PriceListsMaster, k.Vm.CurrentScreen);
            Assert.NotNull(k.Vm.PriceLists);
            Assert.True(k.Vm.PriceLists!.IsAltering, "Ctrl+Enter must open the version for ALTERATION.");
            Assert.Equal("Price List Alteration", k.Vm.PriceLists.Caption);

            // 🔴 THE FORM ARRIVES PRE-LOADED. An alter screen that opened blank would overwrite the version with
            // whatever was typed next and silently drop the slabs the operator did not retype.
            Assert.Equal(k.LevelId, k.Vm.PriceLists.SelectedLevel!.Id);
            Assert.Equal(k.ItemId, k.Vm.PriceLists.SelectedItem!.Id);
            var filled = k.Vm.PriceLists.Slabs.Where(s => !s.IsBlank).ToList();
            Assert.Equal(2, filled.Count);
            Assert.Equal("16000.00", filled[0].RateText);
            Assert.Equal("14850.00", filled[1].RateText);

            // 🔴 NO INTERIOR BLANK ROW. Every text setter appends a blank row once the last row stops being blank,
            // so a naive load strands blanks between the bands of a ladder whose contract is that it is contiguous.
            var blanks = k.Vm.PriceLists.Slabs.Select((s, i) => (s, i)).Where(t => t.s.IsBlank).ToList();
            Assert.Single(blanks);
            Assert.Equal(k.Vm.PriceLists.Slabs.Count - 1, blanks[0].i);

            // The operator fixes the fat-fingered top slab: 14,850 was meant to be 14,500.
            filled[1].RateText = "14500.00";
            Accept(k.Window);

            // The accept reported success, not a refusal.
            Assert.Contains("altered", k.Vm.PriceLists.Message ?? string.Empty);

            // 🔴 ONE version still, not two — this is an ALTERATION, not a revision.
            var live = k.Vm.Company!.PriceListsFor(k.LevelId, k.ItemId).ToList();
            Assert.Single(live);
            Assert.Equal(versionId, live[0].Id);
            Assert.Equal(14500m, live[0].Slabs[1].Rate.Amount);

            // 🔴 RE-READ FROM SQLITE. A correction that lives only in the open aggregate is not a correction.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            var persisted = reloaded.PriceListsFor(k.LevelId, k.ItemId).ToList();
            Assert.Single(persisted);
            Assert.Equal(versionId, persisted[0].Id);
            Assert.Equal(14500m, persisted[0].Slabs[1].Rate.Amount);

            // 🔴 THE MONEY ASSERTION: what the next invoice is actually OFFERED, through the production resolver,
            // for a quantity in the corrected band. Hand-computed: qty 5 falls in the 2+ band, no discount, so the
            // effective unit rate is exactly the corrected 14,500.00.
            var resolved = PriceResolver.Resolve(reloaded, k.LevelId, k.ItemId, 5m, reloaded.BooksBeginFrom);
            Assert.NotNull(resolved);
            Assert.Equal(14500m, resolved!.Value.Rate.Amount);
            Assert.Equal(14500m, resolved.Value.EffectiveUnitRate.Amount);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    /// <summary>
    /// 🔴 <b>THE REFUSAL MUST REACH THE OPERATOR.</b> A sibling review found NO test anywhere asserting a refusal
    /// message was actually SHOWN — suppressing the notice left 13 of 15 tests green while the user pressed the
    /// chord and saw an empty bar. Here: two versions exist, the operator back-dates the newer one onto the
    /// older's date, and the screen must say so and change NOTHING.
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_version_onto_another_versions_date_is_refused_with_a_visible_message()
    {
        var k = NewKit("Z3 PL Clash Co");
        try
        {
            var c = k.Vm.Company!;
            var later = c.BooksBeginFrom.AddMonths(3);
            new PriceListService(c).AddOrReviseList(k.LevelId, k.ItemId, later, new[]
            {
                new PriceListSlab(0m, null, Money.FromRupees(15000m)),
            });
            new CompanyStorage(k.Dir).Save(c);

            OpenPriceListMaster(k.Window, k.Vm);
            ArrowToVersion(k.Window, k.Vm);

            // The history is newest-first, so the first highlighted row is the later version.
            var row = k.Vm.MasterListScreen!.HighlightedMasterRow!;
            var targetId = row.MasterId;
            Assert.Equal(later, c.PriceLists.First(p => p.Id == targetId).ApplicableFrom);

            AlterHighlighted(k.Window);
            Assert.True(k.Vm.PriceLists!.IsAltering);

            // Back-date it onto the FIRST version's date — a clash.
            k.Vm.PriceLists.ApplicableFromText = ApexDate.Format(c.BooksBeginFrom);
            Accept(k.Window);

            // 🔴 THE MESSAGE IS ASSERTED, not just the refusal. An empty notice bar is the defect.
            Assert.False(string.IsNullOrWhiteSpace(k.Vm.PriceLists.Message),
                "The same-date refusal must be SHOWN to the operator, not swallowed.");
            Assert.Contains("cannot share one date", k.Vm.PriceLists.Message!);

            // And nothing moved: both versions keep their own dates, on disk.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            var dates = reloaded.PriceListsFor(k.LevelId, k.ItemId)
                .Select(p => p.ApplicableFrom).OrderBy(d => d).ToList();
            Assert.Equal(2, dates.Count);
            Assert.Equal(c.BooksBeginFrom, dates[0]);
            Assert.Equal(later, dates[1]);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    /// <summary>
    /// 🔴 <b>THE ALTER SCREEN MUST NOT RUN THE CREATE VERB.</b> On a screen that serves both, Ctrl+A has to pick
    /// by mode. Had the branch been missed, Ctrl+A would call <c>Save()</c>, which <c>AddOrReviseList</c> refuses
    /// with "a revision must carry a strictly later date" — the operator's correction would bounce off an error
    /// about revisions, unsaved, with the original rate still live. This test is what catches that specific wiring.
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_on_the_alteration_screen_runs_Alter_and_does_not_append_a_second_version()
    {
        var k = NewKit("Z3 PL Verb Co");
        try
        {
            OpenPriceListMaster(k.Window, k.Vm);
            ArrowToVersion(k.Window, k.Vm);
            AlterHighlighted(k.Window);

            // Keep the SAME applicable-from date. Save() would be refused outright on this date; Alter() accepts.
            var slabs = k.Vm.PriceLists!.Slabs.Where(s => !s.IsBlank).ToList();
            slabs[0].RateText = "15750.00";
            Accept(k.Window);

            var live = k.Vm.Company!.PriceListsFor(k.LevelId, k.ItemId).ToList();
            Assert.Single(live);
            Assert.Equal(15750m, live[0].Slabs[0].Rate.Amount);
            Assert.DoesNotContain("strictly later", k.Vm.PriceLists.Message ?? string.Empty);

            // Hand-computed: qty 1 is in the 0–2 band, so the offered rate is the corrected 15,750.00.
            var reloaded = LoadBack(k.Dir, k.CompanyName);
            var resolved = PriceResolver.Resolve(reloaded, k.LevelId, k.ItemId, 1m, reloaded.BooksBeginFrom);
            Assert.Equal(15750m, resolved!.Value.EffectiveUnitRate.Amount);
        }
        finally { k.Window.Close(); Cleanup(k.Dir); }
    }

    /// <summary>
    /// The engine guard on its own: a slab set that fails validation is refused and the stored version is left
    /// byte-for-byte as it was. An alter that half-applied would be worse than no alter at all.
    /// </summary>
    [Fact]
    public void AlterList_refusing_an_invalid_slab_set_leaves_the_stored_version_untouched()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexZ3PLEng_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var vm = new MainWindowViewModel(storage) { NewCompanyName = "Z3 PL Engine Co" };
            vm.CreateCompany();
            var c = vm.Company!;
            c.EnableMultiplePriceLevels = true;

            var masters = new InventoryService(c);
            var grp = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
            var pls = new PriceListService(c);
            var level = pls.CreateLevel("Retail");
            var original = pls.AddOrReviseList(level.Id, item.Id, c.BooksBeginFrom, new[]
            {
                new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
                new PriceListSlab(2m, null, Money.FromRupees(14850m)),
            });

            // A NON-CONTIGUOUS ladder (0–2 then 5+) — the gap the engine rejects.
            Assert.Throws<InvalidOperationException>(() => pls.AlterList(original.Id, c.BooksBeginFrom, new[]
            {
                new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
                new PriceListSlab(5m, null, Money.FromRupees(14000m)),
            }));

            var still = c.PriceListsFor(level.Id, item.Id).Single();
            Assert.Equal(original.Id, still.Id);
            Assert.Equal(2, still.Slabs.Count);
            Assert.Equal(16000m, still.Slabs[0].Rate.Amount);
            Assert.Equal(14850m, still.Slabs[1].Rate.Amount);
            Assert.Equal(2m, still.Slabs[1].FromQty);

            // A missing id is refused rather than silently creating one.
            Assert.Throws<InvalidOperationException>(() => pls.AlterList(Guid.NewGuid(), c.BooksBeginFrom, new[]
            {
                new PriceListSlab(0m, null, Money.FromRupees(100m)),
            }));
        }
        finally { Cleanup(dir); }
    }
}
