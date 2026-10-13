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
using Avalonia;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WBB-1 (census row 2.12) — <b>the Multi Masters chords and the ALTER grid as an operator meets them</b>:
/// rendered in a real <see cref="MainWindow"/> and driven by keys through the window's own key tunnel.
///
/// <para>🔴 <b>Why this file is separate from the view-model suite.</b>
/// <see cref="MultiMasterAlterAndFamiliesTests"/> proves the batch rules and the persistence. It would go on
/// passing against a build where <b>Alt+H answered nothing</b>, where the commit button still read
/// "Create All (Ctrl+A)" over an alteration screen, and where the stock-item grid rendered no Units picker — all
/// three being the "capability no user can reach" shape this project has filed repeatedly (Alt+P was once inert;
/// three reports printed pages of blank cells; a verb button ran <c>Create()</c> on an alteration screen). The
/// census standard is that a capability is closed only when a user can reach it END TO END, so the chords, the
/// rendered caption and the rendered cells are pinned here.</para>
///
/// <para><b>Vendor, opened by content 2026-10-13.</b>
/// <c>help.tallysolutions.com/tally-prime/charts-of-accounts-tally/</c>: "<i>press <b>Alt+H</b> (Multi-Masters)
/// &gt; <b>Multi Create</b> and press <b>Enter</b></i>"; "<i>press <b>Alt+H</b> (Multi-Masters) &gt; <b>Multi
/// Alter</b> &gt; press <b>Enter</b></i>"; "<i>To remove the opening balances from all the ledgers, press
/// <b>Alt+B</b> (Zero Opening Balance)</i>".</para>
///
/// <para>Headless-safe: layout, focus and key routing only — no Skia, no rendered frame.</para>
/// </summary>
public sealed class MultiMasterChordReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, CompanyStorage Storage, string TempDir)
        NewCompany(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiChord_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Pump(window);
        return (window, vm, storage, tempDir);
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

    private static T? ByName<T>(MainWindow w, string name) where T : Visual
        => Descendants(w).OfType<T>().FirstOrDefault(c => (c as Control)?.Name == name);

    private static Company Reload(CompanyStorage storage, string companyName)
    {
        var entry = storage.ListCompanies().Single(e => e.Name == companyName);
        return storage.Load(entry);
    }

    /// <summary>Walks the ACTIVE cascade column with Down until the highlighted row is <paramref name="label"/>.</summary>
    private static void SelectActiveItem(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            vm.MoveDown();
        }
        Assert.Fail($"menu item '{label}' was not reachable by arrow navigation");
    }

    // ================================================================= Alt+H, the vendor's door

    /// <summary>
    /// 🔴 <b>Alt+H ON THE CHART OF ACCOUNTS OPENS MULTI MASTERS, PRESSED ON THE REAL WINDOW.</b> Census row 2.12
    /// recorded this chord as UNBOUND and it was: before WBB-1 the only <c>Key.H</c> arms in the shell were the
    /// two Ctrl+H arms, so Alt+H fell through and did nothing.
    ///
    /// <para>The column must offer the vendor's TWO verbs under their own headers, and every row must be
    /// reachable by arrows — never a flat dump (UI contract).</para>
    ///
    /// <para><b>Mutation:</b> deleting the Alt+H arm from <c>MainWindow.OnKeyDown</c> fails this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltH_on_the_Chart_of_Accounts_opens_the_Multi_Masters_column()
    {
        var (window, vm, _, tempDir) = NewCompany("Multi AltH Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.True(vm.IsChartOfAccountsScreen);

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.MultiMastersMenu, vm.CurrentScreen);
            Assert.Equal("Multi Masters", vm.Columns[^1].Title);

            // The vendor's two verbs, each its own section header.
            Assert.Contains(vm.Menu, m => m.IsHeader && m.Label == "Multi Create");
            Assert.Contains(vm.Menu, m => m.IsHeader && m.Label == "Multi Alter");

            // Both verbs reach every built family, and each row is reachable by arrows alone.
            foreach (var label in new[]
                     {
                         "Multi Ledger Creation", "Multi Group Creation", "Multi Stock Item Creation",
                         "Multi Godown Creation", "Multi Cost Centre Creation",
                         "Multi Ledger Alteration", "Multi Group Alteration", "Multi Stock Item Alteration",
                         "Multi Godown Alteration", "Multi Cost Centre Alteration",
                     })
                Assert.Contains(vm.Menu, m => !m.IsHeader && m.Label == label);

            // 🔴 Ruling 25: TallyPrime documents Create and Alter only. No Display row is invented here.
            Assert.DoesNotContain(vm.Menu, m => m.Label.Contains("Display", StringComparison.OrdinalIgnoreCase));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// A row of the Alt+H column opens its grid, by arrows and Enter alone, and the spent menu column is POPPED
    /// rather than left on top of the grid — which is what would otherwise bind the page under a column the
    /// operator can never return to.
    /// </summary>
    [AvaloniaFact]
    public void A_row_of_the_AltH_column_opens_its_grid_by_arrows_and_Enter()
    {
        var (window, vm, _, tempDir) = NewCompany("Multi AltH Drill Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Alt);
            Pump(window);

            SelectActiveItem(vm, "Multi Ledger Alteration");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
            Assert.NotNull(vm.MultiMasterCreate);
            Assert.True(vm.MultiMasterCreate!.IsAltering);
            Assert.Equal("Multi Ledger Alteration", vm.Columns[^1].Title);
            Assert.DoesNotContain(vm.Columns, c => c.Title == "Multi Masters");
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The chord is scoped to the surface the vendor documents it on, so it stays a safe no-op app-wide. Pressed
    /// on the Gateway it must change nothing at all.
    /// </summary>
    [AvaloniaFact]
    public void AltH_is_a_quiet_no_op_away_from_the_Chart_of_Accounts()
    {
        var (window, vm, _, tempDir) = NewCompany("Multi AltH Scope Co");
        try
        {
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.Null(vm.MultiMasterCreate);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================= the rendered commit caption

    /// <summary>
    /// 🔴 <b>THE RENDERED CAPTION, WHICH IS THE HALF OF T2-105 A VERB-BY-VERB AUDIT MISSED.</b>
    ///
    /// <para>Four Area-3 masters shipped an alteration screen whose only button was hard-coded
    /// <c>Content="Create (Ctrl+A)"</c>. On the Group master the alteration WORKED and the screen announced a
    /// creation, which is exactly why the omission survived an audit that checked the verbs. This test reads the
    /// <b>realised Button's Content</b> on each verb, so a hard-coded literal cannot pass it.</para>
    ///
    /// <para><b>Mutation:</b> replacing <c>Content="{Binding CommitLabel}"</c> in <c>MainWindow.axaml</c> with the
    /// literal <c>Content="Create All (Ctrl+A)"</c> fails this test on the ALTER case — and the view-model suite
    /// stays entirely GREEN, which is the whole reason this file exists.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_rendered_commit_button_names_the_verb_on_both_screens()
    {
        var (window, vm, _, tempDir) = NewCompany("Multi Caption Co");
        try
        {
            vm.ShowMultiLedgerCreate();
            Pump(window);
            var createButton = ByName<Button>(window, "MultiMasterCommitButton");
            Assert.NotNull(createButton);
            Assert.Equal("Create All (Ctrl+A)", createButton!.Content);

            // The Alt+B affordance belongs to the alteration screen and must not be offered here.
            var zeroOnCreate = ByName<Button>(window, "MultiMasterZeroOpeningButton");
            Assert.False(zeroOnCreate?.IsVisible ?? false);

            vm.Back();
            Pump(window);
            vm.ShowMultiLedgerAlter();
            Pump(window);

            var alterButton = ByName<Button>(window, "MultiMasterCommitButton");
            Assert.NotNull(alterButton);
            Assert.Equal("Alter All (Ctrl+A)", alterButton!.Content);

            var zeroOnAlter = ByName<Button>(window, "MultiMasterZeroOpeningButton");
            Assert.NotNull(zeroOnAlter);
            Assert.True(zeroOnAlter!.IsVisible);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================= the real Ctrl+A on an ALTER grid

    /// <summary>
    /// 🔴 <b>THE WHOLE ALTER VERB, END TO END AND BY KEY ALONE, READ BACK OFF SQLITE.</b> Reach the grid through
    /// Alt+H, type the correction into the RENDERED cell, press <b>Ctrl+A on the window</b> — and the ORIGINAL
    /// ledger carries the new name while the ledger count is unchanged.
    ///
    /// <para><b>Mutation:</b> changing the view model's <c>Accept()</c> from
    /// <c>IsAltering ? AlterBatch() : CreateBatch()</c> to a bare <c>CreateBatch()</c> — byte-for-byte the
    /// T2-105 defect — fails this test on the ledger count.</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_on_the_window_alters_the_original_ledger_and_persists_it()
    {
        const string companyName = "Multi CtrlA Alter Co";
        var (window, vm, storage, tempDir) = NewCompany(companyName);
        try
        {
            // A ledger to correct, created through the real create grid.
            vm.ShowMultiLedgerCreate();
            var create = vm.MultiMasterCreate!;
            create.SelectedUnderGroup =
                create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
            create.Rows[0].Name = "Mike Ltd";
            Assert.True(create.Accept(), create.Message);
            vm.Back();
            Pump(window);

            var id = vm.Company!.FindLedgerByName("Mike Ltd")!.Id;
            var countBefore = vm.Company!.Ledgers.Count;

            // Reach the ALTER grid the vendor's way.
            vm.ShowChartOfAccounts();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Alt);
            Pump(window);
            SelectActiveItem(vm, "Multi Ledger Alteration");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            var m = vm.MultiMasterCreate!;
            Assert.True(m.IsAltering);
            var row = m.Rows.Single(r => r.ExistingId == id);
            row.Name = "Mike Ltd (Corrected)";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.False(m.MessageIsError, m.Message);
            Assert.Equal("1 ledger altered.", m.Message);

            // 🔴 Off the book, re-read from SQLite.
            var book = Reload(storage, companyName);
            Assert.Equal(countBefore, book.Ledgers.Count);          // nothing appended
            Assert.Equal("Mike Ltd (Corrected)", book.Ledgers.Single(l => l.Id == id).Name);
            Assert.Null(book.FindLedgerByName("Mike Ltd"));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>Alt+B PRESSED ON THE REAL WINDOW</b> clears every opening balance on the Multi Ledger Alteration
    /// grid, and only Ctrl+A writes it. Without this arm the capability would exist on the view model with no key
    /// that reaches it — and the vendor names the chord explicitly.
    ///
    /// <para><b>Mutation:</b> deleting the Alt+B arm from <c>MainWindow.OnKeyDown</c> fails this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltB_on_the_window_zeroes_the_opening_balances_and_CtrlA_persists_it()
    {
        const string companyName = "Multi AltB Window Co";
        var (window, vm, storage, tempDir) = NewCompany(companyName);
        try
        {
            vm.ShowMultiLedgerCreate();
            var create = vm.MultiMasterCreate!;
            create.SelectedUnderGroup =
                create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
            create.Rows[0].Name = "November Ltd";
            create.Rows[0].OpeningBalanceText = "9999.99";
            Assert.True(create.Accept(), create.Message);
            vm.Back();
            Pump(window);

            vm.ShowMultiLedgerAlter();
            Pump(window);
            var m = vm.MultiMasterCreate!;
            var row = m.Rows.Single(r => r.Name == "November Ltd");
            Assert.Equal("9999.99", row.OpeningBalanceText);

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(string.Empty, row.OpeningBalanceText);
            Assert.False(m.MessageIsError, m.Message);

            // Staged only — nothing written yet.
            Assert.Equal(9999.99m,
                Reload(storage, companyName).FindLedgerByName("November Ltd")!.OpeningBalance.Amount);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(0m,
                Reload(storage, companyName).FindLedgerByName("November Ltd")!.OpeningBalance.Amount);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================= the stock-item grid renders its columns

    /// <summary>
    /// 🔴 <b>The Multi Stock Item Creation grid renders its own cells, and the LEDGER cells stay hidden on it.</b>
    ///
    /// <para>The nine grid columns are shared by all five families and shown per family by <c>IsVisible</c>, so a
    /// binding mistake would leave the grid looking fine and being functionally empty — and the Units picker is
    /// REQUIRED to create an item, so a grid that rendered no picker could create nothing at all. Visibility is
    /// asserted on the realised controls, in both directions: the stock-item cells must be visible AND the
    /// ledger cells must not, because a stock item has no opening BALANCE.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_stock_item_grid_renders_a_units_picker_and_hides_the_ledger_cells()
    {
        var (window, vm, _, tempDir) = NewCompany("Multi Item Render Co");
        try
        {
            var inventory = new InventoryService(vm.Company!);
            inventory.CreateStockGroup("Finished Goods");
            inventory.CreateSimpleUnit("Nos", "Numbers");

            vm.ShowMultiStockItemCreate();
            Pump(window);

            var list = Descendants(window).OfType<ItemsControl>()
                .FirstOrDefault(c => c.Name == "MultiMasterRows");
            Assert.NotNull(list);
            // Direct children only — the Under picker's own template holds a TextBox and borders of its own, so
            // a tree walk re-counts the picker's internals as row cells.
            var rowGrid = Descendants(list!.GetRealizedContainers().First()).OfType<Grid>().First();
            var cells = rowGrid.GetVisualChildren().OfType<Control>().ToList();

            // Units + Opening Qty + Rate + Amount are shown; Opening Balance + Dr/Cr are not.
            var pickers = cells.OfType<ComboBox>().Where(c => c.IsVisible).ToList();
            Assert.Equal(2, pickers.Count);                    // the Under picker and the Units picker
            Assert.All(pickers, p => Assert.True(p.Focusable,
                "a cell a keyboard cannot focus makes the grid unusable."));

            // Name + Opening Qty + Rate are keyed. Opening Balance is hidden on this family, so it is NOT keyable.
            var boxes = cells.OfType<TextBox>().Where(b => b.IsVisible).ToList();
            Assert.Equal(3, boxes.Count);
            Assert.All(boxes, b => Assert.True(b.Focusable));

            // Amount is a read-only computed TextBlock, never a fourth box.
            Assert.Contains(cells.OfType<TextBlock>(), t => t.IsVisible && t.Width == 110d);

            // 🔴 The ledger-only Dr/Cr cell must be present in the template and HIDDEN here.
            var side = cells.OfType<CheckBox>().Single();
            Assert.False(side.IsVisible, "a stock item has no opening BALANCE, so Dr/Cr must not be offered.");
        }
        finally { Close(window, tempDir); }
    }
}
