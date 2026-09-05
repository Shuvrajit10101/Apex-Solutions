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
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-20 (census 2.12) — <b>the multi-master grid as an operator meets it</b>: rendered in a real
/// <see cref="MainWindow"/> and driven by keys through the window's own key tunnel.
///
/// <para>🔴 <b>Why this file exists separately from <see cref="MultiMasterCreateViewModelTests"/>.</b> That
/// suite drives the view models directly. It proves the batch rules are right, and it would go on passing
/// against a grid that rendered no editable cell, scrolled nothing into view, and answered no key — the
/// "service method with no caller" shape this project has already paid for. The census standard is that a
/// capability is closed only when a user can reach it END TO END, so the rendered cells, the auto-scroll and
/// the real Ctrl+A are pinned here.</para>
///
/// <para>Headless-safe: layout bounds and focus only — no Skia, no rendered frame.</para>
/// </summary>
public sealed class MultiMasterGridReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewCompany(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiGrid_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
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

    private static T? ByName<T>(MainWindow w, string name) where T : Visual
        => Descendants(w).OfType<T>().FirstOrDefault(c => (c as Control)?.Name == name);

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

    /// <summary>Opens Multi Ledger Creation the way an operator does: Create → Multi Masters → Multi Ledger.</summary>
    private static void OpenMultiLedger(MainWindow window, MainWindowViewModel vm)
    {
        SelectActiveItem(vm, "Create");
        vm.DrillIn();
        SelectActiveItem(vm, "Multi Ledger");
        vm.DrillIn();
        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
        Pump(window);
    }

    /// <summary>
    /// The cell <see cref="Grid"/> of realised grid row <paramref name="i"/>.
    ///
    /// <para>Resolved through <see cref="ItemsControl.GetRealizedContainers"/> rather than by hunting the
    /// visual tree for borders: the row's own Under picker contains borders and a text box of its OWN inside
    /// its control template, so a tree-wide search returns several "rows" per row and silently mis-attributes
    /// every cell after the first.</para>
    /// </summary>
    private static Grid RowGrid(MainWindow window, int i)
    {
        var list = ByName<ItemsControl>(window, "MultiMasterRows");
        Assert.NotNull(list);
        var containers = list!.GetRealizedContainers().ToList();
        Assert.True(containers.Count > i, $"grid row {i} was never realised — the template rendered no cells.");
        return Descendants(containers[i]).OfType<Grid>().First();
    }

    /// <summary>Row <paramref name="i"/>'s own text cells (Name, Opening Balance) — not the picker's internals.</summary>
    private static List<TextBox> RowTextBoxes(MainWindow window, int i)
        => RowGrid(window, i).GetVisualChildren().OfType<TextBox>().ToList();

    // ------------------------------------------------------------------ the grid actually renders

    /// <summary>
    /// The grid renders EDITABLE cells, not a read-only dump: the opening blank row carries a name box, an
    /// Under picker and an opening-balance box, and every one of them is focusable — which is the precondition
    /// for the whole screen being drivable without a pointer.
    /// </summary>
    [AvaloniaFact]
    public void The_opening_row_renders_focusable_cells()
    {
        var (window, vm, tempDir) = NewCompany("Multi Render Co");
        try
        {
            OpenMultiLedger(window, vm);

            var boxes = RowTextBoxes(window, 0);
            Assert.Equal(2, boxes.Count);                       // Name of Ledger, Opening Balance
            Assert.All(boxes, b => Assert.True(b.Focusable, "a grid cell a keyboard cannot focus is unreachable."));

            var picker = RowGrid(window, 0).GetVisualChildren().OfType<ComboBox>().Single();
            Assert.True(picker.Focusable);

            // Space toggles the Dr/Cr cell, so the side needs no pointer either.
            var side = RowGrid(window, 0).GetVisualChildren().OfType<CheckBox>().Single();
            Assert.True(side.Focusable);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>Auto-scroll on focus</b> (keyboard-first contract). The grid grows as it is typed, so the row the
    /// operator is on walks off the bottom of the viewport within a screenful. If focus does not drag the
    /// viewport with it, the operator is typing into a cell they cannot see — the grid works with a mouse and
    /// fails the contract.
    /// </summary>
    [AvaloniaFact]
    public void Focusing_a_row_below_the_fold_scrolls_it_into_view()
    {
        var (window, vm, tempDir) = NewCompany("Multi Scroll Co");
        try
        {
            OpenMultiLedger(window, vm);
            var m = vm.MultiMasterCreate!;

            // Type enough rows to overflow the viewport (each fill appends a fresh blank one).
            for (var i = 0; i < 40; i++) m.Rows[^1].Name = $"Row {i + 1} Ltd";
            Pump(window);

            var scroller = ByName<ScrollViewer>(window, "MultiMasterRowsScroller");
            Assert.NotNull(scroller);
            Assert.True(scroller!.Extent.Height > scroller.Viewport.Height,
                "the grid must overflow for this test to mean anything.");
            Assert.Equal(0d, scroller.Offset.Y);

            var deepCell = RowTextBoxes(window, 39)[0];
            deepCell.Focus(NavigationMethod.Tab);
            Pump(window);

            Assert.True(scroller.Offset.Y > 0d,
                "focus moved to a row below the fold and the viewport did not follow it.");

            var cellTop = deepCell.TranslatePoint(default, scroller)!.Value.Y;
            Assert.InRange(cellTop, 0d - 1d, scroller.Viewport.Height);
        }
        finally { Close(window, tempDir); }
    }

    // ------------------------------------------------------------------ the real Ctrl+A

    /// <summary>
    /// The whole slice, end to end and by key alone: reach the grid through the cascade, type two ledgers into
    /// the rendered cells, press <b>Ctrl+A on the window</b> — and both masters exist and are persisted.
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_on_the_window_creates_the_whole_batch_and_persists_it()
    {
        var (window, vm, tempDir) = NewCompany("Multi CtrlA Window Co");
        try
        {
            OpenMultiLedger(window, vm);
            var m = vm.MultiMasterCreate!;
            m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");
            Pump(window);

            // Typed into the RENDERED cells, so the two-way binding is part of what is under test.
            RowTextBoxes(window, 0)[0].Text = "Papa Ltd";
            Pump(window);
            RowTextBoxes(window, 1)[0].Text = "Quebec Ltd";
            Pump(window);
            Assert.Equal("Papa Ltd", m.Rows[0].Name);
            Assert.Equal("Quebec Ltd", m.Rows[1].Name);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.Company!.FindLedgerByName("Papa Ltd"));
            Assert.NotNull(vm.Company!.FindLedgerByName("Quebec Ltd"));
            Assert.False(m.MessageIsError);
            Assert.Equal("2 ledgers created under Sundry Debtors.", m.Message);

            // The grid resets to a single blank row, ready for the next batch — no stale text to re-submit.
            Assert.Single(m.Rows);
            Assert.True(m.Rows[0].IsBlank);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 Enter over the GRID must ASK, not commit — the claim the screen's own comment makes, pinned here.
    ///
    /// <para>Enter is the natural move-to-the-next-cell key in a grid. If it committed outright, the first
    /// operator who used it as navigation would post a half-typed batch of masters. So the grid joins the
    /// master-accept family: Enter raises "Accept these Ledgers? (Y/N)" and writes nothing, and only Y goes
    /// through — down the same path Ctrl+A takes.</para>
    /// </summary>
    [AvaloniaFact]
    public void Enter_asks_before_it_commits_and_Y_goes_through()
    {
        var (window, vm, tempDir) = NewCompany("Multi Enter Co");
        try
        {
            OpenMultiLedger(window, vm);
            var m = vm.MultiMasterCreate!;
            m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");
            m.Rows[0].Name = "Romeo Ltd";
            Pump(window);

            var before = vm.Company!.Ledgers.Count;
            Assert.True(vm.IsMasterAcceptScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Equal("Accept these Ledgers? (Y/N)", vm.AcceptPromptText);
            Assert.Equal(before, vm.Company!.Ledgers.Count);          // asked, wrote nothing
            Assert.Null(vm.Company!.FindLedgerByName("Romeo Ltd"));

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindLedgerByName("Romeo Ltd"));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Escape leaves the grid the way it leaves every other master page — back up the cascade.</summary>
    [AvaloniaFact]
    public void Escape_leaves_the_grid()
    {
        var (window, vm, tempDir) = NewCompany("Multi Escape Co");
        try
        {
            OpenMultiLedger(window, vm);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.NotEqual(Screen.MultiMasterCreate, vm.CurrentScreen);
            Assert.Null(vm.MultiMasterCreate);
        }
        finally { Close(window, tempDir); }
    }
}
